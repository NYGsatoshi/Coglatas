using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Security.Redaction;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.SurveyFeedback.Csv;

public sealed class FeedbackCsvService(
    SurveyFeedbackAuthorization authorization,
    IFeedbackCsvRepository repository,
    ISessionRepository sessions,
    ICurrentUser currentUser,
    ICreateIdempotencyCoordinator idempotency,
    IUnitOfWork unitOfWork,
    IAuditLogger audit,
    IRedactionService redaction,
    IClock clock)
{
    private static readonly TimeSpan PackageLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(5);

    public async Task<Result<FeedbackCsvRequestReceipt>> RequestAsync(
        Guid presentationId, FeedbackCsvRequest request, string? key, string requestId, CancellationToken cancellationToken)
    {
        var actor = await CurrentActorAsync(cancellationToken);
        if (actor is null)
            return SurveyFeedbackService.AuthenticationRequired<FeedbackCsvRequestReceipt>();

        var access = await AuthorizeAsync(actor, presentationId, request.Mode, cancellationToken);
        if (!access.IsSuccess)
            return Forward<FeedbackCsvRequestReceipt, PresentationFeedbackScope>(access);

        if (!FeedbackCsvFormatter.IsValidRequest(request))
            return SurveyFeedbackService.Invalid<FeedbackCsvRequestReceipt>();

        // An independently approved identity export projection is required before this column can be used.
        if (request.Columns!.Contains("respondentName", StringComparer.Ordinal))
            return Failure<FeedbackCsvRequestReceipt>("RestrictedFieldDenied", "This export field is unavailable.");

        var keyError = ValidateKey(key);
        if (keyError is not null)
            return Result<FeedbackCsvRequestReceipt>.Failure(keyError);

        var newId = Guid.NewGuid();
        var intent = new CreateIdempotencyContext(actor.TenantId, actor.UserId, "survey.feedback.csv.request",
            key!, Digest(new { presentationId, request.Mode, request.Columns }), nameof(SurveyFeedbackExportRequest), newId);
        try
        {
            var outcome = await idempotency.ExecuteAsync(intent, async token =>
            {
                var built = await BuildAsync(actor, presentationId, request, token);
                if (!built.IsSuccess)
                    throw new FeedbackCsvRejectedException(built.ErrorDetail!);

                var entity = SurveyFeedbackExportRequest.Prepare(newId, actor.TenantId, presentationId, actor.UserId,
                    request.Mode!, JsonSerializer.Serialize(request.Columns), built.Value!.SourceDigest,
                    requestId, clock.UtcNow.Add(PackageLifetime));
                repository.AddRequest(entity);
                await AuditAsync(actor, access.Value!, "SurveyFeedbackCsvRequested", entity.Id, token);
                return entity;
            }, (recordId, token) => repository.FindRequestAsync(actor.TenantId, actor.UserId, recordId, token),
                cancellationToken);

            if (outcome.Value is not { } export)
                return IdempotencyFailure<FeedbackCsvRequestReceipt>(outcome.Disposition);

            var current = await AuthorizeAsync(actor, presentationId, request.Mode, cancellationToken);
            return current.IsSuccess
                ? Result<FeedbackCsvRequestReceipt>.Success(new(new FeedbackCsvQueued(export.Id, "queued"), export.ReceiptRequestId))
                : Forward<FeedbackCsvRequestReceipt, PresentationFeedbackScope>(current);
        }
        catch (FeedbackCsvRejectedException rejected)
        {
            return Result<FeedbackCsvRequestReceipt>.Failure(rejected.Detail);
        }
    }

    public async Task<Result<FeedbackCsvStatus>> StatusAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = await FindAuthorizedRequestAsync(id, cancellationToken);
        if (!access.IsSuccess)
            return Forward<FeedbackCsvStatus, AuthorizedExport>(access);

        var export = access.Value!;
        var prepared = await RevalidatePackageAsync(export, cancellationToken);
        if (clock.UtcNow < export.Request.ExpiresAtUtc &&
            prepared.ErrorDetail?.Code is "ExportExpired" or "ConcurrentModification" or "ExportTooLarge")
            return Result<FeedbackCsvStatus>.Success(new FeedbackCsvStatus(id, "revoked", null));

        return prepared.IsSuccess
            ? Result<FeedbackCsvStatus>.Success(new FeedbackCsvStatus(id, "ready", export.Request.ExpiresAtUtc))
            : Forward<FeedbackCsvStatus, FeedbackCsvFile>(prepared);
    }

    public async Task<Result<FeedbackCsvGrantResponse>> GrantAsync(
        Guid id, string? key, string requestId, CancellationToken cancellationToken)
    {
        var access = await FindAuthorizedRequestAsync(id, cancellationToken);
        if (!access.IsSuccess)
            return Forward<FeedbackCsvGrantResponse, AuthorizedExport>(access);

        var keyError = ValidateKey(key);
        if (keyError is not null)
            return Result<FeedbackCsvGrantResponse>.Failure(keyError);

        var export = access.Value!;
        var grantId = Guid.NewGuid();
        var intent = new CreateIdempotencyContext(export.Actor.TenantId, export.Actor.UserId,
            "survey.feedback.csv.grant", key!, Digest(new { exportRequestId = id }),
            nameof(SurveyFeedbackExportGrant), grantId);
        try
        {
            var outcome = await idempotency.ExecuteAsync(intent, async token =>
            {
                var prepared = await RevalidatePackageAsync(export, token);
                if (!prepared.IsSuccess)
                    throw new FeedbackCsvRejectedException(GrantFailure(prepared.ErrorDetail!));

                var proposedExpiry = clock.UtcNow.Add(GrantLifetime);
                var issuedGrant = SurveyFeedbackExportGrant.Issue(grantId, export.Request, requestId,
                    proposedExpiry < export.Request.ExpiresAtUtc ? proposedExpiry : export.Request.ExpiresAtUtc);
                repository.AddGrant(issuedGrant);
                await AuditAsync(export.Actor, export.Scope, "SurveyFeedbackCsvGrantIssued", issuedGrant.Id, token);
                return issuedGrant;
            }, (recordId, token) => repository.FindGrantAsync(
                export.Actor.TenantId, export.Actor.UserId, id, recordId, token), cancellationToken);

            if (outcome.Value is not { } grant)
                return IdempotencyFailure<FeedbackCsvGrantResponse>(outcome.Disposition);

            if (clock.UtcNow >= grant.ExpiresAtUtc)
                return Expired<FeedbackCsvGrantResponse>();

            // Replay must pass the package checks again. The receipt is no proof of current authority.
            if (outcome.Disposition == IdempotentCreateDisposition.Replayed)
            {
                var current = await RevalidatePackageAsync(export, cancellationToken);
                if (!current.IsSuccess)
                    return Result<FeedbackCsvGrantResponse>.Failure(GrantFailure(current.ErrorDetail!));
            }

            var finalScope = await AuthorizeAsync(export.Actor, export.Request.PresentationId, export.Request.Mode,
                cancellationToken);
            if (!finalScope.IsSuccess)
                return Forward<FeedbackCsvGrantResponse, PresentationFeedbackScope>(finalScope);

            return Result<FeedbackCsvGrantResponse>.Success(
                new(new FeedbackCsvGrantReceipt(grant.Id, grant.ExpiresAtUtc), grant.ReceiptRequestId));
        }
        catch (FeedbackCsvRejectedException rejected)
        {
            return Result<FeedbackCsvGrantResponse>.Failure(rejected.Detail);
        }
    }

    public async Task<Result<FeedbackCsvFile>> DownloadAsync(Guid id, Guid grantId, CancellationToken cancellationToken)
    {
        var access = await FindAuthorizedRequestAsync(id, cancellationToken);
        if (!access.IsSuccess)
            return Forward<FeedbackCsvFile, AuthorizedExport>(access);

        var export = access.Value!;
        var grant = await repository.FindGrantAsync(export.Actor.TenantId, export.Actor.UserId,
            export.Request.Id, grantId, cancellationToken);
        if (grant is null)
            return SurveyFeedbackService.NotFound<FeedbackCsvFile>();

        if (clock.UtcNow >= grant.ExpiresAtUtc)
            return Expired<FeedbackCsvFile>();

        var package = await RevalidatePackageAsync(export, cancellationToken);
        if (!package.IsSuccess)
            return package.ErrorDetail?.Code == "ConcurrentModification" ? Expired<FeedbackCsvFile>() : package;

        await AuditAsync(export.Actor, export.Scope, "SurveyFeedbackCsvDeliveryAuthorized", grant.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (clock.UtcNow >= grant.ExpiresAtUtc)
            return Expired<FeedbackCsvFile>();

        var current = await AuthorizeAsync(export.Actor, export.Request.PresentationId, export.Request.Mode, cancellationToken);
        return current.IsSuccess ? package : Forward<FeedbackCsvFile, PresentationFeedbackScope>(current);
    }

    private async Task<Result<FeedbackCsvFile>> RevalidatePackageAsync(
        AuthorizedExport export, CancellationToken cancellationToken)
    {
        if (clock.UtcNow >= export.Request.ExpiresAtUtc)
            return Expired<FeedbackCsvFile>();

        var columns = JsonSerializer.Deserialize<string[]>(export.Request.ColumnsJson);
        var request = new FeedbackCsvRequest(export.Request.Mode, columns);
        var built = await BuildAsync(export.Actor, export.Request.PresentationId, request, cancellationToken);
        if (!built.IsSuccess)
            return built;

        return clock.UtcNow < export.Request.ExpiresAtUtc &&
            string.Equals(export.Request.SourceDigest, built.Value!.SourceDigest, StringComparison.Ordinal)
            ? built
            : Expired<FeedbackCsvFile>();
    }

    private async Task<Result<FeedbackCsvFile>> BuildAsync(
        FeedbackActor actor, Guid presentationId, FeedbackCsvRequest request, CancellationToken cancellationToken)
    {
        var access = await AuthorizeAsync(actor, presentationId, request.Mode, cancellationToken);
        if (!access.IsSuccess)
            return Forward<FeedbackCsvFile, PresentationFeedbackScope>(access);

        var source = await repository.ReadVisibleAsync(access.Value!, FeedbackCsvFormatter.MaxRows, cancellationToken);
        if (!source.IsSuccess)
            return Forward<FeedbackCsvFile, FeedbackCsvSnapshot>(source);

        var context = new AuthorizationContext(actor.UserId, actor.TenantId, "SurveyFeedback", RedactionPurpose.ExportBuild,
            presentationId.ToString("N"), RedactionAuthorizationState.Allowed,
            request.Mode == "detail" ? FieldAccessPolicySnapshot.ThroughConfidential : FieldAccessPolicySnapshot.StandardAuthorized);
        var result = FeedbackCsvFormatter.Format(request, source.Value!, redaction, context);
        var current = await AuthorizeAsync(actor, presentationId, request.Mode, cancellationToken);
        return current.IsSuccess ? result : Forward<FeedbackCsvFile, PresentationFeedbackScope>(current);
    }

    private async Task<Result<AuthorizedExport>> FindAuthorizedRequestAsync(
        Guid id, CancellationToken cancellationToken)
    {
        var actor = await CurrentActorAsync(cancellationToken);
        if (actor is null)
            return SurveyFeedbackService.AuthenticationRequired<AuthorizedExport>();

        var request = await repository.FindRequestAsync(actor.TenantId, actor.UserId, id, cancellationToken);
        if (request is null)
            return SurveyFeedbackService.NotFound<AuthorizedExport>();

        var access = await AuthorizeAsync(actor, request.PresentationId, request.Mode, cancellationToken);
        return access.IsSuccess
            ? Result<AuthorizedExport>.Success(new AuthorizedExport(actor, request, access.Value!))
            : Forward<AuthorizedExport, PresentationFeedbackScope>(access);
    }

    private async Task<Result<PresentationFeedbackScope>> AuthorizeAsync(
        FeedbackActor actor, Guid presentationId, string? mode, CancellationToken cancellationToken)
    {
        if (!await CurrentSessionAsync(actor, cancellationToken) ||
            !await authorization.IsCurrentMemberAsync(actor, cancellationToken))
            return SurveyFeedbackService.AuthenticationRequired<PresentationFeedbackScope>();

        var access = await authorization.ReadAccessAsync(actor, presentationId, cancellationToken);
        if (access is not { CanRead: true })
            return SurveyFeedbackService.NotFound<PresentationFeedbackScope>();

        if (mode is not ("aggregate" or "detail"))
            return SurveyFeedbackService.Invalid<PresentationFeedbackScope>();

        return await authorization.CanExportAsync(actor, access, detail: mode == "detail", cancellationToken)
            ? Result<PresentationFeedbackScope>.Success(access.Scope)
            : Failure<PresentationFeedbackScope>("CapabilityDenied", "The current scope does not permit this export.");
    }

    private async Task<FeedbackActor?> CurrentActorAsync(CancellationToken cancellationToken)
    {
        var actor = await authorization.ReadActorAsync(cancellationToken);
        return actor is not null && await CurrentSessionAsync(actor, cancellationToken) ? actor : null;
    }

    private async Task<bool> CurrentSessionAsync(FeedbackActor actor, CancellationToken cancellationToken)
    {
        if (currentUser.SessionId is not { } sessionId || !currentUser.IsAuthenticated ||
            currentUser.UserId != actor.UserId)
            return false;

        var session = await sessions.GetCurrentByIdWithUserAsync(sessionId, cancellationToken);
        return session is { RevokedAt: null, User: { Status: UserStatus.Active, DeletedAt: null } } &&
            session.UserId == actor.UserId && clock.UtcNow < session.ExpiresAt;
    }

    private Task AuditAsync(FeedbackActor actor, PresentationFeedbackScope scope, string action, Guid entityId,
        CancellationToken cancellationToken) =>
        audit.LogAsync(new AuditLogEntry(actor.UserId, action, "SurveyFeedbackCsv", entityId,
            WorkspaceId: scope.WorkspaceId, ProjectId: scope.ProjectId, TenantId: actor.TenantId), cancellationToken);

    private static ApplicationErrorDetail? ValidateKey(string? key) =>
        string.IsNullOrWhiteSpace(key)
            ? new ApplicationErrorDetail("MissingIdempotencyKey", "An Idempotency-Key header is required.", Target: "header.Idempotency-Key")
            : key.Length is < 8 or > 128 || key.Any(character => character is < ' ' or > '~')
                ? new ApplicationErrorDetail("InvalidIdempotencyKey", "The Idempotency-Key header is invalid.", Target: "header.Idempotency-Key")
                : null;

    private static string Digest<T>(T intent) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(intent)));

    private static ApplicationErrorDetail Expiry() =>
        new("ExportExpired", "The export is no longer available.");

    private static Result<T> Expired<T>() => Result<T>.Failure(Expiry());

    private static ApplicationErrorDetail GrantFailure(ApplicationErrorDetail detail) =>
        detail.Code == "ExportTooLarge" ? Expiry() : detail;

    private static Result<T> IdempotencyFailure<T>(IdempotentCreateDisposition disposition) =>
        disposition == IdempotentCreateDisposition.RequestMismatch
            ? Failure<T>("IdempotencyConflict", "The key was already used for a different export operation.")
            : Failure<T>("ConcurrentModification", "The export request could not be reconciled.");

    private static Result<T> Failure<T>(string code, string message) =>
        Result<T>.Failure(new ApplicationErrorDetail(code, message));

    private static Result<T> Forward<T, TSource>(Result<TSource> source) =>
        Result<T>.Failure(source.ErrorDetail!);

    private sealed record AuthorizedExport(FeedbackActor Actor, SurveyFeedbackExportRequest Request, PresentationFeedbackScope Scope);

    private sealed class FeedbackCsvRejectedException(ApplicationErrorDetail detail) : Exception(detail.Code)
    {
        public ApplicationErrorDetail Detail { get; } = detail;
    }
}

using System.Text.Json.Serialization;
using Coglatas.Application.Common;
using Coglatas.Application.Forms.PublicSurveys;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Forms.SurveyManagement;

public sealed record AqChangeCodeRequest(long ExpectedVersionNo, string? Reason);
public sealed record AqChangedCode(long VersionNo, string State,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EntryRef);
public sealed record AqPresentationQrEntry(string Title, string EntryRef);

public sealed partial class AqSurveyManagementService
{
    public Task<Result<AqChangedCode>> ChangeProgramCodeAsync(
        Guid eventId, AqChangeCodeRequest request, bool rotate, CancellationToken cancellationToken) =>
        WriteEventAsync(eventId, Manage, async (scope, token) =>
        {
            var settings = await store.FindAsync<EventFeedbackSettings>(x => x.EventId == eventId, token);
            if (settings is null)
                return NotFound<AqChangedCode>();
            var versionError = CheckVersion<AqChangedCode>(settings.VersionNo, request.ExpectedVersionNo);
            if (versionError is not null)
                return versionError;

            var current = await store.FindAsync<SurveyPublicCode>(x => x.EventId == eventId && x.RevokedAtUtc == null, token);
            if (current is null && !rotate)
                return Conflict<AqChangedCode>("There is no active program entry code.");
            settings.AdvancePublicEntryVersion(request.ExpectedVersionNo);
            await RevokeBeforeReplacementAsync(current, token);
            var replacement = rotate ? NewOpaqueToken() : null;
            if (replacement is not null)
                store.Add(SurveyPublicCode.ForProgram(settings, AqPublicSurveyService.Hash(replacement), codec.ProtectEntry(replacement)));

            await AuditAsync(scope.Actor, rotate ? "aq.program.code.rotate" : "aq.program.code.revoke", eventId, token);
            return Success(new AqChangedCode(settings.VersionNo, rotate ? "active" : "revoked", replacement));
        }, cancellationToken);

    public Task<Result<AqChangedCode>> ChangeTargetCodeAsync(
        Guid targetId, AqChangeCodeRequest request, bool rotate, CancellationToken cancellationToken) =>
        store.WriteAsync(async token =>
        {
            var scoped = await RequireTargetAsync(targetId, Manage, token);
            if (!scoped.IsSuccess)
                return Failure<AqChangedCode>(scoped.ErrorDetail!);
            var (scope, target) = scoped.Value!;
            var versionError = CheckVersion<AqChangedCode>(target.VersionNo, request.ExpectedVersionNo);
            if (versionError is not null)
                return versionError;
            if (target.PublishedSchemaId is null)
                return Conflict<AqChangedCode>("Publish the survey target before issuing a code.");

            var current = await store.FindAsync<SurveyPublicCode>(x => x.SurveyTargetId == targetId && x.RevokedAtUtc == null, token);
            if (current is null && !rotate)
                return Conflict<AqChangedCode>("There is no active target entry code.");
            target.AdvancePublicEntryVersion(request.ExpectedVersionNo);
            await RevokeBeforeReplacementAsync(current, token);
            var replacement = rotate ? IssueTargetCode(target) : null;
            await AuditAsync(scope.Actor, rotate ? "aq.target.code.rotate" : "aq.target.code.revoke", targetId, token);
            return Success(new AqChangedCode(target.VersionNo, rotate ? "active" : "revoked", replacement));
        }, cancellationToken);

    public async Task<Result<IReadOnlyList<AqPresentationQrEntry>>> ListQrEntriesAsync(
        Guid campaignId, Guid? targetId, CancellationToken cancellationToken)
    {
        var scoped = await RequireCampaignAsync(campaignId, Manage, cancellationToken);
        if (!scoped.IsSuccess)
            return Failure<IReadOnlyList<AqPresentationQrEntry>>(scoped.ErrorDetail!);
        var items = await store.PageAsync<SurveyTarget>(x => x.CampaignId == campaignId && x.PublishedSchemaId != null
            && (!targetId.HasValue || x.Id == targetId), null, AssignmentLimit + 1, cancellationToken);
        if (items.Count > AssignmentLimit)
            return Failure<IReadOnlyList<AqPresentationQrEntry>>(new("ExportTooLarge", "The selected QR batch exceeds the supported size."));

        var targetIds = items.Select(x => x.Id).ToArray();
        var presentationIds = items.Select(x => x.PresentationId).ToArray();
        var codes = await store.PageAsync<SurveyPublicCode>(
            x => x.SurveyTargetId.HasValue && targetIds.Contains(x.SurveyTargetId.Value) && x.RevokedAtUtc == null,
            null, AssignmentLimit, cancellationToken);
        var presentations = await store.PageAsync<Presentation>(x => presentationIds.Contains(x.Id), null, AssignmentLimit, cancellationToken);
        var codesByTarget = codes.ToDictionary(x => x.SurveyTargetId!.Value);
        var presentationsById = presentations.ToDictionary(x => x.Id);
        var entries = new List<AqPresentationQrEntry>();
        foreach (var target in items)
        {
            if (codesByTarget.GetValueOrDefault(target.Id)?.ProtectedReprintValue is not { } protectedCode
                || presentationsById.GetValueOrDefault(target.PresentationId) is not { } presentation)
                continue;
            var code = codec.UnprotectEntry(protectedCode);
            if (code is not { Length: 64 } || code.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
                || !string.Equals(
                AqPublicSurveyService.Hash(code), codesByTarget[target.Id].CodeDigest, StringComparison.Ordinal))
                continue;
            entries.Add(new AqPresentationQrEntry(presentation.Title, code));
        }
        return entries.Count == 0
            ? NotFound<IReadOnlyList<AqPresentationQrEntry>>()
            : Success<IReadOnlyList<AqPresentationQrEntry>>(entries.OrderBy(x => x.Title, StringComparer.Ordinal).ToArray());
    }

    private string IssueTargetCode(SurveyTarget target)
    {
        var code = NewOpaqueToken();
        store.Add(SurveyPublicCode.ForTarget(target, AqPublicSurveyService.Hash(code), codec.ProtectEntry(code)));
        return code;
    }

    private async Task RevokeBeforeReplacementAsync(SurveyPublicCode? current, CancellationToken token)
    {
        if (current is null)
            return;
        current.Revoke(clock.UtcNow);
        // Persist revocation first under the active-code unique index, inside the
        // same transaction as the replacement. Neither state leaks mid-rotation.
        await store.FlushAsync(token);
    }
}

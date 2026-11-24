using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.SurveyFeedback;

public sealed record PrivateNoteDto(Guid PrivateNoteId, string Body, DateTimeOffset CreatedAtUtc, long VersionNo);
public sealed record SharedFeedbackDto(Guid SharedFeedbackId, string Body, DateTimeOffset CreatedAtUtc, long VersionNo);

public sealed class FeedbackNotesService(
    SurveyFeedbackAuthorization authorization,
    IFeedbackLifecycleRepository repository,
    IWorkspaceRepository workspaces,
    IAuditLogger audit,
    ISurveyFeedbackInvalidation invalidation,
    ISurveyFeedbackCursor cursors,
    IClock clock)
{
    public Task<Result<PrivateNoteDto>> CreatePrivateAsync(Guid presentationId, string? body,
        CancellationToken cancellationToken) =>
        repository.InWriteTransactionAsync(async token =>
        {
            var actor = await authorization.ReadActorAsync(token);
            if (actor is null)
            {
                return SurveyFeedbackService.AuthenticationRequired<PrivateNoteDto>();
            }

            var access = await authorization.ReadAccessAsync(actor, presentationId, token);
            if (!await CanUsePrivateNotesAsync(actor, access, token))
            {
                return SurveyFeedbackService.NotFound<PrivateNoteDto>();
            }

            if (!ValidNote(body))
            {
                return SurveyFeedbackService.Invalid<PrivateNoteDto>();
            }

            var note = new TeacherPrivateNote
            {
                TenantId = actor.TenantId,
                PresentationId = presentationId,
                AuthorUserId = actor.UserId,
                CreatedAt = clock.UtcNow,
                Text = body!
            };
            repository.AddPrivateNote(note);
            await audit.LogUserActionAsync(actor.UserId, "FeedbackSecurity.PrivateNoteCreated",
                nameof(TeacherPrivateNote), note.Id, cancellationToken: token);
            return Result<PrivateNoteDto>.Success(ToDto(note));
        }, cancellationToken);

    public Task<Result<SharedFeedbackDto>> CreateSharedAsync(Guid presentationId, string? body,
        CancellationToken cancellationToken) =>
        repository.InWriteTransactionAsync(async token =>
        {
            var actor = await authorization.ReadActorAsync(token);
            if (actor is null)
            {
                return SurveyFeedbackService.AuthenticationRequired<SharedFeedbackDto>();
            }

            var access = await authorization.ReadAccessAsync(actor, presentationId, token);
            if (!await CanShareAsync(actor, access, token))
            {
                return SurveyFeedbackService.NotFound<SharedFeedbackDto>();
            }

            if (!ValidNote(body))
            {
                return SurveyFeedbackService.Invalid<SharedFeedbackDto>();
            }

            var note = new SharedTeacherFeedback
            {
                TenantId = actor.TenantId,
                PresentationId = presentationId,
                AuthorUserId = actor.UserId,
                CreatedAt = clock.UtcNow,
                Text = body!
            };
            repository.AddSharedFeedback(note);
            await audit.LogUserActionAsync(actor.UserId, "Feedback.SharedTeacherFeedbackCreated",
                nameof(SharedTeacherFeedback), note.Id, cancellationToken: token);
            await invalidation.InvalidateAsync(actor.TenantId, presentationId, token);
            return Result<SharedFeedbackDto>.Success(ToDto(note));
        }, cancellationToken);

    public async Task<Result<FeedbackPage<PrivateNoteDto>>> ReadPrivateAsync(Guid presentationId, string? cursor,
        int pageSize, CancellationToken cancellationToken)
    {
        var actor = await authorization.ReadActorAsync(cancellationToken);
        if (actor is null)
        {
            return SurveyFeedbackService.AuthenticationRequired<FeedbackPage<PrivateNoteDto>>();
        }

        var access = await authorization.ReadAccessAsync(actor, presentationId, cancellationToken);
        if (!await CanUsePrivateNotesAsync(actor, access, cancellationToken))
        {
            return SurveyFeedbackService.NotFound<FeedbackPage<PrivateNoteDto>>();
        }

        var scope = new FeedbackCursorScope(actor.TenantId, actor.UserId, presentationId, "private-notes");
        if (pageSize is < 1 or > 100 || !ReadPosition(cursor, scope, out var after))
        {
            return SurveyFeedbackService.Invalid<FeedbackPage<PrivateNoteDto>>();
        }

        var rows = await repository.ReadPrivateNotesAsync(actor.TenantId, presentationId, actor.UserId,
            after, pageSize + 1, cancellationToken);
        return Result<FeedbackPage<PrivateNoteDto>>.Success(Page(rows.Select(ToDto).ToArray(), pageSize, scope,
            note => new FeedbackPagePosition(note.CreatedAtUtc, note.PrivateNoteId)));
    }

    public async Task<Result<FeedbackPage<SharedFeedbackDto>>> ReadSharedAsync(Guid presentationId, string? cursor,
        int pageSize, CancellationToken cancellationToken)
    {
        var actor = await authorization.ReadActorAsync(cancellationToken);
        if (actor is null)
        {
            return SurveyFeedbackService.AuthenticationRequired<FeedbackPage<SharedFeedbackDto>>();
        }

        var access = await authorization.ReadAccessAsync(actor, presentationId, cancellationToken);
        if (access is not { CanRead: true } ||
            !access.Presenter && !await CanShareAsync(actor, access, cancellationToken))
        {
            return SurveyFeedbackService.NotFound<FeedbackPage<SharedFeedbackDto>>();
        }

        var scope = new FeedbackCursorScope(actor.TenantId, actor.UserId, presentationId,
            access.Presenter ? "shared-feedback/presenter" : "shared-feedback/author");
        if (pageSize is < 1 or > 100 || !ReadPosition(cursor, scope, out var after))
        {
            return SurveyFeedbackService.Invalid<FeedbackPage<SharedFeedbackDto>>();
        }

        var rows = await repository.ReadSharedFeedbackAsync(actor.TenantId, presentationId,
            access.Presenter ? null : actor.UserId, after, pageSize + 1, cancellationToken);
        return Result<FeedbackPage<SharedFeedbackDto>>.Success(Page(rows.Select(ToDto).ToArray(), pageSize, scope,
            note => new FeedbackPagePosition(note.CreatedAtUtc, note.SharedFeedbackId)));
    }

    private async Task<bool> CanUsePrivateNotesAsync(FeedbackActor actor, PresentationFeedbackAccess? access,
        CancellationToken cancellationToken)
    {
        if (access is not { CanRead: true, Reviewer: true } ||
            !await authorization.HasCapabilityAsync(actor, access.Scope, "feedback.note.private", cancellationToken))
        {
            return false;
        }
        return await workspaces.GetCurrentMemberAsync(access.Scope.WorkspaceId, actor.UserId, cancellationToken) is
            { Status: MembershipStatus.Active, Role: WorkspaceRole.Adviser };
    }

    private async Task<bool> CanShareAsync(FeedbackActor actor, PresentationFeedbackAccess? access,
        CancellationToken cancellationToken) =>
        access is { CanRead: true, Reviewer: true } &&
        await authorization.HasCapabilityAsync(actor, access.Scope, "feedback.feedback.share", cancellationToken);

    private static bool ValidNote(string? body) => !string.IsNullOrWhiteSpace(body) && body.Length <= 10000;

    private static PrivateNoteDto ToDto(TeacherPrivateNote row) => new(row.Id, row.Text, row.CreatedAt, 1);

    private static SharedFeedbackDto ToDto(SharedTeacherFeedback row) => new(row.Id, row.Text, row.CreatedAt, 1);

    private bool ReadPosition(string? cursor, FeedbackCursorScope scope, out FeedbackPagePosition? position)
    {
        position = null;
        return cursor is null || cursors.TryRead(cursor, scope, out position);
    }

    private FeedbackPage<T> Page<T>(T[] rows, int size, FeedbackCursorScope scope, Func<T, FeedbackPagePosition> position) =>
        rows.Length > size
            ? new FeedbackPage<T>(rows[..size], cursors.Write(scope, position(rows[size - 1])))
            : new FeedbackPage<T>(rows);
}

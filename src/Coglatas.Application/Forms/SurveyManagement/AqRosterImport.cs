using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Forms.SurveyManagement;

public sealed record AqRosterInput(int Row, Guid PresentationId, string Role, Guid? PersonRef, string? PendingRosterRef);
public sealed record AqRosterDiagnostic(int Row, string Code);
public sealed record AqRosterAssignment(int Row, string Role, Guid? PersonRef, string? PendingRosterRef);
public sealed record AqRosterSourceVersion(string SourceRef, long VersionNo);
public sealed record AqRosterConfirmation(int Row, AqParticipantAssignment? Assignment, AqPendingPresenter? PendingPresenter);
public sealed record AqRosterPreviewState(
    Guid TenantId,
    Guid ActorId,
    Guid EventId,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<AqRosterInput> Rows,
    IReadOnlyList<AqRosterSourceVersion> SourceVersions);
public sealed record AqRosterPreview(
    string? PreviewRef,
    IReadOnlyList<AqRosterDiagnostic> Diagnostics,
    IReadOnlyList<AqRosterAssignment> ProposedAssignments,
    IReadOnlyList<AqRosterSourceVersion> ExpectedSourceVersions);
public sealed record AqCommitRosterRequest(
    string PreviewRef,
    IReadOnlyList<AqRosterConfirmation>? ConfirmedAssignments,
    IReadOnlyList<AqRosterSourceVersion>? ExpectedSourceVersions);
public sealed record AqRosterReceipt(int PresentationsUpdated, int AssignmentsApplied);

public interface IAqRosterPreviewCodec
{
    string Protect(AqRosterPreviewState preview);
    AqRosterPreviewState? Unprotect(string value);
}

public sealed partial class AqSurveyManagementService
{
    private const int RosterLimit = 64;
    private const string RosterOperation = "aq.roster.import";

    public async Task<Result<AqRosterPreview>> PreviewRosterAsync(
        Guid eventId, IReadOnlyList<AqRosterInput> rows, IReadOnlyList<AqRosterDiagnostic> parseErrors,
        IAqRosterPreviewCodec codec, CancellationToken cancellationToken)
    {
        var scope = await RequireEventAsync<AqRosterPreview>(eventId, Assign, cancellationToken);
        if (!scope.IsSuccess)
            return Failure<AqRosterPreview>(scope.ErrorDetail!);
        if (rows.Count + parseErrors.Count is < 1 or > RosterLimit)
            return Invalid<AqRosterPreview>("Provide between 1 and 64 data rows.");

        var diagnostics = parseErrors.ToList();
        var versions = new List<AqRosterSourceVersion>();
        var proposed = new List<AqRosterAssignment>();
        foreach (var group in rows.GroupBy(row => row.PresentationId).OrderBy(group => group.Key))
        {
            var presentation = await store.FindAsync<Presentation>(item =>
                item.EventId == eventId && item.Id == group.Key, cancellationToken);
            if (presentation is null)
            {
                diagnostics.AddRange(group.Select(row => new AqRosterDiagnostic(row.Row, "PresentationUnavailable")));
                continue;
            }

            versions.Add(new(presentation.Id.ToString("D"), presentation.VersionNo));
            var distinct = new HashSet<(string Role, Guid? Person, string? Pending)>();
            foreach (var row in group)
            {
                if (!distinct.Add((row.Role, row.PersonRef, row.PendingRosterRef)))
                {
                    diagnostics.Add(new(row.Row, "DuplicateAssignment"));
                    continue;
                }
                if (await BuildRosterParticipantAsync(scope.Value!, presentation, row, cancellationToken) is null)
                {
                    diagnostics.Add(new(row.Row, "AssignmentUnavailable"));
                    continue;
                }
                proposed.Add(new(row.Row, row.Role, row.PersonRef, row.PendingRosterRef));
            }
        }

        return diagnostics.Count > 0
            ? Success(new AqRosterPreview(null, diagnostics.OrderBy(row => row.Row).ToArray(), proposed.OrderBy(row => row.Row).ToArray(), []))
            : Success(new AqRosterPreview(
                codec.Protect(new(currentTenant.TenantId, scope.Value!.Actor, eventId, clock.UtcNow.AddMinutes(15), rows, versions)),
                [], proposed.OrderBy(row => row.Row).ToArray(), versions));
    }

    public Task<Result<AqRosterReceipt>> CommitRosterAsync(
        Guid eventId, AqCommitRosterRequest request, string key, IAqRosterPreviewCodec codec, CancellationToken cancellationToken) =>
        WriteEventAsync<AqRosterReceipt>(eventId, Assign, async (scope, token) =>
        {
            if (key.Length is < 8 or > 128 || key.All(char.IsWhiteSpace) || key.Any(c => c is < ' ' or > '~'))
                return Failure<AqRosterReceipt>(new("InvalidIdempotencyKey", "A valid idempotency key is required."));
            if (request.PreviewRef is not { Length: > 0 and <= 32768 }
                || request.ConfirmedAssignments is not { Count: > 0 and <= RosterLimit } confirmed
                || request.ExpectedSourceVersions is not { Count: > 0 and <= RosterLimit } expected)
                return Invalid<AqRosterReceipt>("Provide the preview, confirmed assignments, and source versions.");

            var requestDigest = RosterDigest(JsonSerializer.Serialize(new
            {
                EventId = eventId,
                request.PreviewRef,
                Assignments = confirmed.OrderBy(item => item.Row),
                Versions = expected.OrderBy(item => item.SourceRef, StringComparer.Ordinal),
            }));
            var keyDigest = RosterDigest(key);
            var previous = await store.FindAsync<IdempotencyRecord>(row =>
                row.ActorUserId == scope.Actor && row.Operation == RosterOperation && row.KeyHash == keyDigest, token);
            if (previous is not null)
                return previous.ResourceId == eventId && previous.RequestHash == requestDigest
                    ? Success(new AqRosterReceipt(expected.Count, confirmed.Count))
                    : Failure<AqRosterReceipt>(new("IdempotencyConflict", "This key was used for a different import."));

            var preview = codec.Unprotect(request.PreviewRef);
            if (preview is null || preview.EventId != eventId || preview.TenantId != currentTenant.TenantId
                || preview.ActorId != scope.Actor || preview.ExpiresAt <= clock.UtcNow)
                return Conflict<AqRosterReceipt>("The roster preview is unavailable or expired.");

            var normalized = confirmed.OrderBy(item => item.Row).ToArray();
            var roster = preview.Rows.OrderBy(item => item.Row).ToArray();
            if (normalized.Length != roster.Length || !normalized.Zip(roster).All(pair => Matches(pair.First, pair.Second))
                || !expected.OrderBy(item => item.SourceRef, StringComparer.Ordinal)
                    .SequenceEqual(preview.SourceVersions.OrderBy(item => item.SourceRef, StringComparer.Ordinal)))
                return Conflict<AqRosterReceipt>("Confirm the assignments and source versions from the current preview.");

            var changes = new List<(Presentation Presentation, IReadOnlyList<PresentationParticipant> Prior, List<PresentationParticipant> Next)>();
            foreach (var group in roster.GroupBy(item => item.PresentationId))
            {
                var presentation = await store.FindAsync<Presentation>(item => item.EventId == eventId && item.Id == group.Key, token);
                var version = preview.SourceVersions.Single(item => item.SourceRef == group.Key.ToString("D")).VersionNo;
                if (presentation is null || presentation.VersionNo != version)
                    return Conflict<AqRosterReceipt>("A presentation changed after the roster was previewed.");

                var prior = await store.PageAsync<PresentationParticipant>(
                    item => item.PresentationId == group.Key, null, AssignmentLimit + 1, token);
                if (prior.Count > AssignmentLimit)
                    return Conflict<AqRosterReceipt>("The current assignment limit is exceeded.");
                var replacements = new List<PresentationParticipant>();
                foreach (var row in group)
                {
                    var participant = await BuildRosterParticipantAsync(scope, presentation, row, token);
                    if (participant is null)
                        return Conflict<AqRosterReceipt>("An assignment is no longer eligible.");
                    replacements.Add(participant);
                }
                changes.Add((presentation, prior, replacements));
            }

            var affected = changes.Select(change => change.Presentation.Id).ToArray();
            var readers = await CaptureFeedbackAccessAsync(affected, token);
            foreach (var change in changes)
            {
                change.Presentation.AdvanceParticipantsVersion(change.Presentation.VersionNo);
                foreach (var prior in change.Prior)
                    store.Remove(prior);
            }
            await store.FlushAsync(token);
            foreach (var change in changes)
            {
                foreach (var next in change.Next)
                    store.Add(next);
                await AuditAsync(scope.Actor, "aq.presentation.assignments.import", change.Presentation.Id, token);
            }
            store.Add(new IdempotencyRecord
            {
                TenantId = currentTenant.TenantId, ActorUserId = scope.Actor, Operation = RosterOperation,
                KeyHash = keyDigest, RequestHash = requestDigest, ResourceType = "AqEventRoster", ResourceId = eventId,
            });
            await PublishFeedbackAccessChangesAsync(affected, readers, token);
            return Success(new AqRosterReceipt(changes.Count, roster.Length));
        }, cancellationToken);

    private async Task<PresentationParticipant?> BuildRosterParticipantAsync(
        EventScope scope, Presentation presentation, AqRosterInput row, CancellationToken cancellationToken)
    {
        if (!TryWireEnum(row.Role, out PresentationParticipantRole role))
            return null;
        if (row.PendingRosterRef is { } pending)
        {
            return role == PresentationParticipantRole.Presenter && row.PersonRef is null && pending.Length is > 0 and <= 80
                && pending.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                ? PresentationParticipant.PendingPresenter(presentation, pending) : null;
        }
        if (row.PersonRef is not { } id || id == Guid.Empty)
            return null;

        var member = await store.FindAsync<WorkspaceMember>(item =>
            item.WorkspaceId == scope.Project.WorkspaceId && item.UserId == id && item.Status == MembershipStatus.Active, cancellationToken);
        var tenantMember = await store.FindAsync<TenantUser>(item =>
            item.UserId == id && item.Status == TenantUserStatus.Active, cancellationToken);
        var user = await users.GetByIdAsync(id, cancellationToken);
        return member is not null && tenantMember is not null && user is { Status: UserStatus.Active, DeletedAt: null }
            ? PresentationParticipant.Assign(presentation, scope.Project, member, role) : null;
    }

    private static bool Matches(AqRosterConfirmation confirmed, AqRosterInput row) =>
        confirmed.Row == row.Row && (row.PersonRef is { } person
            ? confirmed.PendingPresenter is null && confirmed.Assignment is { } assignment
                && assignment.PersonRef == person && assignment.Role == row.Role
            : confirmed.Assignment is null && confirmed.PendingPresenter?.PendingRosterRef == row.PendingRosterRef);

    private static string RosterDigest(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

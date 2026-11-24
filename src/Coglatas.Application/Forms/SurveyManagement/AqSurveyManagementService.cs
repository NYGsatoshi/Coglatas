using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Application.Common.Interfaces;
using Coglatas.Application.Events;
using Coglatas.Application.Tenancy;
using Coglatas.Domain.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Forms.SurveyManagement;

public sealed partial class AqSurveyManagementService(
    IAqSurveyManagementStore store,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IEventAuthorizationService events,
    IFormAuthorizationService forms,
    IUserRepository users,
    ICapabilityGrantEvaluator capabilities,
    IAuditLogger audit,
    IClock clock)
{
    private const string Manage = "survey.manage";
    private const string Assign = "presentation.assign";
    private const string EditQuestions = "survey.questions.edit";
    private const string ManageRetention = "survey.retention.manage";
    private const int AssignmentLimit = 100;
    private static readonly (string Name, SurveyProgramFields Flag)[] ProgramFieldNames =
    [
        ("title", SurveyProgramFields.Title), ("time", SurveyProgramFields.Time),
        ("venue", SurveyProgramFields.Venue), ("presenterNames", SurveyProgramFields.PresenterNames),
        ("timeZone", SurveyProgramFields.TimeZone)
    ];

    private sealed record EventScope(Guid Actor, ActivityEvent Event, Project Project);

    public async Task<Result<AqManagementPage<AqManagedPresentation>>> ListPresentationsAsync(
        Guid eventId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var scope = await RequireEventAsync<AqManagementPage<AqManagedPresentation>>(eventId, Manage, cancellationToken);
        return scope.IsSuccess
            ? await PageAsync<Presentation, AqManagedPresentation>(item => item.EventId == eventId,
                eventId, cursor, pageSize, ToPresentation, cancellationToken)
            : Failure<AqManagementPage<AqManagedPresentation>>(scope.ErrorDetail!);
    }

    public async Task<Result<AqManagedPresentation>> GetPresentationAsync(
        Guid eventId, Guid presentationId, CancellationToken cancellationToken)
    {
        var scope = await RequireEventAsync<AqManagedPresentation>(eventId, Manage, cancellationToken);
        if (!scope.IsSuccess)
            return Failure<AqManagedPresentation>(scope.ErrorDetail!);

        var item = await store.FindAsync<Presentation>(x => x.Id == presentationId && x.EventId == eventId, cancellationToken);
        return item is null ? NotFound<AqManagedPresentation>() : Success(ToPresentation(item));
    }

    public Task<Result<AqManagedPresentation>> CreatePresentationAsync(
        Guid eventId, AqPresentationCreateRequest request, CancellationToken cancellationToken) =>
        WriteEventAsync(eventId, Manage, async (scope, token) =>
        {
            if (!ValidSchedule(request.Schedule))
                return Invalid<AqManagedPresentation>("Supply a valid presentation schedule.");
            var schedule = request.Schedule!;
            var item = Presentation.Create(scope.Event, schedule.Title, schedule.Venue, schedule.StartAt, schedule.EndAt);
            store.Add(item);
            await AuditAsync(scope.Actor, "aq.presentation.create", item.Id, token);
            return Success(ToPresentation(item));
        }, cancellationToken);

    public Task<Result<AqManagedPresentation>> UpdatePresentationAsync(
        Guid eventId, Guid presentationId, AqPresentationUpdateRequest request, CancellationToken cancellationToken) =>
        WriteEventAsync(eventId, Manage, async (scope, token) =>
        {
            var item = await store.FindAsync<Presentation>(x => x.Id == presentationId && x.EventId == eventId, token);
            if (item is null)
                return NotFound<AqManagedPresentation>();
            var versionError = CheckVersion<AqManagedPresentation>(item.VersionNo, request.ExpectedVersionNo);
            if (versionError is not null)
                return versionError;
            if (!ValidSchedule(request.Schedule))
                return Invalid<AqManagedPresentation>("Supply a valid presentation schedule.");

            var schedule = request.Schedule!;
            item.UpdateSchedule(request.ExpectedVersionNo, schedule.Title, schedule.Venue, schedule.StartAt, schedule.EndAt);
            await AuditAsync(scope.Actor, "aq.presentation.update", item.Id, token);
            return Success(ToPresentation(item));
        }, cancellationToken);

    public async Task<Result<AqParticipantSettings>> GetParticipantsAsync(Guid presentationId, CancellationToken cancellationToken)
    {
        var scope = await RequirePresentationAsync(presentationId, Assign, cancellationToken);
        if (!scope.IsSuccess)
            return Failure<AqParticipantSettings>(scope.ErrorDetail!);

        var item = scope.Value!.Presentation;
        var assignments = await store.PageAsync<PresentationParticipant>(
            x => x.PresentationId == presentationId, null, AssignmentLimit + 1, cancellationToken);
        return assignments.Count > AssignmentLimit
            ? Conflict<AqParticipantSettings>("The current assignment limit is exceeded.")
            : Success(ToParticipants(item, assignments));
    }

    public Task<Result<AqParticipantSettings>> SetParticipantsAsync(
        Guid presentationId, AqParticipantUpdateRequest request, CancellationToken cancellationToken) =>
        store.WriteAsync(async token =>
        {
            var scoped = await RequirePresentationAsync(presentationId, Assign, token);
            if (!scoped.IsSuccess)
                return Failure<AqParticipantSettings>(scoped.ErrorDetail!);
            var (scope, presentation) = scoped.Value!;
            var versionError = CheckVersion<AqParticipantSettings>(presentation.VersionNo, request.ExpectedVersionNo);
            if (versionError is not null)
                return versionError;
            var pending = request.PendingPresenters ?? [];
            if (request.Assignments is not { } assignments || assignments.Count + pending.Count > AssignmentLimit
                || pending.Any(x => string.IsNullOrWhiteSpace(x.PendingRosterRef) || x.PendingRosterRef.Length > 200)
                || pending.Select(x => x.PendingRosterRef).Distinct(StringComparer.Ordinal).Count() != pending.Count)
            {
                return Invalid<AqParticipantSettings>("Supply a bounded, distinct set of participants.");
            }

            var replacements = new List<PresentationParticipant>();
            var assignedRoles = new HashSet<(Guid, PresentationParticipantRole)>();
            foreach (var assignment in assignments)
            {
                if (!TryWireEnum(assignment.Role, out PresentationParticipantRole role)
                    || !assignedRoles.Add((assignment.PersonRef, role)))
                    return Invalid<AqParticipantSettings>("An assignment role or recipient is invalid.");
                var member = await store.FindAsync<WorkspaceMember>(x => x.WorkspaceId == scope.Project.WorkspaceId
                    && x.UserId == assignment.PersonRef && x.Status == MembershipStatus.Active, token);
                var tenantMember = await store.FindAsync<TenantUser>(x => x.UserId == assignment.PersonRef
                    && x.Status == TenantUserStatus.Active, token);
                var user = await users.GetByIdAsync(assignment.PersonRef, token);
                if (member is null || tenantMember is null || user is not { Status: UserStatus.Active, DeletedAt: null })
                    return Invalid<AqParticipantSettings>("An assignment recipient is not eligible in this workspace.");
                replacements.Add(PresentationParticipant.Assign(presentation, scope.Project, member, role));
            }

            replacements.AddRange(pending.Select(x => PresentationParticipant.PendingPresenter(presentation, x.PendingRosterRef)));
            var current = await store.PageAsync<PresentationParticipant>(
                x => x.PresentationId == presentation.Id, null, AssignmentLimit + 1, token);
            if (current.Count > AssignmentLimit)
                return Conflict<AqParticipantSettings>("The current assignment limit is exceeded.");

            presentation.AdvanceParticipantsVersion(request.ExpectedVersionNo);
            foreach (var assigned in current)
                store.Remove(assigned);
            await store.FlushAsync(token);
            foreach (var assigned in replacements)
                store.Add(assigned);
            await AuditAsync(scope.Actor, "aq.presentation.assignments.update", presentation.Id, token);
            return Success(ToParticipants(presentation, replacements));
        }, cancellationToken);

    public async Task<Result<AqManagedEventSettings>> GetEventSettingsAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var scope = await RequireEventAsync<AqManagedEventSettings>(eventId, Manage, cancellationToken);
        if (!scope.IsSuccess)
            return Failure<AqManagedEventSettings>(scope.ErrorDetail!);

        var settings = await store.FindAsync<EventFeedbackSettings>(x => x.EventId == eventId, cancellationToken);
        if (settings is null)
            return NotFound<AqManagedEventSettings>();

        var entries = await ReadProgramEntriesAsync(eventId, cancellationToken);
        return entries is null ? Conflict<AqManagedEventSettings>("The current program limit is exceeded.")
            : Success(ToEventSettings(settings, entries));
    }

    public Task<Result<AqManagedEventSettings>> UpdateEventSettingsAsync(
        Guid eventId, AqSettingsUpdateRequest request, CancellationToken cancellationToken) =>
        WriteEventAsync(eventId, Manage, async (scope, token) =>
        {
            var current = await store.FindAsync<EventFeedbackSettings>(x => x.EventId == eventId, token);
            var versionError = CheckVersion<AqManagedEventSettings>(current?.VersionNo ?? 1, request.ExpectedVersionNo);
            if (versionError is not null)
                return versionError;

            var start = request.StartAt ?? current?.StartsAtUtc;
            var end = request.EndAt ?? current?.EndsAtUtc;
            var zone = request.TimeZone ?? current?.TimeZoneId;
            if (start is null || end is null || string.IsNullOrWhiteSpace(zone))
                return Invalid<AqManagedEventSettings>("The reception start, end, and time zone are required.");
            if (current is not null && (request.StartAt.HasValue || request.EndAt.HasValue)
                && await store.AnyAsync<SurveyTarget>(x => x.EventId == eventId
                    && (x.StartsAtUtc >= end || x.EndsAtUtc <= start), token))
                return Invalid<AqManagedEventSettings>("The new Event reception would leave an existing target with no reception window.");
            var nameMode = current?.NameMode ?? SurveyNameMode.Anonymous;
            if (request.NameMode is { } mode && !TryWireEnum(mode, out nameMode))
                return Invalid<AqManagedEventSettings>("Unknown name mode.");

            var fields = current?.ProgramFields ?? SurveyProgramFields.None;
            if (request.PublicFields is { } proposed && !TryProgramFields(proposed, out fields))
                return Invalid<AqManagedEventSettings>("Unknown or repeated public field.");

            var published = request.ProgramPublished ?? current?.IsProgramPublished ?? false;
            if (published && fields.HasFlag(SurveyProgramFields.PresenterNames))
                return Failure<AqManagedEventSettings>(new("RestrictedFieldDenied", "Presenter names require the applicable publication approval."));
            var entries = await ReadProgramEntriesAsync(eventId, token);
            if (entries is null)
                return Conflict<AqManagedEventSettings>("The current program limit is exceeded.");
            var selected = request.ProgramTargets;
            if (selected is not null && (selected.Count > AssignmentLimit
                || selected.Select(x => x.PresentationId).Distinct().Count() != selected.Count))
                return Invalid<AqManagedEventSettings>("Supply a bounded program with one target per presentation.");

            var resolved = new List<SurveyProgramEntry>();
            if (selected is not null)
            {
                foreach (var entry in selected)
                {
                    var presentation = await store.FindAsync<Presentation>(
                        x => x.Id == entry.PresentationId && x.EventId == eventId, token);
                    var target = await store.FindAsync<SurveyTarget>(x => x.Id == entry.TargetId
                        && x.EventId == eventId && x.PresentationId == entry.PresentationId && x.PublishedSchemaId != null, token);
                    if (presentation is null || target is null)
                        return Invalid<AqManagedEventSettings>("Each program entry needs an eligible published target.");
                    resolved.Add(SurveyProgramEntry.Select(presentation, target));
                }
            }

            var binding = (request.AccountBindingAllowed ?? current?.AccountBinding == SurveyAccountBinding.ExplicitOptIn)
                ? SurveyAccountBinding.ExplicitOptIn : SurveyAccountBinding.Off;
            var settings = current ?? EventFeedbackSettings.Create(scope.Event, start.Value, end.Value, zone);
            settings.Configure(request.ExpectedVersionNo, start.Value, end.Value, zone,
                request.ReceptionStopped ?? settings.ReceptionStopped, request.ResultsStopped ?? settings.ResultsStopped,
                nameMode, binding, published, fields, presenterNamesApproved: false);
            if (current is null)
                store.Add(settings);
            if (selected is not null)
            {
                foreach (var entry in entries)
                    store.Remove(entry);
                await store.FlushAsync(token);
                foreach (var entry in resolved)
                    store.Add(entry);
                entries = resolved;
            }

            await AuditAsync(scope.Actor, "aq.event.settings.update", eventId, token);
            return Success(ToEventSettings(settings, entries));
        }, cancellationToken);

    public async Task<Result<AqPresentationResultsSettings>> GetPresentationResultsAsync(
        Guid presentationId, CancellationToken cancellationToken)
    {
        var scoped = await RequirePresentationAsync(presentationId, Manage, cancellationToken);
        if (!scoped.IsSuccess)
            return Failure<AqPresentationResultsSettings>(scoped.ErrorDetail!);
        var (scope, presentation) = scoped.Value!;
        var settings = await store.FindAsync<PresentationFeedbackSettings>(x => x.PresentationId == presentation.Id, cancellationToken);
        var parent = await store.FindAsync<EventFeedbackSettings>(x => x.EventId == scope.Event.Id, cancellationToken);
        return Success(ToResults(settings, parent));
    }

    public Task<Result<AqPresentationResultsSettings>> UpdatePresentationResultsAsync(
        Guid presentationId, AqPresentationResultsUpdateRequest request, CancellationToken cancellationToken) =>
        store.WriteAsync(async token =>
        {
            var scoped = await RequirePresentationAsync(presentationId, Manage, token);
            if (!scoped.IsSuccess)
                return Failure<AqPresentationResultsSettings>(scoped.ErrorDetail!);
            var (scope, presentation) = scoped.Value!;
            var current = await store.FindAsync<PresentationFeedbackSettings>(x => x.PresentationId == presentationId, token);
            var versionError = CheckVersion<AqPresentationResultsSettings>(current?.VersionNo ?? 1, request.ExpectedVersionNo);
            if (versionError is not null)
                return versionError;
            if (request.ResultsStopped is not { } stopped)
                return Invalid<AqPresentationResultsSettings>("Supply the desired result stop.");

            var settings = current ?? PresentationFeedbackSettings.Create(presentation);
            settings.SetResultsStop(request.ExpectedVersionNo, stopped);
            if (current is null)
                store.Add(settings);
            var parent = await store.FindAsync<EventFeedbackSettings>(x => x.EventId == scope.Event.Id, token);
            await AuditAsync(scope.Actor, "aq.presentation.results.update", presentationId, token);
            return Success(ToResults(settings, parent));
        }, cancellationToken);

    private async Task<IReadOnlyList<SurveyProgramEntry>?> ReadProgramEntriesAsync(Guid eventId, CancellationToken token)
    {
        var entries = await store.PageAsync<SurveyProgramEntry>(x => x.EventId == eventId, null, AssignmentLimit + 1, token);
        return entries.Count > AssignmentLimit ? null : entries;
    }

    private Task<Result<T>> WriteEventAsync<T>(
        Guid eventId, string capability, Func<EventScope, CancellationToken, Task<Result<T>>> command, CancellationToken cancellationToken) =>
        store.WriteAsync(async token =>
        {
            var scope = await RequireEventAsync<T>(eventId, capability, token);
            return scope.IsSuccess ? await command(scope.Value!, token) : Failure<T>(scope.ErrorDetail!);
        }, cancellationToken);

    private async Task<Result<EventScope>> RequireEventAsync<T>(Guid eventId, string capability, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
            return Failure<EventScope>(new("AuthenticationRequired", "Sign in to continue."));
        if (currentTenant is not { IsAvailable: true, IsPlatformScope: false })
            return NotFound<EventScope>();
        var activityEvent = await store.FindAsync<ActivityEvent>(x => x.Id == eventId && x.DeletedAt == null, cancellationToken);
        if (activityEvent is not { ProjectId: { } projectId, WorkspaceId: null, GroupId: null }
            || activityEvent.TenantId != currentTenant.TenantId || activityEvent.Status == EventStatus.Archived)
            return NotFound<EventScope>();
        var project = await store.FindAsync<Project>(x => x.Id == projectId && x.DeletedAt == null, cancellationToken);
        if (project is null || project.TenantId != activityEvent.TenantId
            || !await events.CanViewEvent(actor, activityEvent, cancellationToken)
            || !await events.CanAccessScope(actor, activityEvent, cancellationToken)
            || !await HasAsync(actor, project.WorkspaceId, capability, cancellationToken))
            return NotFound<EventScope>();

        return Success(new EventScope(actor, activityEvent, project));
    }

    private async Task<Result<(EventScope Event, Presentation Presentation)>> RequirePresentationAsync(
        Guid presentationId, string capability, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Failure<(EventScope, Presentation)>(new("AuthenticationRequired", "Sign in to continue."));
        var presentation = await store.FindAsync<Presentation>(x => x.Id == presentationId, cancellationToken);
        if (presentation is null)
            return NotFound<(EventScope, Presentation)>();
        var scope = await RequireEventAsync<Presentation>(presentation.EventId, capability, cancellationToken);
        return scope.IsSuccess
            ? Success((scope.Value!, presentation))
            : Failure<(EventScope, Presentation)>(scope.ErrorDetail!);
    }

    private Task<bool> HasAsync(Guid actor, Guid workspaceId, string capability, CancellationToken token) =>
        capabilities.HasActiveGrantAsync(actor, currentTenant.TenantId, capability, CapabilityScopeType.Workspace, workspaceId, token);

    private Task AuditAsync(Guid actor, string action, Guid id, CancellationToken token) =>
        audit.LogUserActionAsync(actor, action, "AqSurveyManagement", id, cancellationToken: token);

    private async Task<Result<AqManagementPage<TResponse>>> PageAsync<T, TResponse>(
        System.Linq.Expressions.Expression<Func<T, bool>> filter, Guid scope, string? cursor, int pageSize,
        Func<T, TResponse> map, CancellationToken token)
        where T : Entity, ITenantEntity
    {
        if (pageSize is < 1 or > 100 || !TryReadCursor<T>(cursor, scope, out var after))
            return Invalid<AqManagementPage<TResponse>>("The cursor or page size is invalid.");

        var entities = await store.PageAsync(filter, after, pageSize + 1, token);
        var items = entities.Take(pageSize).Select(map).ToArray();
        var next = entities.Count > pageSize ? MakeCursor<T>(entities[pageSize - 1].Id, scope) : null;
        return Success(new AqManagementPage<TResponse>(items, next));
    }

    private string MakeCursor<T>(Guid position, Guid scope)
    {
        var context = CursorContext<T>(scope);
        return Convert.ToBase64String(position.ToByteArray().Concat(context).ToArray());
    }

    private bool TryReadCursor<T>(string? cursor, Guid scope, out Guid? position)
    {
        position = null;
        if (cursor is null)
            return true;
        Span<byte> bytes = stackalloc byte[48];
        if (!Convert.TryFromBase64String(cursor, bytes, out var written) || written != bytes.Length
            || !bytes[16..].SequenceEqual(CursorContext<T>(scope)))
            return false;
        position = new Guid(bytes[..16]);
        return true;
    }

    private byte[] CursorContext<T>(Guid scope) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{currentTenant.TenantId:D}:{currentUser.UserId:D}:{typeof(T).Name}:{scope:D}"));

    private static Result<T>? CheckVersion<T>(long actual, long expected) =>
        expected <= 0 ? Invalid<T>("A positive expectedVersionNo is required.")
        : expected != actual ? Failure<T>(new("RowVersionConflict", "Reload the current record before updating it."))
        : null;

    private static bool TryWireEnum<T>(string? value, out T parsed) where T : struct, Enum
    {
        foreach (var item in Enum.GetValues<T>())
        {
            if (Wire(item) == value)
            {
                parsed = item;
                return true;
            }
        }

        parsed = default;
        return false;
    }

    private static string Wire<T>(T value) where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static bool TryProgramFields(IReadOnlyList<string> input, out SurveyProgramFields fields)
    {
        fields = SurveyProgramFields.None;
        foreach (var value in input)
        {
            var match = Array.Find(ProgramFieldNames, field => field.Name == value);
            if (match.Flag == SurveyProgramFields.None || fields.HasFlag(match.Flag))
                return false;
            fields |= match.Flag;
        }

        return true;
    }

    private static bool ValidSchedule(AqPresentationSchedule? schedule) =>
        schedule is not null && !string.IsNullOrWhiteSpace(schedule.Title) && schedule.Title.Length <= 200
        && (schedule.Venue?.Length ?? 0) <= 300 && schedule.StartAt < schedule.EndAt;

    private static AqManagedPresentation ToPresentation(Presentation item) => new(
        new AqPresentationSummary(item.Id, item.EventId, item.Title, item.StartsAtUtc, item.EndsAtUtc, item.Venue), item.VersionNo);

    private static AqParticipantSettings ToParticipants(Presentation item, IEnumerable<PresentationParticipant> assignments) =>
        new(item.VersionNo,
            assignments.Where(x => x.UserId.HasValue).Select(x => new AqParticipantAssignment(Wire(x.Role), x.UserId!.Value)).ToArray(),
            assignments.Where(x => x.PendingRosterRef is not null).Select(x => new AqPendingPresenter(x.PendingRosterRef!)).ToArray());

    private static AqManagedEventSettings ToEventSettings(EventFeedbackSettings settings, IEnumerable<SurveyProgramEntry> entries) =>
        new(new AqEventSettings(settings.StartsAtUtc, settings.EndsAtUtc, settings.TimeZoneId,
            settings.ReceptionStopped, settings.ResultsStopped, Wire(settings.NameMode),
            settings.AccountBinding == SurveyAccountBinding.ExplicitOptIn, settings.IsProgramPublished,
            ProgramFieldNames.Where(x => settings.ProgramFields.HasFlag(x.Flag)).Select(x => x.Name).ToArray(),
            entries.Select(x => new AqProgramTargetSelection(x.PresentationId, x.SurveyTargetId)).ToArray()), settings.VersionNo);

    private static AqPresentationResultsSettings ToResults(PresentationFeedbackSettings? settings, EventFeedbackSettings? parent) =>
        new(settings?.ResultsStopped ?? false, settings?.ResultsStopped == true || parent?.ResultsStopped == true, settings?.VersionNo ?? 1);

    private static Result<T> Success<T>(T result) => Result<T>.Success(result);
    private static Result<T> Failure<T>(ApplicationErrorDetail detail) => Result<T>.Failure(detail);
    private static Result<T> Invalid<T>(string message) => Failure<T>(new("ValidationFailed", message));
    private static Result<T> Conflict<T>(string message) => Failure<T>(new("InvalidStateTransition", message));
    private static Result<T> NotFound<T>() => Failure<T>(new("NotFound", "The survey management resource was not found."));
}

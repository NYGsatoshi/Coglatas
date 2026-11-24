using Coglatas.Domain.Common;
using Coglatas.Domain.Enums;

namespace Coglatas.Domain.Entities;

public sealed class Presentation : AuditableEntity, ITenantEntity
{
    private Presentation()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid EventId { get; private init; }
    public Guid ProjectId { get; private init; }
    public string Title { get; private set; } = string.Empty;
    public string? Venue { get; private set; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public long VersionNo { get; private set; } = 1;

    public static Presentation Create(
        ActivityEvent activityEvent, string title, string? venue, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        if (activityEvent.IsDeleted || activityEvent.TenantId == Guid.Empty
            || activityEvent.ProjectId is not { } projectId || projectId == Guid.Empty
            || activityEvent.WorkspaceId is not null || activityEvent.GroupId is not null)
        {
            throw new ArgumentException("A presentation requires a current Project Event.", nameof(activityEvent));
        }

        var presentation = new Presentation
        {
            TenantId = activityEvent.TenantId,
            EventId = activityEvent.Id,
            ProjectId = projectId
        };
        presentation.SetSchedule(title, venue, startsAt, endsAt);
        return presentation;
    }

    public void UpdateSchedule(
        long expectedVersionNo, string title, string? venue, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        SetSchedule(title, venue, startsAt, endsAt);
        VersionNo = nextVersion;
    }

    public void AdvanceParticipantsVersion(long expectedVersionNo) =>
        VersionNo = SurveyVersion.Next(VersionNo, expectedVersionNo);

    private void SetSchedule(string title, string? venue, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (startsAt >= endsAt)
            throw new ArgumentException("A presentation must start before it ends.");

        Title = title;
        Venue = venue;
        StartsAtUtc = startsAt.ToUniversalTime();
        EndsAtUtc = endsAt.ToUniversalTime();
    }
}

public sealed class PresentationParticipant : AuditableEntity, ITenantEntity
{
    private PresentationParticipant()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid PresentationId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? PendingRosterRef { get; private set; }
    public PresentationParticipantRole Role { get; private set; }

    public static PresentationParticipant Assign(
        Presentation presentation, Project owningProject, WorkspaceMember member, PresentationParticipantRole role)
    {
        if (presentation.TenantId != owningProject.TenantId || presentation.ProjectId != owningProject.Id
            || member.TenantId != presentation.TenantId || member.WorkspaceId != owningProject.WorkspaceId
            || member.Status != MembershipStatus.Active || member.UserId == Guid.Empty || !Enum.IsDefined(role))
        {
            throw new ArgumentException("Only a verified active member of the owning workspace can be assigned.");
        }

        return new PresentationParticipant
        {
            TenantId = presentation.TenantId,
            PresentationId = presentation.Id,
            UserId = member.UserId,
            Role = role
        };
    }

    public static PresentationParticipant PendingPresenter(Presentation presentation, string pendingRosterRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pendingRosterRef);
        return new PresentationParticipant
        {
            TenantId = presentation.TenantId,
            PresentationId = presentation.Id,
            PendingRosterRef = pendingRosterRef,
            Role = PresentationParticipantRole.Presenter
        };
    }
}

public sealed class EventFeedbackSettings : AuditableEntity, ITenantEntity
{
    private EventFeedbackSettings()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid EventId { get; private init; }
    public DateTimeOffset StartsAtUtc { get; private set; }
    public DateTimeOffset EndsAtUtc { get; private set; }
    public string TimeZoneId { get; private set; } = string.Empty;
    public bool ReceptionStopped { get; private set; }
    public bool ResultsStopped { get; private set; }
    public bool IsProgramPublished { get; private set; }
    public SurveyNameMode NameMode { get; private set; }
    public SurveyAccountBinding AccountBinding { get; private set; }
    public SurveyProgramFields ProgramFields { get; private set; }
    public long VersionNo { get; private set; } = 1;

    public void AdvancePublicEntryVersion(long expectedVersionNo) =>
        VersionNo = SurveyVersion.Next(VersionNo, expectedVersionNo);

    public static EventFeedbackSettings Create(
        ActivityEvent activityEvent, DateTimeOffset startsAt, DateTimeOffset endsAt, string timeZoneId)
    {
        if (activityEvent.IsDeleted || activityEvent.TenantId == Guid.Empty || activityEvent.ProjectId is null
            || activityEvent.WorkspaceId is not null || activityEvent.GroupId is not null)
        {
            throw new ArgumentException("Survey feedback settings require a current Project Event.", nameof(activityEvent));
        }

        var settings = new EventFeedbackSettings { TenantId = activityEvent.TenantId, EventId = activityEvent.Id };
        settings.SetWindow(startsAt, endsAt, timeZoneId);
        return settings;
    }

    public void SetReception(
        long expectedVersionNo, DateTimeOffset startsAt, DateTimeOffset endsAt, string timeZoneId, bool stopped)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        SetWindow(startsAt, endsAt, timeZoneId);
        ReceptionStopped = stopped;
        VersionNo = nextVersion;
    }

    public void SetResultsStop(long expectedVersionNo, bool stopped)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        ResultsStopped = stopped;
        VersionNo = nextVersion;
    }

    public void SetParticipationPolicy(
        long expectedVersionNo, SurveyNameMode nameMode, SurveyAccountBinding accountBinding)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        ValidateParticipationPolicy(nameMode, accountBinding);

        NameMode = nameMode;
        AccountBinding = accountBinding;
        VersionNo = nextVersion;
    }

    public void SetProgram(
        long expectedVersionNo, bool published, SurveyProgramFields programFields, bool presenterNamesApproved)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        ValidateProgram(published, programFields, presenterNamesApproved);

        IsProgramPublished = published;
        ProgramFields = programFields;
        VersionNo = nextVersion;
    }

    public void Configure(long expectedVersionNo, DateTimeOffset startsAt, DateTimeOffset endsAt, string timeZoneId,
        bool receptionStopped, bool resultsStopped, SurveyNameMode nameMode, SurveyAccountBinding accountBinding,
        bool programPublished, SurveyProgramFields programFields, bool presenterNamesApproved)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        ValidateParticipationPolicy(nameMode, accountBinding);
        ValidateProgram(programPublished, programFields, presenterNamesApproved);
        SetWindow(startsAt, endsAt, timeZoneId);
        ReceptionStopped = receptionStopped;
        ResultsStopped = resultsStopped;
        NameMode = nameMode;
        AccountBinding = accountBinding;
        IsProgramPublished = programPublished;
        ProgramFields = programFields;
        VersionNo = nextVersion;
    }

    private static void ValidateParticipationPolicy(SurveyNameMode nameMode, SurveyAccountBinding accountBinding)
    {
        if (!Enum.IsDefined(nameMode) || !Enum.IsDefined(accountBinding))
            throw new ArgumentException("Unknown participation policy.");
    }

    private static void ValidateProgram(bool published, SurveyProgramFields programFields, bool presenterNamesApproved)
    {
        const SurveyProgramFields allowedFields = SurveyProgramFields.Title | SurveyProgramFields.Time
            | SurveyProgramFields.Venue | SurveyProgramFields.PresenterNames | SurveyProgramFields.TimeZone;
        if ((programFields & ~allowedFields) != 0
            || published && programFields.HasFlag(SurveyProgramFields.PresenterNames) && !presenterNamesApproved)
        {
            throw new ArgumentException("The public field selection lacks the applicable publishing approval.");
        }
    }

    private void SetWindow(DateTimeOffset startsAt, DateTimeOffset endsAt, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        if (startsAt >= endsAt
            || (startsAt.Offset != TimeSpan.Zero && zone.GetUtcOffset(startsAt) != startsAt.Offset)
            || (endsAt.Offset != TimeSpan.Zero && zone.GetUtcOffset(endsAt) != endsAt.Offset))
        {
            throw new ArgumentException("Reception needs an ordered period with valid offsets in its display time zone.");
        }

        StartsAtUtc = startsAt.ToUniversalTime();
        EndsAtUtc = endsAt.ToUniversalTime();
        TimeZoneId = timeZoneId;
    }

    public static DateTimeOffset ResolveLocalTime(DateTime localTime, TimeZoneInfo zone, TimeSpan? offset = null)
    {
        if (localTime.Kind != DateTimeKind.Unspecified || zone.IsInvalidTime(localTime))
            throw new ArgumentException("A local reception time must exist in the event time zone.", nameof(localTime));

        if (zone.IsAmbiguousTime(localTime))
        {
            if (offset is not { } explicitOffset || !zone.GetAmbiguousTimeOffsets(localTime).Contains(explicitOffset))
                throw new ArgumentException("An ambiguous reception time requires a valid explicit offset.", nameof(offset));

            return new DateTimeOffset(localTime, explicitOffset).ToUniversalTime();
        }

        var zoneOffset = zone.GetUtcOffset(localTime);
        if (offset is { } provided && provided != zoneOffset)
            throw new ArgumentException("The provided offset does not match the event time zone.", nameof(offset));

        return new DateTimeOffset(localTime, zoneOffset).ToUniversalTime();
    }
}

public sealed class PresentationFeedbackSettings : AuditableEntity, ITenantEntity
{
    private PresentationFeedbackSettings()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid PresentationId { get; private set; }
    public bool ResultsStopped { get; private set; }
    public long VersionNo { get; private set; } = 1;

    public static PresentationFeedbackSettings Create(Presentation presentation) =>
        new() { TenantId = presentation.TenantId, PresentationId = presentation.Id };

    public void SetResultsStop(long expectedVersionNo, bool stopped)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        ResultsStopped = stopped;
        VersionNo = nextVersion;
    }
}

public sealed class SurveyCampaign : AuditableEntity, ITenantEntity
{
    private SurveyCampaign()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid EventId { get; private init; }
    public Guid FormDefinitionId { get; private init; }
    public bool IsEnabled { get; private set; }
    public bool IsPublished { get; private set; }
    public Guid? RetentionPolicyId { get; private set; }
    public long VersionNo { get; private set; } = 1;

    public static SurveyCampaign Create(
        ActivityEvent activityEvent, Project owningProject, FormDefinition definition, Guid? retentionPolicyId = null)
    {
        if (activityEvent.IsDeleted || owningProject.IsDeleted || definition.IsDeleted
            || activityEvent.TenantId != owningProject.TenantId || activityEvent.TenantId != definition.TenantId
            || activityEvent.ProjectId != owningProject.Id || activityEvent.WorkspaceId is not null
            || activityEvent.GroupId is not null || definition.Purpose != FormPurpose.Survey
            || (definition.ProjectId != owningProject.Id && definition.WorkspaceId != owningProject.WorkspaceId
                && (owningProject.GroupId is null || definition.GroupId != owningProject.GroupId))
            || retentionPolicyId == Guid.Empty)
        {
            throw new ArgumentException("A campaign needs an explicitly registered Survey form in its Project Event scope.");
        }

        return new SurveyCampaign
        {
            TenantId = owningProject.TenantId,
            EventId = activityEvent.Id,
            FormDefinitionId = definition.Id,
            RetentionPolicyId = retentionPolicyId
        };
    }

    public void SetPublication(long expectedVersionNo, bool enabled, bool published)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        IsEnabled = enabled;
        IsPublished = published;
        VersionNo = nextVersion;
    }

    public void Configure(long expectedVersionNo, bool enabled, bool published, Guid retentionPolicyId)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        if (retentionPolicyId == Guid.Empty)
            throw new ArgumentException("A retention policy reference is required.", nameof(retentionPolicyId));

        IsEnabled = enabled;
        IsPublished = published;
        RetentionPolicyId = retentionPolicyId;
        VersionNo = nextVersion;
    }

    public void SetRetentionPolicy(long expectedVersionNo, Guid retentionPolicyId)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        if (retentionPolicyId == Guid.Empty)
            throw new ArgumentException("A retention policy reference is required.", nameof(retentionPolicyId));

        RetentionPolicyId = retentionPolicyId;
        VersionNo = nextVersion;
    }
}

public sealed class SurveyTarget : AuditableEntity, ITenantEntity
{
    private SurveyTarget()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid EventId { get; private init; }
    public Guid CampaignId { get; private set; }
    public Guid PresentationId { get; private init; }
    public Guid FormDefinitionId { get; private init; }
    public Guid? PublishedSchemaId { get; private set; }
    public DateTimeOffset? StartsAtUtc { get; private set; }
    public DateTimeOffset? EndsAtUtc { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool ResultsStopped { get; private set; }
    public string LocalMarkerKey { get; private set; } = string.Empty;
    public long VersionNo { get; private set; } = 1;

    public void AdvancePublicEntryVersion(long expectedVersionNo) =>
        VersionNo = SurveyVersion.Next(VersionNo, expectedVersionNo);

    public static SurveyTarget Create(SurveyCampaign campaign, Presentation presentation, string localMarkerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localMarkerKey);
        if (campaign.TenantId != presentation.TenantId || campaign.EventId != presentation.EventId)
            throw new ArgumentException("The campaign and presentation must belong to the same tenant and Event.");

        return new SurveyTarget
        {
            TenantId = campaign.TenantId,
            EventId = campaign.EventId,
            CampaignId = campaign.Id,
            PresentationId = presentation.Id,
            FormDefinitionId = campaign.FormDefinitionId,
            LocalMarkerKey = localMarkerKey
        };
    }

    public void Configure(long expectedVersionNo, EventFeedbackSettings settings,
        DateTimeOffset? startsAt, DateTimeOffset? endsAt, bool enabled, bool resultsStopped)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        if (settings.TenantId != TenantId || settings.EventId != EventId)
            throw new ArgumentException("The reception window must come from the owning Event.", nameof(settings));

        _ = SurveyReceptionWindow.Intersect(settings.StartsAtUtc, settings.EndsAtUtc, startsAt, endsAt);
        StartsAtUtc = startsAt?.ToUniversalTime();
        EndsAtUtc = endsAt?.ToUniversalTime();
        IsEnabled = enabled;
        ResultsStopped = resultsStopped;
        VersionNo = nextVersion;
    }

    public void Publish(long expectedVersionNo, PublishedSurveySchema schema)
    {
        var nextVersion = SurveyVersion.Next(VersionNo, expectedVersionNo);
        if (schema.TenantId != TenantId || schema.SurveyTargetId != Id || schema.FormDefinitionId != FormDefinitionId)
            throw new ArgumentException("Only a schema published for this target can become current.", nameof(schema));

        PublishedSchemaId = schema.Id;
        VersionNo = nextVersion;
    }
}

public sealed class SurveyProgramEntry : AuditableEntity, ITenantEntity
{
    private SurveyProgramEntry()
    {
    }

    public Guid TenantId { get; private init; }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set
        {
            if (value != TenantId)
                throw new InvalidOperationException("Tenant identity is immutable.");
        }
    }
    public Guid EventId { get; private set; }
    public Guid PresentationId { get; private set; }
    public Guid SurveyTargetId { get; private set; }

    public static SurveyProgramEntry Select(Presentation presentation, SurveyTarget target)
    {
        if (target.TenantId != presentation.TenantId || target.EventId != presentation.EventId
            || target.PresentationId != presentation.Id || target.PublishedSchemaId is null)
        {
            throw new ArgumentException("A program entry requires a published survey target for the exact presentation.");
        }

        return new SurveyProgramEntry
        {
            TenantId = presentation.TenantId,
            EventId = presentation.EventId,
            PresentationId = presentation.Id,
            SurveyTargetId = target.Id
        };
    }
}

public sealed class SurveyReceptionWindow
{
    private SurveyReceptionWindow(DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc)
    {
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }

    public DateTimeOffset StartsAtUtc { get; }
    public DateTimeOffset EndsAtUtc { get; }

    public bool Contains(DateTimeOffset now) => StartsAtUtc <= now && now < EndsAtUtc;

    public static SurveyReceptionWindow Intersect(EventFeedbackSettings settings, SurveyTarget target)
    {
        if (settings.TenantId != target.TenantId || settings.EventId != target.EventId)
            throw new ArgumentException("The target must belong to the reception settings' Event.");

        return Intersect(settings.StartsAtUtc, settings.EndsAtUtc, target.StartsAtUtc, target.EndsAtUtc);
    }

    public static SurveyReceptionWindow Intersect(
        DateTimeOffset eventStartsAt, DateTimeOffset eventEndsAt, DateTimeOffset? targetStartsAt, DateTimeOffset? targetEndsAt)
    {
        var startsAt = targetStartsAt is { } start && start > eventStartsAt ? start : eventStartsAt;
        var endsAt = targetEndsAt is { } end && end < eventEndsAt ? end : eventEndsAt;
        if (startsAt >= endsAt)
            throw new ArgumentException("The target and Event must have a nonempty reception intersection.");

        return new SurveyReceptionWindow(startsAt.ToUniversalTime(), endsAt.ToUniversalTime());
    }
}

internal static class SurveyVersion
{
    internal static long Next(long versionNo, long expectedVersionNo)
    {
        if (versionNo != expectedVersionNo)
            throw new InvalidOperationException("The supplied version does not match the current record.");

        return checked(expectedVersionNo + 1);
    }
}

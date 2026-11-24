using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Coglatas.Application.Common;
using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Common;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.Forms.SurveyManagement;

public interface IAqSurveyManagementStore
{
    Task<T?> FindAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
        where T : Entity, ITenantEntity;

    Task<IReadOnlyList<T>> PageAsync<T>(
        Expression<Func<T, bool>> predicate, Guid? after, int take, CancellationToken cancellationToken)
        where T : Entity, ITenantEntity;

    Task<bool> AnyAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken)
        where T : Entity, ITenantEntity;

    Task<FormVersion?> FindCommonVersionAsync(Guid formId, Guid versionId, CancellationToken cancellationToken);
    Task<TargetQuestionSetVersion?> FindAdditionalVersionAsync(Guid targetId, Guid versionId, CancellationToken cancellationToken);
    void Add<T>(T entity) where T : Entity, ITenantEntity;
    void Remove<T>(T entity) where T : Entity, ITenantEntity;
    Task FlushAsync(CancellationToken cancellationToken);
    Task<Result<T>> WriteAsync<T>(Func<CancellationToken, Task<Result<T>>> command, CancellationToken cancellationToken);
}

public sealed record AqManagementPage<T>(IReadOnlyList<T> Items, string? NextCursor);
public sealed record AqPresentationSchedule(string Title, DateTimeOffset StartAt, DateTimeOffset EndAt, string? Venue);
public sealed record AqPresentationCreateRequest(AqPresentationSchedule? Schedule);
public sealed record AqPresentationUpdateRequest(long ExpectedVersionNo, AqPresentationSchedule? Schedule);
public sealed record AqPresentationSummary(Guid Id, Guid EventId, string Title, DateTimeOffset StartAt, DateTimeOffset EndAt, string? Venue);
public sealed record AqManagedPresentation(AqPresentationSummary Presentation, long VersionNo);
public sealed record AqParticipantAssignment(string Role, Guid PersonRef);
public sealed record AqPendingPresenter(string PendingRosterRef);
public sealed record AqParticipantUpdateRequest(
    long ExpectedVersionNo, IReadOnlyList<AqParticipantAssignment>? Assignments, IReadOnlyList<AqPendingPresenter>? PendingPresenters);
public sealed record AqParticipantSettings(
    long VersionNo, IReadOnlyList<AqParticipantAssignment> Assignments, IReadOnlyList<AqPendingPresenter> PendingPresenters);

public sealed record AqSettingsUpdateRequest(
    long ExpectedVersionNo, DateTimeOffset? StartAt, DateTimeOffset? EndAt, string? TimeZone,
    bool? ReceptionStopped, bool? ResultsStopped, string? NameMode, bool? AccountBindingAllowed,
    bool? ProgramPublished, IReadOnlyList<string>? PublicFields, IReadOnlyList<AqProgramTargetSelection>? ProgramTargets,
    ManualFeedbackInvitationPolicy? InvitationPolicy = null);
public sealed record AqProgramTargetSelection(Guid PresentationId, Guid TargetId);
public sealed record AqEventSettings(
    DateTimeOffset StartAt, DateTimeOffset EndAt, string TimeZone, bool ReceptionStopped,
    bool ResultsStopped, string NameMode, bool AccountBindingAllowed, bool ProgramPublished,
    IReadOnlyList<string> PublicFields, IReadOnlyList<AqProgramTargetSelection> ProgramTargets,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FeedbackInvitationPolicySettings? InvitationPolicy);
public sealed record AqManagedEventSettings(AqEventSettings Settings, long VersionNo);
public sealed record AqPresentationResultsUpdateRequest(long ExpectedVersionNo, bool? ResultsStopped);
public sealed record AqPresentationResultsSettings(bool ResultsStopped, bool EffectiveResultsStopped, long VersionNo);

public sealed record AqFormOwnerRequest(string ScopeType, Guid ScopeId);
public sealed record AqCreateFormRequest(AqFormOwnerRequest? Owner, string Purpose, string Title, string? Description);
public sealed record AqFormCreated(Guid Id, string State, long VersionNo);
public sealed record AqFormSummary(Guid Id, string Title, string Purpose, string State, long VersionNo);
public sealed record AqFormField(
    string FieldKey, string Label, string Type, bool Required,
    IReadOnlyList<SurveyOption>? Options, SurveyFieldConstraints? Constraints);
public sealed record AqFieldCompatibility(string PreviousFieldKey, string FieldKey);
public sealed record AqPublishQuestionVersionRequest(
    long ExpectedVersionNo, string Action, IReadOnlyList<AqFormField>? Fields,
    string? ChangeReason, IReadOnlyList<AqFieldCompatibility>? Compatibility);
public sealed record AqVersionSummary(Guid Id, string State, long VersionNo);
public sealed record AqVersionDetail(Guid Id, string State, long VersionNo, IReadOnlyList<AqFormField> Fields);

public sealed record AqCreateCampaignRequest(Guid FormDefinitionId, Guid? PolicyRef);
public sealed record AqUpdateCampaignRequest(long ExpectedVersionNo, bool? Enabled, bool? Published, AqRetentionPolicyRequest? Retention);
public sealed record AqRetentionPolicyRequest(Guid PolicyRef, string Reason);
public sealed record AqCampaignSummary(
    Guid Id, Guid EventId, Guid FormDefinitionId, long VersionNo, bool Enabled, bool Published, Guid? RetentionPolicyRef);
public sealed record AqTargetSettings(bool Enabled, bool ResultsStopped, DateTimeOffset? StartAt, DateTimeOffset? EndAt);
public sealed record AqCreateTargetRequest(Guid PresentationId, Guid CommonVersionId, AqTargetSettings? Settings);
public sealed record AqUpdateTargetRequest(
    long ExpectedVersionNo, bool? Enabled, bool? ResultsStopped,
    System.Text.Json.JsonElement StartAt, System.Text.Json.JsonElement EndAt);
public sealed record AqTargetSummary(
    Guid Id, Guid CampaignId, Guid PresentationId, Guid CommonVersionId, long VersionNo,
    AqTargetSettings Settings, DateTimeOffset? EffectiveStartsAt, DateTimeOffset? EffectiveEndsAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SurveyEntryRef = null);

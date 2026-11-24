using System.Text.Json;
using Coglatas.Application.Common;
using Coglatas.Domain.Entities;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Forms.SurveyManagement;

public sealed partial class AqSurveyManagementService
{
    public async Task<Result<AqManagementPage<AqFormSummary>>> ListFormsAsync(
        Guid eventId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var result = await RequireEventAsync<AqManagementPage<AqFormSummary>>(eventId, Manage, cancellationToken);
        if (!result.IsSuccess)
            return Failure<AqManagementPage<AqFormSummary>>(result.ErrorDetail!);
        var project = result.Value!.Project;
        return await PageAsync<FormDefinition, AqFormSummary>(
            form => form.Purpose == FormPurpose.Survey && form.DeletedAt == null &&
                (form.ProjectId == project.Id || form.WorkspaceId == project.WorkspaceId ||
                    project.GroupId != null && form.GroupId == project.GroupId),
            eventId, cursor, pageSize, ToFormSummary, cancellationToken);
    }

    public Task<Result<AqFormCreated>> CreateFormAsync(AqCreateFormRequest request, CancellationToken cancellationToken) =>
        store.WriteAsync(async token =>
        {
            if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
                return Failure<AqFormCreated>(new("AuthenticationRequired", "Sign in to continue."));
            if (currentTenant is not { IsAvailable: true, IsPlatformScope: false })
                return NotFound<AqFormCreated>();
            if (request.Owner is not { } owner || owner.ScopeId == Guid.Empty ||
                request.Purpose != "survey" || string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
                return Invalid<AqFormCreated>("Supply a Survey purpose, title, and valid owning scope.");
            var workspaceId = owner.ScopeType == "workspace" ? (Guid?)owner.ScopeId : null;
            var groupId = owner.ScopeType == "group" ? (Guid?)owner.ScopeId : null;
            var projectId = owner.ScopeType == "project" ? (Guid?)owner.ScopeId : null;
            var parent = await ResolveFormWorkspaceAsync(workspaceId, groupId, projectId, token);
            if (parent is null || !await forms.CanCreateForm(actor, workspaceId, groupId, projectId, token)
                || !await HasAsync(actor, parent.Value, EditQuestions, token))
                return NotFound<AqFormCreated>();
            var form = FormDefinition.Create(currentTenant.TenantId, actor, workspaceId, groupId, projectId,
                FormPurpose.Survey, request.Title, request.Description);
            store.Add(form);
            await AuditAsync(actor, "aq.form.create", form.Id, token);
            return Success(new AqFormCreated(form.Id, "draft", form.VersionNo));
        }, cancellationToken);

    public async Task<Result<AqManagementPage<AqVersionSummary>>> ListCommonVersionsAsync(
        Guid formId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var form = await RequireFormAsync(formId, cancellationToken);
        return form.IsSuccess
            ? await PageAsync<FormVersion, AqVersionSummary>(x => x.FormDefinitionId == formId, formId, cursor, pageSize,
                item => new(item.Id, "published", item.Number), cancellationToken)
            : Failure<AqManagementPage<AqVersionSummary>>(form.ErrorDetail!);
    }

    public async Task<Result<AqVersionDetail>> GetCommonVersionAsync(Guid formId, Guid versionId, CancellationToken cancellationToken)
    {
        var form = await RequireFormAsync(formId, cancellationToken);
        if (!form.IsSuccess)
            return Failure<AqVersionDetail>(form.ErrorDetail!);
        var item = await store.FindCommonVersionAsync(formId, versionId, cancellationToken);
        return item is null ? NotFound<AqVersionDetail>() : Success(ToVersionDetail(item.Id, item.Number, item.Fields));
    }

    public Task<Result<AqVersionSummary>> PublishCommonVersionAsync(
        Guid formId, AqPublishQuestionVersionRequest request, CancellationToken cancellationToken) =>
        store.WriteAsync(async token =>
        {
            var result = await RequireFormAsync(formId, token);
            if (!result.IsSuccess)
                return Failure<AqVersionSummary>(result.ErrorDetail!);
            var form = result.Value!;
            var versionError = CheckVersion<AqVersionSummary>(form.VersionNo, request.ExpectedVersionNo);
            if (versionError is not null)
                return versionError;
            if (!TryFields(request, out var fields) || fields.Count == 0)
                return Invalid<AqVersionSummary>("Supply at least one common field and a publish action.");

            if (await store.AnyAsync<FormSubmission>(
                x => x.FormDefinitionId == formId && x.SurveyTargetId != null, token))
            {
                var previousId = await store.FindAsync<FormVersion>(
                    x => x.FormDefinitionId == formId && x.Number == form.VersionNo - 1, token);
                var previous = previousId is null ? null : await store.FindCommonVersionAsync(formId, previousId.Id, token);
                if (!CompatibleChange(previous?.Fields, fields, request))
                    return Conflict<AqVersionSummary>("After submissions, only explicit compatible field corrections may publish.");
            }

            var version = FormVersion.Publish(form, checked((int)form.VersionNo), fields, clock.UtcNow);
            form.AdvancePublicationVersion(request.ExpectedVersionNo);
            store.Add(version);
            await AuditAsync(currentUser.UserId!.Value, "aq.form.version.publish", version.Id, token);
            return Success(new AqVersionSummary(version.Id, "published", version.Number));
        }, cancellationToken);

    public async Task<Result<AqManagementPage<AqCampaignSummary>>> ListCampaignsAsync(
        Guid eventId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var scope = await RequireEventAsync<AqManagementPage<AqCampaignSummary>>(eventId, Manage, cancellationToken);
        return scope.IsSuccess
            ? await PageAsync<SurveyCampaign, AqCampaignSummary>(x => x.EventId == eventId,
                eventId, cursor, pageSize, ToCampaign, cancellationToken)
            : Failure<AqManagementPage<AqCampaignSummary>>(scope.ErrorDetail!);
    }

    public async Task<Result<AqCampaignSummary>> GetCampaignAsync(Guid campaignId, CancellationToken cancellationToken)
    {
        var scoped = await RequireCampaignAsync(campaignId, Manage, cancellationToken);
        return scoped.IsSuccess
            ? Success(ToCampaign(scoped.Value!.Campaign))
            : Failure<AqCampaignSummary>(scoped.ErrorDetail!);
    }

    public Task<Result<AqCampaignSummary>> CreateCampaignAsync(
        Guid eventId, AqCreateCampaignRequest request, CancellationToken cancellationToken) =>
        WriteEventAsync(eventId, Manage, async (scope, token) =>
        {
            var settings = await store.FindAsync<EventFeedbackSettings>(x => x.EventId == eventId, token);
            if (settings is null || request.PolicyRef is { } policyRef && policyRef != eventId)
                return Invalid<AqCampaignSummary>("Configure the selected Event survey policy first.");
            var definition = await store.FindAsync<FormDefinition>(x => x.Id == request.FormDefinitionId
                && x.Purpose == FormPurpose.Survey && x.DeletedAt == null &&
                (x.ProjectId == scope.Project.Id || x.WorkspaceId == scope.Project.WorkspaceId ||
                    scope.Project.GroupId != null && x.GroupId == scope.Project.GroupId), token);
            if (definition is null)
                return NotFound<AqCampaignSummary>();
            var item = SurveyCampaign.Create(scope.Event, scope.Project, definition);
            store.Add(item);
            await AuditAsync(scope.Actor, "aq.campaign.create", item.Id, token);
            return Success(ToCampaign(item));
        }, cancellationToken);

    public Task<Result<AqCampaignSummary>> UpdateCampaignAsync(
        Guid campaignId, AqUpdateCampaignRequest request, CancellationToken cancellationToken) =>
        store.WriteAsync(async token =>
        {
            var scoped = await RequireCampaignAsync(campaignId, Manage, token);
            if (!scoped.IsSuccess)
                return Failure<AqCampaignSummary>(scoped.ErrorDetail!);
            var (scope, item) = scoped.Value!;
            var versionError = CheckVersion<AqCampaignSummary>(item.VersionNo, request.ExpectedVersionNo);
            if (versionError is not null)
                return versionError;
            if (request.Retention is not null)
            {
                if (!await HasAsync(scope.Actor, scope.Project.WorkspaceId, ManageRetention, token))
                    return Failure<AqCampaignSummary>(new("CapabilityDenied", "Retention policy management is separately authorized."));
                return Conflict<AqCampaignSummary>("No approved retention policy is available in this scope.");
            }
            if (request.Enabled is null && request.Published is null)
                return Invalid<AqCampaignSummary>("Provide a campaign setting to update.");
            item.SetPublication(request.ExpectedVersionNo, request.Enabled ?? item.IsEnabled, request.Published ?? item.IsPublished);
            await AuditAsync(scope.Actor, "aq.campaign.settings.update", item.Id, token);
            return Success(ToCampaign(item));
        }, cancellationToken);

    public async Task<Result<AqManagementPage<AqTargetSummary>>> ListTargetsAsync(
        Guid campaignId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var scoped = await RequireCampaignAsync(campaignId, Manage, cancellationToken);
        if (!scoped.IsSuccess)
            return Failure<AqManagementPage<AqTargetSummary>>(scoped.ErrorDetail!);
        if (pageSize is < 1 or > 100 || !TryReadCursor<SurveyTarget>(cursor, campaignId, out var after))
            return Invalid<AqManagementPage<AqTargetSummary>>("The cursor or page size is invalid.");

        var settings = await store.FindAsync<EventFeedbackSettings>(x => x.EventId == scoped.Value!.Campaign.EventId, cancellationToken);
        var rows = await store.PageAsync<SurveyTarget>(x => x.CampaignId == campaignId, after, pageSize + 1, cancellationToken);
        var publishedIds = rows.Take(pageSize).Select(x => x.PublishedSchemaId).OfType<Guid>().ToArray();
        var schemas = await store.PageAsync<PublishedSurveySchema>(x => publishedIds.Contains(x.Id), null, pageSize, cancellationToken);
        var bySchemaId = schemas.ToDictionary(x => x.Id);
        var items = rows.Take(pageSize).Select(item => ToTarget(item,
            item.PublishedSchemaId is { } id ? bySchemaId.GetValueOrDefault(id) : null, settings)).ToArray();
        return Success(new AqManagementPage<AqTargetSummary>(items,
            rows.Count > pageSize ? MakeCursor<SurveyTarget>(rows[pageSize - 1].Id, campaignId) : null));
    }

    public async Task<Result<AqTargetSummary>> GetTargetAsync(Guid targetId, CancellationToken cancellationToken)
    {
        var scoped = await RequireTargetAsync(targetId, Manage, cancellationToken);
        if (!scoped.IsSuccess)
            return Failure<AqTargetSummary>(scoped.ErrorDetail!);
        var item = scoped.Value!.Target;
        var settings = await store.FindAsync<EventFeedbackSettings>(x => x.EventId == item.EventId, cancellationToken);
        var publication = await CurrentPublicationAsync(item, cancellationToken);
        return Success(ToTarget(item, publication, settings));
    }

    public Task<Result<AqTargetSummary>> CreateTargetAsync(
        Guid campaignId, AqCreateTargetRequest request, CancellationToken cancellationToken) =>
        store.WriteAsync(async token =>
        {
            var scoped = await RequireCampaignAsync(campaignId, Manage, token);
            if (!scoped.IsSuccess)
                return Failure<AqTargetSummary>(scoped.ErrorDetail!);
            var (scope, campaign) = scoped.Value!;
            var presentation = await store.FindAsync<Presentation>(
                x => x.Id == request.PresentationId && x.EventId == campaign.EventId, token);
            var settings = await store.FindAsync<EventFeedbackSettings>(x => x.EventId == campaign.EventId, token);
            var common = await store.FindCommonVersionAsync(campaign.FormDefinitionId, request.CommonVersionId, token);
            if (presentation is null || common is null)
                return NotFound<AqTargetSummary>();
            if (settings is null || request.Settings is not { } boundaries)
                return Invalid<AqTargetSummary>("Configure the Event reception and supply target settings.");
            var duplicate = await store.FindAsync<SurveyTarget>(
                x => x.CampaignId == campaignId && x.PresentationId == presentation.Id, token);
            if (duplicate is not null)
                return Conflict<AqTargetSummary>("The campaign already has a target for this presentation.");

            var target = SurveyTarget.Create(campaign, presentation, NewOpaqueToken());
            target.Configure(target.VersionNo, settings, boundaries.StartAt, boundaries.EndAt, boundaries.Enabled, boundaries.ResultsStopped);
            store.Add(target);
            await store.FlushAsync(token);
            var now = clock.UtcNow;
            var emptyVersion = TargetQuestionSetVersion.Publish(target, 1, [], now);
            var composition = PublishedSurveySchema.Compose(target, common, emptyVersion, NewOpaqueToken(), now);
            store.Add(emptyVersion);
            store.Add(composition);
            await store.FlushAsync(token);
            target.Publish(target.VersionNo, composition);
            var entryRef = IssueTargetCode(target);
            await AuditAsync(scope.Actor, "aq.target.create", target.Id, token);
            return Success(ToTarget(target, composition, settings) with { SurveyEntryRef = entryRef });
        }, cancellationToken);

    public Task<Result<AqTargetSummary>> UpdateTargetAsync(Guid targetId, AqUpdateTargetRequest request, CancellationToken cancellationToken) =>
        store.WriteAsync(async token =>
        {
            var scoped = await RequireTargetAsync(targetId, Manage, token);
            if (!scoped.IsSuccess)
                return Failure<AqTargetSummary>(scoped.ErrorDetail!);
            var (scope, target) = scoped.Value!;
            var versionError = CheckVersion<AqTargetSummary>(target.VersionNo, request.ExpectedVersionNo);
            if (versionError is not null)
                return versionError;
            var settings = await store.FindAsync<EventFeedbackSettings>(x => x.EventId == target.EventId, token);
            if (settings is null)
                return Conflict<AqTargetSummary>("Configure the Event reception first.");
            if (!TryDateBoundary(request.StartAt, target.StartsAtUtc, out var start)
                || !TryDateBoundary(request.EndAt, target.EndsAtUtc, out var end))
                return Invalid<AqTargetSummary>("Target reception boundaries must be valid timestamps or null.");

            var publication = await CurrentPublicationAsync(target, token);
            var desiredResultsStop = request.ResultsStopped ?? target.ResultsStopped;
            var affected = desiredResultsStop != target.ResultsStopped ? new[] { target.PresentationId } : [];
            var readers = await CaptureFeedbackAccessAsync(affected, token);
            target.Configure(request.ExpectedVersionNo, settings, start, end,
                request.Enabled ?? target.IsEnabled, desiredResultsStop);
            await AuditAsync(scope.Actor, "aq.target.settings.update", target.Id, token);
            await PublishFeedbackAccessChangesAsync(affected, readers, token);
            return Success(ToTarget(target, publication, settings));
        }, cancellationToken);

    public async Task<Result<AqManagementPage<AqVersionSummary>>> ListTargetVersionsAsync(
        Guid targetId, string? cursor, int pageSize, CancellationToken cancellationToken)
    {
        var scoped = await RequireTargetAsync(targetId, EditQuestions, cancellationToken);
        return scoped.IsSuccess
            ? await PageAsync<TargetQuestionSetVersion, AqVersionSummary>(x => x.SurveyTargetId == targetId, targetId, cursor, pageSize,
                item => new(item.Id, "published", item.Number), cancellationToken)
            : Failure<AqManagementPage<AqVersionSummary>>(scoped.ErrorDetail!);
    }

    public async Task<Result<AqVersionDetail>> GetTargetVersionAsync(Guid targetId, Guid versionId, CancellationToken cancellationToken)
    {
        var scoped = await RequireTargetAsync(targetId, EditQuestions, cancellationToken);
        if (!scoped.IsSuccess)
            return Failure<AqVersionDetail>(scoped.ErrorDetail!);

        var item = await store.FindAdditionalVersionAsync(targetId, versionId, cancellationToken);
        return item is null ? NotFound<AqVersionDetail>() : Success(ToVersionDetail(item.Id, item.Number, item.Fields));
    }

    public Task<Result<AqVersionSummary>> PublishTargetVersionAsync(
        Guid targetId, AqPublishQuestionVersionRequest request, CancellationToken cancellationToken) =>
        store.WriteAsync(async token =>
        {
            var scoped = await RequireTargetAsync(targetId, EditQuestions, token);
            if (!scoped.IsSuccess)
                return Failure<AqVersionSummary>(scoped.ErrorDetail!);
            var (scope, target) = scoped.Value!;
            var versionError = CheckVersion<AqVersionSummary>(target.VersionNo, request.ExpectedVersionNo);
            if (versionError is not null)
                return versionError;
            if (!TryFields(request, out var fields))
                return Invalid<AqVersionSummary>("Supply the additional fields and a publish action.");

            var publication = await CurrentPublicationAsync(target, token);
            var common = publication is null ? null :
                await store.FindCommonVersionAsync(target.FormDefinitionId, publication.FormVersionId, token);
            if (common is null || publication is null)
                return Conflict<AqVersionSummary>("The target has no current published common version.");
            if (await store.AnyAsync<FormSubmission>(x => x.SurveyTargetId == targetId, token))
            {
                var previous = await store.FindAdditionalVersionAsync(targetId, publication.TargetQuestionSetVersionId, token);
                if (!CompatibleChange(previous?.Fields, fields, request))
                    return Conflict<AqVersionSummary>("After submissions, only explicit compatible field corrections may publish.");
            }

            var now = clock.UtcNow;
            var addition = TargetQuestionSetVersion.Publish(target, checked((int)target.VersionNo), fields, now);
            var composition = PublishedSurveySchema.Compose(target, common, addition, NewOpaqueToken(), now);
            store.Add(addition);
            store.Add(composition);
            await store.FlushAsync(token);
            target.Publish(request.ExpectedVersionNo, composition);
            await AuditAsync(scope.Actor, "aq.target.questions.publish", addition.Id, token);
            return Success(new AqVersionSummary(addition.Id, "published", addition.Number));
        }, cancellationToken);

    private async Task<Result<FormDefinition>> RequireFormAsync(Guid formId, CancellationToken token)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor)
            return Failure<FormDefinition>(new("AuthenticationRequired", "Sign in to continue."));
        var form = await store.FindAsync<FormDefinition>(
            x => x.Id == formId && x.Purpose == FormPurpose.Survey && x.DeletedAt == null, token);
        if (form is null || form.TenantId != currentTenant.TenantId)
            return NotFound<FormDefinition>();
        var parent = await ResolveFormWorkspaceAsync(form.WorkspaceId, form.GroupId, form.ProjectId, token);
        return parent is not null && await forms.CanCreateForm(actor, form.WorkspaceId, form.GroupId, form.ProjectId, token)
            && await HasAsync(actor, parent.Value, EditQuestions, token)
            ? Success(form) : NotFound<FormDefinition>();
    }

    private async Task<Guid?> ResolveFormWorkspaceAsync(Guid? workspaceId, Guid? groupId, Guid? projectId, CancellationToken token)
    {
        if (groupId is { } group)
            return (await store.FindAsync<Group>(x => x.Id == group && x.DeletedAt == null, token))?.WorkspaceId;
        if (projectId is { } project)
            return (await store.FindAsync<Project>(x => x.Id == project && x.DeletedAt == null, token))?.WorkspaceId;
        return workspaceId;
    }

    private async Task<Result<(EventScope Event, SurveyCampaign Campaign)>> RequireCampaignAsync(
        Guid campaignId, string capability, CancellationToken token)
    {
        if (!currentUser.IsAuthenticated)
            return Failure<(EventScope, SurveyCampaign)>(new("AuthenticationRequired", "Sign in to continue."));
        var item = await store.FindAsync<SurveyCampaign>(x => x.Id == campaignId, token);
        if (item is null)
            return NotFound<(EventScope, SurveyCampaign)>();
        var scope = await RequireEventAsync<SurveyCampaign>(item.EventId, capability, token);
        return scope.IsSuccess ? Success((scope.Value!, item)) : Failure<(EventScope, SurveyCampaign)>(scope.ErrorDetail!);
    }

    private async Task<Result<(EventScope Event, SurveyTarget Target)>> RequireTargetAsync(Guid id, string capability, CancellationToken token)
    {
        if (!currentUser.IsAuthenticated)
            return Failure<(EventScope, SurveyTarget)>(new("AuthenticationRequired", "Sign in to continue."));
        var item = await store.FindAsync<SurveyTarget>(x => x.Id == id, token);
        if (item is null)
            return NotFound<(EventScope, SurveyTarget)>();
        var scope = await RequireEventAsync<SurveyTarget>(item.EventId, capability, token);
        return scope.IsSuccess ? Success((scope.Value!, item)) : Failure<(EventScope, SurveyTarget)>(scope.ErrorDetail!);
    }

    private Task<PublishedSurveySchema?> CurrentPublicationAsync(SurveyTarget item, CancellationToken token) =>
        item.PublishedSchemaId is { } id
            ? store.FindAsync<PublishedSurveySchema>(x => x.Id == id && x.SurveyTargetId == item.Id, token)
            : Task.FromResult<PublishedSurveySchema?>(null);

    private static bool TryFields(AqPublishQuestionVersionRequest request, out IReadOnlyList<SurveyField> fields)
    {
        var result = new List<SurveyField>();
        fields = result;
        if (request.Action != "publish" || request.Fields is not { } inputs)
            return false;
        foreach (var item in inputs)
        {
            if (!TryWireEnum(item.Type, out SurveyFieldType type))
                return false;
            result.Add(new SurveyField(item.FieldKey, item.Label, type, item.Required, item.Options ?? [], item.Constraints));
        }
        return true;
    }

    private static bool CompatibleChange(
        IReadOnlyList<FormFieldDefinition>? previous, IReadOnlyList<SurveyField> next, AqPublishQuestionVersionRequest request)
    {
        if (previous is null || previous.Count != next.Count || request.Compatibility is not { } mapping
            || mapping.Count != next.Count || string.IsNullOrWhiteSpace(request.ChangeReason))
            return false;
        var oldOrdered = previous.OrderBy(x => x.SortOrder).ToArray();
        var expectedKeys = oldOrdered.Select(item => item.FieldKey).ToHashSet(StringComparer.Ordinal);
        foreach (var pair in mapping)
        {
            if (pair.PreviousFieldKey != pair.FieldKey || !expectedKeys.Remove(pair.FieldKey))
                return false;
        }
        for (var index = 0; index < next.Count; index++)
        {
            var old = oldOrdered[index].ToSurveyField();
            var current = next[index];
            if (old.FieldKey != current.FieldKey || old.Type != current.Type || old.Required != current.Required
                || old.Constraints != current.Constraints
                || !old.Options.Select(x => x.OptionKey).SequenceEqual(current.Options.Select(x => x.OptionKey)))
                return false;
        }
        return true;
    }

    private static bool TryDateBoundary(JsonElement input, DateTimeOffset? previous, out DateTimeOffset? result)
    {
        result = previous;
        if (input.ValueKind == JsonValueKind.Undefined)
            return true;
        if (input.ValueKind == JsonValueKind.Null)
        {
            result = null;
            return true;
        }
        if (input.ValueKind != JsonValueKind.String || !input.TryGetDateTimeOffset(out var parsed))
            return false;
        result = parsed;
        return true;
    }

    private static AqFormSummary ToFormSummary(FormDefinition item) =>
        new(item.Id, item.Title, "survey", item.VersionNo > 1 ? "published" : "draft", item.VersionNo);

    private static AqVersionDetail ToVersionDetail(Guid id, long versionNo, IEnumerable<FormFieldDefinition> fields) =>
        new(id, "published", versionNo, fields.OrderBy(x => x.SortOrder).Select(x => x.ToSurveyField())
            .Select(x => new AqFormField(x.FieldKey, x.Label, Wire(x.Type), x.Required, x.Options.Count == 0 ? null : x.Options, x.Constraints))
            .ToArray());

    private static AqCampaignSummary ToCampaign(SurveyCampaign item) =>
        new(item.Id, item.EventId, item.FormDefinitionId, item.VersionNo, item.IsEnabled, item.IsPublished, item.RetentionPolicyId);

    private static AqTargetSummary ToTarget(SurveyTarget item, PublishedSurveySchema? schema, EventFeedbackSettings? settings)
    {
        SurveyReceptionWindow? window = null;
        if (settings is not null)
        {
            var start = item.StartsAtUtc is { } targetStart && targetStart > settings.StartsAtUtc ? targetStart : settings.StartsAtUtc;
            var end = item.EndsAtUtc is { } targetEnd && targetEnd < settings.EndsAtUtc ? targetEnd : settings.EndsAtUtc;
            if (start < end)
                window = SurveyReceptionWindow.Intersect(settings, item);
        }
        return new(item.Id, item.CampaignId, item.PresentationId, schema?.FormVersionId ?? Guid.Empty, item.VersionNo,
            new(item.IsEnabled, item.ResultsStopped, item.StartsAtUtc, item.EndsAtUtc), window?.StartsAtUtc, window?.EndsAtUtc);
    }

    private static string NewOpaqueToken() => Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}

using System.Text.Json.Serialization;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Projects;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ResearchPlanStepRequest(
    string? Title,
    string? Objective,
    string? ScopeSummary,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ResearchPlanStepStatus Status,
    Guid? BaseStepId = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceResearchPlanRequest(
    long ExpectedVersion,
    IReadOnlyList<ResearchPlanStepRequest>? Steps,
    string? PreviewFingerprint = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewResearchPlanRequest(
    long ExpectedVersion,
    IReadOnlyList<ResearchPlanStepRequest>? Steps);

public sealed record ResearchPlanStepResponse(
    Guid Id,
    int Position,
    string Title,
    string Objective,
    string ScopeSummary,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ResearchPlanStepStatus Status);

public sealed record ResearchPlanRevisionResponse(
    Guid Id,
    int Number,
    DateTimeOffset CreatedAtUtc,
    Guid CreatedByUserId,
    IReadOnlyList<ResearchPlanStepResponse> Steps);

/// <summary>
/// The current saved plan. Its current revision is the exact plan intended for
/// execution-start review; a later execution contract captures that revision.
/// </summary>
public sealed record ResearchPlanResponse(
    Guid? PlanId,
    long Version,
    ResearchPlanRevisionResponse? CurrentRevision,
    bool CanManage);

/// <summary>
/// A normalized proposed step. BaseStepId identifies the step in the current
/// immutable revision from which this proposed step was edited. New steps have
/// no BaseStepId.
/// </summary>
public sealed record ResearchPlanProposedStepResponse(
    Guid? BaseStepId,
    int Position,
    string Title,
    string Objective,
    string ScopeSummary,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ResearchPlanStepStatus Status);

/// <summary>
/// Typed before/after change for one logical step. Kinds may contain both
/// Modified and Reordered when the same existing step changed content and order.
/// </summary>
public sealed record ResearchPlanStepDiffResponse(
    IReadOnlyList<string> Kinds,
    Guid? BaseStepId,
    int? BeforePosition,
    int? AfterPosition,
    ResearchPlanStepResponse? Before,
    ResearchPlanProposedStepResponse? After,
    IReadOnlyList<string> ChangedFields);

public sealed record ResearchPlanImpactItemResponse(
    string Kind,
    string Message,
    int? StepPosition = null,
    Guid? BaseStepId = null);

/// <summary>
/// Server-computed, deliberately bounded impact summary. Free-form plan text is
/// not interpreted as authority to widen source access or mutate Task deliverables.
/// </summary>
public sealed record ResearchPlanImpactSummaryResponse(
    int BeforeStepCount,
    int AfterStepCount,
    bool ExecutionStepCountChanged,
    bool ExecutionOrderChanged,
    bool SourceScopeGuidanceChanged,
    bool DeliverableAlignmentReviewRequired,
    IReadOnlyList<ResearchPlanImpactItemResponse> Items);

/// <summary>
/// Authoritative preview for one exact proposed replacement over one current
/// plan version. Fingerprint is echoed on save so the server can reject a save
/// that does not match the reviewed normalized draft.
/// </summary>
public sealed record ResearchPlanPreviewResponse(
    long BaseVersion,
    Guid? BaseRevisionId,
    int? BaseRevisionNumber,
    string Fingerprint,
    IReadOnlyList<ResearchPlanProposedStepResponse> ProposedSteps,
    IReadOnlyList<ResearchPlanStepDiffResponse> Changes,
    ResearchPlanImpactSummaryResponse Impact);

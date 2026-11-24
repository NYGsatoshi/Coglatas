using System.Text.Json.Serialization;
using Coglatas.Application.Common;
using Coglatas.Domain.Entities;

namespace Coglatas.Application.SurveyFeedback;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManualFeedbackInvitationPolicy(
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] int DecisionWindowSeconds,
    [property: JsonRequired] int MaximumActiveSeconds);

public sealed record FeedbackInvitationPolicySettings(bool Enabled, int DecisionWindowSeconds,
    int MaximumActiveSeconds, long VersionNo, string ApprovalMode);

public sealed class FeedbackEventInvitationPolicyManagement(IFeedbackLifecycleRepository repository)
{
    private const int MaximumDecisionWindowSeconds = 7 * 24 * 60 * 60;
    private const int MaximumActiveWindowSeconds = 30 * 24 * 60 * 60;

    // The event management use case authorizes the event and keeps its versioned write transaction open.
    public async Task<FeedbackInvitationPolicySettings?> ReadAsync(Guid tenantId, Guid eventId,
        CancellationToken cancellationToken)
    {
        var policy = await repository.ReadInvitationPolicyAsync(tenantId, eventId, cancellationToken);
        return policy is null ? null : ToSettings(policy);
    }

    public async Task<Result<FeedbackInvitationPolicySettings>> StageManualAsync(Guid tenantId, Guid eventId,
        ManualFeedbackInvitationPolicy request, CancellationToken cancellationToken)
    {
        if (request.DecisionWindowSeconds is < 1 or > MaximumDecisionWindowSeconds ||
            request.MaximumActiveSeconds is < 1 or > MaximumActiveWindowSeconds)
        {
            return Result<FeedbackInvitationPolicySettings>.Failure(new ApplicationErrorDetail("ValidationFailed",
                "The invitation decision window and maximum duration must be within the supported bounds."));
        }

        var policy = await repository.FindInvitationPolicyForUpdateAsync(tenantId, eventId, cancellationToken);
        if (policy?.ApprovedForAutomaticApproval == true)
        {
            return Result<FeedbackInvitationPolicySettings>.Failure(new ApplicationErrorDetail("RestrictedFieldDenied",
                "The existing approval policy is governed by a separate event authorization."));
        }

        if (policy is null)
        {
            policy = new FeedbackInvitationPolicy
            {
                TenantId = tenantId,
                EventId = eventId,
                Enabled = request.Enabled,
                DecisionWindowSeconds = request.DecisionWindowSeconds,
                MaximumActiveSeconds = request.MaximumActiveSeconds
            };
            repository.AddInvitationPolicy(policy);
        }
        else if (policy.Enabled != request.Enabled || policy.DecisionWindowSeconds != request.DecisionWindowSeconds ||
                 policy.MaximumActiveSeconds != request.MaximumActiveSeconds)
        {
            policy.Enabled = request.Enabled;
            policy.DecisionWindowSeconds = request.DecisionWindowSeconds;
            policy.MaximumActiveSeconds = request.MaximumActiveSeconds;
            policy.VersionNo = checked(policy.VersionNo + 1);
        }

        return Result<FeedbackInvitationPolicySettings>.Success(ToSettings(policy));
    }

    private static FeedbackInvitationPolicySettings ToSettings(FeedbackInvitationPolicy policy) =>
        new(policy.Enabled, policy.DecisionWindowSeconds, policy.MaximumActiveSeconds, policy.VersionNo,
            policy.ApprovedForAutomaticApproval ? "approvedEventPolicy" : "operator");
}

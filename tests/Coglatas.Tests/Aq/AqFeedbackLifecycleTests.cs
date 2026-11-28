using Coglatas.Application.SurveyFeedback;
using Coglatas.Domain.Entities;

namespace Coglatas.Tests.Aq;

[Trait("Feature", "AQ")]
public sealed class AqFeedbackLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 11, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApprovalAndAcceptanceMustBothHappenBeforeTheGrantActivates(bool acceptFirst)
    {
        var policy = NewPolicy();
        var inviter = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var grant = FeedbackViewerGrant.Issue(Guid.NewGuid(), inviter, recipient, policy, 60, Now);
        Assert.False(grant.IsActive(Now));

        var priorVersion = grant.VersionNo;
        if (acceptFirst)
            grant.Accept(policy, priorVersion, Now);
        else
            grant.Approve(policy, approver, priorVersion, Now);
        Assert.False(grant.IsActive(Now));
        Assert.Equal(FeedbackViewerGrantStatus.Pending, grant.Status);

        if (acceptFirst)
        {
            Assert.Throws<InvalidOperationException>(() => grant.Approve(policy, approver, priorVersion, Now));
            grant.Approve(policy, approver, grant.VersionNo, Now);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => grant.Accept(policy, priorVersion, Now));
            grant.Accept(policy, grant.VersionNo, Now);
        }

        Assert.True(grant.IsActive(Now));
        Assert.Equal(Now.AddSeconds(60), grant.ExpiresAtUtc);
        Assert.False(grant.IsActive(Now.AddSeconds(60)));
    }

    [Fact]
    public void RevocationIsTerminalAndAutomaticApprovalStillNeedsAcceptance()
    {
        var policy = NewPolicy();
        policy.ApprovedForAutomaticApproval = true;
        var grant = FeedbackViewerGrant.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), policy, 60, Now);
        Assert.False(grant.IsActive(Now));
        Assert.Null(grant.RecipientAcceptedAtUtc);

        grant.Accept(policy, grant.VersionNo, Now);
        Assert.True(grant.IsActive(Now));
        Assert.Null(grant.ApprovedByUserId);

        grant.Terminate(false, "Synthetic scope removal", grant.VersionNo, Now.AddSeconds(1));
        Assert.False(grant.IsActive(Now.AddSeconds(1)));
        Assert.Equal(FeedbackViewerGrantStatus.Revoked, grant.Status);
        Assert.Throws<InvalidOperationException>(() => grant.Accept(policy, grant.VersionNo, Now.AddSeconds(2)));
    }

    [Fact]
    public void InvitationAtTheDecisionDeadlineCannotActivate()
    {
        var policy = NewPolicy();
        var grant = FeedbackViewerGrant.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), policy, null, Now);
        Assert.Throws<InvalidOperationException>(() => grant.Accept(policy, grant.VersionNo, grant.DecisionDeadlineUtc));
        Assert.False(grant.IsActive(grant.DecisionDeadlineUtc));
        Assert.True(grant.ExpireIfElapsed(grant.DecisionDeadlineUtc));
        Assert.Equal(FeedbackViewerGrantStatus.Expired, grant.Status);
    }

    [Fact]
    public void ResolvingAReportDoesNotChangeAnswerVisibilityAndStaleVersionDoesNotOverwrite()
    {
        var answerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var report = new FeedbackReportCase { FormSubmissionValueId = answerId, ReporterUserId = actorId };
        var visibility = new AnswerVisibilityDecision { FormSubmissionValueId = answerId };

        Assert.False(visibility.IsHidden);
        var originalVersion = visibility.VersionNo;
        visibility.Decide(true, actorId, "Synthetic moderation", originalVersion);
        Assert.True(visibility.IsHidden);
        Assert.Equal(FeedbackReportStatus.Open, report.Status);

        report.Dispose(true, actorId, "Synthetic resolution", report.VersionNo, Now);
        Assert.Equal(FeedbackReportStatus.Dismissed, report.Status);
        Assert.True(visibility.IsHidden);
        Assert.Throws<InvalidOperationException>(() =>
            visibility.Decide(false, actorId, "Stale restore", originalVersion));
        Assert.True(visibility.IsHidden);

        visibility.Decide(false, actorId, "Current restore", visibility.VersionNo);
        Assert.False(visibility.IsHidden);
        Assert.Equal(FeedbackReportStatus.Dismissed, report.Status);
    }

    [Fact]
    public void FeedbackAggregationUsesEligibleValuesAndOmitsIncompatibleQuestionVariants()
    {
        var result = SurveyFeedbackAggregation.Summarize(new FeedbackSummaryInput(3,
        [
            Value("score", "Number", "scale-v1", "2"),
            Value("score", "Number", "scale-v1", "4"),
            Value("color", "SingleChoice", "choices-v1", "\"green\""),
            Value("color", "SingleChoice", "choices-v1", "\"blue\""),
            Value("changed", "Number", "old-meaning", "10"),
            Value("changed", "Number", "new-meaning", "100")
        ]));

        Assert.Equal(3, result.Responses);
        var score = Assert.Single(result.Questions, row => row.FieldKey == "score");
        Assert.Equal(2, score.Denominator);
        Assert.Equal(3m, score.Mean);
        var color = Assert.Single(result.Questions, row => row.FieldKey == "color");
        Assert.Equal(2, color.Denominator);
        Assert.Equal([new FeedbackOptionCount("blue", 1), new FeedbackOptionCount("green", 1)], color.Distribution);
        Assert.DoesNotContain(result.Questions, row => row.FieldKey == "changed");
    }

    private static FeedbackValueSnapshot Value(string fieldKey, string fieldType, string compatibility, string json) =>
        new(Guid.NewGuid(), fieldKey, fieldType, compatibility, json, false);

    private static FeedbackInvitationPolicy NewPolicy() =>
        new()
        {
            TenantId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Enabled = true,
            DecisionWindowSeconds = 180,
            MaximumActiveSeconds = 120
        };
}

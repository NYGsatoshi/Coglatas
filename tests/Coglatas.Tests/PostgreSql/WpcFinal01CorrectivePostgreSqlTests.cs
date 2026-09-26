namespace Coglatas.Tests.PostgreSql;

/// <summary>
/// Compatibility names retained in the worker manifests that were introduced
/// during the first Final01 corrective pass. The authoritative cross-worker
/// evidence lives in the Final01 PostgreSQL acceptance suites.
/// </summary>
public sealed class WpcFinal01CorrectivePostgreSqlTests
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "WPC02A")]
    public Task LegacyUnknownVisibilityClassificationIsAuthorizedAuditedAndConcurrencyControlled() =>
        new WpcFinal01CanonicalCompletionPostgreSqlTests()
            .LegacyUnknownVisibilityCanBeExplicitlyClassifiedThenActivated();

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "WPC02A")]
    public async Task NonDefaultVisibilityMutationRequiresWorkspaceGovernanceOrVisibilityCapability()
    {
        // Negative evidence: project.create never implies visibility management.
        await new Wpc02CCanonicalProjectCreatePostgreSqlTests()
            .DelegatedProjectCreateDoesNotImplyVisibilityManagement();
        await new WpcFinal01CanonicalCompletionPostgreSqlTests()
            .ProjectCreateGrantDoesNotAuthorizeVisibilityChange();

        // Positive evidence: the exact current project.visibility.manage grant
        // authorizes the explicit, audited, concurrency-controlled mutation.
        await new WpcFinal01MergeAuditPostgreSqlTests()
            .VisibilityCapabilityGrantAllowsExplicitVisibilityMutation();
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "WPC02A")]
    public Task ArchivedProjectMembershipMutationFailsClosedWithoutCommit() =>
        new WpcFinal01CanonicalCompletionPostgreSqlTests()
            .ArchivedProjectRejectsAddUpdateAndRemoveMemberWithoutSideEffects();

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "WPC02D")]
    public async Task ProjectGeneralMembershipTracksRoleAndRemovalWithoutStalePostingRights()
    {
        await new WpcFinal01CanonicalCompletionPostgreSqlTests()
            .ProjectMemberViewerDowngradeRemovesProjectGeneralPostRights();
        await new WpcFinal01CanonicalCompletionPostgreSqlTests()
            .WorkspaceVisibleProjectMemberRemovalKeepsBroadReadButRevokesParticipantRights();
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQLIntegration")]
    [Trait("Scope", "WPC02F")]
    public async Task TaskNotificationAndRealtimeUseCanonicalProjectVisibilityAuthorization()
    {
        // MembersOnly revocation plus WorkspaceVisible positive compatibility.
        await new WpcFinal01CanonicalCompletionPostgreSqlTests()
            .MembersOnlyProjectMemberRemovalRevokesConversationAndTaskNotificationAccess();
        await new WpcFinal01CanonicalCompletionPostgreSqlTests()
            .ProjectRealtimeResolverUsesCanonicalVisibilityScope();

        // Restricted is a separate explicit negative: a Workspace-only user may
        // neither open/receive the Task notification nor receive Task/Project realtime.
        await new WpcFinal01MergeAuditPostgreSqlTests()
            .RestrictedProjectBlocksTaskNotificationAndRealtimeForWorkspaceOnlyMember();
    }
}

namespace Coglatas.Tests.SecurityArchitecture;

/// <summary>Source-bound explanations for unresolved cells; no unavailable operation receives RLS credit.</summary>
internal static class SecurityArchitectureRlsOperationDispositions
{
    internal sealed record Cell(string Operation, string Situation, string RoleKind, string DependencyKind,
        string SourceOperation, string? SourceSituation, string ReasonCode, string ExpectedObservedMechanism,
        string NativeSourceDigest, string NativeOperatorAvailability, string ApprovedApplicability,
        string RlsQualification, string AuthorityDisposition);

    public static IReadOnlyList<Cell> Bind(SecurityArchitectureRlsSchemaIdentity.Snapshot native,
        IReadOnlyList<SecurityArchitectureRlsUnavailableOperations.Disposition> unavailable,
        IReadOnlyList<SecurityArchitectureRlsGuardedProbes.Probe> guarded)
    {
        var cells = new List<Cell>();
        foreach (var source in unavailable)
        {
            Assert.Equal("SOURCE_BLOCKED_DIRECT_MUTATION", source.Classification);
            foreach (var situation in new[] { "sameScope", "crossTenant", "missingContext", "invalidContext", "unauthorizedRole", "wrongOwnership" })
            {
                if (situation == "wrongOwnership" && source.Operation != "UPDATE") continue;
                var direct = situation is "sameScope" or "wrongOwnership";
                cells.Add(new(source.Operation, situation,
                    situation == "unauthorizedRole" ? "syntheticUnauthorized" : "syntheticApplication",
                    situation == "sameScope" ? "DIRECT_SOURCE_GUARD" : "POSITIVE_CONTROL_SOURCE_GUARD", source.Operation, null,
                    source.ReasonCode, direct ? source.Guard is not null ? "TRIGGER_REJECTION" : "CONSTRAINT_REJECTION" :
                        situation == "unauthorizedRole" ? "GRANT_DENIAL" : "RLS_FILTER",
                    native.SchemaDigest, "SOURCE_UNAVAILABLE_DIRECT_MUTATION", "UNVERIFIED", "UNVERIFIED",
                    source.AlternateLifecycle == "ParentDeletionCascadeOnlyRetentionAuthorityUnverified"
                        ? "RetentionOperatorAuthorityRequiresOwnerReview" : "UnavailableApplicabilityRequiresOwnerReview"));
            }
        }
        foreach (var source in guarded)
        {
            Assert.Equal("UNVERIFIED", source.RlsQualification);
            cells.Add(new(source.Operation, source.Situation, "syntheticApplication", "NATIVE_BEFORE_RLS_PRECHECK",
                source.Operation, source.Situation, "SourceMutationGuard", "TRIGGER_REJECTION", native.SchemaDigest,
                "POSITIVE_AVAILABLE_NATIVE_PRECHECK_UNRESOLVED", "UNVERIFIED", "UNVERIFIED", source.AuthorityDisposition));
        }
        Assert.Equal(cells.Count, cells.Select(cell => (cell.Operation, cell.Situation)).Distinct().Count());
        Assert.All(cells, cell =>
        {
            Assert.Contains(cell.Operation, new[] { "INSERT", "UPDATE", "DELETE" });
            Assert.Contains(cell.Situation, new[] { "sameScope", "crossTenant", "missingContext", "invalidContext", "unauthorizedRole", "wrongOwnership" });
            Assert.Equal(cell.Situation == "unauthorizedRole" ? "syntheticUnauthorized" : "syntheticApplication", cell.RoleKind);
            Assert.Equal(cell.Operation, cell.SourceOperation);
            Assert.Equal(native.SchemaDigest, cell.NativeSourceDigest);
            Assert.Equal("UNVERIFIED", cell.ApprovedApplicability);
            Assert.Equal("UNVERIFIED", cell.RlsQualification);
            if (cell.DependencyKind == "NATIVE_BEFORE_RLS_PRECHECK")
            {
                Assert.Equal(cell.Situation, cell.SourceSituation);
                Assert.Equal("SourceMutationGuard", cell.ReasonCode);
                Assert.Equal("TRIGGER_REJECTION", cell.ExpectedObservedMechanism);
                Assert.Equal("POSITIVE_AVAILABLE_NATIVE_PRECHECK_UNRESOLVED", cell.NativeOperatorAvailability);
                Assert.Contains(cell.AuthorityDisposition, new[] { "ConcreteParentLookupAuthorityRequiresOwnerReview", "ConcreteMutationAuthorityRequiresOwnerReview" });
            }
            else
            {
                Assert.Null(cell.SourceSituation);
                Assert.Contains(cell.DependencyKind, new[] { "DIRECT_SOURCE_GUARD", "POSITIVE_CONTROL_SOURCE_GUARD" });
                Assert.Equal("SOURCE_UNAVAILABLE_DIRECT_MUTATION", cell.NativeOperatorAvailability);
                Assert.Contains(cell.AuthorityDisposition, new[] { "UnavailableApplicabilityRequiresOwnerReview", "RetentionOperatorAuthorityRequiresOwnerReview" });
                Assert.Equal(cell.Situation == "sameScope", cell.DependencyKind == "DIRECT_SOURCE_GUARD");
                Assert.Contains(cell.ExpectedObservedMechanism, new[] { "TRIGGER_REJECTION", "CONSTRAINT_REJECTION", "RLS_FILTER", "GRANT_DENIAL" });
                Assert.Equal(SecurityArchitectureRlsUnavailableOperations.Reason(native.Table, cell.Operation), cell.ReasonCode);
            }
        });
        return cells;
    }
}

using System.Text.Json;

namespace Coglatas.SecurityArchitecture;

public static class InventoryDiff
{
    public static async Task<ValidationResult> CompareWithHistoryAsync(ContractDocument baseline,
        ContractDocument candidate, DateTimeOffset asOfUtc, SpecRegistryDocument baselineRegistry,
        SpecRegistryDocument candidateRegistry, string expectedSpecificationRevision,
        Func<SpecSource, Task<byte[]?>> readSource)
    {
        var diagnostics = Compare(baseline, candidate, asOfUtc).Diagnostics.ToList();
        var registry = await SpecRegistryValidator.ValidateAsync(candidateRegistry,
            expectedSpecificationRevision, readSource, baselineRegistry);
        diagnostics.AddRange(registry.Diagnostics);
        if (!registry.Valid) return ContractValidator.Result(diagnostics);
        ValidateReferences(baseline, baselineRegistry, "baseline", diagnostics);
        ValidateReferences(candidate, candidateRegistry, "candidate", diagnostics);
        if (SpecDigest.Document(baselineRegistry.Requirements) != SpecDigest.Document(candidateRegistry.Requirements))
            diagnostics.Add(new("HISTORY_CHANGED", "registry",
                "Requirement allocation or retained revision history changed and requires owner review."));
        if (SpecDigest.Document(baselineRegistry) != SpecDigest.Document(candidateRegistry))
            diagnostics.Add(new("REGISTRY_CHANGED", "registry",
                "Registry metadata changed; structural validation does not authorize this transition."));
        return ContractValidator.Result(diagnostics);
    }

    private static void ValidateReferences(ContractDocument contracts, SpecRegistryDocument registry,
        string side, List<Diagnostic> diagnostics)
    {
        var requirements = registry.Requirements.ToDictionary(requirement => requirement.SpecId,
            requirement => requirement.Versions[^1], StringComparer.Ordinal);
        foreach (var contract in contracts.Contracts)
            foreach (var specId in contract.SpecIds)
                if (!requirements.TryGetValue(specId, out var requirement) ||
                    contract.ActivationState != ActivationState.Retired && requirement.Status == SpecStatus.Retired)
                    diagnostics.Add(new("SPEC_CONTRACT_REFERENCE", side,
                        "Contract references must resolve to registered current obligations; retired contracts may retain historical references."));
    }

    public static ValidationResult Compare(ContractDocument baseline, ContractDocument candidate, DateTimeOffset asOfUtc)
    {
        var diagnostics = ContractValidator.Validate(baseline, asOfUtc).Diagnostics
            .Concat(ContractValidator.Validate(candidate, asOfUtc).Diagnostics).ToList();
        if (diagnostics.Count != 0) return ContractValidator.Result(diagnostics);
        var current = candidate.Contracts.ToDictionary(c => c.ContractId, StringComparer.Ordinal);
        var prior = baseline.Contracts.ToDictionary(c => c.ContractId, StringComparer.Ordinal);
        foreach (var old in baseline.Contracts)
        {
            if (!current.TryGetValue(old.ContractId, out var next))
            {
                diagnostics.Add(new("SCOPE_REMOVED", old.ContractId, "Removal requires review; no automatic baseline shrinkage."));
                diagnostics.Add(new("CONTRACT_REMOVED", old.ContractId, "Contract removal requires owner review."));
                diagnostics.Add(new("SCOPE_NARROWED", old.ContractId, "A previously inventoried boundary is absent."));
            }
            else if (JsonSerializer.Serialize(old, ContractJson.Options) != JsonSerializer.Serialize(next, ContractJson.Options))
            {
                diagnostics.Add(new("POLICY_CHANGED", old.ContractId,
                    "Review-visible contract change; approval must be authenticated separately."));
                DescribeChanges(old, next, diagnostics);
            }
        }
        foreach (var added in current.Keys.Except(prior.Keys, StringComparer.Ordinal))
        {
            diagnostics.Add(new("UNCLASSIFIED_ADDITION", added, "New boundary requires mapping and owner review."));
            diagnostics.Add(new("CONTRACT_ADDED", added, "New contract requires owner review."));
            diagnostics.Add(new("SCOPE_EXPANDED", added, "A new boundary is included in the inventory."));
        }
        return ContractValidator.Result(diagnostics);
    }

    private static void DescribeChanges(FlowContract old, FlowContract next, List<Diagnostic> diagnostics)
    {
        void Add(string rule, string reason) => diagnostics.Add(new(rule, old.ContractId, reason));
        Add("CONTRACT_CHANGED", "Contract metadata changed and requires owner review.");
        if (old.Principal != next.Principal)
            Add("PRINCIPAL_CHANGED", "The declared principal changed.");
        if (!old.SpecIds.SequenceEqual(next.SpecIds, StringComparer.Ordinal))
            Add("REQUIREMENT_REFERENCES_CHANGED", "Normative requirement references or their ordering changed.");
        if (!old.RequiredEvidence.SequenceEqual(next.RequiredEvidence))
            Add("EVIDENCE_REQUIREMENTS_CHANGED", "Required evidence classes or their ordering changed.");
        if (old.ActivationState != next.ActivationState)
            Add("ACTIVATION_CHANGED", "The declared activation state changed; this does not authorize activation.");
        if (old.ActivationApproval != next.ActivationApproval)
            Add("APPROVAL_CHANGED", "Structural applicability approval metadata changed; authority remains unverified.");
        if (old.Exception != next.Exception)
            Add("EXCEPTION_CHANGED", "Exception scope, lifetime or structural approval metadata changed.");

        // Scope values are opaque exact identities. Replacing one removes the old scope and adds the new;
        // the tool cannot infer semantic containment from a route, policy expression or resource name.
        var oldScope = Scope(old).ToHashSet(StringComparer.Ordinal);
        var nextScope = Scope(next).ToHashSet(StringComparer.Ordinal);
        var expanded = nextScope.Except(oldScope, StringComparer.Ordinal).Any();
        var narrowed = oldScope.Except(nextScope, StringComparer.Ordinal).Any();
        if (expanded || narrowed) Add("SCOPE_CHANGED", "Exact declared boundary or service scope values changed.");
        if (expanded) Add("SCOPE_EXPANDED", "Declared scope includes values absent from the baseline; review is required.");
        if (narrowed) Add("SCOPE_NARROWED", "Previously declared scope values are absent; coverage shrinkage requires review.");
    }

    private static IEnumerable<string> Scope(FlowContract contract)
    {
        yield return "type:" + contract.Type;
        yield return "source:" + contract.Source;
        yield return "destination:" + contract.Destination;
        yield return "protocol:" + contract.Protocol;
        yield return "operation:" + contract.Operation;
        yield return "tenant:" + contract.TenantScope;
        yield return "resource:" + contract.ResourceScope;
        yield return "boundary:" + contract.TrustBoundary;
        if (contract.Api is { } api) { yield return "route:" + api.Route; yield return "method:" + api.Method; }
        if (contract.Rls is { } rls)
        {
            yield return "schema:" + rls.Schema; yield return "table:" + rls.Table;
            yield return "role:" + rls.Role; yield return "rls-operation:" + rls.Operation;
        }
        if (contract.SignalR is { } signalR)
        {
            yield return "hub:" + signalR.Hub; yield return "hub-method:" + signalR.Method;
            yield return "event:" + signalR.Event; yield return "event-scope:" + signalR.Scope;
        }
        if (contract.Kafka is { } kafka)
        {
            yield return "kafka-resource-type:" + kafka.ResourceType; yield return "kafka-resource:" + kafka.Resource;
            yield return "host:" + kafka.Host; yield return "kafka-operation:" + kafka.Operation;
        }
        if (contract.Service is { } service)
        {
            yield return "audience:" + service.Audience; yield return "service-destination:" + service.Destination;
            foreach (var scope in service.RequiredScopes) yield return "service-scope:" + scope;
        }
    }
}

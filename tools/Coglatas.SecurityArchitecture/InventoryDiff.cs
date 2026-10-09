using System.Text.Json;

namespace Coglatas.SecurityArchitecture;

public static class InventoryDiff
{
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
                diagnostics.Add(new("SCOPE_REMOVED", old.ContractId, "Removal requires review; no automatic baseline shrinkage."));
            else if (JsonSerializer.Serialize(old, ContractJson.Options) != JsonSerializer.Serialize(next, ContractJson.Options))
                diagnostics.Add(new("POLICY_CHANGED", old.ContractId,
                    "Review-visible contract change; approval must be authenticated separately."));
        }
        foreach (var added in current.Keys.Except(prior.Keys, StringComparer.Ordinal))
            diagnostics.Add(new("UNCLASSIFIED_ADDITION", added, "New boundary requires mapping and owner review."));
        return ContractValidator.Result(diagnostics);
    }
}

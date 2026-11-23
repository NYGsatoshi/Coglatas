using System.Security.Cryptography;
using System.Text;

namespace Coglatas.Domain.ProjectIde;

public enum CompilerGraphClosureState { Evaluated, ResourceLimit }

/// <summary>Whole declared binding scope, with original owner locations. No runnable plan or current authority.</summary>
public sealed class CompilerGraphClosure
{
    public const string Format = "coglatas.compiler-graph-closure/1";
    public CompilerBindingClosure Binding { get; }
    public CompilerGraphClosureState State { get; }
    public IReadOnlyList<RelationFinding> Findings { get; }
    public int LogicalOperationsConsumed { get; }
    public ContentDigest FindingsDigest { get; }
    public bool StructuralValidated => State == CompilerGraphClosureState.Evaluated && Findings.All(finding => IsDeferred(finding.Code));
    public SourceJson Data { get; }
    public ContentDigest Digest { get; }
    internal static bool IsDeferred(string code) => code is "coglatas.relation.evidence-not-evaluated" or
        "coglatas.relation.constraint-not-evaluated" or "coglatas.relation.resource-snapshot-not-evaluated";

    private CompilerGraphClosure(CompilerBindingClosure binding, CompilerGraphClosureState state, IEnumerable<RelationFinding> findings, int consumed)
    {
        Binding = binding; State = state; LogicalOperationsConsumed = consumed;
        Findings = new ImmutableListView<RelationFinding>(findings.OrderBy(finding => finding.Location.Data.CanonicalText, StringComparer.Ordinal)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal).ThenBy(finding => finding.State).Distinct());
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            hash.AppendData(Encoding.UTF8.GetBytes("coglatas.compiler-graph-findings/1\0"));
            foreach (var finding in Findings)
            {
                var data = SourceJson.FromObject(new { code = finding.Code, state = finding.State.ToString(), location = finding.Location.Data.Value });
                hash.AppendData(Convert.FromHexString(ContentDigest.Compute("coglatas.graph-finding/1", data.ToCanonicalBytes()).Value));
            }
            FindingsDigest = ContentDigest.Parse(SourceJson.FromObject(new { algorithm = ContentDigest.Algorithm, value = Convert.ToHexStringLower(hash.GetHashAndReset()) }));
        }
        Data = SourceJson.FromObject(new { format = Format, rootInputDigest = binding.Input.InputDigest.Data.Value,
            bindingClosureDigest = binding.Digest.Data.Value, state = state.ToString(), structuralValidated = StructuralValidated,
            findingsDigest = FindingsDigest.Data.Value, findingCount = Findings.Count, logicalOperationsConsumed = consumed,
            scope = "coglatas.declared-snapshots" }, binding.Input.Source.Data.Limits);
        Digest = ContentDigest.Compute(Format, Data.ToCanonicalBytes());
    }
    internal static CompilerGraphClosure Build(CompilerBindingClosure binding, IReadOnlyList<UnitDefinition> units, int remaining, CancellationToken token)
    {
        if (binding.State != CompilerBindingClosureState.Complete || !binding.IncludesDeclaredSnapshots)
            throw new ArgumentException("Whole-graph validation requires a complete declared-snapshot binding scope.");
        var initial = remaining; var findings = new List<RelationFinding>();
        foreach (var region in binding.Regions)
        {
            token.ThrowIfCancellationRequested();
            if (!ProjectSemanticBinder.WithinBudget(region.Source.Data.Value, ref remaining, token))
            {
                findings.Add(new("coglatas.relation.budget-exhausted", RelationFindingState.Unknown,
                    ProjectLocation.Create(binding.Input.Source.Documents[0].DocumentId, binding.Input.Source.Context)));
                return new(binding, CompilerGraphClosureState.ResourceLimit, findings, initial - Math.Max(0, remaining));
            }
        }
        var root = binding.Regions.Single(region => region.IsRoot);
        var result = RelationGraphValidator.ValidateClosedScope(new(root.Source, true, true, true),
            binding.Regions.Where(region => !region.IsRoot).Select(region => new RelationScopeSnapshot(region.Source, true, true, true)),
            units, new(binding.Input.ExecutionProfile.MaximumDeclarations, binding.Input.ExecutionProfile.MaximumRelations), token,
            amount => { remaining -= amount; return remaining >= 0; });
        token.ThrowIfCancellationRequested();
        return new(binding, result.ProcessingComplete ? CompilerGraphClosureState.Evaluated : CompilerGraphClosureState.ResourceLimit,
            result.Findings, initial - Math.Max(0, remaining));
    }
}

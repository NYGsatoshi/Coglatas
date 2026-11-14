using System.Text;
using System.Text.Json.Nodes;

namespace Coglatas.Domain.ProjectIde;

public enum DraftRenameState { Ready, StaleInput, OutOfScope, Unavailable, Unsupported, InvalidContext, ContextConflict, InvalidName, ResourceLimit, NoChange }
public sealed record DraftRenamePreview(DraftRenameState State, ProjectDraftRenamePlan? Plan);

/// <summary>An immutable checked local Draft replacement. It grants no authorization, persistence or Merge authority.</summary>
public sealed class ProjectDraftRenamePlan
{
    public CompilerInputManifest Before { get; }
    public CompilerInputManifest After { get; }
    public ProjectLocation Target { get; }
    public string? PreviousName { get; }
    public string NewName { get; }
    public IReadOnlyList<CompilerBoundReference> References { get; }
    public bool ReferenceCoverageComplete { get; }
    public SourceJson Data { get; }

    internal ProjectDraftRenamePlan(CompilerInputManifest before, CompilerInputManifest after, ProjectLocation target,
        string? previousName, string newName, IReadOnlyList<CompilerBoundReference> references, bool complete)
    {
        Before = before; After = after; Target = target; PreviousName = previousName; NewName = newName;
        References = new ImmutableListView<CompilerBoundReference>(references); ReferenceCoverageComplete = complete;
        Data = SourceJson.FromObject(new
        {
            beforeInputDigest = before.InputDigest.Data.Value, afterInputDigest = after.InputDigest.Data.Value,
            beforeContext = before.Source.Context.Data.Value, afterContext = after.Source.Context.Data.Value,
            target = target.Data.Value, previousName, newName, referenceCoverageComplete = complete,
            referenceOrigins = References.Select(reference => reference.Origin.Data.Value).ToArray()
        });
    }

    /// <summary>Returns the entire prepared immutable Draft or nothing. Hosts must independently reauthorize an actual write.</summary>
    public CompilerInputManifest? ApplyToDraft(CompilerInputManifest current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current); cancellationToken.ThrowIfCancellationRequested();
        return current.Data.Equals(Before.Data) && current.Source.Data.Equals(Before.Source.Data) ? After : null;
    }
}

public static class ProjectDraftRename
{
    public const int MaximumNameBytes = 4096;
    public const int MaximumReferenceOrigins = 4096;

    public static DraftRenamePreview Preview(PartialSemanticModel model, ProjectLocation target, ContentDigest expectedInput,
        string newName, SourceRevisionContext nextContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(expectedInput);
        ArgumentNullException.ThrowIfNull(newName); ArgumentNullException.ThrowIfNull(nextContext); cancellationToken.ThrowIfCancellationRequested();
        var input = model.Input;
        DraftRenamePreview Refuse(DraftRenameState state) => new(state, null);
        if (!input.InputDigest.Equals(expectedInput)) return Refuse(DraftRenameState.StaleInput);
        if (!target.Context.Equals(input.Source.Context)) return Refuse(DraftRenameState.OutOfScope);
        if (input.Source.Context.Proposal is not { } current || nextContext.Proposal is not { } next ||
            current.ProposalId != next.ProposalId || current.CandidateRevisionId == next.CandidateRevisionId ||
            current.BaseRevision != next.BaseRevision || current.CapturedTargetHead != next.CapturedTargetHead)
            return Refuse(DraftRenameState.InvalidContext);
        try { PlanEntityReference.RequireKnownContext(input.Source.Context); PlanEntityReference.RequireKnownContext(nextContext); }
        catch (UnsupportedSourceException) { return Refuse(DraftRenameState.Unsupported); }
        if (ContextAlreadyKnown(model, nextContext, cancellationToken)) return Refuse(DraftRenameState.ContextConflict);
        if (Encoding.UTF8.GetByteCount(newName) > MaximumNameBytes) return Refuse(DraftRenameState.ResourceLimit);
        // Reuse canonical Unicode validation, including unpaired-surrogate rejection.
        try { CanonicalJson.ValidateUnicode(newName); }
        catch (FormatException) { return Refuse(DraftRenameState.InvalidName); }
        CompilerSemanticInspection inspection;
        try { inspection = new CompilerSemanticInspection(model, cancellationToken); }
        catch (FormatException) { return Refuse(DraftRenameState.ResourceLimit); }
        var symbol = inspection.Inspect(target);
        if (symbol is null || symbol.Symbol.PortId is not null || symbol.Symbol.Status != ValueDecodeStatus.Decoded)
            return Refuse(DraftRenameState.Unavailable);
        var declarations = model.Declarations.Where(item => SourceFields.Identity<EntityId>(item.Data.Value, "entityId") == symbol.Symbol.EntityId).ToArray();
        if (declarations.Length != 1 || declarations[0].Declaration is not { } declaration) return Refuse(DraftRenameState.Unavailable);
        if (declaration.DisplayName == newName) return Refuse(DraftRenameState.NoChange);
        if (model.Declarations.Count != input.Source.Declarations.Value.GetArrayLength() + input.Source.Documents.Count ||
            model.Relations.Count != input.Source.Relations.Value.GetArrayLength()) return Refuse(DraftRenameState.ResourceLimit);
        var references = new List<CompilerBoundReference>();
        foreach (var reference in inspection.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reference.State != ReferenceResolutionState.Resolved || reference.SymbolKey != symbol.Symbol.Key) continue;
            if (references.Count == MaximumReferenceOrigins) return Refuse(DraftRenameState.ResourceLimit);
            references.Add(reference);
        }
        ProjectSource afterSource;
        try { afterSource = DraftSourceTransition.Recontextualize(model, nextContext, symbol.Symbol.EntityId, newName, cancellationToken); }
        catch (DraftSourceLimitException) { return Refuse(DraftRenameState.ResourceLimit); }
        var afterDeclarations = PlanDeclarationReader.Read(afterSource).ToDictionary(item => SourceFields.Identity<EntityId>(item.Data.Value, "entityId"));
        if (model.Declarations.Any(item => item.Status == ValueDecodeStatus.Decoded &&
            afterDeclarations[SourceFields.Identity<EntityId>(item.Data.Value, "entityId")].Status != ValueDecodeStatus.Decoded))
            return Refuse(DraftRenameState.Unsupported);
        var after = new CompilerInputManifest(afterSource, input.Settings);
        cancellationToken.ThrowIfCancellationRequested();
        return new(DraftRenameState.Ready, new(input, after, target, declaration.DisplayName, newName, references, inspection.BindingComplete));
    }

    /// <summary>Forks an admitted committed snapshot into one explicitly supplied local candidate. No server Proposal is created.</summary>
    public static CompilerInputManifest Fork(PartialSemanticModel model, ProposalContext proposal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(proposal); cancellationToken.ThrowIfCancellationRequested();
        if (model.Input.Source.Context.CommittedRevision is not { } basis || proposal.BaseRevision != basis)
            throw new ArgumentException("A Draft fork needs its exact committed base.", nameof(proposal));
        var context = SourceRevisionContext.Candidate(proposal);
        if (ContextAlreadyKnown(model, context, cancellationToken)) throw new ArgumentException("Candidate context is already pinned or materialized.", nameof(proposal));
        if (model.Declarations.Count != model.Input.Source.Declarations.Value.GetArrayLength() + model.Input.Source.Documents.Count ||
            model.Relations.Count != model.Input.Source.Relations.Value.GetArrayLength()) throw new InvalidOperationException("Incomplete decoding cannot supply a Draft transition.");
        var source = DraftSourceTransition.Recontextualize(model, context, null, null, cancellationToken);
        return new(source, model.Input.Settings);
    }

    internal static bool ContextAlreadyKnown(PartialSemanticModel model, SourceRevisionContext next, CancellationToken cancellationToken)
    {
        if (model.Input.HasPinnedContext(next, cancellationToken)) return true;
        foreach (var snapshot in model.BoundSnapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot.Source.Context.Equals(next)) return true;
        }
        // Unresolved typed references still reserve their explicitly named context.
        // Reusing it could silently turn a foreign target/origin into a local one.
        foreach (var item in model.Declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Declaration is RequirementPlanDeclaration requirement && requirement.Target.Context.Equals(next)) return true;
            if (item.Declaration is ProjectTestPlanDeclaration test && test.Assertions.Any(assertion => assertion.Target.Context.Equals(next))) return true;
            if (item.Declaration is not TaskPlanDeclaration task) continue;
            foreach (var port in task.Ports)
                if (port.DefaultValue is { } value)
                    foreach (var child in CompilerValueReferenceTraversal.Values(value, ""))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (child.Value.KnownValue is TypedProjectReference reference &&
                            (reference.Context.Equals(next) || reference.Origin.Context.Equals(next))) return true;
                    }
        }
        foreach (var item in model.Relations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Relation is not { } relation) continue;
            if (relation.Source.Reference.Context.Equals(next) || relation.Target.Reference.Context.Equals(next) ||
                relation.Payload is ConstraintRelationPayload constraint && constraint.PredicateReference.Context.Equals(next)) return true;
        }
        return false;
    }
}

/// <summary>Visits decoded typed contracts only. Opaque data and pinned target snapshots are never searched or rewritten.</summary>
internal static class DraftSourceTransition
{
    internal static ProjectSource Recontextualize(PartialSemanticModel model, SourceRevisionContext next, EntityId? rename,
        string? name, CancellationToken cancellationToken, PlanDeclaration? replacement = null,
        IReadOnlyDictionary<EntityId, PlanDeclaration>? replacements = null)
    {
        var source = model.Input.Source;
        PlanEntityReference.RequireKnownContext(source.Context); PlanEntityReference.RequireKnownContext(next);
        var root = JsonNode.Parse(source.Data.CanonicalText)!.AsObject();
        JsonNode Context() => JsonNode.Parse(next.Data.CanonicalText)!;
        SourceJson Encode(JsonNode node)
        {
            try { return SourceJson.FromObject(node, source.Data.Limits); }
            catch (FormatException) { throw new DraftSourceLimitException(); }
        }
        var declarationIndex = model.Declarations.ToDictionary(item => SourceFields.Identity<EntityId>(item.Data.Value, "entityId"));
        foreach (var document in root["documents"]!.AsArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = document!.AsObject();
            EditDeclaration(node["root"]!.AsObject());
            var digest = node["digest"]!.AsObject();
            node.Remove("digest");
            digest["value"] = ContentDigest.Compute(SourceDocument.DigestDomain, Encode(node).ToCanonicalBytes()).Value;
            node["digest"] = digest;
        }
        foreach (var node in root["declarations"]!.AsArray()) { cancellationToken.ThrowIfCancellationRequested(); EditDeclaration(node!.AsObject()); }
        var relationIndex = model.Relations.ToDictionary(item => SourceFields.Identity<RelationId>(item.Data.Value, "relationId"));
        foreach (var node in root["relations"]!.AsArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relation = node!.AsObject();
            if (relationIndex[RelationId.Parse(relation["relationId"]!.GetValue<string>())].Relation is not { } decoded) continue;
            RebasePlanReference(relation["source"]!["reference"]!.AsObject(), decoded.Source.Reference);
            RebasePlanReference(relation["target"]!["reference"]!.AsObject(), decoded.Target.Reference);
            if (decoded.Payload is ConstraintRelationPayload constraint)
                RebasePlanReference(relation["payload"]!["predicateReference"]!.AsObject(), constraint.PredicateReference);
            // Version/evidence contracts are preserved. Candidate/Scenario Requirement interpretation remains explicit unsupported work.
        }
        root["context"] = Context();
        var byDocument = root["documents"]!.AsArray().ToDictionary(node => node!["documentId"]!.GetValue<string>());
        foreach (var entry in root["manifest"]!["documents"]!.AsArray())
            entry!["digest"]!["value"] = byDocument[entry["documentId"]!.GetValue<string>()]!["digest"]!["value"]!.DeepClone();
        cancellationToken.ThrowIfCancellationRequested();
        return ProjectSourceCodec.Decode(Encode(root).ToCanonicalBytes(), source.Data.Limits).RequireSource();

        void RebasePlanReference(JsonObject node, PlanEntityReference reference)
        {
            if (reference.Context.Equals(source.Context)) node["context"] = Context();
        }
        void EditDeclaration(JsonObject node)
        {
            var id = EntityId.Parse(node["entityId"]!.GetValue<string>());
            var decoded = declarationIndex[id].Declaration;
            var replacementDeclaration = replacement?.EntityId == id ? replacement : replacements?.GetValueOrDefault(id);
            if (replacementDeclaration is not null)
            {
                var imported = JsonNode.Parse(replacementDeclaration.Data.CanonicalText)!.AsObject(); node.Clear();
                foreach (var property in imported) node.Add(property.Key, property.Value?.DeepClone());
                decoded = replacementDeclaration;
            }

            if (id == rename) node["displayName"] = name;
            if (decoded is RequirementPlanDeclaration requirement)
                RebasePlanReference(node["payload"]!["target"]!.AsObject(), requirement.Target);
            if (decoded is ProjectTestPlanDeclaration test)
            {
                var assertions = node["payload"]!["assertions"]!.AsArray().ToDictionary(item => item!["key"]!.GetValue<string>(), StringComparer.Ordinal);
                foreach (var assertion in test.Assertions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RebasePlanReference(assertions[assertion.Key]!["target"]!.AsObject(), assertion.Target);
                }
            }
            if (decoded is not TaskPlanDeclaration task) return;
            var ports = node["payload"]!["ports"]!.AsArray().ToDictionary(port => PortId.Parse(port!["portId"]!.GetValue<string>()));
            foreach (var port in task.Ports)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (port.DefaultValue is { } value) RebaseValueOrigins(ports[port.PortId]!["defaultValue"]!.AsObject(), value, source.Context, next, source.Data.Limits, cancellationToken);
            }
        }

    }
    internal static SourceJson RebaseValueOrigins(SourceJson data, SourceRevisionContext before, SourceRevisionContext next,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decoded = TypedProjectValueCodec.Decode(data);
        if (decoded.Value is not { } value) throw new UnsupportedSourceException("Unsupported typed Scenario value.");
        var node = JsonNode.Parse(data.CanonicalText)!.AsObject();
        RebaseValueOrigins(node, value, before, next, data.Limits, cancellationToken);
        try { return SourceJson.FromObject(node, data.Limits); }
        catch (FormatException) { throw new DraftSourceLimitException(); }
    }

    private static void RebaseValueOrigins(JsonObject root, TypedProjectValue rootValue, SourceRevisionContext before,
        SourceRevisionContext next, SourceCodecLimits limits, CancellationToken cancellationToken)
    {
        Visit(root, rootValue);
        SourceJson Encode(JsonNode node)
        {
            try { return SourceJson.FromObject(node, limits); }
            catch (FormatException) { throw new DraftSourceLimitException(); }
        }
        void Visit(JsonObject node, TypedProjectValue value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (value.KnownValue)
            {
                case TypedProjectReference reference:
                    // Targets and manifests stay pinned; only an exact local declaring origin advances.
                    if (reference.Origin.Context.Equals(before)) node["value"]!["origin"]!["context"] = JsonNode.Parse(next.Data.CanonicalText);
                    break;
                case TypedProjectValue child:
                    Visit(node["value"]!.AsObject(), child); break;
                case OptionalProjectValue { Present: true, Value: { } child }:
                    Visit(node["value"]!["value"]!.AsObject(), child); break;
                case ProjectStructureValue structure:
                    foreach (var field in structure.Fields) Visit(node["value"]![field.Key]!.AsObject(), field.Value);
                    break;
                case ProjectCollectionValue collection:
                    var array = node["value"]!.AsArray();
                    for (var i = 0; i < collection.Items.Count; i++) Visit(array[i]!.AsObject(), collection.Items[i]);
                    for (var i = 0; i < collection.Entries.Count; i++) Visit(array[i]!["value"]!.AsObject(), collection.Entries[i].Value);
                    if (value.Type.Kind == "coglatas.set")
                    {
                        var ordered = array.Select(item => (Node: item!, Key: Encode(item!).CanonicalText))
                            .OrderBy(item => item.Key, Comparer<string>.Create(CanonicalJson.ScalarCompare)).ToArray();
                        array.Clear(); foreach (var item in ordered) array.Add(item.Node);
                    }
                    break;
            }
        }
    }

}

internal sealed class DraftSourceLimitException : Exception
{
    internal DraftSourceLimitException() : base("Draft transition exceeds Source processing bounds.") { }
}

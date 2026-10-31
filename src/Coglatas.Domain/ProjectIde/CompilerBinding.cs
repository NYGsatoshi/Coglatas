using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public static class CoreCompilerRules
{
    public const string Schema = "coglatas.core.schema";
    public const string Declarations = "coglatas.core.declarations";
    public const string Relations = "coglatas.core.relations";
    public const string Constraints = "coglatas.core.constraints";
    public const string Requirements = "coglatas.core.requirements";
    public const string Resources = "coglatas.core.resources";
}

public sealed record CompilerBindingFinding(RelationFindingState State, CompilerDiagnostic Diagnostic);
public sealed record CompilerBoundReference(ProjectLocation Origin, ReferenceResolutionState State, string? SymbolKey);
public sealed record CompilerSymbol(string Key, string Kind, EntityId EntityId, PortId? PortId, SourceRevisionContext Context, ProjectLocation Location, ValueDecodeStatus Status);

/// <summary>A materialized authorized snapshot has to match an explicit input pin before it can supply symbols.</summary>
public sealed record CompilerMaterializedSnapshot
{
    public ExternalSnapshotId SnapshotId { get; }
    public string Version { get; }
    public RelationScopeSnapshot Snapshot { get; }
    public CompilerMaterializedSnapshot(ExternalSnapshotId snapshotId, string version, RelationScopeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _ = snapshotId.ToString(); SnapshotId = snapshotId; Version = version; Snapshot = snapshot;
    }
}

/// <summary>Derived editor-facing model. Full compilation remains the reference; no incremental cache or executable plan.</summary>
public sealed class PartialSemanticModel
{
    public CompilerInputManifest Input { get; }
    public IReadOnlyList<DeclarationDecodeResult> Declarations { get; }
    public IReadOnlyList<RelationDecodeResult> Relations { get; }
    public IReadOnlyList<CompilerSymbol> Symbols { get; }
    public IReadOnlyList<CompilerBoundReference> References { get; }
    public IReadOnlyList<CompilerBindingFinding> Findings { get; }
    public IReadOnlyList<RelationScopeSnapshot> BoundSnapshots { get; }
    public bool BindingComplete => Findings.Count == 0 && Input.SourceCoverageComplete;
    internal PartialSemanticModel(CompilerInputManifest input, IEnumerable<DeclarationDecodeResult> declarations, IEnumerable<RelationDecodeResult> relations,
        IEnumerable<CompilerSymbol> symbols, IEnumerable<CompilerBoundReference> references, IEnumerable<CompilerBindingFinding> findings, IEnumerable<RelationScopeSnapshot> snapshots)
    {
        Input = input; Declarations = Array.AsReadOnly(declarations.ToArray()); Relations = Array.AsReadOnly(relations.ToArray());
        Symbols = Array.AsReadOnly(symbols.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray());
        References = Array.AsReadOnly(references.OrderBy(item => item.Origin.Data.CanonicalText, StringComparer.Ordinal).ToArray());
        Findings = Array.AsReadOnly(findings.OrderBy(item => item.Diagnostic.StageOrdinal).ThenBy(item => item.Diagnostic.RuleId, StringComparer.Ordinal)
            .ThenBy(item => item.Diagnostic.Location.Data.CanonicalText, StringComparer.Ordinal).ThenBy(item => item.Diagnostic.Code, StringComparer.Ordinal).ToArray());
        BoundSnapshots = Array.AsReadOnly(snapshots.ToArray());
    }
}

public static class ProjectSemanticBinder
{
    public static PartialSemanticModel Bind(CompilerInputManifest input, IEnumerable<CompilerMaterializedSnapshot?>? materialized = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
        foreach (var id in new[] { CoreCompilerRules.Schema, CoreCompilerRules.Declarations, CoreCompilerRules.Relations })
            if (!input.Rules.Any(rule => rule.RuleId == id && rule.Version == 1 && rule.Required))
                throw new ArgumentException("Binding requires the locked platform structural rule baseline.");
        var source = input.Source;
        var root = ProjectLocation.Create(source.Documents[0].DocumentId, source.Context);
        var findings = new List<CompilerBindingFinding>(); var references = new List<CompilerBoundReference>(); var symbols = new List<CompilerSymbol>();
        var findingIds = new HashSet<Guid>();
        var snapshots = new List<RelationScopeSnapshot>();
        void Add(string rule, string code, RelationFindingState state, ProjectLocation location)
        {
            var basis = state == RelationFindingState.Invalid
                ? new CompilerEvidenceBasis(CompilerEvidenceKind.ModelProof, [input.InputDigest, source.Digest])
                : new CompilerEvidenceBasis(CompilerEvidenceKind.Missing, [], code);
            var diagnostic = new CompilerDiagnostic(input, rule, 1, rule == CoreCompilerRules.Schema ? 0 : 1, code, location, basis);
            if (findingIds.Add(diagnostic.DiagnosticId.Value)) findings.Add(new(state, diagnostic));
        }
        PartialSemanticModel Empty() => new(input, [], [], symbols, references, findings, snapshots);
        var remainingOperations = input.ExecutionProfile.MaximumOperations;
        var remainingDeclarations = input.ExecutionProfile.MaximumDeclarations - source.Declarations.Value.GetArrayLength() - source.Documents.Count;
        if (!WithinBudget(source.Data.Value, ref remainingOperations) || !WithinNumericProfile(source.Data.Value, input.ExecutionProfile.Numbers) ||
            source.Declarations.Value.GetArrayLength() + source.Documents.Count > input.ExecutionProfile.MaximumDeclarations ||
            source.Relations.Value.GetArrayLength() > input.ExecutionProfile.MaximumRelations)
        {
            Add(CoreCompilerRules.Schema, "coglatas.binding.budget-exhausted", RelationFindingState.Unknown, root); return Empty();
        }
        if (!input.SourceCoverageComplete) Add(CoreCompilerRules.Schema, "coglatas.binding.incomplete-source", RelationFindingState.Unknown, root);
        var schema = new CompilerArtifactIdentity(RelationFields.Child(input.Settings, "schema"));
        if (schema.ArtifactId != ProjectSource.SchemaId || schema.Version != "1")
            Add(CoreCompilerRules.Schema, "coglatas.binding.schema-compatibility", RelationFindingState.Unsupported, root);
        if (ValueFields.HasUnknown(source.Data.Value, "format", "schemaId", "schemaVersion", "canonicalEncodingVersion", "tenantId", "projectId", "context", "documents", "manifest", "declarations", "relations", "extensionDeclarations", "packageLock", "sourceReferences") ||
            ValueFields.HasUnknown(source.Manifest.Data.Value, "documents") ||
            source.Manifest.Data.Value.GetProperty("documents").EnumerateArray().Any(item => ValueFields.HasUnknown(item, "documentId", "schemaId", "schemaVersion", "digest")) ||
            source.Documents.Any(document => ValueFields.HasUnknown(document.Data.Value, "documentId", "logicalName", "schemaId", "schemaVersion", "root", "digest") || ValueFields.HasUnknown(document.Digest.Data.Value, "algorithm", "value")) ||
            source.ExtensionDeclarations.Value.GetArrayLength() != 0 || source.PackageLock.Value.GetArrayLength() != 0)
            Add(CoreCompilerRules.Schema, "coglatas.binding.opaque-source", RelationFindingState.Unsupported, root);
        var supplied = (materialized ?? []).Take(33).ToArray();
        if (supplied.Length > 32 || supplied.Any(item => item is null))
        {
            Add(CoreCompilerRules.Schema, "coglatas.binding.snapshot-budget", RelationFindingState.Unknown, root); return Empty();
        }
        var pins = SourceFields.Array(input.Settings.Value, "authorizedExternalSnapshots").EnumerateArray().ToArray();
        foreach (var item in supplied.Select(item => item!))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pin = pins.SingleOrDefault(pin => SourceFields.Identity<ExternalSnapshotId>(pin, "snapshotId") == item.SnapshotId);
            if (pin.ValueKind == JsonValueKind.Undefined || SourceFields.String(pin, "version") != item.Version ||
                !ContentDigest.Parse(SourceJson.FromElement(pin.GetProperty("digest"))).Equals(item.Snapshot.Source.Digest) ||
                !SourceRevisionContext.Parse(SourceJson.FromElement(pin.GetProperty("context"))).Equals(item.Snapshot.Source.Context))
            {
                Add(CoreCompilerRules.Schema, "coglatas.binding.snapshot-pin-mismatch", RelationFindingState.Invalid, root); continue;
            }
            var snapshot = item.Snapshot;
            if (!snapshot.IsAuthorized) { snapshots.Add(snapshot); continue; }
            remainingDeclarations -= snapshot.Source.Declarations.Value.GetArrayLength() + snapshot.Source.Documents.Count;
            if (remainingDeclarations < 0 || !WithinBudget(snapshot.Source.Data.Value, ref remainingOperations) || !WithinNumericProfile(snapshot.Source.Data.Value, input.ExecutionProfile.Numbers))
            {
                Add(CoreCompilerRules.Schema, "coglatas.binding.snapshot-budget", RelationFindingState.Unknown, root); continue;
            }
            if (SourceFields.Token(pin, "coverage") != "coglatas.complete") snapshot = snapshot with { DeclarationsComplete = false, RelationsComplete = false };
            snapshots.Add(snapshot);
        }
        var local = new RelationScopeSnapshot(source, true, input.SourceCoverageComplete, input.SourceCoverageComplete);
        var declarationSets = snapshots.Prepend(local).Where(item => item.IsAuthorized).Select(item => (Snapshot: item, Results: PlanDeclarationReader.Read(item.Source))).ToArray();
        foreach (var set in declarationSets)
        {
            foreach (var result in set.Results)
            {
                var entity = SourceFields.Identity<EntityId>(result.Data.Value, "entityId");
                var location = ProjectLocation.Create(result.DocumentId, set.Snapshot.Source.Context, entityId: entity);
                symbols.Add(new(Key(set.Snapshot.Source.Context, entity), SourceFields.Token(result.Data.Value, "kind"), entity, null, set.Snapshot.Source.Context, location, result.Status));
                if (result.Declaration is TaskPlanDeclaration task)
                    symbols.AddRange(task.Ports.Select(port => new CompilerSymbol(Key(set.Snapshot.Source.Context, entity, port.PortId), port.Type.Kind,
                        entity, port.PortId, set.Snapshot.Source.Context, ProjectLocation.Create(result.DocumentId, set.Snapshot.Source.Context,
                            "/payload/ports/" + port.PortId, entityId: entity), ValueDecodeStatus.Decoded)));
            }
        }
        var declarations = declarationSets[0].Results;
        var symbolIndex = symbols.GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var result in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entity = SourceFields.Identity<EntityId>(result.Data.Value, "entityId");
            var location = ProjectLocation.Create(result.DocumentId, source.Context, entityId: entity);
            if (result.Status != ValueDecodeStatus.Decoded)
                Add(CoreCompilerRules.Declarations, result.Status == ValueDecodeStatus.Invalid ? "coglatas.binding.invalid-declaration" : "coglatas.binding.unsupported-declaration",
                    result.Status == ValueDecodeStatus.Invalid ? RelationFindingState.Invalid : RelationFindingState.Unsupported, location);
            if (!WithinBudget(result.Data.Value, ref remainingOperations))
            { Add(CoreCompilerRules.Schema, "coglatas.binding.budget-exhausted", RelationFindingState.Unknown, root); return Empty(); }
            if (HasUnboundValueReference(result.Data))
            {
                references.Add(new(location, ReferenceResolutionState.Unsupported, null));
                Add(CoreCompilerRules.Declarations, "coglatas.binding.value-reference-unavailable", RelationFindingState.Unsupported, location);
            }
            if (result.Declaration is RequirementPlanDeclaration requirement)
                Resolve(requirement.Target, ProjectLocation.Create(result.DocumentId, source.Context, "/payload/target", entityId: entity), CoreCompilerRules.Declarations);
            if (result.Declaration is ProjectPlanDeclaration project)
                foreach (var target in project.Roots)
                    Resolve(new PlanEntityReference(SourceJson.FromObject(new { tenantId = source.TenantId.ToString(), projectId = source.ProjectId.ToString(), entityId = target.ToString(), context = source.Context.Data.Value })),
                        ProjectLocation.Create(result.DocumentId, source.Context, "/payload/roots/" + target, entityId: entity), CoreCompilerRules.Declarations);
        }
        var descriptorPins = SourceFields.Array(input.Settings.Value, "descriptors").EnumerateArray().Select(item => new CompilerArtifactIdentity(SourceJson.FromElement(item))).ToArray();
        var relations = TypedRelationReader.Read(source);
        foreach (var decoded in relations)
        {
            var relationId = SourceFields.Identity<RelationId>(decoded.Data.Value, "relationId");
            var location = ProjectLocation.Create(SourceFields.Identity<DocumentId>(decoded.Data.Value, "documentId"), source.Context, relationId: relationId);
            if (decoded.Relation is not { } relation)
            {
                Add(CoreCompilerRules.Relations, decoded.Status == ValueDecodeStatus.Invalid ? "coglatas.binding.invalid-relation" : "coglatas.binding.unsupported-relation",
                    decoded.Status == ValueDecodeStatus.Invalid ? RelationFindingState.Invalid : RelationFindingState.Unsupported, location); continue;
            }
            if (!descriptorPins.Any(pin => pin.ArtifactId == relation.DescriptorId && pin.Version == "1" && pin.Digest.Equals(relation.Descriptor.Digest)))
                Add(CoreCompilerRules.Relations, "coglatas.binding.descriptor-lock", RelationFindingState.Unsupported, location);
            Resolve(relation.Source.Reference, ProjectLocation.Create(relation.DocumentId, source.Context, "/source", relationId: relation.RelationId), CoreCompilerRules.Relations, relation.Source.PortId);
            Resolve(relation.Target.Reference, ProjectLocation.Create(relation.DocumentId, source.Context, "/target", relationId: relation.RelationId), CoreCompilerRules.Relations, relation.Target.PortId);
        }
        foreach (var reference in source.SourceReferences.Value.EnumerateArray())
        {
            var document = SourceFields.Identity<DocumentId>(reference, "documentId");
            if (!source.Manifest.DocumentIds.Contains(document))
            { Add(CoreCompilerRules.Schema, "coglatas.binding.reference-origin", input.SourceCoverageComplete ? RelationFindingState.Invalid : RelationFindingState.Unknown, root); continue; }
            var referenceKey = ContentDigest.Compute("coglatas.source-reference/1", SourceJson.FromElement(reference).ToCanonicalBytes()).Value;
            var location = ProjectLocation.Create(document, source.Context, "/sourceReferences/" + referenceKey);
            if (SourceFields.Token(reference, "kind") != "coglatas.entity-reference" ||
                ValueFields.HasUnknown(reference, "kind", "documentId", "entityId"))
            { Add(CoreCompilerRules.Declarations, "coglatas.binding.unsupported-source-reference", RelationFindingState.Unsupported, location); continue; }
            Resolve(new PlanEntityReference(SourceJson.FromObject(new { tenantId = source.TenantId.ToString(), projectId = source.ProjectId.ToString(),
                entityId = SourceFields.String(reference, "entityId"), context = source.Context.Data.Value })), location, CoreCompilerRules.Declarations);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(input, declarations, relations, symbols, references, findings, snapshots);

        void Resolve(PlanEntityReference reference, ProjectLocation origin, string rule, PortId? port = null)
        {
            ReferenceResolutionState state;
            string? key = null;
            var matches = snapshots.Prepend(local).Where(item => item.Source.Context.Equals(reference.Context)).ToArray();
            if (reference.TenantId != source.TenantId) state = ReferenceResolutionState.OutOfScope;
            else if (matches.Length == 0) state = reference.ProjectId == source.ProjectId ? ReferenceResolutionState.OutOfScope : ReferenceResolutionState.UnknownDueToCoverage;
            else if (matches.Any(item => !item.IsAuthorized)) state = ReferenceResolutionState.OutOfScope;
            else if (matches.Length != 1) state = ReferenceResolutionState.Ambiguous;
            else
            {
                var candidates = symbolIndex.GetValueOrDefault(Key(reference.Context, reference.EntityId, port)) ?? [];
                if (candidates.Length == 0) state = matches[0].DeclarationsComplete ? ReferenceResolutionState.MissingWithinCompleteScope : ReferenceResolutionState.UnknownDueToCoverage;
                else if (candidates.Length != 1) state = ReferenceResolutionState.Ambiguous;
                else if (candidates[0].Status != ValueDecodeStatus.Decoded) state = ReferenceResolutionState.Unsupported;
                else { state = ReferenceResolutionState.Resolved; key = candidates[0].Key; }
            }
            references.Add(new(origin, state, key));
            if (state != ReferenceResolutionState.Resolved)
                Add(rule, "coglatas.binding.reference-" + ResolutionToken(state), state switch
                {
                    ReferenceResolutionState.UnknownDueToCoverage => RelationFindingState.Unknown,
                    ReferenceResolutionState.Unsupported => RelationFindingState.Unsupported, _ => RelationFindingState.Invalid
                }, origin);
        }
    }

    private static bool HasUnboundValueReference(SourceJson data)
    {
        var pending = new Stack<JsonElement>(); pending.Push(data.Value);
        while (pending.TryPop(out var value))
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Object && value.TryGetProperty("state", out _) &&
                    TypedProjectValueCodec.Decode(SourceJson.FromElement(value, data.Limits)).Value?.KnownValue is TypedProjectReference)
                    return true;
                foreach (var member in value.EnumerateObject()) pending.Push(member.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) pending.Push(item);
        }
        return false;
    }
    private static string Key(SourceRevisionContext context, EntityId entity, PortId? port = null) =>
        SourceJson.FromObject(new { context = context.Data.Value, entityId = entity.ToString(), portId = port?.ToString() }).CanonicalText;
    private static string ResolutionToken(ReferenceResolutionState state) => state switch
    {
        ReferenceResolutionState.MissingWithinCompleteScope => "missing", ReferenceResolutionState.UnknownDueToCoverage => "coverage-unknown",
        ReferenceResolutionState.Ambiguous => "ambiguous", ReferenceResolutionState.Unsupported => "unsupported", ReferenceResolutionState.OutOfScope => "out-of-scope",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
    private static bool WithinBudget(JsonElement root, ref int operations)
    {
        var pending = new Stack<JsonElement>(); pending.Push(root);
        while (pending.TryPop(out var value))
        {
            if (--operations < 0) return false;
            if (value.ValueKind == JsonValueKind.Object)
                foreach (var member in value.EnumerateObject()) { operations -= member.Name.Length; pending.Push(member.Value); }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) pending.Push(item);
            else if (value.ValueKind == JsonValueKind.String) operations -= value.GetString()!.Length;
            if (operations < 0) return false;
        }
        return true;
    }
    private static bool WithinNumericProfile(JsonElement root, ExactNumberLimits numbers)
    {
        var pending = new Stack<JsonElement>(); pending.Push(root);
        while (pending.TryPop(out var value))
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Object && value.TryGetProperty("state", out _))
                {
                    var data = SourceJson.FromElement(value);
                    if (TypedProjectValueCodec.Decode(data, numbers).Status == ValueDecodeStatus.Invalid && TypedProjectValueCodec.Decode(data).Status == ValueDecodeStatus.Decoded)
                        return false;
                }
                foreach (var member in value.EnumerateObject()) pending.Push(member.Value);
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) pending.Push(item);
        }
        return true;
    }
}

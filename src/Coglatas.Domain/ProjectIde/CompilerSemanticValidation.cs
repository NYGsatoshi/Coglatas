using System.Globalization;
using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public sealed class CompilerSemanticValidation
{
    public PartialSemanticModel Model { get; }
    public IReadOnlyList<CompilerBindingFinding> Findings { get; }
    public IReadOnlyList<RelationFinding> DeferredChecks { get; }
    public DerivedProjectIr? Ir { get; }
    public bool StructuralClosureValidated => Ir is not null;
    public int LogicalOperationsConsumed { get; }
    private CompilerSemanticValidation(PartialSemanticModel model, IEnumerable<CompilerBindingFinding> findings, IEnumerable<RelationFinding> deferred, DerivedProjectIr? ir, int logicalOperationsConsumed)
    {
        Model = model; LogicalOperationsConsumed = logicalOperationsConsumed; Findings = Array.AsReadOnly(findings.OrderBy(item => item.Diagnostic.Location.Data.CanonicalText, StringComparer.Ordinal)
            .ThenBy(item => item.Diagnostic.Code, StringComparer.Ordinal).ToArray());
        DeferredChecks = Array.AsReadOnly(deferred.OrderBy(item => item.Location.Data.CanonicalText, StringComparer.Ordinal).ThenBy(item => item.Code, StringComparer.Ordinal).ToArray()); Ir = ir;
    }

    public static CompilerSemanticValidation Validate(PartialSemanticModel model, IEnumerable<UnitDefinition?>? units = null,
        IEnumerable<CalendarSnapshot?>? calendars = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model); cancellationToken.ThrowIfCancellationRequested();
        var input = model.Input; var source = input.Source;
        var remaining = input.ExecutionProfile.MaximumOperations - model.LogicalOperationsConsumed;
        var root = ProjectLocation.Create(source.Documents[0].DocumentId, source.Context);
        var findings = model.Findings.ToList(); var deferred = new List<RelationFinding>();
        var ids = findings.Select(item => item.Diagnostic.DiagnosticId.Value).ToHashSet();
        void Add(string code, RelationFindingState state, ProjectLocation location)
        {
            var evidence = state == RelationFindingState.Invalid ? new CompilerEvidenceBasis(CompilerEvidenceKind.ModelProof, [input.InputDigest, source.Digest])
                : new CompilerEvidenceBasis(CompilerEvidenceKind.Missing, [], code);
            var diagnostic = new CompilerDiagnostic(input, CoreCompilerRules.Relations, 1, 2, code, location, evidence);
            if (ids.Add(diagnostic.DiagnosticId.Value)) findings.Add(new(state, diagnostic));
        }
        // Do not invoke later caller-owned supplies after an exhausted binding.
        if (model.Findings.Any(item => item.Diagnostic.Code.Contains("budget", StringComparison.Ordinal)) ||
            !ProjectSemanticBinder.WithinBudget(source.Data.Value, ref remaining, cancellationToken))
        {
            Add("coglatas.semantic.budget-exhausted", RelationFindingState.Unknown, root); return Result(null);
        }
        var semanticIdentity = new CompilerArtifactIdentity(RelationFields.Child(input.Settings, "semantics"));
        var irIdentity = new CompilerArtifactIdentity(RelationFields.Child(input.Settings, "ir"));
        if (semanticIdentity.ArtifactId != "coglatas.semantic-model" || semanticIdentity.Version != "1" || irIdentity.ArtifactId != "coglatas.project-ir" || irIdentity.Version != "1")
            Add("coglatas.semantic.version-compatibility", RelationFindingState.Unsupported, root);
        var capabilities = SourceFields.Array(input.Settings.Value, "featureSet").EnumerateArray().Select(ValueFields.String).ToHashSet(StringComparer.Ordinal);
        foreach (var relation in model.Relations.Select(item => item.Relation).OfType<TypedRelation>())
            if (relation.Descriptor.RequiredCapabilities.Any(capability => !capabilities.Contains(capability)))
                Add("coglatas.semantic.capability-unavailable", RelationFindingState.Unsupported, relation.Location);
        var supplyBudgetExhausted = false;
        if (!ReadSupply(units, 1024, unit => unit.Data, out var unitList) ||
            !ReadSupply(calendars, 128, calendar => calendar.Data, out var calendarList))
        {
            Add(supplyBudgetExhausted ? "coglatas.semantic.budget-exhausted" : "coglatas.semantic.materialization-budget",
                RelationFindingState.Unknown, root); return Result(null);
        }
        if (unitList.Select(item => item.UnitId).Distinct(StringComparer.Ordinal).Count() != unitList.Length)
            Add("coglatas.semantic.unit-ambiguous", RelationFindingState.Invalid, root);
        if (calendarList.Select(item => item.Reference.CalendarId).Distinct(StringComparer.Ordinal).Count() != calendarList.Length)
            Add("coglatas.semantic.calendar-ambiguous", RelationFindingState.Invalid, root);
        var unitPins = SourceFields.Array(input.Settings.Value, "units").EnumerateArray().Select(item => new CompilerArtifactIdentity(SourceJson.FromElement(item))).ToArray();
        foreach (var unit in unitList)
            if (!unitPins.Any(pin => pin.ArtifactId == unit.UnitId && pin.Version == unit.Version.ToString(CultureInfo.InvariantCulture) && pin.Digest.Equals(unit.Digest)))
                Add("coglatas.semantic.unit-pin", RelationFindingState.Invalid, root);
        var calendarPins = SourceFields.Array(input.Settings.Value, "calendars").EnumerateArray().Select(item => new CompilerArtifactIdentity(SourceJson.FromElement(item))).ToArray();
        var zonePins = SourceFields.Array(input.Settings.Value, "timeZones").EnumerateArray().Select(item => new CompilerArtifactIdentity(SourceJson.FromElement(item))).ToArray();
        foreach (var calendar in calendarList)
            if (!calendarPins.Any(pin => pin.ArtifactId == calendar.Reference.CalendarId && pin.Version == calendar.Reference.Version && pin.Digest.Equals(calendar.Reference.RulesDigest)) ||
                !zonePins.Any(pin => pin.ArtifactId == "coglatas.time-zone/" + calendar.TimeZone.TimeZoneId && pin.Version == calendar.TimeZone.Version && pin.Digest.Equals(calendar.TimeZone.RulesDigest)))
                Add("coglatas.semantic.calendar-pin", RelationFindingState.Invalid, root);
        var externalKeys = model.Symbols.Where(symbol => !symbol.Context.Equals(source.Context)).Select(symbol => symbol.Key).ToHashSet(StringComparer.Ordinal);
        if (model.References.Any(reference => reference.SymbolKey is not null && externalKeys.Contains(reference.SymbolKey)))
            Add("coglatas.semantic.external-closure-not-lowered", RelationFindingState.Unsupported, root);
        foreach (var declaration in model.Declarations)
        {
            CheckValues(declaration.Data, ProjectLocation.Create(declaration.DocumentId, source.Context, entityId: SourceFields.Identity<EntityId>(declaration.Data.Value, "entityId")));
            var calendar = declaration.Declaration switch { ProjectPlanDeclaration project => project.Calendar, ResourceBindingDeclaration resource => resource.Calendar, _ => null };
            if (calendar is not null && !calendarList.Any(item => item.Reference.Data.Equals(calendar.Data)))
                Add("coglatas.semantic.calendar-unavailable", RelationFindingState.Unknown, root);
            if (declaration.Declaration is TaskPlanDeclaration task)
                foreach (var port in task.Ports)
                    CheckType(port.Type, ProjectLocation.Create(task.DocumentId, source.Context, "/payload/ports/" + port.PortId, entityId: task.EntityId));
        }
        foreach (var relation in model.Relations)
            CheckValues(relation.Data, ProjectLocation.Create(SourceFields.Identity<DocumentId>(relation.Data.Value, "documentId"), source.Context, relationId: SourceFields.Identity<RelationId>(relation.Data.Value, "relationId")));
        if (model.Declarations.Count != 0)
        {
            remaining -= model.Declarations.Count + model.Relations.Count * 4;
            if (remaining < 0) { Add("coglatas.semantic.budget-exhausted", RelationFindingState.Unknown, root); return Result(null); }
            var graph = RelationGraphValidator.Validate(new(source, true, input.SourceCoverageComplete, input.SourceCoverageComplete), model.BoundSnapshots, unitList,
                new(input.ExecutionProfile.MaximumDeclarations, input.ExecutionProfile.MaximumRelations));
            foreach (var finding in graph.Findings)
            {
                if (finding.Code is "coglatas.relation.evidence-not-evaluated" or "coglatas.relation.constraint-not-evaluated" or "coglatas.relation.resource-snapshot-not-evaluated") deferred.Add(finding);
                else Add(finding.Code, finding.State, finding.Location);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        DerivedProjectIr? ir = null;
        if (model.BindingComplete && findings.Count == 0)
        {
            try { ir = new(model); }
            catch (Exception error) when (error is FormatException or OverflowException)
            { Add("coglatas.semantic.artifact-bound", RelationFindingState.Unknown, root); }
        }
        return Result(ir);
        CompilerSemanticValidation Result(DerivedProjectIr? produced) => new(model, findings, deferred, produced, input.ExecutionProfile.MaximumOperations - Math.Max(0, remaining));

        bool ReadSupply<T>(IEnumerable<T?>? supply, int maximum, Func<T, SourceJson> data, out T[] values) where T : class
        {
            var items = new List<T>(); values = [];
            cancellationToken.ThrowIfCancellationRequested();
            if (remaining <= 0) { supplyBudgetExhausted = true; return false; }
            using var iterator = (supply ?? []).GetEnumerator();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (remaining <= 0) { supplyBudgetExhausted = true; return false; }
                var hasNext = iterator.MoveNext();
                cancellationToken.ThrowIfCancellationRequested();
                if (!hasNext) break;
                if (items.Count == maximum || iterator.Current is not { } item) return false;
                if (!ProjectSemanticBinder.WithinBudget(data(item).Value, ref remaining, cancellationToken))
                { supplyBudgetExhausted = true; return false; }
                items.Add(item);
            }
            values = items.ToArray(); return true;
        }

        void CheckQuantityType(TypeReference type, ProjectLocation location)
        {
            var matches = unitList.Where(unit => unit.UnitId == type.UnitId).ToArray();
            if (matches.Length == 0) Add("coglatas.semantic.unit-unavailable", RelationFindingState.Unknown, location);
            else if (matches.Length != 1) Add("coglatas.semantic.unit-ambiguous", RelationFindingState.Invalid, location);
            else if (!matches[0].Dimension.Equals(type.Dimension)) Add("coglatas.semantic.unit-dimension", RelationFindingState.Invalid, location);
        }
        void CheckType(TypeReference type, ProjectLocation location)
        {
            if (type.Kind == "coglatas.quantity") CheckQuantityType(type, location);
            if (!type.ReferenceKindSupported)
                Add("coglatas.semantic.reference-type-unavailable", RelationFindingState.Unsupported, location);
            if (type.ElementType is { } element) CheckType(element, location);
            if (type.KeyType is { } key) CheckType(key, location);
            if (type.ValueType is { } value) CheckType(value, location);
            foreach (var field in type.Fields) CheckType(field.Type, location);
        }

        void CheckValues(SourceJson data, ProjectLocation location)
        {
            var pending = new Stack<JsonElement>(); pending.Push(data.Value);
            while (pending.TryPop(out var value))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (value.ValueKind == JsonValueKind.Object)
                {
                    TypedProjectValue? typed = null;
                    if (value.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.Object)
                    {
                        // Defaultless ports still declare types. Unsupported declarations
                        // retain their raw data, so validate the descriptor independently.
                        try { CheckType(new TypeReference(SourceJson.FromElement(type, data.Limits)), location); }
                        catch (Exception error) when (error is FormatException or ArgumentException)
                        { Add("coglatas.semantic.type-invalid", RelationFindingState.Invalid, location); }
                        if (value.TryGetProperty("state", out _))
                            typed = TypedProjectValueCodec.Decode(SourceJson.FromElement(value, data.Limits), input.ExecutionProfile.Numbers).Value;
                        if (typed?.KnownValue is WorkingDuration duration && !calendarList.Any(calendar => calendar.Reference.Data.Equals(duration.Calendar.Data)))
                            Add("coglatas.semantic.calendar-unavailable", RelationFindingState.Unknown, location);
                        if (typed?.KnownValue is ZonedCivilTime)
                            Add("coglatas.semantic.zoned-time-not-lowered", RelationFindingState.Unsupported, location);
                    }
                    foreach (var member in value.EnumerateObject())
                        if (member.Name != "basis" || typed?.State != ValueState.Unknown) pending.Push(member.Value);
                }
                else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) pending.Push(item);
            }
        }
    }
}

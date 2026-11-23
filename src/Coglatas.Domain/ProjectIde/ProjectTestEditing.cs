using System.Text.Json;

namespace Coglatas.Domain.ProjectIde;

public enum ProjectTestEditState { Ready, StaleInput, OutOfScope, Unavailable, Unsupported, InvalidContext, ContextConflict, InvalidDefinition, IdentityMismatch, ResourceLimit, NoChange }
public sealed record ProjectTestDefinitionExport(ProjectTestEditState State, SourceJson? Definition, ValueDecodeStatus? DecodeStatus);
public sealed record ProjectTestEditPreview(ProjectTestEditState State, ProjectTestEditPlan? Plan);

/// <summary>An exact local Draft replacement, never an authorization, save or Merge grant.</summary>
public sealed class ProjectTestEditPlan
{
    public CompilerInputManifest Before { get; }
    public CompilerInputManifest After { get; }
    public ProjectLocation Target { get; }
    public SourceJson ImportedDefinition { get; }
    public SourceJson Data { get; }
    internal ProjectTestEditPlan(CompilerInputManifest before, CompilerInputManifest after, ProjectLocation target,
        SourceJson original, SourceJson imported, SourceJson result)
    {
        Before = before; After = after; Target = target; ImportedDefinition = imported;
        ContentDigest Digest(SourceJson value) => ContentDigest.Compute("coglatas.project-test-declaration/1", value.ToCanonicalBytes());
        Data = SourceJson.FromObject(new { format = "coglatas.local-test-edit/1", beforeInputDigest = before.InputDigest.Data.Value,
            afterInputDigest = after.InputDigest.Data.Value, beforeSourceDigest = before.Source.Digest.Data.Value, afterSourceDigest = after.Source.Digest.Data.Value,
            beforeContext = before.Source.Context.Data.Value, afterContext = after.Source.Context.Data.Value, target = target.Data.Value,
            beforeDeclarationDigest = Digest(original).Data.Value, importedDeclarationDigest = Digest(imported).Data.Value, afterDeclarationDigest = Digest(result).Data.Value });
    }
    public CompilerInputManifest? ApplyToDraft(CompilerInputManifest current, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current); cancellationToken.ThrowIfCancellationRequested();
        return current.Data.Equals(Before.Data) && current.Source.Data.Equals(Before.Source.Data) ? After : null;
    }
}

/// <summary>Source AST -> canonical definition export; an explicit import prepares the same Test in a new local candidate.</summary>
public static class ProjectTestEditing
{
    public static ProjectTestDefinitionExport Export(PartialSemanticModel model, ProjectLocation target, ContentDigest expectedInput,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(expectedInput);
        cancellationToken.ThrowIfCancellationRequested();
        var state = Admit(model, target, expectedInput, out var declaration);
        return new(state, state == ProjectTestEditState.Ready ? declaration!.Data : null, state == ProjectTestEditState.Ready ? declaration!.Status : null);
    }

    public static ProjectTestEditPreview Preview(PartialSemanticModel model, ProjectLocation target, ContentDigest expectedInput,
        SourceJson definition, SourceRevisionContext nextContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model); ArgumentNullException.ThrowIfNull(target); ArgumentNullException.ThrowIfNull(expectedInput);
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(nextContext); cancellationToken.ThrowIfCancellationRequested();
        ProjectTestEditPreview Refuse(ProjectTestEditState state) => new(state, null);
        var state = Admit(model, target, expectedInput, out var original);
        if (state != ProjectTestEditState.Ready) return Refuse(state);
        var input = model.Input;
        if (nextContext.Proposal is not { } next) return Refuse(ProjectTestEditState.InvalidContext);
        if (input.Source.Context.CommittedRevision is { } basis)
        {
            if (next.BaseRevision != basis) return Refuse(ProjectTestEditState.InvalidContext);
        }
        else if (input.Source.Context.Proposal is not { } current || current.ProposalId != next.ProposalId ||
            current.CandidateRevisionId == next.CandidateRevisionId || current.BaseRevision != next.BaseRevision ||
            current.CapturedTargetHead != next.CapturedTargetHead) return Refuse(ProjectTestEditState.InvalidContext);
        if (original!.Status == ValueDecodeStatus.Unsupported) return Refuse(ProjectTestEditState.Unsupported);
        if (!VersionOne(original.Data.Value)) return Refuse(ProjectTestEditState.Unsupported);
        var decoded = PlanDeclarationReader.Read(definition, target.DocumentId);
        if (decoded.Status == ValueDecodeStatus.Unsupported) return Refuse(ProjectTestEditState.Unsupported);
        if (decoded.Declaration is not ProjectTestPlanDeclaration replacement) return Refuse(ProjectTestEditState.InvalidDefinition);
        if (replacement.EntityId != target.EntityId || replacement.DocumentId != target.DocumentId ||
            original.Data.Value.TryGetProperty("documentId", out _) != definition.Value.TryGetProperty("documentId", out _))
            return Refuse(ProjectTestEditState.IdentityMismatch);
        if (original.Data.Equals(definition)) return Refuse(ProjectTestEditState.NoChange);
        try
        {
            PlanEntityReference.RequireKnownContext(input.Source.Context); PlanEntityReference.RequireKnownContext(nextContext);
            if (ProjectDraftRename.ContextAlreadyKnown(model, nextContext, cancellationToken) || replacement.Assertions.Any(assertion => assertion.Target.Context.Equals(nextContext)))
                return Refuse(ProjectTestEditState.ContextConflict);
            if (model.Declarations.Count != input.Source.Declarations.Value.GetArrayLength() + input.Source.Documents.Count ||
                model.Relations.Count != input.Source.Relations.Value.GetArrayLength()) return Refuse(ProjectTestEditState.ResourceLimit);
            var afterSource = DraftSourceTransition.Recontextualize(model, nextContext, null, null, cancellationToken, replacement);
            var afterDeclarations = PlanDeclarationReader.Read(afterSource).ToDictionary(item => SourceFields.Identity<EntityId>(item.Data.Value, "entityId"));
            if (model.Declarations.Any(item => item.Status == ValueDecodeStatus.Decoded &&
                afterDeclarations[SourceFields.Identity<EntityId>(item.Data.Value, "entityId")].Status != ValueDecodeStatus.Decoded))
                return Refuse(ProjectTestEditState.Unsupported);
            var after = new CompilerInputManifest(afterSource, input.Settings);
            cancellationToken.ThrowIfCancellationRequested();
            return new(ProjectTestEditState.Ready, new(input, after, target, original.Data, definition, afterDeclarations[replacement.EntityId].Data));
        }
        catch (UnsupportedSourceException) { return Refuse(ProjectTestEditState.Unsupported); }
        catch (DraftSourceLimitException) { return Refuse(ProjectTestEditState.ResourceLimit); }
        catch (FormatException) { return Refuse(ProjectTestEditState.InvalidDefinition); }
        catch (ArgumentException) { return Refuse(ProjectTestEditState.Unavailable); }
    }

    private static ProjectTestEditState Admit(PartialSemanticModel model, ProjectLocation target, ContentDigest expected,
        out DeclarationDecodeResult? declaration)
    {
        declaration = null;
        if (!model.Input.InputDigest.Equals(expected)) return ProjectTestEditState.StaleInput;
        if (!model.Input.Source.Context.Equals(target.Context)) return ProjectTestEditState.OutOfScope;
        if (target.EntityId is not { } id || target.RelationId is not null || target.PropertyPath.Length != 0 || target.TextSpan is not null)
            return ProjectTestEditState.Unavailable;
        if (model.Declarations.Count != model.Input.Source.Declarations.Value.GetArrayLength() + model.Input.Source.Documents.Count ||
            model.Relations.Count != model.Input.Source.Relations.Value.GetArrayLength()) return ProjectTestEditState.ResourceLimit;
        var matches = model.Declarations.Where(item => Match(item.Data.Value, "entityId", id.ToString())).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].DocumentId != target.DocumentId || !Match(matches[0].Data.Value, "kind", "coglatas.project-test"))
            return ProjectTestEditState.Unavailable;
        declaration = matches[0]; return ProjectTestEditState.Ready;
    }
    private static bool Match(JsonElement value, string field, string expected) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(field, out var member) && member.ValueKind == JsonValueKind.String && member.GetString() == expected;
    private static bool VersionOne(JsonElement value) => value.TryGetProperty("schemaVersion", out var version) &&
        version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) && number == 1;
}

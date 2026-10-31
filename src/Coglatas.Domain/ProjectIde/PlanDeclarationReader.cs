using System.Collections.ObjectModel;

namespace Coglatas.Domain.ProjectIde;

/// <summary>A declaration shape result, never a complete Compiler verdict or execution permission.</summary>
public sealed class DeclarationDecodeResult
{
    public ValueDecodeStatus Status { get; }
    public PlanDeclaration? Declaration { get; }
    public SourceJson Data { get; }
    public DocumentId DocumentId { get; }
    internal DeclarationDecodeResult(ValueDecodeStatus status, PlanDeclaration? declaration, SourceJson data, DocumentId document)
    { Status = status; Declaration = declaration; Data = data; DocumentId = document; }
}

public static class PlanDeclarationReader
{
    public static DeclarationDecodeResult Read(SourceJson data, DocumentId document)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            _ = document.ToString();
            if (data.Value.ValueKind != System.Text.Json.JsonValueKind.Object) throw new FormatException("Declaration must be an object.");
            if (data.Value.TryGetProperty("documentId", out _) && SourceFields.Identity<DocumentId>(data.Value, "documentId") != document)
                throw new FormatException("Declaration DocumentId mismatch.");
            PlanDeclaration declaration = SourceFields.Token(data.Value, "kind") switch
            {
                "coglatas.project" => new ProjectPlanDeclaration(data, document),
                "coglatas.task" => new TaskPlanDeclaration(data, document),
                "coglatas.milestone" => new MilestonePlanDeclaration(data, document),
                "coglatas.requirement" => new RequirementPlanDeclaration(data, document),
                "coglatas.resource-binding" => new ResourceBindingDeclaration(data, document),
                "coglatas.constraint" => new ConstraintPlanDeclaration(data, document),
                "coglatas.workflow" => new WorkflowPlanDeclaration(data, document),
                _ => throw new UnsupportedSourceException("Unknown declaration kind.")
            };
            return new(ValueDecodeStatus.Decoded, declaration, data, document);
        }
        catch (UnsupportedSourceException) { return new(ValueDecodeStatus.Unsupported, null, data, document); }
        catch (Exception error) when (error is FormatException or ArgumentException or OverflowException)
        { return new(ValueDecodeStatus.Invalid, null, data, document); }
    }

    public static IReadOnlyList<DeclarationDecodeResult> Read(ProjectSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var results = new List<DeclarationDecodeResult>();
        foreach (var document in source.Documents) results.Add(Read(document.Root.Data, document.DocumentId));
        foreach (var declaration in source.Declarations.Value.EnumerateArray())
        {
            var data = SourceJson.FromElement(declaration, source.Data.Limits);
            var result = Read(data, SourceFields.Identity<DocumentId>(declaration, "documentId"));
            if (result.Declaration is ResourceBindingDeclaration resource && resource.TenantId != source.TenantId)
                result = new(ValueDecodeStatus.Invalid, null, data, result.DocumentId);
            results.Add(result);
        }
        return new ReadOnlyCollection<DeclarationDecodeResult>(results);
    }
}

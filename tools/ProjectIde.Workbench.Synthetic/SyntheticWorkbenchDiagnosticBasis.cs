using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

/// <summary>A bounded view of recorded references, not a reconstructed reason DAG or authority grant.</summary>
public sealed class SyntheticDiagnosticBasisPage
{
    public const int PageSize = 32;
    public CompilerInputId InputId { get; }
    public ContentDigest InputDigest { get; }
    public CompilerResultId ResultId { get; }
    public ContentDigest ResultDigest { get; }
    public CompilerDiagnostic Diagnostic { get; }
    public int Offset { get; }
    public int Total { get; }
    public IReadOnlyList<ContentDigest> Premises { get; }
    public bool HasNext => Offset + Premises.Count < Total;
    public bool LogicalStructureRecorded => false;
    public bool CausalStructureRecorded => false;
    public bool PremiseBodiesResolved => false;

    public static SyntheticDiagnosticBasisPage? Read(CompilerResult result, CompilerDiagnostic diagnostic, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(result); ArgumentNullException.ThrowIfNull(diagnostic);
        if (!result.Diagnostics.Any(item => ReferenceEquals(item, diagnostic)) || offset < 0 || offset % PageSize != 0 ||
            offset > 0 && offset >= diagnostic.Evidence.Premises.Count) return null;
        return new(result, diagnostic, offset);
    }

    private SyntheticDiagnosticBasisPage(CompilerResult result, CompilerDiagnostic diagnostic, int offset)
    {
        InputId = result.Input.InputId; InputDigest = result.Input.InputDigest;
        ResultId = result.ResultId; ResultDigest = result.ResultDigest; Diagnostic = diagnostic;
        Offset = offset; Total = diagnostic.Evidence.Premises.Count;
        Premises = Array.AsReadOnly(diagnostic.Evidence.Premises.Skip(offset).Take(PageSize).ToArray());
    }
}

public sealed partial class SyntheticWorkbenchViewModel
{
    /// <summary>Only the exact currently selected owned diagnostic can supply this local view.</summary>
    public SyntheticDiagnosticBasisPage? ReadDiagnosticBasis(int offset = 0)
    {
        if (_disposed || State != "Ready" || _desired is null || _compilation is null ||
            !ReferenceEquals(Result, _compilation.Result) || SelectedDiagnostic is not { } diagnostic ||
            !Result.Diagnostics.Any(item => ReferenceEquals(item, diagnostic)) ||
            !Result.Input.InputDigest.Equals(_desired.Input.ExpectedInputDigest) ||
            !Result.Input.Source.Data.Equals(Source?.Data)) return null;
        return SyntheticDiagnosticBasisPage.Read(Result, diagnostic, offset);
    }
}

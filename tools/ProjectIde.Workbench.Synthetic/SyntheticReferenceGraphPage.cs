using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

/// <summary>The current original Find References page, rendered as a local reference neighborhood.</summary>
public sealed class SyntheticReferenceGraphPage
{
    public CompilerIndexedSymbol Target { get; }
    public CompilerIndexReferencePage Page { get; }
    public IReadOnlyList<CompilerBoundReference> Edges { get; }
    internal SyntheticReferenceGraphPage(CompilerIndexedSymbol target, CompilerIndexReferencePage page)
    { Target = target; Page = page; Edges = new SyntheticReadOnlyList<CompilerBoundReference>(page.References); }
}

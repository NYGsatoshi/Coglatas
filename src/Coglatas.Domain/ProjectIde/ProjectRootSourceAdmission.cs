namespace Coglatas.Domain.ProjectIde;

/// <summary>Explicit local admission of one retained root input; not current runtime read authority or regional IR coverage.</summary>
public sealed class ProjectRootSourceAdmission
{
    public const int MaximumSourceBytes = 4_194_304;
    internal CoreCompilation Compilation { get; }
    public ProjectSource Source => Compilation.Input.Source;
    public ContentDigest InputDigest => Compilation.Input.InputDigest;
    public ContentDigest ResultDigest => Compilation.Result.ResultDigest;
    private ProjectRootSourceAdmission(CoreCompilation compilation) { Compilation = compilation; }

    public bool IsOwnedBy(CoreCompilation compilation) => ReferenceEquals(Compilation, compilation);

    public static ProjectRootSourceAdmission? Read(CoreCompilation compilation, CompilerResult acceptedResult,
        ContentDigest expectedResult, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation); ArgumentNullException.ThrowIfNull(acceptedResult); ArgumentNullException.ThrowIfNull(expectedResult);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(compilation.Result, acceptedResult) || !acceptedResult.ResultDigest.Equals(expectedResult) ||
            !acceptedResult.Input.Data.Equals(compilation.Input.Data)) return null;
        var source = compilation.Input.Source; var bytes = source.Data.ToCanonicalBytes();
        if (bytes.Length > MaximumSourceBytes || !ContentDigest.Compute(ProjectSource.Format, bytes).Equals(source.Digest) ||
            !ProjectSourceCodec.Decode(bytes).RequireSource().Data.Equals(source.Data)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(compilation);
    }
}

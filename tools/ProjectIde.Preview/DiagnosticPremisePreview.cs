using System.Text;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde;

/// <summary>Original native snapshot metadata; body bytes remain in the separately admitted captured Source frames.</summary>
internal static class DiagnosticPremisePreview
{
    internal static object Export(CoreCompilation compilation, ProjectRootSourceAdmission? rootSource = null)
    {
        long metadataBytes = 0;
        object Capture(CompilerDiagnostic diagnostic, DiagnosticPremiseSnapshotMode mode, int budget)
        {
            var snapshot = rootSource is null
                ? ProjectDiagnosticPremiseSnapshot.Read(compilation, diagnostic, compilation.Result.ResultDigest, mode, budget)
                : ProjectDiagnosticPremiseSnapshot.ReadRoot(rootSource, diagnostic, compilation.Result.ResultDigest, mode, budget);
            metadataBytes += Encoding.UTF8.GetByteCount(snapshot.Data.CanonicalText);
            if (metadataBytes > 4_194_304) throw new FormatException("Diagnostic premise metadata exceeds the offline projection bound.");
            return new { text = snapshot.Data.CanonicalText, digest = snapshot.Digest.Data.Value };
        }
        return new { format = "coglatas.local-diagnostic-premise-projection/1", diagnostics = compilation.Result.Diagnostics.Select(diagnostic => new {
            diagnosticId = diagnostic.DiagnosticId.ToString(),
            manifest = Capture(diagnostic, DiagnosticPremiseSnapshotMode.Manifest, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
            available = Capture(diagnostic, DiagnosticPremiseSnapshotMode.AvailableBodies, ProjectCompilerOutputSnapshot.DefaultBodyBudgetBytes),
            budget = Capture(diagnostic, DiagnosticPremiseSnapshotMode.AvailableBodies, 1)
        }).ToArray() };
    }
}

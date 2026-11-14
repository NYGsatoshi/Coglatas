using System.Text.Json;
using Coglatas.Domain.ProjectIde;

namespace Coglatas.ProjectIde.Workbench.Synthetic;

/// <summary>Fixed offline fixture host configuration; neither binary attestation nor runtime trust.</summary>
public static class SyntheticProjectTestConfiguration
{
    private static SourceJson Json(object value) => SourceJson.Parse(JsonSerializer.Serialize(value));
    public static CompilerArtifactIdentity Evaluator { get; } = new(Json(new
    {
        artifactId = "coglatas.synthetic-project-test-evaluator", version = "1",
        digest = ContentDigest.Compute("coglatas.synthetic-project-test-host/1", Json(new
        { mode = "local-input-defaults", version = 1 }).ToCanonicalBytes()).Data.Value
    }));
}

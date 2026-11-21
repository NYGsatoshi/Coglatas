namespace Coglatas.Domain.ProjectIde.VersionedArtifacts;

/// <summary>Projection of untrusted receipt claims and independently checked bindings; never eligibility.</summary>
public sealed class PrototypeReceiptAssessment
{
    public PrototypeArtifact Receipt { get; }
    public string ExecutionClaim { get; }
    public string CoverageClaim { get; }
    public SourceJson ProducerClaim { get; }
    public IReadOnlyList<string> Provenance { get; }
    public string RunId { get; }
    public bool MaterializationComplete { get; }
    public CompilerFreshness Freshness { get; }
    public CompilerEvidenceKind EvidenceKind => CompilerEvidenceKind.Missing;
    public CompilerVerdict Verdict => CompilerVerdict.Inconclusive;

    private PrototypeReceiptAssessment(PrototypeArtifact receipt, bool complete, CompilerFreshness freshness)
    {
        Receipt = receipt; MaterializationComplete = complete; Freshness = freshness;
        var payload = receipt.Data.Value.GetProperty("payload");
        ExecutionClaim = SourceFields.String(payload, "execution");
        CoverageClaim = SourceFields.String(payload, "coverage");
        ProducerClaim = SourceJson.FromElement(payload.GetProperty("producerClaim"));
        RunId = SourceFields.String(payload.GetProperty("captureContext"), "runId");
        Provenance = Array.AsReadOnly(payload.GetProperty("provenance").EnumerateArray().Select(ValueFields.String).ToArray());
    }

    /// <summary>
    /// Requires the existing exact Source/input/manifest/set/receipt bindings. Freshness describes
    /// only caller-supplied desired Source and run equality, not current authorization or producer trust.
    /// A complete body, successful execution claim or candidate producer name cannot supply proof.
    /// </summary>
    public static PrototypeReceiptAssessment Assess(PrototypeArtifact receipt, CompilerInputManifest input,
        PrototypeArtifact association, PrototypeArtifact manifest, PrototypeArtifact actual,
        ArtifactSourcePin? desiredSource = null, string? desiredRunId = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var complete = ArtifactAssembly.BindReceipt(receipt, association, manifest, actual);
        if (ArtifactAssembly.Associate(input, manifest).Identity != association.Identity)
            throw new FormatException("Receipt does not bind the exact compiler input.");
        var runId = SourceFields.String(receipt.Data.Value.GetProperty("payload").GetProperty("captureContext"), "runId");
        var stale = (desiredSource is not null && !desiredSource.Data.Equals(receipt.Source!.Data)) ||
            (desiredRunId is not null && desiredRunId != runId);
        var freshness = stale ? CompilerFreshness.Stale : desiredSource is null || desiredRunId is null
            ? CompilerFreshness.Undetermined : CompilerFreshness.Current;
        return new(receipt, complete, freshness);
    }
}

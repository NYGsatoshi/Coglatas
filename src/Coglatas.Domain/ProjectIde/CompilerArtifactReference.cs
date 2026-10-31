namespace Coglatas.Domain.ProjectIde;

public sealed class CompilerArtifactReference
{
    public string Role { get; }
    public ContentDigest InputDigest { get; }
    public ContentDigest Digest { get; }
    public SourceJson Data { get; }
    public CompilerArtifactReference(string role, string schemaId, int version, ContentDigest inputDigest, ContentDigest digest)
    {
        ArgumentNullException.ThrowIfNull(inputDigest); ArgumentNullException.ThrowIfNull(digest);
        if (!ValueFields.IsToken(role) || !ValueFields.IsToken(schemaId) || version <= 0) throw new ArgumentException("Invalid artifact identity.");
        Role = role; InputDigest = inputDigest; Digest = digest;
        Data = SourceJson.FromObject(new { role, schemaId, version, inputDigest = inputDigest.Data.Value, digest = digest.Data.Value, coverage = "coglatas.complete" });
    }
}

using System.Security.Cryptography;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Version 8 identities retain their complete derivation digest; invocation IDs remain separate.</summary>
public sealed record CompilerInputId
{
    public Guid Value { get; }
    public ContentDigest DerivationDigest { get; }
    internal CompilerInputId(ContentDigest inputDigest) => (Value, DerivationDigest) = CompilerIdentity.Derive("CompilerInputId", inputDigest.Data);
    public static CompilerInputId Derive(ContentDigest inputDigest) { ArgumentNullException.ThrowIfNull(inputDigest); return new(inputDigest); }
    public override string ToString() => Value.ToString("D");
}

public sealed record CompilerResultId
{
    public Guid Value { get; }
    public ContentDigest DerivationDigest { get; }
    internal CompilerResultId(CompilerInputId input, ContentDigest resultDigest) =>
        (Value, DerivationDigest) = CompilerIdentity.Derive("CompilerResultId", SourceJson.FromObject(new object[] { input.ToString(), resultDigest.Data.Value }));
    public static CompilerResultId Derive(CompilerInputId input, ContentDigest resultDigest)
    { ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(resultDigest); return new(input, resultDigest); }
    public override string ToString() => Value.ToString("D");
}

public sealed record DiagnosticId
{
    public Guid Value { get; }
    public ContentDigest DerivationDigest { get; }
    internal DiagnosticId(SourceJson components) => (Value, DerivationDigest) = CompilerIdentity.Derive("DiagnosticId", components);
    public override string ToString() => Value.ToString("D");
}

internal static class CompilerIdentity
{
    internal static (Guid Id, ContentDigest Digest) Derive(string type, SourceJson components)
    {
        var tuple = SourceJson.FromObject(new object[] { "coglatas.id/" + type + "/v1", 1, components.Value });
        var bytes = SHA256.HashData(tuple.ToCanonicalBytes());
        var digest = ContentDigest.Parse(SourceJson.FromObject(new { algorithm = ContentDigest.Algorithm, value = Convert.ToHexStringLower(bytes) }));
        bytes[6] = (byte)((bytes[6] & 15) | 128); bytes[8] = (byte)((bytes[8] & 63) | 128);
        return (new Guid(bytes.AsSpan(0, 16), true), digest);
    }
}

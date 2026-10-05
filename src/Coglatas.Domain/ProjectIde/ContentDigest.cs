using System.Security.Cryptography;
using System.Text;

namespace Coglatas.Domain.ProjectIde;

/// <summary>Content integrity only; neither a domain UUID nor authorization evidence.</summary>
public sealed record ContentDigest
{
    public const string Algorithm = "sha-256";
    public string Value { get; }
    public SourceJson Data { get; }

    private ContentDigest(string value, SourceJson data) { Value = value; Data = data; }

    public static ContentDigest Parse(SourceJson data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (SourceFields.String(data.Value, "algorithm") != Algorithm)
            throw new UnsupportedSourceException("Unsupported digest algorithm.");
        var value = SourceFields.String(data.Value, "value");
        if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new FormatException("A SHA-256 digest is 64 lowercase hexadecimal digits.");
        return new(value, data);
    }

    public static ContentDigest Compute(string domainTag, ReadOnlySpan<byte> canonicalBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainTag);
        if (domainTag.Contains('\0')) throw new ArgumentException("Digest domain cannot contain NUL.", nameof(domainTag));
        CanonicalJson.ValidateUnicode(domainTag);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(domainTag));
        hash.AppendData([0]);
        hash.AppendData(canonicalBytes);
        var value = Convert.ToHexStringLower(hash.GetHashAndReset());
        return Parse(SourceJson.FromObject(new { algorithm = Algorithm, value }));
    }
}

public sealed class UnsupportedSourceException(string message) : Exception(message);

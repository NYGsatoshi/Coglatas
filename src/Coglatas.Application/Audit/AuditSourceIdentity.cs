using System.Security.Cryptography;
using System.Text;
using Coglatas.Domain.Enums;

namespace Coglatas.Application.Audit;

/// <summary>
/// Produces a stable opaque identity for an already-authorized evidence source.
/// The identifier is for grouping/duplicate detection only; it never grants access.
/// </summary>
public static class AuditSourceIdentity
{
    public static string Create(ArtifactEvidenceSourceKind sourceKind, string sourceReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReference);

        var canonicalReference = sourceReference.Trim();
        if (sourceKind != ArtifactEvidenceSourceKind.WebSnapshot &&
            Guid.TryParse(canonicalReference, out var sourceGuid) && sourceGuid != Guid.Empty)
        {
            canonicalReference = sourceGuid.ToString("D");
        }

        var identity = $"{sourceKind}\n{canonicalReference}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"src_{Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant()}";
    }
}

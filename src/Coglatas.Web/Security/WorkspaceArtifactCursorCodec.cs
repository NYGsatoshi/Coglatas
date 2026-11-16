using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Coglatas.Application.Artifacts;
using Microsoft.AspNetCore.DataProtection;

namespace Coglatas.Web.Security;

public sealed class WorkspaceArtifactCursorCodec(IDataProtectionProvider provider) : IWorkspaceArtifactCursorCodec
{
    private readonly IDataProtector _protector = provider.CreateProtector("Coglatas.WorkspaceArtifacts.Cursor.v1");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 8,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public string Encode(WorkspaceArtifactCursor cursor) => _protector.Protect(JsonSerializer.Serialize(cursor, JsonOptions));

    public bool TryDecode(string token, out WorkspaceArtifactCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) return false;
        try
        {
            cursor = JsonSerializer.Deserialize<WorkspaceArtifactCursor>(_protector.Unprotect(token), JsonOptions);
            return cursor is not null;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return false;
        }
    }
}

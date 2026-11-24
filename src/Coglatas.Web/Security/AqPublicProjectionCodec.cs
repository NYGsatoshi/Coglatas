using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Application.Forms.PublicSurveys;
using Microsoft.AspNetCore.DataProtection;

namespace Coglatas.Web.Security;

public sealed class AqPublicProjectionCodec(IDataProtectionProvider provider) : IAqPublicProjectionCodec
{
    private readonly IDataProtector _entryProtector = provider.CreateProtector("Coglatas.Aq.PublicEntry.v1");
    private readonly IDataProtector _cursorProtector = provider.CreateProtector("Coglatas.Aq.PublicProgramCursor.v1");

    public string ProtectEntry(string code) => _entryProtector.Protect(code);

    public string? UnprotectEntry(string protectedCode)
    {
        try
        {
            return _entryProtector.Unprotect(protectedCode);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
    }

    public string ProtectCursor(AqProgramCursor cursor) => _cursorProtector.Protect(JsonSerializer.Serialize(cursor));

    public AqProgramCursor? UnprotectCursor(string protectedCursor)
    {
        try
        {
            return JsonSerializer.Deserialize<AqProgramCursor>(_cursorProtector.Unprotect(protectedCursor));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
    }
}

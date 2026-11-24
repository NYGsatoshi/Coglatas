using System.Security.Cryptography;
using System.Text.Json;
using Coglatas.Application.Forms.PublicSurveys;
using Microsoft.AspNetCore.DataProtection;

namespace Coglatas.Web.Security;

public sealed class AqPublicProjectionCodec(IDataProtectionProvider provider) : IAqPublicProjectionCodec
{
    private readonly IDataProtector entryProtector = provider.CreateProtector("Coglatas.Aq.PublicEntry.v1");
    private readonly IDataProtector cursorProtector = provider.CreateProtector("Coglatas.Aq.PublicProgramCursor.v1");

    public string ProtectEntry(string code) => entryProtector.Protect(code);

    public string? UnprotectEntry(string protectedCode)
    {
        try
        {
            return entryProtector.Unprotect(protectedCode);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public string ProtectCursor(AqProgramCursor cursor) => cursorProtector.Protect(JsonSerializer.Serialize(cursor));

    public AqProgramCursor? UnprotectCursor(string protectedCursor)
    {
        try
        {
            return JsonSerializer.Deserialize<AqProgramCursor>(cursorProtector.Unprotect(protectedCursor));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

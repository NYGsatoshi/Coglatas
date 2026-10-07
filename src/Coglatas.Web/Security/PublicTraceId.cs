using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Coglatas.Web.Security;

/// <summary>
/// Lossless public correlation representation. Short Base64URL groups distinguish
/// opaque trace bytes from contiguous personal-number strings. Internal Activity
/// and logging identities are unchanged; this is not a redaction operation.
/// </summary>
public static class PublicTraceId
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    public static string ForResponse(HttpContext context) =>
        Encode(Activity.Current?.Id ?? context.TraceIdentifier);

    public static string Encode(string value)
    {
        var encoded = WebEncoders.Base64UrlEncode(StrictUtf8.GetBytes(value));
        const int groupLength = 8;
        var groups = Enumerable.Range(0, (encoded.Length + groupLength - 1) / groupLength)
            .Select(index => encoded.Substring(index * groupLength,
                Math.Min(groupLength, encoded.Length - index * groupLength)));
        return "trace-v1:" + string.Join(".", groups);
    }
}

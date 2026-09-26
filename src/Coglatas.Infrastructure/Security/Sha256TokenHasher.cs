using System.Security.Cryptography;
using System.Text;
using Coglatas.Application.Common.Interfaces;

namespace Coglatas.Infrastructure.Security;

public sealed class Sha256TokenHasher : ITokenHasher
{
    public string HashToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
        return Convert.ToHexString(bytes);
    }
}

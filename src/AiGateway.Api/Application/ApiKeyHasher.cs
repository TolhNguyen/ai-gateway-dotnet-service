using System.Security.Cryptography;
using System.Text;

namespace AiGateway.Api.Application;

public sealed class ApiKeyHasher
{
    public string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(bytes);
    }

    public bool Verify(string value, string expectedHash)
    {
        var actual = Hash(value);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual),
            Encoding.UTF8.GetBytes(expectedHash));
    }
}

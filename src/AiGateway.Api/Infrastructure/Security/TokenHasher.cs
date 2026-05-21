using System.Security.Cryptography;
using System.Text;

namespace AiGateway.Api.Infrastructure.Security;

public sealed class TokenHasher
{
    public string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Constant-time compare.</summary>
    public bool Verify(string raw, string expectedHash)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(raw)),
            Encoding.UTF8.GetBytes(expectedHash));

    /// <summary>Cryptographically random token. Format: aigw_&lt;32 url-safe chars&gt;.</summary>
    public string Generate(string prefix = "aigw")
    {
        var raw = RandomNumberGenerator.GetBytes(24);
        var b64 = Convert.ToBase64String(raw)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"{prefix}_{b64}";
    }

    public string FingerprintHex(string raw)
    {
        // Used for dedup detection on user API keys. Same algorithm, different name to keep intent clear.
        return Hash(raw);
    }
}

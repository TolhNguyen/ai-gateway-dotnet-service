using System.Security.Cryptography;
using System.Text;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Infrastructure.Security;

public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string encryptedText);
}

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private readonly byte[] _key;

    public AesGcmSecretProtector(IOptions<AiGatewayOptions> options)
    {
        var base64 = options.Value.EncryptionKeyBase64;

        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new InvalidOperationException("AiGateway:EncryptionKeyBase64 is missing. Generate with: openssl rand -base64 32");
        }

        _key = Convert.FromBase64String(base64);

        if (_key.Length != 32)
        {
            throw new InvalidOperationException("AiGateway:EncryptionKeyBase64 must decode to exactly 32 bytes (AES-256-GCM).");
        }
    }

    public string Protect(string plainText)
    {
        var nonce       = RandomNumberGenerator.GetBytes(12);
        var plainBytes  = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag         = new byte[16];

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var output = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce,       0, output, 0,                          nonce.Length);
        Buffer.BlockCopy(tag,         0, output, nonce.Length,               tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, output, nonce.Length + tag.Length,  cipherBytes.Length);
        return Convert.ToBase64String(output);
    }

    public string Unprotect(string encryptedText)
    {
        var bytes = Convert.FromBase64String(encryptedText);
        if (bytes.Length < 12 + 16)
            throw new InvalidOperationException("Invalid encrypted payload.");

        var nonce      = bytes[..12];
        var tag        = bytes[12..28];
        var cipherText = bytes[28..];
        var plainBytes = new byte[cipherText.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, cipherText, tag, plainBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }
}

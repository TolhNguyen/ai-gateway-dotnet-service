using System.Security.Cryptography;
using AiGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace AiGateway.Api.Infrastructure.Security;

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private readonly byte[] _key;

    public AesGcmSecretProtector(IOptions<AiGatewayOptions> options)
    {
        var base64 = options.Value.EncryptionKeyBase64;
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new InvalidOperationException("AiGateway:EncryptionKeyBase64 is required");
        }

        _key = Convert.FromBase64String(base64);

        if (_key.Length != 32)
        {
            throw new InvalidOperationException("AiGateway:EncryptionKeyBase64 must decode to 32 bytes for AES-256-GCM");
        }
    }

    public string Protect(string plainText)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        var output = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, output, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, output, nonce.Length + tag.Length, ciphertext.Length);

        return Convert.ToBase64String(output);
    }

    public string Unprotect(string encryptedText)
    {
        var input = Convert.FromBase64String(encryptedText);

        if (input.Length < 12 + 16)
        {
            throw new InvalidOperationException("Invalid encrypted secret payload");
        }

        var nonce = input[..12];
        var tag = input[12..28];
        var ciphertext = input[28..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}

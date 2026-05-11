namespace ElectCrm.Infrastructure.Features.Persons;

using System.Security.Cryptography;
using System.Text;
using ElectCrm.Application.Features.Persons;
using Microsoft.Extensions.Configuration;

public sealed class PersonHashingService : IPersonHashingService
{
    private readonly byte[] _key;

    public PersonHashingService(IConfiguration configuration)
    {
        var keyBase64 = configuration["Encryption:PersonKey"]
            ?? throw new InvalidOperationException(
                "Encryption:PersonKey is not configured. " +
                "Set it with: dotnet user-secrets set \"Encryption:PersonKey\" \"<base64-32-bytes>\" " +
                "--project src/ElectCrm.Presentation");

        _key = Convert.FromBase64String(keyBase64);

        if (_key.Length != 32)
            throw new InvalidOperationException("Encryption:PersonKey must be a 32-byte (256-bit) base64-encoded value.");
    }

    public string HashValue(string normalisedValue)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalisedValue));
        return Convert.ToHexStringLower(bytes);
    }

    public string EncryptValue(string plaintext)
    {
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize]; // 12 bytes
        RandomNumberGenerator.Fill(nonce);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize]; // 16 bytes

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Store: nonce (12) + tag (16) + ciphertext, all base64
        var combined = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(combined, 0);
        tag.CopyTo(combined, nonce.Length);
        ciphertext.CopyTo(combined, nonce.Length + tag.Length);

        return Convert.ToBase64String(combined);
    }

    public string DecryptValue(string ciphertext)
    {
        var combined = Convert.FromBase64String(ciphertext);

        var nonce = combined[..12];
        var tag = combined[12..28];
        var encrypted = combined[28..];
        var plaintext = new byte[encrypted.Length];

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Decrypt(nonce, encrypted, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}

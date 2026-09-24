using System.Security.Cryptography;
using System.Text;
using VMS.Shared.Files;

namespace VMS.Modules.Core.Files;

/// <summary>
/// AES-256-GCM. Layout: <c>"VMSF" | version(1) | nonce(12) | tag(16) | ciphertext</c>. The storage key is
/// bound in as associated data, so a file copied or swapped to another path fails to open instead of
/// showing someone else's document, and any change to the bytes on disk is caught by the tag.
/// </summary>
internal static class FileCrypto
{
    private static readonly byte[] Magic = "VMSF"u8.ToArray();
    private const byte Version = 1;
    private const int NonceSize = 12, TagSize = 16;
    private static readonly int HeaderSize = Magic.Length + 1 + NonceSize + TagSize;

    public static byte[] Encrypt(byte[] plain, byte[] key, string storageKey)
    {
        var output = new byte[HeaderSize + plain.Length];
        Magic.CopyTo(output, 0);
        output[Magic.Length] = Version;

        var nonce = output.AsSpan(Magic.Length + 1, NonceSize);
        var tag = output.AsSpan(Magic.Length + 1 + NonceSize, TagSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(HeaderSize), tag, Encoding.UTF8.GetBytes(storageKey));
        return output;
    }

    public static byte[] Decrypt(byte[] blob, byte[] key, string storageKey)
    {
        if (blob.Length < HeaderSize || !blob.AsSpan(0, Magic.Length).SequenceEqual(Magic) || blob[Magic.Length] != Version)
            throw new FileIntegrityException("The stored file is not in the expected format.");

        var plain = new byte[blob.Length - HeaderSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                blob.AsSpan(Magic.Length + 1, NonceSize),
                blob.AsSpan(HeaderSize),
                blob.AsSpan(Magic.Length + 1 + NonceSize, TagSize),
                plain,
                Encoding.UTF8.GetBytes(storageKey));
        }
        catch (CryptographicException ex)
        {
            throw new FileIntegrityException("The stored file has been altered or does not belong at this location.", ex);
        }
        return plain;
    }
}

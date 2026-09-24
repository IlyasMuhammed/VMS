using System.Security.Cryptography;
using System.Text;

namespace VMS.Modules.Core.Files;

/// <summary>Configuration section <c>FileStorage</c>.</summary>
public sealed class FileStorageOptions
{
    public const string Section = "FileStorage";

    /// <summary>
    /// Folder the files live in, outside the database and outside the web root. Required outside
    /// Development. Back it up with the database: the two belong together.
    /// </summary>
    public string? RootPath { get; set; }

    /// <summary>Largest file accepted, in bytes (default 10 MB). A document type may ask for less, never more.</summary>
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Base64 of a 256-bit key that encrypts files at rest. Required outside Development, where a fixed
    /// key is used so files stay readable across restarts. Lose this key and the files are unreadable:
    /// keep it with the same care as the database backups. Generate one with
    /// <c>[Convert]::ToBase64String((1..32 | % { Get-Random -Max 256 }))</c> or any 32 random bytes.
    /// </summary>
    public string? EncryptionKey { get; set; }

    private static readonly byte[] DevelopmentKey = SHA256.HashData(Encoding.UTF8.GetBytes("VMS development file key - never use outside a developer machine"));

    /// <summary>Stops the application starting with storage that would lose or expose files.</summary>
    public void Validate(bool isDevelopment)
    {
        if (MaxBytes < 1) throw new InvalidOperationException("FileStorage:MaxBytes must be positive.");

        if (!isDevelopment && string.IsNullOrWhiteSpace(RootPath))
            throw new InvalidOperationException("FileStorage:RootPath must be set: uploaded files need a folder outside the application.");
        if (!isDevelopment && string.IsNullOrWhiteSpace(EncryptionKey))
            throw new InvalidOperationException("FileStorage:EncryptionKey must be set: files are encrypted at rest and there is no default key outside Development.");

        _ = ResolveKey(isDevelopment);
    }

    public byte[] ResolveKey(bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(EncryptionKey))
            return isDevelopment ? DevelopmentKey : throw new InvalidOperationException("FileStorage:EncryptionKey is not set.");

        byte[] key;
        try { key = Convert.FromBase64String(EncryptionKey); }
        catch (FormatException) { throw new InvalidOperationException("FileStorage:EncryptionKey must be base64."); }

        return key.Length == 32 ? key : throw new InvalidOperationException("FileStorage:EncryptionKey must be 32 bytes (256 bits) once decoded.");
    }
}

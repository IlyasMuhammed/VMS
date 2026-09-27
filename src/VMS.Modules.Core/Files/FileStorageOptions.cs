using System.Security.Cryptography;
using System.Text;

namespace VMS.Modules.Core.Files;

/// <summary>Which <see cref="IFileStore"/> is registered for <c>FileStorage:Provider</c>. Only <see cref="Local"/>
/// is implemented today; <see cref="Blob"/> is reserved so switching to it later is a config change, not a
/// rewrite — everything that stores or reads a file already goes through <see cref="IFileStore"/>, never a path.</summary>
public static class FileStorageProviders
{
    public const string Local = "Local";
    public const string Blob = "Blob";
    public static readonly string[] All = [Local, Blob];
}

/// <summary>Configuration section <c>FileStorage</c>.</summary>
public sealed class FileStorageOptions
{
    public const string Section = "FileStorage";

    /// <summary>Which backend stores the files: <see cref="FileStorageProviders.Local"/> (the only one working
    /// right now) or <see cref="FileStorageProviders.Blob"/> (reserved — there is no blob account to point it at
    /// yet, so choosing it fails fast at startup instead of silently falling back to disk).</summary>
    public string Provider { get; set; } = FileStorageProviders.Local;

    /// <summary>
    /// Folder the files live in, outside the database and outside the web root. Required outside
    /// Development. Back it up with the database: the two belong together. Local provider only.
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
        if (!FileStorageProviders.All.Contains(Provider, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"FileStorage:Provider must be one of: {string.Join(", ", FileStorageProviders.All)}.");
        if (string.Equals(Provider, FileStorageProviders.Blob, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FileStorage:Provider is set to Blob, but no blob storage account is available yet. Set it to Local for now.");

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

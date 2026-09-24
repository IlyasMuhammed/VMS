using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VMS.Shared.Auditing;
using VMS.Shared.Common;
using VMS.Shared.Exceptions;
using VMS.Shared.Files;

namespace VMS.Modules.Core.Files;

/// <summary>Stores files on a local or network folder, encrypted, under generated names in dated folders by owner.</summary>
internal sealed partial class LocalFileStore(
    IOptions<FileStorageOptions> options,
    IHostEnvironment environment,
    IVirusScanner scanner,
    IFileScanAlert alert,
    IAuditContext audit,
    ITenantContext tenantContext,
    ILogger<LocalFileStore> logger) : IFileStore
{
    private readonly FileStorageOptions _options = options.Value;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex OwnerPart();

    // {tenant}/{yyyy}/{MM}/{ownerType}/{ownerId}/{generated name}. Anything else is not one of ours.
    [GeneratedRegex(@"^(?<tenant>[0-9a-f]{32})/\d{4}/\d{2}/[A-Za-z0-9_-]{1,64}/[A-Za-z0-9_-]{1,64}/[0-9a-f]{32}$")]
    private static partial Regex KeyPattern();

    private string Root => Path.GetFullPath(_options.RootPath is { Length: > 0 } configured
        ? configured
        : Path.Combine(environment.ContentRootPath, "App_Data", "files"));

    public async Task<StoredFile> SaveAsync(FileUpload upload, CancellationToken cancellationToken = default)
    {
        if (!OwnerPart().IsMatch(upload.Owner.OwnerType) || !OwnerPart().IsMatch(upload.Owner.OwnerId))
            throw new ArgumentException("A file owner is a type and an id made of letters, digits, hyphen or underscore.", nameof(upload));

        var tenant = upload.TenantId ?? tenantContext.TenantId;
        if (tenant == Guid.Empty) throw new InvalidOperationException("No tenant to store the file for.");

        var originalName = CleanName(upload.FileName);
        var limit = Math.Min(upload.Rules.MaxBytes ?? _options.MaxBytes, _options.MaxBytes);

        var content = await ReadAsync(upload.Content, limit, cancellationToken);
        if (content.Length == 0) throw new BadRequestException("The file is empty.");

        var kind = FileTypeSniffer.Detect(content.AsSpan(0, Math.Min(content.Length, 16)), originalName);
        if (kind == FileKinds.None || !upload.Rules.Allowed.HasFlag(kind))
            throw new BadRequestException($"Only {FileTypeSniffer.Describe(upload.Rules.Allowed)} files are accepted.");

        var scan = await scanner.ScanAsync(content, originalName, cancellationToken);
        if (!scan.IsClean)
        {
            await AlertAsync(new FileThreat(tenant, originalName, scan.Threat ?? "unknown", upload.Owner.OwnerType, upload.Owner.OwnerId, audit.Actor?.UserName));
            throw new BadRequestException("The file was rejected because it may contain a virus. The administrator has been notified.");
        }

        var now = DateTime.UtcNow;
        var storageKey = string.Create(CultureInfo.InvariantCulture,
            $"{tenant:N}/{now:yyyy}/{now:MM}/{upload.Owner.OwnerType}/{upload.Owner.OwnerId}/{Guid.NewGuid():N}");
        var path = PathFor(storageKey);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, FileCrypto.Encrypt(content, _options.ResolveKey(environment.IsDevelopment()), storageKey), cancellationToken);
        File.Move(temporary, path);   // the name appears only once the whole file is on disk

        return new StoredFile(
            storageKey,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            content.Length,
            FileTypeSniffer.ContentType(kind),
            originalName,
            now);
    }

    public async Task<Stream> OpenReadAsync(Guid tenantId, string storageKey, CancellationToken cancellationToken = default)
    {
        var path = PathFor(storageKey, tenantId);
        if (!File.Exists(path)) throw new NotFoundException("The file was not found.");

        var stored = await File.ReadAllBytesAsync(path, cancellationToken);
        try
        {
            return new MemoryStream(FileCrypto.Decrypt(stored, _options.ResolveKey(environment.IsDevelopment()), storageKey), writable: false);
        }
        catch (FileIntegrityException)
        {
            logger.LogError("Stored file {StorageKey} failed its integrity check.", storageKey);
            throw;
        }
    }

    public Task<bool> ExistsAsync(Guid tenantId, string storageKey)
    {
        // A key that is not this tenant's, or not ours at all, is simply not a file this tenant has.
        try { return Task.FromResult(File.Exists(PathFor(storageKey, tenantId))); }
        catch (NotFoundException) { return Task.FromResult(false); }
    }

    public Task DeleteAsync(Guid tenantId, string storageKey)
    {
        var path = PathFor(storageKey, tenantId);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>The full path for a key we issued, for the tenant it belongs to. Anything else is refused before the disk is touched.</summary>
    private string PathFor(string storageKey, Guid? tenantId = null)
    {
        var match = KeyPattern().Match(storageKey);
        if (!match.Success) throw new NotFoundException("The file was not found.");
        if (tenantId is { } tenant && match.Groups["tenant"].Value != tenant.ToString("N")) throw new NotFoundException("The file was not found.");

        var root = Root;
        var full = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new NotFoundException("The file was not found.");
        return full;
    }

    private static async Task<byte[]> ReadAsync(Stream source, long limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > limit)
                throw new BadRequestException($"The file is larger than the {Describe(limit)} limit.");
        }
        return buffer.ToArray();
    }

    private static string Describe(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):0.#} MB" : $"{bytes / 1024.0:0.#} KB";

    /// <summary>The name as a person typed it, minus any folder part and control characters. Kept only as metadata.</summary>
    private static string CleanName(string fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) name = "file";
        return name.Length <= 200 ? name : name[..200];
    }

    private async Task AlertAsync(FileThreat threat)
    {
        try { await alert.ThreatFoundAsync(threat); }
        catch (Exception ex) { logger.LogError(ex, "Could not raise the alert for a rejected upload."); }
    }
}

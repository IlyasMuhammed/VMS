namespace VMS.Shared.Files;

/// <summary>The kinds of file the platform recognises by content, not by what the browser or the file name claims.</summary>
[Flags]
public enum FileKinds
{
    None = 0,
    Pdf = 1,
    Png = 2,
    Jpeg = 4,
    Word = 8,    // .docx
    Excel = 16,  // .xlsx
}

/// <summary>What a caller will accept for one upload. Document types (S5) choose their own.</summary>
/// <param name="Allowed">Kinds accepted. Anything else is rejected, whatever it is called.</param>
/// <param name="MaxBytes">Largest file accepted. Null means the platform limit (<c>FileStorage:MaxBytes</c>).</param>
public sealed record FileRules(FileKinds Allowed, long? MaxBytes = null)
{
    /// <summary>Scans and photographs: PDF, PNG, JPEG.</summary>
    public static readonly FileRules Scans = new(FileKinds.Pdf | FileKinds.Png | FileKinds.Jpeg);

    /// <summary>Scans plus office documents.</summary>
    public static readonly FileRules Documents = new(FileKinds.Pdf | FileKinds.Png | FileKinds.Jpeg | FileKinds.Word | FileKinds.Excel);
}

/// <summary>
/// What the file belongs to. It decides the folder, so files can be found and reviewed by owner:
/// <c>{tenant}/{yyyy}/{MM}/{OwnerType}/{OwnerId}/{generated name}</c>. Both parts are letters, digits, hyphen or underscore.
/// </summary>
public sealed record FileOwner(string OwnerType, string OwnerId);

/// <param name="TenantId">Defaults to the signed-in user's tenant.</param>
public sealed record FileUpload(Stream Content, string FileName, FileOwner Owner, FileRules Rules, Guid? TenantId = null);

/// <summary>
/// What the caller keeps (in its own table, for example the document version): everything needed to find
/// the file again, show what it was called, and prove it has not been altered.
/// </summary>
/// <param name="StorageKey">Where it lives, relative to the storage root. Internal: never send it to a browser.</param>
/// <param name="Sha256">Lower-case hex SHA-256 of the file as uploaded.</param>
/// <param name="ContentType">Decided from the file's content.</param>
/// <param name="OriginalFileName">What the user called it. Metadata only; the stored name is generated.</param>
public sealed record StoredFile(string StorageKey, string Sha256, long SizeBytes, string ContentType, string OriginalFileName, DateTime StoredAtUtc);

/// <summary>Files live on disk, outside the database, encrypted, under generated names (FSD §23A.5).</summary>
public interface IFileStore
{
    /// <summary>
    /// Checks the file (size, real type), scans it, then stores it. Rejects with a message fit to show
    /// the user (a <c>BadRequestException</c>) and stores nothing if any check fails.
    /// </summary>
    Task<StoredFile> SaveAsync(FileUpload upload, CancellationToken cancellationToken = default);

    /// <summary>
    /// The file's content. Throws if the key does not belong to <paramref name="tenantId"/> or the stored
    /// bytes have been altered since they were written.
    /// </summary>
    Task<Stream> OpenReadAsync(Guid tenantId, string storageKey, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid tenantId, string storageKey);

    /// <summary>For the retention job only (BR-DOC-002). Nothing else deletes files.</summary>
    Task DeleteAsync(Guid tenantId, string storageKey);
}

/// <summary>A link that lets a browser fetch one file for a short time, without the storage path.</summary>
/// <param name="Url">Relative to the API root, for example <c>/api/files/download/CfDJ8...</c>.</param>
public sealed record DownloadLink(string Url, DateTime ExpiresAtUtc);

/// <summary>
/// Issues short-lived download links. The caller must already have checked the user may download this
/// file (download permission is separate from view permission) and must record the download in the
/// audit trail (BR-DOC-007); the link itself is the capability, so it is only ever handed to that user.
/// </summary>
public interface IFileDownloadLinks
{
    /// <param name="downloadName">The file name the browser shows. Defaults to the stored file's original name.</param>
    /// <param name="inline">True to display in the browser (a preview); false to save to disk.</param>
    /// <param name="lifetime">Default two minutes, never more than fifteen.</param>
    DownloadLink Create(Guid tenantId, StoredFile file, string? downloadName = null, bool inline = false, TimeSpan? lifetime = null);
}

public sealed record ScanResult(bool IsClean, string? Threat = null)
{
    public static readonly ScanResult Clean = new(true);
}

/// <summary>The antivirus hook. Swap in a real engine by registering another implementation.</summary>
public interface IVirusScanner
{
    Task<ScanResult> ScanAsync(ReadOnlyMemory<byte> content, string fileName, CancellationToken cancellationToken = default);
}

/// <summary>A file was refused because it looked malicious.</summary>
public sealed record FileThreat(Guid TenantId, string FileName, string Threat, string OwnerType, string OwnerId, string? UserName);

/// <summary>Told when a scan fails, so an administrator can be notified (FSD §23A.5). Notifications (Stage 7) plug in here.</summary>
public interface IFileScanAlert
{
    Task ThreatFoundAsync(FileThreat threat);
}

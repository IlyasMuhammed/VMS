using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using VMS.Shared.Files;

namespace VMS.Modules.Core.Files;

/// <summary>What a download link stands for. Inside the encrypted token, so the storage path never reaches the browser.</summary>
public sealed record DownloadTicket(Guid TenantId, string StorageKey, string FileName, string ContentType, string Sha256, bool Inline);

/// <summary>
/// Download links are ASP.NET Data Protection tokens: encrypted, tamper-proof and self-expiring, with
/// nothing stored on the server. Keys come from the Data Protection key ring, so when the API runs on
/// more than one server the ring must be shared (see the deployment notes).
/// </summary>
public sealed class FileDownloadLinks(IDataProtectionProvider provider) : IFileDownloadLinks
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinLifetime = TimeSpan.FromSeconds(1);

    private readonly ITimeLimitedDataProtector _protector = provider.CreateProtector("VMS.FileDownload.v1").ToTimeLimitedDataProtector();

    public DownloadLink Create(Guid tenantId, StoredFile file, string? downloadName = null, bool inline = false, TimeSpan? lifetime = null)
    {
        var life = lifetime ?? DefaultLifetime;
        if (life < MinLifetime) life = MinLifetime;
        if (life > MaxLifetime) life = MaxLifetime;

        var ticket = new DownloadTicket(tenantId, file.StorageKey, string.IsNullOrWhiteSpace(downloadName) ? file.OriginalFileName : downloadName!, file.ContentType, file.Sha256, inline);
        var expires = DateTimeOffset.UtcNow + life;
        var token = _protector.Protect(JsonSerializer.Serialize(ticket), expires);
        return new DownloadLink($"/api/files/download/{token}", expires.UtcDateTime);
    }

    /// <summary>The ticket behind a token, or null if the token is not ours, has been altered, or has expired.</summary>
    public DownloadTicket? Open(string token)
    {
        try
        {
            return JsonSerializer.Deserialize<DownloadTicket>(_protector.Unprotect(token));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }
}

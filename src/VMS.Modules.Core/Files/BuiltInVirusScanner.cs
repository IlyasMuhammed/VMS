using System.Text;
using Microsoft.Extensions.Logging;
using VMS.Shared.Files;

namespace VMS.Modules.Core.Files;

/// <summary>
/// The scanner that ships with the platform: it recognises only the EICAR test signature, which every
/// antivirus product treats as a virus, so the reject-and-alert path can be exercised end to end. It is
/// NOT antivirus protection. Register a real <see cref="IVirusScanner"/> (ClamAV, Defender, an ICAP
/// gateway) before go-live.
/// </summary>
internal sealed class BuiltInVirusScanner : IVirusScanner
{
    // Assembled at run time: written out whole in source, this string would make an antivirus product
    // quarantine the file it is in.
    private static readonly byte[] Eicar = Encoding.ASCII.GetBytes(
        "X5O!P%@AP[4\\PZX54(P^)7CC)7}" + "$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!" + "$H+H*");

    public Task<ScanResult> ScanAsync(ReadOnlyMemory<byte> content, string fileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(content.Span.IndexOf(Eicar) >= 0 ? new ScanResult(false, "EICAR-Test-File") : ScanResult.Clean);
}

/// <summary>Records the threat in the log. The notification module replaces this to alert an administrator.</summary>
internal sealed class LoggingFileScanAlert(ILogger<LoggingFileScanAlert> logger) : IFileScanAlert
{
    public Task ThreatFoundAsync(FileThreat threat)
    {
        logger.LogError(
            "Upload rejected by the virus scan: {Threat} in '{FileName}' for {OwnerType} {OwnerId} (tenant {TenantId}, user {UserName}).",
            threat.Threat, threat.FileName, threat.OwnerType, threat.OwnerId, threat.TenantId, threat.UserName);
        return Task.CompletedTask;
    }
}

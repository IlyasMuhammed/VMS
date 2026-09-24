using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using VMS.Modules.Core.Files;
using VMS.Shared.Files;
using VMS.Shared.Pagination;

namespace VMS.Modules.Core.Controllers;

/// <summary>
/// Serves a file to whoever holds a valid download link. There is no other way to a file: no folder is
/// exposed and no storage path is ever returned. Links are issued only to users who passed the download
/// permission check in the module that owns the document, expire within minutes, and cannot be altered.
/// </summary>
[ApiController]
[Route("api/files")]
public class FilesController(FileDownloadLinks links, IFileStore store, ILogger<FilesController> logger) : ControllerBase
{
    // A browser opening a PDF or an <img> cannot send an Authorization header, so the link itself is the
    // credential: encrypted, expiring, and issued after the caller's permission was checked.
    [AllowAnonymous]
    [HttpGet("download/{token}")]
    public async Task<IActionResult> Download(string token, CancellationToken cancellationToken)
    {
        var ticket = links.Open(token);
        if (ticket is null)
            return NotFound(ApiResponse.Fail("This download link is not valid or has expired."));

        Stream content;
        try
        {
            content = await store.OpenReadAsync(ticket.TenantId, ticket.StorageKey, cancellationToken);
        }
        catch (FileIntegrityException)
        {
            return Conflict(ApiResponse.Fail("The stored file failed its integrity check and was not served."));
        }

        // Prove the file is the one that was uploaded (FSD §23A.5), not merely one that decrypts.
        var actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (!string.Equals(actual, ticket.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("File {StorageKey} does not match its recorded SHA-256.", ticket.StorageKey);
            await content.DisposeAsync();
            return Conflict(ApiResponse.Fail("The stored file no longer matches its recorded checksum and was not served."));
        }
        content.Position = 0;

        var disposition = new ContentDispositionHeaderValue(ticket.Inline ? "inline" : "attachment");
        disposition.SetHttpFileName(ticket.FileName);
        Response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
        Response.Headers[HeaderNames.CacheControl] = "no-store";
        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";

        return File(content, ticket.ContentType);
    }
}

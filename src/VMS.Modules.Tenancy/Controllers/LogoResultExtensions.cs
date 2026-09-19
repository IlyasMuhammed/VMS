using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using VMS.Modules.Tenancy.Models;

namespace VMS.Modules.Tenancy.Controllers;

internal static class LogoResultExtensions
{
    /// <summary>
    /// Streams a logo with its content hash as the ETag, so browsers revalidate cheaply and get a
    /// 304 when nothing changed. Private: it is only ever fetched by a signed-in user.
    /// </summary>
    public static IActionResult LogoResult(this ControllerBase controller, TenantLogoFile logo)
    {
        controller.Response.Headers.CacheControl = "private, max-age=300, must-revalidate";
        // enableRangeProcessing is what makes ASP.NET Core honour If-None-Match and answer 304.
        return controller.File(logo.Content, logo.ContentType, lastModified: null,
            entityTag: new EntityTagHeaderValue($"\"{logo.Version}\""), enableRangeProcessing: true);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VMS.Shared.Messages;
using VMS.Shared.Pagination;

namespace VMS.Modules.Core.Controllers;

public sealed class MessagesModel
{
    public string Locale { get; set; } = "en";
    /// <summary>Message ID → text with <c>{Name}</c> placeholders.</summary>
    public IReadOnlyDictionary<string, string> Messages { get; set; } = new Dictionary<string, string>();
}

/// <summary>
/// The message catalogue, so the screen shows the same wording as the API and a reworded or translated message
/// reaches every screen without a release. Public wording, no personal data, so no sign-in is needed (the sign-in
/// page itself shows messages).
/// </summary>
[ApiController]
[Route("api/messages")]
public class MessagesController(IMessageCatalogue catalogue) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Get([FromQuery] string locale = "en")
    {
        Response.Headers.CacheControl = "public, max-age=60";
        return Ok(ApiResponse<MessagesModel>.Ok(new MessagesModel { Locale = locale, Messages = catalogue.All(locale) }));
    }
}

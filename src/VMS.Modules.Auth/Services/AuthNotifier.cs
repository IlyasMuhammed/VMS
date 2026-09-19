using Microsoft.Extensions.Options;
using VMS.Modules.Auth.Domain;
using VMS.Shared.Common;

namespace VMS.Modules.Auth.Services;

/// <summary>
/// Composes and sends the account emails. A delivery failure is logged, never thrown: the account
/// operation that triggered it has already succeeded, and the invite link is also returned to the
/// admin who triggered it.
/// </summary>
internal sealed class AuthNotifier(IEmailSender email, IOptions<AppSettings> settings, ILogger<AuthNotifier> logger)
{
    public string InviteLink(string rawToken) =>
        $"{settings.Value.BaseUrl.TrimEnd('/')}/auth/accept-invite?token={Uri.EscapeDataString(rawToken)}";

    public Task SendInviteAsync(UserAccount user, string link, bool isReset) => SendAsync(
        user.Email,
        isReset ? "Set a new VMS password" : "You have been invited to VMS",
        $"<p>Hello {System.Net.WebUtility.HtmlEncode(user.FirstName)},</p>" +
        (isReset
            ? "<p>An administrator has requested a password reset for your account.</p>"
            : "<p>An account has been created for you in the Vehicle Management System.</p>") +
        $"<p><a href=\"{link}\">Set your password</a> — the link is valid for 72 hours and can be used once.</p>");

    public Task SendResetCodeAsync(UserAccount user, string code) => SendAsync(
        user.Email,
        "Your VMS password reset code",
        $"<p>Hello {System.Net.WebUtility.HtmlEncode(user.FirstName)},</p>" +
        $"<p>Your password reset code is <strong>{code}</strong>. It is valid for 30 minutes.</p>" +
        "<p>If you did not request this, you can ignore this email.</p>");

    private async Task SendAsync(string to, string subject, string htmlBody)
    {
        try { await email.SendAsync(to, subject, htmlBody); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not send '{Subject}' email to {Recipient}.", subject, to); }
    }
}

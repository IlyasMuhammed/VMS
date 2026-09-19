using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VMS.Shared.Common;

namespace VMS.Modules.Auth.Services;

/// <summary>
/// Sends through SMTP when <c>AppSettings:Smtp:Host</c> is configured. Otherwise nothing leaves the
/// machine: the send is logged (with its body, in Development only, so invite links and reset codes
/// can be picked up locally without an SMTP server).
/// </summary>
internal sealed class SmtpEmailSender(
    IOptions<AppSettings> settings,
    IHostEnvironment environment,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string htmlBody)
    {
        var smtp = settings.Value.Smtp;

        if (string.IsNullOrWhiteSpace(smtp.Host))
        {
            if (environment.IsDevelopment())
                logger.LogInformation("SMTP not configured — email to {To}: {Subject}\n{Body}", to, subject, htmlBody);
            else
                logger.LogWarning("SMTP not configured — email to {To} ({Subject}) was not sent.", to, subject);
            return;
        }

        var from = string.IsNullOrWhiteSpace(smtp.From) ? settings.Value.AppSupportEmail : smtp.From;

        using var client = new SmtpClient(smtp.Host, smtp.Port) { EnableSsl = smtp.EnableSsl };
        if (!string.IsNullOrWhiteSpace(smtp.User))
            client.Credentials = new NetworkCredential(smtp.User, smtp.Password);

        using var message = new MailMessage(from, to, subject, htmlBody) { IsBodyHtml = true };
        await client.SendMailAsync(message);
    }
}

namespace VMS.Shared.Common;

public sealed class AppSettings
{
    /// <summary>HMAC key used to sign JWT access tokens. Must be at least 32 characters.</summary>
    public string Secret { get; set; } = string.Empty;

    public string AppSupportEmail { get; set; } = string.Empty;

    /// <summary>Public URL of the VMS web app — used to build invite and password-reset links.</summary>
    public string BaseUrl { get; set; } = "http://localhost:4200";

    public SmtpSettings Smtp { get; set; } = new();
}

public sealed class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public bool EnableSsl { get; set; } = true;
}

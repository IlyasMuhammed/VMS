using VMS.Shared.Exceptions;

namespace VMS.Modules.Tenancy.Services;

/// <summary>What a tenant logo may be. Checked on the server; the browser's checks are only a courtesy.</summary>
internal static class TenantLogoRules
{
    public const int MaxBytes = 512 * 1024;
    public const string Light = "light";
    public const string Dark = "dark";

    /// <summary>"light" or "dark", any casing. Anything else is a bad request.</summary>
    public static string NormaliseVariant(string? variant) => variant?.Trim().ToLowerInvariant() switch
    {
        Light => Light,
        Dark => Dark,
        _ => throw new BadRequestException("Logo variant must be 'light' or 'dark'.")
    };

    /// <summary>
    /// Identifies the image from its first bytes rather than trusting the file name or the
    /// client's Content-Type. Only raster formats are accepted: SVG can carry script, so it is out.
    /// </summary>
    public static string? DetectContentType(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return "image/jpeg";
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}

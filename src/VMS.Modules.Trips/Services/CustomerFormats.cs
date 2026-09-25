using System.Net.Mail;
using System.Text.RegularExpressions;

namespace VMS.Modules.Trips.Services;

/// <summary>
/// The mobile/email formats FSD §11 asks for. Mirrors <c>VMS.Modules.BusinessPartners.Services.PartnerFormats</c>'
/// own patterns exactly (a Pakistan mobile or E.164) rather than referencing that module — no project reference
/// crosses peer modules in this repo; each module keeps its own small copy, the same as <c>RawSql</c>.
/// </summary>
internal static partial class CustomerFormats
{
    [GeneratedRegex(@"^03\d{2}-\d{7}$")] private static partial Regex MobileLocal();
    [GeneratedRegex(@"^03\d{9}$")] private static partial Regex MobileLocalNoDash();
    [GeneratedRegex(@"^\+\d{8,15}$")] private static partial Regex MobileInternational();
    [GeneratedRegex(@"^[0-9+()\- ]{5,20}$")] private static partial Regex PhonePattern();

    public static bool IsMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var compact = value.Replace(" ", string.Empty);
        return MobileLocal().IsMatch(compact) || MobileLocalNoDash().IsMatch(compact) || MobileInternational().IsMatch(compact);
    }

    public static bool IsPhone(string? value) => value is not null && PhonePattern().IsMatch(value);

    public static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 150) return false;
        try
        {
            var address = new MailAddress(value);
            return address.Address == value && address.Host.Contains('.');
        }
        catch (FormatException) { return false; }
    }
}

using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace VMS.Modules.BusinessPartners.Services;

/// <summary>The formats of FSD §6 and §8: CNIC, NTN, mobile, email, account number, IBAN. Pure, so each rule is testable on its own.</summary>
public static partial class PartnerFormats
{
    [GeneratedRegex(@"^\d{5}-\d{7}-\d$")] private static partial Regex CnicPattern();
    [GeneratedRegex(@"^(\d{7}(-\d)?|\d{13})$")] private static partial Regex NtnPattern();
    [GeneratedRegex(@"^03\d{2}-\d{7}$")] private static partial Regex MobileLocal();
    [GeneratedRegex(@"^03\d{9}$")] private static partial Regex MobileLocalNoDash();
    [GeneratedRegex(@"^\+\d{8,15}$")] private static partial Regex MobileInternational();
    [GeneratedRegex(@"^[0-9-]{1,34}$")] private static partial Regex AccountNumberPattern();
    [GeneratedRegex(@"^PK\d{2}[A-Z0-9]{20}$")] private static partial Regex IbanShape();
    [GeneratedRegex(@"^[0-9+()\- ]{5,20}$")] private static partial Regex PhonePattern();

    /// <summary><c>00000-0000000-0</c>.</summary>
    public static bool IsCnic(string? value) => value is not null && CnicPattern().IsMatch(value);

    /// <summary>
    /// A National Tax Number: the 7-digit number with an optional check digit (<c>1234567</c>, <c>1234567-8</c>) or a
    /// 13-digit number, as issued to individuals on their CNIC. The FSD says "format per FBR"; this is the lenient
    /// reading, so a valid NTN is never refused.
    /// </summary>
    public static bool IsNtn(string? value) => value is not null && (NtnPattern().IsMatch(value) || CnicPattern().IsMatch(value));

    /// <summary>
    /// The mobile in its stored form, or null if it is not a mobile: <c>03XX-XXXXXXX</c> (a missing dash is added) or
    /// international <c>+92…</c> (E.164). Spaces are ignored.
    /// </summary>
    public static string? NormalizeMobile(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var compact = value.Replace(" ", string.Empty);
        if (MobileLocal().IsMatch(compact) || MobileInternational().IsMatch(compact)) return compact;
        return MobileLocalNoDash().IsMatch(compact) ? $"{compact[..4]}-{compact[4..]}" : null;
    }

    /// <summary>A second phone: a landline or another mobile, so only its characters and length are checked.</summary>
    public static bool IsPhone(string? value) => value is not null && PhonePattern().IsMatch(value);

    public static bool IsEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100) return false;
        try
        {
            var address = new MailAddress(value);
            return address.Address == value && address.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Digits and dashes, up to 34 (FSD §8.3).</summary>
    public static bool IsAccountNumber(string? value) => value is not null && AccountNumberPattern().IsMatch(value);

    /// <summary><c>PK</c> and 22 letters or digits, with the ISO 7064 mod-97 checksum (FSD §8.3).</summary>
    public static bool IsIban(string? value)
    {
        if (value is null || !IbanShape().IsMatch(value)) return false;

        // Move the first four characters to the end, turn letters into numbers (A=10 … Z=35), and the result mod 97 must be 1.
        var rearranged = value[4..] + value[..4];
        var digits = new StringBuilder();
        foreach (var c in rearranged) digits.Append(char.IsLetter(c) ? (c - 'A' + 10).ToString() : c.ToString());

        var remainder = 0;
        foreach (var d in digits.ToString()) remainder = (remainder * 10 + (d - '0')) % 97;
        return remainder == 1;
    }
}

/// <summary>How alike two partner names are, for the duplicate warning (FSD §12.1: "Legal Name ≥ 85% similar").</summary>
public static partial class NameSimilarity
{
    /// <summary>Words that say what kind of company it is, not which one, and so must not make two different names look alike.</summary>
    private static readonly HashSet<string> LegalForm = ["pvt", "private", "ltd", "limited", "co", "company", "and", "the", "inc", "llc", "smc"];

    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")] private static partial Regex NotLettersOrDigits();

    /// <summary>Lower case, punctuation and legal-form words removed, single spaces: "Ali Traders (Pvt.) Ltd" and "ALI TRADERS" become "ali traders".</summary>
    public static string Normalize(string? name)
    {
        var words = NotLettersOrDigits().Replace((name ?? string.Empty).ToLowerInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !LegalForm.Contains(w));
        return string.Join(' ', words);
    }

    /// <summary>
    /// 0 to 1: 1 means the same after normalising. The better of the names as written and with their words in sorted
    /// order, so "Traders Ali" is as close to "Ali Traders" as it should be.
    /// </summary>
    public static double Score(string? a, string? b)
    {
        var (x, y) = (Normalize(a), Normalize(b));
        if (x.Length == 0 || y.Length == 0) return 0;
        var direct = Ratio(x, y);
        var sorted = Ratio(SortWords(x), SortWords(y));
        return Math.Max(direct, sorted);
    }

    private static string SortWords(string s) => string.Join(' ', s.Split(' ').Order(StringComparer.Ordinal));

    private static double Ratio(string a, string b)
    {
        if (a == b) return 1;
        var longest = Math.Max(a.Length, b.Length);
        return 1.0 - (double)Distance(a, b) / longest;
    }

    /// <summary>Levenshtein distance: the fewest single-character edits to turn one into the other.</summary>
    public static int Distance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}

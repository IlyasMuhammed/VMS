using System.Globalization;

namespace VMS.Modules.Trips.Services;

/// <summary>§34: "amount in words (PKR)." Uses the South Asian grouping (Crore/Lakh/Thousand) rather than the
/// Western one (Million/Billion) — the natural convention for a Pakistan-based invoice; the FSD names the
/// requirement but not which grouping, so this is a documented, reasonable choice rather than a guess left silent.</summary>
internal static class AmountInWords
{
    private static readonly string[] Ones =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten",
        "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"
    ];
    private static readonly string[] Tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"];

    public static string Convert(decimal amount, string currencyCode)
    {
        var negative = amount < 0;
        amount = Math.Abs(amount);
        var whole = (long)Math.Floor(amount);
        var fraction = (int)Math.Round((amount - whole) * 100, MidpointRounding.AwayFromZero);

        var words = $"{currencyCode} {ConvertWhole(whole)}";
        if (fraction > 0) words += $" and {ConvertBelowThousand(fraction)} Paisa";
        words += " Only";
        return negative ? $"Minus {words}" : words;
    }

    private static string ConvertWhole(long number)
    {
        if (number == 0) return "Zero";
        var parts = new List<string>();
        var crore = number / 1_00_00_000; number %= 1_00_00_000;
        var lakh = number / 1_00_000; number %= 1_00_000;
        var thousand = number / 1_000; number %= 1_000;

        if (crore > 0) parts.Add($"{ConvertBelowThousand(crore)} Crore");
        if (lakh > 0) parts.Add($"{ConvertBelowThousand(lakh)} Lakh");
        if (thousand > 0) parts.Add($"{ConvertBelowThousand(thousand)} Thousand");
        if (number > 0) parts.Add(ConvertBelowThousand(number));
        return string.Join(" ", parts);
    }

    private static string ConvertBelowThousand(long n)
    {
        var parts = new List<string>();
        if (n >= 100) { parts.Add($"{Ones[n / 100]} Hundred"); n %= 100; }
        if (n >= 20) { parts.Add(Tens[n / 10]); n %= 10; if (n > 0) parts.Add(Ones[n]); }
        else if (n > 0) parts.Add(Ones[n]);
        return string.Join(" ", parts);
    }

    /// <summary>The FSD's own localisation note gives the numeral format as Western grouping, e.g. "1,234,567.00"
    /// — kept exactly as specified for the printed figure, even though the words above use the South Asian
    /// Crore/Lakh grouping (the two are independent choices; the FSD is explicit only about the numeral).</summary>
    public static string FormatAmount(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}

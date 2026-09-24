using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace VMS.Shared.Time;

/// <summary>
/// The API's contract for an instant (NFR-DT-05): ISO 8601 <em>with an offset</em>, always UTC on the way
/// out (<c>2026-10-10T09:30:00Z</c>). On the way in any offset is accepted and converted
/// (<c>2026-10-10T14:30:00+05:00</c> is 09:30 UTC); a value with no offset is refused, because nobody can
/// say which clock it was read from. Business dates are not instants: use <see cref="DateOnly"/>, which
/// is exchanged as plain <c>YYYY-MM-DD</c>.
/// <para>
/// A <see cref="DateTime"/> of unspecified kind is taken to be UTC, since that is how the database
/// stores every instant (see <see cref="UtcDateTimeConvention"/>). Without this, the serialiser would
/// write such a value with no offset, and a browser would read it as local time.
/// </para>
/// </summary>
public sealed partial class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    [GeneratedRegex(@"(Z|[+-]\d{2}(:?\d{2})?)$", RegexOptions.IgnoreCase)]
    private static partial Regex EndsWithOffset();

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

        if (text is null || !text.Contains('T') || !EndsWithOffset().IsMatch(text.Trim()) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            throw new JsonException("A date and time must be ISO 8601 with a UTC offset, for example 2026-10-10T14:30:00+05:00 or 2026-10-10T09:30:00Z.");

        return parsed.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(ToUtc(value).ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));

    public static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

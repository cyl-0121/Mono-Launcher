using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MonoLauncher.Core.Models;

/// <summary>
/// Tolerates the date formats found in the wild. Mojang emits ISO-8601 with a colon
/// ("2024-08-08T00:00:00+00:00"), but Fabric/Quilt loader profiles emit the offset without a
/// colon and without a "Z" ("2026-11-15T01:36:43+0000"), which the default parser rejects.
/// </summary>
public sealed class FlexibleDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    private static readonly Regex CompactOffset = new(
        @"(T\d{2}:\d{2}:\d{2}(?:\.\d+)?)([+-]\d{2})(\d{2})$", RegexOptions.Compiled);

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return default;
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var unix))
            return DateTimeOffset.FromUnixTimeSeconds(unix);

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text)) return default;

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var direct))
            return direct;

        // "+0000" -> "+00:00", then retry.
        var normalized = CompactOffset.Replace(text, "$1$2:$3");
        if (normalized != text &&
            DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fixedUp))
            return fixedUp;

        // Last resort: a bare local date time, assume UTC so ordering still works.
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));

        return default;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
}

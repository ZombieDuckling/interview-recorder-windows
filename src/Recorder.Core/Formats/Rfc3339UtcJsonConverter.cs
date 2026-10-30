using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Recorder.Core.Formats;

internal static class Rfc3339UtcTimestamps
{
    internal const string FormatWithMilliseconds = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    internal static DateTimeOffset ParseRequired(string text)
    {
        if (DateTimeOffset.TryParseExact(
                text,
                FormatWithMilliseconds,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        throw new JsonException($"Expected RFC 3339 UTC timestamp in form {FormatWithMilliseconds}.");
    }
}

internal sealed class Rfc3339UtcJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Expected RFC 3339 timestamp string.");
        }

        var text = reader.GetString();
        if (text is null)
        {
            throw new JsonException("Expected RFC 3339 timestamp string.");
        }

        return Rfc3339UtcTimestamps.ParseRequired(text);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToUniversalTime().ToString(Rfc3339UtcTimestamps.FormatWithMilliseconds, CultureInfo.InvariantCulture));
    }
}

internal sealed class NullableRfc3339UtcJsonConverter : JsonConverter<DateTimeOffset?>
{
    private readonly Rfc3339UtcJsonConverter _inner = new();

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        return _inner.Read(ref reader, typeof(DateTimeOffset), options);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        _inner.Write(writer, value.Value, options);
    }
}

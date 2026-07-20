using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TpLink.Sdk.Models;

/// <summary>
/// Reads a JSON number or string token as a string, unmodified. Used for fields whose
/// exact wire format isn't fully confirmed (trafficUsage, onlineTime) — captures the raw
/// value losslessly so parsing logic can live separately and be swapped out without
/// touching the model or the transport layer.
/// </summary>
public class LenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString(CultureInfo.InvariantCulture) : reader.GetDouble().ToString(CultureInfo.InvariantCulture),
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for a lenient string field.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

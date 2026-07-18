using System.Text.Json;
using System.Text.Json.Serialization;

namespace TpLink.Sdk.Models;

/// <summary>
/// downloadLimit/uploadLimit are confirmed live to be "sometimes string, sometimes number"
/// on the same endpoint — phase1-live-findings.md explicitly flags this and says the SDK
/// should parse leniently rather than assume one shape.
/// </summary>
public class LenientIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => reader.GetInt32(),
            JsonTokenType.String => int.TryParse(reader.GetString(), out var value) ? value : null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for a lenient int field.")
        };
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteNumberValue(value.Value);
    }
}

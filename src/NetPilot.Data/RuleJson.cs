using System.Text.Json;
using System.Text.Json.Serialization;
using NetPilot.Core.Devices;

namespace NetPilot.Data;

internal static class RuleJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new MacConverter(), new JsonStringEnumConverter() }
    };

    private sealed class MacConverter : JsonConverter<MacAddress>
    {
        public override MacAddress Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            new(reader.GetString() ?? throw new JsonException("MAC address is required."));
        public override void Write(Utf8JsonWriter writer, MacAddress value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}

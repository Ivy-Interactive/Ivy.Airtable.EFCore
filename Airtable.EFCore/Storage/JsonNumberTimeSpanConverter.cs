using System.Text.Json;
using System.Text.Json.Serialization;

namespace Airtable.EFCore.Storage;

public class JsonNumberTimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var seconds = reader.GetDouble();
        return TimeSpan.FromSeconds(seconds);
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        => throw new NotImplementedException();
}

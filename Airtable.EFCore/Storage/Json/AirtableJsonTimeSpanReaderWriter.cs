using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace Airtable.EFCore.Storage.Json;

public sealed class AirtableJsonTimeSpanReaderWriter : JsonValueReaderWriter<TimeSpan>
{
    public static AirtableJsonTimeSpanReaderWriter Instance { get; } = new();

    private AirtableJsonTimeSpanReaderWriter()
    {
    }

    public override TimeSpan FromJsonTyped(ref Utf8JsonReaderManager manager, object? existingObject = null)
        => TimeSpan.FromSeconds(manager.CurrentReader.GetDouble());

    public override void ToJsonTyped(Utf8JsonWriter writer, TimeSpan value)
        => writer.WriteNumberValue(value.TotalSeconds);
}

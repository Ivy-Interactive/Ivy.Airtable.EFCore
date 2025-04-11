using System.Text.Json;
using AirtableApiClient;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace Airtable.EFCore.Storage.Json;

public sealed class AirtableJsonAttachmentReaderWriter : JsonValueReaderWriter<AirtableAttachment>
{
    public static AirtableJsonAttachmentReaderWriter Instance { get; } = new();

    private AirtableJsonAttachmentReaderWriter()
    {
    }

    public override AirtableAttachment FromJsonTyped(ref Utf8JsonReaderManager manager, object? existingObject = null)
        => JsonSerializer.Deserialize<AirtableAttachment>(ref manager.CurrentReader);

    public override void ToJsonTyped(Utf8JsonWriter writer, AirtableAttachment value)
        => JsonSerializer.Serialize(writer, value.WrittenVersion());
}

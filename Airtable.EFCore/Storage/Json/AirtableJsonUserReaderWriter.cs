using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.Json;

namespace Airtable.EFCore.Storage.Json;

public sealed class AirtableJsonUserReaderWriter : JsonValueReaderWriter<AirtableUser>
{
    public static AirtableJsonUserReaderWriter Instance { get; } = new();

    private AirtableJsonUserReaderWriter()
    {
    }

    public override AirtableUser FromJsonTyped(ref Utf8JsonReaderManager manager, object? existingObject = null)
        => JsonSerializer.Deserialize<AirtableUser>(ref manager.CurrentReader);

    public override void ToJsonTyped(Utf8JsonWriter writer, AirtableUser value)
        => JsonSerializer.Serialize(writer, value.WrittenVersion());
}

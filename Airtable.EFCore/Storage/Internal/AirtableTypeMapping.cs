using Microsoft.EntityFrameworkCore.Storage;

namespace Airtable.EFCore.Storage.Internal;

internal sealed class AirtableTypeMapping : RelationalTypeMapping
{
    public AirtableTypeMapping(string storeType, Type clrType) : base(storeType, clrType)
    {
    }

    private AirtableTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new AirtableTypeMapping(parameters);
}

using Airtable.EFCore.Storage.Json;
using Microsoft.EntityFrameworkCore.Storage;

namespace Airtable.EFCore.Storage.Internal;

internal sealed class AirtableTimeSpanTypeMapping : TimeSpanTypeMapping
{
    public AirtableTimeSpanTypeMapping(string storeType)
        : this(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(typeof(TimeSpan), jsonValueReaderWriter: AirtableJsonTimeSpanReaderWriter.Instance),
                storeType))
    {
    }

    private AirtableTimeSpanTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new AirtableTimeSpanTypeMapping(parameters);
}

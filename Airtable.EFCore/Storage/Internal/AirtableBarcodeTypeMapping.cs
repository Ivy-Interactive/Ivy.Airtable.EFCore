using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Airtable.EFCore.Storage.Internal;

internal sealed class AirtableBarcodeTypeMapping : RelationalTypeMapping
{
    public AirtableBarcodeTypeMapping(string storeType)
        : this(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(
                    typeof(AirtableBarcode),
                    comparer: BarcodeComparer),
                storeType))
    {
    }

    private AirtableBarcodeTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    private static readonly ValueComparer<AirtableBarcode> BarcodeComparer =
        new(
            (a, b) => a == b,

            a => a == null
                ? 0
                : a.GetHashCode(),

            a => a == null
                ? null
                : a.Clone()
        );

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new AirtableBarcodeTypeMapping(parameters);
}

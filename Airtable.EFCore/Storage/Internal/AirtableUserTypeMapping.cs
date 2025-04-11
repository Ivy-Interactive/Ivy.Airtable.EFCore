using Airtable.EFCore.ChangeTracking;
using Airtable.EFCore.Storage.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Airtable.EFCore.Storage.Internal;

internal sealed class AirtableUserTypeMapping : RelationalTypeMapping
{
    public AirtableUserTypeMapping(string storeType, bool isCollection = false)
        : this(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(
                    isCollection
                        ? typeof(ICollection<AirtableUser>)
                        : typeof(AirtableUser),
                    comparer: isCollection
                        ? UserCollectionComparer
                        : UserComparer,
                    jsonValueReaderWriter: isCollection
                        ? new AirtableJsonCollectionOfReferencesReaderWriter<List<AirtableUser>, AirtableUser>(AirtableJsonUserReaderWriter.Instance)
                        : AirtableJsonUserReaderWriter.Instance),
                storeType))
    {
    }

    private AirtableUserTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    private static readonly ValueComparer<AirtableUser> UserComparer =
        new(
            (a, b) => a == b,

            a => a == null
                ? 0
                : a.GetHashCode(),

            a => a == null
                ? null
                : a.Clone()
        );

    private static readonly ValueComparer UserCollectionComparer =
        new AirtableListOfReferenceTypesComparer<List<AirtableUser>, AirtableUser>(UserComparer);

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new AirtableUserTypeMapping(parameters);
}

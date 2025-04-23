using Microsoft.EntityFrameworkCore.Storage;
using AirtableApiClient;

namespace Airtable.EFCore.Storage.Internal;

public class AirtableTypeMappingSource : RelationalTypeMappingSource
{
    internal static readonly Dictionary<string, Type> TypeHints = new();
    private static readonly Dictionary<Type, RelationalTypeMapping> _clrTypeMappings = new();
    private static readonly Dictionary<string, RelationalTypeMapping> _storeTypeMappings = new(StringComparer.OrdinalIgnoreCase);

    static AirtableTypeMappingSource()
    {
        RelationalTypeMapping MakeMapping(string name, Type clrType)
            => clrType switch
            {
                Type t when t == typeof(string)                          => new StringTypeMapping(name, null),
                Type t when t == typeof(double)                          => new DoubleTypeMapping(name),
                Type t when t == typeof(int)                             => new IntTypeMapping(name),
                Type t when t == typeof(bool)                            => new BoolTypeMapping(name),
                Type t when t == typeof(TimeSpan)                        => new TimeSpanTypeMapping(name),
                Type t when t == typeof(AirtableAttachment)              => new AirtableAttachmentTypeMapping(name, isCollection: false),
                Type t when t == typeof(ICollection<AirtableAttachment>) => new AirtableAttachmentTypeMapping(name, isCollection: true),
                Type t when t == typeof(AirtableUser)                    => new AirtableUserTypeMapping(name, isCollection: false),
                Type t when t == typeof(ICollection<AirtableUser>)       => new AirtableUserTypeMapping(name, isCollection: true),
                Type t when t == typeof(AirtableBarcode)                 => new AirtableBarcodeTypeMapping(name),
                Type t                                                   => new AirtableTypeMapping(name, clrType),
            };

        //Note: if more than one entry below has the same name or CLR type, the first matching entry will be used when
        //mapping from one to the other.
        var mappings = new (string, Type)[]
        {
            ("multilineText", typeof(string)),
            ("singleLineText", typeof(string)),
            ("richText", typeof(string)),
            ("email", typeof(string)),
            ("url", typeof(string)),
            ("phoneNumber", typeof(string)),

            ("aiText", typeof(AirtableAiText)),

            ("number", typeof(double)),
            ("number", typeof(int)),

            ("duration", typeof(TimeSpan)),
            ("checkbox", typeof(bool)),
            ("date", typeof(DateOnly)),
            ("dateTime", typeof(DateTimeOffset)),
            ("dateTime", typeof(DateTime)),
            ("multipleAttachments", typeof(ICollection<AirtableAttachment>)),
            ("multipleAttachments", typeof(AirtableAttachment)),
            ("singleCollaborator", typeof(AirtableUser)),
            ("multipleCollaborators", typeof(ICollection<AirtableUser>)),
            ("barcode", typeof(AirtableBarcode)),
            ("button", typeof(AirtableButton)),
        };
        foreach (var (name, clrType) in mappings)
        {
            var mapping = MakeMapping(name, clrType);
            _storeTypeMappings.TryAdd(name, mapping);
            _clrTypeMappings.TryAdd(clrType, mapping);
            TypeHints.TryAdd(name, clrType);
        }
    }

    public AirtableTypeMappingSource(
        TypeMappingSourceDependencies dependencies,
        RelationalTypeMappingSourceDependencies relationalDependencies)
        : base(dependencies, relationalDependencies)
    {
    }

    protected override RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        var mapping = base.FindMapping(mappingInfo)
            ?? FindRawMapping(mappingInfo);

        if (mapping != null && mappingInfo.StoreTypeName != null)
        {
            mapping = mapping.WithStoreTypeAndSize(mappingInfo.StoreTypeName, null);
        }

        return mapping;
    }

    private RelationalTypeMapping? FindRawMapping(RelationalTypeMappingInfo mappingInfo)
    {
        var clrType = mappingInfo.ClrType;

        RelationalTypeMapping? mapping;

        if (clrType != null)
        {
            if (_clrTypeMappings.TryGetValue(clrType, out mapping))
            {
                return mapping;
            }
            else if (ShouldMapClrEnumerableType(clrType))
            {
                return new AirtableTypeMapping(mappingInfo.StoreTypeName ?? "unknownType", clrType);
            }
        }

        var storeTypeName = mappingInfo.StoreTypeName;
        if (storeTypeName != null && _storeTypeMappings.TryGetValue(storeTypeName, out mapping) && (clrType == null || mapping.ClrType.UnwrapNullableType() == clrType))
        {
            return mapping;
        }

        return null;
    }

    private bool ShouldMapClrEnumerableType(in Type clr)
    {
        // Check if clr is IEnumerable<T>
        Type? enumerableType = null;
        if (clr.IsGenericType)
        {
            var genericTypeDefinition = clr.GetGenericTypeDefinition();
            if (genericTypeDefinition == typeof(IEnumerable<>))
            {
                enumerableType = clr;
            }
        }

        // Check if clr implements IEnumerable<T>
        if (enumerableType == null)
        {
            enumerableType = clr.GetInterfaces().FirstOrDefault(interface_ => interface_.IsGenericType && interface_.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        }

        // If neither of the previous two cases are true, return false.
        if (enumerableType == null)
        {
            return false;
        }

        // Check element type
        var elem = clr.GenericTypeArguments.Length > 0
            ? clr.GenericTypeArguments[0]
            : clr.IsArray
                ? clr.GetElementType()
                : null;
        if (elem == typeof(int)
            || elem == typeof(double)
            || elem == typeof(string))
        {
            return true;
        }

        return false;
    }
}

using Microsoft.EntityFrameworkCore.Storage;
using AirtableApiClient;

namespace Airtable.EFCore.Storage.Internal;

public class AirtableTypeMappingSource : RelationalTypeMappingSource
{
    internal static readonly Dictionary<FieldEnum, Type> TypeHints = [];
    private static readonly Dictionary<Type, RelationalTypeMapping> _clrTypeMappings = [];
    private static readonly Dictionary<string, RelationalTypeMapping> _storeTypeMappings = [];

    static AirtableTypeMappingSource()
    {
        RelationalTypeMapping MakeMapping(FieldEnum type, Type clrType)
            => clrType switch
            {
                Type t when t == typeof(string)                          => new StringTypeMapping(type.ToApiString(), null),
                Type t when t == typeof(double)                          => new DoubleTypeMapping(type.ToApiString()),
                Type t when t == typeof(int)                             => new IntTypeMapping(type.ToApiString()),
                Type t when t == typeof(bool)                            => new BoolTypeMapping(type.ToApiString()),
                Type t when t == typeof(TimeSpan)                        => new TimeSpanTypeMapping(type.ToApiString()),
                Type t when t == typeof(AirtableAttachment)              => new AirtableAttachmentTypeMapping(type.ToApiString(), isCollection: false),
                Type t when t == typeof(ICollection<AirtableAttachment>) => new AirtableAttachmentTypeMapping(type.ToApiString(), isCollection: true),
                Type t when t == typeof(AirtableUser)                    => new AirtableUserTypeMapping(type.ToApiString(), isCollection: false),
                Type t when t == typeof(ICollection<AirtableUser>)       => new AirtableUserTypeMapping(type.ToApiString(), isCollection: true),
                Type t when t == typeof(AirtableBarcode)                 => new AirtableBarcodeTypeMapping(type.ToApiString()),
                Type t                                                   => new AirtableTypeMapping(type.ToApiString(), clrType),
            };

        //Note: if more than one entry below has the same name or CLR type, the first matching entry will be used when
        //mapping from one to the other.
        var mappings = new (FieldEnum, Type)[]
        {
            (FieldEnum.MultilineText, typeof(string)),
            (FieldEnum.SingleLineText, typeof(string)),
            (FieldEnum.RichText, typeof(string)),
            (FieldEnum.Email, typeof(string)),
            (FieldEnum.Url, typeof(string)),
            (FieldEnum.PhoneNumber, typeof(string)),

            (FieldEnum.AiText, typeof(AirtableAiText)),

            (FieldEnum.Number, typeof(double)),
            (FieldEnum.Number, typeof(int)),

            (FieldEnum.Duration, typeof(TimeSpan)),
            (FieldEnum.Checkbox, typeof(bool)),
            (FieldEnum.Date, typeof(DateOnly)),
            (FieldEnum.DateTime, typeof(DateTimeOffset)),
            (FieldEnum.DateTime, typeof(DateTime)),
            (FieldEnum.MultipleAttachments, typeof(ICollection<AirtableAttachment>)),
            (FieldEnum.MultipleAttachments, typeof(AirtableAttachment)),
            (FieldEnum.SingleCollaborator, typeof(AirtableUser)),
            (FieldEnum.MultipleCollaborators, typeof(ICollection<AirtableUser>)),
            (FieldEnum.Barcode, typeof(AirtableBarcode)),
            (FieldEnum.Button, typeof(AirtableButton)),
        };
        foreach (var (name, clrType) in mappings)
        {
            var mapping = MakeMapping(name, clrType);
            _storeTypeMappings.TryAdd(name.ToApiString(), mapping);
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
        if (storeTypeName != null && _storeTypeMappings.TryGetValue(storeTypeName, out mapping) && (clrType == null || mapping?.ClrType.UnwrapNullableType() == clrType))
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

using Airtable.EFCore.ChangeTracking;
using Airtable.EFCore.Storage.Json;
using AirtableApiClient;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Airtable.EFCore.Storage.Internal;

internal sealed class AirtableAttachmentTypeMapping : RelationalTypeMapping
{
    public AirtableAttachmentTypeMapping(string storeType, bool isCollection = false)
        : this(
            new RelationalTypeMappingParameters(
                new CoreTypeMappingParameters(
                    isCollection
                        ? typeof(ICollection<AirtableAttachment>)
                        : typeof(AirtableAttachment),
                    comparer: isCollection
                        ? AttachmentCollectionComparer
                        : AttachmentComparer,
                    jsonValueReaderWriter: isCollection
                        ? new AirtableJsonCollectionOfReferencesReaderWriter<List<AirtableAttachment>, AirtableAttachment>(AirtableJsonAttachmentReaderWriter.Instance)
                        : AirtableJsonAttachmentReaderWriter.Instance),
                storeType))
    {
    }

    private AirtableAttachmentTypeMapping(RelationalTypeMappingParameters parameters) : base(parameters)
    {
    }

    private static readonly ValueComparer<AirtableAttachment> AttachmentComparer =
        new(
            (a, b) => AttachmentsEqual(a, b),

            a => HashAttachment(a),

            a => CloneAttachment(a)
        );

    private static readonly ValueComparer AttachmentCollectionComparer = new AirtableListOfReferenceTypesComparer<List<AirtableAttachment>, AirtableAttachment>(AttachmentComparer);

    private static bool AttachmentsEqual(AirtableAttachment? a, AirtableAttachment? b)
    {
        if (a == null || b == null)
        {
            return a == b;
        }

        return a.Id == b.Id && a.Url == b.Url && a.Filename == b.Filename;
    }

    private static int HashAttachment(AirtableAttachment? a)
        => a == null
            ? 0
            : HashCode.Combine(a.Id, a.Url, a.Filename);

    private static AirtableAttachment? CloneAttachment(AirtableAttachment? a)
        => a == null
            ? null
            : new AirtableAttachment()
            {
                Id = a.Id,
                Url = a.Url,
                Filename = a.Filename,
                Size = a.Size,
                Type = a.Type,
                Thumbnails = a.Thumbnails,
            };

    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters)
        => new AirtableAttachmentTypeMapping(parameters);
}

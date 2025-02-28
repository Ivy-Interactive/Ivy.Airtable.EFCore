using Airtable.EFCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Airtable.EFCore.Design;

public class AirtableAnnotationCodeGenerator(AnnotationCodeGeneratorDependencies dependencies) : AnnotationCodeGenerator(dependencies)
{
    private static readonly HashSet<string> _ignoredAnnotations = new()
    {
        RelationalAnnotationNames.ColumnType,
        AirtableAnnotationNames.IsLinkIdColumn,
        AirtableAnnotationNames.IsPluralLinkIdColumn,
    };

    public override IEnumerable<IAnnotation> FilterIgnoredAnnotations(IEnumerable<IAnnotation> annotations)
        => base.FilterIgnoredAnnotations(annotations).Where(a => !_ignoredAnnotations.Contains(a.Name));
}

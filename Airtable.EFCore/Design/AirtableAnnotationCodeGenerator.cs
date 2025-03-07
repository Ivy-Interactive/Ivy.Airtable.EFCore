using Airtable.EFCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Airtable.EFCore.Design;

public class AirtableAnnotationCodeGenerator(AnnotationCodeGeneratorDependencies dependencies) : AnnotationCodeGenerator(dependencies)
{
    private static readonly HashSet<string> _ignoredAnnotations = new()
    {
        RelationalAnnotationNames.ColumnType,
        AirtableAnnotationNames.LinkIdColumn,
        AirtableAnnotationNames.IsLinkIdColumn,
        AirtableAnnotationNames.IsPluralLinkIdColumn,
        AirtableAnnotationNames.IsForwardDirectionForeignKey,
    };

    private static readonly Type _firstGenericArgumentType = Type.MakeGenericMethodParameter(0);
    private static readonly Type _secondGenericArgumentType = Type.MakeGenericMethodParameter(1);
    private static readonly MethodInfo _referenceCollectionBuilderHasLinkIdsPropertyMethod =
        typeof(ReferenceCollectionBuilderExtensions)
            .GetMethod(
                nameof(ReferenceCollectionBuilderExtensions.HasLinkIdsProperty), new[]
                    {
                        typeof(ReferenceCollectionBuilder<,>).MakeGenericType(
                            new[]
                                {
                                    _firstGenericArgumentType,
                                    _secondGenericArgumentType
                                }),
                        typeof(Expression<>).MakeGenericType(
                            typeof(Func<,>).MakeGenericType(
                                new[]
                                    {
                                        _firstGenericArgumentType,
                                        typeof(IEnumerable<string>)
                                    }))
                    })
            ?? throw new InvalidOperationException("Could not find method TryGetValue");

    public override IEnumerable<IAnnotation> FilterIgnoredAnnotations(IEnumerable<IAnnotation> annotations)
        => base.FilterIgnoredAnnotations(annotations).Where(a => !_ignoredAnnotations.Contains(a.Name));

    protected override MethodCallCodeFragment? GenerateFluentApi(IForeignKey foreignKey, IAnnotation annotation)
    {
        if (annotation.Name == AirtableAnnotationNames.LinkIdProperty && annotation.Value is IProperty idProperty)
        {
            var placeholder = new IdPropertyLambdaPlaceholder(idProperty.Name);
            return new MethodCallCodeFragment(_referenceCollectionBuilderHasLinkIdsPropertyMethod, placeholder);
        }

        return base.GenerateFluentApi(foreignKey, annotation);
    }

    internal class IdPropertyLambdaPlaceholder(string propertyName)
    {
        public string PropertyName { get; } = propertyName;
    }
}

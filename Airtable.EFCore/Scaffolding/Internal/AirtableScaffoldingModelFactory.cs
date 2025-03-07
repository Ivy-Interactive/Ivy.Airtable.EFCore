using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Airtable.EFCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore;

namespace Airtable.EFCore.Scaffolding.Internal;

#pragma warning disable EF1001 // Internal EF Core API usage.

public class AirtableScaffoldingModelFactory : RelationalScaffoldingModelFactory
{
    private readonly Dictionary<DatabaseColumn, PropertyBuilder> _columnProperties = new(ReferenceEqualityComparer.Instance);

    private readonly ICandidateNamingService _candidateNamingService;
    private readonly IPluralizer _pluralizer;

    public AirtableScaffoldingModelFactory(
        IOperationReporter reporter,
        ICandidateNamingService candidateNamingService,
        IPluralizer pluralizer,
        ICSharpUtilities cSharpUtilities,
        IScaffoldingTypeMapper scaffoldingTypeMapper,
        IModelRuntimeInitializer modelRuntimeInitializer)
            : base(reporter, candidateNamingService, pluralizer, cSharpUtilities, scaffoldingTypeMapper, modelRuntimeInitializer)
    {
        _candidateNamingService = candidateNamingService;
        _pluralizer = pluralizer;
    }

    protected override string GetPropertyName(DatabaseColumn column)
    {
        if (column.FindAnnotation(AirtableAnnotationNames.IsLinkIdColumn)?.Value is true)
        {
            var oldName = column.Name;
            var candidateName = _candidateNamingService.GenerateCandidateIdentifier(column.Name);
            column.Name = _pluralizer.Singularize(candidateName) + "Id";
            var propertyName = base.GetPropertyName(column);
            column.Name = oldName;
            return propertyName;
        }
        else if (column.FindAnnotation(AirtableAnnotationNames.IsPluralLinkIdColumn)?.Value is true)
        {
            var oldName = column.Name;
            var candidateName = _candidateNamingService.GenerateCandidateIdentifier(column.Name);
            column.Name = _pluralizer.Singularize(candidateName) + "Ids";
            var propertyName = base.GetPropertyName(column);
            column.Name = oldName;
            return propertyName;
        }
        else
        {
            return base.GetPropertyName(column);
        }
    }

    protected override PropertyBuilder? VisitColumn(EntityTypeBuilder builder, DatabaseColumn column)
    {
        var property = base.VisitColumn(builder, column);
        if (property != null)
        {
            _columnProperties.Add(column, property);
        }
        return property;
    }

    protected override IMutableForeignKey? VisitForeignKey(ModelBuilder modelBuilder, DatabaseForeignKey foreignKey)
    {
        var fk = base.VisitForeignKey(modelBuilder, foreignKey);

        if (fk?.FindAnnotation(AirtableAnnotationNames.LinkIdColumn)?.Value is DatabaseColumn linkIdColumn)
        {
            var linkIdProperty = _columnProperties[linkIdColumn].Metadata;
            fk.AddAnnotation(AirtableAnnotationNames.LinkIdProperty, linkIdProperty);
        }

        return fk;
    }
}

#pragma warning restore EF1001 // Internal EF Core API usage.

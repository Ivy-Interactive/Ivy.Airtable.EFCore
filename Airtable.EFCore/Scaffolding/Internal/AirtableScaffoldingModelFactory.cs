using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Design.Internal;
using Airtable.EFCore.Metadata.Conventions;

namespace Airtable.EFCore.Scaffolding.Internal;

#pragma warning disable EF1001 // Internal EF Core API usage.

public class AirtableScaffoldingModelFactory : RelationalScaffoldingModelFactory
{
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
}

#pragma warning restore EF1001 // Internal EF Core API usage.

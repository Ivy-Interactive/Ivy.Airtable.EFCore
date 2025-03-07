using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using Airtable.EFCore.Metadata.Conventions;

namespace Airtable.EFCore.Scaffolding.Internal;

#pragma warning disable EF1001 // Internal EF Core API usage.

public class AirtableCandidateNamingService : CandidateNamingService
{
    public override string GetDependentEndCandidateNavigationPropertyName(IReadOnlyForeignKey foreignKey)
    {
        if (foreignKey.FindAnnotation(AirtableAnnotationNames.IsForwardDirectionForeignKey)?.Value is false)
        {
            var forwardDirectionForeignKey = foreignKey.DeclaringEntityType.GetForeignKeys().FirstOrDefault(fk => fk != foreignKey);
            if (forwardDirectionForeignKey?.FindAnnotation(AirtableAnnotationNames.LinkIdColumn)?.Value is DatabaseColumn linkIdColumn)
            {
                return GenerateCandidateIdentifier(linkIdColumn.Name);
            }
        }

        var name = base.GetDependentEndCandidateNavigationPropertyName(foreignKey);
        // If IsForwardDirectionForeignKey is set to true, that means this foreign key connects to the dependent
        // table, which means we are being asked to provide a candidate name for the inverse skip navigation property.
        if (foreignKey.FindAnnotation(AirtableAnnotationNames.IsForwardDirectionForeignKey)?.Value is true)
        {
            return $"Inverse{name}";
        }
        else
        {
            return name;
        }
    }
}

#pragma warning restore EF1001 // Internal EF Core API usage.

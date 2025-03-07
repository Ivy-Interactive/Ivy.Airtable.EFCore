using Microsoft.EntityFrameworkCore.Design.Internal;
using Microsoft.EntityFrameworkCore.Storage;

namespace Airtable.EFCore.Design.Internal;

#pragma warning disable EF1001 // Internal EF Core API usage.

public class AirtableCSharpHelper(ITypeMappingSource typeMappingSource) : CSharpHelper(typeMappingSource)
{
    public override string UnknownLiteral(object? value)
    {
        if (value is AirtableAnnotationCodeGenerator.IdPropertyLambdaPlaceholder placeholder)
        {
            return $"e => e.{placeholder.PropertyName}";
        }
        return base.UnknownLiteral(value);
    }
}

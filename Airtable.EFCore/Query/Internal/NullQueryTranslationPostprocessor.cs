using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Query;

namespace Airtable.EFCore.Query.Internal;

public class NullQueryTranslationPostprocessor : QueryTranslationPostprocessor
{
    public NullQueryTranslationPostprocessor(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryCompilationContext queryCompilationContext)
        : base(dependencies, queryCompilationContext)
    {
    }

    public override Expression Process(Expression query)
        => query;
}

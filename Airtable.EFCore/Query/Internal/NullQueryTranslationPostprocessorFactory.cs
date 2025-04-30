using Microsoft.EntityFrameworkCore.Query;

namespace Airtable.EFCore.Query.Internal;

public class NullQueryTranslationPostprocessorFactory : IQueryTranslationPostprocessorFactory
{
    public NullQueryTranslationPostprocessorFactory(QueryTranslationPostprocessorDependencies dependencies)
    {
        _dependencies = dependencies;
    }

    private readonly QueryTranslationPostprocessorDependencies _dependencies;

    public virtual QueryTranslationPostprocessor Create(QueryCompilationContext queryCompilationContext)
        => new NullQueryTranslationPostprocessor(
            _dependencies,
            (RelationalQueryCompilationContext)queryCompilationContext);
}

using Microsoft.EntityFrameworkCore.Query;

namespace Airtable.EFCore.Query.Internal;

public class NullQueryTranslationPostprocessorFactory : IQueryTranslationPostprocessorFactory
{
    public NullQueryTranslationPostprocessorFactory(
        QueryTranslationPostprocessorDependencies dependencies,
        RelationalQueryTranslationPostprocessorDependencies relationalDependencies)
    {
        _dependencies = dependencies;
        _relationalDependencies = relationalDependencies;
    }

    private readonly QueryTranslationPostprocessorDependencies _dependencies;

    private readonly RelationalQueryTranslationPostprocessorDependencies _relationalDependencies;

    public virtual QueryTranslationPostprocessor Create(QueryCompilationContext queryCompilationContext)
        => new NullQueryTranslationPostprocessor(
            _dependencies,
            _relationalDependencies,
            (RelationalQueryCompilationContext)queryCompilationContext);
}

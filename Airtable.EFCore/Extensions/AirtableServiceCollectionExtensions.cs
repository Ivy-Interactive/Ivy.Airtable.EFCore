using System.ComponentModel;
using Airtable.EFCore.Diagnostics.Internal;
using Airtable.EFCore.Infrastructure;
using Airtable.EFCore.Metadata.Conventions;
using Airtable.EFCore.Query.Internal;
using Airtable.EFCore.Query.Internal.MethodTranslators;
using Airtable.EFCore.Storage.Internal;
using Airtable.EFCore.Update.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.EntityFrameworkCore;

public static class AirtableServiceCollectionExtensions
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddEntityFrameworkAirtableDatabase(this IServiceCollection serviceCollection)
    {
        var relationalBuilder = new EntityFrameworkRelationalServicesBuilder(serviceCollection)
            .TryAdd<IRelationalTypeMappingSource, AirtableTypeMappingSource>()
            .TryAdd<IProviderConventionSetBuilder, AirtableConventionSetBuilder>()
            .TryAdd<LoggingDefinitions, AirtableLoggingDefinitions>()
            .TryAdd<IDatabaseProvider, DatabaseProvider<AirtableOptionsExtension>>()
            .TryAdd<IDatabase, AirtableDatabase>()
            .TryAdd<IQueryContextFactory, AirtableQueryContextFactory>()
            .TryAdd<IShapedQueryCompilingExpressionVisitorFactory, AirtableShapedQueryCompilingExpressionVisitorFactory>()
            .TryAdd<IQueryableMethodTranslatingExpressionVisitorFactory, AirtableQueryableMethodTranslatingExpressionVisitorFactory>()
            .TryAdd<ISqlGenerationHelper, RelationalSqlGenerationHelper>()
            .TryAdd<IUpdateSqlGenerator, NullUpdateSqlGenerator>()
            .TryAdd<IQueryTranslationPostprocessorFactory, NullQueryTranslationPostprocessorFactory>()
            .TryAddProviderSpecificServices(
                b => b.TryAddScoped<IAirtableClient, AirtableBaseWrapper>()
                      .TryAddScoped<IFormulaExpressionFactory, FormulaExpressionFactory>()
                      .TryAddScoped<IAirtableMethodCallTranslatorProvider, AirtableMethodCallTranslatorProvider>()
            )
            ;

        relationalBuilder.TryAddCoreServices();

        return serviceCollection;
    }
}

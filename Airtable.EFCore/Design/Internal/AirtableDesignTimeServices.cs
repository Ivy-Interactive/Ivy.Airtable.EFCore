using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Scaffolding.Internal;
using Airtable.EFCore.Scaffolding.Internal;
using static Microsoft.EntityFrameworkCore.AirtableServiceCollectionExtensions;

[assembly: DesignTimeProviderServices("Airtable.EFCore.Design.Internal.AirtableDesignTimeServices")]

namespace Airtable.EFCore.Design.Internal;

public class AirtableDesignTimeServices : IDesignTimeServices
{
    public void ConfigureDesignTimeServices(IServiceCollection services)
    {
        services.AddEntityFrameworkAirtableDatabase();
        new EntityFrameworkRelationalDesignServicesBuilder(services)
#pragma warning disable EF1001 // Internal EF Core API usage.
            .TryAddProviderSpecificServices(
                services => services
                    .TryAddSingleton<ICandidateNamingService, AirtableCandidateNamingService>()
                    .TryAddSingleton<ICSharpHelper, AirtableCSharpHelper>()
                    .TryAddSingleton<IScaffoldingModelFactory, AirtableScaffoldingModelFactory>())
            .TryAdd<IAnnotationCodeGenerator, AirtableAnnotationCodeGenerator>()
            .TryAdd<IDatabaseModelFactory, AirtableDatabaseModelFactory>()
            .TryAdd<IProviderConfigurationCodeGenerator, AirtableDatabaseCodeGenerator>()
            .TryAddCoreServices();
#pragma warning restore EF1001 // Internal EF Core API usage.
    }
}

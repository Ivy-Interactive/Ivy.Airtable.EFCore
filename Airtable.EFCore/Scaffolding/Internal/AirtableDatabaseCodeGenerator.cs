using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Airtable.EFCore.Infrastructure;

namespace Airtable.EFCore.Scaffolding.Internal;

public class AirtableDatabaseCodeGenerator : ProviderCodeGenerator
{
    private static readonly MethodInfo UseAirtableDatabaseMethodInfo
        = typeof(AirtableDbContextOptionsExtensions).GetRuntimeMethod(
            nameof(AirtableDbContextOptionsExtensions.UseAirtable),
            [typeof(DbContextOptionsBuilder), typeof(string), typeof(string), typeof(Action<AirtableDbContextOptionsBuilder>)])!;

    public AirtableDatabaseCodeGenerator(ProviderCodeGeneratorDependencies dependencies)
        : base(dependencies)
    {
    }

    public override MethodCallCodeFragment GenerateUseProvider(
        string connectionString,
        MethodCallCodeFragment? providerOptions)
    {
        var connectionOptions = new AirtableDatabaseConnectionStringBuilder(connectionString);

        return new(
            UseAirtableDatabaseMethodInfo,
            providerOptions == null
                ? [connectionOptions.BaseId, connectionOptions.ApiKey]
                : [connectionOptions.BaseId, connectionOptions.ApiKey, new NestedClosureCodeFragment("x", providerOptions)]);
    }
}

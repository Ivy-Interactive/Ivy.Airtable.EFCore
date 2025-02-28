using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using System.Text;

namespace Airtable.EFCore.Infrastructure;

public sealed class AirtableOptionsExtension : RelationalOptionsExtension
{
    private DbContextOptionsExtensionInfo? _info;
    public override DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public string BaseId { get; init; }
    public string ApiKey { get; init; }

    public AirtableOptionsExtension() : base()
    {
    }

    public AirtableOptionsExtension(AirtableOptionsExtension original) : base(original)
    {
        BaseId = original.BaseId;
        ApiKey = original.ApiKey;
    }

    protected override RelationalOptionsExtension Clone()
        => new AirtableOptionsExtension(this);

    public override void ApplyServices(IServiceCollection services)
    {
        services.AddEntityFrameworkAirtableDatabase();
    }

    public override void Validate(IDbContextOptions options)
    {
    }

    public AirtableOptionsExtension WithBaseId(string baseId)
    {
        return new AirtableOptionsExtension(this) { BaseId = baseId };
    }
    public AirtableOptionsExtension WithApiKey(string apiKey)
    {
        return new AirtableOptionsExtension(this) { ApiKey = apiKey };
    }

    public override RelationalOptionsExtension WithConnectionString(string? connectionString)
    {
        var connectionOptions = new AirtableDatabaseConnectionStringBuilder(connectionString);
        var withConnectionString = (AirtableOptionsExtension)base.WithConnectionString(connectionString);
        return new AirtableOptionsExtension(withConnectionString)
        {
            BaseId = connectionOptions.BaseId,
            ApiKey = connectionOptions.ApiKey,
        };
    }

    public override RelationalOptionsExtension WithConnection(DbConnection? connection, bool owned)
        => throw new InvalidOperationException("Setting a DbConnection is not supported. Please use a base ID and API key, or connection string instead.");
    public override RelationalOptionsExtension WithCommandTimeout(int? commandTimeout)
        => throw new NotImplementedException();
    public override RelationalOptionsExtension WithMaxBatchSize(int? maxBatchSize)
        => throw new NotImplementedException();
    public override RelationalOptionsExtension WithMinBatchSize(int? minBatchSize)
        => throw new NotImplementedException();
    public override RelationalOptionsExtension WithUseRelationalNulls(bool useRelationalNulls)
        => throw new NotImplementedException();
    public override RelationalOptionsExtension WithUseQuerySplittingBehavior(QuerySplittingBehavior querySplittingBehavior)
        => throw new NotImplementedException();
    public override RelationalOptionsExtension WithMigrationsAssembly(string? migrationsAssembly)
        => throw new NotImplementedException();
    public override RelationalOptionsExtension WithMigrationsHistoryTableName(string? migrationsHistoryTableName)
        => throw new NotImplementedException();
    public override RelationalOptionsExtension WithMigrationsHistoryTableSchema(string? migrationsHistoryTableSchema)
        => throw new NotImplementedException();
    public override RelationalOptionsExtension WithExecutionStrategyFactory(Func<ExecutionStrategyDependencies, IExecutionStrategy>? executionStrategyFactory)
        => throw new NotImplementedException();

    public override int GetHashCode() => HashCode.Combine(ApiKey, BaseId);
    public override bool Equals(object? obj) => Equals(obj as AirtableOptionsExtension);
    public bool Equals(AirtableOptionsExtension? other) => other != null && Object.Equals(other?.ApiKey, ApiKey) && Object.Equals(other.BaseId, BaseId);

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        private string? _logFragment;

        public ExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        private new AirtableOptionsExtension Extension
            => (AirtableOptionsExtension)base.Extension;

        public override bool IsDatabaseProvider => true;
        public override string LogFragment
        {
            get
            {
                if (_logFragment == null)
                {
                    var builder = new StringBuilder();

                    builder.Append("ServiceEndPoint=").Append(Extension.BaseId);

                    _logFragment = builder.ToString();
                }

                return _logFragment;
            }
        }

        public override int GetServiceProviderHashCode()
        {
            return Extension.GetHashCode();
        }

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
        }

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other)
        {
            return other.Extension is AirtableOptionsExtension ext && Extension.Equals(ext);
        }
    }
}

using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.Scaffolding.Metadata;
using AirtableApiClient;
using Airtable.EFCore.Metadata.Conventions;
using Airtable.EFCore.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Airtable.EFCore.Scaffolding.Internal;

public class AirtableDatabaseModelFactory : DatabaseModelFactory
{
    private struct OneToManyLink
    {
        public DatabaseForeignKey ForeignKey;
        public string DestinationTableId;
    }

    private struct ManyToManyLink
    {
        public DatabaseTable SourceTable;
        public DatabaseColumn SourceColumn;
        public string DestinationTableId;
    }

    public AirtableDatabaseModelFactory(IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger)
    {
        _logger = logger;
    }

    IDiagnosticsLogger<DbLoggerCategory.Scaffolding> _logger;
    private Dictionary<string, FieldModel> _fieldMap = new();
    private Dictionary<string, DatabaseTable> _tableMap = new();
    private List<OneToManyLink> _oneToManyLinks = new();
    private List<ManyToManyLink> _manyToManyLinks = new();

    public override DatabaseModel Create(DbConnection connection, DatabaseModelFactoryOptions options)
        => throw new InvalidOperationException("Creating a DatabaseModel from a DbConnection is not supported. Please use a connection string instead.");

    public override DatabaseModel Create(string connectionString, DatabaseModelFactoryOptions options)
    {
        if (options.Schemas.Any())
        {
            _logger.Logger.LogWarning("Ignoring schema selection arguments, as schemas are not supported by Airtable.");
        }

        var tableModels = GetBaseSchema(connectionString).GetAwaiter().GetResult();

        _fieldMap.Clear();
        _tableMap.Clear();
        _oneToManyLinks.Clear();
        _manyToManyLinks.Clear();

        foreach (var tableModel in tableModels)
        {
            foreach (var fieldModel in tableModel.Fields)
            {
                _fieldMap.Add(fieldModel.Id, fieldModel);
            }
        }

        var tableSet = new HashSet<string>(options.Tables.ToList(), StringComparer.OrdinalIgnoreCase);
        var shouldRestrictTables = tableSet.Any();
        var databaseModel = new DatabaseModel();
        foreach (var tableModel in tableModels)
        {
            if (shouldRestrictTables && !tableSet.Contains(tableModel.Name))
            {
                continue;
            }
            tableSet.Remove(tableModel.Name);

            MakeTableModel(databaseModel, tableModel);
        }

        foreach (var missingTable in tableSet)
        {
            _logger.Logger.LogWarning("Unable to find selected table '{}' in Airtable base.", missingTable);
        }

        foreach (var link in _oneToManyLinks)
        {
            if (!_tableMap.TryGetValue(link.DestinationTableId, out var principalTable))
            {
                continue;
            }

            var table = link.ForeignKey.Table;
            table.ForeignKeys.Add(link.ForeignKey);
            var principalColumn = principalTable.PrimaryKey.Columns.First();
            link.ForeignKey.PrincipalTable = principalTable;
            link.ForeignKey.PrincipalColumns.Add(principalColumn);
        }

        foreach (var link in _manyToManyLinks)
        {
            if (!_tableMap.TryGetValue(link.DestinationTableId, out var destinationTable))
            {
                continue;
            }

            var destinationColumn = destinationTable.PrimaryKey.Columns.First();
            var sourceTable = link.SourceTable;
            var sourceColumn = sourceTable.PrimaryKey.Columns.First();

            var junctionTable = new DatabaseTable
            {
                Database = databaseModel,
                Name = $"Junction {sourceTable.Name} To {destinationTable.Name}",
            };
            databaseModel.Tables.Add(junctionTable);

            var (sourceString, destinationString) = sourceTable == destinationTable
                ? ("Source ", "Destination ")
                : ("", "");

            var sourceIdColumn = new DatabaseColumn
            {
                Table = junctionTable,
                Name = $"{sourceString}{sourceTable.Name} Id",
                StoreType = "singleLineText",
                IsNullable = false,
                DefaultValue = null,
                IsStored = true,
                ["ClrType"] = typeof(string),
            };
            junctionTable.Columns.Add(sourceIdColumn);

            var destinationIdColumn = new DatabaseColumn
            {
                Table = junctionTable,
                Name = $"{destinationString}{destinationTable.Name} Id",
                StoreType = "singleLineText",
                IsNullable = false,
                DefaultValue = null,
                IsStored = true,
                ["ClrType"] = typeof(string),
            };
            junctionTable.Columns.Add(destinationIdColumn);

            junctionTable.PrimaryKey = new DatabasePrimaryKey();
            junctionTable.PrimaryKey.Columns.Add(sourceIdColumn);
            junctionTable.PrimaryKey.Columns.Add(destinationIdColumn);

            var sourceForeignKey = new DatabaseForeignKey
            {
                Table = junctionTable,
                PrincipalTable = sourceTable,
            };
            sourceForeignKey.SetAnnotation(AirtableAnnotationNames.IsForwardDirectionForeignKey, true);
            sourceForeignKey.SetAnnotation(AirtableAnnotationNames.LinkIdColumn, link.SourceColumn);
            sourceForeignKey.Columns.Add(sourceIdColumn);
            sourceForeignKey.PrincipalColumns.Add(sourceColumn);
            junctionTable.ForeignKeys.Add(sourceForeignKey);

            var destinationForeignKey = new DatabaseForeignKey
            {
                Table = junctionTable,
                PrincipalTable = destinationTable,
            };
            destinationForeignKey.SetAnnotation(AirtableAnnotationNames.IsForwardDirectionForeignKey, false);
            destinationForeignKey.Columns.Add(destinationIdColumn);
            destinationForeignKey.PrincipalColumns.Add(destinationColumn);
            junctionTable.ForeignKeys.Add(destinationForeignKey);
        }

        return databaseModel;
    }

    private async Task<List<TableModel>> GetBaseSchema(string connectionString)
    {
        var connectionOptions = new AirtableDatabaseConnectionStringBuilder(connectionString);
        using var airtableBase = new AirtableBase(connectionOptions.ApiKey, connectionOptions.BaseId);
        var baseSchemaResponse = await airtableBase.GetBaseSchema();
        if (!baseSchemaResponse.Success) throw baseSchemaResponse.AirtableApiError;
        return baseSchemaResponse.Tables.ToList();
    }

    private void MakeTableModel(DatabaseModel databaseModel, TableModel tableModel)
    {
        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = tableModel.Name,
            Comment = tableModel.Description,
        };
        databaseModel.Tables.Add(table);

        // Create column for record ID, and make it the primary key.
        var primaryKeyColumn = new DatabaseColumn
        {
            Table = table,
            Name = "Id",
            IsNullable = false,
            StoreType = "recordId",
            ["ClrType"] = typeof(string),
            IsStored = true,
            ValueGenerated = ValueGenerated.OnAdd,
        };
        table.Columns.Add(primaryKeyColumn);

        _tableMap.Add(tableModel.Id, table);

        foreach (var fieldModel in tableModel.Fields)
        {
            MakeColumnModel(table, fieldModel, primaryKeyColumn);
        }

        table.PrimaryKey = new DatabasePrimaryKey
        {
            Name = primaryKeyColumn.Name,
        };
        table.PrimaryKey.Columns.Add(primaryKeyColumn);
    }

    private void MakeColumnModel(DatabaseTable table, FieldModel fieldModel, DatabaseColumn primaryKeyColumn)
    {
        var column = new DatabaseColumn
        {
            Table = table,
            Name = fieldModel.Name,
            IsNullable = true,
            StoreType = fieldModel.Type,
            DefaultValue = null,
            Comment = fieldModel.Description,
        };
        table.Columns.Add(column);

        var mapNumberToInt = false;
        var addCollection = false;

        switch (fieldModel.Type)
        {
            case "formula":
            {
                var options = ParseOptions<FormulaTypeOptions>(fieldModel.Options);
                if (options != null && options.Result != null)
                {
                    column.StoreType = options.Result.Type;
                    column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                }
                break;
            }
            case "rollup":
            {
                var options = ParseOptions<RollupTypeOptions>(fieldModel.Options);
                if (options != null && options.Result != null)
                {
                    column.StoreType = options.Result.Type;
                    column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                }
                break;
            }
            case "multipleLookupValues":
            {
                addCollection = true;
                var options = ParseOptions<LookupTypeOptions>(fieldModel.Options);
                if (options != null && options.Result != null)
                {
                    column.StoreType = options.Result.Type;
                    column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                    if (options.IsValid)
                    {
                        var linkField = _fieldMap[options.RecordLinkFieldId];
                        if (linkField.Type == "multipleRecordLinks")
                        {
                            var linkOptions = ParseOptions<MultipleRecordLinksTypeOptions>(linkField.Options);
                            if (linkOptions != null && linkOptions.PrefersSingleRecordLink)
                            {
                                addCollection = false;
                            }
                        }
                    }
                }
                break;
            }
            case "createdTime":
            {
                var options = ParseOptions<CreatedTimeTypeOptions>(fieldModel.Options);
                if (options != null && options.Result != null)
                {
                    if (options.Result.Type == "dateTime" || options.Result.Type == "date")
                    {
                        column.StoreType = options.Result.Type;
                        column.ValueGenerated = ValueGenerated.OnAdd;
                    }
                }
                break;
            }
            case "lastModifiedTime":
            {
                var options = ParseOptions<LastModifiedTimeTypeOptions>(fieldModel.Options);
                if (options != null && options.IsValid && options.Result != null)
                {
                    if (options.Result.Type == "dateTime" || options.Result.Type == "date")
                    {
                        column.StoreType = options.Result.Type;
                        column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                    }
                }
                break;
            }
            case "autoNumber":
            {
                column.StoreType = "number";
                column.ValueGenerated = ValueGenerated.OnAdd;
                mapNumberToInt = true;
                break;
            }
            case "count":
            {
                column.StoreType = "number";
                column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                mapNumberToInt = true;
                break;
            }
            case "createdBy":
            {
                column.StoreType = "singleCollaborator";
                column.ValueGenerated = ValueGenerated.OnAdd;
                break;
            }
            case "lastModifiedBy":
            {
                column.StoreType = "singleCollaborator";
                column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                break;
            }
        }

        switch (column.StoreType)
        {
            case "number":
            {
                var options = ParseOptions<NumberTypeOptions>(fieldModel.Options);
                if (options != null && options.Precision == 0)
                {
                    mapNumberToInt = true;
                }
                break;
            }
            case "currency":
            {
                var options = ParseOptions<CurrencyTypeOptions>(fieldModel.Options);
                column.StoreType = "number";
                if (options != null && options.Precision == 0)
                {
                    mapNumberToInt = true;
                }
                break;
            }
            case "percent":
            {
                column.StoreType = "number";
                // No need to parse options here. An integer percentage like 12% would be represented as 0.12, so we need a floating point number regardless of precision.
                break;
            }
            case "rating":
            {
                var options = ParseOptions<RatingTypeOptions>(fieldModel.Options);
                column.StoreType = "number";
                mapNumberToInt = true;
                if (options != null && column.Comment == null)
                {
                    column.Comment = $"Range: 1-{options.Max} {options.Icon}s";
                }
                break;
            }
            case "checkbox":
            {
                column.IsNullable = false;
                break;
            }
            case "singleSelect":
            {
                column.StoreType = "singleLineText";
                break;
            }
            case "multipleSelects":
            {
                column.StoreType = "singleLineText";
                addCollection = true;
                break;
            }
            case "externalSyncSource":
            {
                column.StoreType = "singleLineText";
                break;
            }
        }

        if (fieldModel.Type == "multipleRecordLinks")
        {
            var linkOptions = ParseOptions<MultipleRecordLinksTypeOptions>(fieldModel.Options);
            if (linkOptions != null)
            {
                if (linkOptions.PrefersSingleRecordLink)
                {
                    // If there's (probably) only one link, then map to a simple foreign key.
                    var foreignKey = new DatabaseForeignKey
                    {
                        Table = table,
                    };
                    foreignKey.Columns.Add(column);
                    _oneToManyLinks.Add(new OneToManyLink {
                        ForeignKey = foreignKey,
                        DestinationTableId = linkOptions.LinkedTableId });
                    table.ForeignKeys.Add(foreignKey);

                    column.StoreType = "singleLineText";
                    column.SetAnnotation(AirtableAnnotationNames.IsLinkIdColumn, true);
                }
                else
                {
                    // If there may be more than one link, then map to an imaginary junction table.
                    _manyToManyLinks.Add(new ManyToManyLink {
                        SourceTable = table,
                        SourceColumn = column,
                        DestinationTableId = linkOptions.LinkedTableId });

                    addCollection = true;
                    column.StoreType = "singleLineText";
                    column.SetAnnotation(AirtableAnnotationNames.IsPluralLinkIdColumn, true);
                }
            }
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(fieldModel.Name.Trim(), "id"))
        {
            // If there is already a field named "Id", rename the record ID column to "Record Id".
            primaryKeyColumn.Name = "Record Id";
        }

        if (AirtableTypeMappingSource.TypeHints.TryGetValue(column.StoreType, out var clrType))
        {
            if (column.StoreType == "number" && mapNumberToInt)
            {
                clrType = typeof(int);
            }
            if (addCollection)
            {
                clrType = typeof(ICollection<>).MakeGenericType(clrType);
            }
            column["ClrType"] = clrType;
            column.IsStored = column.ValueGenerated != null;
        }
    }

#nullable enable
    private TypeOptions? ParseOptions<TypeOptions>(object options)
        where TypeOptions: class
    {
        var optionsElement = options as JsonElement?;
        if (optionsElement == null)
        {
            return null;
        }

        var optionsJson = JsonSerializer.Serialize(optionsElement);
        return JsonSerializer.Deserialize<TypeOptions>(optionsJson);
    }
#nullable restore

}

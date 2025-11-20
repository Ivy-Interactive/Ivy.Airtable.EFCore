using System.Data.Common;
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
    public AirtableDatabaseModelFactory(IDiagnosticsLogger<DbLoggerCategory.Scaffolding> logger)
    {
        _logger = logger;
    }

    readonly IDiagnosticsLogger<DbLoggerCategory.Scaffolding> _logger;
    private readonly Dictionary<string, FieldModel> _fieldMap = new();
    private readonly Dictionary<string, DatabaseTable> _tableMap = new();

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

        foreach (var tableModel in tableModels)
        {
            foreach (var fieldModel in tableModel.Fields)
            {
                _fieldMap.Add(fieldModel.Id!, fieldModel);
            }
        }

        var tableSet = new HashSet<string>(options.Tables.ToList(), StringComparer.OrdinalIgnoreCase);
        var shouldRestrictTables = tableSet.Any();
        var databaseModel = new DatabaseModel();
        foreach (var tableModel in tableModels)
        {
            if (shouldRestrictTables && !tableSet.Contains(tableModel.Name!))
            {
                continue;
            }
            tableSet.Remove(tableModel.Name!);

            MakeTableModel(databaseModel, tableModel);
        }

        foreach (var missingTable in tableSet)
        {
            _logger.Logger.LogWarning("Unable to find selected table '{}' in Airtable base.", missingTable);
        }

        return databaseModel;
    }

    private async Task<List<TableModel>> GetBaseSchema(string connectionString)
    {
        var connectionOptions = new AirtableDatabaseConnectionStringBuilder(connectionString);
        using var airtableBase = new AirtableBase(connectionOptions.ApiKey, connectionOptions.BaseId);
        var baseSchemaResponse = await airtableBase.GetBaseSchema();
        if (!baseSchemaResponse.Success)
        {
            throw baseSchemaResponse.AirtableApiError ?? new Exception("Unknown error retrieving base schema.");
        }
        return baseSchemaResponse.Tables?.ToList() ?? [];
    }

    private void MakeTableModel(DatabaseModel databaseModel, TableModel tableModel)
    {
        var table = new DatabaseTable
        {
            Database = databaseModel,
            Name = tableModel.Name!,
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

        _tableMap.Add(tableModel.Id!, table);

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
            Name = fieldModel.Name!,
            IsNullable = true,
            StoreType = fieldModel.Type.ToApiString(),
            DefaultValue = null,
            Comment = fieldModel.Description,
        };
        table.Columns.Add(column);

        var mapNumberToInt = false;
        var addCollection = false;

        var mappedFieldType = fieldModel.Type;
        var mappedFieldModel = fieldModel;
        switch (fieldModel.Type)
        {
            case FieldEnum.Formula:
            {
                if (fieldModel.TryGetOptions<FormulaModelOptions>(out var options) && options?.Result != null)
                {
                    mappedFieldType = options.Result.Type;
                    mappedFieldModel = options.Result;
                    column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                }
                break;
            }
            case FieldEnum.Rollup:
            {
                if (fieldModel.TryGetOptions<RollupModelOptions>(out var options) && options?.Result != null)
                {
                    mappedFieldType = options.Result.Type;
                    mappedFieldModel = options.Result;
                    column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                }
                break;
            }
            case FieldEnum.MultipleLookupValues:
            {
                addCollection = true;
                if (fieldModel.TryGetOptions<LookupModelOptions>(out var options) && options?.Result != null)
                {
                    mappedFieldType = options.Result.Type;
                    mappedFieldModel = options.Result;
                    column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                    if (options.IsValid && options.RecordLinkFieldId != null)
                    {
                        var linkField = _fieldMap[options.RecordLinkFieldId];
                        if (linkField.Type == FieldEnum.MultipleRecordLinks)
                        {
                            if (linkField.TryGetOptions<LinkToAnotherRecordModelOptions>(out var linkOptions) && linkOptions != null && linkOptions.PrefersSingleRecordLink)
                            {
                                addCollection = false;
                            }
                        }
                    }
                }
                break;
            }
            case FieldEnum.CreatedTime:
            {
                if (fieldModel.TryGetOptions<CreatedTimeModelOptions>(out var options) && options?.Result != null)
                {
                    if (options.Result.Type is FieldEnum.DateTime or FieldEnum.Date)
                    {
                        mappedFieldType = options.Result.Type;
                        mappedFieldModel = options.Result;
                        column.ValueGenerated = ValueGenerated.OnAdd;
                    }
                }
                break;
            }
            case FieldEnum.LastModifiedTime:
            {
                if (fieldModel.TryGetOptions<LastModifiedTimeModelOptions>(out var options) && options?.Result != null)
                {
                    if (options.Result.Type is FieldEnum.DateTime or FieldEnum.Date)
                    {
                        mappedFieldType = options.Result.Type;
                        mappedFieldModel = options.Result;
                        column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                    }
                }
                break;
            }
            case FieldEnum.AutoNumber:
            {
                mappedFieldType = FieldEnum.Number;
                column.ValueGenerated = ValueGenerated.OnAdd;
                mapNumberToInt = true;
                break;
            }
            case FieldEnum.Count:
            {
                mappedFieldType = FieldEnum.Number;
                column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                mapNumberToInt = true;
                break;
            }
            case FieldEnum.CreatedBy:
            {
                mappedFieldType = FieldEnum.SingleCollaborator;
                column.ValueGenerated = ValueGenerated.OnAdd;
                break;
            }
            case FieldEnum.LastModifiedBy:
            {
                mappedFieldType = FieldEnum.SingleCollaborator;
                column.ValueGenerated = ValueGenerated.OnAddOrUpdate;
                break;
            }
        }

        switch (mappedFieldType)
        {
            case FieldEnum.Number:
            {
                if (mappedFieldModel.TryGetOptions<NumberModelOptions>(out var options) && options != null && options.Precision == 0)
                {
                    mapNumberToInt = true;
                }
                break;
            }
            case FieldEnum.Currency:
            {
                mappedFieldType = FieldEnum.Number;
                if (mappedFieldModel.TryGetOptions<CurrencyModelOptions>(out var options) && options != null && options.Precision == 0)
                {
                    mapNumberToInt = true;
                }
                break;
            }
            case FieldEnum.Percent:
            {
                mappedFieldType = FieldEnum.Number;
                // No need to parse options here. An integer percentage like 12% would be represented as 0.12, so we need a floating point number regardless of precision.
                break;
            }
            case FieldEnum.Rating:
            {
                mappedFieldType = FieldEnum.Number;
                mapNumberToInt = true;
                if (mappedFieldModel.TryGetOptions<RatingModelOptions>(out var options) && options != null && column.Comment == null)
                {
                    column.Comment = $"Range: 1-{options.Max} {options.Icon}s";
                }
                break;
            }
        }

        switch (mappedFieldType)
        {
            case FieldEnum.Checkbox:
            {
                column.IsNullable = false;
                break;
            }
            case FieldEnum.SingleSelect:
            {
                mappedFieldType = FieldEnum.SingleLineText;
                break;
            }
            case FieldEnum.MultipleSelects:
            {
                mappedFieldType = FieldEnum.SingleLineText;
                addCollection = true;
                break;
            }
            case FieldEnum.ExternalSyncSource:
            {
                mappedFieldType = FieldEnum.SingleLineText;
                break;
            }
        }

        if (fieldModel.Type is FieldEnum.MultipleRecordLinks)
        {
            if (fieldModel.TryGetOptions<LinkToAnotherRecordModelOptions>(out var linkOptions) && linkOptions != null)
            {
                if (linkOptions.PrefersSingleRecordLink)
                {
                    mappedFieldType = FieldEnum.SingleLineText;
                    column.SetAnnotation(AirtableAnnotationNames.IsLinkIdColumn, true);
                }
                else
                {
                    addCollection = true;
                    mappedFieldType = FieldEnum.SingleLineText;
                    column.SetAnnotation(AirtableAnnotationNames.IsPluralLinkIdColumn, true);
                }
            }
        }

        column.StoreType = mappedFieldType.ToApiString();

        if (StringComparer.OrdinalIgnoreCase.Equals(fieldModel.Name?.Trim(), "id"))
        {
            // If there is already a field named "Id", rename the record ID column to "Record Id".
            primaryKeyColumn.Name = "Record Id";
        }

        if (AirtableTypeMappingSource.TypeHints.TryGetValue(mappedFieldType, out var clrType))
        {
            if (mappedFieldType is FieldEnum.Number && mapNumberToInt)
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
}

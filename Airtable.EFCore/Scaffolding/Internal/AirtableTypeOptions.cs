using System.Text.Json.Serialization;

#nullable enable

namespace Airtable.EFCore.Scaffolding.Internal;

internal class FormulaTypeOptions
{
    [JsonPropertyName("formula")]
    public string Formula { get; set; }

    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("referencedFieldIds")]
    public string[]? ReferencedFieldIds { get; set; }

    [JsonPropertyName("result")]
    public FieldTypeAndOptions? Result { get; set; }
}

internal class MultipleRecordLinksTypeOptions
{
    [JsonPropertyName("isReversed")]
    public bool IsReversed { get; set; }

    [JsonPropertyName("linkedTableId")]
    public string LinkedTableId { get; set; }

    [JsonPropertyName("prefersSingleRecordLink")]
    public bool PrefersSingleRecordLink { get; set; }

    [JsonPropertyName("inverseLinkFieldId")]
    public string? InverseLinkFieldId { get; set; }

    [JsonPropertyName("viewIdForRecordSelection")]
    public string? ViewIdForRecordSelection { get; set; }
}

internal class LookupTypeOptions
{
    [JsonPropertyName("fieldIdInLinkedTable")]
    public string? FieldIdInLinkedTable { get; set; }

    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("recordLinkFieldId")]
    public string? RecordLinkFieldId { get; set; }

    [JsonPropertyName("result")]
    public FieldTypeAndOptions? Result { get; set; }
}

// Used for both "multipleSelects" and "singleSelect" types
internal class SelectTypeOptions
{
    [JsonPropertyName("choices")]
    public SelectChoice[] Choices { get; set; }
}

internal class SelectChoice
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("id")]
    public string? Color { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

// Used for both "number" and "percent" types
internal class NumberTypeOptions
{
    [JsonPropertyName("precision")]
    public int Precision { get; set; }
}

internal class CurrencyTypeOptions
{
    [JsonPropertyName("precision")]
    public int Precision { get; set; }

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; }
}

internal class RatingTypeOptions
{
    [JsonPropertyName("color")]
    public string Color { get; set; }

    [JsonPropertyName("icon")]
    public string Icon { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}

internal class RollupTypeOptions
{
    [JsonPropertyName("fieldIdInLinkedTable")]
    public string? FieldIdInLinkedTable { get; set; }

    [JsonPropertyName("recordLinkFieldId")]
    public string? RecordLinkFieldId { get; set; }

    [JsonPropertyName("result")]
    public FieldTypeAndOptions? Result { get; set; }

    [JsonPropertyName("isValid")]
    public bool? IsValid { get; set; }

    [JsonPropertyName("referencedFieldIds")]
    public string[]? ReferencedFieldIds { get; set; }
}

internal class CreatedTimeTypeOptions
{
    [JsonPropertyName("result")]
    public FieldTypeAndOptions? Result { get; set; }
}

internal class LastModifiedTimeTypeOptions
{
    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("referencedFieldIds")]
    public string[]? ReferencedFieldIds { get; set; }

    [JsonPropertyName("result")]
    public FieldTypeAndOptions? Result { get; set; }
}

internal class FieldTypeAndOptions
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("options")]
    public object Options { get; set; }
}

using System.Text.Json.Serialization;

#nullable enable

namespace Airtable.EFCore;

public class AirtableAiText
{
    [JsonPropertyName("state")]
    [JsonInclude]
    public string State { get; internal set; }

    [JsonPropertyName("isStale")]
    [JsonInclude]
    public bool IsStale { get; internal set; }

    [JsonPropertyName("value")]
    [JsonInclude]
    public string? Value { get; internal set; }

    [JsonPropertyName("errorType")]
    [JsonInclude]
    public string? ErrorType { get; internal set; }
}

public class AirtableUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("name")]
    [JsonInclude]
    public string? Name { get; internal set; }

    [JsonPropertyName("permissionLevel")]
    [JsonInclude]
    public string? PermissionLevel { get; internal set; }

    [JsonPropertyName("profilePicUrl")]
    [JsonInclude]
    public string? ProfilePicUrl { get; internal set; }
}

public class AirtableBarcode
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }
}

public class AirtableButton
{
    [JsonPropertyName("label")]
    public string Label { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

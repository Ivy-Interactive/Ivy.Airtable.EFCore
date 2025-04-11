using System.Text.Json.Serialization;
using AirtableApiClient;

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

public class AirtableUser : IEquatable<AirtableUser>
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

    internal class AirtableUserId
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
    }

    internal class AirtableUserEmail
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }

    internal object WrittenVersion()
    {
        if (Id != null)
        {
            return new AirtableUserId() { Id = Id };
        }
        else
        {
            return new AirtableUserEmail() { Email = Email };
        }
    }

    public AirtableUser Clone()
        => new()
        {
            Id = Id,
            Email = Email,
            Name = Name,
            PermissionLevel = PermissionLevel,
            ProfilePicUrl = ProfilePicUrl,
        };

    public override bool Equals(object? obj) =>
        Equals(obj as AirtableUser);

    public bool Equals(AirtableUser? other) =>
        other != null && Id == other.Id && Email == other.Email;

    public override int GetHashCode() =>
        HashCode.Combine(Id, Email);

    public static bool operator ==(AirtableUser? left, AirtableUser? right) =>
        EqualityComparer<AirtableUser>.Default.Equals(left, right);

    public static bool operator !=(AirtableUser? left, AirtableUser? right) =>
        !(left == right);
}

public class AirtableBarcode : IEquatable<AirtableBarcode>
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }

    public AirtableBarcode Clone()
        => new()
        {
            Type = Type,
            Text = Text,
        };

    public override bool Equals(object? obj) =>
        Equals(obj as AirtableBarcode);

    public bool Equals(AirtableBarcode? other) =>
        other != null && Type == other.Type && Text == other.Text;

    public override int GetHashCode() =>
        HashCode.Combine(Type, Text);

    public static bool operator ==(AirtableBarcode? left, AirtableBarcode? right) =>
        EqualityComparer<AirtableBarcode>.Default.Equals(left, right);

    public static bool operator !=(AirtableBarcode? left, AirtableBarcode? right) =>
        !(left == right);
}

public class AirtableButton
{
    [JsonPropertyName("label")]
    public string Label { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

internal static class AirtableAttachmentExtensions
{
    internal class AirtableAttachmentId
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
    }

    internal class AirtableAttachmentUrl
    {

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }

    internal class AirtableAttachmentUrlAndFilename
    {

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("filename")]
        public string? Filename { get; set; }
    }

    internal static object WrittenVersion(this AirtableAttachment attachment)
    {
        if (attachment.Id != null)
        {
            return new AirtableAttachmentId() { Id = attachment.Id };
        }
        else if (attachment.Filename != null)
        {
            return new AirtableAttachmentUrlAndFilename()
            {
                Url = attachment.Url,
                Filename = attachment.Filename,
            };
        }
        else
        {
            return new AirtableAttachmentUrl()
            {
                Url = attachment.Url,
            };
        }
    }
}

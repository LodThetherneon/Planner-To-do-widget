using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlannerWidget.Core.Graph;

// A Graph JSON válaszok nyers alakja. Kívülre csak a Models névtér rekordjai mennek.

internal sealed class GraphPage<T>
{
    [JsonPropertyName("value")] public List<T> Value { get; set; } = [];
    [JsonPropertyName("@odata.nextLink")] public string? NextLink { get; set; }
}

internal sealed class TaskDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("planId")] public string? PlanId { get; set; }
    [JsonPropertyName("bucketId")] public string? BucketId { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("percentComplete")] public int? PercentComplete { get; set; }
    [JsonPropertyName("dueDateTime")] public DateTimeOffset? DueDateTime { get; set; }
    [JsonPropertyName("priority")] public int? Priority { get; set; }
    [JsonPropertyName("@odata.etag")] public string? ETag { get; set; }
    [JsonPropertyName("checklistItemCount")] public int? ChecklistItemCount { get; set; }
    [JsonPropertyName("activeChecklistItemCount")] public int? ActiveChecklistItemCount { get; set; }
    [JsonPropertyName("hasDescription")] public bool? HasDescription { get; set; }
    [JsonPropertyName("createdDateTime")] public DateTimeOffset? CreatedDateTime { get; set; }
    [JsonPropertyName("completedDateTime")] public DateTimeOffset? CompletedDateTime { get; set; }
}

internal sealed class PlanDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
}

internal sealed class BucketDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("orderHint")] public string? OrderHint { get; set; }
}

internal sealed class UserDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    [JsonPropertyName("mail")] public string? Mail { get; set; }
    [JsonPropertyName("userPrincipalName")] public string? UserPrincipalName { get; set; }
}

internal sealed class TaskDetailsDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("@odata.etag")] public string? ETag { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }

    /// <summary>A checklist egy objektum, amelynek kulcsai az elem-azonosítók.</summary>
    [JsonPropertyName("checklist")] public Dictionary<string, ChecklistItemDto>? Checklist { get; set; }
}

internal sealed class ChecklistItemDto
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("isChecked")] public bool? IsChecked { get; set; }
    [JsonPropertyName("orderHint")] public string? OrderHint { get; set; }
}

internal sealed class GraphErrorEnvelope
{
    [JsonPropertyName("error")] public GraphErrorDto? Error { get; set; }
}

internal sealed class GraphErrorDto
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

internal static class GraphJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

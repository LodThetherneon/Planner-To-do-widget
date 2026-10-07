using System.Net;
using System.Text.Json.Serialization;

namespace PlannerWidget.Core.Graph;

/// <summary>Egy Microsoft To Do lista.</summary>
public sealed record TodoList(string Id, string DisplayName);

/// <summary>Egy To Do feladat (a szinkronhibák és az életjel forrása).</summary>
public sealed record TodoTask(
    string Id,
    string Title,
    string Status,
    DateTimeOffset? CreatedDateTime,
    DateTimeOffset? LastModifiedDateTime,
    string BodyContent)
{
    public bool IsCompleted => string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Microsoft To Do (Graph /me/todo). A meglévő Tasks.ReadWrite jogosultság elég hozzá – új scope nem kell.
/// </summary>
public interface ITodoClient
{
    Task<IReadOnlyList<TodoList>> GetTodoListsAsync(CancellationToken ct = default);

    /// <summary>A lista nem befejezett feladatai (szerveroldali szűréssel, ha megy; kliensoldalon mindig szűrünk).</summary>
    Task<IReadOnlyList<TodoTask>> GetOpenTodoTasksAsync(string listId, CancellationToken ct = default);

    /// <summary>PATCH status=completed.</summary>
    Task CompleteTodoTaskAsync(string listId, string taskId, CancellationToken ct = default);
}

public sealed partial class PlannerClient : ITodoClient
{
    public async Task<IReadOnlyList<TodoList>> GetTodoListsAsync(CancellationToken ct = default)
    {
        var dtos = await GetAllPagesAsync<TodoListDto>("me/todo/lists", ct).ConfigureAwait(false);
        return dtos.Where(d => !string.IsNullOrEmpty(d.Id)).Select(d => new TodoList(d.Id!, d.DisplayName ?? "")).ToList();
    }

    public async Task<IReadOnlyList<TodoTask>> GetOpenTodoTasksAsync(string listId, CancellationToken ct = default)
    {
        var baseUrl = $"me/todo/lists/{Esc(listId)}/tasks";
        List<TodoTaskDto> dtos;
        try
        {
            dtos = await GetAllPagesAsync<TodoTaskDto>(baseUrl + "?$filter=" + Uri.EscapeDataString("status ne 'completed'"), ct)
                .ConfigureAwait(false);
        }
        catch (GraphApiException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotImplemented)
        {
            // Ha a szűrő nem támogatott, mindent lekérünk, és kliensoldalon szűrünk.
            dtos = await GetAllPagesAsync<TodoTaskDto>(baseUrl, ct).ConfigureAwait(false);
        }

        return dtos
            .Where(d => !string.IsNullOrEmpty(d.Id))
            .Select(d => new TodoTask(d.Id!, d.Title ?? "", d.Status ?? "", d.CreatedDateTime, d.LastModifiedDateTime, d.Body?.Content ?? ""))
            .Where(t => !t.IsCompleted)
            .ToList();
    }

    public async Task CompleteTodoTaskAsync(string listId, string taskId, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["status"] = "completed" };
        using var res = await SendAsync(
            () => JsonRequest(HttpMethod.Patch, $"me/todo/lists/{Esc(listId)}/tasks/{Esc(taskId)}", body),
            idempotent: true, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(res, ct).ConfigureAwait(false);
    }
}

internal sealed class TodoListDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
}

internal sealed class TodoTaskDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("createdDateTime")] public DateTimeOffset? CreatedDateTime { get; set; }
    [JsonPropertyName("lastModifiedDateTime")] public DateTimeOffset? LastModifiedDateTime { get; set; }
    [JsonPropertyName("body")] public TodoBodyDto? Body { get; set; }
}

internal sealed class TodoBodyDto
{
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("contentType")] public string? ContentType { get; set; }
}

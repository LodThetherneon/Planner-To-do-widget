using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PlannerWidget.Core.Models;

namespace PlannerWidget.Core.Graph;

public interface IPlannerClient
{
    Task<IReadOnlyList<PlannerTask>> GetMyTasksAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PlannerPlan>> GetMyPlansAsync(CancellationToken ct = default);
    Task<PlannerPlan?> GetPlanAsync(string planId, CancellationToken ct = default);
    Task<IReadOnlyList<PlannerBucket>> GetBucketsAsync(string planId, CancellationToken ct = default);
    Task<UserProfile> GetMeAsync(CancellationToken ct = default);
    Task<PlannerTask> GetTaskAsync(string taskId, CancellationToken ct = default);
    Task<PlannerTask> CreateTaskAsync(NewTaskRequest request, CancellationToken ct = default);
    Task<PlannerTask> UpdateTaskAsync(PlannerTask task, TaskPatch patch, CancellationToken ct = default);
    Task DeleteTaskAsync(PlannerTask task, CancellationToken ct = default);
    Task<PlannerTaskDetails> GetTaskDetailsAsync(string taskId, CancellationToken ct = default);
    Task<PlannerTaskDetails> SetChecklistItemCheckedAsync(PlannerTaskDetails details, string itemId, bool isChecked, CancellationToken ct = default);
    Task<PlannerTaskDetails> SetChecklistItemTitleAsync(PlannerTaskDetails details, string itemId, string title, CancellationToken ct = default);
}

public sealed record NewTaskRequest(
    string PlanId,
    string BucketId,
    string Title,
    string AssigneeUserId,
    DateOnly? DueDate,
    TaskPriority Priority = TaskPriority.Medium);

/// <summary>Részleges módosítás: csak a nem null mezők kerülnek a PATCH törzsbe.</summary>
public sealed record TaskPatch
{
    public string? Title { get; init; }
    public int? PercentComplete { get; init; }
    public TaskPriority? Priority { get; init; }

    /// <summary>Ha igaz, a <see cref="DueDate"/> értéke (akár null = határidő törlése) bekerül a kérésbe.</summary>
    public bool ChangeDueDate { get; init; }
    public DateOnly? DueDate { get; init; }

    /// <summary>Áthelyezés másik gyűjtőbe (bucket) – ügyeknél ez a státuszváltás.</summary>
    public string? BucketId { get; init; }

    public bool IsEmpty => Title is null && PercentComplete is null && Priority is null && !ChangeDueDate && BucketId is null;

    internal Dictionary<string, object?> ToBody()
    {
        var body = new Dictionary<string, object?>();
        if (Title is not null) body["title"] = Title;
        if (PercentComplete is not null) body["percentComplete"] = PercentComplete;
        if (Priority is not null) body["priority"] = Priority.Value.ToPlanner();
        if (ChangeDueDate) body["dueDateTime"] = DueDateConverter.ToGraph(DueDate);
        if (BucketId is not null) body["bucketId"] = BucketId;
        return body;
    }
}

/// <summary>
/// Vékony, típusos Microsoft Graph kliens a Planner végpontokhoz.
/// Kezeli: lapozás (@odata.nextLink), átmeneti hibák újrapróbálása (429/503/504, Retry-After),
/// ETag-ütközés (412) esetén friss ETag lekérése és egy újrapróbálás.
/// </summary>
public sealed partial class PlannerClient : IPlannerClient
{
    private const int MaxAttempts = 4;
    private readonly HttpClient _http;
    private readonly IAccessTokenProvider _tokens;

    public PlannerClient(HttpClient http, IAccessTokenProvider tokens)
    {
        _http = http;
        _tokens = tokens;
        _http.BaseAddress ??= new Uri(GraphConfig.BaseUrl);
    }

    public async Task<IReadOnlyList<PlannerTask>> GetMyTasksAsync(CancellationToken ct = default)
    {
        var dtos = await GetAllPagesAsync<TaskDto>("me/planner/tasks", ct).ConfigureAwait(false);
        return dtos.Select(Map).Where(t => t is not null).Select(t => t!).ToList();
    }

    public async Task<IReadOnlyList<PlannerPlan>> GetMyPlansAsync(CancellationToken ct = default)
    {
        var dtos = await GetAllPagesAsync<PlanDto>("me/planner/plans", ct).ConfigureAwait(false);
        return dtos.Where(p => !string.IsNullOrEmpty(p.Id))
            .Select(p => new PlannerPlan(p.Id!, p.Title ?? ""))
            .ToList();
    }

    public async Task<PlannerPlan?> GetPlanAsync(string planId, CancellationToken ct = default)
    {
        try
        {
            var dto = await GetJsonAsync<PlanDto>($"planner/plans/{Esc(planId)}", ct).ConfigureAwait(false);
            return dto.Id is null ? null : new PlannerPlan(dto.Id, dto.Title ?? "");
        }
        catch (GraphApiException ex) when (ex.IsNotFound || ex.IsForbidden)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<PlannerBucket>> GetBucketsAsync(string planId, CancellationToken ct = default)
    {
        var dtos = await GetAllPagesAsync<BucketDto>($"planner/plans/{Esc(planId)}/buckets", ct).ConfigureAwait(false);
        return dtos.Where(b => !string.IsNullOrEmpty(b.Id))
            .Select(b => new PlannerBucket(b.Id!, b.Name ?? "", b.OrderHint ?? ""))
            .ToList();
    }

    public async Task<UserProfile> GetMeAsync(CancellationToken ct = default)
    {
        var dto = await GetJsonAsync<UserDto>("me?$select=id,displayName,mail,userPrincipalName", ct).ConfigureAwait(false);
        return new UserProfile(dto.Id ?? "", (dto.DisplayName ?? "").Trim(), dto.Mail ?? dto.UserPrincipalName);
    }

    public async Task<PlannerTask> GetTaskAsync(string taskId, CancellationToken ct = default)
    {
        var dto = await GetJsonAsync<TaskDto>($"planner/tasks/{Esc(taskId)}", ct).ConfigureAwait(false);
        return Map(dto) ?? throw new GraphApiException(HttpStatusCode.NotFound, "A feladat nem található.");
    }

    public async Task<PlannerTask> CreateTaskAsync(NewTaskRequest request, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["planId"] = request.PlanId,
            ["bucketId"] = request.BucketId,
            ["title"] = request.Title,
            ["priority"] = request.Priority.ToPlanner(),
            ["assignments"] = new Dictionary<string, object>
            {
                [request.AssigneeUserId] = new Dictionary<string, string>
                {
                    ["@odata.type"] = "#microsoft.graph.plannerAssignment",
                    ["orderHint"] = " !",
                },
            },
        };
        if (request.DueDate is not null)
        {
            body["dueDateTime"] = DueDateConverter.ToGraph(request.DueDate);
        }

        // POST nem idempotens: hálózati hiba esetén nem próbáljuk újra (duplikált feladat elkerülése).
        using var res = await SendAsync(() => JsonRequest(HttpMethod.Post, "planner/tasks", body), idempotent: false, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(res, ct).ConfigureAwait(false);
        var dto = await res.Content.ReadFromJsonAsync<TaskDto>(GraphJson.Options, ct).ConfigureAwait(false);
        return Map(dto!) ?? throw new GraphApiException(res.StatusCode, "Üres válasz a feladat létrehozásakor.");
    }

    public async Task<PlannerTask> UpdateTaskAsync(PlannerTask task, TaskPatch patch, CancellationToken ct = default)
    {
        if (patch.IsEmpty)
        {
            return task;
        }

        var body = patch.ToBody();
        var etag = task.ETag;
        for (var attempt = 0; ; attempt++)
        {
            using var res = await SendAsync(() =>
            {
                var req = JsonRequest(HttpMethod.Patch, $"planner/tasks/{Esc(task.Id)}", body);
                req.Headers.TryAddWithoutValidation("If-Match", etag);
                req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
                return req;
            }, idempotent: true, ct).ConfigureAwait(false);

            if (res.StatusCode == HttpStatusCode.PreconditionFailed && attempt == 0)
            {
                // Valaki (pl. a Planner webes felülete) közben módosította: friss ETag és újrapróbálás.
                etag = (await GetTaskAsync(task.Id, ct).ConfigureAwait(false)).ETag;
                continue;
            }

            await EnsureSuccessAsync(res, ct).ConfigureAwait(false);
            if (res.StatusCode == HttpStatusCode.NoContent)
            {
                return await GetTaskAsync(task.Id, ct).ConfigureAwait(false);
            }

            var dto = await res.Content.ReadFromJsonAsync<TaskDto>(GraphJson.Options, ct).ConfigureAwait(false);
            return (dto is null ? null : Map(dto)) ?? await GetTaskAsync(task.Id, ct).ConfigureAwait(false);
        }
    }

    public async Task DeleteTaskAsync(PlannerTask task, CancellationToken ct = default)
    {
        var etag = task.ETag;
        for (var attempt = 0; ; attempt++)
        {
            using var res = await SendAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Delete, $"planner/tasks/{Esc(task.Id)}");
                req.Headers.TryAddWithoutValidation("If-Match", etag);
                return req;
            }, idempotent: true, ct).ConfigureAwait(false);

            if (res.StatusCode == HttpStatusCode.NotFound)
            {
                return; // Már törölve van – a cél teljesült.
            }

            if (res.StatusCode == HttpStatusCode.PreconditionFailed && attempt == 0)
            {
                etag = (await GetTaskAsync(task.Id, ct).ConfigureAwait(false)).ETag;
                continue;
            }

            await EnsureSuccessAsync(res, ct).ConfigureAwait(false);
            return;
        }
    }

    public async Task<PlannerTaskDetails> GetTaskDetailsAsync(string taskId, CancellationToken ct = default)
    {
        var dto = await GetJsonAsync<TaskDetailsDto>($"planner/tasks/{Esc(taskId)}/details", ct).ConfigureAwait(false);
        return Map(taskId, dto);
    }

    public Task<PlannerTaskDetails> SetChecklistItemCheckedAsync(
        PlannerTaskDetails details, string itemId, bool isChecked, CancellationToken ct = default) =>
        PatchChecklistItemAsync(details, itemId, "isChecked", isChecked, ct);

    public Task<PlannerTaskDetails> SetChecklistItemTitleAsync(
        PlannerTaskDetails details, string itemId, string title, CancellationToken ct = default)
    {
        title = title.Trim();
        if (title.Length == 0 || title.Length > ChecklistItem.MaxTitleLength)
        {
            throw new ArgumentException($"Az ellenőrzőlista-elem szövege 1–{ChecklistItem.MaxTitleLength} karakter lehet.", nameof(title));
        }

        return PatchChecklistItemAsync(details, itemId, "title", title, ct);
    }

    /// <summary>Egy ellenőrzőlista-elem egy mezőjének módosítása (a többi elem érintetlen marad).</summary>
    private async Task<PlannerTaskDetails> PatchChecklistItemAsync(
        PlannerTaskDetails details, string itemId, string field, object value, CancellationToken ct)
    {
        var body = new Dictionary<string, object?>
        {
            ["checklist"] = new Dictionary<string, object>
            {
                [itemId] = new Dictionary<string, object>
                {
                    ["@odata.type"] = "#microsoft.graph.plannerChecklistItem",
                    [field] = value,
                },
            },
        };

        var etag = details.ETag;
        for (var attempt = 0; ; attempt++)
        {
            using var res = await SendAsync(() =>
            {
                var req = JsonRequest(HttpMethod.Patch, $"planner/tasks/{Esc(details.TaskId)}/details", body);
                req.Headers.TryAddWithoutValidation("If-Match", etag);
                req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
                return req;
            }, idempotent: true, ct).ConfigureAwait(false);

            if (res.StatusCode == HttpStatusCode.PreconditionFailed && attempt == 0)
            {
                etag = (await GetTaskDetailsAsync(details.TaskId, ct).ConfigureAwait(false)).ETag;
                continue;
            }

            await EnsureSuccessAsync(res, ct).ConfigureAwait(false);
            if (res.StatusCode == HttpStatusCode.NoContent)
            {
                return await GetTaskDetailsAsync(details.TaskId, ct).ConfigureAwait(false);
            }

            var dto = await res.Content.ReadFromJsonAsync<TaskDetailsDto>(GraphJson.Options, ct).ConfigureAwait(false);
            return dto is null
                ? await GetTaskDetailsAsync(details.TaskId, ct).ConfigureAwait(false)
                : Map(details.TaskId, dto);
        }
    }

    // ---- Leképezés -------------------------------------------------------------------------

    internal static PlannerTask? Map(TaskDto dto)
    {
        if (string.IsNullOrEmpty(dto.Id) || string.IsNullOrEmpty(dto.PlanId))
        {
            return null;
        }

        return new PlannerTask
        {
            Id = dto.Id,
            PlanId = dto.PlanId,
            BucketId = dto.BucketId ?? "",
            Title = dto.Title ?? "",
            PercentComplete = dto.PercentComplete ?? 0,
            DueDate = DueDateConverter.FromGraph(dto.DueDateTime),
            Priority = dto.Priority ?? 5,
            ETag = dto.ETag ?? "",
            ChecklistItemCount = dto.ChecklistItemCount ?? 0,
            ActiveChecklistItemCount = dto.ActiveChecklistItemCount ?? 0,
            HasDescription = dto.HasDescription ?? false,
            CreatedDateTime = dto.CreatedDateTime,
            CompletedDateTime = dto.CompletedDateTime,
        };
    }

    internal static PlannerTaskDetails Map(string taskId, TaskDetailsDto dto)
    {
        var items = (dto.Checklist ?? [])
            .Select(kv => new ChecklistItem(kv.Key, kv.Value.Title ?? "", kv.Value.IsChecked ?? false, kv.Value.OrderHint ?? ""))
            // A Planner orderHint-je ordinális (bájtonkénti) összehasonlítással rendezendő.
            .OrderBy(i => i.OrderHint, StringComparer.Ordinal)
            .ToList();
        return new PlannerTaskDetails(taskId, dto.ETag ?? "", dto.Description ?? "", items);
    }

    // ---- HTTP segédfüggvények ----------------------------------------------------------------

    private static string Esc(string id) => Uri.EscapeDataString(id);

    private static HttpRequestMessage JsonRequest(HttpMethod method, string url, object body)
    {
        var json = JsonSerializer.Serialize(body, GraphJson.Options);
        return new HttpRequestMessage(method, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var res = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), idempotent: true, ct)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(res, ct).ConfigureAwait(false);
        var value = await res.Content.ReadFromJsonAsync<T>(GraphJson.Options, ct).ConfigureAwait(false);
        return value ?? throw new GraphApiException(res.StatusCode, "Üres válasz a Graph API-tól.");
    }

    private async Task<List<T>> GetAllPagesAsync<T>(string url, CancellationToken ct)
    {
        var all = new List<T>();
        string? next = url;
        var guard = 0;
        while (next is not null && guard++ < 100)
        {
            var page = await GetJsonAsync<GraphPage<T>>(next, ct).ConfigureAwait(false);
            all.AddRange(page.Value);
            next = page.NextLink;
        }

        return all;
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> factory, bool idempotent, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var token = await _tokens.GetAccessTokenAsync(ct).ConfigureAwait(false);
            using var request = factory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (idempotent && attempt < MaxAttempts)
                {
                    await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
                    continue;
                }

                throw new GraphNetworkException("Nincs kapcsolat a Microsoft Graph szolgáltatással.", ex);
            }

            if (IsTransient(response.StatusCode) && attempt < MaxAttempts)
            {
                var delay = RetryAfter(response) ?? Backoff(attempt);
                response.Dispose();
                await Task.Delay(delay, ct).ConfigureAwait(false);
                continue;
            }

            return response;
        }
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout or HttpStatusCode.BadGateway;

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt - 1));

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta)
        {
            return delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;
        }

        return null;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        string message;
        try
        {
            var envelope = await response.Content.ReadFromJsonAsync<GraphErrorEnvelope>(GraphJson.Options, ct).ConfigureAwait(false);
            code = envelope?.Error?.Code;
            message = envelope?.Error?.Message ?? response.ReasonPhrase ?? "Ismeretlen hiba";
        }
        catch (Exception)
        {
            message = response.ReasonPhrase ?? "Ismeretlen hiba";
        }

        var friendly = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "A bejelentkezés lejárt, jelentkezz be újra.",
            HttpStatusCode.Forbidden => "Nincs jogosultság ehhez a művelethez.",
            HttpStatusCode.NotFound => "Az elem nem található (lehet, hogy közben törölték).",
            HttpStatusCode.PreconditionFailed => "Az elemet közben máshol módosították. Frissíts és próbáld újra.",
            _ => $"Graph hiba ({(int)response.StatusCode}): {message}",
        };

        throw new GraphApiException(response.StatusCode, friendly, code);
    }
}

using System.Collections.Concurrent;
using System.Net;
using PlannerWidget.Core.Attendance;
using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Models;

namespace PlannerWidget.App.Services;

/// <summary>Bemutató módban a PDF-et csak olvassuk (mezőfelismerés kipróbálható), írni nem írjuk.</summary>
public sealed class ReadOnlyPdfFormService(IPdfFormService inner) : IPdfFormService
{
    public IReadOnlyDictionary<string, string> ReadFields(string path) => inner.ReadFields(path);

    public void WriteFields(string path, IReadOnlyDictionary<string, string> values) =>
        Log.Info($"[Bemutató] PDF-írás kihagyva ({Path.GetFileName(path)}): " +
                 string.Join(", ", values.Select(v => $"{v.Key}={v.Value}")));
}

/// <summary>
/// Bemutató mód (--demo): kitalált adatokkal, a Microsoft-fiók és a Planner érintése nélkül.
/// Kipróbáláshoz, képernyőképekhez és a felület teszteléséhez.
/// </summary>
public sealed class DemoAuthService : IAuthService
{
    private bool _signedIn = true;

    public string? AccountName => _signedIn ? "minta.felhasznalo@example.com" : null;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task<bool> TrySignInSilentlyAsync(CancellationToken ct = default) => Task.FromResult(_signedIn);

    public async Task SignInInteractiveAsync(CancellationToken ct)
    {
        await Task.Delay(600, ct);
        _signedIn = true;
    }

    public Task SignOutAsync()
    {
        _signedIn = false;
        return Task.CompletedTask;
    }

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) =>
        _signedIn ? Task.FromResult("demo") : throw new NotSignedInException();
}

public sealed class DemoPlannerClient : IPlannerClient
{
    private static readonly PlannerPlan[] Plans =
    [
        new("plan-mfui", "MFÜI"),
        new("plan-bkr", "BKR"),
        new("plan-web", "Honlapos feladatok"),
        new("plan-proj", "MFÜI PROJEKTEK"),
    ];

    private readonly ConcurrentDictionary<string, PlannerTask> _tasks = new();
    private readonly ConcurrentDictionary<string, PlannerTaskDetails> _details = new();
    private int _etag = 1;

    public DemoPlannerClient()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var now = DateTimeOffset.Now;
        Add("Éves beszámoló leadása", "plan-mfui", today.AddDays(-2), 1, checklist: [("Adatok összegyűjtése", true), ("Táblázatok", true), ("Szöveg", false), ("Jóváhagyás", false)]);
        Add("Számla továbbítása a pénzügynek", "plan-bkr", today.AddDays(-1), 3);
        Add("Kari tanácsülés előkészítése", "plan-mfui", today, 3, description: "Napirendi pontok, előterjesztések, jelenléti ív nyomtatása.");
        Add("Hírlevél szövegének véglegesítése", "plan-web", today.AddDays(1), 5);
        Add("Pályázati költségvetés egyeztetése", "plan-proj", today.AddDays(3), 1, checklist: [("Személyi költségek", false), ("Dologi költségek", false)]);
        Add("Honlap – oktatói adatlapok frissítése", "plan-web", today.AddDays(6), 9);
        Add("Laborbeszerzés árajánlatai", "plan-bkr", today.AddDays(12), 5);
        Add("Konferencia-regisztráció", "plan-proj", today.AddDays(25), 9);
        Add("Irodai nyomtató szervizelése", "plan-bkr", null, 5);
        Add("Ötletek a következő félévre", "plan-mfui", null, 9, description: "Szabadon bővíthető lista.");
        for (var i = 1; i <= 15; i++)
        {
            Add($"Lezárt feladat #{i}", Plans[i % Plans.Length].Id, today.AddDays(-i * 2), 5, completed: now.AddDays(-i));
        }
    }

    private void Add(string title, string planId, DateOnly? due, int priority,
        (string Title, bool Checked)[]? checklist = null, string description = "", DateTimeOffset? completed = null)
    {
        var id = "demo-" + Guid.NewGuid().ToString("N")[..8];
        var items = (checklist ?? []).Select((c, i) => new ChecklistItem($"c{i}", c.Title, c.Checked, $"{i:00}")).ToList();
        _tasks[id] = new PlannerTask
        {
            Id = id,
            PlanId = planId,
            BucketId = "b1",
            Title = title,
            DueDate = due,
            Priority = priority,
            PercentComplete = completed is null ? 0 : 100,
            CompletedDateTime = completed,
            ETag = NextETag(),
            ChecklistItemCount = items.Count,
            ActiveChecklistItemCount = items.Count(c => !c.IsChecked),
            HasDescription = description.Length > 0,
        };
        _details[id] = new PlannerTaskDetails(id, NextETag(), description, items);
    }

    private string NextETag() => $"W/\"{Interlocked.Increment(ref _etag)}\"";

    private static Task Latency(CancellationToken ct) => Task.Delay(Random.Shared.Next(150, 450), ct);

    public async Task<IReadOnlyList<PlannerTask>> GetMyTasksAsync(CancellationToken ct = default)
    {
        await Latency(ct);
        return _tasks.Values.ToList();
    }

    public Task<IReadOnlyList<PlannerPlan>> GetMyPlansAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlannerPlan>>(Plans);

    public Task<PlannerPlan?> GetPlanAsync(string planId, CancellationToken ct = default) =>
        Task.FromResult(Plans.FirstOrDefault(p => p.Id == planId));

    public Task<IReadOnlyList<PlannerBucket>> GetBucketsAsync(string planId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlannerBucket>>([new("b1", "Teendők", "1"), new("b2", "Folyamatban", "2"), new("b3", "Kész", "3")]);

    public Task<UserProfile> GetMeAsync(CancellationToken ct = default) =>
        Task.FromResult(new UserProfile("demo-user", "Minta Felhasználó", "minta.felhasznalo@example.com"));

    public Task<PlannerTask> GetTaskAsync(string taskId, CancellationToken ct = default) =>
        _tasks.TryGetValue(taskId, out var t) ? Task.FromResult(t) : throw new GraphApiException(HttpStatusCode.NotFound, "Nem található.");

    public async Task<PlannerTask> CreateTaskAsync(NewTaskRequest request, CancellationToken ct = default)
    {
        await Latency(ct);
        var id = "demo-" + Guid.NewGuid().ToString("N")[..8];
        var task = new PlannerTask
        {
            Id = id,
            PlanId = request.PlanId,
            BucketId = request.BucketId,
            Title = request.Title,
            DueDate = request.DueDate,
            Priority = request.Priority.ToPlanner(),
            ETag = NextETag(),
            CreatedDateTime = DateTimeOffset.Now,
        };
        _tasks[id] = task;
        _details[id] = new PlannerTaskDetails(id, NextETag(), "", []);
        return task;
    }

    public async Task<PlannerTask> UpdateTaskAsync(PlannerTask task, TaskPatch patch, CancellationToken ct = default)
    {
        await Latency(ct);
        var current = _tasks[task.Id];
        var updated = current with
        {
            Title = patch.Title ?? current.Title,
            PercentComplete = patch.PercentComplete ?? current.PercentComplete,
            CompletedDateTime = patch.PercentComplete switch
            {
                100 => DateTimeOffset.Now,
                not null => null,
                _ => current.CompletedDateTime,
            },
            Priority = patch.Priority?.ToPlanner() ?? current.Priority,
            DueDate = patch.ChangeDueDate ? patch.DueDate : current.DueDate,
            ETag = NextETag(),
        };
        _tasks[task.Id] = updated;
        return updated;
    }

    public async Task DeleteTaskAsync(PlannerTask task, CancellationToken ct = default)
    {
        await Latency(ct);
        _tasks.TryRemove(task.Id, out _);
    }

    public async Task<PlannerTaskDetails> GetTaskDetailsAsync(string taskId, CancellationToken ct = default)
    {
        await Latency(ct);
        return _details[taskId];
    }

    public async Task<PlannerTaskDetails> SetChecklistItemCheckedAsync(PlannerTaskDetails details, string itemId, bool isChecked, CancellationToken ct = default)
    {
        await Latency(ct);
        var current = _details[details.TaskId];
        var items = current.Checklist.Select(c => c.Id == itemId ? c with { IsChecked = isChecked } : c).ToList();
        var updated = current with { ETag = NextETag(), Checklist = items };
        _details[details.TaskId] = updated;
        _tasks[details.TaskId] = _tasks[details.TaskId] with { ActiveChecklistItemCount = items.Count(c => !c.IsChecked) };
        return updated;
    }
}

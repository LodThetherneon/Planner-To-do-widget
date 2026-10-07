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

public sealed class DemoPlannerClient : IPlannerClient, ITodoClient
{
    /// <summary>A bemutató „Beérkező ügyek” terve (bemutató módban induláskor ez az ügyterv).</summary>
    public const string TicketPlanId = "plan-ugyek";

    private static readonly PlannerPlan[] Plans =
    [
        new("plan-mfui", "MFÜI"),
        new("plan-bkr", "BKR"),
        new("plan-web", "Honlapos feladatok"),
        new("plan-proj", "MFÜI PROJEKTEK"),
        new(TicketPlanId, "Beérkező ügyek"),
    ];

    private int _taskFetches;
    private int _seededTasks;

    private readonly ConcurrentDictionary<string, PlannerTask> _tasks = new();
    private readonly ConcurrentDictionary<string, PlannerTaskDetails> _details = new();
    private int _etag = 1;

    // ---- To Do: „Szinkronhibák” (2 azonos + 1 másik folyamat hibája) és a „Szinkron életjel”
    private const string SyncListId = "todo-szinkronhibak";
    private readonly ConcurrentDictionary<string, TodoTask> _todo = new();
    private readonly bool _staleHeartbeat;
    private readonly DateTimeOffset _started = DateTimeOffset.Now;

    public DemoPlannerClient(bool staleHeartbeat = false)
    {
        _staleHeartbeat = staleHeartbeat;
        SeedSyncErrors();
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
            Add($"Lezárt feladat #{i}", Plans[i % 4].Id, today.AddDays(-i * 2), 5, completed: now.AddDays(-i));
        }

        // Kitalált ügyek. Állandó azonosítóval, hogy az „Új ügy” értesítés ne szóljon minden indításkor.
        Add("[#101] Laborbeszerzés kérdése", TicketPlanId, today.AddDays(1), 3, id: "demo-ugy-101",
            description: "Kitalált bemutató ügy: árajánlat-kérés laboreszközökre.", bucketId: ""); // gyűjtő nélkül → New
        Add("[#102] Szakdolgozati témabejelentés elakadt", TicketPlanId, today.AddDays(-1), 1, id: "demo-ugy-102", bucketId: Bucket("SOS"));
        Add(Ticket103, TicketPlanId, today.AddDays(5), 5, id: "demo-ugy-103", bucketId: Bucket("Processed"));
        Add("Kézzel felvett ügy (azonosító nélkül)", TicketPlanId, null, 5, id: "demo-ugy-kezi", bucketId: Bucket("In progress"),
            percent: 50);
        Add("[#105] Hallgatói igazolás – hiányzó adat", TicketPlanId, today.AddDays(4), 5, id: "demo-ugy-105", bucketId: Bucket("KÉRDÉSES"));
        Add("[#104] Óraadói szerződés kiegészítése", TicketPlanId, today.AddDays(9), 5, id: "demo-ugy-104", bucketId: Bucket("Revisit"));
        Add("[#100] Jelszó-visszaállítás kérése", TicketPlanId, today.AddDays(-3), 5, id: "demo-ugy-100", completed: now.AddDays(-2),
            bucketId: Bucket("Done"));
    }

    /// <summary>
    /// Bemutató forgatókönyv a letöltések sorszáma szerint (1 = induláskori frissítés):
    /// 2. – „érkezik” a #106 (új ügy értesítés);
    /// 3. – a válaszban még benne van a #103, de utána a flow törli (nincs felelőse) → rá kattintva 404;
    /// 4. – a #103 eltűnik a listából;
    /// 5. – a #103 új feladat-azonosítóval visszajön (újra van felelőse) → nem jön „új ügy” értesítés.
    /// </summary>
    private IReadOnlyList<PlannerTask> NextTaskList()
    {
        var fetch = Interlocked.Increment(ref _taskFetches);
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (fetch == 2)
        {
            Add("[#106] Kérdés az óraszám-elszámolásról", TicketPlanId, today.AddDays(2), 5,
                created: DateTimeOffset.Now, id: NewId(), bucketId: Bucket("KÉRDÉSES")); // mindig „új”, hogy szóljon
        }

        if (fetch == 2 && _tasks.TryGetValue("demo-ugy-100", out var done) && done.IsCompleted)
        {
            // Külsős válasz érkezett a lezárt #100-ra: a rendszer újranyitja és Revisit-be teszi.
            _tasks["demo-ugy-100"] = done with
            {
                PercentComplete = 0, CompletedDateTime = null, BucketId = Bucket("Revisit"), ETag = NextETag(),
            };
            Log.Info("[Bemutató] A lezárt #100-ra válasz érkezett – a rendszer újranyitotta (Revisit).");
        }

        if (fetch == 5)
        {
            Add(Ticket103, TicketPlanId, today.AddDays(5), 5, created: DateTimeOffset.Now, id: NewId(), bucketId: Bucket("Processed"));
            Log.Info("[Bemutató] A #103 ügy új feladat-azonosítóval visszajött (újra van felelőse).");
        }

        var list = _tasks.Values.ToList();
        if (fetch == 3 && _tasks.TryRemove("demo-ugy-103", out _))
        {
            Log.Info("[Bemutató] A #103 ügy feladatát a flow törölte (nincs felelőse) – a widget még nem tud róla.");
        }

        return list;
    }

    private const string Ticket103 = "[#103] Teremfoglalás a jövő heti workshopra";

    /// <summary>Az ügyterv gyűjtői = a Beérkező ügyek lista státuszai (azonosítóik szándékosan „véletlenszerűek”).</summary>
    private static readonly PlannerBucket[] TicketBucketList =
    [
        new("ugy-b-7f3a", "New", "1"), new("ugy-b-91c2", "Processed", "2"), new("ugy-b-0d5e", "SOS", "3"),
        new("ugy-b-4b88", "In progress", "4"), new("ugy-b-c61f", "KÉRDÉSES", "5"), new("ugy-b-e2d0", "Done", "6"),
        new("ugy-b-5a17", "Revisit", "7"),
    ];

    private static string Bucket(string status) => TicketBucketList.First(b => b.Name == status).Id;

    private static string NewId() => "demo-" + Guid.NewGuid().ToString("N")[..8];

    private static GraphApiException NotFound() =>
        new(HttpStatusCode.NotFound, "Az elem nem található (lehet, hogy közben törölték).");

    private PlannerTask TaskOrThrow(string id) => _tasks.TryGetValue(id, out var task) ? task : throw NotFound();

    private PlannerTaskDetails DetailsOrThrow(string id) =>
        _tasks.ContainsKey(id) && _details.TryGetValue(id, out var details) ? details : throw NotFound();

    private void SeedSyncErrors()
    {
        var now = DateTimeOffset.Now;
        static string Body(string run) =>
            "<html><body><p>A folyamat hibára futott (bemutató adat).</p>" +
            $"<p><a href=\"https://make.powerautomate.com/environments/Default-demo/flows/demo-flow/runs/{run}?v3=true&amp;source=demo\">Futás megnyitása</a></p></body></html>";
        _todo["hiba-1"] = new("hiba-1", "HIBA | Ügyek → Planner", "notStarted", now.AddMinutes(-95), now.AddMinutes(-95), Body("08584001"));
        _todo["hiba-2"] = new("hiba-2", "HIBA | Ügyek → Planner", "notStarted", now.AddMinutes(-12), now.AddMinutes(-12), Body("08584002"));
        _todo["hiba-3"] = new("hiba-3", "HIBA | Planner → Beérkező ügyek státusz", "notStarted", now.AddHours(-3), now.AddHours(-3), Body("08584003"));
    }

    public Task<IReadOnlyList<TodoList>> GetTodoListsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TodoList>>([new("todo-teendok", "Teendők"), new(SyncListId, "Szinkronhibák")]);

    public async Task<IReadOnlyList<TodoTask>> GetOpenTodoTasksAsync(string listId, CancellationToken ct = default)
    {
        await Latency(ct);
        if (listId != SyncListId)
        {
            return [];
        }

        // Az életjelet a „folyamat” 5 percenként frissíti; leállítva a bemutató indulása előtt 45 perccel állt meg.
        var heartbeat = _staleHeartbeat ? _started.AddMinutes(-45) : DateTimeOffset.Now.AddMinutes(-2);
        var heartbeatTask = new TodoTask("eletjel", "Szinkron életjel", "notStarted", _started.AddDays(-30), heartbeat, "");
        return _todo.Values.Where(t => !t.IsCompleted).Append(heartbeatTask).ToList();
    }

    public async Task CompleteTodoTaskAsync(string listId, string taskId, CancellationToken ct = default)
    {
        await Latency(ct);
        if (taskId == "eletjel")
        {
            throw new InvalidOperationException("Az életjelet nem szabad lezárni.");
        }

        if (!_todo.TryGetValue(taskId, out var task))
        {
            throw NotFound();
        }

        _todo[taskId] = task with { Status = "completed" };
        Log.Info($"[Bemutató] Szinkronhiba lezárva: {task.Title}");
    }

    private void Add(string title, string planId, DateOnly? due, int priority,
        (string Title, bool Checked)[]? checklist = null, string description = "", DateTimeOffset? completed = null,
        string? id = null, DateTimeOffset? created = null, string bucketId = "b1", int percent = 0)
    {
        // Állandó azonosítók (indításonként ugyanazok), hogy a kézi sorrend és a gyorsítótár a bemutatóban is megmaradjon.
        id ??= $"demo-{++_seededTasks:000}";
        var items = (checklist ?? []).Select((c, i) => new ChecklistItem($"c{i}", c.Title, c.Checked, $"{i:00}")).ToList();
        _tasks[id] = new PlannerTask
        {
            Id = id,
            PlanId = planId,
            BucketId = bucketId,
            Title = title,
            DueDate = due,
            Priority = priority,
            PercentComplete = completed is null ? percent : 100,
            CompletedDateTime = completed,
            CreatedDateTime = created,
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
        return NextTaskList();
    }

    public Task<IReadOnlyList<PlannerPlan>> GetMyPlansAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlannerPlan>>(Plans);

    public Task<PlannerPlan?> GetPlanAsync(string planId, CancellationToken ct = default) =>
        Task.FromResult(Plans.FirstOrDefault(p => p.Id == planId));

    public Task<IReadOnlyList<PlannerBucket>> GetBucketsAsync(string planId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PlannerBucket>>(planId == TicketPlanId
            ? TicketBucketList
            : [new("b1", "Teendők", "1"), new("b2", "Folyamatban", "2"), new("b3", "Kész", "3")]);

    public Task<UserProfile> GetMeAsync(CancellationToken ct = default) =>
        Task.FromResult(new UserProfile("demo-user", "Minta Felhasználó", "minta.felhasznalo@example.com"));

    public Task<PlannerTask> GetTaskAsync(string taskId, CancellationToken ct = default) => Task.FromResult(TaskOrThrow(taskId));

    public async Task<PlannerTask> CreateTaskAsync(NewTaskRequest request, CancellationToken ct = default)
    {
        await Latency(ct);
        var id = NewId();
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
        var current = TaskOrThrow(task.Id);
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
            BucketId = patch.BucketId ?? current.BucketId,
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
        return DetailsOrThrow(taskId);
    }

    public async Task<PlannerTaskDetails> SetChecklistItemTitleAsync(PlannerTaskDetails details, string itemId, string title, CancellationToken ct = default)
    {
        await Latency(ct);
        var current = DetailsOrThrow(details.TaskId);
        var items = current.Checklist.Select(c => c.Id == itemId ? c with { Title = title.Trim() } : c).ToList();
        var updated = current with { ETag = NextETag(), Checklist = items };
        _details[details.TaskId] = updated;
        return updated;
    }

    public async Task<PlannerTaskDetails> SetChecklistItemCheckedAsync(PlannerTaskDetails details, string itemId, bool isChecked, CancellationToken ct = default)
    {
        await Latency(ct);
        var current = DetailsOrThrow(details.TaskId);
        var items = current.Checklist.Select(c => c.Id == itemId ? c with { IsChecked = isChecked } : c).ToList();
        var updated = current with { ETag = NextETag(), Checklist = items };
        _details[details.TaskId] = updated;
        _tasks[details.TaskId] = _tasks[details.TaskId] with { ActiveChecklistItemCount = items.Count(c => !c.IsChecked) };
        return updated;
    }
}

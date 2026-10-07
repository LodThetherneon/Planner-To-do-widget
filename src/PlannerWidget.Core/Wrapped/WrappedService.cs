using System.Globalization;
using System.Text.RegularExpressions;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Storage;

namespace PlannerWidget.Core.Wrapped;

/// <summary>Egy feladat (vagy ügy) a főkönyvben: a Planner nem őrzi a régi feladatokat, ezért mi jegyezzük meg.</summary>
public sealed class LedgerEntry
{
    public string Title { get; set; } = "";
    public bool IsTicket { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? Completed { get; set; }
}

/// <summary>Egy hónap összegzése („Wrapped”).</summary>
public sealed class MonthlyWrapped
{
    /// <summary>„2026-09” formában.</summary>
    public string Month { get; set; } = "";
    public int TasksArrived { get; set; }
    public int TasksArrivedDone { get; set; }
    public int TicketsArrived { get; set; }
    public int TicketsArrivedDone { get; set; }
    /// <summary>A hónapban lezárt feladatok és ügyek összesen (bármikor érkeztek).</summary>
    public int TotalCompleted { get; set; }
    public string? OldestOpenTitle { get; set; }
    public int OldestOpenDays { get; set; }
    public string? FastestTitle { get; set; }
    public double FastestHours { get; set; }
    public DateOnly? BusiestDay { get; set; }
    public int BusiestDayCount { get; set; }

    public bool HasData => TasksArrived + TicketsArrived + TotalCompleted > 0;
}

public sealed class WrappedState
{
    public Dictionary<string, LedgerEntry> Ledger { get; set; } = [];
    public List<MonthlyWrapped> Reports { get; set; } = [];
    /// <summary>Az utolsó hónap, amelyre már készült (vagy kihagytuk) az összegzést („2026-09”).</summary>
    public string? LastGenerated { get; set; }
    /// <summary>Az a hónap, amelyet a felhasználó még nem nyitott meg (piros pötty).</summary>
    public string? Unseen { get; set; }
}

/// <summary>
/// Havi összegzés: minden frissítéskor feljegyzi a látott feladatokat, a hónap első frissítésekor pedig elkészíti az előző
/// hónapét (wrapped.json). Az ügyeket a #szám azonosítja, így a flow által újralétrehozott ügy nem számít kétszer.
/// </summary>
public sealed partial class WrappedService
{
    private const int KeepReports = 12;
    private static readonly TimeSpan KeepLedger = TimeSpan.FromDays(400);

    private readonly JsonFileStore<WrappedState> _store;
    private readonly Func<DateTimeOffset> _clock;
    private WrappedState? _state;

    public WrappedService(string? path = null, Func<DateTimeOffset>? clock = null)
    {
        _store = new JsonFileStore<WrappedState>(path ?? AppPaths.WrappedFile);
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    private WrappedState State => _state ??= _store.Load() ?? new WrappedState();

    public IReadOnlyList<MonthlyWrapped> Reports => State.Reports;
    public bool HasUnseen => State.Unseen is not null;

    /// <summary>A felhasználó megnyitotta az összegzést: eltűnik a piros jelzés.</summary>
    public void MarkSeen()
    {
        if (State.Unseen is null) return;
        State.Unseen = null;
        Save();
    }

    /// <summary>Sikeres frissítés után: feljegyzi a feladatokat, és ha új hónap kezdődött, elkészíti az előzőét.</summary>
    public void Update(IReadOnlyList<PlannerTask> tasks, string? ticketPlanId)
    {
        try
        {
            var now = _clock();
            Record(tasks, ticketPlanId, now);
            GenerateIfDue(now);
            Save();
        }
        catch (Exception ex)
        {
            Log.Warn("A havi összegzés frissítése nem sikerült", ex);
        }
    }

    private void Record(IReadOnlyList<PlannerTask> tasks, string? ticketPlanId, DateTimeOffset now)
    {
        foreach (var t in tasks)
        {
            var isTicket = !string.IsNullOrEmpty(ticketPlanId) && t.PlanId == ticketPlanId;
            var key = KeyOf(t, isTicket);
            var created = (t.CreatedDateTime ?? now).ToLocalTime();
            DateTimeOffset? done = t.IsCompleted ? (t.CompletedDateTime ?? now).ToLocalTime() : null;

            if (!State.Ledger.TryGetValue(key, out var e))
            {
                State.Ledger[key] = new LedgerEntry { Title = t.Title, IsTicket = isTicket, Created = created, Completed = done };
                continue;
            }

            e.Title = t.Title;
            if (created < e.Created) e.Created = created;
            if (done is not null && (e.Completed is null || done > e.Completed)) e.Completed = done;
            // Újranyitott feladat (a ledger korábban késznek látta): marad az első lezárás időpontja.
        }

        var cutoff = now - KeepLedger;
        foreach (var k in State.Ledger.Where(kv => (kv.Value.Completed ?? now) < cutoff).Select(kv => kv.Key).ToList())
        {
            State.Ledger.Remove(k);
        }
    }

    private void GenerateIfDue(DateTimeOffset now)
    {
        var current = MonthKey(now);
        var previous = MonthKey(now.AddMonths(-1));
        if (State.LastGenerated == previous || string.CompareOrdinal(State.LastGenerated, previous) > 0)
        {
            return;
        }

        State.LastGenerated = previous;
        var report = Build(previous, now);
        if (!report.HasData) return;

        State.Reports.RemoveAll(r => r.Month == previous);
        State.Reports.Add(report);
        State.Reports = State.Reports.OrderBy(r => r.Month, StringComparer.Ordinal).TakeLast(KeepReports).ToList();
        State.Unseen = previous;
        Log.Info($"Havi összegzés elkészült: {previous}");
    }

    /// <summary>A megadott hónap („yyyy-MM”) összegzése a főkönyvből.</summary>
    public MonthlyWrapped Build(string month, DateTimeOffset now)
    {
        var start = DateTimeOffset.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal);
        var end = start.AddMonths(1);
        var ledger = State.Ledger.Values.ToList();

        bool In(DateTimeOffset? d) => d is { } v && v >= start && v < end;
        var arrived = ledger.Where(e => In(e.Created)).ToList();
        var finishedInMonth = ledger.Where(e => In(e.Completed)).ToList();

        var r = new MonthlyWrapped
        {
            Month = month,
            TasksArrived = arrived.Count(e => !e.IsTicket),
            TasksArrivedDone = arrived.Count(e => !e.IsTicket && e.Completed is not null),
            TicketsArrived = arrived.Count(e => e.IsTicket),
            TicketsArrivedDone = arrived.Count(e => e.IsTicket && e.Completed is not null),
            TotalCompleted = finishedInMonth.Count,
        };

        var oldest = ledger.Where(e => e.Created < end && (e.Completed is null || e.Completed >= end))
            .OrderBy(e => e.Created).FirstOrDefault();
        if (oldest is not null)
        {
            r.OldestOpenTitle = oldest.Title;
            r.OldestOpenDays = Math.Max(0, (int)(end - oldest.Created).TotalDays);
        }

        var fastest = finishedInMonth.Where(e => e.Completed > e.Created)
            .OrderBy(e => e.Completed!.Value - e.Created).FirstOrDefault();
        if (fastest is not null)
        {
            r.FastestTitle = fastest.Title;
            r.FastestHours = (fastest.Completed!.Value - fastest.Created).TotalHours;
        }

        var busiest = finishedInMonth.GroupBy(e => DateOnly.FromDateTime(e.Completed!.Value.LocalDateTime))
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();
        if (busiest is not null)
        {
            r.BusiestDay = busiest.Key;
            r.BusiestDayCount = busiest.Count();
        }

        return r;
    }

    /// <summary>Bemutató módhoz: kitalált előző havi előzmény, hogy legyen mit mutatni.</summary>
    public void SeedDemoHistory()
    {
        if (State.Reports.Count > 0 || State.Ledger.Keys.Any(k => k.StartsWith("demo-h", StringComparison.Ordinal)))
        {
            return;
        }

        var now = _clock();
        var prev = new DateTimeOffset(now.Year, now.Month, 1, 9, 0, 0, now.Offset).AddMonths(-1);
        var titles = new[] { "Beszámoló összeállítása", "Számlák egyeztetése", "Heti jelentés", "Szerződés átnézése",
            "Leltár ellenőrzése", "Meghívók kiküldése", "Archiválás", "Költségvetés frissítése", "Egyeztetés előkészítése",
            "Jegyzőkönyv", "Határidőnapló", "Adatok rendezése" };
        for (var i = 0; i < titles.Length; i++)
        {
            var created = prev.AddDays(i * 2);
            State.Ledger[$"demo-h{i}"] = new LedgerEntry
            {
                Title = titles[i], IsTicket = false, Created = created,
                Completed = i % 4 == 3 ? null : created.AddDays(1 + i % 5).AddHours(3),
            };
        }

        for (var i = 0; i < 7; i++)
        {
            var created = prev.AddDays(1 + i * 3).AddHours(2);
            State.Ledger[$"demo-ht{i}"] = new LedgerEntry
            {
                Title = $"[#{90 + i}] Minta ügy", IsTicket = true, Created = created,
                Completed = i == 6 ? null : created.AddHours(5 + i * 9),
            };
        }

        State.Ledger["demo-hold"] = new LedgerEntry
        {
            Title = "Régi, félretett feladat", Created = prev.AddDays(-40), Completed = null,
        };
        State.LastGenerated = null;
        GenerateIfDue(now);
        Save();
    }

    private void Save()
    {
        try { _store.Save(State); }
        catch (Exception ex) { Log.Warn("A wrapped.json mentése nem sikerült", ex); }
    }

    public static string MonthKey(DateTimeOffset d) => d.ToLocalTime().ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string KeyOf(PlannerTask t, bool isTicket)
    {
        if (isTicket && TicketNumber().Match(t.Title) is { Success: true } m)
        {
            return "t" + m.Groups[1].Value;
        }

        return t.Id;
    }

    [GeneratedRegex(@"^\s*\[#(\d+)\]")]
    private static partial Regex TicketNumber();
}

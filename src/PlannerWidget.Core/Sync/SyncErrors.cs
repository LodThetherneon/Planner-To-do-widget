using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Logging;

namespace PlannerWidget.Core.Sync;

/// <summary>Egy szinkronhiba-bejegyzés (a „Szinkronhibák” To Do lista egy „HIBA | …” feladata).</summary>
public sealed record SyncErrorEntry(string TaskId, string Title, string ProcessName, DateTimeOffset? Created, Uri? Link);

/// <summary>Azonos című hibák csoportja (darabszámmal); a legutóbbi elöl.</summary>
public sealed record SyncErrorGroup(string Title, string ProcessName, IReadOnlyList<SyncErrorEntry> Entries)
{
    public int Count => Entries.Count;
    public DateTimeOffset? Latest => Entries.FirstOrDefault()?.Created;

    /// <summary>A legutóbbi bejegyzés linkje (ha annak nincs, az első, amelyiknek van).</summary>
    public Uri? Link => Entries.Select(e => e.Link).FirstOrDefault(l => l is not null);
}

/// <summary>A „Szinkron életjel” állapota.</summary>
/// <param name="Found">Van-e életjel-feladat a listában.</param>
/// <param name="LastModified">Mikor frissítette utoljára a folyamat.</param>
/// <param name="IsStale">Riasztani kell-e (6:20–21:00 között 20 percnél régebbi).</param>
/// <param name="Message">A felhasználónak szóló figyelmeztetés, ha <see cref="IsStale"/>.</param>
public sealed record HeartbeatState(bool Found, DateTimeOffset? LastModified, bool IsStale, string? Message)
{
    public static HeartbeatState Missing { get; } = new(false, null, false, null);
}

/// <summary>A szinkronhibák teljes állapota egy frissítés után.</summary>
public sealed record SyncStatus(bool ListFound, IReadOnlyList<SyncErrorGroup> Groups, HeartbeatState Heartbeat)
{
    public static SyncStatus NotConfigured { get; } = new(false, [], HeartbeatState.Missing);

    public int ErrorCount => Groups.Sum(g => g.Count);
}

/// <summary>
/// A „Szinkronhibák” lista értelmezése: hibák („HIBA | &lt;folyamat&gt;”), csoportosítás, Power Automate link a
/// HTML-törzsből, és a „Szinkron életjel” idősáv-logikája. UI- és hálózatfüggetlen, tesztelt.
/// </summary>
public static partial class SyncErrors
{
    public const string ListName = "Szinkronhibák";
    public const string HeartbeatTitle = "Szinkron életjel";

    /// <summary>Az életjelet a folyamat 6:00–21:00 között 5 percenként frissíti; 6:20 előtt még nem riasztunk.</summary>
    public static readonly TimeSpan AlarmFrom = new(6, 20, 0);
    public static readonly TimeSpan AlarmUntil = new(21, 0, 0);
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(20);

    [GeneratedRegex(@"^\s*HIBA\s*\|\s*(?<name>.*?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorTitleRegex();

    [GeneratedRegex(@"https://make\.powerautomate\.com/[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    public static bool IsHeartbeat(string? title) =>
        string.Equals(title?.Trim(), HeartbeatTitle, StringComparison.OrdinalIgnoreCase);

    /// <summary>„HIBA | …” című feladat → bejegyzés; az életjel és minden más → null.</summary>
    public static SyncErrorEntry? TryParse(TodoTask task)
    {
        if (task.IsCompleted || IsHeartbeat(task.Title))
        {
            return null;
        }

        var match = ErrorTitleRegex().Match(task.Title ?? "");
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value;
        return new SyncErrorEntry(task.Id, task.Title!.Trim(), name.Length > 0 ? name : "(ismeretlen folyamat)",
            task.CreatedDateTime, ExtractLink(task.BodyContent));
    }

    /// <summary>Az első make.powerautomate.com link a (HTML) törzsből; az &amp;amp; stb. visszafejtve.</summary>
    public static Uri? ExtractLink(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var match = LinkRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(match.Value).TrimEnd('.', ',', ')', ';');
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;
    }

    /// <summary>Azonos címűek egy csoportba; csoporton belül és a csoportok között is a legutóbbi elöl.</summary>
    public static IReadOnlyList<SyncErrorGroup> Group(IEnumerable<SyncErrorEntry> entries) =>
        entries
            .GroupBy(e => e.Title, StringComparer.Ordinal)
            .Select(g => new SyncErrorGroup(g.Key, g.First().ProcessName,
                g.OrderByDescending(e => e.Created ?? DateTimeOffset.MinValue).ToList()))
            .OrderByDescending(g => g.Latest ?? DateTimeOffset.MinValue)
            .ToList();

    /// <summary>
    /// Életjel: 6:20–21:00 (helyi idő, Europe/Budapest) között riaszt, ha a legutóbbi frissítés 20 percnél régebbi.
    /// Éjszaka (és 6:20 előtt) soha. Hiányzó életjel-feladat: nincs riasztás (a folyamat nincs beállítva).
    /// </summary>
    public static HeartbeatState EvaluateHeartbeat(DateTimeOffset? lastModified, bool found, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        if (!found || lastModified is not { } last)
        {
            return found ? new HeartbeatState(true, null, false, null) : HeartbeatState.Missing;
        }

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var inWindow = localNow.TimeOfDay >= AlarmFrom && localNow.TimeOfDay < AlarmUntil;
        if (!inWindow || now - last <= StaleAfter)
        {
            return new HeartbeatState(true, last, false, null);
        }

        var localLast = TimeZoneInfo.ConvertTime(last, timeZone);
        var when = localLast.Date == localNow.Date
            ? localLast.ToString("HH:mm", CultureInfo.InvariantCulture)
            : localLast.ToString("MM.dd. HH:mm", CultureInfo.InvariantCulture);
        return new HeartbeatState(true, last, true, $"A szinkron nem fut (utolsó: {when}) – ellenőrizd a Power Automate-et");
    }

    /// <summary>A lista feladataiból a teljes állapot.</summary>
    public static SyncStatus Evaluate(IEnumerable<TodoTask> tasks, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        var list = tasks.Where(t => !t.IsCompleted).ToList();
        var heartbeat = list.Where(t => IsHeartbeat(t.Title))
            .OrderByDescending(t => t.LastModifiedDateTime ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        var groups = Group(list.Select(TryParse).OfType<SyncErrorEntry>());
        return new SyncStatus(true, groups,
            EvaluateHeartbeat(heartbeat?.LastModifiedDateTime, heartbeat is not null, now, timeZone));
    }

    /// <summary>Europe/Budapest (Windows-on: Central Europe Standard Time); végső esetben a helyi zóna.</summary>
    public static TimeZoneInfo BudapestTimeZone { get; } = FindBudapest();

    private static TimeZoneInfo FindBudapest()
    {
        foreach (var id in new[] { "Europe/Budapest", "Central Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }
}

/// <summary>
/// A „Szinkronhibák” To Do lista lekérése (a lista azonosítóját megjegyzi; 404-nél újra megkeresi) és a hibák lezárása.
/// </summary>
public sealed class SyncErrorService(ITodoClient client, Func<DateTimeOffset>? clock = null, TimeZoneInfo? timeZone = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);
    private readonly TimeZoneInfo _timeZone = timeZone ?? SyncErrors.BudapestTimeZone;
    private string? _listId;
    private bool _loggedMissing;

    public async Task<SyncStatus> LoadAsync(CancellationToken ct = default)
    {
        var listId = await FindListAsync(ct).ConfigureAwait(false);
        if (listId is null)
        {
            return SyncStatus.NotConfigured;
        }

        IReadOnlyList<TodoTask> tasks;
        try
        {
            tasks = await client.GetOpenTodoTasksAsync(listId, ct).ConfigureAwait(false);
        }
        catch (GraphApiException ex) when (ex.IsNotFound)
        {
            // A listát közben törölték/újra létrehozták: keressük újra név szerint.
            _listId = null;
            listId = await FindListAsync(ct).ConfigureAwait(false);
            if (listId is null)
            {
                return SyncStatus.NotConfigured;
            }

            tasks = await client.GetOpenTodoTasksAsync(listId, ct).ConfigureAwait(false);
        }

        return SyncErrors.Evaluate(tasks, _clock(), _timeZone);
    }

    /// <summary>„Megoldva”: a megadott To Do feladatok lezárása (status=completed). Az életjelhez soha nem nyúl.</summary>
    public async Task ResolveAsync(IEnumerable<SyncErrorEntry> entries, CancellationToken ct = default)
    {
        var listId = _listId ?? await FindListAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("A „Szinkronhibák” lista nem található.");
        foreach (var entry in entries)
        {
            if (SyncErrors.IsHeartbeat(entry.Title))
            {
                continue; // védőkorlát: az életjelet soha nem zárjuk le
            }

            try
            {
                await client.CompleteTodoTaskAsync(listId, entry.TaskId, ct).ConfigureAwait(false);
            }
            catch (GraphApiException ex) when (ex.IsNotFound)
            {
                // Már nincs meg (pl. máshol lezárták): ez is „megoldva”.
            }
        }
    }

    private async Task<string?> FindListAsync(CancellationToken ct)
    {
        if (_listId is not null)
        {
            return _listId;
        }

        var lists = await client.GetTodoListsAsync(ct).ConfigureAwait(false);
        _listId = lists.FirstOrDefault(l => string.Equals(l.DisplayName.Trim(), SyncErrors.ListName, StringComparison.OrdinalIgnoreCase))?.Id;
        if (_listId is null && !_loggedMissing)
        {
            _loggedMissing = true;
            Log.Info($"A „{SyncErrors.ListName}” To Do lista nem található – a szinkronhiba-jelzés nem aktív.");
        }
        else if (_listId is not null)
        {
            _loggedMissing = false;
        }

        return _listId;
    }
}

using System.Globalization;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Storage;

namespace PlannerWidget.Core.Tickets;

/// <summary>Egy frissítés változásai az ügytervben.</summary>
/// <param name="NewTickets">Újonnan megjelent, nyitott ügyek („Új ügy” értesítés).</param>
/// <param name="Reopened">
/// Korábban kész (100%), most újra nyitott ügyek – a rendszer egy Done ügyre érkezett külsős válasz miatt Revisit-be teszi
/// („Új válasz érkezett”). Ugyanaz a feladat-azonosító, csak a készültség esett vissza.
/// </param>
public sealed record TicketChanges(IReadOnlyList<PlannerTask> NewTickets, IReadOnlyList<PlannerTask> Reopened)
{
    public static TicketChanges None { get; } = new([], []);
}

/// <summary>
/// Az „Új ügy” értesítésekhez: megjegyzi, mely ügytervbeli feladatokat látta már (ticket-state.json),
/// így újraindítás után sem értesít kétszer ugyanarról. Ha még nincs mentett állapot, vagy az ügyterv
/// megváltozott, csak felveszi a jelenlegi állapotot (alapállapot), értesítés nélkül.
/// <para>
/// A flow törli a Planner-feladatot, ha a ticketnek nincs felelőse, és új feladat-azonosítóval hozza létre újra,
/// ha ismét kap. Ezért a ticket-azonosítókat (#ID) is megjegyezzük: ha ugyanaz a #ID <see cref="ReturnWindow"/>-on
/// belül tér vissza, az nem „új ügy” (pl. a Felelős mezőt csak átírták), nem szólunk.
/// </para>
/// </summary>
public sealed class TicketTracker
{
    /// <summary>Ennyi időn belül visszatérő #ID-ról nem jön „Új ügy” értesítés.</summary>
    public static readonly TimeSpan ReturnWindow = TimeSpan.FromHours(24);

    /// <summary>A „legutóbb látva” időpontot ennél ritkábban frissítjük (ne írjunk fájlt minden frissítéskor).</summary>
    private static readonly TimeSpan SeenGranularity = TimeSpan.FromHours(1);

    private readonly JsonFileStore<TicketState> _store;
    private TicketState? _state;
    private bool _loaded;

    public TicketTracker(string? path = null)
    {
        _store = new JsonFileStore<TicketState>(path ?? AppPaths.TicketStateFile);
    }

    /// <summary>
    /// Egy sikeres frissítés eredményének feldolgozása. Visszaadja az újonnan megjelent, nyitott ügyeket, amelyekről
    /// értesíteni kell (alapállapot felvételekor üres listát), és elmenti az ismert azonosítókat.
    /// </summary>
    public IReadOnlyList<PlannerTask> Update(IReadOnlyList<PlannerTask> tasks, string? ticketPlanId, DateTimeOffset? now = null) =>
        Track(tasks, ticketPlanId, now).NewTickets;

    /// <summary>Mint az <see cref="Update"/>, de az újranyitott (kész → nyitott) ügyeket is visszaadja.</summary>
    public TicketChanges Track(IReadOnlyList<PlannerTask> tasks, string? ticketPlanId, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(ticketPlanId))
        {
            return TicketChanges.None;
        }

        var time = now ?? DateTimeOffset.Now;
        if (!_loaded)
        {
            _state = _store.Load();
            _loaded = true;
        }

        var inPlan = tasks.Where(t => t.PlanId == ticketPlanId).ToList();
        var current = inPlan.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var baseline = _state is not { KnownTaskIds: not null } || _state.PlanId != ticketPlanId;
        var recent = baseline
            ? new Dictionary<string, DateTimeOffset>()
            : new Dictionary<string, DateTimeOffset>(_state!.RecentTicketIds ?? []);
        var changed = baseline;
        var completed = inPlan.Where(t => t.IsCompleted).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<PlannerTask> notify = [];
        IReadOnlyList<PlannerTask> reopened = [];
        if (baseline)
        {
            Log.Info($"Ügyek: alapállapot felvéve ({current.Count} feladat), értesítés nélkül.");
        }
        else
        {
            // Lejárt bejegyzések törlése: ami ennél régebben tűnt el, az visszatérve már „új ügy”.
            foreach (var old in recent.Where(r => time - r.Value > ReturnWindow).Select(r => r.Key).ToList())
            {
                recent.Remove(old);
                changed = true;
            }

            var fresh = FindNew(tasks, ticketPlanId, _state!.KnownTaskIds!.ToHashSet(StringComparer.Ordinal));
            var returning = fresh.Where(t => Key(t) is { } key && recent.ContainsKey(key)).ToList();
            foreach (var task in returning)
            {
                Log.Info($"Ügy visszatért új feladat-azonosítóval (értesítés nélkül): {task.Title}");
            }

            notify = fresh.Except(returning).ToList();
            changed |= !current.SetEquals(_state.KnownTaskIds!);

            // Kész → nyitott (ugyanaz a feladat): újranyitás. Régi állapotfájlban még nincs kész-lista → nincs jelzés.
            var wasCompleted = (_state.CompletedTaskIds ?? []).ToHashSet(StringComparer.Ordinal);
            reopened = inPlan.Where(t => !t.IsCompleted && wasCompleted.Contains(t.Id)).ToList();
            foreach (var task in reopened)
            {
                Log.Info($"Ügy újranyitva (kész → nyitott): {task.Title}");
            }

            changed |= !completed.SetEquals(wasCompleted);
        }

        // A jelenleg látható #ID-k „legutóbb látva” ideje.
        foreach (var key in inPlan.Select(Key).OfType<string>())
        {
            if (!recent.TryGetValue(key, out var seen) || time - seen >= SeenGranularity)
            {
                recent[key] = time;
                changed = true;
            }
        }

        if (changed)
        {
            // Csak a jelenleg látható feladatokat tartjuk meg, így a fájl nem nő a végtelenségig.
            _state = new TicketState
            {
                PlanId = ticketPlanId,
                KnownTaskIds = current.Order(StringComparer.Ordinal).ToList(),
                CompletedTaskIds = completed.Order(StringComparer.Ordinal).ToList(),
                RecentTicketIds = recent,
            };
            try
            {
                _store.Save(_state);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Az ügyek állapota nem menthető", ex);
            }
        }

        return new TicketChanges(notify, reopened);
    }

    /// <summary>Az ügyterv nyitott feladatai, amelyek nincsenek az ismertek között (létrehozás szerinti sorrendben).</summary>
    public static IReadOnlyList<PlannerTask> FindNew(IEnumerable<PlannerTask> tasks, string ticketPlanId, IReadOnlySet<string> known) =>
        tasks.Where(t => t.PlanId == ticketPlanId && !t.IsCompleted && !known.Contains(t.Id))
            .OrderBy(t => t.CreatedDateTime ?? DateTimeOffset.MaxValue)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static string? Key(PlannerTask task) =>
        TicketParser.ParseId(task.Title) is { } id ? id.ToString(CultureInfo.InvariantCulture) : null;

    private sealed class TicketState
    {
        public string? PlanId { get; set; }
        public List<string>? KnownTaskIds { get; set; }

        /// <summary>Az ügyterv kész (100%) feladatai – az újranyitás felismeréséhez.</summary>
        public List<string>? CompletedTaskIds { get; set; }

        /// <summary>Ticket-azonosító (#ID) → mikor láttuk utoljára az ügytervben.</summary>
        public Dictionary<string, DateTimeOffset>? RecentTicketIds { get; set; }
    }
}

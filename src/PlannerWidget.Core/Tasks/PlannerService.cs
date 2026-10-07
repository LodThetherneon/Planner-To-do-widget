using System.Collections.Concurrent;
using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Storage;

namespace PlannerWidget.Core.Tasks;

/// <summary>
/// A Planner adatok magasabb szintű kezelése: feladatlista + tervnevek egyben,
/// tervnév- és feladat-gyorsítótár (offline indításhoz), bucketek memóriában tartása.
/// </summary>
public sealed class PlannerService
{
    private readonly IPlannerClient _client;
    private readonly JsonFileStore<Dictionary<string, string>> _planTitleStore;
    private readonly JsonFileStore<CachedSnapshot> _snapshotStore;
    private readonly ConcurrentDictionary<string, string> _planTitles;
    private readonly ConcurrentDictionary<string, IReadOnlyList<PlannerBucket>> _buckets = new();
    private UserProfile? _me;

    public PlannerService(IPlannerClient client, string? planTitlesFile = null, string? snapshotFile = null)
    {
        _client = client;
        _planTitleStore = new JsonFileStore<Dictionary<string, string>>(planTitlesFile ?? AppPaths.PlanTitlesFile);
        _snapshotStore = new JsonFileStore<CachedSnapshot>(snapshotFile ?? AppPaths.TaskCacheFile);
        _planTitles = new ConcurrentDictionary<string, string>(_planTitleStore.Load() ?? []);
    }

    public IReadOnlyDictionary<string, string> PlanTitles => _planTitles;

    public IPlannerClient Client => _client;

    /// <summary>Tervnevek átvétele (pl. a régi verzió plan_cache.json fájljából).</summary>
    public void MergePlanTitles(IReadOnlyDictionary<string, string> titles)
    {
        foreach (var (id, title) in titles)
        {
            _planTitles.TryAdd(id, title);
        }

        SavePlanTitles();
    }

    public async Task<UserProfile> GetMeAsync(CancellationToken ct = default) =>
        _me ??= await _client.GetMeAsync(ct).ConfigureAwait(false);

    public void ResetSession()
    {
        _me = null;
        _buckets.Clear();
    }

    /// <summary>Teljes frissítés: a feladataim + minden érintett terv neve.</summary>
    public async Task<PlannerSnapshot> LoadSnapshotAsync(CancellationToken ct = default)
    {
        var tasksTask = _client.GetMyTasksAsync(ct);
        var plansTask = SafeGetPlansAsync(ct);
        var tasks = await tasksTask.ConfigureAwait(false);
        var plans = await plansTask.ConfigureAwait(false);

        var changed = false;
        foreach (var plan in plans)
        {
            changed |= SetTitle(plan.Id, plan.Title);
        }

        var missing = tasks.Select(t => t.PlanId).Distinct().Where(id => !_planTitles.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            var fetched = new ConcurrentBag<PlannerPlan>();
            await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                async (planId, token) =>
                {
                    if (await _client.GetPlanAsync(planId, token).ConfigureAwait(false) is { } plan)
                    {
                        fetched.Add(plan);
                    }
                }).ConfigureAwait(false);

            foreach (var plan in fetched)
            {
                changed |= SetTitle(plan.Id, plan.Title);
            }
        }

        if (changed)
        {
            SavePlanTitles();
        }

        var snapshot = new PlannerSnapshot(tasks, new Dictionary<string, string>(_planTitles), DateTimeOffset.Now);
        SaveSnapshot(snapshot);
        return snapshot;
    }

    /// <summary>Az utolsó sikeres frissítés eredménye (offline indításhoz).</summary>
    public PlannerSnapshot? LoadCachedSnapshot()
    {
        var cached = _snapshotStore.Load();
        return cached is null
            ? null
            : new PlannerSnapshot(cached.Tasks ?? [], cached.PlanTitles ?? new(), cached.FetchedAt);
    }

    public void SaveSnapshot(PlannerSnapshot snapshot)
    {
        try
        {
            _snapshotStore.Save(new CachedSnapshot
            {
                Tasks = snapshot.Tasks.ToList(),
                PlanTitles = snapshot.PlanTitles.ToDictionary(),
                FetchedAt = snapshot.FetchedAt,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("A feladat-gyorsítótár nem menthető", ex);
        }
    }

    public void ClearCache()
    {
        try
        {
            File.Delete(_snapshotStore.FilePath);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Tervek, amelyekbe új feladat vehető fel (név szerint rendezve).</summary>
    public async Task<IReadOnlyList<PlannerPlan>> GetPlansAsync(IEnumerable<PlannerTask> knownTasks, CancellationToken ct = default)
    {
        var plans = await SafeGetPlansAsync(ct).ConfigureAwait(false);
        if (plans.Aggregate(false, (changed, p) => SetTitle(p.Id, p.Title) | changed))
        {
            SavePlanTitles();
        }

        var result = plans.ToDictionary(p => p.Id, p => p);
        foreach (var planId in knownTasks.Select(t => t.PlanId).Distinct())
        {
            if (!result.ContainsKey(planId) && _planTitles.TryGetValue(planId, out var title))
            {
                result[planId] = new PlannerPlan(planId, title);
            }
        }

        return result.Values
            .OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>A terv gyűjtőit a következő lekéréskor újra letölti (pl. ismeretlen bucketId esetén).</summary>
    public void InvalidateBuckets(string planId) => _buckets.TryRemove(planId, out _);

    public async Task<IReadOnlyList<PlannerBucket>> GetBucketsAsync(string planId, CancellationToken ct = default)
    {
        if (_buckets.TryGetValue(planId, out var cached))
        {
            return cached;
        }

        var buckets = await _client.GetBucketsAsync(planId, ct).ConfigureAwait(false);
        var ordered = buckets.OrderBy(b => b.OrderHint, StringComparer.Ordinal).ToList();
        _buckets[planId] = ordered;
        return ordered;
    }

    private async Task<IReadOnlyList<PlannerPlan>> SafeGetPlansAsync(CancellationToken ct)
    {
        try
        {
            return await _client.GetMyPlansAsync(ct).ConfigureAwait(false);
        }
        catch (GraphApiException ex)
        {
            // Néhány tenantban a /me/planner/plans tiltott; a feladatokból akkor is kiderülnek a tervek.
            Log.Warn($"A tervek listája nem kérhető le: {ex.Message}");
            return [];
        }
    }

    private bool SetTitle(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var previous = _planTitles.TryGetValue(id, out var existing) ? existing : null;
        _planTitles[id] = title;
        return previous != title;
    }

    private void SavePlanTitles()
    {
        try
        {
            _planTitleStore.Save(new Dictionary<string, string>(_planTitles));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("A tervnevek nem menthetők", ex);
        }
    }

    private sealed class CachedSnapshot
    {
        public List<PlannerTask>? Tasks { get; set; }
        public Dictionary<string, string>? PlanTitles { get; set; }
        public DateTimeOffset FetchedAt { get; set; }
    }
}

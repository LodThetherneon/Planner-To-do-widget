using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Storage;

namespace PlannerWidget.Core.Tasks;

/// <summary>
/// Kézi (húzással beállított) sorrend az aktív feladatokra. Egyetlen szabály: lejárt feladat nem kerülhet
/// határidő nélküli alá. Új (még nem rendezett) feladatok a határidő szerinti helyükre kerülnek.
/// </summary>
public static class ManualOrder
{
    /// <summary>
    /// A határidő szerint rendezett aktív lista átrendezése a mentett sorrend szerint.
    /// Üres mentett sorrendnél változatlan (= az automatikus sorrend).
    /// </summary>
    public static IReadOnlyList<PlannerTask> Apply(IReadOnlyList<PlannerTask> defaultSorted, IReadOnlyList<string> savedOrder, DateOnly today)
    {
        if (savedOrder.Count == 0 || defaultSorted.Count == 0)
        {
            return defaultSorted;
        }

        var byId = defaultSorted.ToDictionary(t => t.Id);
        var result = savedOrder.Distinct().Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        var placed = result.Select(t => t.Id).ToHashSet();

        // Az új feladatokat visszafelé szúrjuk be a határidő szerint utánuk következő (már elhelyezett) elé,
        // így az egymás utáni újak a saját határidő szerinti sorrendjükben maradnak.
        for (var i = defaultSorted.Count - 1; i >= 0; i--)
        {
            var task = defaultSorted[i];
            if (placed.Contains(task.Id))
            {
                continue;
            }

            var next = defaultSorted.Skip(i + 1).FirstOrDefault(t => placed.Contains(t.Id));
            var index = next is null ? result.Count : result.IndexOf(next);
            result.Insert(index, task);
            placed.Add(task.Id);
        }

        return Enforce(result, today);
    }

    /// <summary>
    /// Húzás eredménye: a látható (esetleg szűrt) lista új sorrendje a teljes sorrendbe illesztve
    /// (a látható feladatok a saját korábbi helyeiken cserélnek helyet). Ha a mozgatott feladat megsértené
    /// a szabályt, a legközelebbi megengedett helyre kerül (<c>Clamped</c> = igaz).
    /// </summary>
    public static (IReadOnlyList<PlannerTask> Order, bool Clamped) Reorder(
        IReadOnlyList<PlannerTask> fullOrder, IReadOnlyList<string> newVisibleOrder, string movedId, DateOnly today)
    {
        var byId = fullOrder.ToDictionary(t => t.Id);
        var visible = newVisibleOrder.Where(byId.ContainsKey).Distinct().ToList();
        var visibleSet = visible.ToHashSet();
        var slots = fullOrder.Select((t, i) => (t, i)).Where(x => visibleSet.Contains(x.t.Id)).Select(x => x.i).ToList();

        var result = fullOrder.ToList();
        for (var k = 0; k < slots.Count && k < visible.Count; k++)
        {
            result[slots[k]] = byId[visible[k]];
        }

        var clamped = false;
        if (byId.TryGetValue(movedId, out var moved))
        {
            var state = TaskOrdering.GetDueState(moved, today);
            var index = result.IndexOf(moved);
            if (state == DueState.Overdue)
            {
                var firstNoDue = result.FindIndex(t => TaskOrdering.GetDueState(t, today) == DueState.NoDueDate);
                if (firstNoDue >= 0 && index > firstNoDue)
                {
                    result.RemoveAt(index);
                    result.Insert(firstNoDue, moved);
                    clamped = true;
                }
            }
            else if (state == DueState.NoDueDate)
            {
                var lastOverdue = result.FindLastIndex(t => TaskOrdering.GetDueState(t, today) == DueState.Overdue);
                if (lastOverdue >= 0 && index < lastOverdue)
                {
                    result.RemoveAt(index);
                    result.Insert(lastOverdue, moved); // az eltávolítás után ez a lejárt utáni hely
                    clamped = true;
                }
            }
        }

        return (Enforce(result, today), clamped);
    }

    /// <summary>
    /// A szabály érvényesítése: a határidő nélküliek alá került lejárt feladatok (pl. mert közben lejártak)
    /// az első határidő nélküli elé kerülnek, egymás közti sorrendjüket megtartva.
    /// </summary>
    public static IReadOnlyList<PlannerTask> Enforce(IReadOnlyList<PlannerTask> order, DateOnly today)
    {
        var firstNoDue = -1;
        for (var i = 0; i < order.Count; i++)
        {
            if (TaskOrdering.GetDueState(order[i], today) == DueState.NoDueDate)
            {
                firstNoDue = i;
                break;
            }
        }

        if (firstNoDue < 0)
        {
            return order;
        }

        var misplaced = order.Skip(firstNoDue).Where(t => TaskOrdering.GetDueState(t, today) == DueState.Overdue).ToList();
        if (misplaced.Count == 0)
        {
            return order;
        }

        var result = order.Except(misplaced).ToList();
        result.InsertRange(firstNoDue, misplaced);
        return result;
    }
}

/// <summary>A kézi sorrend mentése (task-order.json, csak ezen a gépen).</summary>
public sealed class ManualOrderStore(string? path = null)
{
    private readonly JsonFileStore<List<string>> _store = new(path ?? AppPaths.TaskOrderFile);
    private List<string>? _order;

    public IReadOnlyList<string> Order => _order ??= _store.Load() ?? [];

    public bool HasOrder => Order.Count > 0;

    public void Save(IEnumerable<string> taskIds) => Write(taskIds.ToList());

    /// <summary>Vissza az automatikus (határidő szerinti) sorrendre.</summary>
    public void Clear() => Write([]);

    private void Write(List<string> order)
    {
        _order = order;
        try
        {
            _store.Save(order);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("A feladatsorrend nem menthető", ex);
        }
    }
}

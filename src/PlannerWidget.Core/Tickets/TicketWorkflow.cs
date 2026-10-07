using PlannerWidget.Core.Models;

namespace PlannerWidget.Core.Tickets;

/// <summary>
/// Az ügyterv gyűjtői (bucketjei) = a Beérkező ügyek lista státuszai. Egy Power Automate folyamat kb. 5 percenként
/// a gyűjtő alapján átírja a lista státuszát (és fordítva). A widget csak a gyűjtőt és a készültséget módosítja.
/// A nevek pontos egyezéssel számítanak (kis- és nagybetű, ékezet is).
/// </summary>
public static class TicketStatuses
{
    public const string New = "New";
    public const string Processed = "Processed";
    public const string Sos = "SOS";
    public const string InProgress = "In progress";
    public const string Questionable = "KÉRDÉSES";
    public const string Done = "Done";
    public const string Revisit = "Revisit";
}

public enum TicketActionKind
{
    /// <summary>Áthelyezés egy másik gyűjtőbe (<see cref="TicketAction.TargetStatus"/>).</summary>
    MoveToStatus,

    /// <summary>Késznek jelölés (percentComplete = 100) – a lezárást a meglévő folyamat kezeli.</summary>
    Complete,
}

/// <summary>A kártya státuszváltó gombja: felirat + mit csinál.</summary>
public sealed record TicketAction(TicketActionKind Kind, string ButtonText, string? TargetStatus = null);

/// <summary>A „…” menü egy pontja: felirat + cél-gyűjtő.</summary>
public sealed record TicketMenuOption(string Text, string TargetStatus);

public enum TicketForwardKey
{
    TakeOver,
    Start,
    Resume,
    Finish,
}

/// <summary>A karikára kattintva megjelenő „továbblépés” menü egy pontja.</summary>
public sealed record TicketForwardOption(TicketForwardKey Key, string Text, TicketActionKind Kind, string? TargetStatus, bool IsEnabled);

/// <summary>
/// Az ügy-állapotgép (gyűjtő = státusz). Fő lépés gyűjtőnként: New → Átvettem (Processed); Processed, SOS, KÉRDÉSES →
/// Elkezdem (In progress, 50%); Revisit → Folytatom (In progress, 50%) vagy közvetlenül Kész; In progress → Kész
/// (Done + 100% egy PATCH-ben).
/// Revisit-et és Done-t kézzel nem lehet választani: a Revisit-et a rendszer állítja (külsős válasz egy Done ügyre),
/// a Done a „Kész” eredménye.
/// </summary>
public static class TicketWorkflow
{
    /// <summary>„In progress”-be lépéskor a Planner készültsége (Planner: „Folyamatban”).</summary>
    public const int InProgressPercent = 50;

    /// <summary>A fő lépés a gyűjtő neve alapján; null, ha nincs (Done, kész feladat, ismeretlen állapot).</summary>
    public static TicketAction? NextAction(string? status, bool isCompleted)
    {
        if (isCompleted)
        {
            return null;
        }

        return status switch
        {
            TicketStatuses.New => new TicketAction(TicketActionKind.MoveToStatus, "Átvettem", TicketStatuses.Processed),
            TicketStatuses.Processed or TicketStatuses.Sos or TicketStatuses.Questionable =>
                new TicketAction(TicketActionKind.MoveToStatus, "Elkezdem", TicketStatuses.InProgress),
            TicketStatuses.Revisit => new TicketAction(TicketActionKind.MoveToStatus, "Folytatom", TicketStatuses.InProgress),
            TicketStatuses.InProgress => new TicketAction(TicketActionKind.Complete, "Kész"),
            _ => null, // Done vagy ismeretlen
        };
    }

    /// <summary>Befejezni csak „In progress” vagy „Revisit” állapotból lehet (Revisit: a válasz után rögtön le is zárható).</summary>
    public static bool CanComplete(string? status) => status is TicketStatuses.InProgress or TicketStatuses.Revisit;

    /// <summary>A rendszer újranyitotta (külsős válasz egy Done ügyre): „Új válasz érkezett” jelölés.</summary>
    public static bool IsNewReply(string? status, bool isCompleted) => !isCompleted && status == TicketStatuses.Revisit;

    /// <summary>Áthelyezéskor a készültség: In progress → 50%, egyébként nem változik (null).</summary>
    public static int? PercentForMove(string targetStatus) => targetStatus == TicketStatuses.InProgress ? InProgressPercent : null;

    /// <summary>
    /// A karika menüje: a fő lépés (ha áthelyezés) és a „Kész”. A „Kész” mindig látszik (a szabály látszódjon),
    /// de csak „In progress” és „Revisit” állapotban választható. Kész feladatnál / ismeretlen állapotnál üres.
    /// </summary>
    public static IReadOnlyList<TicketForwardOption> ForwardOptions(string? status, bool isCompleted)
    {
        if (isCompleted || status is null)
        {
            return [];
        }

        var options = new List<TicketForwardOption>();
        switch (status)
        {
            case TicketStatuses.New:
                options.Add(new(TicketForwardKey.TakeOver, "Átvettem → Processed", TicketActionKind.MoveToStatus, TicketStatuses.Processed, true));
                break;
            case TicketStatuses.Processed or TicketStatuses.Sos or TicketStatuses.Questionable:
                options.Add(new(TicketForwardKey.Start, "Elkezdem → In progress", TicketActionKind.MoveToStatus, TicketStatuses.InProgress, true));
                break;
            case TicketStatuses.Revisit:
                options.Add(new(TicketForwardKey.Resume, "Folytatom → In progress", TicketActionKind.MoveToStatus, TicketStatuses.InProgress, true));
                break;
        }

        var canFinish = CanComplete(status);
        options.Add(new(TicketForwardKey.Finish, canFinish ? "Kész – lezárás" : "Kész – előbb kezdd el",
            TicketActionKind.Complete, null, canFinish));
        return options;
    }

    /// <summary>Áthelyezhető-e az ügy a cél-állapotba (a karika menüjéből vagy a „…” menüből). Done/Revisit soha.</summary>
    public static bool CanMoveTo(string targetStatus, string? status, bool isCompleted) =>
        IsMenuOptionEnabled(targetStatus, status, isCompleted) ||
        ForwardOptions(status, isCompleted).Any(o => o.Kind == TicketActionKind.MoveToStatus && o.TargetStatus == targetStatus && o.IsEnabled);

    /// <summary>A „…” menü pontjai, ebben a sorrendben (Revisit és Done kézzel nem választható).</summary>
    public static IReadOnlyList<TicketMenuOption> MenuOptions { get; } =
    [
        new("Sürgős (SOS)", TicketStatuses.Sos),
        new("Kérdéses (KÉRDÉSES)", TicketStatuses.Questionable),
        new("Vissza New-ba", TicketStatuses.New),
    ];

    /// <summary>Van-e „…” menü: nem kész ügyen, ismert állapotnál.</summary>
    public static bool HasMenu(string? status, bool isCompleted) => !isCompleted && status is not null;

    /// <summary>A menüpont az aktuális állapotra tiltott (oda nem lehet „átlépni”).</summary>
    public static bool IsMenuOptionEnabled(string targetStatus, string? status, bool isCompleted) =>
        HasMenu(status, isCompleted) && targetStatus != status && MenuOptions.Any(o => o.TargetStatus == targetStatus);
}

/// <summary>Az ügyterv gyűjtői név ↔ azonosító szerint (a Graph válaszából; az azonosítókat sosem égetjük be).</summary>
public sealed class TicketBuckets
{
    private readonly Dictionary<string, string> _nameById;
    private readonly Dictionary<string, string> _idByName;

    public TicketBuckets(string planId, IEnumerable<PlannerBucket> buckets)
    {
        PlanId = planId;
        var list = buckets.ToList();
        _nameById = list.GroupBy(b => b.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        // Pontos egyezés; azonos nevű gyűjtőkből az első (orderHint szerint) számít.
        _idByName = list.GroupBy(b => b.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
    }

    public string PlanId { get; }

    public bool Knows(string bucketId) => _nameById.ContainsKey(bucketId);

    public string? NameOf(string? bucketId) =>
        bucketId is not null && _nameById.TryGetValue(bucketId, out var name) ? name : null;

    public string? IdOf(string status) => _idByName.TryGetValue(status, out var id) ? id : null;

    /// <summary>
    /// Az ügy állapota a gyűjtője alapján. Üres vagy (újratöltés után is) ismeretlen gyűjtő → New
    /// (a folyamat az új ügyeket a New gyűjtőben hozza létre).
    /// </summary>
    public string StatusOf(string? bucketId) => NameOf(bucketId) ?? TicketStatuses.New;

    /// <summary>Van-e az ügyterv feladatai között olyan, amelynek a gyűjtőjét nem ismerjük (pl. új gyűjtő) → újratöltés kell.</summary>
    public bool HasUnknownBucket(IEnumerable<PlannerTask> tasks) =>
        tasks.Any(t => t.PlanId == PlanId && !string.IsNullOrEmpty(t.BucketId) && !Knows(t.BucketId));
}

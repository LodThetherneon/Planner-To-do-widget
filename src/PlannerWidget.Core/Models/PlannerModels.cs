namespace PlannerWidget.Core.Models;

/// <summary>Egy Planner feladat a widget szemszögéből (a Graph válaszból leképezve).</summary>
public sealed record PlannerTask
{
    public required string Id { get; init; }
    public required string PlanId { get; init; }
    public string BucketId { get; init; } = "";
    public required string Title { get; init; }
    public int PercentComplete { get; init; }

    /// <summary>Határidő helyi idő szerinti napként (a Graph UTC időpontjából számolva).</summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>Planner prioritás: 0–10 (1 = sürgős, 3 = fontos, 5 = közepes, 9 = alacsony).</summary>
    public int Priority { get; init; } = 5;

    /// <summary>Optimista konkurenciakezeléshez szükséges ETag (If-Match fejléc).</summary>
    public string ETag { get; init; } = "";

    public int ChecklistItemCount { get; init; }
    public int ActiveChecklistItemCount { get; init; }
    public bool HasDescription { get; init; }
    public DateTimeOffset? CreatedDateTime { get; init; }
    public DateTimeOffset? CompletedDateTime { get; init; }

    public bool IsCompleted => PercentComplete >= 100;
    public bool IsInProgress => PercentComplete is > 0 and < 100;
    public TaskPriority PriorityLevel => TaskPriorityExtensions.FromPlanner(Priority);
    public int CompletedChecklistItemCount => ChecklistItemCount - ActiveChecklistItemCount;
}

public sealed record PlannerPlan(string Id, string Title);

public sealed record PlannerBucket(string Id, string Name, string OrderHint);

public sealed record UserProfile(string Id, string DisplayName, string? Mail);

public sealed record ChecklistItem(string Id, string Title, bool IsChecked, string OrderHint);

public sealed record PlannerTaskDetails(string TaskId, string ETag, string Description, IReadOnlyList<ChecklistItem> Checklist);

/// <summary>Egy frissítés teljes eredménye: feladatok + a tervek nevei.</summary>
public sealed record PlannerSnapshot(
    IReadOnlyList<PlannerTask> Tasks,
    IReadOnlyDictionary<string, string> PlanTitles,
    DateTimeOffset FetchedAt);

public enum TaskPriority
{
    Urgent,
    Important,
    Medium,
    Low,
}

public static class TaskPriorityExtensions
{
    /// <summary>A Planner 0–10 skáláját a felületen látható 4 szintre képezi le.</summary>
    public static TaskPriority FromPlanner(int value) => value switch
    {
        <= 1 => TaskPriority.Urgent,
        <= 4 => TaskPriority.Important,
        <= 7 => TaskPriority.Medium,
        _ => TaskPriority.Low,
    };

    /// <summary>A Planner webes felülete által használt kanonikus értékek.</summary>
    public static int ToPlanner(this TaskPriority priority) => priority switch
    {
        TaskPriority.Urgent => 1,
        TaskPriority.Important => 3,
        TaskPriority.Medium => 5,
        _ => 9,
    };

    public static string ToDisplayName(this TaskPriority priority) => priority switch
    {
        TaskPriority.Urgent => "Sürgős",
        TaskPriority.Important => "Fontos",
        TaskPriority.Medium => "Közepes",
        _ => "Alacsony",
    };
}

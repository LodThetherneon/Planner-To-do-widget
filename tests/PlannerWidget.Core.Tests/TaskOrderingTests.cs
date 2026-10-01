using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Tasks;

namespace PlannerWidget.Core.Tests;

public class TaskOrderingTests
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private static PlannerTask Task(string title, int? dueOffset, int percent = 0, int priority = 5, DateTimeOffset? completed = null) => new()
    {
        Id = title,
        PlanId = "p",
        Title = title,
        DueDate = dueOffset is { } o ? Today.AddDays(o) : null,
        PercentComplete = percent,
        Priority = priority,
        CompletedDateTime = completed,
    };

    [Fact]
    public void Active_order_overdue_soon_later_none()
    {
        var tasks = new[]
        {
            Task("nincs", null),
            Task("később", 30),
            Task("holnap", 1),
            Task("lejárt", -2),
            Task("ma", 0),
            Task("kész", -5, percent: 100),
        };

        var sorted = TaskOrdering.SortActive(tasks, Today).Select(t => t.Title);

        Assert.Equal(["lejárt", "ma", "holnap", "később", "nincs"], sorted);
    }

    [Fact]
    public void Same_due_date_sorted_by_priority()
    {
        var tasks = new[] { Task("alacsony", 2, priority: 9), Task("sürgős", 2, priority: 1) };
        Assert.Equal(["sürgős", "alacsony"], TaskOrdering.SortActive(tasks, Today).Select(t => t.Title));
    }

    [Fact]
    public void Completed_most_recent_first()
    {
        var tasks = new[]
        {
            Task("régi", 0, 100, completed: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)),
            Task("friss", 0, 100, completed: new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero)),
        };
        Assert.Equal(["friss", "régi"], TaskOrdering.SortCompleted(tasks).Select(t => t.Title));
    }

    [Fact]
    public void Counts()
    {
        var counts = TaskOrdering.Count([Task("a", -1), Task("b", 0), Task("c", 5), Task("d", -3, 100)], Today);
        Assert.Equal(new TaskCounts(3, 1, 1), counts);
    }

    [Theory]
    [InlineData(null, "Nincs határidő")]
    [InlineData(0, "Ma")]
    [InlineData(1, "Holnap")]
    [InlineData(-1, "Tegnap lejárt")]
    [InlineData(-4, "4 napja lejárt")]
    [InlineData(2, "Szombat")] // 2026-10-03
    [InlineData(14, "okt. 15.")]
    public void Due_text(int? offset, string expected)
    {
        DateOnly? due = offset is { } o ? Today.AddDays(o) : null;
        Assert.Equal(expected, DueDateFormatter.Format(due, Today));
    }

    [Fact]
    public void Priority_mapping_matches_planner()
    {
        Assert.Equal(TaskPriority.Urgent, TaskPriorityExtensions.FromPlanner(1));
        Assert.Equal(TaskPriority.Important, TaskPriorityExtensions.FromPlanner(3));
        Assert.Equal(TaskPriority.Medium, TaskPriorityExtensions.FromPlanner(5));
        Assert.Equal(TaskPriority.Low, TaskPriorityExtensions.FromPlanner(9));
        Assert.Equal(3, TaskPriority.Important.ToPlanner());
    }
}

public class DueDateConverterTests
{
    private static readonly TimeZoneInfo Budapest = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");

    [Fact]
    public void Late_utc_time_maps_to_next_local_day()
    {
        // Magyar éjfél (nyári idő) UTC-ben az előző nap 22:00 – a régi verzió itt egy nappal korábbit mutatott.
        var utc = new DateTimeOffset(2026, 6, 14, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 6, 15), DueDateConverter.FromGraph(utc, Budapest));
    }

    [Fact]
    public void Round_trip_keeps_the_day()
    {
        var day = new DateOnly(2026, 12, 24);
        var graph = DueDateConverter.ToGraph(day, Budapest)!;
        Assert.Equal("2026-12-24T11:00:00Z", graph);
        Assert.Equal(day, DueDateConverter.FromGraph(DateTimeOffset.Parse(graph), Budapest));
    }
}

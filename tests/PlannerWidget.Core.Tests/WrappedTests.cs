using PlannerWidget.Core.Models;
using PlannerWidget.Core.Wrapped;

namespace PlannerWidget.Core.Tests;

public class WrappedTests
{
    private static PlannerTask T(string id, string title, string plan, DateTimeOffset created, DateTimeOffset? done = null) => new()
    {
        Id = id, PlanId = plan, Title = title, CreatedDateTime = created,
        PercentComplete = done is null ? 0 : 100, CompletedDateTime = done,
    };

    [Fact]
    public void Previous_month_report_is_built_on_first_update_of_new_month_and_marked_unseen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wrapped-{Guid.NewGuid():N}.json");
        var sep = new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.FromHours(2));
        var tasks = new[]
        {
            T("a", "Egy", "p", sep, sep.AddDays(1)),
            T("b", "Kettő", "p", sep.AddDays(2)),
            T("c", "[#7] Ügy", "u", sep.AddDays(3), sep.AddDays(3).AddHours(4)),
            T("d", "Régi", "p", sep.AddDays(-60)),
        };
        var now = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(2));
        var svc = new WrappedService(path, () => now);
        svc.Update(tasks, "u");

        var r = Assert.Single(svc.Reports);
        Assert.Equal("2026-09", r.Month);
        Assert.Equal(2, r.TasksArrived);
        Assert.Equal(1, r.TasksArrivedDone);
        Assert.Equal(1, r.TicketsArrived);
        Assert.Equal(1, r.TicketsArrivedDone);
        Assert.Equal(2, r.TotalCompleted);
        Assert.Equal("Régi", r.OldestOpenTitle);
        Assert.True(svc.HasUnseen);

        svc.MarkSeen();
        Assert.False(new WrappedService(path, () => now).HasUnseen);
        svc.Update(tasks, "u"); // nem készül újra
        Assert.Single(svc.Reports);
        File.Delete(path);
    }

    [Fact]
    public void Recreated_ticket_with_new_task_id_counts_once()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wrapped-{Guid.NewGuid():N}.json");
        var d = new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.FromHours(2));
        var now = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.FromHours(2));
        var svc = new WrappedService(path, () => now);
        svc.Update([T("x1", "[#5] Ügy", "u", d)], "u");
        svc.Update([T("x2", "[#5] Ügy", "u", d.AddDays(1), d.AddDays(2))], "u");
        Assert.Equal(1, svc.Build("2026-09", now).TicketsArrived);
        File.Delete(path);
    }
}

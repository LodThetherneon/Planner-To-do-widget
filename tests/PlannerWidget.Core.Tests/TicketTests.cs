using PlannerWidget.Core.Models;
using PlannerWidget.Core.Settings;
using PlannerWidget.Core.Tickets;

namespace PlannerWidget.Core.Tests;

public class TicketParserTests
{
    private const string TicketPlan = "plan-ugyek";

    private static PlannerTask Task(string title, string planId = TicketPlan) => new() { Id = title, PlanId = planId, Title = title };

    [Theory]
    [InlineData("[#123] Laborbeszerzés kérdése", 123)]
    [InlineData("[#1]x", 1)]
    [InlineData("  [# 42 ]  Szóközökkel", 42)]
    [InlineData("[#007] Vezető nullák", 7)]
    public void Parses_id_from_title_prefix(string title, int expected) =>
        Assert.Equal(expected, TicketParser.ParseId(title));

    [Theory]
    [InlineData("Laborbeszerzés kérdése")]          // hiányzó minta
    [InlineData("Kérdés [#123] a közepén")]          // nem a cím elején
    [InlineData("[#abc] Nem szám")]
    [InlineData("[#12a] Vegyes")]
    [InlineData("[#-5] Negatív")]
    [InlineData("[#0] Nulla")]
    [InlineData("[#] Üres")]
    [InlineData("[#1 2] Szóköz a számban")]
    [InlineData("[#99999999999] Túl nagy")]
    [InlineData("#123 Zárójel nélkül")]
    [InlineData("")]
    public void Missing_or_invalid_prefix_gives_no_id(string title) =>
        Assert.Null(TicketParser.ParseId(title));

    [Fact]
    public void Task_in_ticket_plan_is_ticket()
    {
        var info = TicketParser.TryGet(Task("[#123] Tárgy"), TicketPlan);
        Assert.NotNull(info);
        Assert.Equal(123, info.Id);
    }

    [Fact]
    public void Task_in_ticket_plan_without_prefix_is_ticket_without_id()
    {
        var info = TicketParser.TryGet(Task("Kézzel felvett ügy"), TicketPlan);
        Assert.NotNull(info);
        Assert.Null(info.Id);
    }

    [Fact]
    public void Task_in_other_plan_is_not_ticket() =>
        Assert.Null(TicketParser.TryGet(Task("[#123] Tárgy", "plan-mas"), TicketPlan));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_ticket_plan_means_no_detection(string? planId) =>
        Assert.Null(TicketParser.TryGet(Task("[#123] Tárgy", planId ?? ""), planId));

    [Theory]
    [InlineData("[#123] Laborbeszerzés kérdése", "Laborbeszerzés kérdése")]
    [InlineData("  [# 5 ]   Szóközök  ", "Szóközök")]
    [InlineData("[#123]", "[#123]")]                       // csak előtag: marad az eredeti
    [InlineData("[#abc] Nem szám", "[#abc] Nem szám")]
    [InlineData("Sima cím", "Sima cím")]
    public void StripPrefix(string title, string expected) => Assert.Equal(expected, TicketParser.StripPrefix(title));
}

public class TicketLinkTests
{
    [Fact]
    public void Default_template_builds_dispform_link() =>
        Assert.Equal(
            "https://laesze.sharepoint.com/sites/MFI-RFK/Lists/Berkez%20gyek/DispForm.aspx?ID=123",
            TicketLinks.Build(TicketLinks.DefaultUrlTemplate, 123)?.AbsoluteUri);

    [Fact]
    public void Custom_template_and_case_insensitive_placeholder() =>
        Assert.Equal("https://example.com/t/42/view", TicketLinks.Build("https://example.com/t/{ID}/view", 42)?.AbsoluteUri);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/ticket")]          // nincs {id}
    [InlineData("example.com/{id}")]                    // nem abszolút
    [InlineData("file:///C:/{id}.txt")]                 // nem http(s)
    [InlineData("javascript:alert({id})")]
    public void Invalid_template_gives_no_link(string? template)
    {
        Assert.Null(TicketLinks.Build(template, 1));
        Assert.False(TicketLinks.IsValidTemplate(template));
    }
}

public sealed class TicketTrackerTests : IDisposable
{
    private const string Plan = "plan-ugyek";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pw-ticket-" + Guid.NewGuid().ToString("N"));

    private string StateFile => Path.Combine(_dir, "ticket-state.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Minden feladat-azonosító saját (determinisztikus) ticket-számot kap, hacsak nincs megadva.</summary>
    private static PlannerTask Task(string id, string planId = Plan, bool completed = false, int? ticketNo = null) => new()
    {
        Id = id,
        PlanId = planId,
        Title = $"[#{ticketNo ?? (id.Aggregate(7, (h, c) => (h * 31 + c) & 0xFFFFF) + 1)}] {id}",
        PercentComplete = completed ? 100 : 0,
    };

    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Ticket_recreated_with_new_task_id_soon_is_not_notified()
    {
        // A flow törli a feladatot (nincs felelős), majd új azonosítóval újra létrehozza (újra van).
        var tracker = new TicketTracker(StateFile);
        tracker.Update([Task("regi", ticketNo: 123)], Plan, T0);
        Assert.Empty(tracker.Update([], Plan, T0.AddMinutes(10)));
        Assert.Empty(tracker.Update([Task("uj", ticketNo: 123)], Plan, T0.AddMinutes(30)));

        // Egy valóban új #ID viszont szól.
        Assert.Equal(["masik"], tracker.Update([Task("uj", ticketNo: 123), Task("masik", ticketNo: 124)], Plan, T0.AddHours(1)).Select(t => t.Id));
    }

    [Fact]
    public void Ticket_returning_after_the_window_is_notified()
    {
        var tracker = new TicketTracker(StateFile);
        tracker.Update([Task("regi", ticketNo: 123)], Plan, T0);
        tracker.Update([], Plan, T0.AddHours(1));
        var returned = tracker.Update([Task("uj", ticketNo: 123)], Plan, T0 + TicketTracker.ReturnWindow + TimeSpan.FromHours(2));
        Assert.Equal(["uj"], returned.Select(t => t.Id));
    }

    [Fact]
    public void Recent_ticket_ids_survive_restart()
    {
        new TicketTracker(StateFile).Update([Task("regi", ticketNo: 123)], Plan, T0);
        var restarted = new TicketTracker(StateFile);
        Assert.Empty(restarted.Update([Task("uj", ticketNo: 123)], Plan, T0.AddHours(3)));
    }

    [Fact]
    public void Old_and_new_task_of_same_ticket_together_do_not_notify_twice()
    {
        // Ha a folyamat előbb hozza létre az újat, és csak utána törli a régit: egy frissítés mindkettőt láthatja.
        var tracker = new TicketTracker(StateFile);
        tracker.Update([Task("regi", ticketNo: 123)], Plan, T0);
        Assert.Empty(tracker.Update([Task("regi", ticketNo: 123), Task("uj", ticketNo: 123)], Plan, T0.AddMinutes(5)));
        Assert.Empty(tracker.Update([Task("uj", ticketNo: 123)], Plan, T0.AddMinutes(10)));
    }

    [Fact]
    public void Same_task_returning_after_assignee_restore_is_not_notified()
    {
        // A Plannerben levett felelőst a folyamat visszaállítja: ugyanaz a feladat-azonosító tér vissza.
        var tracker = new TicketTracker(StateFile);
        tracker.Update([Task("feladat", ticketNo: 77)], Plan, T0);
        Assert.Empty(tracker.Update([], Plan, T0.AddHours(2)));
        Assert.Empty(tracker.Update([Task("feladat", ticketNo: 77)], Plan, T0.AddHours(5)));
    }

    [Fact]
    public void Recreated_ticket_leaves_no_stale_task_id_and_is_tracked_from_then_on()
    {
        var tracker = new TicketTracker(StateFile);
        tracker.Update([Task("regi", ticketNo: 123)], Plan, T0);
        tracker.Update([], Plan, T0.AddMinutes(10));
        tracker.Update([Task("uj", ticketNo: 123)], Plan, T0.AddMinutes(20));

        var state = File.ReadAllText(StateFile);
        Assert.DoesNotContain("\"regi\"", state);
        Assert.Contains("\"uj\"", state);

        // Később (a 24 órán túl) az új feladat már ismert: nem „új ügy”, akármeddig látszik.
        Assert.Empty(tracker.Update([Task("uj", ticketNo: 123)], Plan, T0 + TicketTracker.ReturnWindow + TimeSpan.FromDays(3)));
    }

    [Fact]
    public void Deleted_ticket_disappears_from_known_ids()
    {
        var tracker = new TicketTracker(StateFile);
        tracker.Update([Task("a"), Task("b")], Plan, T0);
        Assert.Empty(tracker.Update([Task("a")], Plan, T0.AddMinutes(5)));
        Assert.DoesNotContain("\"b\"", File.ReadAllText(StateFile));
    }

    [Fact]
    public void FindNew_returns_only_open_unknown_tasks_of_ticket_plan()
    {
        var tasks = new[] { Task("régi"), Task("új"), Task("új-kész", completed: true), Task("más-terv", "plan-x") };
        var fresh = TicketTracker.FindNew(tasks, Plan, new HashSet<string> { "régi" });
        Assert.Equal(["új"], fresh.Select(t => t.Id));
    }

    [Fact]
    public void FindNew_nothing_new() =>
        Assert.Empty(TicketTracker.FindNew([Task("a"), Task("b")], Plan, new HashSet<string> { "a", "b" }));

    [Fact]
    public void First_update_is_baseline_without_notification()
    {
        var tracker = new TicketTracker(StateFile);
        Assert.Empty(tracker.Update([Task("a"), Task("b")], Plan));
        Assert.True(File.Exists(StateFile));
    }

    [Fact]
    public void New_ticket_reported_once_and_persisted_across_restart()
    {
        var tracker = new TicketTracker(StateFile);
        tracker.Update([Task("a")], Plan);

        Assert.Equal(["b"], tracker.Update([Task("a"), Task("b")], Plan).Select(t => t.Id));
        Assert.Empty(tracker.Update([Task("a"), Task("b")], Plan));

        // „Újraindítás”: új példány ugyanazzal a fájllal – a már ismert ügyről nem szól újra, az újról igen.
        var restarted = new TicketTracker(StateFile);
        Assert.Equal(["c"], restarted.Update([Task("a"), Task("b"), Task("c")], Plan).Select(t => t.Id));
    }

    [Fact]
    public void Changing_ticket_plan_takes_new_baseline()
    {
        var tracker = new TicketTracker(StateFile);
        tracker.Update([Task("a")], Plan);
        Assert.Empty(tracker.Update([Task("x", "plan-uj"), Task("y", "plan-uj")], "plan-uj"));
        Assert.Equal(["z"], tracker.Update([Task("x", "plan-uj"), Task("z", "plan-uj")], "plan-uj").Select(t => t.Id));
    }

    [Fact]
    public void No_ticket_plan_means_no_notifications()
    {
        var tracker = new TicketTracker(StateFile);
        Assert.Empty(tracker.Update([Task("a")], null));
        Assert.False(File.Exists(StateFile));
    }
}

public sealed class TicketSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pw-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Old_settings_without_tickets_section_load_with_defaults()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            {
              "schemaVersion": 2,
              "theme": "Dark",
              "refreshIntervalMinutes": 10,
              "defaultBucketByPlan": { "p1": "b1" },
              "attendance": { "pdfPath": "C:\\jelenleti.pdf", "workRunning": false }
            }
            """);

        var store = new SettingsStore(path);
        var s = store.Current;

        Assert.Equal(AppSettings.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Equal(AppTheme.Dark, s.Theme);
        Assert.Equal(10, s.RefreshIntervalMinutes);
        Assert.Equal("b1", s.DefaultBucketByPlan["p1"]);
        Assert.NotNull(s.Tickets);
        Assert.Equal(TicketSettings.DefaultPlanId, s.Tickets.PlanId);
        Assert.False(s.Tickets.NoPlan);
        Assert.Equal(TicketLinks.DefaultUrlTemplate, s.Tickets.UrlTemplate);
        Assert.True(s.Tickets.NotifyNewTickets);
        Assert.True(s.Tickets.ConfirmCloseTicket);
    }

    [Fact]
    public void Tickets_section_round_trips_and_blank_template_falls_back_to_default()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        store.Update(s =>
        {
            s.Tickets.PlanId = "plan-ugyek";
            s.Tickets.UrlTemplate = "  ";
            s.Tickets.NotifyNewTickets = false;
        });

        var reloaded = new SettingsStore(path).Current.Tickets;
        Assert.Equal("plan-ugyek", reloaded.PlanId);
        Assert.Equal(TicketLinks.DefaultUrlTemplate, reloaded.UrlTemplate);
        Assert.False(reloaded.NotifyNewTickets);
        Assert.True(reloaded.ConfirmCloseTicket);
    }

    [Fact]
    public void Tickets_section_without_plan_id_gets_default_plan()
    {
        // Az előző verzió a null PlanId-t nem írta ki (WhenWritingNull) – ez „hiányzó”, nem „Nincs”.
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """
            { "schemaVersion": 2, "tickets": { "urlTemplate": "https://example.com/{id}", "notifyNewTickets": false } }
            """);

        var t = new SettingsStore(path).Current.Tickets;
        Assert.Equal(TicketSettings.DefaultPlanId, t.PlanId);
        Assert.Equal("https://example.com/{id}", t.UrlTemplate);
        Assert.False(t.NotifyNewTickets);
    }

    [Fact]
    public void New_install_gets_default_plan() =>
        Assert.Equal(TicketSettings.DefaultPlanId, new SettingsStore(Path.Combine(_dir, "settings.json")).Current.Tickets.PlanId);

    [Fact]
    public void Explicit_none_survives_restart()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        new SettingsStore(path).Update(s =>
        {
            s.Tickets.PlanId = null;
            s.Tickets.NoPlan = true;
        });

        // „Újraindítás” kétszer is: a mentett „Nincs” nem íródik felül az alaptervvel.
        var reloaded = new SettingsStore(path);
        Assert.Null(reloaded.Current.Tickets.PlanId);
        Assert.True(reloaded.Current.Tickets.NoPlan);
        reloaded.Save();
        Assert.Null(new SettingsStore(path).Current.Tickets.PlanId);
    }

    [Fact]
    public void Plan_id_wins_over_stale_none_flag()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 2, "tickets": { "planId": "plan-masik", "noPlan": true } }""");

        var t = new SettingsStore(path).Current.Tickets;
        Assert.Equal("plan-masik", t.PlanId);
        Assert.False(t.NoPlan);
    }

    [Fact]
    public void Null_tickets_section_is_normalized()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "schemaVersion": 2, "tickets": null }""");
        Assert.NotNull(new SettingsStore(path).Current.Tickets);
    }
}

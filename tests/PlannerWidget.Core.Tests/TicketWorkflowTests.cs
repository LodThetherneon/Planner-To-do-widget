using System.Net;
using System.Text;
using System.Text.Json;
using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Tasks;
using PlannerWidget.Core.Tickets;

namespace PlannerWidget.Core.Tests;

public class TicketWorkflowTests
{
    // ---- fő lépés gyűjtőnként ------------------------------------------------------------------

    [Theory]
    [InlineData("New", "Átvettem", "Processed")]
    [InlineData("Processed", "Elkezdem", "In progress")]
    [InlineData("SOS", "Elkezdem", "In progress")]
    [InlineData("KÉRDÉSES", "Elkezdem", "In progress")]
    [InlineData("Revisit", "Folytatom", "In progress")]
    public void Main_step_moves(string status, string button, string target)
    {
        var action = TicketWorkflow.NextAction(status, isCompleted: false);
        Assert.NotNull(action);
        Assert.Equal(TicketActionKind.MoveToStatus, action.Kind);
        Assert.Equal(button, action.ButtonText);
        Assert.Equal(target, action.TargetStatus);
    }

    [Fact]
    public void In_progress_main_step_is_finish()
    {
        var action = TicketWorkflow.NextAction("In progress", isCompleted: false);
        Assert.NotNull(action);
        Assert.Equal(TicketActionKind.Complete, action.Kind);
        Assert.Equal("Kész", action.ButtonText);
    }

    [Theory]
    [InlineData("Done", false)]
    [InlineData("New", true)]          // kész feladat: nincs lépés
    [InlineData("In progress", true)]
    [InlineData(null, false)]          // a gyűjtők még nincsenek betöltve
    [InlineData("Ismeretlen", false)]
    [InlineData("new", false)]         // pontos egyezés
    [InlineData("Kérdéses", false)]
    public void No_main_step(string? status, bool completed) => Assert.Null(TicketWorkflow.NextAction(status, completed));

    // ---- befejezés csak In progress-ből ----------------------------------------------------------

    [Theory]
    [InlineData("In progress", true)]
    [InlineData("Revisit", true)]      // válasz után közvetlenül is lezárható
    [InlineData("New", false)]
    [InlineData("Processed", false)]
    [InlineData("SOS", false)]
    [InlineData("KÉRDÉSES", false)]
    [InlineData("Done", false)]
    [InlineData(null, false)]
    public void Finish_only_from_in_progress_or_revisit(string? status, bool expected) => Assert.Equal(expected, TicketWorkflow.CanComplete(status));

    // ---- készültség áthelyezéskor --------------------------------------------------------------

    [Theory]
    [InlineData("In progress", 50)]
    [InlineData("Processed", null)]
    [InlineData("SOS", null)]
    [InlineData("KÉRDÉSES", null)]
    [InlineData("New", null)]
    public void Percent_for_move(string target, int? expected) => Assert.Equal(expected, TicketWorkflow.PercentForMove(target));

    [Theory]
    [InlineData("Revisit", false, true)]
    [InlineData("Revisit", true, false)]
    [InlineData("In progress", false, false)]
    [InlineData(null, false, false)]
    public void New_reply_marker(string? status, bool completed, bool expected) =>
        Assert.Equal(expected, TicketWorkflow.IsNewReply(status, completed));

    // ---- a karika menüje -----------------------------------------------------------------------

    [Theory]
    [InlineData("New", "TakeOver:Processed:True|Finish::False")]
    [InlineData("Processed", "Start:In progress:True|Finish::False")]
    [InlineData("SOS", "Start:In progress:True|Finish::False")]
    [InlineData("KÉRDÉSES", "Start:In progress:True|Finish::False")]
    [InlineData("Revisit", "Resume:In progress:True|Finish::True")]
    [InlineData("In progress", "Finish::True")]
    [InlineData("Done", "Finish::False")]
    public void Forward_options_per_status(string status, string expected)
    {
        var actual = string.Join("|", TicketWorkflow.ForwardOptions(status, isCompleted: false)
            .Select(o => $"{o.Key}:{o.TargetStatus}:{o.IsEnabled}"));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Forward_option_texts_and_empty_cases()
    {
        Assert.Equal("Átvettem → Processed", TicketWorkflow.ForwardOptions("New", false).First().Text);
        Assert.Equal("Elkezdem → In progress", TicketWorkflow.ForwardOptions("SOS", false).First().Text);
        Assert.Equal("Folytatom → In progress", TicketWorkflow.ForwardOptions("Revisit", false).First().Text);
        Assert.Equal("Kész – lezárás", TicketWorkflow.ForwardOptions("In progress", false).Single().Text);
        Assert.Equal("Kész – lezárás", TicketWorkflow.ForwardOptions("Revisit", false).Last().Text);
        Assert.Equal("Kész – előbb kezdd el", TicketWorkflow.ForwardOptions("New", false).Last().Text);
        Assert.Empty(TicketWorkflow.ForwardOptions("New", isCompleted: true));
        Assert.Empty(TicketWorkflow.ForwardOptions(null, isCompleted: false));
    }

    // ---- a „…” menü ----------------------------------------------------------------------------

    [Fact]
    public void Menu_is_sos_questionable_back_to_new()
    {
        Assert.Equal(["Sürgős (SOS)", "Kérdéses (KÉRDÉSES)", "Vissza New-ba"], TicketWorkflow.MenuOptions.Select(o => o.Text));
        Assert.Equal(["SOS", "KÉRDÉSES", "New"], TicketWorkflow.MenuOptions.Select(o => o.TargetStatus));
    }

    [Theory]
    [InlineData("New", "New", false)]               // az aktuális állapot menüpontja tiltott
    [InlineData("New", "SOS", true)]
    [InlineData("SOS", "SOS", false)]
    [InlineData("SOS", "New", true)]
    [InlineData("KÉRDÉSES", "KÉRDÉSES", false)]
    [InlineData("In progress", "KÉRDÉSES", true)]
    [InlineData("Revisit", "SOS", true)]
    [InlineData("In progress", "Revisit", false)]    // Revisit-et a rendszer állítja
    [InlineData("In progress", "Done", false)]       // Done csak a „Kész”-szel
    public void Menu_option_enabled(string status, string target, bool expected) =>
        Assert.Equal(expected, TicketWorkflow.IsMenuOptionEnabled(target, status, isCompleted: false));

    [Fact]
    public void No_menu_on_completed_or_unknown_state()
    {
        Assert.False(TicketWorkflow.HasMenu("In progress", isCompleted: true));
        Assert.False(TicketWorkflow.HasMenu(null, isCompleted: false));
        Assert.True(TicketWorkflow.HasMenu("New", isCompleted: false));
    }

    // ---- közös szabály: hová lehet áthelyezni ---------------------------------------------------

    [Theory]
    [InlineData("New", "Processed", true)]          // Átvettem
    [InlineData("New", "In progress", false)]       // New-ból nincs közvetlen Elkezdem
    [InlineData("Processed", "In progress", true)]
    [InlineData("SOS", "In progress", true)]
    [InlineData("KÉRDÉSES", "In progress", true)]
    [InlineData("Revisit", "In progress", true)]    // Folytatom
    [InlineData("In progress", "Processed", false)]
    [InlineData("In progress", "SOS", true)]        // „…”
    [InlineData("Processed", "Revisit", false)]     // Revisit kézzel soha
    [InlineData("Processed", "Done", false)]        // Done kézzel soha (csak „Kész”)
    [InlineData("New", "New", false)]
    public void Can_move_to(string status, string target, bool expected) =>
        Assert.Equal(expected, TicketWorkflow.CanMoveTo(target, status, isCompleted: false));

    [Fact]
    public void Cannot_move_completed_ticket() => Assert.False(TicketWorkflow.CanMoveTo("In progress", "Processed", isCompleted: true));

    [Fact]
    public void Every_open_status_reaches_finish_in_at_most_two_steps()
    {
        foreach (var start in new[] { "New", "Processed", "SOS", "KÉRDÉSES", "Revisit", "In progress" })
        {
            var status = start;
            for (var step = 0; step < 3; step++)
            {
                var action = TicketWorkflow.NextAction(status, false)!;
                if (action.Kind == TicketActionKind.Complete)
                {
                    break;
                }

                status = action.TargetStatus;
                Assert.True(step < 2, $"{start}: túl hosszú út");
            }

            Assert.Equal("In progress", status);
        }
    }
}

public class TicketBucketsTests
{
    private static readonly PlannerBucket[] Buckets =
    [
        new("b-new", "New", "1"), new("b-proc", "Processed", "2"), new("b-sos", "SOS", "3"),
        new("b-prog", "In progress", "4"), new("b-q", "KÉRDÉSES", "5"), new("b-done", "Done", "6"), new("b-rev", "Revisit", "7"),
    ];

    [Fact]
    public void Maps_names_and_ids_exactly()
    {
        var map = new TicketBuckets("plan", Buckets);
        Assert.Equal("b-prog", map.IdOf("In progress"));
        Assert.Equal("KÉRDÉSES", map.NameOf("b-q"));
        Assert.Null(map.IdOf("in progress"));
        Assert.Null(map.NameOf("ismeretlen"));
        Assert.Null(map.NameOf(null));
    }

    [Fact]
    public void Detects_unknown_bucket_only_in_ticket_plan()
    {
        var map = new TicketBuckets("plan", Buckets);
        var known = new PlannerTask { Id = "1", PlanId = "plan", Title = "[#1] a", BucketId = "b-new" };
        var newBucket = new PlannerTask { Id = "2", PlanId = "plan", Title = "[#2] b", BucketId = "b-uj" };
        var otherPlan = new PlannerTask { Id = "3", PlanId = "mas", Title = "c", BucketId = "x" };
        Assert.False(map.HasUnknownBucket([known, otherPlan]));
        Assert.True(map.HasUnknownBucket([known, newBucket]));
    }

    [Theory]
    [InlineData("b-sos", "SOS")]
    [InlineData("", "New")]            // nincs gyűjtőben
    [InlineData(null, "New")]
    [InlineData("b-ismeretlen", "New")]  // újratöltés után is ismeretlen
    public void Status_of_bucket_defaults_to_new(string? bucketId, string expected) =>
        Assert.Equal(expected, new TicketBuckets("plan", Buckets).StatusOf(bucketId));

    [Fact]
    public void Empty_bucket_does_not_trigger_reload_but_unknown_does()
    {
        var map = new TicketBuckets("plan", Buckets);
        Assert.False(map.HasUnknownBucket([new PlannerTask { Id = "1", PlanId = "plan", Title = "[#1] a", BucketId = "" }]));
        Assert.True(map.HasUnknownBucket([new PlannerTask { Id = "2", PlanId = "plan", Title = "[#2] b", BucketId = "b-uj" }]));
    }

    [Fact]
    public void Duplicate_names_take_the_first()
    {
        var map = new TicketBuckets("plan", [new("a", "New", "1"), new("b", "New", "2")]);
        Assert.Equal("a", map.IdOf("New"));
    }
}

public class BucketPatchTests
{
    private sealed class FakeTokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("token");
    }

    private sealed class Scripted(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _i;
        public List<(HttpMethod Method, string? IfMatch, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.Headers.TryGetValues("If-Match", out var v) ? v.First() : null, body));
            return responses[_i++](request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static readonly PlannerTask Ticket101 = new()
    {
        Id = "t1", PlanId = "plan", Title = "[#101] Ügy", BucketId = "b-new", ETag = "W/\"1\"",
    };

    [Fact]
    public async Task Move_sends_only_bucket_with_if_match()
    {
        var handler = new Scripted(_ => Json("""{"id":"t1","planId":"plan","title":"[#101] Ügy","bucketId":"b-proc","@odata.etag":"W/\"2\""}"""));
        var updated = await new PlannerClient(new HttpClient(handler), new FakeTokens())
            .UpdateTaskAsync(Ticket101, new TaskPatch { BucketId = "b-proc" });

        var (method, ifMatch, body) = handler.Requests.Single();
        Assert.Equal(HttpMethod.Patch, method);
        Assert.Equal("W/\"1\"", ifMatch);
        var json = JsonDocument.Parse(body!).RootElement;
        Assert.Equal("b-proc", json.GetProperty("bucketId").GetString());
        Assert.Single(json.EnumerateObject());
        Assert.Equal("b-proc", updated.BucketId);
        Assert.Equal("W/\"2\"", updated.ETag);
    }

    [Fact]
    public async Task Move_retries_once_on_412_with_reloaded_etag()
    {
        var handler = new Scripted(
            _ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed),
            _ => Json("""{"id":"t1","planId":"plan","title":"[#101] Ügy","bucketId":"b-new","@odata.etag":"W/\"friss\""}"""),
            _ => Json("""{"id":"t1","planId":"plan","title":"[#101] Ügy","bucketId":"b-proc","@odata.etag":"W/\"3\""}"""));

        var updated = await new PlannerClient(new HttpClient(handler), new FakeTokens())
            .UpdateTaskAsync(Ticket101, new TaskPatch { BucketId = "b-proc" });

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);          // a feladat újratöltése
        Assert.Equal("W/\"friss\"", handler.Requests[2].IfMatch);           // újrapróba a friss ETaggel
        Assert.Contains("b-proc", handler.Requests[2].Body);
        Assert.Equal("b-proc", updated.BucketId);
    }

    [Fact]
    public async Task Start_sends_bucket_and_50_percent_in_one_patch()
    {
        var handler = new Scripted(_ => Json("""{"id":"t1","planId":"plan","title":"[#101] Ügy","bucketId":"b-prog","percentComplete":50,"@odata.etag":"W/\"2\""}"""));
        var updated = await new PlannerClient(new HttpClient(handler), new FakeTokens())
            .UpdateTaskAsync(Ticket101, new TaskPatch { BucketId = "b-prog", PercentComplete = 50 });

        var body = JsonDocument.Parse(handler.Requests.Single().Body!).RootElement;
        Assert.Equal("b-prog", body.GetProperty("bucketId").GetString());
        Assert.Equal(50, body.GetProperty("percentComplete").GetInt32());
        Assert.True(updated.IsInProgress);
    }

    [Fact]
    public async Task Finish_sends_done_bucket_and_100_in_one_patch_with_412_retry()
    {
        var handler = new Scripted(
            _ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed),
            _ => Json("""{"id":"t1","planId":"plan","title":"[#101] Ügy","bucketId":"b-prog","percentComplete":50,"@odata.etag":"W/\"friss\""}"""),
            _ => Json("""{"id":"t1","planId":"plan","title":"[#101] Ügy","bucketId":"b-done","percentComplete":100,"@odata.etag":"W/\"4\""}"""));

        var updated = await new PlannerClient(new HttpClient(handler), new FakeTokens())
            .UpdateTaskAsync(Ticket101, new TaskPatch { BucketId = "b-done", PercentComplete = 100 });

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Patch));   // egy PATCH + egy újrapróba
        var retry = JsonDocument.Parse(handler.Requests[2].Body!).RootElement;
        Assert.Equal("b-done", retry.GetProperty("bucketId").GetString());
        Assert.Equal(100, retry.GetProperty("percentComplete").GetInt32());
        Assert.Equal("W/\"friss\"", handler.Requests[2].IfMatch);
        Assert.True(updated.IsCompleted);
        Assert.Equal("b-done", updated.BucketId);
    }

    [Fact]
    public async Task Second_412_is_an_error()
    {
        var handler = new Scripted(
            _ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed),
            _ => Json("""{"id":"t1","planId":"plan","title":"x","@odata.etag":"W/\"friss\""}"""),
            _ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed));

        var ex = await Assert.ThrowsAsync<GraphApiException>(() => new PlannerClient(new HttpClient(handler), new FakeTokens())
            .UpdateTaskAsync(Ticket101, new TaskPatch { BucketId = "b-proc" }));
        Assert.True(ex.IsConflict);
        Assert.Equal(3, handler.Requests.Count);
    }
}

public sealed class TicketReopenTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "pw-reopen-" + Guid.NewGuid().ToString("N") + ".json");
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 9, 0, 0, TimeSpan.FromHours(2));

    public void Dispose() => File.Delete(_file);

    private static PlannerTask Ticket(int percent, string bucket) => new()
    {
        Id = "t100", PlanId = "plan", Title = "[#100] Jelszó", BucketId = bucket, PercentComplete = percent,
        CompletedDateTime = percent == 100 ? T0 : null,
    };

    [Fact]
    public void Done_ticket_reopened_by_system_is_reported_once()
    {
        var tracker = new TicketTracker(_file);
        tracker.Track([Ticket(100, "b-done")], "plan", T0);                       // alapállapot: kész
        Assert.Empty(tracker.Track([Ticket(100, "b-done")], "plan", T0.AddMinutes(5)).Reopened);

        var changes = tracker.Track([Ticket(0, "b-rev")], "plan", T0.AddMinutes(10)); // külsős válasz → Revisit, 0%
        Assert.Equal(["t100"], changes.Reopened.Select(t => t.Id));
        Assert.Empty(changes.NewTickets);                                             // nem „új ügy”

        Assert.Empty(tracker.Track([Ticket(0, "b-rev")], "plan", T0.AddMinutes(15)).Reopened); // csak egyszer
    }

    [Fact]
    public void Reopen_is_detected_after_restart()
    {
        new TicketTracker(_file).Track([Ticket(100, "b-done")], "plan", T0);
        Assert.Single(new TicketTracker(_file).Track([Ticket(0, "b-rev")], "plan", T0.AddHours(2)).Reopened);
    }

    [Fact]
    public void Reopened_task_is_shown_as_active_even_with_saved_manual_order()
    {
        // Az újranyitott feladatot semmilyen helyi sorrend / „kész” lista nem rejtheti el.
        var today = new DateOnly(2026, 10, 7);
        var other = new PlannerTask { Id = "a", PlanId = "p", Title = "Más" };
        var reopened = Ticket(0, "b-rev");
        var active = TaskOrdering.SortActive([other, reopened], today);
        var ordered = PlannerWidget.Core.Tasks.ManualOrder.Apply(active, ["a"], today);
        Assert.Contains(ordered, t => t.Id == "t100");
        Assert.DoesNotContain(TaskOrdering.SortCompleted([other, reopened]), t => t.Id == "t100");
    }
}

public sealed class TicketTrackerBucketTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "pw-bucket-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void Bucket_change_is_not_a_new_ticket()
    {
        var tracker = new TicketTracker(_file);
        var t0 = DateTimeOffset.Now;
        var task = new PlannerTask { Id = "t1", PlanId = "plan", Title = "[#101] Ügy", BucketId = "b-new" };
        tracker.Update([task], "plan", t0);
        Assert.Empty(tracker.Update([task with { BucketId = "b-proc", ETag = "W/\"2\"" }], "plan", t0.AddMinutes(1)));
        Assert.Empty(tracker.Update([task with { BucketId = "b-prog", ETag = "W/\"3\"" }], "plan", t0.AddMinutes(2)));
    }
}

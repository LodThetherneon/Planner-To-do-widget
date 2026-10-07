using System.Net;
using System.Text;
using System.Text.Json;
using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Sync;

namespace PlannerWidget.Core.Tests;

public class SyncErrorParsingTests
{
    private static TodoTask T(string id, string title, string body = "", DateTimeOffset? created = null, string status = "notStarted") =>
        new(id, title, status, created ?? DateTimeOffset.Now, created ?? DateTimeOffset.Now, body);

    [Theory]
    [InlineData("HIBA | Ügyek → Planner", "Ügyek → Planner")]
    [InlineData("HIBA|Planner → Ügyek", "Planner → Ügyek")]
    [InlineData("  HIBA |   E-mail archiválás  ", "E-mail archiválás")]
    [InlineData("HIBA | ", "(ismeretlen folyamat)")]
    public void Error_title_gives_process_name(string title, string expected) =>
        Assert.Equal(expected, SyncErrors.TryParse(T("1", title))!.ProcessName);

    [Theory]
    [InlineData("Szinkron életjel")]   // az életjel sosem hiba
    [InlineData("szinkron ÉLETJEL")]
    [InlineData("Valami más feladat")]
    [InlineData("Hiba nélkül")]
    public void Non_error_titles_are_ignored(string title) => Assert.Null(SyncErrors.TryParse(T("1", title)));

    [Fact]
    public void Completed_error_is_ignored() => Assert.Null(SyncErrors.TryParse(T("1", "HIBA | X", status: "completed")));

    [Fact]
    public void Link_is_extracted_from_html_and_decoded()
    {
        const string html = """
            <div><p>A folyamat hibára futott.</p>
            <a href="https://make.powerautomate.com/environments/Default-1/flows/abc/runs/08584?v3=true&amp;source=email">Futás megnyitása</a></div>
            """;
        Assert.Equal("https://make.powerautomate.com/environments/Default-1/flows/abc/runs/08584?v3=true&source=email",
            SyncErrors.ExtractLink(html)!.AbsoluteUri);
    }

    [Fact]
    public void Plain_text_link_is_extracted_without_trailing_punctuation() =>
        Assert.Equal("https://make.powerautomate.com/environments/e/flows/f/runs/r",
            SyncErrors.ExtractLink("Részletek: https://make.powerautomate.com/environments/e/flows/f/runs/r.")!.AbsoluteUri);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("<p>nincs link</p>")]
    [InlineData("<a href=\"https://example.com/x\">más oldal</a>")]
    [InlineData("http://make.powerautomate.com/nem-https")]
    public void No_link(string? html) => Assert.Null(SyncErrors.ExtractLink(html));

    [Fact]
    public void Same_titles_are_grouped_with_count_latest_first()
    {
        var t0 = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.FromHours(2));
        var groups = SyncErrors.Group(new[]
        {
            SyncErrors.TryParse(T("a", "HIBA | Ügyek → Planner", created: t0))!,
            SyncErrors.TryParse(T("b", "HIBA | Planner → Ügyek", created: t0.AddMinutes(5)))!,
            SyncErrors.TryParse(T("c", "HIBA | Ügyek → Planner", created: t0.AddMinutes(30),
                body: "https://make.powerautomate.com/environments/e/flows/f/runs/latest"))!,
        });

        Assert.Equal(2, groups.Count);
        Assert.Equal("Ügyek → Planner", groups[0].ProcessName);   // a legutóbbi hiba ebben a csoportban van
        Assert.Equal(2, groups[0].Count);
        Assert.Equal(["c", "a"], groups[0].Entries.Select(e => e.TaskId));
        Assert.Equal(t0.AddMinutes(30), groups[0].Latest);
        Assert.Equal("https://make.powerautomate.com/environments/e/flows/f/runs/latest", groups[0].Link!.AbsoluteUri);
        Assert.Equal(1, groups[1].Count);
        Assert.Null(groups[1].Link);
    }

    [Fact]
    public void Evaluate_excludes_heartbeat_and_counts_errors()
    {
        var tz = SyncErrors.BudapestTimeZone;
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.FromHours(2));
        var status = SyncErrors.Evaluate(new[]
        {
            T("hb", "Szinkron életjel", created: now.AddMinutes(-3)),
            T("1", "HIBA | A"), T("2", "HIBA | A"), T("3", "HIBA | B"), T("x", "Egyéb"),
        }, now, tz);

        Assert.True(status.ListFound);
        Assert.Equal(3, status.ErrorCount);
        Assert.Equal(2, status.Groups.Count);
        Assert.DoesNotContain(status.Groups.SelectMany(g => g.Entries), e => e.TaskId == "hb");
        Assert.True(status.Heartbeat.Found);
        Assert.False(status.Heartbeat.IsStale);
    }
}

public class HeartbeatTests
{
    private static readonly TimeZoneInfo Tz = SyncErrors.BudapestTimeZone;

    /// <summary>Budapesti helyi idő 2026-10-07-én (nyári idő, UTC+2).</summary>
    private static DateTimeOffset At(int hour, int minute, int dayOffset = 0)
    {
        var local = new DateTime(2026, 10, 7, hour, minute, 0, DateTimeKind.Unspecified).AddDays(dayOffset);
        return new DateTimeOffset(local, Tz.GetUtcOffset(local));
    }

    [Theory]
    [InlineData(5, 59)]    // éjszaka
    [InlineData(6, 0)]     // a folyamat csak most indul
    [InlineData(6, 19)]    // 6:20 előtt még nem riasztunk
    [InlineData(21, 0)]    // 21:00-tól már nem
    [InlineData(21, 30)]
    [InlineData(23, 59)]
    public void No_alarm_outside_window_even_if_old(int hour, int minute)
    {
        var state = SyncErrors.EvaluateHeartbeat(At(20, 55, dayOffset: -1), true, At(hour, minute), Tz);
        Assert.False(state.IsStale);
        Assert.Null(state.Message);
    }

    [Fact]
    public void Alarm_at_620_when_morning_run_did_not_start()
    {
        var state = SyncErrors.EvaluateHeartbeat(At(20, 55, dayOffset: -1), true, At(6, 21), Tz);
        Assert.True(state.IsStale);
        Assert.Equal("A szinkron nem fut (utolsó: 10.06. 20:55) – ellenőrizd a Power Automate-et", state.Message);
    }

    [Theory]
    [InlineData(11, 40, false)]   // 20 perc: még rendben
    [InlineData(11, 39, true)]    // 21 perc: riasztás
    [InlineData(11, 58, false)]
    public void Twenty_minute_threshold_in_window(int lastHour, int lastMinute, bool stale)
    {
        var state = SyncErrors.EvaluateHeartbeat(At(lastHour, lastMinute), true, At(12, 0), Tz);
        Assert.Equal(stale, state.IsStale);
        if (stale)
        {
            Assert.Equal($"A szinkron nem fut (utolsó: {lastHour:00}:{lastMinute:00}) – ellenőrizd a Power Automate-et", state.Message);
        }
    }

    [Fact]
    public void Alarm_just_before_2100()
    {
        Assert.True(SyncErrors.EvaluateHeartbeat(At(20, 30), true, At(20, 59), Tz).IsStale);
        Assert.False(SyncErrors.EvaluateHeartbeat(At(20, 30), true, At(21, 1), Tz).IsStale);
    }

    [Fact]
    public void Missing_heartbeat_is_no_alarm()
    {
        var state = SyncErrors.EvaluateHeartbeat(null, false, At(12, 0), Tz);
        Assert.False(state.Found);
        Assert.False(state.IsStale);
    }

    [Fact]
    public void Utc_input_is_converted_to_budapest_time()
    {
        // 10:00 UTC = 12:00 Budapest (nyári idő) → ablakon belül; 4:10 UTC = 6:10 → még nem.
        var last = new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
        Assert.True(SyncErrors.EvaluateHeartbeat(last, true, new DateTimeOffset(2026, 10, 7, 10, 0, 0, TimeSpan.Zero), Tz).IsStale);
        Assert.False(SyncErrors.EvaluateHeartbeat(last.AddDays(-1), true, new DateTimeOffset(2026, 10, 7, 4, 10, 0, TimeSpan.Zero), Tz).IsStale);
    }
}

public class SyncErrorServiceTests
{
    private sealed class FakeTodo : ITodoClient
    {
        public List<TodoList> Lists { get; } = [new("l-egyeb", "Teendők"), new("l-sync", "Szinkronhibák")];
        public Dictionary<string, List<TodoTask>> Tasks { get; } = new();
        public int ListCalls { get; private set; }
        public List<(string List, string Task)> Completed { get; } = [];
        public bool NotFoundOnce { get; set; }

        public Task<IReadOnlyList<TodoList>> GetTodoListsAsync(CancellationToken ct = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<TodoList>>(Lists.ToList());
        }

        public Task<IReadOnlyList<TodoTask>> GetOpenTodoTasksAsync(string listId, CancellationToken ct = default)
        {
            if (NotFoundOnce)
            {
                NotFoundOnce = false;
                throw new GraphApiException(HttpStatusCode.NotFound, "nincs");
            }

            return Task.FromResult<IReadOnlyList<TodoTask>>(Tasks.GetValueOrDefault(listId) ?? []);
        }

        public Task CompleteTodoTaskAsync(string listId, string taskId, CancellationToken ct = default)
        {
            Completed.Add((listId, taskId));
            return Task.CompletedTask;
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.FromHours(2));

    private static TodoTask T(string id, string title, DateTimeOffset? modified = null) => new(id, title, "notStarted", Now, modified ?? Now, "");

    [Fact]
    public async Task Finds_list_by_name_and_caches_id()
    {
        var fake = new FakeTodo();
        fake.Tasks["l-sync"] = [T("1", "HIBA | A"), T("hb", "Szinkron életjel")];
        var service = new SyncErrorService(fake, () => Now, SyncErrors.BudapestTimeZone);

        var s1 = await service.LoadAsync();
        var s2 = await service.LoadAsync();

        Assert.Equal(1, s1.ErrorCount);
        Assert.Equal(1, s2.ErrorCount);
        Assert.Equal(1, fake.ListCalls);
    }

    [Fact]
    public async Task List_404_triggers_new_lookup()
    {
        var fake = new FakeTodo();
        fake.Tasks["l-sync"] = [T("1", "HIBA | A")];
        var service = new SyncErrorService(fake, () => Now, SyncErrors.BudapestTimeZone);
        await service.LoadAsync();

        fake.NotFoundOnce = true;
        var status = await service.LoadAsync();
        Assert.Equal(1, status.ErrorCount);
        Assert.Equal(2, fake.ListCalls);
    }

    [Fact]
    public async Task Missing_list_means_not_configured()
    {
        var fake = new FakeTodo();
        fake.Lists.RemoveAll(l => l.DisplayName == "Szinkronhibák");
        var status = await new SyncErrorService(fake, () => Now).LoadAsync();
        Assert.False(status.ListFound);
        Assert.Equal(0, status.ErrorCount);
    }

    [Fact]
    public async Task Resolve_completes_entries_but_never_the_heartbeat()
    {
        var fake = new FakeTodo();
        fake.Tasks["l-sync"] = [T("1", "HIBA | A"), T("2", "HIBA | A")];
        var service = new SyncErrorService(fake, () => Now);
        var status = await service.LoadAsync();

        await service.ResolveAsync(status.Groups[0].Entries.Append(new SyncErrorEntry("hb", "Szinkron életjel", "", Now, null)));

        Assert.Equal([("l-sync", "1"), ("l-sync", "2")], fake.Completed.OrderBy(c => c.Task).ToList());
    }
}

public class TodoClientTests
{
    private sealed class FakeTokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("token");
    }

    private sealed class Scripted(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _i;
        public List<(HttpMethod Method, string Url, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.AbsoluteUri, body)); // nyers (kódolt) alak
            return responses[_i++](request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Resolve_sends_patch_status_completed()
    {
        var handler = new Scripted(_ => Json("""{"id":"t1","status":"completed"}"""));
        await new PlannerClient(new HttpClient(handler), new FakeTokens()).CompleteTodoTaskAsync("lista 1", "t1");

        var (method, url, body) = handler.Requests.Single();
        Assert.Equal(HttpMethod.Patch, method);
        Assert.EndsWith("me/todo/lists/lista%201/tasks/t1", url);
        Assert.Equal("completed", JsonDocument.Parse(body!).RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Open_tasks_use_filter_and_still_filter_client_side()
    {
        var handler = new Scripted(_ => Json("""
            {"value":[
              {"id":"1","title":"HIBA | A","status":"notStarted","createdDateTime":"2026-10-07T08:00:00Z","body":{"content":"<p>x</p>","contentType":"html"}},
              {"id":"2","title":"HIBA | B","status":"completed"}
            ]}
            """));
        var tasks = await new PlannerClient(new HttpClient(handler), new FakeTokens()).GetOpenTodoTasksAsync("l");

        Assert.Contains("$filter=status%20ne%20%27completed%27", handler.Requests[0].Url.Replace("'", "%27"));
        Assert.Equal(["1"], tasks.Select(t => t.Id));
        Assert.Equal("<p>x</p>", tasks[0].BodyContent);
    }

    [Fact]
    public async Task Unsupported_filter_falls_back_to_client_side()
    {
        var handler = new Scripted(
            _ => Json("""{"error":{"code":"BadRequest","message":"filter"}}""", HttpStatusCode.BadRequest),
            _ => Json("""{"value":[{"id":"1","title":"HIBA | A","status":"inProgress"},{"id":"2","title":"x","status":"completed"}]}"""));
        var tasks = await new PlannerClient(new HttpClient(handler), new FakeTokens()).GetOpenTodoTasksAsync("l");

        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain("filter", handler.Requests[1].Url);
        Assert.Equal(["1"], tasks.Select(t => t.Id));
    }
}

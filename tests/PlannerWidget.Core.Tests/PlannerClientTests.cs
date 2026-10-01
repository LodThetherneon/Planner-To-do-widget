using System.Net;
using System.Text;
using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Models;

namespace PlannerWidget.Core.Tests;

public class PlannerClientTests
{
    private sealed class FakeTokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("token");
    }

    /// <summary>Előre megadott válaszokat ad vissza, és rögzíti a kéréseket.</summary>
    private sealed class ScriptedHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _index;
        public List<(HttpMethod Method, string Url, string? IfMatch, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            request.Headers.TryGetValues("If-Match", out var ifMatch);
            Requests.Add((request.Method, request.RequestUri!.ToString(), ifMatch?.FirstOrDefault(), body));
            return responses[_index++](request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static PlannerClient Client(ScriptedHandler handler) => new(new HttpClient(handler), new FakeTokens());

    [Fact]
    public async Task GetMyTasks_follows_next_link_pages()
    {
        var handler = new ScriptedHandler(
            _ => Json("""{"value":[{"id":"t1","planId":"p","title":"Egy","percentComplete":0,"@odata.etag":"W/\"1\""}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/planner/tasks?$skiptoken=abc"}"""),
            _ => Json("""{"value":[{"id":"t2","planId":"p","title":"Kettő","percentComplete":100,"priority":1,"dueDateTime":"2026-03-05T12:00:00Z"}]}"""));

        var tasks = await Client(handler).GetMyTasksAsync();

        Assert.Equal(["t1", "t2"], tasks.Select(t => t.Id));
        Assert.Equal("W/\"1\"", tasks[0].ETag);
        Assert.True(tasks[1].IsCompleted);
        Assert.Equal(TaskPriority.Urgent, tasks[1].PriorityLevel);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("skiptoken", handler.Requests[1].Url);
    }

    [Fact]
    public async Task Update_retries_once_with_fresh_etag_on_412()
    {
        var handler = new ScriptedHandler(
            _ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed),
            _ => Json("""{"id":"t1","planId":"p","title":"Régi","@odata.etag":"W/\"friss\""}"""),
            _ => Json("""{"id":"t1","planId":"p","title":"Új","percentComplete":100,"@odata.etag":"W/\"3\""}"""));
        var task = new PlannerTask { Id = "t1", PlanId = "p", Title = "Régi", ETag = "W/\"elavult\"" };

        var updated = await Client(handler).UpdateTaskAsync(task, new TaskPatch { PercentComplete = 100 });

        Assert.Equal("W/\"elavult\"", handler.Requests[0].IfMatch);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("W/\"friss\"", handler.Requests[2].IfMatch);
        Assert.True(updated.IsCompleted);
        Assert.Equal("W/\"3\"", updated.ETag);
    }

    [Fact]
    public async Task Patch_can_clear_due_date()
    {
        var handler = new ScriptedHandler(_ => Json("""{"id":"t1","planId":"p","title":"x"}"""));
        var task = new PlannerTask { Id = "t1", PlanId = "p", Title = "x", ETag = "e" };

        await Client(handler).UpdateTaskAsync(task, new TaskPatch { ChangeDueDate = true, DueDate = null });

        Assert.Contains("\"dueDateTime\":null", handler.Requests[0].Body);
    }

    [Fact]
    public async Task Transient_errors_are_retried()
    {
        var handler = new ScriptedHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => Json("""{"value":[]}"""));

        var plans = await Client(handler).GetMyPlansAsync();

        Assert.Empty(plans);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Delete_of_missing_task_is_success()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await Client(handler).DeleteTaskAsync(new PlannerTask { Id = "t", PlanId = "p", Title = "x", ETag = "e" });
    }

    [Fact]
    public async Task Errors_are_translated()
    {
        var handler = new ScriptedHandler(_ => Json("""{"error":{"code":"Forbidden","message":"nope"}}""", HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<GraphApiException>(() => Client(handler).GetMyTasksAsync());
        Assert.True(ex.IsForbidden);
        Assert.Equal("Forbidden", ex.ErrorCode);
    }

    [Fact]
    public async Task Create_sends_assignment_and_priority()
    {
        var handler = new ScriptedHandler(_ => Json("""{"id":"new","planId":"p","title":"Új feladat"}""", HttpStatusCode.Created));

        var created = await Client(handler).CreateTaskAsync(
            new NewTaskRequest("p", "b", "Új feladat", "user-1", new DateOnly(2026, 10, 2), TaskPriority.Important));

        Assert.Equal("new", created.Id);
        var body = handler.Requests[0].Body!;
        Assert.Contains("\"user-1\"", body);
        Assert.Contains("#microsoft.graph.plannerAssignment", body);
        Assert.Contains("\"priority\":3", body);
        Assert.Contains("\"dueDateTime\":\"2026-10-02T", body);
    }

    [Fact]
    public async Task Checklist_is_parsed_and_sorted()
    {
        var handler = new ScriptedHandler(_ => Json("""
            {"id":"t1","@odata.etag":"d1","description":"Leírás",
             "checklist":{"b":{"title":"Második","isChecked":true,"orderHint":"8585"},
                          "a":{"title":"Első","isChecked":false,"orderHint":"8584"}}}
            """));

        var details = await Client(handler).GetTaskDetailsAsync("t1");

        Assert.Equal("Leírás", details.Description);
        Assert.Equal(["Első", "Második"], details.Checklist.Select(c => c.Title));
        Assert.True(details.Checklist[1].IsChecked);
    }
}

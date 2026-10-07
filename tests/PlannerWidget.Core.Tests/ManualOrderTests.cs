using System.Net;
using System.Text;
using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Models;
using PlannerWidget.Core.Tasks;

namespace PlannerWidget.Core.Tests;

public class ManualOrderTests
{
    private static readonly DateOnly Today = new(2026, 10, 5);

    /// <summary>„L…” = lejárt, „M…” = mai, „K…” = később, „N…” = határidő nélküli.</summary>
    private static PlannerTask T(string id)
    {
        int? offset = id[0] switch
        {
            'L' => -2,
            'M' => 0,
            'K' => 10,
            _ => null,
        };
        return new PlannerTask { Id = id, PlanId = "p", Title = id, DueDate = offset is { } o ? Today.AddDays(o) : null };
    }

    private static List<PlannerTask> List(params string[] ids) => ids.Select(T).ToList();

    private static IEnumerable<string> Ids(IEnumerable<PlannerTask> tasks) => tasks.Select(t => t.Id);

    [Fact]
    public void Empty_saved_order_keeps_automatic_order()
    {
        var sorted = List("L1", "M1", "N1");
        Assert.Same(sorted, ManualOrder.Apply(sorted, [], Today));
    }

    [Fact]
    public void Saved_order_is_applied()
    {
        var sorted = List("L1", "M1", "K1", "N1");
        Assert.Equal(["M1", "L1", "N1", "K1"], Ids(ManualOrder.Apply(sorted, ["M1", "L1", "N1", "K1"], Today)));
    }

    [Fact]
    public void New_tasks_go_to_their_automatic_place_and_missing_ones_are_dropped()
    {
        // Mentve: K1, M1 (és egy azóta kész/törölt „X”). Új: L1 – a határidő szerint utána következő M1 elé;
        // N1 – a végére (nincs utána senki).
        var sorted = List("L1", "M1", "K1", "N1");
        Assert.Equal(["K1", "L1", "M1", "N1"], Ids(ManualOrder.Apply(sorted, ["K1", "X", "M1"], Today)));
    }

    [Fact]
    public void Consecutive_new_tasks_keep_their_relative_order()
    {
        var sorted = List("L1", "L2", "M1", "N1");
        Assert.Equal(["L1", "L2", "N1", "M1"], Ids(ManualOrder.Apply(sorted, ["N1", "M1"], Today)));
    }

    [Fact]
    public void Task_that_became_overdue_below_no_due_moves_up()
    {
        // A mentett sorrendben „L1” a határidő nélküli alatt van (pl. akkor még nem volt lejárt).
        var sorted = List("L1", "M1", "N1", "N2");
        Assert.Equal(["M1", "L1", "N1", "N2"], Ids(ManualOrder.Apply(sorted, ["M1", "N1", "N2", "L1"], Today)));
    }

    [Fact]
    public void Dragging_overdue_below_no_due_is_clamped()
    {
        var full = List("L1", "M1", "N1", "N2");
        var (order, clamped) = ManualOrder.Reorder(full, ["M1", "N1", "N2", "L1"], "L1", Today);
        Assert.True(clamped);
        Assert.Equal(["M1", "L1", "N1", "N2"], Ids(order));
    }

    [Fact]
    public void Dragging_no_due_above_overdue_is_clamped()
    {
        var full = List("L1", "L2", "M1", "N1");
        var (order, clamped) = ManualOrder.Reorder(full, ["N1", "L1", "L2", "M1"], "N1", Today);
        Assert.True(clamped);
        Assert.Equal(["L1", "L2", "N1", "M1"], Ids(order));
    }

    [Fact]
    public void Allowed_moves_are_kept()
    {
        var full = List("L1", "L2", "M1", "K1", "N1");
        var (order, clamped) = ManualOrder.Reorder(full, ["L2", "L1", "N1", "M1", "K1"], "N1", Today);
        Assert.False(clamped);
        Assert.Equal(["L2", "L1", "N1", "M1", "K1"], Ids(order));
    }

    [Fact]
    public void Reorder_of_filtered_subset_keeps_hidden_tasks_in_place()
    {
        // Látható: M1, K1, K2 (szűrés); a rejtett L1 és N1 helye nem változik.
        var full = List("L1", "M1", "K1", "N1", "K2");
        var (order, clamped) = ManualOrder.Reorder(full, ["K2", "M1", "K1"], "K2", Today);
        Assert.False(clamped);
        Assert.Equal(["L1", "K2", "M1", "N1", "K1"], Ids(order));
    }

    [Fact]
    public void Store_saves_clears_and_reloads()
    {
        var path = Path.Combine(Path.GetTempPath(), "pw-order-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ManualOrderStore(path);
            Assert.False(store.HasOrder);
            store.Save(["b", "a"]);
            Assert.Equal(["b", "a"], new ManualOrderStore(path).Order);
            store.Clear();
            Assert.False(new ManualOrderStore(path).HasOrder);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class ChecklistTitleTests
{
    private sealed class FakeTokens : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("token");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? IfMatch { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            IfMatch = request.Headers.TryGetValues("If-Match", out var v) ? v.First() : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"@odata.etag":"W/\"2\"","description":"","checklist":{"c1":{"title":"Új szöveg","isChecked":false,"orderHint":"1"}}}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    private static readonly PlannerTaskDetails Details =
        new("t1", "W/\"1\"", "", [new ChecklistItem("c1", "Régi", false, "1")]);

    [Fact]
    public async Task Title_patch_sends_only_the_title_of_that_item()
    {
        var handler = new RecordingHandler();
        var client = new PlannerClient(new HttpClient(handler), new FakeTokens());

        var updated = await client.SetChecklistItemTitleAsync(Details, "c1", "  Új szöveg  ");

        Assert.Equal("W/\"1\"", handler.IfMatch);
        var item = System.Text.Json.JsonDocument.Parse(handler.Body!).RootElement.GetProperty("checklist").GetProperty("c1");
        Assert.Equal("Új szöveg", item.GetProperty("title").GetString());
        Assert.False(item.TryGetProperty("isChecked", out _));
        Assert.Equal("Új szöveg", updated.Checklist[0].Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_title_is_rejected(string title)
    {
        var client = new PlannerClient(new HttpClient(new RecordingHandler()), new FakeTokens());
        await Assert.ThrowsAsync<ArgumentException>(() => client.SetChecklistItemTitleAsync(Details, "c1", title));
    }

    [Fact]
    public async Task Too_long_title_is_rejected()
    {
        var client = new PlannerClient(new HttpClient(new RecordingHandler()), new FakeTokens());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.SetChecklistItemTitleAsync(Details, "c1", new string('x', ChecklistItem.MaxTitleLength + 1)));
        await client.SetChecklistItemTitleAsync(Details, "c1", new string('x', ChecklistItem.MaxTitleLength));
    }
}

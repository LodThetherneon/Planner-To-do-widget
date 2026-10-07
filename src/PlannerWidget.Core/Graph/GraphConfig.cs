namespace PlannerWidget.Core.Graph;

/// <summary>
/// Az Entra (Azure AD) alkalmazásregisztráció adatai. Public client, ezért itt nincs titok:
/// a client ID és a tenant ID nyilvános azonosítók.
/// </summary>
public static class GraphConfig
{
    public const string ClientId = "34e2c374-eb9b-4a30-9f19-879117b91660";
    public const string TenantId = "ba3bfa82-3437-46e6-815c-d987814eeaee";

    public static readonly string[] Scopes = ["User.Read", "Tasks.ReadWrite"];

    public const string BaseUrl = "https://graph.microsoft.com/v1.0/";

    /// <summary>Feladat megnyitása a Planner webes felületén (a régi tasks.office.com link átirányít az újra).</summary>
    public static Uri TaskWebUri(string taskId) =>
        new($"https://tasks.office.com/{TenantId}/Home/Task/{Uri.EscapeDataString(taskId)}");

    public static Uri PlannerHomeUri { get; } = new("https://planner.cloud.microsoft/");
}

using System.Net;

namespace PlannerWidget.Core.Graph;

/// <summary>Nincs érvényes bejelentkezés (interaktív bejelentkezés szükséges).</summary>
public sealed class NotSignedInException(string message = "Nincs bejelentkezve.") : Exception(message);

/// <summary>A Graph API hibát adott vissza.</summary>
public sealed class GraphApiException(HttpStatusCode statusCode, string message, string? errorCode = null)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string? ErrorCode { get; } = errorCode;

    public bool IsConflict => StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict;
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;
}

/// <summary>Hálózati hiba vagy időtúllépés (offline állapot).</summary>
public sealed class GraphNetworkException(string message, Exception inner) : Exception(message, inner);

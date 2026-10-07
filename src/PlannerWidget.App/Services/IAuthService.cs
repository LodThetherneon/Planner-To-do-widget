using PlannerWidget.Core.Graph;

namespace PlannerWidget.App.Services;

public interface IAuthService : IAccessTokenProvider
{
    string? AccountName { get; }

    Task InitializeAsync();

    Task<bool> TrySignInSilentlyAsync(CancellationToken ct = default);

    Task SignInInteractiveAsync(CancellationToken ct);

    Task SignOutAsync();
}

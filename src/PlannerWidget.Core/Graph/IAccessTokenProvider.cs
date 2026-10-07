namespace PlannerWidget.Core.Graph;

public interface IAccessTokenProvider
{
    /// <summary>
    /// Érvényes access tokent ad vissza (csendes megújítással).
    /// <see cref="NotSignedInException"/>-t dob, ha interaktív bejelentkezés kell.
    /// </summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

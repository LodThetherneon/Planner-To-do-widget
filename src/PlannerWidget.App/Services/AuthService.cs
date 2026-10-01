using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using Microsoft.Identity.Client.Extensions.Msal;
using PlannerWidget.Core.Graph;
using PlannerWidget.Core.Logging;
using PlannerWidget.Core.Settings;
using PlannerWidget.Core.Storage;

namespace PlannerWidget.App.Services;

/// <summary>
/// Bejelentkezés MSAL-lal, két úton:
///  1. Windows-fiók (WAM broker): egyszeri bejelentkezés a Windowsba belépett munkahelyi fiókkal, jelszó nélkül.
///     Ehhez az Entra alkalmazásregisztrációban szerepelnie kell a
///     ms-appx-web://microsoft.aad.brokerplugin/{client-id} átirányítási címnek.
///  2. Böngésző (http://localhost): ez működik a régi Python-verzióval azonos regisztrációval is.
/// Ha a WAM nincs engedélyezve a regisztrációban, automatikusan és véglegesen a böngészős módra vált.
/// A token-gyorsítótár DPAPI-val titkosítva a %LOCALAPPDATA%\PlannerWidget mappában van.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly SettingsStore _settings;
    private readonly Func<nint> _parentWindow;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private IPublicClientApplication? _brokerApp;
    private IPublicClientApplication? _browserApp;
    private bool _initialized;

    public AuthService(SettingsStore settings, Func<nint> parentWindow)
    {
        _settings = settings;
        _parentWindow = parentWindow;
    }

    /// <summary>A bejelentkezett fiók felhasználóneve (UPN), ha ismert.</summary>
    public string? AccountName { get; private set; }

    public bool IsBrokerEnabled => _settings.Current.AuthMode == AuthMode.Automatic;

    public async Task InitializeAsync()
    {
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var authority = $"https://login.microsoftonline.com/{GraphConfig.TenantId}";

            _browserApp = PublicClientApplicationBuilder.Create(GraphConfig.ClientId)
                .WithAuthority(authority)
                .WithRedirectUri("http://localhost")
                .WithClientName("Planner Widget")
                .Build();

            _brokerApp = PublicClientApplicationBuilder.Create(GraphConfig.ClientId)
                .WithAuthority(authority)
                .WithParentActivityOrWindow(_parentWindow)
                .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows) { Title = "Planner Widget" })
                .WithClientName("Planner Widget")
                .Build();

            await RegisterCacheAsync(_browserApp, "msal-browser.cache").ConfigureAwait(false);
            await RegisterCacheAsync(_brokerApp, "msal-wam.cache").ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var result = await AcquireSilentAsync(cancellationToken).ConfigureAwait(false);
        return result.AccessToken;
    }

    /// <summary>Bejelentkezés felhasználói beavatkozás nélkül (gyorsítótár vagy Windows-fiók).</summary>
    public async Task<bool> TrySignInSilentlyAsync(CancellationToken ct = default)
    {
        try
        {
            await AcquireSilentAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (NotSignedInException)
        {
            return false;
        }
    }

    /// <summary>Interaktív bejelentkezés. Megszakításkor <see cref="OperationCanceledException"/>-t dob.</summary>
    public async Task SignInInteractiveAsync(CancellationToken ct)
    {
        await InitializeAsync().ConfigureAwait(false);

        if (IsBrokerEnabled)
        {
            try
            {
                var result = await _brokerApp!.AcquireTokenInteractive(GraphConfig.Scopes)
                    .WithParentActivityOrWindow(_parentWindow())
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync(ct).ConfigureAwait(false);
                AccountName = result.Account?.Username;
                Log.Info($"Bejelentkezve (Windows-fiók): {AccountName}");
                return;
            }
            catch (MsalClientException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
            {
                throw new OperationCanceledException("A bejelentkezést megszakították.", ex);
            }
            catch (MsalException ex)
            {
                Log.Warn("A Windows-fiókos (WAM) bejelentkezés nem érhető el, átváltás böngészős módra.", ex);
                DisableBroker();
            }
        }

        try
        {
            var result = await _browserApp!.AcquireTokenInteractive(GraphConfig.Scopes)
                .WithPrompt(Prompt.SelectAccount)
                .WithUseEmbeddedWebView(false)
                .WithSystemWebViewOptions(new SystemWebViewOptions
                {
                    HtmlMessageSuccess = SuccessPage,
                    HtmlMessageError = "<html><body style='font-family:Segoe UI;padding:40px'><h2>Sikertelen bejelentkezés</h2><p>{0}: {1}</p></body></html>",
                })
                .ExecuteAsync(ct).ConfigureAwait(false);
            AccountName = result.Account?.Username;
            Log.Info($"Bejelentkezve (böngésző): {AccountName}");
        }
        catch (MsalClientException ex) when (ex.ErrorCode == MsalError.AuthenticationCanceledError)
        {
            throw new OperationCanceledException("A bejelentkezést megszakították.", ex);
        }
    }

    public async Task SignOutAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        foreach (var app in new[] { _brokerApp!, _browserApp! })
        {
            foreach (var account in await app.GetAccountsAsync().ConfigureAwait(false))
            {
                await app.RemoveAsync(account).ConfigureAwait(false);
            }
        }

        AccountName = null;
        Log.Info("Kijelentkezve.");
    }

    private async Task<AuthenticationResult> AcquireSilentAsync(CancellationToken ct)
    {
        await InitializeAsync().ConfigureAwait(false);

        var apps = IsBrokerEnabled ? new[] { _brokerApp!, _browserApp! } : [_browserApp!];
        foreach (var app in apps)
        {
            var account = (await app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
            if (account is null)
            {
                continue;
            }

            try
            {
                var result = await app.AcquireTokenSilent(GraphConfig.Scopes, account).ExecuteAsync(ct).ConfigureAwait(false);
                AccountName = result.Account?.Username;
                return result;
            }
            catch (MsalUiRequiredException)
            {
                // Lejárt vagy visszavont – próbáljuk a következő utat.
            }
            catch (MsalServiceException ex) when (IsNetworkError(ex))
            {
                throw new GraphNetworkException("Nincs kapcsolat a bejelentkezési szolgáltatással.", ex);
            }
            catch (MsalClientException ex) when (ex.ErrorCode == MsalError.RequestTimeout)
            {
                throw new GraphNetworkException("Időtúllépés a bejelentkezésnél.", ex);
            }
        }

        // Egyszeri bejelentkezés a Windowsba belépett fiókkal (csak WAM esetén).
        if (IsBrokerEnabled)
        {
            try
            {
                var result = await _brokerApp!.AcquireTokenSilent(GraphConfig.Scopes, PublicClientApplication.OperatingSystemAccount)
                    .ExecuteAsync(ct).ConfigureAwait(false);
                AccountName = result.Account?.Username;
                Log.Info($"Egyszeri bejelentkezés a Windows-fiókkal: {AccountName}");
                return result;
            }
            catch (MsalUiRequiredException)
            {
            }
            catch (MsalServiceException ex) when (IsBrokerConfigurationError(ex))
            {
                Log.Warn("A WAM átirányítási cím nincs regisztrálva – böngészős bejelentkezés lesz használva.", ex);
                DisableBroker();
            }
            catch (MsalException ex)
            {
                Log.Warn("Csendes Windows-fiókos bejelentkezés nem sikerült", ex);
            }
        }

        throw new NotSignedInException();
    }

    private void DisableBroker()
    {
        if (_settings.Current.AuthMode != AuthMode.Browser)
        {
            _settings.Update(s => s.AuthMode = AuthMode.Browser);
        }
    }

    private static bool IsBrokerConfigurationError(MsalServiceException ex) =>
        ex.Message.Contains("AADSTS50011", StringComparison.Ordinal) ||
        ex.Message.Contains("redirect", StringComparison.OrdinalIgnoreCase) ||
        ex.ErrorCode is "invalid_request" or "unauthorized_client";

    private static bool IsNetworkError(MsalServiceException ex) =>
        ex.InnerException is HttpRequestException || ex.ErrorCode == MsalError.ServiceNotAvailable;

    private static async Task RegisterCacheAsync(IPublicClientApplication app, string fileName)
    {
        try
        {
            var storage = new StorageCreationPropertiesBuilder(fileName, AppPaths.MsalCacheDirectory).Build();
            var helper = await MsalCacheHelper.CreateAsync(storage).ConfigureAwait(false);
            helper.RegisterCache(app.UserTokenCache);
        }
        catch (Exception ex)
        {
            // Gyorsítótár nélkül is működik, csak minden indításkor be kell jelentkezni.
            Log.Error("A token-gyorsítótár nem állítható be", ex);
        }
    }

    private const string SuccessPage = """
        <html><head><meta charset="utf-8"><title>Planner Widget</title></head>
        <body style="font-family:'Segoe UI',sans-serif;background:#1b1f24;color:#e6e6e6;display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
        <div style="text-align:center"><div style="font-size:48px">✓</div>
        <h2 style="font-weight:600">Sikeres bejelentkezés</h2>
        <p style="color:#9aa4ad">Ezt a lapot bezárhatod, és visszatérhetsz a Planner Widgethez.</p></div></body></html>
        """;
}

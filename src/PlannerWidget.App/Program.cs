using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using PlannerWidget.App.Interop;

namespace PlannerWidget.App;

/// <summary>
/// Saját belépési pont: egyszerre csak egy példány fut. Ha már fut egy, az új indítás
/// csak előhozza a meglévő ablakot (pl. parancsikonra kattintáskor), majd kilép.
/// </summary>
public static class Program
{
    /// <summary>Bemutató mód: <c>PlannerWidget.exe --demo</c>. Kitalált adatok, külön adatmappa.</summary>
    public static bool IsDemo { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        IsDemo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        if (IsDemo)
        {
            Core.Storage.AppPaths.OverrideRoot(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlannerWidget", "demo"));
        }

        if (RedirectToRunningInstance(IsDemo ? "PlannerWidget.Demo" : "PlannerWidget.Main"))
        {
            return 0;
        }

        Application.Start(_1 =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    /// <summary>Az előző példánynak továbbított aktiválás (a <see cref="App"/> erre előhozza az ablakot).</summary>
    public static event EventHandler? ActivatedByAnotherInstance;

    private static bool RedirectToRunningInstance(string instanceKey)
    {
        var mainInstance = AppInstance.FindOrRegisterForKey(instanceKey);
        if (mainInstance.IsCurrent)
        {
            mainInstance.Activated += (_, _) => ActivatedByAnotherInstance?.Invoke(null, EventArgs.Empty);
            return false;
        }

        // A futó példány előtérbe hozhatja magát.
        NativeMethods.AllowSetForegroundWindow((int)mainInstance.ProcessId);
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask()).Wait();
        return true;
    }
}

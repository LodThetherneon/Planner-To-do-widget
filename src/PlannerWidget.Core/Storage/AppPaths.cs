namespace PlannerWidget.Core.Storage;

/// <summary>
/// Minden állapotfájl abszolút útvonalon, a %LOCALAPPDATA%\PlannerWidget mappában van –
/// így nem számít, honnan indul a program (a régi verzió a munkakönyvtárba írt).
/// </summary>
public static class AppPaths
{
    private static string? _root;

    public static string Root
    {
        get
        {
            if (_root is null)
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                _root = Path.Combine(baseDir, "PlannerWidget");
                Directory.CreateDirectory(_root);
            }

            return _root;
        }
    }

    /// <summary>Tesztekhez: más gyökérkönyvtár használata.</summary>
    public static void OverrideRoot(string root)
    {
        Directory.CreateDirectory(root);
        _root = root;
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string TaskCacheFile => Path.Combine(Root, "task-cache.json");
    public static string PlanTitlesFile => Path.Combine(Root, "plan-titles.json");
    public static string MsalCacheDirectory => Root;
    public static string LogDirectory => Path.Combine(Root, "logs");
}

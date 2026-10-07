using System.Text;

namespace PlannerWidget.Core.Logging;

/// <summary>
/// Egyszerű, szálbiztos fájlnapló (%LOCALAPPDATA%\PlannerWidget\logs\app.log).
/// 1 MB fölött az előző naplót app.1.log néven megtartja.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Gate = new();
    private static string? _file;

    public static string? FilePath => _file;

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "app.log");
    }

    public static void Info(string message) => Write("INF", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WRN", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(level).Append("] ")
            .Append(message);
        if (ex is not null)
        {
            line.AppendLine().Append(ex);
        }

        System.Diagnostics.Debug.WriteLine(line.ToString());

        if (_file is null)
        {
            return;
        }

        lock (Gate)
        {
            try
            {
                var info = new FileInfo(_file);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(_file, Path.Combine(info.DirectoryName!, "app.1.log"), overwrite: true);
                }

                File.AppendAllText(_file, line.AppendLine().ToString(), Encoding.UTF8);
            }
            catch (IOException)
            {
                // A naplózás hibája soha ne állítsa meg az alkalmazást.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

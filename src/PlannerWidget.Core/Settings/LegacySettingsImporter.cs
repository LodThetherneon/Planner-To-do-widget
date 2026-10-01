using System.Text.Json;
using PlannerWidget.Core.Attendance;
using PlannerWidget.Core.Logging;

namespace PlannerWidget.Core.Settings;

public sealed record LegacyImportResult(int DefaultBuckets, bool PdfPath, bool Templates, bool WorkState, int PlanTitles);

/// <summary>
/// A régi Python-verzió (planner_defaults.json, plan_cache.json) beállításainak átvétele:
/// tervenkénti alap-bucket, jelenléti PDF útvonala és mezősablonjai, futó munkaidő, tervnevek.
/// </summary>
public static class LegacySettingsImporter
{
    public const string DefaultsFileName = "planner_defaults.json";
    public const string PlanCacheFileName = "plan_cache.json";

    public static LegacyImportResult Import(string legacyDirectory, AppSettings target, IDictionary<string, string> planTitles)
    {
        var buckets = 0;
        bool pdf = false, templates = false, work = false;

        var defaultsPath = Path.Combine(legacyDirectory, DefaultsFileName);
        if (File.Exists(defaultsPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(defaultsPath));
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!property.Name.StartsWith("__", StringComparison.Ordinal) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    !target.DefaultBucketByPlan.ContainsKey(property.Name))
                {
                    target.DefaultBucketByPlan[property.Name] = property.Value.GetString()!;
                    buckets++;
                }
            }

            var root = doc.RootElement;
            if (target.Attendance.PdfPath is null &&
                root.TryGetProperty("__work_pdf_path", out var pdfPath) &&
                pdfPath.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(pdfPath.GetString()))
            {
                target.Attendance.PdfPath = NormalizePath(pdfPath.GetString()!);
                pdf = true;
            }

            if (target.Attendance.Templates is null &&
                root.TryGetProperty("__work_pdf_templates", out var tpl) &&
                tpl.ValueKind == JsonValueKind.Object)
            {
                string? Get(string key) =>
                    tpl.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                var arrival = Get("arrival");
                var leave = Get("leave");
                var hours = Get("hours");
                var sign = Get("sign");
                if (arrival is not null && leave is not null && hours is not null && sign is not null)
                {
                    target.Attendance.Templates = new AttendanceTemplates(arrival, leave, hours, sign, Get("total_hours"));
                    templates = true;
                }
            }

            if (!target.Attendance.WorkRunning &&
                root.TryGetProperty("__work_running", out var running) && running.ValueKind == JsonValueKind.True &&
                root.TryGetProperty("__work_start_iso", out var startIso) &&
                DateTime.TryParse(startIso.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var start))
            {
                target.Attendance.WorkRunning = true;
                target.Attendance.WorkStart = start;
                work = true;
            }
        }

        var titles = 0;
        var planCachePath = Path.Combine(legacyDirectory, PlanCacheFileName);
        if (File.Exists(planCachePath))
        {
            try
            {
                var cache = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(planCachePath)) ?? [];
                foreach (var (id, title) in cache)
                {
                    if (planTitles.TryAdd(id, title))
                    {
                        titles++;
                    }
                }
            }
            catch (JsonException ex)
            {
                Log.Warn("plan_cache.json nem olvasható", ex);
            }
        }

        return new LegacyImportResult(buckets, pdf, templates, work, titles);
    }

    /// <summary>A Python "//szerver/megosztás/..." alakot Windows UNC útvonallá alakítja.</summary>
    public static string NormalizePath(string path) =>
        path.StartsWith("//", StringComparison.Ordinal) ? path.Replace('/', '\\') : Path.GetFullPath(path);

    /// <summary>Igaz, ha a mappában a régi Python-verzió beállításfájlja található.</summary>
    public static bool IsLegacyDirectory(string? directory) =>
        !string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, DefaultsFileName));
}

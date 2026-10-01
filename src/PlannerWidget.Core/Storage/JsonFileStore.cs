using System.Text.Json;
using System.Text.Json.Serialization;
using PlannerWidget.Core.Logging;

namespace PlannerWidget.Core.Storage;

/// <summary>JSON fájl olvasása/írása. Az írás atomikus (ideiglenes fájl + csere), így áramszünet sem rontja el.</summary>
public sealed class JsonFileStore<T>(string path) where T : class
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Ékezetek olvashatóan (nem É formában) – a settings.json kézzel is szerkeszthető maradjon.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _lock = new();

    public string FilePath { get; } = path;

    public T? Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return null;
                }

                using var stream = File.OpenRead(FilePath);
                return JsonSerializer.Deserialize<T>(stream, Options);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warn($"Nem olvasható: {FilePath} ({ex.Message})");
                return null;
            }
        }
    }

    public void Save(T value)
    {
        lock (_lock)
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = FilePath + ".tmp";
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, value, Options);
            }

            File.Move(temp, FilePath, overwrite: true);
        }
    }
}

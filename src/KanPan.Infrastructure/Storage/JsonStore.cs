using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using KanPan.Core;

namespace KanPan.Infrastructure.Storage;

/// <summary>Reads and writes JSON files under one directory. Writes go to a temp file first, then replace the target.</summary>
public sealed class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAppLog _log;

    public JsonStore(string directory, IAppLog? log = null)
    {
        Directory.CreateDirectory(directory);
        RootDirectory = directory;
        _log = log ?? NullLog.Instance;
    }

    public string RootDirectory { get; }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KanPan");

    /// <summary>
    /// Returns <paramref name="fallback"/> when the file is missing. A corrupt file is renamed to *.bak
    /// and reported through <paramref name="wasCorrupt"/>.
    /// </summary>
    public T Load<T>(string fileName, Func<T> fallback, out bool wasCorrupt)
    {
        wasCorrupt = false;
        var path = Path.Combine(RootDirectory, fileName);
        if (!File.Exists(path)) return fallback();
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options) ?? fallback();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            wasCorrupt = true;
            _log.Error($"{fileName} 已损坏，已备份为 .bak 并使用默认值", ex);
            File.Copy(path, path + ".bak", overwrite: true);
            return fallback();
        }
    }

    public T Load<T>(string fileName, Func<T> fallback) => Load(fileName, fallback, out _);

    public void Save<T>(string fileName, T value)
    {
        var path = Path.Combine(RootDirectory, fileName);
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
            JsonSerializer.Serialize(stream, value, Options);
        File.Move(tmp, path, overwrite: true);
    }
}

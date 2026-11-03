using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Nexstrap.Services.Configuration;

/// <summary>
/// Crash-safe JSON persistence for a single document type.
/// Writes go to a temp file first and are then swapped in, so a crash mid-write
/// never leaves a half-written file. A corrupt file is moved aside and defaults are used.
/// </summary>
public sealed class JsonFileStore<T> where T : class, new()
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    public JsonFileStore(string path, ILogger logger)
    {
        _path = path;
        _logger = logger;
    }

    public string FilePath => _path;

    public T Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                _logger.LogInformation("No file at {Path}; using defaults", _path);
                return new T();
            }

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? new T();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                var quarantine = $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                _logger.LogError(ex, "Failed to read {Path}; moving it to {Quarantine} and using defaults", _path, quarantine);
                TryMove(_path, quarantine);
                return new T();
            }
        }
    }

    public void Save(T value)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, SerializerOptions));
            File.Move(temp, _path, overwrite: true);
        }
    }

    private void TryMove(string from, string to)
    {
        try { File.Move(from, to, overwrite: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not quarantine {Path}", from); }
    }
}

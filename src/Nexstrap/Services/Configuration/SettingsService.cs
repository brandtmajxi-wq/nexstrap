using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;

namespace Nexstrap.Services.Configuration;

public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Persist immediately.</summary>
    void Save();

    /// <summary>Coalesce rapid changes (sliders, typing) into one write after a short delay.</summary>
    void SaveDeferred();

    event EventHandler? Saved;
}

public sealed class SettingsService : ISettingsService, IDisposable
{
    private static readonly TimeSpan DeferDelay = TimeSpan.FromMilliseconds(400);

    private readonly JsonFileStore<AppSettings> _store;
    private readonly ILogger<SettingsService> _logger;
    private readonly Timer _deferTimer;

    public SettingsService(ILogger<SettingsService> logger)
        : this(AppInfo.Paths.Settings, logger) { }

    internal SettingsService(string path, ILogger<SettingsService> logger)
    {
        _logger = logger;
        _store = new JsonFileStore<AppSettings>(path, logger);
        Current = _store.Load();
        _deferTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public AppSettings Current { get; }

    public event EventHandler? Saved;

    public void Save()
    {
        _deferTimer.Change(Timeout.Infinite, Timeout.Infinite);
        try
        {
            _store.Save(Current);
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // Never crash the UI because the disk is full or the file is locked.
            _logger.LogError(ex, "Failed to save settings to {Path}", _store.FilePath);
        }
    }

    public void SaveDeferred() => _deferTimer.Change(DeferDelay, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _deferTimer.Dispose();
    }
}

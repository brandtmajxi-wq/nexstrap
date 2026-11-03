using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Services.Integrations;

public interface ICompanionAppService
{
    IReadOnlyList<CompanionApp> Apps { get; }

    event EventHandler? Changed;

    /// <returns>An error message, or null when added.</returns>
    string? Add(string exePath);

    void Update(CompanionApp app);

    void Remove(CompanionApp app);

    /// <summary>Starts one app now, for checking that it works.</summary>
    string? TestLaunch(CompanionApp app);

    /// <summary>Nexstrap must stay alive until Roblox exits so it can close these apps.</summary>
    bool NeedsToWatchSession { get; }
}

/// <summary>
/// Starts the user's chosen programs when a Roblox session launched by Nexstrap begins, and
/// (optionally) asks them to close when it ends. Only processes Nexstrap itself started are closed,
/// and only gracefully, never killed.
/// </summary>
public sealed class CompanionAppService : ICompanionAppService
{
    private readonly ISettingsService _settings;
    private readonly ILogger<CompanionAppService> _logger;
    private readonly List<(CompanionApp App, Process Process)> _running = [];
    private bool _sessionActive;

    public CompanionAppService(ISettingsService settings, IRobloxSessionService session, ILogger<CompanionAppService> logger)
    {
        _settings = settings;
        _logger = logger;
        session.Changed += (_, _) => OnSessionChanged(session.IsActive);
    }

    public IReadOnlyList<CompanionApp> Apps => _settings.Current.CompanionApps;

    public bool NeedsToWatchSession => Apps.Any(a => a.Enabled && a.CloseWithRoblox);

    public event EventHandler? Changed;

    public string? Add(string exePath)
    {
        var error = Validate(exePath);
        if (error is not null) return error;
        if (Apps.Any(a => string.Equals(a.Path, exePath, StringComparison.OrdinalIgnoreCase)))
            return "That program is already in the list.";

        _settings.Current.CompanionApps.Add(new CompanionApp
        {
            Name = DisplayName(exePath),
            Path = exePath
        });
        Commit();
        return null;
    }

    public void Update(CompanionApp app) => Commit();

    public void Remove(CompanionApp app)
    {
        _settings.Current.CompanionApps.Remove(app);
        Commit();
    }

    public string? TestLaunch(CompanionApp app) => Start(app) is null ? $"{app.Name} couldn't be started." : null;

    internal static string? Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return "Choose a program file.";
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "Only programs (.exe files) can be added.";
        if (!File.Exists(path)) return "That program no longer exists.";
        return null;
    }

    internal static string DisplayName(string exePath)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(exePath).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
        }
        catch (FileNotFoundException) { }
        return Path.GetFileNameWithoutExtension(exePath);
    }

    private void OnSessionChanged(bool active)
    {
        if (active == _sessionActive) return;
        _sessionActive = active;
        if (active) StartAll();
        else CloseAll();
    }

    private void StartAll()
    {
        foreach (var app in Apps.Where(a => a.Enabled).ToList())
        {
            if (Start(app) is { } process && app.CloseWithRoblox)
                lock (_running) _running.Add((app, process));
        }
    }

    private Process? Start(CompanionApp app)
    {
        if (Validate(app.Path) is { } error)
        {
            _logger.LogWarning("Skipping companion app {Name}: {Error}", app.Name, error);
            return null;
        }
        try
        {
            var process = Process.Start(new ProcessStartInfo(app.Path, app.Arguments)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(app.Path)!
            });
            _logger.LogInformation("Started companion app {Name}", app.Name);
            return process;
        }
        catch (Win32Exception ex)
        {
            // Includes the user declining a UAC prompt.
            _logger.LogWarning(ex, "Companion app {Name} failed to start", app.Name);
            return null;
        }
    }

    private void CloseAll()
    {
        List<(CompanionApp App, Process Process)> toClose;
        lock (_running)
        {
            toClose = [.. _running];
            _running.Clear();
        }

        foreach (var (app, process) in toClose)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow(); // polite request, same as clicking X
                    _logger.LogInformation("Asked companion app {Name} to close", app.Name);
                }
            }
            catch (InvalidOperationException) { /* already gone */ }
            finally
            {
                process.Dispose();
            }
        }
    }

    private void Commit()
    {
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

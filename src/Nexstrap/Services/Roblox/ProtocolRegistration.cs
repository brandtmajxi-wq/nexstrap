using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

public interface IProtocolRegistration
{
    /// <summary>True when roblox-player: links currently open this Nexstrap executable.</summary>
    bool IsRegistered { get; }

    /// <summary>Makes website Play buttons open Nexstrap, remembering the previous handler.</summary>
    void Register();

    /// <summary>Puts back whatever handled the links before (or removes Nexstrap's entry).</summary>
    void Unregister();
}

/// <summary>
/// Per-user (HKCU) registration of the roblox: and roblox-player: URL schemes. Nothing machine-wide
/// is touched, so no admin rights are needed, and the previous command is saved for an exact restore.
/// </summary>
public sealed class ProtocolRegistration : IProtocolRegistration
{
    internal static readonly string[] Schemes = ["roblox", "roblox-player"];

    private readonly ISettingsService _settings;
    private readonly ILogger<ProtocolRegistration> _logger;
    private readonly string _classesPath;
    private readonly string _exePath;

    public ProtocolRegistration(ISettingsService settings, ILogger<ProtocolRegistration> logger)
        : this(settings, logger, @"Software\Classes", Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.")) { }

    /// <summary>Tests point <paramref name="classesPath"/> at a scratch key.</summary>
    internal ProtocolRegistration(ISettingsService settings, ILogger<ProtocolRegistration> logger, string classesPath, string exePath)
    {
        _settings = settings;
        _logger = logger;
        _classesPath = classesPath;
        _exePath = exePath;
    }

    private string Command => $"\"{_exePath}\" \"%1\"";

    public bool IsRegistered => Schemes.All(s => ReadCommand(s) == Command);

    public void Register()
    {
        var previous = _settings.Current.Launcher.PreviousProtocolHandlers;
        foreach (var scheme in Schemes)
        {
            var current = ReadCommand(scheme);
            // Keep the original handler if Nexstrap was registered before (e.g. from another folder).
            if (current is not null && !current.Contains("Nexstrap", StringComparison.OrdinalIgnoreCase))
                previous[scheme] = current;

            using var key = Registry.CurrentUser.CreateSubKey($@"{_classesPath}\{scheme}");
            key.SetValue(null, $"URL:{scheme} Protocol");
            key.SetValue("URL Protocol", "");
            using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{_exePath}\",0");
            using var command = key.CreateSubKey(@"shell\open\command");
            command.SetValue(null, Command);
        }
        _settings.Save();
        _logger.LogInformation("Registered Nexstrap for {Schemes} (previous handlers saved: {Count})", string.Join(", ", Schemes), previous.Count);
    }

    public void Unregister()
    {
        var previous = _settings.Current.Launcher.PreviousProtocolHandlers;
        foreach (var scheme in Schemes)
        {
            if (ReadCommand(scheme) is not { } current || !current.Contains("Nexstrap", StringComparison.OrdinalIgnoreCase))
                continue; // someone else owns it now; leave it alone

            if (previous.TryGetValue(scheme, out var original))
            {
                using var command = Registry.CurrentUser.CreateSubKey($@"{_classesPath}\{scheme}\shell\open\command");
                command.SetValue(null, original);
                // Point the icon back at the original program too.
                var exe = ExtractExe(original);
                if (exe is not null)
                    using (var icon = Registry.CurrentUser.CreateSubKey($@"{_classesPath}\{scheme}\DefaultIcon"))
                        icon.SetValue(null, $"\"{exe}\",0");
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"{_classesPath}\{scheme}", throwOnMissingSubKey: false);
            }
        }
        previous.Clear();
        _settings.Save();
        _logger.LogInformation("Unregistered Nexstrap URL handlers");
    }

    private string? ReadCommand(string scheme)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{_classesPath}\{scheme}\shell\open\command");
        return key?.GetValue(null) as string;
    }

    internal static string? ExtractExe(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }
        var space = command.IndexOf(' ');
        var path = space > 0 ? command[..space] : command;
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Path.IsPathRooted(path) ? path : null;
    }
}

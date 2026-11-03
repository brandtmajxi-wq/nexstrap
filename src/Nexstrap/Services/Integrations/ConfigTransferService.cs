using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.FastFlags;

namespace Nexstrap.Services.Integrations;

/// <summary>Everything worth moving to another PC, in one file.</summary>
public sealed class ConfigBundle
{
    public const string FormatName = "nexstrap-config";

    /// <summary>No default, so files that merely parse as JSON aren't mistaken for exports.</summary>
    public string? Format { get; set; }
    public int Version { get; set; } = 1;
    public string? ExportedBy { get; set; }
    public DateTime ExportedAtUtc { get; set; }

    public AppearanceSettings? Appearance { get; set; }
    public bool? CloseAfterLaunch { get; set; }
    public bool? CheckForUpdatesOnStartup { get; set; }
    public List<RecentExperience>? RecentExperiences { get; set; }
    public DiscordSettings? Discord { get; set; }
    public List<CompanionApp>? CompanionApps { get; set; }
    public List<FastFlagEntry>? FastFlags { get; set; }
    public List<ThemeProfile>? Themes { get; set; }
    public LaunchScreenSettings? LaunchScreen { get; set; }
    public List<RecentExperience>? FavoriteExperiences { get; set; }
}

public sealed record ConfigImportResult(int FastFlags, int Themes, int CompanionApps, IReadOnlyList<string> Notes);

public interface IConfigTransferService
{
    void Export(string path);

    /// <summary>Validates, backs up the current setup, then applies the file live.</summary>
    ConfigImportResult Import(string path);
}

/// <summary>
/// Exports/imports Nexstrap's configuration. Machine-specific values (window size, chosen Roblox
/// build, URL handler backups, background image files) are never exported.
/// </summary>
public sealed class ConfigTransferService(
    ISettingsService settings,
    IAppearanceService appearance,
    IFastFlagService fastFlags,
    ILogger<ConfigTransferService> logger) : IConfigTransferService
{
    internal string BackupDir { get; init; } = AppInfo.Paths.Backups;

    public void Export(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(CreateBundle(), JsonFileStore<ConfigBundle>.SerializerOptions));

    internal ConfigBundle CreateBundle()
    {
        var s = settings.Current;
        var look = s.Appearance.Clone();
        look.BackgroundImage = null; // the image file itself isn't part of the export

        return new ConfigBundle
        {
            Format = ConfigBundle.FormatName,
            ExportedBy = $"{AppInfo.Name} {AppInfo.VersionString}",
            ExportedAtUtc = DateTime.UtcNow,
            Appearance = look,
            CloseAfterLaunch = s.Launcher.CloseAfterLaunch,
            CheckForUpdatesOnStartup = s.Launcher.CheckForUpdatesOnStartup,
            RecentExperiences = s.Launcher.RecentExperiences.ToList(),
            Discord = s.Discord,
            CompanionApps = s.CompanionApps.ToList(),
            LaunchScreen = new LaunchScreenSettings
            {
                Enabled = s.LaunchScreen.Enabled,
                Title = s.LaunchScreen.Title,
                BackgroundColor = s.LaunchScreen.BackgroundColor,
                AccentColor = s.LaunchScreen.AccentColor,
                Animation = s.LaunchScreen.Animation
            },
            FavoriteExperiences = s.FavoriteExperiences.ToList(),
            FastFlags = fastFlags.Flags.ToList(),
            Themes = appearance.LoadSavedProfiles().Select(p =>
            {
                var a = p.Appearance.Clone();
                a.BackgroundImage = null;
                return new ThemeProfile { Name = p.Name, Appearance = a };
            }).ToList()
        };
    }

    public ConfigImportResult Import(string path)
    {
        ConfigBundle bundle;
        try
        {
            bundle = JsonSerializer.Deserialize<ConfigBundle>(File.ReadAllText(path), JsonFileStore<ConfigBundle>.SerializerOptions)
                     ?? throw new InvalidDataException("The file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("This isn't a Nexstrap configuration file.", ex);
        }
        if (bundle.Format != ConfigBundle.FormatName)
            throw new InvalidDataException("This isn't a Nexstrap configuration file.");
        if (bundle.Version > 1)
            throw new InvalidDataException("This file was made by a newer Nexstrap. Update Nexstrap, then import it again.");

        // Safety net: snapshot the current setup in the same format.
        Directory.CreateDirectory(BackupDir);
        Export(Path.Combine(BackupDir, $"NexstrapConfig-{DateTime.Now:yyyyMMdd-HHmmss}.json"));

        var notes = new List<string>();
        var s = settings.Current;

        if (bundle.CloseAfterLaunch is { } close) s.Launcher.CloseAfterLaunch = close;
        if (bundle.CheckForUpdatesOnStartup is { } check) s.Launcher.CheckForUpdatesOnStartup = check;
        if (bundle.RecentExperiences is { } recent)
            s.Launcher.RecentExperiences = recent.Where(r => r.PlaceId > 0).Take(6).ToList();
        if (bundle.Discord is { } discord)
        {
            if (discord.ApplicationIdOverride is { Length: > 0 } id && !Discord.RichPresenceService.IsValidApplicationId(id))
                discord.ApplicationIdOverride = null;
            s.Discord = discord;
        }

        if (bundle.LaunchScreen is { } screen)
        {
            screen.BackgroundImage = s.LaunchScreen.BackgroundImage; // exports carry no image; keep this PC's
            s.LaunchScreen = screen;
        }
        if (bundle.FavoriteExperiences is { } favorites)
            s.FavoriteExperiences = favorites.Where(f => f.PlaceId > 0).ToList();

        var apps = 0;
        if (bundle.CompanionApps is { } companions)
        {
            var usable = companions.Where(c => CompanionAppService.Validate(c.Path) is null).ToList();
            foreach (var missing in companions.Except(usable))
                notes.Add($"Companion app \"{missing.Name}\" was skipped: {missing.Path} isn't on this PC.");
            s.CompanionApps = usable;
            apps = usable.Count;
        }
        settings.Save();

        var themes = 0;
        foreach (var theme in bundle.Themes ?? [])
        {
            var temp = Path.Combine(Path.GetTempPath(), $"nexstrap-theme-{Guid.NewGuid():N}.json");
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(theme, JsonFileStore<ThemeProfile>.SerializerOptions));
                appearance.ImportProfile(temp);
                themes++;
            }
            catch (InvalidDataException ex)
            {
                notes.Add($"Theme \"{theme.Name}\" was skipped: {ex.Message}");
            }
            finally
            {
                File.Delete(temp);
            }
        }

        // Applied last so the UI ends up showing the imported look.
        if (bundle.Appearance is { } look)
        {
            look.BackgroundImage = s.Appearance.BackgroundImage; // exports carry no image; keep this PC's
            appearance.ApplyProfile(new ThemeProfile { Name = "Imported", Appearance = look });
        }

        var flags = bundle.FastFlags is { } list ? fastFlags.ReplaceAll(list) : fastFlags.Flags.Count;

        logger.LogInformation("Imported configuration: {Flags} flags, {Themes} themes, {Apps} companion apps", flags, themes, apps);
        return new ConfigImportResult(flags, themes, apps, notes);
    }
}

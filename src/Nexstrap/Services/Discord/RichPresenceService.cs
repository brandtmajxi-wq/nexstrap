using System.Net.Http;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Services.Discord;

public enum PresenceStatus
{
    Off,
    /// <summary>No Discord application ID configured.</summary>
    NeedsSetup,
    /// <summary>Enabled; nothing to show because Roblox isn't running.</summary>
    Waiting,
    DiscordNotRunning,
    Rejected,
    Showing,
    Error
}

public interface IRichPresenceService
{
    PresenceStatus Status { get; }

    string StatusText { get; }

    bool IsPreviewing { get; }

    /// <summary>Raised on any status change. May be raised on a background thread.</summary>
    event EventHandler? StatusChanged;

    /// <summary>Shows the menu presence without Roblox running, so setup can be checked.</summary>
    Task SetPreviewAsync(bool on);

    /// <summary>Re-applies after a settings change.</summary>
    Task RefreshAsync();

    string? EffectiveApplicationId { get; }

    /// <summary>Whether Nexstrap should stay alive (hidden) while Roblox runs, to keep the presence up.</summary>
    bool KeepsRunningDuringSession { get; }
}

/// <summary>
/// Mirrors the Roblox session Nexstrap launched onto the user's Discord profile:
/// "Inside Nexstrap · Browsing Roblox" in the app, or the experience name and icon in game.
/// </summary>
public sealed class RichPresenceService : IRichPresenceService, IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(20);

    private readonly IDiscordIpcClient _discord;
    private readonly IRobloxSessionService _session;
    private readonly IExperienceInfoService _experiences;
    private readonly ISettingsService _settings;
    private readonly ILogger<RichPresenceService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _retry;
    private DateTime _previewSince;
    private string? _lastActivityJson;

    public RichPresenceService(IDiscordIpcClient discord, IRobloxSessionService session, IExperienceInfoService experiences,
        ISettingsService settings, ILogger<RichPresenceService> logger)
    {
        _discord = discord;
        _session = session;
        _experiences = experiences;
        _settings = settings;
        _logger = logger;
        _retry = new Timer(_ => _ = RefreshAsync(), null, Timeout.Infinite, Timeout.Infinite);
        session.Changed += (_, _) => _ = RefreshAsync();
    }

    public PresenceStatus Status { get; private set; } = PresenceStatus.Waiting;

    public string StatusText { get; private set; } = "Waiting for Roblox";

    public bool IsPreviewing { get; private set; }

    public event EventHandler? StatusChanged;

    private DiscordSettings Options => _settings.Current.Discord;

    public string? EffectiveApplicationId
    {
        get
        {
            var id = string.IsNullOrWhiteSpace(Options.ApplicationIdOverride) ? AppInfo.DiscordApplicationId : Options.ApplicationIdOverride.Trim();
            return IsValidApplicationId(id) ? id : null;
        }
    }

    public bool KeepsRunningDuringSession => Options.RichPresence && EffectiveApplicationId is not null;

    internal static bool IsValidApplicationId(string? id) => id is { Length: >= 17 and <= 20 } && id.All(char.IsAsciiDigit);

    public async Task SetPreviewAsync(bool on)
    {
        IsPreviewing = on;
        _previewSince = DateTime.UtcNow;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await ApplyAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rich Presence update failed");
            Report(PresenceStatus.Error, "Something went wrong: " + ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ApplyAsync()
    {
        _retry.Change(Timeout.Infinite, Timeout.Infinite);

        if (!Options.RichPresence && !IsPreviewing)
        {
            await ClearAsync();
            Report(PresenceStatus.Off, "Rich Presence is off");
            return;
        }

        var activity = await BuildCurrentAsync();
        if (activity is null)
        {
            await ClearAsync();
            Report(PresenceStatus.Waiting, "Shows up on Discord while Roblox is running from Nexstrap");
            return;
        }

        if (EffectiveApplicationId is not { } appId)
        {
            Report(PresenceStatus.NeedsSetup, "Needs a Discord application ID");
            return;
        }

        if (_discord.State != DiscordConnectionState.Connected)
        {
            _lastActivityJson = null;
            var state = await _discord.ConnectAsync(appId);
            if (state != DiscordConnectionState.Connected)
            {
                _retry.Change(RetryDelay, Timeout.InfiniteTimeSpan); // Discord may start later
                Report(state == DiscordConnectionState.Rejected ? PresenceStatus.Rejected : PresenceStatus.DiscordNotRunning,
                    state == DiscordConnectionState.Rejected
                        ? $"Discord rejected the application ID ({_discord.LastError})"
                        : "Discord isn't running; retrying");
                return;
            }
        }

        var json = activity.ToJsonString();
        if (json == _lastActivityJson) return; // nothing changed

        try
        {
            await _discord.SetActivityAsync(activity);
            _lastActivityJson = json;
            Report(PresenceStatus.Showing, IsPreviewing && !_session.IsActive
                ? "Previewing on Discord"
                : "Showing on Discord: " + activity["details"]?.GetValue<string>());
        }
        catch (DiscordException ex)
        {
            _lastActivityJson = null;
            _retry.Change(RetryDelay, Timeout.InfiniteTimeSpan);
            Report(PresenceStatus.Error, ex.Message);
        }
    }

    private async Task<JsonObject?> BuildCurrentAsync()
    {
        if (_session.Current is { } activity && Options.RichPresence)
        {
            if (activity.Kind != RobloxActivityKind.InGame || activity.PlaceId is not { } placeId)
                return BuildMenu(activity.SinceUtc, Options);

            ExperienceInfo? info = null;
            string? icon = null;
            RobloxUser? user = null;
            if (Options.ShowExperienceDetails)
            {
                try
                {
                    info = activity.UniverseId is { } universe
                        ? await _experiences.LookupUniverseAsync(universe, placeId)
                        : await _experiences.LookupAsync(placeId);
                    if (info is not null) icon = await _experiences.GetIconUrlAsync(info.UniverseId);
                    if (Options.ShowAvatar && activity.UserId is { } userId) user = await _experiences.GetUserAsync(userId);
                }
                catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
                {
                    _logger.LogDebug(ex, "Experience details unavailable for presence");
                }
            }
            return BuildGame(info, icon, placeId, activity.SinceUtc, Options, user);
        }

        return IsPreviewing ? BuildMenu(_previewSince, Options) : null;
    }

    internal static JsonObject BuildMenu(DateTime sinceUtc, DiscordSettings? options = null) => new()
    {
        ["details"] = FitOr(options?.MenuTopLine, DiscordSettings.DefaultMenuTopLine),
        ["state"] = FitOr(options?.MenuBottomLine, DiscordSettings.DefaultMenuBottomLine),
        ["timestamps"] = new JsonObject { ["start"] = new DateTimeOffset(sinceUtc, TimeSpan.Zero).ToUnixTimeSeconds() },
        ["assets"] = new JsonObject { ["large_image"] = CardImage(options), ["large_text"] = AppInfo.Name }
    };

    /// <summary>True for an https link or a Discord Art Asset name (letters, digits, _ and -).</summary>
    internal static bool IsValidImage(string? image)
    {
        var text = image?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 256) return false;
        if (text.Contains("://"))
            return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
        return text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    /// <summary>Discord stores Art Asset names in lower case; links must keep their case.</summary>
    internal static string CardImage(DiscordSettings? options)
    {
        var image = options?.MenuImage?.Trim();
        if (!IsValidImage(image)) return AppInfo.DiscordLogoAsset;
        return image!.Contains("://") ? image : image.ToLowerInvariant();
    }

    internal static JsonObject BuildGame(ExperienceInfo? info, string? iconUrl, long placeId, DateTime sinceUtc,
        DiscordSettings options, RobloxUser? user = null)
    {
        var showDetails = options.ShowExperienceDetails && info is not null;

        // Corner badge: the player's avatar when allowed and known, otherwise the logo.
        var avatar = showDetails && options.ShowAvatar ? user : null;
        var (smallImage, smallText) = avatar?.HeadshotUrl is { } headshot
            ? (headshot, avatar.DisplayName == avatar.Name ? $"Playing as @{avatar.Name}" : $"Playing as {avatar.DisplayName} (@{avatar.Name})")
            : (CardImage(options), $"via {AppInfo.Name}");

        var activity = new JsonObject
        {
            ["details"] = Fit(showDetails ? info!.Name : "In a Roblox experience"),
            ["state"] = Fit(showDetails && info!.CreatorName.Length > 0 ? $"by {info.CreatorName}" : "Inside Nexstrap"),
            ["timestamps"] = new JsonObject { ["start"] = new DateTimeOffset(sinceUtc, TimeSpan.Zero).ToUnixTimeSeconds() },
            ["assets"] = showDetails && iconUrl is not null
                ? new JsonObject
                {
                    ["large_image"] = iconUrl,
                    ["large_text"] = Fit($"{info!.Name} · via {AppInfo.Name}"),
                    ["small_image"] = smallImage,
                    ["small_text"] = Fit(smallText)
                }
                : new JsonObject { ["large_image"] = CardImage(options), ["large_text"] = AppInfo.Name }
        };

        if (options.ShowExperienceDetails && options.ShowExperienceButton)
            activity["buttons"] = new JsonArray(new JsonObject
            {
                ["label"] = "View game",
                ["url"] = $"https://www.roblox.com/games/{placeId}"
            });

        return activity;
    }

    private static string FitOr(string? text, string fallback) => Fit(string.IsNullOrWhiteSpace(text) ? fallback : text);

    /// <summary>Discord requires 2–128 characters.</summary>
    internal static string Fit(string text)
    {
        text = text.Trim();
        if (text.Length > 128) text = text[..127] + "…";
        return text.Length < 2 ? text.PadRight(2) : text;
    }

    private async Task ClearAsync()
    {
        if (_discord.State != DiscordConnectionState.Connected) return;
        try { await _discord.SetActivityAsync(null); }
        catch (DiscordException) { }
        _discord.Disconnect();
        _lastActivityJson = null;
    }

    private void Report(PresenceStatus status, string text)
    {
        if (Status == status && StatusText == text) return;
        Status = status;
        StatusText = text;
        _logger.LogInformation("Rich Presence: {Status} ({Text})", status, text);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _retry.Dispose();
        _discord.Dispose();
    }
}

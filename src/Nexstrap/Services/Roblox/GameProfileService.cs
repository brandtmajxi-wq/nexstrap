using System.IO;
using System.Xml;
using Microsoft.Extensions.Logging;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;

namespace Nexstrap.Services.Roblox;

public interface IGameProfileService
{
    IReadOnlyList<GameProfile> Profiles { get; }

    GameProfile Add(long placeId, string name);

    void Remove(GameProfile profile);

    /// <summary>Call after changing a profile's values.</summary>
    void Save();

    /// <summary>Applies a game's profile to Roblox's settings before joining. Does nothing while Roblox is open.</summary>
    void ApplyFor(long placeId);

    event EventHandler? Changed;
}

/// <summary>
/// Sets a game's frame-rate cap and graphics quality in Roblox's own settings just before joining,
/// and puts the previous values back when Roblox closes. The previous values are saved first,
/// so they're still restored if Nexstrap closes before Roblox does.
/// </summary>
public sealed class GameProfileService : IGameProfileService
{
    private static readonly TimeSpan AfterRobloxCloses = TimeSpan.FromSeconds(2);

    private readonly ISettingsService _settings;
    private readonly IRobloxUserSettingsService _roblox;
    private readonly IRobloxSessionService _session;
    private readonly ILogger<GameProfileService> _logger;

    public GameProfileService(ISettingsService settings, IRobloxUserSettingsService roblox, IRobloxSessionService session, ILogger<GameProfileService> logger)
    {
        _settings = settings;
        _roblox = roblox;
        _session = session;
        _logger = logger;
        session.Changed += (_, _) => { if (!_session.IsActive && Pending is not null) _ = RestoreLaterAsync(); };
        RestorePending();
    }

    public event EventHandler? Changed;

    private List<GameProfile> List => _settings.Current.GameProfiles;

    private ProfileRestore? Pending
    {
        get => _settings.Current.Launcher.ProfileRestore;
        set => _settings.Current.Launcher.ProfileRestore = value;
    }

    public IReadOnlyList<GameProfile> Profiles => List;

    public GameProfile Add(long placeId, string name)
    {
        if (List.FirstOrDefault(p => p.PlaceId == placeId) is { } existing) return existing;

        var profile = new GameProfile { PlaceId = placeId, Name = name };
        List.Add(profile);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return profile;
    }

    public void Remove(GameProfile profile)
    {
        List.Remove(profile);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save() => _settings.Save();

    public void ApplyFor(long placeId)
    {
        if (List.FirstOrDefault(p => p.PlaceId == placeId) is not { } profile) return;
        if (profile.FramerateCap is null && profile.Quality is null) return;

        try
        {
            if (Pending is null)
            {
                var current = _roblox.Load();
                Pending = new ProfileRestore { FramerateCap = current.FramerateCap, Quality = current.SavedQualityLevel };
                _settings.Save();
            }
            _roblox.Save(new RobloxUserSettings { FramerateCap = profile.FramerateCap, SavedQualityLevel = profile.Quality });
            _logger.LogInformation("Applied the game profile for place {Place}", placeId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
        {
            _logger.LogInformation(ex, "Skipped the game profile for place {Place}", placeId);
        }
    }

    private async Task RestoreLaterAsync()
    {
        await Task.Delay(AfterRobloxCloses); // Roblox writes its own settings as it closes
        RestorePending();
    }

    internal void RestorePending()
    {
        if (Pending is not { } previous || _session.IsActive) return;

        try
        {
            _roblox.Save(new RobloxUserSettings { FramerateCap = previous.FramerateCap, SavedQualityLevel = previous.Quality });
            Pending = null;
            _settings.Save();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
        {
            // Roblox is open, perhaps from another launcher. It stays pending for next time.
            _logger.LogInformation(ex, "Couldn't restore Roblox settings after a game profile yet");
        }
    }
}

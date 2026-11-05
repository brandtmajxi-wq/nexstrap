using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Discord;
using Nexstrap.Services.Notifications;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Shell;
using Xunit;

namespace Nexstrap.Tests;

public class DiscordLocationTests
{
    private static readonly ExperienceInfo Info = new(5, 1, "My game", "Maker", 10);

    private static string? State(DiscordSettings options, ExperienceInfo? info, string? location) =>
        RichPresenceService.BuildGame(info, "https://example.com/icon.png", 5, DateTime.UtcNow, options, null, location)["state"]?.GetValue<string>();

    [Fact]
    public void Location_is_off_by_default() =>
        Assert.Equal("by Maker", State(new DiscordSettings(), Info, "Frankfurt, Germany"));

    [Fact]
    public void Location_joins_the_second_line_when_turned_on() =>
        Assert.Equal("by Maker · Frankfurt, Germany", State(new DiscordSettings { ShowServerLocation = true }, Info, "Frankfurt, Germany"));

    [Fact]
    public void Unknown_locations_leave_the_card_as_it_was() =>
        Assert.Equal("by Maker", State(new DiscordSettings { ShowServerLocation = true }, Info, null));

    [Fact]
    public void Hiding_game_details_also_hides_the_location() =>
        Assert.Equal("Inside Nexstrap", State(new DiscordSettings { ShowServerLocation = true, ShowExperienceDetails = false }, Info, "Frankfurt, Germany"));

    [Fact]
    public void Long_lines_stay_inside_Discords_limit()
    {
        var info = Info with { CreatorName = new string('x', 125) };
        Assert.True(State(new DiscordSettings { ShowServerLocation = true }, info, "Frankfurt, Germany")!.Length <= 128);
    }
}

public class GameJoinAlertTests
{
    private const string Job = "11111111-1111-1111-1111-111111111111";

    private sealed class FakeTray : ITrayService
    {
        public List<(string Title, string Text)> Alerts { get; } = [];
        public void Show() { }
        public void ShowAlert(string title, string text) => Alerts.Add((title, text));
        public void Dispose() { }
    }

    private sealed class FakeRegions : IServerRegionService
    {
        public bool IsSearching { get; set; }
        public string? CurrentRegion => null;
        public string? KnownRegion(string serverId) => null;
        public Task<RegionSearchOutcome> FindAsync(long placeId, string? name, string region, IProgress<RegionSearchProgress> progress, CancellationToken ct) =>
            Task.FromResult(RegionSearchOutcome.NotFound);
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class Games : IExperienceInfoService
    {
        public bool Offline { get; set; }
        public Task<ExperienceInfo?> LookupAsync(long placeId, CancellationToken ct = default) => LookupUniverseAsync(1, placeId, ct);
        public Task<ExperienceInfo?> LookupUniverseAsync(long universeId, long placeId, CancellationToken ct = default) =>
            Offline ? throw new System.Net.Http.HttpRequestException("offline") : Task.FromResult<ExperienceInfo?>(new ExperienceInfo(placeId, universeId, "My game", "Maker", 1));
        public Task<string?> GetIconUrlAsync(long universeId, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<RobloxUser?> GetUserAsync(long userId, CancellationToken ct = default) => Task.FromResult<RobloxUser?>(null);
    }

    private readonly FakeTray _tray = new();
    private readonly FakeRegions _regions = new();
    private readonly MemorySettings _settings = new();
    private readonly Games _games = new();
    private bool _robloxInFront;

    private GameJoinAlerts Alerts() =>
        new(new ManualSession(), _settings, _games, _tray, _regions, NullLogger<GameJoinAlerts>.Instance, () => _robloxInFront);

    private static RobloxActivity Loaded(string? address = "128.116.44.33") =>
        new(RobloxActivityKind.InGame, DateTime.UtcNow, 5, 1, Job, 9, address);

    [Fact]
    public async Task Alerts_with_the_game_and_location_when_it_finishes_loading()
    {
        await Alerts().ObserveAsync(Loaded());

        var alert = Assert.Single(_tray.Alerts);
        Assert.Equal("My game is ready", alert.Title);
        Assert.Equal("Current server: Frankfurt, Germany", alert.Text);
    }

    [Fact]
    public async Task Waits_until_the_game_has_loaded()
    {
        await Alerts().ObserveAsync(Loaded() with { UniverseId = null });

        Assert.Empty(_tray.Alerts);
    }

    [Fact]
    public async Task Alerts_once_per_server_and_again_after_leaving()
    {
        var alerts = Alerts();

        await alerts.ObserveAsync(Loaded());
        await alerts.ObserveAsync(Loaded() with { UserId = 10 });
        Assert.Single(_tray.Alerts);

        await alerts.ObserveAsync(new RobloxActivity(RobloxActivityKind.Menu, DateTime.UtcNow));
        await alerts.ObserveAsync(Loaded());
        Assert.Equal(2, _tray.Alerts.Count);
    }

    [Fact]
    public async Task Stays_quiet_when_Roblox_is_in_front()
    {
        _robloxInFront = true;

        await Alerts().ObserveAsync(Loaded());

        Assert.Empty(_tray.Alerts);
    }

    [Fact]
    public async Task Stays_quiet_when_turned_off()
    {
        _settings.Current.Notifications.GameJoinAlerts = false;

        await Alerts().ObserveAsync(Loaded());

        Assert.Empty(_tray.Alerts);
    }

    [Fact]
    public async Task Stays_quiet_during_a_region_search()
    {
        _regions.IsSearching = true;

        await Alerts().ObserveAsync(Loaded());

        Assert.Empty(_tray.Alerts);
    }

    [Fact]
    public async Task Still_alerts_without_a_location_or_a_connection()
    {
        _games.Offline = true;

        await Alerts().ObserveAsync(Loaded("128.116.86.33"));

        var alert = Assert.Single(_tray.Alerts);
        Assert.Equal("Roblox is ready", alert.Title);
        Assert.Equal("You're in the game.", alert.Text);
    }
}

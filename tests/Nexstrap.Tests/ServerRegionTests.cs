using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Roblox;
using Xunit;

namespace Nexstrap.Tests;

public class RobloxRegionsTests
{
    [Theory]
    [InlineData("128.116.44.33", "Frankfurt, Germany")]
    [InlineData("128.116.5.1", "Frankfurt, Germany")]
    [InlineData("128.116.21.33", "Amsterdam, Netherlands")]
    [InlineData("128.116.31.33", "Warsaw, Poland")]
    [InlineData("128.116.86.33", null)]
    [InlineData("10.9.9.26", null)]
    [InlineData("128.116.", null)]
    [InlineData(null, null)]
    public void Finds_the_city_for_an_address(string? address, string? expected) =>
        Assert.Equal(expected, RobloxRegions.Find(address));

    [Fact]
    public void Each_city_is_offered_once()
    {
        Assert.Equal(RobloxRegions.All.Count, RobloxRegions.All.Distinct().Count());
        Assert.Contains(RobloxRegions.Frankfurt, RobloxRegions.All);
    }

    [Fact]
    public void Reads_the_server_address_from_Roblox_log_lines()
    {
        var job = "7aaac9d1-9275-4321-8bfc-f8c03fefa228";
        var start = new RobloxActivity(RobloxActivityKind.Menu, DateTime.UtcNow);

        var joined = RobloxLogParser.Apply(start, $"2026-11-05T12:00:00.000Z,1.0,abc,6 [FLog::Output] ! Joining game '{job}' place 5 at 10.9.9.26");
        var connected = RobloxLogParser.Apply(joined!, "2026-11-05T12:00:01.000Z,2.0,abc,6 [FLog::Network] UDMUX Address = 128.116.44.33, Port = 56311 | RCC Server Address = 10.9.9.26, Port = 56311");

        Assert.Null(joined!.ServerAddress);
        Assert.Equal("128.116.44.33", connected!.ServerAddress);
        Assert.Equal(job, connected.JobId);
    }

    [Fact]
    public void Ignores_a_server_address_outside_a_game()
    {
        var menu = new RobloxActivity(RobloxActivityKind.Menu, DateTime.UtcNow);

        Assert.Null(RobloxLogParser.Apply(menu, "x [FLog::Network] UDMUX Address = 128.116.44.33, Port = 1"));
    }
}

public class ServerRegionServiceTests
{
    private const string A = "11111111-1111-1111-1111-111111111111";
    private const string B = "22222222-2222-2222-2222-222222222222";
    private const string C = "33333333-3333-3333-3333-333333333333";

    private sealed class Browser(params GameServer[] servers) : IServerBrowserService
    {
        public Task<ServerPage> GetServersAsync(long placeId, bool mostPlayersFirst, bool excludeFull, string? cursor = null, CancellationToken ct = default) =>
            Task.FromResult(new ServerPage(servers, null));
    }

    /// <summary>Pretends each hop lands on the server it was given, at the address set for it.</summary>
    private sealed class Hops(ManualSession session, Dictionary<string, string?> addresses) : ILaunchCoordinator
    {
        public List<string> Joined { get; } = [];
        public bool Fail { get; set; }
        public event EventHandler? RecentsChanged { add { } remove { } }
        public Task<LaunchResult?> LaunchAppAsync() => Task.FromResult<LaunchResult?>(null);
        public Task<LaunchResult?> JoinAsync(long placeId, string? name, string? serverId = null) => Task.FromResult<LaunchResult?>(null);
        public Task<LaunchResult?> HopAsync(long placeId, string? name, string serverId)
        {
            Joined.Add(serverId);
            if (Fail) return Task.FromResult<LaunchResult?>(new LaunchResult(false, "no"));
            session.Set(new RobloxActivity(RobloxActivityKind.InGame, DateTime.UtcNow, placeId, null, serverId, null, addresses.GetValueOrDefault(serverId)));
            return Task.FromResult<LaunchResult?>(new LaunchResult(true, "ok"));
        }
    }

    private sealed class Reports : IProgress<RegionSearchProgress>
    {
        public List<RegionSearchProgress> Items { get; } = [];
        public Action? OnReport { get; set; }
        public void Report(RegionSearchProgress value) { Items.Add(value); OnReport?.Invoke(); }
    }

    private static GameServer Server(string id, int playing = 1) => new(id, playing, 10, 60, 50);

    private readonly ManualSession _session = new();
    private readonly CloseCountingLauncher _launcher = new();

    private sealed class CloseCountingLauncher : IRobloxLauncher
    {
        public bool Running { get; set; }
        public bool Closes { get; set; } = true;
        public int CloseCalls { get; private set; }
        public LaunchResult LaunchApp(RobloxInstallation installation) => new(true, "ok");
        public LaunchResult LaunchUri(RobloxInstallation installation, string uri) => new(true, "ok");
        public bool IsRobloxRunning() => Running;
        public bool CloseRoblox(TimeSpan timeout) { CloseCalls++; return Closes; }
    }

    private ServerRegionService Service(ILaunchCoordinator launch, params GameServer[] servers) =>
        new(_session, _launcher, launch, new Browser(servers), NullLogger<ServerRegionService>.Instance)
        {
            JoinTimeout = TimeSpan.FromMilliseconds(200),
            PollDelay = TimeSpan.FromMilliseconds(5)
        };

    [Fact]
    public async Task Stops_at_the_first_server_in_the_region()
    {
        var hops = new Hops(_session, new() { [A] = "128.116.31.33", [B] = "128.116.44.33", [C] = "128.116.21.33" });
        var service = Service(hops, Server(A), Server(B), Server(C));

        var outcome = await service.FindAsync(5, "Game", RobloxRegions.Frankfurt, new Reports(), CancellationToken.None);

        Assert.Equal(RegionSearchOutcome.Found, outcome);
        Assert.Equal([A, B], hops.Joined);
        Assert.Equal(RobloxRegions.Frankfurt, service.CurrentRegion);
    }

    [Fact]
    public async Task Gives_up_when_no_server_matches()
    {
        var hops = new Hops(_session, new() { [A] = "128.116.31.33", [B] = "128.116.21.33" });
        var reports = new Reports();

        var outcome = await Service(hops, Server(A), Server(B)).FindAsync(5, "Game", RobloxRegions.Frankfurt, reports, CancellationToken.None);

        Assert.Equal(RegionSearchOutcome.NotFound, outcome);
        Assert.Contains(reports.Items, r => r is { Joined: true, Landed: "Warsaw, Poland" });
    }

    [Fact]
    public async Task Never_tries_more_than_the_limit()
    {
        var servers = Enumerable.Range(1, 20).Select(i => Server(new Guid(i, 0, 0, new byte[8]).ToString())).ToArray();
        var hops = new Hops(_session, servers.ToDictionary(s => s.Id, _ => (string?)"128.116.31.33"));

        await Service(hops, servers).FindAsync(5, "Game", RobloxRegions.Frankfurt, new Reports(), CancellationToken.None);

        Assert.Equal(ServerRegionService.MaxTries, hops.Joined.Count);
    }

    [Fact]
    public async Task Moves_on_when_a_server_never_reports_an_address()
    {
        var hops = new Hops(_session, new() { [A] = null, [B] = "128.116.44.33" });

        var outcome = await Service(hops, Server(A), Server(B)).FindAsync(5, "Game", RobloxRegions.Frankfurt, new Reports(), CancellationToken.None);

        Assert.Equal(RegionSearchOutcome.Found, outcome);
        Assert.Equal([A, B], hops.Joined);
    }

    [Fact]
    public async Task Closes_Roblox_between_tries_and_stops_if_it_will_not_close()
    {
        _launcher.Running = true;
        _launcher.Closes = false;
        var hops = new Hops(_session, new() { [A] = "128.116.44.33" });

        var outcome = await Service(hops, Server(A)).FindAsync(5, "Game", RobloxRegions.Frankfurt, new Reports(), CancellationToken.None);

        Assert.Equal(RegionSearchOutcome.Failed, outcome);
        Assert.Equal(1, _launcher.CloseCalls);
        Assert.Empty(hops.Joined);
    }

    [Fact]
    public async Task A_failed_launch_ends_the_search()
    {
        var hops = new Hops(_session, []) { Fail = true };

        var outcome = await Service(hops, Server(A), Server(B)).FindAsync(5, "Game", RobloxRegions.Frankfurt, new Reports(), CancellationToken.None);

        Assert.Equal(RegionSearchOutcome.Failed, outcome);
        Assert.Single(hops.Joined);
    }

    [Fact]
    public async Task Does_nothing_if_you_are_already_there()
    {
        var hops = new Hops(_session, []);
        _session.Set(new RobloxActivity(RobloxActivityKind.InGame, DateTime.UtcNow, 5, null, A, null, "128.116.44.33"));

        var outcome = await Service(hops, Server(B)).FindAsync(5, "Game", RobloxRegions.Frankfurt, new Reports(), CancellationToken.None);

        Assert.Equal(RegionSearchOutcome.Found, outcome);
        Assert.Empty(hops.Joined);
    }

    [Fact]
    public async Task Can_be_stopped_between_tries()
    {
        using var stop = new CancellationTokenSource();
        var hops = new Hops(_session, new() { [A] = "128.116.31.33", [B] = "128.116.44.33" });
        var reports = new Reports();
        reports.OnReport = () => { if (reports.Items[^1].Joined) stop.Cancel(); };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(hops, Server(A), Server(B)).FindAsync(5, "Game", RobloxRegions.Frankfurt, reports, stop.Token));

        Assert.Equal([A], hops.Joined);
    }

    [Fact]
    public void Servers_known_to_be_elsewhere_are_skipped_and_matches_go_first()
    {
        var service = Service(new Hops(_session, []));
        _session.Set(new RobloxActivity(RobloxActivityKind.InGame, DateTime.UtcNow, 5, null, A, null, "128.116.31.33"));
        _session.Set(new RobloxActivity(RobloxActivityKind.InGame, DateTime.UtcNow, 5, null, C, null, "128.116.44.33"));

        var ids = service.Candidates([Server(A), Server(B), Server(C), Server("44444444-4444-4444-4444-444444444444", 10)], RobloxRegions.Frankfurt)
            .Select(s => s.Id).ToList();

        Assert.Equal([C, B], ids); // the full server is left out too
        Assert.Equal("Warsaw, Poland", service.KnownRegion(A));
    }
}

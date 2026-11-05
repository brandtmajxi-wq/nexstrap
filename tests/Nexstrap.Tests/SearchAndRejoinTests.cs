using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Roblox;
using Xunit;

namespace Nexstrap.Tests;

public class GameSearchTests
{
    [Fact]
    public void Reads_games_and_skips_sponsored_and_duplicate_results()
    {
        const string json = """
        { "searchResults": [
            { "contentGroupType": "Game", "contents": [
                { "universeId": 1, "rootPlaceId": 10, "name": "First", "creatorName": "Maker", "playerCount": 5, "isSponsored": false } ] },
            { "contentGroupType": "Game", "contents": [
                { "universeId": 2, "rootPlaceId": 20, "name": "Advert", "playerCount": 1, "isSponsored": true } ] },
            { "contentGroupType": "Game", "contents": [
                { "universeId": 1, "rootPlaceId": 10, "name": "First again", "playerCount": 5 },
                { "universeId": 3, "rootPlaceId": 30, "name": "Second", "playerCount": 7 } ] }
        ] }
        """;

        var results = GameSearchService.Parse(json);

        Assert.Equal(2, results.Count);
        Assert.Equal(new GameSearchResult(10, 1, "First", "Maker", 5), results[0]);
        Assert.Equal(new GameSearchResult(30, 3, "Second", "", 7), results[1]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "searchResults": null }""")]
    [InlineData("""{ "searchResults": [ { "contents": "nope" }, { } ] }""")]
    [InlineData("""{ "searchResults": [ { "contents": [ { "name": "No place" }, { "rootPlaceId": 5 }, { "rootPlaceId": "x", "name": "Bad" } ] } ] }""")]
    public void Unexpected_replies_give_no_results_instead_of_failing(string json) =>
        Assert.Empty(GameSearchService.Parse(json));

    [Fact]
    public void Results_are_limited()
    {
        var items = string.Join(",", Enumerable.Range(1, 40).Select(i => $$"""{ "rootPlaceId": {{i}}, "name": "G{{i}}" }"""));
        var results = GameSearchService.Parse($$"""{ "searchResults": [ { "contents": [ {{items}} ] } ] }""");

        Assert.Equal(GameSearchService.MaxResults, results.Count);
    }
}

public class LastServerTests
{
    private const string Job = "6f9619ff-8b86-d011-b42d-00c04fc964ff";
    private const string OtherJob = "7f9619ff-8b86-d011-b42d-00c04fc964ff";

    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    private sealed class RecordingLaunch : ILaunchCoordinator
    {
        public (long Place, string? Name, string? Server)? Joined { get; private set; }
        public event EventHandler? RecentsChanged { add { } remove { } }
        public Task<LaunchResult?> LaunchAppAsync() => Task.FromResult<LaunchResult?>(null);
        public Task<LaunchResult?> JoinAsync(long placeId, string? name, string? serverId = null)
        {
            Joined = (placeId, name, serverId);
            return Task.FromResult<LaunchResult?>(new LaunchResult(true, "ok"));
        }
    }

    private sealed class NoExperiences : IExperienceInfoService
    {
        public Task<ExperienceInfo?> LookupAsync(long placeId, CancellationToken ct = default) => Task.FromResult<ExperienceInfo?>(null);
        public Task<ExperienceInfo?> LookupUniverseAsync(long universeId, long placeId, CancellationToken ct = default) => LookupAsync(placeId, ct);
        public Task<string?> GetIconUrlAsync(long universeId, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<RobloxUser?> GetUserAsync(long userId, CancellationToken ct = default) => Task.FromResult<RobloxUser?>(null);
    }

    private readonly MemorySettings _settings = new();
    private readonly ManualSession _session = new();
    private readonly RecordingLaunch _launch = new();

    private LastServerService Service() =>
        new(_session, _settings, _launch, new NoExperiences(), NullLogger<LastServerService>.Instance);

    private static RobloxActivity Game(string job, long place = 5) => new(RobloxActivityKind.InGame, Now, place, null, job);

    [Fact]
    public void Remembers_the_server_and_rejoins_it_with_its_id()
    {
        _settings.Current.Launcher.RecentExperiences.Add(new RecentExperience { PlaceId = 5, Name = "My game" });
        var service = Service();

        _session.Set(Game(Job));
        _session.Set(new RobloxActivity(RobloxActivityKind.Menu, Now));

        Assert.Equal(Job, service.Available?.JobId);
        service.RejoinAsync();
        Assert.Equal((5L, "My game", Job), _launch.Joined);
    }

    [Fact]
    public void A_new_server_replaces_the_old_one()
    {
        var service = Service();

        _session.Set(Game(Job));
        _session.Set(Game(OtherJob, 6));
        _session.Set(null);

        Assert.Equal(OtherJob, service.Available?.JobId);
        Assert.Equal(6, service.Available?.PlaceId);
    }

    [Fact]
    public void Not_offered_while_you_are_still_in_that_server()
    {
        var service = Service();

        _session.Set(Game(Job));

        Assert.Null(service.Available);
    }

    [Fact]
    public void Ignores_server_ids_Roblox_links_could_not_use()
    {
        var service = Service();

        _session.Set(Game("not-a-guid"));
        _session.Set(null);

        Assert.Null(service.Available);
        Assert.Null(_settings.Current.Launcher.LastServer);
    }

    [Fact]
    public void Old_servers_are_not_offered()
    {
        var server = new LastServerInfo { PlaceId = 5, JobId = Job, SeenUtc = Now };

        Assert.NotNull(LastServerService.Pick(server, null, Now.AddHours(23)));
        Assert.Null(LastServerService.Pick(server, null, Now.AddHours(25)));
    }

    [Fact]
    public void Opening_Roblox_later_does_not_make_an_old_server_look_recent()
    {
        var old = DateTime.UtcNow.AddDays(-3);
        _settings.Current.Launcher.LastServer = new LastServerInfo { PlaceId = 5, JobId = Job, SeenUtc = old };
        var service = Service();

        _session.Set(new RobloxActivity(RobloxActivityKind.Starting, DateTime.UtcNow));
        _session.Set(new RobloxActivity(RobloxActivityKind.Menu, DateTime.UtcNow));

        Assert.Equal(old, _settings.Current.Launcher.LastServer!.SeenUtc);
        Assert.Null(service.Available);
    }

    [Fact]
    public async Task Rejoin_does_nothing_without_a_server()
    {
        Assert.Null(await Service().RejoinAsync());
        Assert.Null(_launch.Joined);
    }

    [Fact]
    public void The_rejoin_link_names_the_server()
    {
        Assert.Equal($"roblox://placeId=5&gameInstanceId={Job}", RobloxLinks.ServerLaunchUri(5, Job));
    }
}

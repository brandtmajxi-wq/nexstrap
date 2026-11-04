using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Discord;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Tests;

public class RobloxLogParserTests
{
    // Real lines from a 0.745 Player log (IPs and job IDs replaced).
    private const string Menu = "2026-11-03T13:47:22.610Z,1.610792,0c30,6 [FLog::SingleSurfaceApp] setStage: (stage:LuaApp)";
    private const string Join = "2026-11-03T13:48:49.170Z,88.170990,1340,6 [FLog::Output] ! Joining game 'a1b2c3d4-0000-1111-2222-333344445555' place 103767961452227 at 10.0.0.1";
    private const string Load = "2026-11-03T13:48:49.170Z,88.170990,1340,6 [FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:103767961452227, join_time:0.43, universeid:10757247806, referral_page:Home, sid:x, clienttime:1762177729.7669999599, userid:123456789";
    private const string Leave = "2026-11-03T16:21:43.587Z,9262.587891,0c30,6 [FLog::SingleSurfaceApp] leaveUGCGameInternal";

    [Fact]
    public void Follows_menu_game_menu()
    {
        var state = new RobloxActivity(RobloxActivityKind.Starting, DateTime.UtcNow);

        state = RobloxLogParser.Apply(state, Menu)!;
        Assert.Equal(RobloxActivityKind.Menu, state.Kind);
        Assert.Equal(new DateTime(2026, 11, 3, 13, 47, 22, 610, DateTimeKind.Utc), state.SinceUtc);

        state = RobloxLogParser.Apply(state, Join)!;
        Assert.Equal(RobloxActivityKind.InGame, state.Kind);
        Assert.Equal(103767961452227, state.PlaceId);
        Assert.Equal("a1b2c3d4-0000-1111-2222-333344445555", state.JobId);
        Assert.Null(state.UniverseId);

        state = RobloxLogParser.Apply(state, Load)!;
        Assert.Equal(10757247806, state.UniverseId);
        Assert.Equal(123456789, state.UserId);

        state = RobloxLogParser.Apply(state, Leave)!;
        Assert.Equal(RobloxActivityKind.Menu, state.Kind);
    }

    [Fact]
    public void Ignores_unrelated_and_repeated_lines()
    {
        var menu = new RobloxActivity(RobloxActivityKind.Menu, DateTime.UtcNow);
        Assert.Null(RobloxLogParser.Apply(menu, Menu));
        Assert.Null(RobloxLogParser.Apply(menu, "2026-11-03T13:47:21.539Z,0.5,0c30,6 [FLog::SingleSurfaceApp] setStage: (stage:Native)"));
        Assert.Null(RobloxLogParser.Apply(menu, Load)); // load-time line without a join
    }
}

public class PresenceContentTests
{
    [Fact]
    public void Menu_presence_matches_the_requested_design()
    {
        var since = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        var a = RichPresenceService.BuildMenu(since);
        Assert.Equal("Inside Nexstrap", (string?)a["details"]);
        Assert.Equal("Browsing Roblox", (string?)a["state"]);
        Assert.Equal(new DateTimeOffset(since).ToUnixTimeSeconds(), (long?)a["timestamps"]!["start"]);
        Assert.Equal(Nexstrap.Core.AppInfo.DiscordLogoAsset, (string?)a["assets"]!["large_image"]);
    }

    [Fact]
    public void Game_presence_uses_name_icon_and_button()
    {
        var info = new ExperienceInfo(1818, 13058, "Crossroads", "Roblox", 20);
        var a = RichPresenceService.BuildGame(info, "https://tr.rbxcdn.com/x/512/512/Image/Png", 1818, DateTime.UtcNow, new DiscordSettings());
        Assert.Equal("Crossroads", (string?)a["details"]);
        Assert.Equal("by Roblox", (string?)a["state"]);
        Assert.Equal("https://tr.rbxcdn.com/x/512/512/Image/Png", (string?)a["assets"]!["large_image"]);
        Assert.Equal(Nexstrap.Core.AppInfo.DiscordLogoAsset, (string?)a["assets"]!["small_image"]);
        Assert.Equal("https://www.roblox.com/games/1818", (string?)a["buttons"]![0]!["url"]);
    }

    [Fact]
    public void Avatar_and_name_go_in_the_corner_badge()
    {
        var info = new ExperienceInfo(1818, 13058, "Wings of Glory", "Nextrium Interactive", 20);
        var user = new RobloxUser(42, "pilot42", "Ace Pilot", "https://tr.rbxcdn.com/headshot/150/150/AvatarHeadshot/Png");

        var a = RichPresenceService.BuildGame(info, "https://icon", 1818, DateTime.UtcNow, new DiscordSettings(), user);
        Assert.Equal("https://tr.rbxcdn.com/headshot/150/150/AvatarHeadshot/Png", (string?)a["assets"]!["small_image"]);
        Assert.Equal("Playing as Ace Pilot (@pilot42)", (string?)a["assets"]!["small_text"]);
        Assert.EndsWith(" · via Nexstrap", (string?)a["assets"]!["large_text"]);
        Assert.Equal("View game", (string?)a["buttons"]![0]!["label"]);

        var hidden = RichPresenceService.BuildGame(info, "https://icon", 1818, DateTime.UtcNow, new DiscordSettings { ShowAvatar = false }, user);
        Assert.Equal(Nexstrap.Core.AppInfo.DiscordLogoAsset, (string?)hidden["assets"]!["small_image"]);
        Assert.DoesNotContain("pilot42", hidden.ToJsonString());
    }

    [Fact]
    public void Private_mode_hides_the_experience()
    {
        var info = new ExperienceInfo(1818, 13058, "Crossroads", "Roblox", 20);
        var a = RichPresenceService.BuildGame(info, "https://x", 1818, DateTime.UtcNow, new DiscordSettings { ShowExperienceDetails = false });
        Assert.Equal("In a Roblox experience", (string?)a["details"]);
        Assert.DoesNotContain("Crossroads", a.ToJsonString());
        Assert.Null(a["buttons"]);
    }

    [Theory]
    [InlineData("A", "A ")]
    [InlineData("  Hi  ", "Hi")]
    public void Fits_discord_length_limits(string input, string expected) => Assert.Equal(expected, RichPresenceService.Fit(input));

    [Fact]
    public void Long_names_are_truncated() => Assert.Equal(128, RichPresenceService.Fit(new string('x', 300)).Length);

    [Theory]
    [InlineData("1234567890123456789", true)]
    [InlineData("123", false)]
    [InlineData("12345678901234567a9", false)]
    [InlineData("", false)]
    public void Validates_application_ids(string id, bool ok) => Assert.Equal(ok, RichPresenceService.IsValidApplicationId(id));
}

public class PresenceFlowTests
{
    private sealed class FakeDiscord : IDiscordIpcClient
    {
        public DiscordConnectionState NextConnect = DiscordConnectionState.Connected;
        public List<JsonObject?> Activities { get; } = [];
        public DiscordConnectionState State { get; private set; }
        public string? LastError => null;

        public Task<DiscordConnectionState> ConnectAsync(string applicationId, CancellationToken ct = default) =>
            Task.FromResult(State = NextConnect);

        public Task SetActivityAsync(JsonObject? activity, CancellationToken ct = default)
        {
            Activities.Add(activity);
            return Task.CompletedTask;
        }

        public void Disconnect() => State = DiscordConnectionState.Disconnected;
        public void Dispose() { }
    }

    private sealed class FakeSession : IRobloxSessionService
    {
        public bool IsActive => Current is not null;
        public RobloxActivity? Current { get; set; }
        public event EventHandler? Changed;
        public void Track(int processId, DateTime launchedAtUtc) { }
        public Task WaitForEndAsync() => Task.CompletedTask;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeExperiences : IExperienceInfoService
    {
        public Task<ExperienceInfo?> LookupAsync(long placeId, CancellationToken ct = default) =>
            Task.FromResult<ExperienceInfo?>(new ExperienceInfo(placeId, 99, "Test Game", "Maker", 1));
        public Task<ExperienceInfo?> LookupUniverseAsync(long universeId, long placeId, CancellationToken ct = default) => LookupAsync(placeId, ct);
        public Task<string?> GetIconUrlAsync(long universeId, CancellationToken ct = default) => Task.FromResult<string?>("https://icon");
        public Task<RobloxUser?> GetUserAsync(long userId, CancellationToken ct = default) =>
            Task.FromResult<RobloxUser?>(new RobloxUser(userId, "tester", "Tester", "https://headshot"));
    }

    private static (RichPresenceService Service, FakeDiscord Discord, FakeSession Session, MemorySettings Settings) Create(string? appId = "1234567890123456789")
    {
        var discord = new FakeDiscord();
        var session = new FakeSession();
        var settings = new MemorySettings();
        settings.Current.Discord.ApplicationIdOverride = appId;
        var service = new RichPresenceService(discord, session, new FakeExperiences(), settings, NullLogger<RichPresenceService>.Instance);
        return (service, discord, session, settings);
    }

    [Fact]
    public async Task Shows_menu_then_game_then_clears()
    {
        var (service, discord, session, _) = Create();

        session.Current = new RobloxActivity(RobloxActivityKind.Menu, DateTime.UtcNow);
        await service.RefreshAsync();
        Assert.Equal(PresenceStatus.Showing, service.Status);
        Assert.Equal("Browsing Roblox", (string?)discord.Activities[^1]!["state"]);

        session.Current = new RobloxActivity(RobloxActivityKind.InGame, DateTime.UtcNow, 1818, 99);
        await service.RefreshAsync();
        Assert.Equal("Test Game", (string?)discord.Activities[^1]!["details"]);

        await service.RefreshAsync();              // unchanged → not re-sent
        Assert.Equal(2, discord.Activities.Count);

        session.Current = null;
        await service.RefreshAsync();
        Assert.Null(discord.Activities[^1]);       // cleared
        Assert.Equal(DiscordConnectionState.Disconnected, discord.State);
        Assert.Equal(PresenceStatus.Waiting, service.Status);
    }

    [Fact]
    public async Task Reports_missing_setup_and_discord_not_running()
    {
        var (noId, _, session1, _) = Create(appId: null);
        session1.Current = new RobloxActivity(RobloxActivityKind.Menu, DateTime.UtcNow);
        await noId.RefreshAsync();
        if (string.IsNullOrEmpty(Nexstrap.Core.AppInfo.DiscordApplicationId))
            Assert.Equal(PresenceStatus.NeedsSetup, noId.Status);
        Assert.False(noId.KeepsRunningDuringSession && string.IsNullOrEmpty(Nexstrap.Core.AppInfo.DiscordApplicationId));

        var (service, discord, session, _) = Create();
        discord.NextConnect = DiscordConnectionState.DiscordNotRunning;
        session.Current = new RobloxActivity(RobloxActivityKind.Menu, DateTime.UtcNow);
        await service.RefreshAsync();
        Assert.Equal(PresenceStatus.DiscordNotRunning, service.Status);
        Assert.Empty(discord.Activities);
    }

    [Fact]
    public async Task Turning_off_clears_and_preview_works_without_roblox()
    {
        var (service, discord, _, settings) = Create();
        await service.SetPreviewAsync(true);
        Assert.Equal(PresenceStatus.Showing, service.Status);
        Assert.Equal("Inside Nexstrap", (string?)discord.Activities[^1]!["details"]);

        await service.SetPreviewAsync(false);
        Assert.Null(discord.Activities[^1]);

        settings.Current.Discord.RichPresence = false;
        await service.RefreshAsync();
        Assert.Equal(PresenceStatus.Off, service.Status);
    }
}

public class DiscordIpcTests
{
    /// <summary>Speaks just enough of Discord's IPC protocol to test the client.</summary>
    private static async Task<List<JsonNode?>> FakeDiscordAsync(string pipeName, bool accept, int frames)
    {
        var received = new List<JsonNode?>();
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync();

        for (var i = 0; i < frames; i++)
        {
            var header = new byte[8];
            await server.ReadExactlyAsync(header);
            var body = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4))];
            await server.ReadExactlyAsync(body);
            var json = JsonNode.Parse(body);
            received.Add(json);

            var (op, reply) = i == 0
                ? accept ? (1, """{"cmd":"DISPATCH","evt":"READY","data":{"v":1}}""") : (2, """{"code":4000,"message":"Invalid Client ID"}""")
                : (1, $$"""{"cmd":"SET_ACTIVITY","data":{},"evt":null,"nonce":"{{json!["nonce"]}}"}""");
            var bytes = Encoding.UTF8.GetBytes(reply);
            var frame = new byte[8 + bytes.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, op);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), bytes.Length);
            bytes.CopyTo(frame, 8);
            await server.WriteAsync(frame);
            if (!accept) break;
        }
        return received;
    }

    [Fact]
    public async Task Handshakes_and_sets_activity()
    {
        var prefix = "nexstrap-test-" + Guid.NewGuid().ToString("N") + "-";
        var server = FakeDiscordAsync(prefix + "0", accept: true, frames: 2);
        using var client = new DiscordIpcClient(NullLogger<DiscordIpcClient>.Instance, prefix);

        Assert.Equal(DiscordConnectionState.Connected, await client.ConnectAsync("1234567890123456789"));
        await client.SetActivityAsync(RichPresenceService.BuildMenu(DateTime.UtcNow));

        var frames = await server;
        Assert.Equal("1234567890123456789", (string?)frames[0]!["client_id"]);
        Assert.Equal("SET_ACTIVITY", (string?)frames[1]!["cmd"]);
        Assert.Equal("Browsing Roblox", (string?)frames[1]!["args"]!["activity"]!["state"]);
        Assert.Equal(Environment.ProcessId, (int?)frames[1]!["args"]!["pid"]);
    }

    [Fact]
    public async Task Reports_rejected_application_id()
    {
        var prefix = "nexstrap-test-" + Guid.NewGuid().ToString("N") + "-";
        var server = FakeDiscordAsync(prefix + "0", accept: false, frames: 1);
        using var client = new DiscordIpcClient(NullLogger<DiscordIpcClient>.Instance, prefix);

        Assert.Equal(DiscordConnectionState.Rejected, await client.ConnectAsync("1234567890123456789"));
        Assert.Equal("Invalid Client ID", client.LastError);
        await server;
    }

    [Fact]
    public async Task Reports_discord_not_running()
    {
        using var client = new DiscordIpcClient(NullLogger<DiscordIpcClient>.Instance, "nexstrap-test-nobody-" + Guid.NewGuid().ToString("N") + "-");
        Assert.Equal(DiscordConnectionState.DiscordNotRunning, await client.ConnectAsync("1234567890123456789"));
    }
}

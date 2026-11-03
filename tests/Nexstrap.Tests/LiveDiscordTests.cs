using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Services.Discord;

namespace Nexstrap.Tests;

/// <summary>
/// Talks to the real Discord desktop app. Opt-in: set NEXSTRAP_LIVE_TESTS=1.
/// Uses a made-up application ID, so Discord rejects the handshake and nothing
/// ever appears on the profile; it proves the pipe protocol against real Discord.
/// </summary>
public class LiveDiscordTests
{
    [Fact]
    public async Task Real_discord_answers_the_handshake()
    {
        if (Environment.GetEnvironmentVariable("NEXSTRAP_LIVE_TESTS") != "1") return;

        using var client = new DiscordIpcClient(NullLogger<DiscordIpcClient>.Instance);
        var state = await client.ConnectAsync("1000000000000000001");

        Console.WriteLine($"LIVE DISCORD: {state} ({client.LastError})");
        Assert.True(state is DiscordConnectionState.Rejected or DiscordConnectionState.Connected,
            $"Expected Discord to answer, got {state}: {client.LastError}");
    }

    /// <summary>Handshake only (no SET_ACTIVITY), so the profile is never changed.</summary>
    [Fact]
    public async Task Real_discord_accepts_the_nexstrap_application()
    {
        if (Environment.GetEnvironmentVariable("NEXSTRAP_LIVE_TESTS") != "1") return;

        using var client = new DiscordIpcClient(NullLogger<DiscordIpcClient>.Instance);
        var state = await client.ConnectAsync(Nexstrap.Core.AppInfo.DiscordApplicationId);

        Console.WriteLine($"LIVE NEXSTRAP APP: {state} ({client.LastError})");
        Assert.Equal(DiscordConnectionState.Connected, state);
    }
}

using Nexstrap.Core;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Nexstrap.Services.Roblox;

/// <summary>One public server. <see cref="Ping"/> is the average reported by its players, not your own ping.</summary>
public sealed record GameServer(string Id, int Playing, int MaxPlayers, double Fps, int? Ping)
{
    public int FreeSlots => Math.Max(0, MaxPlayers - Playing);
    public bool IsFull => FreeSlots == 0;
    public double Fill => MaxPlayers <= 0 ? 0 : Math.Clamp((double)Playing / MaxPlayers, 0, 1);
}

public sealed record ServerPage(IReadOnlyList<GameServer> Servers, string? NextCursor);

public sealed class ServerBrowserException(string message, Exception? inner = null) : Exception(message, inner);

public interface IServerBrowserService
{
    Task<ServerPage> GetServersAsync(long placeId, bool mostPlayersFirst, bool excludeFull, string? cursor = null, CancellationToken ct = default);
}

/// <summary>
/// Roblox's public server list. It needs no sign-in, so no account data is sent.
/// It doesn't include regions: finding those needs an authenticated join request, which Nexstrap never makes.
/// </summary>
public sealed class ServerBrowserService(HttpClient http, ILogger<ServerBrowserService> logger) : IServerBrowserService
{
    private sealed record Response(
        [property: JsonPropertyName("data")] List<Entry>? Data,
        [property: JsonPropertyName("nextPageCursor")] string? NextPageCursor);

    private sealed record Entry(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("maxPlayers")] int MaxPlayers,
        [property: JsonPropertyName("playing")] int Playing,
        [property: JsonPropertyName("fps")] double Fps,
        [property: JsonPropertyName("ping")] int? Ping);

    public async Task<ServerPage> GetServersAsync(long placeId, bool mostPlayersFirst, bool excludeFull, string? cursor = null, CancellationToken ct = default)
    {
        var url = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder={(mostPlayersFirst ? "Desc" : "Asc")}" +
                  $"&excludeFullGames={(excludeFull ? "true" : "false")}&limit=100";
        if (!string.IsNullOrEmpty(cursor)) url += "&cursor=" + Uri.EscapeDataString(cursor);

        try
        {
            using var response = await http.GetAsync(url, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new ServerBrowserException(Loc.Get("Servers.RobloxIsLimitingServerRequestsWait"));
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
                throw new ServerBrowserException(Loc.Get("Servers.RobloxDoesntListServersForThat"));
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<Response>(ct);
            var servers = (body?.Data ?? [])
                .Where(e => Guid.TryParse(e.Id, out _))
                .Select(e => new GameServer(e.Id!, e.Playing, e.MaxPlayers, e.Fps, e.Ping))
                .ToList();
            return new ServerPage(servers, string.IsNullOrEmpty(body?.NextPageCursor) ? null : body.NextPageCursor);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Server list failed for place {Place}", placeId);
            throw new ServerBrowserException(Loc.Get("Servers.CouldntReachRobloxCheckYourConnection"), ex);
        }
    }
}

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Nexstrap.Services.Roblox;

public sealed record ExperienceInfo(long PlaceId, long UniverseId, string Name, string CreatorName, long Playing);

/// <summary>Public profile of the signed-in player, as shown on roblox.com.</summary>
public sealed record RobloxUser(long Id, string Name, string DisplayName, string? HeadshotUrl);

public interface IExperienceInfoService
{
    /// <summary>Public metadata for a place; null if the place doesn't exist. Throws HttpRequestException when offline.</summary>
    Task<ExperienceInfo?> LookupAsync(long placeId, CancellationToken ct = default);

    /// <summary>Same, when the universe is already known (e.g. from Roblox's log).</summary>
    Task<ExperienceInfo?> LookupUniverseAsync(long universeId, long placeId, CancellationToken ct = default);

    /// <summary>HTTPS URL of the experience's 512×512 icon, or null.</summary>
    Task<string?> GetIconUrlAsync(long universeId, CancellationToken ct = default);

    /// <summary>Public username, display name and avatar headshot; null if the user doesn't exist.</summary>
    Task<RobloxUser?> GetUserAsync(long userId, CancellationToken ct = default);
}

/// <summary>Uses Roblox's public, unauthenticated web APIs. No account data is sent.</summary>
public sealed class ExperienceInfoService(HttpClient http, ILogger<ExperienceInfoService> logger) : IExperienceInfoService
{
    private sealed record UniverseResponse([property: JsonPropertyName("universeId")] long? UniverseId);
    private sealed record GamesResponse([property: JsonPropertyName("data")] List<Game> Data);
    private sealed record Game(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("playing")] long Playing,
        [property: JsonPropertyName("creator")] Creator? Creator);
    private sealed record Creator([property: JsonPropertyName("name")] string Name);
    private sealed record ThumbnailResponse([property: JsonPropertyName("data")] List<Thumbnail> Data);
    private sealed record Thumbnail(
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("imageUrl")] string? ImageUrl);

    private sealed record UserResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("displayName")] string DisplayName);

    // Presence refreshes ask repeatedly for the same few games.
    private readonly Dictionary<long, ExperienceInfo> _byUniverse = [];
    private readonly Dictionary<long, string?> _icons = [];
    private readonly Dictionary<long, RobloxUser> _users = [];

    public async Task<RobloxUser?> GetUserAsync(long userId, CancellationToken ct = default)
    {
        lock (_users)
            if (_users.TryGetValue(userId, out var cached)) return cached;

        UserResponse? profile;
        try
        {
            profile = await http.GetFromJsonAsync<UserResponse>($"https://users.roblox.com/v1/users/{userId}", ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        if (profile is null) return null;

        var thumbs = await http.GetFromJsonAsync<ThumbnailResponse>(
            $"https://thumbnails.roblox.com/v1/users/avatar-headshot?userIds={userId}&size=150x150&format=Png&isCircular=false", ct);
        var thumb = thumbs?.Data.FirstOrDefault();
        var headshot = thumb is { State: "Completed", ImageUrl: { } u } && u.StartsWith("https://", StringComparison.Ordinal) ? u : null;

        var user = new RobloxUser(profile.Id, profile.Name, profile.DisplayName, headshot);
        lock (_users) _users[userId] = user;
        return user;
    }

    public async Task<ExperienceInfo?> LookupAsync(long placeId, CancellationToken ct = default)
    {
        var universe = await http.GetFromJsonAsync<UniverseResponse>($"https://apis.roblox.com/universes/v1/places/{placeId}/universe", ct);
        return universe?.UniverseId is { } universeId ? await LookupUniverseAsync(universeId, placeId, ct) : null;
    }

    public async Task<ExperienceInfo?> LookupUniverseAsync(long universeId, long placeId, CancellationToken ct = default)
    {
        lock (_byUniverse)
            if (_byUniverse.TryGetValue(universeId, out var cached)) return cached with { PlaceId = placeId };

        var games = await http.GetFromJsonAsync<GamesResponse>($"https://games.roblox.com/v1/games?universeIds={universeId}", ct);
        var game = games?.Data.FirstOrDefault();
        if (game is null) return null;

        logger.LogDebug("Universe {Universe} is {Name}", universeId, game.Name);
        var info = new ExperienceInfo(placeId, universeId, game.Name, game.Creator?.Name ?? "", game.Playing);
        lock (_byUniverse) _byUniverse[universeId] = info;
        return info;
    }

    public async Task<string?> GetIconUrlAsync(long universeId, CancellationToken ct = default)
    {
        lock (_icons)
            if (_icons.TryGetValue(universeId, out var cached)) return cached;

        var response = await http.GetFromJsonAsync<ThumbnailResponse>(
            $"https://thumbnails.roblox.com/v1/games/icons?universeIds={universeId}&returnPolicy=PlaceHolder&size=512x512&format=Png&isCircular=false", ct);
        var thumb = response?.Data.FirstOrDefault();
        var url = thumb is { State: "Completed", ImageUrl: { } u } && u.StartsWith("https://", StringComparison.Ordinal) ? u : null;
        lock (_icons) _icons[universeId] = url;
        return url;
    }
}

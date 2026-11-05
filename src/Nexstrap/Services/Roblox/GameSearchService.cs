using System.Net.Http;
using System.Text.Json;

namespace Nexstrap.Services.Roblox;

public sealed record GameSearchResult(long PlaceId, long UniverseId, string Name, string CreatorName, long Playing);

public interface IGameSearchService
{
    /// <summary>Finds experiences by name. Throws HttpRequestException when Roblox can't be reached or refuses.</summary>
    Task<IReadOnlyList<GameSearchResult>> SearchAsync(string query, CancellationToken ct = default);
}

/// <summary>
/// Uses the search behind roblox.com's Discover page, without signing in. Roblox doesn't document it,
/// so anything unexpected in the reply is skipped rather than treated as an error.
/// </summary>
public sealed class GameSearchService(HttpClient http) : IGameSearchService
{
    internal const int MaxResults = 20;

    public async Task<IReadOnlyList<GameSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var url = "https://apis.roblox.com/search-api/omni-search?pageType=all"
                  + $"&searchQuery={Uri.EscapeDataString(query.Trim())}&sessionId={Guid.NewGuid():D}";
        return Parse(await http.GetStringAsync(url, ct));
    }

    internal static IReadOnlyList<GameSearchResult> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var results = new List<GameSearchResult>();

        if (!document.RootElement.TryGetProperty("searchResults", out var groups) || groups.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("contents", out var contents) || contents.ValueKind != JsonValueKind.Array) continue;

            foreach (var game in contents.EnumerateArray())
            {
                var place = Long(game, "rootPlaceId");
                var name = Text(game, "name");
                if (place <= 0 || string.IsNullOrEmpty(name)) continue;
                if (game.TryGetProperty("isSponsored", out var sponsored) && sponsored.ValueKind == JsonValueKind.True) continue;
                if (results.Any(r => r.PlaceId == place)) continue;

                results.Add(new GameSearchResult(place, Long(game, "universeId"), name, Text(game, "creatorName") ?? "", Long(game, "playerCount")));
                if (results.Count == MaxResults) return results;
            }
        }
        return results;
    }

    private static long Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

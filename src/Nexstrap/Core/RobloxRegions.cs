namespace Nexstrap.Core;

/// <summary>
/// Where Roblox's game server addresses are, by the third number of 128.116.x.x.
/// From a community list on the Roblox developer forum (July 2024), rounded to the nearest big city.
/// Roblox doesn't publish this, so it can be wrong or out of date. Unlisted addresses are simply unknown.
/// </summary>
public static class RobloxRegions
{
    private const string Prefix = "128.116.";

    private static readonly Dictionary<int, string> ByBlock = new()
    {
        [115] = "Seattle, USA",
        [1] = "Los Angeles, USA", [63] = "Los Angeles, USA", [116] = "Los Angeles, USA",
        [95] = "Dallas, USA",
        [48] = "Chicago, USA", [101] = "Chicago, USA",
        [22] = "Atlanta, USA", [99] = "Atlanta, USA",
        [45] = "Miami, USA", [127] = "Miami, USA",
        [53] = "Ashburn, USA", [102] = "Ashburn, USA",
        [32] = "New York, USA",
        [33] = "London, UK", [119] = "London, UK",
        [21] = "Amsterdam, Netherlands",
        [4] = "Paris, France", [122] = "Paris, France",
        [5] = "Frankfurt, Germany", [44] = "Frankfurt, Germany", [123] = "Frankfurt, Germany",
        [31] = "Warsaw, Poland", [124] = "Warsaw, Poland",
        [104] = "Mumbai, India",
        [55] = "Tokyo, Japan", [120] = "Tokyo, Japan",
        [50] = "Singapore", [97] = "Singapore",
        [51] = "Sydney, Australia"
    };

    public const string Frankfurt = "Frankfurt, Germany";

    public static IReadOnlyList<string> All { get; } = ByBlock.Values.Distinct().Order(StringComparer.Ordinal).ToList();

    /// <summary>The city for a server address, or null if it isn't on the list.</summary>
    public static string? Find(string? address)
    {
        if (address is null || !address.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var block = address[Prefix.Length..].Split('.')[0];
        return int.TryParse(block, out var number) && ByBlock.TryGetValue(number, out var city) ? city : null;
    }
}

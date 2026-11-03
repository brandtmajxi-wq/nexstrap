using System.Text.RegularExpressions;
using System.Web;

namespace Nexstrap.Core;

/// <summary>Parsing and building of the links Roblox's client understands.</summary>
public static partial class RobloxLinks
{
    private const int MaxUriLength = 4096;

    /// <summary>
    /// Accepts a place ID ("1818"), an experience page
    /// ("https://www.roblox.com/games/1818/Crossroads") or a start link ("…/games/start?placeId=1818").
    /// </summary>
    public static bool TryParsePlaceId(string? input, out long placeId)
    {
        placeId = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var text = input.Trim();

        if (long.TryParse(text, out placeId)) return placeId > 0;

        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        if (!(uri.Host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase))) return false;

        var query = HttpUtility.ParseQueryString(uri.Query);
        if (long.TryParse(query["placeId"], out placeId) && placeId > 0) return true;

        var match = GamePath().Match(uri.AbsolutePath);
        return match.Success && long.TryParse(match.Groups[1].Value, out placeId) && placeId > 0;
    }

    /// <summary>Roblox's documented deep link for joining an experience.</summary>
    public static string PlaceLaunchUri(long placeId) => $"roblox://experiences/start?placeId={placeId}";

    /// <summary>
    /// True for roblox: / roblox-player: URIs Windows hands to the registered handler.
    /// Rejects anything with whitespace or quotes so it can't smuggle extra arguments.
    /// </summary>
    public static bool IsLaunchUri(string? arg) =>
        arg is { Length: > 7 and <= MaxUriLength }
        && (arg.StartsWith("roblox:", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("roblox-player:", StringComparison.OrdinalIgnoreCase))
        && !arg.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' || char.IsControl(c));

    /// <summary>Text safe to log: website launch links carry a one-time auth ticket.</summary>
    public static string Redact(string uri)
    {
        var colon = uri.IndexOf(':');
        if (colon < 0) return "(invalid)";
        var scheme = uri[..colon];
        var place = PlaceInUri().Match(uri);
        return place.Success ? $"{scheme}: (placeId {place.Groups[1].Value})" : $"{scheme}: (details hidden)";
    }

    [GeneratedRegex(@"^/games/(\d+)(/|$)", RegexOptions.IgnoreCase)]
    private static partial Regex GamePath();

    [GeneratedRegex(@"placeId(?:=|%3D)(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceInUri();
}

using System.Globalization;
using System.Text;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Core;

/// <summary>
/// Play history as CSV for Excel or Google Sheets. It uses the list separator Windows uses for the region
/// (a semicolon in much of Europe), so Excel splits the columns, and ISO dates, which every spreadsheet reads.
/// </summary>
public static class HistoryExport
{
    /// <summary>One row per saved session (the newest 500), oldest first.</summary>
    public static string Sessions(IEnumerable<PlaySession> sessions, IReadOnlyDictionary<long, string> names, CultureInfo culture)
    {
        var sep = Separator(culture);
        var csv = new StringBuilder();
        Row(csv, sep, Loc.Get("Export.Date"), Loc.Get("Export.Start"), Loc.Get("Export.Game"), Loc.Get("Export.PlaceId"), Loc.Get("Export.Minutes"));
        foreach (var s in sessions.OrderBy(s => s.StartedUtc))
        {
            var start = DateTime.SpecifyKind(s.StartedUtc, DateTimeKind.Utc).ToLocalTime();
            Row(csv, sep,
                start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                start.ToString("HH:mm", CultureInfo.InvariantCulture),
                names.TryGetValue(s.PlaceId, out var name) && name.Length > 0 ? name : "",
                s.PlaceId.ToString(CultureInfo.InvariantCulture),
                Math.Round(s.Seconds / 60, 1).ToString("0.0", culture));
        }
        return csv.ToString();
    }

    /// <summary>One row per place, with totals kept for all time (not only the newest 500 sessions). Most played first.</summary>
    public static string Games(IEnumerable<GamePlayStats> games, CultureInfo culture)
    {
        var sep = Separator(culture);
        var csv = new StringBuilder();
        Row(csv, sep, Loc.Get("Export.Game"), Loc.Get("Export.PlaceId"), Loc.Get("Export.Hours"), Loc.Get("Export.Sessions"), Loc.Get("Export.LastPlayed"));
        foreach (var g in games.OrderByDescending(g => g.Seconds))
            Row(csv, sep,
                g.Name,
                g.PlaceId.ToString(CultureInfo.InvariantCulture),
                Math.Round(g.Seconds / 3600, 2).ToString("0.00", culture),
                g.Sessions.ToString(CultureInfo.InvariantCulture),
                g.LastPlayedUtc == default ? "" : DateTime.SpecifyKind(g.LastPlayedUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        return csv.ToString();
    }

    /// <summary>Excel only splits on the region's separator. If that's also the decimal mark, fall back to a semicolon.</summary>
    internal static string Separator(CultureInfo culture)
    {
        var sep = culture.TextInfo.ListSeparator;
        return string.IsNullOrEmpty(sep) || sep == culture.NumberFormat.NumberDecimalSeparator ? ";" : sep;
    }

    /// <summary>Quotes a value that contains the separator, a quote or a line break, and stops Excel treating =, +, - or @ as a formula.</summary>
    internal static string Field(string value, string sep)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0]) && !double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return value.Contains(sep) || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static void Row(StringBuilder csv, string sep, params string[] fields) =>
        csv.Append(string.Join(sep, fields.Select(f => Field(f, sep)))).Append("\r\n");
}

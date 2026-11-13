namespace Nexstrap.Core;

/// <summary>The weather drawn over the Moscow welcome screen. Random is the setting, never a scene.</summary>
public enum Weather { Random, Clear, PartlyCloudy, Cloudy, Rain, Storm, Snow, Fog, Off, HeavyRain, Wind, Blizzard }

/// <summary>What the animated sky layer draws: the weather, which picture (time of day) it sits on, and whether a jet flies over.</summary>
public sealed record SkyScene(Weather Weather, DayPart Part, bool Jets = false);

/// <summary>
/// Made-up weather for the Moscow screen. It isn't a forecast and reads nothing online:
/// "Random" picks one when Nexstrap starts, weighted by the month so July doesn't get snow.
/// </summary>
public static class MoscowWeather
{
    public static readonly Weather[] Scenes =
    [
        Weather.Clear, Weather.PartlyCloudy, Weather.Cloudy, Weather.Rain, Weather.HeavyRain, Weather.Storm,
        Weather.Wind, Weather.Snow, Weather.Blizzard, Weather.Fog
    ];

    private static Weather? _rolled;

    /// <summary>The weather to draw for a setting: a fixed one, nothing for Off, or this run's random pick.</summary>
    public static Weather Resolve(Weather setting, DateTime now)
    {
        if (setting != Weather.Random) return setting;
        return _rolled ??= Pick(now.Month, Random.Shared.NextDouble());
    }

    /// <summary>Relative chances for each weather in a month (1–12), in <see cref="Scenes"/> order.</summary>
    internal static int[] Weights(int month) => month switch
    {
        // Clear, partly, cloudy, rain, heavy rain, storm, wind, snow, blizzard, fog
        12 or 1 or 2 => [16, 16, 20, 0, 0, 0, 8, 24, 8, 8],
        3 or 11 => [16, 18, 20, 8, 0, 0, 10, 14, 4, 10],
        4 or 10 => [20, 22, 18, 16, 6, 0, 10, 2, 0, 6],
        5 or 9 => [24, 24, 12, 16, 6, 6, 6, 0, 0, 6],
        _ => [28, 26, 8, 12, 8, 10, 4, 0, 0, 4]
    };

    /// <summary>Picks a weather for a month from a roll between 0 and 1.</summary>
    internal static Weather Pick(int month, double roll)
    {
        var weights = Weights(month);
        var target = Math.Clamp(roll, 0, 0.999999) * weights.Sum();
        for (var i = 0; i < weights.Length; i++)
        {
            if (target < weights[i]) return Scenes[i];
            target -= weights[i];
        }
        return Weather.Clear;
    }

    /// <summary>The jet flyovers are dev only for now, like winter and Berlin.</summary>
    public static bool ExtrasAvailable => AppInfo.IsDev;

    /// <summary>The Russian word shown next to Moscow's clock.</summary>
    public static string RussianName(Weather weather, DayPart part) => weather switch
    {
        Weather.Clear => part == DayPart.Night ? "Ясная ночь" : "Солнечно",
        Weather.PartlyCloudy => "Переменная облачность",
        Weather.Cloudy => "Облачно",
        Weather.Rain => "Дождь",
        Weather.HeavyRain => "Ливень",
        Weather.Storm => "Гроза",
        Weather.Wind => "Сильный ветер",
        Weather.Snow => "Снег",
        Weather.Blizzard => "Метель",
        Weather.Fog => "Туман",
        _ => ""
    };
}

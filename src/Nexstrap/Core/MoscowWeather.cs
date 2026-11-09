namespace Nexstrap.Core;

/// <summary>The weather drawn over the Moscow welcome screen. Random is the setting, never a scene.</summary>
public enum Weather { Random, Clear, PartlyCloudy, Cloudy, Rain, Storm, Snow, Fog, Off }

/// <summary>What the animated sky layer draws: the weather and which picture (time of day) it sits on.</summary>
public sealed record SkyScene(Weather Weather, DayPart Part);

/// <summary>
/// Made-up weather for the Moscow screen. It isn't a forecast and reads nothing online:
/// "Random" picks one when Nexstrap starts, weighted by the month so July doesn't get snow.
/// </summary>
public static class MoscowWeather
{
    public static readonly Weather[] Scenes =
        [Weather.Clear, Weather.PartlyCloudy, Weather.Cloudy, Weather.Rain, Weather.Storm, Weather.Snow, Weather.Fog];

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
        // Clear, partly, cloudy, rain, storm, snow, fog
        12 or 1 or 2 => [18, 18, 22, 0, 0, 32, 10],
        3 or 11 => [18, 20, 22, 10, 0, 20, 10],
        4 or 10 => [22, 24, 20, 20, 0, 4, 10],
        5 or 9 => [26, 26, 14, 20, 6, 0, 8],
        _ => [30, 28, 10, 16, 12, 0, 4]
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

    /// <summary>The Russian word shown next to Moscow's clock.</summary>
    public static string RussianName(Weather weather, DayPart part) => weather switch
    {
        Weather.Clear => part == DayPart.Night ? "Ясная ночь" : "Солнечно",
        Weather.PartlyCloudy => "Переменная облачность",
        Weather.Cloudy => "Облачно",
        Weather.Rain => "Дождь",
        Weather.Storm => "Гроза",
        Weather.Snow => "Снег",
        Weather.Fog => "Туман",
        _ => ""
    };
}

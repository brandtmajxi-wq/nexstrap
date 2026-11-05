using Nexstrap.Models;

namespace Nexstrap.Core;

public enum WinterMode { Automatic, Always, Off }

public enum Holiday { None, Christmas, NewYear }

/// <summary>Decides when the winter theme and holiday greetings apply, from the PC's date.</summary>
public static class WinterSeason
{
    /// <summary>The winter theme is still dev only: public builds have no pictures for it.</summary>
    public static bool Available => AppInfo.IsDev;

    /// <summary>What the app should show right now.</summary>
    public static (bool Winter, Holiday Holiday) Resolve(WinterSettings settings, DateTime now, bool available)
    {
        if (!available) return (false, Holiday.None);
        if (settings.ChristmasMode) return (true, Holiday.Christmas);
        return (IsActive(settings.Mode, now), settings.Mode == WinterMode.Off ? Holiday.None : HolidayOn(now));
    }

    /// <summary>Automatic runs from 1 December to 6 January.</summary>
    public static bool IsActive(WinterMode mode, DateTime date) => mode switch
    {
        WinterMode.Always => true,
        WinterMode.Off => false,
        _ => date.Month == 12 || (date.Month == 1 && date.Day <= 6)
    };

    public static Holiday HolidayOn(DateTime date) => (date.Month, date.Day) switch
    {
        (12, >= 24 and <= 26) => Holiday.Christmas,
        (12, 31) or (1, 1) => Holiday.NewYear,
        _ => Holiday.None
    };
}

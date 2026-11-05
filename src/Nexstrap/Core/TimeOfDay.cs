namespace Nexstrap.Core;

public enum DayPart { Night, Morning, Afternoon, Evening }

/// <summary>Splits the day by the PC's clock, for greetings and the Welcome page's scene.</summary>
public static class TimeOfDay
{
    public static DayPart Part(int hour) => hour switch
    {
        < 5 => DayPart.Night,
        < 12 => DayPart.Morning,
        < 18 => DayPart.Afternoon,
        _ => DayPart.Evening
    };
}

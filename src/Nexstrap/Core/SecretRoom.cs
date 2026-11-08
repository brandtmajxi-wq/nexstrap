using System.Security.Cryptography;
using System.Text;

namespace Nexstrap.Core;

/// <summary>
/// The password for the Moscow page. Only a SHA-256 fingerprint is kept, so the source on GitHub doesn't show the password.
/// It keeps a surprise hidden; it isn't protecting anything private.
/// </summary>
public static class SecretRoom
{
    private const string Salt = "nexstrap-moscow|";

    internal const string PasswordHash = "a749e63bff8c268bf4828bf874bedcd4c9cfe7e34852a7b34059adc5b2164430";

    internal static string Fingerprint(string password) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + password.Trim()))).ToLowerInvariant();

    public static bool Check(string? password) =>
        !string.IsNullOrWhiteSpace(password) && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Fingerprint(password)), Encoding.ASCII.GetBytes(PasswordHash));

    /// <summary>Moscow is UTC+3 all year.</summary>
    public static DateTime MoscowTime(DateTime utcNow)
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(utcNow, TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time")); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return utcNow.AddHours(3); }
    }

    public static string Greeting(DayPart part, string? name)
    {
        var hello = part switch
        {
            DayPart.Morning => "Доброе утро",
            DayPart.Afternoon => "Добрый день",
            DayPart.Evening => "Добрый вечер",
            _ => "Доброй ночи"
        };
        return string.IsNullOrWhiteSpace(name) ? hello + "!" : $"{hello}, {name}!";
    }
}

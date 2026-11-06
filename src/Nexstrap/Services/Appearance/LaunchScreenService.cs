using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Configuration;
using Nexstrap.Views;

namespace Nexstrap.Services.Appearance;

public sealed record LaunchScreenPreset(string Name, string BackgroundColor, string AccentColor, LaunchAnimation Animation);

public interface ILaunchScreen
{
    /// <summary>Shows the screen until Roblox's window opens, it exits, or a timeout passes.</summary>
    void ShowUntilReady(int processId);

    void Preview();

    /// <summary>Copies an image for the screen. Returns an error message, or null on success.</summary>
    string? SetImage(string sourcePath);

    /// <summary>Copies a logo for the screen. Returns an error message, or null on success.</summary>
    string? SetLogo(string sourcePath);
}

public sealed class LaunchScreenService(ISettingsService settings, IThemeService theme, ILogger<LaunchScreenService> logger) : ILaunchScreen
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PreviewLength = TimeSpan.FromSeconds(4);

    public static IReadOnlyList<LaunchScreenPreset> Presets { get; } =
    [
        new("Nexstrap", "#0E0F13", "#7C6CFF", LaunchAnimation.Spinner),
        new("Midnight", "#0B1220", "#38BDF8", LaunchAnimation.Bar),
        new("Ember", "#1A0F0A", "#F97316", LaunchAnimation.Pulse),
        new("Daylight", "#F4F5F8", "#6366F1", LaunchAnimation.Bar),
        new("Neon", "#0A0A14", "#22D3EE", LaunchAnimation.Dots),
        new("Minimal", "#000000", "#FFFFFF", LaunchAnimation.None)
    ];

    private LaunchScreenWindow? _window;

    private LaunchScreenSettings Options => settings.Current.LaunchScreen;

    /// <summary>Closes once Roblox shows a window or exits; never stays up past the timeout.</summary>
    internal static bool ShouldClose(bool hasWindow, bool exited, TimeSpan elapsed, TimeSpan timeout, TimeSpan minimum = default) =>
        exited || elapsed >= timeout || (hasWindow && elapsed >= minimum);

    public void ShowUntilReady(int processId)
    {
        if (!Options.Enabled) return;
        Open();
        _ = WaitAndCloseAsync(processId);
    }

    public void Preview()
    {
        Open();
        _ = CloseAfterAsync(PreviewLength);
    }

    public string? SetImage(string sourcePath) => CopyImage(sourcePath, "launch-", path => Options.BackgroundImage = path);

    public string? SetLogo(string sourcePath) => CopyImage(sourcePath, "logo-", path => Options.LogoImage = path);

    private string? CopyImage(string sourcePath, string prefix, Action<string> keep)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AppearanceService.SupportedImageExtensions.Contains(ext))
            return Loc.Get("LaunchScreen.UseAPNGJPGBMPOr");
        if (theme.LoadImage(sourcePath) is null)
            return Loc.Get("Appearance.ThatFileCouldntBeReadAs");

        try
        {
            Directory.CreateDirectory(AppInfo.Paths.Backgrounds);
            string hash;
            using (var stream = File.OpenRead(sourcePath))
                hash = Convert.ToHexString(SHA256.HashData(stream))[..16].ToLowerInvariant();
            var target = Path.Combine(AppInfo.Paths.Backgrounds, prefix + hash + ext);
            if (!File.Exists(target)) File.Copy(sourcePath, target);
            keep(target);
            settings.Save();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Loc.Get("Appearance.NexstrapCouldntCopyThatImage") + ex.Message;
        }
    }

    private void Open()
    {
        _window?.Close();
        var picture = Options.BackgroundImage is { } path ? theme.LoadImage(path) : null;
        var logo = Options.LogoImage is { } logoPath ? theme.LoadImage(logoPath) : null;
        _window = new LaunchScreenWindow(Options, picture, logo);
        _window.Show();
    }

    private void Close()
    {
        _window?.CloseSoftly(Options.Fade);
        _window = null;
    }

    private async Task WaitAndCloseAsync(int processId)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            using var process = Process.GetProcessById(processId);
            while (true)
            {
                await Task.Delay(300);
                process.Refresh();
                if (ShouldClose(!process.HasExited && process.MainWindowHandle != IntPtr.Zero, process.HasExited, clock.Elapsed, Timeout, TimeSpan.FromSeconds(Options.MinimumSeconds))) break;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            logger.LogDebug(ex, "Roblox process went away before the loading screen closed");
        }
        Close();
    }

    private async Task CloseAfterAsync(TimeSpan delay)
    {
        await Task.Delay(delay);
        Close();
    }
}

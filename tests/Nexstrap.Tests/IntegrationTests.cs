using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Models;
using Nexstrap.Services.Appearance;
using Nexstrap.Services.FastFlags;
using Nexstrap.Services.Integrations;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Tests;

internal sealed class ManualSession : IRobloxSessionService
{
    public bool IsActive => Current is not null;
    public RobloxActivity? Current { get; private set; }
    public DateTime? StartedUtc => Current?.SinceUtc;
    public event EventHandler? Changed;
    public void Track(int processId, DateTime launchedAtUtc) => Set(new RobloxActivity(RobloxActivityKind.Starting, launchedAtUtc));
    public Task WaitForEndAsync() => Task.CompletedTask;
    public void Set(RobloxActivity? a) { Current = a; Changed?.Invoke(this, EventArgs.Empty); }
}

public class CompanionAppTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    // A real, harmless classic Win32 program with a window we can politely close.
    // (Windows 11's notepad.exe hands off to the Store app and exits, so it can't be used here.)
    private static readonly string Notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "charmap.exe");
    private const string ProcessName = "charmap";

    [Theory]
    [InlineData("", "Choose")]
    [InlineData("relative.exe", "Choose")]
    [InlineData(@"C:\Windows\win.ini", "Only programs")]
    [InlineData(@"C:\does\not\exist.exe", "no longer exists")]
    public void Rejects_bad_paths(string path, string expected) =>
        Assert.Contains(expected, CompanionAppService.Validate(path));

    [Fact]
    public void Adds_once_with_a_friendly_name_and_persists()
    {
        var settings = new MemorySettings();
        var service = new CompanionAppService(settings, new ManualSession(), NullLogger<CompanionAppService>.Instance);

        Assert.Null(service.Add(Notepad));
        Assert.NotNull(service.Add(Notepad)); // duplicate
        var app = Assert.Single(settings.Current.CompanionApps);
        Assert.False(string.IsNullOrWhiteSpace(app.Name));
        Assert.True(app.Enabled);
        Assert.False(service.NeedsToWatchSession);

        app.CloseWithRoblox = true;
        Assert.True(service.NeedsToWatchSession);
    }

    [Fact]
    public void Starts_with_the_session_and_closes_when_it_ends()
    {
        if (!File.Exists(Notepad)) return;
        var settings = new MemorySettings();
        var session = new ManualSession();
        var service = new CompanionAppService(settings, session, NullLogger<CompanionAppService>.Instance);
        service.Add(Notepad);
        settings.Current.CompanionApps[0].CloseWithRoblox = true;

        var before = Process.GetProcessesByName(ProcessName).Select(p => p.Id).ToHashSet();
        session.Track(1, DateTime.UtcNow);

        Process? started = null;
        for (var i = 0; i < 40 && started is null; i++)
        {
            Thread.Sleep(250);
            started = Process.GetProcessesByName(ProcessName).FirstOrDefault(p => !before.Contains(p.Id) && p.MainWindowHandle != IntPtr.Zero);
        }
        Assert.NotNull(started);

        try
        {
            session.Set(null); // Roblox closed
            Assert.True(started!.WaitForExit(10000), "companion app should close politely");
        }
        finally
        {
            if (!started!.HasExited) started.Kill();
        }
    }

    [Fact]
    public void Disabled_apps_are_not_started()
    {
        var settings = new MemorySettings();
        var session = new ManualSession();
        var service = new CompanionAppService(settings, session, NullLogger<CompanionAppService>.Instance);
        settings.Current.CompanionApps.Add(new CompanionApp { Name = "Gone", Path = @"C:\nope\gone.exe", Enabled = true });
        settings.Current.CompanionApps.Add(new CompanionApp { Name = "Off", Path = Notepad, Enabled = false });

        var before = Process.GetProcessesByName(ProcessName).Length;
        session.Track(1, DateTime.UtcNow); // must not throw for the missing one
        Thread.Sleep(800);
        Assert.Equal(before, Process.GetProcessesByName(ProcessName).Length);
    }
}

public class ConfigTransferTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private (ConfigTransferService Transfer, MemorySettings Settings, AppearanceService Appearance, FastFlagService Flags) Create(string name)
    {
        var settings = new MemorySettings();
        var appearance = new AppearanceService(settings, new FakeThemeService(), NullLogger<AppearanceService>.Instance,
            _dir.File(name + "-themes"), _dir.File(name + "-bg"));
        var flags = new FastFlagService(NullLogger<FastFlagService>.Instance, _dir.File(name + "-flags.json"), _dir.File(name + "-flagbackups"));
        var transfer = new ConfigTransferService(settings, appearance, flags, NullLogger<ConfigTransferService>.Instance)
        {
            BackupDir = _dir.File(name + "-backups")
        };
        return (transfer, settings, appearance, flags);
    }

    [Fact]
    public void Round_trips_a_setup_to_another_pc()
    {
        var (fromTransfer, fromSettings, fromAppearance, fromFlags) = Create("a");
        fromAppearance.SetAccent("#22C55E");
        fromAppearance.SetTheme(AppTheme.Light);
        fromAppearance.SaveProfile("Shared look");
        fromFlags.Set("FIntDebugForceMSAASamples", "4");
        fromFlags.Set("FFlagDebugSkyGray", "True");
        fromFlags.SetEnabled("FFlagDebugSkyGray", false);
        fromSettings.Current.Launcher.CloseAfterLaunch = true;
        fromSettings.Current.Launcher.PreferredInstallation = @"C:\machine\specific";
        fromSettings.Current.Discord.ShowAvatar = false;
        fromSettings.Current.CompanionApps.Add(new CompanionApp { Name = "Missing", Path = @"C:\nope\x.exe" });

        var file = _dir.File("export.json");
        fromTransfer.Export(file);
        var json = File.ReadAllText(file);
        Assert.DoesNotContain("machine\\\\specific", json); // never exported

        var (toTransfer, toSettings, toAppearance, toFlags) = Create("b");
        var result = toTransfer.Import(file);

        Assert.Equal("#22C55E", toSettings.Current.Appearance.AccentColor);
        Assert.Equal(AppTheme.Light, toSettings.Current.Appearance.Theme);
        Assert.True(toSettings.Current.Launcher.CloseAfterLaunch);
        Assert.Null(toSettings.Current.Launcher.PreferredInstallation);
        Assert.False(toSettings.Current.Discord.ShowAvatar);
        Assert.Equal(2, toFlags.Flags.Count);
        Assert.False(toFlags.Flags.Single(f => f.Name == "FFlagDebugSkyGray").Enabled); // disabled state kept
        Assert.Equal("Shared look", Assert.Single(toAppearance.LoadSavedProfiles()).Name);
        Assert.Equal(0, result.CompanionApps);
        Assert.Contains(result.Notes, n => n.Contains("Missing"));
        Assert.Single(Directory.GetFiles(_dir.File("b-backups"), "NexstrapConfig-*.json")); // safety backup
    }

    [Fact]
    public void Rejects_foreign_or_future_files()
    {
        var (transfer, _, _, _) = Create("c");
        File.WriteAllText(_dir.File("other.json"), """{ "FFlagDebugSkyGray": "True" }""");
        Assert.Throws<InvalidDataException>(() => transfer.Import(_dir.File("other.json")));

        File.WriteAllText(_dir.File("future.json"), """{ "Format": "nexstrap-config", "Version": 9 }""");
        Assert.Contains("newer", Assert.Throws<InvalidDataException>(() => transfer.Import(_dir.File("future.json"))).Message);

        File.WriteAllText(_dir.File("broken.json"), "{ not json");
        Assert.Throws<InvalidDataException>(() => transfer.Import(_dir.File("broken.json")));
    }
}

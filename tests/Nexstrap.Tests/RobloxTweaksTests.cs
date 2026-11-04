using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Nexstrap.Models;
using Nexstrap.Services.Maintenance;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Setup;
using Nexstrap.ViewModels.Pages;
using Xunit;

namespace Nexstrap.Tests;

public sealed class RobloxTweaksTests : IDisposable
{
    private const string ScratchRun = @"Software\NexstrapTweaksTests\Run";

    private readonly string _dir = Directory.CreateTempSubdirectory("nexstrap-tweaks-").FullName;

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\NexstrapTweaksTests", throwOnMissingSubKey: false);
    }

    private string Write(string relative, byte[] content)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Png(int width, int height)
    {
        var bytes = new byte[32];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static byte[] Ogg(byte marker) => [.. "OggS"u8.ToArray(), marker];

    private RobloxModService Mods(MemorySettings settings) =>
        new(settings, NullLogger<RobloxModService>.Instance, Path.Combine(_dir, "Mods"));

    private string Build()
    {
        var build = Path.Combine(_dir, "build");
        Write(Path.Combine("build", RobloxModService.DeathSoundTargets[0]), Ogg(1));
        foreach (var cursor in RobloxModService.CursorTargets) Write(Path.Combine("build", cursor), Png(64, 64));
        return build;
    }

    [Theory]
    [InlineData(0, 8, null)]
    [InlineData(8, 8, null)]
    [InlineData(12, 8, null)]
    [InlineData(2, 8, 3L)]
    [InlineData(4, 8, 15L)]
    public void Affinity_mask_uses_the_first_cores_and_ignores_limits_that_restrict_nothing(int limit, int processors, long? expected)
    {
        var mask = RobloxProcessTuner.AffinityMask(limit, processors);
        Assert.Equal(expected, mask is null ? null : (long)mask.Value);
    }

    [Fact]
    public void No_priority_option_can_reach_realtime()
    {
        foreach (var priority in Enum.GetValues<RobloxPriority>())
            Assert.NotEqual(ProcessPriorityClass.RealTime, RobloxProcessTuner.ToPriorityClass(priority));
        Assert.Null(RobloxProcessTuner.ToPriorityClass(RobloxPriority.Automatic));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData(" My Roblox ", "My Roblox")]
    [InlineData("a\r\nb", "ab")]
    public void Window_titles_are_cleaned(string? input, string? expected) =>
        Assert.Equal(expected, RobloxProcessTuner.CleanTitle(input));

    [Fact]
    public void Tune_sets_priority_and_cores_on_a_real_process()
    {
        var settings = new MemorySettings();
        settings.Current.Launcher.RobloxPriority = RobloxPriority.BelowNormal;
        settings.Current.Launcher.RobloxCoreLimit = 2;
        var tuner = new RobloxProcessTuner(settings, NullLogger<RobloxProcessTuner>.Instance);

        using var process = Process.Start(new ProcessStartInfo("ping.exe", "127.0.0.1 -n 6") { CreateNoWindow = true, UseShellExecute = false })!;
        try
        {
            tuner.Tune(process);
            process.Refresh();
            Assert.Equal(ProcessPriorityClass.BelowNormal, process.PriorityClass);
            if (Environment.ProcessorCount > 2) Assert.Equal(3, (long)process.ProcessorAffinity);
        }
        finally { process.Kill(); }
    }

    [Fact]
    public async Task Window_title_is_set_on_a_real_window()
    {
        var tuner = new RobloxProcessTuner(new MemorySettings(), NullLogger<RobloxProcessTuner>.Instance);
        const string script = "Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.Form; $f.Text = 'before'; $f.ShowDialog()";
        using var process = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{script}\"") { UseShellExecute = false })!;
        try
        {
            await tuner.ApplyTitleAsync(process, "Nexstrap test title", 120, TimeSpan.FromMilliseconds(250));
            process.Refresh();
            Assert.Equal("Nexstrap test title", process.MainWindowTitle);
        }
        finally { process.Kill(); }
    }

    [Fact]
    public void Core_choices_never_offer_every_core_twice()
    {
        var values = RobloxSettingsViewModel.BuildCoreChoices(8).Select(c => c.Value).ToList();
        Assert.Equal([0, 2, 4, 6], values);
    }

    [Fact]
    public void Mods_are_applied_once_and_restored_exactly()
    {
        var settings = new MemorySettings();
        var mods = Mods(settings);
        var build = Build();
        var sound = Path.Combine(build, RobloxModService.DeathSoundTargets[0]);
        var cursor = Path.Combine(build, RobloxModService.CursorTargets[0]);
        var originalSound = File.ReadAllBytes(sound);
        var originalCursor = File.ReadAllBytes(cursor);

        Assert.Null(mods.SetDeathSound(Write("my.ogg", Ogg(9))));
        Assert.Null(mods.SetCursor(Write("my.png", Png(32, 32))));
        mods.Sync(build);
        mods.Sync(build); // a second launch must not back up the replaced file

        Assert.Equal(Ogg(9), File.ReadAllBytes(sound));
        Assert.Equal(Png(32, 32), File.ReadAllBytes(cursor));

        mods.ClearDeathSound();
        mods.ClearCursor();
        mods.Sync(build);

        Assert.Equal(originalSound, File.ReadAllBytes(sound));
        Assert.Equal(originalCursor, File.ReadAllBytes(cursor));
        Assert.False(Directory.Exists(Path.Combine(build, RobloxModService.BackupFolder)));
    }

    [Fact]
    public void Mods_never_touch_another_launchers_build()
    {
        var settings = new MemorySettings();
        var mods = Mods(settings);
        var build = Build();
        var sound = Path.Combine(build, RobloxModService.DeathSoundTargets[0]);
        var original = File.ReadAllBytes(sound);
        Assert.Null(mods.SetDeathSound(Write("my.ogg", Ogg(9))));

        mods.ApplyTo(new RobloxInstallation(InstallationSource.Voidstrap, build, Path.Combine(build, "RobloxPlayerBeta.exe"), "version-x", "1", DateTime.Now));

        Assert.Equal(original, File.ReadAllBytes(sound));
    }

    [Fact]
    public void Bad_mod_files_are_refused()
    {
        var mods = Mods(new MemorySettings());

        Assert.NotNull(mods.SetDeathSound(Write("sound.mp3", Ogg(1))));
        Assert.NotNull(mods.SetDeathSound(Write("fake.ogg", [1, 2, 3, 4, 5])));
        Assert.NotNull(mods.SetCursor(Write("cursor.jpg", Png(64, 64))));
        Assert.NotNull(mods.SetCursor(Write("fake.png", [1, 2, 3])));
        Assert.NotNull(mods.SetCursor(Write("huge.png", Png(512, 512))));
    }

    [Fact]
    public void Cleanup_removes_only_old_files_in_its_folders()
    {
        var logs = Path.Combine(_dir, "logs");
        var old = Write(Path.Combine("logs", "old.log"), new byte[100]);
        var fresh = Write(Path.Combine("logs", "new.log"), new byte[100]);
        var outside = Write("outside.log", new byte[100]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));
        File.SetLastWriteTimeUtc(outside, DateTime.UtcNow.AddDays(-8));

        var result = new CleanupService(new FakeLauncher(), NullLogger<CleanupService>.Instance, [logs, Path.Combine(_dir, "missing")]).CleanOldFiles();

        Assert.Equal(new CleanupResult(1, 100), result);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Cleanup_waits_while_Roblox_is_open()
    {
        var logs = Path.Combine(_dir, "logs");
        var old = Write(Path.Combine("logs", "old.log"), new byte[100]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-8));

        var result = new CleanupService(new FakeLauncher { Running = true }, NullLogger<CleanupService>.Instance, [logs]).CleanOldFiles();

        Assert.Equal(0, result.Files);
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void Windows_startup_entry_can_be_added_moved_and_removed()
    {
        var startup = new WindowsStartup(ScratchRun, @"C:\Old\Nexstrap.exe");
        Assert.False(startup.IsEnabled);

        startup.Enable();
        Assert.True(startup.IsEnabled);
        Assert.Equal("\"C:\\Old\\Nexstrap.exe\"", WindowsStartup.Read(ScratchRun));

        WindowsStartup.Retarget(ScratchRun, @"C:\New\Nexstrap.exe");
        Assert.False(startup.IsEnabled); // this copy is no longer the one Windows opens
        Assert.Equal("\"C:\\New\\Nexstrap.exe\"", WindowsStartup.Read(ScratchRun));

        startup.Disable();
        Assert.Null(WindowsStartup.Read(ScratchRun));
    }

    [Fact]
    public void Retarget_does_not_add_an_entry_the_user_never_turned_on()
    {
        WindowsStartup.Retarget(ScratchRun, @"C:\New\Nexstrap.exe");
        Assert.Null(WindowsStartup.Read(ScratchRun));
    }
}

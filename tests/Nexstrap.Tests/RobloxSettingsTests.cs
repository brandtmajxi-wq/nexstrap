using System.IO;
using System.Xml;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Nexstrap.Models;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Tests;

internal sealed class FakeLauncher : IRobloxLauncher
{
    public bool Running { get; set; }
    public LaunchResult LaunchApp(RobloxInstallation installation) => new(true, "ok");
    public LaunchResult LaunchUri(RobloxInstallation installation, string uri) => new(true, "ok");
    public bool IsRobloxRunning() => Running;
}

public class RobloxUserSettingsTests : IDisposable
{
    // Same shape as a real GlobalBasicSettings_13.xml (trimmed), including keys Nexstrap doesn't know.
    private const string Sample = """
        <roblox xmlns:xmime="http://www.w3.org/2005/05/xmlmime" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://www.roblox.com/roblox.xsd" version="4">
        	<External>null</External>
        	<External>nil</External>
        	<Item class="UserGameSettings" referent="RBX0">
        		<Properties>
        			<token name="CameraMode">0</token>
        			<bool name="CameraYInverted">false</bool>
        			<int name="FramerateCap">120</int>
        			<bool name="Fullscreen">false</bool>
        			<float name="MasterVolume">0.200000003</float>
        			<float name="MouseSensitivity">2.20000005</float>
        			<token name="SavedQualityLevel">4</token>
        			<Vector2 name="MouseSensitivityFirstPerson"><X>2.2</X><Y>2.2</Y></Vector2>
        		</Properties>
        	</Item>
        </roblox>
        """;

    private readonly TempDir _dir = new();
    private readonly FakeLauncher _launcher = new();
    private readonly RobloxUserSettingsService _service;

    public RobloxUserSettingsTests()
    {
        File.WriteAllText(_dir.File("GlobalBasicSettings_13.xml"), Sample);
        _service = new RobloxUserSettingsService(_launcher, NullLogger<RobloxUserSettingsService>.Instance,
            _dir.File("GlobalBasicSettings_13.xml"), _dir.File("Backups"));
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Reads_the_in_game_values()
    {
        var s = _service.Load();
        Assert.Equal(120, s.FramerateCap);
        Assert.Equal(4, s.SavedQualityLevel);
        Assert.False(s.Fullscreen);
        Assert.Equal(0.2, s.MasterVolume!.Value, 3);
        Assert.Equal(2.2, s.MouseSensitivity!.Value, 3);
        Assert.False(s.CameraYInverted);
    }

    [Fact]
    public void Saves_only_changed_keys_and_keeps_everything_else()
    {
        _service.Save(new RobloxUserSettings { FramerateCap = 240, Fullscreen = true, MasterVolume = 0.5 });

        var s = _service.Load();
        Assert.Equal(240, s.FramerateCap);
        Assert.True(s.Fullscreen);
        Assert.Equal(0.5, s.MasterVolume!.Value, 3);
        Assert.Equal(4, s.SavedQualityLevel); // untouched

        var xml = File.ReadAllText(_dir.File("GlobalBasicSettings_13.xml"));
        Assert.Contains("MouseSensitivityFirstPerson", xml);           // unknown keys preserved
        Assert.Contains("<token name=\"CameraMode\">0</token>", xml);
        Assert.Single(_service.ListBackups());                          // backup before writing
    }

    [Fact]
    public void Values_are_clamped_to_roblox_ranges()
    {
        _service.Save(new RobloxUserSettings { SavedQualityLevel = 99, MasterVolume = 3, MouseSensitivity = 50 });
        var s = _service.Load();
        Assert.Equal(10, s.SavedQualityLevel);
        Assert.Equal(1, s.MasterVolume);
        Assert.Equal(4, s.MouseSensitivity);
    }

    [Fact]
    public void Refuses_to_write_while_roblox_runs()
    {
        _launcher.Running = true;
        var before = File.ReadAllText(_dir.File("GlobalBasicSettings_13.xml"));
        Assert.Throws<InvalidOperationException>(() => _service.Save(new RobloxUserSettings { FramerateCap = 60 }));
        Assert.Equal(before, File.ReadAllText(_dir.File("GlobalBasicSettings_13.xml")));
    }

    [Fact]
    public void Restore_brings_back_the_old_file()
    {
        var backup = _service.BackupNow();
        _service.Save(new RobloxUserSettings { FramerateCap = 60 });
        Assert.Equal(60, _service.Load().FramerateCap);

        _service.Restore(backup);
        Assert.Equal(120, _service.Load().FramerateCap);
    }

    [Fact]
    public void Restore_rejects_files_that_arent_roblox_settings()
    {
        var bogus = _dir.File("bogus.xml");
        File.WriteAllText(bogus, "<html><body>nope</body></html>");
        Assert.Throws<InvalidDataException>(() => _service.Restore(new SettingsBackup(bogus, DateTime.Now)));
        Assert.Equal(120, _service.Load().FramerateCap);
    }

    [Fact]
    public void Missing_file_reads_as_defaults_and_cannot_be_saved()
    {
        File.Delete(_dir.File("GlobalBasicSettings_13.xml"));
        Assert.Null(_service.Load().FramerateCap);
        Assert.Throws<InvalidOperationException>(() => _service.Save(new RobloxUserSettings { FramerateCap = 60 }));
    }
}

public class ProtocolRegistrationTests : IDisposable
{
    private readonly string _root = @"Software\NexstrapTests\" + Guid.NewGuid().ToString("N") + @"\Classes";
    private const string Exe = @"C:\Apps\Nexstrap\Nexstrap.exe";
    private const string Voidstrap = "\"C:\\Users\\x\\AppData\\Local\\Voidstrap\\Voidstrap.exe\" -player \"%1\"";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root[..(_root.LastIndexOf('\\'))], throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\NexstrapTests");
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
        {
            parent.Close();
            try { Registry.CurrentUser.DeleteSubKey(@"Software\NexstrapTests", throwOnMissingSubKey: false); }
            catch (InvalidOperationException) { } // another test still uses it
        }
    }

    private string? Command(string scheme)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{_root}\{scheme}\shell\open\command");
        return key?.GetValue(null) as string;
    }

    private void SeedVoidstrap()
    {
        foreach (var scheme in ProtocolRegistration.Schemes)
            using (var key = Registry.CurrentUser.CreateSubKey($@"{_root}\{scheme}\shell\open\command"))
                key.SetValue(null, Voidstrap);
    }

    [Fact]
    public void Register_then_unregister_restores_previous_handler()
    {
        SeedVoidstrap();
        var settings = new MemorySettings();
        var reg = new ProtocolRegistration(settings, NullLogger<ProtocolRegistration>.Instance, _root, Exe);

        Assert.False(reg.IsRegistered);
        reg.Register();
        Assert.True(reg.IsRegistered);
        Assert.Equal($"\"{Exe}\" \"%1\"", Command("roblox-player"));
        Assert.Equal(Voidstrap, settings.Current.Launcher.PreviousProtocolHandlers["roblox-player"]);

        reg.Unregister();
        Assert.False(reg.IsRegistered);
        Assert.Equal(Voidstrap, Command("roblox-player"));
        Assert.Equal(Voidstrap, Command("roblox"));
        Assert.Empty(settings.Current.Launcher.PreviousProtocolHandlers);
    }

    [Fact]
    public void Unregister_without_previous_handler_removes_the_scheme()
    {
        var reg = new ProtocolRegistration(new MemorySettings(), NullLogger<ProtocolRegistration>.Instance, _root, Exe);
        reg.Register();
        reg.Unregister();
        Assert.Null(Command("roblox-player"));
    }

    [Fact]
    public void Unregister_leaves_other_launchers_alone()
    {
        var reg = new ProtocolRegistration(new MemorySettings(), NullLogger<ProtocolRegistration>.Instance, _root, Exe);
        SeedVoidstrap(); // another launcher owns it; Nexstrap never registered
        reg.Unregister();
        Assert.Equal(Voidstrap, Command("roblox-player"));
    }

    [Theory]
    [InlineData("\"C:\\A B\\Voidstrap.exe\" -player \"%1\"", "C:\\A B\\Voidstrap.exe")]
    [InlineData("C:\\Roblox\\RobloxPlayerBeta.exe %1", "C:\\Roblox\\RobloxPlayerBeta.exe")]
    [InlineData("rundll32 something", null)]
    public void Extracts_executable_from_command(string command, string? exe) =>
        Assert.Equal(exe, ProtocolRegistration.ExtractExe(command));
}

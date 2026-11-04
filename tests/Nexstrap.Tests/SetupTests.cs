using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Nexstrap.Core;
using Nexstrap.Services.Roblox;
using Nexstrap.Services.Setup;

namespace Nexstrap.Tests;

public class LanguageTests
{
    public static TheoryData<string> Codes => [.. Loc.Languages.Select(l => l.Code)];

    [Theory]
    [MemberData(nameof(Codes))]
    public void Every_language_has_exactly_the_english_keys(string code)
    {
        var english = Loc.Load("en");
        var language = Loc.Load(code);
        Assert.Equal(english.Keys.Order(), language.Keys.Order());
        Assert.All(language, pair =>
        {
            Assert.False(string.IsNullOrWhiteSpace(pair.Value));
            Assert.Equal(english[pair.Key].Contains("{0}"), pair.Value.Contains("{0}"));
        });
    }

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("pt-BR", "pt")]
    [InlineData("ja-JP", "en")]
    public void Detects_the_windows_language(string culture, string expected) =>
        Assert.Equal(expected, Loc.Detect(new CultureInfo(culture)));
}

public class InstallServiceTests : IDisposable
{
    private sealed class FakeProtocol : IProtocolRegistration
    {
        public string? RegisteredFor;
        public int Unregistered;
        public bool IsRegistered => RegisteredFor is not null;
        public void Register() { }
        public void RegisterFor(string exePath) => RegisteredFor = exePath;
        public void Unregister() => Unregistered++;
    }

    private readonly string _key = @"Software\NexstrapTests\Uninstall" + Guid.NewGuid().ToString("N");
    private readonly TempDir _dir = new();
    private readonly MemorySettings _settings = new();
    private readonly FakeProtocol _protocol = new();
    private readonly string _source, _folder, _desktop, _startMenu;

    public InstallServiceTests()
    {
        _source = Path.Combine(_dir.Path, "Downloads", "Nexstrap.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_source)!);
        File.WriteAllText(_source, "new build");
        _folder = Path.Combine(_dir.Path, "Programs", "Nexstrap");
        _desktop = _dir.File("Desktop");
        _startMenu = _dir.File("Start");
        Directory.CreateDirectory(_desktop);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
        _dir.Dispose();
    }

    private InstallService Service(string? exe = null) => new(_settings, _protocol, NullLogger<InstallService>.Instance,
        _key, _startMenu, _desktop, exe ?? _source, _dir.File("Data"));

    private InstallOptions Options(bool links = true) => new(_folder, true, true, links);

    [Fact]
    public void Installs_the_exe_shortcuts_and_windows_entry()
    {
        var target = Service().Install(Options());

        Assert.Equal("new build", File.ReadAllText(target));
        Assert.True(File.Exists(Path.Combine(_desktop, "Nexstrap.lnk")));
        Assert.True(File.Exists(Path.Combine(_startMenu, "Nexstrap.lnk")));
        Assert.Equal(target, _protocol.RegisteredFor);

        using var key = Registry.CurrentUser.OpenSubKey(_key)!;
        Assert.Equal("Nexstrap", key.GetValue("DisplayName"));
        Assert.Equal(AppInfo.VersionString, key.GetValue("DisplayVersion"));
        Assert.Equal($"\"{target}\" --uninstall", key.GetValue("UninstallString"));
        Assert.Equal(_folder, key.GetValue("InstallLocation"));
    }

    [Fact]
    public void Unticked_options_are_left_out_and_links_are_released()
    {
        Service().Install(new InstallOptions(_folder, false, false, false));
        Assert.False(File.Exists(Path.Combine(_desktop, "Nexstrap.lnk")));
        Assert.False(File.Exists(Path.Combine(_startMenu, "Nexstrap.lnk")));
        Assert.Null(_protocol.RegisteredFor);
        Assert.Equal(1, _protocol.Unregistered);
    }

    [Fact]
    public void Installing_again_replaces_the_exe()
    {
        var target = Service().Install(Options());
        File.WriteAllText(_source, "newer build");
        Service().Install(Options());
        Assert.Equal("newer build", File.ReadAllText(target));
    }

    [Fact]
    public void Offers_setup_only_for_a_plain_start_of_an_uninstalled_copy()
    {
        var service = Service();
        Assert.True(service.ShouldOfferSetup(new StartupOptions()));
        Assert.False(service.ShouldOfferSetup(new StartupOptions { JustUpdated = true }));
        Assert.False(service.ShouldOfferSetup(new StartupOptions { StartPage = "About" }));
        Assert.False(service.ShouldOfferSetup(new StartupOptions { Positional = ["roblox://x"] }));

        _settings.Current.SkippedSetup = true;
        Assert.False(service.ShouldOfferSetup(new StartupOptions()));
        _settings.Current.SkippedSetup = false;

        var target = service.Install(Options());
        Assert.False(Service(target).ShouldOfferSetup(new StartupOptions()));
        Assert.True(Service().ShouldOfferSetup(new StartupOptions())); // a newer download offers the update
    }

    [Fact]
    public void Rejects_admin_and_invalid_folders()
    {
        var service = Service();
        Assert.Equal("Setup.Error.AdminFolder", service.ValidateFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nexstrap")));
        Assert.Equal("Setup.Error.Folder", service.ValidateFolder("relative\\path"));
        Assert.Equal("Setup.Error.Folder", service.ValidateFolder(""));
        Assert.Equal("Setup.Error.Folder", service.ValidateFolder(@"\\server\share\Nexstrap"));
        Assert.Null(service.ValidateFolder(_folder));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Cleanup_never_deletes_the_install_folder_recursively()
    {
        var keep = InstallService.BuildCleanupArguments(@"C:\Games\Nexstrap.exe", null);
        Assert.Contains(@"del /f /q ""C:\Games\Nexstrap.exe""", keep);
        Assert.Contains(@"rmdir ""C:\Games""", keep);
        Assert.DoesNotContain("/s", keep);

        var delete = InstallService.BuildCleanupArguments(@"C:\Games\Nexstrap.exe", @"C:\Data\Nexstrap");
        Assert.Contains(@"rmdir /s /q ""C:\Data\Nexstrap""", delete);
        Assert.DoesNotContain(@"rmdir /s /q ""C:\Games""", delete);
    }

    [Fact]
    public void Uninstall_removes_the_windows_entry_shortcuts_and_links()
    {
        // The real exe deletion runs in a helper; a fake path keeps it harmless here.
        var target = Service().Install(Options());
        Service(target).Uninstall(deleteData: false);

        Assert.Null(Registry.CurrentUser.OpenSubKey(_key));
        Assert.False(File.Exists(Path.Combine(_desktop, "Nexstrap.lnk")));
        Assert.False(File.Exists(Path.Combine(_startMenu, "Nexstrap.lnk")));
        Assert.Equal(1, _protocol.Unregistered);
    }
}

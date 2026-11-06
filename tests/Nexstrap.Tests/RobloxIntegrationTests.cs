using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Core;
using Nexstrap.Models;
using Nexstrap.Services.Roblox;

namespace Nexstrap.Tests;

public class RobloxLinksTests
{
    [Theory]
    [InlineData("1818", 1818)]
    [InlineData("  1818 ", 1818)]
    [InlineData("https://www.roblox.com/games/1818/Crossroads", 1818)]
    [InlineData("www.roblox.com/games/920587237/Adopt-Me", 920587237)]
    [InlineData("https://www.roblox.com/games/start?placeId=606849621&launchData=x", 606849621)]
    [InlineData("https://web.roblox.com/games/42", 42)]
    public void Parses_place_ids(string input, long expected)
    {
        Assert.True(RobloxLinks.TryParsePlaceId(input, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("hello")]
    [InlineData("https://evil.example/games/1818")]
    [InlineData("https://roblox.com.evil.example/games/1818")]
    [InlineData("https://www.roblox.com/users/1/profile")]
    public void Rejects_non_place_input(string input) => Assert.False(RobloxLinks.TryParsePlaceId(input, out _));

    [Fact]
    public void Builds_documented_deep_link() =>
        Assert.Equal("roblox://placeId=1818", RobloxLinks.PlaceLaunchUri(1818));

    [Fact]
    public void Builds_server_link_and_rejects_anything_but_a_guid()
    {
        Assert.Equal("roblox://placeId=1818&gameInstanceId=b6c72548-3e72-4029-a7b6-056e397f1d18",
            RobloxLinks.ServerLaunchUri(1818, "b6c72548-3e72-4029-a7b6-056e397f1d18"));
        Assert.True(RobloxLinks.IsLaunchUri(RobloxLinks.ServerLaunchUri(1818, "b6c72548-3e72-4029-a7b6-056e397f1d18")));
        Assert.Throws<ArgumentException>(() => RobloxLinks.ServerLaunchUri(1818, "x&launchData=evil"));
    }

    [Theory]
    [InlineData("roblox://experiences/start?placeId=1818", true)]
    [InlineData("roblox-player:1+launchmode:play+gameinfo:SECRET+placelauncherurl:https%3A%2F%2Fx%3FplaceId%3D1818", true)]
    [InlineData("roblox://x \"--evil\"", false)]
    [InlineData("roblox://x y", false)]
    [InlineData("https://www.roblox.com", false)]
    [InlineData("C:\\Windows\\system32\\calc.exe", false)]
    [InlineData(null, false)]
    public void Validates_launch_uris(string? uri, bool valid) => Assert.Equal(valid, RobloxLinks.IsLaunchUri(uri));

    [Fact]
    public void Redaction_never_leaks_the_auth_ticket()
    {
        var redacted = RobloxLinks.Redact("roblox-player:1+launchmode:play+gameinfo:SECRETTICKET+placelauncherurl:https%3A%2F%2Fx%3FplaceId%3D1818");
        Assert.DoesNotContain("SECRET", redacted);
        Assert.Contains("1818", redacted);
    }
}

public class DeploymentManifestTests
{
    private const string Sample = "v0\r\nRobloxApp.zip\r\n81738fb2f70789726dfc21b3707415b2\r\n137795773\r\n174128171\r\nssl.zip\r\na0d355cd801e310ef1339996f01b0685\r\n130569\r\n228725\r\n";

    [Fact]
    public void Parses_v0_manifest()
    {
        var packages = RobloxDeployment.ParseManifest(Sample);
        Assert.Equal(2, packages.Count);
        Assert.Equal(new PackageEntry("RobloxApp.zip", "81738fb2f70789726dfc21b3707415b2", 137795773, 174128171), packages[0]);
    }

    [Theory]
    [InlineData("v1\nRobloxApp.zip\nabc\n1\n1")]
    [InlineData("v0\nRobloxApp.zip\n81738fb2f70789726dfc21b3707415b2\n1")]
    [InlineData("v0\nRobloxApp.zip\nnot-a-hash-not-a-hash-not-a-hash\n1\n1")]
    [InlineData("")]
    public void Rejects_bad_manifests(string text) => Assert.Throws<InstallException>(() => RobloxDeployment.ParseManifest(text));

    [Theory]
    [InlineData("version-5a21b87bbc2641cf", true)]
    [InlineData("version-../../x", false)]
    [InlineData("5a21b87bbc2641cf", false)]
    [InlineData("", false)]
    public void Validates_version_hashes(string hash, bool valid) => Assert.Equal(valid, RobloxDeployment.IsValidHash(hash));
}

public class DeploymentInstallTests : IDisposable
{
    private const string Hash = "version-0123456789abcdef";
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open())) w.Write(content);
        return ms.ToArray();
    }

    private static string Md5(byte[] data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

    /// <summary>Serves a manifest and package zips like setup.rbxcdn.com; can corrupt one package.</summary>
    private sealed class FakeCdn(Dictionary<string, byte[]> packages, string? corrupt = null, string[]? extraManifestEntries = null) : HttpMessageHandler
    {
        public int Requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (path == $"{Hash}-rbxPkgManifest.txt")
            {
                var sb = new StringBuilder("v0\n");
                foreach (var (name, data) in packages) sb.Append($"{name}\n{Md5(data)}\n{data.Length}\n{data.Length * 2}\n");
                foreach (var extra in extraManifestEntries ?? []) sb.Append($"{extra}\n{new string('a', 32)}\n1\n1\n");
                sb.Append("RobloxPlayerInstaller.exe\nffffffffffffffffffffffffffffffff\n10\n10\n");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sb.ToString()) });
            }
            foreach (var (name, data) in packages)
            {
                if (path != $"{Hash}-{name}") continue;
                var body = name == corrupt ? data.Reverse().ToArray() : data;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private RobloxDeployment Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), NullLogger<RobloxDeployment>.Instance, _dir.File("Versions"), _dir.File("Downloads"));

    private static Dictionary<string, byte[]> Packages() => new()
    {
        ["RobloxApp.zip"] = Zip(("RobloxPlayerBeta.exe", "exe"), ("RobloxPlayerBeta.dll", "dll")),
        ["content-fonts.zip"] = Zip(("arial.ttf", "font"), (@"families\x.json", "{}")),
        ["extracontent-luapackages.zip"] = Zip(("Packages/a.lua", "return 1")),
    };

    [Fact]
    public async Task Installs_into_the_expected_layout()
    {
        var progress = new List<InstallProgress>();
        var dir = await Create(new FakeCdn(Packages())).InstallAsync(new ClientVersionInfo("0.1.0.1", Hash),
            new SyncProgress<InstallProgress>(progress.Add));

        Assert.Equal(_dir.File(Path.Combine("Versions", Hash)), dir);
        Assert.True(File.Exists(Path.Combine(dir, "RobloxPlayerBeta.exe")));
        Assert.True(File.Exists(Path.Combine(dir, @"content\fonts\arial.ttf")));
        Assert.True(File.Exists(Path.Combine(dir, @"content\fonts\families\x.json")));
        Assert.True(File.Exists(Path.Combine(dir, @"ExtraContent\LuaPackages\Packages\a.lua")));
        Assert.Contains("<ContentFolder>content</ContentFolder>", File.ReadAllText(Path.Combine(dir, "AppSettings.xml")));
        Assert.True(File.Exists(Path.Combine(dir, RobloxDeployment.CompleteMarker)));
        Assert.False(File.Exists(Path.Combine(dir, "RobloxPlayerInstaller.exe")));

        Assert.Empty(Directory.GetDirectories(_dir.File("Versions"), "_staging-*"));
        Assert.Empty(Directory.GetFiles(_dir.File("Downloads")));
        Assert.Equal(1.0, progress[^1].Fraction);

        // The locator accepts it as a Nexstrap build.
        var found = new RobloxLocator(NullLogger<RobloxLocator>.Instance).Scan([(InstallationSource.Nexstrap, _dir.File("Versions"))]);
        Assert.Equal(Hash, Assert.Single(found).VersionHash);
    }

    [Fact]
    public async Task Corrupt_download_fails_and_leaves_nothing_behind()
    {
        var packages = Packages();
        var ex = await Assert.ThrowsAsync<InstallException>(() =>
            Create(new FakeCdn(packages, corrupt: "content-fonts.zip")).InstallAsync(new ClientVersionInfo("0.1.0.1", Hash), null));

        Assert.Contains("integrity", ex.Message);
        Assert.False(Directory.Exists(_dir.File(Path.Combine("Versions", Hash))));
        Assert.Empty(Directory.GetDirectories(_dir.File("Versions")));
    }

    [Fact]
    public async Task Unknown_package_is_refused_before_downloading()
    {
        var cdn = new FakeCdn(Packages(), extraManifestEntries: ["brand-new-thing.zip"]);
        var ex = await Assert.ThrowsAsync<InstallException>(() => Create(cdn).InstallAsync(new ClientVersionInfo("0.1.0.1", Hash), null));
        Assert.Contains("brand-new-thing.zip", ex.Message);
        Assert.Equal(1, cdn.Requests); // only the manifest
    }

    [Fact]
    public async Task Already_installed_build_is_reused()
    {
        var cdn = new FakeCdn(Packages());
        var deployment = Create(cdn);
        await deployment.InstallAsync(new ClientVersionInfo("0.1.0.1", Hash), null);
        var before = cdn.Requests;
        await deployment.InstallAsync(new ClientVersionInfo("0.1.0.1", Hash), null);
        Assert.Equal(before, cdn.Requests);
    }

    [Fact]
    public void Zip_slip_paths_are_rejected()
    {
        var zip = _dir.File("evil.zip");
        File.WriteAllBytes(zip, Zip(("../../outside.txt", "x")));
        Assert.Throws<InstallException>(() => RobloxDeployment.ExtractSafely(zip, _dir.File("dest")));
        Assert.False(File.Exists(_dir.File("outside.txt")));
    }

    /// <summary>Progress&lt;T&gt; posts asynchronously; tests need synchronous reports.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public class ClientSelectionTests
{
    private static RobloxInstallation Install(InstallationSource source, string hash, int daysAgo) =>
        new(source, $@"C:\{source}\{hash}", $@"C:\{source}\{hash}\RobloxPlayerBeta.exe", hash, "0.1", DateTime.Now.AddDays(-daysAgo));

    private static RobloxClientService Create(MemorySettings settings) =>
        new(null!, null!, null!, settings, NullLogger<RobloxClientService>.Instance);

    [Fact]
    public void Prefers_live_build_and_nexstrap_copy()
    {
        var oldVoid = Install(InstallationSource.Voidstrap, "version-old", 0);
        var liveVoid = Install(InstallationSource.Voidstrap, "version-live", 3);
        var liveNex = Install(InstallationSource.Nexstrap, "version-live", 5);
        var service = Create(new MemorySettings());

        Assert.Equal(liveNex, service.ResolvePreferred([oldVoid, liveVoid, liveNex], new ClientVersionInfo("1", "version-live")));
        Assert.Equal(liveVoid, service.ResolvePreferred([oldVoid, liveVoid], new ClientVersionInfo("1", "version-live")));
        Assert.Equal(oldVoid, service.ResolvePreferred([oldVoid, liveVoid], null)); // newest when live unknown
    }

    [Fact]
    public void Keeps_nexstraps_own_build_when_nothing_matches_a_new_roblox_version()
    {
        var voidstrap = Install(InstallationSource.Voidstrap, "version-old", 0);
        var nexstrap = Install(InstallationSource.Nexstrap, "version-old", 2);
        var service = Create(new MemorySettings());

        Assert.Equal(nexstrap, service.ResolvePreferred([voidstrap, nexstrap], new ClientVersionInfo("2", "version-new")));
        Assert.Equal(nexstrap, service.ResolvePreferred([voidstrap, nexstrap], null));
        Assert.Equal(voidstrap, service.ResolvePreferred([voidstrap], new ClientVersionInfo("2", "version-new")));
    }

    [Fact]
    public void Pinned_choice_wins_until_it_disappears()
    {
        var a = Install(InstallationSource.Official, "version-a", 1);
        var b = Install(InstallationSource.Bloxstrap, "version-b", 0);
        var settings = new MemorySettings();
        settings.Current.Launcher.PreferredInstallation = a.Directory;
        var service = Create(settings);

        Assert.Equal(a, service.ResolvePreferred([a, b], new ClientVersionInfo("1", "version-b")));
        Assert.Equal(b, service.ResolvePreferred([b], null));
    }

    [Fact]
    public void Update_flag_reflects_preferred_build()
    {
        var a = Install(InstallationSource.Official, "version-a", 1);
        Assert.True(new ClientState([a], a, new ProtocolHandlerInfo(null), new ClientVersionInfo("2", "version-b"), null).UpdateAvailable);
        Assert.False(new ClientState([a], a, new ProtocolHandlerInfo(null), new ClientVersionInfo("1", "version-a"), null).UpdateAvailable);
        Assert.False(new ClientState([a], a, new ProtocolHandlerInfo(null), null, "offline").UpdateAvailable);
    }
}

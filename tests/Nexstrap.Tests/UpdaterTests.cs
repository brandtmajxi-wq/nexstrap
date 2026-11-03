using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Nexstrap.Core;
using Nexstrap.Services.Updates;

namespace Nexstrap.Tests;

public class UpdateInstallerTests : IDisposable
{
    private const string Owner = "acme", Repo = "nexstrap";
    // The "release" ships the test's own copy of Nexstrap, so its tag must be the build's version.
    private static readonly string Tag = "v" + AppInfo.VersionString;
    private static readonly string Base = $"https://github.com/{Owner}/{Repo}/releases/download/{Tag}/";

    private readonly TempDir _dir = new();

    // A real Windows binary whose version resource says ProductName=Nexstrap, version 0.1.0.
    private static readonly byte[] NexstrapBinary = File.ReadAllBytes(typeof(AppInfo).Assembly.Location);
    private static readonly string NexstrapHash = Convert.ToHexString(SHA256.HashData(NexstrapBinary)).ToLowerInvariant();

    public void Dispose() => _dir.Dispose();

    private sealed class FakeGitHub(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            return Task.FromResult(files.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private (UpdateInstaller Installer, FakeGitHub Server) Create(Dictionary<string, byte[]> files)
    {
        var server = new FakeGitHub(files);
        return (new UpdateInstaller(new HttpClient(server), NullLogger<UpdateInstaller>.Instance, _dir.File("Downloads"), Owner, Repo), server);
    }

    private static GitHubRelease Release(string? tag = null, string? digest = null, bool withSums = true, long? size = null, string? exeUrl = null)
    {
        var release = new GitHubRelease { TagName = tag ?? Tag };
        release.Assets.Add(new GitHubAsset { Name = "Nexstrap.exe", Size = size ?? NexstrapBinary.Length, DownloadUrl = exeUrl ?? Base + "Nexstrap.exe", Digest = digest });
        if (withSums) release.Assets.Add(new GitHubAsset { Name = "SHA256SUMS.txt", Size = 100, DownloadUrl = Base + "SHA256SUMS.txt" });
        return release;
    }

    private static Dictionary<string, byte[]> Files(byte[]? exe = null, string? sumsHash = null) => new()
    {
        [Base + "Nexstrap.exe"] = exe ?? NexstrapBinary,
        [Base + "SHA256SUMS.txt"] = System.Text.Encoding.UTF8.GetBytes($"{sumsHash ?? NexstrapHash}  Nexstrap.exe\n")
    };

    [Fact]
    public async Task Verifies_a_genuine_release()
    {
        var (installer, _) = Create(Files());
        var progress = new List<UpdateProgress>();
        var update = await installer.DownloadAndVerifyAsync(Release(digest: "sha256:" + NexstrapHash), new Sync<UpdateProgress>(progress.Add));

        Assert.Equal(new Version(AppInfo.Version.Major, AppInfo.Version.Minor, AppInfo.Version.Build), update.Version);
        Assert.Equal(NexstrapHash, update.Sha256);
        Assert.True(File.Exists(update.FilePath));
        Assert.Contains(progress, p => p.Stage.StartsWith("Downloading") && p.Fraction == 1);
    }

    [Fact]
    public async Task Tampered_download_is_rejected_and_deleted()
    {
        var tampered = NexstrapBinary.ToArray();
        tampered[^1] ^= 0xFF;
        var (installer, _) = Create(Files(exe: tampered));

        var ex = await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAndVerifyAsync(Release(), null));
        Assert.Contains("checksum", ex.Message);
        Assert.Empty(Directory.GetFiles(_dir.File("Downloads"), "*.exe", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Release_without_any_checksum_is_refused_before_downloading()
    {
        var (installer, server) = Create(Files());
        var ex = await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAndVerifyAsync(Release(withSums: false), null));
        Assert.Contains("no checksum", ex.Message);
        Assert.Empty(server.Requested);
    }

    [Fact]
    public async Task Disagreeing_checksums_are_refused()
    {
        var (installer, _) = Create(Files());
        var ex = await Assert.ThrowsAsync<UpdateException>(() =>
            installer.DownloadAndVerifyAsync(Release(digest: "sha256:" + new string('a', 64)), null));
        Assert.Contains("disagree", ex.Message);
    }

    [Fact]
    public async Task Files_hosted_elsewhere_are_never_downloaded()
    {
        var (installer, server) = Create(Files());
        await Assert.ThrowsAsync<UpdateException>(() =>
            installer.DownloadAndVerifyAsync(Release(exeUrl: "https://evil.example/Nexstrap.exe"), null));
        await Assert.ThrowsAsync<UpdateException>(() =>
            installer.DownloadAndVerifyAsync(Release(exeUrl: $"https://github.com/someone-else/nexstrap/releases/download/{Tag}/Nexstrap.exe"), null));
        Assert.Empty(server.Requested);
    }

    [Fact]
    public async Task Wrong_version_inside_the_file_is_rejected()
    {
        var (installer, _) = Create(Files());
        var ex = await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAndVerifyAsync(Release(tag: "v9.9.9"), null));
        Assert.Contains("version", ex.Message);
    }

    [Fact]
    public async Task Size_mismatch_is_rejected()
    {
        var (installer, _) = Create(Files());
        await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAndVerifyAsync(Release(size: NexstrapBinary.Length - 10), null));
        await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAndVerifyAsync(Release(size: NexstrapBinary.Length + 10), null));
    }

    [Fact]
    public async Task Non_nexstrap_programs_are_rejected()
    {
        var other = File.ReadAllBytes(typeof(Xunit.FactAttribute).Assembly.Location);
        var hash = Convert.ToHexString(SHA256.HashData(other)).ToLowerInvariant();
        var (installer, _) = Create(Files(exe: other, sumsHash: hash));
        var ex = await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAndVerifyAsync(Release(size: other.Length), null));
        Assert.Contains("isn't Nexstrap", ex.Message);
    }

    [Fact]
    public async Task Drafts_and_prereleases_are_ignored()
    {
        var (installer, _) = Create(Files());
        var draft = Release();
        draft.Draft = true;
        await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAndVerifyAsync(draft, null));
        var pre = Release();
        pre.Prerelease = true;
        await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAndVerifyAsync(pre, null));
    }

    [Fact]
    public void Swaps_the_exe_restarts_and_cleans_up()
    {
        var appDir = Path.Combine(_dir.Path, "app");
        Directory.CreateDirectory(appDir);
        var current = Path.Combine(appDir, "Nexstrap.exe");
        File.WriteAllText(current, "old build");
        var downloaded = _dir.File("new.exe");
        File.WriteAllText(downloaded, "new build");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(downloaded))).ToLowerInvariant();

        string? started = null;
        var installer = new UpdateInstaller(new HttpClient(), NullLogger<UpdateInstaller>.Instance, _dir.File("Downloads"), Owner, Repo)
        {
            CurrentExePath = current,
            StartProcess = p => started = p
        };

        installer.InstallAndRestart(new VerifiedUpdate(new Version(0, 2, 0), downloaded, hash));

        Assert.Equal("new build", File.ReadAllText(current));
        Assert.Equal("old build", File.ReadAllText(current + ".old"));
        Assert.Equal(current, started);

        installer.CleanUpAfterUpdate();
        Assert.False(File.Exists(current + ".old"));
    }

    [Fact]
    public void Refuses_to_swap_a_file_changed_after_verification()
    {
        var current = _dir.File("Nexstrap.exe");
        File.WriteAllText(current, "old build");
        var downloaded = _dir.File("new.exe");
        File.WriteAllText(downloaded, "changed later");
        var installer = new UpdateInstaller(new HttpClient(), NullLogger<UpdateInstaller>.Instance, _dir.File("Downloads"), Owner, Repo)
        {
            CurrentExePath = current,
            StartProcess = _ => throw new Exception("must not start")
        };

        Assert.Throws<UpdateException>(() => installer.InstallAndRestart(new VerifiedUpdate(new Version(0, 2, 0), downloaded, new string('0', 64))));
        Assert.Equal("old build", File.ReadAllText(current));
    }

    [Fact]
    public void Development_builds_are_not_self_updated()
    {
        var installer = new UpdateInstaller(new HttpClient(), NullLogger<UpdateInstaller>.Instance, _dir.File("Downloads"), Owner, Repo)
        {
            CurrentExePath = _dir.File("dotnet.exe")
        };
        Assert.Throws<UpdateException>(() => installer.InstallAndRestart(new VerifiedUpdate(new Version(1, 0, 0), "x", "y")));
    }

    [Theory]
    [InlineData("abc  Nexstrap.exe", null)]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000  Other.exe", null)]
    [InlineData("ABCDEF0000000000000000000000000000000000000000000000000000000000 *Nexstrap.exe", "abcdef0000000000000000000000000000000000000000000000000000000000")]
    public void Parses_sha256sum_files(string text, string? expected) =>
        Assert.Equal(expected, UpdateInstaller.ParseChecksumFile(text, "Nexstrap.exe"));

    private sealed class Sync<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

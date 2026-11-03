using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;

namespace Nexstrap.Services.Updates;

public sealed record UpdateProgress(string Stage, long BytesDone, long BytesTotal)
{
    public double Fraction => BytesTotal <= 0 ? 0 : Math.Clamp((double)BytesDone / BytesTotal, 0, 1);
}

public sealed class UpdateException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A downloaded update that passed every check and is ready to swap in.</summary>
public sealed record VerifiedUpdate(Version Version, string FilePath, string Sha256);

public interface IUpdateInstaller
{
    /// <summary>Downloads the release's Nexstrap.exe and verifies it. Nothing is run.</summary>
    Task<VerifiedUpdate> DownloadAndVerifyAsync(GitHubRelease release, IProgress<UpdateProgress>? progress, CancellationToken ct = default);

    /// <summary>
    /// Swaps the verified file in for the running exe and starts it. The caller must exit immediately
    /// afterwards, without running normal shutdown code (see HomeViewModel.InstallUpdateAsync).
    /// </summary>
    void InstallAndRestart(VerifiedUpdate update);

    /// <summary>Removes leftovers of a previous update (old exe, downloads). Safe to call at every start.</summary>
    void CleanUpAfterUpdate();
}

/// <summary>
/// Nexstrap's self-updater. Release convention (see docs): a GitHub release tagged vX.Y.Z with assets
/// "Nexstrap.exe" and "SHA256SUMS.txt" ("&lt;sha256&gt;  Nexstrap.exe" per line).
///
/// A download is only installed when ALL of these hold:
///   • it came from this repository's release download URL over HTTPS,
///   • its size equals the size GitHub reports,
///   • its SHA-256 equals SHA256SUMS.txt and/or GitHub's asset digest (at least one, and they must agree),
///   • the file is a Windows program whose ProductName is "Nexstrap" and whose version equals the tag.
/// </summary>
public sealed class UpdateInstaller : IUpdateInstaller
{
    public const string ExeAssetName = "Nexstrap.exe";
    public const string ChecksumAssetName = "SHA256SUMS.txt";
    internal const string OldExeSuffix = ".old";

    private readonly HttpClient _http;
    private readonly ILogger<UpdateInstaller> _logger;
    private readonly string _downloadRoot;
    private readonly string _owner;
    private readonly string _repo;

    public UpdateInstaller(HttpClient http, ILogger<UpdateInstaller> logger)
        : this(http, logger, AppInfo.Paths.Downloads, AppInfo.RepositoryOwner, AppInfo.RepositoryName) { }

    internal UpdateInstaller(HttpClient http, ILogger<UpdateInstaller> logger, string downloadRoot, string owner, string repo)
    {
        _http = http;
        _logger = logger;
        _downloadRoot = downloadRoot;
        _owner = owner;
        _repo = repo;
    }

    /// <summary>Overridable for tests: path of the exe being replaced.</summary>
    internal string? CurrentExePath { get; init; } = Environment.ProcessPath;

    /// <summary>Overridable for tests: starts the new exe.</summary>
    internal Action<string> StartProcess { get; init; } = path =>
        Process.Start(new ProcessStartInfo(path, "--updated") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! });

    public async Task<VerifiedUpdate> DownloadAndVerifyAsync(GitHubRelease release, IProgress<UpdateProgress>? progress, CancellationToken ct = default)
    {
        if (release.Draft || release.Prerelease)
            throw new UpdateException("Only published, non-preview releases are installed.");
        if (!UpdateService.TryParseVersion(release.TagName, out var version))
            throw new UpdateException("The release has an unrecognised version tag.");

        var exeAsset = FindAsset(release, ExeAssetName)
                       ?? throw new UpdateException($"This release has no {ExeAssetName} to install. Download it from the release page instead.");
        RequireOfficialUrl(exeAsset);

        // Expected hash(es): GitHub's digest and/or our checksum file. They must agree.
        var expected = new List<string>();
        if (ParseDigest(exeAsset.Digest) is { } digest) expected.Add(digest);
        if (FindAsset(release, ChecksumAssetName) is { } sumsAsset)
        {
            RequireOfficialUrl(sumsAsset);
            progress?.Report(new UpdateProgress("Reading checksums…", 0, exeAsset.Size));
            var sums = await GetStringAsync(sumsAsset.DownloadUrl, ct);
            if (ParseChecksumFile(sums, ExeAssetName) is { } fromFile) expected.Add(fromFile);
            else throw new UpdateException($"{ChecksumAssetName} doesn't list {ExeAssetName}.");
        }
        if (expected.Count == 0)
            throw new UpdateException("This release has no checksum, so Nexstrap can't verify it and won't install it.");
        if (expected.Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            throw new UpdateException("The release's checksums disagree with each other. The update was not installed.");

        var dir = Path.Combine(_downloadRoot, "update-" + version);
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, ExeAssetName);
        var actual = await DownloadAsync(exeAsset, target, progress, ct);

        progress?.Report(new UpdateProgress("Verifying…", exeAsset.Size, exeAsset.Size));
        try
        {
            if (!string.Equals(actual, expected[0], StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("The download doesn't match the release's checksum. It may be corrupted or tampered with, so it was deleted.");
            VerifyIdentity(target, version);
        }
        catch
        {
            TryDelete(target);
            throw;
        }

        _logger.LogInformation("Verified update {Version} ({Hash})", version, actual);
        return new VerifiedUpdate(version, target, actual);
    }

    public void InstallAndRestart(VerifiedUpdate update)
    {
        var current = CurrentExePath ?? throw new UpdateException("Nexstrap couldn't find its own file.");
        if (!Path.GetFileName(current).Equals(ExeAssetName, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("Self-update only works for the published Nexstrap.exe (not development builds).");

        // Re-check right before swapping in case the file changed since verification.
        if (!string.Equals(Sha256Of(update.FilePath), update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException("The downloaded update changed after it was verified. It was not installed.");

        var old = current + OldExeSuffix;
        TryDelete(old);

        // Load the process assembly now: after the swap, this exe can't load anything new from its bundle.
        _ = new ProcessStartInfo();

        // Windows allows renaming a running exe, but not overwriting it.
        File.Move(current, old);
        try
        {
            File.Copy(update.FilePath, current);
        }
        catch (Exception ex)
        {
            // Roll back so the user is never left without a working Nexstrap.
            _logger.LogError(ex, "Update swap failed; restoring the previous version");
            if (File.Exists(current)) TryDelete(current);
            File.Move(old, current);
            throw new UpdateException("The update couldn't be put in place: " + ex.Message, ex);
        }

        _logger.LogInformation("Installed {Version}; restarting", update.Version);
        StartProcess(current);
    }

    public void CleanUpAfterUpdate()
    {
        if (CurrentExePath is { } current) TryDelete(current + OldExeSuffix);
        try
        {
            if (!Directory.Exists(_downloadRoot)) return;
            foreach (var dir in Directory.GetDirectories(_downloadRoot, "update-*"))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Update cleanup skipped");
        }
    }

    // ===== Checks =====

    private static GitHubAsset? FindAsset(GitHubRelease release, string name) =>
        release.Assets.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void RequireOfficialUrl(GitHubAsset asset)
    {
        var prefix = $"https://github.com/{_owner}/{_repo}/releases/download/";
        if (!asset.DownloadUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UpdateException($"{asset.Name} isn't hosted on this project's GitHub releases, so it won't be downloaded.");
    }

    internal static string? ParseDigest(string? digest) =>
        digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && IsSha256(d[7..]) ? d[7..].ToLowerInvariant() : null;

    /// <summary>Reads "hash  name" / "hash *name" lines (sha256sum format).</summary>
    internal static string? ParseChecksumFile(string text, string fileName)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOfAny([' ', '\t']);
            if (space <= 0) continue;
            var hash = line[..space];
            var name = line[space..].Trim().TrimStart('*');
            if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase) && IsSha256(hash)) return hash.ToLowerInvariant();
        }
        return null;
    }

    private static bool IsSha256(string s) => s.Length == 64 && s.All(char.IsAsciiHexDigit);

    /// <summary>The file must be a Nexstrap build of exactly the release's version.</summary>
    internal static void VerifyIdentity(string path, Version expected)
    {
        FileVersionInfo info;
        try { info = FileVersionInfo.GetVersionInfo(path); }
        catch (Exception ex) { throw new UpdateException("The download isn't a valid program.", ex); }

        if (!string.Equals(info.ProductName, AppInfo.Name, StringComparison.Ordinal))
            throw new UpdateException("The downloaded file isn't Nexstrap. It was deleted.");
        var fileVersion = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
        if (fileVersion != new Version(expected.Major, expected.Minor, Math.Max(expected.Build, 0)))
            throw new UpdateException($"The downloaded file is version {fileVersion}, not {expected}. It was deleted.");
    }

    // ===== IO =====

    private async Task<string> DownloadAsync(GitHubAsset asset, string target, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        try
        {
            long done = 0;
            var (hash, bytes) = await HashingDownload.ToFileAsync(_http, asset.DownloadUrl, target, HashAlgorithmName.SHA256,
                maxBytes: asset.Size, read =>
                {
                    done += read;
                    progress?.Report(new UpdateProgress("Downloading update…", done, asset.Size));
                }, ct);
            if (bytes != asset.Size)
                throw new UpdateException("The download is incomplete. Try again.");
            return hash;
        }
        catch (InvalidDataException)
        {
            TryDelete(target);
            throw new UpdateException("The download is larger than the release says. It was stopped.");
        }
        catch (HttpRequestException ex)
        {
            TryDelete(target);
            throw new UpdateException("Couldn't download the update: " + ex.Message, ex);
        }
        catch
        {
            TryDelete(target);
            throw;
        }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        try { return await _http.GetStringAsync(url, ct); }
        catch (HttpRequestException ex) { throw new UpdateException("Couldn't download the release's checksums: " + ex.Message, ex); }
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

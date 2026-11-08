using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;

namespace Nexstrap.Services.Roblox;

/// <summary>The current live Roblox Player build, as published by Roblox.</summary>
public sealed record ClientVersionInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("clientVersionUpload")] string VersionHash);

/// <summary>One line group in rbxPkgManifest.txt.</summary>
public sealed record PackageEntry(string Name, string Md5, long PackedSize, long UnpackedSize);

public sealed record InstallProgress(string Stage, long BytesDone, long BytesTotal)
{
    public double Fraction => BytesTotal <= 0 ? 0 : Math.Clamp((double)BytesDone / BytesTotal, 0, 1);
}

public sealed class InstallException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What to download from Roblox's deployment CDN: the Player or Studio.</summary>
public sealed record RobloxProduct(string BinaryType, string Executable, string ProgressKey,
    IReadOnlyDictionary<string, string> PackageDirectories, IReadOnlySet<string> IgnoredPackages)
{
    public string VersionEndpoint => "https://clientsettingscdn.roblox.com/v2/client-version/" + BinaryType;

    public static RobloxProduct Player { get; } = new("WindowsPlayer", RobloxLocator.PlayerExecutable, "Launch.DownloadingRoblox",
        RobloxDeployment.PackageDirectories, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RobloxPlayerInstaller.exe" });

    public static RobloxProduct Studio { get; } = new("WindowsStudio64", StudioLocator.StudioExecutable, "Studio.Downloading",
        RobloxDeployment.StudioPackageDirectories, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "RobloxStudioInstaller.exe" });
}

public interface IRobloxDeployment
{
    Task<ClientVersionInfo> GetLatestVersionAsync(CancellationToken ct = default);

    /// <summary>Downloads, verifies (MD5) and extracts a build into Nexstrap's Versions folder.</summary>
    Task<string> InstallAsync(ClientVersionInfo version, IProgress<InstallProgress>? progress, CancellationToken ct = default);

    /// <summary>Directory a Nexstrap-managed build lives in.</summary>
    string DirectoryFor(string versionHash);
}

/// <summary>
/// Installs official, unmodified Roblox Player builds from Roblox's own deployment CDN
/// (the same files the official installer uses). Every package is MD5-verified against
/// Roblox's manifest before extraction; nothing is executed during installation.
/// </summary>
public sealed class RobloxDeployment : IRobloxDeployment
{
    public static string VersionEndpoint => RobloxProduct.Player.VersionEndpoint;
    public const string CdnBase = "https://setup.rbxcdn.com/";
    public const string CompleteMarker = ".nexstrap-complete";

    /// <summary>Where each package is extracted, relative to the version folder.</summary>
    internal static readonly IReadOnlyDictionary<string, string> PackageDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxApp.zip"] = "",
        ["WebView2.zip"] = "",
        ["shaders.zip"] = "shaders",
        ["ssl.zip"] = "ssl",
        ["WebView2RuntimeInstaller.zip"] = "WebView2RuntimeInstaller",
        ["content-avatar.zip"] = @"content\avatar",
        ["content-configs.zip"] = @"content\configs",
        ["content-fonts.zip"] = @"content\fonts",
        ["content-sky.zip"] = @"content\sky",
        ["content-sounds.zip"] = @"content\sounds",
        ["content-textures2.zip"] = @"content\textures",
        ["content-models.zip"] = @"content\models",
        ["content-platform-fonts.zip"] = @"PlatformContent\pc\fonts",
        ["content-platform-dictionaries.zip"] = @"PlatformContent\pc\shared_compression_dictionaries",
        ["content-terrain.zip"] = @"PlatformContent\pc\terrain",
        ["content-textures3.zip"] = @"PlatformContent\pc\textures",
        ["extracontent-luapackages.zip"] = @"ExtraContent\LuaPackages",
        ["extracontent-translations.zip"] = @"ExtraContent\translations",
        ["extracontent-models.zip"] = @"ExtraContent\models",
        ["extracontent-textures.zip"] = @"ExtraContent\textures",
        ["extracontent-places.zip"] = @"ExtraContent\places",
    };

    /// <summary>Studio's packages, checked against Roblox's own Studio install (0.745/0.746).</summary>
    internal static readonly IReadOnlyDictionary<string, string> StudioPackageDirectories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RobloxStudio.zip"] = "",
        ["Libraries.zip"] = "",
        ["LibrariesQt5.zip"] = "",
        ["redist.zip"] = "",
        ["WebView2.zip"] = "",
        ["ApplicationConfig.zip"] = "ApplicationConfig",
        ["BuiltInPlugins.zip"] = "BuiltInPlugins",
        ["BuiltInStandalonePlugins.zip"] = "BuiltInStandalonePlugins",
        ["Plugins.zip"] = "Plugins",
        ["StudioFonts.zip"] = "StudioFonts",
        ["shaders.zip"] = "shaders",
        ["ssl.zip"] = "ssl",
        ["WebView2RuntimeInstaller.zip"] = "WebView2RuntimeInstaller",
        ["content-avatar.zip"] = @"content\avatar",
        ["content-configs.zip"] = @"content\configs",
        ["content-fonts.zip"] = @"content\fonts",
        ["content-models.zip"] = @"content\models",
        ["content-qt_translations.zip"] = @"content\qt_translations",
        ["content-sky.zip"] = @"content\sky",
        ["content-sounds.zip"] = @"content\sounds",
        ["content-textures2.zip"] = @"content\textures",
        ["content-studio_svg_textures.zip"] = @"content\studio_svg_textures",
        ["content-api-docs.zip"] = @"content\api_docs",
        ["content-platform-fonts.zip"] = @"PlatformContent\pc\fonts",
        ["content-platform-dictionaries.zip"] = @"PlatformContent\pc\shared_compression_dictionaries",
        ["content-terrain.zip"] = @"PlatformContent\pc\terrain",
        ["content-textures3.zip"] = @"PlatformContent\pc\textures",
        ["extracontent-scripts.zip"] = @"ExtraContent\scripts",
        ["extracontent-luapackages.zip"] = @"ExtraContent\LuaPackages",
        ["extracontent-translations.zip"] = @"ExtraContent\translations",
        ["extracontent-models.zip"] = @"ExtraContent\models",
        ["extracontent-textures.zip"] = @"ExtraContent\textures",
        ["studiocontent-models.zip"] = @"StudioContent\models",
        ["studiocontent-textures.zip"] = @"StudioContent\textures",
    };

    private const string AppSettingsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Settings>\r\n\t<ContentFolder>content</ContentFolder>\r\n\t<BaseUrl>http://www.roblox.com</BaseUrl>\r\n</Settings>\r\n";

    private const int MaxParallelDownloads = 4;

    private readonly HttpClient _http;
    private readonly ILogger<RobloxDeployment> _logger;
    private readonly string _versionsRoot;
    private readonly string _downloadsRoot;
    private readonly RobloxProduct _product;

    public RobloxDeployment(HttpClient http, ILogger<RobloxDeployment> logger)
        : this(http, logger, AppInfo.Paths.Versions, AppInfo.Paths.Downloads) { }

    internal RobloxDeployment(HttpClient http, ILogger<RobloxDeployment> logger, string versionsRoot, string downloadsRoot, RobloxProduct? product = null)
    {
        _http = http;
        _logger = logger;
        _versionsRoot = versionsRoot;
        _downloadsRoot = downloadsRoot;
        _product = product ?? RobloxProduct.Player;
    }

    /// <summary>Roblox Studio, kept in its own folder so Player detection and cleanup never see it.</summary>
    public static RobloxDeployment ForStudio(HttpClient http, ILogger<RobloxDeployment> logger) =>
        new(http, logger, AppInfo.Paths.Studio, AppInfo.Paths.Downloads, RobloxProduct.Studio);

    public string DirectoryFor(string versionHash) => Path.Combine(_versionsRoot, versionHash);

    public async Task<ClientVersionInfo> GetLatestVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var info = await _http.GetFromJsonAsync<ClientVersionInfo>(_product.VersionEndpoint, ct);
            if (info is null || !IsValidHash(info.VersionHash))
                throw new InstallException("Roblox returned an unexpected version response.");
            return info;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            throw new InstallException(Loc.Get("Launch.CouldntReachRobloxToCheckThe"), ex);
        }
    }

    internal static bool IsValidHash(string? hash) =>
        hash is { Length: > 8 and < 64 } && hash.StartsWith("version-", StringComparison.Ordinal)
        && hash[8..].All(char.IsAsciiLetterOrDigit);

    /// <summary>Parses "v0" manifests: name, md5, packed size, unpacked size — four lines per package.</summary>
    internal static IReadOnlyList<PackageEntry> ParseManifest(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || lines[0] != "v0")
            throw new InstallException("Roblox's package manifest uses a format Nexstrap doesn't understand yet.");
        if ((lines.Length - 1) % 4 != 0)
            throw new InstallException("Roblox's package manifest is incomplete.");

        var packages = new List<PackageEntry>();
        for (var i = 1; i < lines.Length; i += 4)
        {
            if (!long.TryParse(lines[i + 2], out var packed) || !long.TryParse(lines[i + 3], out var unpacked)
                || lines[i + 1].Length != 32 || !lines[i + 1].All(char.IsAsciiHexDigit))
                throw new InstallException($"Roblox's package manifest has an invalid entry for {lines[i]}.");
            packages.Add(new PackageEntry(lines[i], lines[i + 1].ToLowerInvariant(), packed, unpacked));
        }
        return packages;
    }

    public async Task<string> InstallAsync(ClientVersionInfo version, IProgress<InstallProgress>? progress, CancellationToken ct = default)
    {
        if (!IsValidHash(version.VersionHash))
            throw new InstallException("Invalid Roblox version identifier.");

        var target = DirectoryFor(version.VersionHash);
        if (File.Exists(Path.Combine(target, CompleteMarker)))
        {
            _logger.LogInformation("{Hash} is already installed", version.VersionHash);
            return target;
        }

        progress?.Report(new InstallProgress(Loc.Get("Launch.ReadingPackageList"), 0, 0));
        var manifestText = await GetStringAsync($"{CdnBase}{version.VersionHash}-rbxPkgManifest.txt", ct);
        var packages = ParseManifest(manifestText).Where(p => !_product.IgnoredPackages.Contains(p.Name)).ToList();

        var unknown = packages.Where(p => !_product.PackageDirectories.ContainsKey(p.Name)).Select(p => p.Name).ToList();
        if (unknown.Count > 0)
            throw new InstallException(
                $"Roblox added packages Nexstrap doesn't know how to place yet ({string.Join(", ", unknown)}). " +
                "Install it from roblox.com for now; a Nexstrap update will add support.");

        // Staging folder name deliberately doesn't start with "version-" so detection ignores it.
        var staging = Path.Combine(_versionsRoot, "_staging-" + version.VersionHash);
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(_downloadsRoot);

        var total = packages.Sum(p => p.PackedSize);
        long done = 0;
        var extractLock = new SemaphoreSlim(1, 1);
        var downloadSlots = new SemaphoreSlim(MaxParallelDownloads);

        try
        {
            var tasks = packages.Select(async package =>
            {
                await downloadSlots.WaitAsync(ct);
                string zip;
                try
                {
                    zip = await DownloadVerifiedAsync(version.VersionHash, package, bytes =>
                    {
                        var now = Interlocked.Add(ref done, bytes);
                        progress?.Report(new InstallProgress(Loc.Get(_product.ProgressKey), now, total));
                    }, ct);
                }
                finally
                {
                    downloadSlots.Release();
                }

                await extractLock.WaitAsync(ct);
                try
                {
                    await Task.Run(() => ExtractSafely(zip, Path.Combine(staging, _product.PackageDirectories[package.Name])), ct);
                }
                finally
                {
                    extractLock.Release();
                }
            });
            await Task.WhenAll(tasks);

            progress?.Report(new InstallProgress(Loc.Get("Launch.FinishingUp"), total, total));
            await File.WriteAllTextAsync(Path.Combine(staging, "AppSettings.xml"), AppSettingsXml, ct);

            if (!File.Exists(Path.Combine(staging, _product.Executable)))
                throw new InstallException($"The downloaded build doesn't contain {_product.Executable}.");

            await File.WriteAllTextAsync(Path.Combine(staging, CompleteMarker), $"{version.Version}\n{DateTime.UtcNow:O}\n", ct);

            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.Move(staging, target);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }

        // Package zips are only needed until extraction succeeds.
        foreach (var p in packages) TryDeleteFile(Path.Combine(_downloadsRoot, p.Md5 + ".zip"));

        _logger.LogInformation("Installed Roblox {Version} ({Hash}) to {Dir}", version.Version, version.VersionHash, target);
        return target;
    }

    private async Task<string> DownloadVerifiedAsync(string hash, PackageEntry package, Action<long> onBytes, CancellationToken ct)
    {
        var path = Path.Combine(_downloadsRoot, package.Md5 + ".zip");
        if (File.Exists(path) && await Md5OfFileAsync(path, ct) == package.Md5)
        {
            onBytes(package.PackedSize);
            return path;
        }

        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            long reported = 0;
            try
            {
                var (actual, _) = await HashingDownload.ToFileAsync(_http, $"{CdnBase}{hash}-{package.Name}", path,
                    HashAlgorithmName.MD5, maxBytes: null, bytes => { reported += bytes; onBytes(bytes); }, ct);
                if (actual == package.Md5) return path;

                throw new InstallException($"{package.Name} failed its integrity check (expected {package.Md5}, got {actual}).");
            }
            catch (Exception ex) when (attempt < attempts && !ct.IsCancellationRequested && ex is HttpRequestException or IOException or InstallException)
            {
                _logger.LogWarning(ex, "Download of {Package} failed (attempt {Attempt}); retrying", package.Name, attempt);
                onBytes(-reported);
                TryDeleteFile(path);
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
            catch (HttpRequestException ex)
            {
                TryDeleteFile(path);
                throw new InstallException($"Couldn't download {package.Name}: {ex.Message}", ex);
            }
            catch
            {
                TryDeleteFile(path);
                throw;
            }
        }
    }

    /// <summary>Extracts a zip, refusing any entry that would land outside <paramref name="destination"/>.</summary>
    internal static void ExtractSafely(string zipPath, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var relative = entry.FullName.Replace('/', '\\').TrimStart('\\');
            var full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InstallException($"A Roblox package contained an unsafe path ({entry.FullName}).");

            if (relative.EndsWith('\\') || entry.Name.Length == 0)
            {
                Directory.CreateDirectory(full);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            entry.ExtractToFile(full, overwrite: true);
        }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        try
        {
            return await _http.GetStringAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new InstallException("Couldn't download Roblox's package list: " + ex.Message, ex);
        }
    }

    private static async Task<string> Md5OfFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await MD5.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    private void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not remove {Dir}", dir); }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { }
    }
}

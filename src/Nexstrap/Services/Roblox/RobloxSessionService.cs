using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Nexstrap.Core;

namespace Nexstrap.Services.Roblox;

public enum RobloxActivityKind
{
    /// <summary>Process started, menu not shown yet.</summary>
    Starting,
    /// <summary>In the Roblox app (home, avatar, friends…), not in a game.</summary>
    Menu,
    InGame
}

public sealed record RobloxActivity(RobloxActivityKind Kind, DateTime SinceUtc, long? PlaceId = null, long? UniverseId = null,
    string? JobId = null, long? UserId = null, string? ServerAddress = null);

/// <summary>Roblox stopped without closing normally. PlaceId is set if it happened during a game.</summary>
public sealed record RobloxCrash(long? PlaceId, int? ExitCode, string LogPath);

/// <summary>
/// Reads the Roblox Player's own log lines (verified against 0.745 logs):
///   "[FLog::SingleSurfaceApp] setStage: (stage:LuaApp)"          → menu
///   "[FLog::Output] ! Joining game '{job}' place {placeId} at …"  → in game
///   "[FLog::Network] UDMUX Address = 128.116.44.33, Port = …"     → the server's public address
///   "[FLog::GameJoinLoadTime] Report game_join_loadtime: placeid:…, universeid:…, …, userid:…"
///   "[FLog::SingleSurfaceApp] leaveUGCGameInternal"               → back to menu
/// </summary>
public static partial class RobloxLogParser
{
    /// <summary>Returns the new activity, or null if the line changes nothing.</summary>
    public static RobloxActivity? Apply(RobloxActivity current, string line)
    {
        if (line.Contains("[FLog::SingleSurfaceApp] setStage: (stage:LuaApp)", StringComparison.Ordinal)
            || line.Contains("[FLog::SingleSurfaceApp] leaveUGCGameInternal", StringComparison.Ordinal))
            return current.Kind == RobloxActivityKind.Menu ? null : new RobloxActivity(RobloxActivityKind.Menu, Timestamp(line));

        var join = JoiningGame().Match(line);
        if (join.Success && long.TryParse(join.Groups[2].Value, out var place))
            return new RobloxActivity(RobloxActivityKind.InGame, Timestamp(line), place, null, join.Groups[1].Value);

        var server = ServerAddress().Match(line);
        if (server.Success && current.Kind == RobloxActivityKind.InGame && current.ServerAddress != server.Groups[1].Value)
            return current with { ServerAddress = server.Groups[1].Value };

        var load = JoinLoadTime().Match(line);
        if (load.Success && current.Kind == RobloxActivityKind.InGame
            && long.TryParse(load.Groups[1].Value, out var loadPlace) && loadPlace == current.PlaceId
            && long.TryParse(load.Groups[2].Value, out var universe))
        {
            long? user = long.TryParse(load.Groups[3].Value, out var u) ? u : current.UserId;
            if (current.UniverseId != universe || current.UserId != user)
                return current with { UniverseId = universe, UserId = user };
        }

        return null;
    }

    /// <summary>Every normal close in real logs ends with these lines; a process that was cut off never writes them.</summary>
    public static bool ClosedNormally(IEnumerable<string> lastLines) =>
        lastLines.Any(l => l.Contains("[FLog::SingleSurfaceApp] shutDown:", StringComparison.Ordinal)
                           || l.Contains("[DFLog::SystemDialogHandler] Platform handler was destroyed", StringComparison.Ordinal));

    /// <summary>Needs both signs, because a false alarm after a normal close is worse than a missed crash.</summary>
    public static bool IsCrash(int? exitCode, IEnumerable<string> lastLines) => exitCode != 0 && !ClosedNormally(lastLines);

    internal static DateTime Timestamp(string line)
    {
        var comma = line.IndexOf(',');
        return comma > 0 && DateTime.TryParse(line[..comma], CultureInfo.InvariantCulture,
                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t
            : DateTime.UtcNow;
    }

    [GeneratedRegex(@"! Joining game '([0-9a-fA-F-]+)' place (\d+)")]
    private static partial Regex JoiningGame();

    [GeneratedRegex(@"\[FLog::Network\] UDMUX Address = (\d+\.\d+\.\d+\.\d+)")]
    private static partial Regex ServerAddress();

    [GeneratedRegex(@"Report game_join_loadtime: placeid:(\d+),.*?universeid:(\d+)(?:.*?userid:(\d+))?")]
    private static partial Regex JoinLoadTime();
}

public interface IRobloxSessionService
{
    /// <summary>True while a Roblox process launched by Nexstrap is running.</summary>
    bool IsActive { get; }

    RobloxActivity? Current { get; }

    /// <summary>When Roblox was opened. Unlike Current.SinceUtc, this doesn't change when joining or leaving a game.</summary>
    DateTime? StartedUtc { get; }

    /// <summary>Raised on activity changes and when the session ends (Current becomes null). Background thread.</summary>
    event EventHandler? Changed;

    /// <summary>Raised after the session ended, on a background thread. WaitForEndAsync waits for the handlers.</summary>
    event EventHandler<RobloxCrash>? Crashed;

    /// <summary>Start following a Roblox process Nexstrap just launched.</summary>
    void Track(int processId, DateTime launchedAtUtc);

    /// <summary>Completes when the tracked session ends (immediately if none).</summary>
    Task WaitForEndAsync();
}

/// <summary>Follows one Roblox Player process and its log file until it exits.</summary>
public sealed class RobloxSessionService(ILogger<RobloxSessionService> logger) : IRobloxSessionService
{
    private static readonly string LogsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "logs");

    /// <summary>In real logs the closing lines sit within the last 60 or so.</summary>
    private const int CrashCheckLines = 200;

    private CancellationTokenSource? _cts;
    private Task _run = Task.CompletedTask;

    public bool IsActive => Current is not null;

    public RobloxActivity? Current { get; private set; }

    public DateTime? StartedUtc { get; private set; }

    public event EventHandler? Changed;

    public event EventHandler<RobloxCrash>? Crashed;

    public void Track(int processId, DateTime launchedAtUtc)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        StartedUtc = launchedAtUtc;
        Set(new RobloxActivity(RobloxActivityKind.Starting, launchedAtUtc));
        _run = Task.Run(() => RunAsync(processId, launchedAtUtc, token), token);
    }

    public Task WaitForEndAsync() => _run;

    private async Task RunAsync(int processId, DateTime launchedAtUtc, CancellationToken ct)
    {
        RobloxCrash? crash = null;
        try
        {
            using var process = await ResolveProcessAsync(processId, ct);
            if (process is null)
            {
                logger.LogInformation("Roblox exited before its session could be tracked");
                return;
            }

            var log = await FindLogAsync(launchedAtUtc, ct);
            logger.LogInformation("Tracking Roblox pid {Pid}, log {Log}", process.Id, log is null ? "(none)" : Path.GetFileName(log));

            if (log is null)
            {
                // No log means no menu/game detail; still keep a generic session while Roblox runs.
                Set(new RobloxActivity(RobloxActivityKind.Menu, launchedAtUtc));
                await process.WaitForExitAsync(ct);
                return;
            }

            await TailAsync(log, process, ct);
            crash = await CheckForCrashAsync(log, process, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Roblox session tracking failed");
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                logger.LogInformation("Roblox session ended");
                Set(null);
                if (crash is not null) RaiseCrashed(crash);
            }
        }
    }

    /// <summary>Only for sessions that got as far as the menu, since a log with nothing in it says nothing about how Roblox closed.</summary>
    private async Task<RobloxCrash?> CheckForCrashAsync(string log, Process process, CancellationToken ct)
    {
        if (Current is not { Kind: not RobloxActivityKind.Starting } last) return null;

        await Task.Delay(1000, ct); // the last lines can land just after the process has gone

        var running = Process.GetProcessesByName("RobloxPlayerBeta");
        var stillOpen = running.Length > 0;
        foreach (var other in running) other.Dispose();
        if (stillOpen) return null;

        int? exitCode = null;
        try { exitCode = process.ExitCode; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            logger.LogDebug(ex, "Roblox's exit code isn't available");
        }

        IReadOnlyList<string> lines;
        try { lines = LogReader.Tail(log); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't read Roblox's log to see how it closed");
            return null;
        }

        var crashed = RobloxLogParser.IsCrash(exitCode, lines.TakeLast(CrashCheckLines));
        logger.LogInformation("Roblox closed with exit code {Code}, crashed: {Crashed}", exitCode, crashed);
        return crashed ? new RobloxCrash(last.Kind == RobloxActivityKind.InGame ? last.PlaceId : null, exitCode, log) : null;
    }

    private void RaiseCrashed(RobloxCrash crash)
    {
        try { Crashed?.Invoke(this, crash); }
        catch (Exception ex) { logger.LogError(ex, "Handling Roblox's crash failed"); }
    }

    /// <summary>
    /// The process we started may hand over to an already-running client and exit;
    /// in that case follow the client that's still running.
    /// </summary>
    private static async Task<Process?> ResolveProcessAsync(int processId, CancellationToken ct)
    {
        try
        {
            var started = Process.GetProcessById(processId);
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            if (!started.HasExited) return started;
            started.Dispose();
        }
        catch (ArgumentException) { /* already gone */ }

        return Process.GetProcessesByName("RobloxPlayerBeta").OrderByDescending(p => p.StartTime).FirstOrDefault();
    }

    private static async Task<string?> FindLogAsync(DateTime launchedAtUtc, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (Directory.Exists(LogsDir))
            {
                var log = new DirectoryInfo(LogsDir).GetFiles("*_Player_*_last.log")
                    .Where(f => !f.Name.Contains("CrashHandler", StringComparison.OrdinalIgnoreCase))
                    .Where(f => f.CreationTimeUtc >= launchedAtUtc.AddSeconds(-10))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .FirstOrDefault();
                if (log is not null) return log.FullName;
            }
            await Task.Delay(1000, ct);
        }
        return null;
    }

    private async Task TailAsync(string path, Process process, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var pending = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var chunk = await reader.ReadToEndAsync(ct);
            if (chunk.Length > 0)
            {
                pending.Append(chunk);
                var text = pending.ToString();
                var lastNewline = text.LastIndexOf('\n');
                if (lastNewline >= 0)
                {
                    foreach (var line in text[..lastNewline].Split('\n'))
                        if (Current is { } current && RobloxLogParser.Apply(current, line) is { } next)
                        {
                            logger.LogInformation("Roblox activity: {Kind} {Place}", next.Kind, next.PlaceId);
                            Set(next);
                        }
                    pending.Remove(0, lastNewline + 1);
                }
            }
            else if (process.HasExited)
            {
                return;
            }
            else
            {
                await Task.Delay(750, ct);
            }
        }
    }

    private void Set(RobloxActivity? activity)
    {
        Current = activity;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

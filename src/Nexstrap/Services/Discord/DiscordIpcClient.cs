using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Nexstrap.Services.Discord;

public enum DiscordConnectionState
{
    Disconnected,
    /// <summary>Discord desktop isn't running (no IPC pipe).</summary>
    DiscordNotRunning,
    /// <summary>Discord refused the handshake, usually an invalid application ID.</summary>
    Rejected,
    Connected
}

public sealed class DiscordException(string message) : Exception(message);

public interface IDiscordIpcClient : IDisposable
{
    DiscordConnectionState State { get; }

    /// <summary>Discord's last error message, if any.</summary>
    string? LastError { get; }

    Task<DiscordConnectionState> ConnectAsync(string applicationId, CancellationToken ct = default);

    /// <summary>Sets (or with null clears) the activity shown on the user's profile.</summary>
    Task SetActivityAsync(JsonObject? activity, CancellationToken ct = default);

    void Disconnect();
}

/// <summary>
/// Minimal client for Discord's local RPC pipe (\\.\pipe\discord-ipc-N).
/// Frames are [int32 opcode][int32 length][UTF-8 JSON], little-endian.
/// Only SET_ACTIVITY is used; nothing is read from the user's Discord account.
/// </summary>
public sealed class DiscordIpcClient : IDiscordIpcClient
{
    private const int OpHandshake = 0, OpFrame = 1, OpClose = 2;
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<DiscordIpcClient> logger;
    private readonly string _pipePrefix;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;

    public DiscordIpcClient(ILogger<DiscordIpcClient> logger) : this(logger, "discord-ipc-") { }

    /// <summary>Tests point this at their own fake pipe so they never touch the real Discord.</summary>
    internal DiscordIpcClient(ILogger<DiscordIpcClient> logger, string pipePrefix)
    {
        this.logger = logger;
        _pipePrefix = pipePrefix;
    }

    public DiscordConnectionState State { get; private set; } = DiscordConnectionState.Disconnected;

    public string? LastError { get; private set; }

    public async Task<DiscordConnectionState> ConnectAsync(string applicationId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            CloseUnlocked();
            LastError = null;

            for (var i = 0; i < 10; i++)
            {
                var pipe = new NamedPipeClientStream(".", $"{_pipePrefix}{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(300, ct);
                }
                catch (Exception ex) when (ex is TimeoutException or IOException)
                {
                    await pipe.DisposeAsync();
                    continue;
                }

                _pipe = pipe;
                await WriteFrameAsync(OpHandshake, new JsonObject { ["v"] = 1, ["client_id"] = applicationId }, ct);
                var (op, reply) = await ReadFrameAsync(ct);

                if (op == OpFrame && reply?["evt"]?.GetValue<string>() == "READY")
                {
                    logger.LogInformation("Connected to Discord on pipe {Index}", i);
                    return State = DiscordConnectionState.Connected;
                }

                LastError = reply?["message"]?.GetValue<string>() ?? "Discord closed the connection.";
                logger.LogWarning("Discord rejected the handshake: {Error}", LastError);
                CloseUnlocked();
                return State = DiscordConnectionState.Rejected;
            }

            return State = DiscordConnectionState.DiscordNotRunning;
        }
        catch (Exception ex) when (ex is IOException or JsonException or DiscordException or TimeoutException)
        {
            LastError = ex.Message;
            logger.LogWarning(ex, "Discord connection failed");
            CloseUnlocked();
            return State = DiscordConnectionState.Disconnected;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetActivityAsync(JsonObject? activity, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_pipe is null || State != DiscordConnectionState.Connected)
                throw new DiscordException("Not connected to Discord.");

            var payload = new JsonObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = activity },
                ["nonce"] = Guid.NewGuid().ToString()
            };
            await WriteFrameAsync(OpFrame, payload, ct);
            var (op, reply) = await ReadFrameAsync(ct);

            if (op == OpClose || reply?["evt"]?.GetValue<string>() == "ERROR")
            {
                LastError = reply?["data"]?["message"]?.GetValue<string>() ?? reply?["message"]?.GetValue<string>() ?? "Unknown Discord error";
                throw new DiscordException(LastError);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Discord was closed or restarted.
            LastError = "Lost the connection to Discord.";
            CloseUnlocked();
            State = DiscordConnectionState.Disconnected;
            throw new DiscordException(LastError);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Disconnect()
    {
        _gate.Wait();
        try
        {
            CloseUnlocked();
            State = DiscordConnectionState.Disconnected;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteFrameAsync(int opcode, JsonObject body, CancellationToken ct)
    {
        var json = Encoding.UTF8.GetBytes(body.ToJsonString());
        var frame = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0), opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), json.Length);
        json.CopyTo(frame, 8);
        await _pipe!.WriteAsync(frame, ct);
        await _pipe.FlushAsync(ct);
    }

    private async Task<(int Op, JsonNode? Body)> ReadFrameAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReplyTimeout);
        try
        {
            var header = new byte[8];
            await _pipe!.ReadExactlyAsync(header, timeout.Token);
            var op = BinaryPrimitives.ReadInt32LittleEndian(header);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            if (length is < 0 or > 1 << 20) throw new DiscordException("Discord sent an invalid frame.");

            var body = new byte[length];
            await _pipe.ReadExactlyAsync(body, timeout.Token);
            return (op, length == 0 ? null : JsonNode.Parse(body));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("Discord didn't answer in time.");
        }
        catch (EndOfStreamException)
        {
            throw new IOException("Discord closed the connection.");
        }
    }

    private void CloseUnlocked()
    {
        _pipe?.Dispose();
        _pipe = null;
    }

    public void Dispose()
    {
        CloseUnlocked();
        _gate.Dispose();
    }
}

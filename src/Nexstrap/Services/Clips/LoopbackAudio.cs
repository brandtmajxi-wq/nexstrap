using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Nexstrap.Services.Clips;

/// <summary>
/// Records what the PC is playing (the default speakers or headphones), the way Windows lets any app do it
/// ("loopback"). The sound is turned into 16-bit stereo and kept in a short buffer for the clip recorder.
/// Windows sends nothing while the PC is silent, so the reader fills gaps with silence.
/// </summary>
internal sealed class LoopbackAudio : IDisposable
{
    private const int MaxBufferedSeconds = 2;

    private readonly ILogger _logger;
    private readonly Thread _thread;
    private readonly object _gate = new();
    private short[] _ring = [];
    private int _start, _count;
    private volatile bool _stop;
    private readonly ManualResetEventSlim _ready = new();

    public int SampleRate { get; private set; } = 48000;

    /// <summary>False if Windows wouldn't give Nexstrap the sound; clips then have none.</summary>
    public bool Working { get; private set; }

    public LoopbackAudio(ILogger logger)
    {
        _logger = logger;
        _thread = new Thread(Run) { IsBackground = true, Name = "Nexstrap clip audio" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
    }

    /// <summary>Copies up to <paramref name="destination"/>.Length samples (left, right, left, …) and returns how many there were.</summary>
    public int Read(Span<short> destination)
    {
        lock (_gate)
        {
            var n = Math.Min(destination.Length, _count) & ~1;
            for (var i = 0; i < n; i++) destination[i] = _ring[(_start + i) % _ring.Length];
            _start = (_start + n) % Math.Max(1, _ring.Length);
            _count -= n;
            return n;
        }
    }

    /// <summary>Throws away what's buffered, so a new clip piece starts in time with the picture.</summary>
    public void Clear()
    {
        lock (_gate) { _start = 0; _count = 0; }
    }

    private void Write(ReadOnlySpan<short> samples)
    {
        lock (_gate)
        {
            foreach (var s in samples)
            {
                if (_count == _ring.Length) { _start = (_start + 1) % _ring.Length; _count--; } // too far behind: drop the oldest
                _ring[(_start + _count) % _ring.Length] = s;
                _count++;
            }
        }
    }

    private void Run()
    {
        IAudioClient? client = null;
        IntPtr mix = IntPtr.Zero;
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Check(enumerator.GetDefaultAudioEndpoint(0 /* render */, 0 /* console */, out var device));
            var iid = typeof(IAudioClient).GUID;
            Check(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var activated));
            client = (IAudioClient)activated;
            Check(client.GetMixFormat(out mix));

            var format = ReadFormat(mix);
            if (format is null)
            {
                _logger.LogWarning("Clips: the speakers use a sound format Nexstrap can't read, so clips have no sound");
                return;
            }
            var (channels, rate, kind, bytesPerSample) = format.Value;
            SampleRate = rate;
            lock (_gate) _ring = new short[rate * 2 * MaxBufferedSeconds];

            Check(client.Initialize(0 /* shared */, 0x00020000 /* loopback */, 10_000_000, 0, mix, IntPtr.Zero));
            var captureIid = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref captureIid, out var service));
            var capture = (IAudioCaptureClient)service;
            Check(client.Start());
            Working = true;
            _ready.Set();

            var stereo = new short[rate]; // half a second of stereo at most per packet
            while (!_stop)
            {
                Thread.Sleep(10);
                while (!_stop && capture.GetNextPacketSize(out var packet) == 0 && packet > 0)
                {
                    if (capture.GetBuffer(out var data, out var frames, out var flags, out _, out _) != 0) break;
                    var n = (int)Math.Min(frames, (uint)(stereo.Length / 2));
                    var silent = (flags & 0x2) != 0;
                    for (var f = 0; f < n; f++)
                    {
                        for (var c = 0; c < 2; c++)
                        {
                            var ch = Math.Min(c, channels - 1);
                            stereo[f * 2 + c] = silent ? (short)0 : SampleAt(data, (f * channels + ch) * bytesPerSample, kind);
                        }
                    }
                    capture.ReleaseBuffer(frames);
                    Write(stereo.AsSpan(0, n * 2));
                }
            }
            client.Stop();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Clips: couldn't record the PC's sound, so clips have no sound");
        }
        finally
        {
            if (mix != IntPtr.Zero) Marshal.FreeCoTaskMem(mix);
            if (client is not null) Marshal.ReleaseComObject(client);
            _ready.Set();
        }
    }

    private enum SampleKind { Float32, Int16, Int32 }

    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PcmSubtype = new("00000001-0000-0010-8000-00aa00389b71");

    /// <summary>Reads WAVEFORMATEX / WAVEFORMATEXTENSIBLE: channels, rate and how each sample is stored.</summary>
    private static (int Channels, int Rate, SampleKind Kind, int BytesPerSample)? ReadFormat(IntPtr format)
    {
        var tag = (ushort)Marshal.ReadInt16(format, 0);
        var channels = (ushort)Marshal.ReadInt16(format, 2);
        var rate = Marshal.ReadInt32(format, 4);
        var bits = (ushort)Marshal.ReadInt16(format, 14);
        if (tag == 0xFFFE)
        {
            var sub = Marshal.PtrToStructure<Guid>(format + 24);
            tag = sub == FloatSubtype ? (ushort)3 : sub == PcmSubtype ? (ushort)1 : (ushort)0;
        }
        return (tag, bits) switch
        {
            (3, 32) => (channels, rate, SampleKind.Float32, 4),
            (1, 16) => (channels, rate, SampleKind.Int16, 2),
            (1, 32) => (channels, rate, SampleKind.Int32, 4),
            _ => null
        };
    }

    private static short SampleAt(IntPtr data, int offset, SampleKind kind) => kind switch
    {
        SampleKind.Float32 => (short)Math.Clamp(BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, offset)) * 32767f, -32768f, 32767f),
        SampleKind.Int16 => Marshal.ReadInt16(data, offset),
        _ => (short)(Marshal.ReadInt32(data, offset) >> 16)
    };

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    // ===== Windows' audio interfaces (only the methods used, in their real order) =====

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}

using System.Diagnostics;
using System.Threading.Channels;
using Windows.Graphics.Capture;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Nexstrap.Services.Clips;

/// <summary>
/// A few seconds of the Roblox window and the PC's sound, encoded straight to an MP4 file by the graphics card
/// (through Windows' own encoder). The recorder keeps several of these and joins the newest when you save a clip.
/// Picture and sound are timed from the same clock, so they stay together.
/// </summary>
internal sealed class ClipSegment
{
    private static readonly TimeSpan AudioChunk = TimeSpan.FromMilliseconds(20);

    private readonly Channel<Direct3D11CaptureFrame> _frames = Channel.CreateBounded<Direct3D11CaptureFrame>(
        new BoundedChannelOptions(3) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly LoopbackAudio? _audio;
    private readonly long _start; // in 100-ns units on the same clock as capture frames
    private long _lastVideo;
    private TimeSpan _audioPosition;
    private volatile bool _ending;

    public string Path { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>How long the finished file is.</summary>
    public TimeSpan Duration => TimeSpan.FromTicks(Interlocked.Read(ref _lastVideo));

    /// <summary>Finishes when the file is complete (or failed).</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public bool Succeeded { get; private set; }

    public ClipSegment(string path, int width, int height, LoopbackAudio? audio)
    {
        Path = path;
        Width = width;
        Height = height;
        _audio = audio;
        _start = Now();
        audio?.Clear();
    }

    internal static long Now() => (long)(Stopwatch.GetTimestamp() * (10_000_000.0 / Stopwatch.Frequency));

    public async Task StartAsync()
    {
        var video = new VideoStreamDescriptor(VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)Width, (uint)Height));
        var source = _audio is { Working: true } audio
            ? new MediaStreamSource(video, new AudioStreamDescriptor(AudioEncodingProperties.CreatePcm((uint)audio.SampleRate, 2, 16)))
            : new MediaStreamSource(video);
        source.BufferTime = TimeSpan.Zero;
        source.Starting += (_, e) => e.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += OnSampleRequested;

        var profile = Profile(Width, Height, _audio is { Working: true } ? _audio.SampleRate : 0);
        // FileRandomAccessStream.OpenAsync only opens files that exist, so make the (empty) file first.
        System.IO.File.Create(Path).Dispose();
        var stream = await FileRandomAccessStream.OpenAsync(Path, FileAccessMode.ReadWrite);
        var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = true };
        var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, stream, profile);
        if (!prepared.CanTranscode)
        {
            stream.Dispose();
            throw new InvalidOperationException("Windows can't encode this clip: " + prepared.FailureReason);
        }

        Completion = Task.Run(async () =>
        {
            try
            {
                await prepared.TranscodeAsync();
                Succeeded = Duration > TimeSpan.Zero;
            }
            finally
            {
                stream.Dispose();
                while (_frames.Reader.TryRead(out var left)) left.Dispose();
            }
        });
    }

    /// <summary>MP4 with H.264 picture (about 8 Mbit/s, up to 60 frames a second) and AAC sound.</summary>
    internal static MediaEncodingProfile Profile(int width, int height, int sampleRate)
    {
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Video.Width = (uint)width;
        profile.Video.Height = (uint)height;
        profile.Video.Bitrate = 8_000_000;
        profile.Video.FrameRate.Numerator = 60;
        profile.Video.FrameRate.Denominator = 1;
        profile.Audio = sampleRate > 0 ? AudioEncodingProperties.CreateAac((uint)sampleRate, 2, 160_000) : null;
        return profile;
    }

    /// <summary>Hands a captured frame to the encoder. False if it can't take one right now; the caller then lets it go.</summary>
    public bool Offer(Direct3D11CaptureFrame frame) => !_ending && _frames.Writer.TryWrite(frame);

    /// <summary>Stops taking frames; the file is finished once the encoder has written what it has.</summary>
    public void End()
    {
        _ending = true;
        _frames.Writer.TryComplete();
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var request = args.Request;
        var deferral = request.GetDeferral();
        var isVideo = request.StreamDescriptor is VideoStreamDescriptor;
        _ = Task.Run(async () =>
        {
            try
            {
                request.Sample = isVideo ? await NextVideoAsync() : await NextAudioAsync();
            }
            catch (Exception)
            {
                request.Sample = null; // ends that stream; the file keeps what was written
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private async Task<MediaStreamSample?> NextVideoAsync()
    {
        while (await _frames.Reader.WaitToReadAsync())
        {
            if (!_frames.Reader.TryRead(out var frame)) continue;
            var time = frame.SystemRelativeTime.Ticks - _start;
            if (time < 0 || time <= Interlocked.Read(ref _lastVideo) && _lastVideo > 0)
            {
                frame.Dispose(); // from before this piece began, or out of order
                continue;
            }
            Interlocked.Exchange(ref _lastVideo, time);
            var sample = MediaStreamSample.CreateFromDirect3D11Surface(frame.Surface, TimeSpan.FromTicks(time));
            sample.Processed += (_, _) => frame.Dispose();
            return sample;
        }
        return null; // the piece has ended
    }

    /// <summary>Sound in 20 ms pieces, released in step with the clock; silence fills any gap.</summary>
    private async Task<MediaStreamSample?> NextAudioAsync()
    {
        var audio = _audio!;
        while (!_ending && Now() - _start < (_audioPosition + AudioChunk).Ticks)
            await Task.Delay(5);
        if (_ending && _audioPosition.Ticks >= Interlocked.Read(ref _lastVideo)) return null;

        var frames = audio.SampleRate / 50;
        var samples = new short[frames * 2];
        audio.Read(samples);
        var bytes = new byte[samples.Length * 2];
        System.Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        var sample = MediaStreamSample.CreateFromBuffer(CryptographicBuffer.CreateFromByteArray(bytes), _audioPosition);
        sample.Duration = AudioChunk;
        _audioPosition += AudioChunk;
        return sample;
    }
}

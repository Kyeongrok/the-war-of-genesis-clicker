using System.Collections.Concurrent;
using System.Diagnostics;

namespace WarOfGenesis.Assets;

/// <summary>
/// 원본 Bink 영상을 <b>뒤 실에서 앞서 풀며</b> 트는 것 — 영상 실은 컷을 몇 장 앞서 풀어 0xAARRGGBB 로 쌓아 두고(<see cref="Ahead"/>장),
/// 소리 실은 소리 전체 길이의 PCM 배열을 먼저 잡아 앞에서부터 채운다(<see cref="Sound"/> — 믹서는 바로 틀어도 된다. 소리 풀기는 제 빠르기보다 훨씬 빠르다).
/// </summary>
/// <remarks>
/// 컷은 앞 컷에 기대어 풀리므로 늦어도 건너뛰지 않고 차례로 다 푼다 — 늦은 컷은 보여 주는 쪽(<see cref="TakeFrame"/>)이 버린다.
/// <see cref="Dispose"/> 하면 두 실 다 멈추고 파일을 닫는다.
/// </remarks>
public sealed class BinkPlayback : IDisposable
{
    /// <summary>미리 풀어 둘 컷 수.</summary>
    public const int Ahead = 6;

    private sealed record Frame(int Index, uint[] Pixels);

    private readonly BinkMovie _movie;
    private readonly BlockingCollection<Frame> _ready = new(Ahead);
    private readonly ConcurrentQueue<uint[]> _free = new();
    private readonly CancellationTokenSource _cancel = new();
    private readonly Thread _videoThread;
    private readonly Thread? _audioThread;
    private Frame? _head;
    private volatile bool _videoDone, _audioDone;

    /// <summary>소리 실이 소리를 끝까지 다 채웠나(소리가 없으면 처음부터 true).</summary>
    public bool AudioFinished => _audioDone || _audioThread == null;

    public int Width => _movie.Width;
    public int Height => _movie.Height;
    public double Fps => _movie.Fps;
    public int FrameCount => _movie.FrameCount;

    /// <summary>소리 — 뒤에서 채워지는 중일 수 있다. 소리 갈래가 없으면 null.</summary>
    public PcmSound? Sound { get; }

    /// <summary>영상 실이 컷 하나 푸는 데 쓴 시간(밀리초) — 가장 긴 것과 평균(시험 기록용).</summary>
    public double MaxDecodeMs { get; private set; }
    public double MeanDecodeMs => _decoded == 0 ? 0 : _decodeTicks * 1000.0 / Stopwatch.Frequency / _decoded;
    private long _decodeTicks;
    private int _decoded;

    /// <summary>보여 줄 차례인데 그 컷이 아직 안 풀려 있던 번 수(<see cref="TakeFrame"/> 부른 번 기준).</summary>
    public int LateTicks { get; private set; }
    private int _given = -1;

    /// <summary>풀다 깨진 컷 수(그 컷은 앞 컷을 그대로 보인다).</summary>
    public int BadFrames { get; private set; }

    private BinkPlayback(BinkMovie movie)
    {
        _movie = movie;
        if (movie.HasAudio && !movie.Info.UseDct && movie.Info.Channels is 1 or 2)
        {
            long total = movie.CountAudioSamples();
            if (total > 0 && total < int.MaxValue)
            {
                var samples = new short[total];
                Sound = new PcmSound(samples, movie.Info.SampleRate, movie.Info.Channels);
                _audioThread = new Thread(() => AudioLoop(samples)) { IsBackground = true, Name = "BinkAudio" };
            }
        }
        _videoThread = new Thread(VideoLoop) { IsBackground = true, Name = "BinkVideo" };
    }

    /// <summary>열어서 두 실을 건다. 못 열면 예외(<see cref="InvalidDataException"/>, <see cref="IOException"/> 따위).</summary>
    public static BinkPlayback Start(string path, long offset = 0, long length = -1)
    {
        var movie = BinkMovie.Open(path, offset, length);
        try
        {
            var p = new BinkPlayback(movie);
            p._audioThread?.Start();
            p._videoThread.Start();
            return p;
        }
        catch
        {
            movie.Dispose();
            throw;
        }
    }

    /// <summary>첫 컷이 풀렸나 — 그 전에는 보여 주는 쪽이 시계와 소리를 세워 둔다.</summary>
    public bool FirstFrameReady => _head != null || _ready.Count > 0 || _videoDone;

    /// <summary>영상 실이 끝까지 다 풀었고 쌓인 컷도 다 가져갔나.</summary>
    public bool Drained => _videoDone && _head == null && _ready.Count == 0;

    /// <summary>
    /// <paramref name="index"/> 번 컷까지 풀린 것 가운데 가장 늦은 것을 <paramref name="dst"/>(너비×높이)에 옮긴다 — 그보다 앞 컷은 버린다.
    /// 새로 옮긴 컷이 없으면 false(아직 안 풀렸거나 이미 보인 컷).
    /// </summary>
    public bool TakeFrame(int index, Span<uint> dst)
    {
        Frame? pick = null;
        while (true)
        {
            if (_head == null && !_ready.TryTake(out _head)) break;
            if (_head.Index > index) break;
            if (pick != null) _free.Enqueue(pick.Pixels);
            pick = _head;
            _head = null;
        }
        // 보여야 할 컷이 아직 안 풀렸으면 늦은 것 — 그때는 앞 컷이 한 틀 더 남는다(시험 기록용).
        if ((pick?.Index ?? _given) < index && index < FrameCount && !_videoDone) LateTicks++;
        if (pick == null) return false;
        pick.Pixels.AsSpan(0, Width * Height).CopyTo(dst);
        _free.Enqueue(pick.Pixels);
        _given = pick.Index;
        return true;
    }

    private void VideoLoop()
    {
        var token = _cancel.Token;
        try
        {
            var dec = new BinkVideoDecoder(_movie.Width, _movie.Height, _movie.Info.Revision);
            byte[] buf = [];
            for (int i = 0; i < _movie.FrameCount && !token.IsCancellationRequested; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                int n = _movie.ReadFrame(i, ref buf);
                _movie.Split(buf.AsSpan(0, n), out _, out var video);
                if (!dec.Decode(buf.AsSpan(0, n)[video])) BadFrames++;
                if (!_free.TryDequeue(out var pixels)) pixels = new uint[_movie.Width * _movie.Height];
                dec.ToArgb(pixels);
                long dt = Stopwatch.GetTimestamp() - t0;
                _decodeTicks += dt;
                _decoded++;
                MaxDecodeMs = Math.Max(MaxDecodeMs, dt * 1000.0 / Stopwatch.Frequency);
                _ready.Add(new Frame(i, pixels), token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ObjectDisposedException) { }
        finally { _videoDone = true; }
    }

    private void AudioLoop(short[] samples)
    {
        var token = _cancel.Token;
        try
        {
            var dec = new BinkAudio.RdctDecoder(_movie.Info.SampleRate, _movie.Info.Channels);
            var output = new List<short>(16384);
            byte[] buf = [];
            int at = 0;
            for (int i = 0; i < _movie.FrameCount && at < samples.Length && !token.IsCancellationRequested; i++)
            {
                int n = _movie.ReadAudio(i, ref buf);
                if (n < 4) continue;
                var packet = buf.AsSpan(0, n);
                output.Clear();
                dec.DecodePacket(packet, output);
                int count = Math.Min(output.Count, samples.Length - at);
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(output)[..count].CopyTo(samples.AsSpan(at));
                at += count;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ObjectDisposedException) { }
        finally { _audioDone = true; }
    }

    public void Dispose()
    {
        _cancel.Cancel();
        _videoThread.Join(2000);
        _audioThread?.Join(2000);
        _movie.Dispose();
        _ready.Dispose();
        _cancel.Dispose();
    }
}

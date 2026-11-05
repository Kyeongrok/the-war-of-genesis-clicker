using System.IO;
using System.Runtime.InteropServices;

namespace WarOfGenesis.Assets;

/// <summary>
/// 배경음악 하나와 효과음 여럿을 섞어 winmm <c>waveOut</c> 하나로 트는 작은 믹서(44.1kHz 스테레오 16비트).
/// </summary>
/// <remarks>
/// 원본 게임은 효과음을 겹쳐 튼다(분석-사운드 "전투 효과음") — <c>PlaySound</c> 처럼 앞 소리를 끊으면 안 된다.
/// 효과음은 표본율이 달라도 되고(22050 모노 등), 이웃 두 표본 사이를 선형으로 이어 늘린다(감사4 S8 — 원본은 DirectSound 가 늘린다.
/// 전에는 가장 가까운 표본이라 22 kHz 효과음에 고역 잡음이 섞였다). 뒤쪽 실 하나가 40ms 버퍼를 몇 개씩 밀어 넣는다.
/// </remarks>
public sealed class AudioMixer : IDisposable
{
    private const int Rate = 44100, BufferFrames = Rate / 25, QueuedBuffers = 2;   // 줄 선 버퍼 2개(80ms) — 3개일 때 효과음이 80~130ms 늦었다(ba-21 sound D2)
    private const uint WhdrDone = 1;

    /// <summary>
    /// Snd 효과음 칸 수 — 원본은 DirectSound 버퍼 칸 32개(<c>0x101a99f8</c>~<c>0x101a9a78</c>)가 다 차면
    /// <b>새 소리를 버린다</b>(<c>0x10028898~0x100288be</c>, 감사4 S7). 음성·채널 소리(Bink 쪽)는 셈 밖.
    /// </summary>
    public const int EffectSlots = 32;

    private sealed class Voice(PcmSound sound, float gain, bool loop, int tag, float left = 1f, float right = 1f, bool slotted = false)
    {
        public readonly PcmSound Sound = sound;
        public readonly double Step = (double)sound.SampleRate / Rate;
        public float Gain = gain;
        public bool Loop = loop;
        public readonly int Tag = tag;
        /// <summary>좌우 곱 — 팬(감사4 S1·S2). 1 이면 그 쪽을 줄이지 않는다.</summary>
        public readonly float Left = left, Right = right;
        /// <summary>효과음 칸 하나를 차지하나(<see cref="EffectSlots"/>).</summary>
        public readonly bool Slotted = slotted;
        public double Position;
        /// <summary>되풀이로 처음에 되감은 수 — <see cref="MusicPlayback"/>.</summary>
        public int Laps;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx { public ushort Tag, Channels; public uint SamplesPerSec, AvgBytesPerSec; public ushort BlockAlign, Bits, Size; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr { public IntPtr Data; public uint Length, Recorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr hwo, uint device, ref WaveFormatEx fmt, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, uint size);

    private static readonly uint HdrSize = (uint)Marshal.SizeOf<WaveHdr>();

    private readonly object _gate = new();
    private readonly List<Voice> _effects = [];
    private Voice? _music;
    private readonly Thread? _thread;
    private volatile bool _stop;

    /// <summary>환경 변수 <c>AUDIOMIXER_DUMP</c> 에 파일 경로가 있으면 섞은 소리를 그 WAV 로도 남긴다(테스트용 — 들어 보지 않고 확인하려고).</summary>
    private readonly FileStream? _dump = Environment.GetEnvironmentVariable("AUDIOMIXER_DUMP") is { Length: > 0 } p
        ? new FileStream(p, FileMode.Create, FileAccess.Write) : null;

    /// <summary><c>AUDIOMIXER_SILENT=1</c> 이면 장치로 내보내지 않고 섞기만 한다(테스트용 — 스피커로 소리가 안 난다).</summary>
    private static readonly bool Silent = Environment.GetEnvironmentVariable("AUDIOMIXER_SILENT") == "1";

    /// <summary>
    /// 장치가 이만큼(ms) 버퍼를 하나도 돌려주지 않으면 죽은 것으로 보고 닫았다 다시 연다. 버퍼는 40ms 짜리 둘이라 보통은 80ms 안에 돌아온다.
    /// </summary>
    private const int StallMs = 1500;

    /// <summary>장치를 못 열었을 때 다시 열어 보는 사이(ms).</summary>
    private const int ReopenMs = 2000;

    /// <summary>
    /// 시험 문 <c>AUDIOMIXER_FAKE_STALL_MS=&lt;ms&gt;</c> — 장치를 연 지 그만큼 지나면 한 번, 장치가 버퍼를 안 돌려주는 것처럼 군다(다시 열기 시험).
    /// 무음 버퍼만 흘리는 시험에서 쓴다 — 스피커로 소리가 나지 않게 효과음을 걸지 않는다.
    /// </summary>
    private static readonly int FakeStallMs = int.TryParse(Environment.GetEnvironmentVariable("AUDIOMIXER_FAKE_STALL_MS"), out int fs) ? fs : -1;

    /// <summary>
    /// 장치를 다시 연 수(멈춘 장치 · 쓰기 실패 · 처음에 못 열었다가 연 것) — 버그 리포트에 싣는다.
    /// 소리 장치가 바뀌거나(HDMI · 블루투스 헤드셋) 잠깐 사라지면 waveOut 이 버퍼를 안 돌려주고 멈춰, 전에는 껐다 켤 때까지 소리가 하나도 안 났다(버그 리포트 #47).
    /// </summary>
    public int DeviceResets => Volatile.Read(ref _deviceResets);
    private int _deviceResets;

    /// <summary>장치로 내보낸 버퍼 수 — 시험이 다시 연 뒤에도 소리가 흐르는지 본다.</summary>
    public long BuffersWritten => Interlocked.Read(ref _buffersWritten);
    private long _buffersWritten;

    /// <summary>소리 장치를 연다. 장치가 없으면 섞기만 하고(곡 시계 · 효과음 끝은 그대로 흐른다) 틈틈이 다시 열어 본다.</summary>
    public AudioMixer()
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = "AudioMixer" };
        _thread.Start();
    }

    private static IntPtr OpenDevice()
    {
        var fmt = new WaveFormatEx { Tag = 1, Channels = 2, SamplesPerSec = Rate, Bits = 16, BlockAlign = 4, AvgBytesPerSec = Rate * 4 };
        return waveOutOpen(out IntPtr device, unchecked((uint)-1), ref fmt, IntPtr.Zero, IntPtr.Zero, 0) == 0 ? device : IntPtr.Zero;
    }

    /// <summary>장치를 닫는다 — 줄 선 버퍼를 다 돌려받아 빈 버퍼로 되돌린다.</summary>
    private static void CloseDevice(ref IntPtr device, Queue<IntPtr> busy, Queue<IntPtr> free)
    {
        if (device == IntPtr.Zero) return;
        waveOutReset(device);
        while (busy.Count > 0)
        {
            IntPtr hdr = busy.Dequeue();
            waveOutUnprepareHeader(device, hdr, HdrSize);
            free.Enqueue(hdr);
        }
        waveOutClose(device);
        device = IntPtr.Zero;
    }

    /// <summary>효과음을 한 번 튼다. <paramref name="tag"/> 는 <see cref="IsPlaying"/> 로 겹침을 막을 때 쓴다.</summary>
    /// <param name="loop">끝에서 처음으로 되감는다 — 필드 스크립트 행동 501 의 인자3 이 1 일 때.</param>
    /// <param name="left">왼쪽 곱(팬) — 1 이면 줄이지 않는다.</param>
    /// <param name="right">오른쪽 곱(팬).</param>
    /// <param name="slotted">Snd 효과음 칸을 쓰나 — 칸 <see cref="EffectSlots"/> 개가 다 차 있으면 이 소리는 버린다.</param>
    /// <returns>틀었으면 true, 칸이 차서 버렸으면 false.</returns>
    public bool PlayEffect(PcmSound sound, float gain = 1f, int tag = 0, bool loop = false, float left = 1f, float right = 1f, bool slotted = false)
    {
        if (sound.FrameCount == 0) return false;
        lock (_gate)
        {
            if (slotted && _effects.Count(v => v.Slotted) >= EffectSlots) return false;
            _effects.Add(new Voice(sound, gain, loop, tag, left, right, slotted));
        }
        return true;
    }

    /// <summary>그 표를 단 되풀이 소리를 <b>이번 바퀴까지만</b> 돌게 한다 — 원본은 모션이 바뀌면 되풀이를 끄고 끝까지 울린다(감사4 S3).</summary>
    public void EndLoop(int tag)
    {
        lock (_gate)
            foreach (var v in _effects)
                if (v.Tag == tag) v.Loop = false;
    }

    /// <summary>그 표를 단 효과음을 멈춘다 — 필드 스크립트 행동 505(채널 소리 끄기)가 쓴다.</summary>
    public void StopEffect(int tag)
    {
        lock (_gate) _effects.RemoveAll(v => v.Tag == tag);
    }

    /// <summary>그 표를 단 효과음의 소리 크기만 바꾼다 — 필드 스크립트 행동 506(채널 음량)이 쓴다.</summary>
    public void SetEffectGain(int tag, float gain)
    {
        lock (_gate)
            foreach (var v in _effects)
                if (v.Tag == tag) v.Gain = gain;
    }

    public bool IsPlaying(int tag)
    {
        lock (_gate) return _effects.Any(v => v.Tag == tag);
    }

    /// <summary>배경음악을 바꾼다(처음부터). <paramref name="loop"/> 면 끝에서 처음으로 되감는다.</summary>
    public void PlayMusic(PcmSound sound, bool loop, float gain)
    {
        lock (_gate)
        {
            _music = sound.FrameCount == 0 ? null : new Voice(sound, gain, loop, -1);
            _musicSerial++;
            _musicPaused = false;
        }
    }

    /// <summary>
    /// 배경음악 전체 크기(0~1) — 설정 창의 B.G.M 막대. 원본은 <b>모든 곡·페이드·줄이기</b>에 이 설정을 곱한다
    /// (<c>0x10025320</c>: 크기 × <c>[0x10163f80]</c>/100, 곡 생성자 <c>0x100252a0</c> → <c>0x100253e0</c>, 감사4 V1).
    /// 그래서 곡마다의 크기(<see cref="SetMusicGain"/>)와 따로 들고 섞을 때만 곱한다.
    /// </summary>
    public float MusicVolume
    {
        get { lock (_gate) return _musicVolume; }
        set { lock (_gate) _musicVolume = Math.Clamp(value, 0f, 1f); }
    }

    private float _musicVolume = 1f;

    /// <summary>창이 뒤에 있을 때 배경음악에 더 곱하는 값 — 앞이면 1, 다른 창이 앞이면 0.05(사용자 요청: 백그라운드에서는 BGM 5%).</summary>
    public float MusicFocus
    {
        get { lock (_gate) return _musicFocus; }
        set { lock (_gate) _musicFocus = Math.Clamp(value, 0f, 1f); }
    }

    private float _musicFocus = 1f;

    /// <summary>배경음악 소리 크기만 바꾼다(레벨업 창이 뜨면 40%). 실제 크기는 여기에 <see cref="MusicVolume"/> 를 곱한 것.</summary>
    public void SetMusicGain(float gain)
    {
        lock (_gate) if (_music != null) _music.Gain = gain;
    }

    public void StopMusic()
    {
        lock (_gate) { _music = null; _musicPaused = false; }
    }

    private long _musicSerial;

    /// <summary>
    /// 지금 울리는 배경음악 — (곡을 건 차례, 되감은 수). 곡이 없거나 멈춰 있으면 null. 파트1 필드 701(곡이 끝날 때까지)이 걸 때 값과 견준다.
    /// </summary>
    public (long Serial, int Laps)? MusicPlayback
    {
        get { lock (_gate) return _music == null || _musicPaused ? null : (_musicSerial, _music.Laps); }
    }

    /// <summary>음악이 멈춰 있나(개체는 남아 있다) — <see cref="PauseMusic"/>.</summary>
    private bool _musicPaused;

    /// <summary>
    /// 배경음악을 <b>지우지 않고</b> 멈춘다 — 원본 크기 0 = 일시정지(<c>0x10025000</c>, 감사4 M5).
    /// <paramref name="rewind"/> 면 처음으로 되감는다 — 필드 514(<c>0x10024fb0</c>, 감사4 M2).
    /// </summary>
    public void PauseMusic(bool rewind = false)
    {
        lock (_gate)
        {
            _musicPaused = true;
            if (rewind && _music != null) _music.Position = 0;
        }
    }

    /// <summary>멈춘 배경음악을 그 자리에서 이어 튼다(<c>0x10025480</c>).</summary>
    public void ResumeMusic()
    {
        lock (_gate) _musicPaused = false;
    }

    /// <summary>버퍼 하나만큼 섞는다 — 장치가 있든 없든 목소리는 이만큼 나아간다.</summary>
    private void MixOne(int[] mix, short[] pcm)
    {
        Array.Clear(mix);
        lock (_gate)
        {
            if (_music != null && !_musicPaused && !Mix(_music, mix, _musicVolume * _musicFocus)) _music = null;
            _effects.RemoveAll(v => !Mix(v, mix, 1f));
        }
        for (int i = 0; i < mix.Length; i++) pcm[i] = (short)Math.Clamp(mix[i], short.MinValue, short.MaxValue);
        if (_dump != null) { _dump.Write(MemoryMarshal.AsBytes<short>(pcm)); _dump.Flush(); }
    }

    /// <summary>
    /// 섞어서 장치로 민다. 장치가 버퍼를 <see cref="StallMs"/> 동안 하나도 안 돌려주거나 쓰기가 실패하면(장치가 바뀌었다 · 사라졌다)
    /// 닫고 다시 연다 — 전에는 줄이 찬 채로 멈춰 게임을 껐다 켤 때까지 아무 소리도 안 났다(버그 리포트 #47). 장치가 없는 동안은
    /// 무음 시험처럼 40ms 마다 섞기만 해 곡 시계(필드 701) · 효과음 끝(<see cref="IsPlaying"/>)이 멈추지 않는다.
    /// </summary>
    private void Pump()
    {
        var headers = new List<IntPtr>();
        var free = new Queue<IntPtr>();
        var busy = new Queue<IntPtr>();
        var mix = new int[BufferFrames * 2];
        var pcm = new short[BufferFrames * 2];
        IntPtr device = IntPtr.Zero;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long nextOpen = 0, lastProgress = 0, openedAt = 0, lastMix = 0;
        bool everOpened = false, fakeStalled = false;
        try
        {
            for (int i = 0; i <= QueuedBuffers; i++)
            {
                IntPtr hdr = Marshal.AllocHGlobal((int)HdrSize);
                Marshal.StructureToPtr(new WaveHdr { Data = Marshal.AllocHGlobal(BufferFrames * 4) }, hdr, false);
                headers.Add(hdr);
                free.Enqueue(hdr);
            }

            while (!_stop)
            {
                long now = clock.ElapsedMilliseconds;
                if (!Silent && device == IntPtr.Zero && now >= nextOpen)
                {
                    device = OpenDevice();
                    if (device != IntPtr.Zero)
                    {
                        if (everOpened || now > ReopenMs / 2) Interlocked.Increment(ref _deviceResets);
                        everOpened = true;
                        lastProgress = openedAt = now;
                    }
                    else nextOpen = now + ReopenMs;
                }
                if (Silent || device == IntPtr.Zero)
                {
                    // 장치 없이 섞기만 한다(무음 시험 · 장치를 못 연 동안).
                    MixOne(mix, pcm);
                    Thread.Sleep(1000 / 25);
                    continue;
                }

                bool stallTest = FakeStallMs >= 0 && !fakeStalled && now - openedAt >= FakeStallMs;
                while (!stallTest && busy.Count > 0 && (Marshal.PtrToStructure<WaveHdr>(busy.Peek()).Flags & WhdrDone) != 0)
                {
                    IntPtr done = busy.Dequeue();
                    waveOutUnprepareHeader(device, done, HdrSize);
                    free.Enqueue(done);
                    lastProgress = now;
                }
                // 장치가 버퍼를 안 돌려주는 동안에도 목소리는 제 빠르기로 나아간다(섞어 버린다) — 곡 시계(701) · 효과음 끝이 멈춰 장면이 서지 않게.
                if (busy.Count >= QueuedBuffers && now - lastProgress > 200 && now - lastMix >= 1000 / 25)
                {
                    MixOne(mix, pcm);
                    lastMix = now;
                }
                if (busy.Count > 0 && now - lastProgress > StallMs)
                {
                    if (stallTest) fakeStalled = true;
                    CloseDevice(ref device, busy, free);
                    nextOpen = now;   // 곧바로 다시 연다(기본 장치가 바뀌었으면 새 장치로)
                    continue;
                }
                bool failed = false;
                while (!stallTest && busy.Count < QueuedBuffers && free.Count > 0)
                {
                    MixOne(mix, pcm);
                    lastMix = now;
                    IntPtr hdr = free.Dequeue();
                    var h = Marshal.PtrToStructure<WaveHdr>(hdr);
                    Marshal.Copy(pcm, 0, h.Data, pcm.Length);
                    Marshal.StructureToPtr(new WaveHdr { Data = h.Data, Length = (uint)(pcm.Length * 2) }, hdr, false);
                    if (waveOutPrepareHeader(device, hdr, HdrSize) != 0) { free.Enqueue(hdr); failed = true; break; }
                    if (waveOutWrite(device, hdr, HdrSize) != 0) { waveOutUnprepareHeader(device, hdr, HdrSize); free.Enqueue(hdr); failed = true; break; }
                    if (busy.Count == 0) lastProgress = now;   // 빈 줄에 처음 선 버퍼 — 여기서부터 잰다
                    busy.Enqueue(hdr);
                    Interlocked.Increment(ref _buffersWritten);
                }
                if (failed)
                {
                    CloseDevice(ref device, busy, free);
                    nextOpen = now + 500;
                    continue;
                }
                Thread.Sleep(10);
            }
        }
        finally
        {
            if (device != IntPtr.Zero) CloseDevice(ref device, busy, free);
            foreach (IntPtr hdr in headers)
            {
                Marshal.FreeHGlobal(Marshal.PtrToStructure<WaveHdr>(hdr).Data);
                Marshal.FreeHGlobal(hdr);
            }
        }
    }

    /// <summary>
    /// 목소리 하나를 버퍼에 더한다. 끝났으면 false. 이웃 두 표본 사이는 선형으로 잇는다(감사4 S8) — 끝 표본의 다음은
    /// 되풀이면 처음 표본, 아니면 끝 표본 그대로.
    /// </summary>
    /// <param name="master">목소리 크기에 더 곱할 값 — 배경음악은 <see cref="MusicVolume"/>.</param>
    private static bool Mix(Voice v, int[] mix, float master)
    {
        var s = v.Sound;
        int ch = s.Channels;
        long frames = s.FrameCount;
        float gl = v.Gain * master * v.Left, gr = v.Gain * master * v.Right;
        // 크기가 NaN · 무한이면 (int) 가 int.MinValue 가 되어 섞은 소리가 한쪽 끝에 붙는다(모든 소리가 안 들린다) — 그 목소리는 0 으로.
        if (!float.IsFinite(gl) || !float.IsFinite(gr)) gl = gr = 0;
        for (int i = 0; i < BufferFrames; i++)
        {
            long idx = (long)v.Position;
            if (idx >= frames)
            {
                if (!v.Loop) return false;
                v.Position -= frames * Math.Floor(v.Position / frames);
                v.Laps++;
                idx = (long)v.Position;
            }
            long next = idx + 1 < frames ? idx + 1 : v.Loop ? 0 : idx;
            float frac = (float)(v.Position - idx);
            float l0 = s.Samples[idx * ch], l1 = s.Samples[next * ch];
            float l = l0 + (l1 - l0) * frac, r = l;
            if (ch > 1)
            {
                float r0 = s.Samples[idx * ch + 1], r1 = s.Samples[next * ch + 1];
                r = r0 + (r1 - r0) * frac;
            }
            mix[2 * i] += (int)(l * gl);
            mix[2 * i + 1] += (int)(r * gr);
            v.Position += v.Step;
        }
        return true;
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join();
        _dump?.Dispose();
    }
}

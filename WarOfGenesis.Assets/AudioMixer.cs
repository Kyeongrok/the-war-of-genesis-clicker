using System.IO;
using System.Runtime.InteropServices;

namespace WarOfGenesis.Assets;

/// <summary>
/// 배경음악 하나와 효과음 여럿을 섞어 winmm <c>waveOut</c> 하나로 트는 작은 믹서(44.1kHz 스테레오 16비트).
/// </summary>
/// <remarks>
/// 원본 게임은 효과음을 겹쳐 튼다(분석-사운드 "전투 효과음") — <c>PlaySound</c> 처럼 앞 소리를 끊으면 안 된다.
/// 효과음은 표본율이 달라도 되고(22050 모노 등), 가장 가까운 표본으로 늘린다. 뒤쪽 실 하나가 40ms 버퍼를 몇 개씩 밀어 넣는다.
/// </remarks>
public sealed class AudioMixer : IDisposable
{
    private const int Rate = 44100, BufferFrames = Rate / 25, QueuedBuffers = 3;
    private const uint WhdrDone = 1;

    private sealed class Voice(PcmSound sound, float gain, bool loop, int tag)
    {
        public readonly PcmSound Sound = sound;
        public readonly double Step = (double)sound.SampleRate / Rate;
        public float Gain = gain;
        public readonly bool Loop = loop;
        public readonly int Tag = tag;
        public double Position;
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

    /// <summary>소리 장치를 연다. 장치가 없으면 조용히 아무 소리도 안 낸다.</summary>
    public AudioMixer()
    {
        IntPtr device = IntPtr.Zero;
        var fmt = new WaveFormatEx { Tag = 1, Channels = 2, SamplesPerSec = Rate, Bits = 16, BlockAlign = 4, AvgBytesPerSec = Rate * 4 };
        if (!Silent && waveOutOpen(out device, unchecked((uint)-1), ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0) return;
        _thread = new Thread(() => Pump(device)) { IsBackground = true, Name = "AudioMixer" };
        _thread.Start();
    }

    /// <summary>효과음을 한 번 튼다. <paramref name="tag"/> 는 <see cref="IsPlaying"/> 로 겹침을 막을 때 쓴다.</summary>
    /// <param name="loop">끝에서 처음으로 되감는다 — 필드 스크립트 행동 501 의 인자3 이 1 일 때.</param>
    public void PlayEffect(PcmSound sound, float gain = 1f, int tag = 0, bool loop = false)
    {
        if (sound.FrameCount == 0) return;
        lock (_gate) _effects.Add(new Voice(sound, gain, loop, tag));
    }

    /// <summary>그 표를 단 효과음을 멈춘다 — 필드 스크립트 행동 505(채널 소리 끄기)가 쓴다.</summary>
    public void StopEffect(int tag)
    {
        lock (_gate) _effects.RemoveAll(v => v.Tag == tag);
    }

    public bool IsPlaying(int tag)
    {
        lock (_gate) return _effects.Any(v => v.Tag == tag);
    }

    /// <summary>배경음악을 바꾼다(처음부터). <paramref name="loop"/> 면 끝에서 처음으로 되감는다.</summary>
    public void PlayMusic(PcmSound sound, bool loop, float gain)
    {
        lock (_gate) _music = sound.FrameCount == 0 ? null : new Voice(sound, gain, loop, -1);
    }

    /// <summary>배경음악 소리 크기만 바꾼다(레벨업 창이 뜨면 40%).</summary>
    public void SetMusicGain(float gain)
    {
        lock (_gate) if (_music != null) _music.Gain = gain;
    }

    public void StopMusic()
    {
        lock (_gate) _music = null;
    }

    private void Pump(IntPtr device)
    {
        var headers = new List<IntPtr>();
        var free = new Queue<IntPtr>();
        var busy = new Queue<IntPtr>();
        var mix = new int[BufferFrames * 2];
        var pcm = new short[BufferFrames * 2];
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
                while (!Silent && busy.Count > 0 && (Marshal.PtrToStructure<WaveHdr>(busy.Peek()).Flags & WhdrDone) != 0)
                {
                    IntPtr done = busy.Dequeue();
                    waveOutUnprepareHeader(device, done, HdrSize);
                    free.Enqueue(done);
                }
                while ((Silent && _dump != null) || (busy.Count < QueuedBuffers && free.Count > 0))
                {
                    Array.Clear(mix);
                    lock (_gate)
                    {
                        if (_music != null && !Mix(_music, mix)) _music = null;
                        _effects.RemoveAll(v => !Mix(v, mix));
                    }
                    for (int i = 0; i < mix.Length; i++) pcm[i] = (short)Math.Clamp(mix[i], short.MinValue, short.MaxValue);
                    if (_dump != null) { _dump.Write(MemoryMarshal.AsBytes<short>(pcm)); _dump.Flush(); }

                    if (Silent) break;   // 섞기만 하고 장치로는 안 보낸다

                    IntPtr hdr = free.Dequeue();
                    var h = Marshal.PtrToStructure<WaveHdr>(hdr);
                    Marshal.Copy(pcm, 0, h.Data, pcm.Length);
                    Marshal.StructureToPtr(new WaveHdr { Data = h.Data, Length = (uint)(pcm.Length * 2) }, hdr, false);
                    waveOutPrepareHeader(device, hdr, HdrSize);
                    waveOutWrite(device, hdr, HdrSize);
                    busy.Enqueue(hdr);
                }
                Thread.Sleep(Silent ? 1000 / 25 : 10);
            }
        }
        finally
        {
            if (!Silent)
            {
            waveOutReset(device);
            foreach (IntPtr hdr in busy) waveOutUnprepareHeader(device, hdr, HdrSize);
            waveOutClose(device);
            }
            foreach (IntPtr hdr in headers)
            {
                Marshal.FreeHGlobal(Marshal.PtrToStructure<WaveHdr>(hdr).Data);
                Marshal.FreeHGlobal(hdr);
            }
        }
    }

    /// <summary>목소리 하나를 버퍼에 더한다. 끝났으면 false.</summary>
    private static bool Mix(Voice v, int[] mix)
    {
        var s = v.Sound;
        int ch = s.Channels;
        long frames = s.FrameCount;
        for (int i = 0; i < BufferFrames; i++)
        {
            long idx = (long)v.Position;
            if (idx >= frames)
            {
                if (!v.Loop) return false;
                v.Position -= frames;
                idx = (long)v.Position;
            }
            int l = s.Samples[idx * ch], r = ch > 1 ? s.Samples[idx * ch + 1] : l;
            mix[2 * i] += (int)(l * v.Gain);
            mix[2 * i + 1] += (int)(r * v.Gain);
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

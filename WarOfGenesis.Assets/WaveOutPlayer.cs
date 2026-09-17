using System.Runtime.InteropServices;

namespace WarOfGenesis.Assets;

/// <summary>
/// 풀어 둔 16비트 PCM 을 Windows 기본 소리 장치로 트는 작은 재생기 — winmm <c>waveOut*</c> 을 바로 부른다.
/// NuGet·임시 파일 없이 단일 exe 에서 그대로 돌고, 멈춤·자리 옮기기·반복·음량을 표본 단위로 다룬다.
/// </summary>
/// <remarks>
/// 뒤쪽 실 하나가 약 0.1초짜리 버퍼를 몇 개씩 장치에 밀어 넣는다(CALLBACK_NULL + 완료 플래그 확인).
/// 음량은 버퍼를 채울 때 곱해 넣으므로 바꾼 뒤 0.1~0.3초쯤 늦게 들린다.
/// </remarks>
public sealed class WaveOutPlayer : IDisposable
{
    private const int BufferMilliseconds = 100;
    private const int QueuedBuffers = 3;
    private const uint WhdrDone = 0x00000001;
    private const int MmsyserrNoerror = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public IntPtr lpData;
        public uint dwBufferLength, dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags, dwLoops;
        public IntPtr lpNext, reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MmTime
    {
        public uint wType;
        public uint value;   // TIME_SAMPLES 일 때 표본 수
        public uint pad1, pad2;
    }

    [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr hwo, uint uDeviceID, ref WaveFormatEx fmt, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr hwo);
    [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, uint size);
    [DllImport("winmm.dll")] private static extern int waveOutGetPosition(IntPtr hwo, ref MmTime time, uint size);

    private static readonly uint HdrSize = (uint)Marshal.SizeOf<WaveHdr>();

    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _stopRequested;
    private PcmSound? _sound;
    private IntPtr _device;
    private long _startFrame;       // 재생을 시작한 자리(표본 프레임)
    private volatile float _volume = 1f;

    /// <summary>끝까지 다 틀어서 스스로 멈췄을 때(반복이 꺼져 있을 때만). 뒤쪽 실에서 불린다.</summary>
    public event Action? Ended;

    public bool IsPlaying => _thread is { IsAlive: true };

    public bool Loop { get; set; }

    /// <summary>0~1.</summary>
    public float Volume { get => _volume; set => _volume = Math.Clamp(value, 0f, 1f); }

    /// <summary>지금 들리는 자리(초). 멈춰 있으면 마지막으로 시작한 자리.</summary>
    public double PositionSeconds
    {
        get
        {
            lock (_gate)
            {
                if (_sound is not { } s) return 0;
                long frame = _startFrame;
                if (_device != IntPtr.Zero)
                {
                    var t = new MmTime { wType = 2 };   // TIME_SAMPLES
                    if (waveOutGetPosition(_device, ref t, (uint)Marshal.SizeOf<MmTime>()) == MmsyserrNoerror && t.wType == 2)
                        frame += t.value;
                }
                long total = s.FrameCount;
                if (total > 0) frame = Loop || frame < total ? frame % total : total;
                return (double)frame / s.SampleRate;
            }
        }
    }

    /// <summary><paramref name="startSeconds"/> 부터 튼다. 이미 틀고 있으면 멈추고 새로 튼다.</summary>
    public void Play(PcmSound sound, double startSeconds = 0)
    {
        Stop();
        if (sound.FrameCount == 0) return;
        var fmt = new WaveFormatEx
        {
            wFormatTag = 1, nChannels = (ushort)sound.Channels, nSamplesPerSec = (uint)sound.SampleRate,
            wBitsPerSample = 16, nBlockAlign = (ushort)(sound.Channels * 2),
            nAvgBytesPerSec = (uint)(sound.SampleRate * sound.Channels * 2),
        };
        int err = waveOutOpen(out var device, unchecked((uint)-1), ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
        if (err != MmsyserrNoerror) throw new InvalidOperationException($"소리 장치를 열지 못했습니다(waveOutOpen {err}).");

        lock (_gate)
        {
            _sound = sound;
            _device = device;
            _startFrame = Math.Clamp((long)(startSeconds * sound.SampleRate), 0, sound.FrameCount - 1);
        }
        _stopRequested = false;
        _thread = new Thread(() => Pump(sound, device, _startFrame)) { IsBackground = true, Name = "WaveOutPlayer" };
        _thread.Start();
    }

    /// <summary>멈춰 있을 때 다음에 틀 자리를 정한다(틀고 있으면 그 자리부터 새로 튼다).</summary>
    public void Seek(PcmSound sound, double seconds)
    {
        if (IsPlaying) { Play(sound, seconds); return; }
        lock (_gate)
        {
            _sound = sound;
            _startFrame = Math.Clamp((long)(seconds * sound.SampleRate), 0, Math.Max(0, sound.FrameCount - 1));
        }
    }

    public void Stop()
    {
        var thread = _thread;
        if (thread == null) return;
        _stopRequested = true;
        thread.Join();
        _thread = null;
    }

    private void Pump(PcmSound sound, IntPtr device, long startFrame)
    {
        int ch = sound.Channels;
        int framesPerBuffer = Math.Max(1, sound.SampleRate * BufferMilliseconds / 1000);
        var headers = new List<IntPtr>();
        var free = new Queue<IntPtr>();
        var busy = new Queue<IntPtr>();
        long next = startFrame;     // 다음에 채울 프레임
        bool finished = false;
        short[] scratch = new short[framesPerBuffer * ch];

        try
        {
            for (int i = 0; i < QueuedBuffers + 1; i++)
            {
                IntPtr hdr = Marshal.AllocHGlobal((int)HdrSize);
                IntPtr buf = Marshal.AllocHGlobal(framesPerBuffer * ch * 2);
                Marshal.StructureToPtr(new WaveHdr { lpData = buf }, hdr, false);
                headers.Add(hdr);
                free.Enqueue(hdr);
            }

            while (!_stopRequested)
            {
                while (busy.Count > 0 && (Marshal.PtrToStructure<WaveHdr>(busy.Peek()).dwFlags & WhdrDone) != 0)
                {
                    IntPtr done = busy.Dequeue();
                    waveOutUnprepareHeader(device, done, HdrSize);
                    free.Enqueue(done);
                }

                while (!finished && busy.Count < QueuedBuffers && free.Count > 0)
                {
                    int filled = 0;
                    while (filled < framesPerBuffer)
                    {
                        if (next >= sound.FrameCount)
                        {
                            if (!Loop) { finished = true; break; }
                            next = 0;
                        }
                        int take = (int)Math.Min(framesPerBuffer - filled, sound.FrameCount - next);
                        Array.Copy(sound.Samples, next * ch, scratch, filled * ch, take * ch);
                        filled += take;
                        next += take;
                    }
                    if (filled == 0) break;

                    float vol = _volume;
                    if (vol < 0.999f)
                        for (int i = 0; i < filled * ch; i++) scratch[i] = (short)(scratch[i] * vol);

                    IntPtr hdr = free.Dequeue();
                    var h = Marshal.PtrToStructure<WaveHdr>(hdr);
                    Marshal.Copy(scratch, 0, h.lpData, filled * ch);
                    Marshal.StructureToPtr(new WaveHdr { lpData = h.lpData, dwBufferLength = (uint)(filled * ch * 2) }, hdr, false);
                    waveOutPrepareHeader(device, hdr, HdrSize);
                    waveOutWrite(device, hdr, HdrSize);
                    busy.Enqueue(hdr);
                }

                if (finished && busy.Count == 0) break;
                Thread.Sleep(15);
            }
        }
        finally
        {
            lock (_gate)
            {
                // 멈춘 자리를 기억해 둔다 — 다시 틀 때 이어 가려고(Reset 하면 자리가 0 이 되므로 그 전에 읽는다).
                var t = new MmTime { wType = 2 };
                if (waveOutGetPosition(device, ref t, (uint)Marshal.SizeOf<MmTime>()) == MmsyserrNoerror && t.wType == 2)
                    _startFrame = (startFrame + t.value) % sound.FrameCount;
                if (finished) _startFrame = 0;
                waveOutReset(device);
                foreach (IntPtr hdr in busy) waveOutUnprepareHeader(device, hdr, HdrSize);
                waveOutClose(device);
                _device = IntPtr.Zero;
            }
            foreach (IntPtr hdr in headers)
            {
                Marshal.FreeHGlobal(Marshal.PtrToStructure<WaveHdr>(hdr).lpData);
                Marshal.FreeHGlobal(hdr);
            }
        }

        if (finished && !_stopRequested) Ended?.Invoke();
    }

    public void Dispose() => Stop();
}

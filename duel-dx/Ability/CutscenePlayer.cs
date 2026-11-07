using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 기술 컷신 — 헬 카이트·진무 천지파열·아수라 파천무처럼 원본이 전체 화면 영상(<c>Mov\NNNN.mov</c>, 640×480 · 15fps · 소리 있음)을 트는 기술.
/// 모드 &gt; 편의성 「기술 영상 보기」로 켜고 끈다(사용자 요청 menu-24).
/// </summary>
/// <remarks>
/// <para>
/// 원본은 그 work 의 핸들러가 단계마다 영상을 하나씩 건다(<c>0x100d05c0</c> · 전체 화면, 옵시디안 분석-스킬 「영상 이펙트」) — 타이타니아 슈발츠는
/// 0039 다음에 0043, 아수라 파천무는 0049 · 0050 · 0060. 이 게임은 <b>치는 순간에 그 영상들을 차례로 다 틀고</b> 끝나면 피해가 들어간다
/// (단계 사이의 유닛 동작은 옮기지 않았다). 영상이 도는 동안 게임 시계는 선다. 클릭 · Esc · Enter · Space 로 넘긴다.
/// </para>
/// <para>
/// 원본 게임 폴더에 그 영상(<c>Mov\NNNN.mov</c> — 낱장이나 <c>Mov0?.pak</c> 안)이 있으면 <b>그것을 바로 푼다</b>(<see cref="BinkPlayback"/> — 우리 Bink 1 디코더,
/// FFmpeg 과 컷마다 비트까지 같다). 뒤 실이 컷을 몇 장 앞서 풀고, 소리도 뒤에서 채운다. 원본에 없을 때만(파트1 만 있는 PC)
/// <c>tools/make_cutscenes.py</c> 가 미리 풀어 둔 <c>assets/effects/cut/NNNN.wgm</c>(JPEG 컷 + PCM 소리)을 읽는다 — 이것은 꾸러미에서 받는다
/// (<see cref="AssetPack.FetchExtra"/>, 아직 없으면 이번에는 영상 없이 지나가고 뒤에서 받아 둔다. 전투를 열 때 미리 받는다 — <see cref="Prefetch"/>).
/// 오프닝(<c>0008</c>)은 꾸러미에 없어 원본에서만 튼다.
/// </para>
/// </remarks>
internal sealed unsafe class CutscenePlayer(GameWindow host)
{
    /// <summary>work 번호 → 차례로 트는 영상들(G3PartII.dll 의 핸들러가 넘기는 <c>Mov\NNNN.mov</c> 이름).</summary>
    internal static readonly Dictionary<int, int[]> WorkCutscenes = new()
    {
        [1467] = [6],            // 헬 카이트
        [1521] = [36],           // 사이킥 드라이브
        [1522] = [38],           // 셰틀라이트 어텍
        [1523] = [39, 43],       // 타이타니아 슈발츠
        [1524] = [37],           // 버닝 웜
        [1528] = [33, 46],       // 코메트
        [1588] = [47],           // 폭풍검
        [1589] = [49, 50, 60],   // 아수라 파천무
        [1590] = [59],           // 진무 천지파열
    };

    private const string Kind = "effects/cut";
    private static string FileName(int movie) => $"{movie:D4}.wgm";

    /// <summary>재생 중인 영상 — 원본 Bink(<see cref="BinkClip"/>)나 미리 푼 .wgm(<see cref="WgmClip"/>).</summary>
    private abstract class Clip : IDisposable
    {
        public abstract int Width { get; }
        public abstract int Height { get; }
        public abstract double Fps { get; }
        public abstract int Count { get; }
        public abstract PcmSound? Sound { get; }
        /// <summary>첫 컷을 보일 수 있나 — 그 전에는 시계와 소리를 세워 둔다.</summary>
        public virtual bool Ready => true;
        /// <summary>그 번호 컷(늦었으면 거기까지 풀린 가장 늦은 컷)을 <paramref name="frame"/>(너비×높이)에 옮긴다. 옮긴 것이 없으면 false.</summary>
        public abstract bool Show(int index, int shown, uint[] frame);
        public abstract void Dispose();
    }

    /// <summary>원본 게임 폴더의 Bink 영상을 뒤 실에서 풀며 튼다.</summary>
    private sealed class BinkClip(BinkPlayback playback) : Clip
    {
        public override int Width => playback.Width;
        public override int Height => playback.Height;
        public override double Fps => playback.Fps;
        public override int Count => playback.FrameCount;
        public override PcmSound? Sound => playback.Sound;
        public override bool Ready => playback.FirstFrameReady;
        public BinkPlayback Playback => playback;
        public override bool Show(int index, int shown, uint[] frame) => playback.TakeFrame(index, frame);
        public override void Dispose() => playback.Dispose();
    }

    /// <summary>미리 풀어 둔 .wgm — 컷 자리 표와 소리.</summary>
    private sealed class WgmClip(FileStream file, int width, int height, double fps, long[] offsets, PcmSound? sound) : Clip
    {
        public override int Width => width;
        public override int Height => height;
        public override double Fps => fps;
        public override int Count => offsets.Length;
        public override PcmSound? Sound => sound;

        public override bool Show(int index, int shown, uint[] frame)
        {
            if (index == shown) return false;
            try
            {
                file.Position = offsets[index];
                Span<byte> head = stackalloc byte[4];
                file.ReadExactly(head);
                var bytes = new byte[BitConverter.ToInt32(head)];
                file.ReadExactly(bytes);
                using var stream = new MemoryStream(bytes);
                using var bitmap = new Bitmap(stream);
                if (bitmap.Width != width || bitmap.Height != height) return false;
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < bitmap.Height; y++)
                        new Span<uint>((void*)(data.Scan0 + y * data.Stride), bitmap.Width).CopyTo(frame.AsSpan(y * bitmap.Width, bitmap.Width));
                }
                finally { bitmap.UnlockBits(data); }
                return true;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException) { return false; /* 깨진 컷은 앞 컷을 그대로 둔다 */ }
        }

        public override void Dispose() => file.Dispose();
    }

    /// <summary>원본 게임 폴더에서 그 영상 자리(낱장이나 묶음 안 토막) — 없으면 null. 폴더가 바뀌지 않는 한 한 번만 찾는다.</summary>
    internal static (string Path, long Offset, long Length)? FindOriginal(int movie)
    {
        if (Environment.GetEnvironmentVariable("DUELDX_NOBINK") == "1") return null;   // 시험 문 — .wgm 길을 본다
        if (OriginalGame.Files is not { } files) return null;
        lock (OriginalCache)
        {
            string key = $"{OriginalGame.Root}|{movie}";
            if (!OriginalCache.TryGetValue(key, out var found))
            {
                try { found = files.FindRange("Mov", $"{movie:D4}.mov"); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { found = null; }
                OriginalCache[key] = found;
            }
            return found;
        }
    }

    private static readonly Dictionary<string, (string Path, long Offset, long Length)?> OriginalCache = [];

    /// <summary>그 영상을 지금 틀 수 있나 — 원본에 있거나 받아 둔 .wgm 이 있다(없으면 .wgm 받기를 뒤에서 건다).</summary>
    private static bool Available(int movie) => FindOriginal(movie) != null || AssetPack.FetchExtra(Kind, FileName(movie)) != null;

    private readonly Queue<int> _queue = new();
    private Clip? _clip;
    private double _clipStart;
    private int _shown = -1, _soundTag;
    private uint[] _frame = [];
    private bool _soundStarted;

    /// <summary>사용자 폴더의 영상(파트1 엔딩, <see cref="StartVideo"/>)을 뒤에서 마련하는 중(꺼내기 · 베끼기) — 다 되면 주 스레드에서 <see cref="_video"/> 로 연다.</summary>
    private Task<string?>? _loading;
    private DirectShowVideo? _video;
    private Action<bool>? _videoDone;
    private int _frameW, _frameH;

    /// <summary>영상이 도는 중인가 — 그동안 게임 시계와 갱신이 선다(<see cref="GameWindow"/> 의 틀 고리).</summary>
    internal bool Playing => _clip != null || _queue.Count > 0 || _loading != null || _video != null;

    /// <summary>
    /// 그 work 의 컷신을 건다 — 설정이 꺼져 있거나, 적 행동을 건너뛰는 중이거나, 영상 파일이 아직 없으면 아무것도 안 한다(없는 것은 뒤에서 받아 둔다).
    /// </summary>
    internal void Start(WorkData w)
    {
        if (!host._skillMovies || host._skippingAction || !WorkCutscenes.TryGetValue(w.Id, out var movies)) return;
        // 하나라도 없으면 이번에는 통째로 넘긴다 — 반만 나오면 이상하다(없는 .wgm 은 모두 뒤에서 받기를 건다).
        if (!movies.Select(Available).ToArray().All(ok => ok)) return;
        foreach (int m in movies) _queue.Enqueue(m);
    }

    /// <summary>
    /// 이야기 영상 하나를 건다 — 필드 스크립트 행동 902(<c>0x100f2c40</c>: <c>Mov\%04d.mov</c> 를 필드 위에 틀고, 도는 동안 필드 장면은 안 그린다).
    /// 기술 영상 설정과 상관없이 튼다. 원본에도 없고 받아 둔 .wgm 도 아직 없으면 false(뒤에서 받아 둔다) — 그때는 전처럼 영상 없이 지나간다.
    /// <paramref name="originalOnly"/> 면 원본 게임 폴더의 것만 튼다(오프닝 — 꾸러미에 없다).
    /// </summary>
    internal bool StartMovie(int movie, bool originalOnly = false)
    {
        if (originalOnly ? FindOriginal(movie) == null : !Available(movie)) return false;
        _queue.Enqueue(movie);
        return true;
    }

    /// <summary>
    /// 사용자 폴더의 영상 하나를 건다 — <paramref name="prepare"/> 는 뒤 스레드에서 돌며(캐비닛 꺼내기 · 베끼기에 몇 초 걸릴 수 있다) 틀 파일 자리나 null 을 준다.
    /// 그 동안은 검은 화면에 「영상 준비 중」을 띄우고 게임 시계를 세운다. 파일은 주 스레드에서 DirectShow 로 연다(<see cref="DirectShowVideo"/> — 소리도 DirectShow 가 낸다).
    /// 끝나면(넘기거나 다 보거나 못 열면) <paramref name="done"/>(틀었나)를 부른다.
    /// </summary>
    internal void StartVideo(Func<string?> prepare, Action<bool> done)
    {
        Skip();
        _videoDone = done;
        _loading = Task.Run(() =>
        {
            try { return prepare(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
        });
    }

    /// <summary>전투에 선 인물들이 가진 컷신 기술의 영상을 미리 받아 둔다(뒤에서, 기다리지 않는다).</summary>
    internal void Prefetch(IEnumerable<CharacterData> characters)
    {
        if (!host._skillMovies || host._db is not { } db) return;
        foreach (var c in characters)
            foreach (var ab in db.Abilities.Values.Where(a => c.HasAbility(a.Id)))
                foreach (int work in ab.WorkByLevel.Values)
                    if (WorkCutscenes.TryGetValue(work, out var movies))
                        foreach (int m in movies)
                            if (FindOriginal(m) == null) AssetPack.FetchExtra(Kind, FileName(m));   // 원본에 있으면 안 받는다
    }

    private int _prefetchedBattle = -1;

    /// <summary>전투가 새로 섰으면 한 번 미리 받기를 건다 — 틀마다 불러도 된다.</summary>
    internal void PrefetchForBattle()
    {
        if (!host._battleLoaded || host._scene.Id == _prefetchedBattle || !host._units.Any(u => u.Data != null)) return;
        _prefetchedBattle = host._scene.Id;
        Prefetch(host._units.Select(u => u.Data).OfType<CharacterData>());
    }

    /// <summary>실제 시각(초)으로 영상을 나아가게 한다 — 게임 속도와 상관없이 제 빠르기로 돈다.</summary>
    internal void Update(double realTime)
    {
        if (_loading != null || _video != null) { UpdateVideo(realTime); return; }
        if (_clip == null)
        {
            if (!_queue.TryDequeue(out int movie)) return;
            if (Open(movie) is not { } clip) return;       // 못 읽는 파일은 넘긴다
            _clip = clip;
            _frameW = clip.Width;
            _frameH = clip.Height;
            if (_frame.Length != _frameW * _frameH) _frame = new uint[_frameW * _frameH];
            _clipStart = realTime;
            _shown = -1;
            _soundStarted = false;
        }
        // 첫 컷이 풀릴 때까지(원본 Bink — 몇 ms) 시계와 소리를 세워 둔다. 그래야 소리와 컷이 같이 간다.
        if (!_soundStarted)
        {
            if (!_clip.Ready) { _clipStart = realTime; return; }
            _soundStarted = true;
            _clipStart = realTime;
            if (_clip.Sound != null && !GameWindow.Muted)
                host._mixer.PlayEffect(_clip.Sound, host._effectGain, _soundTag = ++host._soundTag);
        }
        int index = (int)((realTime - _clipStart) * _clip.Fps);
        if (index >= _clip.Count) { CloseClip(); return; }
        if (_clip.Show(index, _shown, _frame)) _shown = index;
    }

    private void UpdateVideo(double realTime)
    {
        if (_loading is { } loading)
        {
            if (!loading.IsCompleted) return;
            _loading = null;
            if (loading.Result is not { } path) { FinishVideo(false); return; }
            try { _video = DirectShowVideo.Open(path, GameWindow.Muted ? 0 : host._effectGain); }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidDataException or IOException or TypeLoadException)
            {
                GameWindow.TestRunTrace($"video open failed {ex.Message}");   // 시험 전용 줄
                FinishVideo(false);
                return;
            }
            _frameW = _video.Width;
            _frameH = _video.Height;
            _frame = new uint[_frameW * _frameH];
            _shown = -1;
        }
        if (_video is not { } v) return;
        if (v.Finished) { FinishVideo(true); return; }
        if (v.CopyFrame(_frame)) _shown = 0;
    }

    private void FinishVideo(bool played)
    {
        _loading = null;
        _video?.Dispose();
        _video = null;
        _frameW = _frameH = 0;
        var done = _videoDone;
        _videoDone = null;
        done?.Invoke(played);
    }

    /// <summary>지금 영상을 넘긴다 — 남은 영상도 다 넘기고 전투로 돌아간다.</summary>
    internal void Skip()
    {
        _queue.Clear();
        CloseClip();
        if (_loading != null || _video != null) FinishVideo(true);
    }

    private void CloseClip()
    {
        if (_soundTag != 0) host._mixer.StopEffect(_soundTag);
        _soundTag = 0;
        if (_clip is BinkClip bink)   // 시험 기록 — 원본 영상을 제 빠르기로 풀었나
            GameWindow.TestRunTrace($"bink close frames={bink.Count} shown={_shown} meanDecode={bink.Playback.MeanDecodeMs:F2}ms maxDecode={bink.Playback.MaxDecodeMs:F2}ms late={bink.Playback.LateTicks} bad={bink.Playback.BadFrames}");
        _clip?.Dispose();
        _clip = null;
    }

    private static Clip? Open(int movie)
    {
        if (FindOriginal(movie) is { } original)
        {
            try
            {
                var playback = BinkPlayback.Start(original.Path, original.Offset, original.Length);
                GameWindow.TestRunTrace($"bink open {movie:D4} {playback.Width}x{playback.Height} frames={playback.FrameCount} sound={playback.Sound?.Samples.Length ?? 0}");
                return new BinkClip(playback);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
            {
                GameWindow.TestRunTrace($"bink open failed {movie:D4} {ex.Message}");   // 못 풀면 받아 둔 .wgm 이 있으면 그것으로
            }
        }
        if (AssetPack.FetchExtra(Kind, FileName(movie)) is not { } path) return null;
        FileStream? file = null;
        try
        {
            file = File.OpenRead(path);
            var head = new byte[26];
            file.ReadExactly(head);
            if (head[0] != 'W' || head[1] != 'G' || head[2] != 'M' || head[3] != '1') throw new InvalidDataException(path);
            int width = BitConverter.ToUInt16(head, 4), height = BitConverter.ToUInt16(head, 6);
            int fpsNum = BitConverter.ToUInt16(head, 8), fpsDen = BitConverter.ToUInt16(head, 10);
            int count = BitConverter.ToInt32(head, 12), rate = BitConverter.ToInt32(head, 16), channels = BitConverter.ToUInt16(head, 20);
            int soundBytes = BitConverter.ToInt32(head, 22);
            if (width is <= 0 or > 2048 || height is <= 0 or > 2048 || fpsNum <= 0 || fpsDen <= 0 || count is < 0 or > 100000 || soundBytes < 0)
                throw new InvalidDataException(path);
            PcmSound? sound = null;
            if (soundBytes > 0 && channels is 1 or 2)
            {
                var pcm = new byte[soundBytes];
                file.ReadExactly(pcm);
                var samples = new short[soundBytes / 2];
                Buffer.BlockCopy(pcm, 0, samples, 0, samples.Length * 2);
                sound = new PcmSound(samples, rate, channels);
            }
            else file.Position += soundBytes;
            var offsets = new long[count];
            var size = new byte[4];
            for (int i = 0; i < count; i++)
            {
                offsets[i] = file.Position;
                file.ReadExactly(size);
                file.Position += BitConverter.ToInt32(size);
            }
            return new WgmClip(file, width, height, fpsNum / (double)fpsDen, offsets, sound);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            file?.Dispose();
            return null;
        }
    }

    /// <summary>이번 틀에 영상 컷을 그리나(<see cref="Draw"/> 와 같은 조건) — 사용자 영상을 여는 중에도 검은 화면을 그린다.</summary>
    internal bool Showing => _loading != null || (_clip != null || _video != null) && _frameW > 0 && _frame.Length == _frameW * _frameH;

    /// <summary>보이는 화면을 검게 덮고 영상 컷을 비율대로 맞춰 그린다 — 맨 위에 그린다.</summary>
    internal void Draw()
    {
        if (!Showing) return;
        int vw = host.ViewWidth, vh = host.ViewHeight;
        host.FillRect(host._camX, host._camY, vw, vh, 0xFF000000);
        if (_loading != null || _shown < 0)
        {
            if (_loading != null) host.UiText("영상 준비 중…", host._camX + vw / 2 - 40, host._camY + vh / 2 - 8, 0xC0FFFFFF, 12);
            host.UiText("클릭 · Esc: 넘기기", host._camX + vw - 130, host._camY + vh - 22, 0x80FFFFFF, 11);
            return;
        }
        PaintFrame();
        host.UiText("클릭 · Esc: 넘기기", host._camX + vw - 130, host._camY + vh - 22, 0x80FFFFFF, 11);
    }

    /// <summary>
    /// 마지막으로 보인 컷을 판에 한 번 더 깐다 — 모세스 워프 영상(<see cref="MosesScene.StepMosesSystemSwitch"/>)이 끝난 뒤 원본 효과 4 가
    /// 영상의 마지막 화면(백버퍼)에서 새 성계로 크로스페이드하므로, 그 「전 화면」을 떠 두려고 쓴다. 보인 컷이 없으면 false.
    /// </summary>
    internal bool PaintLastFrame()
    {
        if (_shown < 0 || _frameW <= 0 || _frame.Length != _frameW * _frameH) return false;
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        PaintFrame();
        return true;
    }

    private void PaintFrame()
    {
        int vw = host.ViewWidth, vh = host.ViewHeight;
        var clip = (W: _frameW, H: _frameH);
        // 화면에 다 들어가는 가장 큰 크기(가까운 점 뽑기).
        int w = Math.Min(vw, vh * clip.W / clip.H), h = w * clip.H / clip.W;
        int left = host._camX + (vw - w) / 2, top = host._camY + (vh - h) / 2;
        for (int y = 0; y < h; y++)
        {
            int by = top + y;
            if ((uint)by >= host.BoardHeight) continue;
            int sy = y * clip.H / h * clip.W;
            for (int x = 0; x < w; x++)
            {
                int bx = left + x;
                if ((uint)bx >= host.BoardWidth) continue;
                host._fb[by * host.BoardWidth + bx] = _frame[sy + x * clip.W / w] | 0xFF000000;
            }
        }
    }
}

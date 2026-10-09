using System.Drawing;
using System.Drawing.Imaging;
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
/// 게임은 Bink 영상을 못 풀어서 <c>tools/make_cutscenes.py</c> 가 미리 풀어 둔 <c>assets/effects/cut/NNNN.wgm</c>(JPEG 컷 + PCM 소리)을 읽는다.
/// 이 파일은 원본 게임 폴더에서 만들 수 없어 누구나 받아야 한다(<see cref="AssetPack.FetchExtra"/>) — 아직 없으면 이번에는 영상 없이 지나가고
/// 뒤에서 받아 둔다. 전투를 열 때 그 기술을 가진 인물이 있으면 미리 받는다(<see cref="Prefetch"/>).
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

    /// <summary>재생 중인 영상 파일 — 컷 자리 표와 소리.</summary>
    private sealed class Clip(FileStream file, int width, int height, double fps, long[] offsets, PcmSound? sound) : IDisposable
    {
        public int Width => width;
        public int Height => height;
        public double Fps => fps;
        public int Count => offsets.Length;
        public PcmSound? Sound => sound;

        public byte[] Jpeg(int index)
        {
            file.Position = offsets[index];
            Span<byte> head = stackalloc byte[4];
            file.ReadExactly(head);
            var bytes = new byte[BitConverter.ToInt32(head)];
            file.ReadExactly(bytes);
            return bytes;
        }

        public void Dispose() => file.Dispose();
    }

    private readonly Queue<int> _queue = new();
    private Clip? _clip;
    private double _clipStart;
    private int _shown = -1, _soundTag;
    private uint[] _frame = [];

    /// <summary>영상이 도는 중인가 — 그동안 게임 시계와 갱신이 선다(<see cref="GameWindow"/> 의 틀 고리).</summary>
    internal bool Playing => _clip != null || _queue.Count > 0;

    /// <summary>
    /// 그 work 의 컷신을 건다 — 설정이 꺼져 있거나, 적 행동을 건너뛰는 중이거나, 영상 파일이 아직 없으면 아무것도 안 한다(없는 것은 뒤에서 받아 둔다).
    /// </summary>
    internal void Start(WorkData w)
    {
        if (!host._skillMovies || host._skippingAction || !WorkCutscenes.TryGetValue(w.Id, out var movies)) return;
        // 하나라도 없으면 이번에는 통째로 넘긴다 — 반만 나오면 이상하다.
        var paths = movies.Select(m => AssetPack.FetchExtra(Kind, FileName(m))).ToArray();
        if (paths.Any(p => p == null)) return;
        foreach (int m in movies) _queue.Enqueue(m);
    }

    /// <summary>전투에 선 인물들이 가진 컷신 기술의 영상을 미리 받아 둔다(뒤에서, 기다리지 않는다).</summary>
    internal void Prefetch(IEnumerable<CharacterData> characters)
    {
        if (!host._skillMovies || host._db is not { } db) return;
        foreach (var c in characters)
            foreach (var ab in db.Abilities.Values.Where(a => c.HasAbility(a.Id)))
                foreach (int work in ab.WorkByLevel.Values)
                    if (WorkCutscenes.TryGetValue(work, out var movies))
                        foreach (int m in movies) AssetPack.FetchExtra(Kind, FileName(m));
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
        if (_clip == null)
        {
            if (!_queue.TryDequeue(out int movie)) return;
            if (Open(movie) is not { } clip) return;       // 못 읽는 파일은 넘긴다
            _clip = clip;
            _clipStart = realTime;
            _shown = -1;
            if (clip.Sound != null && !GameWindow.Muted)
                host._mixer.PlayEffect(clip.Sound, host._effectGain, _soundTag = ++host._soundTag);
        }
        int index = (int)((realTime - _clipStart) * _clip.Fps);
        if (index >= _clip.Count) { CloseClip(); return; }
        if (index != _shown) Decode(_clip, _shown = index);
    }

    /// <summary>지금 영상을 넘긴다 — 남은 영상도 다 넘기고 전투로 돌아간다.</summary>
    internal void Skip()
    {
        _queue.Clear();
        CloseClip();
    }

    private void CloseClip()
    {
        if (_soundTag != 0) host._mixer.StopEffect(_soundTag);
        _soundTag = 0;
        _clip?.Dispose();
        _clip = null;
    }

    private static Clip? Open(int movie)
    {
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
            return new Clip(file, width, height, fpsNum / (double)fpsDen, offsets, sound);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            file?.Dispose();
            return null;
        }
    }

    private void Decode(Clip clip, int index)
    {
        try
        {
            using var stream = new MemoryStream(clip.Jpeg(index));
            using var bitmap = new Bitmap(stream);
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                if (_frame.Length != bitmap.Width * bitmap.Height) _frame = new uint[bitmap.Width * bitmap.Height];
                for (int y = 0; y < bitmap.Height; y++)
                    new Span<uint>((void*)(data.Scan0 + y * data.Stride), bitmap.Width).CopyTo(_frame.AsSpan(y * bitmap.Width, bitmap.Width));
            }
            finally { bitmap.UnlockBits(data); }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { /* 깨진 컷은 앞 컷을 그대로 둔다 */ }
    }

    /// <summary>보이는 화면을 검게 덮고 영상 컷을 비율대로 맞춰 그린다 — 맨 위에 그린다.</summary>
    internal void Draw()
    {
        if (_clip is not { } clip || _frame.Length != clip.Width * clip.Height) return;
        int vw = host.ViewWidth, vh = host.ViewHeight;
        host.FillRect(host._camX, host._camY, vw, vh, 0xFF000000);
        // 화면에 다 들어가는 가장 큰 크기(가까운 점 뽑기).
        int w = Math.Min(vw, vh * clip.Width / clip.Height), h = w * clip.Height / clip.Width;
        int left = host._camX + (vw - w) / 2, top = host._camY + (vh - h) / 2;
        for (int y = 0; y < h; y++)
        {
            int by = top + y;
            if ((uint)by >= host.BoardHeight) continue;
            int sy = y * clip.Height / h * clip.Width;
            for (int x = 0; x < w; x++)
            {
                int bx = left + x;
                if ((uint)bx >= host.BoardWidth) continue;
                host._fb[by * host.BoardWidth + bx] = _frame[sy + x * clip.Width / w] | 0xFF000000;
            }
        }
        host.DrawText("클릭 · Esc: 넘기기", host._camX + vw - 130, host._camY + vh - 22, 0x80FFFFFF, 11);
    }
}

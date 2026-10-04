using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>UI 그림 그리기 — 타이틀·모세스·필드·전투가 다 쓰는 Obs 그림 풀기와 섞기.</summary>
internal sealed unsafe partial class GameWindow
{
    internal readonly Dictionary<int, UiSprite?> _ui = [];

    /// <summary>아직 안 푼 그림 파일 자리 — 처음 쓸 때 푼다(이펙트가 많아 시작할 때 다 풀면 몇 초 걸린다).</summary>
    internal readonly Dictionary<int, string> _uiPaths = [];

    /// <summary>그림을 (처음 쓸 때 풀어) 돌려준다.</summary>
    internal UiSprite? UiFor(int id)
    {
        // 자료는 배경 스레드(LoadScene)가 읽으면서도 이 캐시를 채우고, 그리기 스레드도 처음 쓰는 그림을 여기서 푼다 —
        // 잠그지 않으면 Dictionary 가 깨진다(「Operations that change non-concurrent collections…」로 죽었다).
        lock (_ui)
        {
            if (_ui.TryGetValue(id, out var sprite)) return sprite;
            // 아직 그림 목록을 못 읽었으면(자료 읽기 전) 기억해 두지 않는다 — 나중에 다시 묻는다.
            if (!_uiPaths.TryGetValue(id, out string? path)) return null;
            // 그림이 한 장도 없는 Obs(메테오의 Obs 0311 처럼 소리 키만 든 것)는 ArgumentException 으로 떨어진다 — 없는 그림으로 친다.
            try { return _ui[id] = new UiSprite(ObsSprite.Decode(path), ObsMotionTable.Load(path)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { return _ui[id] = null; }
        }
    }

    /// <summary>그림 섞기. <c>Dim</c> 은 그림 색을 15/31 로 어둡게 찍는다 — 꺼진 목록 줄(원본 물들이기 방식 2 · 세기 16, 분석-캐릭터 st-5).</summary>
    /// <summary>
    /// 그림 섞기. Dodge·Screen 은 모션 섞기 키 10·12 — 원본 색표(<c>0x1000b7c0</c>, 5비트 채널 a=바탕·b=그림):
    /// 10 = <c>min(31, a·32 / (32−b))</c>(닷지 — 검은 그림은 바탕 그대로), 12 = <c>max + (31−max)·min/31</c>(스크린).
    /// </summary>
    internal enum UiBlend { Alpha, Add, AddDim, Darken, Dim, Dodge, Screen }

    /// <summary>모션 섞기 키(종류 3) 값 → 섞기. 17 가산 · 10 닷지 · 12 스크린, 나머지는 보통.</summary>
    internal static UiBlend BlendOf(int key) => key switch { 17 => UiBlend.Add, 10 => UiBlend.Dodge, 12 => UiBlend.Screen, _ => UiBlend.Alpha };

    /// <summary>
    /// 섞기 키 1~7 의 비침 — 원본 색표(<c>0x1000b7c0</c>)는 k 에서 <c>(4k·그림 + (31−4k)·바탕)/31</c>, 8 은 그림 그대로다.
    /// 그 밖의 키는 1(불투명). 분석-필드 「Fld 0354 돌문」.
    /// </summary>
    internal static double BlendFade(int key) => key is >= 1 and <= 7 ? 4 * key / 31.0 : 1;

    internal static uint DodgeColor(uint d, uint c)
    {
        uint Ch(int shift)
        {
            int a = (int)(d >> shift & 0xFF), b = (int)(c >> shift & 0xFF);
            return (uint)Math.Min(255, b >= 255 ? 255 : a * 256 / (256 - b));
        }
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    internal static uint ScreenColor(uint d, uint c)
    {
        uint Ch(int shift)
        {
            int a = (int)(d >> shift & 0xFF), b = (int)(c >> shift & 0xFF), hi = Math.Max(a, b), lo = Math.Min(a, b);
            return (uint)Math.Min(255, hi + (255 - hi) * lo / 255);
        }
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>그림을 이 네모 안으로만 그린다 — 필드처럼 640×480 틀 밖으로 새면 안 되는 화면이 쓴다.</summary>
    internal (int Left, int Top, int Width, int Height)? _uiClip;

    /// <summary>UI Obs 한 장을 모션표 틱에 맞춰 (x, y) 에 그린다(컷의 X·Y 가 기준점에서 왼쪽 위까지 거리). 그렸으면 true.</summary>
    /// <param name="fade">
    /// 0~1 의 밝기 — 필드 인물이 서서히 사라지고 나타날 때(행동 210·211) 쓴다. 1 이면 그대로 그린다.
    /// </param>
    internal bool DrawUi(int obs, int motion, int tick, int x, int y, UiBlend blend, bool loop = true, double fade = 1, bool mirror = false)
    {
        var clip = _uiClip;
        if (UiFor(obs) is not { } sprite || sprite.FrameAt(motion, tick, loop, mirror) is not { } f) return false;
        int left = x + f.X, top = y + f.Y;
        // 그릴 네모를 판과 자르기 네모로 먼저 자른다 — 전에는 픽셀마다 경계를 검사해, 화면보다 큰 필드 그림(층마다 통짜 그림)이 여러 장이면
        // 한 틀에 40~160ms 가 들었다(사용자 보고: Fld 0017 이 툭툭 끊김).
        int x0 = Math.Max(0, left), x1 = Math.Min(BoardWidth, left + f.W), y0 = Math.Max(0, top), y1 = Math.Min(BoardHeight, top + f.H);
        if (clip is { } box)
        {
            x0 = Math.Max(x0, box.Left); x1 = Math.Min(x1, box.Left + box.Width);
            y0 = Math.Max(y0, box.Top); y1 = Math.Min(y1, box.Top + box.Height);
        }
        if (x0 >= x1 || y0 >= y1) return true;
        var src = f.Px;
        var fb = _fb;
        // 가장 흔한 꼴(불투명 덮어쓰기)은 곧바로 옮긴다.
        if (blend == UiBlend.Alpha && fade >= 1)
        {
            for (int py = y0; py < y1; py++)
            {
                int si = (py - top) * f.W + (x0 - left), di = py * BoardWidth + x0;
                for (int n = x1 - x0; n > 0; n--, si++, di++)
                {
                    uint c = src[si];
                    if ((c & 0xFF000000) != 0) fb[di] = c | 0xFF000000;
                }
            }
            return true;
        }
        for (int py = y0; py < y1; py++)
        {
            int yy = py - top;
            for (int px = x0; px < x1; px++)
            {
                int xx = px - left;
                uint c = f.Px[yy * f.W + xx];
                if ((c & 0xFF000000) == 0) continue;
                int i = py * BoardWidth + px;
                uint d = _fb[i];
                uint drawn = blend switch
                {
                    UiBlend.Add => AddColor(d, c, 256),
                    UiBlend.AddDim => AddColor(d, c, 100),
                    UiBlend.Darken => ScaleColor(d, 11, 31),
                    UiBlend.Dim => ScaleColor(c, 15, 31),
                    UiBlend.Dodge => DodgeColor(d, c),
                    UiBlend.Screen => ScreenColor(d, c),
                    _ => c | 0xFF000000,
                };
                // 밝기가 1 보다 작으면 바탕과 섞는다 — 원본의 8단계 밝기를 그대로 흉내 낸다.
                if (fade < 1)
                {
                    int k = Math.Clamp((int)(fade * 256), 0, 256);
                    uint Mix(int shift) =>
                        (uint)(((int)(drawn >> shift & 0xFF) * k + (int)(d >> shift & 0xFF) * (256 - k)) / 256);
                    drawn = 0xFF000000 | Mix(16) << 16 | Mix(8) << 8 | Mix(0);
                }
                _fb[i] = drawn;
            }
        }
        return true;
    }

    /// <summary>
    /// 더하기 합성에 넣을 채널 값 — 아주 어두운 값(≤ 0x18)은 0 으로, 0x30 부터는 그대로, 그 사이는 곧게 잇는다.
    /// 빛 이펙트 그림은 가장자리가 완전한 검정이 아니라 어두운 갈색(0x0C · 0x1C)으로 끝나는 것이 많아(이데아 캐논 Obs 0987),
    /// 그대로 더하면 그림 네모가 통째로 밝게 떴다 — 여러 장이 겹치면 더 뚜렷하다(사용자 보고). 원본 화면과 견줘 정한 값은 아니다.
    /// </summary>
    internal static readonly byte[] AddFloor = [.. Enumerable.Range(0, 256).Select(v => (byte)(v <= 0x18 ? 0 : v < 0x30 ? (v - 0x18) * 2 : v))];

    internal static uint AddColor(uint d, uint c, int weight)
    {
        uint Ch(int shift) => (uint)Math.Min(255, (int)(d >> shift & 0xFF) + AddFloor[c >> shift & 0xFF] * weight / 256);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    internal static uint ScaleColor(uint d, int num, int den)
    {
        uint Ch(int shift) => (uint)((int)(d >> shift & 0xFF) * num / den);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }
}

/// <summary>UI 용 Obs 하나 — 벌·컷 그림과 모션표.</summary>
internal sealed class UiSprite
{
    internal readonly Dictionary<(int Sub, int Slot), SpriteFrame> _frames = [];
    internal readonly Dictionary<(int Sub, int Slot), SpriteFrame> _mirrored = [];
    internal readonly ObsMotionTable? _table;

    public UiSprite(IReadOnlyList<ObsMotion> motions, ObsMotionTable? table)
    {
        _table = table;
        // 모션표 키는 벌 안 순번이 아니라 <b>장 번호</b>다 — 0471 처럼 번호에 구멍이 있으면 둘이 다르다
        // (분석-모션 「Obs 파일 갈래(0471 같은 UI 그림)」).
        foreach (var motion in motions)
            foreach (var frame in motion.Frames)
                _frames[(motion.Id, frame.SlotId)] = SpriteFrame.From(frame);
    }

    internal MotionKey? KeyAt(int motion, int tick, bool loop)
    {
        if (_table?.Clips.GetValueOrDefault(motion) is { } clip)
        {
            // 한 번만 재생하는 것(이펙트)은 모션이 끝나면 null — 그때 사라진다.
            if (!loop && tick >= Math.Max(clip.Length, 1)) return null;
            return clip.KeyAt(tick, loop);
        }
        return loop ? new MotionKey(0, 0, 0, 0) : null;
    }

    /// <summary>
    /// 모션 m 의 tick 째 컷. 되풀이가 아니면 모션이 끝난 뒤에는 null(이펙트가 사라지는 때).
    /// <paramref name="mirror"/> 면 좌우로 뒤집은 컷을 준다 — 인물이 오른쪽을 볼 때, 몸에 붙는 무기·검기 층도
    /// 몸과 같이 뒤집어야 하기 때문이다(안 뒤집으면 왼쪽을 보고 치는 그림 그대로 남아 반대쪽에 나타나 보인다).
    /// </summary>
    public SpriteFrame? FrameAt(int motion, int tick, bool loop = true, bool mirror = false)
    {
        if (KeyAt(motion, tick, loop) is not { } k || !_frames.TryGetValue((k.SubentryId, k.Slot), out var frame)) return null;
        if (!mirror) return frame;
        if (!_mirrored.TryGetValue((k.SubentryId, k.Slot), out var m)) _mirrored[(k.SubentryId, k.Slot)] = m = frame.Mirrored();
        return m;
    }

    /// <summary>그 모션이 한 바퀴 도는 데 걸리는 틱 — 모르면 0.</summary>
    public int MotionLength(int motion) => _table?.Clips.GetValueOrDefault(motion)?.Length ?? 0;

    /// <summary>그 모션의 키 묶음(자식 그림·소리 따위) — 없으면 null.</summary>
    public ObsMotionClip? Clip(int motion) => _table?.Clips.GetValueOrDefault(motion);

    /// <summary>그 모션의 tick 틱 섞기 방식(17 = 더하기 합성).</summary>
    public int BlendAt(int motion, int tick) => _table?.Clips.GetValueOrDefault(motion)?.BlendAt(tick) ?? 0;
}

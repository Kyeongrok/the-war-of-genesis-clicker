using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>인물이 바라보는 쪽. 오른쪽은 옆모습(왼쪽) 그림을 좌우로 뒤집어 쓴다.</summary>
public enum Facing { Up, Down, Left, Right }

/// <summary>모션 안의 그림 한 칸 — 몇 틱째부터 몇 틱 동안 어느 몸짓벌(<see cref="SubentryId"/>)의 몇 번째 장을 보여 주나.</summary>
public readonly record struct MotionKey(int Start, int Length, int SubentryId, int Slot);

/// <summary>모션 하나 — 번호, 전체 길이(틱), 그림 키들(시작 틱 순서).</summary>
public sealed record ObsMotionClip(int Id, int Length, IReadOnlyList<MotionKey> Keys)
{
    /// <summary><paramref name="tick"/> 틱에 보일 그림 키. 반복이면 길이로 감아 돌리고, 아니면 마지막 키에서 멈춘다.</summary>
    public MotionKey? KeyAt(int tick, bool loop)
    {
        if (Keys.Count == 0) return null;
        int length = Math.Max(Length, Keys[^1].Start + Keys[^1].Length);
        if (length <= 0) return Keys[0];
        tick = loop ? ((tick % length) + length) % length : Math.Min(tick, length - 1);

        MotionKey found = Keys[0];
        foreach (var key in Keys)
        {
            if (key.Start > tick) break;
            found = key;
        }
        return found;
    }
}

/// <summary>
/// <c>Obs/NNNN.obs</c> 안의 모션표 — "동작 × 방향"마다 어느 컷을 몇 틱씩 보여 주는지.
/// </summary>
/// <remarks>
/// <c>G3PartII.dll</c> 의 <c>LoadG4ObsFile</c>(<c>0x1002f050</c>)·모션 로더(<c>0x1002fa00</c>)가 읽는 순서
/// (옵시디안 분석-모션 "스킬 사용 모션", <c>tools/re/skill_motion.py</c>):
/// <code>
/// u16, u16 벌 개수 n, u16 최대번호, (u16 번호, u32 위치) × n      ← 몸짓벌 색인표
/// u16 모션 개수 m, u16 최대번호, (u16 번호, u32 위치) × m          ← 모션표
/// 모션: u16 번호, u16 nA, u16 nB, u16 길이(틱), u16 nU, (u16 벌, u16 장) × nU,
///       키 26바이트 × (nA + nB) — u16 종류, u16 시작틱, u16 길이, i16 × 10
/// </code>
/// 그림 키는 B 목록의 종류 0 이다(인자 0 = 몸짓벌 번호, 1 = 장 번호). 다른 종류(소리, 다른 Obs 모션 …)는 건너뛴다.
/// 모션 번호 = 동작 × 3 + 방향(0 뒷모습, 1 옆모습(왼쪽), 2 앞모습; 오른쪽은 옆모습을 뒤집음). 동작 0 = 서기, 1 = 걷기.
/// 없는 모션이면 서기로 떨어진다(<c>SetAction 0x10072820</c>).
/// </remarks>
public sealed class ObsMotionTable
{
    public const int ActionStand = 0, ActionWalk = 1;
    public const int DirectionBack = 0, DirectionSide = 1, DirectionFront = 2;

    private readonly Dictionary<int, ObsMotionClip> _clips;

    private ObsMotionTable(Dictionary<int, ObsMotionClip> clips) => _clips = clips;

    public IReadOnlyDictionary<int, ObsMotionClip> Clips => _clips;

    public static ObsMotionTable? Load(string path) => Parse(File.ReadAllBytes(path));

    public static ObsMotionTable? Parse(byte[] b)
    {
        try
        {
            int subentries = U16(b, 2);
            int o = 6 + 6 * subentries;
            if (o + 4 > b.Length) return null;
            int motionCount = U16(b, o);
            o += 4;

            var clips = new Dictionary<int, ObsMotionClip>();
            for (int i = 0; i < motionCount; i++)
            {
                int id = U16(b, o + 6 * i);
                int p = (int)U32(b, o + 6 * i + 2);
                int na = U16(b, p + 2), nb = U16(b, p + 4), length = U16(b, p + 6), nu = U16(b, p + 8);
                p += 10 + 4 * nu;

                var keys = new List<MotionKey>();
                for (int k = 0; k < na + nb; k++, p += 26)
                {
                    if (k < na || U16(b, p) != 0) continue;
                    keys.Add(new MotionKey(U16(b, p + 2), U16(b, p + 4), S16(b, p + 6), S16(b, p + 8)));
                }
                keys.Sort((x, y) => x.Start.CompareTo(y.Start));
                clips[id] = new ObsMotionClip(id, length, keys);
            }
            return new ObsMotionTable(clips);
        }
        catch (ArgumentOutOfRangeException) { return null; }
        catch (IndexOutOfRangeException) { return null; }
    }

    /// <summary>바라보는 쪽 → 모션표 방향 번호(0 뒷모습, 1 옆모습, 2 앞모습). 오른쪽도 1 이고 그릴 때 뒤집는다.</summary>
    public static int DirectionOf(Facing facing) => facing switch
    {
        Facing.Up => DirectionBack,
        Facing.Down => DirectionFront,
        _ => DirectionSide,
    };

    /// <summary>동작·방향으로 모션을 고른다. 없으면 같은 방향의 서기로 떨어진다.</summary>
    public ObsMotionClip? Resolve(int action, int direction)
    {
        if (_clips.TryGetValue(action * 3 + direction, out var clip) && clip.Keys.Count > 0) return clip;
        return _clips.GetValueOrDefault(direction);
    }

    private static int U16(byte[] b, int o) => b[o] | (b[o + 1] << 8);
    private static short S16(byte[] b, int o) => (short)U16(b, o);
    private static uint U32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
}

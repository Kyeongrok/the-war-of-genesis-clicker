using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 비(어빌리티 3) — 검기가 날아가 맞은 적을 <b>시전자가 보는 쪽으로 밀어낸다</b>(fg-18). 핸들러 <c>0x1007fed0</c>.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>단계 0 — 시전자 동작 12(베기), 소리 껍데기 379.</item>
/// <item>단계 1(8틱 뒤) — 검기 Obs 43 이 시전자(높이 +70)에서 겨눈 칸으로 빠르기 30 으로 날아간다(<c>0x100c3340</c>·<c>0x100c3490</c>). 닿으면 피해.</item>
/// <item>단계 2 — 맞은 인물이 시전자 쪽으로 돌아서(<c>0x100e56c0</c>) 맞음 동작 2 를 붙든 채(반복 1000),
/// 시전자가 보는 쪽으로 <c>work+0xc/4 − 거리</c> 칸 밀려난다 — work+0xc 는 사거리 최대(4분의 1칸)라 <b>사거리 끝까지 밀린다</b>.
/// 밀 칸이 5 넘으면 빠르기 35·곱 0.87, 3 넘으면 28·0.83, 아니면 20·0.8(틱마다 곱해 줄고 1 아래로는 안 준다). Obs 109 가 붙는다.</item>
/// <item>단계 3(다 밀려나면) — 맞은 인물은 서기(동작 0)로, 시전자는 동작 24 로 돌아온다.</item>
/// </list>
/// 막힌 칸·다른 인물·맵 끝에서는 거기서 멈춘다(원본 이동기의 막힘 처리는 확인 안 함 — 가설).
/// 전에는 도구 표(동작 5·7·12·<b>2</b>·0·24)를 모두 시전자에게 틀어서 시전자가 맞는 모션을 했고, 밀어내기는 없었다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>비 레벨 1~20 의 work.</summary>
    private static readonly HashSet<int> BiWorks = [10, .. Enumerable.Range(221, 19)];

    private const int BiTrailObs = 109;

    /// <summary>원본에서 한 칸 = 월드 40.</summary>
    private const int WorldPerCell = 40;

    /// <summary>시전자 쪽 동작만 — 맞음(2)·서기(0)는 맞은 인물의 것이다. 24 는 다 밀려난 뒤 넉백 루틴이 튼다.</summary>
    private static readonly (int[] Actions, AbilityEffect[] Effects) BiScript =
        ([5, 7, 12], [new(379, 0, false, 0), new(43, 0, false, 42, 0, 1, true)]);

    /// <summary>
    /// 밀어내기 — <paramref name="target"/> 을 시전자가 보는 쪽으로 사거리 끝까지(막히면 거기까지) 밀고, 시전자는 동작 24 로 돌아온다.
    /// </summary>
    private IEnumerable<bool> KnockbackRoutine(UnitState user, WorkData w, UnitState target, int soulDrain = 0, int pushCells = -1)
    {
        var (dc, dr) = user.Facing switch
        {
            Facing.Up => (0, -1),
            Facing.Down => (0, 1),
            Facing.Left => (-1, 0),
            _ => (1, 0),
        };
        int distance = dc != 0 ? Math.Abs(target.Col - user.Col) : Math.Abs(target.Row - user.Row);
        int push = pushCells >= 0 ? pushCells : Math.Max(0, w.RangeMax - distance);
        int col = target.Col, row = target.Row, moved = 0;
        while (moved < push && CanStand(col + dc, row + dr, target)) { col += dc; row += dr; moved++; }
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"knockback work {w.Id}: {user.ChrCode}({user.Col},{user.Row}) {user.Facing} → {target.ChrCode}({target.Col},{target.Row}) 거리 {distance} 밀 칸 {push} → {moved}칸 ({col},{row})" + Environment.NewLine);

        if (moved > 0)
        {
            // 한 틱에 가는 거리 — 원본 이동기처럼 틱마다 곱해 줄고 1 아래로는 안 준다.
            var (speed, factor) = push > 5 ? (35.0, 0.87) : push > 3 ? (28.0, 0.83) : (20.0, 0.8);
            var steps = new List<double>();
            for (double pos = 0, total = moved * WorldPerCell; pos < total;)
            {
                pos = Math.Min(total, pos + speed);
                speed = Math.Max(1, speed * factor);
                steps.Add(pos / total);
            }

            target.Facing = Opposite(user.Facing);
            target.PlayAction(HitAction, steps.Count / TicksPerSecond + 0.05);
            var (tx, ty) = UnitFoot(target);
            _effects.Add((BiTrailObs, 0, _lastTime, tx, ty));
            target.BeginSlide(col, row);
            double start = _lastTime;
            for (int k; (k = (int)((_lastTime - start) * TicksPerSecond)) < steps.Count;)
            {
                target.SetSlide(steps[k]);
                yield return true;
            }
            target.SetSlide(1);
            target.PlayAction(ObsMotionTable.ActionStand, 0);
        }
        // 다이나믹 크래쉬 — 다 밀린 뒤 대상 SOUL −min(10, SOUL)(0x1009fd40 단계 2).
        if (soulDrain > 0 && _db is { } db)
        {
            int drain = Math.Min(soulDrain, target.Soul);
            target.Soul -= drain;
            ShowNumber(target, $"{db.T(41)} -{drain}", MissColor);
        }
        if (BiWorks.Contains(w.Id)) PlayAction(user, DrawnAction(user, 24));
    }

    private static Facing Opposite(Facing f) => f switch
    {
        Facing.Up => Facing.Down,
        Facing.Down => Facing.Up,
        Facing.Left => Facing.Right,
        _ => Facing.Left,
    };
}

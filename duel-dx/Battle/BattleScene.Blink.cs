using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 이동 종류 1(<c>.chr</c> 파일 17 == 1) — 걷지 않고 순간이동한다(이동 핸들러 <c>0x10076680</c> 갈래, ba-20 P4).
/// </summary>
/// <remarks>
/// 마리아·카르마타·바루스·장교·루나스·엠블라·시빌라·팬텀마리아(14개 chr). 차례(<c>0x10076688~0x10076e5a</c>):
/// 동작 19(모션 57·58·59) + 16틱에 걸쳐 옅어짐 + 잔상 12개(네 방향 ±20i px, 늦춤 2i 틱, 수명 10, 섞기 6−i) →
/// 숨김·카메라를 도착 칸으로 → 15틱 뒤 칸을 옮기고 15틱 더 숨김 → 동작 20(모션 60·61·62) + 잔상 12개(늦춤 (5−i)×2) + 다시 짙어짐.
/// 카메라가 설 때까지 기다리는 단계 2 는 줄였다(곧바로 15틱 기다림).
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    internal sealed class Blink
    {
        public (int Col, int Row) Dest, From;
        public double Start;
        public int Stage;
        public int Dir;      // 0 위 · 1 옆 · 2 아래 — 모션 57+Dir / 60+Dir
    }

    internal readonly Dictionary<UnitState, Blink> _blinks = [];

    /// <summary>순간이동 잔상 — 주인, 모션, 좌우, 뜨는 시각, 발 자리, 짙기, 수명 틱.</summary>
    internal readonly List<(UnitState Owner, int Motion, bool Mirror, double Start, int X, int Y, double Fade, int Life)> _blinkGhosts = [];

    internal const int BlinkFadeTicks = 16, BlinkHiddenTicks = 15, BlinkGhostLife = 10;

    /// <summary>걸을 길이 걸린 유닛이 순간이동꾼이면 걷기 대신 순간이동을 시작한다. 시작했으면 true.</summary>
    internal bool TryBeginBlink(UnitState unit)
    {
        if (unit.Data is not { MoveKind: 1 } || unit.Path.Count == 0 || unit.IsMoving) return false;
        var dest = unit.Path.Last();
        var first = unit.Path.Peek();
        unit.Path.Clear();
        if (dest == (unit.Col, unit.Row)) return true;
        // 목적지 쪽으로 돈다(0x1001ed50 → 0x100e56f0).
        unit.Facing = FacingToward(unit.Col, unit.Row, dest.Col, dest.Row);
        _ = first;
        int dir = unit.Facing switch { Facing.Up => 0, Facing.Down => 2, _ => 1 };
        _blinks[unit] = new Blink { Dest = dest, From = (unit.Col, unit.Row), Start = host._lastTime, Stage = 0, Dir = dir };
        PlayBlinkMotion(unit, 57 + dir, BlinkFadeTicks + 2 * BlinkHiddenTicks);
        SpawnBlinkGhosts(unit, 57 + dir, arriving: false);
        return true;
    }

    /// <summary>그 모션이 없는 그림(엠블라 Obs 577)은 서기 그대로 둔 채 바쁨만 건다.</summary>
    internal void PlayBlinkMotion(UnitState unit, int motion, int ticks)
    {
        bool has = host._sprites.TryGetValue(unit.ChrCode, out var sprite) && sprite.MotionTicks(motion) > 0;
        if (has) unit.PlayMotion(motion, ticks / TicksPerSecond, loop: false);
        else unit.PlayAction(0, ticks / TicksPerSecond);
    }

    internal void SpawnBlinkGhosts(UnitState unit, int motion, bool arriving)
    {
        if (!host._sprites.TryGetValue(unit.ChrCode, out var sprite) || sprite.MotionTicks(motion) <= 0) return;
        var (fx, fy) = host.Btl.UnitFoot(unit);
        bool mirror = unit.Facing == Facing.Right;
        for (int i = 1; i <= 3; i++)
        {
            double start = host._lastTime + (arriving ? (5 - i) * 2 : 2 * i) / TicksPerSecond;
            double fade = (6 - i) * 4 / 31.0;   // 섞기 단계 6−i
            foreach (var (dx, dy) in new[] { (20 * i, 0), (-20 * i, 0), (0, 16 * i), (0, -16 * i) })   // 월드 20px = 화면 세로 16px
                _blinkGhosts.Add((unit, motion, mirror, start, fx + dx, fy + dy, fade, BlinkGhostLife));
        }
    }

    internal void StepBlinks()
    {
        if (_blinks.Count == 0) return;
        foreach (var (unit, b) in _blinks.ToArray())
        {
            double t = (host._lastTime - b.Start) * TicksPerSecond;
            // 죽었거나, 그 사이 사건이 자리를 옮겼으면(200·201·ResetTo) 순간이동을 그만둔다 — 옛 도착 칸으로 되돌리지 않게.
            if (!unit.Alive || (b.Stage < 2 && (unit.Col, unit.Row) != b.From)) { unit.Fade = 1; _blinks.Remove(unit); continue; }
            switch (b.Stage)
            {
                case 0:   // 2틱마다 한 단계씩 옅어진다(보통 → 단계 1 = 13%)
                    unit.Fade = Math.Max(4 / 31.0, 1 - t / BlinkFadeTicks * (1 - 4 / 31.0));
                    if (t < BlinkFadeTicks) break;
                    unit.Fade = 0;
                    if (unit.OnField && unit.LeaderIndex < 0) CenterOnCell(b.Dest.Col, b.Dest.Row);   // 부하는 카메라를 안 끈다
                    b.Stage = 1;
                    break;
                case 1:   // 15틱 뒤 칸을 옮기고 도착 잔상을 띄운다. 본체는 15틱 더 숨는다.
                    if (t < BlinkFadeTicks + BlinkHiddenTicks) break;
                    unit.WarpTo(b.Dest.Col, b.Dest.Row);
                    PlayBlinkMotion(unit, 60 + b.Dir, BlinkHiddenTicks + 18);
                    SpawnBlinkGhosts(unit, 60 + b.Dir, arriving: true);
                    b.Stage = 2;
                    break;
                default:
                {
                    double shown = t - BlinkFadeTicks - 2 * BlinkHiddenTicks;
                    unit.Fade = shown < 0 ? 0 : Math.Min(1, 4 / 31.0 + shown / BlinkFadeTicks * (1 - 4 / 31.0));
                    if (shown < 18) break;
                    unit.Fade = 1;
                    _blinks.Remove(unit);
                    break;
                }
            }
        }
    }

    internal void DrawBlinkGhosts()
    {
        host.LegionStageAb.DrawLegionGhosts();
        for (int i = _blinkGhosts.Count - 1; i >= 0; i--)
        {
            var g = _blinkGhosts[i];
            if (host._lastTime < g.Start) continue;
            int tick = (int)((host._lastTime - g.Start) * TicksPerSecond);
            if (tick >= g.Life || !host._sprites.TryGetValue(g.Owner.ChrCode, out var sprite)
                || sprite.FrameOfMotion(g.Motion, tick, g.Mirror) is not { } frame) { _blinkGhosts.RemoveAt(i); continue; }
            host.BlitMasked(frame.Px, frame.W, frame.H, g.X + frame.X, g.Y + frame.Y, fade: g.Fade);
        }
    }
}

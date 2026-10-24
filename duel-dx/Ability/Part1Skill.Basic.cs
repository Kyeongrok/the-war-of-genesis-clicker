using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 기본 공격 — 인물 칸 9(없으면 무기 갈래)의 Attack 번호로 원본 <c>alpha.dll</c> 의 기본 공격 핸들러를 옮겼다(볼트 분석-파트1-기술-b 「기본 공격」 절).
/// 칼 · 창 · 빔소드 · 활 · 단발사격 · 빔라이플과 용병 칼 · 창 · 빔소드 · 단발 · 2연사 · 3연사 · 빔라이플 · 2연 · 화이어 애로우 · 아이스미사일 ·
/// 라이트닝볼츠 · 부메랑 · 던지기 · 바즈카 · 더블 어택 · 쌍권총 · 포격 · 용병파.
/// </summary>
/// <remarks>
/// <para>앞머리(Attack +0x69)는 모두 0 — 핸들러가 곧바로 돈다. 칼질 갈래는 시전자 동작만 걸고 피해는 그 모션의 타격 키(종류 5)에서 난다.
/// 쏘는 갈래는 탄 · 맞은 자리 그림에 붙인 맞히기 사건(<c>0x100768f0</c> 시작 때 · <c>0x10076950</c> 사라질 때 · <c>0x100769b0</c> 칸이 바뀔 때)으로 친다.
/// 총소리 · 휘두르는 소리는 시전자 모션의 소리 키(해럴드 Obs 280 동작 2 = 소리 32 · 40)이고, 핸들러가 따로 내는 소리는 CSound 2038(탄이 닿을 때) ·
/// 6 · 81 · 80(용병 마법 셋, 시작 때)뿐이다.</para>
/// <para>피해는 전투 쪽(Part1Rules)이 세고 타 수(+0x66)를 세기에 곱하므로, 여러 발(2연사 · 3연사 · 쌍권총)도 대상마다 첫 발에서 한 번만 친다.</para>
/// <para>원본과 다르게 한 것: 맞은 자리(<c>0x10039d90</c> — 발에서 x ±10, 높이는 그림 높이의 절반 안 아무 데)는 발 위 8~31px 로 어림했다.
/// 탄에 붙는 카메라 대기열(<c>0x1009a0c0</c>)은 탄이 뜰 때 겨눈 자리로 카메라를 보내는 것으로 어림했다.</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    private enum BasicStyle { None, Melee, Arrow, Shot, Beam, Missile, Boomerang, Throw, Cannon }

    /// <summary>이 틀이 그리는 파트1 기본 공격(Attack 번호)인가.</summary>
    internal static bool DrawsBasic(int attackId) => BasicKind(attackId) != BasicStyle.None;

    /// <summary>Attack 번호 → 핸들러 갈래(표 <c>0x100433f0</c> · <c>0x1004354c</c>/<c>0x1004342c</c> · <c>0x10043684</c>).</summary>
    private static BasicStyle BasicKind(int id) => id switch
    {
        1 or 2 or 3 or 280 or (>= 297 and <= 305) or (>= 318 and <= 320) or (>= 352 and <= 354) => BasicStyle.Melee,
        4 => BasicStyle.Arrow,
        5 or (>= 306 and <= 314) or (>= 342 and <= 344) or (>= 355 and <= 357) => BasicStyle.Shot,
        10 or (>= 315 and <= 317) => BasicStyle.Beam,
        >= 324 and <= 332 => BasicStyle.Missile,
        >= 333 and <= 335 => BasicStyle.Boomerang,
        >= 336 and <= 338 => BasicStyle.Throw,
        362 => BasicStyle.Cannon,
        _ => BasicStyle.None,
    };

    /// <summary>
    /// 시전자의 파트1 동작 — 용병 갈래는 레벨 1 · 2 · 3 이 SetAction 바탕 6 · 9 · 12(동작 2 · 3 · 4). 칼공격 1 은 동작 2 · 3 중 아무것(<c>0x10044551</c>),
    /// 빔라이플 10 · 포격 362 는 3, 용병파 280 은 20, 나머지 맨 갈래(2 · 3 · 4 · 5)는 2.
    /// </summary>
    private int BasicAction(int id) => id switch
    {
        1 => Rand(2) == 0 ? 2 : 3,
        2 or 3 or 4 or 5 => 2,
        10 or 362 => 3,
        280 => 20,
        _ => 2 + BasicLevel(id),
    };

    /// <summary>용병 갈래의 레벨(0 · 1 · 2) — 셋씩 묶인 번호(297~320 · 324~338 · 342~344 · 352~357).</summary>
    private static int BasicLevel(int id) => id switch
    {
        >= 297 and <= 320 => (id - 297) % 3,
        >= 324 and <= 338 => (id - 324) % 3,
        >= 342 and <= 344 => id - 342,
        >= 352 and <= 357 => (id - 352) % 3,
        _ => 0,
    };

    /// <summary>
    /// 기본 공격 하나를 처음부터 끝까지 — 시각표를 깔고 대상마다 맞는 때에 <paramref name="hit"/> 를 한 번 부른다.
    /// <paramref name="cells"/> 는 범위 칸(포격이 칸마다 터진다), <paramref name="aim"/> 은 겨눈 칸.
    /// </summary>
    internal IEnumerable<bool> BasicRoutine(int attackId, UnitState user, List<int> targets, List<(int Col, int Row)> cells, (int Col, int Row) aim, Action<int> hit)
    {
        host.PreloadSounds([.. BasicSounds.Select(P)]);
        int[] need = [.. BasicNeeds(attackId).Select(P), .. user.Data?.SpriteId is > Part1Game.ObsBase and var own ? [(int)own] : Array.Empty<int>()];
        bool Ready(int id) => AssetPack.Fetch("moses/obs", $"{id:D4}.obs", 0) != null;
        for (double until = host._lastTime + 40; need.Any(id => !Ready(id)) && host._lastTime < until;)
        {
            host.Toast($"파트1 그림을 받는 중입니다… ({need.Count(Ready)}/{need.Length})");
            for (double poll = host._lastTime + 0.5; host._lastTime < poll;) yield return true;
        }
        _user = user;
        _home = host.Btl.UnitFoot(user);
        _events.Clear();
        _shifts.Clear();
        var (ax, ay) = CellFoot(aim.Col, aim.Row);
        _t0 = host._lastTime;
        int last = BasicKind(attackId) switch
        {
            BasicStyle.Melee => BasicMelee(attackId, targets, (ax, ay), hit),
            BasicStyle.Arrow => BasicArrow(targets, (ax, ay), hit),
            BasicStyle.Shot => BasicShot(attackId, targets, (ax, ay), hit),
            BasicStyle.Beam => BasicBeam(attackId, targets, (ax, ay), hit),
            BasicStyle.Missile => BasicMissile(attackId, targets, (ax, ay), hit),
            BasicStyle.Boomerang => BasicBoomerang(attackId, targets, (ax, ay), hit),
            BasicStyle.Throw => BasicThrow(attackId, targets, (ax, ay), hit),
            BasicStyle.Cannon => BasicCannon(targets, cells, (ax, ay), hit),
            _ => 0,
        };
        double finish = At(last);
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 basic {attackId} ({BasicKind(attackId)}): caster {user.ChrCode}, targets {targets.Count}, fx {_fx.Count}, events {_events.Count}, took {last} ticks" + Environment.NewLine);
        _events.Sort((a, b) => a.At.CompareTo(b.At));
        int next = 0;
        while (host._lastTime < finish || next < _events.Count)
        {
            while (next < _events.Count && _events[next].At <= host._lastTime) _events[next++].Run();
            yield return true;
        }
        user.PlayAction(ObsMotionTable.ActionStand, 0);
    }

    /// <summary>기본 공격이 쓰는 파트1 소리 — CSound 와 탄 · 맞은 자리 그림의 소리 키.</summary>
    private static readonly int[] BasicSounds = [2037, 2038, 6, 57, 58, 81, 56, 71, 80, 2, 8, 64, 62, 63];

    /// <summary>그 기본 공격이 쓰는 파트1 그림 — 파트1 이 없는 PC 에서 쓰기 전에 받아 둔다(시전자 그림은 따로).</summary>
    private static int[] BasicNeeds(int id) => BasicKind(id) switch
    {
        BasicStyle.Arrow => [56, 793, 183],
        BasicStyle.Shot => id is >= 342 and <= 344 ? [10] : [183],
        BasicStyle.Beam => [505],
        BasicStyle.Missile => id switch { <= 326 => [55, 198, 48], <= 329 => [56, 275, 51], _ => [56, 276, 71] },
        BasicStyle.Cannon => [123, 10, 48, 34, 38, 366],
        _ => [],
    };

    /// <summary>맞은 자리(<c>0x10039d90</c>) — 대상의 몸 아무 점, 대상이 없으면 겨눈 칸 ±20.</summary>
    private (double X, double Y) BodyPoint(int target, (double X, double Y) aim)
    {
        if (target < 0) return (aim.X + Rand(41) - 20, aim.Y + (Rand(41) - 20) * 0.8);
        var (fx, fy) = host.Btl.UnitFoot(host._units[target]);
        return (fx + Rand(20) - 10, fy - 8 - Rand(24));
    }

    /// <summary>시전자를 겨눈 쪽으로 돌리고 파트1 동작을 한 번 건다 — (걸린 틱, 모션의 첫 타격 키 틱).</summary>
    private (int Length, int Hit) BasicSwing((double X, double Y) toward, int part1Action)
    {
        var u = _user;
        if (toward != _home) u.Facing = FacingOf(toward.X - _home.X, toward.Y - _home.Y);
        int len = Swing(u, part1Action, 8);
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite)) return (len, Math.Max(1, len / 2));
        int action = WeaponAction(part1Action), use = sprite.RawClip(action * 3) != null ? action : 8;
        var hits = sprite.Clip(use, u.Facing)?.Hits;
        return (len, hits is { Count: > 0 } ? hits.Min(h => h.Start) : Math.Max(1, len / 2));
    }

    /// <summary>
    /// 칼질 갈래 — 칼공격 <c>0x10044500</c> · 창/빔소드 <c>0x100445c0</c> · 용병 칼 <c>0x10061a10</c> · 용병 창 <c>0x10061af0</c> · 용병 빔소드 <c>0x10061bd0</c> ·
    /// 용병 2연 <c>0x10062870</c> · 더블 어택 <c>0x10068ed0</c> · 용병파 <c>0x10055840</c>. 대상 쪽을 보고 동작을 한 번 걸 뿐이다 —
    /// 소리는 그 모션의 소리 키, 피해는 그 모션의 첫 타격 키에서(없으면 동작의 절반).
    /// </summary>
    private int BasicMelee(int id, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var (len, at) = BasicSwing(TargetPoint(targets, aim), BasicAction(id));
        foreach (int ti in targets) HitAt(at, ti, hit);
        return Math.Max(len, at) + 1;
    }

    /// <summary>
    /// 활공격(<c>0x10044640</c>) — 시전자 앞(방향 4: 위 · 왼 · 아래 · 오른으로 20) z+60 에 빛 56:1, 28틱 뒤 화살 793(16방향 — 9 이상은 16−d 를 뒤집어,
    /// 소리 2037)이 빠르기 1 × 1.4 로 T 까지 난다. 닿으면 소리 2038, 다음 틱 T 에 칼금 183(모션 0~2)이 서고 그 첫 틱에 친다. 시전자는 동작 2.
    /// </summary>
    private int BasicArrow(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        var t = BodyPoint(targets.Count > 0 ? targets[0] : -1, aim);
        var (len, _) = BasicSwing(t, 2);
        var (ox, oy) = Dir4(t.X - hx, t.Y - hy) switch { 0 => (0, -20), 1 => (-20, 0), 2 => (0, 20), _ => (20, 0) };
        (double X, double Y) s = (hx + ox, hy + oy * 0.8 - 60 * 0.6);
        Put(56, 1, 0, s.X, s.Y);
        int d = Dir16(s, t);
        var flight = Move(s, t, 1, 1.4);
        Along(793, d < 9 ? d : 16 - d, 28, AsPath(flight.Prepend(s).ToList()), life: flight.Count, mirror: d >= 9, sounds: true);
        Later(28, () => host.Btl.CenterOn(t.X, t.Y));
        int arrive = 28 + flight.Count;
        Sound(2038, arrive);
        Put(183, Rand(3), arrive + 1, t.X, t.Y, sounds: true);
        foreach (int ti in targets) HitAt(arrive + 2, ti, hit);
        return Math.Max(len, arrive + 1 + Length(183, 0)) + 1;
    }

    /// <summary>
    /// 총 갈래 — 단발사격 <c>0x10044b40</c> · 용병 단발 <c>0x10061cb0</c>(20틱) · 용병 2연사 <c>0x10061f00</c>(20 · 29) · 용병 3연사 <c>0x10062210</c>(20 · 29 · 38) ·
    /// 쌍권총 <c>0x10068fb0</c>(30 · 36) · 바즈카 <c>0x100640f0</c>(20, 그림 10). 탄은 그리지 않는다 — 시전자 동작(총구 불 · 총소리는 모션 키)과 함께
    /// 그 틱마다 맞은 자리에 칼금 183(모션 0~2, 바즈카는 폭발 10:0 — 소리 8)이 서고 그 첫 틱에 친다. 첫 발이 서면 카메라가 그리로 간다.
    /// </summary>
    private int BasicShot(int id, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        int[] delays = id switch
        {
            >= 309 and <= 311 => [20, 29],
            >= 312 and <= 314 => [20, 29, 38],
            >= 355 and <= 357 => [30, 36],
            _ => [20],
        };
        bool bazooka = id is >= 342 and <= 344;
        int first = targets.Count > 0 ? targets[0] : -1;
        var (len, _) = BasicSwing(TargetPoint(targets, aim), BasicAction(id));
        int last = len;
        for (int k = 0; k < delays.Length; k++)
        {
            var p = BodyPoint(first, aim);
            if (bazooka) Put(10, 0, delays[k], p.X, p.Y, sounds: true);
            else Put(183, Rand(3), delays[k], p.X, p.Y, sounds: true);
            if (k == 0)
            {
                Later(delays[k] + 1, () => host.Btl.CenterOn(p.X, p.Y));
                foreach (int ti in targets) HitAt(delays[k] + 1, ti, hit);
            }
            last = Math.Max(last, delays[k] + (bazooka ? Length(10, 0) : Length(183, 0)));
        }
        return last + 1;
    }

    /// <summary>
    /// 빔라이플(<c>0x100457f0</c>) · 용병 빔라이플(<c>0x10062530</c>) — 시전자 z+60 에서 보는 쪽(방향 4)으로 판 끝까지 빛살 505(위 6 · 아래 7 · 왼 8 · 오른 9)가
    /// 15틱 뒤 빠르기 40 으로 내달리며, 지나는 칸의 적을 그 칸에 들어설 때 친다(<c>0x100769b0</c>). 시전자는 동작 3(용병은 레벨로 2 · 3 · 4).
    /// </summary>
    private int BasicBeam(int id, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        var (len, _) = BasicSwing(t, BasicAction(id));
        int dir = Dir4(t.X - hx, t.Y - hy), col = Math.Clamp((int)(hx / TileW), 0, Math.Max(0, host.Cols - 1));
        (double X, double Y) s = (hx, hy - 60 * 0.6);
        (double X, double Y) e = dir switch
        {
            0 => (hx, host.CellCenterY(col, 0) - 60 * 0.6),
            1 => (TileW / 2, s.Y),
            2 => (hx, host.CellCenterY(col, Math.Max(0, host.Rows - 1)) - 60 * 0.6),
            _ => (host.Cols * TileW - TileW / 2, s.Y),
        };
        int motion = dir switch { 0 => 6, 2 => 7, 1 => 8, _ => 9 };
        var flight = Move(s, e, 40, 1.0);
        Along(505, motion, 15, AsPath(flight.Prepend(s).ToList()), life: flight.Count + 1);
        Later(15, () => host.Btl.CenterOn(t.X, t.Y));
        foreach (int ti in targets)
        {
            var (ux, uy) = host.Btl.UnitFoot(host._units[ti]);
            int best = 0;
            for (int k = 1; k < flight.Count; k++)
                if (Dist(flight[k], (ux, uy - 36)) < Dist(flight[best], (ux, uy - 36))) best = k;
            HitAt(15 + best + 1, ti, hit);
        }
        return Math.Max(len, 15 + flight.Count + 1) + 1;
    }

    /// <summary>
    /// 용병 마법 셋 — 화이어 애로우 <c>0x10062a30</c>(소리 6 · 빛 55:1 · 불덩이 198 · 터짐 48:0, 24틱) · 아이스미사일 <c>0x10062dd0</c>(소리 81 · 빛 56:1 ·
    /// 얼음살 275 16방향 · 51:0, 71틱) · 라이트닝볼츠 <c>0x100631e0</c>(소리 80 · 빛 56:1 · 번개알 276 · 71:0, 31틱 — 록슬리의 기본 공격).
    /// 시전자 z+120 에 빛이 한 번 서고, 20~34틱 뒤 탄이 빠르기 1 × 1.3 으로 T 까지 난다. 닿은 다음 틱 T 에 터짐이 서고 그 첫 틱에 친다.
    /// </summary>
    private int BasicMissile(int id, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        var (sound, glow, shot, burst, life) = id switch
        {
            <= 326 => (6, 55, 198, 48, 24),
            <= 329 => (81, 56, 275, 51, 71),
            _ => (80, 56, 276, 71, 31),
        };
        var t = BodyPoint(targets.Count > 0 ? targets[0] : -1, aim);
        var (len, _) = BasicSwing(t, BasicAction(id));
        (double X, double Y) s = (hx, hy - 120 * 0.6);
        Sound(sound, 0);
        Put(glow, 1, 0, s.X, s.Y);
        int start = 20 + Rand(15);
        var flight = Move(s, t, 1, 1.3);
        Along(shot, shot == 275 ? Dir16(s, t) : 0, start, AsPath(flight.Prepend(s).ToList(), 2), life: flight.Count, sounds: shot != 276);   // 276 의 소리 키는 0(없음)
        Later(start, () => host.Btl.CenterOn(t.X, t.Y));
        int arrive = start + flight.Count;
        Put(burst, 0, arrive + 1, t.X, t.Y, life: life, sounds: true);
        foreach (int ti in targets) HitAt(arrive + 2, ti, hit);
        return Math.Max(len, arrive + 1 + life) + 1;
    }

    /// <summary>
    /// 용병 부메랑(<c>0x10063580</c>, 표의 자리 <c>0x10043209</c>) — 시전자가 동작 2 · 3 · 4 를 한 번 하고 동작 22 로 붙든 사이, 자기 그림의 동작 21 모션이
    /// z+60 에서 T 로 빠르기 20 으로 날아가 닿을 때(사라질 때) 친다(소리 2038). 곧 T 에서 다시 시전자에게 돌아오고, 돌아오면 시전자가 동작 23 을 한 번.
    /// </summary>
    private int BasicBoomerang(int id, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = BodyPoint(targets.Count > 0 ? targets[0] : -1, aim);
        var (len, _) = BasicSwing(t, BasicAction(id));
        int dir = Dir4(t.X - hx, t.Y - hy);
        (double X, double Y) s = (hx, hy - 60 * 0.6);
        var (gm, gmirror) = BodyMotion(21, dir);
        int body = user.Data?.SpriteId ?? 0;
        bool drawn = body > 0 && host.UiFor(body) != null;
        int go = len + 1;
        var outward = Move(s, t, 20, 1.0);
        var back = Move(t, s, 20, 1.0);
        if (drawn)
        {
            Along(body, gm, go, AsPath(outward.Prepend(s).ToList()), life: outward.Count, mirror: gmirror, rawObs: true);
            Along(body, gm, go + outward.Count + 1, AsPath(back.Prepend(t).ToList()), life: back.Count, mirror: gmirror, rawObs: true);
        }
        Later(go, () => { Act(user, 22, ObsMotionTable.ActionStand, repeat: 0); host.Btl.CenterOn(t.X, t.Y); });
        int arrive = go + outward.Count;
        Sound(2038, arrive);
        foreach (int ti in targets) HitAt(arrive, ti, hit);
        int home = arrive + 1 + back.Count;
        Later(home + 1, () => { host.Btl.CenterOnUnit(user); Swing(user, 23, ObsMotionTable.ActionStand); });
        return home + 1 + ActionTicks(user, 23, ObsMotionTable.ActionStand) + 1;
    }

    /// <summary>
    /// 용병 던지기(<c>0x10063a40</c>) — 시전자가 동작 2 · 3 · 4 를 하고 15틱 뒤 자기 그림의 동작 21 모션(창 · 단검)이 z+60 에서 T 로 빠르기 20 × 1.3 으로 날아가
    /// 닿을 때 친다(소리 2038). 닿은 자리에 꽂힌 채 두 틱마다 한 단계씩 바래며 16틱 남는다.
    /// </summary>
    private int BasicThrow(int id, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = BodyPoint(targets.Count > 0 ? targets[0] : -1, aim);
        var (len, _) = BasicSwing(t, BasicAction(id));
        int dir = Dir4(t.X - hx, t.Y - hy);
        (double X, double Y) s = (hx, hy - 60 * 0.6);
        var (gm, gmirror) = BodyMotion(21, dir);
        int body = user.Data?.SpriteId ?? 0;
        var flight = Move(s, t, 20, 1.3);
        if (body > 0 && host.UiFor(body) != null)
        {
            Along(body, gm, 15, AsPath(flight.Prepend(s).ToList()), life: flight.Count, mirror: gmirror, rawObs: true);
            Along(body, gm, 15 + flight.Count + 1, [(0, t.X, t.Y)], life: 16, mirror: gmirror, fadeFrom: 1, fadeTo: 0.1, rawObs: true);
        }
        Later(15, () => host.Btl.CenterOn(t.X, t.Y));
        int arrive = 15 + flight.Count;
        Sound(2038, arrive);
        foreach (int ti in targets) HitAt(arrive, ti, hit);
        return Math.Max(len, arrive + 17) + 1;
    }

    /// <summary>
    /// 포격(<c>0x100692d0</c>, 고르곤 · 자주포) — 시전자 동작 3, 28틱에 T 에 불꽃 123:2(카메라가 그리로). 범위 칸마다 28~32틱에 폭발 10:0 또는 48:0 이 서고
    /// 그 첫 틱에 그 칸의 대상을 친다. 칸마다 연기 34:0 다섯(솟음, 33~42틱) · 불티 38:0 다섯 · 파편 366(모션 0~3) 열이 28~37틱에 흩어진다.
    /// </summary>
    private int BasicCannon(List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var t = TargetPoint(targets, aim);
        var (len, _) = BasicSwing(t, 3);
        Put(123, 2, 28, t.X, t.Y, sounds: true);
        Later(29, () => host.Btl.CenterOn(t.X, t.Y));
        var spots = cells.Select(c => (-1, CellFoot(c.Col, c.Row))).ToList();
        foreach (int ti in targets)
        {
            var (fx, fy) = host.Btl.UnitFoot(host._units[ti]);
            spots.RemoveAll(c => c.Item1 < 0 && Math.Abs(c.Item2.X - fx) < 20 && Math.Abs(c.Item2.Y - fy) < 16);
            spots.Add((ti, (fx, fy)));
        }
        if (spots.Count == 0) spots.Add((-1, t));
        int last = Math.Max(len, 28 + Length(123, 2));
        foreach (var (ti, (x, y)) in spots)
        {
            int at = 28 + Rand(5), boom = Rand(2) == 0 ? 10 : 48;
            Put(boom, 0, at, x, y, sounds: true);
            if (ti >= 0) HitAt(at + 1, ti, hit);
            for (int n = 0; n < 5; n++) Rise(34, 0, 33 + Rand(10), x + Rand(41) - 20, y + (Rand(41) - 20) * 0.8, 3, 1.1);
            for (int n = 0; n < 5; n++) Debris(38, 0, 28 + Rand(10), x, y, 5, 0.9);
            for (int n = 0; n < 10; n++) Debris(366, Rand(4), 28 + Rand(10), x, y, 20, 0.8);
            last = Math.Max(last, at + Math.Max(Length(boom, 0), 45));
        }
        return last + 1;
    }
}

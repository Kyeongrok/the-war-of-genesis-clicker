using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 큰 기계 · 활 · 소환수 기술 — 다탄두 미사일 285 · 확산 레이져 283 · 버스터 포 282 · 버스터 캐논 393 · 버스터 런쳐 397 · 롱 레이져 398 ·
/// 천노탄 403~405 · 다크 라이트 406 · 네크로 271 · 가베라의 저주 407, 그리고 기술로 붙인 용병파 280 · 부메랑 333~335 · 던지기 336~338(기본 공격 틀로 보낸다).
/// 볼트 분석-파트1-기술-b 12절.
/// </summary>
/// <remarks>
/// <para>원본 핸들러: 285 <c>0x100589e0</c> · 283 <c>0x100581a0</c> · 282 <c>0x10056140</c>(단계 +0x20e) · 393 <c>0x1006f5a0</c> · 397 <c>0x10071690</c>(단계) ·
/// 398 <c>0x10072e60</c> · 403~405 <c>0x10073770</c> · 406 <c>0x10073e40</c>(단계) · 271/407 <c>0x10052b20(Obs)</c>. 앞머리(+0x69)는 천노탄 2, 네크로 3, 가베라 4, 나머지 0.</para>
/// <para>버스터 포 · 다크 라이트 · 버스터 런쳐 · 버스터 캐논은 한 틀이다 — 겨눈 쪽 앞의 점 F 로 빛알이 모이고(화면 이동기), F 에서 바깥으로 고리 520 · 519 · 518 이 퍼지고,
/// 큰 빛줄기(527 · 896 · 509, 모션 0~2 나타남 → 3~5 붙듦 30틱 → 6~8 끝, 이어지는 토막 9 · 11 · 13 / 10 · 12 · 14)가 F 너머 화면 끝까지 30px 토막으로 깔린다.
/// 피해는 보이지 않는 이동기(<c>0x1007f6e0</c>)가 시전자에서 판 끝까지 빠르기 40 으로 달리며 칸이 바뀔 때 적이 선 칸마다 친다(<c>0x100769b0</c>) —
/// 그 칸에 666:0 · 526:0 · 667:0 · 518~520 · 파편 35 스물(Attack 번호에 따라 앞부분을 건너뛴다, <c>0x1007f790</c>).</para>
/// <para>원본은 화면 그림을 640×480 화면 픽셀로 시전자 화면 자리에서 떨어뜨려 놓는다 — 여기서는 같은 픽셀만큼 판 위 시전자 발에서 떨어뜨린다(판 픽셀 y 가 이미 화면 y).
/// 빛줄기 토막은 보이는 판 끝까지. 네크로 · 가베라는 대상마다 네 쪽에서 한 번씩 친다(<see cref="BigHitsEachTime"/>).</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    /// <summary>이 파일이 그리는 파트1 Attack 번호인가.</summary>
    internal static bool IsBigSkill(int attackId) => attackId is 285 or 283 or 282 or 393 or 397 or 398 or (>= 403 and <= 406)
        or 271 or 407 or 280 or (>= 333 and <= 338);

    /// <summary>원본이 대상마다 여러 번 맞히는 기술(네크로 · 가베라의 저주 — 네 쪽에서 한 번씩, Attack +0x66 = 1).</summary>
    internal static bool BigHitsEachTime(int work) => IsBigWork(work) && Part1SkillBook.AttackIdOf(work) is 271 or 407;

    /// <summary>기술 하나 — Attack 번호로 나눠 시각표를 깔고 끝나는 틱을 돌려준다.</summary>
    private int Big(int attackId, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit) => attackId switch
    {
        280 => BasicMelee(attackId, targets, aim, hit),
        >= 333 and <= 335 => BasicBoomerang(attackId, targets, aim, hit),
        >= 336 and <= 338 => BasicThrow(attackId, targets, aim, hit),
        271 => Necro(32, targets, hit),
        407 => Necro(248, targets, hit),
        282 or 406 or 397 => Buster(attackId, targets, cells, aim, hit),
        393 => BusterCannon(targets, aim, hit),
        398 => LongLaser(targets, aim, hit),
        >= 403 and <= 405 => Cheonno(attackId, targets, aim, hit),
        283 => SpreadLaser(targets, aim, hit),
        285 => MultiMissile(targets, cells, aim, hit),
        _ => 0,
    };

    /// <summary>앞머리 — 천노탄은 검 기술 종류 2(<see cref="WeaponPrep"/>), 네크로 3 · 가베라 4 · 나머지 0 은 <see cref="MagicPrep"/> 이 Attack.dat 대로.</summary>
    private int BigPrep(int attackId, UnitState user, (double X, double Y) aim) =>
        attackId is >= 403 and <= 405 ? WeaponPrep(user, aim) : MagicPrep(attackId, user, aim);

    /// <summary>그 기술이 쓰는 파트1 그림(앞머리 · 자식 그림까지).</summary>
    private static int[] BigNeeds(int attackId)
    {
        int[] tile = [666, 526, 667, 518, 519, 520, 35];
        int[] own = attackId switch
        {
            280 => [],
            >= 333 and <= 338 => [],
            271 => [55, 49, 32, 796],
            407 => [55, 49, 91, 50, 897, 248, 796],
            282 or 397 => [57, 65, 520, 519, 518, 527, 77, .. tile],
            406 => [54, 64, 520, 519, 518, 896, 77, .. tile],
            393 => [57, 65, 518, 519, 520, 509, .. tile],
            398 => [505],
            >= 403 and <= 405 => [184, 55, 672, 793, 183],
            283 => [524, 525, 526, 518, 519, 520, 35, 63, 65],
            285 => [283, 10, 48, 34, 38, 366],
            _ => [],
        };
        return [.. own.Distinct()];
    }

    private static readonly (int X, int Y)[] BigDirs = [(0, -1), (-1, 0), (0, 1), (1, 0)];

    // ---- 네크로 · 가베라의 저주 ----

    /// <summary>
    /// 네크로 271(Obs 32) · 가베라의 저주 407(Obs 248) — <c>0x10052b20(obs)</c>. 대상마다, 네 쪽 k(0 아래 · 1 오른 · 2 위 · 3 왼, 대상 옆 칸이 판 안일 때)에서 7k 틱 늦게:
    /// 그 짐승의 걷는 잔상(동작 1, 대상을 보고)이 대상에서 120 떨어진 데서 80 까지 다가오며 짙어지고(빠르기 10 × 0.9, 4틱마다 한 단계, 28틱),
    /// 이어 그 자리에서 공격 동작(동작 2)을 한 번, 잔상이 끝나고 22틱 뒤 대상 z+40 에 할퀸 자국 796(모션 0~2)이 서며 친다(그 쪽마다 한 번 — 원본은 넷까지).
    /// 공격이 끝나면 선 잔상(동작 0)이 5틱마다 한 단계 바래며(35틱) 사라진다. 첫 대상만 소리 2072(7k 틱) · 2073(공격이 끝날 때). 카메라는 첫 대상에.
    /// </summary>
    private int Necro(int obs, List<int> targets, Action<int> hit)
    {
        int last = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            var u = host._units[targets[i]];
            var (tx, ty) = ((double, double))host.Btl.UnitFoot(u);
            if (i == 0) Later(1, () => host.Btl.CenterOn(tx, ty));
            for (int k = 0; k < 4; k++)
            {
                var (sx, sy) = k switch { 0 => (0, 1), 1 => (1, 0), 2 => (0, -1), _ => (-1, 0) };
                if (u.Col + sx < 0 || u.Col + sx >= host.Cols || u.Row + sy < 0 || u.Row + sy >= host.Rows) continue;
                int d = 7 * k;
                (double X, double Y) near = (tx + 80 * sx, ty + 80 * sy * 0.8), far = (tx + 120 * sx, ty + 120 * sy * 0.8);
                if (i == 0) Sound(2072, d);
                var (walk, wm) = BodyMotion(1, k);
                var glide = Move(far, near, 10, 0.9);
                while (glide.Count < 28) glide.Add(near);
                Along(obs, walk, d, AsPath(glide.Prepend(far).ToList()), life: 28, mirror: wm, fadeFrom: 0.125, fadeTo: 1);
                var (strike, sm) = BodyMotion(2, k);
                int s = d + 29, len = Length(obs, strike);
                Put(obs, strike, s, near.X, near.Y, mirror: sm, sounds: true);
                int mark = d + 28 + 22;
                Put(796, Rand(3), mark, tx, ty - 40 * 0.6);
                HitAt(mark + 1, targets[i], hit);
                if (i == 0) Sound(2073, s + len);
                var (stand, stm) = BodyMotion(0, k);
                Along(obs, stand, s + len + 1, [(0, near.X, near.Y)], life: 35, mirror: stm, fadeFrom: 1, fadeTo: 0.125);
                last = Math.Max(last, Math.Max(s + len + 36, mark + Length(796, 0)));
            }
        }
        return last + 1;
    }

    // ---- 버스터 포 · 다크 라이트 · 버스터 런쳐 ----

    /// <summary>
    /// 버스터 포 282 · 다크 라이트 406 · 버스터 런쳐 397 — 단계 핸들러(+0x20e). 런쳐는 먼저 시전자를 둘레 3×3 칸 중 가장 높은 땅 +20 까지 들어 올린다(빠르기 10 × 0.95, 걷는 동작).
    /// 카메라를 시전자 앞으로 옮기고 한꺼번에: 소리 1844(30틱), 빛알 백 개(282 · 397 은 57 · 65, 406 은 54 · 64 의 모션 2)가 F ± 150 에서 F 로 모인다
    /// (빠르기 1 × 1.1/1.3/1.5, 처음 스물은 30~39틱, 나머지는 40 + i/5). 고리 520 · 519 · 518 이 F 에서 바깥으로 40 씩 85 · 90 · 95틱(아무렇게나 뒤집힘).
    /// 소리 1843(90틱), 빛줄기(282 · 397 은 527, 406 은 896) — 세 줄(런쳐는 한 줄)이 90틱에 나타나고 30틱 붙들고 끝 모션 동안 가장자리 두 줄이 서로 자리를 바꾼다.
    /// 190틱에 판 끝까지 달리는 이동기 셋(시전자 줄과 옆 ±40 — 런쳐는 하나)이 적 칸마다 친다. 282 · 406 은 범위 칸 일곱에 하나꼴로 140틱에 불 77(100틱).
    /// 시전자는 파트1 동작 42 를 한 번. 190틱을 더 기다린 뒤 카메라를 시전자에 돌리고 끝난다.
    /// </summary>
    private int Buster(int attackId, List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        var (vx, vy) = BigDirs[dir];
        bool dark = attackId == 406, launcher = attackId == 397;
        int start = 0;
        if (launcher)
        {
            Act(user, 1, 1, repeat: 0);
            var up = Move(_home, (hx, hy - 20 * 0.6), 10, 0.95);
            start = up.Count + 2;
            var held = new List<(double X, double Y)>(up);
            while (held.Count < start + 400) held.Add((hx, hy - 20 * 0.6));
            Carry(0, held);
        }
        (int X, int Y)[] focus = dark ? [(0, -202), (-30, -150), (0, -138), (30, -150)] : [(-33, -157), (-150, -50), (33, 27), (150, -50)];
        (double X, double Y) f = (hx + focus[dir].X, hy + focus[dir].Y);
        Later(start, () => host.Btl.CenterOn(f.X, f.Y));
        Later(start, () => Swing(user, 42, 8));
        Sound(1844, start + 30);
        int[] sparks = dark ? [54, 64] : [57, 65];
        int last = start;
        for (int i = 0; i < 100; i++)
        {
            (double X, double Y) from = (f.X + (Rand(2) == 0 ? -1 : 1) * Rand(150), f.Y + (Rand(2) == 0 ? -1 : 1) * Rand(150));
            var path = Move(from, f, 1, new[] { 1.1, 1.3, 1.5 }[Rand(3)]);
            int d = start + (i < 20 ? 30 + Rand(10) : 40 + i / 5);
            Along(sparks[Rand(2)], 2, d, AsPath(path.Prepend(from).ToList(), 2), life: path.Count);
            last = Math.Max(last, d + path.Count);
        }
        for (int i = 0; i < 3; i++)
            Put(520 - i, 0, start + 85 + 5 * i, f.X + vx * (40 * i - 20), f.Y + vy * (40 * i - 20), mirror: Rand(2) == 0);
        Sound(1843, start + 90);
        // 빛줄기 — 앞끝이 F 쯤에 오도록 F 에서 138px 바깥이 첫 토막의 자리(원본 282 의 화면 수치 −295 · +163 · ∓288 과 F 의 차이).
        (double X, double Y) anchor = (f.X + vx * 138, f.Y + vy * 138);
        last = Math.Max(last, BigBeam(dark ? 896 : 527, start + 90, anchor, dir, launcher ? 1 : 3, 10, 30, swap: true));
        // 맞히기 — 190틱에 판 끝까지(줄은 시전자와 옆 ±40 월드).
        int variant = launcher ? 2 : 0;
        last = Math.Max(last, BigSweep(targets, dir, start + 190, variant, hit));
        if (!launcher)
        {
            var area = GunCells(targets, cells, aim);
            foreach (var cell in area)
            {
                if (Rand(7) != 0) continue;
                var (cx, cy) = CellFoot(cell.Col, cell.Row);
                Put(77, Rand(3), start + 140, cx, cy, life: 100);
            }
            last = Math.Max(last, start + 240);
        }
        int end = Math.Max(last, start + 190 + 2);
        Later(end, () => host.Btl.CenterOnUnit(user));
        if (launcher) Later(end - 2, () => user.PlayAction(ObsMotionTable.ActionStand, 0));
        return end + 1;
    }

    /// <summary>
    /// 큰 빛줄기 — <paramref name="cols"/> 줄(겨눈 쪽에 가로로 <paramref name="gap"/>px 간격)이 <paramref name="delay"/> 틱에 나타나고(모션 0 위 · 1 옆 · 2 아래, 오른쪽은 1 뒤집어)
    /// <paramref name="hold"/> 틱 붙들고(3~5) 끝 모션(6~8) 한 번. 첫 토막 너머 보이는 판 끝까지 30px 마다 이어지는 토막(위 · 아래 9/11/13, 옆 10/12/14)이 같은 때에.
    /// <paramref name="swap"/> 이면 끝 모션 동안 가장자리 두 줄이 서로 자리를 바꾼다(빠르기 1). 끝나는 틱.
    /// </summary>
    private int BigBeam(int obs, int delay, (double X, double Y) anchor, int dir, int cols, int gap, int hold, bool swap)
    {
        var (vx, vy) = BigDirs[dir];
        bool side = dir is 1 or 3, mirror = dir == 3;
        int m0 = dir switch { 0 => 0, 2 => 2, _ => 1 }, e0 = side ? 10 : 9;
        int a = delay, b = a + Length(obs, m0) + 1, c = b + hold + 1, end = c + Length(obs, m0 + 6);
        var (l, top, r, bottom) = View;
        for (double k = 0; k < 60; k++)
        {
            double ax = anchor.X + vx * 30 * k, ay = anchor.Y + vy * 30 * k;
            if (k > 0 && (ax < l - 30 || ax > r + 30 || ay < top - 30 || ay > bottom + 30)) break;
            int m = k == 0 ? m0 : e0;
            int[] seq = k == 0 ? [m, m + 3, m + 6] : [m, m + 2, m + 4];
            for (int i = 0; i < cols; i++)
            {
                double off = cols == 1 ? 0 : (i - 1) * gap;
                (double X, double Y) p = side ? (ax, ay + off) : (ax + off, ay);
                Put(obs, seq[0], a, p.X, p.Y, mirror: mirror);
                Put(obs, seq[1], b, p.X, p.Y, life: hold, mirror: mirror);
                if (swap && cols > 1 && i != 1)
                {
                    (double X, double Y) q = side ? (ax, ay - off) : (ax - off, ay);
                    var path = Move(p, q, 1, 1.0);
                    while (path.Count < Length(obs, seq[2])) path.Add(q);
                    Along(obs, seq[2], c, AsPath(path.Prepend(p).ToList(), 2), life: Length(obs, seq[2]), mirror: mirror);
                }
                else Put(obs, seq[2], c, p.X, p.Y, mirror: mirror);
            }
        }
        return end;
    }

    /// <summary>
    /// 판 끝까지 달리는 맞히기 이동기(<c>0x1007f6e0</c>, 빠르기 40) — <paramref name="delay"/> 틱에 떠나 대상마다 그 칸에 들어설 때 친다.
    /// 칸 그림: 종류 0(282 · 406) 666:0 · 526:0 → 종류 1(393) 667:0(y+20 · z+20) → 종류 2(397) 526:0 · 518~520 둘(하나는 뒤집어) · 파편 35 스물(빠르기 1~3 × 1.1). 마지막 틱.
    /// </summary>
    private int BigSweep(List<int> targets, int dir, int delay, int variant, Action<int> hit)
    {
        var (hx, hy) = _home;
        int last = delay;
        foreach (int ti in targets)
        {
            var (x, y) = ((double, double))host.Btl.UnitFoot(host._units[ti]);
            double far = dir is 0 or 2 ? Math.Abs(y - hy) / 0.8 : Math.Abs(x - hx);
            int at = delay + 2 + (int)Math.Ceiling(far / 40);
            (double X, double Y) body = (x, y - 24);
            if (variant == 0)
            {
                Put(666, 0, at, body.X, body.Y);
                Put(526, 0, at, body.X, body.Y, sounds: true);
            }
            if (variant <= 1) Put(667, 0, at, body.X, body.Y + 20 * 0.8 - 20 * 0.6, sounds: true);
            Put(526, 0, at, body.X, body.Y, sounds: variant == 2);
            Put(518 + Rand(3), 0, at, body.X, body.Y);
            Put(518 + Rand(3), 0, at, body.X, body.Y, mirror: true);
            for (int n = 0; n < 20; n++) Debris(35, 0, at, body.X, body.Y, Rand(3) + 1, 1.1);
            HitAt(at, ti, hit);
            Later(at, () => host.Btl.CenterOn(x, y));
            last = Math.Max(last, at + 64);
        }
        return last;
    }

    /// <summary>
    /// 버스터 캐논 393(<c>0x1006f5a0</c>) — 소리 1894, 빛알 쉰(57 · 65 모션 0, 판 위)이 시전자 ± 100 에서 F(위 (0, −120) · 왼 (−100, −30) · 아래 (0, +60) · 오른 (+100, −30))로
    /// 모이고(빠르기 1 × 1.2/1.4/1.6), 고리 518~520 둘(20틱 간격), 소리 1893(37틱). 빛줄기 509 한 줄이 37틱에 시전자 앞에서 나타나 판 끝까지 30px 토막으로 깔리고(30틱 붙듦),
    /// 107틱에 판 끝까지 달리는 이동기 하나가 적 칸마다 친다(칸 그림 종류 1). 시전자는 파트1 동작 42.
    /// </summary>
    private int BusterCannon(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        int last = Swing(user, 42, 8);
        Sound(1894, 0);
        (int X, int Y)[] focus = [(0, -120), (-100, -30), (0, 60), (100, -30)];
        (double X, double Y) f = (hx + focus[dir].X, hy + focus[dir].Y * 0.8);
        for (int i = 0; i < 50; i++)
        {
            (double X, double Y) from = (hx + (Rand(2) == 0 ? -1 : 1) * Rand(100), hy + (Rand(2) == 0 ? -1 : 1) * Rand(100) * 0.8 - 40);
            var path = Move(from, f, 1, new[] { 1.2, 1.4, 1.6 }[Rand(3)]);
            int d = i < 10 ? Rand(5) : i * 3 / 4;
            Along(Rand(2) == 0 ? 57 : 65, 0, d, AsPath(path.Prepend(from).ToList(), 2), life: path.Count);
            last = Math.Max(last, d + path.Count);
        }
        for (int i = 0; i < 2; i++) Put(518 + Rand(3), 0, 20 * i, f.X, f.Y, mirror: Rand(2) == 0);
        Sound(1893, 37);
        var (vx, vy) = BigDirs[dir];
        last = Math.Max(last, BigBeam(509, 37, (hx + vx * 30, hy + vy * 30 - 36), dir, 1, 0, 30, swap: false));
        Later(0, () => host.Btl.CenterOn(f.X, f.Y));
        last = Math.Max(last, BigSweep(targets, dir, 107, 1, hit));
        return last + 1;
    }

    // ---- 롱 레이져 ----

    /// <summary>
    /// 롱 레이져 398(<c>0x10072e60</c>) — 소리 1914. 시전자를 둘레 3×3 칸의 가장 높은 땅 +20 까지 들어 올리고(운반자, 빠르기 10, 걷는 동작), 닿으면 소리 1895,
    /// 15틱 뒤 그 높이에서 빛살 505(위 6 · 아래 7 · 왼 8 · 오른 9)가 보는 쪽 판 끝까지 빠르기 40 으로 난다 — 들어서는 칸마다 적을 친다. 빛살이 끝나면 시전자를 내린다.
    /// </summary>
    private int LongLaser(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        Sound(1914, 0);
        Act(user, 1, 1, repeat: 0);
        double lift = 20 * 0.6;
        var up = Move(_home, (hx, hy - lift), 10, 1.0);
        int arrive = up.Count;
        Sound(1895, arrive);
        int s = arrive + 17;
        (double X, double Y) from = (hx, hy - lift);
        var (ux, uy) = BigDirs[dir];
        var (l, top, r, b) = View;
        double reach = dir switch { 0 => from.Y - top, 1 => from.X - l, 2 => b - from.Y, _ => r - from.X } + 40;
        (double X, double Y) to = (from.X + ux * Math.Max(40, reach), from.Y + uy * Math.Max(40, reach));
        var flight = Move(from, to, 40, 1.0);
        int m = dir switch { 0 => 6, 2 => 7, 1 => 8, _ => 9 };
        Along(505, m, s, AsPath(flight.Prepend(from).ToList()), life: flight.Count);
        foreach (int ti in targets)
        {
            var (fx, fy) = ((double, double))host.Btl.UnitFoot(host._units[ti]);
            int best = 0;
            for (int k = 1; k < flight.Count; k++)
                if (Dist(flight[k], (fx, fy - lift)) < Dist(flight[best], (fx, fy - lift))) best = k;
            HitAt(s + best + 1, ti, hit);
        }
        Later(s, () => host.Btl.CenterOn(t.X, t.Y));
        int down = s + flight.Count + 2;
        var back = Move((hx, hy - lift), _home, 10, 1.0);
        var held = new List<(double X, double Y)>(up);
        while (held.Count < down) held.Add((hx, hy - lift));
        held.AddRange(back);
        Carry(0, held);
        int end = down + back.Count + 1;
        Later(end - 1, () => { user.PlayAction(ObsMotionTable.ActionStand, 0); host.Btl.CenterOnUnit(user); });
        return end + 1;
    }

    // ---- 천노탄 ----

    /// <summary>
    /// 천노탄 403~405(<c>0x10073770</c>) — 소리 2053, 시전자 x+40 z+180 에 빛 55:2, 15틱에 x+40 z+40 에 기운 672:1(카메라가 따라간다), 그것이 끝나면 시전자 앞(보는 쪽 20) z+60 에 빛 55:1.
    /// 그것이 끝나고 3i 틱마다 화살 793(16방향, 9 이상은 뒤집어) i = 0 … Attack +0x66 − 1(3 · 4 · 5발)이 아무 적에게나(바로 앞 화살과 다른 적) 빠르기 1 × 1.4 로 난다.
    /// 닿으면 소리 2038, 그 자리에 칼금 183 이 서며 친다. 시전자는 핸들러 때 파트1 동작 2. 여기서는 앞 화살들이 대상을 하나씩 맡고 그 첫 화살에서 친다.
    /// </summary>
    private int Cheonno(int attackId, List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        int last = Swing(user, 2, 8);
        var (ox, oy) = BigDirs[dir];
        Sound(2053, 0);
        Put(55, 2, 0, hx + 40, hy - 180 * 0.6);
        Put(672, 1, 15, hx + 40, hy - 40 * 0.6);
        Later(15, () => host.Btl.CenterOn(hx + 40, hy));
        int glow = 15 + Length(672, 1) + 1;
        (double X, double Y) s = (hx + 20 * ox, hy + 20 * oy * 0.8 - 60 * 0.6);
        Put(55, 1, glow, s.X, s.Y);
        int go = glow + Length(55, 1) + 2;
        int shots = attackId - 400, prev = -1;
        var struck = new HashSet<int>();
        for (int i = 0; i < shots; i++)
        {
            int ti = -1;
            if (targets.Count > 0)
            {
                ti = i < targets.Count ? targets[i] : targets[Rand(targets.Count)];
                if (targets.Count > 1 && i >= targets.Count) while (ti == prev) ti = targets[Rand(targets.Count)];
            }
            prev = ti;
            var p = BodyPoint(ti, aim);
            int d = Dir16(s, p), at = go + 3 * i;
            var flight = Move(s, p, 1, 1.4);
            Along(793, d < 9 ? d : 16 - d, at, AsPath(flight.Prepend(s).ToList()), life: flight.Count, mirror: d >= 9, sounds: true);
            if (i == 1 || shots == 1) Later(at, () => host.Btl.CenterOn(p.X, p.Y));
            int arrive = at + flight.Count;
            Sound(2038, arrive);
            Put(183, Rand(3), arrive + 1, p.X, p.Y, sounds: true);
            if (ti >= 0 && struck.Add(ti)) HitAt(arrive + 2, ti, hit);
            last = Math.Max(last, arrive + 1 + Length(183, 0));
        }
        return last + 1;
    }

    // ---- 확산 레이져 ----

    /// <summary>
    /// 확산 레이져 283(<c>0x100581a0</c>) — 시전자는 파트1 동작 41. 15틱에 수정 524:1 이 시전자 z+100 에서 겨눈 칸(±20, 땅 위 z+100)으로 빠르기 5 × 1.2 로 날고(카메라가 따라간다),
    /// 닿으면 그 자리에 524:0. 다시 15틱 뒤(대상 j 는 j·20/n 틱 더) 빛살 525(16방향)가 거기서 대상마다 빠르기 10 × 1.5 로 꽂히고, 닿으면 526:0 이 서며 친다.
    /// 이어 그 자리에 518~520 둘(하나는 뒤집어), 흔들리며 솟는 연기 35 열(빠르기 3 × 1.01, 5~14틱), 흩어지는 빛 63 · 65 열씩.
    /// </summary>
    private int SpreadLaser(List<int> targets, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        user.Facing = FacingOf4(Dir4(t.X - hx, t.Y - hy));
        int last = Swing(user, 41, 8);
        (double X, double Y) from = (hx, hy - 100 * 0.6), prism = (aim.X + Rand(40) - 20, aim.Y + (Rand(40) - 20) * 0.8 - 100 * 0.6);
        var fly = Move(from, prism, 5, 1.2);
        Along(524, 1, 15, AsPath(fly.Prepend(from).ToList()), life: fly.Count);
        Later(15, () => host.Btl.CenterOn(prism.X, prism.Y + 60));
        int arrive = 15 + fly.Count;
        Put(524, 0, arrive + 1, prism.X, prism.Y);
        last = Math.Max(last, arrive + 1 + Length(524, 0));
        int n = Math.Max(1, targets.Count);
        for (int j = 0; j < targets.Count; j++)
        {
            int ti = targets[j];
            var p = BodyPoint(ti, aim);
            int d = Dir16(prism, p), at = arrive + 2 + 15 + j * (20 / n);
            var beam = Move(prism, p, 10, 1.5);
            Along(525, d < 9 ? d : 16 - d, at, AsPath(beam.Prepend(prism).ToList()), life: beam.Count, mirror: d >= 9);
            int land = at + beam.Count + 1;
            Put(526, 0, land, p.X, p.Y, sounds: true);
            HitAt(land + 1, ti, hit);
            Put(518 + Rand(3), 0, land + 1, p.X, p.Y);
            Put(518 + Rand(3), 0, land + 1, p.X, p.Y, mirror: true);
            for (int k = 0; k < 10; k++) Rise(35, 0, land + 5 + Rand(10), p.X + (Rand(2) == 0 ? -1 : 1) * Rand(20), p.Y + (Rand(2) == 0 ? -1 : 1) * Rand(20) * 0.8, 3, 1.01);
            for (int k = 0; k < 10; k++) Debris(63, 0, land + Rand(10), p.X, p.Y, 25, 0.8);
            for (int k = 0; k < 10; k++) Debris(65, 0, land + Rand(10), p.X, p.Y, 10, 0.95);
            last = Math.Max(last, land + Math.Max(Length(526, 0), 40));
        }
        return last + 1;
    }

    // ---- 다탄두 미사일 ----

    /// <summary>
    /// 다탄두 미사일 285(<c>0x100589e0</c>) — Attack +0x58 = 16 발. i 째는 겨눈 칸 둘레의 아무 칸(적이 서 있으면 그 몸)을 노린다. 시전자 자리(z−20)에서
    /// 미사일 283(16방향)이 보는 쪽 옆으로 5i 벌어지며 떠오르고(빠르기 1 × 1.1), (16 − i)·5 틱 떠 있다가(첫 발이 뜰 때 소리 94) 굽은 길로 과녁에 날아가
    /// 폭발 10 · 48(반반)이 서며 그 칸을 친다. 연기 34 다섯 · 불티 38 다섯 · 파편 366 열. 시전자는 파트1 동작 39. 여기서는 앞 발들이 대상 칸을 하나씩 맡는다.
    /// </summary>
    private int MultiMissile(List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var user = _user;
        var (hx, hy) = _home;
        var t = TargetPoint(targets, aim);
        int dir = Dir4(t.X - hx, t.Y - hy);
        user.Facing = FacingOf4(dir);
        int last = Swing(user, 39, 8);
        var area = GunCells(targets, cells, aim);
        const int N = 16;
        var (px, py) = dir is 0 or 2 ? (1, 0) : (0, 1);
        int first = int.MaxValue;
        var struck = new HashSet<int>();
        for (int i = 0; i < N; i++)
        {
            int ti = i < targets.Count ? targets[i] : -1;
            (double X, double Y) goal;
            if (ti >= 0) goal = BodyPoint(ti, aim);
            else
            {
                var cell = area[Rand(area.Count)];
                var (cx, cy) = CellFoot(cell.Col, cell.Row);
                goal = (cx + Rand(41) - 20, cy + (Rand(41) - 20) * 0.8);
            }
            int side = i % 2 == 0 ? 1 : -1;
            (double X, double Y) s0 = (hx, hy + 20 * 0.6 - 30), s1 = (hx + px * side * 5 * i, hy + py * side * 5 * i * 0.8 - 30 - 20);
            int d = Dir16(s0, goal), m = d < 9 ? d : 16 - d;
            var rise = Move(s0, s1, 1, 1.1);
            Along(283, m, 0, AsPath(rise.Prepend(s0).ToList()), life: rise.Count, mirror: d >= 9);
            int hover = (N - i) * 5, leave = rise.Count + 1 + hover;
            Along(283, m, rise.Count + 1, [(0, s1.X, s1.Y)], life: hover, mirror: d >= 9);
            if (i == 0) Sound(94, rise.Count + 1 + hover);
            var arc = Arc(s1, goal, 1, 1.1, 70, 60);
            Along(283, m, leave, AsPath(arc.Prepend(s1).ToList()), life: arc.Count, mirror: d >= 9);
            int land = leave + arc.Count + 1;
            first = Math.Min(first, leave);
            Put(Rand(2) == 0 ? 10 : 48, 0, land, goal.X, goal.Y, sounds: i % 3 == 0);
            if (ti >= 0 && struck.Add(ti)) HitAt(land + 1, ti, hit);
            for (int n = 0; n < 5; n++) Rise(34, 0, land + 5 + Rand(10), goal.X + (Rand(2) == 0 ? -1 : 1) * Rand(20), goal.Y + (Rand(2) == 0 ? -1 : 1) * Rand(20) * 0.8, 3, 1.1);
            for (int n = 0; n < 5; n++) Debris(38, 0, land + Rand(10), goal.X, goal.Y, 15, 0.9);
            for (int n = 0; n < 10; n++) Debris(366, Rand(4), land + Rand(10), goal.X, goal.Y, 25, 0.8);
            last = Math.Max(last, land + Math.Max(Length(10, 0), 40));
        }
        Later(first, () => host.Btl.CenterOn(t.X, t.Y));
        return last + 1;
    }

    /// <summary>굽은 길(<c>0x1007dfc0</c> 의 어림) — 곧은 이동기 길에 가운데가 <paramref name="bulge"/>px 솟는 포물선을 얹는다.</summary>
    private static List<(double X, double Y)> Arc((double X, double Y) from, (double X, double Y) to, double speed, double factor, double max, double bulge)
    {
        var line = Move(from, to, speed, factor, max);
        var path = new List<(double X, double Y)>(line.Count);
        for (int k = 0; k < line.Count; k++)
        {
            double u = (k + 1.0) / line.Count;
            path.Add((line[k].X, line[k].Y - bulge * 4 * u * (1 - u)));
        }
        return path;
    }
}

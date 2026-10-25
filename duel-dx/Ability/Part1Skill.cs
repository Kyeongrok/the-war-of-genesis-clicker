using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 창세기전3 파트1 의 기술 — 검 기술(블리자드 스탐 · 설화난영참 · 헬 스트라이크 · 아수라파천무), 마법(썬더스탐),
/// 소환(헬카이트 · 어비스 네레이드 · 머스펠 헤임 · 천수관음 · 타이타니아 · 묵호자)(사용자 요청 menu-85 · menu-89).
/// 이 게임에서는 스킬 파일 <c>assets/data/skills/200NN.json</c>(파트1 어빌리티 번호 + 20000)의 어빌리티로 캐릭터 에디터에서 붙여 쓴다.
/// </summary>
/// <remarks>
/// <para>원본 <c>alpha.dll</c> 의 핸들러를 옮겼다(볼트 분석-파트1-검기술 · 분석-파트1-소환). 파트2 와 달리 핸들러가 <b>한 번만</b> 돌며
/// 이펙트를 지연 · 사슬 · 수명으로 다 깔아 두므로, 여기서도 앞머리가 끝나면 시각표를 다 짜 두고 그 때마다 그리고 친다.
/// 이 파일은 그 틀(그림 · 선 · 눈 · 비 · 번쩍임 · 하얗게 · 시전자 옮기기)이고 기술마다의 시각표는 같은 이름의 다른 파일에 있다.</para>
/// <para>단위는 판 픽셀 — 월드 → 화면은 x 그대로 · y × 0.8 · 높이 z × 0.6(<c>0x1008fbb0</c>). 이동기(<c>0x100780f0</c>)는 틱마다 빠르기만큼
/// 다가가고 빠르기에 곱수를 곱한다(최대 40 · 최소 1). 원을 도는 이동기는 반지름 r 둘레를 8r 점으로 걷는다(<c>0x10009b50</c>).
/// 그림·소리 번호는 파트1 번호에 <see cref="Part1Game.ObsBase"/> 를 더해 부른다.</para>
/// <para>화면에 붙는 그림(소환수의 모습 — <c>0x100860b0</c> 갈래)은 640×480 화면의 가운데를 기준으로 그 픽셀만큼 떨어진 자리에 그린다.
/// 넓은 화면에서도 크기는 그대로고 가운데에 모인다. 화면 끝까지 가는 것(빛줄기 · 구름)만 보이는 화면 끝을 쓴다.</para>
/// </remarks>
internal sealed partial class Part1Skill(GameWindow host)
{
    /// <summary>이 틀이 그리는 파트1 기술인가 — 고정 work 들 · 라이트닝볼츠(Attack 번호로 · 생성된 어빌리티 work 포함).</summary>
    internal static bool Handles(int work) => Works.Contains(work) || IsLightning(work) || IsWeaponWork(work) || IsMagicWork(work) || IsSword2Work(work) || IsGunWork(work) || IsBigWork(work);

    /// <summary>총 · 기계 · 용병 · 잡기술(연사 · 저격 · 댓쉬 · 워프 · 도발 · 집중 · 기도 · 희생 · 최면 … , Part1Skill.Gun).</summary>
    internal static bool IsGunWork(int work) => work > Part1Game.ChrBase && !Works.Contains(work) && IsGunSkill(Part1SkillBook.AttackIdOf(work));

    /// <summary>큰 기계 · 활 · 소환수 기술(다탄두 미사일 · 확산 레이져 · 버스터 포/캐논/런쳐 · 롱 레이져 · 천노탄 · 다크 라이트 · 네크로 · 가베라의 저주, Part1Skill.Big).</summary>
    internal static bool IsBigWork(int work) => work > Part1Game.ChrBase && !Works.Contains(work) && IsBigSkill(Part1SkillBook.AttackIdOf(work));

    /// <summary>남은 검 특기(천지파열무 · 풍아열공참 · 지하드 · 새틀라이트 어택 · 마룡광아탄 · 플라즈마 슬래쉬 · 혈랑마혼 · 소드브레이크, Part1Skill.Sword2).</summary>
    internal static bool IsSword2Work(int work) => work > Part1Game.ChrBase && !Works.Contains(work) && IsSword2Skill(Part1SkillBook.AttackIdOf(work));

    /// <summary>마법(블레이드 · 화이어 애로우 ~ 아마게돈, Part1Skill.Magic2) — Attack 번호로.</summary>
    internal static bool IsMagicWork(int work) => work > Part1Game.ChrBase && !Works.Contains(work) && IsMagicSkill(Part1SkillBook.AttackIdOf(work));

    /// <summary>무기 기술(연 · 살 · 비 · 혼 · 파 · 참 · 격 · 광, Part1Skill.Weapon) — Attack 번호로.</summary>
    internal static bool IsWeaponWork(int work) => work > Part1Game.ChrBase && IsWeaponSkill(Part1SkillBook.AttackIdOf(work));

    internal static readonly HashSet<int> Works =
        [BlizzardWork, SeolhwaWork, HellStrikeWork, AsuraWork, StormSwordWork, FlyingSlashWork, ThunderWork, MeteorWork,
         LightningWork, LightningWork + 1, LightningWork + 2, LightningWork + 3,
         HellKiteWork, NereidWork, MuspelWork, KannonWork, TitaniaWork, BlackFoxWork];

    private static int P(int part1) => Part1Game.ObsBase + part1;

    /// <summary>
    /// 그릴 그림 하나 — 시작(초), 길(마디 사이는 곧게), 수명(null 이면 모션 한 번). <paramref name="Screen"/> 이면 길이 화면 가운데 기준이다.
    /// 옅기는 처음 → 끝으로 곧게 바뀐다(섞기 13 단계를 올리고 내리는 그림). <paramref name="Flicker"/> 면 2틱마다 모션 0~2 를 아무렇게나(번개 기둥 0x1007b500).
    /// </summary>
    private sealed record Fx(int Obs, int Motion, double Start, (int Tick, double X, double Y)[] Path, int? Life, bool Mirror,
                             bool Screen = false, double FadeFrom = 1, double FadeTo = 1, bool Flicker = false);

    /// <summary>선 하나(빗줄기 · 빛줄기 · 빛살) — 틱마다 두 끝과 옅기를 준다. null 이면 그 틱에는 안 그린다.</summary>
    private sealed record Line(double Start, int Life, bool Screen, Func<int, (double X0, double Y0, double X1, double Y1, double Alpha)?> At, uint Color, int Width = 2);

    /// <summary>설화난영참의 눈송이 — 점으로 떨어진다(<c>0x10082b00</c>, 색 13).</summary>
    private sealed record Flake(double Start, double X, double Y, double Z, double Speed, int Sway);

    private readonly List<Fx> _fx = [];
    private readonly List<Line> _lines = [];
    private readonly List<Flake> _flakes = [];

    /// <summary>번쩍임(<c>0x10087d70</c>) — 그동안 화면 전체가 밝게 뒤집히고 빗줄기가 하얗다.</summary>
    private readonly List<(double Start, double End)> _flashes = [];

    /// <summary>하얗게 바래기(<c>0x100836f0</c> → <c>0x1008ff10</c>) — 원본은 화면을 멈추고 8장에 걸쳐 하얗게 했다 되돌린다. 여기서는 멈추지 않는다.</summary>
    private readonly List<(double Start, int Frames, uint Color)> _whiteouts = [];

    private double _t0;

    /// <summary>앞머리가 끝날 때 시전자에게 걸 파트1 동작(그림에 없으면 Fallback) — Hold 면 원본 잠금(+0x26 = −1)처럼 기술이 끝날 때까지 붙든다.</summary>
    private (int Part1, int Fallback, bool Hold)? _afterPrep;
    private UnitState _user = null!;

    /// <summary>겨눈 칸(원본 +0x200 · +0x202) — 프로즌 아이스가 덩이 길이의 거리를 이 칸 가운데에서 잰다.</summary>
    private (int Col, int Row) _aimCell;
    private (double X, double Y) _home;
    private readonly List<(double At, Action Run)> _events = [];
    private readonly List<(double Start, List<(double X, double Y)> Points)> _shifts = [];

    private double At(int tick) => _t0 + tick / TicksPerSecond;
    private int Rand(int n) => host.Btl._fxRandom.Next(Math.Max(1, n));
    private int Length(int obs, int motion) => Math.Max(1, host.UiFor(P(obs))?.MotionLength(motion) ?? 1);

    private void Later(int tick, Action run) => _events.Add((At(tick), run));
    private void HitAt(int tick, int target, Action<int> hit) => Later(tick, () => hit(target));

    private void Put(int obs, int motion, int tick, double x, double y, int? life = null, bool mirror = false, bool? sounds = null) =>
        Along(obs, motion, tick, [(0, x, y)], life, mirror, sounds);

    /// <summary>
    /// 그림 하나를 깐다. 원본 이펙트(<c>0x10085e00</c> 과 그 이동기들 — 키 처리가 모두 <c>0x100982d0</c> → <c>0x10098370</c>)는 제 모션의 소리 키(종류 1)를
    /// 늘 스스로 내고(<c>0x1001a330</c>), 자식 키(종류 2)로 붙는 이펙트도 제 소리를 낸다. 그래서 <paramref name="sounds"/> 를 안 주면 소리를 낸다 —
    /// 다만 시전자 그림을 빌린 잔상(<paramref name="rawObs"/>)은 그 동작 소리를 시전자 동작이 이미 내므로 달라고 할 때만 낸다.
    /// </summary>
    private void Along(int obs, int motion, int tick, (int Tick, double X, double Y)[] path, int? life = null, bool mirror = false, bool? sounds = null,
                       bool screen = false, double fadeFrom = 1, double fadeTo = 1, bool flicker = false, bool rawObs = false)
    {
        int id = rawObs ? obs : P(obs);
        _fx.Add(new Fx(id, motion, At(tick), path, life, mirror, screen, fadeFrom, fadeTo, flicker));
        if (sounds ?? !rawObs) KeySounds(id, motion, tick, 0);
    }

    /// <summary>그 모션의 소리 키와 자식 이펙트의 소리 키를 틱에 맞춰 예약한다(자식은 넷 깊이까지).</summary>
    private void KeySounds(int id, int motion, int tick, int depth)
    {
        if (host.UiFor(id)?.Clip(motion) is not { } clip) return;
        foreach (var (t, sound) in clip.Sounds) QueueSound(tick + t, sound);
        if (depth < 4)
            foreach (var (start, obs, child, _, _, _, _) in clip.Children) KeySounds(obs, child, tick + start, depth + 1);
    }

    /// <summary>
    /// 소리 하나를 예약한다 — 원본 <c>0x1001a330</c> 은 같은 소리가 이미 이번 장(+0x30 = 장 번호)에 울렸으면 다시 내지 않는다.
    /// 그래서 같은 틱의 같은 소리는 한 번만 낸다(같은 틱에 터지는 폭발 여럿 · 원을 도는 해골 셋). 파트1 소리 0 은 빈 키다(번개알 276).
    /// </summary>
    private void QueueSound(int tick, int sound)
    {
        long at = (long)Math.Round(At(tick) * TicksPerSecond);
        if (sound <= Part1Game.ObsBase || !_soundTicks.Add((at, sound))) return;
        host._pendingSounds.Add((At(tick), sound));
    }

    private readonly HashSet<(long Tick, int Sound)> _soundTicks = [];

    /// <summary>화면에 붙는 그림 — 화면 가운데에서 (dx, dy) 만큼.</summary>
    private void Screen(int obs, int motion, int tick, double dx, double dy, int? life = null, bool mirror = false, bool? sounds = null, double fadeFrom = 1, double fadeTo = 1) =>
        Along(obs, motion, tick, [(0, dx, dy)], life, mirror, sounds, screen: true, fadeFrom: fadeFrom, fadeTo: fadeTo);

    private void Sound(int part1, int tick) => QueueSound(tick, P(part1));

    /// <summary>곧은 이동기 — 틱마다의 자리(첫 칸 = 출발 다음 틱). 닿은 틱이 마지막 칸이다.</summary>
    private static List<(double X, double Y)> Move((double X, double Y) from, (double X, double Y) to, double speed, double factor, double max = 40, double min = 1, bool add = false)
    {
        var points = new List<(double X, double Y)>();
        var (x, y) = from;
        for (int guard = 0; guard < 600; guard++)
        {
            double dx = to.X - x, dy = to.Y - y, far = Math.Sqrt(dx * dx + dy * dy);
            if (far <= speed) { points.Add(to); break; }
            (x, y) = (x + dx / far * speed, y + dy / far * speed);
            points.Add((x, y));
            speed = Math.Clamp(add ? speed + factor : speed * factor, min, max);
        }
        if (points.Count == 0) points.Add(to);
        return points;
    }

    private static (int Tick, double X, double Y)[] AsPath(List<(double X, double Y)> points, int every = 1)
    {
        var path = new List<(int, double, double)>();
        for (int i = 0; i < points.Count; i += every) path.Add((i, points[i].X, points[i].Y));
        if ((points.Count - 1) % every != 0) path.Add((points.Count - 1, points[^1].X, points[^1].Y));
        return [.. path];
    }

    /// <summary>원을 도는 이동기(<c>0x10080000</c>) — 반지름 r(월드) 둘레 8r 점을 틱당 빠르기만큼 걷는다. 화면 y 는 0.8배(<paramref name="flat"/> 가 아니면).</summary>
    private static (int Tick, double X, double Y)[] Circle(double cx, double cy, double r, int life, double speed, double factor, bool reverse, double start, bool flat = false)
    {
        int points = Math.Max(8, (int)(8 * r));
        var path = new List<(int, double, double)>();
        double walked = 0;
        for (int t = 0; t <= life; t += 2)
        {
            double a = start + (reverse ? -1 : 1) * walked / points * 2 * Math.PI;
            path.Add((t, cx + r * Math.Cos(a), cy + r * Math.Sin(a) * (flat ? 1 : 0.8)));
            for (int k = 0; k < 2; k++) { walked += speed; speed = Math.Clamp(speed * factor, 0, 40); }
        }
        return [.. path];
    }

    /// <summary>한 바퀴를 다 도는 틱 수 — 빠르기 · 곱수 · 최대 40.</summary>
    private static int LapTicks(double r, double speed, double factor)
    {
        int points = Math.Max(8, (int)(8 * r)), t = 0;
        for (double walked = 0; walked < points && t < 2000; t++) { walked += speed; speed = Math.Min(40, speed * factor); }
        return t;
    }

    /// <summary>솟는 그림(<c>0x1007a500</c>) — 빠르기만큼 높이 z 가 오른다(곱수, 1~40).</summary>
    private void Rise(int obs, int motion, int tick, double x, double y, double speed, double factor, int? life = null)
    {
        int len = life ?? Length(obs, motion);
        var path = new List<(int, double, double)>();
        double z = 0;
        for (int t = 0; t <= len; t += 3)
        {
            path.Add((t, x, y - z * 0.6));
            for (int k = 0; k < 3; k++) { z += speed; speed = Math.Clamp(speed * factor, 1, 40); }
        }
        Along(obs, motion, tick, [.. path], life);
    }

    /// <summary>터지는 파편(<c>0x10079780</c>) — 둘레 ±300(높이 ±100) 의 아무 점으로 날아가고 수명(0 = 모션 한 번)이 되면 끝난다.</summary>
    private void Debris(int obs, int motion, int tick, double x, double y, double speed, double factor, int? life = null)
    {
        double tx = x + Rand(601) - 300, ty = y + (Rand(601) - 300) * 0.8 - Math.Max(0, Rand(201) - 100) * 0.6;
        var points = Move((x, y), (tx, ty), speed, factor, 40, 0.5);
        int len = life ?? Length(obs, motion);
        Along(obs, motion, tick, AsPath(points.Take(len).Prepend((x, y)).ToList(), 2), life);
    }

    /// <summary>얼음 조각 열 개 — 16방향(<c>r &lt; 9</c> 면 모션 r, 아니면 16−r 을 뒤집어)과 눈 연기 스물.</summary>
    private void Shards(int tick, double x, double y)
    {
        for (int n = 0; n < 10; n++)
        {
            int r = Rand(16);
            Put(787, r < 9 ? r : 16 - r, tick + Rand(5), x, y, mirror: r >= 9);
        }
        for (int n = 0; n < 20; n++) Debris(39, 0, tick + Rand(5), x, y, Rand(10) + 5, 0.9);
    }

    /// <summary>번개 기둥 — Obs 75 넷을 높이 425 씩 쌓는다(화면 255px, 위로 화면 밖까지). 2틱마다 모션이 바뀌어 번쩍인다.</summary>
    private void Bolt(int tick, double x, double y, int life)
    {
        for (int k = 0; k < 4; k++) Along(75, 0, tick, [(0, x, y - 255 * k)], life, flicker: true);
    }

    /// <summary>파트1 동작 번호 → 이 게임의 동작 번호(<see cref="Part1Game.ActionMap"/>). 16 이상은 40 을 더한 자리에 옮겨 두었다.</summary>
    private static int Part2Action(int part1) => part1 switch
    {
        0 => 0, 1 => 1, 2 => 8, 3 => 9, 4 => 7, 5 => 2, 6 => 6, 9 => 5, 11 => 13, 12 => 14, 13 or 14 or 15 => Part1Game.ExtraBase + part1,
        >= Part1Game.ExtraFrom => Part1Game.ExtraBase + part1,
        _ => 0,
    };

    private static Facing FacingOf(double dx, double dy) =>
        Math.Abs(dx) >= Math.Abs(dy) * 1.25 ? (dx < 0 ? Facing.Left : Facing.Right) : (dy < 0 ? Facing.Up : Facing.Down);

    /// <summary>
    /// 인물에게 파트1 동작을 건다(<paramref name="repeat"/> 번, 0 이면 붙듦) — 그 동작이 없는 그림(파트2 모습)이면 <paramref name="fallback"/>(이 게임 동작).
    /// 걸린 길이(틱)를 돌려준다.
    /// <para>원본은 인물 동작을 걸면 그 모션의 키를 틱마다 돌려(<c>0x100982d0</c>) 소리 키(종류 1)와 자식 이펙트(종류 2)의 소리도 낸다 —
    /// 풍아열공참(293)의 칼질 소리는 핸들러가 아니라 살라딘 동작 14(모션 42~44 의 Snd 24 · 22 · 26 · 16 · 23, 자식 227 의 26)에 있다.
    /// 그래서 되풀이마다 그 모션의 소리를 예약한다. 붙드는 동작(<paramref name="repeat"/> 0)은 끝나는 때를 몰라 소리를 안 낸다
    /// (앞머리 끝 자세는 원본도 +0x26 = −1 로 키를 멈춘다).</para>
    /// </summary>
    private int Act(UnitState u, int part1Action, int fallback, int repeat = 1)
    {
        if (!host._sprites.TryGetValue(u.ChrCode, out var sprite)) return 0;
        int action = Part2Action(part1Action), use = sprite.RawClip(action * 3) != null ? action : fallback;
        double once = Math.Max(0.1, sprite.ActionSeconds(use, u.Facing));
        u.PlayAction(use, repeat <= 0 ? 30 : once * repeat);
        for (int r = 0; r < repeat; r++) host.ScheduleActionSounds(u, use, r * once);
        return repeat <= 0 ? 0 : (int)Math.Round(once * repeat * TicksPerSecond);
    }

    /// <summary>시전자를 이 길대로 옮긴다(파트1 CDash 가 틱마다 유닛 자리를 덮어쓴다 — 0x100821dc). 길은 판 픽셀 발 자리.</summary>
    private void Carry(int tick, List<(double X, double Y)> points) => _shifts.Add((At(tick), points));

    /// <summary>판에 보이는 칸 — (왼, 위, 오른, 아래) 판 픽셀.</summary>
    private (int L, int T, int R, int B) View => (host._camX, host._camY, host._camX + host.ViewWidth, host._camY + host.ViewHeight);

    /// <summary>
    /// 기술 하나를 처음부터 끝까지 — 앞머리 뒤 시각표를 깔고, 대상마다 맞는 때에 <paramref name="hit"/> 를 부른다.
    /// <paramref name="cells"/> 는 범위 칸(대상을 고르지 않고 칸마다 터지는 머스펠 헤임 · 타이타니아가 쓴다).
    /// <paramref name="proc"/> 면 무기 덧기술 — 원본은 +0x212 = 1 로 불러 앞머리 없이 바로 핸들러를 돈다(<c>0x10035661</c> → <c>0x10039e60(…, 1)</c>).
    /// </summary>
    internal IEnumerable<bool> Routine(WorkData w, UnitState user, List<int> targets, List<(int Col, int Row)> cells, (int Col, int Row) aim, Action<int> hit,
                                       bool proc = false)
    {
        host.PreloadSounds([.. Part1Game.SkillSounds.Select(P)]);
        // 파트1 이 없는 PC — 이 기술의 그림을 릴리즈에서 받는다. 다 올 때까지(최대 40초) 기다리며 몇 개 받았는지 알린다(menu-91).
        int[] need = [.. NeedsFor(w.Id).Select(P), .. user.Data?.SpriteId is > Part1Game.ObsBase and var own ? [(int)own] : Array.Empty<int>()];
        bool Ready(int id) => AssetPack.Fetch("moses/obs", $"{id:D4}.obs", 0) != null;
        for (double until = host._lastTime + 40; need.Any(id => !Ready(id)) && host._lastTime < until;)
        {
            host.Toast($"파트1 그림을 받는 중입니다… ({need.Count(Ready)}/{need.Length})");
            for (double poll = host._lastTime + 0.5; host._lastTime < poll;) yield return true;
        }
        _user = user;
        _aimCell = aim;
        _afterPrep = null;
        _home = host.Btl.UnitFoot(user);
        _events.Clear();
        _shifts.Clear();
        _soundTicks.Clear();
        var facing = user.Facing;
        var (hx, hy) = _home;
        var (ax, ay) = CellFoot(aim.Col, aim.Row);

        _t0 = host._lastTime;
        int prep = proc ? 0 : IsWeaponWork(w.Id) ? WeaponPrep(user, (ax, ay)) : IsMagicWork(w.Id) ? MagicPrep(Part1SkillBook.AttackIdOf(w.Id), user, (ax, ay))
            : IsSword2Work(w.Id) ? Sword2Prep(Part1SkillBook.AttackIdOf(w.Id), user, (ax, ay))
            : IsBigWork(w.Id) ? BigPrep(Part1SkillBook.AttackIdOf(w.Id), user, (ax, ay))
            : IsGunWork(w.Id) ? GunPrep(Part1SkillBook.AttackIdOf(w.Id), user, (ax, ay)) : w.Id is HellKiteWork or NereidWork or MuspelWork or KannonWork or TitaniaWork or BlackFoxWork or ThunderWork or MeteorWork
            ? SummonPrep(user, (ax, ay)) : SwordPrep(user);
        for (double end = At(prep); host._lastTime < end;) yield return true;
        // 앞머리 끝 자세 — 원본은 앞머리 마지막 틱에 동작을 한 번 더 걸고, 마법 · 소환(종류 3 · 4)과 총(종류 5)은 +0x26 = −1(0x1009a210)로
        // 모션 키 진행을 멈춰 그 자세(록슬리 동작 24 = 팔을 뻗은 장 41/48)를 핸들러가 끝날 때까지 붙든다(0x100982d0 이 +0x26 ≠ 0 이면 건너뛴다).
        // 전에는 이것을 Later 로 걸어 바로 아래 _events.Clear() 에 지워져, 화살이 나는 동안 시전자가 서 있었다.
        if (!proc && _afterPrep is { } after) Act(user, after.Part1, after.Fallback, after.Hold ? 0 : 1);
        _afterPrep = null;

        _t0 = host._lastTime;
        _events.Clear();
        int last = w.Id switch
        {
            BlizzardWork => Blizzard(targets, hit),
            SeolhwaWork => Seolhwa(targets, hit),
            HellStrikeWork => HellStrike(targets, hit),
            AsuraWork => Asura(targets, hit),
            ThunderWork => Thunder(targets, hit),
            MeteorWork => Meteor(targets, hit),
            _ when IsLightning(w.Id) => Lightning(w.Id, targets, (ax, ay), hit),
            _ when IsWeaponWork(w.Id) => Weapon(Part1SkillBook.AttackIdOf(w.Id), targets, (ax, ay), hit),
            _ when IsMagicWork(w.Id) => Magic(Part1SkillBook.AttackIdOf(w.Id), targets, cells, (ax, ay), hit),
            _ when IsSword2Work(w.Id) => Sword2(Part1SkillBook.AttackIdOf(w.Id), targets, cells, (ax, ay), hit),
            _ when IsBigWork(w.Id) => Big(Part1SkillBook.AttackIdOf(w.Id), targets, cells, (ax, ay), hit),
            _ when IsGunWork(w.Id) => Gun(Part1SkillBook.AttackIdOf(w.Id), targets, cells, (ax, ay), hit),
            FlyingSlashWork => FlyingSlash(targets, (ax, ay), hit),
            StormSwordWork => StormSword(targets, hit),
            HellKiteWork => HellKite(targets, hit),
            NereidWork => Nereid(targets, hit),
            MuspelWork => Muspel(targets, hit),
            KannonWork => Kannon(targets, hit),
            TitaniaWork => Titania(cells, hit),
            BlackFoxWork => BlackFox(targets, hit),
            _ => 0,
        };
        double finish = At(last);
        if (BattleScene.Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 work {w.Id}: targets {targets.Count}, cells {cells.Count}, fx {_fx.Count}, lines {_lines.Count}, events {_events.Count}, took {last} ticks" + Environment.NewLine);

        _events.Sort((a, b) => a.At.CompareTo(b.At));
        int next = 0;
        while (host._lastTime < finish || next < _events.Count)
        {
            while (next < _events.Count && _events[next].At <= host._lastTime) _events[next++].Run();
            user.Shift = (0, 0);
            foreach (var (start, points) in _shifts)
            {
                int tick = (int)((host._lastTime - start) * TicksPerSecond);
                if (tick < 0 || tick >= points.Count) continue;
                user.Shift = (points[tick].X - hx, points[tick].Y - hy);
            }
            yield return true;
        }
        user.Shift = (0, 0);
        user.Fade = 1;
        user.Facing = facing;
        user.PlayAction(ObsMotionTable.ActionStand, 0);
    }

    /// <summary>그 기술이 쓰는 파트1 그림(앞머리 · 자식 그림까지) — 파트1 이 없는 PC 에서 쓰기 전에 받아 둔다.</summary>
    private static int[] NeedsFor(int work)
    {
        if (IsWeaponWork(work)) return WeaponNeeds(Part1SkillBook.AttackIdOf(work));
        if (IsMagicWork(work)) return MagicNeeds(Part1SkillBook.AttackIdOf(work));
        if (IsSword2Work(work)) return Sword2Needs(Part1SkillBook.AttackIdOf(work));
        if (IsGunWork(work)) return GunNeeds(Part1SkillBook.AttackIdOf(work));
        if (IsBigWork(work)) return BigNeeds(Part1SkillBook.AttackIdOf(work));
        int[] sword = [395], summon = [55, 49, 91, 50, 897];
        int[] own = work switch
        {
            BlizzardWork => [37, 39, 897, 101, 56, 112, 51, 768, 787],
            SeolhwaWork => [39, 173, 190, 192, 118, 101, 56, 112, 51, 767, 787],
            HellStrikeWork => [124, 10, 832, 228, 38],
            AsuraWork => [124, 526, 38, 832, 518, 519, 83, 696, 862, 846, 843],
            ThunderWork => [75, 379, 71, 277, 77],
            MeteorWork => [115, 34, 64, 123, 228, 124, 116, 23, 96, 93],
            _ when IsLightning(work) => [56, 35, 65, 76, 75, 379, 71, 277, 41, 77],
            FlyingSlashWork => [503, 504, 230],
            StormSwordWork => [959, 75, 72, 379, 76, 67],
            HellKiteWork => [103, 671, 10, 23, 93],
            NereidWork => [114, 56, 65, 51, 787],
            MuspelWork => [211],
            KannonWork => [497, 212, 194],
            TitaniaWork => [210, 71, 379, 667],
            BlackFoxWork => [282, 56, 526, 192],
            _ => [],
        };
        return [.. (work is BlizzardWork or SeolhwaWork or HellStrikeWork or AsuraWork or StormSwordWork or FlyingSlashWork ? sword : summon).Concat(own).Distinct()];
    }

    /// <summary>검 기술 앞머리(<c>0x10043950</c>, 50틱) — 시전자 둘레에 Obs 395:0(소리 1878), 정면을 보고 공격 준비(파트1 동작 9)를 붙든다.</summary>
    private int SwordPrep(UnitState user)
    {
        var (hx, hy) = _home;
        Put(395, 0, 0, hx, hy - 60 * 0.6, sounds: true);
        host.Btl.CenterOnUnit(user);
        user.Facing = Facing.Down;
        user.PlayAction(5, 51 / TicksPerSecond);
        _afterPrep = (9, 5, false);   // 50틱에 동작 9 를 한 번 더(잠그지 않는다)
        return 51;
    }

    /// <summary>
    /// 소환 · 마법 앞머리(<c>0x10043de0</c>, 54틱) — 검은 빛 55:0, 마법진 49:0·1(소리 73) · 91:0·1(10틱 뒤, 소리 1846), 솟는 불티 50:0 다섯 무리 ×7,
    /// 기운 897:0→1→2, 카메라를 시전자에. 시전자는 대상 쪽을 보고 파트1 동작 4 를 54틱 붙든 뒤 동작 24 를 한 번 한다.
    /// </summary>
    private int SummonPrep(UnitState user, (double X, double Y) aim)
    {
        var (hx, hy) = _home;
        Put(55, 0, 0, hx, hy - 40 * 0.6);
        Put(49, 0, 0, hx, hy, sounds: true);
        Put(49, 1, 0, hx, hy);
        Put(91, 0, 10, hx, hy);
        Put(91, 1, 10, hx, hy, sounds: true);
        for (int k = 0; k < 5; k++)
            for (int n = 0; n < 7; n++) Rise(50, 0, 20 + 5 * k, hx + Rand(41) - 20, hy + Rand(41) - 20, Rand(20) + 10, 0.95);
        int m0 = Length(897, 0), m1 = Length(897, 1);
        Put(897, 0, 10, hx, hy - 20 * 0.6);
        Put(897, 1, 10 + m0 + 1, hx, hy - 20 * 0.6);
        Put(897, 2, 10 + m0 + m1 + 2, hx, hy - 20 * 0.6);
        host.Btl.CenterOnUnit(user);
        if (aim != _home) user.Facing = FacingOf(aim.X - hx, aim.Y - hy);
        user.PlayAction(Part2Action(4), 54 / TicksPerSecond);
        _afterPrep = (24, Part2Action(4), true);
        return 55;
    }

    /// <summary>그림 · 선 · 눈송이 · 번쩍임을 그린다 — DrawEffects 가 부른다.</summary>
    internal void Draw()
    {
        double now = host._lastTime;
        int cx = host._camX + host.ViewWidth / 2, cy = host._camY + host.ViewHeight / 2;
        bool flash = _flashes.Any(f => now >= f.Start && now < f.End);
        _fx.RemoveAll(f =>
        {
            if (now < f.Start) return false;
            int tick = (int)((now - f.Start) * TicksPerSecond);
            if (host.UiFor(f.Obs) is not { } sprite) return true;
            int end = f.Life ?? Math.Max(1, sprite.MotionLength(f.Motion));
            if (tick >= end) return true;
            var path = f.Path;
            int k = 0;
            while (k + 1 < path.Length && tick >= path[k + 1].Tick) k++;
            var (a, b) = (path[k], path[Math.Min(k + 1, path.Length - 1)]);
            double t = b.Tick > a.Tick ? Math.Clamp((tick - a.Tick) / (double)(b.Tick - a.Tick), 0, 1) : 0;
            int x = (int)(a.X + (b.X - a.X) * t) + (f.Screen ? cx : 0), y = (int)(a.Y + (b.Y - a.Y) * t) + (f.Screen ? cy : 0);
            bool loop = f.Life != null;
            int motion = f.Flicker ? (int)((uint)(tick / 2 * 2654435761u + (uint)f.GetHashCode()) % 3) : f.Motion;
            double fade = f.FadeFrom + (f.FadeTo - f.FadeFrom) * Math.Clamp(tick / (double)Math.Max(1, end - 1), 0, 1);
            if (sprite.Clip(motion) is { Children.Count: > 0 } clip) host.Btl.DrawUnitLayers(clip, tick, x, y, f.Mirror, fade, loop: loop);
            int key = sprite.BlendAt(motion, tick);
            host.DrawUi(f.Obs, motion, tick, x, y, BlendOf(key), loop: loop, mirror: f.Mirror, fade: fade * BlendFade(key));
            return false;
        });
        _lines.RemoveAll(l =>
        {
            if (now < l.Start) return false;
            int tick = (int)((now - l.Start) * TicksPerSecond);
            if (tick >= l.Life) return true;
            if (l.At(tick) is not { } seg) return false;
            int ox = l.Screen ? cx : 0, oy = l.Screen ? cy : 0;
            uint color = flash ? 0xFFFFFFFF : l.Color;
            uint alpha = (uint)Math.Clamp(seg.Alpha * (color >> 24), 0, 255) << 24 | (color & 0xFFFFFF);
            double dx = seg.X1 - seg.X0, dy = seg.Y1 - seg.Y0;
            int steps = Math.Max(1, (int)(Math.Sqrt(dx * dx + dy * dy) / 3));
            for (int s = 0; s <= steps; s++)
                host.FillRect((int)(seg.X0 + dx * s / steps) + ox, (int)(seg.Y0 + dy * s / steps) + oy, l.Width, l.Width, alpha);
            return false;
        });
        _flakes.RemoveAll(f =>
        {
            if (now < f.Start) return false;
            double tick = (now - f.Start) * TicksPerSecond, z = f.Z - f.Speed * tick;
            if (z <= 0) return true;
            double sway = Math.Abs(tick % (2 * f.Sway) - f.Sway) - f.Sway / 2.0;
            host.FillRect((int)(f.X + sway), (int)(f.Y - z * 0.6), 2, 2, 0xFFF4F8FF);
            return false;
        });
        // 번쩍임 — 원본은 회색(222) 사각을 뒤집는 섞기로 덮는다(어두운 곳이 밝아진다). 여기서는 밝은 반투명으로 덮는다(가설).
        _flashes.RemoveAll(f => now >= f.End);
        if (flash) host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0x70E8E8F0);
        _whiteouts.RemoveAll(w => now >= w.Start + w.Frames / TicksPerSecond);
        foreach (var (start, frames, color) in _whiteouts)
        {
            if (now < start) continue;
            double t = (now - start) * TicksPerSecond / frames, level = t < 0.5 ? t * 2 : (1 - t) * 2;
            host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, (uint)(Math.Clamp(level, 0, 1) * 0xE0) << 24 | (color & 0xFFFFFF));
        }
    }
}

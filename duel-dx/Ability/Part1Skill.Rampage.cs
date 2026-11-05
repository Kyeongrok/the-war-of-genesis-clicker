using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 폭주(Attack 380, 어빌리티 73 — 올리비에) — 핸들러 <c>0x1006d000</c>(볼트 분석-파트1-기술-b 0.2 · 6 · 9.8 절이 「380 이 이것을 다시 쓴다」고만 적은 것).
/// </summary>
/// <remarks>
/// <para>원본은 유닛 +0x20e 단계로 틱마다 다시 불린다(점프표 <c>0x1006d344</c>, 7갈래):
/// 0 — 시전자 둘레에 기운(Obs 897:0 → 897:1 수명 0x420 → 897:2, z+20)과 Obs 20:4(z+60, 수명 0x386)를 깔고, 정면을 보고 동작 9(<c>0x10035e00(0x1b, 2, 1)</c>)를
/// 잠근 채(<c>0x1009a210(−1)</c>) <b>메테오 핸들러</b>(<c>0x1004a190</c>)를 부른다 ·
/// 1 · 3 — 바쁜 이펙트 수(world +0xc9ea)가 3 이하가 될 때까지(= 깔린 기운 셋만 남을 때까지) 기다린다 ·
/// 2 — <b>문 라이트 핸들러</b>(<c>0x1004c480</c>) · 4 — <b>썬더스탐 핸들러</b>(<c>0x1004dc80</c>) ·
/// 5 — 바쁜 이펙트가 0 이 될 때까지(기운까지 다 사라질 때까지) 기다린다 ·
/// 6 — 시전자가 버써크(+0x24e)가 아니면 표지(<c>0x10041c60(8)</c>)를 달고 버써크 255 를 건다(<c>0x10042570(8, 0xff)</c>, 이미 걸려 있으면 그대로) → 끝(<c>0x1003a350</c>).</para>
/// <para>세 핸들러는 시전자 +0x1fc = 380 인 채로 불려 피해는 모두 폭주(Attack 380)의 세기로 든다. 그래서 대상마다 단계마다 한 번씩, 많게는 세 번 맞는다.
/// 리메이크는 버써크를 파트2 상태 4(버서커)로 걸고, 그 상태는 차례로 줄지 않아 큐어 · 회복 아이템으로 풀 때까지 남는다(원본 255 와 같은 꼴).</para>
/// <para>가설: 「바쁜 수 3 이하」를 앞 단계 이펙트가 다 끝난 때로 본다 — 다음 단계는 앞 핸들러가 돌려준 끝 틱에 시작한다.</para>
/// <para>전에는 이 핸들러를 안 옮겨 work 20380 이 빌린 마법 틀로 판 전체를 한 번 치고 끝났고 버써크도 안 걸렸다(버그 리포트 #48).</para>
/// </remarks>
internal sealed partial class Part1Skill
{
    internal const int RampageAttack = 380;

    /// <summary>폭주(Attack 380)인가 — 어빌리티 73 이 만든 work(보통 20380).</summary>
    internal static bool IsRampageWork(int work) => work > Part1Game.ChrBase && !Works.Contains(work) && Part1SkillBook.AttackIdOf(work) == RampageAttack;

    private static readonly int[] RampageAura = [897, 20];

    private int Rampage(List<int> targets, List<(int Col, int Row)> cells, (double X, double Y) aim, Action<int> hit)
    {
        var (hx, hy) = _home;
        // 단계 0 — 기운 넷. 897:1 은 897:0 뒤에(0x10076580), 20:4 도 897:0 뒤에, 897:2 는 897:1 뒤에 잇는다.
        int m0 = Length(897, 0), auraHold = 0x420, sparkHold = 0x386;
        Put(897, 0, 0, hx, hy - 20 * 0.6);
        Put(897, 1, m0 + 1, hx, hy - 20 * 0.6, life: auraHold);
        Put(20, 4, m0 + 1, hx, hy - 60 * 0.6, life: sparkHold);
        int auraEnd = m0 + 1 + auraHold + 1 + Length(897, 2);
        Put(897, 2, m0 + 1 + auraHold + 1, hx, hy - 20 * 0.6);

        // 단계마다 같은 대상은 한 번 — 메테오는 운석 여럿이 같은 이를 덮어도 한 번(리메이크 메테오와 같이), 세 단계를 합치면 세 번까지.
        // 대상 목록은 시작 때 것이라 앞 단계에서 쓰러진 이는 건너뛴다(원본 문 라이트는 그때 판 위의 적을 훑는다).
        Action<int> Once()
        {
            var struck = new HashSet<int>();
            return ti => { if (host._units[ti] is { Alive: true, Hp: > 0 } && struck.Add(ti)) hit(ti); };
        }

        int meteorEnd = Stage(0, () => Meteor(targets, Once()));
        int moonEnd = Stage(meteorEnd, () => MoonLight(124, targets, cells, aim, Once()));
        int thunderEnd = Stage(moonEnd, () => Thunder(targets, Once()));
        int end = Math.Max(thunderEnd, auraEnd);

        // 시전자는 정면(아래)을 보고 동작 9 를 끝까지 붙든다(+0x26 = −1).
        _user.Facing = Facing.Down;
        if (host._sprites.TryGetValue(_user.ChrCode, out var sprite))
        {
            int action = Part2Action(9), use = sprite.RawClip(action * 3) != null ? action : 5;
            _user.PlayAction(use, end / TicksPerSecond);
        }

        // 단계 6 — 버써크(파트1 상태 8 → 리메이크 상태 4). 이미 걸려 있으면 PutAilment 가 값만 견준다.
        var user = _user;
        Later(end, () =>
        {
            if (user.Alive && user.Hp > 0 && !user.HasStatus(4)) host.Btl.PutAilment(user, 4, 1, [], user);
            if (BattleScene.Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"part1 rampage end: chr {user.ChrCode} berserk {user.HasStatus(4)} (meteor ~{meteorEnd} · moon ~{moonEnd} · thunder ~{thunderEnd} · aura {auraEnd} ticks)" + Environment.NewLine);
        });
        return end + 1;
    }

    /// <summary>핸들러 하나를 <paramref name="offset"/> 틱 뒤에 시작하는 것으로 깐다 — 끝 틱(전체 기준)을 돌려준다.</summary>
    private int Stage(int offset, Func<int> body)
    {
        double saved = _t0;
        _t0 += offset / TicksPerSecond;
        try { return offset + body(); }
        finally { _t0 = saved; }
    }

    private static int[] RampageNeeds() =>
        [.. RampageAura, 95, 115, 34, 64, 54, 123, 228, 124, 116, 23, 96, 93, .. MoonLightNeeds, 75, 379, 71, 277, 77];
}

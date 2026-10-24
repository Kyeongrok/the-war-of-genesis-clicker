namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1 전투 이벤트 명령(코드 + 인자 6칸)을 파트2 명령(코드 + 인자 8칸, <see cref="BattleScript"/>)으로 옮긴다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 4·5·6절. 뜻이 같고 번호·인자 자리만 다른 것은 파트2 코드로 바꾸고,
/// 파트2 에 없는 것은 <see cref="Part1Only"/> + 파트1 코드로 남긴다(엔진에 파트1 갈래를 둘 때 쓴다).
/// <list type="bullet">
/// <item>파트1 비교 조건은 모두 「기준 a1 · 연산 a2」다. 파트2 100·101·400 은 「연산 a1 · 기준 a2」라 자리를 바꾼다.</item>
/// <item>파트1 사각형은 두 낱말에 바이트 넷(x1 | y1 &lt;&lt; 8, x2 | y2 &lt;&lt; 8)이고, 파트2 는 낱말 넷이다.</item>
/// <item>파트1 조건 300·301 의 편은 생짜 편 번호(4 플레이어 · 3 동맹 · 0~2 적), 파트2 400·401 도 같은 생짜 번호를 받는다(BattleScene.Events 가 u.Side 와 바로 견준다).</item>
/// <item>행동 200(대사)의 줄 번호는 TXR·Tlk 가 아니라 그 판의 <c>.dlg</c> 줄(<see cref="Part1Battle.Lines"/>)이다 — 파트2 600 의 a2 자리에 그대로 둔다.</item>
/// <item>행동 400(기술 시전)의 a1 은 파트1 <c>Attack.dat</c> 번호다 — 파트2 207 의 a2(work) 자리에 그대로 둔다.</item>
/// </list>
/// </remarks>
public static class Part1Script
{
    /// <summary>파트2 에 같은 것이 없는 파트1 명령 — 코드 = 이 값 + 파트1 코드.</summary>
    public const int Part1Only = 30000;

    public static ScriptCommand Condition(ScriptCommand c)
    {
        var a = c.Args;
        return c.Code switch
        {
            0 => Make(2, a[0], a[1], a[2]),                                  // 타이머 비교 — 같은 자리
            1 => Make(1),                                                    // 전투 시작
            2 => Make(3, a[0], 0),                                           // 시간 틱 == a0
            100 => Make(200, a[0], 0, a[1]),                                 // 전장에 있나 — 깃발 a1 → a2
            101 => Make(201, a[0]),                                          // 죽었나·없나
            200 => Make(300, a[0], 0, a[1]),                                 // 붙었다 — B a1 → a2
            201 => Make(301, a[0], 0, a[1]),                                 // A 가 B 를 때렸다 — B a1 → a2
            300 => Make(400, a[0], a[2], a[1], Lo(a[3]), Hi(a[3]), Lo(a[4]), Hi(a[4])),   // 사각형 안의 수
            301 => Make(401, a[0]),                                          // 편 전멸 — 파트2 조건 400·401 도 생짜 편 번호다
            302 => Make(402, a[0], 0, Lo(a[1]), Hi(a[1]), Lo(a[2]), Hi(a[2])),                         // 사각형 안에 있나
            400 => Make(100, a[0], a[2], a[1]),                              // 전투 변수 비교 — 기준·연산 바꿈
            401 => Make(101, a[0], a[2], a[1]),                              // 진행 깃발 비교 — 기준·연산 바꿈
            _ => Keep(c),                                                    // 102 유닛 변수 · 103 HP 값 · 104 · 402 장비
        };
    }

    public static ScriptCommand Action(ScriptCommand c)
    {
        var a = c.Args;
        return c.Code switch
        {
            0 or 1 or 2 or 3 => Make(c.Code, a[0], a[1]),                    // 이벤트 부르기 · 기다림 · 틱 기다림 · 접기 — 같다
            100 => Make(11, a[0]),                                           // 승패
            101 => Make(10, a[0]),                                           // 이어서 다음 전투
            104 => Make(6, a[0]),                                            // 끝내고 필드로
            200 => Make(600, a[0], 0, a[1]),                                 // 대사 상자 — .dlg 줄 a1 → a2
            201 => Make(601, a[0], 0, a[1]),                                 // 말풍선(가설)
            300 => Make(200, a[0], 0, a[1], a[2], a[3]),                     // 증원 등장
            301 => Make(201, a[0], 0, a[1], a[2], a[3]),                     // 퇴장
            302 => Make(202, a[0], 0, a[1], a[2]),                           // 칸으로 걷기
            400 => Make(207, a[0], 0, a[1]),                                 // 기술 시전 — Attack.dat 번호
            506 => Make(707, a[0], 0, a[1]),                                 // 잃은 HP % 회복
            507 => Make(706, a[0], 0, a[1]),                                 // 최대 HP % 피해
            509 => Make(708, a[0], 1, a[1]),                                 // 편 바꾸기 — 부하까지
            600 => Make(400, a[0], a[1]),                                    // 카메라를 칸으로
            602 => Make(402, a[0]),                                          // 카메라를 유닛에게
            700 => Make(100, a[0], a[1]),                                    // 전투 변수 = 값
            701 => Make(101, a[0], a[1]),                                    // 전투 변수 += 값
            702 => Make(102, a[0], a[1]),                                    // 진행 깃발 = 값
            703 => Make(103, a[0], 0, a[1]),                                 // 진행 깃발 += 값(파트2 연산 0 = 더하기)
            704 => Make(900, a[0], 1),                                       // 타이머 켜기
            707 => Make(907, a[0], a[1]),                                    // 맵 물체 상태
            708 => Make(906, Lo(a[0]), Hi(a[0]), Lo(a[1]), Hi(a[1])),        // 영역 강조
            _ => Keep(c),
        };
    }

    /// <summary>파트1 이벤트 하나를 파트2 꼴로 — 둘째 낱말(파트1 에 없다)은 0.</summary>
    public static BattleEvent Event(Part1BattleEvent e) =>
        new(e.Index, e.MaxFire, 0, [.. e.Conditions.Select(Condition)], [.. e.Actions.Select(Action)]);

    private static short Lo(short packed) => (short)(packed & 0xFF);
    private static short Hi(short packed) => (short)((packed >> 8) & 0xFF);

    private static ScriptCommand Make(int code, params int[] args)
    {
        var full = new short[8];
        for (int i = 0; i < args.Length; i++) full[i] = (short)args[i];
        return new ScriptCommand(code, full);
    }

    private static ScriptCommand Keep(ScriptCommand c)
    {
        var full = new short[8];
        c.Args.CopyTo(full, 0);
        return new ScriptCommand(Part1Only + c.Code, full);
    }
}

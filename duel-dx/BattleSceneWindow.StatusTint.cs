namespace DuelDx;

/// <summary>
/// 상태이상에 걸린 인물을 물들이는 색 — 빙결은 파랗게, 중독은 청록, 버서커·화염은 붉게(<c>0x10071be2</c>~<c>0x10071c6d</c>).
/// </summary>
/// <remarks>
/// 원본은 인물을 그릴 때 물들이기 <b>방식 번호와 세기</b>를 같이 넘기고, 세기는 상태이상일 때 <b>늘 30 붙박이</b>다
/// (틱·프레임이 안 들어간다). 방식은 걸린 상태 중 <b>하나만</b> 고른다 — 우선순위 <b>6 빙결 &gt; 5 마비 &gt; 2 화염 &gt; 4 버서커 &gt; 3 중독</b>.
/// <para>
/// 방식 번호는 <c>0x1001ab10</c> 의 <b>두 번째 switch</b>(<c>0x1001ad42</c>, 1~18)로 들어가고, 채널마다 색표 한 장을 고른다.
/// 쓰는 표는 세 가지뿐이다(모두 <c>0x1000b7c0</c> 이 시작할 때 만든다, 자리 = <c>표[c×32 + α]</c>):
/// </para>
/// <list type="bullet">
/// <item><b>민짜</b> <c>0x10185818</c> — <c>v = c</c>(그대로).</item>
/// <item><b>검정쪽</b> <c>0x1018b818</c> — <c>v = c(31−α)/31</c>. α=30 이니 <b>사실상 그 채널을 죽인다</b>.</item>
/// <item><b>특수곡선</b> <c>0x10188818</c> — <c>s = (31−c)/4 + 3; v = (31 − αs/5) × c / 31</c>, 그 뒤 <b>16비트 부호 없는 비교</b>로
/// 31 을 넘으면 31. α 가 크면 값이 음수로 내려가 <b>31 로 감긴다</b> — 원본에 그대로 남아 있는 자국이라 여기서도 그대로 둔다.</item>
/// </list>
/// 마비(5)는 색표가 아니라 딴 합성 함수(<c>ds:0x101737d8</c>)를 타는 번쩍임이라 여기서는 아무것도 안 바꾼다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>상태이상 물들이기 세기 — 원본이 다섯 갈래 모두 곧이곧대로 넣는 값.</summary>
    private const int StatusTintAlpha = 30;

    /// <summary>채널 하나를 옮기는 5비트 색표(<c>표[c5] → v5</c>).</summary>
    private static byte[] TintTable(Func<int, int> f) => [.. Enumerable.Range(0, 32).Select(c => (byte)f(c))];

    private static readonly byte[] TintIdentity = TintTable(c => c);
    private static readonly byte[] TintToBlack = TintTable(c => c * (31 - StatusTintAlpha) / 31);
    private static readonly byte[] TintCurve = TintTable(c =>
    {
        int s = (31 - c) / 4 + 3;
        int v = (31 - StatusTintAlpha * s / 5) * c / 31;
        return (ushort)v > 31 ? 31 : v;      // 음수가 16비트로 감겨 31 이 되는 것까지 원본 그대로
    });

    /// <summary>그 인물에게 걸 색표 셋(빨강·초록·파랑). 물들일 것이 없으면 null.</summary>
    private static (byte[] R, byte[] G, byte[] B)? StatusTintOf(UnitState u)
    {
        if (u.HasStatus(6)) return (TintCurve, TintCurve, TintIdentity);      // 빙결 — 파랗게 씻긴다
        if (u.HasStatus(5)) return null;                                      // 마비 — 색표를 안 쓴다
        if (u.HasStatus(2)) return (TintIdentity, TintCurve, TintIdentity);   // 화염 — 붉게(초록만 뭉갬)
        if (u.HasStatus(4)) return (TintIdentity, TintToBlack, TintIdentity); // 버서커 — 자홍
        if (u.HasStatus(3)) return (TintToBlack, TintIdentity, TintIdentity); // 중독 — 청록
        return null;
    }

    /// <summary>8비트 색 한 점을 5비트 색표로 옮긴다.</summary>
    private static uint ApplyStatusTint(uint c, (byte[] R, byte[] G, byte[] B) t)
    {
        static uint Map(uint channel, byte[] table)
        {
            int v = table[channel >> 3];                 // 8비트 → 5비트
            return (uint)(v << 3 | v >> 2);             // 5비트 → 8비트로 펴기
        }
        return c & 0xFF000000 | Map(c >> 16 & 0xFF, t.R) << 16 | Map(c >> 8 & 0xFF, t.G) << 8 | Map(c & 0xFF, t.B);
    }
}

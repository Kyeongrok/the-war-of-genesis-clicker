using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 인물의 차례 목소리 — <b>원본에 없는 리메이크만의 것</b>(사용자 요청). 원본 파트1 은 차례가 와도 목소리를 안 낸다.
/// 필드 대사의 성우 음성(행동 206~208 [얼굴, 줄, Snd]) 가운데 어울리는 한 마디를 골라, 내가 움직이는 그 인물의 차례가 오면 낸다
/// (<see cref="BattleScene.PlayTurnCall"/> — 파트2 차례 목소리와 같은 자리 · 같은 조건). 모드 &gt; 일반 「파트1 차례 목소리」로 끈다(기본 켬).
/// </summary>
/// <remarks>인물을 더하려면 <see cref="People"/> 에 (그 사람의 파트1 Chr 번호들, 파트1 Snd 번호들) 한 줄을 넣는다. 목소리가 여럿이면 차례마다 틱으로 고른다.
/// 파트1 에셋 팩(<see cref="Part1Skill.PackList"/>)에도 저절로 실린다.</remarks>
internal static class Part1TurnCall
{
    /// <summary>
    /// 인물 묶음 — 같은 사람이 판마다 다른 Chr 번호로 나오므로(버몬트 대공 30 · 107 · 152 …) 번호 여럿에 같은 목소리들을 준다.
    /// 목소리는 인물마다 몇 개든 — 차례마다 틱으로 하나를 고른다.
    /// </summary>
    private static readonly (int[] Chrs, int[] Snds, string Who)[] People =
    [
        ([65, 184], [124, 148, 157, 1872], "크리스티앙"),   // 필드 0094 「좋아, 좋아. 그럼 열심히 하자구!」(207 [340, 13, 124]) · 「뭐라고요」 · 「어이, 아저씨」 · 「알겠다구요!」(사용자 요청 fg-88)
        ([10], [1107, 1141], "셰라자드"),  // 필드 0022 사건 3 「살라딘님!」(208 [311, 16, 1107], fg-45) · 필드 0029 사건 2 「왜 웃으세요?」(207 [311, 26, 1141], fg-87)
        // 사용자가 골라 둔 목소리(fg-88) — 「번호_인물_대사」 파일 이름의 번호가 파트1 Snd 번호다.
        ([30, 107, 152, 178, 316, 362], [298, 304], "버몬트 대공"),   // 「조금 늦었군」 · 「물론이지」
        ([42], [1566], "롤랑 솔즈베리"),                               // 「그렇다면 대공께서는」
        ([71, 302], [1622, 1650], "죠안 카트라이트"),                  // 「뭐라고요!」 · 「알겠습니다」
        ([12], [1823], "샤프리야르"),                                  // 「지난번, 라쉬카 사건 때도 많은 도움을 주었습니…」
    ];

    /// <summary>파트1 Chr 번호(<see cref="Part1Game.ChrBase"/> 를 뺀 것) → 파트1 Snd 번호들.</summary>
    internal static readonly IReadOnlyDictionary<int, int[]> Calls =
        People.SelectMany(p => p.Chrs.Select(c => (c, p.Snds))).ToDictionary(x => x.c, x => x.Snds);

    /// <summary>그 유닛의 차례 목소리(게임 안 소리 번호 = <see cref="Part1Game.ObsBase"/> + Snd) — 없으면 null.</summary>
    internal static int[]? For(UnitState u) =>
        u.ChrCode > Part1Game.ChrBase && Calls.TryGetValue(u.ChrCode - Part1Game.ChrBase, out var snd)
            ? [.. snd.Select(n => Part1Game.ObsBase + n)] : null;

    /// <summary>에셋 팩에 실을 파트1 Snd 번호.</summary>
    internal static IEnumerable<int> Sounds => People.SelectMany(p => p.Snds).Distinct();
}

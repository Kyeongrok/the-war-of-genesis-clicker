using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 인물의 차례 목소리 — <b>원본에 없는 리메이크만의 것</b>(사용자 요청). 원본 파트1 은 차례가 와도 목소리를 안 낸다.
/// 필드 대사의 성우 음성(행동 206~208 [얼굴, 줄, Snd]) 가운데 어울리는 한 마디를 골라, 내가 움직이는 그 인물의 차례가 오면 낸다
/// (<see cref="BattleScene.PlayTurnCall"/> — 파트2 차례 목소리와 같은 자리 · 같은 조건). 모드 &gt; 일반 「파트1 차례 목소리」로 끈다(기본 켬).
/// </summary>
/// <remarks>인물을 더하려면 <see cref="Calls"/> 에 파트1 Chr 번호 → 파트1 Snd 번호들을 한 줄 넣는다. 여럿이면 차례마다 틱으로 고른다.
/// 파트1 에셋 팩(<see cref="Part1Skill.PackList"/>)에도 저절로 실린다.</remarks>
internal static class Part1TurnCall
{
    /// <summary>파트1 Chr 번호(<see cref="Part1Game.ChrBase"/> 를 뺀 것) → 파트1 Snd 번호들.</summary>
    internal static readonly IReadOnlyDictionary<int, int[]> Calls = new Dictionary<int, int[]>
    {
        [65] = [124],   // 크리스티앙 — 필드 0094 「좋아, 좋아. 그럼 열심히 하자구!」(207 [340, 13, 124])
        [10] = [1107],  // 셰라자드 — 필드 0022 사건 3 「살라딘님!」(208 [311, 16, 1107], 사용자 요청 fg-45)
    };

    /// <summary>그 유닛의 차례 목소리(게임 안 소리 번호 = <see cref="Part1Game.ObsBase"/> + Snd) — 없으면 null.</summary>
    internal static int[]? For(UnitState u) =>
        u.ChrCode > Part1Game.ChrBase && Calls.TryGetValue(u.ChrCode - Part1Game.ChrBase, out var snd)
            ? [.. snd.Select(n => Part1Game.ObsBase + n)] : null;

    /// <summary>에셋 팩에 실을 파트1 Snd 번호.</summary>
    internal static IEnumerable<int> Sounds => Calls.Values.SelectMany(s => s);
}

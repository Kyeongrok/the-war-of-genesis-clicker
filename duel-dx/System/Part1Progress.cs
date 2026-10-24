namespace DuelDx;

/// <summary>
/// 파트1 진행 — 파트2 파티(<c>_party</c>·모세스 동료)와 따로 든다. 일행(인물 번호 = <see cref="WarOfGenesis.Assets.Part1Game.ChrBase"/> + 파트1 번호) · 지금 챕터 · 이긴 전투.
/// </summary>
/// <remarks>
/// 파트1 은 판에 편 4 로 나온 인물이 그대로 일행이 된다고 보고(가설 — 합류 명령은 챕터 사건 쪽, 아직 안 풀었다), 판을 세울 때 들인다.
/// 다음 판에서 Btl 에 없는 일행은 그 판의 배치 칸(절 C)에 서고, 파트2 배치 단계로 고쳐 세운다. 세이브에는 아직 안 적는다(챕터·연대표 단계에서).
/// </remarks>
internal sealed class Part1Progress
{
    /// <summary>시험용 DUELDX_PART1PARTY=「0,1,5」 — 파트1 번호로 일행을 미리 채운다(판을 이어 열 챕터 흐름이 아직 없어서).</summary>
    internal readonly List<int> Party = [.. (Environment.GetEnvironmentVariable("DUELDX_PART1PARTY") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out int n) ? WarOfGenesis.Assets.Part1Game.ChrBase + n : -1).Where(n => n >= 0)];

    /// <summary>지금 챕터(<see cref="WarOfGenesis.Assets.Part1Chapter"/> 번호) — 전투를 마치면 이 챕터 지도로 돌아온다. 없으면 -1.</summary>
    internal int ChapterId = -1;

    /// <summary>이긴 전투(파트1 번호) — 챕터 지도에 ✓ 로 보인다.</summary>
    internal readonly HashSet<int> Cleared = [];

    /// <summary>챕터 사건 발동 수 — 챕터 → (사건 → 횟수)(<see cref="Part1SceneScript"/>). 지도로 돌아와도 들머리가 다시 안 나오게.</summary>
    internal readonly Dictionary<int, Dictionary<int, int>> Fired = [];

    internal Dictionary<int, int> ChapterFired(int chapterId) =>
        Fired.TryGetValue(chapterId, out var fired) ? fired : Fired[chapterId] = [];

    /// <summary>새 게임 — 일행·챕터·이긴 전투·사건 발동 수를 비운다.</summary>
    internal void Reset()
    {
        Party.Clear();
        Cleared.Clear();
        Fired.Clear();
        ChapterId = -1;
    }

    /// <summary>판에 편 4 로 선 인물을 일행에 들인다(차례 유지, 겹치지 않게).</summary>
    internal void Join(IEnumerable<int> chrCodes)
    {
        foreach (int chr in chrCodes)
            if (!Party.Contains(chr)) Party.Add(chr);
    }
}

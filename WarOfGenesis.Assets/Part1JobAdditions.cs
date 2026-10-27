namespace WarOfGenesis.Assets;

/// <summary>
/// 원본에 없는 파트1 직업 어빌리티 — <c>Job.dat</c> 을 읽은 뒤 직업이 가르치는 칸에 덧붙인다(사용자 요청).
/// </summary>
/// <remarks>
/// <c>Job.dat</c> 은 사용자의 파트1 폴더에서 읽으므로 고치지 않는다. 원본 칸은 14개(<see cref="Part1Job.ParseAll"/>)지만 리메이크는 20개까지 더한다(사용자 요청) — 넘치면 더하지 않는다.
/// 이미 가르치는 어빌리티면 최대 레벨만 그 값으로 올린다. 레벨 0 이면 그 직업에서 뺀다.
/// </remarks>
public static class Part1JobAdditions
{
    /// <summary>직업 번호 → (어빌리티, 최대 레벨) 들.</summary>
    private static readonly Dictionary<int, (int Ability, int Level)[]> Adds = new()
    {
        [19] = [(47, 1), (3, 3)],   // 돌격기사 — 힐 1(나이트 · 로열가드처럼 1 까지) · SKILL 3(원본은 안 가르친다, 사용자 요청 fg-55)
        [48] = [(47, 1), (63, 1)],   // 물리학자 — 힐 1 · 잠긴상자열기 1(사용자 요청 fg-56 · fg-57)
        [20] = [(26, 1)],   // 전투기사 — 혼 1(사용자 요청). 투사(6)는 혼 5 까지 가르친다
        [78] = [(119, 0), (33, 1)],   // 무슬림 — 소환 능력을 빼고 화이어 애로우 1(사용자 요청). 나이트 · 마하즈리다처럼 1 까지
    };

    private const int MaxSlots = 20;

    public static IReadOnlyList<Part1Job> Apply(IReadOnlyList<Part1Job> jobs) =>
        [.. jobs.Select(job => Adds.TryGetValue(job.Id, out var adds) ? job with { Abilities = Merge(job.Abilities, adds) } : job)];

    private static (int Ability, int Level)[] Merge((int Ability, int Level)[] abilities, (int Ability, int Level)[] adds)
    {
        var list = abilities.ToList();
        foreach (var (ability, level) in adds)
        {
            int at = list.FindIndex(a => a.Ability == ability);
            if (level <= 0) { if (at >= 0) list.RemoveAt(at); }
            else if (at >= 0) list[at] = (ability, Math.Max(list[at].Level, level));
            else if (list.Count < MaxSlots) list.Add((ability, level));
        }
        return [.. list];
    }
}

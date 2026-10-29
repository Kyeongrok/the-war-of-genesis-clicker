using System.IO;
using System.Text.Json;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 진행 — 파트2 파티(<c>_party</c>·모세스 동료)와 따로 든다. 일행(인물 번호 = <see cref="WarOfGenesis.Assets.Part1Game.ChrBase"/> + 파트1 번호) · 지금 챕터 · 이긴 전투.
/// </summary>
/// <remarks>
/// 파트1 은 판에 편 4 로 나온 인물이 그대로 일행이 된다고 보고(가설 — 합류 명령은 챕터 사건 쪽, 아직 안 풀었다), 판을 세울 때 들인다.
/// 다음 판에서 Btl 에 없는 일행은 그 판의 배치 칸(절 C)에 서고, 파트2 배치 단계로 고쳐 세운다.
/// 챕터 지도를 열 때마다 <c>part1.json</c>(저장 폴더)에 적어 두고, 파트1 타이틀의 CONTINUE 가 그 챕터에서 이어 간다(파트2 세이브 슬롯과 따로).
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

    /// <summary>끝낸 챕터(챕터 사건 501) — 연대표에 ✓.</summary>
    internal readonly HashSet<int> FinishedChapters = [];

    /// <summary>챕터 사건 발동 수 — 챕터 → (사건 → 횟수)(<see cref="Part1SceneScript"/>). 지도로 돌아와도 들머리가 다시 안 나오게.</summary>
    internal readonly Dictionary<int, Dictionary<int, int>> Fired = [];

    internal Dictionary<int, int> ChapterFired(int chapterId) =>
        Fired.TryGetValue(chapterId, out var fired) ? fired : Fired[chapterId] = [];

    /// <summary>새 게임 — 일행·챕터·이긴 전투·사건 발동 수를 비운다.</summary>
    internal void Reset()
    {
        Party.Clear();
        Cleared.Clear();
        FinishedChapters.Clear();
        Fired.Clear();
        ChapterId = -1;
        PartyPlace = 0;
        PendingActions = [];
        Chain = null;
        Fight = null;
        Grown = [];
        LevelPicks = [];
        Stocks = [];
        Equipped = [];
        Looks = [];
        PlaceBattles = [];
        PlaceTowns = [];
        MapTurns = [];
        ChapterMarkers = [];
        ChapterVars = [];
        Riding = [];
        ChapterTeams = [];
        SelectedTeam = [];
        ChapterMarkerPrev = [];
        Home = [];
        Formed = [];
        _homeVersion++;
    }

    /// <summary>인물마다 자란 것 — 누적 경험치와 어빌리티 레벨(레벨업으로 올린 것). 인물 레코드에 덮어 쓴다(<see cref="Apply"/>).</summary>
    internal sealed class Growth
    {
        public int Experience { get; set; }
        public Dictionary<int, int> Abilities { get; set; } = [];

        /// <summary>바꾼 직업(Job.dat 번호, 인물 칸 7) — 0 이면 레코드 그대로.</summary>
        public int Job { get; set; }

        /// <summary>캐릭터 에디터로 더하고 뺀 능력치 — 인물 칸 번호(<see cref="Part1Chr.Str"/> 20 ~ <see cref="Part1Chr.Mr"/> 27) → 더할 값(사용자 요청 menu-102).</summary>
        public Dictionary<int, int> Stats { get; set; } = [];

        internal Growth Copy() => new() { Experience = Experience, Abilities = new(Abilities), Job = Job, Stats = new(Stats) };
    }

    internal Dictionary<int, Growth> Grown = [];

    /// <summary>성장 · 고를 레벨업을 통째로 떠 둔다 — RESTART 가 전투 전으로 되돌린다(<see cref="SystemMenu.RestartBattle"/>).</summary>
    internal (Dictionary<int, Growth> Grown, List<(int Chr, int Level)> Picks) SnapshotGrowth() =>
        (Grown.ToDictionary(g => g.Key, g => g.Value.Copy()), [.. LevelPicks]);

    internal void RestoreGrowth((Dictionary<int, Growth> Grown, List<(int Chr, int Level)> Picks) shot)
    {
        Grown = shot.Grown.ToDictionary(g => g.Key, g => g.Value.Copy());
        LevelPicks = [.. shot.Picks];
    }

    /// <summary>고를 레벨업(인물, 오른 레벨) — 전투에서 쌓이고 챕터 지도에서 하나씩 고른다(<see cref="Part1ChapterScreen"/>).</summary>
    internal List<(int Chr, int Level)> LevelPicks = [];

    /// <summary>
    /// 그 인물이 올릴 수 있는 어빌리티 — 직업 표의 후보(<see cref="Part1Job.LevelUpCandidates"/>). 어빌리티 칸은 10 개라 다 찼으면 이미 배운 것을 올리는 것만.
    /// </summary>
    internal List<(int Ability, int NewLevel, string Name)> PickCandidates(int chrCode)
    {
        if (Part1Chr.Load(chrCode - Part1Game.ChrBase) is not { } raw) return [];
        var chr = Apply(raw);
        var abilities = Part1Ability.LoadAll();
        // 원본(0x1009dac0)은 배운 어빌리티를 번호별 칸(+0x8c + 번호, 0xff = 안 배움)에 두어 개수 제한이 없다 — 예전 10개 제한 때문에
        // 10개를 배운 버몬트에게 직업의 새 어빌리티(갑옷 장착 · HP · STR …)가 후보로 안 떴다(사용자 보고).
        return Part1Job.ById(Part1Job.LoadAll(), chr.JobId)?.LevelUpCandidates(chr)
                   .Select(c => (c.Ability, c.NewLevel, Part1Ability.ById(abilities, c.Ability)?.Name ?? $"어빌리티 {c.Ability}")).ToList() ?? [];
    }

    /// <summary>고른 어빌리티를 올린다.</summary>
    internal void Pick(int chrCode, int ability, int newLevel)
    {
        if (Part1Chr.Load(chrCode - Part1Game.ChrBase) is not { } raw) return;
        GrowthOf(chrCode, raw.Experience).Abilities[ability] = newLevel;
    }

    /// <summary>
    /// 용병 · 탑승물 — Chr → 수(챕터 사건 908 · 910, 마을 용병 길드). 원본은 무리마다 (Chr, 수) 칸 32개(+0x910 · +0xa14)를 들고
    /// 출전 배치 창이 「용병 배치」 · 「탑승」으로 꺼내 쓴다. 리메이크는 용병을 배치 창 「용병 배치」(Part1MercDeploy)로 고른 인물 둘레 3×3 의 부하로 세우고,
    /// 탑승물은 배치 창 「탑승」(Part1MountDeploy)이나 일행 창에서 정한 사람(<see cref="Riding"/>)이 타고 선다. 원본처럼 지금 고른(들어간) 팀의 것이다(<see cref="ActiveStock"/>).
    /// 시험용 DUELDX_PART1MERCS=「580:3,587:2」(파트1 번호:수)로 무리마다 처음 용병을, DUELDX_PART1MOUNTS=「473:2」로 탑승물을 미리 채운다.
    /// </summary>
    internal Dictionary<int, int> Mercenaries { get => ActiveStock.Mercenaries; set => ActiveStock.Mercenaries = value; }

    internal Dictionary<int, int> Mounts { get => ActiveStock.Mounts; set => ActiveStock.Mounts = value; }

    private static readonly Dictionary<int, int> TestMercs = TestCounts("DUELDX_PART1MERCS"), TestMounts = TestCounts("DUELDX_PART1MOUNTS");

    private static Dictionary<int, int> TestCounts(string variable) => (Environment.GetEnvironmentVariable(variable) ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(':'))
        .Where(p => p.Length == 2 && int.TryParse(p[0], out _) && int.TryParse(p[1], out int c) && c > 0)
        .GroupBy(p => WarOfGenesis.Assets.Part1Game.ChrBase + int.Parse(p[0])).ToDictionary(g => g.Key, g => g.Sum(p => int.Parse(p[1])));

    private static readonly List<int> TestBag = [.. (Environment.GetEnvironmentVariable("DUELDX_PART1BAG") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out int n) ? n : 0).Where(n => n > 0)];

    /// <summary>이야기 무리 · 챕터 팀 하나의 짐 — 아이템(+0x10c) · 용병(+0x910) · 탑승물(+0xa14). 돈(0x101a07e0[무리])은 무리에만 있다(팀 짐의 <see cref="Eld"/> 는 안 쓴다).</summary>
    internal sealed class Stock
    {
        public List<int> Bag { get; set; } = [];
        public Dictionary<int, int> Mercenaries { get; set; } = [];
        public Dictionary<int, int> Mounts { get; set; } = [];
        public int Eld { get; set; }

        internal bool IsEmpty => Bag.Count == 0 && Mercenaries.Values.All(v => v <= 0) && Mounts.Values.All(v => v <= 0);

        /// <summary>
        /// 다른 짐을 통째로 이리 더한다(<paramref name="cap"/> 이 있으면 칸마다 그 수까지 — 넘는 것은 <paramref name="from"/> 에 남는다).
        /// 504 · 506 이 무리 → 팀 0 을 99 로 잘라 옮기고(0x100c0620), 챕터 끝이 팀 → 무리를 자르지 않고 되돌린다(0x100ae560).
        /// </summary>
        internal void TakeAll(Stock from, int cap = int.MaxValue)
        {
            if (ReferenceEquals(from, this)) return;
            foreach (int id in from.Bag.Distinct().ToList())
            {
                int move = Math.Min(from.Bag.Count(b => b == id), Math.Max(0, cap - Bag.Count(b => b == id)));
                for (int k = 0; k < move; k++) { from.Bag.Remove(id); Bag.Add(id); }
            }
            Merge(Mercenaries, from.Mercenaries, cap);
            Merge(Mounts, from.Mounts, cap);
        }

        private static void Merge(Dictionary<int, int> to, Dictionary<int, int> from, int cap)
        {
            foreach (var (chr, count) in from.ToList())
            {
                int move = Math.Min(count, Math.Max(0, cap - to.GetValueOrDefault(chr)));
                if (move <= 0) continue;
                to[chr] = to.GetValueOrDefault(chr) + move;
                if (count - move > 0) from[chr] = count - move; else from.Remove(chr);
            }
        }
    }

    /// <summary>
    /// 이야기 무리 → 짐. 원본은 무리 16칸(0x101a0828, 0xaa8 꼴)과 무리 돈(0x101a07e0 ×16)을 따로 들고, 챕터 행동 906 · 908 · 910 · 912 · 처치 보상 · 상점이
    /// 모두 지금 챕터의 무리(머리 낱말 1)에 쓴다(팀 0~7 도 챕터 끝에 그 무리로 돌아간다, 0x100ae560). 새 게임(0x100ad4c0)만 모두 비우고,
    /// 연대표에서 다른 분기를 열 때는 비우지 않는다 — 그 분기의 무리 짐이 보일 뿐이다.
    /// 예전엔 짐 하나를 모든 분기가 같이 써서 크림슨 크루세이더 분기(무리 2) 챕터 21 에 시반 슈미터 분기(무리 1)의 아지다하카가 떴다(사용자 보고).
    /// </summary>
    internal Dictionary<int, Stock> Stocks = [];

    /// <summary>지금 챕터 무리의 짐(무리가 없으면 0 칸) — 교환 창의 「본  부」 쪽이다.</summary>
    internal Stock CurrentStock => StockOf(CurrentGroup);

    /// <summary>
    /// 지금 쓰는 짐 — 이 챕터에 팀이 있으면 고른(지도에서 전투 · 마을에 들어간, 원본 +0x5578) 팀의 짐, 없으면 무리 짐.
    /// 원본은 팀마다 아이템 · 용병 · 탑승물을 따로 들어(챕터 +0x38 팀 8칸 × 0xaa8) 전투(0x10032f50 이 그 팀을 전투 +0xaea0 으로 베낀다) ·
    /// 배치 창의 용병 배치 · 탑승 · 마을 상점(0x100cafdf) · 상태 창 아이템이 모두 그 팀 것을 쓴다. 돈만 무리 것이다(<see cref="Eld"/>).
    /// </summary>
    internal Stock ActiveStock => ActiveTeam?.Stock ?? CurrentStock;

    /// <summary>지금 챕터에서 고른 팀(팀이 없으면 null).</summary>
    internal Part1Team? ActiveTeam =>
        ChapterId >= 0 && ChapterTeams.TryGetValue(ChapterId, out var teams) && teams.Count > 0
            ? teams[Math.Clamp(SelectedTeam.GetValueOrDefault(ChapterId), 0, teams.Count - 1)] : null;

    /// <summary>
    /// 행동 906 · 908 · 910 [무리, …] 이 쓸 짐 — 99 = 이야기 무리(본부), 0~7 = 지금 챕터의 그 팀(0x100c14f0 · 0x100c1610 · 0x100c1730 이 0x63 으로 갈래).
    /// 팀이 아직 없으면 만들어 둔다(원본도 팀 수를 늘리지 않고 그 칸에 적는다 — 챕터 0 의 906 [0, …] 은 504 앞에 있다).
    /// </summary>
    internal Stock ScriptStock(int target)
    {
        if (target is < 0 or > 7 || ChapterId < 0) return CurrentStock;
        if (!ChapterTeams.TryGetValue(ChapterId, out var teams)) ChapterTeams[ChapterId] = teams = [];
        while (teams.Count <= target) teams.Add(new Part1Team { Place = 0 });
        return teams[target].Stock;
    }

    /// <summary>
    /// 팀을 꾸린 챕터 — 원본 팀 수(+0x557e) ≠ 0 에 맞선다. 리메이크는 챕터를 열 때부터 팀 0(무리 앞 10명)을 두므로 「원본에도 팀이 있나」를 따로 든다:
    /// 504 · 506 이 옮겼거나 904 가 팀에 사람을 넣었거나 교환 창으로 팀을 꾸렸으면 켠다. 안 켜진 채 지도가 손에 들어오면 원본처럼 팀 편성 교환 창을 연다(0x100b3f10).
    /// </summary>
    internal HashSet<int> Formed = [];

    /// <summary>
    /// 504 · 506(0x100c0620 · 0x100c0a60) — 팀을 아직 안 꾸렸고 머리 낱말 2 == 1 이면 무리 짐을 칸마다 99 까지 팀 0 으로 옮긴다(사람은 리메이크의 팀 0 = 무리 앞 10명이라 할 일이 없다).
    /// </summary>
    internal void FormTeamZero()
    {
        if (ChapterId < 0 || Formed.Contains(ChapterId)) return;
        if (WarOfGenesis.Assets.Part1Chapter.Load(ChapterId) is not { Header.Length: > 2 } chapter || chapter.Header[2] != 1) return;
        ScriptStock(0).TakeAll(CurrentStock, 99);
        Formed.Add(ChapterId);
    }

    internal Stock StockOf(int group) =>
        Stocks.TryGetValue(group, out var s) ? s : Stocks[group] = new Stock { Bag = [.. TestBag], Mercenaries = new(TestMercs), Mounts = new(TestMounts) };

    /// <summary>
    /// 챕터 끝(0x100ae560) — 원본은 팀 짐을 무리에 되돌린 뒤 무리 용병을 머릿수 × 100 eld 로 바꾸고(0x100ae714) 용병 수 · 탑승물 수(+0x90c · +0xa10)를 0 으로 한다.
    /// 그래서 용병 · 탑승물은 챕터를 넘어가지 않고(시반 슈미터 분기 챕터마다 910 이 아지다하카를 새로 준다), 아이템 · 돈만 무리에 남는다.
    /// </summary>
    internal void CloseChapter()
    {
        var stock = CurrentStock;
        // 팀 짐을 무리로 되돌린다(자르지 않는다) — 팀 · 고른 팀 · 꾸림 표도 지운다(다음에 이 챕터를 열면 원본처럼 새 챕터 객체에서 시작).
        if (ChapterId >= 0 && ChapterTeams.Remove(ChapterId, out var teams))
            foreach (var team in teams) stock.TakeAll(team.Stock);
        SelectedTeam.Remove(ChapterId);
        Formed.Remove(ChapterId);
        stock.Eld += stock.Mercenaries.Values.Where(v => v > 0).Sum() * 100;
        stock.Mercenaries.Clear();
        stock.Mounts.Clear();
        foreach (int chr in GroupMembers()) Riding.Remove(chr);
    }

    /// <summary>
    /// 탈 사람 → 탈것(둘 다 20000 + 파트1 번호) — 배치 창의 「탑승」(원본 칸 +0x68, Part1MountDeploy)이나 일행 창에서 정한다. 원본은 판마다 새로 고르지만 리메이크는 적어 두고 다음 판도 태운다. 전투에서는 탈것으로 서고
    /// 탄 이는 판 밖에 있다가 탈것이 쓰러지면 그 칸에 내려선다(<see cref="BattleScene"/> 의 _part1Riders).
    /// </summary>
    internal Dictionary<int, int> Riding = [];

    /// <summary>
    /// 챕터 팀(원본 챕터 +0x38, 8개까지 · 분석-파트1-챕터 10.4) — 챕터 → 팀들. 팀 0 은 다른 팀에 안 든 일행 모두, 팀 1 부터는 904 [팀, Chr] 가 옮긴 사람들.
    /// 팀마다 지도 자리가 있고 「N팀」 단추로 고른 팀이 움직이고 싸운다(<see cref="SelectedTeam"/>).
    /// </summary>
    internal Dictionary<int, List<Part1Team>> ChapterTeams = [];

    /// <summary>챕터 → 고른 팀(원본 장면 +0x13c04).</summary>
    internal Dictionary<int, int> SelectedTeam = [];

    /// <summary>
    /// 지금 싸우러 나가는 사람 — 지금 챕터에서 고른 팀(팀 0 이면 다른 팀에 안 든 일행, 팀 N 이면 그 팀 사람). 팀이 없으면 일행 모두.
    /// </summary>
    internal List<int> ActiveMembers() => TeamMembers(SelectedTeam.GetValueOrDefault(ChapterId));

    /// <summary>지금 챕터의 그 팀 사람 — 팀 0 은 다른 팀에 안 든 무리 앞 10명, 팀 N 은 그 팀에 옮긴 사람(무리 차례). 팀이 없으면 무리 앞 10명.</summary>
    internal List<int> TeamMembers(int team)
    {
        var group = GroupMembers();
        if (!ChapterTeams.TryGetValue(ChapterId, out var teams) || teams.Count == 0 || teams.Count == 1 && !teams[0].Fixed) return [.. group.Take(TeamSize)];
        team = Math.Clamp(team, 0, teams.Count - 1);
        if (team > 0 || teams[0].Fixed) return [.. group.Where(teams[team].Members.Contains)];
        return [.. PoolMembers().Take(TeamSize)];
    }

    /// <summary>본부 — 팀 1 부터에 안 든 무리 사람 모두(팀 0 은 이 앞 10명). 교환 창의 「본  부」 쪽.</summary>
    internal List<int> PoolMembers()
    {
        var group = GroupMembers();
        if (!ChapterTeams.TryGetValue(ChapterId, out var teams) || teams.Count == 0 || teams.Count == 1 && !teams[0].Fixed) return group;
        var elsewhere = teams.Skip(teams[0].Fixed ? 0 : 1).SelectMany(t => t.Members).ToHashSet();
        return [.. group.Where(c => !elsewhere.Contains(c))];
    }

    /// <summary>팀 하나에 들 수 있는 사람 수(원본 교환 창 0x100b87d0 — 받는 쪽이 팀이면 10명까지).</summary>
    internal const int TeamLimit = TeamSize;

    /// <summary>504 · 506 이 팀 0 으로 옮기는 수 — 무리 앞 10명(Chr 번호 차례, 0x100c0620).</summary>
    private const int TeamSize = 10;

    /// <summary>
    /// 이야기 무리(원본 16개, 0x101a0828 · 분석-파트1-챕터 12절) — 인물마다 「집 무리」. 처음 값은 인물 레코드 칸 8(파일 0x42), 행동 1000 [Chr, 8, 무리] 가 바꾼다.
    /// 무리 1 살라딘 · 마르자나 · 무카파 · 발라, 2 버몬트 · 록슬리 · 깁슨, 3 크리스티앙 · 시리우스 … — 챕터 머리 낱말 1 이 지금 무리다.
    /// </summary>
    internal Dictionary<int, int> Home = [];

    private static Dictionary<int, int>? _homeDefaults;

    internal int HomeOf(int chr)
    {
        if (Home.TryGetValue(chr, out int g)) return g;
        _homeDefaults ??= [];
        lock (_homeDefaults)
        {
            if (!_homeDefaults.TryGetValue(chr, out g))
                _homeDefaults[chr] = g = WarOfGenesis.Assets.Part1Chr.Load(chr - WarOfGenesis.Assets.Part1Game.ChrBase) is { } c ? c.Fields[8] : 0;
        }
        return g;
    }

    /// <summary>지금 챕터의 무리(머리 낱말 1) — 0 이면 무리 없음(일행 목록을 쓴다).</summary>
    internal int CurrentGroup
    {
        get
        {
            if (_groupOf is { } known && known.Chapter == ChapterId) return known.Group;
            int g = ChapterId >= 0 && WarOfGenesis.Assets.Part1Chapter.Load(ChapterId) is { Header.Length: > 1 } c ? c.Header[1] : 0;
            _groupOf = (ChapterId, g);
            return g;
        }
    }

    private (int Chapter, int Group)? _groupOf;

    /// <summary>지금 무리의 사람 — 인물 0~105(원본이 무리를 채우는 범위)와 일행에 든 사람 가운데 집 무리가 지금 무리인 것, Chr 번호 차례. 무리가 없으면 일행.</summary>
    internal List<int> GroupMembers()
    {
        int g = CurrentGroup;
        if (g <= 0) return Party;
        if (_groupCache is { } cached && cached.Group == g && cached.Version == _homeVersion) return cached.Members;
        var members = GroupMembers(g);
        _groupCache = (g, _homeVersion, members);
        return members;
    }

    /// <summary>그 무리의 사람 — 인물 0~105 와 일행 가운데 집 무리가 그것인 사람, Chr 번호 차례(전투 절 D 의 무리 칸도 이 차례로 꺼낸다).</summary>
    internal List<int> GroupMembers(int group) =>
        [.. Enumerable.Range(WarOfGenesis.Assets.Part1Game.ChrBase, 106).Concat(Party).Distinct().Where(c => HomeOf(c) == group).OrderBy(c => c)];

    private (int Group, int Version, List<int> Members)? _groupCache;
    private int _homeVersion;

    /// <summary>행동 1000 [Chr, 8, 무리] — 그 사람의 집 무리를 바꾼다(0x100c1890).</summary>
    internal void MoveToGroup(int chr, int group)
    {
        Home[chr] = group;
        _homeVersion++;
        Join([chr]);
    }

    /// <summary>우리 편 사람인가(지금 무리 · 일행) — 아니면 용병.</summary>
    internal bool IsOurs(int chr) => Party.Contains(chr) || GroupMembers().Contains(chr);

    /// <summary>챕터 사건 1201 이 바꾼 장소 — 「챕터:장소」 → 전투 번호(장소를 그 전투 장소로 만든다).</summary>
    internal Dictionary<string, int> PlaceBattles = [];

    /// <summary>챕터 사건 1200 이 바꾼 장소 — 「챕터:장소」 → 마을 번호(장소를 그 마을 장소로 만든다).</summary>
    internal Dictionary<string, int> PlaceTowns = [];

    /// <summary>챕터 지도 턴(원본 +0x557c — 「턴 종료」마다 +1, 조건 1 이 본다) — 챕터 → 턴.</summary>
    internal Dictionary<int, int> MapTurns = [];

    /// <summary>챕터마다 지도 말 자리(900 이 옮긴 것)와 장면 변수(800 · 101) — 지도를 다시 열어도 · 이어 하기에도 그대로.</summary>
    internal Dictionary<int, List<int>> ChapterMarkers = [];

    /// <summary>챕터마다 지도 말의 앞 장소(900 이 옮길 때 옛 장소) — 조건 201(막 닿음) · 303(머무름)이 본다. 전투 · 필드에 다녀와도 그대로.</summary>
    internal Dictionary<int, List<int>> ChapterMarkerPrev = [];
    internal Dictionary<int, Dictionary<int, int>> ChapterVars = [];

    internal Dictionary<int, int> VarsOf(int chapterId) =>
        ChapterVars.TryGetValue(chapterId, out var vars) ? vars : ChapterVars[chapterId] = [];

    /// <summary>번 돈(eld) — 처치 보상(<see cref="Part1Combat.KillReward"/>). 원본처럼 팀이 아니라 지금 챕터 무리의 돈이다(0x101a07e0[머리 낱말 1], 상태 창 소지금도 이것).</summary>
    internal int Eld { get => CurrentStock.Eld; set => CurrentStock.Eld = value; }

    /// <summary>그 인물의 자란 것 — 없으면 레코드의 경험치로 새로 만든다.</summary>
    internal Growth GrowthOf(int chrCode, int baseExperience) =>
        Grown.TryGetValue(chrCode, out var g) ? g : Grown[chrCode] = new Growth { Experience = baseExperience };

    /// <summary>인물 레코드에 자란 것을 입힌다(GameDatabase.Part1Growth).</summary>
    internal Part1Chr Apply(Part1Chr chr)
    {
        int code = Part1Game.ChrBase + chr.Code;
        if (Grown.TryGetValue(code, out var g))
        {
            chr = chr.WithGrowth(g.Experience, g.Abilities);
            if (g.Job > 0) { var f = (int[])chr.Fields.Clone(); f[7] = g.Job; chr = chr with { Fields = f }; }
            if (g.Stats is { Count: > 0 })
            {
                var f = (int[])chr.Fields.Clone();
                foreach (var (field, add) in g.Stats) if ((uint)field < f.Length) f[field] = Math.Max(0, f[field] + add);
                chr = chr with { Fields = f };
            }
        }
        if (Equipped.TryGetValue(code, out var slots)) chr = chr.WithEquipment(slots);
        // 그림 · 반신 · 얼굴은 따로 — 「초상」만 바꾼 인물(menu-101)은 그림이 0 이다.
        if (Looks.TryGetValue(code, out var look) && (look.Sprite > 0 || look.Bust > 0 || look.Face > 0))
        {
            var f = (int[])chr.Fields.Clone();
            if (look.Sprite > 0) f[3] = look.Sprite;
            if (look.Bust > 0) f[4] = look.Bust;
            if (look.Face > 0) f[5] = look.Face;
            chr = chr with { Fields = f };
        }
        return chr;
    }

    /// <summary>
    /// 캐릭터 에디터 「외형」이 바꾼 모습(사용자 요청 menu-97) — 다른 파트1 인물 레코드에서 빌린 칸 3 전투 그림 · 칸 4 반신 · 칸 5 얼굴(파트1 번호, 그림 0 = 레코드 그대로)과
    /// 파트2 목소리 묶음(Dmg.dat 번호, 0 = 없음 — 파트1 에는 목소리 묶음이 없다).
    /// </summary>
    internal sealed class Look
    {
        public int Sprite { get; set; }
        public int Bust { get; set; }
        public int Face { get; set; }
        public int Voice { get; set; }
        /// <summary>파트2 인물 모습(menu-100) — 파트2 Obs · 얼굴 번호. 0 이면 없음(그때는 위 파트1 칸을 본다).</summary>
        public int Part2Sprite { get; set; }
        public int Part2Face { get; set; }
    }

    /// <summary>인물마다 바꾼 외형 — 없으면 인물 레코드 그대로.</summary>
    internal Dictionary<int, Look> Looks = [];

    internal Look LookOf(int chrCode) => Looks.TryGetValue(chrCode, out var l) ? l : Looks[chrCode] = new Look();

    /// <summary>그 인물의 목소리 묶음(GameDatabase.Part1Voice) — 안 바꿨으면 0.</summary>
    // 파트2 원본이 없는 PC 에서는 입혀 둔 파트2 모습을 안 쓴다 — 제 파트1 모습으로 선다(menu-109).
    internal (ushort Sprite, ushort Face) Part2LookOf(int chrCode) =>
        !OriginalGameSetup.Part1Only && Looks.TryGetValue(chrCode, out var l) ? ((ushort)Math.Max(0, l.Part2Sprite), (ushort)Math.Max(0, l.Part2Face)) : ((ushort)0, (ushort)0);

    /// <summary>파트1 화면(상태 · 일행)이 그릴 얼굴 Obs — 파트2 초상을 입혔으면 그 번호, 아니면 파트1 얼굴(20000 + 칸 5). 없으면 0.</summary>
    internal int FaceObs(Part1Chr chr) =>
        !OriginalGameSetup.Part1Only && Looks.TryGetValue(Part1Game.ChrBase + chr.Code, out var l) && l.Part2Face > 0 ? l.Part2Face : chr.FaceId > 0 ? Part1Game.ObsBase + chr.FaceId : 0;

    internal ushort VoiceOf(int chrCode) => Looks.TryGetValue(chrCode, out var l) ? (ushort)Math.Clamp(l.Voice, 0, ushort.MaxValue) : (ushort)0;

    /// <summary>가방 — 산 아이템(Item.dat 번호, 같은 것이 여럿이면 여러 번). 지금 고른 팀의 것(<see cref="ActiveStock"/>). 시험용 DUELDX_PART1BAG=「126,126」 으로 무리마다 미리 채운다.</summary>
    internal List<int> Bag { get => ActiveStock.Bag; set => ActiveStock.Bag = value; }

    /// <summary>인물마다 바꿔 낀 장비 다섯 칸 — 없으면 인물 레코드 그대로.</summary>
    internal Dictionary<int, int[]> Equipped = [];

    /// <summary>일행이 선 챕터 지도 장소(차례).</summary>
    internal int PartyPlace;

    /// <summary>챕터 사건의 남은 행동 — 전투·필드에 갔다 오면 이어 돌 것(세이브에 같이 적는다).</summary>
    internal List<ScriptCommand> PendingActions = [];

    /// <summary>
    /// 사슬 자리 — 챕터 사건이 연 장면(남은 행동 머리의 502 · 503 · 900)이 다음 장면으로 이어졌을 때(전투 101 → 전투 · 104 → 필드 · 필드 502 → 전투 · 501 → 필드)
    /// 지금 가 있는 장면 [갈래(1 전투 · 2 필드), 번호]. 챕터로 돌아오면 지운다. 있으면 남은 행동 머리는 이미 지난 것 —
    /// 이어 하기는 이 장면부터 열고, 전투 세이브는 머리를 빼고 잇는다(분석-파트1-챕터 15절). 원본은 사슬 안에서 세이브가 장면 번호를 적는다.
    /// </summary>
    internal int[]? Chain;

    /// <summary>
    /// 지도에서 연 전투 — [챕터, 갈래, 말(0부터, 없으면 −1), 장소]. 원본 장면 4 의 돌아온 갈래 <c>+0x13cac</c>(1 진입 0x100b0ddb · 2 팀이 말에게 0x100b131a ·
    /// 3 말이 팀에게 900 0x100c117d)다. 이기고 챕터로 돌아오면(<see cref="Part1ChapterScreen.SettleFight"/>) 그 장소의 전투를 지우고 말을 쓰러뜨린다(0x100b4de0).
    /// </summary>
    internal int[]? Fight;

    internal static string SavePath => UserDataFolder.File("part1.json");

    internal static bool HasSave => File.Exists(SavePath);

    private sealed record SaveData(List<int> Party, int ChapterId, int PartyPlace, List<int> Cleared, List<int> FinishedChapters,
                                   Dictionary<int, Dictionary<int, int>> Fired, string Flags, List<short[]> Pending,
                                   Dictionary<int, Growth>? Grown = null, int Eld = 0, List<int[]>? LevelPicks = null,
                                   List<int>? Bag = null, Dictionary<int, int[]>? Equipped = null,
                                   Dictionary<int, int>? Mercenaries = null, Dictionary<int, int>? Mounts = null,
                                   Dictionary<string, int>? PlaceBattles = null, Dictionary<int, int>? MapTurns = null,
                                   Dictionary<int, List<int>>? ChapterMarkers = null, Dictionary<int, Dictionary<int, int>>? ChapterVars = null,
                                   Dictionary<int, int>? Riding = null,
                                   Dictionary<int, List<Part1Team>>? ChapterTeams = null, Dictionary<int, int>? SelectedTeam = null,
                                   Dictionary<int, List<int>>? ChapterMarkerPrev = null, Dictionary<int, int>? Home = null,
                                   int[]? Chain = null, Dictionary<string, int>? PlaceTowns = null, Dictionary<int, Stock>? Stocks = null,
                                   List<int>? Formed = null, int StockVersion = 0, int[]? Fight = null, Dictionary<int, Look>? Looks = null);

    /// <summary>세이브 짐 꼴 — 1 = 팀마다 짐(<see cref="Part1Team.Stock"/>). 0 은 팀이 무리 짐을 같이 쓰던 세이브.</summary>
    private const int StockVersionNow = 1;

    /// <summary>진행과 진행 깃발(파트2 와 같은 칸)을 적는다. 못 적으면 조용히 넘어간다.</summary>
    internal void Save(byte[] flags)
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder.Path);
            File.WriteAllText(SavePath, ToJson(flags));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>진행 전부를 JSON 한 덩이로 — part1.json 과 전투 세이브(SystemMenu 의 SaveState.Part1)가 같이 쓴다.</summary>
    internal string ToJson(byte[] flags)
    {
        var data = new SaveData(Party, ChapterId, PartyPlace, [.. Cleared], [.. FinishedChapters], Fired, Convert.ToBase64String(flags),
                                [.. PendingActions.Select(c => (short[])[(short)c.Code, .. c.Args])], Grown, 0, [.. LevelPicks.Select(p => (int[])[p.Chr, p.Level])], null, Equipped, null, null, PlaceBattles, MapTurns, ChapterMarkers, ChapterVars, Riding, ChapterTeams, SelectedTeam, ChapterMarkerPrev, Home, Chain, PlaceTowns, Stocks,
                                [.. Formed], StockVersionNow, Fight, Looks.Count > 0 ? Looks : null);
        return JsonSerializer.Serialize(data);
    }

    /// <summary>적어 둔 진행을 읽어 들인다 — 깃발은 <paramref name="flags"/> 에 덮어쓴다. 없거나 못 읽으면 false.</summary>
    internal bool Load(byte[] flags)
    {
        try { return FromJson(File.ReadAllText(SavePath), flags); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary><see cref="ToJson"/> 로 적은 것을 되살린다 — 깃발은 <paramref name="flags"/> 에 덮어쓴다. 못 읽으면 false.</summary>
    internal bool FromJson(string json, byte[] flags)
    {
        try
        {
            if (JsonSerializer.Deserialize<SaveData>(json) is not { } data) return false;
            Reset();
            Party.AddRange(data.Party);
            ChapterId = data.ChapterId;
            PartyPlace = data.PartyPlace;
            Cleared.UnionWith(data.Cleared);
            FinishedChapters.UnionWith(data.FinishedChapters);
            foreach (var (chapter, events) in data.Fired) Fired[chapter] = new(events);
            var saved = Convert.FromBase64String(data.Flags);
            Array.Clear(flags);
            saved.AsSpan(0, Math.Min(saved.Length, flags.Length)).CopyTo(flags);
            // v1.0.12 전 세이브 — 사이럽스(Btl 0137)를 전멸로 이겨 깃발 63 을 놓쳤으면 켠다(BattleScene.Part1WinFlagsOnWipe, fg-38). 판은 한 번만 열려 다시 못 한다.
            if (Cleared.Contains(137) && flags.Length > 63 && flags[63] == 0) flags[63] = 1;
            PendingActions = [.. data.Pending.Where(w => w.Length > 0).Select(w => new ScriptCommand((ushort)w[0], w[1..]))];
            Grown = data.Grown ?? [];
            LevelPicks = [.. (data.LevelPicks ?? []).Where(p => p.Length == 2).Select(p => (p[0], p[1]))];
            Equipped = data.Equipped ?? [];
            Looks = data.Looks ?? [];   // 외형 칸 전의 세이브에는 없다
            PlaceBattles = data.PlaceBattles ?? [];
            PlaceTowns = data.PlaceTowns ?? [];
            MapTurns = data.MapTurns ?? [];
            ChapterMarkers = data.ChapterMarkers ?? [];
            ChapterVars = data.ChapterVars ?? [];
            Riding = data.Riding ?? [];
            ChapterTeams = data.ChapterTeams ?? [];
            SelectedTeam = data.SelectedTeam ?? [];
            ChapterMarkerPrev = data.ChapterMarkerPrev ?? [];
            Home = data.Home ?? [];
            Chain = data.Chain is [_, _] chain ? chain : null;
            Fight = data.Fight is [_, _, _, _] fight ? fight : null;
            Formed = [.. data.Formed ?? []];
            _homeVersion++;
            if (data.Stocks != null) Stocks = data.Stocks;
            else
            {
                // 무리별 짐 전의 세이브 — 하나로 든 짐을 지금 챕터 무리에 넣는다. 탑승물은 챕터 끝마다 비므로(CloseChapter) 이 챕터의 910 이 준 것까지만 남긴다
                // (예전 세이브에는 다른 줄기 챕터의 아지다하카 · 썬더볼트가 쌓여 있다).
                var stock = Stocks[CurrentGroup] = new Stock { Bag = data.Bag ?? [], Mercenaries = data.Mercenaries ?? [], Eld = data.Eld };
                var granted = (ChapterId >= 0 ? WarOfGenesis.Assets.Part1Chapter.Load(ChapterId)?.Events : null)?.SelectMany(e => e.Actions)
                              .Where(c => c.Code == 910 && c.Args[1] > 0 && c.Args[2] > 0)
                              .GroupBy(c => WarOfGenesis.Assets.Part1Game.ChrBase + c.Args[1]).ToDictionary(g => g.Key, g => g.Sum(c => (int)c.Args[2])) ?? [];
                foreach (var (mount, count) in data.Mounts ?? [])
                    if (Math.Min(count, granted.GetValueOrDefault(mount)) is > 0 and var keep) stock.Mounts[mount] = keep;
            }
            if (data.StockVersion < 1)
            {
                // 팀마다 짐 전의 세이브 — 팀이 무리 짐을 같이 썼다. 이미 연 챕터는 꾸린 것으로 치고, 지금 챕터의 무리 짐은 504 가 한 것처럼 팀 0 에 넘긴다
                // (그 판의 사람들이 쓰던 짐이 그대로 손에 남게; 지난 챕터의 팀 표는 짐이 비어 있다).
                Formed = [.. ChapterTeams.Keys];
                if (ChapterId >= 0 && ChapterTeams.TryGetValue(ChapterId, out var teams) && teams.Count > 0) teams[0].Stock.TakeAll(CurrentStock);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or NotSupportedException) { return false; }
    }

    /// <summary>판에 편 4 로 선 인물을 일행에 들인다(차례 유지, 겹치지 않게).</summary>
    internal void Join(IEnumerable<int> chrCodes)
    {
        foreach (int chr in chrCodes)
            if (!Party.Contains(chr)) Party.Add(chr);
    }
}

/// <summary>챕터 팀 하나 — 사람(팀 1 부터만 적는다, 20000 + 파트1 번호)과 지도 자리(장소 차례).</summary>
internal sealed class Part1Team
{
    public List<int> Members { get; set; } = [];

    /// <summary>
    /// 팀 0 을 옮긴 사람만 드는 팀으로 쓰나 — 평소 팀 0 은 「다른 팀에 안 든 무리 앞 10명」이라 본부와 같은 사람이 보였다. 본부에서 교환 창을 열면 켜서
    /// 본부 ↔ 1팀이 나뉘게 한다(사용자 보고 fg-58: Chp 0023). 팀 0 을 비운 채 닫으면 끈다(빈 팀으로 싸우지 않게). 팀 1 부터는 늘 옮긴 사람만.
    /// </summary>
    public bool Fixed { get; set; }
    public int Place { get; set; }

    /// <summary>이번 턴에 움직였나(원본 팀 칸 +0xaa4) — 「이  동」 · 「진  입」이 켜고 「턴종료」가 끈다. 팀은 턴마다 한 번 움직인다.</summary>
    public bool Moved { get; set; }

    /// <summary>팀의 짐(원본 팀 칸 +0x108 아이템 · +0x90c 용병 · +0xa10 탑승물) — 챕터 끝에 무리로 돌아간다(<see cref="Part1Progress.CloseChapter"/>).</summary>
    public Part1Progress.Stock Stock { get; set; } = new();
}

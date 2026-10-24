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
        Grown = [];
        Eld = 0;
        LevelPicks = [];
        Bag = [];
        Equipped = [];
        Mercenaries = [];
        Mounts = [];
        PlaceBattles = [];
        MapTurns = [];
        ChapterMarkers = [];
        ChapterVars = [];
        Riding = [];
        ChapterTeams = [];
        SelectedTeam = [];
        ChapterMarkerPrev = [];
        Home = [];
        _homeVersion++;
    }

    /// <summary>인물마다 자란 것 — 누적 경험치와 어빌리티 레벨(레벨업으로 올린 것). 인물 레코드에 덮어 쓴다(<see cref="Apply"/>).</summary>
    internal sealed class Growth
    {
        public int Experience { get; set; }
        public Dictionary<int, int> Abilities { get; set; } = [];

        /// <summary>바꾼 직업(Job.dat 번호, 인물 칸 7) — 0 이면 레코드 그대로.</summary>
        public int Job { get; set; }
    }

    internal Dictionary<int, Growth> Grown = [];

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
        return Part1Job.ById(Part1Job.LoadAll(), chr.JobId)?.LevelUpCandidates(chr)
                   .Where(c => c.NewLevel > 1 || chr.Abilities.Length < 10)
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
    /// 출전 배치 창이 「용병 배치」 · 「탑승」으로 꺼내 쓴다. 리메이크는 용병을 배치 명단 끝에 수만큼 세우고, 탑승물은 일행 창에서 정한 사람(<see cref="Riding"/>)이 타고 선다.
    /// </summary>
    internal Dictionary<int, int> Mercenaries = [], Mounts = [];

    /// <summary>
    /// 탈 사람 → 탈것(둘 다 20000 + 파트1 번호) — 일행 창에서 정한다(원본은 출전 배치 창의 「탑승」, 칸 +0x68). 전투에서는 탈것으로 서고
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
    internal List<int> ActiveMembers()
    {
        var group = GroupMembers();
        if (!ChapterTeams.TryGetValue(ChapterId, out var teams) || teams.Count <= 1) return [.. group.Take(TeamSize)];
        int team = Math.Clamp(SelectedTeam.GetValueOrDefault(ChapterId), 0, teams.Count - 1);
        if (team > 0) return [.. group.Where(teams[team].Members.Contains)];
        var elsewhere = teams.Skip(1).SelectMany(t => t.Members).ToHashSet();
        return [.. group.Where(c => !elsewhere.Contains(c)).Take(TeamSize)];
    }

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
        var members = Enumerable.Range(WarOfGenesis.Assets.Part1Game.ChrBase, 106).Concat(Party).Distinct()
                                .Where(c => HomeOf(c) == g).OrderBy(c => c).ToList();
        _groupCache = (g, _homeVersion, members);
        return members;
    }

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

    /// <summary>챕터 지도 턴(원본 +0x557c — 「턴 종료」마다 +1, 조건 1 이 본다) — 챕터 → 턴.</summary>
    internal Dictionary<int, int> MapTurns = [];

    /// <summary>챕터마다 지도 말 자리(900 이 옮긴 것)와 장면 변수(800 · 101) — 지도를 다시 열어도 · 이어 하기에도 그대로.</summary>
    internal Dictionary<int, List<int>> ChapterMarkers = [];

    /// <summary>챕터마다 지도 말의 앞 장소(900 이 옮길 때 옛 장소) — 조건 201(막 닿음) · 303(머무름)이 본다. 전투 · 필드에 다녀와도 그대로.</summary>
    internal Dictionary<int, List<int>> ChapterMarkerPrev = [];
    internal Dictionary<int, Dictionary<int, int>> ChapterVars = [];

    internal Dictionary<int, int> VarsOf(int chapterId) =>
        ChapterVars.TryGetValue(chapterId, out var vars) ? vars : ChapterVars[chapterId] = [];

    /// <summary>번 돈(eld) — 처치 보상(<see cref="Part1Combat.KillReward"/>).</summary>
    internal int Eld;

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
        }
        if (Equipped.TryGetValue(code, out var slots)) chr = chr.WithEquipment(slots);
        return chr;
    }

    /// <summary>가방 — 산 아이템(Item.dat 번호, 같은 것이 여럿이면 여러 번). 시험용 DUELDX_PART1BAG=「126,126」 으로 미리 채운다.</summary>
    internal List<int> Bag = [.. (Environment.GetEnvironmentVariable("DUELDX_PART1BAG") ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out int n) ? n : 0).Where(n => n > 0)];

    /// <summary>인물마다 바꿔 낀 장비 다섯 칸 — 없으면 인물 레코드 그대로.</summary>
    internal Dictionary<int, int[]> Equipped = [];

    /// <summary>일행이 선 챕터 지도 장소(차례).</summary>
    internal int PartyPlace;

    /// <summary>챕터 사건의 남은 행동 — 전투·필드에 갔다 오면 이어 돌 것(세이브에 같이 적는다).</summary>
    internal List<ScriptCommand> PendingActions = [];

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
                                   Dictionary<int, List<int>>? ChapterMarkerPrev = null, Dictionary<int, int>? Home = null);

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
                                [.. PendingActions.Select(c => (short[])[(short)c.Code, .. c.Args])], Grown, Eld, [.. LevelPicks.Select(p => (int[])[p.Chr, p.Level])], Bag, Equipped, Mercenaries, Mounts, PlaceBattles, MapTurns, ChapterMarkers, ChapterVars, Riding, ChapterTeams, SelectedTeam, ChapterMarkerPrev, Home);
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
            PendingActions = [.. data.Pending.Where(w => w.Length > 0).Select(w => new ScriptCommand((ushort)w[0], w[1..]))];
            Grown = data.Grown ?? [];
            Eld = data.Eld;
            LevelPicks = [.. (data.LevelPicks ?? []).Where(p => p.Length == 2).Select(p => (p[0], p[1]))];
            Bag = data.Bag ?? [];
            Equipped = data.Equipped ?? [];
            Mercenaries = data.Mercenaries ?? [];
            Mounts = data.Mounts ?? [];
            PlaceBattles = data.PlaceBattles ?? [];
            MapTurns = data.MapTurns ?? [];
            ChapterMarkers = data.ChapterMarkers ?? [];
            ChapterVars = data.ChapterVars ?? [];
            Riding = data.Riding ?? [];
            ChapterTeams = data.ChapterTeams ?? [];
            SelectedTeam = data.SelectedTeam ?? [];
            ChapterMarkerPrev = data.ChapterMarkerPrev ?? [];
            Home = data.Home ?? [];
            _homeVersion++;
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
    public int Place { get; set; }
}

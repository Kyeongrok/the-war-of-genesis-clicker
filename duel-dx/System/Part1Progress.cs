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
    }

    /// <summary>인물마다 자란 것 — 누적 경험치와 어빌리티 레벨(레벨업으로 올린 것). 인물 레코드에 덮어 쓴다(<see cref="Apply"/>).</summary>
    internal sealed class Growth
    {
        public int Experience { get; set; }
        public Dictionary<int, int> Abilities { get; set; } = [];
    }

    internal Dictionary<int, Growth> Grown = [];

    /// <summary>번 돈(eld) — 처치 보상(<see cref="Part1Combat.KillReward"/>).</summary>
    internal int Eld;

    /// <summary>그 인물의 자란 것 — 없으면 레코드의 경험치로 새로 만든다.</summary>
    internal Growth GrowthOf(int chrCode, int baseExperience) =>
        Grown.TryGetValue(chrCode, out var g) ? g : Grown[chrCode] = new Growth { Experience = baseExperience };

    /// <summary>인물 레코드에 자란 것을 입힌다(GameDatabase.Part1Growth).</summary>
    internal Part1Chr Apply(Part1Chr chr) =>
        Grown.TryGetValue(Part1Game.ChrBase + chr.Code, out var g) ? chr.WithGrowth(g.Experience, g.Abilities) : chr;

    /// <summary>일행이 선 챕터 지도 장소(차례).</summary>
    internal int PartyPlace;

    /// <summary>챕터 사건의 남은 행동 — 전투·필드에 갔다 오면 이어 돌 것(세이브에 같이 적는다).</summary>
    internal List<ScriptCommand> PendingActions = [];

    internal static string SavePath => UserDataFolder.File("part1.json");

    internal static bool HasSave => File.Exists(SavePath);

    private sealed record SaveData(List<int> Party, int ChapterId, int PartyPlace, List<int> Cleared, List<int> FinishedChapters,
                                   Dictionary<int, Dictionary<int, int>> Fired, string Flags, List<short[]> Pending,
                                   Dictionary<int, Growth>? Grown = null, int Eld = 0);

    /// <summary>진행과 진행 깃발(파트2 와 같은 칸)을 적는다. 못 적으면 조용히 넘어간다.</summary>
    internal void Save(byte[] flags)
    {
        var data = new SaveData(Party, ChapterId, PartyPlace, [.. Cleared], [.. FinishedChapters], Fired, Convert.ToBase64String(flags),
                                [.. PendingActions.Select(c => (short[])[(short)c.Code, .. c.Args])], Grown, Eld);
        try
        {
            Directory.CreateDirectory(UserDataFolder.Path);
            File.WriteAllText(SavePath, JsonSerializer.Serialize(data));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>적어 둔 진행을 읽어 들인다 — 깃발은 <paramref name="flags"/> 에 덮어쓴다. 없거나 못 읽으면 false.</summary>
    internal bool Load(byte[] flags)
    {
        try
        {
            if (JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SavePath)) is not { } data) return false;
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

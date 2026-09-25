using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WarOfGenesis.Assets;

/// <summary>
/// 스킬 자료 — 어빌리티 하나를 <b>공통 정의 한 벌 + 레벨마다 달라지는 칸</b>으로 적은 <c>assets/data/skills/NNNN.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// 원본 <c>Dat\NNNN.att</c> 는 레벨마다 62바이트 work 레코드를 통째로 둔다 — 엘레맨탈 파이어 20레벨이면 사거리·대상·범위까지 스무 벌이다.
/// 레벨이 여럿인 어빌리티 122개(work 1,559개)를 세어 보면 레벨마다 달라지는 칸은 위력·사거리 최대·TP·EXP·효과 범위 정도고
/// (드물게 HP 계수·명중·최소 대상 수·사거리 최소·범위 모양·SOUL·준비 동작), 나머지는 어느 어빌리티에서도 레벨마다 같다.
/// 그래서 <b>모든 레벨이 같은 칸은 <c>common</c> 에 한 번</b>, 레벨마다 다른 칸만 <c>levels</c> 줄에 적는다.
/// </para>
/// <para>
/// 레코드의 62바이트는 <see cref="Fields"/> 가 <b>빠짐없이</b> 이름을 붙인다(뜻을 모르는 바이트는 <c>b11</c> 처럼 자리 이름) —
/// 그래서 JSON 을 펼치면(<see cref="Expand"/>) 원본 레코드와 바이트까지 같다. 게임(<see cref="GameDatabase.Load"/>)은 이 폴더가 있으면
/// work 를 여기서 만들고, 없으면(게임 폴더) <c>.att</c> 를 읽는다. 처음 만들 때는 <see cref="FromAtt"/> 로 <c>.att</c> 에서 뽑는다.
/// </para>
/// <para>
/// 어빌리티에 딸리지 않은 work(어빌리티 0 — 기본공격·몬스터 기술 등)는 <c>work-NNNN.json</c> 에 한 레벨짜리로 둔다.
/// </para>
/// </remarks>
public sealed class SkillFile
{
    /// <summary>어빌리티 번호(0 이면 어빌리티 없는 work 하나).</summary>
    public int Ability { get; set; }

    /// <summary>보기용 이름(읽을 때 안 쓴다).</summary>
    public string Name { get; set; } = "";

    /// <summary>원래 들어 있던 <c>.att</c> 파일 번호 — 되돌려 쓸 때(내보내기)를 위해 남긴다.</summary>
    public int Att { get; set; }

    /// <summary>모든 레벨이 같은 칸 — 칸 이름 → 값.</summary>
    public Dictionary<string, int> Common { get; set; } = [];

    /// <summary>레벨마다 — work 번호와 그 레벨만의 칸.</summary>
    public List<SkillLevel> Levels { get; set; } = [];

    /// <summary>파일 이름 — <c>0029.json</c>, 어빌리티 없는 work 는 <c>work-0001.json</c>.</summary>
    public string FileName => Ability > 0 ? $"{Ability:D4}.json" : $"work-{Levels.FirstOrDefault()?.Work ?? 0:D4}.json";
}

/// <summary>스킬 한 레벨 — work 번호, 레벨, 그 레벨에서만 다른 칸.</summary>
public sealed class SkillLevel
{
    public int Work { get; set; }
    public int Level { get; set; }
    public Dictionary<string, int> Fields { get; set; } = [];
}

/// <summary>work 레코드 62바이트의 칸 표와 <see cref="SkillFile"/> 읽기·쓰기·펼치기.</summary>
public static class SkillBook
{
    /// <summary>
    /// 칸 하나 — 이름 · 파일 오프셋 · 크기(1·2) · 부호. 뜻은 <see cref="WorkData"/> 의 매개변수 설명을 따른다(오프셋이 같다).
    /// <c>work</c>(0)·<c>level</c>(4)은 레벨 줄의 <see cref="SkillLevel.Work"/>·<see cref="SkillLevel.Level"/> 로, <c>ability</c>(2)는 파일의 것으로 채운다.
    /// </summary>
    /// <param name="Enum">값이 정해진 칸이면 그 enum(<see cref="TargetMode"/> 등) — 편집기가 드롭다운으로 고르게 한다.</param>
    public sealed record Field(string Name, int Offset, int Size, bool Signed, string Meaning, Type? Enum = null);

    public const int RecordSize = 62;

    public static IReadOnlyList<Field> Fields { get; } =
    [
        new("work", 0, 2, false, "work 번호"),
        new("ability", 2, 2, false, "어빌리티 번호"),
        new("level", 4, 1, false, "어빌리티 레벨"),
        new("rangeShape", 5, 1, false, "사거리 모양 — 0 자기 자리, 1 마름모, 2 십자, 3 부채꼴, 4 화면 전체, 5 직선, 6 폭3 줄, 7 폭5 줄, 8 대각선 X, 9 45° 삼각형", typeof(AreaShape)),
        new("rangeKind", 6, 1, false, "사거리 종류 — 0 없음, 1·3 자기 값, 2 무기 사거리, 4 무기 + 자기 값", typeof(RangeKind)),
        new("rangeMin", 7, 2, false, "사거리 최소(4분의 1칸: 값×4−3)"),
        new("rangeMax", 9, 2, false, "사거리 최대(칸)"),
        new("b11", 11, 1, false, "(모름)"),
        new("heightRange", 12, 1, false, "사거리에 높이 차를 더한다"),
        new("sight", 13, 1, false, "시야 — 사이에 더 높은 칸이 있으면 못 겨눈다"),
        new("sameHeightRange", 14, 1, false, "사거리: 같은 높이 칸만"),
        new("heightGraded", 15, 1, false, "높이 차등(올려치기 +3/층, 내려치기 −2/층)"),
        new("targetMode", 16, 1, false, "대상 방식 — 0·2 자기 자리, 1 적, 3·6 아무 칸, 4 아군, 5 아무 유닛, 7 빈 칸, 8 오브젝트", typeof(TargetMode)),
        new("areaShape", 17, 1, false, "효과 범위 모양(사거리와 같은 표)", typeof(AreaShape)),
        new("b18", 18, 1, false, "(모름)"),
        new("heightArea", 19, 1, false, "효과 범위에 높이 차를 더한다"),
        new("b20", 20, 1, false, "(모름)"),
        new("sameHeightArea", 21, 1, false, "효과 범위: 같은 높이 칸만"),
        new("areaMax", 22, 2, true, "효과 범위 최대(칸)"),
        new("areaMin", 24, 2, false, "효과 범위 최소"),
        new("areaMode", 26, 1, false, "효과 범위 안에서 맞히는 대상(대상 방식과 같은 값)", typeof(TargetMode)),
        new("kind", 27, 1, false, "종류 — 0 피해, 1·5 회복, 2·3 보조, 4 오브젝트", typeof(WorkKind)),
        new("bonus1Stat", 28, 1, false, "효과 1 — 맞힌 대상에게 거는 상태이상 번호(30~33·37·48 은 능력치 보정)", typeof(StatusEffect)),
        new("bonus1Value", 29, 2, true, "효과 1 값 — 세기·변화량(31 PSY 에 −10 이면 PSY −10)"),
        new("bonus2Stat", 31, 1, false, "효과 2 — 맞힌 대상에게 거는 상태이상 번호(30~33·37·48 은 능력치 보정)", typeof(StatusEffect)),
        new("bonus2Value", 32, 2, true, "효과 2 값 — 세기·변화량(31 PSY 에 −10 이면 PSY −10)"),
        new("bonus3Stat", 34, 1, false, "효과 3 — 맞힌 대상에게 거는 상태이상 번호(30~33·37·48 은 능력치 보정)", typeof(StatusEffect)),
        new("bonus3Value", 35, 2, true, "효과 3 값 — 세기·변화량(31 PSY 에 −10 이면 PSY −10)"),
        new("power", 37, 2, true, "위력 — 피해면 공격력×(200+값)/2000, 회복이면 최대 HP %"),
        new("accuracy", 39, 1, false, "명중 바탕"),
        new("critical", 40, 1, false, "치명 확률 − 1(%)"),
        new("hpFactor", 41, 2, false, "체질 비용 — 체질 비율대로 나눠 SOUL(÷10)·TP(÷4)·HP(÷2) 비용에 더한다. 맨탈체 SOUL · 코절체 TP · 에텔체 HP 전부, 아스트럴체 SOUL·TP 반반, 메텔체 HP·SOUL 반반"),
        new("exp", 43, 2, false, "다음 레벨 EXP"),
        new("tp", 45, 2, false, "TP 비용"),
        new("soul", 47, 2, false, "SOUL 비용"),
        new("soulSpend", 49, 1, false, "SOUL 을 쓰나 — True 면 soul 도 빠지고, False 면 soul 은 있어야 하는 양일 뿐 안 빠진다", typeof(SoulSpend)),
        new("b50", 50, 1, false, "(모름)"),
        new("b51", 51, 1, false, "(모름)"),
        new("b52", 52, 1, false, "(모름)"),
        new("b53", 53, 1, false, "(모름)"),
        new("b54", 54, 1, false, "(모름)"),
        new("minTargets", 55, 1, false, "AI 가 쓸 만하다고 보는 최소 대상 수 − 1"),
        new("aiCriterion", 56, 1, false, "AI 칸 점수 기준"),
        new("prepare", 57, 1, false, "준비 동작 종류(7 = 필살기)"),
        new("b58", 58, 1, false, "(모름)"),
        new("b59", 59, 1, false, "(모름)"),
        new("b60", 60, 1, false, "(모름)"),
        new("b61", 61, 1, false, "(모름)"),
    ];

    /// <summary>레벨 줄·파일이 따로 드는 칸 — <c>common</c>/레벨 칸에는 안 적는다.</summary>
    private static readonly HashSet<string> Identity = ["work", "ability", "level"];

    private static int Get(byte[] r, Field f) => f.Size == 1 ? r[f.Offset] : f.Signed ? BitConverter.ToInt16(r, f.Offset) : BitConverter.ToUInt16(r, f.Offset);

    private static void Set(byte[] r, Field f, int v)
    {
        if (f.Size == 1) r[f.Offset] = (byte)v;
        else BitConverter.TryWriteBytes(r.AsSpan(f.Offset, 2), (ushort)(short)v);
    }

    /// <summary><c>.att</c> 파일들에서 스킬 파일을 만든다 — 어빌리티마다 하나, 어빌리티 없는 work 는 work 마다 하나.</summary>
    public static List<SkillFile> FromAtt(GameFiles files, Func<int, string>? abilityName = null)
    {
        var records = new List<(int Att, byte[] Record)>();
        foreach (int f in GameDatabase.WorkFiles)
        {
            if (files.Read("Dat", $"{f:D4}.att") is not { } a) continue;
            for (int i = 0, n = BitConverter.ToUInt16(a, 2), o = 6; i < n && o + RecordSize <= a.Length; i++, o += RecordSize)
                records.Add((f, a[o..(o + RecordSize)]));
        }
        var fAbility = Fields.First(x => x.Name == "ability");
        var fLevel = Fields.First(x => x.Name == "level");
        var fWork = Fields.First(x => x.Name == "work");
        var result = new List<SkillFile>();
        foreach (var group in records.GroupBy(r => Get(r.Record, fAbility) is var ab && ab > 0 ? ab : -Get(r.Record, fWork)))
        {
            var list = group.OrderBy(r => Get(r.Record, fLevel)).ThenBy(r => Get(r.Record, fWork)).ToList();
            var skill = new SkillFile
            {
                Ability = Math.Max(0, group.Key),
                Name = group.Key > 0 ? abilityName?.Invoke(group.Key) ?? "" : "",
                Att = list[0].Att,
            };
            var varying = Fields.Where(f => !Identity.Contains(f.Name) && list.Select(r => Get(r.Record, f)).Distinct().Count() > 1).ToHashSet();
            foreach (var f in Fields)
                if (!Identity.Contains(f.Name) && !varying.Contains(f)) skill.Common[f.Name] = Get(list[0].Record, f);
            foreach (var (att, r) in list)
            {
                var level = new SkillLevel { Work = Get(r, fWork), Level = Get(r, fLevel) };
                foreach (var f in Fields.Where(varying.Contains)) level.Fields[f.Name] = Get(r, f);
                // 한 어빌리티의 레벨이 다른 .att 에 들어 있으면 그 레벨에 적어 둔다(자료에는 없다).
                if (att != skill.Att) level.Fields["att"] = att;
                skill.Levels.Add(level);
            }
            result.Add(skill);
        }
        return result;
    }

    /// <summary>스킬 파일을 work 레코드들로 펼친다 — (work 번호, 62바이트).</summary>
    public static IEnumerable<(int Work, byte[] Record)> Expand(SkillFile skill)
    {
        foreach (var level in skill.Levels)
        {
            var r = new byte[RecordSize];
            foreach (var f in Fields)
            {
                int v = f.Name switch
                {
                    "work" => level.Work,
                    "ability" => skill.Ability,
                    "level" => level.Level,
                    _ => level.Fields.TryGetValue(f.Name, out int own) ? own : skill.Common.GetValueOrDefault(f.Name),
                };
                Set(r, f, v);
            }
            yield return (level.Work, r);
        }
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 한글 이름을 \uXXXX 로 풀지 않는다
    };

    /// <summary>
    /// JSON 으로 — 레벨 한 줄이 한 줄에 들어가게 적는다(<c>{ "work": 389, "level": 1, "power": 0 }</c>). 스무 레벨을 눈으로 훑고 git 에서 바뀐 줄이 바로 보이게.
    /// </summary>
    public static string ToJson(SkillFile skill)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"ability\": {skill.Ability},\n");
        sb.Append($"  \"name\": {JsonSerializer.Serialize(skill.Name, Json)},\n");
        sb.Append($"  \"att\": {skill.Att},\n");
        sb.Append("  \"common\": {");
        sb.Append(string.Join(",", skill.Common.Select(kv => $"\n    \"{kv.Key}\": {kv.Value}")));
        sb.Append(skill.Common.Count > 0 ? "\n  },\n" : "},\n");
        sb.Append("  \"levels\": [");
        sb.Append(string.Join(",", skill.Levels.Select(l =>
            "\n    { \"work\": " + l.Work + ", \"level\": " + l.Level + string.Concat(l.Fields.Select(kv => $", \"{kv.Key}\": {kv.Value}")) + " }")));
        sb.Append(skill.Levels.Count > 0 ? "\n  ]\n" : "]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    public static SkillFile? FromJson(string text)
    {
        if (JsonNode.Parse(text) is not JsonObject o) return null;
        var skill = new SkillFile
        {
            Ability = o["ability"]?.GetValue<int>() ?? 0,
            Name = o["name"]?.GetValue<string>() ?? "",
            Att = o["att"]?.GetValue<int>() ?? 0,
        };
        if (o["common"] is JsonObject common)
            foreach (var (k, v) in common) if (v != null) skill.Common[k] = v.GetValue<int>();
        if (o["levels"] is JsonArray levels)
            foreach (var node in levels.OfType<JsonObject>())
            {
                var level = new SkillLevel { Work = node["work"]?.GetValue<int>() ?? 0, Level = node["level"]?.GetValue<int>() ?? 0 };
                foreach (var (k, v) in node)
                    if (k is not ("work" or "level") && v != null) level.Fields[k] = v.GetValue<int>();
                skill.Levels.Add(level);
            }
        return skill;
    }

    /// <summary>폴더(<c>skills</c>)의 스킬 파일을 다 읽는다 — 없거나 비었으면 빈 목록.</summary>
    public static List<SkillFile> Load(GameFiles files)
    {
        var list = new List<SkillFile>();
        IReadOnlyDictionary<string, long> names;
        try { names = files.List("skills", ".json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return list; }
        foreach (var name in names.Keys)
            if (files.Read("skills", name) is { } b && FromJson(Encoding.UTF8.GetString(b)) is { } skill) list.Add(skill);
        return list;
    }
}

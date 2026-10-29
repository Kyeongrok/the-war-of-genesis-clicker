using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WarOfGenesis.Assets;

/// <summary>
/// 군단 덮어쓰기 — <c>assets/data/legions/NNNN.json</c> 에 고친 군단만 적는다. 원본 <c>Dat/For.dat</c> 은 그대로 두고(사용자 요청),
/// 게임·편집기는 <see cref="LoadAll"/> 로 원본을 읽은 뒤 이 파일들을 덮어쓴다.
/// </summary>
/// <remarks>
/// 적는 칸: 부하 Chr(최대 6, 진형 자리 차례) · 진형 · 부하 보정 LP·PSY·DEP · 군단기(어빌리티, 필요 세력, 대장 Chr)×최대 5.
/// 이름·설명 TXR 은 원본 것을 그대로 쓴다(<c>name</c> 은 보기용). TP·DEX 보정은 읽는 코드가 없어(분석-군단 1절) 두지 않는다.
/// <para>
/// 새 군단(ed-8) — 원본 For.dat 번호(0~80)와 겹치지 않게 <see cref="FirstCustomId"/>(900)부터 쓴다. 이름 TXR 이 없으니 json 의
/// <c>name</c>·<c>description</c> 을 그대로 보인다(<see cref="LegionName"/>). <c>owned</c> 를 켜면 처음부터 가진 군단으로 친다(<see cref="LegionData.Owned"/>).
/// </para>
/// </remarks>
public static class LegionBook
{
    public const string Folder = "legions";

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string FileName(int id) => $"{id:D4}.json";

    /// <summary>새 군단 번호의 시작 — 원본 For.dat 의 최대 번호는 80(파일 머리 u16 최대 번호, 레코드 79개)이라 넉넉히 띄운다.</summary>
    public const int FirstCustomId = 900;

    /// <summary>보일 군단 이름 — 새 군단은 json 의 <c>name</c>, 원본 군단은 이름 TXR.</summary>
    public static string LegionName(this GameDatabase db, LegionData l) =>
        l.CustomName is { Length: > 0 } custom ? custom : db.T(l.NameId) is { Length: > 0 } t ? t : $"군단 {l.Id}";

    /// <summary>보일 군단 설명 — 새 군단은 json 의 <c>description</c>, 원본 군단은 설명 TXR(<c>$n</c> 줄바꿈은 원본 TXR 과 같다).</summary>
    public static string LegionDescription(this GameDatabase db, LegionData l) =>
        l.CustomName != null ? l.CustomDescription ?? "" : db.T(l.DescriptionId);

    public static string ToJson(LegionData l, string name)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"id\": {l.Id},\n");
        sb.Append($"  \"name\": {JsonSerializer.Serialize(l.CustomName ?? name, Json)},\n");
        if (l.CustomDescription is { Length: > 0 } desc) sb.Append($"  \"description\": {JsonSerializer.Serialize(desc, Json)},\n");
        if (l.Owned) sb.Append("  \"owned\": true,\n");
        if (l.Leader > 0) sb.Append($"  \"leader\": {l.Leader},\n");
        sb.Append($"  \"members\": [{string.Join(", ", l.Members)}],\n");
        sb.Append($"  \"formation\": {l.Formation},\n");
        sb.Append($"  \"lpBonus\": {l.LpBonus},\n");
        sb.Append($"  \"psyBonus\": {l.PsyBonus},\n");
        sb.Append($"  \"depBonus\": {l.DepBonus},\n");
        sb.Append("  \"skills\": [");
        sb.Append(string.Join(",", l.Skills.Select(k => $"\n    {{ \"ability\": {k.Ability}, \"power\": {k.Power}, \"leader\": {k.Leader} }}")));
        sb.Append(l.Skills.Length > 0 ? "\n  ]\n" : "]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>
    /// JSON 을 원본 군단 위에 덮는다 — 이름·설명 TXR 은 원본 것. 빠진 칸은 원본 값을 쓴다.
    /// 원본에 없는 번호(<paramref name="original"/> 이 null)는 새 군단이라 <c>name</c>·<c>description</c> 을 그대로 이름·설명으로 쓴다.
    /// </summary>
    public static LegionData? FromJson(string text, LegionData? original)
    {
        if (JsonNode.Parse(text) is not JsonObject o || o["id"] is not { } idNode) return null;
        int id = idNode.GetValue<int>();
        static ushort U(JsonNode? n) => (ushort)Math.Clamp(n?.GetValue<int>() ?? 0, 0, ushort.MaxValue);
        static string? S(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : null;
        var members = (o["members"] as JsonArray)?.Select(U).Where(m => m != 0).Take(6).ToArray() ?? original?.Members ?? [];
        var skills = (o["skills"] as JsonArray)?.OfType<JsonObject>()
                         .Select(k => (U(k["ability"]), U(k["power"]), U(k["leader"]))).Where(k => k.Item1 != 0).Take(5).ToArray()
                     ?? original?.Skills ?? [];
        return new LegionData(id, original?.NameId ?? 0, members,
                              (byte)Math.Clamp(o["formation"]?.GetValue<int>() ?? original?.Formation ?? 0, 0, 5),
                              o["lpBonus"] is { } lp ? U(lp) : original?.LpBonus ?? 0,
                              o["psyBonus"] is { } psy ? U(psy) : original?.PsyBonus ?? 0,
                              o["depBonus"] is { } dep ? U(dep) : original?.DepBonus ?? 0,
                              skills, original?.DescriptionId ?? 0)
        {
            Leader = o["leader"] is { } leader ? U(leader) : original?.Leader ?? 0,
            CustomName = original == null ? S(o["name"])?.Trim() is { Length: > 0 } name ? name : $"군단 {id}" : null,
            CustomDescription = original == null ? S(o["description"]) : null,
            Owned = o["owned"] is JsonValue ow && ow.TryGetValue(out bool owned) && owned,
        };
    }

    /// <summary>원본 For.dat 만 — 편집기가 「원본과 다른가」를 가를 때.</summary>
    public static Dictionary<int, LegionData> LoadOriginal(GameFiles files) => LegionData.ParseAll(files.Read("Dat", "For.dat"));

    /// <summary>원본 For.dat 에 <c>legions/*.json</c> 덮어쓰기를 얹은 군단 표 — 게임이 쓰는 것.</summary>
    public static Dictionary<int, LegionData> LoadAll(GameFiles files)
    {
        var all = LoadOriginal(files);
        IReadOnlyDictionary<string, long> names;
        try { names = files.List(Folder, ".json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return all; }
        foreach (var name in names.Keys)
        {
            if (files.Read(Folder, name) is not { } b) continue;
            try
            {
                string text = Encoding.UTF8.GetString(b);
                int id = JsonNode.Parse(text)?["id"]?.GetValue<int>() ?? -1;
                if (FromJson(text, all.GetValueOrDefault(id)) is { } l) all[l.Id] = l;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        }
        return all;
    }

    /// <summary>두 군단이 같은 구성인가 — 편집기가 원본과 같으면 덮어쓰기 파일을 안 남긴다.</summary>
    public static bool SameContent(LegionData a, LegionData b) =>
        a.Leader == b.Leader && a.Members.SequenceEqual(b.Members) && a.Formation == b.Formation && a.LpBonus == b.LpBonus && a.PsyBonus == b.PsyBonus
        && a.DepBonus == b.DepBonus && a.Skills.SequenceEqual(b.Skills)
        && a.CustomName == b.CustomName && a.CustomDescription == b.CustomDescription && a.Owned == b.Owned;
}

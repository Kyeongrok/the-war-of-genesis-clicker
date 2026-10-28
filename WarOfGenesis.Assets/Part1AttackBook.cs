using System.Text;
using System.Text.Json.Nodes;

namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1 기술 덮어쓰기 — <c>assets/data/part1-attacks/NNNN.json</c> 에 고친 기술(레벨 한 칸 = <c>Attack.dat</c> 한 칸)만 적는다.
/// 원본 <c>Dats\Attack.dat</c> 은 사용자의 파트1 폴더에서 읽기만 하고, <see cref="Part1Attack.LoadAll"/> 이 그 위에 이 파일들을 덮는다
/// (<see cref="ItemBook"/> 과 같은 꼴, 사용자 요청 ed-sk-10 「파트1 레벨별 데미지 · 명중률 · 효과도 파트2 어빌리티처럼」).
/// </summary>
/// <remarks>
/// 적는 칸: 세기 식(<c>+0x42</c>) · 더하는 세기(<c>+0x44</c>, 제곱해 더한다) · 피해 %(원본에 없는 우리 칸) · 명중률(<c>+0x48</c>) · 타수(<c>+0x66</c>) ·
/// 속성 · 둘째 속성(<c>+0x64</c> · <c>+0x65</c>) · 소울(<c>+0x4c</c>). 빠진 칸은 원본 값을 쓴다. 이름은 보기용.
/// 게임은 켤 때 한 번 읽는다(<see cref="Reload"/> 는 편집기가 저장한 뒤 부른다).
/// </remarks>
public static class Part1AttackBook
{
    public const string Folder = "part1-attacks";

    /// <summary>고칠 수 있는 칸 — JSON 이름, 메모리 자리(<see cref="Part1Attack.Raw"/>), 보이는 이름.</summary>
    public static readonly (string Key, int At, string Label)[] Fields =
    [
        ("formula", 0x42, "세기 식"),
        ("powerAdd", 0x44, "더하는 세기"),
        ("accuracy", 0x48, "명중률"),
        ("hits", 0x66, "타수"),
        ("element", 0x64, "속성"),
        ("element2", 0x65, "둘째 속성"),
        ("cost", 0x4c, "소울"),
    ];

    /// <summary>세기 식 번호의 뜻 — <see cref="Part1Combat.Power"/> · 분석-파트1-전투 9절.</summary>
    public static string FormulaName(int formula) => formula switch
    {
        0 or 7 => "무기 · STR",
        1 => "무기 · DEX+SKILL",
        2 => "무기 · SKILL",
        3 => "무기 · SKILL+STR",
        4 => "무기 · DEX+SKILL+STR",
        5 => "마법 피해 · INT²",
        6 => "회복 · INT²",
        8 => "마법 · INT²",
        >= 10 and <= 28 => "상태이상 · INT²",
        29 or 30 => "둘째 무기 · STR/DEX",
        31 => "둘째 무기 · SKILL",
        _ => $"식 {formula}",
    };

    /// <summary>속성 번호의 이름 — 1 화염 · 2 빙한 · 3 전격 · 4 회복 · 5 암흑(0 = 무기 속성). 둘째 속성(<c>+0x65</c>, 1~4 — 인물 저항 칸 35~38)은 이름을 모른다.</summary>
    public static string ElementName(int element) => element switch
    {
        0 => "-", 1 => "화염", 2 => "빙한", 3 => "전격", 4 => "회복", 5 => "암흑", _ => $"{element}",
    };

    private static Dictionary<int, JsonObject>? _edits;

    public static string FileName(int id) => $"{id:D4}.json";

    /// <summary>덮어쓰기 파일이 있는 폴더 — assets 를 못 찾으면 null.</summary>
    public static string? EditFolder
    {
        get
        {
            try { return Path.Combine(AssetsFolder.Find("data"), Folder); }
            catch (DirectoryNotFoundException) { return null; }
        }
    }

    /// <summary>다음 <see cref="Apply"/> 가 파일을 다시 읽게 한다.</summary>
    public static void Reload() => _edits = null;

    private static Dictionary<int, JsonObject> Edits()
    {
        if (_edits != null) return _edits;
        var edits = new Dictionary<int, JsonObject>();
        try
        {
            if (EditFolder is { } folder && Directory.Exists(folder))
                foreach (string path in Directory.EnumerateFiles(folder, "*.json"))
                    if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int id) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject o)
                        edits[id] = o;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        return _edits = edits;
    }

    /// <summary>원본 기술들 위에 덮어쓰기를 얹는다 — 없는 번호는 버린다.</summary>
    public static IReadOnlyList<Part1Attack> Apply(IReadOnlyList<Part1Attack> original)
    {
        var edits = Edits();
        if (edits.Count == 0) return original;
        return [.. original.Select(a => edits.TryGetValue(a.Id, out var o) ? FromJson(o, a) : a)];
    }

    /// <summary>JSON 한 칸을 원본 기술 위에 덮는다 — 빠진 칸은 원본 값.</summary>
    public static Part1Attack FromJson(JsonObject o, Part1Attack original)
    {
        var raw = new Dictionary<int, int>(original.Raw);
        foreach (var (key, at, _) in Fields)
            if (o[key] is JsonValue v && v.TryGetValue(out long n)) raw[at] = (int)Math.Clamp(n, at is 0x44 or 0x4c ? int.MinValue : 0, at is 0x44 or 0x4c ? int.MaxValue : 255);
        int percent = o["damagePercent"] is JsonValue p && p.TryGetValue(out long d) ? (int)Math.Clamp(d, 0, 1000) : original.DamagePercent;
        return original with { Raw = raw, DamagePercent = percent };
    }

    public static string ToJson(Part1Attack a)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"id\": {a.Id},\n");
        sb.Append($"  \"name\": {System.Text.Json.JsonSerializer.Serialize(a.Name, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })},\n");
        foreach (var (key, at, _) in Fields) sb.Append($"  \"{key}\": {a.Raw[at]},\n");
        sb.Append($"  \"damagePercent\": {a.DamagePercent}\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>고칠 수 있는 칸이 모두 같은가.</summary>
    public static bool SameContent(Part1Attack a, Part1Attack b) =>
        a.DamagePercent == b.DamagePercent && Fields.All(f => a.Raw[f.At] == b.Raw[f.At]);
}

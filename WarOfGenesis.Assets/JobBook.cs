using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WarOfGenesis.Assets;

/// <summary>
/// 직업(<c>Dat/Job.dat</c>)을 <b>계열 × 형</b> 묶음으로 다시 적은 자료 — 저장소 <c>assets/data/jobs/*.json</c>.
/// </summary>
/// <remarks>
/// 원본은 1단계(제 체질의 계열)와 2단계(다른 체질이 그 계열로 갈아탄 것)를 <b>따로 된 레코드</b>로 두는데, 25쌍 가운데 18쌍은
/// 배우는 어빌리티가 똑같고 나머지 일곱도 하나 더하거나 빼는 정도다([[분석-체질]] 「1단계 · 2단계 · 3단계 한눈에」).
/// 그래서 형마다 <b>공통 어빌리티 목록을 한 번만</b> 적고, 2단계는 <see cref="JobStage.Add"/>·<see cref="JobStage.Remove"/> 차이만 든다.
/// 레벨·성장률·이름·설명은 단계마다 다르니 단계에 둔다. 3단계와 계열에 안 든 직업(몬스터·NPC)은 목록을 통째로 든다.
/// 게임은 <see cref="Expand"/> 로 원래 레코드(<see cref="JobData"/>)로 풀어 쓴다 — 동작은 원본과 같다.
/// </remarks>
public sealed class JobFile
{
    /// <summary>계열 번호 1~5(사이클론·타키리온·포스트럴·아크로스트·오즈마), 0 = 계열에 안 든 직업들.</summary>
    public int Family { get; set; }
    public string Name { get; set; } = "";
    public List<JobForm> Forms { get; } = [];

    /// <summary>목록을 통째로 드는 직업 — 3단계, 그리고 계열 0 이면 몬스터·NPC 직업들.</summary>
    public List<JobStage> Standalone { get; } = [];
}

/// <summary>계열의 형 하나(일반·공격·방어·고속·보조) — 1·2단계가 같이 쓰는 어빌리티 목록과 두 단계.</summary>
public sealed class JobForm
{
    public string Form { get; set; } = "";

    /// <summary>1·2단계 공통의 배울 수 있는 어빌리티 — 차례가 곧 「배울 수 있는」 차례다. 1단계는 이 목록 그대로다.</summary>
    public List<int> Abilities { get; } = [];

    public JobStage Stage1 { get; set; } = new();
    public JobStage? Stage2 { get; set; }
}

/// <summary>직업 레코드 하나의 단계별 칸 — 번호, 필요 레벨·어빌리티, 성장률 12칸, 체질별 이름 6칸, 설명.</summary>
public sealed class JobStage
{
    public int Job { get; set; }
    public int NeedLevel { get; set; }
    public int NeedAbility { get; set; }
    public int NeedAbilityLevel { get; set; }

    /// <summary>파일 +7 의 12칸 — 6 LP · 7 TP · 9 PSY · 10 DEP · 11 DEX 가 레벨업 성장률(%), 0~5·8 은 뜻 모름(사이클론만 값).</summary>
    public int[] Growth { get; set; } = new int[12];

    /// <summary>파일 +53 의 6칸 — 첫 칸 다음부터 에텔·멘탈·아스트럴·코절·메텔 이름 TXR.</summary>
    public int[] Names { get; set; } = new int[6];
    public int Description { get; set; }

    /// <summary>2단계에만 — 공통 목록에 끼워 넣는 어빌리티와 그 자리.</summary>
    public List<(int Ability, int At)> Add { get; } = [];

    /// <summary>2단계에만 — 공통 목록에서 빼는 어빌리티.</summary>
    public List<int> Remove { get; } = [];

    /// <summary>목록을 통째로 드는 직업(<see cref="JobFile.Standalone"/>)의 어빌리티.</summary>
    public List<int>? Abilities { get; set; }
}

public static class JobBook
{
    public const int SlotCount = 11;
    public static readonly string[] Forms = ["일반형", "공격형", "방어형", "고속형", "보조형"];

    /// <summary>폴더 이름(<c>assets/data/jobs</c>).</summary>
    public const string Folder = "jobs";

    /// <summary>JSON 묶음을 게임이 쓰는 직업 레코드로 푼다.</summary>
    public static IEnumerable<JobData> Expand(JobFile file)
    {
        foreach (var form in file.Forms)
        {
            yield return Record(form.Stage1, form.Abilities);
            if (form.Stage2 is { } s2) yield return Record(s2, StageAbilities(form, s2));
        }
        foreach (var stage in file.Standalone) yield return Record(stage, stage.Abilities ?? []);
    }

    /// <summary>2단계의 실제 목록 — 공통 목록에서 뺄 것을 빼고, 더할 것을 제자리에 끼운다.</summary>
    public static List<int> StageAbilities(JobForm form, JobStage stage)
    {
        var list = form.Abilities.Where(a => !stage.Remove.Contains(a)).ToList();
        foreach (var (ability, at) in stage.Add.OrderBy(a => a.At)) list.Insert(Math.Clamp(at, 0, list.Count), ability);
        return list;
    }

    private static JobData Record(JobStage s, IReadOnlyList<int> abilities) =>
        new(s.Job, [.. s.Growth.Select(v => (ushort)v)],
            [.. Enumerable.Range(0, SlotCount).Select(k => k < abilities.Count ? (ushort)abilities[k] : (ushort)0)],
            [.. s.Names.Select(v => (ushort)v)], (ushort)s.Description, (ushort)s.NeedLevel, (ushort)s.NeedAbility, (byte)s.NeedAbilityLevel);

    /// <summary>
    /// 원본 <c>Job.dat</c> 레코드와 계열 표(<c>Dep.dat</c>)로 묶음을 만든다 — 계열 f 의 Dep 레코드 3f+1·3f+2·3f+3 이 1·2·3단계다.
    /// 2단계 목록이 공통 목록의 뺄 것·더할 것으로 똑같이 되살아나지 않으면(차례까지 비교) 그 단계는 목록을 통째로 든다.
    /// </summary>
    public static List<JobFile> Build(IReadOnlyDictionary<int, JobData> jobs, IReadOnlyList<DepData> deps, Func<ushort, string> text)
    {
        var files = new List<JobFile>();
        var placed = new HashSet<int>();
        for (int family = 1; family <= 5; family++)
        {
            var d1 = deps.FirstOrDefault(d => d.Id == 3 * (family - 1) + 1);
            var d2 = deps.FirstOrDefault(d => d.Id == 3 * (family - 1) + 2);
            var d3 = deps.FirstOrDefault(d => d.Id == 3 * (family - 1) + 3);
            if (d1 == null) continue;
            var file = new JobFile { Family = family, Name = text(d1.NameId) };
            for (int k = 0; k < Forms.Length && k < d1.Jobs.Length; k++)
            {
                if (!jobs.TryGetValue(d1.Jobs[k], out var j1)) continue;
                var form = new JobForm { Form = Forms[k], Stage1 = Stage(j1) };
                form.Abilities.AddRange(RawSlots(j1));
                placed.Add(j1.Id);
                if (d2 != null && k < d2.Jobs.Length && jobs.TryGetValue(d2.Jobs[k], out var j2))
                {
                    var s2 = Stage(j2);
                    var want = RawSlots(j2);
                    s2.Remove.AddRange(form.Abilities.Where(a => !want.Contains(a)));
                    for (int i = 0; i < want.Count; i++)
                        if (!form.Abilities.Contains(want[i])) s2.Add.Add((want[i], i));
                    if (!StageAbilities(form, s2).SequenceEqual(want)) { s2.Add.Clear(); s2.Remove.Clear(); s2.Abilities = want; }
                    form.Stage2 = s2;
                    placed.Add(j2.Id);
                }
                file.Forms.Add(form);
            }
            if (d3 != null)
                foreach (var id in d3.Jobs.Where(id => id != 0 && jobs.ContainsKey(id)))
                {
                    var s3 = Stage(jobs[id]);
                    s3.Abilities = RawSlots(jobs[id]);
                    file.Standalone.Add(s3);
                    placed.Add(id);
                }
            files.Add(file);
        }
        var others = new JobFile { Family = 0, Name = "그 밖" };
        foreach (var job in jobs.Values.OrderBy(j => j.Id).Where(j => !placed.Contains(j.Id)))
        {
            var s = Stage(job);
            s.Abilities = RawSlots(job);
            others.Standalone.Add(s);
        }
        files.Add(others);
        return files;
    }

    /// <summary>칸 그대로(뒤쪽 빈 칸만 자름) — 몬스터 직업 몇은 중간이 비어 있다(37·40·81). 게임은 빈 칸을 건너뛰니 뜻은 같지만 원본과 칸까지 맞춘다.</summary>
    private static List<int> RawSlots(JobData j)
    {
        var list = j.AbilityList.Select(a => (int)a).ToList();
        while (list.Count > 0 && list[^1] == 0) list.RemoveAt(list.Count - 1);
        return list;
    }

    private static JobStage Stage(JobData j) => new()
    {
        Job = j.Id, NeedLevel = j.NeedLevel, NeedAbility = j.NeedAbility, NeedAbilityLevel = j.NeedAbilityLevel,
        Growth = [.. j.Growth.Select(v => (int)v)], Names = [.. j.NamesByBody.Select(v => (int)v)], Description = j.DescriptionId,
    };

    /// <summary>파일 이름 — 「01_사이클론.json」, 계열에 안 든 것은 「00_그 밖.json」.</summary>
    public static string FileName(JobFile file) => $"{file.Family:D2}_{file.Name}.json";

    // ── JSON ────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ToJson(JobFile file)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"family\": {file.Family},\n");
        sb.Append($"  \"name\": {JsonSerializer.Serialize(file.Name, Json)},\n");
        sb.Append("  \"forms\": [");
        sb.Append(string.Join(",", file.Forms.Select(f =>
            "\n    {\n" +
            $"      \"form\": {JsonSerializer.Serialize(f.Form, Json)},\n" +
            $"      \"abilities\": [{string.Join(", ", f.Abilities)}],\n" +
            $"      \"stage1\": {StageJson(f.Stage1)}" +
            (f.Stage2 is { } s2 ? $",\n      \"stage2\": {StageJson(s2)}" : "") +
            "\n    }")));
        sb.Append(file.Forms.Count > 0 ? "\n  ],\n" : "],\n");
        sb.Append("  \"standalone\": [");
        sb.Append(string.Join(",", file.Standalone.Select(s => "\n    " + StageJson(s))));
        sb.Append(file.Standalone.Count > 0 ? "\n  ]\n" : "]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    private static string StageJson(JobStage s)
    {
        var parts = new List<string> { $"\"job\": {s.Job}", $"\"needLevel\": {s.NeedLevel}" };
        if (s.NeedAbility != 0) parts.Add($"\"needAbility\": {s.NeedAbility}, \"needAbilityLevel\": {s.NeedAbilityLevel}");
        parts.Add($"\"growth\": [{string.Join(", ", s.Growth)}]");
        parts.Add($"\"names\": [{string.Join(", ", s.Names)}]");
        parts.Add($"\"description\": {s.Description}");
        if (s.Add.Count > 0) parts.Add($"\"add\": [{string.Join(", ", s.Add.Select(a => $"{{ \"ability\": {a.Ability}, \"at\": {a.At} }}"))}]");
        if (s.Remove.Count > 0) parts.Add($"\"remove\": [{string.Join(", ", s.Remove)}]");
        if (s.Abilities != null) parts.Add($"\"abilities\": [{string.Join(", ", s.Abilities)}]");
        return "{ " + string.Join(", ", parts) + " }";
    }

    public static JobFile? FromJson(string text)
    {
        if (JsonNode.Parse(text) is not JsonObject o) return null;
        var file = new JobFile { Family = o["family"]?.GetValue<int>() ?? 0, Name = o["name"]?.GetValue<string>() ?? "" };
        if (o["forms"] is JsonArray forms)
            foreach (var f in forms.OfType<JsonObject>())
            {
                var form = new JobForm { Form = f["form"]?.GetValue<string>() ?? "", Stage1 = StageFrom(f["stage1"] as JsonObject) ?? new() };
                form.Abilities.AddRange(Ints(f["abilities"]));
                form.Stage2 = StageFrom(f["stage2"] as JsonObject);
                file.Forms.Add(form);
            }
        if (o["standalone"] is JsonArray standalone)
            foreach (var s in standalone.OfType<JsonObject>())
                if (StageFrom(s) is { } stage) file.Standalone.Add(stage);
        return file;
    }

    private static int[] Ints(JsonNode? node) => node is JsonArray a ? [.. a.Select(v => v?.GetValue<int>() ?? 0)] : [];

    private static JobStage? StageFrom(JsonObject? o)
    {
        if (o == null) return null;
        var s = new JobStage
        {
            Job = o["job"]?.GetValue<int>() ?? 0,
            NeedLevel = o["needLevel"]?.GetValue<int>() ?? 0,
            NeedAbility = o["needAbility"]?.GetValue<int>() ?? 0,
            NeedAbilityLevel = o["needAbilityLevel"]?.GetValue<int>() ?? 0,
            Growth = Ints(o["growth"]),
            Names = Ints(o["names"]),
            Description = o["description"]?.GetValue<int>() ?? 0,
            Abilities = o["abilities"] is JsonArray ? [.. Ints(o["abilities"])] : null,
        };
        if (o["add"] is JsonArray add)
            foreach (var a in add.OfType<JsonObject>()) s.Add.Add((a["ability"]?.GetValue<int>() ?? 0, a["at"]?.GetValue<int>() ?? 0));
        s.Remove.AddRange(Ints(o["remove"]));
        return s;
    }

    /// <summary>폴더(<c>jobs</c>)의 직업 파일을 다 읽는다 — 없거나 비었으면 빈 목록.</summary>
    public static List<JobFile> Load(GameFiles files)
    {
        var list = new List<JobFile>();
        IReadOnlyDictionary<string, long> names;
        try { names = files.List(Folder, ".json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return list; }
        foreach (var name in names.Keys)
            if (files.Read(Folder, name) is { } b && FromJson(Encoding.UTF8.GetString(b)) is { } file) list.Add(file);
        return list;
    }
}

using System.Windows;
using System.Windows.Controls;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 요소 &gt; 직업 — <c>Dat/Job.dat</c> 직업을 계열(<c>Dep.dat</c>) → 단계 → 형 차례로 늘어놓고, 고르면 상세를 보인다.
/// </summary>
/// <remarks>
/// 단계의 뜻은 [[분석-체질]] 「1단계 · 2단계 · 3단계 한눈에」 — 1단계는 제 체질의 계열, 2단계는 <b>다른 체질이 그 계열로 갈아탈 때</b>,
/// 3단계는 처음 계열의 최고 발전형. 성장률은 레벨업 식(<see cref="GameDatabase.LevelUp"/>)이 쓰는 칸(6 LP · 7 TP · 9 PSY · 10 DEP · 11 DEX)이다.
/// </remarks>
public partial class JobViewerWindow : Window
{
    public sealed record Pair(string Label, string Value);

    public sealed record JobRow(int Id, string Title, string Names, int NeedLevel, string Description,
                                IReadOnlyList<Pair> Facts, IReadOnlyList<Pair> NamesByBody, IReadOnlyList<Pair> Growth,
                                IReadOnlyList<string> Abilities, IReadOnlyList<string> Characters, string SearchText)
    {
        public string NeedLevelText => NeedLevel > 0 ? $"Lv{NeedLevel}" : "";
    }

    private static readonly string[] Forms = ["일반형", "공격형", "방어형", "고속형", "보조형"];
    private static readonly string[] Bodies = ["에텔", "멘탈", "아스트럴", "코절", "메텔"];

    private readonly List<JobRow> _rows = [];

    public JobViewerWindow(GameDatabase db)
    {
        InitializeComponent();

        // 처음 직업이 이것인 인물들 — .chr 번호대를 통째로 훑는다(없는 번호는 건너뜀).
        var starters = Enumerable.Range(0, 1000).Select(db.Character).OfType<CharacterData>()
            .Where(c => db.T(c.NameId) is { Length: > 0 } n && !n.StartsWith('<') && !n.StartsWith('='))
            .GroupBy(c => (int)c.JobId).ToDictionary(g => g.Key, g => g.ToList());

        var placed = new HashSet<int>();
        foreach (var dep in db.Deps.Where(d => d.Id >= 1).OrderBy(d => d.Id))
        {
            int tier = (dep.Id - 1) % 3 + 1;
            string family = db.T(dep.NameId);
            for (int k = 0; k < dep.Jobs.Length; k++)
                if (dep.Jobs[k] != 0 && placed.Add(dep.Jobs[k]) && db.Jobs.TryGetValue(dep.Jobs[k], out var job))
                    _rows.Add(MakeRow(db, job, $"{family} {tier}단계{(tier < 3 && k < Forms.Length ? " · " + Forms[k] : "")}", family, tier, starters));
        }
        foreach (var job in db.Jobs.Values.OrderBy(j => j.Id))
            if (placed.Add(job.Id)) _rows.Add(MakeRow(db, job, "그 밖(몬스터·NPC)", "", 0, starters));

        List.ItemsSource = _rows;
        StatusText.Text = $"{_rows.Count}개";
        List.SelectedIndex = 0;
    }

    private static JobRow MakeRow(GameDatabase db, JobData job, string title, string family, int tier,
                                  Dictionary<int, List<CharacterData>> starters)
    {
        // 이름 칸은 6개 — 첫 칸은 체질이 아니고, 둘째부터 에텔·멘탈·아스트럴·코절·메텔 차례다(skill_list.py 와 같다).
        var names = job.NamesByBody.Skip(1).Take(Bodies.Length).Select(db.T).Select(n => n == "없음" ? "" : n).ToList();
        string joined = string.Join("/", names.Where(n => n.Length > 0).Distinct());

        string need = job.NeedAbility != 0 && db.Abilities.TryGetValue(job.NeedAbility, out var nab)
            ? $"{db.T(nab.NameId)} Lv{job.NeedAbilityLevel}" : "없음";
        var facts = new List<Pair>
        {
            new("계열", family.Length > 0 ? $"{family} {tier}단계" : "계열 없음"),
            new("필요 레벨", job.NeedLevel.ToString()),
            new("필요 어빌리티", need),
            new("누가", tier switch
            {
                1 => "제 체질 본래의 계열",
                2 => "다른 체질이 이 계열로 갈아탔을 때 (1단계 · 레벨 30 이상)",
                3 => "처음 계열이 이 계열인 사람의 최고 발전형 (2단계 · 레벨 60 이상)",
                _ => "플레이어 직업이 아님",
            }),
        };

        var byBody = names.Select((n, i) => new Pair(Bodies[i], n.Length > 0 ? n : "—")).ToList();
        int G(int i) => i < job.Growth.Length ? job.Growth[i] : 0;
        var growth = new List<Pair> { new("LP", $"{G(6)}"), new("TP", $"{G(7)}"), new("PSY", $"{G(9)}"), new("DEP", $"{G(10)}"), new("DEX", $"{G(11)}") };

        var abilities = job.AbilityList.Where(a => a != 0)
            .Select(a => db.Abilities.TryGetValue(a, out var ab) ? $"{db.T(ab.NameId)} (#{a}, 최대 Lv{ab.MaxLevel})" : $"#{a}").ToList();
        if (abilities.Count == 0) abilities.Add("없음");

        var people = starters.GetValueOrDefault(job.Id)?.Select(c => $"{db.T(c.NameId)} (Chr {c.Code:D4}, Lv{c.Level})").Distinct().ToList() ?? [];
        if (people.Count == 0) people.Add("없음");

        // 설명 TXR 의 「$n」 은 원본 창의 줄바꿈 자리다 — 여기서는 글이 알아서 접히니 뺀다.
        string description = job.DescriptionId != 0 ? db.T(job.DescriptionId).Replace("$n", "") : "";
        string search = string.Join(" ", new[] { title, joined, job.Id.ToString() }.Concat(abilities));
        return new JobRow(job.Id, title, joined.Length > 0 ? joined : "(이름 없음)", job.NeedLevel, description,
                          facts, byBody, growth, abilities, people, search);
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => Detail.DataContext = List.SelectedItem;

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string q = FilterBox.Text.Trim();
        var shown = q.Length == 0 ? _rows : _rows.Where(r => r.SearchText.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        List.ItemsSource = shown;
        StatusText.Text = $"{shown.Count}개";
        if (List.SelectedItem == null && shown.Count > 0) List.SelectedIndex = 0;
    }
}

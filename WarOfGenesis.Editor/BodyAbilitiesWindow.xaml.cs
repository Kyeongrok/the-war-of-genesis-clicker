using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 개발 &gt; 체질 — 계열 × 형마다 <b>배울 수 있는 어빌리티</b>를 더하고 뺀다. 자료는 저장소 <c>assets/data/jobs/*.json</c>(<see cref="JobBook"/>).
/// </summary>
/// <remarks>
/// 1단계(제 체질의 계열)와 2단계(다른 체질이 그 계열로 갈아탄 것)는 원본에서 따로 된 레코드지만 목록이 거의 같아서, 형마다
/// <b>1·2단계 공통 목록 한 벌</b>을 고치면 두 단계에 같이 들어간다(사용자 요청 — 예전에는 두 레코드를 따로 고쳐야 했다).
/// 2단계에만 있는 것은 「2단계에만 더함」 줄에서, 3단계와 몬스터·NPC 직업은 제 줄에서 고친다.
/// 원본 규칙: 배울 수 있는 목록 = 직업 레코드의 어빌리티 11칸 가운데 아직 없고 선행 조건을 채운 것, 차례가 곧 보이는 차례.
/// </remarks>
public partial class BodyAbilitiesWindow : Window
{
    private const int SlotCount = JobBook.SlotCount;

    /// <summary>고칠 목록 하나 — 형의 공통 목록, 2단계에만 더하는 것, 또는 통째로 든 직업의 목록.</summary>
    private sealed record JobRow(JobFile File, string Label, Func<List<int>> Get, Action<List<int>> Set, Func<int> Room, string Note);

    private sealed record SlotRow(int Index, int AbilityId, string Label);

    private sealed record AbilityRow(int Id, string Label);

    private readonly GameDatabase _db;
    private readonly string _folder;
    private List<JobFile> _files = [];
    private readonly List<JobRow> _rows = [];
    private readonly List<AbilityRow> _abilities = [];
    private readonly HashSet<JobFile> _changed = [];

    public BodyAbilitiesWindow(GameDatabase db, string jobsFolder)
    {
        InitializeComponent();
        _db = db;
        _folder = jobsFolder;
        LoadFiles();
        foreach (var ab in db.Abilities.Values.OrderBy(a => a.Id))
            _abilities.Add(new AbilityRow(ab.Id, $"{ab.Id:D4} {db.T(ab.NameId)}{CategoryTag(ab.Category)}"));
        RefreshJobList();
        RefreshAbilityPicker();
        Status($"직업 파일 {_files.Count}개 — {_folder}");
    }

    private static string CategoryTag(byte category) => category switch
    {
        0 => " (비전투)",
        2 => " (군단기)",
        3 => " (패시브)",
        _ => "",
    };

    private void LoadFiles()
    {
        _files = [.. Directory.EnumerateFiles(_folder, "*.json").Order()
            .Select(p => JobBook.FromJson(File.ReadAllText(p, Encoding.UTF8))).OfType<JobFile>().OrderBy(f => f.Family == 0 ? 99 : f.Family)];
        _changed.Clear();
        BuildRows();
    }

    /// <summary>계열 → 형(공통 · 2단계만) → 3단계 차례로 고칠 줄을 만든다. 계열에 안 든 직업은 뒤에 「그 밖」으로.</summary>
    private void BuildRows()
    {
        _rows.Clear();
        foreach (var file in _files)
        {
            foreach (var form in file.Forms)
            {
                string jobs = form.Stage2 is { } s ? $"직업 {form.Stage1.Job}·{s.Job}" : $"직업 {form.Stage1.Job}";
                _rows.Add(new JobRow(file, $"{file.Name} · {form.Form} — 1·2단계 공통 ({JobName(form.Stage1)}, {jobs})",
                    () => form.Abilities.ToList(),
                    list => { form.Abilities.Clear(); form.Abilities.AddRange(list); form.Stage2?.Remove.RemoveAll(a => !list.Contains(a)); },
                    () => SlotCount - (form.Stage2?.Add.Count ?? 0),
                    Stage2Note(form)));
                if (form.Stage2 is { Abilities: null } s2)
                    _rows.Add(new JobRow(file, $"{file.Name} · {form.Form} — 2단계에만 더함 ({JobName(s2)}, 직업 {s2.Job})",
                        () => [.. s2.Add.Select(a => a.Ability)],
                        list =>
                        {
                            // 남은 것은 제자리를 지키고, 새로 더한 것은 목록 끝에 붙는다.
                            var keep = s2.Add.Where(a => list.Contains(a.Ability)).ToList();
                            s2.Add.Clear();
                            s2.Add.AddRange(keep);
                            foreach (int a in list.Where(a => keep.All(k => k.Ability != a))) s2.Add.Add((a, SlotCount));
                        },
                        () => SlotCount - form.Abilities.Count(a => !s2.Remove.Contains(a)),
                        "공통 목록에 더해 2단계(다른 체질이 이 계열로 갈아탔을 때)에만 배울 수 있는 것."));
            }
            foreach (var stage in file.Standalone)
            {
                string label = file.Family == 0 ? $"그 밖 — {JobName(stage)} (직업 {stage.Job})" : $"{file.Name} · 3단계 — {JobName(stage)} (직업 {stage.Job})";
                _rows.Add(new JobRow(file, label, () => (stage.Abilities ?? []).ToList(), list => stage.Abilities = list, () => SlotCount, ""));
            }
        }
    }

    private string Stage2Note(JobForm form)
    {
        if (form.Stage2 is not { } s) return "1단계 전용 형(2단계 없음).";
        if (s.Abilities != null) return "2단계는 목록을 따로 든다.";
        var parts = new List<string>();
        if (s.Add.Count > 0) parts.Add($"2단계에만 더함: {string.Join(", ", s.Add.Select(a => AbilityName(a.Ability)))}");
        if (s.Remove.Count > 0) parts.Add($"2단계에서 뺌: {string.Join(", ", s.Remove.Select(AbilityName))}");
        return parts.Count == 0 ? "1·2단계 목록이 같다." : string.Join(" · ", parts);
    }

    private string AbilityName(int id) => _db.Abilities.TryGetValue(id, out var ab) ? _db.T(ab.NameId) : $"#{id}";

    /// <summary>
    /// 직업 이름 — 체질(에텔·멘탈·아스트럴·코절·메텔)마다 이름 칸이 따로 있고, 그 체질이 못 가는 칸은 TXR 「없음」이다.
    /// 1단계·3단계는 제 체질 칸 하나만, 2단계는 <b>제 체질을 뺀 넷</b>이 차 있다(분석-체질 1-2). 빈 칸을 빼고 모두 「/」로 잇는다.
    /// </summary>
    private string JobName(JobStage stage)
    {
        var names = stage.Names.Skip(1).Select(n => _db.T((ushort)n)).Where(n => n.Length > 0 && n != "없음").Distinct().ToList();
        return names.Count > 0 ? string.Join("/", names) : $"직업 {stage.Job}";
    }

    private void RefreshJobList()
    {
        string filter = JobFilterBox.Text.Trim();
        var selected = (JobList.SelectedItem as JobRow)?.Label;
        JobList.ItemsSource = filter.Length == 0 ? _rows.ToList()
            : _rows.Where(j => j.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (selected is { } label) JobList.SelectedItem = ((List<JobRow>)JobList.ItemsSource).FirstOrDefault(j => j.Label == label);
    }

    private void RefreshAbilityPicker()
    {
        string filter = AbilityFilterBox.Text.Trim();
        AbilityPicker.ItemsSource = filter.Length == 0 ? _abilities.ToList()
            : _abilities.Where(a => a.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (AbilityPicker.Items.Count > 0 && AbilityPicker.SelectedIndex < 0) AbilityPicker.SelectedIndex = 0;
    }

    private void RefreshSlots()
    {
        if (JobList.SelectedItem is not JobRow row) { SlotList.ItemsSource = null; JobTitle.Text = "왼쪽에서 직업을 고르세요"; return; }
        var slots = row.Get();
        JobTitle.Text = row.Note.Length > 0 ? $"{row.Label}\n{row.Note}" : row.Label;
        var rows = new List<SlotRow>();
        for (int k = 0; k < slots.Count; k++)
        {
            int id = slots[k];
            string label = id == 0 ? "(빈 칸)"
                : _db.Abilities.TryGetValue(id, out var ab)
                    ? $"{id:D4} {_db.T(ab.NameId)}{CategoryTag(ab.Category)}{Prereq(ab)}"
                    : $"{id:D4} (자료 없음)";
            rows.Add(new SlotRow(k, id, $"{k + 1,2}. {label}"));
        }
        SlotList.ItemsSource = rows;
    }

    private string Prereq(AbilityData ab)
    {
        var parts = new List<string>();
        if (ab.Prereq1 != 0 && _db.Abilities.TryGetValue(ab.Prereq1, out var p1)) parts.Add($"{_db.T(p1.NameId)} Lv{ab.Prereq1Level}");
        if (ab.Prereq2 != 0 && _db.Abilities.TryGetValue(ab.Prereq2, out var p2)) parts.Add($"{_db.T(p2.NameId)} Lv{ab.Prereq2Level}");
        return parts.Count == 0 ? "" : $" — 선행: {string.Join(", ", parts)}";
    }

    private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshSlots();

    private void JobFilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshJobList();

    private void AbilityFilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshAbilityPicker();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is not JobRow row) { Status("먼저 왼쪽에서 목록을 고르세요."); return; }
        if (AbilityPicker.SelectedItem is not AbilityRow pick) { Status("더할 어빌리티를 고르세요."); return; }
        var slots = row.Get();
        if (slots.Contains(pick.Id)) { Status($"{pick.Label} 은(는) 이미 목록에 있습니다."); return; }
        if (slots.Count(a => a != 0) >= row.Room()) { Status($"칸이 다 찼습니다 — 직업마다 {SlotCount}칸까지입니다(2단계 몫까지 셈). 하나를 지우고 더하세요."); return; }
        int empty = slots.IndexOf(0);
        if (empty >= 0) slots[empty] = pick.Id; else slots.Add(pick.Id);
        row.Set(slots);
        _changed.Add(row.File);
        RefreshSlots();
        Status($"{pick.Label} 을(를) 더했습니다. 저장을 눌러야 파일에 남습니다.");
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is not JobRow row) { Status("먼저 왼쪽에서 목록을 고르세요."); return; }
        if (SlotList.SelectedItem is not SlotRow slot) { Status("지울 어빌리티 칸을 고르세요."); return; }
        var slots = row.Get();
        slots.RemoveAt(slot.Index);            // 뒤 칸을 앞으로 당긴다 — 목록 차례가 곧 「배울 수 있는」 차례다
        row.Set(slots);
        _changed.Add(row.File);
        RefreshSlots();
        Status($"{slot.Label.Trim()} 을(를) 뺐습니다. 저장을 눌러야 파일에 남습니다.");
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var file in _changed)
                File.WriteAllText(Path.Combine(_folder, JobBook.FileName(file)), JobBook.ToJson(file), new UTF8Encoding(false));
            int n = _changed.Count;
            _changed.Clear();
            Status($"저장했습니다 — 파일 {n}개({_folder}). 게임을 다시 켜면 반영됩니다.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status($"저장하지 못했습니다: {ex.Message}");
        }
    }

    private void RevertButton_Click(object sender, RoutedEventArgs e)
    {
        LoadFiles();
        RefreshJobList();
        RefreshSlots();
        Status("파일에 있는 대로 되돌렸습니다.");
    }

    private void Status(string text) => StatusText.Text = (_changed.Count > 0 ? "[저장 안 됨] " : "") + text;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_changed.Count > 0 && MessageBox.Show(this, "저장하지 않은 변경이 있습니다. 그냥 닫을까요?", "체질 어빌리티",
                                                  MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
        base.OnClosing(e);
    }
}

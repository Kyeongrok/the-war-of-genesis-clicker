using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 개발 &gt; 체질 — 계열마다 형(일반·공격·방어·고속·보조)별 <b>배울 수 있는 어빌리티</b>를 표 하나로 보고 더하고 뺀다.
/// 자료는 저장소 <c>assets/data/jobs/*.json</c>(<see cref="JobBook"/>).
/// </summary>
/// <remarks>
/// 왼쪽은 계열 한 줄씩, 오른쪽 표는 <b>열 = 형과 3단계 직업</b>, <b>행 = 어빌리티 칸</b>이다(사용자 요청 — 예전에는 계열 × 형 × 단계가
/// 한 줄씩 펼쳐져 길었다). 위쪽 「공통」 행은 1·2단계가 같이 배우는 목록, 아래 「2단계만」 행은 다른 체질이 이 계열로 갈아탔을 때만
/// 더 배우는 것이다. 원본 규칙: 배울 수 있는 목록 = 직업 레코드의 11칸 가운데 아직 없고 선행 조건을 채운 것, 차례가 곧 보이는 차례.
/// </remarks>
public partial class BodyAbilitiesWindow : Window
{
    private const int SlotCount = JobBook.SlotCount;

    /// <summary>표의 열 하나 — 형(공통 목록과 2단계 전용 목록) 또는 목록을 통째로 든 직업(3단계·몬스터).</summary>
    private sealed record Column(string Header, Func<List<int>> Get, Action<List<int>> Set, Func<int> Room,
                                 Func<List<int>>? Get2 = null, Action<List<int>>? Set2 = null, Func<int>? Room2 = null, string Note = "");

    /// <summary>표의 행 — 「공통」(2단계 아님) 또는 「2단계만」의 몇째 칸. 칸 글은 열 차례로.</summary>
    public sealed record TableRow(string Label, bool Stage2, int Index, string[] Cells);

    private sealed record FamilyRow(JobFile File, string Label);

    private sealed record AbilityRow(int Id, string Label);

    private readonly GameDatabase _db;
    private readonly string _folder;
    private List<JobFile> _files = [];
    private readonly List<AbilityRow> _abilities = [];
    private readonly HashSet<JobFile> _changed = [];
    private List<Column> _columns = [];

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
        JobList.SelectedIndex = 0;
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
    }

    private void RefreshJobList()
    {
        string filter = JobFilterBox.Text.Trim();
        var selected = (JobList.SelectedItem as FamilyRow)?.File.Family;
        var rows = _files.Select(f => new FamilyRow(f, f.Family == 0 ? $"그 밖 ({f.Standalone.Count}개)" : f.Name))
            .Where(r => filter.Length == 0 || r.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)
                        || BuildColumns(r.File).Any(c => c.Header.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
        JobList.ItemsSource = rows;
        if (selected is { } family) JobList.SelectedItem = rows.FirstOrDefault(r => r.File.Family == family);
    }

    /// <summary>그 계열의 열 — 형 다섯(공통 + 2단계 전용), 3단계 직업들. 「그 밖」은 직업마다 한 열.</summary>
    private List<Column> BuildColumns(JobFile file)
    {
        var columns = new List<Column>();
        foreach (var form in file.Forms)
        {
            var s2 = form.Stage2 is { Abilities: null } s ? s : null;
            string jobs = form.Stage2 is { } st ? $"직업 {form.Stage1.Job}·{st.Job}" : $"직업 {form.Stage1.Job}";
            columns.Add(new Column($"{form.Form}\n{JobName(form.Stage1)}\n{jobs}",
                () => form.Abilities.ToList(),
                list => { form.Abilities.Clear(); form.Abilities.AddRange(list); form.Stage2?.Remove.RemoveAll(a => !list.Contains(a)); },
                () => SlotCount - (s2?.Add.Count ?? 0),
                s2 == null ? null : () => [.. s2.Add.Select(a => a.Ability)],
                s2 == null ? null : list =>
                {
                    // 남은 것은 제자리를 지키고, 새로 더한 것은 목록 끝에 붙는다.
                    var keep = s2.Add.Where(a => list.Contains(a.Ability)).ToList();
                    s2.Add.Clear();
                    s2.Add.AddRange(keep);
                    foreach (int a in list.Where(a => keep.All(k => k.Ability != a))) s2.Add.Add((a, SlotCount));
                },
                s2 == null ? null : () => SlotCount - form.Abilities.Count(a => a != 0 && !s2.Remove.Contains(a)),
                s2 is { Remove.Count: > 0 } ? $"{form.Form} 2단계에서 뺌: {string.Join(", ", s2.Remove.Select(AbilityName))}" : ""));
        }
        foreach (var stage in file.Standalone)
            columns.Add(new Column(file.Family == 0 ? $"{JobName(stage)}\n직업 {stage.Job}" : $"3단계\n{JobName(stage)}\n직업 {stage.Job}",
                () => (stage.Abilities ?? []).ToList(), list => stage.Abilities = list, () => SlotCount));
        return columns;
    }

    private string AbilityName(int id) => id == 0 ? "" : _db.Abilities.TryGetValue(id, out var ab) ? _db.T(ab.NameId) : $"#{id}";

    /// <summary>직업 이름 — 체질마다 이름 칸이 있고 못 가는 칸은 TXR 「없음」이다. 빈 칸을 빼고 「/」로 잇는다(분석-체질 1-2).</summary>
    private string JobName(JobStage stage)
    {
        var names = stage.Names.Skip(1).Select(n => _db.T((ushort)n)).Where(n => n.Length > 0 && n != "없음").Distinct().ToList();
        return names.Count > 0 ? string.Join("/", names) : $"직업 {stage.Job}";
    }

    /// <summary>고른 계열의 표를 다시 그린다 — 칸 선택은 되도록 지킨다.</summary>
    private void RefreshTable()
    {
        var keep = (Table.CurrentCell.Column?.DisplayIndex, Table.CurrentCell.Item as TableRow);
        if (JobList.SelectedItem is not FamilyRow family) { Table.ItemsSource = null; Table.Columns.Clear(); JobTitle.Text = "왼쪽에서 계열을 고르세요"; return; }
        _columns = BuildColumns(family.File);
        var common = _columns.Select(c => c.Get()).ToList();
        var stage2 = _columns.Select(c => c.Get2?.Invoke() ?? []).ToList();
        int commonRows = Math.Max(1, common.Max(l => l.Count));
        int stage2Rows = _columns.Any(c => c.Get2 != null) ? Math.Max(1, stage2.Max(l => l.Count)) : 0;

        var rows = new List<TableRow>();
        for (int i = 0; i < commonRows; i++)
            rows.Add(new TableRow($"공통 {i + 1}", false, i, [.. common.Select(l => i < l.Count ? AbilityName(l[i]) : "")]));
        for (int i = 0; i < stage2Rows; i++)
            rows.Add(new TableRow($"2단계만 {i + 1}", true, i, [.. stage2.Select(l => i < l.Count ? AbilityName(l[i]) : "")]));

        Table.Columns.Clear();
        Table.Columns.Add(new DataGridTextColumn { Header = "칸", Binding = new Binding(nameof(TableRow.Label)), FontWeight = FontWeights.SemiBold });
        for (int c = 0; c < _columns.Count; c++)
            Table.Columns.Add(new DataGridTextColumn { Header = _columns[c].Header, Binding = new Binding($"Cells[{c}]"), MinWidth = 110 });
        Table.ItemsSource = rows;

        var notes = _columns.Select(c => c.Note).Where(n => n.Length > 0).ToList();
        JobTitle.Text = family.File.Family == 0
            ? "그 밖 — 계열에 안 든 직업(몬스터·NPC). 열마다 목록을 통째로 든다."
            : $"{family.File.Name} — 「공통」은 1·2단계가 같이 배우고, 「2단계만」은 다른 체질이 이 계열로 갈아탔을 때만 더 배운다."
              + (notes.Count > 0 ? "\n" + string.Join(" · ", notes) : "");

        if (keep.Item1 is int col && col < Table.Columns.Count && keep.Item2 is { } old
            && rows.FirstOrDefault(r => r.Stage2 == old.Stage2 && r.Index == old.Index) is { } again)
            Table.CurrentCell = new DataGridCellInfo(again, Table.Columns[col]);
    }

    private void RefreshAbilityPicker()
    {
        string filter = AbilityFilterBox.Text.Trim();
        AbilityPicker.ItemsSource = filter.Length == 0 ? _abilities.ToList()
            : _abilities.Where(a => a.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (AbilityPicker.Items.Count > 0 && AbilityPicker.SelectedIndex < 0) AbilityPicker.SelectedIndex = 0;
    }

    /// <summary>지금 고른 칸 — (열, 행). 칸 열(0번)이나 아무것도 안 골랐으면 null.</summary>
    private (Column Col, TableRow Row)? Picked() =>
        Table.CurrentCell.Column?.DisplayIndex is int d && d >= 1 && d - 1 < _columns.Count && Table.CurrentCell.Item is TableRow row
            ? (_columns[d - 1], row) : null;

    private void JobList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshTable();

    private void JobFilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshJobList();

    private void AbilityFilterBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshAbilityPicker();

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is not FamilyRow family) { Status("먼저 왼쪽에서 계열을 고르세요."); return; }
        if (Picked() is not { } picked) { Status("더할 형(열)의 칸을 하나 고르세요."); return; }
        if (AbilityPicker.SelectedItem is not AbilityRow pick) { Status("더할 어빌리티를 고르세요."); return; }
        var col = picked.Col;
        bool toStage2 = Stage2Only.IsChecked == true;
        if (toStage2 && col.Get2 == null) { Status("이 열에는 2단계가 없습니다 — 「2단계에만」을 끄고 더하세요."); return; }
        var list = toStage2 ? col.Get2!() : col.Get();
        if (list.Contains(pick.Id) || (!toStage2 && col.Get2?.Invoke().Contains(pick.Id) == true))
        { Status($"{pick.Label} 은(는) 이미 이 형의 목록에 있습니다."); return; }
        if (list.Count(a => a != 0) >= (toStage2 ? col.Room2!() : col.Room()))
        { Status($"칸이 다 찼습니다 — 직업마다 {SlotCount}칸까지입니다(공통과 2단계 몫을 합쳐 셈). 하나를 지우고 더하세요."); return; }
        int empty = list.IndexOf(0);
        if (empty >= 0) list[empty] = pick.Id; else list.Add(pick.Id);
        if (toStage2) col.Set2!(list); else col.Set(list);
        _changed.Add(family.File);
        RefreshTable();
        Status($"{col.Header.Split('\n')[0]}{(toStage2 ? " 2단계" : "")}에 {pick.Label} 을(를) 더했습니다. 저장을 눌러야 파일에 남습니다.");
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is not FamilyRow family) { Status("먼저 왼쪽에서 계열을 고르세요."); return; }
        if (Picked() is not { } picked) { Status("지울 어빌리티 칸을 고르세요."); return; }
        var (col, row) = picked;
        if (row.Stage2 && col.Get2 == null) { Status("그 칸은 비어 있습니다."); return; }
        var list = row.Stage2 ? col.Get2!() : col.Get();
        if (row.Index >= list.Count) { Status("그 칸은 비어 있습니다."); return; }
        string name = AbilityName(list[row.Index]);
        list.RemoveAt(row.Index);             // 뒤 칸을 앞으로 당긴다 — 목록 차례가 곧 「배울 수 있는」 차례다
        if (row.Stage2) col.Set2!(list); else col.Set(list);
        _changed.Add(family.File);
        RefreshTable();
        Status($"{col.Header.Split('\n')[0]}{(row.Stage2 ? " 2단계" : "")}에서 {name} 을(를) 뺐습니다. 저장을 눌러야 파일에 남습니다.");
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
        RefreshTable();
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

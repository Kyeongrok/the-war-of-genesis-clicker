using System.ComponentModel;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 스킬 편집 — <c>assets/data/skills/NNNN.json</c>(<see cref="SkillBook"/>)을 「공통 칸 한 벌 + 레벨마다 다른 칸」으로 고친다.
/// </summary>
/// <remarks>
/// 공통 칸은 한 번 고치면 모든 레벨에 든다. 칸을 레벨별로 내리면 모든 레벨에 지금 값이 복사되고, 레벨별 열을 공통으로 올리면 첫 레벨 값을 쓴다.
/// work·레벨 번호는 고치지 않는다 — 기술 연출·AI·전투 스크립트(행동 207)가 work 번호로 기술을 가리킨다.
/// </remarks>
public partial class SkillEditView : UserControl
{
    public sealed class SkillRow
    {
        public required SkillFile Skill { get; set; }
        public string Path { get; init; } = "";
        public int Ability => Skill.Ability;
        public string Name => Skill.Ability > 0 ? Skill.Name : $"(work {Skill.Levels.FirstOrDefault()?.Work})";
        public int Levels => Skill.Levels.Count;

        /// <summary>work 번호 — 한 레벨이면 그 번호, 여럿이면 첫 레벨…끝 레벨. 기본공격(근접 work 1·원거리 work 6)도 어빌리티 이름으로만 떠서 찾기 어려웠다.</summary>
        public string Works => Skill.Levels.Count switch
        {
            0 => "",
            1 => Skill.Levels[0].Work.ToString(),
            _ => $"{Skill.Levels[0].Work}…{Skill.Levels[^1].Work}",
        };
        public int Varying => Skill.Levels.SelectMany(l => l.Fields.Keys).Where(k => k != "att").Distinct().Count();
        public string Dirty { get; set; } = "";

        /// <summary>레벨 줄을 지웠나 — 저장할 때 .abi 최대 레벨도 줄 수로 줄인다.</summary>
        public bool LevelsTrimmed { get; set; }
    }

    public sealed class CommonRow
    {
        public string Field { get; init; } = "";
        public int Value { get; set; }
        public string Meaning { get; init; } = "";

        /// <summary>정해진 값이 있는 칸이면 고를 거리(<see cref="TargetMode"/> 등) — 없으면 null 이고 수로 적는다.</summary>
        public IReadOnlyList<EnumChoices.Choice>? Choices { get; init; }

        /// <summary>표에 보일 글 — 고를 거리가 있으면 그 이름(「3 아무 칸」), 없으면 수.</summary>
        public string Display => Choices?.FirstOrDefault(c => c.Value == Value)?.Label ?? Value.ToString();
        public Visibility ChoicesVisibility => Choices != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility NumberVisibility => Choices == null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>스피너로 고치는 칸이면 한 번에 움직일 양(tp 10 · areaMax 1) — 이 칸은 편집 상태 없이 늘 스피너로 보인다.</summary>
        public double? Step { get; init; }
        public double SpinnerStep => Step ?? 1;
        public bool Signed { get; init; }
        public double SpinnerMin => Signed ? -32768 : 0;
        public double SpinnerMax => Signed ? 32767 : 65535;
        public Visibility SpinnerVisibility => Step != null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility TextVisibility => Step == null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>칸 이름 → 그 칸의 고를 거리(enum 이 있는 칸만).</summary>
    private static readonly Dictionary<string, IReadOnlyList<EnumChoices.Choice>> Choices =
        SkillBook.Fields.Where(f => f.Enum != null).ToDictionary(f => f.Name, f => EnumChoices.Of(f.Enum!));

    private readonly List<SkillRow> _rows = [];
    private ICollectionView? _view;
    private string _folder = "";
    private bool _filling;

    /// <summary>어빌리티 이름·설명(TXR)을 읽으려고 드는 저장소 자료 — 못 읽으면 설명 없이 연다.</summary>
    private GameDatabase? _db;

    public SkillEditView()
    {
        InitializeComponent();
        FilterBox.Text = LoadFilter();   // 지난번 찾기 글 — 목록이 채워지면 이 글로 거른다
        Loaded += (_, _) => { if (_folder.Length == 0) Load(); };
        PreviewKeyDown += OnPreviewKeyDownForUndo;   // 창에 다시 붙어도 한 번만 읽는다
    }

    private SkillRow? Current => SkillGrid.SelectedItem as SkillRow;

    private void Load()
    {
        try { _folder = System.IO.Path.Combine(AssetsFolder.Find("data"), "skills"); }
        catch (DirectoryNotFoundException) { StatusText.Text = "저장소 assets/data 를 못 찾았습니다."; return; }
        if (!Directory.Exists(_folder)) { StatusText.Text = $"{_folder} 가 없습니다."; return; }
        try { _db = GameDatabase.Load(GameFiles.FromFolder(AssetsFolder.Find("data"))); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { _db = null; }
        foreach (string path in Directory.EnumerateFiles(_folder, "*.json"))
            if (SkillBook.FromJson(File.ReadAllText(path, Encoding.UTF8)) is { } skill)
            {
                var added = new SkillRow { Skill = skill, Path = path };
                _rows.Add(added);
                Remember(added);
            }
        _view = CollectionViewSource.GetDefaultView(_rows.OrderBy(r => r.Ability == 0).ThenBy(r => r.Ability).ThenBy(r => r.Name).ToList());
        _view.Filter = o => o is SkillRow r && Matches(r);
        SkillGrid.ItemsSource = _view;
        UpdateStatus();
    }

    /// <summary>어빌리티 설명(abi +0x1c TXR, 「$n」 줄바꿈)과 분류·최대 레벨·선행 조건 — 전에는 스킬 편집에 설명이 아예 안 보였다(사용자 보고).</summary>
    private string DescriptionOf(int abilityId)
    {
        if (_db == null) return "(설명을 읽지 못했습니다 — 저장소 TXR 자료가 없습니다)";
        if (!_db.Abilities.TryGetValue(abilityId, out var ab)) return abilityId == 0 ? "(어빌리티에 안 묶인 work)" : $"어빌리티 {abilityId} 자료 없음";
        string text = _db.T(ab.DescriptionId).Replace("$n", Environment.NewLine);
        var extra = new List<string> { $"최대 Lv{ab.MaxLevel}" };
        if (ab.Prereq1 != 0 && _db.Abilities.TryGetValue(ab.Prereq1, out var p1)) extra.Add($"선행 {_db.T(p1.NameId)} Lv{ab.Prereq1Level}");
        if (ab.Prereq2 != 0 && _db.Abilities.TryGetValue(ab.Prereq2, out var p2)) extra.Add($"선행 {_db.T(p2.NameId)} Lv{ab.Prereq2Level}");
        return (text.Length > 0 ? text : "(설명 없음)") + Environment.NewLine + string.Join(" · ", extra);
    }

    private bool Matches(SkillRow r)
    {
        string q = FilterBox.Text.Trim();
        return q.Length == 0 || r.Name.Replace(" ", "").Contains(q.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)
               || r.Ability.ToString() == q || r.Skill.Levels.Any(l => l.Work.ToString() == q);
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        FilterClear.Visibility = FilterBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _view?.Refresh();
        SaveFilter(FilterBox.Text);
    }

    private void FilterClear_Click(object sender, RoutedEventArgs e)
    {
        FilterBox.Clear();
        FilterBox.Focus();
    }

    /// <summary>찾기 글을 두는 곳 — 게임 폴더 자리(gameroot.txt)와 같은 편집기 설정 폴더.</summary>
    private static readonly string FilterPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WarOfGenesis.Editor", "skillfilter.txt");

    private static string LoadFilter()
    {
        try { return File.Exists(FilterPath) ? File.ReadAllText(FilterPath) : ""; }
        catch (IOException) { return ""; }
    }

    private static void SaveFilter(string text)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilterPath)!);
            File.WriteAllText(FilterPath, text);
        }
        catch (IOException) { /* 못 남겨도 이번에 쓰는 데는 지장 없다 */ }
    }

    private void UpdateStatus()
    {
        int dirty = _rows.Count(r => r.Dirty.Length > 0);
        StatusText.Text = $"스킬 파일 {_rows.Count}개" + (dirty > 0 ? $" · 저장 안 한 스킬 {dirty}개" : "");
    }

    private void SkillGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => Fill();

    /// <summary>「범위」 탭 칸 — 레코드에서 rangeShape 부터 areaMode 까지(사거리·높이·시야·대상 방식·효과 범위).</summary>
    private static readonly HashSet<string> RangeFields = [.. SkillBook.Fields.Select(f => f.Name)
        .SkipWhile(n => n != "rangeShape").TakeWhile(n => n != "kind")];

    private void CommonTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == CommonTabs && IsLoaded) Fill();
    }

    private void Fill()
    {
        if (Current is not { } row) { CommonGrid.ItemsSource = null; LevelGrid.ItemsSource = null; SkillDescription.Text = ""; return; }
        SkillDescription.Text = DescriptionOf(row.Ability);
        _filling = true;
        var meaning = SkillBook.Fields.ToDictionary(f => f.Name, f => f.Meaning);
        bool rangeTab = CommonTabs.SelectedItem != OtherTab;
        var common = row.Skill.Common.Where(kv => RangeFields.Contains(kv.Key) == rangeTab).ToList();
        CommonGrid.ItemsSource = common.Select(kv => new CommonRow
        {
            Field = kv.Key, Value = kv.Value, Meaning = meaning.GetValueOrDefault(kv.Key, ""), Choices = Choices.GetValueOrDefault(kv.Key),
            Step = SpinnerSteps.TryGetValue(kv.Key, out double step) ? step : null,
            Signed = SkillBook.Fields.FirstOrDefault(f => f.Name == kv.Key)?.Signed == true,
        }).ToList();
        int ranges = row.Skill.Common.Keys.Count(RangeFields.Contains);
        RangeTab.Header = $"범위 ({ranges})";
        OtherTab.Header = $"나머지 ({row.Skill.Common.Count - ranges})";
        CommonHeader.Text = $"공통 — 모든 레벨이 같은 칸 ({row.Skill.Common.Count}개)";

        var table = new DataTable();
        table.Columns.Add("level", typeof(int)).ReadOnly = true;
        table.Columns.Add("work", typeof(int)).ReadOnly = true;
        var varying = VaryingFields(row.Skill);
        foreach (string f in varying) table.Columns.Add(f, typeof(int));
        foreach (var level in row.Skill.Levels)
        {
            var dr = table.NewRow();
            dr["level"] = level.Level;
            dr["work"] = level.Work;
            foreach (string f in varying) dr[f] = level.Fields.GetValueOrDefault(f);
            table.Rows.Add(dr);
        }
        // 스피너(tp)처럼 편집 상태를 거치지 않고 바뀐 값도 스킬 자료에 옮긴다 — CellEditEnding 은 편집 상태에서만 온다.
        table.ColumnChanged += (_, ev) =>
        {
            if (_filling || ev.Column is not { } col || !varying.Contains(col.ColumnName)) return;
            int i = table.Rows.IndexOf(ev.Row);
            if (i < 0 || i >= row.Skill.Levels.Count || ev.Row[col] is not int v) return;
            if (row.Skill.Levels[i].Fields.GetValueOrDefault(col.ColumnName) == v) return;
            row.Skill.Levels[i].Fields[col.ColumnName] = v;
            MarkDirty(row);
        };
        LevelGrid.ItemsSource = table.DefaultView;
        _filling = false;
    }

    /// <summary>레벨별 칸 — 칸 표 차례대로(att 는 빼고).</summary>
    private static List<string> VaryingFields(SkillFile skill)
    {
        var names = skill.Levels.SelectMany(l => l.Fields.Keys).Where(k => k != "att").ToHashSet();
        return [.. SkillBook.Fields.Select(f => f.Name).Where(names.Contains)];
    }

    // ── 되돌리기(Ctrl+Z) ─────────────────────────────────────────────────────

    /// <summary>스킬마다 마지막으로 본 상태(JSON) — 고칠 때 이것을 되돌리기 더미에 쌓는다.</summary>
    private readonly Dictionary<SkillRow, string> _lastJson = [];

    /// <summary>되돌리기 더미 — 고친 스킬과 그 직전 상태. 스피너 한 칸·셀 하나·단추 하나가 한 걸음이다.</summary>
    private readonly Stack<(SkillRow Row, string Json, bool Trimmed)> _undo = new();

    private const int UndoLimit = 500;

    private void Remember(SkillRow row) => _lastJson[row] = SkillBook.ToJson(row.Skill);

    /// <summary>직전에 고친 것 하나를 되돌린다(사용자 요청: Ctrl+Z). 저장한 뒤라도 되돌리면 다시 「고침」 표시가 붙는다.</summary>
    private void Undo()
    {
        if (_undo.Count == 0) { StatusText.Text = "되돌릴 것이 없습니다."; return; }
        var (row, json, trimmed) = _undo.Pop();
        if (SkillBook.FromJson(json) is not { } skill) return;
        row.Skill = skill;
        row.LevelsTrimmed = trimmed;
        _lastJson[row] = json;
        row.Dirty = DiskJson(row) == json ? "" : "●";
        if (SkillGrid.SelectedItem != row) { SkillGrid.SelectedItem = row; SkillGrid.ScrollIntoView(row); }
        else Fill();
        SkillGrid.Items.Refresh();
        UpdateStatus();
        StatusText.Text = $"{row.Name} 을(를) 한 걸음 되돌렸습니다 (남은 되돌리기 {_undo.Count}).";
    }

    /// <summary>파일에 저장된 상태를 같은 모양의 JSON 으로 — 되돌린 결과가 저장본과 같으면 「고침」 표시를 뗀다.</summary>
    private static string? DiskJson(SkillRow row)
    {
        try { return SkillBook.FromJson(File.ReadAllText(row.Path, Encoding.UTF8)) is { } s ? SkillBook.ToJson(s) : null; }
        catch (IOException) { return null; }
    }

    private void OnPreviewKeyDownForUndo(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Z || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (Keyboard.FocusedElement == FilterBox) return;          // 찾기 칸은 글자 되돌리기를 그대로 둔다
        CommonGrid.CancelEdit();
        LevelGrid.CancelEdit();
        Undo();
        e.Handled = true;
    }

    private void MarkDirty(SkillRow row)
    {
        string now = SkillBook.ToJson(row.Skill);
        if (_lastJson.TryGetValue(row, out var before) && before != now)
        {
            _undo.Push((row, before, row.LevelsTrimmed));
            if (_undo.Count > UndoLimit) { var keep = _undo.Take(UndoLimit).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
        }
        _lastJson[row] = now;
        row.Dirty = "●";
        SkillGrid.Items.Refresh();
        UpdateStatus();
    }

    /// <summary>레벨별 표 — 고를 거리가 있는 칸(대상 방식 등이 레벨마다 다를 때)은 드롭다운 열로 바꾼다.</summary>
    /// <summary>스피너로 고치는 레벨별 칸과 한 번에 오르내리는 폭 — tp 는 10씩(사용자 요청).</summary>
    private static readonly Dictionary<string, double> SpinnerSteps = new() { ["tp"] = 10, ["areaMax"] = 1, ["rangeMax"] = 1, ["bonus1Value"] = 5, ["power"] = 5, ["exp"] = 5 };

    private void LevelGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (SpinnerSteps.TryGetValue(e.PropertyName, out double step))
        {
            // 늘 스피너로 보인다 — 칸을 편집 상태로 바꾸지 않고 ▲▼·휠·↑↓ 로 바로 고친다. 값은 표(DataTable)에 바로 들어가고
            // 표의 ColumnChanged 가 스킬 자료로 옮긴다(Fill).
            var spinner = new FrameworkElementFactory(typeof(Controls.NumericSpinner));
            spinner.SetBinding(Controls.NumericSpinner.ValueProperty,
                new Binding($"[{e.PropertyName}]") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            spinner.SetValue(Controls.NumericSpinner.StepProperty, step);
            // 부호 있는 칸(power·bonus 값 — 약화는 음수)은 음수도 둔다.
            bool signed = SkillBook.Fields.FirstOrDefault(f => f.Name == e.PropertyName)?.Signed == true;
            spinner.SetValue(Controls.NumericSpinner.MinimumProperty, signed ? -32768.0 : 0.0);
            spinner.SetValue(Controls.NumericSpinner.MaximumProperty, signed ? 32767.0 : 65535.0);
            e.Column = new DataGridTemplateColumn { Header = e.PropertyName, MinWidth = 70, CellTemplate = new DataTemplate { VisualTree = spinner } };
            return;
        }
        if (!Choices.TryGetValue(e.PropertyName, out var choices)) return;
        e.Column = new DataGridComboBoxColumn
        {
            Header = e.PropertyName,
            ItemsSource = choices,
            DisplayMemberPath = "Label",
            SelectedValuePath = "Value",
            SelectedValueBinding = new Binding($"[{e.PropertyName}]"),
        };
    }

    /// <summary>공통 표의 스피너 칸 — 편집 상태를 거치지 않으니 값이 바뀔 때마다 스킬 자료로 바로 옮긴다.</summary>
    private void CommonSpinner_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_filling || Current is not { } row || (sender as FrameworkElement)?.DataContext is not CommonRow c) return;
        int v = (int)e.NewValue;
        if (row.Skill.Common.GetValueOrDefault(c.Field) == v) return;
        c.Value = v;
        row.Skill.Common[c.Field] = v;
        MarkDirty(row);
    }

    private void CommonGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (_filling || e.EditAction != DataGridEditAction.Commit || Current is not { } row) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (CommonGrid.ItemsSource is not List<CommonRow> list) return;
            foreach (var c in list) row.Skill.Common[c.Field] = c.Value;
            MarkDirty(row);
            CommonGrid.Items.Refresh();         // 드롭다운으로 고른 이름(「3 아무 칸」)을 칸에 다시 보인다
        });
    }

    private void LevelGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (_filling || e.EditAction != DataGridEditAction.Commit || Current is not { } row) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (LevelGrid.ItemsSource is not DataView view) return;
            var varying = VaryingFields(row.Skill);
            for (int i = 0; i < view.Count && i < row.Skill.Levels.Count; i++)
                foreach (string f in varying)
                    if (view[i][f] is int v) row.Skill.Levels[i].Fields[f] = v;
            MarkDirty(row);
        });
    }

    /// <summary>공통 칸 하나를 레벨별로 내린다 — 모든 레벨에 지금 값을 넣는다.</summary>
    private void ToLevels_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row || CommonGrid.SelectedItem is not CommonRow c) { StatusText.Text = "공통 표에서 칸을 하나 고르세요."; return; }
        foreach (var level in row.Skill.Levels) level.Fields[c.Field] = c.Value;
        row.Skill.Common.Remove(c.Field);
        MarkDirty(row);
        Fill();
    }

    /// <summary>work 번호 → 그 work 을 쓰는 곳 설명들(전투 이벤트 필살기·기본공격 인물). 처음 볼 때 한 번 훑는다.</summary>
    private Dictionary<int, List<string>>? _workRefs;

    private Dictionary<int, List<string>> WorkRefIndex()
    {
        if (_workRefs != null) return _workRefs;
        var map = new Dictionary<int, List<string>>();
        void Add(int work, string where) { if (!map.TryGetValue(work, out var l)) map[work] = l = []; if (!l.Contains(where)) l.Add(where); }
        try
        {
            string data = AssetsFolder.Find("data");
            var files = GameFiles.FromFolder(data);
            // 전투 이벤트 행동 207 「기술 시전(보스 필살기)」 — 인자 2 가 work 번호(0x10052400).
            foreach (string path in Directory.EnumerateFiles(System.IO.Path.Combine(data, "Btl"), "*.btl"))
            {
                if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(path), out int id)) continue;
                foreach (var e in BattleEvents.Parse(File.ReadAllBytes(path)) ?? [])
                    foreach (var a in e.Actions.Where(a => a.Code == 207 && a.Args.Length > 2))
                        Add(a.Args[2], $"Btl {id:D4} 이벤트 {e.Index} 필살기");
            }
            // 기본공격 — .chr 의 기본공격 work.
            if (_db != null)
                foreach (string path in Directory.EnumerateFiles(System.IO.Path.Combine(data, "Chr"), "*.chr"))
                    if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(path), out int code) && _db.Character(code) is { } c)
                        Add(c.BasicWorkId, $"기본공격 {_db.T(c.NameId)}({code:D4})");
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException or InvalidDataException) { }
        return _workRefs = map;
    }

    private void LevelGrid_CurrentCellChanged(object? sender, EventArgs e)
    {
        if (LevelGrid.CurrentCell.Item is not DataRowView view || view.Row["work"] is not int work || view.Row["level"] is not int level)
        {
            WorkRefs.Text = "";
            return;
        }
        var refs = WorkRefIndex().GetValueOrDefault(work) ?? [];
        const int Shown = 12;
        string list = refs.Count == 0 ? "어빌리티 레벨 말고는 쓰는 곳 없음"
            : string.Join(" · ", refs.Take(Shown)) + (refs.Count > Shown ? $" 외 {refs.Count - Shown}곳" : "");
        WorkRefs.Text = $"Lv{level} = work {work} — {list}";
    }

    /// <summary>
    /// 고른 셀의 레벨 줄을 지운다 — 뒤 레벨을 하나씩 당겨(10 → 9 …) 빈 레벨이 안 생기게 한다. 9 만 빼고 두면 Lv8 인물이 Lv9 에 오를 때
    /// 쓸 work 이 없어 기술이 사라지고, 다음 레벨 EXP 도 0 이 된다. work 번호는 그대로 둔다(연출·AI·전투 스크립트가 work 로 가리킨다).
    /// 최대 레벨(.abi +4)은 저장할 때 줄 수에 맞춰 줄인다.
    /// </summary>
    private void DeleteLevel_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;
        if (LevelGrid.CurrentCell.Item is not DataRowView view || view.Row["level"] is not int level)
        {
            StatusText.Text = "레벨별 표에서 지울 레벨의 셀을 하나 고르세요.";
            return;
        }
        if (row.Skill.Levels.Count <= 1) { StatusText.Text = "마지막 한 레벨은 지울 수 없습니다."; return; }
        int index = row.Skill.Levels.FindIndex(l => l.Level == level);
        if (index < 0) return;
        if (MessageBox.Show(Window.GetWindow(this)!, $"{row.Name} Lv{level} (work {row.Skill.Levels[index].Work}) 줄을 지우고 뒤 레벨을 하나씩 당길까요?",
                            "레벨 지우기", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        row.Skill.Levels.RemoveAt(index);
        for (int i = index; i < row.Skill.Levels.Count; i++) row.Skill.Levels[i].Level--;
        row.LevelsTrimmed = true;
        MarkDirty(row);
        Fill();
        StatusText.Text = $"Lv{level} 을(를) 지웠습니다 — 이제 최대 Lv{row.Skill.Levels.Count}. 저장하면 반영됩니다.";
    }

    /// <summary>레벨별 열 하나를 공통으로 올린다 — 첫 레벨 값이 모든 레벨의 값이 된다.</summary>
    private void ToCommon_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;
        string? field = LevelGrid.CurrentCell.Column?.Header as string;
        if (field is null or "level" or "work") { StatusText.Text = "레벨별 표에서 올릴 칸의 셀을 하나 고르세요."; return; }
        int value = row.Skill.Levels.FirstOrDefault()?.Fields.GetValueOrDefault(field) ?? 0;
        var distinct = row.Skill.Levels.Select(l => l.Fields.GetValueOrDefault(field)).Distinct().Count();
        if (distinct > 1 && MessageBox.Show(Window.GetWindow(this)!, $"「{field}」 는 레벨마다 값이 다릅니다. 첫 레벨 값 {value} 로 모두 맞출까요?", "공통으로",
                                            MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        foreach (var level in row.Skill.Levels) level.Fields.Remove(field);
        // 칸 표 차례를 지키며 공통에 넣는다.
        var common = new Dictionary<string, int>(row.Skill.Common) { [field] = value };
        row.Skill.Common = SkillBook.Fields.Where(f => common.ContainsKey(f.Name)).ToDictionary(f => f.Name, f => common[f.Name]);
        MarkDirty(row);
        Fill();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveDirty();

    private bool SaveDirty()
    {
        var dirty = _rows.Where(r => r.Dirty.Length > 0).ToList();
        if (dirty.Count == 0) { StatusText.Text = "고친 스킬이 없습니다."; return true; }
        try
        {
            foreach (var r in dirty)
            {
                File.WriteAllText(r.Path, SkillBook.ToJson(r.Skill), new UTF8Encoding(false));
                if (r.LevelsTrimmed && r.Ability > 0 && _db?.Abilities.GetValueOrDefault(r.Ability) is { } ab)
                {
                    // 원본에서 이미 최대 레벨이 줄 수보다 작은 어빌리티(희생 10·20줄 따위)는 그 값을 넘기지 않는다.
                    int max = Math.Min(ab.MaxLevel, r.Skill.Levels.Count);
                    GameDatabase.WriteAbilityMaxLevel(System.IO.Path.GetDirectoryName(_folder)!, r.Ability, max);
                    r.LevelsTrimmed = false;
                }
                r.Dirty = "";
            }
            SkillGrid.Items.Refresh();
            StatusText.Text = $"스킬 {string.Join(", ", dirty.Select(r => r.Name))} 을(를) 저장했습니다 — 게임을 다시 켜면 반영됩니다.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"저장하지 못했습니다: {ex.Message}";
            return false;
        }
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row || SkillBook.FromJson(File.ReadAllText(row.Path, Encoding.UTF8)) is not { } skill) return;
        // 되돌리기 전 상태도 더미에 넣어, 「이 스킬 되돌리기」 자체를 Ctrl+Z 로 무를 수 있게 한다.
        if (_lastJson.TryGetValue(row, out var before)) _undo.Push((row, before, row.LevelsTrimmed));
        row.Skill = skill;
        row.Dirty = "";
        Remember(row);
        SkillGrid.Items.Refresh();
        Fill();
        UpdateStatus();
    }

    /// <summary>그 어빌리티를 골라 보여 준다 — 스킬 창에서 부른다.</summary>
    public void SelectAbility(int ability)
    {
        FilterBox.Text = "";
        if (_rows.FirstOrDefault(r => r.Ability == ability) is { } row) { SkillGrid.SelectedItem = row; SkillGrid.ScrollIntoView(row); }
    }

    /// <summary>창을 닫아도 되나 — 저장 안 한 스킬이 있으면 물어본다(아니오·저장 성공이면 true, 취소면 false). 편집기 첫 화면이 닫힐 때 부른다.</summary>
    public bool ConfirmClose()
    {
        int dirty = _rows.Count(r => r.Dirty.Length > 0);
        if (dirty == 0) return true;
        var answer = MessageBox.Show(Window.GetWindow(this)!, $"저장 안 한 스킬이 {dirty}개 있습니다. 저장할까요?", "스킬 편집", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer != MessageBoxResult.Cancel && (answer != MessageBoxResult.Yes || SaveDirty());
    }
}

using System.ComponentModel;
using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
public partial class SkillEditWindow : Window
{
    public sealed class SkillRow
    {
        public required SkillFile Skill { get; set; }
        public string Path { get; init; } = "";
        public int Ability => Skill.Ability;
        public string Name => Skill.Ability > 0 ? Skill.Name : $"(work {Skill.Levels.FirstOrDefault()?.Work})";
        public int Levels => Skill.Levels.Count;
        public int Varying => Skill.Levels.SelectMany(l => l.Fields.Keys).Where(k => k != "att").Distinct().Count();
        public string Dirty { get; set; } = "";
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

    public SkillEditWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Load();
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
                _rows.Add(new SkillRow { Skill = skill, Path = path });
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

    private void Filter_Changed(object sender, TextChangedEventArgs e) => _view?.Refresh();

    private void UpdateStatus()
    {
        int dirty = _rows.Count(r => r.Dirty.Length > 0);
        StatusText.Text = $"스킬 파일 {_rows.Count}개" + (dirty > 0 ? $" · 저장 안 한 스킬 {dirty}개" : "");
    }

    private void SkillGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => Fill();

    private void Fill()
    {
        if (Current is not { } row) { CommonGrid.ItemsSource = null; LevelGrid.ItemsSource = null; SkillDescription.Text = ""; return; }
        SkillDescription.Text = DescriptionOf(row.Ability);
        _filling = true;
        var meaning = SkillBook.Fields.ToDictionary(f => f.Name, f => f.Meaning);
        CommonGrid.ItemsSource = row.Skill.Common.Select(kv => new CommonRow
        {
            Field = kv.Key, Value = kv.Value, Meaning = meaning.GetValueOrDefault(kv.Key, ""), Choices = Choices.GetValueOrDefault(kv.Key),
        }).ToList();
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

    private void MarkDirty(SkillRow row)
    {
        row.Dirty = "●";
        SkillGrid.Items.Refresh();
        UpdateStatus();
    }

    /// <summary>레벨별 표 — 고를 거리가 있는 칸(대상 방식 등이 레벨마다 다를 때)은 드롭다운 열로 바꾼다.</summary>
    /// <summary>스피너로 고치는 레벨별 칸과 한 번에 오르내리는 폭 — tp 는 10씩(사용자 요청).</summary>
    private static readonly Dictionary<string, double> SpinnerSteps = new() { ["tp"] = 10 };

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
            spinner.SetValue(Controls.NumericSpinner.MinimumProperty, 0.0);
            spinner.SetValue(Controls.NumericSpinner.MaximumProperty, 65535.0);
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

    /// <summary>레벨별 열 하나를 공통으로 올린다 — 첫 레벨 값이 모든 레벨의 값이 된다.</summary>
    private void ToCommon_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;
        string? field = LevelGrid.CurrentCell.Column?.Header as string;
        if (field is null or "level" or "work") { StatusText.Text = "레벨별 표에서 올릴 칸의 셀을 하나 고르세요."; return; }
        int value = row.Skill.Levels.FirstOrDefault()?.Fields.GetValueOrDefault(field) ?? 0;
        var distinct = row.Skill.Levels.Select(l => l.Fields.GetValueOrDefault(field)).Distinct().Count();
        if (distinct > 1 && MessageBox.Show(this, $"「{field}」 는 레벨마다 값이 다릅니다. 첫 레벨 값 {value} 로 모두 맞출까요?", "공통으로",
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
        row.Skill = skill;
        row.Dirty = "";
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

    protected override void OnClosing(CancelEventArgs e)
    {
        int dirty = _rows.Count(r => r.Dirty.Length > 0);
        if (dirty > 0)
        {
            var answer = MessageBox.Show(this, $"저장 안 한 스킬이 {dirty}개 있습니다. 저장할까요?", "스킬 편집", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !SaveDirty())) e.Cancel = true;
        }
        base.OnClosing(e);
    }
}

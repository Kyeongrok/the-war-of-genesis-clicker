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
        // 아이콘 고르기 — 0 번은 「원본」(.abi 값을 쓴다), 그다음이 값 0, 1, 2 …
        IconSideBox.ItemsSource = IconSideChoices;
        IconAreaBox.ItemsSource = IconAreaChoices;
        IconKindBox.ItemsSource = IconKindChoices;
        // 채우기에 고를 칸 — 수로 적는 칸만(고를 거리가 정해진 칸은 사이를 채울 뜻이 없다). 자주 고치는 칸을 앞에 둔다.
        string[] first = ["power", "tp", "soul", "exp", "accuracy", "critical", "hpFactor", "bonus1Value", "bonus2Value", "bonus3Value", "rangeMax", "areaMax"];
        var numeric = SkillBook.Fields.Where(f => f.Enum == null && f.Name != "att").Select(f => f.Name).ToList();
        FillFieldBox.ItemsSource = first.Where(numeric.Contains).Concat(numeric.Where(n => !first.Contains(n))).ToList();
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
        try { FillBodyFilter(GameFiles.FromFolder(AssetsFolder.Find("data"))); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { BodyFilterBox.IsEnabled = false; }
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
    /// <param name="maxLevel">스킬 파일의 maxLevel(0 이면 .abi 값을 보인다).</param>
    private string DescriptionOf(int abilityId, int maxLevel = 0)
    {
        if (_db == null) return "(설명을 읽지 못했습니다 — 저장소 TXR 자료가 없습니다)";
        if (!_db.Abilities.TryGetValue(abilityId, out var ab)) return abilityId == 0 ? "(어빌리티에 안 묶인 work)" : $"어빌리티 {abilityId} 자료 없음";
        string text = _db.T(ab.DescriptionId).Replace("$n", Environment.NewLine);
        var extra = new List<string> { maxLevel > 0 ? $"최대 Lv{maxLevel} (원본 {ab.MaxLevel})" : $"최대 Lv{ab.MaxLevel}" };
        if (ab.Prereq1 != 0 && _db.Abilities.TryGetValue(ab.Prereq1, out var p1)) extra.Add($"선행 {_db.T(p1.NameId)} Lv{ab.Prereq1Level}");
        if (ab.Prereq2 != 0 && _db.Abilities.TryGetValue(ab.Prereq2, out var p2)) extra.Add($"선행 {_db.T(p2.NameId)} Lv{ab.Prereq2Level}");
        return (text.Length > 0 ? text : "(설명 없음)") + Environment.NewLine + string.Join(" · ", extra);
    }

    private bool Matches(SkillRow r)
    {
        if (BodyFilterBox.SelectedItem is BodyChoice { Abilities: { } allowed } && !allowed.Contains(r.Ability)) return false;
        string q = FilterBox.Text.Trim();
        return q.Length == 0 || r.Name.Replace(" ", "").Contains(q.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)
               || r.Ability.ToString() == q || r.Skill.Levels.Any(l => l.Work.ToString() == q);
    }

    /// <summary>체질 고르기 줄 — 「전체」는 거르지 않는다(Abilities = null).</summary>
    private sealed record BodyChoice(string Label, HashSet<int>? Abilities)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// 체질(계열) 고르기 채우기 — 계열마다 그 계열 직업들(형 다섯 × 1·2단계 + 3단계)이 <b>배울 수 있는</b> 어빌리티를 모은다(assets/data/jobs, 사용자 요청 ed-sk-3).
    /// 「그 밖」은 계열에 안 든 직업(몬스터·NPC) 것이다.
    /// </summary>
    private void FillBodyFilter(GameFiles files)
    {
        var choices = new List<BodyChoice> { new("전체", null) };
        foreach (var file in JobBook.Load(files).OrderBy(f => f.Family == 0 ? 99 : f.Family))
        {
            var set = JobBook.Expand(file).SelectMany(j => j.AbilityList).Where(a => a != 0).Select(a => (int)a).ToHashSet();
            choices.Add(new BodyChoice(file.Family == 0 ? "그 밖 (몬스터·NPC)" : file.Name, set));
        }
        BodyFilterBox.ItemsSource = choices;
        BodyFilterBox.SelectedIndex = 0;
    }

    private void BodyFilter_Changed(object sender, SelectionChangedEventArgs e) => _view?.Refresh();

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
        if (Current is not { } row) { CommonGrid.ItemsSource = null; LevelGrid.ItemsSource = null; SkillDescription.Text = ""; DescriptionOverride.Text = ""; return; }
        SkillDescription.Text = DescriptionOf(row.Ability, row.Skill.MaxLevel);
        DescriptionOverride.Text = row.Skill.Description.Replace("$n", Environment.NewLine);
        DescriptionOverride.IsEnabled = row.Ability > 0;   // 어빌리티에 안 묶인 work 는 설명 창이 없다
        _iconFilling = true;
        IconSideBox.SelectedIndex = Math.Clamp(row.Skill.IconSide + 1, 0, IconSideChoices.Length - 1);
        IconAreaBox.SelectedIndex = Math.Clamp(row.Skill.IconArea + 1, 0, IconAreaChoices.Length - 1);
        IconKindBox.SelectedIndex = Math.Clamp(row.Skill.IconKind + 1, 0, IconKindChoices.Length - 1);
        _iconFilling = false;
        IconSideBox.IsEnabled = IconAreaBox.IsEnabled = IconKindBox.IsEnabled = row.Ability > 0;
        _filling = true;
        var meaning = SkillBook.Fields.ToDictionary(f => f.Name, f => f.Meaning);
        // 탭 넷 — 범위 · 나머지 · AI(AI 가 쓸지 고를 때 보는 칸) · 기타(뜻을 모르는 bNN 칸, 사용자 요청).
        static int TabOf(string field) =>
            field.Length > 1 && field[0] == 'b' && field.Skip(1).All(char.IsDigit) ? 3
            : field is "minTargets" || field.StartsWith("ai", StringComparison.Ordinal) ? 2
            : RangeFields.Contains(field) ? 0 : 1;
        int tab = CommonTabs.SelectedItem == MiscTab ? 3 : CommonTabs.SelectedItem == AiTab ? 2 : CommonTabs.SelectedItem == OtherTab ? 1 : 0;
        var common = row.Skill.Common.Where(kv => TabOf(kv.Key) == tab).ToList();
        CommonGrid.ItemsSource = common.Select(kv => new CommonRow
        {
            Field = kv.Key, Value = kv.Value, Meaning = meaning.GetValueOrDefault(kv.Key, ""), Choices = Choices.GetValueOrDefault(kv.Key),
            Step = SpinnerSteps.TryGetValue(kv.Key, out double step) ? step : null,
            Signed = SkillBook.Fields.FirstOrDefault(f => f.Name == kv.Key)?.Signed == true,
        }).ToList();
        int Count(int t) => row.Skill.Common.Keys.Count(k => TabOf(k) == t);
        RangeTab.Header = $"범위 ({Count(0)})";
        OtherTab.Header = $"나머지 ({Count(1)})";
        AiTab.Header = $"AI ({Count(2)})";
        MiscTab.Header = $"기타 ({Count(3)})";
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
        UpdateFillBoxes();
        UpdateRangePreview();
    }

    // ── 레벨 채우기 ─────────────────────────────────────────────────────────

    /// <summary>칸의 지금 값 — 그 레벨에 따로 적힌 값이 있으면 그것, 없으면 공통 값.</summary>
    private static int ValueAt(SkillFile skill, int levelIndex, string field) =>
        (uint)levelIndex < (uint)skill.Levels.Count && skill.Levels[levelIndex].Fields.TryGetValue(field, out int v) ? v : skill.Common.GetValueOrDefault(field);

    /// <summary>채우기 칸의 첫·끝 값을 고른 칸의 지금 값(첫 레벨 · 끝 레벨)으로 채워 둔다.</summary>
    private void UpdateFillBoxes()
    {
        if (Current is not { } row || FillFieldBox.SelectedItem is not string field) return;
        FillStartBox.Text = ValueAt(row.Skill, 0, field).ToString();
        FillEndBox.Text = ValueAt(row.Skill, row.Skill.Levels.Count - 1, field).ToString();
    }

    private void FillField_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateFillBoxes();

    /// <summary>
    /// 첫 레벨 값에서 끝 레벨 값까지 고르게 채운다(사용자 요청 ed-sk-9) — 레벨마다 하나씩 넣지 않아도 된다.
    /// 공통 칸이면 레벨별 칸으로 내려서 넣는다. 「단위」 가 5 면 값을 5 의 배수로 맞춘다. 한 번이 되돌리기 한 걸음이다.
    /// </summary>
    private void FillApply_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) { StatusText.Text = "스킬을 고르세요."; return; }
        if (FillFieldBox.SelectedItem is not string field) { StatusText.Text = "채울 칸을 고르세요."; return; }
        if (!int.TryParse(FillStartBox.Text.Trim(), out int start) || !int.TryParse(FillEndBox.Text.Trim(), out int end))
        { StatusText.Text = "첫 레벨 값과 끝 레벨 값을 수로 넣으세요."; return; }
        int unit = int.TryParse(FillUnitBox.Text.Trim(), out int u) && u > 0 ? u : 1;
        var info = SkillBook.Fields.First(f => f.Name == field);
        long max = (1L << (8 * info.Size)) - 1, low = info.Signed ? -(max + 1) / 2 : 0, high = info.Signed ? max / 2 : max;
        if (start < low || start > high || end < low || end > high) { StatusText.Text = $"「{field}」 에는 {low}~{high} 만 넣을 수 있습니다."; return; }
        LevelGrid.CommitEdit(DataGridEditingUnit.Row, true);
        int n = row.Skill.Levels.Count;
        for (int i = 0; i < n; i++)
        {
            double exact = n == 1 ? start : start + (end - start) * (double)i / (n - 1);
            int value = (int)Math.Clamp(Math.Round(exact / unit, MidpointRounding.AwayFromZero) * unit, low, high);
            // 양 끝은 넣은 값 그대로 — 단위에 안 맞아도 사용자가 적은 값이 우선이다.
            row.Skill.Levels[i].Fields[field] = i == 0 ? start : i == n - 1 ? end : value;
        }
        row.Skill.Common.Remove(field);
        MarkDirty(row);
        Fill();
        StatusText.Text = $"{row.Name}: 「{field}」 를 Lv1 {start} → Lv{n} {end} 로 채웠습니다 (Ctrl+Z 로 되돌림, 저장해야 반영).";
    }

    // ── 범위 미리보기 ────────────────────────────────────────────────────────

    private const int PreviewRadius = 10, PreviewCell = 11;

    /// <summary>미리보기에서 겨눈 칸(시전자 기준) — 사거리 안의 칸을 누르면 옮긴다. 사거리가 바뀌어 밖이 되면 다시 고른다.</summary>
    private (int Dx, int Dy) _previewAim = (0, -1);

    /// <summary>레벨별 표에서 고른 줄(없으면 첫 레벨) — 미리보기가 그 레벨의 값으로 그린다.</summary>
    private int PreviewLevelIndex(SkillRow row)
    {
        var view = LevelGrid.CurrentCell.Item as DataRowView ?? LevelGrid.SelectedCells.Select(c => c.Item).OfType<DataRowView>().FirstOrDefault();
        int index = view != null && view.Row["level"] is int level ? row.Skill.Levels.FindIndex(l => l.Level == level) : 0;
        return Math.Max(0, index);
    }

    /// <summary>
    /// 사거리(파랑)와 겨눈 칸의 효과 범위(빨강)를 평지 격자에 그린다 — 게임과 같은 모양 표(<see cref="WorkShape"/>)를 쓴다.
    /// 높이·시야·지형은 뺀 그림이다. 시전자는 가운데, 위를 본다.
    /// </summary>
    private void UpdateRangePreview()
    {
        RangeCanvas.Children.Clear();
        if (Current is not { } row || row.Skill.Levels.Count == 0) { RangeCaption.Text = ""; return; }
        int li = PreviewLevelIndex(row);
        int V(string field) => ValueAt(row.Skill, li, field);
        int rangeShape = V("rangeShape"), rangeMin = V("rangeMin"), rangeMax = V("rangeMax"), rangeKind = V("rangeKind"), mode = V("targetMode");
        int areaShape = V("areaShape"), areaMin = V("areaMin"), areaMax = V("areaMax");
        bool self = mode is 0 or 2 || rangeShape == 0;
        // 무기 사거리를 쓰는 기술(종류 2·4)은 무기 사거리를 1칸으로 보고 그린다.
        const int weapon = 1;
        int maxCells = rangeKind switch { 2 => weapon, 4 => weapon + rangeMax, _ => rangeMax };
        int minQ = rangeMin == 0 ? 0 : rangeMin * 4 - 3, maxQ = maxCells * 4;

        bool InRange(int dx, int dy)
        {
            if (self) return dx == 0 && dy == 0;
            int d = 4 * (Math.Abs(dx) + Math.Abs(dy));
            if (!WorkShape.UsesFacing(rangeShape)) return WorkShape.Reaches(rangeShape, dx, dy, 3, minQ, maxQ, d, d);
            for (int way = 0; way < 4; way++)
                if (WorkShape.Reaches(rangeShape, dx, dy, way, minQ, maxQ, d, d)) return true;
            return false;
        }

        if (self) _previewAim = (0, 0);
        else if (Math.Abs(_previewAim.Dx) > PreviewRadius || Math.Abs(_previewAim.Dy) > PreviewRadius || !InRange(_previewAim.Dx, _previewAim.Dy))
        {
            // 위쪽으로 가장 먼 칸부터, 없으면 사거리 안 아무 칸.
            (int, int)? pick = null;
            for (int k = PreviewRadius; k >= 0 && pick == null; k--) if (InRange(0, -k)) pick = (0, -k);
            for (int dy = -PreviewRadius; dy <= PreviewRadius && pick == null; dy++)
                for (int dx = -PreviewRadius; dx <= PreviewRadius && pick == null; dx++) if (InRange(dx, dy)) pick = (dx, dy);
            _previewAim = pick ?? (0, 0);
        }
        var (ax, ay) = _previewAim;
        // 시전자 → 겨눈 칸 쪽을 본다(게임의 FacingToward 와 같은 규칙). 제자리면 위.
        int facing = ax == 0 && ay == 0 ? 0 : Math.Abs(ax) >= Math.Abs(ay) ? (ax >= 0 ? 3 : 2) : (ay >= 0 ? 1 : 0);
        int areaMinQ = areaMin * (4 * areaMin - 3), areaMaxQ = areaMax * 4;

        bool InArea(int dx, int dy)        // 겨눈 칸 기준
        {
            if (areaShape == 0) return dx == 0 && dy == 0;
            int d = 4 * (Math.Abs(dx) + Math.Abs(dy));
            if (areaShape == 4) return d >= areaMinQ && Math.Abs(dx) <= 8;        // 화면 전체 — 가로 8칸까지(세로는 화면 높이)
            if (dx == 0 && dy == 0) return WorkShape.Covers(areaShape, 0, 0, facing) && 0 >= areaMinQ;
            return WorkShape.Reaches(areaShape, dx, dy, facing, areaMinQ, areaMaxQ, d, d);
        }

        var rangeBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x5F, 0xB8));
        var areaBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC8, 0x3C, 0x3C));
        var bothBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB0, 0x4C, 0xC0));
        var emptyBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x24, 0x36));
        int rangeCount = 0, areaCount = 0;
        for (int dy = -PreviewRadius; dy <= PreviewRadius; dy++)
            for (int dx = -PreviewRadius; dx <= PreviewRadius; dx++)
            {
                bool r = InRange(dx, dy), a = InArea(dx - ax, dy - ay);
                if (r) rangeCount++;
                if (a) areaCount++;
                var cell = new System.Windows.Shapes.Rectangle
                {
                    Width = PreviewCell - 1, Height = PreviewCell - 1,
                    Fill = r && a ? bothBrush : a ? areaBrush : r ? rangeBrush : emptyBrush,
                };
                Canvas.SetLeft(cell, (dx + PreviewRadius) * PreviewCell);
                Canvas.SetTop(cell, (dy + PreviewRadius) * PreviewCell);
                RangeCanvas.Children.Add(cell);
            }
        // 시전자(초록 점)와 겨눈 칸(흰 테두리).
        var caster = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = System.Windows.Media.Brushes.LimeGreen };
        Canvas.SetLeft(caster, PreviewRadius * PreviewCell + 1.5);
        Canvas.SetTop(caster, PreviewRadius * PreviewCell + 1.5);
        RangeCanvas.Children.Add(caster);
        var aim = new System.Windows.Shapes.Rectangle { Width = PreviewCell - 1, Height = PreviewCell - 1, Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1.5 };
        Canvas.SetLeft(aim, (ax + PreviewRadius) * PreviewCell);
        Canvas.SetTop(aim, (ay + PreviewRadius) * PreviewCell);
        RangeCanvas.Children.Add(aim);

        string lv = row.Skill.Levels.Count > 1 ? $"Lv{row.Skill.Levels[li].Level} · " : "";
        string note = rangeKind is 2 or 4 ? " 무기 사거리를 쓰는 기술이라 무기 1칸으로 보고 그렸다." : "";
        RangeCaption.Text = $"{lv}사거리 {rangeCount}칸(파랑) · 효과 범위 {areaCount}칸(빨강) · 초록 점 = 시전자, 흰 테두리 = 겨눈 칸. "
                            + (self ? "제자리에 쓰는 기술." : "파란 칸을 누르면 겨눈 칸을 옮긴다.") + " 평지 기준(높이·시야 제외)." + note;
    }

    private void RangeCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var at = e.GetPosition(RangeCanvas);
        int dx = (int)(at.X / PreviewCell) - PreviewRadius, dy = (int)(at.Y / PreviewCell) - PreviewRadius;
        if (Math.Abs(dx) > PreviewRadius || Math.Abs(dy) > PreviewRadius) return;
        var before = _previewAim;
        _previewAim = (dx, dy);
        UpdateRangePreview();              // 사거리 밖을 눌렀으면 미리보기가 다시 고른다
        if (_previewAim != (dx, dy)) { _previewAim = before; UpdateRangePreview(); }
    }

    /// <summary>
    /// 설명 덮어쓰기 칸을 떠날 때 스킬 자료에 옮긴다 — 글자마다 옮기면 되돌리기(Ctrl+Z)가 한 글자씩 쌓인다.
    /// 줄바꿈은 원본 설명처럼 <c>$n</c> 으로 적는다(게임 설명 창이 <c>$n</c> 에서 줄을 바꾼다).
    /// </summary>
    // ── 목록 아이콘 덮어쓰기 ───────────────────────────────────────────────────

    /// <summary>아이콘 고르기 칸의 줄 — 0 번이 「원본」, i 번이 값 i−1(분석-스킬 「아이콘」 표).</summary>
    private static readonly string[] IconSideChoices = ["원본 (.abi)", "0 파랑 — 아군", "1 빨강 — 적", "2 노랑 — 피아 무관"];
    private static readonly string[] IconAreaChoices = ["원본 (.abi)", "0 한 사람", "1 여럿"];
    private static readonly string[] IconKindChoices = ["원본 (.abi)", "0 攻 공격", "1 回 회복", "2 異 보조", "3 軍 군단기", "4 必 필살기"];

    private bool _iconFilling;

    /// <summary>아이콘을 고르면 스킬 자료(iconSide·iconArea·iconKind, −1 = 원본)에 옮긴다 — 한 번 고른 것이 되돌리기 한 걸음.</summary>
    private void Icon_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_iconFilling || Current is not { } row || row.Ability <= 0) return;
        int side = IconSideBox.SelectedIndex - 1, area = IconAreaBox.SelectedIndex - 1, kind = IconKindBox.SelectedIndex - 1;
        if (side == row.Skill.IconSide && area == row.Skill.IconArea && kind == row.Skill.IconKind) return;
        row.Skill.IconSide = side;
        row.Skill.IconArea = area;
        row.Skill.IconKind = kind;
        MarkDirty(row);
        StatusText.Text = $"{row.Name}: 목록 아이콘을 바꿨습니다 (저장해야 반영).";
    }

    private void DescriptionOverride_LostFocus(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row || row.Ability <= 0) return;
        string text = DescriptionOverride.Text.Trim().Replace("\r\n", "\n").Replace("\n", "$n");
        if (text == row.Skill.Description) return;
        row.Skill.Description = text;
        MarkDirty(row);
        StatusText.Text = text.Length == 0 ? $"{row.Name}: 원본 설명으로 되돌렸습니다 (저장해야 반영)." : $"{row.Name}: 설명을 바꿨습니다 (저장해야 반영).";
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
    private readonly Stack<(SkillRow Row, string Json)> _undo = new();

    private const int UndoLimit = 500;

    private void Remember(SkillRow row) => _lastJson[row] = SkillBook.ToJson(row.Skill);

    /// <summary>직전에 고친 것 하나를 되돌린다(사용자 요청: Ctrl+Z). 저장한 뒤라도 되돌리면 다시 「고침」 표시가 붙는다.</summary>
    private void Undo()
    {
        if (_undo.Count == 0) { StatusText.Text = "되돌릴 것이 없습니다."; return; }
        var (row, json) = _undo.Pop();
        if (SkillBook.FromJson(json) is not { } skill) return;
        row.Skill = skill;
        _lastJson[row] = json;
        row.Dirty = DiskJson(row) == json ? "" : "●";
        if (SkillGrid.SelectedItem != row) { SkillGrid.SelectedItem = row; SkillGrid.ScrollIntoView(row); }
        else Fill();
        RefreshGrid(SkillGrid);
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
            _undo.Push((row, before));
            if (_undo.Count > UndoLimit) { var keep = _undo.Take(UndoLimit).Reverse().ToList(); _undo.Clear(); foreach (var k in keep) _undo.Push(k); }
        }
        _lastJson[row] = now;
        row.Dirty = "●";
        RefreshGrid(SkillGrid);
        UpdateStatus();
        if (row == Current) UpdateRangePreview();
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

    /// <summary>
    /// 표를 다시 그린다 — 다만 <b>칸을 고치는 중이면 건너뛴다</b>. 값을 확정하고 곧바로 다음 칸을 고치기 시작하면(소울 비용을 잇달아 고칠 때)
    /// 미뤄 둔 새로 고침이 그 편집 도중에 돌아 「AddNew 또는 EditItem 트랜잭션 중에는 Refresh 를 쓸 수 없습니다」로 편집기가 죽었다(사용자 보고).
    /// 건너뛴 새로 고침은 그 편집이 확정될 때 다시 돈다.
    /// </summary>
    private static void RefreshGrid(DataGrid grid)
    {
        if (grid.Items is System.ComponentModel.IEditableCollectionView { IsEditingItem: true } or System.ComponentModel.IEditableCollectionView { IsAddingNew: true }) return;
        grid.Items.Refresh();
    }

    private void CommonGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (_filling || e.EditAction != DataGridEditAction.Commit || Current is not { } row) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (CommonGrid.ItemsSource is not List<CommonRow> list) return;
            foreach (var c in list) row.Skill.Common[c.Field] = c.Value;
            MarkDirty(row);
            RefreshGrid(CommonGrid);         // 드롭다운으로 고른 이름(「3 아무 칸」)을 칸에 다시 보인다
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
        if (!_filling) UpdateRangePreview();     // 고른 레벨의 값으로 다시 그린다
    }

    /// <summary>
    /// 고른 셀의 레벨 줄을 지운다 — 뒤 레벨을 하나씩 당겨(10 → 9 …) 빈 레벨이 안 생기게 한다. 9 만 빼고 두면 Lv8 인물이 Lv9 에 오를 때
    /// 쓸 work 이 없어 기술이 사라지고, 다음 레벨 EXP 도 0 이 된다. work 번호는 그대로 둔다(연출·AI·전투 스크립트가 work 로 가리킨다).
    /// 최대 레벨(.abi +4)은 저장할 때 줄 수에 맞춰 줄인다.
    /// </summary>
    /// <summary>
    /// 「게임에서 실험」 — 고친 것을 저장한 뒤 게임(<c>WarOfGenesis.exe</c>)을 <c>DUELDX_ARENA=어빌리티:레벨</c> 로 띄운다(게임 쪽 <c>GameWindow.ArenaScene</c>).
    /// 게임 실행 파일은 편집기 곁(설치판·배포판)에서 찾고, 없으면 저장소의 <c>duel-dx/bin</c> 아래에서 가장 최근에 빌드한 것을 쓴다.
    /// </summary>
    private void TryInGame_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row || row.Ability <= 0) { StatusText.Text = "어빌리티에 묶인 스킬을 고르세요."; return; }
        LevelGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var view = LevelGrid.CurrentCell.Item as DataRowView
                   ?? LevelGrid.SelectedCells.Select(c => c.Item).OfType<DataRowView>().FirstOrDefault()
                   ?? LevelGrid.SelectedItem as DataRowView;
        int level = view != null && int.TryParse(Convert.ToString(view.Row["level"]), out int chosen) ? chosen : 1;
        SaveDirty();
        // 게임이 편집기를 띄웠으면 제 자리를 알려 준다(WAROFGENESIS_GAME).
        string? exe = Environment.GetEnvironmentVariable("WAROFGENESIS_GAME") is { Length: > 0 } given && System.IO.File.Exists(given) ? given : null;
        string beside = System.IO.Path.Combine(AppContext.BaseDirectory, "WarOfGenesis.exe");
        if (exe != null) { }
        else if (System.IO.File.Exists(beside)) exe = beside;
        else
            for (var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory); dir != null && exe == null; dir = dir.Parent)
            {
                string bin = System.IO.Path.Combine(dir.FullName, "duel-dx", "bin");
                if (!System.IO.Directory.Exists(bin)) continue;
                // 시험용으로 빌드한 폴더(sweeptest 따위)가 아니라 평소 돌리는 Debug·Release 것을 쓴다.
                exe = new[] { "Debug", "Release" }
                    .Select(c => System.IO.Path.Combine(bin, c))
                    .Where(System.IO.Directory.Exists)
                    .SelectMany(c => System.IO.Directory.EnumerateFiles(c, "WarOfGenesis.exe", System.IO.SearchOption.AllDirectories))
                    .OrderByDescending(System.IO.File.GetLastWriteTimeUtc).FirstOrDefault();
            }
        if (exe == null) { StatusText.Text = "게임 실행 파일(WarOfGenesis.exe)을 못 찾았습니다 — 게임을 한 번 빌드하세요."; return; }
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(exe) { WorkingDirectory = System.IO.Path.GetDirectoryName(exe)!, UseShellExecute = false };
            start.Environment["DUELDX_ARENA"] = $"{row.Ability}:{level}";
            start.Environment["DUELDX_TITLE"] = "0";
            start.Environment["DUELDX_BATTLE"] = "106";      // 실험은 Btl 0106 에서(사용자 요청)
            System.Diagnostics.Process.Start(start);
            StatusText.Text = $"{row.Name} Lv{level} 실험판을 띄웠습니다 — 값을 고친 뒤 다시 누르면 새 값으로 다시 뜹니다.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
        {
            StatusText.Text = $"게임을 못 띄웠습니다: {ex.Message}";
        }
    }

    private void DeleteLevel_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;
        // 고른 줄 — 지금 셀이 없으면(단추를 누르며 초점이 옮겨 가거나, 줄 머리로 고른 때) 고른 셀·고른 줄에서 찾는다.
        // 전에는 지금 셀만 봐서, 줄을 골라 두고 단추를 눌러도 「셀을 고르세요」만 뜨고 안 지워졌다(사용자 보고: 메테오).
        LevelGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var view = LevelGrid.CurrentCell.Item as DataRowView
                   ?? LevelGrid.SelectedCells.Select(c => c.Item).OfType<DataRowView>().FirstOrDefault()
                   ?? LevelGrid.SelectedItem as DataRowView;
        if (view == null || !int.TryParse(Convert.ToString(view.Row["level"]), out int level))
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
        // 최대 레벨은 .abi 를 안 고치고 스킬 파일의 maxLevel 에 적는다(사용자 요청). 원본에서 이미 최대 레벨이 줄 수보다
        // 작은 어빌리티(희생 10·20줄 따위)는 그 값을 넘기지 않는다.
        int abiMax = _db?.Abilities.GetValueOrDefault(row.Ability)?.MaxLevel ?? row.Skill.Levels.Count + 1;
        int before = row.Skill.MaxLevel > 0 ? row.Skill.MaxLevel : abiMax;
        // 배울 수 있는 레벨 안의 줄을 지웠으면 최대 레벨도 하나 준다 — 메테오처럼 줄(20)이 최대 레벨(10)보다 많은 어빌리티는
        // 뒤 줄이 당겨 올라와 최대 레벨이 그대로라 「안 지워진」 것처럼 보였다(사용자 보고).
        if (row.Ability > 0) row.Skill.MaxLevel = Math.Max(1, Math.Min(level <= before ? before - 1 : before, row.Skill.Levels.Count));
        MarkDirty(row);
        Fill();
        StatusText.Text = $"Lv{level} 을(를) 지웠습니다 — 이제 최대 Lv{(row.Skill.MaxLevel > 0 ? row.Skill.MaxLevel : row.Skill.Levels.Count)} (.abi 는 그대로, 스킬 파일에 적음). 저장하면 반영됩니다.";
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
                r.Dirty = "";
            }
            RefreshGrid(SkillGrid);
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
        if (_lastJson.TryGetValue(row, out var before)) _undo.Push((row, before));
        row.Skill = skill;
        row.Dirty = "";
        Remember(row);
        RefreshGrid(SkillGrid);
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

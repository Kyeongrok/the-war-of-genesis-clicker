using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 파트1 기술(<c>Dats\Attack.dat</c> — 레벨 한 칸이 기술 한 칸) 편집 창 — 세기 식 · 더하는 세기 · 피해 % · 명중률 · 타수 · 속성 · 소울을 표에서 바로 고친다.
/// 원본 Attack.dat 은 사용자의 파트1 폴더에서 읽기만 하고, 고친 레벨만 <c>assets/data/part1-attacks/NNNN.json</c> 에 적는다(<see cref="Part1AttackBook"/>,
/// 사용자 요청 ed-sk-10). 게임은 켤 때 이 표를 읽는다.
/// </summary>
public partial class Part1AttackEditWindow : Window
{
    /// <summary>표 한 줄(기술 한 레벨) — 칸을 고치면 <see cref="Attack"/> 이 따라 바뀐다.</summary>
    public sealed class AttackRow : INotifyPropertyChanged
    {
        public Part1Attack Attack { get; set; } = null!;
        public Part1Attack Original { get; init; } = null!;
        /// <summary>이 레벨이 속한 어빌리티(Abi.dat) 번호들과 그 안의 레벨 — 어빌리티가 안 쓰는 기술(용병 · 적 전용)은 비었다.</summary>
        public List<int> Abilities { get; } = [];
        public int Level { get; set; }
        public bool Dirty { get; set; }

        public int Id => Attack.Id;
        public string Name => Attack.Name;
        public bool Changed => !Part1AttackBook.SameContent(Attack, Original);
        public string Mark => Dirty ? "●" : Changed ? "고침" : "";

        private int Get(int at) => Attack.Raw[at];
        private void Put(int at, int value)
        {
            var raw = new Dictionary<int, int>(Attack.Raw) { [at] = value };
            Set(Attack with { Raw = raw });
        }

        public int Formula { get => Get(0x42); set => Put(0x42, Math.Clamp(value, 0, 255)); }
        public string FormulaText => Part1AttackBook.FormulaName(Formula);
        public int PowerAdd { get => Get(0x44); set => Put(0x44, value); }
        public int DamagePercent { get => Attack.DamagePercent; set => Set(Attack with { DamagePercent = Math.Clamp(value, 0, 1000) }); }
        public int Accuracy { get => Get(0x48); set => Put(0x48, Math.Clamp(value, 0, 255)); }
        public int Hits { get => Get(0x66); set => Put(0x66, Math.Clamp(value, 0, 255)); }
        public int Element { get => Get(0x64); set => Put(0x64, Math.Clamp(value, 0, 255)); }
        public int Element2 { get => Get(0x65); set => Put(0x65, Math.Clamp(value, 0, 255)); }
        public int Cost { get => Get(0x4c); set => Put(0x4c, value); }

        /// <summary>지금 값으로 읽은 한 줄 — 「마법 피해 · 세기 INT² + 60² · ×7 · 80% · 명중 90% · 화염」.</summary>
        public string Summary
        {
            get
            {
                var bits = new List<string> { FormulaText };
                if (PowerAdd != 0) bits.Add($"세기 + {PowerAdd}²");
                if (Hits > 1) bits.Add($"타수 {Hits}");
                if (DamagePercent != 100) bits.Add($"피해 {DamagePercent}%");
                bits.Add($"명중 {Accuracy}%");
                if (Element != 0) bits.Add(Part1AttackBook.ElementName(Element));
                if (Element2 != 0) bits.Add($"둘째 속성 {Element2}");
                if (Cost != 0) bits.Add($"소울 {Cost}");
                return string.Join(" · ", bits);
            }
        }

        public void Reset(Part1Attack attack)
        {
            Attack = attack;
            Raise();
        }

        private void Set(Part1Attack attack)
        {
            Attack = attack;
            Dirty = true;
            Raise();
        }

        public void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly string _folder;
    private readonly List<AttackRow> _rows = [];
    private readonly List<(string Label, Part1Ability? Ability)> _abilities = [];
    private bool _filling;

    public Part1AttackEditWindow()
    {
        InitializeComponent();
        if (Part1Game.Root is null)
            throw new DirectoryNotFoundException("파트1 폴더를 못 찾았습니다 — WAROFGENESIS_PART1 이나 게임의 파트1 폴더 설정을 먼저 맞춰 주세요.");
        _folder = Part1AttackBook.EditFolder ?? throw new DirectoryNotFoundException("assets/data 폴더를 못 찾았습니다.");
        Part1AttackBook.Reload();
        var original = Part1Attack.LoadOriginal();
        var current = Part1Attack.LoadAll();
        foreach (var a in current)
            _rows.Add(new AttackRow { Attack = a, Original = Part1Attack.ById(original, a.Id) ?? a });

        // 어빌리티마다 레벨을 붙인다 — 같은 기술을 여러 어빌리티가 쓰면(연 · 다하카 두번 물기) 앞의 레벨을 둔다.
        foreach (var ab in Part1Tables.Abilities.Where(ab => ab.AttackId > 0))
        {
            var ids = Part1SkillBook.LevelAttackIds(ab, current);
            if (ids.Count == 0) continue;
            _abilities.Add(($"{ab.Id} {ab.Name} ({ids.Count}레벨)", ab));
            for (int lv = 0; lv < ids.Count; lv++)
                if (Part1Attack.ById(current, ids[lv]) is { } a && _rows.FirstOrDefault(r => r.Id == a.Id) is { } row)
                {
                    row.Abilities.Add(ab.Id);
                    if (row.Level == 0) row.Level = lv + 1;
                }
        }

        _filling = true;
        AbilityBox.ItemsSource = new[] { "(모두)", "(고친 것만)" }.Concat(_abilities.Select(x => x.Label)).ToList();
        AbilityBox.SelectedIndex = 0;
        _filling = false;
        LegendText.Text = "세기 식: 0~4 무기 · 5 마법 피해 · 6 회복 · 8 마법 · 10~28 상태이상 · 29~31 둘째 무기 / 속성: 0 무기 속성 · 1 화염 · 2 빙한 · 3 전격 · 4 회복 · 5 암흑 / 둘째 속성: 1~4(저항 칸 35~38) / "
                          + "피해 % 는 원본에 없는 칸(100 = 그대로)";
        ApplyFilter();
        ShowStatus();
    }

    private void ShowStatus(string? note = null) =>
        StatusText.Text = (note != null ? note + " — " : "")
                          + $"기술 {_rows.Count}칸 · 고친 것 {_rows.Count(r => r.Changed)}칸 · 저장 안 한 것 {_rows.Count(r => r.Dirty)}칸. 칸을 고치고 「저장」을 누르면 게임을 다시 켤 때부터 쓴다.";

    private void ApplyFilter()
    {
        int pick = AbilityBox.SelectedIndex;
        string find = SearchBox.Text.Trim();
        IEnumerable<AttackRow> rows = _rows;
        if (pick == 1) rows = rows.Where(r => r.Changed || r.Dirty);
        else if (pick >= 2 && _abilities[pick - 2].Ability is { } ab)
        {
            var ids = Part1SkillBook.LevelAttackIds(ab, [.. _rows.Select(r => r.Attack)]);
            rows = ids.Select(id => _rows.First(r => r.Id == id));
        }
        if (find.Length > 0) rows = rows.Where(r => r.Name.Contains(find, StringComparison.OrdinalIgnoreCase));
        AttackGrid.ItemsSource = rows.ToList();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_filling && IsInitialized && AttackGrid != null) ApplyFilter();
    }

    private void AttackGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        // 칸의 값이 줄에 들어간 뒤에 상태 글을 고친다.
        Dispatcher.BeginInvoke(() => ShowStatus());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AttackGrid.CommitEdit(DataGridEditingUnit.Row, true);
        try
        {
            int written = 0, removed = 0;
            foreach (var row in _rows.Where(r => r.Dirty))
            {
                string path = System.IO.Path.Combine(_folder, Part1AttackBook.FileName(row.Id));
                if (row.Changed)
                {
                    Directory.CreateDirectory(_folder);
                    File.WriteAllText(path, Part1AttackBook.ToJson(row.Attack), new UTF8Encoding(false));
                    written++;
                }
                else if (File.Exists(path))
                {
                    // 원본과 같아졌으면 덮어쓰기 파일을 안 남긴다.
                    File.Delete(path);
                    removed++;
                }
                row.Dirty = false;
                row.Raise();
            }
            Part1AttackBook.Reload();
            ShowStatus($"저장했습니다(적음 {written} · 지움 {removed})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"저장하지 못했습니다: {ex.Message}";
        }
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (AttackGrid.SelectedItem is not AttackRow row) return;
        AttackGrid.CommitEdit(DataGridEditingUnit.Row, true);
        row.Reset(row.Original);
        row.Dirty = true;
        row.Raise();
        ShowStatus($"{row.Name} 을(를) 원본 값으로 되돌렸습니다(「저장」을 눌러야 적힌다)");
    }
}

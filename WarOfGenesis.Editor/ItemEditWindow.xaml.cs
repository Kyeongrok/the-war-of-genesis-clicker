using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 아이템(<c>Dat/Itm.dat</c>) 편집 창 — 값 · 공격 · 방어 · 사거리 · 장비 보정 셋 · 쓰는 work 을 표에서 바로 고친다. 원본 Itm.dat 은 그대로 두고
/// 고친 아이템만 <c>assets/data/items/NNNN.json</c> 에 적는다(<see cref="ItemBook"/>, 사용자 요청 ed-2). 게임은 켤 때 이 표를 읽는다.
/// </summary>
public partial class ItemEditWindow : Window
{
    /// <summary>표 한 줄 — 칸을 고치면 <see cref="Item"/> 이 따라 바뀐다.</summary>
    public sealed class ItemRow : INotifyPropertyChanged
    {
        public ItemData Item { get; set; } = null!;
        public ItemData Original { get; init; } = null!;
        public string Name { get; init; } = "";
        public string TypeName { get; init; } = "";
        public string Description { get; init; } = "";
        public bool Dirty { get; set; }

        public int Id => Item.Id;
        public bool Changed => !ItemBook.SameContent(Item, Original);
        public string Mark => Dirty ? "●" : Changed ? "고침" : "";

        public uint Price { get => Item.Price; set => Set(Item with { Price = value }); }
        public ushort Attack { get => Item.Attack; set => Set(Item with { Attack = value }); }
        public ushort Defense { get => Item.Defense; set => Set(Item with { Defense = value }); }
        public ushort Range { get => Item.Range; set => Set(Item with { Range = value }); }
        public ushort UseWork { get => Item.UseWork; set => Set(Item with { UseWork = value }); }

        // 보정 세 칸 — 번호가 0 이면 빈 칸. 고치는 동안에는 빈 칸도 자리를 지켜야 해서 따로 들고 있다.
        private (ushort Stat, short Value)[]? _slots;
        private (ushort Stat, short Value)[] Slots => _slots ??= [.. Item.Bonuses.Concat(Enumerable.Repeat(((ushort)0, (short)0), 3)).Take(3)];
        private void SetSlot(int i, ushort? stat, short? value)
        {
            var slots = Slots;
            slots[i] = (stat ?? slots[i].Stat, value ?? slots[i].Value);
            Set(Item with { Bonuses = [.. slots.Where(s => s.Stat != 0)] }, keepSlots: true);
        }
        public ushort Stat1 { get => Slots[0].Stat; set => SetSlot(0, value, null); }
        public short Value1 { get => Slots[0].Value; set => SetSlot(0, null, value); }
        public ushort Stat2 { get => Slots[1].Stat; set => SetSlot(1, value, null); }
        public short Value2 { get => Slots[1].Value; set => SetSlot(1, null, value); }
        public ushort Stat3 { get => Slots[2].Stat; set => SetSlot(2, value, null); }
        public short Value3 { get => Slots[2].Value; set => SetSlot(2, null, value); }

        /// <summary>지금 값으로 읽은 효과 한 줄 — 「경험치 % +15 · 최대 TP +20」.</summary>
        public string Summary
        {
            get
            {
                var bits = Item.Bonuses.Select(b => $"{ItemBook.StatNames.GetValueOrDefault(b.Stat, $"번호 {b.Stat}")} {b.Value:+#;-#;0}").ToList();
                if (Item.AttackEffects is { Length: > 0 } effects) bits.Add("기본공격에 상태 " + string.Join(", ", effects.Select(e => $"{e.Status}({e.Value})")));
                return string.Join(" · ", bits);
            }
        }

        public void Reset(ItemData item)
        {
            _slots = null;
            Item = item;
            Raise();
        }

        private void Set(ItemData item, bool keepSlots = false)
        {
            if (!keepSlots) _slots = null;
            Item = item;
            Dirty = true;
            Raise();
        }

        public void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>종류 번호의 이름 — Itm.dat 의 구분 줄(=== 리본류 === …)을 따랐다.</summary>
    private static readonly Dictionary<int, string> TypeNames = new()
    {
        [0] = "VES", [1] = "부스터", [2] = "아머", [3] = "신발", [4] = "벨트", [5] = "반지", [6] = "목걸이", [7] = "사용 아이템", [8] = "리본",
        [9] = "요요", [10] = "사진기", [11] = "크로", [12] = "쌍권총", [13] = "씨가", [14] = "일반검", [15] = "대검", [16] = "아수라검",
        [17] = "포이즌 포트", [19] = "적 무기", [255] = "구분 줄",
    };

    private readonly GameDatabase _db;
    private readonly string _folder;
    private readonly List<ItemRow> _rows = [];
    private bool _filling;

    public ItemEditWindow()
    {
        InitializeComponent();
        string data = AssetsFolder.Find("data");
        _folder = System.IO.Path.Combine(data, ItemBook.Folder);
        var files = GameFiles.FromFolder(data);
        _db = GameDatabase.Load(files);

        var original = ItemBook.LoadOriginal(files);
        foreach (var (id, item) in ItemBook.LoadAll(files).OrderBy(kv => kv.Value.Type).ThenBy(kv => kv.Key))
            _rows.Add(new ItemRow
            {
                Item = item, Original = original.GetValueOrDefault(id) ?? item,
                Name = _db.T(item.NameId) is { Length: > 0 } n ? n : $"아이템 {id}",
                TypeName = TypeNames.GetValueOrDefault(item.Type, $"종류 {item.Type}"),
                Description = _db.T(item.DescriptionId),
            });

        _filling = true;
        TypeBox.ItemsSource = new[] { "(모두)" }.Concat(_rows.Select(r => r.Item.Type).Distinct().OrderBy(t => t)
            .Select(t => $"{t} {TypeNames.GetValueOrDefault(t, "")}".TrimEnd())).ToList();
        // 반지류부터 연다 — 이 창을 만든 까닭이 반지의 경험치 보정이다.
        TypeBox.SelectedIndex = Math.Max(0, ((List<string>)TypeBox.ItemsSource).FindIndex(t => t.StartsWith("5 ")));
        _filling = false;
        LegendText.Text = "보정 번호: " + string.Join(" · ", ItemBook.StatNames.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")) + " (0 = 빈 칸)";
        ApplyFilter();
        ShowStatus();
    }

    private void ShowStatus(string? note = null) =>
        StatusText.Text = (note != null ? note + " — " : "")
                          + $"아이템 {_rows.Count}개 · 고친 것 {_rows.Count(r => r.Changed)}개 · 저장 안 한 것 {_rows.Count(r => r.Dirty)}개. 칸을 고치고 「저장」을 누르면 게임을 다시 켤 때부터 쓴다.";

    private void ApplyFilter()
    {
        string type = TypeBox.SelectedItem as string ?? "(모두)", find = SearchBox.Text.Trim();
        int? wanted = type != "(모두)" && int.TryParse(type.Split(' ')[0], out int t) ? t : null;
        ItemGrid.ItemsSource = _rows.Where(r => (wanted == null || r.Item.Type == wanted) && (find.Length == 0 || r.Name.Contains(find, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_filling && IsInitialized && ItemGrid != null) ApplyFilter();
    }

    private void ItemGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        DescriptionText.Text = ItemGrid.SelectedItem is ItemRow row ? $"{row.Name}: {row.Description}" : "";

    private void ItemGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        // 칸의 값이 줄에 들어간 뒤에 상태 글을 고친다.
        Dispatcher.BeginInvoke(() => ShowStatus());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ItemGrid.CommitEdit(DataGridEditingUnit.Row, true);
        try
        {
            int written = 0, removed = 0;
            foreach (var row in _rows.Where(r => r.Dirty))
            {
                string path = System.IO.Path.Combine(_folder, ItemBook.FileName(row.Id));
                if (row.Changed)
                {
                    Directory.CreateDirectory(_folder);
                    File.WriteAllText(path, ItemBook.ToJson(row.Item, row.Name), new UTF8Encoding(false));
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
            ShowStatus($"저장했습니다(적음 {written} · 지움 {removed})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"저장하지 못했습니다: {ex.Message}";
        }
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (ItemGrid.SelectedItem is not ItemRow row) return;
        ItemGrid.CommitEdit(DataGridEditingUnit.Row, true);
        row.Reset(row.Original);
        row.Dirty = true;
        row.Raise();
        ShowStatus($"{row.Name} 을(를) 원본 값으로 되돌렸습니다(「저장」을 눌러야 적힌다)");
    }
}

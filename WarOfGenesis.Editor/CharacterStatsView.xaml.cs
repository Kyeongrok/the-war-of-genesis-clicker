using System.ComponentModel;
using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 모든 캐릭터 레코드(<c>Chr/*.chr</c>)의 기본정보·능력치를 보는 편집기 첫 화면 — 왼쪽 목록에서 고르면 오른쪽에 상세가 나온다
/// (이름, 칭호, 체질, 계열, 직업, Status 화면 수치 LV·HP·SOUL·TP·ATK·ACR·RDP·LP·CTP·STP·PSY·DEP·DEX, 무기·장비, 어빌리티).
/// 예전에는 22열짜리 표 한 장이라 읽기 어려웠다(사용자 요청).
/// </summary>
/// <remarks>
/// 수치는 <see cref="GameDatabase"/> 의 식(<c>G3PartII.dll</c> 에서 옮김)으로 셈한다. 죠안(Chr 0221)은 게임 Status
/// 화면과 HP 만 빼고 모두 맞는다(HP 는 어빌리티 패시브 보너스를 아직 안 넣음).
/// </remarks>
public partial class CharacterStatsView : UserControl
{
    /// <summary>상세의 「이름 : 값」 한 줄.</summary>
    public sealed record Stat(string Label, string Value);

    /// <summary>초상화 한 장 — 모션(표정) 하나의 첫 컷.</summary>
    public sealed record Portrait(ImageSource Picture, int Width, int Height, string Label)
    {
        public string Size => $"{Width}×{Height}";
    }

    public sealed record Row(int Code, int FaceId, string Name, string Title, string Body, string Family, string Job, int Level,
                             int Hp, string Soul, int Tp, int Atk, int Acr, int Rdp, uint Lp, int Ctp, int Stp,
                             int Psy, int Dep, int Dex, string Weapon, IReadOnlyList<string> EquipmentItems,
                             IReadOnlyList<string> AbilityItems, bool IsPlaceholder, string Category = "")
    {
        /// <summary>목록 둘째 줄 — 분류 · 칭호 · 직업.</summary>
        public string Subtitle => string.Join(" · ", new[] { Category, Title, Job }.Where(t => t.Length > 0));

        /// <summary>상세 머리 밑 한 줄 — 칭호 · 체질 · 계열 · 직업.</summary>
        public string Profile => string.Join(" · ", new[] { Title, Body, Family, Job }.Where(t => t.Length > 0));

        public IReadOnlyList<Stat> Vitals => [new("LV", Level.ToString()), new("HP", Hp.ToString()), new("SOUL", Soul), new("TP", Tp.ToString())];
        public IReadOnlyList<Stat> Combat => [new("ATK", Atk.ToString()), new("ACR", Acr.ToString()), new("RDP", Rdp.ToString())];
        public IReadOnlyList<Stat> Bases => [new("LP", Lp.ToString()), new("CTP", Ctp.ToString()), new("STP", Stp.ToString()),
                                             new("PSY", Psy.ToString()), new("DEP", Dep.ToString()), new("DEX", Dex.ToString())];
    }

    private ICollectionView? _view;

    /// <summary>목록 우클릭 「모션 매핑 보기」 — 고른 레코드 번호.</summary>
    public event Action<int>? MotionMappingRequested;

    /// <summary>목록 우클릭 「내보내기」 — 고른 레코드 번호.</summary>
    public event Action<int>? ExportRequested;

    public CharacterStatsView() => InitializeComponent();

    /// <summary>목록을 채운다 — 원본 게임 폴더가 아니라 우리 게임이 쓰는 <c>assets/data</c> 자료로(사용자 요청).</summary>
    private GameDatabase? _db;

    /// <summary>
    /// 고른 인물의 어빌리티 목록을 고친다 — 줄마다 어빌리티와 레벨, 「추가」·「삭제」, 「저장」을 누르면 <c>assets/data/chr-edits/NNNN.json</c> 의
    /// <c>abilities</c> 에 적는다(그 파일의 다른 칸은 그대로). 저장 뒤 목록을 다시 읽어 보인다.
    /// </summary>
    private void EditAbilities_Click(object sender, RoutedEventArgs e)
    {
        if (_db is not { } db || List.SelectedItem is not Row row || db.Character(row.Code) is not { } c) return;
        var choices = db.Abilities.Values.OrderBy(a => a.Id).Select(a => $"{a.Id} {db.T(a.NameId)}").ToList();
        var dialog = new Window
        {
            Title = $"어빌리티 편집 — {row.Code:D4} {row.Name}", Width = 420, Height = 520, Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var rows = new StackPanel();
        void AddLine(int ability, int level)
        {
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var remove = new Button { Content = "삭제", Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(remove, Dock.Right);
            var levelBox = new TextBox { Text = level.ToString(), Width = 44, Margin = new Thickness(6, 0, 0, 0), ToolTip = "레벨" };
            DockPanel.SetDock(levelBox, Dock.Right);
            var box = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, ItemsSource = choices, Text = choices.FirstOrDefault(t => t.StartsWith(ability + " ", StringComparison.Ordinal)) ?? "" };
            remove.Click += (_, _) => rows.Children.Remove(line);
            line.Children.Add(remove);
            line.Children.Add(levelBox);
            line.Children.Add(box);
            rows.Children.Add(line);
        }
        foreach (var (ability, level) in c.Abilities.Where(a => a.Ability != 0)) AddLine(ability, level);
        var add = new Button { Content = "추가", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 6, 6, 0) };
        add.Click += (_, _) => AddLine(0, 1);
        var save = new Button { Content = "저장", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 6, 6, 0), IsDefault = true };
        var reset = new Button { Content = "원본으로", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 6, 0, 0), ToolTip = "덮어쓴 어빌리티 목록을 지우고 .chr 의 것으로 되돌린다" };
        static int Leading(string text)
        {
            int end = 0;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            return end > 0 && int.TryParse(text[..end], out int v) ? v : 0;
        }
        void Write(System.Text.Json.Nodes.JsonArray? abilities)
        {
            string folder = Path.Combine(AssetsFolder.Find("data"), GameDatabase.CharacterEditFolder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{row.Code:D4}.json");
            var node = File.Exists(path) && System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) is System.Text.Json.Nodes.JsonObject known
                ? known : new System.Text.Json.Nodes.JsonObject { ["code"] = row.Code, ["name"] = row.Name };
            node.Remove("abilities");
            if (abilities != null) node["abilities"] = abilities;
            // 덮어쓸 것이 하나도 안 남으면(code·name 뿐) 파일을 지운다.
            if (abilities == null && node.All(kv => kv.Key is "code" or "name")) { if (File.Exists(path)) File.Delete(path); }
            else File.WriteAllText(path, node.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            dialog.DialogResult = true;
        }
        save.Click += (_, _) =>
        {
            var list = new System.Text.Json.Nodes.JsonArray();
            foreach (DockPanel line in rows.Children.OfType<DockPanel>())
            {
                int id = Leading(line.Children.OfType<ComboBox>().First().Text.Trim());
                if (id == 0 || !db.Abilities.ContainsKey(id)) continue;
                int level = int.TryParse(line.Children.OfType<TextBox>().First().Text.Trim(), out int lv) ? Math.Clamp(lv, 1, 99) : 1;
                list.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["level"] = level, ["name"] = db.T(db.Abilities[id].NameId) });
            }
            Write(list);
        };
        reset.Click += (_, _) => Write(null);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(add);
        buttons.Children.Add(save);
        buttons.Children.Add(reset);
        var panel = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var note = new TextBlock { Text = "원본 .chr 은 그대로 두고 assets/data/chr-edits 에 적는다. 새로 여는 전투부터 반영된다.", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(note, Dock.Top);
        panel.Children.Add(note);
        panel.Children.Add(buttons);
        panel.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        dialog.Content = panel;
        if (dialog.ShowDialog() != true) return;
        // 덮어쓰기는 자료 개체가 한 번 읽어 들고 있으므로 새로 읽는다.
        int code = row.Code;
        int category = CategoryBox.SelectedIndex;
        Load(GameDatabase.Load(GameFiles.FromFolder(AssetsFolder.Find("data"))), Enumerable.Range(0, 1000));
        if (category >= 0 && category < CategoryBox.Items.Count) CategoryBox.SelectedIndex = category;
        List.SelectedItem = List.Items.Cast<Row>().FirstOrDefault(r => r.Code == code);
        if (List.SelectedItem != null) List.ScrollIntoView(List.SelectedItem);
    }

    public void Load(GameDatabase db, IEnumerable<int> codes)
    {
        _db = db;
        _portraits.Clear();

        var rows = new List<Row>();
        foreach (int code in codes.OrderBy(c => c))
        {
            if (db.Character(code) is not { } c) continue;
            rows.Add(MakeRow(db, c));
        }
        var categories = Classify(db, rows);
        for (int i = 0; i < rows.Count; i++) rows[i] = rows[i] with { Category = categories.GetValueOrDefault(rows[i].Code, OtherCategory) };
        CategoryBox.ItemsSource = new[] { AllCategories }.Concat(CategoryNames).Concat([OtherCategory])
            .Select(n => n == AllCategories ? n : $"{n} ({rows.Count(r => r.Category == n && !r.IsPlaceholder)})").ToList();
        CategoryBox.SelectedIndex = 0;
        List.ItemsSource = rows;
        _view = CollectionViewSource.GetDefaultView(rows);
        _view.Filter = Accept;
        StatusText.Text = $"{_view.Cast<object>().Count()}명";
        List.SelectedIndex = 0;
    }

    private void List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        Detail.DataContext = List.SelectedItem;
        var faceId = (List.SelectedItem as Row)?.FaceId ?? 0;
        var portraits = PortraitsOf(faceId);
        Portraits.ItemsSource = portraits;
        PortraitHeader.Text = faceId == 0 ? "초상화 (없음)" : $"초상화 — Obs {faceId:D4} · {portraits.Count}장";
    }

    private readonly Dictionary<int, IReadOnlyList<Portrait>> _portraits = [];

    private Dictionary<int, string>? _obsPaths;

    /// <summary>저장소 <c>assets</c> 아래 모든 <c>.obs</c> — 번호 → 파일(초상은 <c>moses/obs</c>·<c>characters/*</c> 에 흩어져 있다).</summary>
    private Dictionary<int, string> ObsPaths => _obsPaths ??= IndexObs();

    private static Dictionary<int, string> IndexObs()
    {
        var map = new Dictionary<int, string>();
        try
        {
            foreach (string path in Directory.EnumerateFiles(AssetsFolder.Find(""), "*.obs", SearchOption.AllDirectories))
                if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int id)) map.TryAdd(id, path);
        }
        catch (DirectoryNotFoundException) { }
        return map;
    }

    /// <summary>초상 Obs 의 모션(표정)마다 첫 컷 — 저장소 assets 에서 읽고, 한 번 푼 것은 들고 있는다.</summary>
    private IReadOnlyList<Portrait> PortraitsOf(int faceId)
    {
        if (faceId == 0) return [];
        if (_portraits.TryGetValue(faceId, out var cached)) return cached;
        var list = new List<Portrait>();
        try
        {
            if (ObsPaths.TryGetValue(faceId, out var path))
                foreach (var motion in ObsSprite.Decode(File.ReadAllBytes(path)))
                {
                    var f = motion.Frames[0];
                    var bitmap = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
                    bitmap.Freeze();
                    list.Add(new Portrait(bitmap, f.Width, f.Height, $"#{motion.Id}"));
                }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { }
        return _portraits[faceId] = list;
    }

    private static Row MakeRow(GameDatabase db, CharacterData c)
    {
        string name = db.T(c.NameId);
        int tp = db.MaxTp(c);
        int soul = db.SoulStart;

        string ItemName(ushort id) => id != 0 && db.Items.TryGetValue(id, out var it) ? db.T(it.NameId) : "";
        var weapon = c.Items[0] != 0 && db.Items.TryGetValue(c.Items[0], out var w) ? w : null;
        string weaponText = weapon == null ? "" : $"{db.T(weapon.NameId)} ({GameDatabase.WeaponTypeName(weapon.Type)}, 공격 {weapon.Attack})";
        var equipment = c.Items.Skip(1).Select(ItemName).Where(s => s.Length > 0).ToList();
        if (equipment.Count == 0) equipment.Add("없음");

        var abilities = c.Abilities.OrderBy(a => a.Ability).Select(a =>
        {
            if (!db.Abilities.TryGetValue(a.Ability, out var ab)) return $"#{a.Ability} Lv{a.Level}";
            string next = a.Level < ab.MaxLevel && ab.WorkByLevel.TryGetValue(a.Level, out int wid) && db.Works.TryGetValue(wid, out var work)
                ? $" ({work.ExpCost}exp)" : "";
            return $"{db.T(ab.NameId)} Lv{a.Level}{next}";
        }).ToList();
        if (abilities.Count == 0) abilities.Add("없음");

        bool placeholder = name.Length == 0 || c.Lp <= 1 || name.StartsWith('<') || name.StartsWith('=') || name.StartsWith('-');
        return new Row(c.Code, c.FaceId, name, db.T(c.TitleId), db.BodyName(c.Body), db.FamilyName(c), db.JobName(c), c.Level,
                       db.MaxHp(c), $"{soul}/{db.MaxSoul(c)}", tp, db.Atk(c, soul), db.Acr(c, tp), db.RdpAtFullHp(c),
                       c.Lp, c.Ctp, db.Stp(c), db.Psy(c), c.Dep, db.Dex(c), weaponText.Length > 0 ? weaponText : "없음", equipment, abilities, placeholder);
    }

    private const string AllCategories = "전체", OtherCategory = "그 밖";

    /// <summary>분류 이름 — 사용자 요청(아군 주인공 · 적군 주인공 · 부대 리더 · 부대원).</summary>
    private static readonly string[] CategoryNames = ["아군 주인공", "적군 주인공", "부대 리더", "부대원"];

    /// <summary>
    /// 인물을 넷으로 가른다 — 자료에 「분류」 칸이 없어 쓰임새로 미룬다(어림):
    /// 아군 주인공 = 파티 레벨 성장을 안 받는 명단(<c>Dat/0002.nch</c> — 플레이어 인물과 손님),
    /// 부대원 = 어느 군단(<c>For.dat</c>)의 부하 자리에 있는 인물,
    /// 적군 주인공 = 전투에 적 편으로 서고, 초상화가 있고, 이름이 그 인물 하나뿐인(일반병처럼 여럿이 같은 이름을 쓰지 않는) 인물,
    /// 부대 리더 = 군단의 리더 칸에 적혔거나 전투에서 군단을 이끌고 서는 인물. 위에서부터 먼저 맞는 것으로 정한다.
    /// </summary>
    private static Dictionary<int, string> Classify(GameDatabase db, List<Row> rows)
    {
        var result = new Dictionary<int, string>();
        try
        {
            var files = GameFiles.FromFolder(AssetsFolder.Find("data"));
            var members = new HashSet<int>();
            var leaders = new HashSet<int>();
            foreach (var legion in LegionBook.LoadAll(files).Values)
            {
                foreach (ushort m in legion.Members) members.Add(m);
                if (legion.Leader > 0) leaders.Add(legion.Leader);
            }
            var enemies = new HashSet<int>();
            foreach (string name in files.List("Btl", ".btl").Keys)
                if (int.TryParse(Path.GetFileNameWithoutExtension(name), out int btl) && BattleFile.Parse(btl, files.Read("Btl", name)) is { } battle)
                    foreach (var u in battle.Units.Where(u => u.ChrCode > 0))
                    {
                        if (u.Squad > 0) leaders.Add(u.ChrCode);
                        if (u.Side is not (3 or 4)) enemies.Add(u.ChrCode);
                    }
            var nameCount = rows.Where(r => !r.IsPlaceholder).GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.Count());
            foreach (var r in rows)
            {
                if (r.IsPlaceholder) continue;
                result[r.Code] = db.LevelExempt.Contains(r.Code) ? CategoryNames[0]
                               : members.Contains(r.Code) ? CategoryNames[3]
                               : enemies.Contains(r.Code) && r.FaceId > 0 && nameCount.GetValueOrDefault(r.Name) == 1 ? CategoryNames[1]
                               : leaders.Contains(r.Code) ? CategoryNames[2]
                               : OtherCategory;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or DirectoryNotFoundException) { }
        return result;
    }

    private bool Accept(object o)
    {
        if (o is not Row r) return false;
        if (HideEmptyToggle.IsChecked == true && r.IsPlaceholder) return false;
        // 분류 — 고른 이름(뒤의 「(개수)」는 뗀다)과 같아야 한다.
        if (CategoryBox.SelectedItem is string chosen && chosen != AllCategories && !chosen.StartsWith(r.Category + " (", StringComparison.Ordinal)) return false;
        string q = FilterBox.Text.Trim();
        return q.Length == 0 || r.Name.Contains(q) || r.Title.Contains(q) || r.Job.Contains(q) || r.Family.Contains(q)
               || r.Code.ToString("D4").Contains(q);
    }

    private void MotionMappingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is Row r) MotionMappingRequested?.Invoke(r.Code);
    }

    private void ExportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is Row r) ExportRequested?.Invoke(r.Code);
    }

    /// <summary>우클릭한 항목을 먼저 고른다 — 컨텍스트 메뉴가 그 캐릭터를 대상으로 하도록.</summary>
    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonDown(e);
        if (ItemsControl.ContainerFromElement(List, (DependencyObject)e.OriginalSource) is ListBoxItem item) item.IsSelected = true;
    }

    /// <summary>지금 고른 인물의 Chr 번호 — 없으면 null. 군단 편집기의 구성원 고르기 창이 쓴다.</summary>
    public int? SelectedCode => (List.SelectedItem as Row)?.Code;

    /// <summary>
    /// 목록 줄을 두 번 누른 인물 — 구성원 고르기 창이 「이 인물로」와 같게 받는다.
    /// </summary>
    public event Action<int>? RowDoubleClicked;

    /// <summary>그 번호의 인물을 고르고 보이게 한다 — 찾기 글이 그 인물을 가리면 찾기를 비운다.</summary>
    public void Select(int code)
    {
        if (List.ItemsSource is not IEnumerable<Row> rows || rows.FirstOrDefault(r => r.Code == code) is not { } row) return;
        if (_view != null && !_view.Contains(row)) { FilterBox.Text = ""; HideEmptyToggle.IsChecked = false; _view.Refresh(); }
        List.SelectedItem = row;
        List.ScrollIntoView(row);
    }

    protected override void OnPreviewMouseDoubleClick(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDoubleClick(e);
        if (ItemsControl.ContainerFromElement(List, (DependencyObject)e.OriginalSource) is ListBoxItem { Content: Row r }) RowDoubleClicked?.Invoke(r.Code);
    }

    private void FilterBox_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        _view.Refresh();
        StatusText.Text = $"{_view.Cast<object>().Count()}명";
        if (List.SelectedItem == null) List.SelectedIndex = 0;
    }
}

using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 군단(<c>Dat/For.dat</c>) 편집 창 — 부하 여섯·진형·부하 보정·군단기를 고친다. 원본 For.dat 은 그대로 두고
/// 고친 군단만 <c>assets/data/legions/NNNN.json</c> 에 적는다(<see cref="LegionBook"/>, 사용자 요청). 게임은 전투를 시작할 때 이 표로 부하를 만든다.
/// </summary>
public partial class LegionEditWindow : Window
{
    /// <summary>목록 한 줄 — 지금 값(<see cref="Legion"/>)과 원본(<see cref="Original"/>).</summary>
    public sealed class LegionRow
    {
        public LegionData Legion { get; set; } = null!;
        public LegionData? Original { get; init; }
        public string Name { get; init; } = "";
        public int Battles { get; init; }
        public bool Dirty { get; set; }
        public int Id => Legion.Id;
        public bool Changed => Original == null || !LegionBook.SameContent(Legion, Original);
        public string Mark => Dirty ? "●" : Changed ? "고침" : "";
        public string MembersText { get; set; } = "";
    }

    private readonly GameDatabase _db;
    private readonly string _folder;
    private readonly List<LegionRow> _rows = [];
    private readonly List<string> _chrChoices = [];
    private readonly List<string> _abilityChoices = [];
    private readonly Dictionary<int, string> _chrNames = [];
    private bool _filling;

    /// <summary>군단마다 전투 자료가 세우는 대장(Chr → 몇 전투) — 리더 칸 옆에 「지금 리더」로 보인다.</summary>
    private readonly Dictionary<int, Dictionary<int, int>> _battleLeaders = [];

    private static readonly string[] FormationNames = ["0 학익진", "1 일자형", "2 십자형", "3 역학익진", "4 젓가락형", "5 이자형"];

    public LegionEditWindow()
    {
        InitializeComponent();
        string data = AssetsFolder.Find("data");
        _folder = System.IO.Path.Combine(data, LegionBook.Folder);
        var files = GameFiles.FromFolder(data);
        _db = GameDatabase.Load(files);

        for (int code = 0; code < 1000; code++)
            if (_db.Character(code) is { } c)
            {
                string name = _db.T(c.NameId);
                _chrNames[code] = name;
                _chrChoices.Add($"{code} {name}");
            }
        _abilityChoices.Add("0 (없음)");
        foreach (var ab in _db.Abilities.Values.OrderBy(a => a.Category == 2 ? 0 : 1).ThenBy(a => a.Id))
            _abilityChoices.Add($"{ab.Id} {_db.T(ab.NameId)}{(ab.Category == 2 ? " (군단기)" : "")}");
        FormationBox.ItemsSource = FormationNames;

        // 어느 전투가 이 군단을 부르나 — Btl 인물 줄의 편대 번호(파일 15).
        var battles = new Dictionary<int, HashSet<int>>();
        foreach (var name in files.List("Btl", ".btl").Keys)
            if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(name), out int btl) && BattleFile.Parse(btl, files.Read("Btl", name)) is { } bf)
                foreach (var u in bf.Units.Where(u => u.Squad > 0))
                {
                    (battles.TryGetValue(u.Squad, out var set) ? set : battles[u.Squad] = []).Add(btl);
                    var leaders = _battleLeaders.TryGetValue(u.Squad, out var l) ? l : _battleLeaders[u.Squad] = [];
                    leaders[u.ChrCode] = leaders.GetValueOrDefault(u.ChrCode) + 1;
                }

        var original = LegionBook.LoadOriginal(files);
        foreach (var (id, legion) in LegionBook.LoadAll(files).OrderBy(kv => kv.Key))
        {
            var orig = original.GetValueOrDefault(id);
            var row = new LegionRow
            {
                Legion = legion, Original = orig, Battles = battles.GetValueOrDefault(id)?.Count ?? 0,
                Name = _db.T(legion.NameId) is { Length: > 0 } n ? n : $"군단 {id}",
            };
            row.MembersText = MembersText(row.Legion);
            _rows.Add(row);
        }
        LegionGrid.ItemsSource = _rows;
        BuildLeaderRow();
        BuildMemberBoxes();
        BuildSkillRows();
        StatusText.Text = $"군단 {_rows.Count}개 — 고친 것 {_rows.Count(r => r.Changed)}개. 고치고 「저장」을 누르면 게임이 다음 전투부터 쓴다(게임 켜 둔 채면 개발 > 어빌리티 반영).";
        if (_rows.Count > 0) LegionGrid.SelectedIndex = 0;
    }

    private LegionRow? Current => LegionGrid.SelectedItem as LegionRow;

    private string MembersText(LegionData l) =>
        string.Join(", ", l.Members.GroupBy(m => m).Select(g => $"{_chrNames.GetValueOrDefault(g.Key, g.Key.ToString())}{(g.Count() > 1 ? $"×{g.Count()}" : "")}"));

    // ── 입력 칸 만들기 ───────────────────────────────────────────────────────

    private readonly List<ComboBox> _memberBoxes = [];
    private readonly List<(ComboBox Ability, TextBox Power, ComboBox Leader)> _skillRows = [];

    private ComboBox NewChoiceBox(List<string> items, double width, string automationId)
    {
        var box = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, ItemsSource = items, Width = width, Margin = new Thickness(0, 1, 6, 1) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(box, automationId);
        box.LostFocus += (_, _) => Commit();
        box.SelectionChanged += (_, _) => { if (!_filling && box.IsDropDownOpen) Commit(); };
        box.DropDownClosed += (_, _) => Commit();
        return box;
    }

    private void BuildMemberBoxes()
    {
        for (int i = 0; i < 6; i++)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            line.Children.Add(new TextBlock { Text = $"{i + 1} ", Width = 16, VerticalAlignment = VerticalAlignment.Center });
            var box = NewChoiceBox(_chrChoices, 240, $"LegionEditMember{i + 1}");
            _memberBoxes.Add(box);
            line.Children.Add(box);
            line.Children.Add(PickButton(box, $"부하 {i + 1}", $"LegionEditPickMember{i + 1}"));
            MembersPanel.Children.Add(line);
        }
    }

    private ComboBox _leaderBox = null!;

    /// <summary>리더 줄 — 고르기 칸 + 「고르기…」. 0 은 전투 자료대로.</summary>
    private void BuildLeaderRow()
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new TextBlock { Text = "★ ", Width = 16, VerticalAlignment = VerticalAlignment.Center });
        _leaderBox = NewChoiceBox(["0 전투 자료대로", .. _chrChoices], 240, "LegionEditLeader");
        line.Children.Add(_leaderBox);
        line.Children.Add(PickButton(_leaderBox, "리더", "LegionEditPickLeader"));
        LeaderPanel.Children.Add(line);
    }

    /// <summary>
    /// 「고르기…」 — 구성원 고르기 창(캐릭터 스탯과 같은 목록·초상·능력치)을 띄워 그 칸에 고른 인물을 넣는다(ed-1: 구성원을 누르면 편집 화면).
    /// </summary>
    private Button PickButton(ComboBox box, string what, string automationId)
    {
        var button = new Button { Content = "고르기…", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 1, 6, 1) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) =>
        {
            if (Current is null) return;
            var pick = new MemberPickWindow(_db, LeadingNumber(box.Text), $"{what} — 군단 {Current.Id} {Current.Name}") { Owner = this };
            if (pick.ShowDialog() != true || pick.Chosen is not { } code) return;
            box.Text = code == 0 ? (box == _leaderBox ? "0 전투 자료대로" : "") : ChrLabel(code);
            Commit();
        };
        return button;
    }

    private void BuildSkillRows()
    {
        for (int i = 0; i < 5; i++)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal };
            var ability = NewChoiceBox(_abilityChoices, 240, $"LegionEditSkill{i + 1}");
            var power = new TextBox { Width = 60, Margin = new Thickness(0, 1, 6, 1), VerticalContentAlignment = VerticalAlignment.Center };
            power.LostFocus += (_, _) => Commit();
            var leader = NewChoiceBox(["0 누구나", .. _chrChoices], 220, $"LegionEditLeader{i + 1}");
            line.Children.Add(ability);
            line.Children.Add(new TextBlock { Text = "세력 ", VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(power);
            line.Children.Add(new TextBlock { Text = "대장 ", VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(leader);
            _skillRows.Add((ability, power, leader));
            SkillsPanel.Children.Add(line);
        }
    }

    // ── 보이기 ───────────────────────────────────────────────────────────────

    private void LegionGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => Fill();

    private void Fill()
    {
        if (Current is not { } row) { Detail.IsEnabled = false; return; }
        _filling = true;
        Detail.IsEnabled = true;
        var l = row.Legion;
        TitleText.Text = $"군단 {l.Id} — {row.Name}" + (row.Original == null ? " (원본에 없는 군단)" : "");
        DescriptionText.Text = (_db.T(l.DescriptionId) is { Length: > 0 } d ? d.Replace("$n", " ") : "(설명 없음)")
                               + $"  ·  이 군단을 쓰는 전투 {row.Battles}개";
        FormationBox.SelectedIndex = l.Formation;
        _leaderBox.Text = l.Leader == 0 ? "0 전투 자료대로" : ChrLabel(l.Leader);
        BattleLeadersText.Text = _battleLeaders.TryGetValue(l.Id, out var bl) && bl.Count > 0
            ? "전투 자료의 리더: " + string.Join(", ", bl.OrderByDescending(k => k.Value).Select(k => $"{_chrNames.GetValueOrDefault(k.Key, k.Key.ToString())}({k.Key}) {k.Value}번"))
              + (l.Leader > 0 ? $" → 모두 {ChrLabel(l.Leader)} 로 바뀜" : "")
            : "이 군단을 쓰는 전투가 없다(모세스에서 얻는 아군 군단일 수 있다 — 아군 대장은 안 바꾼다)";
        for (int i = 0; i < 6; i++) _memberBoxes[i].Text = i < l.Members.Length ? ChrLabel(l.Members[i]) : "";
        LpBox.Text = l.LpBonus.ToString();
        PsyBox.Text = l.PsyBonus.ToString();
        DepBox.Text = l.DepBonus.ToString();
        BonusHint.Text = $"세력 1000 이면 LP +{l.LpBonus * 10} · PSY +{l.PsyBonus * 10} · DEP +{l.DepBonus * 10}";
        for (int i = 0; i < 5; i++)
        {
            var (ability, power, leader) = _skillRows[i];
            if (i < l.Skills.Length)
            {
                var s = l.Skills[i];
                ability.Text = _abilityChoices.FirstOrDefault(a => LeadingNumber(a) == s.Ability) ?? s.Ability.ToString();
                power.Text = s.Power.ToString();
                leader.Text = s.Leader == 0 ? "0 누구나" : ChrLabel(s.Leader);
            }
            else { ability.Text = ""; power.Text = ""; leader.Text = ""; }
        }
        DrawFormation(l);
        _filling = false;
    }

    private string ChrLabel(int code) => _chrNames.TryGetValue(code, out var n) ? $"{code} {n}" : code.ToString();

    /// <summary>「192 가이아버그」 → 192. 숫자가 없으면 0.</summary>
    private static int LeadingNumber(string? text)
    {
        text = (text ?? "").Trim();
        int end = 0;
        while (end < text.Length && char.IsDigit(text[end])) end++;
        return end > 0 && int.TryParse(text[..end], out int v) ? v : 0;
    }

    /// <summary>인물 그림(첫 벌 첫 장) — 한 번 푼 것은 들고 있는다. 못 찾으면 null.</summary>
    private readonly Dictionary<int, System.Windows.Media.Imaging.BitmapSource?> _chrImages = [];

    /// <summary>게임 폴더(편집기 첫 화면에서 연 것) — 저장소에 안 뽑아 둔 인물의 그림을 여기서 읽는다.</summary>
    private readonly GameFiles? _game = MainWindow.LoadSavedGameRoot() is { Length: > 0 } root && System.IO.Directory.Exists(root) ? GameFiles.FromGameRoot(root) : null;

    /// <summary>
    /// 그 Chr 의 실제 그림 — 인물 레코드의 그림 Obs(<c>+0xc</c>)를 저장소(<c>assets/characters/NNNN_이름</c>)에서, 없으면 게임 폴더의 <c>Obs</c> 에서 읽는다.
    /// </summary>
    private System.Windows.Media.Imaging.BitmapSource? ChrImage(int chr)
    {
        if (chr <= 0) return null;
        if (_chrImages.TryGetValue(chr, out var known)) return known;
        System.Windows.Media.Imaging.BitmapSource? image = null;
        try
        {
            if (_db.Character(chr) is { SpriteId: > 0 } c)
            {
                byte[]? obs = null;
                string characters = AssetsFolder.Find("characters");
                foreach (string folder in System.IO.Directory.Exists(characters) ? System.IO.Directory.GetDirectories(characters, $"{chr:D4}_*") : [])
                    if (System.IO.Path.Combine(folder, $"{c.SpriteId:D4}.obs") is var path && System.IO.File.Exists(path)) obs = System.IO.File.ReadAllBytes(path);
                obs ??= _game?.Read("Obs", $"{c.SpriteId:D4}.obs");
                if (obs != null && ObsSprite.DecodeFirstFrame(obs) is { Width: > 0, Height: > 0 } f)
                {
                    image = System.Windows.Media.Imaging.BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
                    image.Freeze();
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or ArgumentException or IndexOutOfRangeException) { }
        return _chrImages[chr] = image;
    }

    /// <summary>진형 그림 — 대장 가운데, 부하 자리에 그 인물의 실제 그림과 자리 번호(위를 볼 때, <see cref="LegionData.FormationCells"/>).</summary>
    private void DrawFormation(LegionData l)
    {
        FormationCanvas.Children.Clear();
        const int cell = 44, half = 3;   // 7×7 칸, 가운데가 대장
        for (int y = -half; y <= half; y++)
            for (int x = -half; x <= half; x++)
            {
                var r = new Rectangle { Width = cell - 1, Height = cell - 1, Stroke = Brushes.LightGray, Fill = Brushes.White };
                Canvas.SetLeft(r, (x + half) * cell);
                Canvas.SetTop(r, (y + half) * cell);
                FormationCanvas.Children.Add(r);
            }
        void Label(int dx, int dy, string text, Brush fill)
        {
            var b = new Border { Width = cell - 1, Height = cell - 1, Background = fill, Child = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold } };
            Canvas.SetLeft(b, (dx + half) * cell);
            Canvas.SetTop(b, (dy + half) * cell);
            FormationCanvas.Children.Add(b);
        }
        // 칸 위에 그 인물의 그림을 얹는다 — 발이 칸 아래에 오게 키를 맞춰(칸보다 크면 위로 삐져나온다) 그리고, 구석에 번호를 남긴다.
        void Unit(int dx, int dy, int chr, string mark, Brush fill)
        {
            Label(dx, dy, ChrImage(chr) == null ? mark : "", fill);
            if (ChrImage(chr) is not { } picture) return;
            double scale = Math.Min(1.0, Math.Min((cell + 12.0) / picture.PixelWidth, (cell * 1.6) / picture.PixelHeight));
            var image = new Image { Source = picture, Width = picture.PixelWidth * scale, Height = picture.PixelHeight * scale, ToolTip = _chrNames.GetValueOrDefault(chr, chr.ToString()) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(image, (dx + half) * cell + (cell - image.Width) / 2);
            Canvas.SetTop(image, (dy + half) * cell + cell - 2 - image.Height);
            Panel.SetZIndex(image, 10 + dy);       // 아랫줄이 윗줄을 가린다
            FormationCanvas.Children.Add(image);
            var tag = new TextBlock { Text = mark, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, Background = fill, Padding = new Thickness(2, 0, 2, 0) };
            Canvas.SetLeft(tag, (dx + half) * cell + 1);
            Canvas.SetTop(tag, (dy + half) * cell + 1);
            Panel.SetZIndex(tag, 30);
            FormationCanvas.Children.Add(tag);
        }
        Unit(0, 0, l.Leader, "★", Brushes.Gold);
        var cells = LegionData.FormationCells[Math.Clamp((int)l.Formation, 0, 5)];
        for (int i = 0; i < 6; i++)
            Unit(cells[i].Dx, cells[i].Dy, i < l.Members.Length ? l.Members[i] : 0, (i + 1).ToString(), i < l.Members.Length ? Brushes.LightSkyBlue : Brushes.WhiteSmoke);
    }

    // ── 고치기 ───────────────────────────────────────────────────────────────

    private void FormationBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Commit();

    private void Bonus_LostFocus(object sender, RoutedEventArgs e) => Commit();

    /// <summary>입력 칸들을 읽어 고른 군단에 옮긴다 — 바뀐 것이 있으면 「●」(저장 안 함) 표시.</summary>
    private void Commit()
    {
        if (_filling || Current is not { } row) return;
        static ushort Clamp(int v) => (ushort)Math.Clamp(v, 0, ushort.MaxValue);
        ushort Parse(TextBox box, ushort old) => int.TryParse(box.Text.Trim(), out int v) ? Clamp(v) : old;
        var old = row.Legion;
        var members = _memberBoxes.Select(b => LeadingNumber(b.Text)).Where(m => m > 0).Select(m => (ushort)m).ToArray();
        var skills = _skillRows
            .Select(s => (Ability: Clamp(LeadingNumber(s.Ability.Text)), Power: Parse(s.Power, 0), Leader: Clamp(LeadingNumber(s.Leader.Text))))
            .Where(s => s.Ability != 0).ToArray();
        var next = old with
        {
            Leader = Clamp(LeadingNumber(_leaderBox.Text)),
            Members = members,
            Formation = (byte)Math.Max(0, FormationBox.SelectedIndex),
            LpBonus = Parse(LpBox, old.LpBonus),
            PsyBonus = Parse(PsyBox, old.PsyBonus),
            DepBonus = Parse(DepBox, old.DepBonus),
            Skills = skills,
        };
        if (LegionBook.SameContent(next, old)) return;
        row.Legion = next;
        row.Dirty = true;
        row.MembersText = MembersText(next);
        LegionGrid.Items.Refresh();
        Fill();
        StatusText.Text = $"군단 {row.Id} 을(를) 고쳤습니다 — 「저장」을 눌러야 파일에 적힙니다.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Commit();
        var dirty = _rows.Where(r => r.Dirty).ToList();
        if (dirty.Count == 0) { StatusText.Text = "저장할 것이 없습니다."; return; }
        try
        {
            Directory.CreateDirectory(_folder);
            foreach (var r in dirty)
            {
                string path = System.IO.Path.Combine(_folder, LegionBook.FileName(r.Id));
                // 원본과 같아졌으면 덮어쓰기 파일을 남기지 않는다.
                if (!r.Changed) { if (File.Exists(path)) File.Delete(path); }
                else File.WriteAllText(path, LegionBook.ToJson(r.Legion, r.Name), new UTF8Encoding(false));
                r.Dirty = false;
            }
            LegionGrid.Items.Refresh();
            StatusText.Text = $"군단 {string.Join(", ", dirty.Select(r => r.Id))} 을(를) 저장했습니다 — 게임은 다음 전투부터 쓴다(켜 둔 채면 개발 > 어빌리티 반영).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"저장하지 못했습니다: {ex.Message}";
        }
    }

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (Current is not { } row) return;
        if (row.Original is not { } orig) { StatusText.Text = "원본 For.dat 에 없는 군단이라 되돌릴 원본이 없습니다."; return; }
        row.Legion = orig;
        row.Dirty = true;   // 저장하면 덮어쓰기 파일을 지운다
        row.MembersText = MembersText(orig);
        LegionGrid.Items.Refresh();
        Fill();
        StatusText.Text = $"군단 {row.Id} 을(를) 원본 값으로 돌렸습니다 — 「저장」을 누르면 덮어쓰기 파일을 지웁니다.";
    }
}

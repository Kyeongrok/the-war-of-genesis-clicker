using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 타워 디펜스 편집 창(사용자 요청 td-4) — 스테이지(맵 · 길 · 시작 골드 · 생명 · 적 · 웨이브)와 타워(인물 · 무기 갈래 · 값 · 영웅 오라 · 갈래 수치)를 고친다.
/// 저장하면 <c>assets/data/towerdefense/</c> 에 <c>towers.json</c> · <c>stage-NN.json</c> 을 적고(<see cref="TowerDefenseBook"/>),
/// 게임은 타워 디펜스를 열 때마다 다시 읽는다. 길은 게임과 같은 셈(<see cref="TowerDefenseBook.BuildPath"/>)으로 그려 보인다.
/// </summary>
/// <summary>인물 번호 → 그 인물의 전투 그림 첫 장(타워 · 적 표의 「그림」 칸, 사용자 요청 td-7). 파트1(20000+)은 파트1 폴더 Obss 에서 파트2 꼴로 바꿔 읽는다.</summary>
public sealed class ChrPictureConverter : System.Windows.Data.IValueConverter
{
    private static readonly Dictionary<int, BitmapSource?> Cache = [];
    private static GameDatabase? _db;
    private static GameFiles? _game;
    private static bool _ready;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int chr ? Picture(chr) : null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public static BitmapSource? Picture(int chr)
    {
        if (chr <= 0) return null;
        if (Cache.TryGetValue(chr, out var known)) return known;
        BitmapSource? image = null;
        try
        {
            byte[]? obs = null;
            if (chr >= TowerDefenseBook.Part1Base)
            {
                if (Part1Game.Character(chr - TowerDefenseBook.Part1Base) is { SpriteId: > 0 } p1 && Part1Game.Read("Obss", $"{p1.SpriteId:D4}.obs") is { } raw)
                    obs = Part1Game.ToPart2Obs(raw);
            }
            else
            {
                if (!_ready)
                {
                    _ready = true;
                    try { _db = GameDatabase.Load(GameFiles.FromFolder(AssetsFolder.Find("data"))); } catch (Exception ex) when (ex is IOException or InvalidDataException) { }
                    _game = MainWindow.LoadSavedGameRoot() is { Length: > 0 } root && Directory.Exists(root) ? GameFiles.FromGameRoot(root) : null;
                }
                if (_db?.Character(chr) is { SpriteId: > 0 } c)
                {
                    string characters = AssetsFolder.Find("characters");
                    foreach (string folder in Directory.Exists(characters) ? Directory.GetDirectories(characters, $"{chr:D4}_*") : [])
                        if (System.IO.Path.Combine(folder, $"{c.SpriteId:D4}.obs") is var path && File.Exists(path)) obs = File.ReadAllBytes(path);
                    obs ??= _game?.Read("Obs", $"{c.SpriteId:D4}.obs");
                }
            }
            if (obs != null && ObsSprite.DecodeFirstFrame(obs) is { Width: > 0, Height: > 0 } f)
            {
                image = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
                image.Freeze();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or UnauthorizedAccessException) { }
        return Cache[chr] = image;
    }
}

public partial class TowerDefenseEditWindow : Window
{
    /// <summary>웨이브 칸의 한 줄 — 스테이지의 웨이브 목록을 펼친 것.</summary>
    public sealed class WaveRow
    {
        internal TowerDefenseEditWindow? Owner;
        public int Wave { get; set; } = 1;
        public int Enemy { get; set; }
        public int Count { get; set; } = 5;
        public double Every { get; set; } = 1;
        public string EnemyName => Owner?._stage is { } s && Enemy >= 0 && Enemy < s.Enemies.Count ? s.Enemies[Enemy].Name : "(없는 번호)";
    }



    private static readonly Dictionary<string, string> WeaponNames = new()
    {
        ["Sword"] = "장검", ["Spear"] = "창", ["DualBlade"] = "쌍검", ["GreatSword"] = "대검", ["Bow"] = "활", ["Gun"] = "총",
        ["Beam"] = "빔", ["Magic"] = "마법", ["Frost"] = "냉기(감속)",
    };

    private static readonly Dictionary<string, string> AuraNames = new()
    {
        ["None"] = "없음", ["Power"] = "공격력", ["Haste"] = "공격 속도", ["Range"] = "사거리",
    };

    private readonly string _folder;
    private readonly GameFiles _files;
    private readonly ObservableCollection<TdStageEntry> _stages = [];
    private readonly ObservableCollection<TdTowerEntry> _towers = [];
    private readonly ObservableCollection<TdWeaponEntry> _weapons = [];
    private readonly ObservableCollection<TdEnemyEntry> _enemies = [];
    private readonly ObservableCollection<WaveRow> _waves = [];
    private TdStageEntry? _stage;
    private ObtMapImage? _map;
    private bool _filling;

    public TowerDefenseEditWindow()
    {
        InitializeComponent();
        string data = AssetsFolder.Find("data");
        _folder = System.IO.Path.Combine(data, TowerDefenseBook.Folder);
        _files = GameFiles.FromFolder(data);

        TowerWeaponColumn.ItemsSource = WeaponNames.ToList();
        TowerAuraColumn.ItemsSource = AuraNames.ToList();
        StageList.ItemsSource = _stages;
        TowerGrid.ItemsSource = _towers;
        WeaponGrid.ItemsSource = _weapons;
        EnemyGrid.ItemsSource = _enemies;
        WaveGrid.ItemsSource = _waves;
        foreach (var grid in new[] { TowerGrid, WeaponGrid, EnemyGrid, WaveGrid }) grid.BeginningEdit += (_, _) => PushUndo(commitGrids: false);
        _towers.CollectionChanged += (_, _) => RebuildTree();
        TowerGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(() => { RebuildTree(); TowerGrid.CommitEdit(DataGridEditingUnit.Row, true); if (!TowerGrid.IsKeyboardFocusWithin || TowerGrid.CurrentCell.IsValid) try { TowerGrid.Items.Refresh(); } catch (InvalidOperationException) { } }, System.Windows.Threading.DispatcherPriority.Background);
        EnemyGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(() => { EnemyGrid.CommitEdit(DataGridEditingUnit.Row, true); if (!EnemyGrid.IsKeyboardFocusWithin || EnemyGrid.CurrentCell.IsValid) try { EnemyGrid.Items.Refresh(); } catch (InvalidOperationException) { } }, System.Windows.Threading.DispatcherPriority.Background);
        WaveGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(() => { WaveGrid.CommitEdit(DataGridEditingUnit.Row, true); if (!WaveGrid.IsKeyboardFocusWithin || WaveGrid.CurrentCell.IsValid) try { WaveGrid.Items.Refresh(); } catch (InvalidOperationException) { } }, System.Windows.Threading.DispatcherPriority.Background);

        Fill(TowerDefenseBook.LoadTowers(_files), TowerDefenseBook.LoadStages(_files));
        StatusText.Text = Directory.Exists(_folder) && Directory.EnumerateFiles(_folder, "*.json").Any()
            ? $"{_folder} 에서 읽었습니다."
            : "아직 고친 것이 없어 게임의 바탕 값을 보입니다 — 저장하면 그때부터 이 파일들을 씁니다.";
    }

    private void Fill(TdTowersFile towers, List<TdStageEntry> stages)
    {
        _towers.Clear(); _weapons.Clear(); _stages.Clear();
        foreach (var t in towers.Towers) _towers.Add(t);
        foreach (var w in towers.Weapons.OrderBy(w => Array.IndexOf(TowerDefenseBook.WeaponKeys, w.Key))) _weapons.Add(w);
        foreach (var s in stages) _stages.Add(s);
        _stage = null;
        StageList.SelectedIndex = _stages.Count > 0 ? 0 : -1;
    }

    // ── 스테이지 고르기 · 칸 ──

    private void StageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filling) return;
        CommitStage();
        ShowStage(StageList.SelectedItem as TdStageEntry);
    }

    private void ShowStage(TdStageEntry? s, bool scroll = true)
    {
        _stage = s;
        StagePanel.IsEnabled = s != null;
        _enemies.Clear(); _waves.Clear();
        if (s == null) { MapImage.Source = null; MapOverlay.Children.Clear(); return; }
        _filling = true;
        NameBox.Text = s.Name;
        MapBox.Text = s.Map;
        GoldBox.Text = s.StartGold.ToString(CultureInfo.InvariantCulture);
        LivesBox.Text = s.StartLives.ToString(CultureInfo.InvariantCulture);
        HpBox.Text = s.HpPerWave.ToString(CultureInfo.InvariantCulture);
        BgmBox.Text = s.Bgm.ToString(CultureInfo.InvariantCulture);
        IdBox.Text = s.Id;
        _filling = false;
        foreach (var en in s.Enemies) _enemies.Add(en);
        for (int w = 0; w < s.Waves.Count; w++)
            foreach (var g in s.Waves[w]) _waves.Add(new WaveRow { Owner = this, Wave = w + 1, Enemy = g.Enemy, Count = g.Count, Every = g.Every });
        LoadMap(scroll);
    }

    /// <summary>칸에 적은 것을 지금 스테이지에 옮긴다 — 다른 스테이지로 갈 때와 저장할 때.</summary>
    private void CommitStage(bool commitGrids = true)
    {
        if (_stage is not { } s) return;
        s.Name = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : "이름 없음";
        s.Map = MapBox.Text.Trim();
        s.Id = IdBox.Text.Trim().Length > 0 ? IdBox.Text.Trim() : s.Map;
        if (int.TryParse(GoldBox.Text, out int g)) s.StartGold = Math.Max(0, g);
        if (int.TryParse(LivesBox.Text, out int l)) s.StartLives = Math.Max(1, l);
        if (double.TryParse(HpBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double h)) s.HpPerWave = Math.Max(0, h);
        if (int.TryParse(BgmBox.Text, out int b)) s.Bgm = b;
        if (commitGrids)
        {
            EnemyGrid.CommitEdit(DataGridEditingUnit.Row, true);
            WaveGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }
        s.Enemies = [.. _enemies];
        s.Waves = _waves.Where(r => r.Wave >= 1).GroupBy(r => r.Wave).OrderBy(gr => gr.Key)
            .Select(gr => gr.Select(r => new TdSpawnEntry { Enemy = r.Enemy, Count = r.Count, Every = r.Every }).ToList()).ToList();
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_filling || _stage == null) return;
        _stage.Name = NameBox.Text;
        StageList.Items.Refresh();
    }

    private void StageAdd_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        var basis = TowerDefenseBook.DefaultStages()[0];
        var s = new TdStageEntry
        {
            Id = $"custom-{DateTime.Now:yyyyMMddHHmmss}", Name = $"새 스테이지 {_stages.Count + 1}", Map = basis.Map,
            Enemies = basis.Enemies, Waves = basis.Waves,
        };
        _stages.Add(s);
        StageList.SelectedItem = s;
    }

    private void StageCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        PushUndo();
        var copy = System.Text.Json.JsonSerializer.Deserialize<TdStageEntry>(TowerDefenseBook.ToJson(_stage),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        copy.Id = $"custom-{DateTime.Now:yyyyMMddHHmmss}";
        copy.Name += " 사본";
        _stages.Insert(_stages.IndexOf(_stage) + 1, copy);
        StageList.SelectedItem = copy;
    }

    private void StageDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        if (_stages.Count <= 1) { StatusText.Text = "스테이지는 하나는 있어야 합니다."; return; }
        PushUndo();
        int i = _stages.IndexOf(_stage);
        _stage = null;
        _stages.RemoveAt(i);
        StageList.SelectedIndex = Math.Min(i, _stages.Count - 1);
    }

    private void StageUp_Click(object sender, RoutedEventArgs e) => MoveStage(-1);
    private void StageDown_Click(object sender, RoutedEventArgs e) => MoveStage(1);

    private void MoveStage(int d)
    {
        if (_stage == null) return;
        int i = _stages.IndexOf(_stage), j = i + d;
        if (j < 0 || j >= _stages.Count) return;
        PushUndo();
        _filling = true;
        _stages.Move(i, j);
        _filling = false;
        StageList.SelectedItem = _stage;
    }

    // ── 맵 · 길 ──

    private void MapPick_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        var pick = new TdMapPickWindow(_files, MapBox.Text.Trim()) { Owner = this };
        if (pick.ShowDialog() != true || pick.Picked is not { } map) return;
        PushUndo();
        MapBox.Text = map;
        _stage.Map = map;
        _stage.CamX = _stage.CamY = -1;
        _stage.AreaW = _stage.AreaH = 0;
        LoadMap();
    }

    private void MapLoad_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        PushUndo();
        _stage.Map = MapBox.Text.Trim();
        _stage.CamX = _stage.CamY = -1;
        LoadMap();
    }

    /// <summary>스테이지의 맵을 읽는다 — part1:NNNN 은 파트1 폴더 Obts, NNNN.obt 는 assets/maps(게임과 같은 곳).</summary>
    internal static ObtMapImage? ReadMap(GameFiles files, string name)
    {
        try
        {
            if (name.StartsWith("part1:", StringComparison.Ordinal)) return int.TryParse(name[6..], out int id) ? Part1Map.Load(id) : null;
            // 파트2 맵은 게임과 같이 assets/maps(원본 폴더에서 뽑아 둔 것), 없으면 data/Obt
            string loose = System.IO.Path.Combine(AssetsFolder.Find("maps"), name);
            if (File.Exists(loose)) return ObtMap.Parse(File.ReadAllBytes(loose), name);
            return files.Read("Obt", name) is { } bytes ? ObtMap.Parse(bytes, name) : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    private void LoadMap(bool scroll = true)
    {
        _map = null;
        MapImage.Source = null;
        if (_stage is not { } s) return;
        _map = ReadMap(_files, s.Map);
        if (_map is not { } m)
        {
            StatusText.Text = s.Map.StartsWith("part1:", StringComparison.Ordinal) && Part1Game.Root == null
                ? $"맵 {s.Map} 을 못 읽었습니다 — 파트1 폴더가 없습니다. 게임에서도 파트2 사막으로 대신 열립니다."
                : $"맵 {s.Map} 을 못 읽었습니다.";
            MapOverlay.Children.Clear();
            return;
        }
        var bitmap = BitmapSource.Create(m.Width, m.Height, 96, 96, PixelFormats.Bgra32, null, m.Bgra, m.Width * 4);
        bitmap.Freeze();
        MapImage.Source = bitmap;
        MapOverlay.Width = m.Width;
        MapOverlay.Height = m.Height;
        // 옛 꼴(보는 네모 안 칸) 경유점은 맵 칸으로 바꿔 둔다 — 저장하면 맵 칸으로 적힌다.
        if (s.ViewCells)
        {
            var area = TowerDefenseBook.Area(m, s);
            s.Waypoints = s.Waypoints.Select(p => TowerDefenseBook.ViewToMap(m, area.X, area.Y, p[0], p[1]) is var (c, r) ? new[] { c, r } : p).ToList();
            s.ViewCells = false;
        }
        Redraw();
        if (!scroll) return;
        // 노란 네모(싸움터)가 보이게 굴린다
        var (vx, vy, _, _) = TowerDefenseBook.Area(m, s);
        MapScroll.UpdateLayout();
        MapScroll.ScrollToHorizontalOffset(Math.Max(0, vx - 40));
        MapScroll.ScrollToVerticalOffset(Math.Max(0, vy - 40));
    }

    private static (double X, double Y) CellTop(ObtMapImage m, int col, int row) =>
        (col * TowerDefenseBook.Cell, row * TowerDefenseBook.CellH - m.HeightAt(col, row) * TowerDefenseBook.HeightStep - m.OriginY);

    private void Redraw()
    {
        MapOverlay.Children.Clear();
        if (_map is not { } m || _stage is not { } s) return;
        var field = TdField.Of(m, s);
        var (ax, ay, aw, ah) = field.Area;
        var opened = s.Open.Select(p => (p[0], p[1])).ToHashSet();
        var blockedHand = s.Blocked.Select(p => (p[0], p[1])).ToHashSet();

        // 못 걷는 칸 — 어둡게, 손으로 막은 칸은 붉게, 손으로 연 칸은 파란 테
        var blocked = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0));
        var handBlocked = new SolidColorBrush(Color.FromArgb(120, 200, 40, 40));
        blocked.Freeze(); handBlocked.Freeze();
        for (int r = 0; r < m.Rows; r++)
            for (int c = 0; c < m.Cols; c++)
            {
                if (blockedHand.Contains((c, r))) AddRect(CellTop(m, c, r), TowerDefenseBook.Cell, TowerDefenseBook.CellH, handBlocked, null);
                else if (!field.Ground(c, r)) AddRect(CellTop(m, c, r), TowerDefenseBook.Cell, TowerDefenseBook.CellH, blocked, null);
                if (opened.Contains((c, r))) AddRect(CellTop(m, c, r), TowerDefenseBook.Cell, TowerDefenseBook.CellH, null, Brushes.DeepSkyBlue, 1);
            }

        // 길 — 입구 초록 → 출구 빨강
        var path = field.BuildPath(s.Waypoints.Select(p => (p[0], p[1])).ToList());
        for (int i = 0; i < path.Count; i++)
        {
            double t = path.Count > 1 ? i / (double)(path.Count - 1) : 0;
            var fill = new SolidColorBrush(Color.FromArgb(120, (byte)(64 + 160 * t), (byte)(192 - 120 * t), 64));
            AddRect(CellTop(m, path[i].Col, path[i].Row), TowerDefenseBook.Cell, TowerDefenseBook.CellH, fill, null);
        }

        // 싸움터 네모 — 노랑, 손잡이 여덟, 게임 첫 화면(640×364)은 점선
        var view = new Rectangle { Width = aw, Height = ah, Stroke = Brushes.Yellow, StrokeThickness = 2, IsHitTestVisible = false };
        Canvas.SetLeft(view, ax);
        Canvas.SetTop(view, ay);
        MapOverlay.Children.Add(view);
        if (aw > TowerDefenseBook.ViewW || ah > TowerDefenseBook.ViewH)
        {
            var screen = new Rectangle
            {
                Width = Math.Min(aw, TowerDefenseBook.ViewW), Height = Math.Min(ah, TowerDefenseBook.ViewH), Stroke = Brushes.Yellow, StrokeThickness = 1,
                StrokeDashArray = [4, 4], IsHitTestVisible = false,
            };
            Canvas.SetLeft(screen, ax);
            Canvas.SetTop(screen, ay);
            MapOverlay.Children.Add(screen);
        }
        foreach (var (hx, hy) in new[] { (ax, ay), (ax + aw, ay), (ax, ay + ah), (ax + aw, ay + ah), (ax + aw / 2, ay), (ax + aw / 2, ay + ah), (ax, ay + ah / 2), (ax + aw, ay + ah / 2) })
            AddRect((hx - 5, hy - 5), 10, 10, Brushes.Yellow, Brushes.Black, 1);
        bool big = aw > TowerDefenseBook.ViewW || ah > TowerDefenseBook.ViewH;
        var size = new TextBlock
        {
            Text = $"싸움터 {aw}×{ah}" + (big ? " (점선 = 게임 한 화면, 나머지는 굴려 봄)" : ""), Foreground = Brushes.Yellow,
            Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), IsHitTestVisible = false,
        };
        Canvas.SetLeft(size, ax + 8);
        Canvas.SetTop(size, Math.Max(0, ay - 20));
        MapOverlay.Children.Add(size);

        // 경유점 — 번호, 첫 · 끝은 입구 · 출구, 싸움터 밖이면 주황 테
        for (int i = 0; i < s.Waypoints.Count; i++)
        {
            var (x, y) = CellTop(m, s.Waypoints[i][0], s.Waypoints[i][1]);
            var color = i == 0 ? Brushes.LimeGreen : i == s.Waypoints.Count - 1 ? Brushes.Red : Brushes.White;
            AddRect((x + 2, y + 2), TowerDefenseBook.Cell - 4, TowerDefenseBook.CellH - 4, null, color);
            string label = i == 0 ? "입구" : i == s.Waypoints.Count - 1 ? "출구" : (i + 1).ToString(CultureInfo.InvariantCulture);
            var text = new TextBlock { Text = label, Foreground = color, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), IsHitTestVisible = false };
            Canvas.SetLeft(text, x + 4);
            Canvas.SetTop(text, y + 6);
            MapOverlay.Children.Add(text);
            if (!field.Inside(s.Waypoints[i][0], s.Waypoints[i][1])) AddRect((x, y), TowerDefenseBook.Cell, TowerDefenseBook.CellH, null, Brushes.Orange, 1);
        }
        int outside = s.Waypoints.Count(p => !field.Inside(p[0], p[1]));
        StatusText.Text = $"{s.Name} — 맵 {m.Width}×{m.Height}, 길 {path.Count}칸, 경유점 {s.Waypoints.Count}개"
                          + (s.Waypoints.Count < 2 ? " (2개보다 적으면 게임이 저절로 고릅니다)" : "")
                          + (outside > 0 ? $" · ⚠ 경유점 {outside}개가 싸움터 밖이라 안쪽 칸으로 당겨집니다(주황 테)" : "");
    }

    private void AddRect((double X, double Y) at, double w, double h, Brush? fill, Brush? stroke, double thickness = 2)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = fill, Stroke = stroke, StrokeThickness = stroke != null ? thickness : 0, IsHitTestVisible = false };
        Canvas.SetLeft(r, at.X);
        Canvas.SetTop(r, at.Y);
        MapOverlay.Children.Add(r);
    }

    /// <summary>그림 좌표의 칸 — 높이 때문에 겹치면 앞(아래) 줄이 이긴다(게임의 CellAt 과 같은 셈).</summary>
    private (int Col, int Row) CellAt(Point p)
    {
        if (_map is not { } m) return (-1, -1);
        int col = (int)(p.X / TowerDefenseBook.Cell);
        if (col < 0 || col >= m.Cols) return (-1, -1);
        for (int row = m.Rows - 1; row >= 0; row--)
        {
            double top = CellTop(m, col, row).Y;
            if (p.Y >= top && p.Y < top + TowerDefenseBook.CellH) return (col, row);
        }
        return (-1, -1);
    }

    // 끌기 — 싸움터 네모 옮기기 · 크기(쥔 가장자리), 걷기 칸 칠하기(칠하는 값)
    private enum Drag { None, Move, Resize, Paint }
    private Drag _drag;
    private bool _dragLeft, _dragRight, _dragTop, _dragBottom, _paintOpen;
    private Point _dragFrom;
    private (int X, int Y, int W, int H) _dragArea;
    private (int, int) _lastPainted = (-1, -1);

    /// <summary>싸움터 네모의 어느 가장자리 근처인가(7px), 안쪽인가.</summary>
    private static (bool L, bool R, bool T, bool B, bool Inside) AreaHit(Point p, (int X, int Y, int W, int H) a)
    {
        const double Grip = 7;
        bool inX = p.X >= a.X - Grip && p.X <= a.X + a.W + Grip, inY = p.Y >= a.Y - Grip && p.Y <= a.Y + a.H + Grip;
        bool l = inY && Math.Abs(p.X - a.X) <= Grip, r = inY && Math.Abs(p.X - (a.X + a.W)) <= Grip;
        bool t = inX && Math.Abs(p.Y - a.Y) <= Grip, b = inX && Math.Abs(p.Y - (a.Y + a.H)) <= Grip;
        return (l, r, t, b, p.X > a.X && p.X < a.X + a.W && p.Y > a.Y && p.Y < a.Y + a.H);
    }

    private void MapOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_map is not { } m || _stage is not { } s) return;
        var p = e.GetPosition(MapOverlay);
        var area = TowerDefenseBook.Area(m, s);
        if (ToolGround.IsChecked == true)
        {
            var (gc, gr) = CellAt(p);
            if (gc < 0) return;
            PushUndo();
            _paintOpen = !TdField.Of(m, s).Ground(gc, gr);
            _drag = Drag.Paint;
            _lastPainted = (-1, -1);
            Paint(gc, gr);
            MapOverlay.CaptureMouse();
            return;
        }
        var hit = AreaHit(p, area);
        if (hit.L || hit.R || hit.T || hit.B)
        {
            PushUndo();
            (_drag, _dragLeft, _dragRight, _dragTop, _dragBottom) = (Drag.Resize, hit.L, hit.R, hit.T, hit.B);
        }
        else if (hit.Inside && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            PushUndo();
            _drag = Drag.Move;
        }
        else
        {
            if (CellAt(p) is var (c, r) && c >= 0)
            {
                PushUndo();
                s.Waypoints.Add([c, r]);
                Redraw();
            }
            return;
        }
        _dragFrom = p;
        _dragArea = area;
        MapOverlay.CaptureMouse();
    }

    private void MapOverlay_MouseMove(object sender, MouseEventArgs e)
    {
        if (_map is not { } m || _stage is not { } s) return;
        var p = e.GetPosition(MapOverlay);
        if (_drag == Drag.None)
        {
            // 커서 모양으로 끌 수 있는 곳을 알린다
            var hit = AreaHit(p, TowerDefenseBook.Area(m, s));
            MapOverlay.Cursor = ToolGround.IsChecked == true ? Cursors.Pen
                : (hit.L && hit.T) || (hit.R && hit.B) ? Cursors.SizeNWSE : (hit.R && hit.T) || (hit.L && hit.B) ? Cursors.SizeNESW
                : hit.L || hit.R ? Cursors.SizeWE : hit.T || hit.B ? Cursors.SizeNS
                : hit.Inside && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? Cursors.SizeAll : Cursors.Cross;
            return;
        }
        if (_drag == Drag.Paint)
        {
            if (CellAt(p) is var (c, r) && c >= 0) Paint(c, r);
            return;
        }
        int dx = (int)(p.X - _dragFrom.X), dy = (int)(p.Y - _dragFrom.Y);
        var (x, y, w, h) = _dragArea;
        const int Min = 160;
        if (_drag == Drag.Move) { x += dx; y += dy; }
        else
        {
            if (_dragLeft) { int nx = Math.Min(x + dx, x + w - Min); w += x - nx; x = nx; }
            if (_dragRight) w = Math.Max(Min, w + dx);
            if (_dragTop) { int ny = Math.Min(y + dy, y + h - Min); h += y - ny; y = ny; }
            if (_dragBottom) h = Math.Max(Min, h + dy);
        }
        w = Math.Min(w, m.Width);
        h = Math.Min(h, m.Height);
        x = Math.Clamp(x, 0, m.Width - w);
        y = Math.Clamp(y, 0, m.Height - h);
        (s.CamX, s.CamY, s.AreaW, s.AreaH) = (x, y, w, h);
        Redraw();
    }

    private void MapOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == Drag.None) return;
        _drag = Drag.None;
        MapOverlay.ReleaseMouseCapture();
        Redraw();
    }

    /// <summary>걷기 칸 칠하기 — 맵 자료 그대로가 되면 덮어쓰기에서 뺀다.</summary>
    private void Paint(int c, int r)
    {
        if (_map is not { } m || _stage is not { } s || _lastPainted == (c, r)) return;
        _lastPainted = (c, r);
        s.Open.RemoveAll(p => p[0] == c && p[1] == r);
        s.Blocked.RemoveAll(p => p[0] == c && p[1] == r);
        bool natural = (m.FlagsAt(c, r) & 0x9) == 0;
        if (_paintOpen && !natural) s.Open.Add([c, r]);
        if (!_paintOpen && natural) s.Blocked.Add([c, r]);
        Redraw();
    }

    private void MapOverlay_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_map is not { } m || _stage is not { Waypoints.Count: > 0 } s || ToolGround.IsChecked == true) return;
        var p = e.GetPosition(MapOverlay);
        PushUndo();
        int nearest = Enumerable.Range(0, s.Waypoints.Count)
            .MinBy(i => CellTop(m, s.Waypoints[i][0], s.Waypoints[i][1]) is var (x, y) ? Math.Pow(x + 20 - p.X, 2) + Math.Pow(y + 16 - p.Y, 2) : 0);
        s.Waypoints.RemoveAt(nearest);
        Redraw();
    }

    private void PathClear_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        PushUndo();
        _stage.Waypoints.Clear();
        Redraw();
    }

    private void GroundClear_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        PushUndo();
        _stage.Open.Clear();
        _stage.Blocked.Clear();
        Redraw();
    }

    private void CamCenter_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        PushUndo();
        _stage.CamX = _stage.CamY = -1;
        Redraw();
    }

    private void AreaReset_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        PushUndo();
        _stage.AreaW = _stage.AreaH = 0;
        Redraw();
    }

    // ── 실행 취소(Ctrl+Z, 사용자 요청 td-9) — 바꾸기 전 모든 자료를 통째로 적어 둔다 ──

    private readonly List<(string Towers, List<string> Stages, int Selected)> _undo = [];
    private static readonly System.Text.Json.JsonSerializerOptions UndoJson = new() { PropertyNameCaseInsensitive = true };

    /// <param name="commitGrids">표 칸 고치기를 시작할 때는 거짓 — 그 표를 건드리지 않고 지금 값만 적는다.</param>
    private void PushUndo(bool commitGrids = true)
    {
        CommitStage(commitGrids);
        if (commitGrids)
        {
            TowerGrid.CommitEdit(DataGridEditingUnit.Row, true);
            WeaponGrid.CommitEdit(DataGridEditingUnit.Row, true);
        }
        _undo.Add((TowerDefenseBook.ToJson(new TdTowersFile { Weapons = [.. _weapons], Towers = [.. _towers] }),
                   _stages.Select(TowerDefenseBook.ToJson).ToList(), StageList.SelectedIndex));
        if (_undo.Count > 200) _undo.RemoveAt(0);
    }

    private void Undo()
    {
        if (_undo.Count == 0) { StatusText.Text = "더 되돌릴 것이 없습니다."; return; }
        var (towers, stages, selected) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        var t = System.Text.Json.JsonSerializer.Deserialize<TdTowersFile>(towers, UndoJson)!;
        var list = stages.Select(j => System.Text.Json.JsonSerializer.Deserialize<TdStageEntry>(j, UndoJson)!).ToList();
        double hx = MapScroll.HorizontalOffset, vy = MapScroll.VerticalOffset;
        _filling = true;
        _towers.Clear(); _weapons.Clear(); _stages.Clear();
        foreach (var x in t.Towers) _towers.Add(x);
        foreach (var w in t.Weapons) _weapons.Add(w);
        foreach (var s in list) _stages.Add(s);
        _stage = null;
        StageList.SelectedIndex = Math.Clamp(selected, -1, _stages.Count - 1);
        _filling = false;
        ShowStage(StageList.SelectedItem as TdStageEntry, scroll: false);
        MapScroll.ScrollToHorizontalOffset(hx);
        MapScroll.ScrollToVerticalOffset(vy);
        StatusText.Text = $"되돌렸습니다 (남은 단계 {_undo.Count})";
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Z || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        // 글자 칸을 고치는 중이면 그 칸의 실행 취소를 쓴다
        if (Keyboard.FocusedElement is TextBox { IsReadOnly: false }) return;
        e.Handled = true;
        Undo();
    }

    // ── 적 · 웨이브 ──

    private void EnemyGrid_LoadingRow(object? sender, DataGridRowEventArgs e) => e.Row.Header = e.Row.GetIndex();

    private void EnemyAdd_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        _enemies.Add(new TdEnemyEntry { Chr = TowerDefenseBook.Part1Base + 144, Fallback = 230, Name = "솔져", Hp = 60, Speed = 1.3, Bounty = 6 });
    }

    private void EnemyDelete_Click(object sender, RoutedEventArgs e)
    {
        if (EnemyGrid.SelectedItem is not TdEnemyEntry en) return;
        PushUndo();
        _enemies.Remove(en);
        WaveGrid.Items.Refresh();
    }

    private void WaveAdd_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        int wave = _waves.Count > 0 ? _waves.Max(w => w.Wave) + 1 : 1;
        if (WaveGrid.SelectedItem is WaveRow sel) wave = sel.Wave;   // 고른 줄이 있으면 그 웨이브에 묶음을 더한다
        var row = new WaveRow { Owner = this, Wave = wave, Enemy = 0, Count = 5, Every = 1 };
        int at = WaveGrid.SelectedItem is WaveRow s ? _waves.IndexOf(s) + 1 : _waves.Count;
        _waves.Insert(at, row);
        WaveGrid.SelectedItem = row;
    }

    private void WaveDelete_Click(object sender, RoutedEventArgs e)
    {
        if (WaveGrid.SelectedItem is not WaveRow r) return;
        PushUndo();
        _waves.Remove(r);
    }

    // ── 타워 ──

    private void TowerAdd_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        var t = new TdTowerEntry { Chr = TowerDefenseBook.Part1Base + 155, Name = "새 타워", Weapon = "Sword", Cost = 60 };
        int at = TowerGrid.SelectedItem is TdTowerEntry s ? _towers.IndexOf(s) + 1 : _towers.Count;
        _towers.Insert(at, t);
        TowerGrid.SelectedItem = t;
    }

    private void TowerDelete_Click(object sender, RoutedEventArgs e)
    {
        if (TowerGrid.SelectedItem is not TdTowerEntry t) return;
        PushUndo();
        _towers.Remove(t);
    }

    private void TowerUp_Click(object sender, RoutedEventArgs e) => MoveTower(-1);
    private void TowerDown_Click(object sender, RoutedEventArgs e) => MoveTower(1);

    private void MoveTower(int d)
    {
        if (TowerGrid.SelectedItem is not TdTowerEntry t) return;
        int i = _towers.IndexOf(t), j = i + d;
        if (j < 0 || j >= _towers.Count) return;
        PushUndo();
        _towers.Move(i, j);
        TowerGrid.SelectedItem = t;
    }

    // ── 올리기 표(전직표, td-12) ──

    /// <summary>짓기 칸 타워부터 「다음 타워」를 따라 내려가는 나무 — 그림 · 이름 · 값. 없는 키는 붉게, 되돌아오는 고리는 끊는다.</summary>
    private void RebuildTree()
    {
        UpgradeTree.Items.Clear();
        var byKey = _towers.GroupBy(t => t.KeyOrName).ToDictionary(g => g.Key, g => g.First());
        TreeViewItem Node(TdTowerEntry t, HashSet<string> seen)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Image { Source = ChrPictureConverter.Picture(t.Chr), Width = 32, Height = 40, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
            row.Children.Add(new TextBlock
            {
                Text = $"{t.Name}  {t.Cost}골드 · {WeaponNames.GetValueOrDefault(t.Weapon, t.Weapon)}" + (t.Hero ? " · 영웅" : ""),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), FontWeight = t.Buildable ? FontWeights.Bold : FontWeights.Normal,
            });
            var item = new TreeViewItem { Header = row, Tag = t, IsExpanded = true };
            var next = new HashSet<string>(seen) { t.KeyOrName };
            foreach (string k in t.Upgrades)
            {
                if (!byKey.TryGetValue(k, out var n)) { item.Items.Add(new TreeViewItem { Header = $"「{k}」 — 없는 키", Foreground = Brushes.Red }); continue; }
                if (next.Contains(k)) { item.Items.Add(new TreeViewItem { Header = $"「{k}」 — 앞으로 되돌아감(고리)", Foreground = Brushes.OrangeRed }); continue; }
                item.Items.Add(Node(n, next));
            }
            return item;
        }
        foreach (var t in _towers.Where(t => t.Buildable)) UpgradeTree.Items.Add(Node(t, []));
        var reachable = new HashSet<string>();
        void Walk(TdTowerEntry t) { if (!reachable.Add(t.KeyOrName)) return; foreach (var k in t.Upgrades) if (byKey.TryGetValue(k, out var n)) Walk(n); }
        foreach (var t in _towers.Where(t => t.Buildable)) Walk(t);
        var orphans = _towers.Where(t => !reachable.Contains(t.KeyOrName)).ToList();
        if (orphans.Count > 0)
            UpgradeTree.Items.Add(new TreeViewItem { Header = $"어디서도 못 가는 타워: {string.Join(", ", orphans.Select(o => o.Name))}", Foreground = Brushes.OrangeRed });
    }

    private void UpgradeTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: TdTowerEntry t }) { TowerGrid.SelectedItem = t; TowerGrid.ScrollIntoView(t); }
    }

    // ── 저장 ──

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        TowerGrid.CommitEdit(DataGridEditingUnit.Row, true);
        WeaponGrid.CommitEdit(DataGridEditingUnit.Row, true);
        CommitStage();
        var problems = new List<string>();
        var stages = _stages.ToList();
        foreach (var s in stages)
        {
            // 검사는 사본으로 — 틀린 묶음을 지우는 Valid 가 편집 중인 표를 건드리지 않게
            var check = System.Text.Json.JsonSerializer.Deserialize<TdStageEntry>(TowerDefenseBook.ToJson(s), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            if (!TowerDefenseBook.Valid(check)) problems.Add($"「{s.Name}」: 맵 · 적 · 웨이브 중 빈 것이 있어 게임이 이 스테이지를 건너뜁니다");
            else if (check.Waves.Sum(w => w.Count) != s.Waves.Sum(w => w.Count)) problems.Add($"「{s.Name}」: 없는 적 번호를 가리키는 웨이브 줄은 게임이 버립니다");
        }
        if (_towers.Count(t => t.Buildable && !t.Hero) > 10 || _towers.Count(t => t.Buildable && t.Hero) > 10) problems.Add("한 탭에 10개가 넘으면 게임 짓기 칸에 다 안 보입니다");
        if (stages.Select(s => s.Id).Distinct().Count() != stages.Count) problems.Add("기록 열쇠가 겹치는 스테이지가 있습니다(최고 기록을 함께 씁니다)");
        try
        {
            Directory.CreateDirectory(_folder);
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(System.IO.Path.Combine(_folder, TowerDefenseBook.TowersFile),
                TowerDefenseBook.ToJson(new TdTowersFile { Weapons = [.. _weapons], Towers = [.. _towers] }), utf8);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < stages.Count; i++)
            {
                string name = TowerDefenseBook.StageFileName(i);
                File.WriteAllText(System.IO.Path.Combine(_folder, name), TowerDefenseBook.ToJson(stages[i]), utf8);
                written.Add(name);
            }
            foreach (var old in Directory.EnumerateFiles(_folder, TowerDefenseBook.StagePrefix + "*.json"))
                if (!written.Contains(System.IO.Path.GetFileName(old))) File.Delete(old);
            StatusText.Text = $"저장했습니다 — 타워 {_towers.Count}개 · 스테이지 {stages.Count}개 ({_folder}). 게임에서 타워 디펜스를 다시 열면 먹습니다."
                              + (problems.Count > 0 ? "  ⚠ " + string.Join(" · ", problems) : "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"저장하지 못했습니다: {ex.Message}";
        }
    }

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "스테이지와 타워를 모두 게임의 바탕 값으로 되돌립니다. (저장해야 파일에 적힙니다)", "타워 디펜스 편집",
                            MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        PushUndo();
        Fill(TowerDefenseBook.DefaultTowers(), TowerDefenseBook.DefaultStages());
        StatusText.Text = "바탕 값을 불러왔습니다 — 저장하면 파일에 적힙니다.";
    }
}

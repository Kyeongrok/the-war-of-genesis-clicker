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

    private static readonly string[] MapSuggestions =
        ["part1:0086", "part1:0093", "part1:0088", "part1:0100", "part1:0105", "part1:0011", "part1:0009", "part1:0079", "0153.obt"];

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
        MapBox.ItemsSource = MapSuggestions;
        StageList.ItemsSource = _stages;
        TowerGrid.ItemsSource = _towers;
        WeaponGrid.ItemsSource = _weapons;
        EnemyGrid.ItemsSource = _enemies;
        WaveGrid.ItemsSource = _waves;
        WaveGrid.CellEditEnding += (_, _) => Dispatcher.BeginInvoke(() => WaveGrid.Items.Refresh(), System.Windows.Threading.DispatcherPriority.Background);

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

    private void ShowStage(TdStageEntry? s)
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
        LoadMap();
    }

    /// <summary>칸에 적은 것을 지금 스테이지에 옮긴다 — 다른 스테이지로 갈 때와 저장할 때.</summary>
    private void CommitStage()
    {
        if (_stage is not { } s) return;
        s.Name = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : "이름 없음";
        s.Map = MapBox.Text.Trim();
        s.Id = IdBox.Text.Trim().Length > 0 ? IdBox.Text.Trim() : s.Map;
        if (int.TryParse(GoldBox.Text, out int g)) s.StartGold = Math.Max(0, g);
        if (int.TryParse(LivesBox.Text, out int l)) s.StartLives = Math.Max(1, l);
        if (double.TryParse(HpBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double h)) s.HpPerWave = Math.Max(0, h);
        if (int.TryParse(BgmBox.Text, out int b)) s.Bgm = b;
        EnemyGrid.CommitEdit(DataGridEditingUnit.Row, true);
        WaveGrid.CommitEdit(DataGridEditingUnit.Row, true);
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
        CommitStage();
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
        CommitStage();
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
        _filling = true;
        _stages.Move(i, j);
        _filling = false;
        StageList.SelectedItem = _stage;
    }

    // ── 맵 · 길 ──

    private void MapLoad_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        _stage.Map = MapBox.Text.Trim();
        _stage.CamX = _stage.CamY = -1;
        LoadMap();
    }

    private void LoadMap()
    {
        _map = null;
        MapImage.Source = null;
        if (_stage is not { } s) return;
        try
        {
            if (s.Map.StartsWith("part1:", StringComparison.Ordinal))
                _map = int.TryParse(s.Map[6..], out int id) ? Part1Map.Load(id) : null;
            else if (_files.Read("Obt", s.Map) is { } bytes)
                _map = ObtMap.Parse(bytes, s.Map);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
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
            var (cx, cy) = TowerDefenseBook.Camera(m, s.CamX, s.CamY);
            s.Waypoints = s.Waypoints.Select(p => TowerDefenseBook.ViewToMap(m, cx, cy, p[0], p[1]) is var (c, r) ? new[] { c, r } : p).ToList();
            s.ViewCells = false;
        }
        Redraw();
        // 노란 네모(게임 화면)가 보이게 굴린다
        var (vx, vy) = TowerDefenseBook.Camera(m, s.CamX, s.CamY);
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
        var (camX, camY) = TowerDefenseBook.Camera(m, s.CamX, s.CamY);

        // 못 걷는 칸 — 빗금 대신 옅은 검정
        var blocked = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
        blocked.Freeze();
        for (int r = 0; r < m.Rows; r++)
            for (int c = 0; c < m.Cols; c++)
                if ((m.FlagsAt(c, r) & 0x9) != 0) AddRect(CellTop(m, c, r), TowerDefenseBook.Cell, TowerDefenseBook.CellH, blocked, null);

        // 길 — 입구 초록 → 출구 빨강
        var path = TowerDefenseBook.BuildPath(m, camX, camY, s.Waypoints.Select(p => (p[0], p[1])).ToList());
        for (int i = 0; i < path.Count; i++)
        {
            double t = path.Count > 1 ? i / (double)(path.Count - 1) : 0;
            var fill = new SolidColorBrush(Color.FromArgb(120, (byte)(64 + 160 * t), (byte)(192 - 120 * t), 64));
            AddRect(CellTop(m, path[i].Col, path[i].Row), TowerDefenseBook.Cell, TowerDefenseBook.CellH, fill, null);
        }

        // 보는 네모(게임 화면) — 노랑
        var view = new Rectangle { Width = TowerDefenseBook.ViewW, Height = TowerDefenseBook.ViewH, Stroke = Brushes.Yellow, StrokeThickness = 2, IsHitTestVisible = false };
        Canvas.SetLeft(view, camX);
        Canvas.SetTop(view, camY);
        MapOverlay.Children.Add(view);

        // 경유점 — 번호, 첫 · 끝은 입구 · 출구
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
        }
        StatusText.Text = $"{s.Name} — 맵 {m.Width}×{m.Height}, 길 {path.Count}칸, 경유점 {s.Waypoints.Count}개" + (s.Waypoints.Count < 2 ? " (2개보다 적으면 게임이 저절로 고릅니다)" : "");
    }

    private void AddRect((double X, double Y) at, double w, double h, Brush? fill, Brush? stroke)
    {
        var r = new Rectangle { Width = w, Height = h, Fill = fill, Stroke = stroke, StrokeThickness = stroke != null ? 2 : 0, IsHitTestVisible = false };
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

    private void MapOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_map is not { } m || _stage is not { } s) return;
        var p = e.GetPosition(MapOverlay);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            s.CamX = (int)Math.Clamp(p.X - TowerDefenseBook.ViewW / 2, 0, Math.Max(0, m.Width - TowerDefenseBook.ViewW));
            s.CamY = (int)Math.Clamp(p.Y - TowerDefenseBook.ViewH / 2, 0, Math.Max(0, m.Height - TowerDefenseBook.ViewH));
        }
        else if (CellAt(p) is var (c, r) && c >= 0)
            s.Waypoints.Add([c, r]);
        Redraw();
    }

    private void MapOverlay_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_map is not { } m || _stage is not { Waypoints.Count: > 0 } s) return;
        var p = e.GetPosition(MapOverlay);
        int nearest = Enumerable.Range(0, s.Waypoints.Count)
            .MinBy(i => CellTop(m, s.Waypoints[i][0], s.Waypoints[i][1]) is var (x, y) ? Math.Pow(x + 20 - p.X, 2) + Math.Pow(y + 16 - p.Y, 2) : 0);
        s.Waypoints.RemoveAt(nearest);
        Redraw();
    }

    private void PathClear_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        _stage.Waypoints.Clear();
        Redraw();
    }

    private void PathUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_stage is not { Waypoints.Count: > 0 } s) return;
        s.Waypoints.RemoveAt(s.Waypoints.Count - 1);
        Redraw();
    }

    private void CamCenter_Click(object sender, RoutedEventArgs e)
    {
        if (_stage == null) return;
        _stage.CamX = _stage.CamY = -1;
        Redraw();
    }

    // ── 적 · 웨이브 ──

    private void EnemyGrid_LoadingRow(object? sender, DataGridRowEventArgs e) => e.Row.Header = e.Row.GetIndex();

    private void EnemyAdd_Click(object sender, RoutedEventArgs e) =>
        _enemies.Add(new TdEnemyEntry { Chr = TowerDefenseBook.Part1Base + 144, Fallback = 230, Name = "솔져", Hp = 60, Speed = 1.3, Bounty = 6 });

    private void EnemyDelete_Click(object sender, RoutedEventArgs e)
    {
        if (EnemyGrid.SelectedItem is TdEnemyEntry en) _enemies.Remove(en);
        WaveGrid.Items.Refresh();
    }

    private void WaveAdd_Click(object sender, RoutedEventArgs e)
    {
        int wave = _waves.Count > 0 ? _waves.Max(w => w.Wave) + 1 : 1;
        if (WaveGrid.SelectedItem is WaveRow sel) wave = sel.Wave;   // 고른 줄이 있으면 그 웨이브에 묶음을 더한다
        var row = new WaveRow { Owner = this, Wave = wave, Enemy = 0, Count = 5, Every = 1 };
        int at = WaveGrid.SelectedItem is WaveRow s ? _waves.IndexOf(s) + 1 : _waves.Count;
        _waves.Insert(at, row);
        WaveGrid.SelectedItem = row;
    }

    private void WaveDelete_Click(object sender, RoutedEventArgs e)
    {
        if (WaveGrid.SelectedItem is WaveRow r) _waves.Remove(r);
    }

    // ── 타워 ──

    private void TowerAdd_Click(object sender, RoutedEventArgs e)
    {
        var t = new TdTowerEntry { Chr = TowerDefenseBook.Part1Base + 155, Name = "새 타워", Weapon = "Sword", Cost = 60 };
        int at = TowerGrid.SelectedItem is TdTowerEntry s ? _towers.IndexOf(s) + 1 : _towers.Count;
        _towers.Insert(at, t);
        TowerGrid.SelectedItem = t;
    }

    private void TowerDelete_Click(object sender, RoutedEventArgs e)
    {
        if (TowerGrid.SelectedItem is TdTowerEntry t) _towers.Remove(t);
    }

    private void TowerUp_Click(object sender, RoutedEventArgs e) => MoveTower(-1);
    private void TowerDown_Click(object sender, RoutedEventArgs e) => MoveTower(1);

    private void MoveTower(int d)
    {
        if (TowerGrid.SelectedItem is not TdTowerEntry t) return;
        int i = _towers.IndexOf(t), j = i + d;
        if (j < 0 || j >= _towers.Count) return;
        _towers.Move(i, j);
        TowerGrid.SelectedItem = t;
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
        if (_towers.Count(t => !t.Hero) > 10 || _towers.Count(t => t.Hero) > 10) problems.Add("한 탭에 10개가 넘으면 게임 짓기 칸에 다 안 보입니다");
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
        Fill(TowerDefenseBook.DefaultTowers(), TowerDefenseBook.DefaultStages());
        StatusText.Text = "바탕 값을 불러왔습니다 — 저장하면 파일에 적힙니다.";
    }
}

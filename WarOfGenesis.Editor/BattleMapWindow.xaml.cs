using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarOfGenesis.Assets;
using Line = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WarOfGenesis.Editor;

/// <summary>
/// 게임의 모든 전투(<c>Btl</c>)와 전투 맵(<c>Obt</c>)을 목록으로 보고, 고른 것을 원본 맵 위에 인물·배치 칸과 함께 그리는 창.
/// </summary>
/// <remarks>
/// 전투 → <c>Map/NNNN.map</c> 둘째 워드 → 배경 <c>Obt</c>. 인물 그림은 Chr 의 sprite 번호로 <c>Obs</c> 첫 장을 쓴다.
/// 게임 폴더(낱장 + pak)에서 바로 읽는다. 테두리 색: 파랑 플레이어, 초록 동맹 AI, 빨강 적. 노란 칸은 플레이어 배치 칸.
/// </remarks>
public partial class BattleMapWindow : Window
{
    private const int TileW = ObtMap.CellWidth, TileH = ObtMap.CellHeight;

    public sealed record Entry(string Label, BattleFile? Battle, int ObtId, string Title)
    {
        public override string ToString() => Label;
    }

    private readonly GameFiles? _files;
    private readonly GameDatabase? _db;
    private readonly List<Entry> _entries = [];
    private ICollectionView? _view;
    private readonly Dictionary<int, BitmapSource?> _spriteCache = [];
    private int _loadVersion;

    private (Entry Entry, ObtMapImage? Map, BitmapSource? Background, Dictionary<int, BitmapSource?> Sprites, string Error)? _shown;

    public BattleMapWindow(string gameRoot, GameDatabase? db)
    {
        InitializeComponent();
        if (gameRoot.Length == 0 || !Directory.Exists(gameRoot))
        {
            ListStatus.Text = "먼저 메인 창에서 게임 폴더를 여세요.";
            return;
        }
        _files = GameFiles.FromGameRoot(gameRoot);
        _db = db;
        Loaded += (_, _) => StartLoadingList();
    }

    // ── 목록 ─────────────────────────────────────────────────────────────────

    private void StartLoadingList()
    {
        ListStatus.Text = "전투·맵 목록을 읽는 중...";
        var files = _files!;
        var db = _db;
        System.Threading.Tasks.Task.Run(() =>
        {
            var entries = new List<Entry>();
            var usedObt = new HashSet<int>();
            var obtIds = files.List("Obt", ".obt").Keys.Select(IdOf).Where(i => i >= 0).ToHashSet();

            foreach (string name in files.List("Btl", ".btl").Keys)
            {
                int id = IdOf(name);
                if (id < 0 || BattleFile.Parse(id, files.Read("Btl", name)) is not { } battle) continue;
                int obt = BattleFile.ObtOfMap(files.Read("Map", $"{battle.MapId:D4}.map")) ?? -1;
                if (obt >= 0) usedObt.Add(obt);
                string title = db?.T(battle.TitleId) ?? "";
                string label = $"Btl {id:D4}  {(title.Length > 0 ? title : "(이름 없음)")}  · Obt {(obt >= 0 ? obt.ToString("D4") : "?")}";
                entries.Add(new Entry(label, battle, obt, title));
            }
            foreach (int obt in obtIds.Where(o => !usedObt.Contains(o)).OrderBy(o => o))
                entries.Add(new Entry($"Obt {obt:D4}  (전투에 안 쓰임)", null, obt, ""));

            Dispatcher.BeginInvoke(() => ShowList(entries, obtIds.Count));
        });
    }

    private static int IdOf(string fileName) => int.TryParse(Path.GetFileNameWithoutExtension(fileName), out int id) ? id : -1;

    private void ShowList(List<Entry> entries, int obtCount)
    {
        _entries.Clear();
        _entries.AddRange(entries);
        MapList.ItemsSource = _entries;
        _view = CollectionViewSource.GetDefaultView(_entries);
        _view.Filter = Accept;
        ListStatus.Text = $"전투 {entries.Count(e => e.Battle != null)}개 · 맵 {obtCount}개";
        var first = _entries.FirstOrDefault(e => e.Battle?.Id == BattleDemoScene.BtlId) ?? _entries.FirstOrDefault();
        if (first != null) MapList.SelectedItem = first;
    }

    private bool Accept(object o)
    {
        if (o is not Entry e) return false;
        if (e.Battle == null && ShowBareMapsToggle.IsChecked != true) return false;
        string q = FilterBox.Text.Trim();
        return q.Length == 0 || e.Label.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => _view?.Refresh();

    // ── 고른 맵 그리기 ───────────────────────────────────────────────────────

    private void MapList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MapList.SelectedItem is not Entry entry || _files == null) return;
        int version = ++_loadVersion;
        StatusText.Text = $"{entry.Label} 읽는 중...";
        var files = _files;
        var db = _db;
        System.Threading.Tasks.Task.Run(() =>
        {
            ObtMapImage? map = null;
            BitmapSource? background = null;
            string error = "";
            var sprites = new Dictionary<int, BitmapSource?>();
            try
            {
                if (entry.ObtId >= 0 && files.Read("Obt", $"{entry.ObtId:D4}.obt") is { } obt)
                {
                    map = ObtMap.Parse(obt, $"{entry.ObtId:D4}.obt");
                    background = BitmapSource.Create(map.Width, map.Height, 96, 96, PixelFormats.Bgra32, null, map.Bgra, map.Width * 4);
                    background.Freeze();
                }
                else error = $"Obt {entry.ObtId:D4} 를 못 찾았습니다.";

                foreach (var unit in entry.Battle?.Units ?? [])
                    if (!sprites.ContainsKey(unit.ChrCode)) sprites[unit.ChrCode] = SpriteFor(files, db, unit.ChrCode);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
            {
                error = ex.Message;
            }
            Dispatcher.BeginInvoke(() =>
            {
                if (version != _loadVersion) return;
                _shown = (entry, map, background, sprites, error);
                Redraw();
            });
        });
    }

    private BitmapSource? SpriteFor(GameFiles files, GameDatabase? db, int chrCode)
    {
        if (db?.Character(chrCode) is not { } c) return null;
        lock (_spriteCache)
            if (_spriteCache.TryGetValue(c.SpriteId, out var cached)) return cached;

        BitmapSource? bitmap = null;
        if (files.Read("Obs", $"{c.SpriteId:D4}.obs") is { } obs && ObsSprite.DecodeFirstFrame(obs) is { } frame)
        {
            bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4);
            bitmap.Freeze();
        }
        lock (_spriteCache) _spriteCache[c.SpriteId] = bitmap;
        return bitmap;
    }

    private void View_Changed(object sender, RoutedEventArgs e) => Redraw();

    private void Redraw()
    {
        Board.Children.Clear();
        if (_shown is not { } shown) return;
        var (entry, map, background, sprites, error) = shown;

        int cols = map?.Cols ?? 0, rows = map?.Rows ?? 0;
        Board.Width = Math.Max(cols * TileW, map?.Width ?? 0);
        Board.Height = rows * TileH;

        if (background != null)
        {
            var image = new Image { Source = background, Width = background.PixelWidth, Height = background.PixelHeight };
            Canvas.SetTop(image, map!.OriginY);
            Board.Children.Add(image);
        }
        if (GridToggle.IsChecked == true) DrawGridLines(cols, rows);
        if (UnitsToggle.IsChecked == true && entry.Battle is { } battle) DrawBattle(battle, sprites);

        StatusText.Text = Describe(entry, map, error);
    }

    private string Describe(Entry entry, ObtMapImage? map, string error)
    {
        var lines = new List<string>();
        string size = map != null ? $"{map.Cols}×{map.Rows}칸" : "";
        if (entry.Battle is { } b)
        {
            string T(ushort id) => _db?.T(id) ?? "";
            lines.Add($"Btl {b.Id:D4}  「{entry.Title}」   Map {b.MapId:D4} → Obt {entry.ObtId:D4} {size}   BGM {b.Bgm}");
            lines.Add($"승리: {T(b.WinId)}   패배: {T(b.LoseId)}");
            var groups = b.Units.GroupBy(u => u.Side).OrderByDescending(g => g.Key)
                .Select(g => $"{BattleFile.SideName(g.Key)} {g.Count()}: " + string.Join(", ", g.Select(u => $"{ChrName(u.ChrCode)}({u.X},{u.Y})")));
            lines.AddRange(groups);
            if (b.Placement.Count > 0) lines.Add($"배치 칸 {b.Placement.Count}개");
            if (b.Objects.Count > 0) lines.Add($"오브젝트 {b.Objects.Count}개");
            if (b.ParseError.Length > 0) lines.Add($"※ {b.ParseError}");
        }
        else lines.Add($"Obt {entry.ObtId:D4} {size} — 이 맵을 쓰는 전투(Btl)가 없습니다.");
        if (error.Length > 0) lines.Add($"못 읽음: {error}");
        return string.Join("\n", lines);
    }

    private string ChrName(int code)
    {
        if (_db?.Character(code) is not { } c) return $"Chr {code}";
        string name = _db.T(c.NameId);
        return name.Length > 0 ? name : $"Chr {code}";
    }

    private void DrawGridLines(int cols, int rows)
    {
        var brush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
        brush.Freeze();
        for (int c = 0; c <= cols; c++)
            Board.Children.Add(new Line { X1 = c * TileW, X2 = c * TileW, Y1 = 0, Y2 = rows * TileH, Stroke = brush, StrokeThickness = 1 });
        for (int r = 0; r <= rows; r++)
            Board.Children.Add(new Line { X1 = 0, X2 = cols * TileW, Y1 = r * TileH, Y2 = r * TileH, Stroke = brush, StrokeThickness = 1 });
    }

    private void DrawBattle(BattleFile battle, Dictionary<int, BitmapSource?> sprites)
    {
        var placeFill = Frozen(Color.FromArgb(70, 255, 220, 60));
        var placeStroke = Frozen(Color.FromArgb(200, 255, 220, 60));
        foreach (var cell in battle.Placement)
        {
            var box = new Rectangle { Width = TileW, Height = TileH, Fill = placeFill, Stroke = placeStroke, StrokeThickness = 1 };
            Canvas.SetLeft(box, cell.X * TileW);
            Canvas.SetTop(box, cell.Y * TileH);
            Board.Children.Add(box);
        }

        foreach (var unit in battle.Units.OrderBy(u => u.Y))
        {
            Color color = unit.Side switch { 4 => Colors.DeepSkyBlue, 3 => Colors.LimeGreen, _ => Colors.OrangeRed };
            double tileX = unit.X * TileW, tileY = unit.Y * TileH;
            var box = new Rectangle
            {
                Width = TileW, Height = TileH, StrokeThickness = 2,
                Stroke = Frozen(color), Fill = Frozen(Color.FromArgb(50, color.R, color.G, color.B)),
                ToolTip = $"Chr {unit.ChrCode:D4} {ChrName(unit.ChrCode)} — {BattleFile.SideName(unit.Side)}, ({unit.X},{unit.Y}), 방향 {unit.Direction}",
            };
            Canvas.SetLeft(box, tileX);
            Canvas.SetTop(box, tileY);
            Board.Children.Add(box);

            if (sprites.GetValueOrDefault(unit.ChrCode) is not { } sprite) continue;
            var image = new Image { Source = sprite, Width = sprite.PixelWidth, Height = sprite.PixelHeight, IsHitTestVisible = false };
            Canvas.SetLeft(image, tileX + TileW / 2.0 - sprite.PixelWidth / 2.0);
            Canvas.SetTop(image, tileY + TileH - sprite.PixelHeight);
            Canvas.SetZIndex(image, 10);
            Board.Children.Add(image);
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

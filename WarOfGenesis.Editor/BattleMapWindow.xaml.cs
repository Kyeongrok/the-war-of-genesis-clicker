using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarOfGenesis.Assets;
using Line = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WarOfGenesis.Editor;

/// <summary>
/// 게임의 모든 전투(<c>Btl</c>)와 전투 맵(<c>Obt</c>)을 목록으로 보고, 고른 전투를 보여 주는 창 — 원본 맵 위에 인물이
/// 서기 모션으로 움직이고, 옆 판에 인물 명단(능력치)과 이벤트 스크립트(조건·행동)를 풀어 보여 준다.
/// </summary>
/// <remarks>
/// <para>
/// 전투 → <c>Map/NNNN.map</c> 둘째 워드 → 배경 <c>Obt</c>. 인물 그림은 Chr 의 sprite 번호 <c>Obs</c> 의 모션표에서
/// 동작 0(서기) × 방향(Btl 방향 바이트 0 뒤·1 옆(왼쪽)·2 앞·3 반대쪽 옆 = 옆모습을 좌우로 뒤집음)을 30틱/초로 넘긴다
/// (<c>SetAction 0x10072820</c>, duel-dx 와 같은 셈). 컷은 발 자리(칸 가운데) 기준 X·Y 로 놓는다.
/// 필요한 컷만 sprite 번호마다 한 번 백그라운드에서 풀어 둔다(<see cref="ObsSprite.DecodeFrames"/>).
/// </para>
/// <para>
/// 게임 폴더(낱장 + pak)에서 바로 읽는다. 테두리 색: 파랑 플레이어, 초록 동맹 AI, 빨강 적. 노란 칸은 플레이어 배치 칸.
/// 챕터·장소는 <see cref="BattleChapters"/>(battle_list.py 옮김)로 목록을 다 읽은 뒤 채운다.
/// </para>
/// </remarks>
public partial class BattleMapWindow : Window
{
    private const int TileW = ObtMap.CellWidth, TileH = ObtMap.CellHeight;
    private const double TicksPerSecond = 30;

    public sealed record Entry(string Label, BattleFile? Battle, int ObtId, string Title)
    {
        public override string ToString() => Label;
    }

    /// <summary>인물 명단 한 줄(표에 묶음).</summary>
    public sealed record UnitRow(int Index, int No, string Name, string Side, string Level, int LevelOffset, string Cell, string Facing,
                                 string Hp, string Tp, string Atk, string Squad, string Wake, string Chr);

    /// <summary>한 컷 — 그림과 발 자리에서 그림 왼쪽 위까지의 거리.</summary>
    private sealed record Cut(BitmapSource Bitmap, int X, int Y);

    /// <summary>sprite 하나에서 서기 모션에 쓰는 컷들과 모션표. 오른쪽 컷은 처음 쓸 때 뒤집어 둔다(UI 스레드에서만).</summary>
    private sealed class SpriteSet(ObsMotionTable? table, Dictionary<(int Sub, int Slot), Cut> cuts, Cut? first)
    {
        private readonly Dictionary<(int Sub, int Slot), Cut> _mirrored = [];

        public int CutCount => cuts.Count;

        public Cut? CutAt(Facing facing, int tick)
        {
            var clip = table?.Resolve(ObsMotionTable.ActionStand, ObsMotionTable.DirectionOf(facing));
            if (clip?.KeyAt(tick, loop: true) is not { } k || !cuts.TryGetValue((k.SubentryId, k.Slot), out var cut)) return first;
            if (facing != Facing.Right) return cut;

            // 오른쪽 = 옆모습(왼쪽)을 발 자리를 축으로 뒤집는다 — X 도 따라 옮긴다(duel-dx SpriteFrame.Mirrored).
            if (!_mirrored.TryGetValue((k.SubentryId, k.Slot), out var m))
            {
                var bitmap = new TransformedBitmap(cut.Bitmap, new ScaleTransform(-1, 1));
                bitmap.Freeze();
                _mirrored[(k.SubentryId, k.Slot)] = m = new Cut(bitmap, -(cut.X + cut.Bitmap.PixelWidth), cut.Y);
            }
            return m;
        }
    }

    /// <summary>고른 전투를 그릴 자료(백그라운드에서 모음).</summary>
    private sealed record Shown(Entry Entry, ObtMapImage? Map, BitmapSource? Background, string Error,
                                IReadOnlyList<UnitRow> Rows, int[] SpriteIds,
                                IReadOnlyList<BattleEvent>? Events, bool EventsExact, Dictionary<int, string> Talk);

    private sealed record UnitVisual(BattleUnitRecord Unit, int SpriteId, Image Image);

    private readonly GameFiles? _files;
    private readonly GameDatabase? _db;
    private readonly List<Entry> _entries = [];
    private ICollectionView? _view;
    private int _loadVersion;
    private Shown? _shown;
    private Dictionary<int, BattleOrigin>? _origins;

    private readonly Dictionary<int, SpriteSet?> _sprites = [];
    private readonly HashSet<int> _spritesLoading = [];
    private readonly List<UnitVisual> _visuals = [];
    private Rectangle? _highlight;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(1000 / TicksPerSecond) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public BattleMapWindow(string gameRoot, GameDatabase? db)
    {
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateVisuals();
        Closed += (_, _) => _timer.Stop();
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

            // 챕터·장소(battle_list.py) — 목록보다 늦게 채운다.
            Dictionary<int, BattleOrigin>? origins = null;
            try { origins = BattleChapters.Build(files, id => db?.T(id) ?? ""); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { }
            Dispatcher.BeginInvoke(() =>
            {
                _origins = origins;
                if (_shown != null) StatusText.Text = Describe(_shown);
            });
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

    // ── 고른 전투 읽기 ───────────────────────────────────────────────────────

    private void MapList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MapList.SelectedItem is not Entry entry || _files == null) return;
        int version = ++_loadVersion;
        StatusText.Text = $"{entry.Label} 읽는 중...";
        var files = _files;
        System.Threading.Tasks.Task.Run(() =>
        {
            var shown = LoadShown(files, entry);
            Dispatcher.BeginInvoke(() =>
            {
                if (version != _loadVersion) return;
                _shown = shown;
                ShowSidePanel(shown);
                Redraw();
                foreach (int spriteId in shown.SpriteIds.Where(s => s > 0).Distinct()) RequestSprite(spriteId);
            });
        });
    }

    private Shown LoadShown(GameFiles files, Entry entry)
    {
        ObtMapImage? map = null;
        BitmapSource? background = null;
        string error = "";
        var rows = new List<UnitRow>();
        var spriteIds = Array.Empty<int>();
        IReadOnlyList<BattleEvent>? events = null;
        bool exact = false;
        var talk = new Dictionary<int, string>();
        try
        {
            if (entry.ObtId >= 0 && files.Read("Obt", $"{entry.ObtId:D4}.obt") is { } obt)
            {
                map = ObtMap.Parse(obt, $"{entry.ObtId:D4}.obt");
                background = BitmapSource.Create(map.Width, map.Height, 96, 96, PixelFormats.Bgra32, null, map.Bgra, map.Width * 4);
                background.Freeze();
            }
            else error = $"Obt {entry.ObtId:D4} 를 못 찾았습니다.";

            if (entry.Battle is { } battle)
            {
                spriteIds = new int[battle.Units.Count];
                for (int i = 0; i < battle.Units.Count; i++)
                {
                    var u = battle.Units[i];
                    var c = _db?.Character(u.ChrCode);
                    spriteIds[i] = c?.SpriteId ?? 0;
                    rows.Add(RowOf(i, u, c));
                }
                events = BattleEvents.Parse(files.Read("Btl", $"{battle.Id:D4}.btl"), out exact);
                talk = VoiceLines.ReadTlk(files.Read("Tlk", $"{battle.Id:D4}.tlb"));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            error = ex.Message;
        }
        return new Shown(entry, map, background, error, rows, spriteIds, events, exact, talk);
    }

    private UnitRow RowOf(int index, BattleUnitRecord u, CharacterData? c)
    {
        string wake = u.WakeCondition switch
        {
            0 => "처음부터",
            1 => $"같은 편 ≤{u.WakeValue}",
            2 => $"적 ≤{u.WakeValue}",
            3 => $"틱 ≥{u.WakeValue}",
            _ => $"{u.WakeCondition}:{u.WakeValue}",
        };
        if (u.Side == 4) wake = "-";
        else if (u.AiMove != 0) wake += $" · 이동 {u.AiMove}";

        string facing = u.Facing switch { Facing.Up => "뒤(위)", Facing.Down => "앞(아래)", Facing.Left => "왼쪽", _ => "오른쪽" };
        var db = _db;
        string Stat(Func<GameDatabase, CharacterData, int> f) => c != null && db != null ? f(db, c).ToString() : "?";
        return new UnitRow(index, u.No, ChrName(u.ChrCode), BattleFile.SideName(u.Side),
                           c != null ? c.Level.ToString() : "?", u.LevelOffset, $"({u.X},{u.Y})", facing,
                           Stat((d, ch) => d.MaxHp(ch)), Stat((d, ch) => d.MaxTp(ch)), Stat((d, ch) => d.Atk(ch, d.SoulStart)),
                           u.Squad != 0 ? u.Squad.ToString() : "", wake, $"{u.ChrCode:D4}");
    }

    // ── 인물 그림(서기 모션) ─────────────────────────────────────────────────

    /// <summary>sprite 번호의 서기 모션 컷을 아직 안 풀었으면 백그라운드에서 푼다. 한 번 푼 것은 창이 닫힐 때까지 둔다.</summary>
    private void RequestSprite(int spriteId)
    {
        if (_files == null || _sprites.ContainsKey(spriteId) || !_spritesLoading.Add(spriteId)) return;
        UpdateSpriteStatus();
        var files = _files;
        System.Threading.Tasks.Task.Run(() =>
        {
            SpriteSet? set = null;
            try { set = LoadSprite(files, spriteId); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { }
            Dispatcher.BeginInvoke(() =>
            {
                _spritesLoading.Remove(spriteId);
                _sprites[spriteId] = set;
                UpdateSpriteStatus();
                UpdateVisuals();
            });
        });
    }

    private static SpriteSet? LoadSprite(GameFiles files, int spriteId)
    {
        if (files.Read("Obs", $"{spriteId:D4}.obs") is not { } obs) return null;
        var table = ObsMotionTable.Parse(obs);
        var wanted = new HashSet<(int Sub, int Slot)>();
        if (table != null)
            for (int d = 0; d < 3; d++)
                foreach (var k in table.Resolve(ObsMotionTable.ActionStand, d)?.Keys ?? [])
                    wanted.Add((k.SubentryId, k.Slot));

        var cuts = new Dictionary<(int, int), Cut>();
        if (wanted.Count > 0)
            foreach (var (key, f) in ObsSprite.DecodeFrames(obs, wanted))
                cuts[key] = ToCut(f);
        Cut? first = ObsSprite.DecodeFirstFrame(obs) is { } ff ? ToCut(ff) : null;
        return new SpriteSet(table, cuts, first);
    }

    private static Cut ToCut(ObsFrame f)
    {
        var bitmap = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
        bitmap.Freeze();
        return new Cut(bitmap, f.X, f.Y);
    }

    private void UpdateSpriteStatus() =>
        SpriteStatus.Text = _spritesLoading.Count > 0 ? $"인물 그림 푸는 중 {_spritesLoading.Count}개..." : "";

    private void Animate_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        UpdateTimer();
        UpdateVisuals();
    }

    private void UpdateTimer()
    {
        if (AnimateToggle.IsChecked == true && _visuals.Count > 0) _timer.Start();
        else _timer.Stop();
    }

    /// <summary>지금 틱의 컷으로 인물 그림을 바꾼다. 재생을 끄면 0틱(서기 첫 컷)에 멈춘다.</summary>
    private void UpdateVisuals()
    {
        int tick = AnimateToggle.IsChecked == true ? (int)(_clock.Elapsed.TotalSeconds * TicksPerSecond) : 0;
        foreach (var v in _visuals)
        {
            if (_sprites.GetValueOrDefault(v.SpriteId)?.CutAt(v.Unit.Facing, tick) is not { } cut)
            {
                v.Image.Source = null;
                continue;
            }
            if (!ReferenceEquals(v.Image.Source, cut.Bitmap))
            {
                v.Image.Source = cut.Bitmap;
                v.Image.Width = cut.Bitmap.PixelWidth;
                v.Image.Height = cut.Bitmap.PixelHeight;
            }
            Canvas.SetLeft(v.Image, v.Unit.X * TileW + TileW / 2 + cut.X);
            Canvas.SetTop(v.Image, v.Unit.Y * TileH + TileH / 2 + cut.Y);
        }
    }

    // ── 그리기 ───────────────────────────────────────────────────────────────

    private void View_Changed(object sender, RoutedEventArgs e) => Redraw();

    private void Redraw()
    {
        if (Board == null) return;   // XAML 을 읽는 중(체크 상자 초기값이 Checked 를 부름)
        Board.Children.Clear();
        _visuals.Clear();
        _highlight = null;
        if (_shown is not { } shown) { UpdateTimer(); return; }
        var map = shown.Map;

        int cols = map?.Cols ?? 0, rows = map?.Rows ?? 0;
        Board.Width = Math.Max(cols * TileW, map?.Width ?? 0);
        Board.Height = rows * TileH;

        if (shown.Background is { } background)
        {
            var image = new Image { Source = background, Width = background.PixelWidth, Height = background.PixelHeight };
            Canvas.SetTop(image, map!.OriginY);
            Board.Children.Add(image);
        }
        if (GridToggle.IsChecked == true) DrawGridLines(cols, rows);
        if (UnitsToggle.IsChecked == true && shown.Entry.Battle is { } battle) DrawBattle(battle, shown.SpriteIds);

        StatusText.Text = Describe(shown);
        UpdateVisuals();
        UpdateHighlight(false);
        UpdateTimer();
    }

    private string Describe(Shown shown)
    {
        var (entry, map, error) = (shown.Entry, shown.Map, shown.Error);
        var lines = new List<string>();
        string size = map != null ? $"{map.Cols}×{map.Rows}칸" : "";
        if (entry.Battle is { } b)
        {
            string T(ushort id) => _db?.T(id) ?? "";
            lines.Add($"Btl {b.Id:D4}  「{entry.Title}」   Map {b.MapId:D4} → Obt {entry.ObtId:D4} {size}   BGM {b.Bgm}");
            lines.Add($"승리: {T(b.WinId)}   패배: {T(b.LoseId)}");
            if (_origins == null) lines.Add("챕터: (찾는 중...)");
            else if (_origins.GetValueOrDefault(b.Id) is { } origin)
            {
                string chapters = string.Join(", ", origin.Chapters.Take(3).Select(c => $"Chp {c.Chapter:D4}「{c.Title}」"));
                if (origin.Chapters.Count > 3) chapters += $" 외 {origin.Chapters.Count - 3}";
                lines.Add($"챕터: {(chapters.Length > 0 ? chapters : "(못 찾음)")}   불리는 곳: {string.Join(" · ", origin.Callers.DefaultIfEmpty("-"))}");
            }
            else lines.Add("챕터: (못 찾음 — 대전·시험용·옛 형식으로 보임)");
            var groups = b.Units.GroupBy(u => u.Side).OrderByDescending(g => g.Key)
                .Select(g => $"{BattleFile.SideName(g.Key)} {g.Count()}");
            string extra = (b.Placement.Count > 0 ? $" · 배치 칸 {b.Placement.Count}개" : "") + (b.Objects.Count > 0 ? $" · 오브젝트 {b.Objects.Count}개" : "");
            lines.Add(string.Join(" · ", groups) + extra + $" · 이벤트 {shown.Events?.Count.ToString() ?? "?"}개");
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
        var brush = Frozen(Color.FromArgb(64, 255, 255, 255));
        for (int c = 0; c <= cols; c++)
            Board.Children.Add(new Line { X1 = c * TileW, X2 = c * TileW, Y1 = 0, Y2 = rows * TileH, Stroke = brush, StrokeThickness = 1 });
        for (int r = 0; r <= rows; r++)
            Board.Children.Add(new Line { X1 = 0, X2 = cols * TileW, Y1 = r * TileH, Y2 = r * TileH, Stroke = brush, StrokeThickness = 1 });
    }

    private void DrawBattle(BattleFile battle, int[] spriteIds)
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

        for (int i = 0; i < battle.Units.Count; i++)
        {
            var unit = battle.Units[i];
            Color color = unit.Side switch { 4 => Colors.DeepSkyBlue, 3 => Colors.LimeGreen, _ => Colors.OrangeRed };
            int index = i;
            var box = new Rectangle
            {
                Width = TileW, Height = TileH, StrokeThickness = 2, Cursor = Cursors.Hand,
                Stroke = Frozen(color), Fill = Frozen(Color.FromArgb(50, color.R, color.G, color.B)),
                ToolTip = $"#{unit.No} Chr {unit.ChrCode:D4} {ChrName(unit.ChrCode)} — {BattleFile.SideName(unit.Side)}, ({unit.X},{unit.Y}), 방향 {unit.Direction}",
            };
            box.MouseLeftButtonDown += (_, _) => SelectUnit(index);
            Canvas.SetLeft(box, unit.X * TileW);
            Canvas.SetTop(box, unit.Y * TileH);
            Board.Children.Add(box);

            // 아래 줄 인물이 위 줄 인물을 가리도록 칸 y 순서로 쌓는다.
            var image = new Image { IsHitTestVisible = false };
            Canvas.SetZIndex(image, 10 + unit.Y);
            Board.Children.Add(image);
            _visuals.Add(new UnitVisual(unit, i < spriteIds.Length ? spriteIds[i] : 0, image));
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // ── 옆 판: 인물 명단 ─────────────────────────────────────────────────────

    private void ShowSidePanel(Shown shown)
    {
        UnitGrid.ItemsSource = shown.Rows;
        ShowEvents(shown);
    }

    private void SelectUnit(int index)
    {
        if (_shown == null || index < 0 || index >= _shown.Rows.Count) return;
        UnitsTab.IsSelected = true;
        UnitGrid.SelectedItem = _shown.Rows[index];
        UnitGrid.ScrollIntoView(UnitGrid.SelectedItem);
    }

    private void UnitGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHighlight(true);

    /// <summary>고른 인물 칸에 굵은 노란 테를 두르고, <paramref name="scroll"/> 이면 판을 그 칸으로 옮긴다.</summary>
    private void UpdateHighlight(bool scroll)
    {
        if (_highlight != null) Board.Children.Remove(_highlight);
        _highlight = null;
        if (UnitGrid.SelectedItem is not UnitRow row || _shown?.Entry.Battle is not { } battle || row.Index >= battle.Units.Count
            || UnitsToggle.IsChecked != true) return;

        var unit = battle.Units[row.Index];
        _highlight = new Rectangle
        {
            Width = TileW + 8, Height = TileH + 8, StrokeThickness = 3, IsHitTestVisible = false,
            Stroke = Brushes.Yellow, Fill = Frozen(Color.FromArgb(60, 255, 255, 0)),
        };
        Canvas.SetLeft(_highlight, unit.X * TileW - 4);
        Canvas.SetTop(_highlight, unit.Y * TileH - 4);
        Canvas.SetZIndex(_highlight, 5);
        Board.Children.Add(_highlight);
        if (!scroll) return;
        var target = _highlight;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ReferenceEquals(target, _highlight)) target.BringIntoView(new Rect(-240, -200, TileW + 480, TileH + 400));
        });
    }

    // ── 옆 판: 이벤트 스크립트 ───────────────────────────────────────────────

    private static readonly Brush ConditionBrush = Frozen(Color.FromRgb(90, 90, 90));
    private static readonly Brush TalkBrush = Frozen(Color.FromRgb(20, 60, 140));
    private static readonly Brush FlowBrush = Frozen(Color.FromRgb(170, 30, 30));
    private static readonly Brush SoundBrush = Frozen(Color.FromRgb(20, 110, 60));

    private void ShowEvents(Shown shown)
    {
        EventList.Items.Clear();
        if (shown.Entry.Battle is not { } battle)
        {
            EventStatus.Text = "전투가 아닌 맵입니다.";
            return;
        }
        if (shown.Events is not { } events)
        {
            EventStatus.Text = "이벤트 절을 못 읽었습니다(옛 형식·빈 파일).";
            return;
        }
        int actions = events.Sum(e => e.Actions.Count);
        EventStatus.Text = $"이벤트 {events.Count}개 · 행동 {actions}개{(shown.EventsExact ? "" : " · ※ 파일 끝과 안 맞음")}. " +
                           "조건 코드 뜻은 코드로 안 풀림(원 번호, 끝의 ? 는 자료 꼴로 본 짐작). 행동 이름: 10 다음 전투, 6 필드로, 600 대사 상자, 601 말풍선, 500·501 소리, 512 배경음악. " +
                           "대사 글 = Tlk/NNNN.tlb.";

        foreach (var ev in events)
        {
            string max = ev.MaxFire <= 0 ? $"제한 없음({ev.MaxFire})" : $"{ev.MaxFire}번";
            EventList.Items.Add(Row($"이벤트 {ev.Index}  · 최대 발동 {max}{(ev.Word2 != 0 ? $" · 둘째 워드 {ev.Word2}" : "")}",
                                    Brushes.Black, bold: true, top: 6));
            if (ev.Conditions.Count == 0) EventList.Items.Add(Row("  조건 없음", ConditionBrush));
            foreach (var cond in ev.Conditions)
            {
                string guess = ConditionGuess(cond);
                EventList.Items.Add(Row($"  조건 {cond.Code} {cond.ArgsText()}{(guess.Length > 0 ? "  — " + guess : "")}", ConditionBrush));
            }
            foreach (var act in ev.Actions)
            {
                var (text, brush) = DescribeAction(act, battle, shown.Talk);
                EventList.Items.Add(Row("  → " + text, brush));
            }
        }
    }

    /// <summary>
    /// 조건 코드 뜻 짐작(코드로 확인 안 함 — 물음표). 0045 이벤트 꼴에서: 401(0) 뒤에 "버그들을 다 잡았으면" 대사와 다음 전투,
    /// 401(4)·401(3) 뒤에 행동 11, 3(10·25·30…) 은 튜토리얼 대사가 차례로 나오는 값, 1 은 첫 대사.
    /// </summary>
    private static string ConditionGuess(ScriptCommand cond) => cond.Code switch
    {
        1 => "전투 시작?",
        3 => $"시간 틱 ≥ {cond.Args[0]}?",
        401 => $"편 {cond.Args[0]} 전멸?",
        _ => "",
    };

    private static TextBlock Row(string text, Brush brush, bool bold = false, double top = 0) => new()
    {
        Text = text, Foreground = brush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0),
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
    };

    private (string Text, Brush Brush) DescribeAction(ScriptCommand act, BattleFile battle, Dictionary<int, string> talk)
    {
        var w = act.Args;
        string name = BattleEvents.ActionName(act.Code);
        string head = name.Length > 0 ? $"{act.Code} {name}" : $"행동 {act.Code}";
        string Line(int line) => talk.TryGetValue(line, out var s) ? $"「{s.Replace("$n", " ")}」" : $"(줄 {line} 없음)";
        switch (act.Code)
        {
            case 10:
                var next = _entries.FirstOrDefault(e => e.Battle?.Id == w[0]);
                return ($"{head}: Btl {w[0]:D4}「{next?.Title ?? "?"}」", FlowBrush);
            case 6:
                return ($"{head}: Fld {w[0]:D4}", FlowBrush);
            case 600:
                string voice = w[3] > 0 ? $" · 음성 {w[3]:D4}" : "";
                return ($"{head}: {Speaker(w[0], battle)} {Line(w[2])}{voice} · 얼굴 {w[4]}  {act.ArgsText()}", TalkBrush);
            case 601:
                // 분석-사운드 표는 말하는 이를 비워 뒀지만, 자료의 인자0 이 600 과 같은 꼴(221 죠안, 10016 = 이 전투 #16)이라 같이 풀어 보인다.
                return ($"{head}: {Speaker(w[0], battle)} {Line(w[2])}  {act.ArgsText()}", TalkBrush);
            case 500 or 501:
                return ($"{head}: {w[0]:D4}  {act.ArgsText()}", SoundBrush);
            case 512:
                return ($"{head}: BGM {w[0]}  {act.ArgsText()}", SoundBrush);
            case 11:
                return ($"행동 11 {act.ArgsText()}  — 전투 끝(패배/결과)?", FlowBrush);
            case 1:
                return ("행동 1  — 대사 기다림?", ConditionBrush);
            default:
                return ($"{head} {act.ArgsText()}", Brushes.Black);
        }
    }

    /// <summary>말하는 이: 10000 미만 = Chr, 10000~19999 = 이 전투 인물 레코드 번호(−10000), 20000 이상 = 실행 중 결정(분석-사운드).</summary>
    private string Speaker(int s, BattleFile battle)
    {
        if (s is > 0 and < 10000) return ChrName(s);
        if (s is >= 10000 and < 20000)
            return battle.Units.FirstOrDefault(u => u.No == s - 10000) is { } u ? $"{ChrName(u.ChrCode)}(#{u.No})" : $"유닛 #{s - 10000}";
        return s >= 20000 ? "(실행 중 결정)" : "(말하는 이 없음)";
    }
}

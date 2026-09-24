using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 필드 파일 <c>Fld\NNNN.fld</c> 편집 창 — 필드 목록, 배경 위 미리보기(물체·인물·첫 화면 틀), 머리·물체·인물·스크립트 고치기.
/// </summary>
/// <remarks>
/// <para>
/// 읽기는 <see cref="FieldFile"/> 가 하고, 고치기는 <see cref="FieldFileWriter"/> 로 <b>제자리</b>에 쓴다 — 레코드 크기가 정해져 있어
/// 뒤 자료가 안 움직인다. 물체·인물·명령을 더하거나 빼는 것은 스크립트가 열쇠(<c>10000+열쇠</c>)·차례로 가리키고 있어 하지 않는다.
/// </para>
/// <para>
/// 필드는 게임 폴더가 아니라 저장소 <c>assets/data/Fld</c> 에서 읽는다(duel-dx 가 읽는 그 파일). 저장하면 처음 한 번만 원본을
/// <c>.bak</c> 으로 남긴다(챕터 편집 창과 같다). 대사 글은 같은 폴더 곁의 <c>Tlk\NNNN.tlf</c> 에서 꺼내 보여 준다.
/// </para>
/// <para>
/// 그림(<c>Obs</c>)은 게임 폴더가 열려 있으면 거기서, 아니면 저장소 assets 아래의 <c>.obs</c> 에서 찾는다. 물체는 제 모션(갈래 칸)의 첫 컷,
/// 인물은 Chr 그림의 서기(앞) 첫 컷을 쓴다. 못 찾으면 네모·동그라미 표시만 그린다.
/// </para>
/// </remarks>
public partial class FieldWindow : Window
{
    /// <summary>필드 목록 한 줄.</summary>
    public sealed class FieldRow
    {
        public int Id { get; init; }
        public int Background { get; set; }
        public int Bgm { get; set; }
        public int Objects { get; init; }
        public int People { get; init; }
        public int Events { get; init; }
        public string UsedBy { get; init; } = "";
    }

    /// <summary>물체 표 한 줄 — 고치면 <see cref="FieldFileWriter.WriteObject"/> 로 간다.</summary>
    public sealed class ObjectRow
    {
        public int Index { get; init; }
        public int Key { get; set; }
        public int Picture { get; set; }
        public int Kind { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Layer { get; set; }
        public int Script => 10000 + Key;
        public FieldObject ToRecord() => new(Key, Picture, X, Y, Kind, Layer);
    }

    /// <summary>인물 표 한 줄.</summary>
    public sealed class PersonRow
    {
        public int Index { get; init; }
        public int Key { get; set; }
        public int ChrCode { get; set; }
        public string Name { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Layer { get; set; }
        public FieldPerson ToRecord() => new(Key, ChrCode, X, Y, Layer);
    }

    /// <summary>이벤트 표 한 줄.</summary>
    public sealed class EventRow
    {
        public int Index { get; init; }
        public int MaxFire { get; set; }
        public int Conditions { get; init; }
        public int Actions { get; init; }
        public string Summary { get; init; } = "";
    }

    /// <summary>명령 표 한 줄 — 조건이든 행동이든 18바이트(코드 + 인자 8개) 한 레코드.</summary>
    public sealed class CommandRow
    {
        public string Part { get; init; } = "";
        public int Offset { get; init; }
        public bool IsCondition { get; init; }
        public int Code { get; set; }
        public short A0 { get; set; }
        public short A1 { get; set; }
        public short A2 { get; set; }
        public short A3 { get; set; }
        public short A4 { get; set; }
        public short A5 { get; set; }
        public short A6 { get; set; }
        public short A7 { get; set; }
        public string Name { get; set; } = "";
        public string Note { get; set; } = "";
        public ScriptCommand ToCommand() => new(Code, [A0, A1, A2, A3, A4, A5, A6, A7]);
    }

    private sealed record Cut(BitmapSource Bitmap, int X, int Y);

    private readonly string _gameRoot;
    private readonly GameDatabase? _db;
    private readonly GameFiles? _gameFiles;
    private string _fldFolder = "";
    private readonly List<FieldRow> _rows = [];
    private ICollectionView? _view;

    /// <summary>필드 번호 → 파일 바이트(고친 것이 여기 먼저 들어가고 저장하면 파일로 간다).</summary>
    private readonly Dictionary<int, byte[]> _bytes = [];
    private readonly HashSet<int> _dirty = [];
    private FieldFile? _current;
    private TalkTable? _talk;

    /// <summary>저장소 assets 아래 <c>.obs</c> — 번호 → 경로(게임 폴더가 없을 때 쓴다).</summary>
    private Dictionary<int, string>? _assetObs;
    /// <summary>(Obs, 모션) → 컷. 못 푼 것은 null 로 기억한다. -1 모션은 인물(서기 앞).</summary>
    private readonly Dictionary<(int Obs, int Motion), Cut?> _cuts = [];
    private readonly HashSet<(int Obs, int Motion)> _cutsLoading = [];

    /// <summary>끌고 있는 것 — 물체면 true, 인물이면 false · 표의 차례 · 누른 자리와 처음 좌표.</summary>
    private (bool IsObject, int Index, Point Start, int X, int Y, FrameworkElement Element, double Left, double Top)? _drag;

    private bool _filling;

    public FieldWindow(string gameRoot, GameDatabase? db)
    {
        InitializeComponent();
        _gameRoot = gameRoot;
        _db = db;
        _gameFiles = gameRoot.Length > 0 && Directory.Exists(gameRoot) ? GameFiles.FromGameRoot(gameRoot) : null;
        Loaded += (_, _) => StartLoading();
    }

    // ── 읽기 ────────────────────────────────────────────────────────────────

    private void StartLoading()
    {
        string data;
        try { data = AssetsFolder.Find("data"); }
        catch (DirectoryNotFoundException) { StatusText.Text = "저장소 assets/data 를 못 찾았습니다."; return; }
        _fldFolder = System.IO.Path.Combine(data, "Fld");
        if (!Directory.Exists(_fldFolder)) { StatusText.Text = $"{_fldFolder} 가 없습니다."; return; }
        StatusText.Text = "필드를 읽는 중...";
        Task.Run(() =>
        {
            var fields = new List<FieldFile>();
            var bytes = new Dictionary<int, byte[]>();
            foreach (string path in Directory.EnumerateFiles(_fldFolder, "*.fld"))
            {
                if (!int.TryParse(System.IO.Path.GetFileNameWithoutExtension(path), out int id)) continue;
                byte[] b = File.ReadAllBytes(path);
                if (FieldFile.Parse(id, b) is not { } field) continue;
                fields.Add(field);
                bytes[id] = b;
            }
            var usedBy = FindUsers(data, fields);
            Dispatcher.BeginInvoke(() => Show(fields, bytes, usedBy));
        });
    }

    /// <summary>
    /// 그 필드를 부르는 곳 — 챕터 장소(값 <c>10000+필드</c>), 다른 필드의 행동 6, 전투가 끝나고 가는 필드(Btl 행동 6).
    /// </summary>
    private static Dictionary<int, List<string>> FindUsers(string data, List<FieldFile> fields)
    {
        var users = new Dictionary<int, List<string>>();
        void Add(int field, string who) { if (!users.TryGetValue(field, out var l)) users[field] = l = []; if (!l.Contains(who)) l.Add(who); }
        try
        {
            string chp = System.IO.Path.Combine(AssetsFolder.Find("moses"), "chp");
            if (Directory.Exists(chp))
                foreach (string path in Directory.EnumerateFiles(chp, "*.chp"))
                    if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(path), out int id)
                        && ChapterFile.Parse(id, File.ReadAllBytes(path)) is { } chapter)
                        foreach (var place in chapter.Places.Where(p => p.Value is >= 10000 and < 20000))
                            Add(place.Value - 10000, $"Chp {id:D4}");
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or IOException) { }
        foreach (var f in fields)
            foreach (var a in f.Events.SelectMany(e => e.Actions).Where(a => a.Code == 6 && a.Args.Length > 0))
                Add(a.Args[0], $"Fld {f.Id:D4}");
        string btl = System.IO.Path.Combine(data, "Btl");
        if (Directory.Exists(btl))
            foreach (string path in Directory.EnumerateFiles(btl, "*.btl"))
                if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(path), out int id)
                    && BattleEvents.Parse(File.ReadAllBytes(path)) is { } events)
                    foreach (var a in events.SelectMany(e => e.Actions).Where(a => a.Code == 6 && a.Args.Length > 0))
                        Add(a.Args[0], $"Btl {id:D4}");
        return users;
    }

    private void Show(List<FieldFile> fields, Dictionary<int, byte[]> bytes, Dictionary<int, List<string>> usedBy)
    {
        foreach (var (id, b) in bytes) _bytes[id] = b;
        _rows.Clear();
        foreach (var f in fields.OrderBy(f => f.Id))
            _rows.Add(new FieldRow
            {
                Id = f.Id, Background = f.Background, Bgm = f.Bgm, Objects = f.Objects.Count, People = f.People.Count,
                Events = f.Events.Count, UsedBy = string.Join(", ", usedBy.GetValueOrDefault(f.Id) ?? []),
            });
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = o => o is FieldRow r && Matches(r);
        FieldGrid.ItemsSource = _view;
        UpdateStatus();
        if (_rows.Count > 0) FieldGrid.SelectedIndex = 0;
    }

    private bool Matches(FieldRow r)
    {
        string q = FilterBox.Text.Trim();
        return q.Length == 0 || r.Id.ToString("D4").Contains(q) || r.UsedBy.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int shown = _view?.Cast<object>().Count() ?? 0;
        StatusText.Text = $"필드 {shown}/{_rows.Count}개" + (_dirty.Count > 0 ? $" · 저장 안 한 필드 {_dirty.Count}개" : "");
    }

    /// <summary>그 필드를 골라 연다 — 다른 창(챕터 등)에서 부를 수 있다.</summary>
    public void SelectField(int id)
    {
        FilterBox.Text = "";
        if (_rows.FirstOrDefault(r => r.Id == id) is { } row) { FieldGrid.SelectedItem = row; FieldGrid.ScrollIntoView(row); }
    }

    private void FieldGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FieldGrid.SelectedItem is not FieldRow row || !_bytes.TryGetValue(row.Id, out var b)) return;
        _current = FieldFile.Parse(row.Id, b);
        _talk = TalkTable.Parse(ReadTalk(row.Id));
        EditTabs.IsEnabled = _current != null;
        FillEditors();
        DrawPreview();
    }

    private byte[]? ReadTalk(int id)
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_fldFolder)!, "Tlk", $"{id:D4}.tlf");
        return File.Exists(path) ? File.ReadAllBytes(path) : _gameFiles?.Read("Tlk", $"{id:D4}.tlf");
    }

    // ── 표 채우기 ──────────────────────────────────────────────────────────

    private void FillEditors()
    {
        if (_current is not { } f) return;
        _filling = true;
        HeadBackgroundBox.Text = f.Background.ToString();
        HeadCameraXBox.Text = f.CameraX.ToString();
        HeadCameraYBox.Text = f.CameraY.ToString();
        HeadBgmBox.Text = f.Bgm.ToString();
        ObjectGrid.ItemsSource = f.Objects.Select((o, i) => new ObjectRow
        {
            Index = i, Key = o.Key, Picture = o.Picture, Kind = o.Kind, X = o.X, Y = o.Y, Layer = o.Layer,
        }).ToList();
        PersonGrid.ItemsSource = f.People.Select((p, i) => new PersonRow
        {
            Index = i, Key = p.Key, ChrCode = p.ChrCode, Name = ChrName(p.ChrCode), X = p.X, Y = p.Y, Layer = p.Layer,
        }).ToList();
        int keep = EventGrid.SelectedIndex;
        EventGrid.ItemsSource = f.Events.Select(e => new EventRow
        {
            Index = e.Index, MaxFire = e.MaxFire, Conditions = e.Conditions.Count, Actions = e.Actions.Count, Summary = Summarize(e),
        }).ToList();
        EventGrid.SelectedIndex = keep >= 0 && keep < f.Events.Count ? keep : (f.Events.Count > 0 ? 0 : -1);
        FillCommands();
        PreviewHeader.Text = $"Fld {f.Id:D4} — 배경 Bgr {f.Background:D4} · BGM {f.Bgm}" + (f.Exact ? "" : $" · 끝에 남는 바이트 {f.TailBytes}");
        _filling = false;
    }

    private void EventGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filling) FillCommands();
    }

    private void FillCommands()
    {
        if (_current is not { } f || EventGrid.SelectedItem is not EventRow row || row.Index >= f.Events.Count) { CommandGrid.ItemsSource = null; return; }
        var ev = f.Events[row.Index];
        var list = new List<CommandRow>();
        for (int i = 0; i < ev.Conditions.Count; i++) list.Add(ToRow(ev.Conditions[i], ev.ConditionOffsets[i], true, i));
        for (int i = 0; i < ev.Actions.Count; i++) list.Add(ToRow(ev.Actions[i], ev.ActionOffsets[i], false, i));
        CommandGrid.ItemsSource = list;
    }

    private CommandRow ToRow(ScriptCommand c, int offset, bool condition, int i)
    {
        short A(int k) => k < c.Args.Length ? c.Args[k] : (short)0;
        var row = new CommandRow
        {
            Part = condition ? $"조건 {i}" : $"행동 {i}", Offset = offset, IsCondition = condition, Code = c.Code,
            A0 = A(0), A1 = A(1), A2 = A(2), A3 = A(3), A4 = A(4), A5 = A(5), A6 = A(6), A7 = A(7),
        };
        Describe(row);
        return row;
    }

    /// <summary>뜻과 글·대상 칸을 채운다 — 코드나 인자를 고친 뒤에도 다시 부른다.</summary>
    private void Describe(CommandRow row)
    {
        var c = row.ToCommand();
        row.Name = row.IsCondition ? FieldScript.ConditionName(c.Code) : FieldScript.ActionName(c.Code);
        row.Note = row.IsCondition ? "" : NoteOf(c);
    }

    private string NoteOf(ScriptCommand c)
    {
        short A(int k) => k < c.Args.Length ? c.Args[k] : (short)0;
        if (FieldScript.TalkArg(c.Code) is >= 0 and var t)
        {
            string text = _talk?[A(t)] ?? "";
            string who = c.Code is 600 or 601 or 602 ? PersonName(A(0)) : "";
            return (who.Length > 0 ? who + ": " : "") + (text.Length > 0 ? $"「{text.Replace("\n", " ")}」" : $"글 {A(t)} 없음");
        }
        return c.Code switch
        {
            0 => $"이벤트 {A(0)}",
            6 => File.Exists(System.IO.Path.Combine(_fldFolder, $"{A(0):D4}.fld")) ? $"Fld {A(0):D4}" : $"Fld {A(0):D4} 가 assets 에 없다",
            10 => $"Btl {A(0):D4}",
            >= 202 and <= 213 or 402 => PersonName(A(0)),
            >= 300 and <= 307 when A(0) >= 10000 => $"물체 {A(0) - 10000}",
            302 => $"새 그림 Obs {A(0)}",
            500 => PersonName(A(1)),
            501 => PersonName(A(2)),
            701 or 702 or 704 or 805 => ChrName(A(0)),
            801 or 802 => ChrName(A(1)),
            804 => ChrName(A(0)),
            _ => "",
        };
    }

    /// <summary>인물 열쇠 → 그 인물 이름(필드의 인물 목록에서).</summary>
    private string PersonName(int key) =>
        _current?.People.FirstOrDefault(p => p.Key == key) is { } p ? ChrName(p.ChrCode) : key == 0 ? "" : $"인물 {key}";

    private string ChrName(int chr) =>
        _db?.Character(chr) is { } c && _db.T(c.NameId) is { Length: > 0 } n ? n : $"Chr {chr:D4}";

    /// <summary>이벤트 요약 — 첫 대사, 없으면 행동 이름 몇 개.</summary>
    private string Summarize(FieldEvent e)
    {
        if (e.Index == 0) return "살려 둘 이벤트 목록: " + string.Join(", ", e.Actions.Select(a => a.Args.Length > 0 ? a.Args[0] : 0));
        foreach (var a in e.Actions)
            if (FieldScript.TalkArg(a.Code) is >= 0 and var t && t < a.Args.Length && _talk?[a.Args[t]] is { Length: > 0 } text)
                return $"「{text.Replace("\n", " ")}」";
        return string.Join(" · ", e.Actions.Select(a => FieldScript.ActionName(a.Code)).Where(n => n.Length > 0)
                                            .Select(n => n.Split(" [")[0]).Distinct().Take(4));
    }

    // ── 고치기 ──────────────────────────────────────────────────────────────

    private void HeadApply_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { } f) return;
        if (!short.TryParse(HeadBackgroundBox.Text, out short bg) || !short.TryParse(HeadCameraXBox.Text, out short cx)
            || !short.TryParse(HeadCameraYBox.Text, out short cy) || !short.TryParse(HeadBgmBox.Text, out short bgm))
        {
            StatusText.Text = "머리 칸에는 −32768~32767 의 수만 넣을 수 있습니다.";
            return;
        }
        FieldFileWriter.WriteHeader(_bytes[f.Id], bg, cx, cy, bgm);
        if (_rows.FirstOrDefault(r => r.Id == f.Id) is { } row) { row.Background = bg; row.Bgm = bgm; _view?.Refresh(); }
        Reparse(f.Id);
    }

    /// <summary>표 칸을 다 고치면(바인딩이 값을 넣은 뒤) 그 표 전체를 바이트에 옮겨 쓴다.</summary>
    private void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || sender is not DataGrid grid) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => ApplyGrid(grid));
    }

    private void ApplyGrid(DataGrid grid)
    {
        if (_current is not { } f) return;
        byte[] b = _bytes[f.Id];
        if (grid == ObjectGrid && grid.ItemsSource is List<ObjectRow> objects)
            foreach (var r in objects) FieldFileWriter.WriteObject(b, r.Index, r.ToRecord());
        else if (grid == PersonGrid && grid.ItemsSource is List<PersonRow> people)
            foreach (var r in people) FieldFileWriter.WritePerson(b, f, r.Index, r.ToRecord());
        else if (grid == EventGrid && grid.ItemsSource is List<EventRow> events)
            foreach (var r in events) FieldFileWriter.WriteMaxFire(b, f.Events[r.Index], r.MaxFire);
        else if (grid == CommandGrid && grid.ItemsSource is List<CommandRow> commands)
            foreach (var r in commands) FieldFileWriter.WriteCommand(b, r.Offset, r.ToCommand());
        else return;
        Reparse(f.Id);
    }

    /// <summary>바이트가 바뀌었다 — 다시 읽어 표·미리보기를 새로 그리고 저장할 것으로 표시한다.</summary>
    private void Reparse(int id)
    {
        if (FieldFile.Parse(id, _bytes[id]) is not { } f) { StatusText.Text = "고친 파일을 다시 읽지 못했습니다 — 되돌리기를 누르세요."; return; }
        _current = f;
        _dirty.Add(id);
        FillEditors();
        DrawPreview();
        UpdateStatus();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveDirty();

    private bool SaveDirty()
    {
        if (_dirty.Count == 0) { StatusText.Text = "고친 필드가 없습니다."; return true; }
        try
        {
            foreach (int id in _dirty.Order())
            {
                string path = System.IO.Path.Combine(_fldFolder, $"{id:D4}.fld");
                string backup = path + ".bak";
                if (!File.Exists(backup) && File.Exists(path)) File.Copy(path, backup);
                File.WriteAllBytes(path, _bytes[id]);
            }
            StatusText.Text = $"필드 {string.Join(", ", _dirty.Order().Select(id => id.ToString("D4")))} 을(를) 저장했습니다 — 게임을 다시 켜면 반영됩니다(처음 원본은 .bak).";
            _dirty.Clear();
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
        if (_current is not { } f) return;
        _bytes[f.Id] = File.ReadAllBytes(System.IO.Path.Combine(_fldFolder, $"{f.Id:D4}.fld"));
        _current = FieldFile.Parse(f.Id, _bytes[f.Id]);
        _dirty.Remove(f.Id);
        if (_current is { } back && _rows.FirstOrDefault(r => r.Id == f.Id) is { } row) { row.Background = back.Background; row.Bgm = back.Bgm; _view?.Refresh(); }
        FillEditors();
        DrawPreview();
        UpdateStatus();
        StatusText.Text = $"Fld {f.Id:D4} 을(를) 파일에 있는 대로 되돌렸습니다.";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_dirty.Count > 0)
        {
            var answer = MessageBox.Show(this, $"저장 안 한 필드가 {_dirty.Count}개 있습니다. 저장할까요?", "필드 편집",
                                         MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !SaveDirty())) e.Cancel = true;
        }
        base.OnClosing(e);
    }

    // ── 미리보기 ────────────────────────────────────────────────────────────

    private void PreviewOption_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) DrawPreview(); }

    private void PartGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_filling) DrawPreview(); }

    private void DrawPreview()
    {
        PreviewCanvas.Children.Clear();
        if (_current is not { } f) return;
        var background = LoadBackground(f.Background);
        PreviewCanvas.Width = Math.Max(640, background?.PixelWidth ?? 0);
        PreviewCanvas.Height = Math.Max(480, background?.PixelHeight ?? 0);
        if (background != null)
            PreviewCanvas.Children.Add(new Image { Source = background, Width = background.PixelWidth, Height = background.PixelHeight });
        PreviewStatus.Text = background == null ? $"배경 Bgr {f.Background:D4} 을(를) 못 찾았습니다" : "";

        var camera = new Rectangle { Width = 640, Height = 480, Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = [6, 4], IsHitTestVisible = false };
        Canvas.SetLeft(camera, Math.Max(0, f.CameraX));
        Canvas.SetTop(camera, Math.Max(0, f.CameraY));

        bool sprites = SpritesToggle.IsChecked == true, labels = LabelsToggle.IsChecked == true;
        // 층이 낮은 것부터 — 게임도 층 차례로 그린다. 인물 층 −1(붙박이)은 맨 위.
        var parts = f.Objects.Select((o, i) => (IsObject: true, Index: i, o.X, o.Y, Layer: o.Layer, Obs: o.Picture, Motion: o.Kind, Label: $"물체 {o.Key}"))
            .Concat(f.People.Select((p, i) => (IsObject: false, Index: i, p.X, p.Y, Layer: p.Layer < 0 ? 8 : p.Layer,
                                               Obs: (int)(_db?.Character(p.ChrCode)?.SpriteId ?? 0), Motion: -1, Label: ChrName(p.ChrCode))))
            .OrderBy(p => p.Layer);
        foreach (var p in parts)
        {
            bool selected = p.IsObject ? ObjectGrid.SelectedIndex == p.Index && EditTabs.SelectedIndex == 1
                                       : PersonGrid.SelectedIndex == p.Index && EditTabs.SelectedIndex == 2;
            FrameworkElement element;
            double left, top;
            if (sprites && p.Obs > 0 && CutOf(p.Obs, p.Motion) is { } cut)
            {
                element = new Image { Source = cut.Bitmap, Width = cut.Bitmap.PixelWidth, Height = cut.Bitmap.PixelHeight };
                (left, top) = (p.X + cut.X, p.Y + cut.Y);
            }
            else
            {
                element = p.IsObject
                    ? new Rectangle { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(160, 255, 140, 0)), Stroke = Brushes.Orange }
                    : new Ellipse { Width = 16, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(160, 60, 140, 255)), Stroke = Brushes.DeepSkyBlue };
                (left, top) = (p.X - 8, p.Y - 8);
            }
            element.Cursor = Cursors.SizeAll;
            element.ToolTip = $"{p.Label} ({p.X}, {p.Y}) 층 {(p.Layer == 8 ? "붙박이" : p.Layer)}" + (p.Obs > 0 ? $" · Obs {p.Obs}" : "");
            var drag = p;
            element.MouseLeftButtonDown += (_, e) => BeginDrag(drag.IsObject, drag.Index, drag.X, drag.Y, element, e);
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            PreviewCanvas.Children.Add(element);
            if (selected)
            {
                var mark = new Rectangle { Width = Math.Max(16, element.Width) + 6, Height = Math.Max(16, element.Height) + 6, Stroke = Brushes.Yellow, StrokeThickness = 2, IsHitTestVisible = false };
                Canvas.SetLeft(mark, left - 3);
                Canvas.SetTop(mark, top - 3);
                PreviewCanvas.Children.Add(mark);
            }
            if (labels)
            {
                var text = new TextBlock
                {
                    Text = p.Label, Foreground = p.IsObject ? Brushes.Orange : Brushes.DeepSkyBlue, FontSize = 11,
                    Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false,
                };
                Canvas.SetLeft(text, p.X + 10);
                Canvas.SetTop(text, p.Y - 6);
                PreviewCanvas.Children.Add(text);
            }
        }
        PreviewCanvas.Children.Add(camera);
    }

    private BitmapSource? LoadBackground(int id)
    {
        try
        {
            byte[]? b = null;
            string path = System.IO.Path.Combine(AssetsFolder.Find("moses"), "bgr", $"{id:D4}.bgr");
            if (File.Exists(path)) b = File.ReadAllBytes(path);
            b ??= _gameFiles?.Read("Bgr", $"{id:D4}.bgr");
            if (b == null) return null;
            // .bgr 은 그냥 JPEG(타이틀은 GIF)다 — WPF 가 머리를 보고 알아서 푼다.
            var frame = BitmapFrame.Create(new MemoryStream(b), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException or NotSupportedException or FileFormatException) { return null; }
    }

    /// <summary>그 그림의 컷 — 없으면 뒤에서 풀기 시작하고 null(다 풀면 미리보기를 다시 그린다).</summary>
    private Cut? CutOf(int obs, int motion)
    {
        if (_cuts.TryGetValue((obs, motion), out var cut)) return cut;
        if (_cutsLoading.Add((obs, motion)))
            Task.Run(() =>
            {
                var loaded = LoadCut(obs, motion);
                Dispatcher.BeginInvoke(() =>
                {
                    _cuts[(obs, motion)] = loaded;
                    _cutsLoading.Remove((obs, motion));
                    if (loaded != null && _cutsLoading.Count == 0) DrawPreview();
                });
            });
        return null;
    }

    private Cut? LoadCut(int obs, int motion)
    {
        try
        {
            if (ReadObs(obs) is not { } b) return null;
            var table = ObsMotionTable.Parse(b);
            var clip = motion < 0 ? table?.Resolve(ObsMotionTable.ActionStand, ObsMotionTable.DirectionFront)
                                  : table?.Clips.GetValueOrDefault(motion);
            if (clip?.Keys.FirstOrDefault() is { } key
                && ObsSprite.DecodeFrames(b, new HashSet<(int, int)> { (key.SubentryId, key.Slot) }).Values.FirstOrDefault() is { } frame)
                return ToCut(frame);
            return ObsSprite.DecodeFirstFrame(b) is { } first ? ToCut(first) : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    private static Cut ToCut(ObsFrame f)
    {
        var bitmap = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
        bitmap.Freeze();
        return new Cut(bitmap, f.X, f.Y);
    }

    private byte[]? ReadObs(int id)
    {
        if (_gameFiles?.Read("Obs", $"{id:D4}.obs") is { } b) return b;
        lock (_cuts)
        {
            if (_assetObs == null)
            {
                _assetObs = [];
                try
                {
                    string root = System.IO.Path.GetDirectoryName(AssetsFolder.Find("data"))!;
                    foreach (string path in Directory.EnumerateFiles(root, "*.obs", SearchOption.AllDirectories))
                        if (int.TryParse(System.IO.Path.GetFileNameWithoutExtension(path), out int n)) _assetObs.TryAdd(n, path);
                }
                catch (Exception ex) when (ex is IOException or DirectoryNotFoundException) { }
            }
        }
        return _assetObs.TryGetValue(id, out string? p) ? File.ReadAllBytes(p) : null;
    }

    // ── 끌어 옮기기 ────────────────────────────────────────────────────────

    private void BeginDrag(bool isObject, int index, int x, int y, FrameworkElement element, MouseButtonEventArgs e)
    {
        _drag = (isObject, index, e.GetPosition(PreviewCanvas), x, y, element, Canvas.GetLeft(element), Canvas.GetTop(element));
        EditTabs.SelectedIndex = isObject ? 1 : 2;
        (isObject ? ObjectGrid : PersonGrid).SelectedIndex = index;
        PreviewCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag is not { } d) return;
        var now = e.GetPosition(PreviewCanvas);
        Canvas.SetLeft(d.Element, d.Left + Math.Round(now.X - d.Start.X));
        Canvas.SetTop(d.Element, d.Top + Math.Round(now.Y - d.Start.Y));
    }

    private void Preview_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not { } d) return;
        _drag = null;
        PreviewCanvas.ReleaseMouseCapture();
        if (_current is not { } f) return;
        var now = e.GetPosition(PreviewCanvas);
        int dx = (int)Math.Round(now.X - d.Start.X), dy = (int)Math.Round(now.Y - d.Start.Y);
        if (dx == 0 && dy == 0) { DrawPreview(); return; }
        int x = Math.Clamp(d.X + dx, short.MinValue, short.MaxValue), y = Math.Clamp(d.Y + dy, short.MinValue, short.MaxValue);
        if (d.IsObject) FieldFileWriter.WriteObject(_bytes[f.Id], d.Index, f.Objects[d.Index] with { X = x, Y = y });
        else FieldFileWriter.WritePerson(_bytes[f.Id], f, d.Index, f.People[d.Index] with { X = x, Y = y });
        Reparse(f.Id);
        (d.IsObject ? ObjectGrid : PersonGrid).SelectedIndex = d.Index;
    }
}

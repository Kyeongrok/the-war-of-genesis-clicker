using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 모세스(챕터) 화면의 자료를 통째로 보는 창 — 챕터 목록, 그 챕터의 <b>항성계 → 행성 → 장소</b> 나무,
/// 그리고 상점·메일·성도 점 표.
/// </summary>
/// <remarks>
/// <para>
/// 옵시디안 <b>분석-모세스</b> 를 그대로 옮긴 것이다: 6절(Chp 레코드 — 성도 점 12B · 항성계 66B · 행성 84B · 장소 20B) ·
/// 10절(메일 <c>Dat\MAIL.DAT</c>) · 12절(상점 <c>Shp\NNNN.shp</c>). 읽기는 <see cref="ChapterFile"/>·<see cref="ShopFile"/>·<see cref="MailFile"/> 가 한다.
/// </para>
/// <para>
/// 챕터 차례는 제목 TXR 2284~2313 이 이야기 순서고 그 밖(시험용·옛 자료)은 뒤로 민다 — 분석-전투목록(ba-7) 과 같은 규칙이다.
/// 장소 값 <c>v</c> 는 <b>10000 미만 전투 <c>v</c> · 10000+ 필드 <c>v−10000</c> · 20000+ 상점 <c>v−20000</c></b>,
/// 자동 발생 장소는 항행 목록에 안 나오고 챕터를 열자마자 그리로 간다(분석-모세스 1절「들어오는 두 갈래」).
/// 전투 장소를 두 번 누르면 <see cref="BattleMapWindow.SelectBattle"/> 로 전투 보기 창이 그 전투를 연다.
/// </para>
/// <para>
/// <b>장소 고치기</b> — 챕터는 게임 폴더가 아니라 저장소 <c>assets/moses/chp</c> 에서 읽고(duel-dx 가 읽는 그 파일),
/// 고른 장소의 레코드 20바이트를 <see cref="ChapterFile.WritePlace"/> 로 제자리에 고쳐 쓴다 — 뒤 자료가 안 움직인다.
/// 장소를 더하거나 빼는 것은 행성 칸·스크립트가 번호로 가리키고 있어 여기서 하지 않는다.
/// 저장하면 처음 한 번만 원본을 <c>.bak</c> 으로 남긴다(체질 어빌리티 창과 같다).
/// </para>
/// </remarks>
public partial class ChapterWindow : Window
{
    /// <summary>챕터 목록 한 줄.</summary>
    public sealed record ChapterRow(int Id, string Title, int Background, int Bgm, string StepText,
                                    int SystemCount, int PlanetCount, int PlaceCount, string Note,
                                    bool Story, string Search)
    {
        /// <summary>장소를 고치면 다시 읽은 것으로 갈아 끼운다.</summary>
        public ChapterFile Chapter { get; set; } = null!;
    }

    /// <summary>상점 탭 한 줄 — 그 챕터의 기본 상점 둘과 장소가 가리키는 상점들.</summary>
    public sealed record ShopRow(int No, string Use, string Name, string KindText, string RateText, string Owner, string Items);

    /// <summary>메일 탭 한 줄 — <c>Dat\MAIL.DAT</c> 전체(챕터별 우편함이 아니라 편지 원본 표다).</summary>
    public sealed record MailRow(int Id, string Sender, string Title, string Preview);

    /// <summary>성도 점 탭 한 줄.</summary>
    public sealed record LandmarkRow(int No, int Obs, int Motion, string Pos, string System, string Note);

    /// <summary>나무에서 두 번 누를 수 있는 것 — 장소가 가리키는 곳.</summary>
    private sealed record PlaceTag(ChapterFile.Place Place);

    private readonly string _gameRoot;
    private readonly GameDatabase? _db;
    private readonly List<ChapterRow> _rows = [];
    private Dictionary<int, ShopFile> _shops = [];
    private ICollectionView? _view;
    private BattleMapWindow? _mapWindow;
    private string _countText = "";

    /// <summary>챕터를 읽은 폴더(<c>assets/moses/chp</c>) — 비어 있으면 게임 폴더에서 읽은 것이라 고칠 수 없다.</summary>
    private string _chpFolder = "";
    /// <summary>챕터 번호 → 파일 바이트(고친 것이 여기 먼저 들어가고 저장하면 파일로 간다).</summary>
    private readonly Dictionary<int, byte[]> _bytes = [];
    /// <summary>고치고 아직 저장 안 한 챕터들.</summary>
    private readonly HashSet<int> _dirty = [];
    /// <summary>편집 칸을 채우는 중에는 바뀜 알림을 무시한다.</summary>
    private bool _fillingEditor;

    public ChapterWindow(string gameRoot, GameDatabase? db)
    {
        InitializeComponent();
        _gameRoot = gameRoot;
        _db = db;
        Loaded += (_, _) => StartLoading();
    }

    private string T(int id) => id > 0 && _db is { } db ? db.T((ushort)id) : "";

    // ── 자료 읽기(백그라운드) ────────────────────────────────────────────────

    private void StartLoading()
    {
        if (_gameRoot.Length == 0 || !Directory.Exists(_gameRoot))
        {
            StatusText.Text = "먼저 메인 창에서 게임 폴더를 여세요.";
            return;
        }
        StatusText.Text = "챕터(Chp)·상점(Shp)·편지(MAIL.DAT)를 읽는 중...";
        var files = GameFiles.FromGameRoot(_gameRoot);
        Task.Run(() =>
        {
            List<ChapterFile> chapters = [];
            Dictionary<int, ShopFile> shops = [];
            List<MailEntry> mails = [];
            int chpFiles = 0;
            string error = "";
            try
            {
                if (RepoChapterFolder() is { } folder)
                {
                    // 저장소 사본에서 읽는다 — 고친 것을 저장할 곳이자 게임(duel-dx)이 읽는 곳이다.
                    foreach (string path in Directory.EnumerateFiles(folder, "*.chp"))
                    {
                        chpFiles++;
                        if (!int.TryParse(Path.GetFileNameWithoutExtension(path), out int id)) continue;
                        byte[] bytes = File.ReadAllBytes(path);
                        if (ChapterFile.Parse(id, bytes) is not { } chapter) continue;
                        chapters.Add(chapter);
                        _bytes[id] = bytes;
                    }
                    chapters = [.. chapters.OrderBy(c => c.StoryRank).ThenBy(c => c.Id)];
                    _chpFolder = folder;
                }
                else
                {
                    chpFiles = files.List("Chp", ".chp").Count;
                    chapters = ChapterFile.LoadAll(files);
                }
                shops = ShopFile.LoadAll(files);
                mails = MailFile.Load(files);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                error = ex.Message;
            }
            Dispatcher.BeginInvoke(() => Show(chapters, shops, mails, chpFiles, error));
        });
    }

    /// <summary>저장소 <c>assets/moses/chp</c> — 없으면(배포용 exe 등) null 이고 그때는 게임 폴더에서 읽기만 한다.</summary>
    private static string? RepoChapterFolder()
    {
        try
        {
            string folder = Path.Combine(AssetsFolder.Find("moses"), "chp");
            return Directory.Exists(folder) ? folder : null;
        }
        catch (DirectoryNotFoundException) { return null; }
    }

    private void Show(List<ChapterFile> chapters, Dictionary<int, ShopFile> shops, List<MailEntry> mails, int chpFiles, string error)
    {
        _shops = shops;
        _rows.Clear();
        foreach (var c in chapters)
        {
            string title = T(c.TitleText) is { Length: > 0 } t ? t : "(제목 없음)";
            var notes = new List<string>();
            if (!c.Exact) notes.Add($"자료 끝 {c.TailBytes}바이트 남음");
            if (c.Landmarks.Count == 0 && c.Systems.Count > 0) notes.Add("성도 점 없음");
            _rows.Add(new ChapterRow(c.Id, title, c.Background, c.Bgm, StepText(c.StartStep, c.StartNumber),
                                     c.Systems.Count, c.Planets.Count, c.Places.Count, string.Join(" · ", notes),
                                     c.StoryRank < 9999, $"{c.Id:D4} {title}")
            { Chapter = c });
        }

        ChapterGrid.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = Accept;

        MailGrid.ItemsSource = mails.Select(m => new MailRow(m.Id, SenderName(m.Sender), T(m.TitleText), m.Preview(120))).ToList();

        int story = _rows.Count(r => r.Story);
        _countText = $"챕터 {_rows.Count}개(이야기 {story}개 · 그 밖 {_rows.Count - story}개, Chp 파일 {chpFiles}개 중 {chpFiles - _rows.Count}개는 배치가 안 맞아 건너뜀)"
                     + $" · 상점 {shops.Count}개 · 편지 {mails.Count}통"
                     + (error.Length > 0 ? $" · 읽기 실패: {error}" : "")
                     + (_chpFolder.Length > 0 ? $" · 읽은 곳 {_chpFolder}" : " · 게임 폴더에서 읽음(저장소 assets 가 없어 고칠 수 없음)");
        UpdateStatus();
        if (_rows.Count > 0) ChapterGrid.SelectedIndex = 0;
    }

    /// <summary>최저 항행 단계 — 0 항성계 · 1 행성 · 2 장소(분석-모세스 6절). 그 단계에서 시작할 번호를 함께 적는다.</summary>
    private static string StepText(int step, int number) => step switch
    {
        0 => "0 항성계",
        1 => $"1 행성 (항성계 {number})",
        2 => $"2 장소 (행성 {number})",
        _ => $"{step} (안 씀)",
    };

    private string SenderName(int chrCode) =>
        _db?.Character(chrCode) is { } c && T(c.NameId) is { Length: > 0 } n ? n : $"Chr {chrCode:D4}";

    // ── 걸러내기 ─────────────────────────────────────────────────────────────

    private bool Accept(object o)
    {
        if (o is not ChapterRow r) return false;
        if (StoryOnlyToggle.IsChecked == true && !r.Story) return false;
        foreach (string word in FilterBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!r.Search.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _view?.Refresh();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_countText.Length == 0) return;
        int shown = _view?.Cast<object>().Count() ?? _rows.Count;
        StatusText.Text = $"{_countText} · 보이는 줄 {shown}개";
    }

    // ── 고른 챕터 풀어 보이기 ────────────────────────────────────────────────

    private void ChapterGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChapterGrid.SelectedItem is not ChapterRow row) return;
        var chapter = row.Chapter;
        TreeHeader.Text = $"Chp {chapter.Id:D4}「{row.Title}」 — 항성계 → 행성 → 장소  ·  배경 Bgr {chapter.Background:D4} · BGM {chapter.Bgm:D4} · 최저 항행 단계 {row.StepText}";
        BuildTree(chapter);
        ShopGrid.ItemsSource = BuildShops(chapter);
        LandmarkGrid.ItemsSource = BuildLandmarks(chapter);
    }

    /// <summary>항성계 → 행성 → 장소 나무. 어느 항성계·행성에도 안 달린 것은 따로 묶어 둔다.</summary>
    private void BuildTree(ChapterFile chapter)
    {
        PlaceTree.Items.Clear();
        var usedPlanets = new HashSet<int>();
        var usedPlaces = new HashSet<int>();

        foreach (var system in chapter.Systems)
        {
            var node = Node($"항성계 {system.No} 「{TextOr(system.NameText)}」  ·  배경 Bgr {system.Background:D4}"
                            + $"  ·  행성 {system.Planets.Count}개"
                            + (system.People.Count > 0 ? $"  ·  통신 인물 {system.People.Count}명" : ""), null);
            foreach (int planetNo in system.Planets)
            {
                usedPlanets.Add(planetNo);
                if (chapter.PlanetOf(planetNo) is not { } planet)
                {
                    node.Items.Add(Node($"행성 {planetNo} — 레코드가 없다", null));
                    continue;
                }
                node.Items.Add(PlanetNode(chapter, planet, usedPlaces));
            }
            PlaceTree.Items.Add(node);
        }

        var loosePlanets = chapter.Planets.Where(p => !usedPlanets.Contains(p.No)).ToList();
        if (loosePlanets.Count > 0)
        {
            var node = Node($"어느 항성계에도 안 달린 행성 {loosePlanets.Count}개", null);
            foreach (var planet in loosePlanets) node.Items.Add(PlanetNode(chapter, planet, usedPlaces));
            PlaceTree.Items.Add(node);
        }

        var loosePlaces = chapter.Places.Where(p => !usedPlaces.Contains(p.No)).ToList();
        if (loosePlaces.Count > 0)
        {
            var node = Node($"어느 행성에도 안 달린 장소 {loosePlaces.Count}개 (스크립트나 자동 발생으로만 쓰인다)", null);
            foreach (var place in loosePlaces) node.Items.Add(PlaceNode(place));
            PlaceTree.Items.Add(node);
        }
    }

    private TreeViewItem PlanetNode(ChapterFile chapter, ChapterFile.Planet planet, HashSet<int> usedPlaces)
    {
        var node = Node($"행성 {planet.No} 「{TextOr(planet.NameText)}」  ·  성도 Obs {planet.MapObs:D4} 모션 {planet.MapMotion} @ ({planet.X}, {planet.Y})"
                        + $"  ·  구체 Obs {planet.GlobeObs:D4} 모션 {planet.GlobeMotion}"
                        + $"  ·  장소 {planet.Places.Count}개", null);
        foreach (int placeNo in planet.Places)
        {
            usedPlaces.Add(placeNo);
            if (chapter.PlaceOf(placeNo) is not { } place)
            {
                node.Items.Add(Node($"장소 {placeNo} — 레코드가 없다", null));
                continue;
            }
            node.Items.Add(PlaceNode(place));
        }
        return node;
    }

    private TreeViewItem PlaceNode(ChapterFile.Place place)
    {
        string text = $"장소 {place.No} 「{TextOr(place.NameText)}」 — {ValueText(place)}";
        if (place.IsAuto) text += "  ·  자동 발생(목록에 안 나오고 챕터를 열자마자 간다)";
        if (place.DescText > 0) text += $"  ·  설명 「{T(place.DescText)}」";
        var node = Node(text, new PlaceTag(place));
        if (place.Conditions.FirstOrDefault(c => c.Variable >= 0) is { Variable: >= 0 } cond)
            node.ToolTip = $"조건(가설): 변수 {cond.Variable} {OperatorText(cond.Operator)} {cond.Value}";
        return node;
    }

    /// <summary>장소 값이 가리키는 곳(분석-모세스 6절「장소를 누르면」).</summary>
    private static string ValueText(ChapterFile.Place place) => place.Value <= 0
        ? $"가는 곳 없음 (값 {place.Value})"
        : place.Kind switch
        {
            ChapterFile.PlaceKind.Shop => $"상점 {place.Target:D4} (값 {place.Value})",
            ChapterFile.PlaceKind.Field => $"필드 {place.Target:D4} (값 {place.Value})",
            _ => $"전투 {place.Target:D4} (값 {place.Value}) — 두 번 누르면 전투 보기 창이 열린다",
        };

    /// <summary>조건 연산자 표(분석-모세스 6절 <c>0x100fdad0</c>).</summary>
    private static string OperatorText(int op) => op switch
    {
        0 => "==", 1 => "!=", 2 => "<", 3 => "<=", 4 => ">", 5 => ">=", _ => $"연산자 {op}",
    };

    private static TreeViewItem Node(string text, PlaceTag? tag) => new() { Header = text, Tag = tag, IsExpanded = true };

    private string TextOr(int textId) => T(textId) is { Length: > 0 } s ? s : "(이름 없음)";

    /// <summary>그 챕터가 쓰는 상점 — 기본 상점 둘(Chp 머리 3·4)과 장소 값 20000+ 가 가리키는 것들.</summary>
    private List<ShopRow> BuildShops(ChapterFile chapter)
    {
        var uses = new Dictionary<int, List<string>>();
        void Use(int no, string why)
        {
            if (no < 0) return;
            if (!uses.TryGetValue(no, out var list)) uses[no] = list = [];
            if (!list.Contains(why)) list.Add(why);
        }

        Use(chapter.ItemShop, "기본 아이템 상점 (주 화면 ITEM SHOP)");
        Use(chapter.VtShop, "기본 VT 상점 (주 화면 VT SHOP)");
        foreach (var place in chapter.Places.Where(p => p.Value > 0 && p.Kind == ChapterFile.PlaceKind.Shop))
            Use(place.Target, $"장소 「{TextOr(place.NameText)}」");

        var rows = new List<ShopRow>();
        foreach (int no in uses.Keys.OrderBy(n => n))
        {
            string use = string.Join(" · ", uses[no]);
            if (!_shops.TryGetValue(no, out var shop))
            {
                rows.Add(new ShopRow(no, use, "(Shp 파일이 없다)", "", "", "", ""));
                continue;
            }
            rows.Add(new ShopRow(no, use, TextOr(shop.NameText),
                                 shop.IsEquipment ? "2 장비 상점" : $"{shop.Kind} 그 밖",
                                 $"{shop.Rate}%",
                                 $"{TextOr(shop.OwnerNameText)} (Obs {shop.OwnerObs:D4})",
                                 string.Join(", ", shop.Items.Select(id => ItemText(shop, id)))));
        }
        return rows;
    }

    /// <summary>파는 아이템 한 칸 — 이름과 그 상점에서의 값(<c>Itm 가격 × 가격률 / 100</c>).</summary>
    private string ItemText(ShopFile shop, int itemId)
    {
        if (_db?.Items.GetValueOrDefault(itemId) is not { } item) return $"Itm {itemId}(자료 없음)";
        string name = T(item.NameId) is { Length: > 0 } n ? n : $"Itm {itemId}";
        // 가격이 0xffffffff 인 칸은 목록 구분선 노릇을 하는 가짜 아이템이다(itm.dat 에 "=== 옵션 ===" 같은 줄이 있다).
        return item.Price > int.MaxValue ? $"{name}(구분선)" : $"{name} {shop.PriceOf(item.Price)}GP";
    }

    /// <summary>성도 점 — 자리가 행성과 똑같으면 항행 단계 1 에서 숨긴다(분석-모세스 6절 단계 1).</summary>
    private List<LandmarkRow> BuildLandmarks(ChapterFile chapter)
    {
        var rows = new List<LandmarkRow>();
        foreach (var lm in chapter.Landmarks)
        {
            string system = chapter.SystemOf(lm.SystemNo) is { } s ? $"{lm.SystemNo} 「{TextOr(s.NameText)}」" : $"{lm.SystemNo} (레코드 없음)";
            var same = chapter.Planets.FirstOrDefault(p => p.X == lm.X && p.Y == lm.Y);
            string note;
            if (same != null)
                note = $"행성 {same.No}「{TextOr(same.NameText)}」 자리와 같음 — 항행 단계 1 에서 점을 숨긴다";
            else
            {
                var near = chapter.Planets.OrderBy(p => Math.Abs(p.X - lm.X) + Math.Abs(p.Y - lm.Y)).FirstOrDefault();
                note = near == null
                    ? "행성이 없다"
                    : $"가장 가까운 행성 {near.No}「{TextOr(near.NameText)}」 와 ({near.X - lm.X:+#;-#;0}, {near.Y - lm.Y:+#;-#;0}) 어긋남 — 점이 그대로 남는다";
            }
            rows.Add(new LandmarkRow(lm.No, lm.Obs, lm.Motion, $"({lm.X}, {lm.Y})", system, note));
        }
        return rows;
    }

    // ── 전투 보기 창 잇기 ────────────────────────────────────────────────────

    private void PlaceTree_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedPlace();

    private void PlaceTree_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OpenSelectedPlace();
        e.Handled = true;
    }

    /// <summary>고른 장소가 전투면 전투 보기 창에서 연다 — 창은 하나만 띄우고, 떠 있으면 전투만 바꾼다.</summary>
    private void OpenSelectedPlace()
    {
        if (PlaceTree.SelectedItem is not TreeViewItem { Tag: PlaceTag { Place: { Value: > 0 } place } }) return;
        if (place.Kind != ChapterFile.PlaceKind.Battle)
        {
            StatusText.Text = $"{_countText} · {(place.Kind == ChapterFile.PlaceKind.Shop ? $"상점 {place.Target:D4} 은 아래 상점 탭에 있습니다" : $"필드 {place.Target:D4} 를 여는 창은 아직 없습니다")}";
            return;
        }
        if (_mapWindow == null)
        {
            _mapWindow = new BattleMapWindow(_gameRoot, _db) { Owner = Owner ?? this };
            _mapWindow.Closed += (_, _) => _mapWindow = null;
            _mapWindow.Show();
        }
        _mapWindow.SelectBattle(place.Target);
        _mapWindow.Activate();
    }

    // ── 장소 고치기 ──────────────────────────────────────────────────────────

    private ChapterRow? CurrentRow => ChapterGrid.SelectedItem as ChapterRow;

    private ChapterFile.Place? SelectedPlace =>
        PlaceTree.SelectedItem is TreeViewItem { Tag: PlaceTag tag } ? tag.Place : null;

    private void PlaceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e) => FillEditor(SelectedPlace);

    /// <summary>고른 장소를 편집 칸에 채운다 — 장소가 아니면(항성계·행성 줄) 칸을 잠근다.</summary>
    private void FillEditor(ChapterFile.Place? place)
    {
        _fillingEditor = true;
        try
        {
            bool editable = place != null && _chpFolder.Length > 0 && place.Offset >= 0;
            PlaceEditor.IsEnabled = editable;
            if (place == null)
            {
                PlaceEditor.Header = "장소 고치기 — 나무에서 장소를 고르세요";
                return;
            }
            PlaceEditor.Header = $"장소 {place.No} 고치기 — Chp {CurrentRow?.Id:D4} 의 파일 +0x{place.Offset:X} 부터 20바이트"
                                 + (_chpFolder.Length == 0 ? " (게임 폴더에서 읽어 고칠 수 없음)" : "");
            PlaceNameBox.Text = place.NameText.ToString();
            PlaceDescBox.Text = place.DescText.ToString();
            PlaceKindBox.SelectedIndex = place.Value <= 0 ? 3 : (int)place.Kind;
            PlaceTargetBox.Text = place.Value <= 0 ? "" : place.Target.ToString();
            PlaceAutoBox.IsChecked = place.IsAuto;
            PlaceLonBox.Text = place.Lon.ToString();
            PlaceLatBox.Text = place.Lat.ToString();
            var (variable, value, op) = place.Conditions.Count > 0 ? place.Conditions[0] : (-1, -1, -1);
            PlaceCondVarBox.Text = variable.ToString();
            PlaceCondValueBox.Text = variable < 0 ? "" : value.ToString();
            PlaceCondOpBox.SelectedIndex = op is >= 0 and <= 5 ? op : -1;
        }
        finally
        {
            _fillingEditor = false;
            UpdatePreviews();
        }
    }

    private void PlaceField_Changed(object sender, RoutedEventArgs e)
    {
        if (!_fillingEditor) UpdatePreviews();
    }

    /// <summary>TXR 번호 옆에 그 글을, 가는 곳 번호 옆에 그 자료가 있는지를 보여 준다.</summary>
    private void UpdatePreviews()
    {
        PlaceNamePreview.Text = int.TryParse(PlaceNameBox.Text, out int name) ? T(name) : "";
        PlaceDescPreview.Text = int.TryParse(PlaceDescBox.Text, out int desc) ? T(desc) : "";
        PlaceTargetBox.IsEnabled = PlaceKindBox.SelectedIndex is >= 0 and <= 2;
        PlaceTargetPreview.Text = !int.TryParse(PlaceTargetBox.Text, out int target) || PlaceKindBox.SelectedIndex is < 0 or > 2 ? "" : PlaceKindBox.SelectedIndex switch
        {
            2 => _shops.TryGetValue(target, out var shop) ? $"상점 「{TextOr(shop.NameText)}」" : "Shp 파일이 없다",
            1 => File.Exists(Path.Combine(AssetsFolderOrEmpty("data"), "Fld", $"{target:D4}.fld")) ? $"Fld {target:D4}" : $"Fld {target:D4} 가 assets 에 없다",
            _ => File.Exists(Path.Combine(AssetsFolderOrEmpty("data"), "Btl", $"{target:D4}.btl")) ? $"Btl {target:D4}" : $"Btl {target:D4} 가 assets 에 없다",
        };
        PlaceCondValueBox.IsEnabled = PlaceCondOpBox.IsEnabled = int.TryParse(PlaceCondVarBox.Text, out int v) && v >= 0;
    }

    private static string AssetsFolderOrEmpty(string sub)
    {
        try { return AssetsFolder.Find(sub); }
        catch (DirectoryNotFoundException) { return ""; }
    }

    /// <summary>편집 칸의 값으로 장소 레코드를 고쳐 쓰고 챕터를 다시 읽는다 — 파일에는 저장을 눌러야 간다.</summary>
    private void PlaceApply_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRow is not { } row || SelectedPlace is not { } place || !_bytes.TryGetValue(row.Id, out var bytes)) return;
        if (ReadEdited(place) is not { } edited) return;

        ChapterFile.WritePlace(bytes, edited);
        if (ChapterFile.Parse(row.Id, bytes) is not { } reread)
        {
            PlaceEditStatus.Text = "고쳐 쓴 뒤 챕터를 다시 못 읽었습니다 — 되돌리기를 누르세요.";
            return;
        }
        row.Chapter = reread;
        _dirty.Add(row.Id);
        BuildTree(reread);
        SelectPlaceNode(edited.No);
        PlaceEditStatus.Text = $"장소 {edited.No} 를 고쳤습니다 — 저장을 눌러야 파일에 남습니다. (저장 안 한 챕터 {_dirty.Count}개)";
    }

    /// <summary>편집 칸을 읽어 새 장소 레코드를 만든다. 값이 틀리면 알리고 null.</summary>
    private ChapterFile.Place? ReadEdited(ChapterFile.Place place)
    {
        string? error = null;
        int Word(TextBox box, string what, int min, int max, int blank = int.MinValue)
        {
            if (box.Text.Trim().Length == 0 && blank != int.MinValue) return blank;
            if (int.TryParse(box.Text.Trim(), out int v) && v >= min && v <= max) return v;
            error ??= $"{what} 은(는) {min}~{max} 사이 정수여야 합니다.";
            return 0;
        }

        int name = Word(PlaceNameBox, "이름 TXR", -1, short.MaxValue);
        int desc = Word(PlaceDescBox, "설명 TXR", -1, short.MaxValue);
        int lon = Word(PlaceLonBox, "경도칸", -1, 9);
        int lat = Word(PlaceLatBox, "위도칸", -1, 9);
        // 장소 값: 전투 v · 필드 10000+v · 상점 20000+v(분석-모세스 6절). 「없음」이면 원래 값이 없던 모양(0 이하)을 그대로 두고, 아니면 −1.
        int value = PlaceKindBox.SelectedIndex switch
        {
            0 => Word(PlaceTargetBox, "전투 번호", 1, 9999),
            1 => 10000 + Word(PlaceTargetBox, "필드 번호", 0, 9999),
            2 => 20000 + Word(PlaceTargetBox, "상점 번호", 0, 9999),
            _ => place.Value <= 0 ? place.Value : -1,
        };
        int variable = Word(PlaceCondVarBox, "조건 깃발", -1, 1999);   // 진행 깃발은 2000칸(0x101b6050)
        (int, int, int) condition = (-1, -1, -1);
        if (variable >= 0)
        {
            if (PlaceCondOpBox.SelectedIndex < 0) error ??= "조건 연산자를 고르세요.";
            condition = (variable, Word(PlaceCondValueBox, "조건 값", short.MinValue, short.MaxValue), PlaceCondOpBox.SelectedIndex);
        }

        if (error != null)
        {
            PlaceEditStatus.Text = error;
            return null;
        }
        return place with
        {
            NameText = name, DescText = desc, Value = value, Lon = lon, Lat = lat,
            Auto = PlaceAutoBox.IsChecked == true ? Math.Max(1, place.Auto) : 0,
            Conditions = [condition],
        };
    }

    private void SelectPlaceNode(int placeNo)
    {
        foreach (var item in AllNodes(PlaceTree.Items))
            if (item.Tag is PlaceTag { Place.No: var no } && no == placeNo)
            {
                item.IsSelected = true;
                item.BringIntoView();
                return;
            }
    }

    private static IEnumerable<TreeViewItem> AllNodes(ItemCollection items)
    {
        foreach (var item in items.OfType<TreeViewItem>())
        {
            yield return item;
            foreach (var child in AllNodes(item.Items)) yield return child;
        }
    }

    /// <summary>고친 챕터를 모두 저장소 <c>assets/moses/chp</c> 에 쓴다. 처음 한 번은 원본을 .bak 으로 남긴다.</summary>
    private void Save_Click(object sender, RoutedEventArgs e) => SaveDirty();

    private bool SaveDirty()
    {
        if (_dirty.Count == 0) { PlaceEditStatus.Text = "고친 챕터가 없습니다."; return true; }
        try
        {
            foreach (int id in _dirty.Order())
            {
                string path = Path.Combine(_chpFolder, $"{id:D4}.chp");
                string backup = path + ".bak";
                if (!File.Exists(backup) && File.Exists(path)) File.Copy(path, backup);
                File.WriteAllBytes(path, _bytes[id]);
            }
            PlaceEditStatus.Text = $"챕터 {string.Join(", ", _dirty.Order().Select(id => id.ToString("D4")))} 을(를) 저장했습니다 — 게임을 다시 켜면 반영됩니다(처음 원본은 .bak).";
            _dirty.Clear();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PlaceEditStatus.Text = $"저장하지 못했습니다: {ex.Message}";
            return false;
        }
    }

    /// <summary>고른 챕터를 파일에 있는 대로 다시 읽는다.</summary>
    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentRow is not { } row || _chpFolder.Length == 0) return;
        byte[] bytes = File.ReadAllBytes(Path.Combine(_chpFolder, $"{row.Id:D4}.chp"));
        if (ChapterFile.Parse(row.Id, bytes) is not { } chapter) return;
        _bytes[row.Id] = bytes;
        row.Chapter = chapter;
        _dirty.Remove(row.Id);
        int? keep = SelectedPlace?.No;
        BuildTree(chapter);
        if (keep is { } no) SelectPlaceNode(no);
        PlaceEditStatus.Text = $"Chp {row.Id:D4} 을(를) 파일에 있는 대로 되돌렸습니다.";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_dirty.Count > 0)
        {
            var answer = MessageBox.Show(this, $"저장 안 한 챕터가 {_dirty.Count}개 있습니다. 저장할까요?", "챕터 편집",
                                         MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !SaveDirty())) e.Cancel = true;
        }
        base.OnClosing(e);
    }
}

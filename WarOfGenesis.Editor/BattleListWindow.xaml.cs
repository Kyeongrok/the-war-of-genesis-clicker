using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 게임의 모든 전투(<c>Btl</c>)를 챕터별로 묶어 보여 주는 창 — 옵시디안 분석-전투목록(ba-7) 의 표를 그대로 옮긴 것이다.
/// 줄을 두 번 누르면 그 전투의 맵과 아군·적군 배치를 <see cref="BattleMapWindow"/> 에서 연다.
/// </summary>
/// <remarks>
/// 챕터 묶음은 <see cref="BattleChapters.BuildAll"/>(battle_list.py 옮김)이 계산한다: 챕터 장소 레코드 · Chp·Fld 스크립트
/// 행동 10 · 앞 전투 이벤트 행동 10(이어서) 이 세 간선으로 챕터에서 닿는 전투를 그 챕터 것으로 친다(274개 중 210개).
/// 어느 챕터에서도 안 닿는 전투는 마지막 묶음("못 이은 전투")에 몰아 둔다. 목록 읽기는 백그라운드에서 두 걸음으로 한다
/// — 전투 파일을 다 읽어 줄을 보인 뒤, 챕터 잇기(0.2초쯤)를 끝내고 묶음을 채운다.
/// </remarks>
public partial class BattleListWindow : Window
{
    /// <summary>전투 한 줄. <see cref="Group"/> 이 묶음 이름(챕터), <see cref="GroupRank"/> 이 묶음 차례다.</summary>
    public sealed record Row(int Id, string Name, int MapId, string Obt, int AllyCount, int AlliedCount, int EnemyCount,
                             int PlacementCount, string Allies, string Enemies, string Win, string Lose,
                             string Callers, string Chapters, int GroupRank, string Group, string Search);

    private const string OrphanGroup = "(챕터에 못 이은 전투 — 대전·시험용·옛 형식으로 보임)";

    private readonly string _gameRoot;
    private readonly GameDatabase? _db;
    private readonly List<Row> _rows = [];
    private ICollectionView? _view;
    private BattleMapWindow? _mapWindow;
    private string _countText = "";

    public BattleListWindow(string gameRoot, GameDatabase? db)
    {
        InitializeComponent();
        _gameRoot = gameRoot;
        _db = db;
        Loaded += (_, _) => StartLoading();
    }

    // ── 목록 읽기(백그라운드) ────────────────────────────────────────────────

    private void StartLoading()
    {
        if (_gameRoot.Length == 0 || !Directory.Exists(_gameRoot))
        {
            StatusText.Text = "먼저 메인 창에서 게임 폴더를 여세요.";
            return;
        }
        StatusText.Text = "전투 파일을 읽는 중...";
        var files = GameFiles.FromGameRoot(_gameRoot);
        var db = _db;
        System.Threading.Tasks.Task.Run(() =>
        {
            var battles = ReadBattles(files, db);
            Dispatcher.BeginInvoke(() => StatusText.Text = $"전투 {battles.Count}개를 읽었습니다 — 챕터를 잇는 중...");

            Dictionary<int, BattleOrigin> origins = [];
            List<ChapterInfo> chapters = [];
            string error = "";
            try { (origins, chapters) = BattleChapters.BuildAll(files, id => db?.T(id) ?? ""); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { error = ex.Message; }

            var rows = Group(battles, origins, chapters);
            Dispatcher.BeginInvoke(() => Show(rows, chapters.Count, error));
        });
    }

    /// <summary>Btl 파일을 다 읽어 줄로 만든다(챕터 칸은 아직 빈 채로).</summary>
    private static List<Row> ReadBattles(GameFiles files, GameDatabase? db)
    {
        var names = new Dictionary<int, string>();
        string ChrName(int code)
        {
            if (names.TryGetValue(code, out string? cached)) return cached;
            string name = db?.Character(code) is { } c ? db.T(c.NameId) : "";
            return names[code] = name.Length > 0 ? name : $"Chr {code:D4}";
        }
        string T(ushort id) => db?.T(id) is { Length: > 0 } s ? s : "-";

        var rows = new List<Row>();
        foreach (string file in files.List("Btl", ".btl").Keys)
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file), out int id)) continue;
            if (BattleFile.Parse(id, files.Read("Btl", file)) is not { } battle) continue;

            int obt = BattleFile.ObtOfMap(files.Read("Map", $"{battle.MapId:D4}.map")) ?? -1;
            var ally = battle.Units.Where(u => u.Side == 4).ToList();
            var allied = battle.Units.Where(u => u.Side == 3).ToList();
            var enemy = battle.Units.Where(u => u.Side is not (3 or 4)).ToList();
            string allies = Compose(ally.Concat(allied).Select(u => ChrName(u.ChrCode)));
            string enemies = Compose(enemy.Select(u => ChrName(u.ChrCode)));
            string name = db?.T(battle.TitleId) is { Length: > 0 } t ? t : "(이름 없음)";

            rows.Add(new Row(id, name, battle.MapId, obt >= 0 ? $"{obt:D4}" : "?",
                             ally.Count, allied.Count, enemy.Count, battle.Placement.Count,
                             allies, enemies, T(battle.WinId), T(battle.LoseId),
                             "", "", int.MaxValue, OrphanGroup, ""));
        }
        return rows;
    }

    /// <summary>같은 인물이 여럿이면 "이름×수" 로 묶는다(분석-전투목록 표와 같은 꼴). 처음 나온 차례를 지킨다.</summary>
    private static string Compose(IEnumerable<string> names)
    {
        var order = new List<string>();
        var count = new Dictionary<string, int>();
        foreach (string n in names)
            if (!count.TryAdd(n, 1)) count[n]++;
            else order.Add(n);
        return order.Count == 0 ? "-" : string.Join(", ", order.Select(n => count[n] > 1 ? $"{n}×{count[n]}" : n));
    }

    /// <summary>전투마다 닿는 챕터·불리는 곳을 채우고, 묶음(챕터) 차례대로 줄을 늘어놓는다.</summary>
    private static List<Row> Group(List<Row> battles, Dictionary<int, BattleOrigin> origins, List<ChapterInfo> chapters)
    {
        var rank = new Dictionary<int, int>();
        var label = new Dictionary<int, string>();
        for (int i = 0; i < chapters.Count; i++)
        {
            var c = chapters[i];
            rank[c.Id] = i;
            var parts = new List<string> { $"Chp {c.Id:D4}「{(c.Title.Length > 0 ? c.Title : "이름 없음")}」" };
            if (c.Systems.Count > 0) parts.Add($"항성계 {string.Join(", ", c.Systems)}");
            if (c.Planets.Count > 0) parts.Add($"행성 {string.Join(", ", c.Planets)}");
            label[c.Id] = string.Join("  ·  ", parts);
        }

        var rows = new List<Row>();
        foreach (var b in battles)
        {
            var origin = origins.GetValueOrDefault(b.Id);
            string callers = origin is { Callers.Count: > 0 } ? string.Join(" · ", origin.Callers) : "-";
            string chapterText = origin is { Chapters.Count: > 0 }
                ? string.Join(", ", origin.Chapters.Select(c => $"Chp {c.Chapter:D4}「{c.Title}」"))
                : "-";
            var first = origin?.Chapters.FirstOrDefault();
            bool reached = origin is { Chapters.Count: > 0 };
            var row = b with
            {
                Callers = callers,
                Chapters = chapterText,
                GroupRank = reached ? rank.GetValueOrDefault(first!.Value.Chapter, int.MaxValue - 1) : int.MaxValue,
                Group = reached ? label.GetValueOrDefault(first!.Value.Chapter, OrphanGroup) : OrphanGroup,
            };
            rows.Add(row with
            {
                Search = string.Join(" ", [$"{row.Id:D4}", row.Name, $"Obt {row.Obt}", row.Allies, row.Enemies,
                                           row.Win, row.Lose, row.Callers, row.Chapters, row.Group]),
            });
        }
        // 묶음은 줄이 나오는 차례대로 만들어진다 — 챕터 차례(제목 txr 순)로 미리 늘어놓는다.
        return [.. rows.OrderBy(r => r.GroupRank).ThenBy(r => r.Id)];
    }

    private void Show(List<Row> rows, int chapterCount, string error)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        Grid.ItemsSource = _rows;
        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Row.Group)));
        _view.Filter = Accept;

        int reached = _rows.Count(r => r.GroupRank != int.MaxValue);
        _countText = $"챕터 {chapterCount}개 · 전투 {_rows.Count}개(챕터에 닿는 것 {reached}개 · 못 이은 것 {_rows.Count - reached}개)"
                     + (error.Length > 0 ? $" · 챕터 잇기 실패: {error}" : "");
        UpdateStatus();
    }

    // ── 걸러내기 ─────────────────────────────────────────────────────────────

    private bool Accept(object o)
    {
        if (o is not Row r) return false;
        if (r.GroupRank == int.MaxValue && OrphanToggle.IsChecked != true) return false;
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

    // ── 전투 보기 창 열기 ────────────────────────────────────────────────────

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OpenSelected();
        e.Handled = true;
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenSelected();

    /// <summary>고른 전투를 전투 보기 창에서 연다 — 창은 하나만 띄우고, 이미 떠 있으면 그 창에서 전투만 바꾼다.</summary>
    private void OpenSelected()
    {
        if (Grid.SelectedItem is not Row row) return;
        if (_mapWindow == null)
        {
            _mapWindow = new BattleMapWindow(_gameRoot, _db) { Owner = Owner ?? this };
            _mapWindow.Closed += (_, _) => _mapWindow = null;
            _mapWindow.Show();
        }
        _mapWindow.SelectBattle(row.Id);
        _mapWindow.Activate();
    }
}

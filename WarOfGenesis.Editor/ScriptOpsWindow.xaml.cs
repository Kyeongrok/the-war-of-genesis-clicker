using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 스크립트 명령 사전 — 전투(<c>Btl</c>)·필드(<c>Fld</c>)·챕터(<c>Chp</c>) 이벤트 스크립트의 조건·행동 코드마다 뜻·인자·원본 주소와,
/// 저장소 assets 의 자료에서 그 코드가 쓰인 곳을 보여 준다.
/// </summary>
/// <remarks>
/// 뜻은 <see cref="BattleScript"/>(전투)·<see cref="FieldScript"/>(필드·챕터 — 둘은 스크립트 꼴과 실행기가 같다)에서 온다.
/// 자료에는 쓰였는데 표에 없는 코드도 「(모름)」으로 줄을 만들어, 아직 안 풀린 코드가 한눈에 보이게 한다.
/// </remarks>
public partial class ScriptOpsWindow : Window
{
    /// <summary>표 한 줄.</summary>
    public sealed record OpRow(int Code, string Name, string Args, string Note, string Address, int Uses, int Files);

    /// <summary>쓰인 곳 한 줄.</summary>
    public sealed record UseRow(string File, int Event, string Line, short A0, short A1, short A2, short A3, short A4, short A5, short A6, short A7);

    private sealed record Use(string File, int Event, string Line, ScriptCommand Command);

    /// <summary>(스크립트 갈래, 조건이면 true, 코드) → 쓰인 곳.</summary>
    private readonly Dictionary<(bool Battle, bool Condition, int Code), List<Use>> _uses = [];
    private ICollectionView? _view;
    private bool _loaded;

    public ScriptOpsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => StartLoading();
    }

    private bool Battle => KindBox.SelectedIndex is 0 or 1;
    private bool Condition => KindBox.SelectedIndex is 1 or 3;

    private void StartLoading()
    {
        StatusText.Text = "Btl·Fld·Chp 를 훑는 중...";
        Task.Run(() =>
        {
            var uses = new Dictionary<(bool, bool, int), List<Use>>();
            void Add(bool battle, bool condition, string file, int ev, int line, ScriptCommand c)
            {
                if (!uses.TryGetValue((battle, condition, c.Code), out var list)) uses[(battle, condition, c.Code)] = list = [];
                list.Add(new Use(file, ev, (condition ? "조건 " : "행동 ") + line, c));
            }
            string error = "";
            try
            {
                string data = AssetsFolder.Find("data");
                foreach (var (id, bytes) in Files(Path.Combine(data, "Btl"), "*.btl"))
                    if (BattleEvents.Parse(bytes) is { } events)
                        foreach (var e in events)
                        {
                            for (int i = 0; i < e.Conditions.Count; i++) Add(true, true, $"Btl {id:D4}", e.Index, i, e.Conditions[i]);
                            for (int i = 0; i < e.Actions.Count; i++) Add(true, false, $"Btl {id:D4}", e.Index, i, e.Actions[i]);
                        }
                foreach (var (id, bytes) in Files(Path.Combine(data, "Fld"), "*.fld"))
                    if (FieldFile.Parse(id, bytes) is { } field)
                        AddEvents(field.Events, $"Fld {id:D4}");
                string chp = Path.Combine(AssetsFolder.Find("moses"), "chp");
                foreach (var (id, bytes) in Files(chp, "*.chp"))
                    if (ChapterFile.Parse(id, bytes) is { } chapter)
                        AddEvents(chapter.Events, $"Chp {id:D4}");
            }
            catch (Exception ex) when (ex is IOException or DirectoryNotFoundException) { error = ex.Message; }

            void AddEvents(IReadOnlyList<FieldEvent> events, string file)
            {
                foreach (var e in events)
                {
                    for (int i = 0; i < e.Conditions.Count; i++) Add(false, true, file, e.Index, i, e.Conditions[i]);
                    for (int i = 0; i < e.Actions.Count; i++) Add(false, false, file, e.Index, i, e.Actions[i]);
                }
            }

            Dispatcher.BeginInvoke(() =>
            {
                foreach (var (key, list) in uses) _uses[key] = list;
                _loaded = true;
                if (error.Length > 0) StatusText.Text = $"자료를 다 못 읽었습니다: {error}";
                Fill();
            });
        });
    }

    private static IEnumerable<(int Id, byte[] Bytes)> Files(string folder, string pattern)
    {
        if (!Directory.Exists(folder)) yield break;
        foreach (string path in Directory.EnumerateFiles(folder, pattern))
            if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int id)) yield return (id, File.ReadAllBytes(path));
    }

    private void Kind_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Fill(); }

    private void Fill()
    {
        if (!_loaded) return;
        bool battle = Battle, condition = Condition;
        var known = battle ? (condition ? BattleScript.Conditions : BattleScript.Actions)
                           : (condition ? FieldScript.Conditions : FieldScript.Actions);
        var rows = new Dictionary<int, OpRow>();
        foreach (var op in known) rows[op.Code] = Row(op.Code, op.Name, op.Args, op.Note, op.Address);
        foreach (var ((b, c, code), _) in _uses)
            if (b == battle && c == condition && !rows.ContainsKey(code)) rows[code] = Row(code, "(모름)", "", "표에 없는 코드 — 자료에는 쓰였다.", "");
        OpRow Row(int code, string name, string args, string note, string address)
        {
            var list = _uses.GetValueOrDefault((battle, condition, code)) ?? [];
            return new OpRow(code, name, args, note, address, list.Count, list.Select(u => u.File).Distinct().Count());
        }
        _view = CollectionViewSource.GetDefaultView(rows.Values.OrderBy(r => r.Code).ToList());
        _view.Filter = o => o is OpRow r && Matches(r);
        OpGrid.ItemsSource = _view;
        UpdateStatus();
    }

    private bool Matches(OpRow r)
    {
        if (UsedOnlyToggle.IsChecked == true && r.Uses == 0) return false;
        string q = FilterBox.Text.Trim();
        return q.Length == 0 || r.Code.ToString() == q || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.Note.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Args.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _view?.Refresh();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_view == null) return;
        var shown = _view.Cast<OpRow>().ToList();
        StatusText.Text = $"코드 {shown.Count}개 · 모르는 코드 {shown.Count(r => r.Name == "(모름)")}개 · 쓰인 수 합 {shown.Sum(r => r.Uses)}";
    }

    private void OpGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OpGrid.SelectedItem is not OpRow r) return;
        DetailTitle.Text = $"{(Condition ? "조건" : "행동")} {r.Code} — {r.Name}";
        DetailArgs.Text = r.Args.Length > 0 ? $"인자: {r.Args}" : "인자: (없음)";
        DetailNote.Text = r.Note + (r.Address.Length > 0 ? $"\n원본 처리기 {r.Address}" : "");
        var list = _uses.GetValueOrDefault((Battle, Condition, r.Code)) ?? [];
        short A(ScriptCommand c, int k) => k < c.Args.Length ? c.Args[k] : (short)0;
        UseGrid.ItemsSource = list.Select(u => new UseRow(u.File, u.Event, u.Line,
            A(u.Command, 0), A(u.Command, 1), A(u.Command, 2), A(u.Command, 3), A(u.Command, 4), A(u.Command, 5), A(u.Command, 6), A(u.Command, 7))).ToList();
    }

    /// <summary>그 코드를 골라 보여 준다 — 다른 창에서 부를 수 있다.</summary>
    public void Select(bool battle, bool condition, int code)
    {
        KindBox.SelectedIndex = (battle ? 0 : 2) + (condition ? 1 : 0);
        FilterBox.Text = "";
        Fill();
        if (_view?.Cast<OpRow>().FirstOrDefault(r => r.Code == code) is { } row) { OpGrid.SelectedItem = row; OpGrid.ScrollIntoView(row); }
    }
}

using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 이펙트 보기 — 기술(어빌리티·work)이 쓰는 이펙트 목록과, Obs 모션 하나하나를 원본 애니메이터 규칙대로 재생해 본다.
/// </summary>
/// <remarks>
/// <para>
/// 그리기: 모션의 그림 키(<see cref="ObsMotionClip.KeyAt"/>) · 자식 키(다른 Obs 의 모션을 치우침만큼 옮겨 겹침, <c>0x100e5410</c> case 2) ·
/// 섞기 키(17 더하기 · 10 닷지 · 12 스크린 · 1~8 밝기 단계 더하기) · 자리 키(<see cref="ObsMotionClip.OffsetAt"/>)를 따른다. 30틱/초.
/// </para>
/// <para>
/// 기술 탭의 이펙트는 duel-dx 의 <c>AbilityScripts.g.cs</c>(도구 <c>tools/re/work_fx_table.py</c> 가 원본 핸들러에서 뽑은 표)를 읽는다.
/// 저장소 밖(배포판)에서는 그 파일이 없어 기술 탭이 비고 Obs 탭만 쓴다.
/// </para>
/// <para>
/// Obs 는 저장소 assets 아래(effects·ui·moses/obs·characters)에서 먼저 찾고, 없으면 게임 폴더(pak)에서 읽는다. 게임(duel-dx)은 assets 만 읽으므로
/// 「assets ✗」인 Obs 는 게임에서 그려지지 않는다.
/// </para>
/// </remarks>
public partial class EffectViewerWindow : Window
{
    public sealed record WorkRow(int Ability, string Name, int Level, int Work, int Prepare, int Effects, int Missing);
    public sealed record EffectRow(string Part, string Kind, string Obs, string Motion, string Where, string Delay, string Life,
                                   string InAssets, string InGame, string Note, string Address);

    /// <summary><c>work_fx_extra.tsv</c> 한 줄 — 원본 준비·핸들러가 띄우는 것 하나.</summary>
    private sealed record Extra(string Part, string Kind, string Obs, string Motion, string Where, string Delay, string Life, string Picture, string Movie, string Address);
    public sealed record ObsRow(int Obs, string Where, string InAssets);
    public sealed record MotionRow(int Motion, int Length, int Keys, int Children, string Blends, string Sounds);

    /// <summary>뽑은 표의 이펙트 한 줄(duel-dx <c>AbilityEffect</c> 와 같은 꼴).</summary>
    private sealed record Fx(int Obs, int Motion, bool OnTarget, int Lift, int Delay, int Count, bool Fly);

    /// <summary>재생 중인 것 하나 — 시작 틱, 출발·도착(날기면 모션 길이 동안 옮긴다).</summary>
    private sealed record Playing(int Obs, int Motion, int Start, int X0, int Y0, int X1, int Y1, bool Fly);

    private sealed record Sprite(ObsMotionTable Table, Dictionary<(int Sub, int Slot), ObsFrame> Frames);

    private const int W = 640, H = 480;
    private readonly GameDatabase? _db;
    private readonly GameFiles? _game;
    private readonly Dictionary<int, string> _assetObs = [];
    private readonly Dictionary<int, (int[] Actions, Fx[] Effects)> _scripts = [];
    private readonly Dictionary<int, List<int>> _movies = [];
    /// <summary>work → 원본이 띄우는 것 전부(<c>tools/re/work_fx_extra.tsv</c>).</summary>
    private readonly Dictionary<int, List<Extra>> _extras = [];
    private readonly Dictionary<int, Sprite?> _sprites = [];
    private readonly List<WorkRow> _workRows = [];
    private readonly List<ObsRow> _obsRows = [];
    private ICollectionView? _workView, _obsView;

    private readonly WriteableBitmap _bitmap = new(W, H, 96, 96, PixelFormats.Bgra32, null);
    private readonly uint[] _pixels = new uint[W * H];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1 / 30.0) };
    private List<Playing> _playing = [];
    private int _tick, _length = 1;
    private bool _paused;

    public EffectViewerWindow(string gameRoot, GameDatabase? db)
    {
        InitializeComponent();
        _db = db;
        _game = gameRoot.Length > 0 && Directory.Exists(gameRoot) ? GameFiles.FromGameRoot(gameRoot) : null;
        PreviewImage.Source = _bitmap;
        _timer.Tick += (_, _) => { if (!_paused) { _tick++; if (_tick >= _length && LoopToggle.IsChecked == true) _tick = 0; Render(); } };
        Loaded += (_, _) => { Load(); _timer.Start(); Render(); };
        Closed += (_, _) => _timer.Stop();
    }

    // ── 읽기 ────────────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            string root = Path.GetDirectoryName(AssetsFolder.Find("data"))!;
            // effects·ui 를 먼저 — 같은 번호가 인물 sprite 에도 있으면 이펙트 쪽을 쓴다.
            foreach (string folder in new[] { "effects", "ui", Path.Combine("moses", "obs"), "characters" })
            {
                string dir = Path.Combine(root, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (string path in Directory.EnumerateFiles(dir, "*.obs", SearchOption.AllDirectories))
                    if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int n)) _assetObs.TryAdd(n, path);
            }
            // duel-dx/AbilityScripts.g.cs — 저장소 뿌리(assets 의 부모)에서 찾는다.
            string gen = Path.Combine(Path.GetDirectoryName(root)!, "duel-dx", "AbilityScripts.g.cs");
            if (File.Exists(gen)) ParseScripts(File.ReadAllText(gen));
            string tsv = Path.Combine(Path.GetDirectoryName(root)!, "tools", "re", "work_fx_extra.tsv");
            if (File.Exists(tsv)) ParseExtras(File.ReadAllLines(tsv));
        }
        catch (DirectoryNotFoundException) { }

        foreach (int work in _scripts.Keys.Union(_extras.Keys).Order())
        {
            var w = _db?.Works.GetValueOrDefault(work);
            var ab = w != null ? _db!.Abilities.GetValueOrDefault(w.AbilityId) : null;
            _workRows.Add(new WorkRow(w?.AbilityId ?? 0, ab != null ? _db!.T(ab.NameId) : "", w?.Level ?? 0, work, w?.Prepare ?? 0,
                                      _extras.TryGetValue(work, out var ex) ? ex.Count : _scripts[work].Effects.Length,
                                      PictureObs(work).Distinct().Count(o => !_assetObs.ContainsKey(o))));
        }
        _workView = CollectionViewSource.GetDefaultView(_workRows.OrderBy(r => r.Ability).ThenBy(r => r.Level).ToList());
        _workView.Filter = o => o is WorkRow r && MatchesSkill(r);
        WorkGrid.ItemsSource = _workView;

        var numbers = new SortedSet<int>(_assetObs.Keys);
        if (_game != null) foreach (var name in _game.List("Obs", ".obs").Keys)
            if (int.TryParse(Path.GetFileNameWithoutExtension(name), out int n)) numbers.Add(n);
        foreach (int n in numbers)
            _obsRows.Add(new ObsRow(n, _assetObs.TryGetValue(n, out var p) ? Path.GetFileName(Path.GetDirectoryName(p)!) : "게임", _assetObs.ContainsKey(n) ? "○" : "✗"));
        _obsView = CollectionViewSource.GetDefaultView(_obsRows);
        _obsView.Filter = o => o is ObsRow r && MatchesObs(r);
        ObsGrid.ItemsSource = _obsView;
        if (_scripts.Count == 0) Tabs.SelectedIndex = 1;
        PreviewInfo.Text = $"assets 의 Obs {_assetObs.Count}개" + (_game != null ? " · 게임 폴더의 Obs 도 읽는다" : " · 게임 폴더를 안 열어 assets 만 읽는다")
                         + (_scripts.Count > 0 ? $" · 기술 표 work {_scripts.Count}개" : " · duel-dx/AbilityScripts.g.cs 를 못 찾아 기술 탭이 비었다");
    }

    /// <summary><c>[work] = ([동작…], [new(Obs, 모션, 대상?, 높이[, 지연, 수, 날기]), …]),</c> 줄과 영상 표 <c>[work] = [new(영상, …)]</c> 를 읽는다.</summary>
    private void ParseScripts(string text)
    {
        var line = new Regex(@"^\s*\[(\d+)\] = \(\[([\d, ]*)\], \[(.*)\]\),\s*$", RegexOptions.Multiline);
        var fx = new Regex(@"new\((\d+), (\d+), (true|false), (-?\d+)(?:, (-?\d+), (-?\d+), (true|false))?\)");
        foreach (Match m in line.Matches(text))
        {
            int work = int.Parse(m.Groups[1].Value);
            if (_scripts.ContainsKey(work)) continue;
            int[] actions = [.. m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse)];
            var effects = fx.Matches(m.Groups[3].Value).Select(f => new Fx(
                int.Parse(f.Groups[1].Value), int.Parse(f.Groups[2].Value), f.Groups[3].Value == "true", int.Parse(f.Groups[4].Value),
                f.Groups[5].Success ? int.Parse(f.Groups[5].Value) : 0, f.Groups[6].Success ? int.Parse(f.Groups[6].Value) : 1,
                f.Groups[7].Success && f.Groups[7].Value == "true")).ToArray();
            _scripts[work] = (actions, effects);
        }
        var movie = new Regex(@"^\s*\[(\d+)\] = \[new\((\d+), (true|false)", RegexOptions.Multiline);
        foreach (Match m in movie.Matches(text))
        {
            int work = int.Parse(m.Groups[1].Value);
            if (!_movies.TryGetValue(work, out var list)) _movies[work] = list = [];
            list.Add(int.Parse(m.Groups[2].Value));
        }
    }

    /// <summary>
    /// 머리 줄: work abi 이름 Lv 갈래 핸들러 호출VA 생성자 종류 Obs 모션 자리 x y z 붙일곳 단계 지연 수명 그림 영상 영상인자.
    /// 도구의 「지연」 칸은 <c>0x100c2530</c>(실제로는 <b>수명</b>), 「수명」 칸은 <c>0x100c24d0</c>(실제로는 <b>시작 지연</b>)이라 바꿔 읽는다.
    /// </summary>
    private void ParseExtras(string[] lines)
    {
        foreach (string line in lines.Skip(1))
        {
            var c = line.Split('\t');
            if (c.Length < 22 || !int.TryParse(c[0], out int work)) continue;
            if (!_extras.TryGetValue(work, out var list)) _extras[work] = list = [];
            list.Add(new Extra(c[4], c[8], c[9], c[10], c[11], c[18], c[17], c[19], c[20], c[6]));
        }
    }

    /// <summary>그 work 가 쓰는 그림 Obs 번호들 — 뽑은 목록의 숫자 Obs(소리 껍데기 제외)와 게임 표.</summary>
    private IEnumerable<int> PictureObs(int work)
    {
        if (_extras.TryGetValue(work, out var ex))
            foreach (var e in ex)
                if (e.Kind == "obs" && int.TryParse(e.Obs, out int n)) yield return n;
        if (_scripts.TryGetValue(work, out var s))
            foreach (var f in s.Effects) yield return f.Obs;
    }

    private byte[]? ReadObs(int n) =>
        _assetObs.TryGetValue(n, out string? path) ? File.ReadAllBytes(path) : _game?.Read("Obs", $"{n:D4}.obs");

    private Sprite? SpriteOf(int n)
    {
        if (_sprites.TryGetValue(n, out var s)) return s;
        try
        {
            if (ReadObs(n) is { } b && ObsMotionTable.Parse(b) is { } table)
            {
                var wanted = table.Clips.Values.SelectMany(c => c.Keys).Select(k => (k.SubentryId, k.Slot)).ToHashSet();
                return _sprites[n] = new Sprite(table, wanted.Count > 0 ? ObsSprite.DecodeFrames(b, wanted) : []);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { }
        return _sprites[n] = null;
    }

    // ── 목록 ────────────────────────────────────────────────────────────────

    private bool MatchesSkill(WorkRow r)
    {
        string q = SkillFilterBox.Text.Trim();
        return q.Length == 0 || r.Name.Replace(" ", "").Contains(q.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)
               || r.Work.ToString() == q || r.Ability.ToString() == q;
    }

    private bool MatchesObs(ObsRow r) =>
        (MissingOnlyToggle.IsChecked != true || r.InAssets == "✗") && (ObsFilterBox.Text.Trim() is not { Length: > 0 } q || r.Obs.ToString().StartsWith(q));

    private void SkillFilter_Changed(object sender, TextChangedEventArgs e) => _workView?.Refresh();
    private void ObsFilter_Changed(object sender, RoutedEventArgs e) => _obsView?.Refresh();

    private void WorkGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkGrid.SelectedItem is not WorkRow r) return;
        bool hasScript = _scripts.TryGetValue(r.Work, out var script);
        var game = hasScript ? script.Effects.Select(f => (f.Obs.ToString(), f.Motion.ToString())).ToHashSet() : [];
        string InAssets(string obs) => int.TryParse(obs, out int n) ? (_assetObs.ContainsKey(n) ? "○" : "✗") : "";
        if (_extras.TryGetValue(r.Work, out var extras))
            EffectGrid.ItemsSource = extras.Select(x => new EffectRow(x.Part, x.Kind, x.Obs, x.Motion, x.Where, x.Delay, x.Life, InAssets(x.Obs),
                x.Kind is "obs" or "obs?" ? (game.Contains((x.Obs, x.Motion)) ? "○" : "✗") : "",
                x.Kind == "mov" ? x.Movie : int.TryParse(x.Obs, out int n) && int.TryParse(x.Motion, out int m) ? NoteOf(n, m) : x.Picture, x.Address)).ToList();
        else if (hasScript)
            EffectGrid.ItemsSource = script.Effects.Select(f => new EffectRow("", f.Fly ? "날기" : "obs", f.Obs.ToString(), f.Motion.ToString(),
                f.OnTarget ? "대상" : "나", f.Delay.ToString(), "", InAssets(f.Obs.ToString()), "○", NoteOf(f.Obs, f.Motion), "")).ToList();
        string movies = _movies.TryGetValue(r.Work, out var list) ? " · 영상 Mov " + string.Join(", ", list.Select(m => m.ToString("D4"))) : "";
        WorkInfo.Text = $"work {r.Work}" + (hasScript ? $" — 동작 사슬 [{string.Join(", ", script.Actions)}]" : "") + $" · 준비 동작 {r.Prepare}"
                        + (r.Prepare == 7 ? "(필살기 — 준비 갈래는 공통 앞머리, 게임은 FinisherPrelude 가 그린다)" : "") + movies;
        PlayWork();
    }

    /// <summary>그림이 없는 Obs(소리 껍데기)나 자식만 든 모션을 알린다.</summary>
    private string NoteOf(int obs, int motion)
    {
        if (SpriteOf(obs) is not { } s) return "못 읽음";
        if (s.Frames.Count == 0) return "그림 없음(소리 껍데기)";
        if (s.Table.Clips.GetValueOrDefault(motion) is not { } c) return "그 모션 없음";
        if (c.Keys.Count == 0 && c.Children.Count > 0) return $"자식 {c.Children.Count}개로만 된 모션";
        return c.Sounds.Count > 0 ? "소리 " + string.Join(",", c.Sounds.Select(x => x.Sound)) : "";
    }

    private void EffectGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EffectGrid.SelectedItem is EffectRow r && int.TryParse(r.Obs, out int obs) && int.TryParse(r.Motion, out int motion)) PlaySingle(obs, motion);
    }

    private void PlayWork_Click(object sender, RoutedEventArgs e) => PlayWork();

    /// <summary>
    /// 기술의 그림을 모두 겹친다 — 시전자는 왼쪽 (200, 330), 대상은 오른쪽 (440, 330). 뽑은 목록이 있으면 그 가운데 Obs·모션이 숫자로 정해진
    /// 것만(셈으로 정하는 obs? 와 코드 그림은 못 그린다), 없으면 게임 표를 쓴다. 필살기의 공통 앞머리(준비 갈래)는 뺀다.
    /// </summary>
    private void PlayWork()
    {
        if (WorkGrid.SelectedItem is not WorkRow r) return;
        var list = new List<Playing>();
        if (_extras.TryGetValue(r.Work, out var extras))
        {
            foreach (var x in extras)
            {
                if (x.Kind != "obs" || (r.Prepare == 7 && x.Part == "준비")) continue;
                if (!int.TryParse(x.Obs, out int obs) || !int.TryParse(x.Motion, out int motion)) continue;
                int delay = int.TryParse(x.Delay, out int d) && d is > 0 and < 1000 ? d : 0;
                var (px, py) = x.Where == "self" ? (200, 330) : (440, 330);
                list.Add(new Playing(obs, motion, delay, px, py, px, py, false));
            }
        }
        else if (_scripts.TryGetValue(r.Work, out var script))
            foreach (var f in script.Effects)
            {
                if (f.Fly) { list.Add(new Playing(f.Obs, f.Motion, f.Delay, 200, 330 - f.Lift, 440, 330 - f.Lift, true)); continue; }
                var (x, y) = f.OnTarget ? (440, 330) : (200, 330);
                for (int k = 0; k < Math.Max(1, f.Count); k++) list.Add(new Playing(f.Obs, f.Motion, f.Delay + k, x, y - f.Lift, x, y - f.Lift, false));
            }
        Start(list, $"work {r.Work} 전체 — 왼쪽 표시가 시전자, 오른쪽이 대상(자리 셈이 필요한 것은 그 표시 위에 겹친다)");
    }

    private void ObsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ObsGrid.SelectedItem is not ObsRow r) return;
        if (SpriteOf(r.Obs) is not { } s) { MotionGrid.ItemsSource = null; PreviewInfo.Text = $"Obs {r.Obs} 을(를) 못 읽었다"; return; }
        MotionGrid.ItemsSource = s.Table.Clips.OrderBy(k => k.Key).Select(k => new MotionRow(k.Key, k.Value.Length, k.Value.Keys.Count, k.Value.Children.Count,
            string.Join(" ", k.Value.Blends.Select(b => b.Mode).Distinct()), string.Join(" ", k.Value.Sounds.Select(x => x.Sound)))).ToList();
        if (MotionGrid.Items.Count > 0) MotionGrid.SelectedIndex = 0;
    }

    private void MotionGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ObsGrid.SelectedItem is ObsRow o && MotionGrid.SelectedItem is MotionRow m) PlaySingle(o.Obs, m.Motion);
    }

    private void PlaySingle(int obs, int motion) =>
        Start([new Playing(obs, motion, 0, W / 2, H * 2 / 3, W / 2, H * 2 / 3, false)], $"Obs {obs} 모션 {motion} — 십자가 기준점");

    private void Start(List<Playing> list, string info)
    {
        _playing = list;
        _length = Math.Max(1, list.Select(p => p.Start + Math.Max(1, LengthOf(p.Obs, p.Motion, 0))).DefaultIfEmpty(1).Max());
        _tick = 0;
        PreviewInfo.Text = info + $" · 길이 {_length}틱";
        Render();
    }

    /// <summary>모션이 끝나는 틱 — 자식까지 본다.</summary>
    private int LengthOf(int obs, int motion, int depth)
    {
        if (depth > 4 || SpriteOf(obs)?.Table.Clips.GetValueOrDefault(motion) is not { } c) return 0;
        int own = Math.Max(c.Length, c.Keys.Count > 0 ? c.Keys[^1].Start + Math.Max(1, c.Keys[^1].Length) : 0);
        foreach (var ch in c.Children) own = Math.Max(own, ch.Start + LengthOf(ch.Obs, ch.Motion, depth + 1));
        return own;
    }

    // ── 재생 조작 ──────────────────────────────────────────────────────────

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PlayButton.Content = _paused ? "▶ 재생" : "⏸ 멈춤";
    }

    private void StepBack_Click(object sender, RoutedEventArgs e) { _paused = true; PlayButton.Content = "▶ 재생"; _tick = Math.Max(0, _tick - 1); Render(); }
    private void StepForward_Click(object sender, RoutedEventArgs e) { _paused = true; PlayButton.Content = "▶ 재생"; _tick++; Render(); }
    private void Restart_Click(object sender, RoutedEventArgs e) { _tick = 0; Render(); }
    private void Background_Changed(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Render(); }

    private void Zoom_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        double z = ZoomBox.SelectedIndex == 1 ? 2 : 1;
        PreviewImage.LayoutTransform = new ScaleTransform(z, z);
    }

    // ── 그리기 ──────────────────────────────────────────────────────────────

    private void Render()
    {
        uint bg = BackgroundBox.SelectedIndex switch { 1 => 0xFF606060, 2 => 0xFF5A3A22, _ => 0xFF000000 };
        Array.Fill(_pixels, bg);
        // 기준점 표시 — 시전자·대상 자리(또는 한 이펙트의 기준점)
        foreach (var (x, y) in _playing.SelectMany(p => new[] { (p.X0, p.Y0), (p.X1, p.Y1) }).Distinct()) Cross(x, y);
        foreach (var p in _playing)
        {
            int t = _tick - p.Start;
            if (t < 0) continue;
            int len = Math.Max(1, LengthOf(p.Obs, p.Motion, 0));
            if (t >= len && !p.Fly && _playing.Count > 1) continue;
            double f = p.Fly ? Math.Min(1, (double)t / len) : 0;
            DrawMotion(p.Obs, p.Motion, t, (int)(p.X0 + (p.X1 - p.X0) * f), (int)(p.Y0 + (p.Y1 - p.Y0) * f), false, 0);
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, W, H), _pixels, W * 4, 0);
        TickText.Text = $"틱 {_tick,4} / {_length}";
    }

    private void Cross(int x, int y)
    {
        for (int d = -6; d <= 6; d++)
        {
            Put(x + d, y, 0xFFFF4040);
            Put(x, y + d, 0xFFFF4040);
        }
    }

    private void Put(int x, int y, uint c) { if ((uint)x < W && (uint)y < H) _pixels[y * W + x] = c; }

    /// <summary>모션 하나를 그 틱에 그린다 — 자식은 제 시작 틱부터 제 길이 동안(원본처럼 끝나면 사라진다).</summary>
    private void DrawMotion(int obs, int motion, int tick, int x, int y, bool mirror, int depth)
    {
        if (depth > 4 || SpriteOf(obs) is not { } s || s.Table.Clips.GetValueOrDefault(motion) is not { } clip) return;
        foreach (var ch in clip.Children)
        {
            if (tick < ch.Start) continue;
            int life = LengthOf(ch.Obs, ch.Motion, depth + 1);
            if (life > 0 && tick - ch.Start >= life) continue;
            bool flip = mirror && ch.Flag == 0;
            DrawMotion(ch.Obs, ch.Motion, tick - ch.Start, x + (flip ? -ch.X : ch.X), y + ch.Y, flip, depth + 1);
        }
        if (clip.KeyAt(tick, loop: false) is not { } key || !s.Frames.TryGetValue((key.SubentryId, key.Slot), out var frame)) return;
        var (ox, oy) = clip.OffsetAt(tick);
        int mode = clip.BlendAt(tick);
        Blit(frame, x + ox, y + oy, mode, mirror);
    }

    private void Blit(ObsFrame f, int x, int y, int mode, bool mirror)
    {
        int left = mirror ? x - f.X - f.Width + 1 : x + f.X, top = y + f.Y;
        for (int yy = 0; yy < f.Height; yy++)
        {
            int py = top + yy;
            if ((uint)py >= H) continue;
            for (int xx = 0; xx < f.Width; xx++)
            {
                int px = left + (mirror ? f.Width - 1 - xx : xx);
                if ((uint)px >= W) continue;
                int o = (yy * f.Width + xx) * 4;
                if (f.Bgra[o + 3] == 0) continue;
                uint c = (uint)(f.Bgra[o] | f.Bgra[o + 1] << 8 | f.Bgra[o + 2] << 16);
                ref uint d = ref _pixels[py * W + px];
                d = mode switch
                {
                    17 => Add(d, c, 256),
                    10 => Dodge(d, c),
                    12 => Screen(d, c),
                    >= 1 and <= 8 => Add(d, c, mode * 32),    // 밝기 단계 — 1/8 씩(가설: 필살기 빛 알갱이 343)
                    _ => 0xFF000000 | c,
                };
            }
        }
    }

    private static uint Add(uint d, uint c, int k)
    {
        uint Ch(int s) => (uint)Math.Min(255, (int)(d >> s & 0xFF) + (int)(c >> s & 0xFF) * k / 256);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Screen(uint d, uint c)
    {
        uint Ch(int s) { int a = (int)(d >> s & 0xFF), b = (int)(c >> s & 0xFF); return (uint)(255 - (255 - a) * (255 - b) / 255); }
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Dodge(uint d, uint c)
    {
        uint Ch(int s) { int a = (int)(d >> s & 0xFF), b = (int)(c >> s & 0xFF); return (uint)(b >= 255 ? 255 : Math.Min(255, a * 255 / (255 - b))); }
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>그 기술을 골라 보여 준다 — 다른 창에서 부를 수 있다.</summary>
    public void SelectWork(int work)
    {
        Tabs.SelectedIndex = 0;
        SkillFilterBox.Text = work.ToString();
        if (_workView?.Cast<WorkRow>().FirstOrDefault() is { } row) WorkGrid.SelectedItem = row;
    }
}

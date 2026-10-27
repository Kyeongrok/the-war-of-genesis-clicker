using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 모션 &gt; 사용자 그림 만들기 — PNG 시트(AI 로 뽑은 걷기 · 서기 · 공격 …)를 게임 전투 그림(파트2 꼴 Obs)으로 싸서 게임의 사용자 그림 폴더
/// (<c>%APPDATA%\DuelDx\sprites\번호_이름.obs</c>)에 넣는다(사용자 요청 ed-4 · menu-103). 자르기 · 배경 지우기 · 줄이기 · 팔레트 · Obs 쓰기는
/// <c>tools/custom-sprite/sheet_to_obs.py</c> 가 하고, 이 창은 시트마다 줄 · 칸을 그림에서 찾아 채우고 그 도구를 부른다.
/// </summary>
/// <remarks>설정(시트 목록)은 넣을 때 같은 폴더에 <c>번호_이름.json</c> 으로 남겨, 동작을 더할 때 열어 이어 만든다.</remarks>
public partial class CustomSpriteWindow : Window
{
    /// <summary>시트 한 장의 설정 — 표의 한 줄.</summary>
    public sealed class Part
    {
        public string Path { get; set; } = "";
        public string FileName => System.IO.Path.GetFileName(Path);
        public int Action { get; set; } = 1;
        public string Rows { get; set; } = "";
        public int ColX { get; set; }
        public int ColW { get; set; } = 192;
        public int Cols { get; set; } = 8;
        public string Background { get; set; } = "key=ff00ff";
        public string Anchor { get; set; } = "body";
        public string Hit { get; set; } = "";
        public string Sound { get; set; } = "";
        public int Ticks { get; set; }
    }

    private sealed record Project(int Id, string Name, int Height, List<Part> Parts);

    /// <summary>게임 동작 번호와 이름(분석-모션 — 파트2 동작, 파트1 그림도 이 번호로 옮겨 쓴다).</summary>
    private static readonly Dictionary<int, string> Actions = new()
    {
        [0] = "0 서기", [1] = "1 걷기", [2] = "2 맞기·쓰러짐", [5] = "5 공격 준비", [6] = "6 시전", [7] = "7 기 모으기",
        [8] = "8 기본 공격", [9] = "9 원거리", [13] = "13 연 2타", [14] = "14 연 3타", [15] = "15 발동",
    };

    private readonly ObservableCollection<Part> _parts = [];
    private string? _built;

    private static string SpriteFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuelDx", "sprites");

    public CustomSpriteWindow()
    {
        InitializeComponent();
        ActionColumn.ItemsSource = Actions;
        PartsGrid.ItemsSource = _parts;
        // 넣어 둔 그림을 골라 이어 만든다 — 창을 열면 첫 그림(설정 .json 이 있는 것)을 바로 연다(사용자 보고: 열었는데 에바가 안 보인다).
        var installed = Directory.Exists(SpriteFolder)
            ? Directory.EnumerateFiles(SpriteFolder, "*.obs").Select(System.IO.Path.GetFileNameWithoutExtension).OfType<string>().Order().ToList() : [];
        InstalledBox.ItemsSource = installed;
        Loaded += (_, _) => { if (installed.Count > 0) InstalledBox.SelectedIndex = 0; else Log("넣어 둔 사용자 그림이 없습니다 — 「시트 추가…」로 시작하세요."); };
    }

    /// <summary>넣어 둔 그림을 고르면 — 설정(.json)이 있으면 열고 미리보기를 만든다, 없으면 그 그림의 모션만 볼 수 있게.</summary>
    private void InstalledBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (InstalledBox.SelectedItem is not string stem) return;
        string obs = System.IO.Path.Combine(SpriteFolder, stem + ".obs"), json = System.IO.Path.ChangeExtension(obs, ".json");
        _built = obs;
        string[] head = stem.Split('_', 2);
        (IdBox.Text, NameBox.Text) = (head[0], head.Length > 1 ? head[1] : "");
        if (!File.Exists(json)) { _parts.Clear(); Log($"{stem}: 설정(.json)이 없어 시트 목록은 비어 있습니다 — 「모션 보기」로 지금 그림을 볼 수 있습니다."); return; }
        if (!LoadProject(json)) return;
        Build_Click(this, new RoutedEventArgs());
    }

    private void Log(string text) { LogBox.AppendText(text + Environment.NewLine); LogBox.ScrollToEnd(); }

    // ── 시트 고르기 · 줄 찾기 ──────────────────────────────────────────────────

    private void AddSheet_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "그림 (*.png)|*.png|모든 파일|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        foreach (string file in dialog.FileNames)
            foreach (var part in Detect(file))
            {
                _parts.Add(part);
                Log($"{part.FileName}: 줄 {part.Rows} · 칸 {part.Cols} × {part.ColW} · {part.Background}");
            }
    }

    /// <summary>
    /// 시트의 줄 · 칸을 그림에서 찾는다 — 바탕 색(왼쪽 위 픽셀)과 다른 픽셀이 있는 가로 띠 = 줄, 세로 띠 = 칸. 칸은 같은 너비로 나뉜 것으로 본다.
    /// 줄 셋마다 한 동작(뒷모습 · 옆모습 · 앞모습 차례로 가정 — AI 시트는 틀릴 수 있다). 바탕이 핑크면 색 키, 아니면 번짐.
    /// </summary>
    private List<Part> Detect(string file)
    {
        var bmp = new FormatConvertedBitmap(new BitmapImage(new Uri(file)), PixelFormats.Bgra32, null, 0);
        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        byte bb = px[(3 * w + 3) * 4], bg = px[(3 * w + 3) * 4 + 1], br = px[(3 * w + 3) * 4 + 2];
        bool Ink(int x, int y)
        {
            int o = (y * w + x) * 4;
            return Math.Max(Math.Abs(px[o] - bb), Math.Max(Math.Abs(px[o + 1] - bg), Math.Abs(px[o + 2] - br))) > 40;
        }
        static List<(int From, int To)> Runs(int n, Func<int, int> count, int minGap)
        {
            var runs = new List<(int, int)>();
            int start = -1, lastInk = -10;
            for (int i = 2; i < n - 2; i++)
            {
                if (count(i) <= 2) continue;
                if (start < 0) start = i;
                else if (i - lastInk > minGap) { runs.Add((start, lastInk + 1)); start = i; }
                lastInk = i;
            }
            if (start >= 0) runs.Add((start, lastInk + 1));
            return runs;
        }
        var rowRuns = Runs(h, y => Enumerable.Range(0, w / 2).Count(i => Ink(i * 2, y)), 3);
        var colRuns = Runs(w, x => Enumerable.Range(0, h / 2).Count(i => Ink(x, i * 2)), 6);
        int cols = Math.Max(1, colRuns.Count), colW = (int)Math.Round(w / (double)cols);
        // 띠 사이 가운데를 경계로(첫 줄은 0 부터, 끝 줄은 그림 끝까지).
        var bands = rowRuns.Select((r, i) => (Top: i == 0 ? 0 : (rowRuns[i - 1].To + r.From) / 2, Bottom: i == rowRuns.Count - 1 ? h : (r.To + rowRuns[i + 1].From) / 2)).ToList();
        bool pink = br > 200 && bg < 80 && bb > 200;
        string background = pink ? $"key={br:x2}{bg:x2}{bb:x2}" : "flood=5";
        string[] dirs = ["back", "side", "front"];
        var parts = new List<Part>();
        for (int g = 0; g < bands.Count; g += 3)
        {
            var rows = bands.Skip(g).Take(3).Select((b, i) => $"{dirs[i]}:{b.Top}:{b.Bottom}");
            int action = _parts.Count == 0 && parts.Count == 0 ? 1 : 8;
            parts.Add(new Part
            {
                Path = file, Action = action, Rows = string.Join(",", rows), ColX = 0, ColW = colW, Cols = cols, Background = background,
                Anchor = action is 0 or 1 ? "body" : "foot",
            });
        }
        if (parts.Count == 0) Log($"{System.IO.Path.GetFileName(file)}: 그림에서 줄을 못 찾았습니다 — 바탕이 한 색인지 보세요.");
        return parts;
    }

    private void RemoveSheet_Click(object sender, RoutedEventArgs e)
    {
        if (PartsGrid.SelectedItem is Part p) _parts.Remove(p);
    }

    // ── 만들기 ─────────────────────────────────────────────────────────────

    private (int Id, string Name, int Height)? Header()
    {
        if (!int.TryParse(IdBox.Text, out int id) || id is < 30000 or > 30999) { Log("번호는 30000~30999 로 하세요."); return null; }
        string name = string.Concat(NameBox.Text.Trim().Split(System.IO.Path.GetInvalidFileNameChars()));
        if (name.Length == 0 || name.Contains('_')) { Log("이름을 적으세요(「_」는 못 씁니다)."); return null; }
        return (id, name, int.TryParse(HeightBox.Text, out int height) && height > 10 ? height : 100);
    }

    /// <summary>시트를 BGRA 로 푼다 — 같은 시트를 여러 줄이 쓰면 한 번만.</summary>
    private static CustomSpriteBuilder.Image LoadSheet(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.EndInit();
        var bmp = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var px = new byte[bmp.PixelWidth * bmp.PixelHeight * 4];
        bmp.CopyPixels(px, bmp.PixelWidth * 4, 0);
        return new CustomSpriteBuilder.Image(bmp.PixelWidth, bmp.PixelHeight, px);
    }

    /// <summary>표의 한 줄 → 만들기 설정. 틀린 칸이 있으면 그 까닭을 돌려준다.</summary>
    private static CustomSpriteBuilder.Part ToPart(Part p, CustomSpriteBuilder.Image sheet)
    {
        var rows = new List<CustomSpriteBuilder.Row>();
        foreach (string spec in p.Rows.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] f = spec.Split('@')[0].Split(':');
            if (f.Length != 3 || !int.TryParse(f[1], out int top) || !int.TryParse(f[2], out int bottom)) throw new FormatException($"{p.FileName}: 줄 「{spec}」 — 방향:위:아래 로 적으세요");
            if (f[0] == "right") continue;                       // 게임이 옆모습을 뒤집는다
            if (!Enum.TryParse<CustomSpriteBuilder.Direction>(f[0], true, out var dir)) throw new FormatException($"{p.FileName}: 방향 「{f[0]}」 — back · side · front");
            rows.Add(new(dir, top, bottom));
        }
        (byte, byte, byte)? key = null;
        int flood = 5, tol = 40;
        foreach (string opt in p.Background.Split('|', ' ', ';').Where(o => o.Length > 0))
        {
            string[] kv = opt.Split('=', 2);
            string v = kv.Length > 1 ? kv[1] : "";
            if (kv[0] == "key" && v.Length == 6) key = (Convert.ToByte(v[..2], 16), Convert.ToByte(v[2..4], 16), Convert.ToByte(v[4..], 16));
            else if (kv[0] == "flood" && int.TryParse(v, out int n)) (key, flood) = (null, n);
            else if (kv[0] == "tol" && int.TryParse(v, out int t)) tol = t;
        }
        var anchor = p.Anchor.Trim().ToLowerInvariant() switch { "foot" => CustomSpriteBuilder.Anchor.Foot, "cell" => CustomSpriteBuilder.Anchor.Cell, _ => CustomSpriteBuilder.Anchor.Body };
        var hits = p.Hit.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToList();
        var sounds = p.Sound.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Split('@')).Select(x => (int.Parse(x[0]), int.Parse(x[1]))).ToList();
        return new(sheet, p.Action, rows, p.ColX, p.ColW, p.Cols, key, tol, flood, anchor, hits, 25, sounds, p.Ticks);
    }

    /// <summary>만들고 미리보기 — <see cref="CustomSpriteBuilder"/>(C#, 파이썬 없이). 시간이 걸려 뒤에서 돈다.</summary>
    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        PartsGrid.CommitEdit();
        if (Header() is not { } head || _parts.Count == 0) { if (_parts.Count == 0) Log("시트를 먼저 추가하세요."); return; }
        List<CustomSpriteBuilder.Part> parts;
        try
        {
            var sheets = new Dictionary<string, CustomSpriteBuilder.Image>();
            parts = [.. _parts.Select(p => ToPart(p, sheets.TryGetValue(p.Path, out var img) ? img : sheets[p.Path] = LoadSheet(p.Path)))];
        }
        catch (Exception ex) when (ex is FormatException or IOException or NotSupportedException or IndexOutOfRangeException or OverflowException)
        {
            Log($"설정이 틀렸습니다: {ex.Message}");
            return;
        }
        Log("만드는 중…");
        IsEnabled = false;
        try
        {
            var result = await Task.Run(() => CustomSpriteBuilder.Build(parts, head.Height));
            string work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx-custom-sprite");
            Directory.CreateDirectory(work);
            string obs = System.IO.Path.Combine(work, $"{head.Id}_{head.Name}.obs");
            File.WriteAllBytes(obs, result.Obs);
            _built = obs;
            var p = result.Preview;
            var bmp = BitmapSource.Create(p.Width, p.Height, 96, 96, PixelFormats.Bgra32, null, p.Bgra, p.Width * 4);
            PreviewImage.Source = bmp;
            Log($"장 {result.Frames} · 모션 {string.Join(" ", result.Motions)} · 배율 {string.Join(" ", result.Scales.Select(x => x.ToString("0.000")))}");
            Log("미리보기 — 장마다 발밑을 같은 자리에 둔 그림이다. 흔들리거나 잘린 줄이 없으면 「게임에 넣기」.");
        }
        catch (InvalidOperationException ex) { Log(ex.Message); }
        finally { IsEnabled = true; }
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_built is not { } built || !File.Exists(built) || Header() is not { } head) { Log("먼저 「만들고 미리보기」를 하세요."); return; }
        Directory.CreateDirectory(SpriteFolder);
        string keep = System.IO.Path.GetFullPath(built);
        foreach (string old in Directory.EnumerateFiles(SpriteFolder, $"{head.Id}_*.obs").Where(f => !string.Equals(System.IO.Path.GetFullPath(f), keep, StringComparison.OrdinalIgnoreCase))) File.Delete(old);   // 이름을 바꿨으면 옛 파일을 지운다(같은 번호 둘이면 게임이 하나만 본다)
        string target = System.IO.Path.Combine(SpriteFolder, $"{head.Id}_{head.Name}.obs");
        if (!string.Equals(System.IO.Path.GetFullPath(built), System.IO.Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(built, target, true);
        File.WriteAllText(System.IO.Path.ChangeExtension(target, ".json"), JsonSerializer.Serialize(new Project(head.Id, head.Name, head.Height, [.. _parts]), new JsonSerializerOptions { WriteIndented = true }));
        Log($"넣었습니다: {target} — 게임의 캐릭터 에디터 「외형」 맨 위 「{head.Name} · 사용자 그림」(켜 둔 게임은 갈래를 다시 누르면 보인다).");
    }

    private void ViewMotions_Click(object sender, RoutedEventArgs e)
    {
        string? path = _built is { } b && File.Exists(b) ? b : null;
        if (path == null) { Log("먼저 「만들고 미리보기」를 하세요."); return; }
        if (ObsMotionTable.Load(path, applyEdits: false) is not { } table) { Log("모션표를 못 읽었습니다."); return; }
        new MotionMappingWindow(System.IO.Path.GetFileNameWithoutExtension(path), table, ObsSprite.Decode(path)) { Owner = this }.Show();
    }

    // ── 설정 열기 · 저장 ─────────────────────────────────────────────────────

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        PartsGrid.CommitEdit();
        if (Header() is not { } head) return;
        var dialog = new SaveFileDialog { Filter = "사용자 그림 설정 (*.json)|*.json", FileName = $"{head.Id}_{head.Name}.json" };
        if (dialog.ShowDialog(this) != true) return;
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new Project(head.Id, head.Name, head.Height, [.. _parts]), new JsonSerializerOptions { WriteIndented = true }));
        Log($"저장했습니다: {dialog.FileName}");
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "사용자 그림 설정 (*.json)|*.json", InitialDirectory = Directory.Exists(SpriteFolder) ? SpriteFolder : "" };
        if (dialog.ShowDialog(this) != true) return;
        LoadProject(dialog.FileName);
    }

    private bool LoadProject(string file)
    {
        try
        {
            if (JsonSerializer.Deserialize<Project>(File.ReadAllText(file)) is not { } project) return false;
            (IdBox.Text, NameBox.Text, HeightBox.Text) = (project.Id.ToString(), project.Name, project.Height.ToString());
            _parts.Clear();
            // 예제 설정은 시트 길이 설정 파일 폴더 기준이다(src/에바/sheet.png) — 그 자리로 편다.
            foreach (var p in project.Parts)
            {
                if (!System.IO.Path.IsPathRooted(p.Path)) p.Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!, p.Path));
                _parts.Add(p);
            }
            var missing = project.Parts.Where(p => !File.Exists(p.Path)).Select(p => p.FileName).Distinct().ToList();
            Log($"열었습니다: {System.IO.Path.GetFileName(file)} — 시트 {_parts.Count}줄{(missing.Count > 0 ? $" · 없는 시트 {string.Join(", ", missing)}" : "")}");
            return missing.Count == 0;
        }
        catch (Exception ex) when (ex is JsonException or IOException) { Log($"설정을 못 읽었습니다: {ex.Message}"); return false; }
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 한 인물 몸짓 파일(Obs)의 <b>모션표</b>를 보는 창 — 모션 번호가 "동작 × 방향"으로 어떻게 나뉘고, 각 모션이
/// 어느 몸짓벌·장을 몇 틱씩 넘기는지 확인하고 틱에 맞춰 재생한다.
/// </summary>
/// <remarks>
/// 모션 번호 = 동작 × 3 + 방향(0 뒷모습, 1 옆모습, 2 앞모습) — <see cref="ObsMotionTable"/>.
/// 동작 이름은 0 서기·1 걷기만 확정이고 나머지는 옵시디안 분석-모션의 추정 이름이다. 틱 빠르기(틱/초)는 원본 값을
/// 아직 몰라서 슬라이더로 바꿔 볼 수 있게 했다.
/// </remarks>
public partial class MotionMappingWindow : Window
{
    private static readonly Dictionary<int, string> ActionNames = new()
    {
        [0] = "서기", [1] = "걷기", [2] = "맞음", [5] = "공격 준비", [9] = "쏘기", [6] = "시전 자세?", [7] = "기 모으기?",
        [8] = "베기", [13] = "연속 베기?", [14] = "연속 베기2?", [15] = "시전 발동?", [24] = "공격 후 복귀",
        [26] = "필살기?", [27] = "필살기2?",
    };

    private static readonly string[] DirectionNames = ["뒷모습", "옆모습", "앞모습"];

    private readonly ObsMotionTable _table;

    /// <summary>손질을 안 입힌 원본 모션표 — 손질은 늘 이것에서 다시 만든다.</summary>
    private readonly ObsMotionTable _original;

    /// <summary>이 몸짓 파일 번호 — 손질을 이 번호로 적는다(<see cref="MotionEdits"/>).</summary>
    private readonly int _obs;
    private readonly Dictionary<(int Sub, int Slot), BitmapSource> _frames = [];
    /// <summary>장의 원본 픽셀과 원점 기준 자리 — PNG 내보내기(<see cref="StripPng"/>)가 쓴다.</summary>
    private readonly Dictionary<(int Sub, int Slot), ObsFrame> _raw = [];
    private readonly string _title;
    private readonly Dictionary<(int Sub, int Slot), BitmapSource> _mirrored = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Stopwatch _clock = new();

    /// <summary>재생 시계에 더하는 틱 — 틱 줄을 눌러 멈춘 자리. 다시 재생하면 여기서부터 이어 간다.</summary>
    private int _tickOffset;

    /// <summary>재생이 지금 컷의 줄을 고르는 중 — 이때의 고르기는 사람이 누른 것이 아니다.</summary>
    private bool _syncingKey;
    private ObsMotionClip? _current;

    private sealed class ClipItem(ObsMotionClip clip, string label)
    {
        public ObsMotionClip Clip { get; } = clip;
        public override string ToString() => label;
    }

    public MotionMappingWindow(string title, ObsMotionTable table, IReadOnlyList<ObsMotion> subentries, int obs = 0, ObsMotionTable? original = null)
    {
        InitializeComponent();
        _table = table;
        _obs = obs;
        _original = original ?? table;
        _title = title;
        Title = $"모션 매핑 — {title}";

        foreach (var sub in subentries)
            for (int i = 0; i < sub.Frames.Count; i++)
            {
                var f = sub.Frames[i];
                var bmp = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
                bmp.Freeze();
                // 모션표가 가리키는 것은 <b>장 번호</b>지 벌 안 순번이 아니다 — 못 푼 장이 하나라도 있으면 순번이 밀린다.
                _frames[(sub.Id, f.SlotId)] = bmp;
                _raw[(sub.Id, f.SlotId)] = f;
            }

        int withKeys = table.Clips.Values.Count(c => c.Keys.Count > 0);
        HeaderText.Text = $"{title}   모션 {table.Clips.Count}개(그림 있는 것 {withKeys}개), 몸짓벌 {subentries.Count}개 — " +
                          "모션 번호 = 동작 × 3 + 방향. 동작 이름에 ? 가 붙은 것은 추정. 틱 손질은 게임에도 들어간다(assets/data/motion_edits.json).";

        DirectionFilter.Items.Add("전체");
        foreach (var d in DirectionNames) DirectionFilter.Items.Add(d);
        DirectionFilter.SelectedIndex = 0;

        _timer.Tick += (_, _) => ShowTick();
        Closed += (_, _) => _timer.Stop();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || DirectionFilter.SelectedIndex < 0) return;
        MotionList.Items.Clear();
        int dir = DirectionFilter.SelectedIndex - 1;
        foreach (var clip in _table.Clips.Values.OrderBy(c => c.Id))
        {
            if (dir >= 0 && clip.Id % 3 != dir) continue;
            if (clip.Keys.Count == 0 && EmptyToggle.IsChecked != true) continue;
            int action = clip.Id / 3;
            string name = ActionNames.GetValueOrDefault(action, "");
            MotionList.Items.Add(new ClipItem(clip, ClipLabel(clip)));
        }
        if (MotionList.Items.Count > 0) MotionList.SelectedIndex = 0;
    }

    private string ClipLabel(ObsMotionClip clip)
    {
        int action = clip.Id / 3;
        string name = ActionNames.GetValueOrDefault(action, "");
        string edited = MotionEdits.Get(_obs, clip.Id) != null ? " · 손질됨" : "";
        // 무슨 동작을 어느 쪽에서 본 것인지를 앞에 — 모션 번호(동작 × 3 + 방향)는 뒤 괄호에(사용자 요청 ed-5: 「모션 0 · 1 · 2」가 셋 다 서기라 헷갈린다).
        string what = name.Length > 0 ? name : $"동작 {action}";
        return $"{what} — {DirectionNames[clip.Id % 3]}   (동작 {action} · 모션 {clip.Id}) · {clip.Length}틱 · 그림 {clip.Keys.Count}{edited}";
    }

    private void MotionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _current = (MotionList.SelectedItem as ClipItem)?.Clip;
        if (_current == null) { FrameImage.Source = null; KeyList.Items.Clear(); return; }
        FillKeyList(0);
        _tickOffset = 0;
        _clock.Restart();
        if (PlayToggle.IsChecked == true) _timer.Start();
        ShowTick();
    }

    /// <summary>
    /// 틱 줄을 사람이 누르면 재생을 멈추고 그 컷을 위에 보인다 — 편집할 컷을 눈으로 고르려고.
    /// 재생 중에 지금 컷의 줄을 따라 고르는 것(<see cref="_syncingKey"/>)은 여기서 무시한다.
    /// </summary>
    private void KeyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingKey || _current == null || KeyList.SelectedIndex < 0 || KeyList.SelectedIndex >= _current.Keys.Count) return;
        if (!KeyList.IsKeyboardFocusWithin && !KeyList.IsMouseOver) return;   // 코드가 고른 것(목록 다시 채우기)은 넘긴다
        PlayToggle.IsChecked = false;
        _tickOffset = _current.Keys[KeyList.SelectedIndex].Start;
        _clock.Reset();
        ShowTick();
    }

    private void FillKeyList(int select)
    {
        KeyList.Items.Clear();
        if (_current == null) return;
        foreach (var (k, i) in _current.Keys.Select((k, i) => (k, i)))
            KeyList.Items.Add($"{i,3}: 틱 {k.Start,4} ~ {k.Start + k.Length - 1,4} ({k.Length,2}틱)  몸짓벌 {k.SubentryId}  장 {k.Slot,3}" +
                              (_raw.TryGetValue((k.SubentryId, k.Slot), out var f) ? $"  {f.Width}×{f.Height}" : "  (그림 없음)"));
        if (KeyList.Items.Count > 0) KeyList.SelectedIndex = Math.Clamp(select, 0, KeyList.Items.Count - 1);
        EditState.Text = _obs <= 0 ? "(파일 번호를 몰라 저장 안 됨)"
                       : MotionEdits.Get(_obs, _current.Id) != null ? "손질됨 — 게임에 들어감" : "원본";
    }

    // ── 틱 손질 ──────────────────────────────────────────────────────────────

    /// <summary>지금 모션의 손질(없으면 원본 그대로인 꼴) — 남길 원본 키 번호와 길이.</summary>
    private (List<int> Keep, List<int> Lengths)? CurrentEdit()
    {
        if (_current == null || !_original.Clips.TryGetValue(_current.Id, out var orig)) return null;
        if (MotionEdits.Get(_obs, _current.Id) is { } e) return ([.. e.Keep], [.. e.Lengths]);
        var keys = orig.Keys.OrderBy(k => k.Start).ToList();
        return ([.. Enumerable.Range(0, keys.Count)], [.. keys.Select(k => Math.Max(1, k.Length))]);
    }

    /// <summary>손질을 적고 그 모션을 다시 만들어 보인다. 원본과 같으면 손질을 지운다.</summary>
    private void Commit(List<int> keep, List<int> lengths, int select)
    {
        if (_current == null || _obs <= 0 || !_original.Clips.TryGetValue(_current.Id, out var orig)) return;
        var keys = orig.Keys.OrderBy(k => k.Start).ToList();
        bool same = keep.Count == keys.Count && keep.Select((k, i) => k == i && lengths[i] == Math.Max(1, keys[i].Length)).All(x => x);
        var edit = same || keep.Count == 0 ? null : new MotionEdits.Edit([.. keep], [.. lengths]);
        MotionEdits.Set(_obs, _current.Id, edit);
        _current = edit == null ? orig : MotionEdits.Apply(orig, edit);
        if (MotionList.SelectedItem is ClipItem)
        {
            int at = MotionList.SelectedIndex;
            MotionList.SelectionChanged -= MotionList_SelectionChanged;
            MotionList.Items[at] = new ClipItem(_current, ClipLabel(_current));
            MotionList.SelectedIndex = at;
            MotionList.SelectionChanged += MotionList_SelectionChanged;
        }
        FillKeyList(select);
        _tickOffset = _current.Keys.Count > 0 ? _current.Keys[Math.Clamp(select, 0, _current.Keys.Count - 1)].Start : 0;
        _clock.Restart();
        if (PlayToggle.IsChecked != true) _clock.Stop();
        ShowTick();
    }

    private void DeleteKey_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentEdit() is not var (keep, lengths) || KeyList.SelectedIndex < 0 || keep.Count <= 1) return;
        int i = KeyList.SelectedIndex;
        keep.RemoveAt(i);
        lengths.RemoveAt(i);
        Commit(keep, lengths, i);
    }

    private void ChangeLength(int delta)
    {
        if (CurrentEdit() is not var (keep, lengths) || KeyList.SelectedIndex < 0) return;
        int i = KeyList.SelectedIndex;
        lengths[i] = Math.Max(1, lengths[i] + delta);
        Commit(keep, lengths, i);
    }

    private void Shorter_Click(object sender, RoutedEventArgs e) => ChangeLength(-1);
    private void Longer_Click(object sender, RoutedEventArgs e) => ChangeLength(+1);

    private void Scale(double factor)
    {
        if (CurrentEdit() is not var (keep, lengths)) return;
        for (int i = 0; i < lengths.Count; i++) lengths[i] = Math.Max(1, (int)Math.Round(lengths[i] * factor));
        Commit(keep, lengths, KeyList.SelectedIndex);
    }

    private void Faster_Click(object sender, RoutedEventArgs e) => Scale(0.8);
    private void Slower_Click(object sender, RoutedEventArgs e) => Scale(1.25);

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null || !_original.Clips.TryGetValue(_current.Id, out var orig)) return;
        var keys = orig.Keys.OrderBy(k => k.Start).ToList();
        Commit([.. Enumerable.Range(0, keys.Count)], [.. keys.Select(k => Math.Max(1, k.Length))], KeyList.SelectedIndex);
    }

    private void PlayToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        if (PlayToggle.IsChecked == true) { _clock.Start(); _timer.Start(); }
        else { _clock.Stop(); _timer.Stop(); }
    }

    private void MirrorToggle_Changed(object sender, RoutedEventArgs e) => ShowTick();

    private void ShowTick()
    {
        if (_current == null) return;
        int tick = _tickOffset + (int)(_clock.Elapsed.TotalSeconds * SpeedSlider.Value);
        var key = _current.KeyAt(tick, loop: true);
        // 재생 중이면 지금 보이는 컷의 줄을 따라 고른다 — 몇 번째 장·몇 틱인지 바로 보이게.
        if (key is { } shown && PlayToggle.IsChecked == true)
        {
            int index = -1;
            for (int i = 0; i < _current.Keys.Count; i++) if (_current.Keys[i].Start <= shown.Start) index = i;
            if (index >= 0 && KeyList.SelectedIndex != index)
            {
                _syncingKey = true;
                KeyList.SelectedIndex = index;
                KeyList.ScrollIntoView(KeyList.SelectedItem);
                _syncingKey = false;
            }
        }
        if (key is not { } k || !_frames.TryGetValue((k.SubentryId, k.Slot), out var bmp))
        {
            FrameImage.Source = null;
            TickInfo.Text = key == null ? "그림 키가 없는 모션" : "그 장의 그림을 못 풀었습니다";
            return;
        }

        if (MirrorToggle.IsChecked == true)
        {
            if (!_mirrored.TryGetValue((k.SubentryId, k.Slot), out var m))
            {
                m = new TransformedBitmap(bmp, new ScaleTransform(-1, 1));
                m.Freeze();
                _mirrored[(k.SubentryId, k.Slot)] = m;
            }
            bmp = m;
        }
        FrameImage.Source = bmp;
        int length = Math.Max(1, _current.Length);
        TickInfo.Text = $"틱 {tick % length} / {length}   몸짓벌 {k.SubentryId} 장 {k.Slot}" + SizeText(k);
    }

    /// <summary>그 장의 그림 크기 · 기준점과, 이 모션의 장들을 기준점에 맞춰 겹친 크기(사용자 요청 ed-11).</summary>
    private string SizeText(MotionKey k)
    {
        if (!_raw.TryGetValue((k.SubentryId, k.Slot), out var f)) return "";
        string text = $"   크기 {f.Width}×{f.Height} (기준점에서 {f.X},{f.Y})";
        var all = _current!.Keys.Select(x => _raw.GetValueOrDefault((x.SubentryId, x.Slot))).OfType<ObsFrame>().ToList();
        if (all.Count > 1)
        {
            int l = all.Min(a => a.X), t = all.Min(a => a.Y), r = all.Max(a => a.X + a.Width), b = all.Max(a => a.Y + a.Height);
            text += $"   모션 전체 {r - l}×{b - t}";
        }
        return text;
    }

    // ── PNG 내보내기(사용자 요청 ed-6) ─────────────────────────────────────

    /// <summary>
    /// 모션 하나의 그림 키들을 왼쪽부터 한 칸씩 늘어놓은 PNG — 칸은 모든 장이 들어가는 같은 크기이고, 장마다 원점 기준 자리(<see cref="ObsFrame.X"/> · Y)에 놓아
    /// 칸끼리 원점이 같다(그대로 이어 보면 게임에서처럼 움직인다). 바탕은 투명. 그림 키가 없거나 장을 못 풀었으면 null.
    /// </summary>
    private BitmapSource? StripPng(ObsMotionClip clip, bool mirror) => SheetPng([clip], mirror);

    /// <summary>모션의 장들(그림 키 차례) — 못 푼 장은 뺀다.</summary>
    private List<ObsFrame> ClipFrames(ObsMotionClip clip) =>
        [.. clip.Keys.Select(k => _raw.GetValueOrDefault((k.SubentryId, k.Slot))).OfType<ObsFrame>()];

    /// <summary>
    /// 여러 모션을 한 장에 — 모션마다 한 줄, 그 모션의 장은 왼쪽부터 한 칸씩. 칸은 시트 안의 모든 장이 들어가는 같은 크기이고 원점도 같다.
    /// 줄이 짧은 모션은 오른쪽이 빈다(투명). 그림 있는 모션이 하나도 없으면 null.
    /// </summary>
    private BitmapSource? SheetPng(IReadOnlyList<ObsMotionClip> clips, bool mirror)
    {
        var rows = clips.Select(ClipFrames).Where(r => r.Count > 0).ToList();
        if (rows.Count == 0) return null;
        var all = rows.SelectMany(r => r).ToList();
        int left = all.Min(f => f.X), top = all.Min(f => f.Y);
        int cellW = all.Max(f => f.X + f.Width) - left, cellH = all.Max(f => f.Y + f.Height) - top;
        int width = cellW * rows.Max(r => r.Count), height = cellH * rows.Count;
        var px = new byte[width * height * 4];
        for (int row = 0; row < rows.Count; row++)
            for (int n = 0; n < rows[row].Count; n++)
            {
                var f = rows[row][n];
                for (int y = 0; y < f.Height; y++)
                    for (int x = 0; x < f.Width; x++)
                    {
                        int dx = f.X - left + x;
                        if (mirror) dx = cellW - 1 - dx;
                        Buffer.BlockCopy(f.Bgra, (y * f.Width + x) * 4, px, ((row * cellH + f.Y - top + y) * width + n * cellW + dx) * 4, 4);
                    }
            }
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, px, width * 4);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// 「모두 PNG로」 의 묶음 — 동작 번호로 나눈다(분석-모션): 0 서기 · 1 걷기 · 2 맞음(죽음도 같은 동작) · 5 준비 · 8 베기 · 9 쏘기 · 24 복귀 = 공격,
    /// 6 · 7 · 13 · 14 · 15 · 26 · 27(시전 · 기 모으기 · 연속 베기 · 필살기) = 기술, 그 밖은 기타(사용자 요청 ed-9: 모션마다 파일 하나면 너무 많다).
    /// </summary>
    private static readonly (string Name, int[] Actions)[] SheetGroups =
    [
        ("서기", [0]), ("이동", [1]), ("맞음", [2]), ("공격", [5, 8, 9, 24]), ("기술", [6, 7, 13, 14, 15, 26, 27]),
    ];

    private static string SheetGroup(int action) => SheetGroups.FirstOrDefault(g => g.Actions.Contains(action)).Name ?? "기타";

    private string SafeName(string name) => string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));

    /// <summary>파일 이름 — 「에바_서기_뒷모습_모션00.png」 꼴(이름 없는 동작은 「동작7」).</summary>
    private string PngName(ObsMotionClip clip)
    {
        string action = ActionNames.GetValueOrDefault(clip.Id / 3, "") is { Length: > 0 } n ? n.Replace("?", "") : $"동작{clip.Id / 3}";
        string name = $"{_title}_{action}_{DirectionNames[clip.Id % 3]}_모션{clip.Id:D2}.png";
        return SafeName(name);
    }

    private static void SavePng(BitmapSource bmp, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    private void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        bool mirror = MirrorToggle.IsChecked == true;
        if (StripPng(_current, mirror) is not { } bmp) { EditState.Text = "그림 키가 없는 모션입니다"; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = PngName(_current), Filter = "PNG 그림|*.png", DefaultExt = ".png" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            SavePng(bmp, dialog.FileName);
            EditState.Text = $"내보냄: {System.IO.Path.GetFileName(dialog.FileName)} ({_current.Keys.Count}장, {bmp.PixelWidth}×{bmp.PixelHeight}{(mirror ? ", 뒤집음" : "")})";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { EditState.Text = $"못 썼습니다: {ex.Message}"; }
    }

    private void ExportAllPng_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "모션 PNG 를 넣을 폴더" };
        if (dialog.ShowDialog(this) != true) return;
        bool mirror = MirrorToggle.IsChecked == true;
        // 묶음마다 PNG 한 장(모션마다 한 줄) + 어느 줄이 어느 모션인지 적은 목록 하나.
        var index = new List<string> { $"{_title} 모션 시트 — 줄마다 모션 하나, 칸마다 장 하나(같은 칸 크기 · 같은 원점, 투명 바탕){(mirror ? ", 좌우 뒤집음" : "")}" };
        int written = 0;
        try
        {
            var groups = _table.Clips.Values.Where(c => ClipFrames(c).Count > 0).OrderBy(c => c.Id).GroupBy(c => SheetGroup(c.Id / 3))
                .OrderBy(g => Array.FindIndex(SheetGroups, s => s.Name == g.Key) is var i && i >= 0 ? i : SheetGroups.Length);
            foreach (var group in groups)
            {
                var clips = group.ToList();
                if (SheetPng(clips, mirror) is not { } bmp) continue;
                string file = SafeName($"{_title}_{group.Key}.png");
                SavePng(bmp, System.IO.Path.Combine(dialog.FolderName, file));
                written++;
                int cellW = bmp.PixelWidth / clips.Max(c => ClipFrames(c).Count), cellH = bmp.PixelHeight / clips.Count;
                index.Add("");
                index.Add($"{file} — {clips.Count}줄, 칸 {cellW}×{cellH}");
                for (int row = 0; row < clips.Count; row++)
                    index.Add($"  줄 {row}: {ClipLabel(clips[row])}");
            }
            if (written > 0) System.IO.File.WriteAllLines(System.IO.Path.Combine(dialog.FolderName, SafeName($"{_title}_목록.txt")), index);
            EditState.Text = $"시트 {written}장({string.Join(" · ", groups.Select(g => $"{g.Key} {g.Count()}"))})과 목록을 내보냈습니다{(mirror ? "(뒤집음)" : "")}";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { EditState.Text = $"못 썼습니다({written}장까지): {ex.Message}"; }
    }
}

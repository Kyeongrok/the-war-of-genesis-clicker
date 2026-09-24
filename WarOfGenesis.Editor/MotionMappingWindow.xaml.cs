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
        [0] = "서기", [1] = "걷기", [2] = "맞음?", [5] = "공격 준비?", [6] = "시전 자세?", [7] = "기 모으기?",
        [8] = "베기?", [13] = "연속 베기?", [14] = "연속 베기2?", [15] = "시전 발동?", [24] = "공격 후 복귀?",
        [26] = "필살기?", [27] = "필살기2?",
    };

    private static readonly string[] DirectionNames = ["뒷모습", "옆모습", "앞모습"];

    private readonly ObsMotionTable _table;

    /// <summary>손질을 안 입힌 원본 모션표 — 손질은 늘 이것에서 다시 만든다.</summary>
    private readonly ObsMotionTable _original;

    /// <summary>이 몸짓 파일 번호 — 손질을 이 번호로 적는다(<see cref="MotionEdits"/>).</summary>
    private readonly int _obs;
    private readonly Dictionary<(int Sub, int Slot), BitmapSource> _frames = [];
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
        Title = $"모션 매핑 — {title}";

        foreach (var sub in subentries)
            for (int i = 0; i < sub.Frames.Count; i++)
            {
                var f = sub.Frames[i];
                var bmp = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
                bmp.Freeze();
                // 모션표가 가리키는 것은 <b>장 번호</b>지 벌 안 순번이 아니다 — 못 푼 장이 하나라도 있으면 순번이 밀린다.
                _frames[(sub.Id, f.SlotId)] = bmp;
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
        return $"모션 {clip.Id,3} = 동작 {action,2} {name} · {DirectionNames[clip.Id % 3]} · {clip.Length}틱 · 그림 {clip.Keys.Count}{edited}";
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
                              (_frames.ContainsKey((k.SubentryId, k.Slot)) ? "" : "  (그림 없음)"));
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
        TickInfo.Text = $"틱 {tick % length} / {length}   몸짓벌 {k.SubentryId} 장 {k.Slot}";
    }
}

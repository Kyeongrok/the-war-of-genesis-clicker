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
    private readonly Dictionary<(int Sub, int Slot), BitmapSource> _frames = [];
    private readonly Dictionary<(int Sub, int Slot), BitmapSource> _mirrored = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Stopwatch _clock = new();
    private ObsMotionClip? _current;

    private sealed class ClipItem(ObsMotionClip clip, string label)
    {
        public ObsMotionClip Clip { get; } = clip;
        public override string ToString() => label;
    }

    public MotionMappingWindow(string title, ObsMotionTable table, IReadOnlyList<ObsMotion> subentries)
    {
        InitializeComponent();
        _table = table;
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
                          "모션 번호 = 동작 × 3 + 방향. 동작 이름에 ? 가 붙은 것은 추정.";

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
            MotionList.Items.Add(new ClipItem(clip,
                $"모션 {clip.Id,3} = 동작 {action,2} {name} · {DirectionNames[clip.Id % 3]} · {clip.Length}틱 · 그림 {clip.Keys.Count}"));
        }
        if (MotionList.Items.Count > 0) MotionList.SelectedIndex = 0;
    }

    private void MotionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _current = (MotionList.SelectedItem as ClipItem)?.Clip;
        if (_current == null) { FrameImage.Source = null; KeyList.Text = ""; return; }

        KeyList.Text = string.Join(Environment.NewLine, _current.Keys.Select((k, i) =>
            $"{i,3}: 틱 {k.Start,4} ~ {k.Start + k.Length - 1,4} ({k.Length,2}틱)  몸짓벌 {k.SubentryId}  장 {k.Slot,3}" +
            (_frames.ContainsKey((k.SubentryId, k.Slot)) ? "" : "  (그림 없음)")));
        _clock.Restart();
        if (PlayToggle.IsChecked == true) _timer.Start();
        ShowTick();
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
        int tick = (int)(_clock.Elapsed.TotalSeconds * SpeedSlider.Value);
        var key = _current.KeyAt(tick, loop: true);
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

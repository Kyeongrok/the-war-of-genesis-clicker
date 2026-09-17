using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 게임 소리를 듣는 창 — 배경음악(<c>BGM/*.bgm</c> 스테레오) · 효과음(<c>Snd/*.snd</c>) · 인물 대사(<c>BGM/*.bgm</c> 모노)를
/// 탭으로 나눠 목록으로 보이고, 고른 것을 풀어(Bink 는 <see cref="BinkAudio"/>, WAV 는 <see cref="WaveSound"/>) 튼다.
/// </summary>
/// <remarks>
/// 목록은 파일 머리만 읽어 뒤쪽에서 채운다(3454 + 800개). 소리는 재생을 누를 때 풀고 몇 개만 붙잡아 둔다
/// — 음악 한 곡이 풀면 20MB 남짓이다. 재생은 <see cref="WaveOutPlayer"/>(winmm) 로 한다.
/// </remarks>
public partial class SoundWindow : Window
{
    public sealed class Row(SoundEntry entry, VoiceLine? line)
    {
        public SoundEntry Entry { get; } = entry;
        public int Id => Entry.Id;
        public string FileName => Entry.FileName;
        public string Length => FormatTime(Entry.Duration.TotalSeconds, showTenths: true);
        public string Format => $"{Entry.SampleRate} Hz · {(Entry.Channels == 2 ? "스테레오" : "모노")}" +
                                (Entry.Kind == SoundKind.Effect ? $" · {Entry.BitsPerSample}비트" : "");
        public string SizeText => Entry.Size >= 1024 * 1024 ? $"{Entry.Size / 1048576.0:F1} MB" : $"{Entry.Size / 1024.0:F0} KB";
        public string Note => Entry.Note;
        public string Speaker => line?.Speaker ?? "";
        public string Source => line?.Source ?? "";
        public string Text => line?.Text ?? "";
    }

    public enum Page { Music, Effect, Voice }

    private readonly GameFiles _files;
    private readonly string _gameRoot;
    private readonly WaveOutPlayer _player = new();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, PcmSound> _decoded = [];   // 파일 이름 → 푼 소리(최근 몇 개만)
    private readonly List<string> _decodedOrder = [];

    private ICollectionView? _musicView, _effectView, _voiceView;
    private int _musicCount, _effectCount, _voiceCount;
    private Row? _current;
    private PcmSound? _currentSound;
    private bool _dragging;
    private int _loadToken;

    public SoundWindow(string gameRoot, Page page)
    {
        InitializeComponent();
        _gameRoot = gameRoot;
        _files = GameFiles.FromGameRoot(gameRoot);
        _player.Volume = (float)(VolumeSlider.Value / 100);
        _player.Ended += () => Dispatcher.BeginInvoke(UpdateTransport);
        ShowPage(page);

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => UpdateTransport(), Dispatcher);
        _timer.Start();
        Loaded += async (_, _) => await LoadListsAsync();
    }

    public void ShowPage(Page page) => Tabs.SelectedItem = page switch
    {
        Page.Music => MusicTab,
        Page.Effect => EffectTab,
        _ => VoiceTab,
    };

    // ── 목록 ────────────────────────────────────────────────────────────────

    private async Task LoadListsAsync()
    {
        StatusText.Text = "목록을 읽는 중…";
        try
        {
            var (music, voices, effects, lines) = await Task.Run(() =>
            {
                var (m, v) = SoundCatalog.ScanBgm(_gameRoot);
                var e = SoundCatalog.ScanSnd(_files);
                IReadOnlyDictionary<int, VoiceLine> l;
                try { l = VoiceLines.Load(_files); }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { l = new Dictionary<int, VoiceLine>(); }
                return (m, v, e, l);
            });

            var musicRows = music.Select(e => new Row(e, lines.GetValueOrDefault(e.Id))).ToList();
            var effectRows = effects.Select(e => new Row(e, null)).ToList();
            var voiceRows = voices.Select(e => new Row(e, lines.GetValueOrDefault(e.Id))).ToList();
            (_musicCount, _effectCount, _voiceCount) = (musicRows.Count, effectRows.Count, voiceRows.Count);

            MusicGrid.ItemsSource = musicRows;
            EffectGrid.ItemsSource = effectRows;
            VoiceGrid.ItemsSource = voiceRows;
            _musicView = CollectionViewSource.GetDefaultView(musicRows);
            _effectView = CollectionViewSource.GetDefaultView(effectRows);
            _voiceView = CollectionViewSource.GetDefaultView(voiceRows);
            foreach (var view in new[] { _musicView, _effectView, _voiceView }) view.Filter = Accept;

            int matched = voiceRows.Count(r => r.Text.Length > 0);
            VoiceHelpText.Text = lines.Count == 0
                ? "BGM\\NNNN.bgm 중 모노인 것(음성). 대사·말하는 이는 이 게임 폴더에서 찾지 못했다."
                : $"BGM\\NNNN.bgm 중 모노인 것(음성). 대사·말하는 이를 찾은 것 {matched}/{voiceRows.Count}개 — {VoiceLines.SourceNote}";
            if (musicRows.Count + effectRows.Count + voiceRows.Count == 0)
                StatusText.Text = $"'{_gameRoot}' 에서 BGM\\*.bgm · Snd\\*.snd 를 못 찾았습니다.";
            else
                UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            StatusText.Text = $"소리 목록을 읽지 못했습니다: {ex.Message}";
        }
    }

    private bool Accept(object o)
    {
        if (o is not Row r) return false;
        string q = FilterBox.Text.Trim();
        return q.Length == 0 || r.Id.ToString("D4").Contains(q) || r.Note.Contains(q, StringComparison.OrdinalIgnoreCase)
               || r.Speaker.Contains(q) || r.Text.Contains(q) || r.Source.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private (DataGrid Grid, ICollectionView? View, int Total, string Label) CurrentPage() => Tabs.SelectedItem switch
    {
        TabItem t when t == MusicTab => (MusicGrid, _musicView, _musicCount, "배경음악"),
        TabItem t when t == EffectTab => (EffectGrid, _effectView, _effectCount, "효과음"),
        _ => (VoiceGrid, _voiceView, _voiceCount, "인물 대사"),
    };

    private void UpdateStatus()
    {
        var (_, view, total, label) = CurrentPage();
        if (view == null) return;
        int shown = view.Cast<object>().Count();
        StatusText.Text = shown == total ? $"{label} {total}개" : $"{label} {shown}/{total}개 보임";
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _musicView?.Refresh();
        _effectView?.Refresh();
        _voiceView?.Refresh();
        UpdateStatus();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != Tabs) return;   // 안쪽 DataGrid 의 선택 이벤트가 올라온 것은 무시
        if (LoopToggle != null && Tabs.SelectedItem == MusicTab) LoopToggle.IsChecked = true;
        if (StatusText != null) UpdateStatus();
    }

    // ── 재생 ────────────────────────────────────────────────────────────────

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 머리줄·스크롤 막대를 두 번 누른 것은 무시한다 — 줄 위에서 누른 것만 튼다.
        if (sender is DataGrid grid && ItemsControl.ContainerFromElement(grid, (DependencyObject)e.OriginalSource) is DataGridRow { Item: Row row })
            _ = PlayRowAsync(row, 0);
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        var (grid, _, _, _) = CurrentPage();
        if (grid.SelectedItem is Row row && row != _current) { _ = PlayRowAsync(row, 0); return; }
        if (_current == null) { StatusText.Text = "먼저 목록에서 파일을 고르세요."; return; }
        if (_player.IsPlaying) return;
        _ = PlayRowAsync(_current, _player.PositionSeconds);
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _player.Stop();   // 멈춘 자리를 기억한다 — 재생을 누르면 이어서 튼다
        UpdateTransport();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _player.Stop();
        if (_currentSound != null) _player.Seek(_currentSound, 0);
        UpdateTransport();
    }

    private void LoopToggle_Changed(object sender, RoutedEventArgs e) => _player.Loop = LoopToggle.IsChecked == true;

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeText == null) return;
        _player.Volume = (float)(e.NewValue / 100);
        VolumeText.Text = $"{e.NewValue:F0}%";
    }

    private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _dragging = true;

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        // IsMoveToPointEnabled 로 값이 먼저 바뀐 뒤에 옮긴다.
        Dispatcher.BeginInvoke(() =>
        {
            if (_currentSound == null) return;
            try { _player.Seek(_currentSound, PositionSlider.Value); }
            catch (InvalidOperationException ex) { StatusText.Text = ex.Message; }
            UpdateTransport();
        }, DispatcherPriority.Input);
    }

    private async Task PlayRowAsync(Row row, double startSeconds)
    {
        int token = ++_loadToken;
        _player.Stop();
        NowPlayingText.Text = $"{row.Entry.Folder}\\{row.FileName} — 푸는 중…";
        PcmSound sound;
        try
        {
            if (!_decoded.TryGetValue(row.FileName, out sound!))
            {
                sound = await Task.Run(() => SoundCatalog.Load(_files, row.Entry));
                Remember(row.FileName, sound);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            if (token == _loadToken) NowPlayingText.Text = $"{row.Entry.Folder}\\{row.FileName} 을(를) 풀지 못했습니다: {ex.Message}";
            return;
        }
        if (token != _loadToken) return;   // 푸는 동안 다른 것을 골랐다

        _current = row;
        _currentSound = sound;
        PositionSlider.Maximum = Math.Max(0.001, sound.Duration.TotalSeconds);
        string who = row.Speaker.Length > 0 ? $" · {row.Speaker}: {row.Text}" : row.Note.Length > 0 ? $" · {row.Note}" : "";
        NowPlayingText.Text = $"{row.Entry.Folder}\\{row.FileName} ({FormatTime(sound.Duration.TotalSeconds, true)}, {sound.SampleRate} Hz, " +
                              $"{(sound.Channels == 2 ? "스테레오" : "모노")}){who}";
        try
        {
            _player.Loop = LoopToggle.IsChecked == true;
            _player.Play(sound, startSeconds >= sound.Duration.TotalSeconds ? 0 : startSeconds);
        }
        catch (InvalidOperationException ex)
        {
            StatusText.Text = ex.Message;
        }
        UpdateTransport();
    }

    private void Remember(string name, PcmSound sound)
    {
        _decoded[name] = sound;
        _decodedOrder.Remove(name);
        _decodedOrder.Add(name);
        while (_decodedOrder.Count > 8)
        {
            _decoded.Remove(_decodedOrder[0]);
            _decodedOrder.RemoveAt(0);
        }
    }

    private void UpdateTransport()
    {
        double total = _currentSound?.Duration.TotalSeconds ?? 0;
        double pos = _currentSound == null ? 0 : _player.PositionSeconds;
        if (!_dragging) PositionSlider.Value = Math.Min(pos, PositionSlider.Maximum);
        TimeText.Text = $"{FormatTime(_dragging ? PositionSlider.Value : pos, false)} / {FormatTime(total, false)}";
        PlayButton.IsEnabled = !_player.IsPlaying || (CurrentPage().Grid.SelectedItem is Row r && r != _current);
        PauseButton.IsEnabled = _player.IsPlaying;
    }

    private static string FormatTime(double seconds, bool showTenths)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return showTenths ? $"{(int)t.TotalMinutes}:{t.Seconds:D2}.{t.Milliseconds / 100}" : $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _player.Dispose();
    }
}

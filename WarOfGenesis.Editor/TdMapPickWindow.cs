using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 타워 디펜스 스테이지의 맵 고르기 창(사용자 요청 td-11) — 파트1 Obt(<c>part1:NNNN</c>)와 파트2 Obt(<c>assets/maps/NNNN.obt</c>)를 작은 그림으로 늘어놓고,
/// 눌러 고르면(두 번 누르거나 「고르기」) <see cref="Picked"/> 에 이름을 담고 닫는다. 그림은 뒤에서 하나씩 읽어 채운다.
/// </summary>
public sealed class TdMapPickWindow : Window
{
    public sealed class Entry
    {
        public string Name { get; init; } = "";
        public string Label { get; set; } = "";
        public ImageSource? Thumb { get; set; }
    }

    private const int ThumbW = 220;

    public string? Picked { get; private set; }

    private readonly List<Entry> _all = [];
    private readonly ListBox _list = new();
    private readonly TextBox _filter = new() { Width = 160, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _part = new() { Width = 90, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };
    private readonly CancellationTokenSource _stop = new();

    public TdMapPickWindow(GameFiles files, string current)
    {
        Title = "맵 고르기 — 타워 디펜스";
        Width = 1180;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _part.ItemsSource = new[] { "모두", "파트1", "파트2" };
        _part.SelectedIndex = 0;
        _part.SelectionChanged += (_, _) => ApplyFilter();
        _filter.TextChanged += (_, _) => ApplyFilter();

        var factory = new FrameworkElementFactory(typeof(WrapPanel));
        factory.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
        _list.ItemsPanel = new ItemsPanelTemplate(factory);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.ItemTemplate = BuildTemplate();
        _list.MouseDoubleClick += (_, _) => Choose();

        var ok = new Button { Content = "고르기", Padding = new Thickness(14, 3, 14, 3), Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        ok.Click += (_, _) => Choose();
        var cancel = new Button { Content = "닫기", Padding = new Thickness(14, 3, 14, 3), IsCancel = true };

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(new TextBlock { Text = "찾기", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        top.Children.Add(_filter);
        top.Children.Add(_part);
        top.Children.Add(_status);
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);
        var root = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(_list);
        Content = root;

        // 목록 — 파트1 은 번호를 돌며 있는 것만(낱장 · 묶음 모두), 파트2 는 assets/data/Obt
        var names = new List<string>();
        if (Part1Game.Root != null)
            for (int id = 0; id < 1000; id++)
                if (Part1Game.Read("Obts", $"{id:D4}.obt") is { Length: > 0 }) names.Add($"part1:{id:D4}");
        try
        {
            string maps = AssetsFolder.Find("maps");
            if (Directory.Exists(maps)) names.AddRange(Directory.EnumerateFiles(maps, "*.obt").Select(f => Path.GetFileName(f)!).Order(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
        foreach (var n in names) _all.Add(new Entry { Name = n, Label = n });
        ApplyFilter();
        if (_all.FirstOrDefault(e => e.Name == current) is { } cur) { _list.SelectedItem = cur; _list.ScrollIntoView(cur); }
        _status.Text = Part1Game.Root == null ? $"맵 {_all.Count}장 — 파트1 폴더가 없어 파트2 맵만" : $"맵 {_all.Count}장";

        Closed += (_, _) => _stop.Cancel();
        _ = Task.Run(() => LoadThumbs(files, _stop.Token));
    }

    private static DataTemplate BuildTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(MarginProperty, new Thickness(4));
        panel.SetValue(WidthProperty, (double)ThumbW);
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(Entry.Thumb)));
        image.SetValue(HeightProperty, 150.0);
        image.SetValue(Image.StretchProperty, Stretch.Uniform);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Entry.Label)));
        text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        panel.AppendChild(image);
        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    private void ApplyFilter()
    {
        string f = _filter.Text.Trim();
        int part = _part.SelectedIndex;
        _list.ItemsSource = _all.Where(e => (part == 0 || (part == 1) == e.Name.StartsWith("part1:", StringComparison.Ordinal))
                                            && (f.Length == 0 || e.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>맵을 하나씩 읽어 작게 줄인 그림과 크기를 채운다(열 장마다 한 번 그린다).</summary>
    private void LoadThumbs(GameFiles files, CancellationToken stop)
    {
        int done = 0;
        foreach (var e in _all.ToList())
        {
            if (stop.IsCancellationRequested) return;
            if (TowerDefenseEditWindow.ReadMap(files, e.Name) is not { Width: > 0 } m) { e.Label = $"{e.Name} (못 읽음)"; continue; }
            int step = Math.Max(1, (int)Math.Ceiling(m.Width / (double)ThumbW));
            int w = m.Width / step, h = m.Height / step;
            var px = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Buffer.BlockCopy(m.Bgra, ((y * step) * m.Width + x * step) * 4, px, (y * w + x) * 4, 4);
            var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
            bmp.Freeze();
            e.Thumb = bmp;
            e.Label = $"{e.Name}  {m.Width}×{m.Height}";
            if (++done % 10 == 0) Dispatcher.BeginInvoke(() => _list.Items.Refresh());
        }
        Dispatcher.BeginInvoke(() => _list.Items.Refresh());
    }

    private void Choose()
    {
        if (_list.SelectedItem is not Entry e) return;
        Picked = e.Name;
        DialogResult = true;
    }
}

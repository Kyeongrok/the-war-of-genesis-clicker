using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarOfGenesis.Assets;
using Line = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace WarOfGenesis.Editor;

/// <summary>
/// <see cref="BattleDemoScene"/>(Btl 0045, 코어헌터 훈련장)를 원본 전투 맵(<c>Obt</c>) 위 32×34 칸 판에 그려 보는 창.
/// </summary>
/// <remarks>
/// duel-dx 데모의 D3D 렌더링을 WPF <see cref="Canvas"/> 로 옮긴 것 — 게임 폴더 없이도, 저장소에
/// 내보내 둔 <c>assets/characters</c>·<c>assets/maps</c> 만으로 열어 볼 수 있다. 배치
/// 자료는 duel-dx 와 이 창이 <see cref="BattleDemoScene"/> 하나를 함께 쓴다 — 둘 다 고칠 필요
/// 없이 한 군데서만 고치면 된다.
/// </remarks>
public partial class BattleMapWindow : Window
{
    private const int TileW = ObtMap.CellWidth, TileH = ObtMap.CellHeight;

    public BattleMapWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => StartLoadingScene();
    }

    private void StartLoadingScene()
    {
        StatusText.Text = "전투 자료를 읽는 중...";
        System.Threading.Tasks.Task.Run(LoadScene);
    }

    /// <summary>배경 스레드에서 배경 그림·캐릭터 그림을 읽는다. 창은 먼저 뜬다.</summary>
    private void LoadScene()
    {
        BitmapSource? background = null;
        var sprites = new Dictionary<int, BitmapSource>();
        string loadError = "";

        try
        {
            background = LoadBackground();

            string charactersRoot = FindAssetsRoot("characters");
            var manifests = CollectExportedManifests(charactersRoot);

            foreach (int chrCode in BattleDemoScene.Roster.Select(u => u.ChrCode).Distinct())
            {
                if (!manifests.TryGetValue(chrCode, out var manifest)) continue;

                string obsPath = Path.Combine(charactersRoot,
                    CharacterExport.FolderNameFor(chrCode, manifest.Name),
                    CharacterExport.ObsFileName(manifest.SpriteCode));

                var frame = ObsSprite.DecodeFirstFrame(obsPath);
                if (frame == null) continue;

                var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32,
                                                  null, frame.Bgra, frame.Width * 4);
                bitmap.Freeze();
                sprites[chrCode] = bitmap;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or DirectoryNotFoundException)
        {
            loadError = ex.Message;
        }

        Dispatcher.BeginInvoke(() => ShowScene(background, sprites, loadError));
    }

    private void ShowScene(BitmapSource? background, Dictionary<int, BitmapSource> sprites, string loadError)
    {
        Board.Children.Clear();

        if (background != null)
        {
            var image = new Image
            {
                Source = background,
                Width = background.PixelWidth,
                Height = background.PixelHeight,
            };
            Canvas.SetLeft(image, 0);
            Canvas.SetTop(image, _mapOriginY);
            Board.Children.Add(image);
        }

        DrawGridLines();
        DrawUnits(sprites);

        int allies = BattleDemoScene.Roster.Count(u => u.IsAlly);
        int enemies = BattleDemoScene.Roster.Count(u => !u.IsAlly);
        StatusText.Text =
            $"{BattleDemoScene.Title} — 전투 Btl {BattleDemoScene.BtlId:D4}   아군 {allies}   적군 {enemies}   " +
            $"배경: Obt {Path.GetFileNameWithoutExtension(BattleDemoScene.MapFile)}\n" +
            "파란 테두리 = 아군, 빨간 테두리 = 적군";
        if (loadError.Length > 0) StatusText.Text += $"\n못 읽은 자료가 있습니다: {loadError}";
    }

    private void DrawGridLines()
    {
        double width = BattleDemoScene.Cols * TileW, height = BattleDemoScene.Rows * TileH;
        var brush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
        brush.Freeze();

        for (int c = 0; c <= BattleDemoScene.Cols; c++)
        {
            double x = c * TileW;
            Board.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = height, Stroke = brush, StrokeThickness = 1 });
        }
        for (int r = 0; r <= BattleDemoScene.Rows; r++)
        {
            double y = r * TileH;
            Board.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = brush, StrokeThickness = 1 });
        }
    }

    private void DrawUnits(Dictionary<int, BitmapSource> sprites)
    {
        foreach (var unit in BattleDemoScene.Roster)
        {
            double tileX = unit.Col * TileW, tileY = unit.Row * TileH;
            Color color = unit.IsAlly ? Colors.DeepSkyBlue : Colors.OrangeRed;
            var stroke = new SolidColorBrush(color);
            stroke.Freeze();
            var fill = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B));
            fill.Freeze();

            var box = new Rectangle { Width = TileW, Height = TileH, Stroke = stroke, StrokeThickness = 2, Fill = fill };
            Canvas.SetLeft(box, tileX);
            Canvas.SetTop(box, tileY);
            Board.Children.Add(box);

            if (!sprites.TryGetValue(unit.ChrCode, out var sprite)) continue;

            var image = new Image
            {
                Source = sprite,
                Width = sprite.PixelWidth,
                Height = sprite.PixelHeight,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(image, tileX + TileW / 2.0 - sprite.PixelWidth / 2.0);
            Canvas.SetTop(image, tileY + TileH - sprite.PixelHeight);
            Canvas.SetZIndex(image, 10);
            Board.Children.Add(image);
        }
    }

    private double _mapOriginY;

    /// <summary><c>assets/maps/</c> 의 전투 맵(<c>.obt</c>)을 풀어 그림으로 만든다.</summary>
    private BitmapSource LoadBackground()
    {
        var map = ObtMap.Load(Path.Combine(FindAssetsRoot("maps"), BattleDemoScene.MapFile));
        _mapOriginY = map.OriginY;
        var bitmap = BitmapSource.Create(map.Width, map.Height, 96, 96, PixelFormats.Bgra32, null, map.Bgra, map.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary><c>assets/characters/</c> 밑의 인물들을 전부 훑어 Chr 코드별로 모은다.</summary>
    private static Dictionary<int, ExportedCharacter> CollectExportedManifests(string charactersRoot)
    {
        var result = new Dictionary<int, ExportedCharacter>();
        if (!Directory.Exists(charactersRoot)) return result;

        foreach (var folder in Directory.EnumerateDirectories(charactersRoot))
        {
            var manifest = CharacterExport.LoadManifest(folder);
            if (manifest != null) result[manifest.ChrCode] = manifest;
        }
        return result;
    }

    private static string FindAssetsRoot(string subFolder) => AssetsFolder.Find(subFolder);
}

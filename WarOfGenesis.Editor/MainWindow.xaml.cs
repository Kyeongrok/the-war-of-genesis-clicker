using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 창세기전3 파트2의 <c>Chr</c>/<c>Obs</c> 자료로 전투 모션을 <b>한 컷씩</b> 넘겨 보는 창.
/// cds-helper 의 「모션 메이커」류 헬퍼 화면과 같은 자리 — 이름으로 인물을 찾아 그 몸짓
/// 벌을 고르면 오른쪽에서 이전·다음 단추(또는 슬라이더)로 프레임을 한 장씩 넘긴다.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WarOfGenesis.Editor", "gameroot.txt");

    private const string DefaultGameRoot = @"C:\Users\Administrator\Downloads\gen3pt2";

    private TxrTable? _txr;
    private string _chrFolder = "", _obsFolder = "";
    private List<ChrRecord> _allChrRecords = [];

    private sealed class MotionItem(string label, ObsMotion motion)
    {
        public ObsMotion Motion { get; } = motion;
        public override string ToString() => label;
    }

    private ChrRecord? _currentRecord;
    private string _currentName = "";
    private List<ObsMotion> _currentSpriteMotions = [];
    private List<ObsMotion> _currentFaceMotions = [];

    private List<ObsFrame> _currentFrames = [];
    private int _currentIndex;
    private bool _suppressSliderEvent;
    private DispatcherTimer? _playTimer;

    public MainWindow()
    {
        InitializeComponent();
        GameRootBox.Text = LoadSavedGameRoot() ?? DefaultGameRoot;
        Loaded += (_, _) => TryLoadGameRoot(GameRootBox.Text, announce: false);
    }

    // ── 게임 폴더 ────────────────────────────────────────────────────────────

    private static string? LoadSavedGameRoot()
    {
        try { return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : null; }
        catch (IOException) { return null; }
    }

    private static void SaveGameRoot(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, path);
        }
        catch (IOException) { /* 저장 못 해도 이번 판 쓰는 데는 지장 없다 */ }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "창세기전3 파트2 폴더를 고르세요 (Chr·Obs·TXR 이 든 곳)",
            InitialDirectory = Directory.Exists(GameRootBox.Text) ? GameRootBox.Text : "",
        };
        if (dialog.ShowDialog(this) == true) GameRootBox.Text = dialog.FolderName;
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e) => TryLoadGameRoot(GameRootBox.Text, announce: true);

    private void TryLoadGameRoot(string root, bool announce)
    {
        string txrPath = Path.Combine(root, "TXR", "Txr.dat");
        string chrFolder = Path.Combine(root, "Chr");
        string obsFolder = Path.Combine(root, "Obs");

        if (!File.Exists(txrPath) || !Directory.Exists(chrFolder) || !Directory.Exists(obsFolder))
        {
            StatusText.Text = $"'{root}' 에서 TXR\\Txr.dat · Chr · Obs 를 못 찾았습니다.";
            return;
        }

        try
        {
            _txr = TxrTable.Open(txrPath);
            _chrFolder = chrFolder;
            _obsFolder = obsFolder;

            // Chr.pak 만 있고 낱장이 없으면 통째로 푼다 — Chr 은 578개뿐이라 금방 끝난다.
            if (!Directory.EnumerateFiles(chrFolder, "*.chr").Any() && File.Exists(Path.Combine(chrFolder, "Chr.idx")))
                PakArchive.Extract(chrFolder, "Chr");

            _allChrRecords = ChrTable.Scan(chrFolder);
            SaveGameRoot(root);
            StatusText.Text = $"열었습니다 — 이름 글 {_txr.Rows.Count}줄, 인물 레코드 {_allChrRecords.Count}개. " +
                              "왼쪽에 번호대로 다 늘어놓았습니다 — 이름을 몰라도 그냥 골라 보세요.";
            ShowAllCharacters();
            if (announce) SearchBox.Focus();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            StatusText.Text = $"여는 중 문제가 생겼습니다: {ex.Message}";
        }
    }

    // ── 이름 검색 ────────────────────────────────────────────────────────────

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Search();
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e) => Search();

    /// <summary>이름을 몰라도 볼 수 있게, 처음 열었을 때 인물 레코드를 번호대로 다 늘어놓는다.</summary>
    private void ShowAllCharacters()
    {
        PopulateCharacterList(_allChrRecords.OrderBy(r => r.ChrCode));
        SearchStatus.Text = $"전체 {_allChrRecords.Count}개";
    }

    private void Search()
    {
        if (_txr == null) { StatusText.Text = "먼저 게임 폴더를 여세요."; return; }

        string query = SearchBox.Text.Trim();
        if (query.Length == 0) { ShowAllCharacters(); StatusText.Text = "왼쪽에서 인물을 고르세요."; return; }

        // 숫자만 쳤으면 Chr 파일 번호·몸짓 번호·초상 번호로도 찾아 준다 — 이름을 몰라도
        // 도록(록)이나 번호표만 보고 바로 찾아볼 수 있게.
        List<ChrRecord> matches;
        if (int.TryParse(query, out int number))
        {
            matches = _allChrRecords
                .Where(r => r.ChrCode == number || r.SpriteCode == number || r.FaceCode == number)
                .OrderBy(r => r.ChrCode)
                .ToList();
        }
        else
        {
            var codes = _txr.FindNameCodes(query);
            matches = _allChrRecords
                .Where(r => codes.Contains(r.NameCode) || codes.Contains(r.AltNameCode))
                .OrderBy(r => r.ChrCode)
                .ToList();
        }

        PopulateCharacterList(matches);
        SearchStatus.Text = $"{matches.Count}명 찾음";
        StatusText.Text = matches.Count == 0
            ? $"'{query}' 로는 못 찾았습니다."
            : "왼쪽에서 인물을 고르세요.";
    }

    private void PopulateCharacterList(IEnumerable<ChrRecord> records)
    {
        CharacterList.Items.Clear();
        MotionList.Items.Clear();
        ClearViewer();

        foreach (var r in records)
        {
            string name = _txr?.TextOf(r.NameCode) ?? "";
            if (name.Length == 0) name = _txr?.TextOf(r.AltNameCode) ?? "";
            CharacterList.Items.Add(new CharacterItem(r, name));
        }
    }

    private sealed class CharacterItem(ChrRecord record, string name)
    {
        public ChrRecord Record { get; } = record;
        public string Name { get; } = name;
        public override string ToString() =>
            $"{(Name.Length > 0 ? Name : "(이름 없음)")}  [{Record.File}]  sprite={Record.SpriteCode} face={Record.FaceCode} job={Record.JobCode}";
    }

    // ── 인물 → 몸짓 벌 ───────────────────────────────────────────────────────

    private void CharacterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        MotionList.Items.Clear();
        ClearViewer();
        _currentRecord = null;
        _currentSpriteMotions = [];
        _currentFaceMotions = [];

        if (CharacterList.SelectedItem is not CharacterItem item) return;

        var record = item.Record;
        _currentRecord = record;
        _currentName = item.Name;

        _currentSpriteMotions = LoadMotions(record.SpriteCode, "몸짓");
        _currentFaceMotions = record.FaceCode != 0 && record.FaceCode == record.SpriteCode
            ? _currentSpriteMotions
            : LoadMotions(record.FaceCode, "초상");

        foreach (var motion in _currentSpriteMotions)
            MotionList.Items.Add(new MotionItem($"몸짓 #{record.SpriteCode} — 몸짓 {motion.Id} ({motion.Frames.Count}컷)", motion));
        foreach (var motion in _currentFaceMotions)
            MotionList.Items.Add(new MotionItem($"초상 #{record.FaceCode} — 몸짓 {motion.Id} ({motion.Frames.Count}컷)", motion));

        StatusText.Text = MotionList.Items.Count == 0
            ? "이 인물의 그림 파일(Obs)을 못 찾았습니다."
            : "가운데서 몸짓을 고르면 오른쪽에서 한 컷씩 볼 수 있습니다. 「내보내기」로 이 인물의 그림을 파일로 뽑을 수 있습니다.";
    }

    private List<ObsMotion> LoadMotions(ushort code, string kind)
    {
        if (code == 0) return [];

        string obsName = $"{code:D4}.obs";
        if (!PakArchive.EnsureFile(_obsFolder, "Obs", obsName))
        {
            StatusText.Text = $"{kind} 그림({obsName})을 Obs00~03.pak 에서 못 찾았습니다.";
            return [];
        }

        try { return ObsSprite.Decode(Path.Combine(_obsFolder, obsName)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            StatusText.Text = $"{obsName} 을(를) 풀지 못했습니다: {ex.Message}";
            return [];
        }
    }

    // ── 내보내기 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 고른 인물의 몸짓·초상 그림을 전부 PNG + <c>character.json</c> 자리표로 뽑는다.
    /// </summary>
    /// <remarks>
    /// PNG 로 안 굽는다 — <c>&lt;ChrCode&gt;.chr</c> 와 <c>&lt;SpriteCode&gt;.obs</c>
    /// (초상 번호가 다르면 그것도)를 게임 폴더에서 <b>그대로 복사</b>한다. 그러면
    /// <see cref="ObsSprite"/>·<see cref="ChrTable"/> 를 한 글자도 안 고치고 이 폴더에도
    /// 그대로 쓸 수 있다. 한 번 뽑아서 저장소에 커밋해 두면, 원본 게임
    /// (<see cref="GameRootBox"/> 의 폴더)이 없는 컴퓨터에서도 그 인물을 다시 그릴 수
    /// 있다 — duel-dx 같은 데모가 게임 없이 돌아가게 하려는 것이다.
    /// </remarks>
    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRecord is not { } record) { StatusText.Text = "먼저 왼쪽에서 인물을 고르세요."; return; }

        string chrPath = Path.Combine(_chrFolder, CharacterExport.ChrFileName(record.ChrCode));
        if (!File.Exists(chrPath))
        {
            StatusText.Text = $"{Path.GetFileName(chrPath)} 을(를) 못 찾았습니다.";
            return;
        }

        string? spritePath = EnsureObsPath(record.SpriteCode);
        string? facePath = record.FaceCode == record.SpriteCode ? spritePath : EnsureObsPath(record.FaceCode);
        if (spritePath == null && facePath == null)
        {
            StatusText.Text = "내보낼 그림(Obs)이 없습니다.";
            return;
        }

        string assetsRoot = GuessAssetsRoot();
        Directory.CreateDirectory(assetsRoot);
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "내보낼 곳을 고르세요 (보통 저장소의 assets/characters)",
            InitialDirectory = assetsRoot,
        };
        if (dialog.ShowDialog(this) != true) return;

        string outDir = Path.Combine(dialog.FolderName, CharacterExport.FolderNameFor(record.ChrCode, _currentName));
        Directory.CreateDirectory(outDir);

        File.Copy(chrPath, Path.Combine(outDir, Path.GetFileName(chrPath)), overwrite: true);
        if (spritePath != null) File.Copy(spritePath, Path.Combine(outDir, Path.GetFileName(spritePath)), overwrite: true);
        if (facePath != null && facePath != spritePath)
            File.Copy(facePath, Path.Combine(outDir, Path.GetFileName(facePath)), overwrite: true);

        CharacterExport.SaveManifest(outDir, new ExportedCharacter(_currentName, record.ChrCode, record.SpriteCode, record.FaceCode));

        StatusText.Text = $"내보냈습니다: {outDir}";
    }

    /// <summary>그 번호의 <c>.obs</c> 를 게임 폴더에 갖춰(없으면 pak 에서 풀어) 그 자리를 준다.</summary>
    private string? EnsureObsPath(int code)
    {
        if (code == 0) return null;
        string name = CharacterExport.ObsFileName(code);
        return PakArchive.EnsureFile(_obsFolder, "Obs", name) ? Path.Combine(_obsFolder, name) : null;
    }

    /// <summary>저장소 뿌리를 거슬러 올라가 <c>assets/characters</c> 를 찍어 준다 — 못 찾으면 바탕화면.</summary>
    private static string GuessAssetsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "duel-dx")))
                return Path.Combine(dir.FullName, "assets", "characters");
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    // ── 몸짓 → 한 컷씩 보기 ──────────────────────────────────────────────────

    private void MotionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MotionList.SelectedItem is not MotionItem item) { ClearViewer(); return; }

        _currentFrames = [.. item.Motion.Frames];
        _currentIndex = 0;

        _suppressSliderEvent = true;
        FrameSlider.Maximum = Math.Max(0, _currentFrames.Count - 1);
        FrameSlider.Value = 0;
        _suppressSliderEvent = false;

        ShowFrame(0);
    }

    private void ShowFrame(int index)
    {
        if (_currentFrames.Count == 0) { ClearViewer(); return; }

        index = ((index % _currentFrames.Count) + _currentFrames.Count) % _currentFrames.Count;
        _currentIndex = index;
        var frame = _currentFrames[index];

        var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32,
                                         null, frame.Bgra, frame.Width * 4);
        bitmap.Freeze();
        FrameImage.Source = bitmap;

        FrameLabel.Text = $"{index + 1} / {_currentFrames.Count}";
        FrameInfo.Text = $"장(slot) {frame.SlotId}   {frame.Width}x{frame.Height}   자리 ({frame.X}, {frame.Y})";

        if (!_suppressSliderEvent)
        {
            _suppressSliderEvent = true;
            FrameSlider.Value = index;
            _suppressSliderEvent = false;
        }
    }

    private void ClearViewer()
    {
        _currentFrames = [];
        _currentIndex = 0;
        FrameImage.Source = null;
        FrameLabel.Text = "0 / 0";
        FrameInfo.Text = "";
        FrameSlider.Maximum = 0;
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => ShowFrame(_currentIndex - 1);

    private void NextButton_Click(object sender, RoutedEventArgs e) => ShowFrame(_currentIndex + 1);

    private void FrameSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSliderEvent) return;
        ShowFrame((int)Math.Round(e.NewValue));
    }

    // ── 자동 재생 ────────────────────────────────────────────────────────────

    private void PlayToggle_Checked(object sender, RoutedEventArgs e)
    {
        _playTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _playTimer.Tick -= PlayTick;
        _playTimer.Tick += PlayTick;
        _playTimer.Start();
    }

    private void PlayToggle_Unchecked(object sender, RoutedEventArgs e) => _playTimer?.Stop();

    private void PlayTick(object? sender, EventArgs e) => ShowFrame(_currentIndex + 1);
}

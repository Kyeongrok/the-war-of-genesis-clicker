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
/// 편집기 첫 화면 — 게임 폴더를 열면 캐릭터 스탯(<see cref="CharacterStatsView"/>)을 바로 보여 주고, 나머지 도구는 메뉴에서 연다.
/// 예전 첫 화면(이름 검색 · 몸짓 벌 · 한 컷씩 보기)은 지웠다(사용자 요청) — 모션 매핑·내보내기는 목록 우클릭으로 옮겼다.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WarOfGenesis.Editor", "gameroot.txt");

    private const string DefaultGameRoot = @"C:\Users\Administrator\Downloads\gen3pt2";

    private TxrTable? _txr;
    private string _chrFolder = "", _obsFolder = "", _gameRoot = "";
    private GameDatabase? _database;
    private List<ChrRecord> _allChrRecords = [];

    public MainWindow()
    {
        InitializeComponent();
        GameRootBox.Text = LoadSavedGameRoot() ?? DefaultGameRoot;
        Stats.MotionMappingRequested += ShowMotionMapping;
        Stats.ExportRequested += ExportCharacter;
        Loaded += (_, _) => TryLoadGameRoot(GameRootBox.Text);
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

    private void LoadButton_Click(object sender, RoutedEventArgs e) => TryLoadGameRoot(GameRootBox.Text);

    private void TryLoadGameRoot(string root)
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

            // 낱장과 Chr.pak 안의 레코드를 합쳐 읽는다 — 게임 폴더에는 낱장이 일부(86개)만 있어, 낱장만 읽으면 살라딘 0219 같은
            // 이야기용 레코드가 빠지고 0006·0075 같은 시험·다른 판 살라딘만 보였다(사용자 보고). 게임 폴더에는 아무것도 안 쓴다.
            _allChrRecords = ChrTable.ScanAll(GameFiles.FromGameRoot(root));
            _gameRoot = root;
            _database = GameDatabase.Load(GameFiles.FromGameRoot(root));
            SaveGameRoot(root);
            // 게임 폴더에는 낱장 Chr 가 일부만 있다 — pak 안 레코드까지 보려고 번호대를 통째로 훑는다(없는 번호는 건너뜀).
            Stats.Load(_database, Enumerable.Range(0, 1000));
            StatusText.Text = $"열었습니다 — 인물 레코드 {_allChrRecords.Count}개. 목록을 우클릭하면 모션 매핑 보기·내보내기.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            StatusText.Text = $"여는 중 문제가 생겼습니다: {ex.Message}";
        }
    }

        // ── 모션 매핑 창 ─────────────────────────────────────────────────────────

    private ChrRecord? RecordOf(int code) => _allChrRecords.FirstOrDefault(r => r.ChrCode == code);

    private string NameOf(ChrRecord r)
    {
        string name = _txr?.TextOf(r.NameCode) ?? "";
        return name.Length > 0 ? name : _txr?.TextOf(r.AltNameCode) ?? "";
    }

    /// <summary>목록 우클릭 — 고른 캐릭터의 몸짓 파일(Obs) 모션표를 연다.</summary>
    private void ShowMotionMapping(int code)
    {
        if (RecordOf(code) is not { } record) { StatusText.Text = $"Chr {code:D4} 레코드를 못 찾았습니다."; return; }

        string? path = EnsureObsPath(record.SpriteCode);
        if (path == null) { StatusText.Text = $"몸짓 그림 {record.SpriteCode:D4}.obs 를 못 찾았습니다."; return; }

        if (ObsMotionTable.Load(path) is not { } table)
        {
            StatusText.Text = $"{record.SpriteCode:D4}.obs 에서 모션표를 못 읽었습니다.";
            return;
        }
        new MotionMappingWindow($"{NameOf(record)} (Chr {record.ChrCode:D4}, sprite {record.SpriteCode})", table, ObsSprite.Decode(path),
                                record.SpriteCode, ObsMotionTable.Load(path, applyEdits: false)) { Owner = this }.Show();
    }

    // ── 요소 > 직업 창 ───────────────────────────────────────────────────────

    private void JobViewerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_gameRoot.Length == 0) { StatusText.Text = "먼저 게임 폴더를 여세요."; return; }
        try
        {
            _database ??= GameDatabase.Load(GameFiles.FromGameRoot(_gameRoot));
            new JobViewerWindow(_database) { Owner = this }.Show();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            StatusText.Text = $"게임 자료를 읽지 못했습니다: {ex.Message}";
        }
    }

    // ── 개발 > 체질(배울 수 있는 어빌리티) 창 ───────────────────────────────

    /// <summary>계열 × 형마다 배울 수 있는 어빌리티를 더하고 뺀다. 저장은 저장소 assets/data/jobs/*.json 에 — 게임 폴더는 안 건드린다.</summary>
    private void BodyAbilitiesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_gameRoot.Length == 0) { StatusText.Text = "먼저 게임 폴더를 여세요."; return; }
        try
        {
            _database ??= GameDatabase.Load(GameFiles.FromGameRoot(_gameRoot));
            string jobs = Path.Combine(AssetsFolder.Find("data"), JobBook.Folder);
            if (!Directory.Exists(jobs)) { StatusText.Text = $"저장소 자료가 없습니다: {jobs}"; return; }
            new BodyAbilitiesWindow(_database, jobs) { Owner = this }.Show();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or DirectoryNotFoundException)
        {
            StatusText.Text = $"게임 자료를 읽지 못했습니다: {ex.Message}";
        }
    }

    // ── 전투 목록(챕터별) 창 ─────────────────────────────────────────────────

    /// <summary>분석-전투목록(ba-7) 의 표 — 챕터별 전투 목록. 줄을 두 번 누르면 전투 보기 창이 그 전투를 연다.</summary>
    private void BattleListMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_gameRoot.Length == 0) { StatusText.Text = "먼저 게임 폴더를 여세요."; return; }
        try
        {
            _database ??= GameDatabase.Load(GameFiles.FromGameRoot(_gameRoot));
            new BattleListWindow(_gameRoot, _database) { Owner = this }.Show();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            StatusText.Text = $"게임 자료를 읽지 못했습니다: {ex.Message}";
        }
    }

    // ── 챕터(모세스) 보기 창 ─────────────────────────────────────────────────

    /// <summary>분석-모세스 의 챕터 자료 — 항성계·행성·장소 나무와 상점·메일·성도 점 표.</summary>
    private void ChapterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_gameRoot.Length == 0) { StatusText.Text = "먼저 게임 폴더를 여세요."; return; }
        try
        {
            _database ??= GameDatabase.Load(GameFiles.FromGameRoot(_gameRoot));
            new ChapterWindow(_gameRoot, _database) { Owner = this }.Show();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            StatusText.Text = $"게임 자료를 읽지 못했습니다: {ex.Message}";
        }
    }

    // ── 필드(Fld) 편집 창 ──────────────────────────────────────────────────

    /// <summary>필드 파일 — 배경 위 물체·인물 미리보기와 머리·물체·인물·스크립트 고치기. 저장소 assets/data/Fld 를 고친다.</summary>
    private void FieldMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // 필드는 저장소 assets 에서 읽으니 게임 폴더가 없어도 연다 — 있으면 인물 이름·그림을 게임 자료에서 가져온다.
        try
        {
            if (_gameRoot.Length > 0) _database ??= GameDatabase.Load(GameFiles.FromGameRoot(_gameRoot));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            StatusText.Text = $"게임 자료를 읽지 못했습니다(이름 없이 엽니다): {ex.Message}";
        }
        new FieldWindow(_gameRoot, _database) { Owner = this }.Show();
    }

    // ── 스킬(어빌리티) 창 ────────────────────────────────────────────────────

    /// <summary>assets/data/skills — 스킬마다 공통 칸 한 벌과 레벨별 칸을 보고 고친다. 게임 폴더 없이 연다.</summary>
    private void SkillEditMenuItem_Click(object sender, RoutedEventArgs e) => new SkillEditWindow { Owner = this }.Show();

    // ── 스크립트 명령 사전 ─────────────────────────────────────────────────

    /// <summary>전투·필드·챕터 스크립트의 조건·행동 코드(200 증원, 909 베라모드 폭주 …)의 뜻과 쓰인 곳. 게임 폴더 없이 연다.</summary>
    private void ScriptOpsMenuItem_Click(object sender, RoutedEventArgs e) => new ScriptOpsWindow { Owner = this }.Show();

    // ── 이펙트 보기 ────────────────────────────────────────────────────────

    /// <summary>기술이 쓰는 이펙트와 Obs 모션을 재생해 본다. 게임 폴더가 열려 있으면 기술 이름과 assets 에 없는 Obs 도 보인다.</summary>
    private void EffectViewerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_gameRoot.Length > 0) _database ??= GameDatabase.Load(GameFiles.FromGameRoot(_gameRoot));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            StatusText.Text = $"게임 자료를 읽지 못했습니다(이름 없이 엽니다): {ex.Message}";
        }
        new EffectViewerWindow(_gameRoot, _database) { Owner = this }.Show();
    }

    // ── 에셋: 소리(배경음악 · 효과음 · 인물 대사) 창 ────────────────────────

    private SoundWindow? _soundWindow;

    private void MusicMenuItem_Click(object sender, RoutedEventArgs e) => OpenSoundWindow(SoundWindow.Page.Music);

    private void EffectSoundMenuItem_Click(object sender, RoutedEventArgs e) => OpenSoundWindow(SoundWindow.Page.Effect);

    private void VoiceMenuItem_Click(object sender, RoutedEventArgs e) => OpenSoundWindow(SoundWindow.Page.Voice);

    /// <summary>소리 창은 하나만 띄운다 — 이미 떠 있으면 그 탭으로 옮겨 앞으로 가져온다.</summary>
    private void OpenSoundWindow(SoundWindow.Page page)
    {
        if (_gameRoot.Length == 0) { StatusText.Text = "먼저 게임 폴더를 여세요."; return; }
        string folder = page == SoundWindow.Page.Effect ? SoundCatalog.SndFolder : SoundCatalog.BgmFolder;
        if (!Directory.Exists(Path.Combine(_gameRoot, folder)))
        {
            StatusText.Text = $"'{_gameRoot}' 에 {folder} 폴더가 없습니다.";
            return;
        }
        if (_soundWindow is { IsLoaded: true })
        {
            _soundWindow.ShowPage(page);
            _soundWindow.Activate();
            return;
        }
        _soundWindow = new SoundWindow(_gameRoot, page) { Owner = this };
        _soundWindow.Closed += (_, _) => _soundWindow = null;
        _soundWindow.Show();
    }

    // ── 전투맵 보기 창 ───────────────────────────────────────────────────────

    /// <summary>게임 폴더의 모든 전투(Btl)·맵(Obt) 목록 창을 연다.</summary>
    private void BattleMapMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_gameRoot.Length == 0) { StatusText.Text = "먼저 게임 폴더를 여세요."; return; }
        try
        {
            _database ??= GameDatabase.Load(GameFiles.FromGameRoot(_gameRoot));
            new BattleMapWindow(_gameRoot, _database) { Owner = this }.Show();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            StatusText.Text = $"게임 자료를 읽지 못했습니다: {ex.Message}";
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
    private void ExportCharacter(int code)
    {
        if (RecordOf(code) is not { } record) { StatusText.Text = $"Chr {code:D4} 레코드를 못 찾았습니다."; return; }
        string name = NameOf(record);

        string chrPath = Path.Combine(_chrFolder, CharacterExport.ChrFileName(record.ChrCode));
        // pak 에만 있는 레코드(살라딘 0219 따위)는 낱장으로 꺼내 둔다 — Obs 를 꺼내는 것과 같은 길.
        if (!File.Exists(chrPath)) PakArchive.EnsureFile(_chrFolder, "Chr", Path.GetFileName(chrPath));
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

        string outDir = Path.Combine(dialog.FolderName, CharacterExport.FolderNameFor(record.ChrCode, name));
        Directory.CreateDirectory(outDir);

        File.Copy(chrPath, Path.Combine(outDir, Path.GetFileName(chrPath)), overwrite: true);
        if (spritePath != null) File.Copy(spritePath, Path.Combine(outDir, Path.GetFileName(spritePath)), overwrite: true);
        if (facePath != null && facePath != spritePath)
            File.Copy(facePath, Path.Combine(outDir, Path.GetFileName(facePath)), overwrite: true);

        CharacterExport.SaveManifest(outDir, new ExportedCharacter(name, record.ChrCode, record.SpriteCode, record.FaceCode));

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
}

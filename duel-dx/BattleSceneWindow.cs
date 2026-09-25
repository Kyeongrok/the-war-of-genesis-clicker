using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using DuelDx.Native;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 「게임을 켜면 전투 맵이 펼쳐지고, 거기 투입되는 캐릭터들이 배치되어 있다」는 장면 — 자료는
/// <see cref="BattleDemoScene"/> 공용 상수를 그대로 쓴다.
/// </summary>
/// <remarks>
/// 아군·적군 구성은 실제 게임을 DOSBox 에서 띄워 메모리를 읽어 찾은 <b><c>Btl/0045.btl</c></b>
/// (코어헌터 훈련장 첫 전투)이다 — <c>Project/the-war-of-genesis/분석/분석-첫전투.md</c> 참고.
/// 캐릭터 배치(Chr 코드·X/Y)는 이 파일에서 직접 읽어낸 값을 그대로 박아 뒀다 — 아직
/// <c>.btl</c> 을 일반적으로 읽어들이는 코드는 없다(이 전투 하나만 보여 주는 데모).
///
/// 배경은 원본 전투 맵 <c>Obt/0153.obt</c> 를 <see cref="ObtMap"/> 으로 풀어 그린다. 칸 하나는
/// 원본과 같은 40×32 픽셀이다. 화면에 들어가도록 확대 배율은 모니터 작업 영역에 맞춰 2배 안에서 정한다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow : IDisposable
{
    /// <summary>지금 벌이는 전투 — 자료에서 읽는다(<see cref="DemoScene"/>). 못 읽으면 예전 상수 그대로.</summary>
    private DemoScene _scene = DemoScene.Fallback;

    /// <summary>한 칸 걸어가는 데 드는 시간(초).</summary>
    public const double StepSeconds = StepTicks / TicksPerSecond;

    /// <summary>
    /// 한 칸을 걷는 틱 수 — 원본 틱(초당 30)에 맞춰 <b>8틱(0.267초)</b>. 가로 40픽셀이면 틱마다 5픽셀, 세로 32픽셀이면 4픽셀씩
    /// 똑같이 움직인다. 예전에는 0.25초를 시간으로 나눠 한 프레임에 2.67픽셀 — 판을 낮은 해상도로 그리니 2·3픽셀이 번갈아
    /// 속도가 프레임마다 출렁여 끊겨 보였다(사용자 보고).
    /// </summary>
    public const int StepTicks = 8;

    /// <summary>
    /// 모션표 한 틱의 길이 — 1초에 몇 틱. 원본 게임의 틱 빠르기는 아직 확인 못 해서 눈으로 맞춘 값이다.
    /// </summary>
    public const double TicksPerSecond = 30;

    private const int TileW = ObtMap.CellWidth, TileH = ObtMap.CellHeight;
    // 판 크기는 <b>전투마다 맵을 따라</b> 바뀐다(0153 = 32×34, 0156 레이토스 = 37×71 …).
    // 맵을 바꿀 때 ResizeBoard 가 화면 버퍼·텍스처·창 크기를 다시 잡는다.
    private int Cols = BattleDemoScene.Cols, Rows = BattleDemoScene.Rows;
    private const int GridTop = 40;
    private int BoardWidth => Cols * TileW;
    private int BoardHeight => GridTop + BoardPad + Rows * TileH;

    // ── 칸 높이(고도) → 화면 (분석-전투 「자리 갱신 0x100ea910」: 화면 y = 월드 y×32/40 − 월드 z×12/20, 유닛 z = 칸 높이×20) ──

    /// <summary>칸 높이 한 층이 화면에서 위로 올라가는 픽셀 — 12.</summary>
    private const int HeightStep = 12;

    /// <summary>지금 판이 맵 크기 그대로인가(타이틀·모세스 틀이면 아니다 — 그때는 높이·여백을 안 쓴다).</summary>
    private bool BoardIsMap => _map is { } m && m.Cols == Cols && m.Rows == Rows;

    /// <summary>
    /// 맵 그림이 0줄보다 위로 나온 만큼(높은 칸이 위로 올라가 그려진 부분) 판 위에 덧대는 여백.
    /// 그림 원점이 −264 인 맵(Obt 0031)은 맨 윗줄이 22층 높이라 그만큼 위에 그려져 있다.
    /// </summary>
    private int BoardPad => BoardIsMap ? Math.Max(0, -_map!.OriginY) : 0;

    private int HeightPx(int col, int row) => BoardIsMap ? HeightStep * _map!.HeightAt(col, row) : 0;

    /// <summary>칸의 화면 윗줄 — 높이만큼 위로 올라간다.</summary>
    private int CellTop(int col, int row) => GridTop + BoardPad + row * TileH - HeightPx(col, row);

    private int CellCenterY(int col, int row) => CellTop(col, row) + TileH / 2;

    /// <summary>칸 사이를 걷는 인물의 높이 — 네 이웃 칸을 거리로 섞어 층 사이를 매끄럽게 넘는다.</summary>
    private double HeightPxAt(double x, double y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        return HeightPx(x0, y0) * (1 - fx) * (1 - fy) + HeightPx(x0 + 1, y0) * fx * (1 - fy)
             + HeightPx(x0, y0 + 1) * (1 - fx) * fy + HeightPx(x0 + 1, y0 + 1) * fx * fy;
    }

    /// <summary>판 픽셀 (bx, by) 가 놓인 칸의 줄 — 높이 때문에 겹치면 아래 줄(나중에 그린 쪽)이 이긴다. 없으면 −1.</summary>
    private int RowAt(int bx, int by)
    {
        int col = bx / TileW;
        if ((uint)col >= Cols) return -1;
        for (int row = Rows - 1; row >= 0; row--)
        {
            int top = CellTop(col, row);
            if (by >= top && by < top + TileH) return row;
        }
        return -1;
    }

    /// <summary>맵 그림의 아랫끝(판 픽셀) — 카메라는 원본처럼 그림 밖으로 안 내려간다(맵 `+0x398~+0x39e` 로 자름).</summary>
    private int PictureBottom => BoardIsMap ? GridTop + BoardPad + _map!.OriginY + _map.Height : BoardHeight;

    /// <summary>카메라가 내려갈 수 있는 끝.</summary>
    private int CamMax => Math.Max(0, Math.Min(BoardHeight, PictureBottom) - ViewHeight);

    /// <summary>
    /// 창에 보이는 판 높이 — 판 전체의 70%, 다만 원본 화면 높이(480 + 머리줄)까지만. 나머지는 <see cref="_camY"/> 로 위아래로 스크롤한다.
    /// 세로로 긴 맵(Obt 0156 은 2272 픽셀)도 원본처럼 줌아웃하지 않고 스크롤한다.
    /// </summary>
    private int ViewHeight => Math.Min(BoardHeight * 7 / 10, Math.Max(240, (int)Math.Round(_resH / _zoom)));

    /// <summary>설정 > 해상도 — <b>창 크기</b>(화면 픽셀). 원본은 640×480. 보이는 판은 이것을 배율로 나눈 만큼이다.</summary>
    private int _viewW = UserSettings.Current.ViewW, _viewH = UserSettings.Current.ViewH;

    /// <summary>모니터에 들어가게 깎은 해상도(<see cref="FitZoom"/> 가 채운다) — 창 너비·(머리줄 뺀) 창 높이.</summary>
    private int _resW = UserSettings.Current.ViewW, _resH = UserSettings.Current.ViewH;

    /// <summary>
    /// 창에 보이는 판 너비 — 원본 화면 너비 640 까지만. 더 넓은 맵(Btl 0131 같은 1480 픽셀)은 원본처럼 <b>줌아웃하지 않고</b>
    /// <see cref="_camX"/> 로 좌우 스크롤한다(차례인 인물을 따라간다).
    /// </summary>
    private int ViewWidth => Math.Min(BoardWidth, Math.Max(320, (int)Math.Round(_resW / _zoom)));

    /// <summary>창 안에서 판 그림이 놓이는 자리(화면 픽셀) — 판이 창보다 작으면 가운데에 두고 둘레는 검게 남긴다.</summary>
    private int ViewOffsetX => Math.Max(0, (_resW - (int)(ViewWidth * _zoom)) / 2);
    private int ViewOffsetY => Math.Max(0, (_resH - (int)(ViewHeight * _zoom)) / 2);

    /// <summary>화면 픽셀 → 판 픽셀(카메라 더한 것).</summary>
    private (int X, int Y) BoardPoint(int clientX, int clientY) =>
        ((int)Math.Floor((clientX - ViewOffsetX) / _zoom) + _camX, (int)Math.Floor((clientY - ViewOffsetY) / _zoom) + _camY);
    /// <summary>배율의 위아래 한계. 자동은 판이 창보다 작을 때(모세스·타이틀 640×480)만 창을 채우도록 키운다.</summary>
    private const double MinZoom = 0.5, MaxZoomChosen = 4;

    /// <summary>화면 픽셀 ÷ 판 픽셀 — <see cref="FitZoom"/> 가 정한다.</summary>
    private double _zoom;

    private const string GameRoot = @"C:\Users\Administrator\Downloads\gen3pt2";

    private const uint BgColor = 0xFF14100C;
    private const uint GridLine = 0x40FFFFFF;
    private const uint White = 0xFFF2EAD6;
    private const uint DimGray = 0xFFA09888;

    private ObtMapImage? _map;
    private Dictionary<int, UnitSprite> _sprites = [];

    /// <summary>인물마다 읽어 둔 그림이 어느 레코드 그림(+0xc) 값으로 읽은 것인가 — 바뀌면 다시 읽는다.</summary>
    private readonly Dictionary<int, int> _spriteCodes = [];
    private UnitState[] _units = [.. DemoScene.Fallback.Roster.Select(u => new UnitState(u))];   // 자료를 읽으면 BuildUnits 로 다시 만든다
    private int _selected = -1;
    private readonly Dictionary<int, string> _names = [];
    private string _loadError = "";
    private volatile bool _loading = true;
    private bool _showGrid = UserSettings.Current.ShowGrid;
    /// <summary>발밑 HP·TP 막대 — 원본에는 없어서 기본은 끔(H 키).</summary>
    private bool _showGauges = UserSettings.Current.ShowGauges;

    private uint[] _fb = [];
    private readonly Dictionary<string, (uint[] Px, int W, int H)> _textCache = [];

    private bool _running;
    private double _lastTime;

    private IntPtr _hwnd;
    private static readonly bool Offscreen = Environment.GetEnvironmentVariable("DUELDX_OFFSCREEN") == "1";
    private static readonly Win32.WndProc StaticWndProcDelegate = StaticWndProcTrampoline;
    private static BattleSceneWindow? _active;
    private static ushort _classAtom;
    private const string ClassName = "BattleSceneDx";

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _ctx = null!;
    private IDXGISwapChain1 _swapChain = null!;

    /// <summary>
    /// 스왑체인이 새 프레임을 받을 수 있게 되면 신호가 오는 핸들 — 이걸 기다린 <b>다음에</b> 입력을 읽고 그린다.
    /// 기본값(최대 3프레임 미리 쌓기)이면 CPU 가 노는 동안(합성은 2~5ms) 프레임이 줄을 서서 입력이 늦게 보였다.
    /// 줄은 1프레임 — 입력에서 화면까지 가장 짧다(화면 밖 시험에서는 2프레임보다 vsync 를 조금 더 놓쳤다).
    /// </summary>
    private IntPtr _frameWait;
    private ID3D11RenderTargetView _backBufferRtv = null!;
    private ID3D11Texture2D _boardTex = null!;
    private ID3D11ShaderResourceView _boardSrv = null!;
    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;

    // ── 게임 자료 읽기 ───────────────────────────────────────────────────────

    /// <summary>지금 전투에 나오는 인물들의 그림과 초상을 (없는 것만) 읽는다.</summary>
    private void LoadRosterSprites()
    {
        string assetsRoot = FindRepoAssetsRoot();
        var manifests = CollectExportedManifests(assetsRoot);
        var sprites = new Dictionary<int, UnitSprite>(_sprites);

        foreach (int chrCode in _units.Select(u => u.ChrCode).Distinct())
        {
            // 인물 레코드의 그림(+0xc)은 필드 행동 701 칸 0 이 바꾼다 — Fld 0088 이 살라딘을 건슬라이서 그림 1185 로 바꾼다.
            // 전에는 뽑아 둔 목록의 그림만 써서 바뀐 모션이 전투에 안 나왔다(사용자 보고: Btl 0150).
            int wanted = _units.FirstOrDefault(u => u.ChrCode == chrCode)?.Data?.SpriteId ?? 0;
            if (sprites.ContainsKey(chrCode) && (wanted == 0 || _spriteCodes.GetValueOrDefault(chrCode) == wanted)) continue;
            string name = "";

            // 몸짓벌을 모두 풀고 모션표(서기·걷기 …)를 같이 읽는다. 모션표가 없으면 첫 컷 하나로 서 있는다.
            // 그림이 assets 에 없는 인물은 <b>그 사람만</b> 빼고 간다 — 자리를 찾는 것까지 이 안에서 해야
            // 원본 게임 폴더가 없는 기계에서도 전투가 열린다(군단 부하처럼 안 뽑아 둔 인물이 하나 있으면 전부 막혔다).
            try
            {
                string obsPath;
                (name, obsPath) = manifests.TryGetValue(chrCode, out var m)
                    ? (m.Name, Path.Combine(assetsRoot, CharacterExport.FolderNameFor(chrCode, m.Name), CharacterExport.ObsFileName(m.SpriteCode)))
                    : LoadFromGameFolder(chrCode);
                // 바뀐 그림이 그 인물 폴더에 있으면 그것을 쓴다(없으면 목록 그림 그대로).
                if (wanted > 0 && Path.Combine(Path.GetDirectoryName(obsPath)!, CharacterExport.ObsFileName(wanted)) is var changed && File.Exists(changed))
                {
                    obsPath = changed;
                    if (Trace)
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"sprite {chrCode}: record picture {wanted} → {obsPath}" + Environment.NewLine);
                }
                _spriteCodes[chrCode] = wanted;
                _names[chrCode] = name;
                var motions = ObsSprite.Decode(obsPath);
                if (motions.Count == 0) continue;
                sprites[chrCode] = new UnitSprite(motions, ObsMotionTable.Load(obsPath));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or DirectoryNotFoundException
                                             or InvalidOperationException or ArgumentException)
            {
                _loadError = $"{(name.Length > 0 ? name : "인물")}({chrCode}) 그림을 못 읽었습니다";
            }
        }
        _sprites = sprites;
        // 그림이 없는 인물은 판에서 뺀다 — 안 그러면 보이지도 않는 인물 때문에 승패가 안 난다.
        if (_units.Any(u => !sprites.ContainsKey(u.ChrCode)))
            _units = [.. _units.Where(u => sprites.ContainsKey(u.ChrCode))];

        // Status 화면용 초상(첫 컷). 없어도 전투판은 그린다.
        foreach (var (code, m) in manifests)
        {
            if (_faces.ContainsKey(code) || !_units.Any(u => u.ChrCode == code) || m.FaceCode == 0) continue;
            string facePath = Path.Combine(assetsRoot, CharacterExport.FolderNameFor(code, m.Name), CharacterExport.ObsFileName(m.FaceCode));
            if (File.Exists(facePath) && DecodeFaceFrame(facePath) is { } face) _faces[code] = SpriteFrame.From(face);
        }
    }

    /// <summary>
    /// 전투 자료와 맵만 먼저 읽어 <b>판 크기를 정한다</b> — 창·텍스처를 만들기 전에, <b>주 스레드에서</b> 돈다.
    /// </summary>
    /// <remarks>
    /// 전에는 이것까지 배경 스레드에서 했는데, 그러면 <see cref="ResizeBoard"/> 가 그리기 고리와 <b>같이</b> 돌아
    /// 텍스처를 그 밑에서 갈아 치웠다. 맵이 큰 전투(예: Btl 0051)에서 화면 버퍼가 텍스처보다 커져 CLR 이 그대로 죽었다.
    /// 인물 세우기도 판 크기를 정한 <b>뒤</b>여야 진형 칸이 옛 크기로 잘리지 않는다.
    /// </remarks>
    /// <summary>
    /// 타이틀·연대표·모세스가 쓰는 판 크기 — 그 화면들은 <b>640×480 틀</b>만 있으면 된다.
    /// </summary>
    /// <remarks>보이는 높이가 판의 <b>70%</b> 라, 480픽셀을 다 보이려면 줄 수를 그만큼 넉넉히 잡아야 한다.</remarks>
    private const int TitleBoardCols = MosesW / ObtMap.CellWidth;
    private const int TitleBoardRows = MosesH * 10 / 7 / ObtMap.CellHeight + 1;

    private void LoadBoard()
    {
        try
        {
            _db = GameDatabase.Load(GameFiles.FromFolder(AssetsFolder.Find("data")));

            // 시작하자마자 전투를 읽지는 않는다 — 어느 전투를 할지는 타이틀에서 고르고 나서야 정해진다.
            // 이어하기로 다른 곳을 고르면 미리 읽은 것이 헛일이 되고, 그만큼 창이 늦게 뜬다.
            if (!WantsBattleAtStart(out int startId))
            {
                ResizeBoard(TitleBoardCols, TitleBoardRows);
                return;
            }
            LoadBattleBoard(startId);
            _battleLoaded = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            _loadError = ex.Message;
        }
    }

    /// <summary>
    /// 켜자마자 전투를 읽어야 하나 — <b>화면 밖 시험</b>에서 타이틀을 건너뛸 때뿐이다.
    /// </summary>
    private static bool WantsBattleAtStart(out int id)
    {
        id = 0;
        bool skipTitle = Environment.GetEnvironmentVariable("DUELDX_TITLE") == "0";
        bool wanted = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_BATTLE"), out id) && id > 0;
        if (wanted) return true;
        id = BattleDemoScene.BtlId;
        return skipTitle;
    }

    /// <summary>
    /// 전투 자료를 한 번이라도 읽었나 — 타이틀에서 시작하면 <b>고르기 전까지 아무 전투도 안 읽는다</b>.
    /// </summary>
    private bool _battleLoaded;

    /// <summary>그 전투의 자료·맵을 읽고 판을 그 크기로 잡는다.</summary>
    private void LoadBattleBoard(int id)
    {
        // DUELDX_LEGION=<Chr>:<군단> 이면 그 인물에게 군단을 배속하고 시작한다(화면 밖 시험용 — 부하·군단기).
        if (Environment.GetEnvironmentVariable("DUELDX_LEGION")?.Split(':') is [var lc, var ll] && int.TryParse(lc, out int lchr) && int.TryParse(ll, out int lid))
            _unitLegion[lchr] = lid;
        if (DemoScene.Load(id, _db) is { } loaded) _scene = loaded;
        _map = ObtMap.Load(Path.Combine(AssetsFolder.Find("maps"), _scene.MapFile));
        ResizeBoard(_map.Cols, _map.Rows);
        _units = BuildUnits(_scene);
        LoadEvents(_scene.Id);
    }

    /// <summary>나머지(그림·소리)는 배경 스레드에서 읽는다 — 창은 먼저 뜬다.</summary>
    private void LoadScene()
    {
        try
        {
            LoadRosterSprites();
            InitBattle();
            LoadRingAssets();
            LoadAudio();
            // 타이틀·모세스·필드를 여는 훅은 판(_fb·_map)을 갈아 끼우므로 <b>주 스레드</b>에서 돌린다(Update) — 여기서 부르면
            // 그리는 중에 판이 바뀌어 DrawBackground 가 간헐적으로 죽었다.
            _openHooksPending = true;
            // 자동 저장은 전투를 열 때가 아니라 내 차례가 시작될 때 한다(원본 상태 22, StartTurn).
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
        {
            _loadError = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>창을 작업 영역 가운데(가로)·맨 위(세로)에 놓는다 — 아래가 잘리지 않게(an-ui-2).</summary>
    private static int WindowLeft(int windowWidth)
    {
        var work = new Win32.Rect();
        if (!Win32.SystemParametersInfoW(Win32.SPI_GETWORKAREA, 0, ref work, 0)) return 0;
        return Math.Max(work.Left, work.Left + (work.Width - windowWidth) / 2);
    }

    /// <summary>
    /// 배율을 정한다 — 설정 > 해상도가 <b>창 크기 그대로</b>, 설정 > 배율이 <b>확대 그대로</b>다(사용자 요청: 「해상도는 창 크기, 배율은 정직하게」).
    /// 보이는 판 = 창 ÷ 배율. 판이 그보다 작으면(모세스·타이틀 640×480) 창 가운데에 두고 둘레를 검게 남긴다.
    /// 배율 「자동」은 판 내용(모세스 640×480, 맵은 판의 70%)이 창을 채우는 가장 큰 배율(0.05 단위, 최대 4배, 최소 1배).
    /// 해상도가 모니터 작업 영역보다 크면 들어가는 데까지만(<see cref="_resW"/>·<see cref="_resH"/>).
    /// </summary>
    private double FitZoom()
    {
        var work = new Win32.Rect();
        if (Win32.SystemParametersInfoW(Win32.SPI_GETWORKAREA, 0, ref work, 0))
        {
            // 제목 표시줄·메뉴·테두리 몫을 조금 남긴다.
            _resW = Math.Max(320, Math.Min(_viewW, work.Width - 32));
            _resH = Math.Max(240, Math.Min(_viewH, work.Height - 100));
        }
        else { _resW = _viewW; _resH = _viewH; }

        if (_zoomPercent > 0) return Math.Clamp(_zoomPercent / 100.0, MinZoom, MaxZoomChosen);

        int contentW = Math.Min(BoardWidth, _resW);
        int contentH = Math.Min(BoardIsMap ? BoardHeight * 7 / 10 : GridTop + MosesH, _resH);
        double fill = Math.Floor(Math.Min(_resW / (double)contentW, _resH / (double)contentH) * 20) / 20;
        return Math.Clamp(fill, 1, MaxZoomChosen);
    }

    /// <summary>설정 > 해상도 — 고른 배율 %(0 = 자동). 켤 때 읽고 바꾸면 저장한다.</summary>
    private int _zoomPercent = UserSettings.Current.ZoomPercent;

    /// <summary>배율·해상도를 바꿨을 때 — 판은 그대로 두고 셰이더·텍스처·창만 다시 만든다.</summary>
    private void ApplyZoom(bool force = false)
    {
        double zoom = FitZoom();
        if (!force && Math.Abs(zoom - _zoom) < 1e-9) return;
        _zoom = zoom;
        _camTarget = Math.Clamp(_camTarget, 0, CamMax);
        _camTargetX = Math.Clamp(_camTargetX, 0, CamMaxX);
        if (_hwnd != IntPtr.Zero) RebuildView();
    }

    /// <summary>
    /// 새 맵에 맞춰 판·화면 버퍼·텍스처·창 크기를 다시 잡는다(전투마다 맵 크기가 다르다).
    /// 창을 아직 안 만들었으면 크기만 정해 두고, 만들었으면 텍스처와 창까지 다시 만든다.
    /// </summary>
    private void ResizeBoard(int cols, int rows)
    {
        cols = Math.Max(1, cols);
        rows = Math.Max(1, rows);
        bool same = cols == Cols && rows == Rows && _fb.Length == BoardWidth * BoardHeight;
        Cols = cols;
        Rows = rows;
        _zoom = FitZoom();
        if (_fb.Length != BoardWidth * BoardHeight) _fb = new uint[BoardWidth * BoardHeight];
        _camY = 0;
        _camX = 0;
        _camPosX = _camTargetX = 0;
        if (same || _hwnd == IntPtr.Zero) return;
        RebuildView();
    }

    /// <summary>보이는 판 크기·배율에 맞춰 셰이더·텍스처·창·스왑체인을 다시 만든다.</summary>
    private void RebuildView()
    {
        // 배율과 판 자리는 픽셀 셰이더에 박혀 있다 — 달라졌으면 셰이더부터 다시 빌드한다.
        if (Math.Abs(_shaderZoom - _zoom) > 1e-9 || _shaderOffset != (ViewOffsetX, ViewOffsetY)) CompileShaders();

        // 텍스처는 보이는 판 크기로 다시 만든다.
        _boardSrv?.Dispose();
        _boardTex?.Dispose();
        _boardTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)ViewWidth,
            Height = (uint)ViewHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
        });
        _boardSrv = _device.CreateShaderResourceView(_boardTex);

        // 창과 스왑체인은 해상도 그대로 — 판 그림은 그 안 가운데에 놓인다(Draw 의 뷰포트).
        int pixelW = _resW, pixelH = _resH;
        var rect = new Win32.Rect { Left = 0, Top = 0, Right = pixelW, Bottom = pixelH };
        Win32.AdjustWindowRect(ref rect, Win32.WS_OVERLAPPEDWINDOW, true);
        Win32.SetWindowPos(_hwnd, IntPtr.Zero, Offscreen ? -8000 : WindowLeft(rect.Width), 0,
                           rect.Width, rect.Height, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        _backBufferRtv?.Dispose();
        _swapChain.ResizeBuffers(2, (uint)pixelW, (uint)pixelH, Format.B8G8R8A8_UNorm, SwapChainFlags.FrameLatencyWaitableObject);
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _backBufferRtv = _device.CreateRenderTargetView(backBuffer);
    }

    /// <summary><c>assets/characters/</c> 밑의 인물들을 전부 훑어 Chr 코드별로 모은다.</summary>
    private static Dictionary<int, ExportedCharacter> CollectExportedManifests(string assetsRoot)
    {
        var result = new Dictionary<int, ExportedCharacter>();
        if (assetsRoot.Length == 0 || !Directory.Exists(assetsRoot)) return result;

        foreach (var folder in Directory.EnumerateDirectories(assetsRoot))
        {
            var manifest = CharacterExport.LoadManifest(folder);
            if (manifest != null) result[manifest.ChrCode] = manifest;
        }
        return result;
    }

    private static string FindRepoAssetsRoot()
    {
        try { return AssetsFolder.Find("characters"); }
        catch (DirectoryNotFoundException) { return ""; }
    }

    /// <summary>내보낸 것이 없을 때의 마지막 수단 — 실제 게임 폴더에서 읽는다.</summary>
    private static (string Name, string ObsPath) LoadFromGameFolder(int chrCode)
    {
        string chrFolder = Path.Combine(GameRoot, "Chr");
        string obsFolder = Path.Combine(GameRoot, "Obs");
        string txrPath = Path.Combine(GameRoot, "TXR", "Txr.dat");

        if (!Directory.EnumerateFiles(chrFolder, "*.chr").Any() && File.Exists(Path.Combine(chrFolder, "Chr.idx")))
            PakArchive.Extract(chrFolder, "Chr");

        var txr = TxrTable.Open(txrPath);
        var record = ChrTable.Scan(chrFolder).First(r => r.ChrCode == chrCode);
        string name = txr.TextOf(record.NameCode);
        if (name.Length == 0) name = txr.TextOf(record.AltNameCode);

        string obsName = CharacterExport.ObsFileName(record.SpriteCode);
        if (!PakArchive.EnsureFile(obsFolder, "Obs", obsName))
            throw new FileNotFoundException($"{obsName} 을(를) Obs00~03.pak 에서 못 찾았습니다.");

        return (name, Path.Combine(obsFolder, obsName));
    }

    // ── 창 열기 · 메시지 펌프 ─────────────────────────────────────────────────

    public void Run()
    {
        RegisterClassOnce();
        // 판 크기를 맨 먼저 정한다 — 화면 텍스처도, 픽셀 셰이더에 박히는 배율도 이 크기로 만들어진다.
        LoadBoard();
        CreateDevice();
        CreateNativeWindow();
        CreateSwapChain();

        // DUELDX_OFFSCREEN=1 이면 화면 밖에 포커스를 뺏지 않고 띄운다 — 자동 테스트가 사용자 화면을 건드리지 않게(WM_APP_SNAPSHOT 으로 확인).
        Win32.ShowWindow(_hwnd, Offscreen ? 4 : 5);
        Win32.UpdateWindow(_hwnd);

        System.Threading.Tasks.Task.Run(LoadScene);

        _running = true;
        var clock = Stopwatch.StartNew();

        while (_running)
        {
            // 줄에 자리가 나면 그때 입력을 받아 그린다 — 미리 그려 둔 프레임 뒤에 입력이 밀리지 않게.
            Win32.WaitForSingleObjectEx(_frameWait, 100, true);
            while (Win32.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
            {
                if (msg.Message == Win32.WM_QUIT) { _running = false; break; }
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessageW(ref msg);
            }
            if (!_running) break;

            // 게임 시계 — 실제로 흐른 시간 × 게임 속도. 모션·걷기·이펙트·소리 예약이 모두 이 시계를 보므로 함께 빨라진다.
            double real = clock.Elapsed.TotalSeconds, dt = Math.Min(real - _realTime, 0.1) * _gameSpeed / 100.0;
            _realTime = real;
            double now = _lastTime + dt;
            Update(dt);
            _lastTime = now;

            UpdateCursor();
            Render();
        }
    }

    /// <summary>지난 프레임의 실제 시각(초) — 게임 시계(<c>_lastTime</c>)는 여기서 흐른 만큼 × 게임 속도로 나아간다.</summary>
    private double _realTime;

    /// <summary>
    /// 창 제목에 찍는 버전 — 릴리즈 빌드는 태그(v0.10.0 따위). 손으로 빌드한 것은 git 마지막 태그와 그 뒤 커밋 수(v0.10.0+2),
    /// git 이 없어 못 셌으면 「개발판」.
    /// </summary>
    private static string AppVersion
    {
        get
        {
            string v = typeof(BattleSceneWindow).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "";
            v = v.Split('+')[0];                                   // 커밋 해시(+abc…) 꼬리는 뗀다
            if (v.Length == 0 || v.EndsWith("-dev")) return "개발판";
            // git describe 꼴(0.10.0-2-gabc1234) — 태그 바로 위면 태그만, 뒤에 커밋이 더 있으면 +개수.
            if (System.Text.RegularExpressions.Regex.Match(v, @"^(.+)-(\d+)-g[0-9a-f]+$") is { Success: true } m)
                return m.Groups[2].Value == "0" ? "v" + m.Groups[1].Value : $"v{m.Groups[1].Value}+{m.Groups[2].Value}";
            return "v" + v;
        }
    }

    private static void RegisterClassOnce()
    {
        if (_classAtom != 0) return;
        var wc = new Win32.WndClassEx
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.WndClassEx>(),
            Style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
            WindowProc = StaticWndProcDelegate,
            Instance = Win32.GetModuleHandleW(null),
            Cursor = Win32.LoadCursorW(IntPtr.Zero, (IntPtr)Win32.IDC_ARROW),
            // 창·작업 표시줄 아이콘 = 실행 파일에 박힌 원본 게임 아이콘(menu-5)
            Icon = Win32.LoadIconW(Win32.GetModuleHandleW(null), (IntPtr)32512),
            ClassName = ClassName,
        };
        _classAtom = Win32.RegisterClassExW(ref wc);
    }

    private static IntPtr StaticWndProcTrampoline(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        _active != null ? _active.WndProc(hWnd, msg, wParam, lParam)
                        : Win32.DefWindowProcW(hWnd, msg, wParam, lParam);

    private void CreateNativeWindow()
    {
        if (_fb.Length == 0) ResizeBoard(Cols, Rows);   // 창 크기를 정하기 전에 판 버퍼부터
        _zoom = FitZoom();
        int pixelW = _resW, pixelH = _resH;

        var rect = new Win32.Rect { Left = 0, Top = 0, Right = pixelW, Bottom = pixelH };
        Win32.AdjustWindowRect(ref rect, Win32.WS_OVERLAPPEDWINDOW, true);
        IntPtr menu = CreateMenuBar();

        _active = this;
        _hwnd = Win32.CreateWindowExW(0, ClassName, $"창세기전3 파트2 — WarOfGenesis {AppVersion}",
            Win32.WS_OVERLAPPEDWINDOW, Offscreen ? -8000 : WindowLeft(rect.Width), 0,
            rect.Width, rect.Height,
            IntPtr.Zero, menu, Win32.GetModuleHandleW(null), IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("창을 만들지 못했습니다.");
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
            case Win32.WM_ERASEBKGND:
                return (IntPtr)1;
            case Win32.WM_SETCURSOR:
                // 판 위에서는 게임 고유 커서(an-ui-1)를 하드웨어 커서로 건다 — UpdateCursor 가 컷에 맞춰 만든 것.
                if ((((long)lParam) & 0xFFFF) == Win32.HTCLIENT) { Win32.SetCursor(_hwCursor); return (IntPtr)1; }
                break;
            case Win32.WM_KEYDOWN:
                // 키 반복(lParam 30번 비트)은 무시한다 — 누르고 있는 동안은 Update 가 알아서 이어 걷는다.
                if (((long)lParam & 0x40000000) == 0) OnKeyDown((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_KEYUP:
                _heldMoveKeys.Remove((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL when _progressOpen:
                _progressScroll -= 3 * (short)(((long)wParam >> 16) & 0xFFFF) / 120;   // 진행 상태 창이 떠 있으면 그 목록을 굴린다
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL when _statusUnit >= 0 && ScrollStatusLists(-(short)(((long)wParam >> 16) & 0xFFFF) / 120):
                return IntPtr.Zero;                                          // 스테이터스 창의 어빌리티 목록 위면 그 목록을 굴린다
            case Win32.WM_MOUSEWHEEL when SlotsOpen:
                ScrollSlots(-(short)(((long)wParam >> 16) & 0xFFFF) / 120);   // 슬롯 목록이 떠 있으면 휠은 목록을 굴린다
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL:
                // Shift+휠은 좌우로(넓은 맵), 그냥 휠은 위아래로.
                if (((long)wParam & 0x0004) != 0) ScrollCameraX(-(short)(((long)wParam >> 16) & 0xFFFF) / 120.0 * TileW * 2);
                else ScrollCamera(-(short)(((long)wParam >> 16) & 0xFFFF) / 120.0 * TileH * 2);
                return IntPtr.Zero;
            case 0x020E:   // WM_MOUSEHWHEEL — 가로 휠
                ScrollCameraX((short)(((long)wParam >> 16) & 0xFFFF) / 120.0 * TileW * 2);
                return IntPtr.Zero;
            case Win32.WM_APP_SNAPSHOT:
                SaveSnapshot();
                return IntPtr.Zero;
            case Win32.WM_INITMENUPOPUP when wParam == _replayMenu:
                RebuildReplayMenu();
                return IntPtr.Zero;
            case Win32.WM_COMMAND:
                OnMenuCommand((int)((long)wParam & 0xFFFF));
                return IntPtr.Zero;
            case Win32.WM_KILLFOCUS:
                _heldMoveKeys.Clear();
                return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
            case Win32.WM_LBUTTONDOWN:
                OnClick((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
                return IntPtr.Zero;
            case Win32.WM_RBUTTONUP:
                CloseUnitInfo();
                _abilityPressed = -1;      // 어빌리티 설명은 오른쪽 단추를 떼면 사라진다
                _statusTip = null;         // 스테이터스 설명도(0x10042c00)
                return IntPtr.Zero;
            case Win32.WM_RBUTTONDOWN:
            case Win32.WM_MOUSEMOVE:
            {
                var (bx, by) = BoardPoint((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
                _mouse = (bx, by);
                UpdateMosesHover(bx, by);
                UpdateChaptersHover(bx, by);
                UpdateSlotsHover(bx, by);
                UpdateAbilityHover(bx, by);
                UpdateAimHover(bx, by);
                UpdateTitleHover(bx, by);
                UpdateRecordsHover(bx, by);
                UpdateFieldHover(bx, by);
                if (msg == Win32.WM_RBUTTONDOWN) OnRightClick(bx, by);
                else OnRingMouseMove(bx, by);
                return IntPtr.Zero;
            }
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // ── 입력 · 이동 ──────────────────────────────────────────────────────────

    private void OnKeyDown(int key)
    {
        if (key == 'W' && FieldOpen && RunWipeIfAsked()) return;   // 화면 밖 시험: DUELDX_WIPE 전환을 손으로 건다
        if (key == 'T' && !FieldOpen && TouchNearestObjectForTest()) return;
        // 대사는 아무 키로나 한 줄씩 넘기고, <b>Esc 면 그 장면을 통째로</b> 건너뛴다 — 대사뿐 아니라 기다림·걷기·전환까지.
        if (_progressOpen) { if (key == Win32.VK_ESCAPE) ToggleProgress(); return; }
        if (key == Win32.VK_ESCAPE && SkipScene()) return;
        if (OnTalkInput(skipAll: key == Win32.VK_ESCAPE)) return;
        if ((key == Win32.VK_RETURN || key == Win32.VK_SPACE) && SkipCurrentWait()) return;   // 컷씬 기다림은 Enter·Space 로 넘긴다
        if (_keysOpen) { OnKeysKey(key); return; }
        if (_chaptersOpen) { if (key == Win32.VK_ESCAPE) _chaptersOpen = false; return; }
        // 타이틀 화면에서는 슬롯 창만 키를 받는다(원본 타이틀은 키 처리가 없다).
        if (_titleOpen) { if (key == Win32.VK_ESCAPE) CloseSystemWindow(); return; }
        // 연대표에서도 전투 키는 안 먹고 Esc 로 시스템 메뉴만 연다.
        if (_episodesOpen)
        {
            if (key != Win32.VK_ESCAPE || CloseSystemWindow()) return;
            Play(578);
            OpenSystemMenu();
            return;
        }
        if (OnAbilityMenuKey(key)) return;
        if (LevelUpOpen) { CloseLevelUp(); return; }
        // 전투가 끝나고 배너가 떠 있으면 아무 키나 누르면 — 이기고 이어지는 전투가 있으면 그 전투로(이벤트 행동 10),
        // 없거나 졌으면 모세스 화면으로 간다(mo-1).
        if (_outcome.Length > 0 && !_mosesOpen && !FieldOpen && !_episodesOpen) { LeaveFinishedBattle(); return; }
        // 모세스 화면에서는 Esc 가 페이지를 닫고, 주 화면이면 모세스 시스템 메뉴를 연다(분석-모세스 13절).
        if (_mosesOpen)
        {
            if (_statusUnit >= 0) { if (key == Win32.VK_ESCAPE) _statusUnit = -1; return; }   // 스테이터스 창은 Esc 로 닫는다
            // 성도에서는 ←·→ 로도 항성계를 옮긴다(좌우 단추와 같은 일).
            if (_mosesPage == 0 && _mosesStep == 1 && key is Win32.VK_LEFT or Win32.VK_RIGHT)
            {
                MosesTurnSystem(key == Win32.VK_LEFT ? -1 : 1);
                return;
            }
            if (key != Win32.VK_ESCAPE || CloseSystemWindow()) return;
            if (_mosesPage != -1) { MosesGoBack(); return; }
            Play(578);
            OpenSystemMenu();
            return;
        }
        if (key == Win32.VK_ESCAPE && CloseSystemWindow()) return;
        if (key == Win32.VK_ESCAPE)
        {
            // 취소할 것이 있으면 취소하고, 없으면 시스템 메뉴를 연다(menu-6).
            if (_statusUnit >= 0) _statusUnit = -1;
            else if (_ringUnit >= 0) CancelRing();
            else if (!CancelStep(undoMove: true)) OpenSystemMenu();
            return;
        }
        if (key == Win32.VK_RETURN && _ringUnit >= 0 && _ringPhase == RingPhase.Idle && _ringHover >= 0) { PickRingItem(_ringHover); return; }
        if (_statusUnit >= 0) return;

        // 대상 고르는 중: Enter·공격 키 = 커서의 적 치기, 다음 인물 키(Tab) = 다른 적
        // 기본공격뿐 아니라 적 하나를 겨누는 어빌리티도 같게 다룬다.
        if (_targetWork >= 0 && _attackCursor >= 0 && _ringUnit < 0)
        {
            if (key == Win32.VK_RETURN || _keys.ActionFor(key) == KeyAction.Attack) { AttackCursorTarget(); return; }
            if (_keys.ActionFor(key) == KeyAction.NextUnit) { CycleAttackCursor(); return; }
        }
        // 어빌리티를 고른 단축키를 한 번 더(또는 Enter) — 저절로 겨눈 대상에게 바로 쓴다.
        if (_targetWork >= 0 && !_targetIsBasicAttack && _ringUnit < 0
            && (key == _targetHotkey || key == Win32.VK_RETURN) && UseAimedAbility()) return;

        // 어빌리티 목록을 안 열고도 1~4 를 누르면 그 줄의 어빌리티를 바로 고른다(Q·W·E·R 은 다른 단축키와 겹쳐 뺀다).
        if (key is >= '1' and <= '4' && IsPlayerTurn && !_abilityMenu && _targetWork < 0 && _ringUnit < 0 && !_units[_turn].IsBusy)
        {
            RingShortcut(RingCommand.Ability);
            if (_abilityMenu && OnAbilityMenuKey(key)) return;
        }

        switch (MoveActionFor(key) ?? _keys.ActionFor(key))
        {
            case KeyAction.Grid: _showGrid = !_showGrid; SaveSettings(); break;
            case KeyAction.Gauges: _showGauges = !_showGauges; SaveSettings(); break;
            case KeyAction.Ring: ToggleRingForSelected(); break;
            case KeyAction.Attack: RingShortcut(RingCommand.Attack); break;
            case KeyAction.Ability: RingShortcut(RingCommand.Ability); break;
            case KeyAction.Rest: RingShortcut(RingCommand.Rest); break;
            case KeyAction.Status:
                if (_ringUnit >= 0) RingShortcut(RingCommand.Status);
                else if ((uint)_selected < _units.Length) _statusUnit = _selected;
                break;
            case KeyAction.Item: RingShortcut(RingCommand.Item); break;
            case KeyAction.System:
                if (_ringUnit >= 0) RingShortcut(RingCommand.System);
                else OpenSystemMenu();
                break;
            case KeyAction.NextUnit:
                for (int i = 1; i <= _units.Length; i++)
                {
                    int next = (Math.Max(_selected, 0) + i) % _units.Length;
                    if (_units[next].Alive && _units[next].IsAlly) { _selected = next; break; }
                }
                break;
            case KeyAction.MoveUp or KeyAction.MoveDown or KeyAction.MoveLeft or KeyAction.MoveRight:
                if (_ringUnit >= 0 || _abilityMenu || _targetWork >= 0) break;   // 링·목록이 열려 있거나 대상을 고르는 중이면 걷지 않는다
                _heldMoveKeys.Remove(key);
                _heldMoveKeys.Add(key);   // 마지막에 누른 키가 맨 뒤 — 그 방향을 따른다
                StepByKey(key);
                break;
        }
    }

    /// <summary>걷기 키 — 방향키는 늘, 그 밖은 단축키 표에서.</summary>
    private KeyAction? MoveActionFor(int key) => key switch
    {
        Win32.VK_UP => KeyAction.MoveUp,
        Win32.VK_DOWN => KeyAction.MoveDown,
        Win32.VK_LEFT => KeyAction.MoveLeft,
        Win32.VK_RIGHT => KeyAction.MoveRight,
        _ => _keys.ActionFor(key) is KeyAction.MoveUp or KeyAction.MoveDown or KeyAction.MoveLeft or KeyAction.MoveRight ? _keys.ActionFor(key) : null,
    };

    /// <summary>
    /// 클릭: 공격 고르는 중이면 그 적을 친다. 인물이면 그 인물을 고르고(수치·영역 보기),
    /// 차례인 아군의 파란 칸이면 거기까지 걷는다. 빈 칸이면 차례인 인물로 선택을 되돌린다.
    /// </summary>
    /// <summary>
    /// 전투가 끝나고 배너가 떠 있을 때 — 이기고 이어지는 전투가 있으면 그 전투로(이벤트 행동 10),
    /// 없거나 졌으면 모세스 화면으로 간다(mo-1). 키든 클릭이든 한 번이면 넘어간다(원본도 배너를 눌러 건너뛴다, 분석-전투).
    /// </summary>
    private void LeaveFinishedBattle()
    {
        // 결과와 행선지는 <b>한 번만</b> 쓴다 — 전에는 남아 있어서, 연대표(모세스가 아님)에서 에피소드를 누르면
        // 그 클릭이 다시 「배너 넘기기」가 되어 Btl 0137 의 끝 필드 55 가 또 열렸다(사용자 보고).
        bool won = _outcome.StartsWith('승');
        int nextField = _eventNextField;
        _outcome = "";
        _eventNextField = 0;
        // 상태이상은 그 전투에서만 간다 — 판에 남은 유닛에 붙어 있으면 모세스 스테이터스 창에 그대로 보였다(사용자 보고).
        foreach (var u in _units) u.ClearStatus();
        if (won) ApplyBattleFlags(_scene.Id);   // 이긴 전투가 세우는 진행 깃발
        // 이어지는 전투는 이벤트 행동 10 이 정한 것이 먼저다(Btl 자료의 값은 그 다음).
        int next = _eventNextBattle > 0 ? _eventNextBattle : _scene.NextBattle;
        _eventNextBattle = 0;
        if (won && next > 0 && StartBattle(next)) return;
        // 행동 6 은 전투를 끝내고 그 필드로 보낸다.
        if (won && nextField > 0 && OpenField(nextField)) return;
        OpenMoses();
    }

    private void OnClick(int clientX, int clientY)
    {
        if (_progressOpen) { var (px, py) = BoardPoint(clientX, clientY); OnProgressClick(px, py); return; }
        if (LevelUpOpen) { CloseLevelUp(); return; }
        if (_notice != null) { _notice = null; return; }     // 「저장되었습니다.」 같은 알림은 클릭으로 바로 닫는다
        // 배너는 클릭 한 번으로 넘긴다 — 전에는 키만 받아서 눌러도 바로 안 넘어갔다.
        if (_outcome.Length > 0 && !_mosesOpen && !FieldOpen && !_episodesOpen) { LeaveFinishedBattle(); return; }
        if (OnTalkInput()) return;            // 대사는 클릭 한 번으로 넘긴다
        if (SkipCurrentWait()) return;        // 컷씬(그림만 띄워 두고 기다리는 틈)도 클릭 한 번으로 넘긴다
        var (bx, by) = BoardPoint(clientX, clientY);
        if (OnFieldClick(bx, by)) return;
        if (OnRecordsClick(bx, by)) return;
        if (OnEpisodesClick(bx, by)) return;
        if (OnTitleClick(bx, by)) return;
        if (OnChaptersClick(bx, by)) return;
        if (OnItemMenuClick(bx, by)) return;
        if (OnMosesClick(bx, by)) return;
        if (OnKeysClick(bx, by) || OnSystemClick(bx, by) || OnStatusClick(bx, by) || OnRingClick(bx, by) || OnAbilityMenuClick(bx, by)) return;

        if (by < GridTop) return;
        int col = bx / TileW, row = RowAt(bx, by);
        if (row < 0) return;
        if (OnTargetClick(col, row)) return;

        int index = UnitAtBoard(bx, by);
        if (index >= 0)
        {
            // 적군은 고를 수 없다(fa-9) — 대신 내 차례면 클릭만으로 바로 공격한다(fa-12).
            if (_units[index].IsAlly) _selected = index;
            else if (IsPlayerTurn && !_units[_turn].IsBusy) QuickAttack(index);
            return;
        }
        if (TryTouchObject(col, row) || TryBreakObject(col, row)) return;
        if (_selected == _turn && TryWalkTo(col, row)) return;
        _selected = _turn;
    }

    /// <summary>
    /// 차례인 아군을 그쪽으로 돌려세우고, 이동 영역(파랑) 안이면 한 칸 걷게 한다(TP 는 행동할 때 한 번에 뺀다). 움직이는 중이면 무시한다.
    /// </summary>
    private void TryStep(Facing facing, int dx, int dy)
    {
        if (!IsPlayerTurn) return;
        _selected = _turn;
        var unit = _units[_turn];
        if (unit.IsBusy) return;

        unit.Facing = facing;
        int col = unit.Col + dx, row = unit.Row + dy;
        if ((uint)col >= Cols || (uint)row >= Rows || ComputeRange(unit) is not { } range) return;
        int index = row * Cols + col;
        if (!range.CanReach(index)) return;   // 이동 영역(차례 시작 자리 기준) 밖

        unit.BeginStep(col, row);
    }

    /// <summary>누르고 있는 이동 키(누른 순서). 키보드 반복 대신 이걸로 한 칸이 끝나는 즉시 다음 칸을 잇는다.</summary>
    private readonly List<int> _heldMoveKeys = [];

    private void StepByKey(int key)
    {
        switch (MoveActionFor(key))
        {
            case KeyAction.MoveUp: TryStep(Facing.Up, 0, -1); break;
            case KeyAction.MoveDown: TryStep(Facing.Down, 0, 1); break;
            case KeyAction.MoveLeft: TryStep(Facing.Left, -1, 0); break;
            case KeyAction.MoveRight: TryStep(Facing.Right, 1, 0); break;
        }
    }

    /// <summary>DUELDX_POSE=&lt;Chr&gt;:&lt;동작&gt;:&lt;L|R|U|D&gt; 면 그 인물이 그 동작을 그 방향으로 되풀이한다(화면 밖 그림 시험용 — 무기 층·이펙트 자리를 본다).</summary>
    private static readonly string? PoseHook = Environment.GetEnvironmentVariable("DUELDX_POSE");

    /// <summary>DUELDX_WORK=&lt;work 번호&gt;[:near] 면 첫 아군이 그 기술을 한 번 쓴다(화면 밖 이펙트 시험용 — 모션·이펙트 자리를 본다). near 면 적 셋을 곁으로 옮긴다.</summary>
    private static readonly string? WorkHook = Environment.GetEnvironmentVariable("DUELDX_WORK");

    private bool _workHookDone;

    /// <summary>DUELDX_SAVE=&lt;칸&gt; 이 걸어 둔 저장 — 한 번만 한다(화면 밖 시험용).</summary>
    private int? _saveSlotPending;

    /// <summary>DUELDX_SAVEAT=&lt;초&gt; 면 그때 저장한다(기본 3초) — 전투가 이어진 뒤를 저장해 보려고.</summary>
    private static readonly double SaveHookAt =
        double.TryParse(Environment.GetEnvironmentVariable("DUELDX_SAVEAT"), out double at) && at > 0 ? at : 3;

    /// <summary>DUELDX_AIM=&lt;work&gt; 면 플레이어 차례의 인물이 그 work 을 <b>제 칸에</b> 겨눠 누른 것처럼 한다(화면 밖 시험용 — 자기에게 쓰기).</summary>
    private bool _aimHookDone;

    private void ApplyAimHook()
    {
        if (_aimHookDone || !int.TryParse(Environment.GetEnvironmentVariable("DUELDX_AIM"), out int id)) return;
        if (!IsPlayerTurn || _routine != null || _talk != null || _runningEvent >= 0 || Work(id) is not { } w) return;
        _aimHookDone = true;
        var u = _units[_turn];
        (_targetWork, _targetIsBasicAttack) = (w.Id, false);
        u.Hp = Math.Max(1, u.Hp / 2);
        int before = u.Hp;
        bool took = OnTargetClick(u.Col, u.Row);
        if (Trace)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"aim hook: {u.ChrCode}({u.Col},{u.Row}) work {w.Id} tm {w.TargetMode} am {w.AreaMode} took {took} routine {_routine != null} toast '{_toast}' hp {before}" + Environment.NewLine);
    }

    private void ApplyWorkHook()
    {
        ApplyAimHook();
        if (WorkHook == null || _workHookDone || _routine != null || _units.Length == 0) return;
        if (_talk != null || _outcome.Length > 0) return;   // 대사가 끝나기를 기다린다
        if (_turnNo < 1) return;                            // 시작 사건(적이 나타나기 전)이 끝나기를 기다린다
        var parts = WorkHook.Split(':');
        if (!int.TryParse(parts[0], out int id) || Work(id) is not { } w) return;
        // DUELDX_WORKBY=<Chr> 면 그 인물이 쓴다(없으면 첫 아군).
        int by = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_WORKBY"), out int chrBy) ? chrBy : 0;
        int caster = Array.FindIndex(_units, u => u.Alive && u.OnField && u.IsAlly && (by == 0 || u.ChrCode == by));
        if (caster < 0) return;
        _workHookDone = true;
        var a = _units[caster];
        // 「<work>:near」 면 적 셋을 시전자 곁 칸으로 옮겨 둔다 — 자기 중심 범위기(나인 크루세이더)를 시험할 때 대상이 있게.
        if (parts.Length > 1 && parts[1] == "near")
        {
            (int C, int R)[] spots = [(a.Col + 2, a.Row), (a.Col, a.Row + 2), (a.Col + 1, a.Row - 2)];
            int k = 0;
            foreach (var enemy in _units.Where(u => u.Alive && u.OnField && !u.IsAlly))
            {
                if (k >= spots.Length) break;
                var (c, r) = spots[k++];
                if (c < 0 || r < 0 || c >= Cols || r >= Rows) continue;
                enemy.WarpTo(c, r);
            }
        }
        int target = Array.FindIndex(_units, u => u.Alive && u.OnField && !u.IsAlly);
        // 자기 중심 기술은 게임처럼 대상 없이(−1) 제 칸에 쓴다(UseSelfCentredWork).
        if (w.SelfCentred) { _routine = UseWorkRoutine(caster, w, -1, a.Col, a.Row, []); return; }
        _routine = UseWorkRoutine(caster, w, target,
                                  target >= 0 ? _units[target].Col : a.Col,
                                  target >= 0 ? _units[target].Row : a.Row, []);
    }

    private void ApplyPoseHook()
    {
        if (PoseHook == null || _units.Length == 0) return;
        var parts = PoseHook.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int chr) || !int.TryParse(parts[1], out int action)) return;
        foreach (var u in _units)
        {
            if (u.ChrCode != chr || !u.Alive) continue;
            if (parts.Length > 2) u.Facing = parts[2] switch { "R" => Facing.Right, "U" => Facing.Up, "D" => Facing.Down, _ => Facing.Left };
            if (u.Action < 0) PlayAction(u, action);
        }
    }

    /// <summary>배경 읽기가 끝난 뒤 주 스레드에서 한 번 돌릴 시험 훅(DUELDX_TITLE·MOSES·FIELD·LEVELUP).</summary>
    private volatile bool _openHooksPending;

    private void Update(double dt)
    {
        if (_openHooksPending)
        {
            _openHooksPending = false;
            OpenTitleIfAsked();
            OpenMosesIfAsked();
            OpenFieldIfAsked();
            OpenLevelUpIfAsked();
            OpenSlotsIfAsked();
            // DUELDX_SAVE=<칸> 이면 화면이 다 선 뒤 그 칸에 한 번 저장한다(화면 밖 시험용 — 세이브에 무엇이 적히는지 본다).
            if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_SAVE"), out int saveSlot)) _saveSlotPending = saveSlot;
            // DUELDX_LOAD=<칸> 이면 그 세이브를 바로 불러온다(화면 밖 시험용). 모세스로 돌아오면 DUELDX_MOSESPAGE 도 따른다.
            if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_LOAD"), out int slot) && LoadBattleFrom(SlotPath(slot)) && _mosesOpen
                && int.TryParse(Environment.GetEnvironmentVariable("DUELDX_MOSESPAGE"), out int page))
            {
                if (page == 7) OpenMosesStyle(); else if (page == 6) OpenMosesLegion();
                else if (page == 1) { MosesGoPage(1); _mosesPage = 1; }   // 메일 — 배달까지 돈다
            }
            OpenStatusIfAsked();                  // 불러온 뒤에 연다 — 먼저 열면 불러오기가 창을 닫는다
        }
        // 시험용 저장은 모세스·타이틀에서도 되어야 한다 — 아래 이른 되돌아감보다 먼저 한다.
        // 화면이 다 서고 나서 저장한다 — 첫 틀에 하면 모세스가 아직 안 열려 전투로 적힌다.
        if (_saveSlotPending is { } pending && _lastTime > SaveHookAt) { _saveSlotPending = null; SaveBattleTo(SlotPath(pending)); }
        // 타이틀·연대표·모세스 화면에서는 전투가 뒤에서 돌면 안 된다 — 차례도 이벤트도 멈추고 화면만 그린다.
        // (모세스를 빼 두었더니 뒤에서 턴이 흘러 전투 대사가 떠 버렸고, 그 대사가 화면 클릭을 다 먹었다.)
        if (_titleOpen || _episodesOpen || _mosesOpen || _recordsOpen)
        {
            UpdateSounds();
            // 모세스가 떠 있는 동안 챕터 스크립트(대사·고르기가 든 사건)를 필드와 같은 실행기로 돌린다(원본 챕터 장면도 같은 실행기).
            if (_mosesOpen && !_chapterDone) { UpdateTalk(); UpdateField(); }
            return;
        }

        // 필드도 전투와 따로 도는 장면이다 — 스크립트만 돌리고 전투는 멈춘다.
        if (FieldOpen)
        {
            UpdateSounds();
            UpdateTalk();
            UpdateField();
            return;
        }

        foreach (var unit in _units) unit.Advance(dt * TicksPerSecond, dt);

        // 키를 누르고 있으면 한 칸이 끝난 그 프레임에 바로 다음 칸을 건다 — 멈칫하지 않고 걷기 컷도 이어진다.
        if (_heldMoveKeys.Count > 0 && _ringUnit < 0 && _statusUnit < 0 && !_keysOpen && !_abilityMenu && _targetWork < 0 && IsPlayerTurn && !_units[_turn].IsBusy)
            StepByKey(_heldMoveKeys[^1]);

        // 정해 둔 길이 있으면 한 칸씩 이어 걷는다(클릭 이동·공격 자리로 가기·적 AI).
        foreach (var unit in _units)
        {
            if (unit.IsMoving || !unit.Path.TryDequeue(out var cell)) continue;
            unit.Facing = cell.Col > unit.Col ? Facing.Right : cell.Col < unit.Col ? Facing.Left : cell.Row > unit.Row ? Facing.Down : Facing.Up;
            unit.BeginStep(cell.Col, cell.Row);
        }

        foreach (var unit in _units) unit.SettleIfStopped();
        ResolvePendingTouch();                  // 상자 옆까지 걸어간 인물이 멈췄으면 연다
        SyncFollowers();
        UpdateCamera(dt);
        ApplyPoseHook();
        ApplyWorkHook();
        SyncVirtualStatus();
        UpdateSounds();
        UpdateRing();
        UpdateTalk();
        StepEvent();
        UpdateTurn();
        RefreshMoveRange();
    }

    // ── 프레임 합성 ──────────────────────────────────────────────────────────

    /// <summary>DUELDX_PERF=1 이면 초마다 fps 와 합성·올리기·그리기 ms 를 <c>%TEMP%\dueldx_perf.log</c> 에 적는다(성능 확인용).</summary>
    private static readonly bool PerfLog = Environment.GetEnvironmentVariable("DUELDX_PERF") == "1";
    private readonly Stopwatch _perf = new();
    private double _pc, _pu, _pd; private int _pn; private double _pStart;

    private void Render()
    {
        if (!PerfLog) { Compose(); Upload(); Draw(); return; }
        _perf.Restart(); Compose(); double c = _perf.Elapsed.TotalMilliseconds;
        Upload(); double u = _perf.Elapsed.TotalMilliseconds;
        Draw(); double d = _perf.Elapsed.TotalMilliseconds;
        _pc += c; _pu += u - c; _pd += d - u; _pn++;
        if (_realTime - _pStart >= 1)
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_perf.log"),
                $"fps {_pn / (_realTime - _pStart):F0} compose {_pc / _pn:F1} upload {_pu / _pn:F1} draw {_pd / _pn:F1} ms, game {_lastTime:F1}s real {_realTime:F1}s" + Environment.NewLine);
            _pc = _pu = _pd = 0; _pn = 0; _pStart = _realTime;
        }
    }

    private void Compose()
    {
        Array.Fill(_fb, BgColor);
        DrawBackground();
        DrawMoveRange();
        DrawWorkRange();
        if (_showGrid) DrawGridLines();
        DrawObjects();
        DrawUnits();
        DrawBodyClones();
        DrawEffects();
        DrawMovies();
        DrawRipples();
        if (_showGauges) DrawGauges();
        DrawPopups();
        DrawNumbers();
        DrawCritFlash();
        DrawStatus();
        DrawHud();
        DrawRing();
        DrawAbilityMenu();
        DrawItemMenu();
        DrawStatusScreen();
        DrawTalk();
        DrawToast();
        DrawOutcomeBanner();
        DrawUnitInfo();
        if (!_mosesOpen) DrawSystem();
        DrawKeysPanel();
        DrawLevelUp();
        DrawMoses();
        DrawField();
        DrawRecords();
        DrawTitle();
        DrawEpisodes();
        DrawChapters();
        DrawSceneTag();
        DrawProgress();
    }

    private void DrawBackground()
    {
        // 판이 맵 크기가 아니면(모세스·타이틀 틀로 바꾼 뒤, 또는 배경 스레드가 맵을 갈아 끼우는 중) 그리지 않는다 —
        // 판 버퍼 길이와 맵 너비가 어긋나 AsSpan 이 밖으로 나가 죽었다(간헐).
        if (_map is not { } map || !BoardIsMap) return;

        int boardH = BoardPad + Rows * TileH;
        for (int y = 0; y < map.Height; y++)
        {
            int by = y + map.OriginY + BoardPad;
            if ((uint)by >= boardH) continue;

            int w = Math.Min(map.Width, BoardWidth);
            var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(map.Bgra.AsSpan(y * map.Width * 4, w * 4));
            src.CopyTo(_fb.AsSpan((GridTop + by) * BoardWidth, w));
        }
    }

    private void DrawGridLines()
    {
        // 칸마다 제 높이 자리에 네모를 친다 — 층이 다른 칸은 격자도 어긋나 절벽이 보인다.
        for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
                StrokeRect(col * TileW, CellTop(col, row), TileW + 1, TileH + 1, GridLine);
    }

    private void DrawUnits()
    {
        var sprites = _sprites;
        // 그림을 읽는 스레드가 <b>인물 배열을 통째로 갈아 끼운다</b>(그림 없는 인물을 빼면서) —
        // 그리는 동안 길이가 줄면 칸을 벗어난다. 한 벌을 붙잡아 놓고 그린다.
        var units = _units;

        // 아래 줄 인물이 위 줄 인물을 가리도록 발 위치(y) 순서로 그린다.
        foreach (int i in Enumerable.Range(0, units.Length).OrderBy(i => units[i].Y))
        {
            var unit = units[i];
            if (!unit.Alive || !unit.OnField) continue;
            var (footX, footY) = UnitFoot(unit);
            int headY = footY - TileH;

            if (sprites.TryGetValue(unit.ChrCode, out var sprite))
            {
                var frame = sprite.FrameFor(unit);
                // 맞을 때의 흰 번쩍임(물들이기 키)과 1픽셀 떨림(자리 키)은 모션 자료에 들어 있다(분석-전투 fg-10).
                var (clip, tick) = sprite.CurrentClip(unit);
                var tint = clip?.TintAt(tick);
                var (ox, oy) = clip?.OffsetAt(tick) ?? (0, 0);
                // 몸 그림도 모션표의 섞기 키(종류 3)를 따른다 — 17 이면 더하기. 장교(Obs 0165·0863)는 걸을 때 몸이 빛 공으로 바뀌는데,
                // 불투명으로 찍어 공 둘레의 검은 부분이 원판처럼 남았다(사용자 보고, Btl 0256). 딸린 층·필드 소품은 이미 이렇게 그린다.
                bool additive = clip?.BlendAt(tick) == 17;
                BlitMasked(frame.Px, frame.W, frame.H, footX + frame.X + ox, footY + frame.Y + oy, tint, StatusTintOf(unit), unit.Fade, additive);
                headY = footY + frame.Y;
                DrawUnitLayers(clip, tick, footX + ox, footY + oy, unit.Facing == Facing.Right, unit.Fade, loop: unit.Action < 0);
            }
            if (i == _turn && _outcome.Length == 0) DrawTurnMarker(footX, headY);
        }
    }

    /// <summary>
    /// 차례인 인물 머리 위의 반짝이는 역삼각형(원본 화면 캡처, 구현 노트 ui-2). 폭 13·높이 9 픽셀, 연보라, 밝기가 오르내린다.
    /// </summary>
    private void DrawTurnMarker(int x, int headY)
    {
        const int W = 13, H = 9, Gap = 4;
        uint alpha = (uint)(150 + 105 * (0.5 + 0.5 * Math.Sin(_lastTime * Math.PI * 2 * 1.5)));
        uint fill = alpha << 24 | 0xE0D8FF, edge = alpha << 24 | 0x6050A0;
        int top = headY - Gap - H;
        for (int row = 0; row < H; row++)
        {
            int half = (W / 2) * (H - 1 - row) / (H - 1);
            for (int dx = -half; dx <= half; dx++)
                SetPixel(x + dx, top + row, dx == -half || dx == half || row == 0 ? edge : fill);
        }
    }

    /// <summary>인물 발 자리(판 픽셀) — 걷는 중이면 두 칸 사이.</summary>
    private (int X, int Y) UnitFoot(UnitState unit) =>
        ((int)(unit.X * TileW) + TileW / 2,
         GridTop + BoardPad + (int)(unit.Y * TileH) + TileH / 2 - (int)Math.Round(HeightPxAt(unit.X, unit.Y)));

    private void DrawStatus()
    {
        // 설정 > 상단 상태 줄 보이기(기본 끔) — 꺼 두어도 자료를 못 읽었다는 경고는 보인다.
        if (!_showStatusBar)
        {
            if (!_loading && _loadError.Length > 0) DrawText($"못 읽은 자료가 있습니다: {_loadError}", _camX + 4, _camY + 4, 0xFFD05050);
            return;
        }
        FillRect(_camX, _camY, ViewWidth, GridTop, _camY > 0 ? 0xE014100C : 0);
        if (_loading)
        {
            DrawText("전투 자료를 읽는 중...", _camX + 4, _camY + 4, White);
            return;
        }

        // 아직 안 나온 사람(배치 (0,0))은 세지 않는다 — 판에 보이는 수와 맞아야 한다.
        int allies = _units.Count(u => u.Alive && u.OnField && u.IsAlly);
        int enemies = _units.Count(u => u.Alive && u.OnField && !u.IsAlly);
        // 보이는 폭이 640 이라 짧게 — 자세한 단축키는 설정 메뉴에서.
        DrawText($"{_scene.Title} — Btl {_scene.Id:D4}   아군 {allies}  적군 {enemies}   {KeyBindings.KeyName(_keys[KeyAction.NextUnit])}: 인물  {KeyBindings.KeyName(_keys[KeyAction.Grid])}: 격자  {KeyBindings.KeyName(_keys[KeyAction.Gauges])}: 체력바",
                 _camX + 4, _camY + 4, White);
        if (_loadError.Length > 0) DrawText($"못 읽은 자료가 있습니다: {_loadError}", _camX + 4, _camY + 20, 0xFFD05050);
        else DrawText(TurnLine(), _camX + 4, _camY + 20, 0xFFFFE8A0);
    }

    // ── 글자 ─────────────────────────────────────────────────────────────────

    private (uint[] Px, int W, int H) GetText(string text, uint argb, float size = 13f)
    {
        string key = text + ":" + argb + ":" + size;
        if (_textCache.TryGetValue(key, out var cached)) return cached;

        var color = Color.FromArgb((int)argb);
        var made = TextRaster.Render(text, color, size) ?? ([], 0, 0);
        if (_textCache.Count > 500) _textCache.Clear();
        _textCache[key] = made;
        return made;
    }

    private void DrawText(string text, int x, int y, uint argb, float size = 13f)
    {
        if (text.Length == 0) return;
        var (px, w, h) = GetText(text, argb, size);
        if (w > 0) BlitMasked(px, w, h, x, y);
    }

    // ── 그림 원소 ────────────────────────────────────────────────────────────

    private void SetPixel(int x, int y, uint color)
    {
        if ((uint)x >= BoardWidth || (uint)y >= BoardHeight) return;
        uint a = color >> 24;
        if (a == 0) return;

        int idx = y * BoardWidth + x;
        _fb[idx] = a == 0xFF ? color : Blend(_fb[idx], color, a);
    }

    private static uint Blend(uint bg, uint fg, uint a)
    {
        uint br = (byte)(((bg >> 16 & 0xFF) * (255 - a) + (fg >> 16 & 0xFF) * a) / 255);
        uint bgc = (byte)(((bg >> 8 & 0xFF) * (255 - a) + (fg >> 8 & 0xFF) * a) / 255);
        uint bb = (byte)(((bg & 0xFF) * (255 - a) + (fg & 0xFF) * a) / 255);
        return 0xFF000000u | (br << 16) | (bgc << 8) | bb;
    }

    /// <summary>
    /// 컷 한 장을 찍는다. <paramref name="tint"/> 가 있으면 물들인다 — 방식 k(1~7)는 (n·픽셀 + (31−n)·세기)/31,
    /// 방식 3 은 n = 19 로 맞을 때의 흰 번쩍임이다(분석-전투 "맞는 효과").
    /// </summary>
    /// <param name="fade">0~1 의 밝기 — 이스케이프처럼 인물이 사라졌다 나타날 때 쓴다(<see cref="UnitState.Fade"/>). 1 이면 그대로 그린다.</param>
    private void BlitMasked(uint[] src, int srcW, int srcH, int dstX, int dstY, (int Mode, int Strength)? tint = null,
                            (byte[] R, byte[] G, byte[] B)? status = null, double fade = 1, bool additive = false)
    {
        if (fade <= 0) return;
        int n = tint is { } t ? t.Mode switch { 1 => 27, 2 => 23, 3 => 19, 4 => 15, 5 => 11, 6 => 7, 7 => 3, _ => 31 } : 31;
        int level = tint is { } tt ? Math.Clamp(tt.Strength, 0, 31) * 255 / 31 : 0;
        int k = fade < 1 ? Math.Clamp((int)(fade * 256), 0, 256) : 256;

        for (int y = 0; y < srcH; y++)
        {
            int dy = dstY + y;
            if ((uint)dy >= BoardHeight) continue;
            for (int x = 0; x < srcW; x++)
            {
                int dx = dstX + x;
                if ((uint)dx >= BoardWidth) continue;
                uint c = src[y * srcW + x];
                if ((c & 0xFF000000) == 0) continue;
                if (n < 31)
                {
                    uint Ch(int shift) => (uint)Math.Clamp((n * (int)(c >> shift & 0xFF) + (31 - n) * level) / 31, 0, 255);
                    c = c & 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
                }
                if (status is { } st) c = ApplyStatusTint(c, st);
                if (additive)
                {
                    // 더하기 합성 — 검정은 +0 이라 안 보인다. 흐려지는 중(fade)이면 덜 더한다.
                    int at = dy * BoardWidth + dx;
                    _fb[at] = AddColor(_fb[at], c, k);
                    continue;
                }
                if (k < 256)
                {
                    uint d = _fb[dy * BoardWidth + dx];
                    uint Mix(int shift) => (uint)(((int)(c >> shift & 0xFF) * k + (int)(d >> shift & 0xFF) * (256 - k)) / 256);
                    c = 0xFF000000 | Mix(16) << 16 | Mix(8) << 8 | Mix(0);
                }
                SetPixel(dx, dy, c);
            }
        }
    }

    private void FillRect(int x, int y, int w, int h, uint color)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                SetPixel(xx, yy, color);
    }

    private void StrokeRect(int x, int y, int w, int h, uint color)
    {
        for (int xx = x; xx < x + w; xx++) { SetPixel(xx, y, color); SetPixel(xx, y + h - 1, color); }
        for (int yy = y; yy < y + h; yy++) { SetPixel(x, yy, color); SetPixel(x + w - 1, yy, color); }
    }

    // ── D3D11 / DXGI ─────────────────────────────────────────────────────────

    private void CreateDevice()
    {
        var flags = DeviceCreationFlags.BgraSupport;
        var levels = new[] { FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels,
                                out ID3D11Device dev, out ID3D11DeviceContext ctx).CheckError();
        _device = dev;
        _ctx = ctx;

        CompileShaders();

        _boardTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)ViewWidth,
            Height = (uint)ViewHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
        });
        _boardSrv = _device.CreateShaderResourceView(_boardTex);
    }

    /// <summary>
    /// 화면을 그리는 셰이더 — <b>배율이 픽셀 셰이더에 박혀 있어</b>, 판 크기가 바뀌어 배율이 달라지면 다시 빌드해야 한다.
    /// </summary>
    private void CompileShaders()
    {
        string shader = $$"""
            Texture2D<float4> Board : register(t0);
            struct VSOut { float4 pos : SV_Position; };
            VSOut VS(uint id : SV_VertexID)
            {
                VSOut o;
                float2 uv = float2((id << 1) & 2, id & 2);
                o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
                return o;
            }
            float4 PS(VSOut i) : SV_Target
            {
                // board sits at (offset) inside the window; SV_Position is window-based (ASCII only: the compiler is given a char count)
                int2 uv = int2((i.pos.xy - float2({{ViewOffsetX}}, {{ViewOffsetY}})) / {{_zoom.ToString(System.Globalization.CultureInfo.InvariantCulture)}});
                return Board.Load(int3(uv, 0));
            }
            """;
        var vsBlob = Compiler.Compile(shader, "VS", "battlescene.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(shader, "PS", "battlescene.hlsl", "ps_4_0");
        _vs?.Dispose();
        _ps?.Dispose();
        _vs = _device.CreateVertexShader(vsBlob.Span);
        _ps = _device.CreatePixelShader(psBlob.Span);
        _shaderZoom = _zoom;
        _shaderOffset = (ViewOffsetX, ViewOffsetY);
    }

    /// <summary>지금 셰이더에 박혀 있는 판 자리 — <see cref="ViewOffsetX"/>·<see cref="ViewOffsetY"/> 와 달라지면 다시 빌드한다.</summary>
    private (int X, int Y) _shaderOffset;

    /// <summary>지금 셰이더에 박혀 있는 배율 — <see cref="_zoom"/> 과 달라지면 다시 빌드한다.</summary>
    private double _shaderZoom;

    private void CreateSwapChain()
    {
        int w = _resW, h = _resH;
        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();
        var desc = new SwapChainDescription1
        {
            Width = (uint)w,
            Height = (uint)h,
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.FlipDiscard,
            Scaling = Scaling.None,
            Flags = SwapChainFlags.FrameLatencyWaitableObject,
        };
        _swapChain = factory.CreateSwapChainForHwnd(_device, _hwnd, desc);
        using (var swapChain2 = _swapChain.QueryInterface<IDXGISwapChain2>())
        {
            swapChain2.MaximumFrameLatency = 1;
            _frameWait = swapChain2.FrameLatencyWaitableObject;
        }
        using var back = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _backBufferRtv = _device.CreateRenderTargetView(back);
    }

    private void Upload()
    {
        var map = _ctx.Map(_boardTex, 0, MapMode.WriteDiscard);
        try
        {
            for (int y = 0; y < ViewHeight; y++)
            {
                var dst = new Span<uint>((void*)(map.DataPointer + y * map.RowPitch), ViewWidth);
                _fb.AsSpan((_camY + y) * BoardWidth + _camX, ViewWidth).CopyTo(dst);
            }
        }
        finally { _ctx.Unmap(_boardTex, 0); }
    }

    private void Draw()
    {
        int w = (int)(ViewWidth * _zoom), h = (int)(ViewHeight * _zoom);
        _ctx.OMSetRenderTargets(_backBufferRtv);
        _ctx.ClearRenderTargetView(_backBufferRtv, new Vortice.Mathematics.Color4(0, 0, 0, 1));
        _ctx.RSSetViewport(ViewOffsetX, ViewOffsetY, w, h);   // 판이 창보다 작으면 가운데
        _ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _ctx.VSSetShader(_vs);
        _ctx.PSSetShader(_ps);
        _ctx.PSSetShaderResources(0, [_boardSrv]);
        _ctx.Draw(3, 0);
        _swapChain.Present(1, PresentFlags.None);
    }

    public void Dispose()
    {
        if (_active == this) _active = null;
        _mixer.Dispose();
        DestroyCursors();
        _backBufferRtv?.Dispose();
        if (_frameWait != IntPtr.Zero) { Win32.CloseHandle(_frameWait); _frameWait = IntPtr.Zero; }
        _swapChain?.Dispose();
        _boardSrv?.Dispose();
        _boardTex?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
        _ctx?.Dispose();
        _device?.Dispose();
        if (_hwnd != IntPtr.Zero) { Win32.DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }
}

/// <summary>몸짓 한 컷 — BGRA 픽셀과, 발 자리에서 그림 왼쪽 위까지의 거리(X·Y).</summary>
internal sealed record SpriteFrame(uint[] Px, int W, int H, int X, int Y)
{
    public static SpriteFrame From(ObsFrame f)
    {
        var px = new uint[f.Width * f.Height];
        Buffer.BlockCopy(f.Bgra, 0, px, 0, f.Bgra.Length);
        return new SpriteFrame(px, f.Width, f.Height, f.X, f.Y);
    }

    /// <summary>좌우로 뒤집은 컷 — 발 자리를 축으로 뒤집으니 X 도 따라 옮긴다.</summary>
    public SpriteFrame Mirrored()
    {
        var px = new uint[Px.Length];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                px[y * W + x] = Px[y * W + (W - 1 - x)];
        return new SpriteFrame(px, W, H, -(X + W), Y);
    }
}

/// <summary>인물 하나의 컷 전부와 걷기 표. 오른쪽 컷은 처음 쓸 때 뒤집어 둔다.</summary>
internal sealed class UnitSprite
{
    private readonly Dictionary<(int Sub, int Slot), SpriteFrame> _frames = [];
    private readonly Dictionary<(int Sub, int Slot), SpriteFrame> _mirrored = [];
    private readonly SpriteFrame _first;
    private readonly ObsMotionTable? _table;

    public UnitSprite(IReadOnlyList<ObsMotion> motions, ObsMotionTable? table)
    {
        _table = table;
        // 컷은 장 번호로 담는다(모션표 키가 장 번호다 — 분석-모션 「Obs 파일 갈래」).
        foreach (var motion in motions)
            foreach (var frame in motion.Frames)
                _frames[(motion.Id, frame.SlotId)] = SpriteFrame.From(frame);
        _first = SpriteFrame.From(motions[0].Frames[0]);
    }

    /// <summary>
    /// 지금 보일 컷 — 걷는 중이면 걷기(동작 1), 아니면 서기(동작 0, 까딱이는 숨쉬기) 모션을 틱에 맞춰 넘긴다.
    /// 오른쪽을 보면 옆모습 컷을 뒤집는다.
    /// </summary>
    /// <summary>지금 재생 중인 모션(물들이기·자리 키까지 들어 있다)과 그 틱.</summary>
    public (ObsMotionClip? Clip, int Tick) CurrentClip(UnitState unit)
    {
        return (_table?.Resolve(ActionOf(unit), ObsMotionTable.DirectionOf(unit.Facing)), (int)(unit.AnimTime * BattleSceneWindow.TicksPerSecond));
    }

    /// <summary>
    /// 지금 재생할 동작 — 걷는 중이면 걷기(1), 아니면 서기(0). 다만 <b>걷기 그림이 한 컷뿐인 인물</b>(카르마타처럼
    /// 자료에 걷는 그림이 없는 쪽)은 걷는 동안에도 서기 모션을 돌려 미끄러지듯 굳어 보이지 않게 한다.
    /// </summary>
    private int ActionOf(UnitState unit)
    {
        if (unit.Action >= 0) return unit.Action;
        if (!unit.IsMoving) return ObsMotionTable.ActionStand;
        var walk = _table?.Resolve(ObsMotionTable.ActionWalk, ObsMotionTable.DirectionOf(unit.Facing));
        return walk is { Keys.Count: > 1 } ? ObsMotionTable.ActionWalk : ObsMotionTable.ActionStand;
    }

    /// <summary>걷기 모션에 달린 소리 키들(시작 틱, Snd 번호).</summary>
    public IReadOnlyList<(int Start, int Sound)> WalkSounds(Facing facing) =>
        _table?.Resolve(ObsMotionTable.ActionWalk, ObsMotionTable.DirectionOf(facing))?.Sounds ?? [];

    public SpriteFrame FrameFor(UnitState unit)
    {
        int action = ActionOf(unit);
        var clip = _table?.Resolve(action, ObsMotionTable.DirectionOf(unit.Facing));
        var key = clip?.KeyAt((int)(unit.AnimTime * BattleSceneWindow.TicksPerSecond), loop: unit.Action < 0);
        if (key is not { } k || !_frames.TryGetValue((k.SubentryId, k.Slot), out var frame)) return _first;
        if (unit.Facing != Facing.Right) return frame;

        if (!_mirrored.TryGetValue((k.SubentryId, k.Slot), out var mirrored))
            _mirrored[(k.SubentryId, k.Slot)] = mirrored = frame.Mirrored();
        return mirrored;
    }

    /// <summary>모션 번호(동작·방향이 아니라 Obs 안의 번호)로 그 틱의 컷 — 몸 복제(분신) 이펙트가 쓴다. 없으면 null.</summary>
    public SpriteFrame? FrameOfMotion(int motion, int tick, bool mirror)
    {
        if (_table?.Clips.GetValueOrDefault(motion) is not { } clip || clip.KeyAt(tick, loop: false) is not { } k
            || !_frames.TryGetValue((k.SubentryId, k.Slot), out var frame)) return null;
        if (!mirror) return frame;
        if (!_mirrored.TryGetValue((k.SubentryId, k.Slot), out var mirrored))
            _mirrored[(k.SubentryId, k.Slot)] = mirrored = frame.Mirrored();
        return mirrored;
    }

    /// <summary>동작·방향의 모션(소리 키까지 들어 있다). 없으면 null.</summary>
    public ObsMotionClip? Clip(int action, Facing facing) => _table?.Resolve(action, ObsMotionTable.DirectionOf(facing));

    /// <summary>
    /// 그 동작에서 <b>타격이 나는 순간</b>(초) — 모션에 붙은 첫 소리 키 자리, 없으면 길이의 60%.
    /// 원본도 때리는 소리와 함께 피해가 뜬다(분석-사운드·분석-모션).
    /// </summary>
    public double HitMomentSeconds(int action, Facing facing)
    {
        if (_table?.Resolve(action, ObsMotionTable.DirectionOf(facing)) is not { } clip) return 0;
        // 치는 동작이 뜨고 아주 잠깐(0.05초) 뒤에 피해가 뜬다 — 사용자가 원본을 보고 알려 준 감각이다.
        // 모션이 그보다 짧으면 모션 길이를 넘지 않는다.
        return Math.Min(0.05, Math.Max(0, clip.Length - 1) / BattleSceneWindow.TicksPerSecond);
    }

    /// <summary>한 번 재생할 동작의 길이(초). 모션표에 없으면 0.</summary>
    public double ActionSeconds(int action, Facing facing) =>
        (_table?.Resolve(action, ObsMotionTable.DirectionOf(facing))?.Length ?? 0) / BattleSceneWindow.TicksPerSecond;
}

/// <summary>판 위 인물 하나의 지금 상태 — 칸 자리, 바라보는 쪽, 걷는 중이면 어디서 어디로 얼마나 왔는지.</summary>
internal sealed class UnitState(DemoUnit unit)
{
    public int ChrCode { get; } = unit.ChrCode;

    /// <summary>편 3·4 는 내 쪽이다 — 이벤트 행동 708 이 편을 바꾸면 이 값도 따라 바뀐다.</summary>
    public bool IsAlly => Side >= 3;

    /// <summary>Btl 레코드의 편 번호 — 4 내 부대 · 3 동맹 · 0~2 적. 이벤트 조건이 이 번호로 부대를 고른다.</summary>
    public int Side { get; set; } = unit.Side;

    /// <summary>Btl 배치표에서의 레코드 번호 — 이벤트가 <c>10000+N</c> 으로 가리키는 번호. 부하는 대장 것을 물려받는다.</summary>
    public int Record { get; } = unit.Record;

    /// <summary>플레이어가 직접 움직이는가 — 편 4 만 그렇다. 편 3(동맹)은 제 차례에 AI 가 움직인다(ba-6).</summary>
    public bool PlayerControlled { get; } = unit.PlayerControlled;

    /// <summary>For.dat 군단 번호(Btl 레코드 파일 15) — 0 이 아니면 부하들이 진형을 지어 따라다닌다.</summary>
    public int LegionId { get; set; } = unit.Legion;

    /// <summary>부하면 대장의 자리 번호, 대장·혼자면 −1. 부하는 차례를 안 받는다(분석-군단).</summary>
    public int LeaderIndex { get; set; } = -1;

    /// <summary>대장일 때 진형에서 내 자리(부하마다 0~5).</summary>
    public int FormationSlot { get; set; } = -1;

    /// <summary>군단 세력(원본 <c>CChr+0x148[군단]</c>) — 처음 1000, 대장이 죽어 물려받으면 0.6배.</summary>
    public int LegionPowerPercent { get; set; } = 1000;

    /// <summary>도착할(걷는 중이면 향하는) 칸. 자리 차지 판정도 이 칸으로 한다.</summary>
    /// <summary>배치 레코드의 레벨 보정 — 파티 레벨에 더해 그 인물의 레벨을 낸다.</summary>
    public int LevelOffset { get; } = unit.LevelOffset;

    /// <summary>AI 이동 방식·깨어남 조건 — 배치 레코드가 사람마다 들고 있다.</summary>
    public int AiMove { get; } = unit.AiMove;

    public int WakeCondition { get; } = unit.WakeCondition;

    public int WakeValue { get; } = unit.WakeValue;

    /// <summary>깨어났나 — 깨기 전에는 제 차례마다 쉬기만 한다. 사람이 움직이는 인물은 늘 깨어 있다.</summary>
    public bool Awake { get; set; } = unit.Side >= 3;

    /// <summary>
    /// 지금 전장에 서 있나 — <b>배치 칸이 (0,0) 이면 「아직 안 나온 사람」</b>이다.
    /// </summary>
    /// <remarks>
    /// 원본 로더는 그런 줄의 자리를 <c>(−100,−100)</c> 으로 덮어써 맵 밖으로 보낸다(<c>0x10062263</c>).
    /// 그래서 전멸 판정·차례·이벤트 조건이 그들을 세지 않는다. 이벤트 행동 200·214 가 불러들이고 201 이 내보낸다.
    /// 자료에 그런 줄이 <b>221개</b>(아군 편 제외), 그것을 부르는 전투가 <b>74개</b>다.
    /// </remarks>
    public bool OnField { get; set; } = unit.Col != 0 || unit.Row != 0;

    /// <summary>
    /// 이 사람을 <b>마지막으로 때린</b> 사람 — 원본 <c>유닛+0xfc</c>. 전투 내내 안 지운다.
    /// </summary>
    /// <remarks>이벤트 조건 301 이 「누가 마지막에 때렸나」를 이 값으로 본다. 죽은 뒤에도 남아 있어야 한다.</remarks>
    public UnitState? LastHitBy { get; set; }

    public int Col { get; private set; } = unit.Col;
    public int Row { get; private set; } = unit.Row;
    public Facing Facing { get; set; } = unit.Facing;

    private int _fromCol = unit.Col, _fromRow = unit.Row;
    private double _progress = 1;

    public bool IsMoving => _progress < 1;

    /// <summary>그릴 밝기(0~1) — 이스케이프(work 1583)가 겨눈 칸으로 사라졌다 나타날 때만 코드가 손으로 움직인다.</summary>
    public double Fade { get; set; } = 1;

    // ── 전투 수치 (게임 표를 읽은 뒤 채운다) ──
    public CharacterData? Data { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Tp { get; set; }
    public int MaxTp { get; set; }
    public int Stp { get; set; }
    public int Soul { get; set; }
    public int MaxSoul { get; set; }
    public int Ctp => Data?.Ctp ?? 0;
    public bool HasTurn { get; set; }
    public bool Alive { get; set; } = true;

    /// <summary>전투를 시작한 칸 — RESTART 로 되돌릴 때 쓴다.</summary>
    public int StartCol { get; } = unit.Col;
    public int StartRow { get; } = unit.Row;

    /// <summary>전투를 시작할 때 보는 쪽 — Btl 레코드의 방향(파일 8, <c>SetAction(0, 방향)</c>: 0 위 · 1 왼 · 2 아래 · 3 오른).</summary>
    public Facing StartFacing { get; } = unit.Facing;

    /// <summary>
    /// 전투를 처음부터 다시 할 때 — 자리·상태를 처음으로 돌린다(수치는 InitBattle 이 다시 채운다).
    /// 보는 쪽은 Btl 에 적힌 방향으로 되돌린다 — 전에는 아군은 오른쪽·적은 왼쪽으로 박아서, 오른쪽에서 시작하는
    /// 아군(Btl 0281 의 란·베라모드, 방향 1)도 벽을 보고 섰다(사용자 보고). <paramref name="keepFacing"/> 면 지금 쪽을 둔다.
    /// </summary>
    public void ResetTo(int col, int row, bool keepFacing = false)
    {
        WarpTo(col, row);
        OriginCol = col;
        OriginRow = row;
        if (!keepFacing) Facing = StartFacing;
        Alive = true;
        HasTurn = true;
        Stance = 0;
        Action = -1;
        ClearStatus();
    }

    /// <summary>자세(<c>+0x4d4</c>) — 1 방어(work 516), 2 회피(work 515). 다음 차례가 오면 풀린다.</summary>
    public int Stance { get; set; }

    /// <summary>상태이상 칸 셋(<c>+0x4bf[3]</c> 번호 · <c>+0x4c2[3]</c> 값) — 분석-전투 「6. 상태이상」.</summary>
    public byte[] StatusId { get; } = new byte[3];
    public short[] StatusValue { get; } = new short[3];

    /// <summary>
    /// 칸마다 그 상태이상을 건 인물 — 원본에는 없다. 상태이상(22·23·24 사망 조건)으로 쓰러지면 처치 경험치를 이들이 나눠 받는다.
    /// 세이브에는 안 실린다(불러온 뒤에 쓰러지면 아무도 안 받는다).
    /// </summary>
    public UnitState?[] StatusSource { get; } = new UnitState?[3];

    /// <summary>슬롯이 아니라 전투용 보정으로 바로 더해지는 것들(번호 30·31·32·33·37·48).</summary>
    public int BonusDex { get; set; }
    public int BonusPsy { get; set; }
    public int BonusDep { get; set; }
    public int BonusMaxTp { get; set; }
    public int BonusMaxSoul { get; set; }
    public int BonusMaxHp { get; set; }

    /// <summary>그 상태이상이 걸려 있으면 값, 아니면 0. 44·45·46 은 「없음」이라 세지 않는다.</summary>
    public int Status(int id)
    {
        for (int i = 0; i < 3; i++)
            if (StatusId[i] == id && id is not (44 or 45 or 46)) return StatusValue[i];
        return 0;
    }

    public bool HasStatus(int id)
    {
        for (int i = 0; i < 3; i++) if (StatusId[i] == id && id is not (44 or 45 or 46)) return true;
        return false;
    }

    public void ClearStatus()
    {
        Array.Clear(StatusId);
        Array.Clear(StatusValue);
        Array.Clear(StatusSource);
        BonusDex = BonusPsy = BonusDep = BonusMaxTp = BonusMaxSoul = BonusMaxHp = 0;
    }

    /// <summary>차례를 시작한 칸 — 이동 영역과 걸음 비용을 이 칸에서 센다.</summary>
    public int OriginCol { get; set; } = unit.Col;
    public int OriginRow { get; set; } = unit.Row;

    /// <summary>앞으로 밟을 칸들 — 한 칸 다 걸으면 다음 칸을 꺼낸다.</summary>
    public Queue<(int Col, int Row)> Path { get; } = new();

    /// <summary>한 번 재생 중인 동작(공격 등). −1 이면 서기/걷기를 알아서 고른다.</summary>
    public int Action { get; private set; } = -1;

    private double _actionLeft;

    /// <summary>걷거나, 걸을 길이 남았거나, 동작을 재생하는 중.</summary>
    public bool IsBusy => IsMoving || Path.Count > 0 || Action >= 0;

    public void PlayAction(int action, double seconds)
    {
        Action = action;
        AnimTime = 0;
        _actionLeft = seconds;
    }

    /// <summary>지금 동작(서기/걷기)을 시작한 뒤 흐른 시간(초). 동작이 바뀌면 0 부터 다시 센다.</summary>
    public double AnimTime { get; private set; }

    // 인물마다 서기 숨쉬기가 한꺼번에 맞춰 움직이지 않게 시작 위치를 조금씩 흩뜨린다.
    private readonly double _idleOffset = (unit.Col * 7 + unit.Row * 13) % 10 / 10.0;

    /// <summary>그릴 자리(칸 단위, 소수) — 걷는 중이면 두 칸 사이.</summary>
    public double X => _fromCol + (Col - _fromCol) * _progress;
    public double Y => _fromRow + (Row - _fromRow) * _progress;

    /// <summary>방금 한 칸을 다 걸었는데 아직 다음 칸이 정해지지 않았다 — 이번 프레임 안에 이어 걸으면 걷기 컷을 잇는다.</summary>
    private bool _justArrived;

    /// <summary>이번 칸에서 남은 틱(한 칸 = <see cref="BattleSceneWindow.StepTicks"/>) — 이어 걸을 때 다음 칸에 넘겨 속도가 들쭉날쭉하지 않게 한다.</summary>
    private double _carry;

    /// <summary>이번 칸을 걷기 시작한 뒤 흐른 틱 — 자리는 <b>다 채운 틱</b>만큼만 나아간다(틱마다 같은 픽셀).</summary>
    private double _stepTicks;

    /// <summary>걷기 없이 바로 그 칸에 세운다(걸음 물리기).</summary>
    public void WarpTo(int col, int row)
    {
        Path.Clear();
        _justArrived = false;
        _fromCol = Col = col;
        _fromRow = Row = row;
        _progress = 1;
    }

    public void BeginStep(int col, int row)
    {
        if (!IsMoving && !_justArrived) AnimTime = 0;
        double carry = _justArrived ? _carry : 0;
        _justArrived = false;
        _fromCol = Col; _fromRow = Row;
        Col = col; Row = row;
        _stepTicks = Math.Min(carry, BattleSceneWindow.StepTicks - 1);
        _progress = Math.Min(Math.Floor(_stepTicks) / BattleSceneWindow.StepTicks, 0.99);
    }

    /// <param name="ticks">이번 프레임에 흐른 틱(초당 30).</param>
    public void Advance(double ticks, double dt)
    {
        AnimTime += dt;
        if (Action >= 0 && (_actionLeft -= dt) <= 0) { Action = -1; AnimTime = _idleOffset; }
        if (!IsMoving) return;
        _stepTicks += ticks;
        _progress = Math.Min(1, Math.Floor(_stepTicks) / BattleSceneWindow.StepTicks);
        if (!IsMoving) { _justArrived = true; _carry = _stepTicks - BattleSceneWindow.StepTicks; }
    }

    /// <summary>이어 걷지 않고 멈췄으면 서기 숨쉬기로 돌린다.</summary>
    public void SettleIfStopped()
    {
        if (!_justArrived) return;
        _justArrived = false;
        AnimTime = _idleOffset;
    }
}

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
internal sealed unsafe partial class GameWindow : IDisposable
{
    /// <summary>지금 벌이는 전투 — 자료에서 읽는다(<see cref="DemoScene"/>). 못 읽으면 예전 상수 그대로.</summary>
    internal DemoScene _scene = DemoScene.Fallback;

    /// <summary>한 칸 걸어가는 데 드는 시간(초).</summary>
    public const double StepSeconds = StepTicks / TicksPerSecond;

    /// <summary>
    /// 한 칸을 걷는 틱 수 — 원본은 평지에서 틱마다 8픽셀, 곧 <b>한 칸 5틱(0.167초)</b>이다(0x10073920 · 0x10073b4a mov eax,8, ba-20 P1).
    /// 전에는 8틱(틱마다 5픽셀)으로 원본보다 1.6배 느렸다. 틱마다 정수 픽셀로
    /// 똑같이 움직인다. 예전에는 0.25초를 시간으로 나눠 한 프레임에 2.67픽셀 — 판을 낮은 해상도로 그리니 2·3픽셀이 번갈아
    /// 속도가 프레임마다 출렁여 끊겨 보였다(사용자 보고).
    /// </summary>
    public const int StepTicks = 5;

    /// <summary>
    /// 모션표 한 틱의 길이 — 1초에 몇 틱. 원본 게임의 틱 빠르기는 아직 확인 못 해서 눈으로 맞춘 값이다.
    /// </summary>
    public const double TicksPerSecond = 30;

    internal const int TileW = ObtMap.CellWidth, TileH = ObtMap.CellHeight;
    // 판 크기는 <b>전투마다 맵을 따라</b> 바뀐다(0153 = 32×34, 0156 레이토스 = 37×71 …).
    // 맵을 바꿀 때 ResizeBoard 가 화면 버퍼·텍스처·창 크기를 다시 잡는다.
    internal int Cols = BattleDemoScene.Cols, Rows = BattleDemoScene.Rows;
    internal const int GridTop = 40;
    internal int BoardWidth => Cols * TileW;
    internal int BoardHeight => GridTop + BoardPad + Rows * TileH;

    // ── 칸 높이(고도) → 화면 (분석-전투 「자리 갱신 0x100ea910」: 화면 y = 월드 y×32/40 − 월드 z×12/20, 유닛 z = 칸 높이×20) ──

    /// <summary>칸 높이 한 층이 화면에서 위로 올라가는 픽셀 — 12.</summary>
    internal const int HeightStep = 12;

    /// <summary>지금 판이 맵 크기 그대로인가(타이틀·모세스 틀이면 아니다 — 그때는 높이·여백을 안 쓴다).</summary>
    internal bool BoardIsMap => _map is { } m && m.Cols == Cols && m.Rows == Rows;

    /// <summary>
    /// 맵 그림이 0줄보다 위로 나온 만큼(높은 칸이 위로 올라가 그려진 부분) 판 위에 덧대는 여백.
    /// 그림 원점이 −264 인 맵(Obt 0031)은 맨 윗줄이 22층 높이라 그만큼 위에 그려져 있다.
    /// </summary>
    internal int BoardPad => BoardIsMap ? Math.Max(0, -_map!.OriginY) : 0;

    internal int HeightPx(int col, int row) => BoardIsMap ? HeightStep * _map!.HeightAt(col, row) : 0;

    /// <summary>칸의 화면 윗줄 — 높이만큼 위로 올라간다.</summary>
    internal int CellTop(int col, int row) => GridTop + BoardPad + row * TileH - HeightPx(col, row);

    internal int CellCenterY(int col, int row) => CellTop(col, row) + TileH / 2;

    /// <summary>
    /// 한 칸을 걷는 틱 — 평지 5, 한 층 차 6, 두 층 차 8, 비탈(다음 칸 깃발 0x40)·뛰어넘기(두 층 차 + 0x20)는 10
    /// (걷기 0x10073920 의 높이 갈래 0x10073b54~0x10073d57, ba-20 P5). 틱별 깡충 뛰는 z 곡선은 따르지 않고 고르게 넘는다.
    /// </summary>
    internal int StepTicksBetween(int fromCol, int fromRow, int toCol, int toRow)
    {
        if (!BoardIsMap || _map is not { } map || (uint)toCol >= map.Cols || (uint)toRow >= map.Rows
            || (uint)fromCol >= map.Cols || (uint)fromRow >= map.Rows) return StepTicks;
        int diff = Math.Abs(map.HeightAt(toCol, toRow) - map.HeightAt(fromCol, fromRow));
        if (diff == 0) return StepTicks;
        int flags = Btl.CellFlagsAt(toCol, toRow);
        if ((flags & 0x40) != 0 || (diff >= 2 && (flags & 0x20) != 0)) return 10;
        return diff == 1 ? 6 : 8;
    }

    /// <summary>칸 사이를 걷는 인물의 높이 — 네 이웃 칸을 거리로 섞어 층 사이를 매끄럽게 넘는다.</summary>
    internal double HeightPxAt(double x, double y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        return HeightPx(x0, y0) * (1 - fx) * (1 - fy) + HeightPx(x0 + 1, y0) * fx * (1 - fy)
             + HeightPx(x0, y0 + 1) * (1 - fx) * fy + HeightPx(x0 + 1, y0 + 1) * fx * fy;
    }

    /// <summary>판 픽셀 (bx, by) 가 놓인 칸의 줄 — 높이 때문에 겹치면 아래 줄(나중에 그린 쪽)이 이긴다. 없으면 −1.</summary>
    internal int RowAt(int bx, int by)
    {
        int col = bx / TileW;
        if ((uint)col >= Cols) return -1;
        // 높이가 다른 칸들의 윗면이 같은 자리에 겹칠 수 있다 — 앞(아래) 칸부터 보되, 벽 칸(플래그 &9 — 못 들어가는 칸)은 뒤에 설 수 있는 칸이
        // 겹쳐 있으면 양보한다. 전에는 높은 벽 칸이 그 뒤 바닥 칸의 클릭을 가로챘다(Btl 0298 의 (21,12) 가 줄 16 으로 잡힘).
        int wall = -1;
        for (int row = Rows - 1; row >= 0; row--)
        {
            int top = CellTop(col, row);
            if (by < top || by >= top + TileH) continue;
            if (!_battleLoaded || _map is null || (Btl.CellFlagsAt(col, row) & 0x9) == 0 || Btl.ObjectAt(col, row) != null) return row;   // 물체가 선 칸은 벽이 아니다
            if (wall < 0) wall = row;
        }
        return wall;
    }

    /// <summary>맵 그림의 아랫끝(판 픽셀) — 카메라는 원본처럼 그림 밖으로 안 내려간다(맵 `+0x398~+0x39e` 로 자름).</summary>
    internal int PictureBottom => BoardIsMap ? GridTop + BoardPad + _map!.OriginY + _map.Height : BoardHeight;

    /// <summary>카메라가 내려갈 수 있는 끝.</summary>
    internal int CamMax => Math.Max(0, Math.Min(BoardHeight, PictureBottom) - ViewHeight);

    /// <summary>
    /// 창에 보이는 판 높이 — 판 전체의 70%, 다만 원본 화면 높이(480 + 머리줄)까지만. 나머지는 <see cref="_camY"/> 로 위아래로 스크롤한다.
    /// 세로로 긴 맵(Obt 0156 은 2272 픽셀)도 원본처럼 줌아웃하지 않고 스크롤한다.
    /// </summary>
    internal int ViewHeight => Math.Min(BoardHeight * 7 / 10, Math.Max(240, (int)Math.Round(_resH / _zoom)));

    /// <summary>설정 > 해상도 — <b>창 크기</b>(화면 픽셀). 원본은 640×480. 보이는 판은 이것을 배율로 나눈 만큼이다.</summary>
    internal int _viewW = UserSettings.Current.ViewW, _viewH = UserSettings.Current.ViewH;

    /// <summary>모니터에 들어가게 깎은 해상도(<see cref="FitZoom"/> 가 채운다) — 창 너비·(머리줄 뺀) 창 높이.</summary>
    internal int _resW = UserSettings.Current.ViewW, _resH = UserSettings.Current.ViewH;

    /// <summary>
    /// 창에 보이는 판 너비 — 원본 화면 너비 640 까지만. 더 넓은 맵(Btl 0131 같은 1480 픽셀)은 원본처럼 <b>줌아웃하지 않고</b>
    /// <see cref="_camX"/> 로 좌우 스크롤한다(차례인 인물을 따라간다).
    /// </summary>
    internal int ViewWidth => Math.Min(BoardWidth, Math.Max(320, (int)Math.Round(_resW / _zoom)));

    /// <summary>창 안에서 판 그림이 놓이는 자리(화면 픽셀) — 판이 창보다 작으면 가운데에 두고 둘레는 검게 남긴다.</summary>
    internal int ViewOffsetX => Math.Max(0, (_resW - (int)(ViewWidth * _zoom)) / 2);
    internal int ViewOffsetY => Math.Max(0, (_resH - (int)(ViewHeight * _zoom)) / 2);

    /// <summary>화면 픽셀 → 판 픽셀(카메라 더한 것).</summary>
    internal (int X, int Y) BoardPoint(int clientX, int clientY) =>
        ((int)Math.Floor((clientX - ViewOffsetX) / _zoom) + _camX, (int)Math.Floor((clientY - ViewOffsetY) / _zoom) + _camY);
    /// <summary>배율의 위아래 한계. 자동은 판이 창보다 작을 때(모세스·타이틀 640×480)만 창을 채우도록 키운다.</summary>
    internal const double MinZoom = 0.5, MaxZoomChosen = 4;

    /// <summary>화면 픽셀 ÷ 판 픽셀 — <see cref="FitZoom"/> 가 정한다.</summary>
    internal double _zoom;

    internal const string GameRoot = @"C:\Users\Administrator\Downloads\gen3pt2";

    internal const uint BgColor = 0xFF14100C;
    internal const uint GridLine = 0x40FFFFFF;
    internal const uint White = 0xFFF2EAD6;
    internal const uint DimGray = 0xFFA09888;

    internal ObtMapImage? _map;
    internal Dictionary<int, UnitSprite> _sprites = [];

    /// <summary>인물마다 읽어 둔 그림이 어느 레코드 그림(+0xc) 값으로 읽은 것인가 — 바뀌면 다시 읽는다.</summary>
    internal readonly Dictionary<int, int> _spriteCodes = [];
    internal UnitState[] _units = [.. DemoScene.Fallback.Roster.Select(u => new UnitState(u))];   // 자료를 읽으면 BuildUnits 로 다시 만든다
    internal int _selected = -1;
    internal readonly Dictionary<int, string> _names = [];
    internal string _loadError = "";
    internal volatile bool _loading = true;
    internal bool _showGrid = UserSettings.Current.ShowGrid;
    /// <summary>발밑 HP·TP 막대 — 원본에는 없어서 기본은 끔(H 키).</summary>
    internal bool _showGauges = UserSettings.Current.ShowGauges;

    internal uint[] _fb = [];
    internal readonly Dictionary<string, (uint[] Px, int W, int H)> _textCache = [];

    internal bool _running;
    internal double _lastTime;

    internal IntPtr _hwnd;
    internal static readonly bool Offscreen = Environment.GetEnvironmentVariable("DUELDX_OFFSCREEN") == "1";
    internal static readonly Win32.WndProc StaticWndProcDelegate = StaticWndProcTrampoline;
    internal static GameWindow? _active;
    internal static ushort _classAtom;
    internal const string ClassName = "BattleSceneDx";

    internal ID3D11Device _device = null!;
    internal ID3D11DeviceContext _ctx = null!;
    internal IDXGISwapChain1 _swapChain = null!;

    /// <summary>
    /// 스왑체인이 새 프레임을 받을 수 있게 되면 신호가 오는 핸들 — 이걸 기다린 <b>다음에</b> 입력을 읽고 그린다.
    /// 기본값(최대 3프레임 미리 쌓기)이면 CPU 가 노는 동안(합성은 2~5ms) 프레임이 줄을 서서 입력이 늦게 보였다.
    /// 줄은 1프레임 — 입력에서 화면까지 가장 짧다(화면 밖 시험에서는 2프레임보다 vsync 를 조금 더 놓쳤다).
    /// </summary>
    internal IntPtr _frameWait;
    internal ID3D11RenderTargetView _backBufferRtv = null!;
    internal ID3D11Texture2D _boardTex = null!;
    internal ID3D11ShaderResourceView _boardSrv = null!;
    internal ID3D11VertexShader _vs = null!;
    internal ID3D11PixelShader _ps = null!;

    // ── 게임 자료 읽기 ───────────────────────────────────────────────────────

    /// <summary>지금 전투에 나오는 인물들의 그림과 초상을 (없는 것만) 읽는다.</summary>
    /// <summary>
    /// 인물 폴더(<c>assets/characters/NNNN_이름</c>)의 파일 자리 — 설치판은 첫 챕터에 안 나오는 인물의 그림을 따로 받는다(AssetPack).
    /// 없으면 받을 때까지 기다렸다가 받은 자리를 돌려준다(끝내 없으면 본디 경로 — 부르는 쪽이 「못 읽음」으로 다룬다).
    /// </summary>
    internal static string CharacterFile(string path, int wait = 90)
    {
        string folder = Path.GetFileName(Path.GetDirectoryName(path) ?? ""), file = Path.GetFileName(path);
        if (Environment.GetEnvironmentVariable("DUELDX_ASSETLOG") is { Length: > 0 } log)
            try { File.AppendAllText(log, $"characters/{folder}/{file}" + Environment.NewLine); } catch (IOException) { }
        if (File.Exists(path)) return path;
        return AssetPack.Fetch($"characters/{folder}", file, wait) ?? path;
    }

    internal void LoadRosterSprites()
    {
        string assetsRoot = FindRepoAssetsRoot();
        var manifests = CollectExportedManifests(assetsRoot);
        var sprites = new Dictionary<int, UnitSprite>(_sprites);

        foreach (int chrCode in _units.Select(u => u.ChrCode).Distinct())
        {
            // 인물 레코드의 그림(+0xc)은 필드 행동 701 칸 0 이 바꾼다 — Fld 0088 이 살라딘을 건슬라이서 그림 1185 로 바꾼다.
            // 전에는 뽑아 둔 목록의 그림만 써서 바뀐 모션이 전투에 안 나왔다(사용자 보고: Btl 0150).
            // 전투를 막 세운 때에는 유닛의 자료(Data)가 아직 비어 있다(InitBattle 이 뒤에 채운다) — 그때는 명부(_party)의 그림을 본다.
            // 전에는 이때 「바뀐 그림 없음」으로 보아 옛 그림으로 섰고, 불러오면(자료가 찬 뒤 다시 읽어) 그제야 바뀐 그림이 떠서
            // 「저장했다 불러오면 복장이 바뀐다」로 보였다(사용자 보고: Btl 0050 의 죠안 — 챕터 10 이 그림을 441 로 놓았다).
            var shown = _units.FirstOrDefault(u => u.ChrCode == chrCode);
            int wanted = shown?.Data?.SpriteId ?? (shown is { IsAlly: true } ? _party.GetValueOrDefault(chrCode)?.SpriteId : null) ?? 0;
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
                if (wanted > 0 && Path.Combine(Path.GetDirectoryName(obsPath)!, CharacterExport.ObsFileName(wanted)) is var changedPath && CharacterFile(changedPath, 30) is var changed && File.Exists(changed))
                {
                    obsPath = changed;
                    if (BattleScene.Trace)
                        File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"sprite {chrCode}: record picture {wanted} → {obsPath}" + Environment.NewLine);
                }
                _spriteCodes[chrCode] = wanted;
                _names[chrCode] = name;
                obsPath = CharacterFile(obsPath);
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
            facePath = CharacterFile(facePath, wait: 20);
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
    internal const int TitleBoardCols = MosesScene.MosesW / ObtMap.CellWidth;
    internal const int TitleBoardRows = MosesScene.MosesH * 10 / 7 / ObtMap.CellHeight + 1;

    internal void LoadBoard()
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
    internal static bool WantsBattleAtStart(out int id)
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
    internal bool _battleLoaded;

    /// <summary>그 전투의 자료·맵을 읽고 판을 그 크기로 잡는다.</summary>
    internal void LoadBattleBoard(int id)
    {
        // DUELDX_LEGION=<Chr>:<군단> 이면 그 인물에게 군단을 배속하고 시작한다(화면 밖 시험용 — 부하·군단기).
        if (Environment.GetEnvironmentVariable("DUELDX_LEGION")?.Split(':') is [var lc, var ll] && int.TryParse(lc, out int lchr) && int.TryParse(ll, out int lid))
            Mos._unitLegion[lchr] = lid;
        // DUELDX_PARTYSPRITE=<Chr>:<Obs> 면 그 인물의 명부 그림을 바꿔 두고 시작한다(화면 밖 시험용 — 챕터가 701 로 복장을 바꾼 뒤의 전투 시작).
        if (Environment.GetEnvironmentVariable("DUELDX_PARTYSPRITE")?.Split(':') is [var pc, var po] && int.TryParse(pc, out int pchr) && ushort.TryParse(po, out ushort pobs)
            && (_party.GetValueOrDefault(pchr) ?? _db?.Character(pchr)) is { } record)
            _party[pchr] = record with { SpriteId = pobs };
        if (DemoScene.Load(id, _db) is { } loaded) _scene = loaded;
        if (Arena is { } arena) _scene = ArenaScene(_scene, arena.Chr);
        _map = ObtMap.Load(AssetPack.MapPath(_scene.MapFile));
        ResizeBoard(_map.Cols, _map.Rows);
        _units = Btl.BuildUnits(_scene);
        // 첫 판(시험 훅 포함)은 배치 단계 없이 — 새로 거는 전투(StartBattle)만 연다. DUELDX_DEPLOY=1 이면 첫 판에서도 연다(화면 밖 시험용).
        Btl.BeginDeployOrDrop(_scene, fresh: Environment.GetEnvironmentVariable("DUELDX_DEPLOY") == "1");
        if (Arena == null) Btl.LoadEvents(_scene.Id);      // 실험판은 그 전투의 사건(대사·증원·승패 조건)을 안 돌린다
    }

    /// <summary>
    /// 어빌리티 실험판(편집기의 「게임에서 실험」) — <c>DUELDX_ARENA=어빌리티:레벨[:Chr]</c> 이면 여는 전투(<c>DUELDX_BATTLE</c>, 편집기는 106)에
    /// 아군은 하나(기본 죠안 221)만 세우고 적은 그 전투 그대로 둔다. 그 아군은 <b>그 어빌리티 하나만</b> 그 레벨로 갖는다(SOUL·TP 가득, 차례마다 다시 채움). 사건은 안 돈다.
    /// </summary>
    internal static (int Ability, int Level, int Chr)? Arena { get; } =
        Environment.GetEnvironmentVariable("DUELDX_ARENA")?.Split(':') is { Length: >= 2 } parts
        && int.TryParse(parts[0], out int arenaAbility) && int.TryParse(parts[1], out int arenaLevel)
            ? (arenaAbility, Math.Max(1, arenaLevel), parts.Length > 2 && int.TryParse(parts[2], out int arenaChr) ? arenaChr : 221) : null;

    internal static DemoScene ArenaScene(DemoScene scene, int chr)
    {
        // 그 전투(편집기는 Btl 0106 을 연다 — 사용자 요청)의 적은 그대로 두고, 아군은 다 빼고 실험할 인물 하나만 세운다 —
        // 자리는 첫 아군 자리, 아군이 없는 전투면 첫 배치 칸.
        var enemies = scene.Roster.Where(u => !u.IsAlly).ToList();
        var (col, row) = scene.Roster.FirstOrDefault(u => u.Side == 4) is { } ally ? (ally.Col, ally.Row)
                       : scene.Placement is { Count: > 0 } spots ? (spots[0].Col, spots[0].Row)
                       : (Math.Max(0, scene.StartCol), Math.Max(0, scene.StartRow));
        var roster = new List<DemoUnit> { new(chr, col, row, 4, 0, Facing.Right) };
        roster.AddRange(enemies);
        return scene with { Title = "어빌리티 실험", Roster = [.. roster], NextBattle = 0, Placement = null, LegionsAllowed = false };
    }

    /// <summary>나머지(그림·소리)는 배경 스레드에서 읽는다 — 창은 먼저 뜬다.</summary>
    internal void LoadScene()
    {
        try
        {
            LoadRosterSprites();
            Btl.InitBattle();
            Btl.LoadRingAssets();
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
    internal static int WindowLeft(int windowWidth)
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
    internal double FitZoom()
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
        int contentH = Math.Min(BoardIsMap ? BoardHeight * 7 / 10 : GridTop + MosesScene.MosesH, _resH);
        double fill = Math.Floor(Math.Min(_resW / (double)contentW, _resH / (double)contentH) * 20) / 20;
        return Math.Clamp(fill, 1, MaxZoomChosen);
    }

    /// <summary>설정 > 해상도 — 고른 배율 %(0 = 자동). 켤 때 읽고 바꾸면 저장한다.</summary>
    internal int _zoomPercent = UserSettings.Current.ZoomPercent;

    /// <summary>배율·해상도를 바꿨을 때 — 판은 그대로 두고 셰이더·텍스처·창만 다시 만든다.</summary>
    internal void ApplyZoom(bool force = false)
    {
        double zoom = FitZoom();
        if (!force && Math.Abs(zoom - _zoom) < 1e-9) return;
        _zoom = zoom;
        Btl._camTarget = Math.Clamp(Btl._camTarget, 0, CamMax);
        Btl._camTargetX = Math.Clamp(Btl._camTargetX, 0, Btl.CamMaxX);
        if (_hwnd != IntPtr.Zero) RebuildView();
    }

    /// <summary>
    /// 새 맵에 맞춰 판·화면 버퍼·텍스처·창 크기를 다시 잡는다(전투마다 맵 크기가 다르다).
    /// 창을 아직 안 만들었으면 크기만 정해 두고, 만들었으면 텍스처와 창까지 다시 만든다.
    /// </summary>
    internal void ResizeBoard(int cols, int rows)
    {
        cols = Math.Max(1, cols);
        rows = Math.Max(1, rows);
        bool same = cols == Cols && rows == Rows && _fb.Length == BoardWidth * BoardHeight;
        Cols = cols;
        Rows = rows;
        _zoom = FitZoom();
        if (_fb.Length != BoardWidth * BoardHeight) _fb = new uint[BoardWidth * BoardHeight];
        // 세로 자리·명령까지 지운다 — 예전엔 가로만 지워 새 전투가 앞 전투의 세로 자리에서 미끄러져 왔다(감사4 C5).
        Btl.ResetCamera();
        // 전투 맵이면 다음 전투 틀에 시작 카메라(Btl 워드 2·3)와 16틀 페이드인을 건다(0x10061ad0, 감사4 C6).
        _battleIntroPending = BoardIsMap;
        // 판을 갈았으면 장면이 바뀐 것 — 남은 페이드는 버린다(메뉴로 불러오기·타이틀로 가면 끝나지 않은 페이드가 남아 입력을 막았다).
        if (_fadeOutStart >= 0) _mixer.SetMusicGain(_musicGain);   // 줄이던 음악 크기도 되돌린다
        _fadeInStart = _fadeOutStart = -1;
        _afterFadeOut = null;                    // 떠나던 중에 판이 다른 길로 갈렸다 — 묵은 「다음 장면」이 남지 않게
        _fadeOutKeepMusic = false;
        if (same || _hwnd == IntPtr.Zero) return;
        RebuildView();
    }

    /// <summary>보이는 판 크기·배율에 맞춰 셰이더·텍스처·창·스왑체인을 다시 만든다.</summary>
    internal void RebuildView()
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
    internal static Dictionary<int, ExportedCharacter> CollectExportedManifests(string assetsRoot)
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

    internal static string FindRepoAssetsRoot()
    {
        try { return AssetsFolder.Find("characters"); }
        catch (DirectoryNotFoundException) { return ""; }
    }

    /// <summary>내보낸 것이 없을 때의 마지막 수단 — 실제 게임 폴더에서 읽는다.</summary>
    internal static (string Name, string ObsPath) LoadFromGameFolder(int chrCode)
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
            // 시험 전용: DUELDX_TESTRUN=N 이면 한 프레임에 N 번 갱신한다(기본 1 — 평소대로, GameWindow.TestRun.cs).
            for (int step = 0; step < TestRunSteps && _running; step++)
            {
                double now = _lastTime + dt;
                Update(dt);
                _lastTime = now;
                TestRunTick();
            }
            // 적 행동 건너뛰기(모드) — 그 행동이 끝날 때까지 한 틀에 여러 번, 한 틱씩 갱신한다.
            for (int step = 0; step < 240 && _skippingAction && _running; step++)
            {
                double tickDt = 1.0 / TicksPerSecond, now = _lastTime + tickDt;
                Update(tickDt);
                _lastTime = now;
                StepSkipEnemyAction();
            }

            UpdateCursor();
            Render();
        }
    }

    /// <summary>지난 프레임의 실제 시각(초) — 게임 시계(<c>_lastTime</c>)는 여기서 흐른 만큼 × 게임 속도로 나아간다.</summary>
    internal double _realTime;

    /// <summary>
    /// 창 제목에 찍는 버전 — 릴리즈 빌드는 태그(v0.10.0 따위). 손으로 빌드한 것은 git 마지막 태그와 그 뒤 커밋 수(v0.10.0+2),
    /// git 이 없어 못 셌으면 「개발판」.
    /// </summary>
    internal static string AppVersion
    {
        get
        {
            string v = typeof(GameWindow).Assembly
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

    internal static void RegisterClassOnce()
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

    internal static IntPtr StaticWndProcTrampoline(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        _active != null ? _active.WndProc(hWnd, msg, wParam, lParam)
                        : Win32.DefWindowProcW(hWnd, msg, wParam, lParam);

    internal void CreateNativeWindow()
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

    internal IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
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
                Btl._heldMoveKeys.Remove((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL when ProgressScr._progressOpen:
                ProgressScr._progressScroll -= 3 * (short)(((long)wParam >> 16) & 0xFFFF) / 120;   // 진행 상태 창이 떠 있으면 그 목록을 굴린다
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL when _statusUnit >= 0 && StatusScr.ScrollStatusLists(-(short)(((long)wParam >> 16) & 0xFFFF) / 120):
                return IntPtr.Zero;                                          // 스테이터스 창의 어빌리티 목록 위면 그 목록을 굴린다
            case Win32.WM_MOUSEWHEEL when Mos.OnMosesMailWheel((short)(((long)wParam >> 16) & 0xFFFF) / 120):
                return IntPtr.Zero;                                          // 모세스 메일 목록 위면 그 목록을 굴린다
            case Win32.WM_MOUSEWHEEL when Mos.OnMosesShopWheel((short)(((long)wParam >> 16) & 0xFFFF) / 120):
                return IntPtr.Zero;                                          // 모세스 상점 목록 위면 그 목록을 굴린다
            case Win32.WM_MOUSEWHEEL when SlotsScr.SlotsOpen:
                SlotsScr.ScrollSlots(-(short)(((long)wParam >> 16) & 0xFFFF) / 120);   // 슬롯 목록이 떠 있으면 휠은 목록을 굴린다
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL when CharEditScr._open:
                CharEditScr.OnWheel((short)(((long)wParam >> 16) & 0xFFFF) / 120);
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL when Btl._itemMenu:
                // 아이템 목록은 8줄 창이다(0x100d3830) — 휠로 굴린다. 전에는 9종째부터 고를 수 없었다(ba-20 G1).
                Btl._itemTop = Math.Clamp(Btl._itemTop - (short)(((long)wParam >> 16) & 0xFFFF) / 120, 0, Math.Max(0, Btl.ItemRowsList().Count - BattleScene.ItemRows));
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL when Btl._abilityMenu && !Sys.SystemOpen:
                Btl.ScrollAbilityMenu(-(short)(((long)wParam >> 16) & 0xFFFF) / 120);   // 어빌리티 목록은 8줄 창 — 휠로 굴린다(ba-20 G12)
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL when Sys.SystemOpen || Btl.LevelUpOpen || Btl._abilityMenu || Mos._mosesOpen:
                return IntPtr.Zero;   // 창이 떠 있으면 휠이 뒤의 카메라를 굴리지 않는다
            case Win32.WM_MOUSEWHEEL:
                // Shift+휠은 좌우로(넓은 맵), 그냥 휠은 위아래로.
                if (((long)wParam & 0x0004) != 0) Btl.ScrollCameraX(-(short)(((long)wParam >> 16) & 0xFFFF) / 120.0 * TileW * 2);
                else Btl.ScrollCamera(-(short)(((long)wParam >> 16) & 0xFFFF) / 120.0 * TileH * 2);
                return IntPtr.Zero;
            case 0x020E:   // WM_MOUSEHWHEEL — 가로 휠
                Btl.ScrollCameraX((short)(((long)wParam >> 16) & 0xFFFF) / 120.0 * TileW * 2);
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
                Btl._heldMoveKeys.Clear();
                return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
            case Win32.WM_LBUTTONDOWN:
                OnClick((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
                return IntPtr.Zero;
            case Win32.WM_RBUTTONUP:
                Btl.CloseUnitInfo();
                Btl._abilityPressed = -1;      // 어빌리티 설명은 오른쪽 단추를 떼면 사라진다
                StatusScr._statusTip = null;         // 스테이터스 설명도(0x10042c00)
                Mos._styleTip = null;          // 모세스 전직 화면의 어빌리티 설명도
                Btl._ringHelp = null;          // 링 항목 설명도
                return IntPtr.Zero;
            case Win32.WM_RBUTTONDOWN:
            case Win32.WM_MOUSEMOVE:
            {
                var (bx, by) = BoardPoint((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
                _mouse = (bx, by);
                Btl._mouseView = (bx - _camX, by - _camY);   // 가장자리 스크롤은 화면 자리로 본다(Camera.cs)
                Mos.UpdateMosesHover(bx, by);
                ChaptersScr.UpdateChaptersHover(bx, by);
                SlotsScr.UpdateSlotsHover(bx, by);
                Btl.UpdateAbilityHover(bx, by);
                Btl.UpdateAimHover(bx, by);
                TitleScr.UpdateTitleHover(bx, by);
                RecordsScr.UpdateRecordsHover(bx, by);
                Fld.UpdateFieldHover(bx, by);
                if (msg == Win32.WM_RBUTTONDOWN) Btl.OnRightClick(bx, by);
                else Btl.OnRingMouseMove(bx, by);
                return IntPtr.Zero;
            }
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // ── 입력 · 이동 ──────────────────────────────────────────────────────────

    internal void OnKeyDown(int key)
    {
        if (key == 'W' && FieldOpen && Fld.RunWipeIfAsked()) return;   // 화면 밖 시험: DUELDX_WIPE 전환을 손으로 건다
        if (key == 'T' && !FieldOpen && Btl.TouchNearestObjectForTest()) return;
        if (_afterFadeOut != null) return;     // 장면을 떠나는 페이드 동안은 입력을 안 받는다
        if (SceneFading && !Mos._mosesOpen && !FieldOpen && !TitleScr._titleOpen && !EpisodesScr._episodesOpen && !RecordsScr._recordsOpen) return;   // 전투 시작·끝 페이드 동안은 입력을 안 받는다(0x10061ad0·0x10061d91)
        // 대사는 아무 키로나 한 줄씩 넘기고, <b>Esc 면 그 장면을 통째로</b> 건너뛴다 — 대사뿐 아니라 기다림·걷기·전환까지.
        if (ProgressScr._progressOpen) { if (key == Win32.VK_ESCAPE) ProgressScr.ToggleProgress(); return; }
        if (_afterFadeOut != null) return;     // 장면을 떠나는 페이드 동안은 입력을 안 받는다
        if (key == Win32.VK_ESCAPE && Tlk.SkipScene()) return;
        if (Tlk.OnTalkInput(skipAll: key == Win32.VK_ESCAPE)) return;
        if ((key == Win32.VK_RETURN || key == Win32.VK_SPACE) && Tlk.SkipCurrentWait()) return;   // 컷씬 기다림은 Enter·Space 로 넘긴다
        if (CharEditScr._open) { CharEditScr.OnKey(key); return; }
        if (TuningScr._tuningOpen) { TuningScr.OnTuningKey(key); return; }
        if (Btl._deployOpen && (key == Win32.VK_RETURN || key == Win32.VK_ESCAPE)) { Btl.OnDeployKey(key); return; }
        if (_keysOpen) { OnKeysKey(key); return; }
        if (ChaptersScr._chaptersOpen) { if (key == Win32.VK_ESCAPE) ChaptersScr._chaptersOpen = false; return; }
        // 타이틀 화면 — Esc 는 슬롯 창, Enter 는 초점 단추 NEW GAME(Title.cs).
        if (TitleScr.OnTitleKey(key)) return;
        // 연대표에서도 전투 키는 안 먹고 Esc 로 시스템 메뉴만 연다.
        if (EpisodesScr._episodesOpen)
        {
            if (key != Win32.VK_ESCAPE || Sys.CloseSystemWindow()) return;
            Sys.OpenSystemMenu();   // 연대표 Esc 에는 소리가 없다(0x10106e50, ba-20 T6)
            return;
        }
        if (Btl.OnAbilityMenuKey(key)) return;
        if (Btl.LevelUpOpen) { Btl.CloseLevelUp(); return; }
        // 전투가 끝나고 배너가 떠 있으면 아무 키나 누르면 — 이기고 이어지는 전투가 있으면 그 전투로(이벤트 행동 10),
        // 없거나 졌으면 모세스 화면으로 간다(mo-1).
        if (Btl._outcome.Length > 0 && !Mos._mosesOpen && !FieldOpen && !EpisodesScr._episodesOpen) { if (OutcomeInputReady) Btl.LeaveFinishedBattle(); return; }
        // 모세스 화면에서는 Esc 가 페이지를 닫고, 주 화면이면 모세스 시스템 메뉴를 연다(분석-모세스 13절).
        if (Mos._mosesOpen)
        {
            if (_statusUnit >= 0) { if (key == Win32.VK_ESCAPE) _statusUnit = -1; return; }   // 스테이터스 창은 Esc 로 닫는다
            // 성도에서는 ←·→ 로도 항성계를 옮긴다(좌우 단추와 같은 일).
            if (Mos._mosesPage == 0 && Mos._mosesStep == 1 && key is Win32.VK_LEFT or Win32.VK_RIGHT)
            {
                Mos.MosesTurnSystem(key == Win32.VK_LEFT ? -1 : 1);
                return;
            }
            if (key != Win32.VK_ESCAPE || Sys.CloseSystemWindow()) return;
            if (Mos._mosesPage is not (-1 or 0)) { Mos.MosesGoBack(); return; }   // 주 화면·항행에서는 Esc 가 시스템 메뉴다(분석-모세스 13절)
            if (Sys.ChapterEventRunning) return;                              // 챕터 사건이 도는 동안은 메뉴가 안 열린다(0x100f78ec, ba-20 T7)
            Play(578);
            Sys.OpenSystemMenu();
            return;
        }
        if (key == Win32.VK_ESCAPE && Sys.CloseSystemWindow()) return;
        // 전투에서 메뉴·창이 떠 있는 동안은 다른 키도 안 받는다 — 전투가 상태 25 에 서 있다(0x1006b290 하위 1, 감사5 S1).
        if (Sys.SystemOpen && !FieldOpen) return;
        if (key == Win32.VK_ESCAPE)
        {
            // 취소할 것이 있으면 취소하고, 없으면 시스템 메뉴를 연다(menu-6). 전투에서는 내 유닛 조종 상태(원본 상태 22)일 때만 —
            // AI 차례·행동·이벤트·배치·레벨업·결과 중에는 취소로만 쓰인다(감사5 S1·S8). 필드는 편의로 열되 SAVE 는 꺼져 있다(S3).
            if (_statusUnit >= 0) _statusUnit = -1;
            else if (Btl._ringUnit >= 0) Btl.CancelRing();
            else if (!Btl.CancelStep(undoMove: true) && (FieldOpen || Sys.CanOpenBattleMenu)) Sys.OpenSystemMenu();
            return;
        }
        if (key == Win32.VK_RETURN && Btl._ringUnit >= 0 && Btl._ringPhase == BattleScene.RingPhase.Idle && Btl._ringHover >= 0) { Btl.PickRingItem(Btl._ringHover); return; }
        if (_statusUnit >= 0) return;

        // 대상 고르는 중: Enter·공격 키 = 커서의 적 치기, 다음 인물 키(Tab) = 다른 적
        // 기본공격뿐 아니라 적 하나를 겨누는 어빌리티도 같게 다룬다.
        if (Btl._targetWork >= 0 && Btl._attackCursor >= 0 && Btl._ringUnit < 0)
        {
            if (key == Win32.VK_RETURN || _keys.ActionFor(key) == KeyAction.Attack) { Btl.AttackCursorTarget(); return; }
            if (_keys.ActionFor(key) == KeyAction.NextUnit) { Btl.CycleAttackCursor(); return; }
        }
        // 어빌리티를 고른 단축키를 한 번 더(또는 Enter) — 저절로 겨눈 대상에게 바로 쓴다.
        if (Btl._targetWork >= 0 && !Btl._targetIsBasicAttack && Btl._ringUnit < 0
            && (key == Btl._targetHotkey || key == Win32.VK_RETURN) && Btl.UseAimedAbility()) return;

        // 어빌리티 목록을 안 열고도 1~4 를 누르면 그 줄의 어빌리티를 바로 고른다(Q·W·E·R 은 다른 단축키와 겹쳐 뺀다).
        if (key is >= '1' and <= '4' && Btl.IsPlayerTurn && !Btl._abilityMenu && Btl._targetWork < 0 && Btl._ringUnit < 0 && !_units[Btl._turn].IsBusy)
        {
            Btl.RingShortcut(BattleScene.RingCommand.Ability);
            if (Btl._abilityMenu && Btl.OnAbilityMenuKey(key)) return;
        }

        switch (MoveActionFor(key) ?? _keys.ActionFor(key))
        {
            case KeyAction.Grid: _showGrid = !_showGrid; SaveSettings(); break;
            case KeyAction.Gauges: _showGauges = !_showGauges; SaveSettings(); break;
            case KeyAction.Ring: Btl.ToggleRingForSelected(); break;
            case KeyAction.Attack: Btl.RingShortcut(BattleScene.RingCommand.Attack); break;
            case KeyAction.Ability: Btl.RingShortcut(BattleScene.RingCommand.Ability); break;
            case KeyAction.Rest: Btl.RingShortcut(BattleScene.RingCommand.Rest); break;
            case KeyAction.Status:
                if (Btl._ringUnit >= 0) Btl.RingShortcut(BattleScene.RingCommand.Status);
                else if ((uint)_selected < _units.Length) _statusUnit = _selected;
                break;
            case KeyAction.Item: Btl.RingShortcut(BattleScene.RingCommand.Item); break;
            case KeyAction.System:
                if (Btl._ringUnit >= 0) Btl.RingShortcut(BattleScene.RingCommand.System);
                else if (FieldOpen || Sys.CanOpenBattleMenu) Sys.OpenSystemMenu();   // Esc 와 같은 문(감사5 S1·S8)
                break;
            case KeyAction.NextUnit:
                for (int i = 1; i <= _units.Length; i++)
                {
                    int next = (Math.Max(_selected, 0) + i) % _units.Length;
                    if (_units[next].Alive && _units[next].IsAlly) { _selected = next; break; }
                }
                break;
            case KeyAction.MoveUp or KeyAction.MoveDown or KeyAction.MoveLeft or KeyAction.MoveRight:
                if (Btl._ringUnit >= 0 || Btl._abilityMenu || Btl._targetWork >= 0) break;   // 링·목록이 열려 있거나 대상을 고르는 중이면 걷지 않는다
                Btl._heldMoveKeys.Remove(key);
                Btl._heldMoveKeys.Add(key);   // 마지막에 누른 키가 맨 뒤 — 그 방향을 따른다
                Btl.StepByKey(key);
                break;
        }
    }

    /// <summary>걷기 키 — 방향키는 늘, 그 밖은 단축키 표에서.</summary>
    internal KeyAction? MoveActionFor(int key) => key switch
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
    internal void OnClick(int clientX, int clientY)
    {
        if (ProgressScr._progressOpen) { var (px, py) = BoardPoint(clientX, clientY); ProgressScr.OnProgressClick(px, py); return; }
        if (_afterFadeOut != null) return;     // 장면을 떠나는 페이드 동안은 입력을 안 받는다
        if (SceneFading && !Mos._mosesOpen && !FieldOpen && !TitleScr._titleOpen) return;   // 전투 시작·끝 페이드 동안은 입력을 안 받는다
        if (Btl.LevelUpOpen) { Btl.CloseLevelUp(); return; }
        if (TrySkipEnemyAction()) return;      // 적이 행동하는 동안의 클릭 = 그 행동 건너뛰기(모드)
        if (SlotsScr._notice != null) { SlotsScr._notice = null; return; }     // 「저장되었습니다.」 같은 알림은 클릭으로 바로 닫는다
        // 배너는 클릭 한 번으로 넘긴다 — 전에는 키만 받아서 눌러도 바로 안 넘어갔다.
        if (Btl._outcome.Length > 0 && !Mos._mosesOpen && !FieldOpen && !EpisodesScr._episodesOpen) { if (OutcomeInputReady) Btl.LeaveFinishedBattle(); return; }
        if (Tlk.OnTalkInput()) return;            // 대사는 클릭 한 번으로 넘긴다
        if (Tlk.SkipCurrentWait()) return;        // 컷씬(그림만 띄워 두고 기다리는 틈)도 클릭 한 번으로 넘긴다
        var (bx, by) = BoardPoint(clientX, clientY);
        if (CharEditScr.OnClick(bx, by)) return;       // 도구 > 캐릭터 에디터
        if (TuningScr.OnTuningClick(bx, by)) return;   // 모드 > 조정 창은 어느 화면 위에서든 먼저 받는다
        if (Btl.OnDeployClick(bx, by)) return;   // 캐릭터 배치 단계
        if (Fld.OnFieldClick(bx, by)) return;
        if (RecordsScr.OnRecordsClick(bx, by)) return;
        if (EpisodesScr.OnEpisodesClick(bx, by)) return;
        if (TitleScr.OnTitleClick(bx, by)) return;
        if (ChaptersScr.OnChaptersClick(bx, by)) return;
        if (Mos.OnMosesClick(bx, by)) return;
        // 위에 그려지는 창이 먼저 받는다 — 전에는 아이템 목록이 시스템 메뉴·Status 보다 먼저 클릭을 먹었다(ba-20 G2).
        if (OnKeysClick(bx, by) || Sys.OnSystemClick(bx, by) || StatusScr.OnStatusClick(bx, by)) return;
        if (Btl.OnItemMenuClick(bx, by)) return;
        if (Btl.OnRingClick(bx, by) || Btl.OnAbilityMenuClick(bx, by)) return;

        if (by < GridTop) return;
        int col = bx / TileW, row = RowAt(bx, by);
        // 적 몸통을 눌렀으면 그 적의 칸을 누른 것으로 본다 — 발밑 칸만 보면 몸통 클릭이 윗칸(빈 칸)으로 갔다.
        int index = UnitOrFoeAt(bx, by);
        if (index >= 0) (col, row) = (_units[index].Col, _units[index].Row);
        if (row < 0) return;
        if (Btl.OnTargetClick(col, row)) return;

        if (index >= 0)
        {
            // 적군은 고를 수 없다(fa-9) — 대신 내 차례면 클릭만으로 바로 공격한다(fa-12).
            if (_units[index].IsAlly) _selected = index;
            else if (Btl.IsPlayerTurn && !_units[Btl._turn].IsBusy) Btl.QuickAttack(index);
            return;
        }
        if (Btl.TryTouchObject(col, row) || Btl.TryAttackObject(col, row)) return;
        if (_selected == Btl._turn && Btl.TryWalkTo(col, row)) return;
        _selected = Btl._turn;
    }

    /// <summary>배경 읽기가 끝난 뒤 주 스레드에서 한 번 돌릴 시험 훅(DUELDX_TITLE·MOSES·FIELD·LEVELUP).</summary>
    internal volatile bool _openHooksPending;

    internal void Update(double dt)
    {
        if (_openHooksPending)
        {
            _openHooksPending = false;
            TitleScr.OpenTitleIfAsked();
            Mos.OpenMosesIfAsked();
            Fld.OpenFieldIfAsked();
            Btl.OpenLevelUpIfAsked();
            SlotsScr.OpenSlotsIfAsked();
            // DUELDX_SAVE=<칸> 이면 화면이 다 선 뒤 그 칸에 한 번 저장한다(화면 밖 시험용 — 세이브에 무엇이 적히는지 본다).
            if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_SAVE"), out int saveSlot)) Btl._saveSlotPending = saveSlot;
            // DUELDX_LOAD=<칸> 이면 그 세이브를 바로 불러온다(화면 밖 시험용). 모세스로 돌아오면 DUELDX_MOSESPAGE 도 따른다.
            if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_LOAD"), out int slot) && Sys.LoadBattleFrom(SlotsScreen.SlotPath(slot)) && Mos._mosesOpen
                && int.TryParse(Environment.GetEnvironmentVariable("DUELDX_MOSESPAGE"), out int page))
            {
                if (page == 7) Mos.OpenMosesStyle(); else if (page == 6) Mos.OpenMosesLegion();
                else if (page == 1) { Mos.MosesGoPage(1); Mos._mosesPage = 1; }   // 메일 — 배달까지 돈다
            }
            StatusScr.OpenStatusIfAsked();                  // 불러온 뒤에 연다 — 먼저 열면 불러오기가 창을 닫는다
        }
        // 시험용 저장은 모세스·타이틀에서도 되어야 한다 — 아래 이른 되돌아감보다 먼저 한다.
        // 화면이 다 서고 나서 저장한다 — 첫 틀에 하면 모세스가 아직 안 열려 전투로 적힌다.
        if (Btl._saveSlotPending is { } pending && _lastTime > BattleScene.SaveHookAt) { Btl._saveSlotPending = null; Sys.SaveBattleTo(SlotsScreen.SlotPath(pending)); }
        SlotsScr.UpdateSlotArrows();                     // 슬롯 스크롤 화살표 누르고 있기(감사5 S9) — 타이틀·기록 화면에서도 돈다
        // 타이틀·연대표·모세스 화면에서는 전투가 뒤에서 돌면 안 된다 — 차례도 이벤트도 멈추고 화면만 그린다.
        // (모세스를 빼 두었더니 뒤에서 턴이 흘러 전투 대사가 떠 버렸고, 그 대사가 화면 클릭을 다 먹었다.)
        if (TitleScr._titleOpen || EpisodesScr._episodesOpen || Mos._mosesOpen || RecordsScr._recordsOpen)
        {
            UpdateSounds();
            // 모세스 전직 화면의 STATUS 로 고친 어빌리티·장비를 파티 자료에 적는다 — 전에는 여기서 돌아가 버려 한 번도 안 적혔다(사용자 보고: 유진 LP증가).
            StatusScr.SyncVirtualStatus();
            // 모세스가 떠 있는 동안 챕터 스크립트(대사·고르기가 든 사건)를 필드와 같은 실행기로 돌린다(원본 챕터 장면도 같은 실행기).
            if (Mos._mosesOpen && !EpisodesScr._chapterDone) { Tlk.UpdateTalk(); Fld.UpdateField(); }
            StepSceneFadeClock();               // 타이틀·연대표·기록 화면의 들고 나는 페이드
            return;
        }

        // 필드도 전투와 따로 도는 장면이다 — 스크립트만 돌리고 전투는 멈춘다.
        if (FieldOpen)
        {
            UpdateSounds();
            StatusScr.SyncVirtualStatus();
            Tlk.UpdateTalk();
            Fld.UpdateField();
            if (_afterFadeOut != null) StepSceneFadeClock();   // 필드에서 EXIT GAME 으로 나가는 페이드
            return;
        }

        // 시스템 메뉴·슬롯·확인·음량·MISSION 창이 떠 있는 동안 전투는 선다 — 원본 상태 25(CTRL_SYS, 0x1006b290) 하위 1 은 아무것도 안 해
        // 틱·AI·이벤트·걷기가 멈춘다. 전에는 뒤에서 계속 돌아 AI 차례·행동 도중이 저장됐다(감사5 S1).
        if (Sys.SystemOpen) { UpdateSounds(); if (_afterFadeOut != null) StepSceneFadeClock(); return; }

        foreach (var unit in _units) unit.Advance(dt * TicksPerSecond, dt);

        // 키를 누르고 있으면 한 칸이 끝난 그 프레임에 바로 다음 칸을 건다 — 멈칫하지 않고 걷기 컷도 이어진다.
        if (Btl._heldMoveKeys.Count > 0 && Btl._ringUnit < 0 && _statusUnit < 0 && !_keysOpen && !Btl._abilityMenu && Btl._targetWork < 0 && Btl.IsPlayerTurn && !_units[Btl._turn].IsBusy)
            Btl.StepByKey(Btl._heldMoveKeys[^1]);

        // 정해 둔 길이 있으면 한 칸씩 이어 걷는다(클릭 이동·공격 자리로 가기·적 AI).
        Btl.StepBlinks();
        LegionStageAb.StepLegionFades();
        UnitFxAb.StepUnitFx();
        Btl.StepPendingExits();
        foreach (var unit in _units)
        {
            // 증원(행동 200)이 맵 변 밖에서 곧게 걸어 들어오는 중 — 한 틱에 8px(0x10050eb0 단계 1). 다 들어온 뒤에 길을 걷는다.
            if (unit.Entering) { unit.StepEntry(8 * TicksPerSecond * dt); continue; }
            if (Btl._blinks.ContainsKey(unit) || Btl.TryBeginBlink(unit)) continue;   // 이동 종류 1 은 걷지 않고 순간이동한다(ba-20 P4)
            if (unit.IsMoving || !unit.Path.TryDequeue(out var cell)) continue;
            unit.Facing = cell.Col > unit.Col ? Facing.Right : cell.Col < unit.Col ? Facing.Left : cell.Row > unit.Row ? Facing.Down : Facing.Up;
            unit.BeginStep(cell.Col, cell.Row, StepTicksBetween(unit.Col, unit.Row, cell.Col, cell.Row));
            // 걷기(명령 0x2710)는 칸 경계마다 걷는 인물 따라가기를 건다(0x10076ef8 → 0x100eac40(8), 감사4 C2).
            // 군단 부하는 대장만 따라간다(0x2711 은 대장일 때만, 0x10077786).
            if (unit.LeaderIndex < 0 && unit.OnField) Btl.FollowUnit(Array.IndexOf(_units, unit));
        }

        foreach (var unit in _units) unit.SettleIfStopped();
        Btl.ResolvePendingTouch();                  // 상자 옆까지 걸어간 인물이 멈췄으면 연다
        Btl.SyncFollowers();
        StepSceneFade();                        // 새 판이면 시작 카메라·페이드인, 끝났으면 페이드아웃 뒤 다음 장면
        if (Mos._mosesOpen || FieldOpen || TitleScr._titleOpen || EpisodesScr._episodesOpen) return;   // 페이드아웃이 장면을 넘겼다
        Btl.UpdateCamera(dt);
        Btl.ApplyPoseHook();
        if (_fadeInStart < 0) Btl.ApplyWorkHook();
        StatusScr.SyncVirtualStatus();
        UpdateSounds();
        Btl.UpdateRing();
        Tlk.UpdateTalk();
        // 페이드인 동안은 틱·이벤트가 멈춘다(원본은 페이드를 다 한 뒤에 루프로 들어간다, 0x10061ad0).
        if (_fadeInStart >= 0) return;
        Btl.StepEvent();
        Btl.UpdateTurn();
        Btl.RefreshMoveRange();
        UpdateOutcomeBanner();                  // 배너는 음악이 끝나고(최소 121틱) 저절로 넘어간다(0x1006afa0)
    }

    // ── 프레임 합성 ──────────────────────────────────────────────────────────

    /// <summary>DUELDX_PERF=1 이면 초마다 fps 와 합성·올리기·그리기 ms 를 <c>%TEMP%\dueldx_perf.log</c> 에 적는다(성능 확인용).</summary>
    internal static readonly bool PerfLog = Environment.GetEnvironmentVariable("DUELDX_PERF") == "1";
    internal readonly Stopwatch _perf = new();
    internal double _pc, _pu, _pd; private int _pn; private double _pStart;
    /// <summary>성능 기록용 — 한 틀의 합성 가운데 전투 판 · 모세스 · 필드 그리기에 든 ms 누적.</summary>
    internal double _pBattle, _pMoses, _pField;

    /// <summary>성능 기록(DUELDX_PERF)의 전투 그리기 구간별 합 — 어느 그리기가 느린지 가른다.</summary>
    internal readonly Dictionary<string, double> _pParts = [];
    internal double _pMark;

    internal void PerfMark(string part)
    {
        if (!PerfLog) return;
        double now = _perf.Elapsed.TotalMilliseconds;
        _pParts[part] = _pParts.GetValueOrDefault(part) + now - _pMark;
        _pMark = now;
    }

    internal void Render()
    {
        if (!PerfLog) { Compose(); Upload(); Draw(); return; }
        _perf.Restart(); Compose(); double c = _perf.Elapsed.TotalMilliseconds;
        Upload(); double u = _perf.Elapsed.TotalMilliseconds;
        Draw(); double d = _perf.Elapsed.TotalMilliseconds;
        _pc += c; _pu += u - c; _pd += d - u; _pn++;
        if (_realTime - _pStart >= 1)
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_perf.log"),
                $"fps {_pn / (_realTime - _pStart):F0} compose {_pc / _pn:F1} upload {_pu / _pn:F1} draw {_pd / _pn:F1} ms, game {_lastTime:F1}s real {_realTime:F1}s" + $" (battle {_pBattle / _pn:F1} moses {_pMoses / _pn:F1} field {_pField / _pn:F1})" + Environment.NewLine);
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_perf.log"),
                "  parts " + string.Join(" ", _pParts.OrderByDescending(p => p.Value).Take(5).Select(p => $"{p.Key} {p.Value / _pn:F1}")) + Environment.NewLine);
            _pc = _pu = _pd = 0; _pn = 0; _pStart = _realTime;
            _pBattle = _pMoses = _pField = 0;
            _pParts.Clear();
        }
    }

    /// <summary>
    /// 기술을 쓰는 동안 맵이 물든다(ba-20 P2) — 상태 14 CHRWORK 가 work <c>+0x3a</c>(1 → 방식 2)·<c>+0x3b</c>(세기)로 <c>0x1002e8d0</c> 을 부르고
    /// 행동이 끝나면 <c>0x1002e920</c>(목표 −1). 지금 세기는 틱마다 한 칸씩 목표로 간다(<c>0x1006408a</c>). 맵 그리기만 이 값을 본다(유닛·이펙트는 그대로 — 가설).
    /// </summary>
    internal double _mapTint = -1, _mapTintClock;
    internal int _mapTintTarget = -1;

    internal readonly byte[] _mapTintLut = new byte[256];
    internal int _mapTintLutFor = -1;

    internal void DrawMapTint()
    {
        double ticks = Math.Clamp((_lastTime - _mapTintClock) * TicksPerSecond, 0, 4);
        _mapTintClock = _lastTime;
        if (!_battleLoaded || Mos._mosesOpen || FieldOpen || TitleScr._titleOpen || EpisodesScr._episodesOpen) { _mapTint = _mapTintTarget = -1; return; }
        if (Btl._routine == null && Btl._eventRoutine == null) _mapTintTarget = -1;   // 행동이 끊겨도(불러오기·전투 끝) 남지 않게
        if (_mapTint < _mapTintTarget) _mapTint = Math.Min(_mapTintTarget, Math.Max(_mapTint, -1) + ticks);
        else if (_mapTint > _mapTintTarget) _mapTint = Math.Max(_mapTintTarget, _mapTint - ticks);
        if (_mapTint <= -1) return;
        // 방식 2: 5비트 채널 v = (23·c + 8·세기)/31 — 세기 0 이면 74% 로 어두워지고 세기가 오를수록 회색이 뜬다.
        int add = (int)(Math.Max(0, Math.Floor(_mapTint)) * 8 * 255 / (31 * 31));
        // 채널 값 256가지를 미리 셈해 두고 줄 단위로 바꾼다 — 전에는 픽셀마다 식을 세 번 셈해 한 틀에 38ms 를 먹었다(기술 쓰는 내내 15fps).
        if (_mapTintLutFor != add)
        {
            for (int v = 0; v < 256; v++) _mapTintLut[v] = (byte)Math.Min(255, v * 23 / 31 + add);
            _mapTintLutFor = add;
        }
        int x0 = Math.Max(0, _camX), x1 = Math.Min(BoardWidth, _camX + ViewWidth), width = BoardWidth;
        int y0 = Math.Max(0, _camY), y1 = Math.Min(BoardHeight, _camY + ViewHeight);
        if (x1 <= x0) return;
        fixed (uint* fb = _fb)
        fixed (byte* lut = _mapTintLut)
            for (int y = y0; y < y1; y++)
            {
                uint* px = fb + y * width + x0, end = px + (x1 - x0);
                for (; px < end; px++)
                {
                    uint c = *px;
                    *px = 0xFF000000 | (uint)lut[c >> 16 & 0xFF] << 16 | (uint)lut[c >> 8 & 0xFF] << 8 | lut[c & 0xFF];
                }
            }
    }

    internal void Compose()
    {
        if (PerfLog) _pMark = _perf.Elapsed.TotalMilliseconds;
        Array.Fill(_fb, BgColor);
        DrawBackground();
        PerfMark("바탕");
        DrawMapTint();
        PerfMark("물들임");
        Btl.DrawMoveRange();
        Btl.DrawDeployCells();
        Btl.DrawWorkRange();
        if (_showGrid) Btl.DrawGridLines();
        PerfMark("영역");
        Btl.DrawObjects();
        PerfMark("물체");
        Btl.DrawUnits();
        PerfMark("유닛");
        Btl.DrawBodyClones();
        Btl.DrawBlinkGhosts();
        StagingAb.DrawStageFx();
        Btl.DrawEffects();
        Mov.DrawMovies();
        Mov.DrawRipples();
        PerfMark("이펙트");
        if (_showGauges) Btl.DrawGauges();
        Btl.DrawPopups();
        Btl.DrawNumbers();
        Btl.DrawCritFlash();
        DrawStatus();
        PerfMark("숫자");
        Btl.DrawHud();
        Btl.DrawChestList();
        Btl.DrawRing();
        Btl.DrawAbilityMenu();
        Btl.DrawItemMenu();
        StatusScr.DrawStatusScreen();
        Tlk.DrawTalk();
        DrawToast();
        DrawOutcomeBanner();
        Btl.DrawUnitInfo();
        if (!Mos._mosesOpen) Sys.DrawSystem();
        Btl.DrawDeployPanel();
        DrawKeysPanel();
        TuningScr.DrawTuning();
        CharEditScr.Draw();
        Btl.DrawLevelUp();
        PerfMark("창");
        double perfA = PerfLog ? _perf.Elapsed.TotalMilliseconds : 0;
        Mos.DrawMoses();
        double perfB = PerfLog ? _perf.Elapsed.TotalMilliseconds : 0;
        _fldScene?.DrawField();
        if (PerfLog) { _pBattle += perfA; _pMoses += perfB - perfA; _pField += _perf.Elapsed.TotalMilliseconds - perfB; }
        RecordsScr.DrawRecords();
        TitleScr.DrawTitle();
        EpisodesScr.DrawEpisodes();
        ChaptersScr.DrawChapters();
        DrawSceneFade();                       // 전투 시작·끝 16틀 페이드(감사4 C6·C7)
        DrawSceneTag();
        ProgressScr.DrawProgress();
    }

    internal void DrawBackground()
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

    internal void DrawStatus()
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
        else DrawText(Btl.TurnLine(), _camX + 4, _camY + 20, 0xFFFFE8A0);
    }

    // ── 글자 ─────────────────────────────────────────────────────────────────

    internal (uint[] Px, int W, int H) GetText(string text, uint argb, float size = 13f)
    {
        string key = text + ":" + argb + ":" + size;
        if (_textCache.TryGetValue(key, out var cached)) return cached;

        var color = Color.FromArgb((int)argb);
        var made = TextRaster.Render(text, color, size) ?? ([], 0, 0);
        if (_textCache.Count > 500) _textCache.Clear();
        _textCache[key] = made;
        return made;
    }

    internal void DrawText(string text, int x, int y, uint argb, float size = 13f)
    {
        if (text.Length == 0) return;
        var (px, w, h) = GetText(text, argb, size);
        if (w > 0) BlitMasked(px, w, h, x, y);
    }

    // ── 그림 원소 ────────────────────────────────────────────────────────────

    internal void SetPixel(int x, int y, uint color)
    {
        if ((uint)x >= BoardWidth || (uint)y >= BoardHeight) return;
        uint a = color >> 24;
        if (a == 0) return;

        int idx = y * BoardWidth + x;
        _fb[idx] = a == 0xFF ? color : Blend(_fb[idx], color, a);
    }

    internal static uint Blend(uint bg, uint fg, uint a)
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
    internal void BlitMasked(uint[] src, int srcW, int srcH, int dstX, int dstY, (int Mode, int Strength)? tint = null,
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
                if (status is { } st) c = BattleScene.ApplyStatusTint(c, st);
                if (additive)
                {
                    // 더하기 합성 — 검정은 +0 이라 안 보인다. 흐려지는 중(fade)이면 덜 더한다.
                    int at = dy * BoardWidth + dx;
                    _fb[at] = GameWindow.AddColor(_fb[at], c, k);
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

    internal void FillRect(int x, int y, int w, int h, uint color)
    {
        // 네모를 판 안으로 자르고, 불투명이면 줄 단위로 채운다 — 픽셀마다 SetPixel 을 부르던 때는 화면 가득 한 번에 30ms 가 들어
        // 필드(틀마다 화면 전체를 검게 지운다)가 15~20fps 로 끊겼다(사용자 보고: Fld 0017).
        int x0 = Math.Max(0, x), x1 = Math.Min(BoardWidth, x + w), y0 = Math.Max(0, y), y1 = Math.Min(BoardHeight, y + h);
        if (x0 >= x1 || y0 >= y1) return;
        uint a = color >> 24;
        if (a == 0) return;
        if (a == 0xFF)
        {
            for (int yy = y0; yy < y1; yy++) _fb.AsSpan(yy * BoardWidth + x0, x1 - x0).Fill(color);
            return;
        }
        for (int yy = y0; yy < y1; yy++)
        {
            int i = yy * BoardWidth + x0;
            for (int n = x1 - x0; n > 0; n--, i++) _fb[i] = Blend(_fb[i], color, a);
        }
    }

    internal void StrokeRect(int x, int y, int w, int h, uint color)
    {
        for (int xx = x; xx < x + w; xx++) { SetPixel(xx, y, color); SetPixel(xx, y + h - 1, color); }
        for (int yy = y; yy < y + h; yy++) { SetPixel(x, yy, color); SetPixel(x + w - 1, yy, color); }
    }

    // ── D3D11 / DXGI ─────────────────────────────────────────────────────────

    internal void CreateDevice()
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
    internal void CompileShaders()
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
    internal (int X, int Y) _shaderOffset;

    /// <summary>지금 셰이더에 박혀 있는 배율 — <see cref="_zoom"/> 과 달라지면 다시 빌드한다.</summary>
    internal double _shaderZoom;

    internal void CreateSwapChain()
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

    internal void Upload()
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

    internal void Draw()
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

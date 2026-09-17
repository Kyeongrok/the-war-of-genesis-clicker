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
    private static readonly BattleUnit[] Roster = BattleDemoScene.Roster;

    /// <summary>한 칸 걸어가는 데 드는 시간(초).</summary>
    public const double StepSeconds = 0.25;

    /// <summary>
    /// 모션표 한 틱의 길이 — 1초에 몇 틱. 원본 게임의 틱 빠르기는 아직 확인 못 해서 눈으로 맞춘 값이다.
    /// </summary>
    public const double TicksPerSecond = 30;

    private const int TileW = ObtMap.CellWidth, TileH = ObtMap.CellHeight;
    private const int Cols = BattleDemoScene.Cols, Rows = BattleDemoScene.Rows;
    private const int GridTop = 40;
    private const int BoardWidth = Cols * TileW, BoardHeight = GridTop + Rows * TileH;

    /// <summary>창에 보이는 판 높이 — 판 전체의 80%. 나머지는 <see cref="_camY"/> 로 위아래로 스크롤한다.</summary>
    private const int ViewHeight = BoardHeight * 4 / 5;
    private const double MaxZoom = 2;

    /// <summary>화면 픽셀 ÷ 판 픽셀. 창이 모니터 작업 영역에 들어가도록 <see cref="MaxZoom"/> 안에서 줄인다.</summary>
    private readonly double _zoom = FitZoom();

    private const string GameRoot = @"C:\Users\Administrator\Downloads\gen3pt2";

    private const uint BgColor = 0xFF14100C;
    private const uint GridLine = 0x40FFFFFF;
    private const uint White = 0xFFF2EAD6;
    private const uint DimGray = 0xFFA09888;

    private ObtMapImage? _map;
    private Dictionary<int, UnitSprite> _sprites = [];
    private readonly UnitState[] _units = [.. Roster.Select(u => new UnitState(u))];
    private int _selected = -1;
    private readonly Dictionary<int, string> _names = [];
    private string _loadError = "";
    private volatile bool _loading = true;
    private bool _showGrid;
    /// <summary>발밑 HP·TP 막대 — 원본에는 없어서 기본은 끔(H 키).</summary>
    private bool _showGauges;

    private readonly uint[] _fb = new uint[BoardWidth * BoardHeight];
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
    private ID3D11RenderTargetView _backBufferRtv = null!;
    private ID3D11Texture2D _boardTex = null!;
    private ID3D11ShaderResourceView _boardSrv = null!;
    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;

    // ── 게임 자료 읽기 ───────────────────────────────────────────────────────

    /// <summary>배경 스레드에서 배경 그림·배치된 캐릭터들을 읽는다. 창은 먼저 뜬다.</summary>
    private void LoadScene()
    {
        try
        {
            _map = ObtMap.Load(Path.Combine(AssetsFolder.Find("maps"), BattleDemoScene.MapFile));

            string assetsRoot = FindRepoAssetsRoot();
            var manifests = CollectExportedManifests(assetsRoot);
            var sprites = new Dictionary<int, UnitSprite>();

            foreach (int chrCode in Roster.Select(u => u.ChrCode).Distinct())
            {
                var (name, obsPath) = manifests.TryGetValue(chrCode, out var m)
                    ? (m.Name, Path.Combine(assetsRoot, CharacterExport.FolderNameFor(chrCode, m.Name), CharacterExport.ObsFileName(m.SpriteCode)))
                    : LoadFromGameFolder(chrCode);

                _names[chrCode] = name;

                // 몸짓벌을 모두 풀고 모션표(서기·걷기 …)를 같이 읽는다. 모션표가 없으면 첫 컷 하나로 서 있는다.
                var motions = ObsSprite.Decode(obsPath);
                if (motions.Count == 0) continue;
                sprites[chrCode] = new UnitSprite(motions, ObsMotionTable.Load(obsPath));
            }
            _sprites = sprites;

            // Status 화면용 게임 표와 초상(첫 컷). 없어도 전투판은 그린다.
            _db = GameDatabase.Load(GameFiles.FromFolder(AssetsFolder.Find("data")));
            foreach (var (code, m) in manifests)
            {
                if (!Roster.Any(u => u.ChrCode == code) || m.FaceCode == 0) continue;
                string facePath = Path.Combine(assetsRoot, CharacterExport.FolderNameFor(code, m.Name), CharacterExport.ObsFileName(m.FaceCode));
                if (File.Exists(facePath) && ObsSprite.DecodeFirstFrame(facePath) is { } face) _faces[code] = SpriteFrame.From(face);
            }
            InitBattle();
            LoadRingAssets();
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

    /// <summary>모니터 작업 영역(작업 표시줄 뺀 곳)에 창이 들어가는 가장 큰 배율 — 최대 <see cref="MaxZoom"/>.</summary>
    private static double FitZoom()
    {
        var work = new Win32.Rect();
        if (!Win32.SystemParametersInfoW(Win32.SPI_GETWORKAREA, 0, ref work, 0)) return 1;

        // 제목 표시줄·테두리 몫을 조금 남긴다.
        double fit = Math.Min((work.Width - 32) / (double)BoardWidth, (work.Height - 100) / (double)ViewHeight);
        return Math.Clamp(Math.Floor(fit * 20) / 20, 0.5, MaxZoom);
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
            while (Win32.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
            {
                if (msg.Message == Win32.WM_QUIT) { _running = false; break; }
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessageW(ref msg);
            }
            if (!_running) break;

            double now = clock.Elapsed.TotalSeconds;
            Update(Math.Min(now - _lastTime, 0.1));
            _lastTime = now;

            Render();
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
            ClassName = ClassName,
        };
        _classAtom = Win32.RegisterClassExW(ref wc);
    }

    private static IntPtr StaticWndProcTrampoline(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) =>
        _active != null ? _active.WndProc(hWnd, msg, wParam, lParam)
                        : Win32.DefWindowProcW(hWnd, msg, wParam, lParam);

    private void CreateNativeWindow()
    {
        int pixelW = (int)(BoardWidth * _zoom), pixelH = (int)(ViewHeight * _zoom);

        var rect = new Win32.Rect { Left = 0, Top = 0, Right = pixelW, Bottom = pixelH };
        Win32.AdjustWindowRect(ref rect, Win32.WS_OVERLAPPEDWINDOW, true);
        IntPtr menu = CreateMenuBar();

        _active = this;
        _hwnd = Win32.CreateWindowExW(0, ClassName, $"{BattleDemoScene.Title} — 전투 Btl {BattleDemoScene.BtlId:D4}",
            Win32.WS_OVERLAPPEDWINDOW, Offscreen ? -8000 : Win32.CW_USEDEFAULT, Offscreen ? 0 : Win32.CW_USEDEFAULT,
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
            case Win32.WM_KEYDOWN:
                // 키 반복(lParam 30번 비트)은 무시한다 — 누르고 있는 동안은 Update 가 알아서 이어 걷는다.
                if (((long)lParam & 0x40000000) == 0) OnKeyDown((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_KEYUP:
                _heldMoveKeys.Remove((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_MOUSEWHEEL:
                ScrollCamera(-(short)(((long)wParam >> 16) & 0xFFFF) / 120.0 * TileH * 2);
                return IntPtr.Zero;
            case Win32.WM_APP_SNAPSHOT:
                SaveSnapshot();
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
            case Win32.WM_RBUTTONDOWN:
            case Win32.WM_MOUSEMOVE:
            {
                int bx = (int)((short)((long)lParam & 0xFFFF) / _zoom), by = (int)((short)(((long)lParam >> 16) & 0xFFFF) / _zoom) + _camY;
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
        if (_keysOpen) { OnKeysKey(key); return; }
        if (key == Win32.VK_ESCAPE)
        {
            if (_statusUnit >= 0) _statusUnit = -1;
            else if (_ringUnit >= 0) CancelRing();
            else CancelStep(undoMove: true);
            return;
        }
        if (key == Win32.VK_RETURN && _ringUnit >= 0 && _ringPhase == RingPhase.Idle && _ringHover >= 0) { PickRingItem(_ringHover); return; }
        if (_statusUnit >= 0) return;

        // 공격 대상 고르는 중: Enter·공격 키 = 커서의 적 공격, 다음 인물 키(Tab) = 다른 적
        if (_targetWork >= 0 && _targetIsBasicAttack && _ringUnit < 0)
        {
            if (key == Win32.VK_RETURN || _keys.ActionFor(key) == KeyAction.Attack) { AttackCursorTarget(); return; }
            if (_keys.ActionFor(key) == KeyAction.NextUnit) { CycleAttackCursor(); return; }
        }

        switch (MoveActionFor(key) ?? _keys.ActionFor(key))
        {
            case KeyAction.Grid: _showGrid = !_showGrid; break;
            case KeyAction.Gauges: _showGauges = !_showGauges; break;
            case KeyAction.Ring: ToggleRingForSelected(); break;
            case KeyAction.Attack: RingShortcut(RingCommand.Attack); break;
            case KeyAction.Ability: RingShortcut(RingCommand.Ability); break;
            case KeyAction.Rest: RingShortcut(RingCommand.Rest); break;
            case KeyAction.Status:
                if (_ringUnit >= 0) RingShortcut(RingCommand.Status);
                else if ((uint)_selected < _units.Length) _statusUnit = _selected;
                break;
            case KeyAction.NextUnit:
                for (int i = 1; i <= _units.Length; i++)
                {
                    int next = (Math.Max(_selected, 0) + i) % _units.Length;
                    if (_units[next].Alive) { _selected = next; break; }
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
    private void OnClick(int clientX, int clientY)
    {
        int bx = (int)(clientX / _zoom), by = (int)(clientY / _zoom) + _camY;
        if (OnKeysClick(bx, by) || OnStatusClick(bx, by) || OnRingClick(bx, by) || OnAbilityMenuClick(bx, by)) return;

        int boardY = by - GridTop;
        if (boardY < 0) return;
        int col = bx / TileW, row = boardY / TileH;
        if (OnTargetClick(col, row)) return;

        int index = UnitAtBoard(bx, by);
        if (index >= 0) { _selected = index; return; }
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

    private void Update(double dt)
    {
        foreach (var unit in _units) unit.Advance(dt / StepSeconds, dt);

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
        UpdateCamera(dt);
        UpdateRing();
        UpdateTurn();
        RefreshMoveRange();
    }

    // ── 프레임 합성 ──────────────────────────────────────────────────────────

    private void Render()
    {
        Compose();
        Upload();
        Draw();
    }

    private void Compose()
    {
        Array.Fill(_fb, BgColor);
        DrawBackground();
        DrawMoveRange();
        DrawWorkRange();
        if (_showGrid) DrawGridLines();
        DrawUnits();
        if (_showGauges) DrawGauges();
        DrawPopups();
        DrawStatus();
        DrawRing();
        DrawAbilityMenu();
        DrawStatusScreen();
        DrawToast();
        DrawOutcome();
        DrawKeysPanel();
    }

    private void DrawBackground()
    {
        if (_map is not { } map) return;

        int boardH = Rows * TileH;
        for (int y = 0; y < map.Height; y++)
        {
            int by = y + map.OriginY;
            if ((uint)by >= boardH) continue;

            int w = Math.Min(map.Width, BoardWidth);
            var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(map.Bgra.AsSpan(y * map.Width * 4, w * 4));
            src.CopyTo(_fb.AsSpan((GridTop + by) * BoardWidth, w));
        }
    }

    private void DrawGridLines()
    {
        for (int c = 0; c <= Cols; c++)
        {
            int x = Math.Min(c * TileW, BoardWidth - 1);
            for (int y = GridTop; y < BoardHeight; y++) SetPixel(x, y, GridLine);
        }
        for (int r = 0; r <= Rows; r++)
        {
            int y = Math.Min(GridTop + r * TileH, BoardHeight - 1);
            for (int x = 0; x < BoardWidth; x++) SetPixel(x, y, GridLine);
        }
    }

    private void DrawUnits()
    {
        var sprites = _sprites;

        // 아래 줄 인물이 위 줄 인물을 가리도록 발 위치(y) 순서로 그린다.
        foreach (int i in Enumerable.Range(0, _units.Length).OrderBy(i => _units[i].Y))
        {
            var unit = _units[i];
            if (!unit.Alive) continue;
            var (footX, footY) = UnitFoot(unit);
            int headY = footY - TileH;

            if (sprites.TryGetValue(unit.ChrCode, out var sprite))
            {
                var frame = sprite.FrameFor(unit);
                BlitMasked(frame.Px, frame.W, frame.H, footX + frame.X, footY + frame.Y);
                headY = footY + frame.Y;
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
    private static (int X, int Y) UnitFoot(UnitState unit) =>
        ((int)(unit.X * TileW) + TileW / 2, GridTop + (int)(unit.Y * TileH) + TileH / 2);

    private void DrawStatus()
    {
        FillRect(0, _camY, BoardWidth, GridTop, _camY > 0 ? 0xE014100C : 0);
        if (_loading)
        {
            DrawText("전투 자료를 읽는 중...", 4, _camY + 4, White);
            return;
        }

        int allies = _units.Count(u => u.Alive && u.IsAlly), enemies = _units.Count(u => u.Alive && !u.IsAlly);
        DrawText($"{BattleDemoScene.Title} — 전투 Btl {BattleDemoScene.BtlId:D4}   아군 {allies}   적군 {enemies}   클릭·{KeyBindings.KeyName(_keys[KeyAction.NextUnit])}: 인물 보기   {KeyBindings.KeyName(_keys[KeyAction.Grid])}: 격자   {KeyBindings.KeyName(_keys[KeyAction.Gauges])}: 체력바   설정 메뉴: 단축키",
                 4, _camY + 4, White);
        if (_loadError.Length > 0) DrawText($"못 읽은 자료가 있습니다: {_loadError}", 4, _camY + 20, 0xFFD05050);
        else DrawText(TurnLine(), 4, _camY + 20, 0xFFFFE8A0);
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

    private void BlitMasked(uint[] src, int srcW, int srcH, int dstX, int dstY)
    {
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
                int2 uv = int2(i.pos.xy / {{_zoom.ToString(System.Globalization.CultureInfo.InvariantCulture)}});
                return Board.Load(int3(uv, 0));
            }
            """;
        var vsBlob = Compiler.Compile(shader, "VS", "battlescene.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(shader, "PS", "battlescene.hlsl", "ps_4_0");
        _vs = _device.CreateVertexShader(vsBlob.Span);
        _ps = _device.CreatePixelShader(psBlob.Span);

        _boardTex = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = BoardWidth,
            Height = ViewHeight,
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

    private void CreateSwapChain()
    {
        int w = (int)(BoardWidth * _zoom), h = (int)(ViewHeight * _zoom);
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
        };
        _swapChain = factory.CreateSwapChainForHwnd(_device, _hwnd, desc);
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
                var dst = new Span<uint>((void*)(map.DataPointer + y * map.RowPitch), BoardWidth);
                _fb.AsSpan((_camY + y) * BoardWidth, BoardWidth).CopyTo(dst);
            }
        }
        finally { _ctx.Unmap(_boardTex, 0); }
    }

    private void Draw()
    {
        int w = (int)(BoardWidth * _zoom), h = (int)(ViewHeight * _zoom);
        _ctx.OMSetRenderTargets(_backBufferRtv);
        _ctx.RSSetViewport(0, 0, w, h);
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
        _backBufferRtv?.Dispose();
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
        foreach (var motion in motions)
            for (int i = 0; i < motion.Frames.Count; i++)
                _frames[(motion.Id, i)] = SpriteFrame.From(motion.Frames[i]);
        _first = SpriteFrame.From(motions[0].Frames[0]);
    }

    /// <summary>
    /// 지금 보일 컷 — 걷는 중이면 걷기(동작 1), 아니면 서기(동작 0, 까딱이는 숨쉬기) 모션을 틱에 맞춰 넘긴다.
    /// 오른쪽을 보면 옆모습 컷을 뒤집는다.
    /// </summary>
    public SpriteFrame FrameFor(UnitState unit)
    {
        int action = unit.Action >= 0 ? unit.Action : unit.IsMoving ? ObsMotionTable.ActionWalk : ObsMotionTable.ActionStand;
        var clip = _table?.Resolve(action, ObsMotionTable.DirectionOf(unit.Facing));
        var key = clip?.KeyAt((int)(unit.AnimTime * BattleSceneWindow.TicksPerSecond), loop: unit.Action < 0);
        if (key is not { } k || !_frames.TryGetValue((k.SubentryId, k.Slot), out var frame)) return _first;
        if (unit.Facing != Facing.Right) return frame;

        if (!_mirrored.TryGetValue((k.SubentryId, k.Slot), out var mirrored))
            _mirrored[(k.SubentryId, k.Slot)] = mirrored = frame.Mirrored();
        return mirrored;
    }

    /// <summary>한 번 재생할 동작의 길이(초). 모션표에 없으면 0.</summary>
    public double ActionSeconds(int action, Facing facing) =>
        (_table?.Resolve(action, ObsMotionTable.DirectionOf(facing))?.Length ?? 0) / BattleSceneWindow.TicksPerSecond;
}

/// <summary>판 위 인물 하나의 지금 상태 — 칸 자리, 바라보는 쪽, 걷는 중이면 어디서 어디로 얼마나 왔는지.</summary>
internal sealed class UnitState(BattleUnit unit)
{
    public int ChrCode { get; } = unit.ChrCode;
    public bool IsAlly { get; } = unit.IsAlly;

    /// <summary>도착할(걷는 중이면 향하는) 칸. 자리 차지 판정도 이 칸으로 한다.</summary>
    public int Col { get; private set; } = unit.Col;
    public int Row { get; private set; } = unit.Row;
    public Facing Facing { get; set; } = unit.IsAlly ? Facing.Right : Facing.Left;

    private int _fromCol = unit.Col, _fromRow = unit.Row;
    private double _progress = 1;

    public bool IsMoving => _progress < 1;

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

    /// <summary>이번 칸에서 남은 진행량(한 칸 = 1) — 이어 걸을 때 다음 칸에 넘겨 속도가 들쭉날쭉하지 않게 한다.</summary>
    private double _carry;

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
        _progress = Math.Min(carry, 0.99);
    }

    public void Advance(double progressDelta, double dt)
    {
        AnimTime += dt;
        if (Action >= 0 && (_actionLeft -= dt) <= 0) { Action = -1; AnimTime = _idleOffset; }
        if (!IsMoving) return;
        double next = _progress + progressDelta;
        _progress = Math.Min(1, next);
        if (!IsMoving) { _justArrived = true; _carry = next - 1; }
    }

    /// <summary>이어 걷지 않고 멈췄으면 서기 숨쉬기로 돌린다.</summary>
    public void SettleIfStopped()
    {
        if (!_justArrived) return;
        _justArrived = false;
        AnimTime = _idleOffset;
    }
}

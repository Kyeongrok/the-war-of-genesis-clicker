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
/// 배경 그림은 <b>확인 못 한 자리표시자</b>다 — 어느 <c>Map</c>/<c>Bgr</c> id가 이 전투에
/// 진짜 쓰이는지 아직 확인 못 했다(<c>.btl</c> 헤더 두 번째 워드 61이 <c>Map/0061.map</c> 일
/// 수 있다). 그래서 <c>Bgr</c> 묶음 261장 중 위에서 내려다보는 전투 배경으로 보이는
/// <c>0200.bgr</c> 을 임시로 골라 썼다.
/// </remarks>
internal sealed unsafe class BattleSceneWindow : IDisposable
{
    private static readonly BattleUnit[] Roster = BattleDemoScene.Roster;

    /// <summary>한 칸 걸어가는 데 드는 시간(초)과 걷기 컷을 넘기는 빠르기(컷/초).</summary>
    public const double StepSeconds = 0.25, WalkFps = 12;

    private const int TileSize = 25;
    private const int Cols = BattleDemoScene.Cols, Rows = BattleDemoScene.Rows;
    private const int GridTop = 40;
    private const int BoardWidth = Cols * TileSize, BoardHeight = GridTop + Rows * TileSize;
    private const int Zoom = 2;

    private const string GameRoot = @"C:\Users\Administrator\Downloads\gen3pt2";
    private const string PlaceholderBgFile = BattleDemoScene.PlaceholderBackgroundFile;

    private const uint BgColor = 0xFF14100C;
    private const uint GridLine = 0x40FFFFFF;
    private const uint AllyMark = 0xFF5AA0F0, EnemyMark = 0xFFE05050, SelectMark = 0xFFFFD040;
    private const uint White = 0xFFF2EAD6;
    private const uint DimGray = 0xFFA09888;

    private uint[] _bgPixels = [];
    private Dictionary<int, UnitSprite> _sprites = [];
    private readonly UnitState[] _units = [.. Roster.Select(u => new UnitState(u))];
    private int _selected = -1;
    private readonly Dictionary<int, string> _names = [];
    private string _loadError = "";
    private volatile bool _loading = true;
    private bool _showGrid;

    private readonly uint[] _fb = new uint[BoardWidth * BoardHeight];
    private readonly Dictionary<string, (uint[] Px, int W, int H)> _textCache = [];

    private bool _running;
    private double _lastTime;

    private IntPtr _hwnd;
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
            _bgPixels = LoadBackground();

            string assetsRoot = FindRepoAssetsRoot();
            var manifests = CollectExportedManifests(assetsRoot);
            var sprites = new Dictionary<int, UnitSprite>();

            foreach (int chrCode in Roster.Select(u => u.ChrCode).Distinct())
            {
                var (name, obsPath) = manifests.TryGetValue(chrCode, out var m)
                    ? (m.Name, Path.Combine(assetsRoot, CharacterExport.FolderNameFor(chrCode, m.Name), CharacterExport.ObsFileName(m.SpriteCode)))
                    : LoadFromGameFolder(chrCode);

                _names[chrCode] = name;

                int spriteCode = int.Parse(Path.GetFileNameWithoutExtension(obsPath));
                var walk = WalkCycles.Find(spriteCode);

                // 걷기 표가 있는 인물만 몸짓벌을 다 푼다(느리다). 없으면 첫 컷 하나로 서 있는다.
                List<ObsFrame> frames = walk != null
                    ? [.. ObsSprite.Decode(obsPath).SelectMany(m => m.Frames)]
                    : ObsSprite.DecodeFirstFrame(obsPath) is { } first ? [first] : [];
                if (frames.Count == 0) continue;

                sprites[chrCode] = new UnitSprite([.. frames.Select(SpriteFrame.From)], walk);
            }
            _sprites = sprites;
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

    /// <summary>
    /// <c>assets/backgrounds/</c> 의 자리표시자 배경(JPEG)을 GDI+ 로 읽어 보드 크기(800×800)에
    /// 그대로 맞춘다 — 크기가 이미 딱 맞아서 늘리지 않는다.
    /// </summary>
    private static uint[] LoadBackground()
    {
        string path = Path.Combine(AssetsFolder.Find("backgrounds"), PlaceholderBgFile);
        if (!File.Exists(path)) throw new FileNotFoundException($"배경 그림을 못 찾았습니다: {path}");

        using var bitmap = new Bitmap(path);
        using var resized = new Bitmap(bitmap, new Size(Cols * TileSize, Rows * TileSize));

        var rect = new Rectangle(0, 0, resized.Width, resized.Height);
        var data = resized.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var px = new uint[resized.Width * resized.Height];
            for (int y = 0; y < resized.Height; y++)
            {
                var row = new ReadOnlySpan<uint>((void*)(data.Scan0 + y * data.Stride), resized.Width);
                row.CopyTo(px.AsSpan(y * resized.Width, resized.Width));
            }
            return px;
        }
        finally { resized.UnlockBits(data); }
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

        Win32.ShowWindow(_hwnd, 5);
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
        int pixelW = BoardWidth * Zoom, pixelH = BoardHeight * Zoom;

        var rect = new Win32.Rect { Left = 0, Top = 0, Right = pixelW, Bottom = pixelH };
        Win32.AdjustWindowRect(ref rect, Win32.WS_OVERLAPPEDWINDOW, false);

        _active = this;
        _hwnd = Win32.CreateWindowExW(0, ClassName, $"{BattleDemoScene.Title} — 전투 Btl {BattleDemoScene.BtlId:D4} (자리표시자 배경)",
            Win32.WS_OVERLAPPEDWINDOW, Win32.CW_USEDEFAULT, Win32.CW_USEDEFAULT,
            rect.Width, rect.Height,
            IntPtr.Zero, IntPtr.Zero, Win32.GetModuleHandleW(null), IntPtr.Zero);
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
                OnKeyDown((int)wParam);
                return IntPtr.Zero;
            case Win32.WM_LBUTTONDOWN:
                OnClick((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // ── 입력 · 이동 ──────────────────────────────────────────────────────────

    private void OnKeyDown(int key)
    {
        switch (key)
        {
            case Win32.VK_ESCAPE: _running = false; break;
            case Win32.VK_G: _showGrid = !_showGrid; break;
            case Win32.VK_TAB: _selected = (_selected + 1) % _units.Length; break;
            case Win32.VK_UP: TryStep(Facing.Up, 0, -1); break;
            case Win32.VK_DOWN: TryStep(Facing.Down, 0, 1); break;
            case Win32.VK_LEFT: TryStep(Facing.Left, -1, 0); break;
            case Win32.VK_RIGHT: TryStep(Facing.Right, 1, 0); break;
        }
    }

    /// <summary>클릭한 칸에 선 인물을 고른다. 빈 칸이면 선택을 푼다.</summary>
    private void OnClick(int clientX, int clientY)
    {
        int boardY = clientY / Zoom - GridTop;
        if (boardY < 0) return;
        int col = clientX / Zoom / TileSize, row = boardY / TileSize;
        _selected = Array.FindIndex(_units, u => u.Col == col && u.Row == row);
    }

    /// <summary>
    /// 고른 인물을 그쪽으로 돌려세우고, 판 안이고 다른 인물이 없으면 한 칸 걷게 한다. 걷는 중이면
    /// 무시한다 — 방향키를 누르고 있으면 키 반복으로 한 칸 끝날 때마다 다시 들어와 계속 걷는다.
    /// </summary>
    private void TryStep(Facing facing, int dx, int dy)
    {
        if ((uint)_selected >= _units.Length) return;
        var unit = _units[_selected];
        if (unit.IsMoving) return;

        unit.Facing = facing;
        int col = unit.Col + dx, row = unit.Row + dy;
        if ((uint)col >= Cols || (uint)row >= Rows) return;
        if (_units.Any(u => u.Col == col && u.Row == row)) return;

        unit.BeginStep(col, row);
    }

    private void Update(double dt)
    {
        foreach (var unit in _units) unit.Advance(dt / StepSeconds, dt);
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
        if (_showGrid) DrawGridLines();
        DrawUnits();
        DrawStatus();
    }

    private void DrawBackground()
    {
        if (_bgPixels.Length == 0) return;
        for (int y = 0; y < Rows * TileSize; y++)
        {
            var src = _bgPixels.AsSpan(y * Cols * TileSize, Cols * TileSize);
            var dst = _fb.AsSpan((GridTop + y) * BoardWidth, Cols * TileSize);
            src.CopyTo(dst);
        }
    }

    private void DrawGridLines()
    {
        for (int c = 0; c <= Cols; c++)
        {
            int x = c * TileSize;
            for (int y = GridTop; y < BoardHeight; y++) SetPixel(x, y, GridLine);
        }
        for (int r = 0; r <= Rows; r++)
        {
            int y = GridTop + r * TileSize;
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
            int tileX = unit.Col * TileSize, tileY = GridTop + unit.Row * TileSize;
            uint mark = i == _selected ? SelectMark : unit.IsAlly ? AllyMark : EnemyMark;
            FillRect(tileX + 2, tileY + 2, TileSize - 4, TileSize - 4, mark & 0x60FFFFFF | 0x60000000);
            StrokeRect(tileX, tileY, TileSize, TileSize, mark);

            if (!sprites.TryGetValue(unit.ChrCode, out var sprite)) continue;

            var frame = sprite.FrameFor(unit);
            int footX = (int)(unit.X * TileSize) + TileSize / 2;
            int footY = GridTop + (int)(unit.Y * TileSize) + TileSize / 2;
            BlitMasked(frame.Px, frame.W, frame.H, footX + frame.X, footY + frame.Y);
        }
    }

    private void DrawStatus()
    {
        if (_loading)
        {
            DrawText("전투 자료를 읽는 중...", 4, 4, White);
            return;
        }

        int allies = Roster.Count(u => u.IsAlly), enemies = Roster.Count(u => !u.IsAlly);
        DrawText($"{BattleDemoScene.Title} — 전투 Btl {BattleDemoScene.BtlId:D4}   아군 {allies}   적군 {enemies}   배경: 자리표시자(Bgr 0200, 미확인)",
                 4, 4, White);
        if (_loadError.Length > 0) DrawText($"못 읽은 자료가 있습니다: {_loadError}", 4, 20, 0xFFD05050);
        else DrawText($"클릭·Tab: 인물 고르기   방향키: 걷기   G: 격자 {(_showGrid ? "끄기" : "켜기")}", 4, 20, DimGray);
    }

    // ── 글자 ─────────────────────────────────────────────────────────────────

    private (uint[] Px, int W, int H) GetText(string text, uint argb)
    {
        string key = text + ":" + argb;
        if (_textCache.TryGetValue(key, out var cached)) return cached;

        var color = Color.FromArgb((int)argb);
        var made = TextRaster.Render(text, color) ?? ([], 0, 0);
        if (_textCache.Count > 500) _textCache.Clear();
        _textCache[key] = made;
        return made;
    }

    private void DrawText(string text, int x, int y, uint argb)
    {
        if (text.Length == 0) return;
        var (px, w, h) = GetText(text, argb);
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
                int2 uv = int2(i.pos.x / {{Zoom}}.0, i.pos.y / {{Zoom}}.0);
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
            Height = BoardHeight,
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
        int w = BoardWidth * Zoom, h = BoardHeight * Zoom;
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
            for (int y = 0; y < BoardHeight; y++)
            {
                var dst = new Span<uint>((void*)(map.DataPointer + y * map.RowPitch), BoardWidth);
                _fb.AsSpan(y * BoardWidth, BoardWidth).CopyTo(dst);
            }
        }
        finally { _ctx.Unmap(_boardTex, 0); }
    }

    private void Draw()
    {
        int w = BoardWidth * Zoom, h = BoardHeight * Zoom;
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
internal sealed class UnitSprite(SpriteFrame[] frames, WalkCycle? walk)
{
    private readonly Dictionary<int, SpriteFrame> _mirrored = [];

    public SpriteFrame FrameFor(UnitState unit)
    {
        if (walk == null) return frames[0];

        var range = walk.For(unit.Facing);
        int step = unit.IsMoving ? (int)(unit.WalkTime * BattleSceneWindow.WalkFps) % range.Count : 0;
        int index = Math.Min(range.Start + step, frames.Length - 1);
        if (unit.Facing != Facing.Right) return frames[index];

        if (!_mirrored.TryGetValue(index, out var mirrored))
            _mirrored[index] = mirrored = frames[index].Mirrored();
        return mirrored;
    }
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
    public double WalkTime { get; private set; }

    /// <summary>그릴 자리(칸 단위, 소수) — 걷는 중이면 두 칸 사이.</summary>
    public double X => _fromCol + (Col - _fromCol) * _progress;
    public double Y => _fromRow + (Row - _fromRow) * _progress;

    public void BeginStep(int col, int row)
    {
        _fromCol = Col; _fromRow = Row;
        Col = col; Row = row;
        _progress = 0;
    }

    public void Advance(double progressDelta, double dt)
    {
        if (!IsMoving) { WalkTime = 0; return; }
        WalkTime += dt;
        _progress = Math.Min(1, _progress + progressDelta);
    }
}

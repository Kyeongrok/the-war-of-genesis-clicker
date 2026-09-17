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
/// 「게임을 켜면 첫 전투 맵이 펼쳐지고, 거기 투입되는 캐릭터들이 배치되어 있다」는 첫 장면.
/// </summary>
/// <remarks>
/// 아군·적군 구성은 <c>Project/the-war-of-genesis/분석/분석-전투구성.md</c> 에서 정적
/// 분석으로 찾아낸 <b>실제 <c>Btl/0173.btl</c> 자료</b>다 — "영혼의 검" 챕터(<c>0019.chp</c>)
/// 의 첫 전투이자, 게임 전체를 통틀어 그 챕터에서 가장 먼저 나오는 전투. 캐릭터 배치
/// (Chr 코드·X/Y)는 이 파일에서 직접 읽어낸 값을 그대로 박아 뒀다 — 아직 <c>.btl</c> 을
/// 일반적으로 읽어들이는 코드는 없다(이 전투 하나만 보여 주는 첫 데모).
///
/// 배경 그림은 <b>확인 못 한 자리표시자</b>다 — 어느 <c>Map</c>/<c>Bgr</c> id가 이 전투에
/// 진짜 쓰이는지는 <c>CBattle+0xa0</c> 을 채우는 함수가 실행 시점 상태(세이브 진행도)를
/// 거쳐야 풀리는 값이라 정적 분석만으론 못 찾았다(분석 노트 참고). 그래서 <c>Bgr</c> 묶음
/// 261장 중 위에서 내려다보는 전투 배경으로 보이는 <c>0200.bgr</c> 을 임시로 골라 썼다.
/// </remarks>
internal sealed unsafe class BattleSceneWindow : IDisposable
{
    private readonly record struct Unit(int ChrCode, int Col, int Row, bool IsAlly);

    /// <summary><c>Btl/0173.btl</c> 을 파싱해서 얻은 실제 배치. 아군 7 + 적군 15 = 22명.</summary>
    private static readonly Unit[] Roster =
    [
        // 아군 — 천사(266)×2, 아델룬장교(302)×4, 세큘리티볼(19)×1
        new(266, 17, 11, true), new(266, 13, 14, true),
        new(302, 7, 30, true), new(302, 8, 18, true), new(302, 10, 24, true), new(302, 3, 27, true),
        new(19, 5, 22, true),
        // 적군 — 유블레인(33)×3, 엠블라(60)×6, 시녀(54)×6
        new(33, 1, 20, false), new(33, 30, 30, false), new(33, 26, 8, false),
        new(60, 15, 8, false), new(60, 19, 10, false), new(60, 13, 11, false),
        new(60, 6, 16, false), new(60, 10, 19, false), new(60, 11, 20, false),
        new(54, 9, 21, false), new(54, 5, 17, false), new(54, 11, 22, false),
        new(54, 16, 14, false), new(54, 15, 10, false), new(54, 11, 13, false),
    ];

    private const int TileSize = 25;
    private const int Cols = 32, Rows = 32;
    private const int GridTop = 40;
    private const int BoardWidth = Cols * TileSize, BoardHeight = GridTop + Rows * TileSize;
    private const int Zoom = 1;

    private const string GameRoot = @"C:\Users\Administrator\Downloads\gen3pt2";
    private const string PlaceholderBgFile = "0200_placeholder.jpg";

    private const uint BgColor = 0xFF14100C;
    private const uint GridLine = 0x40FFFFFF;
    private const uint AllyMark = 0xFF5AA0F0, EnemyMark = 0xFFE05050;
    private const uint White = 0xFFF2EAD6;
    private const uint DimGray = 0xFFA09888;

    private uint[] _bgPixels = [];
    private readonly Dictionary<int, (uint[] Px, int W, int H)> _sprites = [];
    private readonly Dictionary<int, string> _names = [];
    private string _loadError = "";
    private volatile bool _loading = true;

    private readonly uint[] _fb = new uint[BoardWidth * BoardHeight];
    private readonly Dictionary<string, (uint[] Px, int W, int H)> _textCache = [];

    private bool _running;

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

    /// <summary>배경 스레드에서 배경 그림·캐릭터 22명(고유 5종)을 읽는다. 창은 먼저 뜬다.</summary>
    private void LoadScene()
    {
        try
        {
            _bgPixels = LoadBackground();

            string assetsRoot = FindRepoAssetsRoot();
            var manifests = CollectExportedManifests(assetsRoot);

            foreach (int chrCode in Roster.Select(u => u.ChrCode).Distinct())
            {
                var (name, obsPath) = manifests.TryGetValue(chrCode, out var m)
                    ? (m.Name, Path.Combine(assetsRoot, CharacterExport.FolderNameFor(chrCode, m.Name), CharacterExport.ObsFileName(m.SpriteCode)))
                    : LoadFromGameFolder(chrCode);

                _names[chrCode] = name;

                var frame = ObsSprite.DecodeFirstFrame(obsPath);
                if (frame == null) continue;

                var px = new uint[frame.Width * frame.Height];
                Buffer.BlockCopy(frame.Bgra, 0, px, 0, frame.Bgra.Length);
                _sprites[chrCode] = (px, frame.Width, frame.Height);
            }
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
        string repoRoot = FindRepoRoot();
        string path = Path.Combine(repoRoot, "assets", "backgrounds", PlaceholderBgFile);
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

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "WarOfGenesis.Editor")))
                return dir.FullName;
        throw new DirectoryNotFoundException("저장소 뿌리(WarOfGenesis.Editor 옆)를 못 찾았습니다.");
    }

    private static string FindRepoAssetsRoot()
    {
        try { return Path.Combine(FindRepoRoot(), "assets", "characters"); }
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

            Render();
        }
        _ = clock.Elapsed;
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
        _hwnd = Win32.CreateWindowExW(0, ClassName, "영혼의 검 — 첫 전투 (Btl 0173, 자리표시자 배경)",
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
                if ((int)wParam == Win32.VK_ESCAPE) _running = false;
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
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
        DrawGridLines();
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
        foreach (var unit in Roster)
        {
            int tileX = unit.Col * TileSize, tileY = GridTop + unit.Row * TileSize;
            uint mark = unit.IsAlly ? AllyMark : EnemyMark;
            FillRect(tileX + 2, tileY + 2, TileSize - 4, TileSize - 4, mark & 0x60FFFFFF | 0x60000000);
            StrokeRect(tileX, tileY, TileSize, TileSize, mark);

            if (_sprites.TryGetValue(unit.ChrCode, out var sprite))
            {
                int x = tileX + TileSize / 2 - sprite.W / 2;
                int y = tileY + TileSize - sprite.H;
                BlitMasked(sprite.Px, sprite.W, sprite.H, x, y);
            }
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
        DrawText($"영혼의 검 — 첫 전투 (Btl 0173)   아군 {allies}   적군 {enemies}   배경: 자리표시자(Bgr 0200, 미확인)",
                 4, 4, White);
        if (_loadError.Length > 0) DrawText($"못 읽은 자료가 있습니다: {_loadError}", 4, 20, 0xFFD05050);
        else DrawText("파란 테두리 = 아군, 빨간 테두리 = 적군", 4, 20, DimGray);
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

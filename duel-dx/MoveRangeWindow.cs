using System.Diagnostics;
using System.IO;
using DuelDx.Native;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 「전투 중 캐릭터 차례가 오면 바닥에 이동범위가 깔리는」 화면의 첫 데모.
/// </summary>
/// <remarks>
/// 캐릭터 그림은 <b>진짜 게임 자료</b>다 — <c>WarOfGenesis.Assets</c>(TXR 이름표 · Chr
/// 레코드 · Obs 도트그림)로 실제 <c>gen3pt2</c> 게임 폴더에서 읽는다. 반면 <b>이동범위
/// 모양은 아직 임시</b>다 — <c>Project/the-war-of-genesis/분석/모션분석.md</c> 에 적어
/// 둔 정적 분석(0x1006dee0 부근)으로는 "커서가 가리키는 한 칸이 다닐 수 있는가"를
/// 확인하는 자리까지만 찾았고, 이동력만큼 갈 수 있는 칸 전체를 골라내는 flood-fill
/// 본체(지형 비용·장애물)는 아직 못 찾았다. 그래서 지금은 <b>맨해튼 거리</b>(상하좌우
/// 이동만 세는 다이아몬드 모양)로 대신한다 — 진짜 셈을 찾으면 <see cref="InRange"/>
/// 하나만 바꾸면 된다.
///
/// 창·그리기 얼개(Win32 창 + D3D11 텍스처 한 장 찍기)는 옛 일기토 데모와 같다.
/// </remarks>
internal sealed unsafe class MoveRangeWindow : IDisposable
{
    private const int TileSize = 32;
    private const int Cols = 13, Rows = 8;
    private const int GridTop = 30;
    private const int BoardWidth = Cols * TileSize, BoardHeight = GridTop + Rows * TileSize;
    private const int Zoom = 2;

    /// <summary>창세기전3 파트2 게임 폴더. <c>assets/characters</c> 에 내보낸 인물이 없을 때만 여기서 읽는다.</summary>
    private const string GameRoot = @"C:\Users\Administrator\Downloads\gen3pt2";

    /// <summary><c>assets/characters</c> 에도 게임 폴더에도 아무것도 없을 때 물러설 인물 번호.</summary>
    private const int FallbackChrCode = 7;

    /// <summary>이동력(칸 수). 실제 능력치 대신 임시로 박아 둔 값이다.</summary>
    private const int MovePoints = 4;

    private const uint BgColor = 0xFF1A1410;
    private const uint TileA = 0xFF3A5A2E, TileB = 0xFF335227;
    private const uint TileEdge = 0xFF1E2E16;
    private const uint RangeTint = 0xA0348CE0;
    private const uint CharTileTint = 0xC0E0C848;
    private const uint White = 0xFFF2EAD6;
    private const uint Yellow = 0xFFE8C864;
    private const uint DimGray = 0xFFA09888;

    private uint[] _charFrame = [];
    private int _charFrameW, _charFrameH;
    private string _charName = "?";
    private int _charSpriteCode;
    private string _loadError = "";

    /// <summary>
    /// 자료를 아직 읽는 중인가. <see cref="LoadCharacter"/> 가 배경 스레드에서 돌므로
    /// 창은 <b>곧바로</b> 뜨고, 그림이 도착하면 다음 프레임에 반영된다.
    /// </summary>
    private volatile bool _loading = true;

    private int _charCol = Cols / 2, _charRow = Rows / 2;
    private bool _showRange;

    private readonly uint[] _fb = new uint[BoardWidth * BoardHeight];
    private readonly Dictionary<string, (uint[] Px, int W, int H)> _textCache = [];

    private bool _running;

    private IntPtr _hwnd;
    private static readonly Win32.WndProc StaticWndProcDelegate = StaticWndProcTrampoline;
    private static MoveRangeWindow? _active;
    private static ushort _classAtom;
    private const string ClassName = "MoveRangeDx";

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _ctx = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID3D11RenderTargetView _backBufferRtv = null!;
    private ID3D11Texture2D _boardTex = null!;
    private ID3D11ShaderResourceView _boardSrv = null!;
    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;

    // ── 게임 자료 읽기 ───────────────────────────────────────────────────────

    /// <summary>
    /// 배경 스레드에서 캐릭터 그림을 읽는다. Chr 578개 훑기·TXR 이름표 훑기·(처음
    /// 한 번이면) Obs pak 풀기까지 겹치면 꽤 걸릴 수 있어서, 창을 막지 않으려고
    /// <see cref="Run"/> 이 창을 띄운 <b>다음</b>에 이걸 따로 돌린다. 또
    /// <see cref="ObsSprite.DecodeFirstFrame"/> 로 <b>필요한 한 장만</b> 풀어서
    /// (원래 sprite 하나가 백 장 넘는 몸짓을 다 풀면 몇 초씩 걸렸다) 훨씬 빨라졌다.
    /// </summary>
    private void LoadCharacter()
    {
        try
        {
            var (name, spriteCode, obsPath) = FindAnyExported() ?? LoadFromGameFolder(FallbackChrCode);

            var idle = ObsSprite.DecodeFirstFrame(obsPath)
                       ?? throw new InvalidDataException($"{Path.GetFileName(obsPath)} 에서 그림을 못 풀었습니다.");

            var frame = new uint[idle.Width * idle.Height];
            Buffer.BlockCopy(idle.Bgra, 0, frame, 0, idle.Bgra.Length);

            // 다 갖춰진 뒤에 한꺼번에 반영한다 — 그리는 스레드(Render)가 절반만
            // 채워진 상태를 보지 않게.
            _charFrameW = idle.Width;
            _charFrameH = idle.Height;
            _charFrame = frame;
            _charName = name;
            _charSpriteCode = spriteCode;
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
    /// <c>assets/characters/</c> 밑에 뽑아 둔 인물이 있으면 <b>아무거나 하나</b> 골라 쓴다 —
    /// 원본 게임 없이도 도는 길이다. <c>WarOfGenesis.Editor</c> 의 「내보내기」가 그 폴더를
    /// 만든다. 폴더 이름 순으로 골라 늘 같은 것이 뜨게 한다.
    /// </summary>
    private static (string Name, int SpriteCode, string ObsPath)? FindAnyExported()
    {
        string assetsRoot = FindRepoAssetsRoot();
        if (assetsRoot.Length == 0 || !Directory.Exists(assetsRoot)) return null;

        foreach (var folder in Directory.EnumerateDirectories(assetsRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            var manifest = CharacterExport.LoadManifest(folder);
            if (manifest == null) continue;

            string obsPath = Path.Combine(folder, CharacterExport.ObsFileName(manifest.SpriteCode));
            if (File.Exists(obsPath)) return (manifest.Name, manifest.SpriteCode, obsPath);
        }
        return null;
    }

    /// <summary>저장소 뿌리를 거슬러 올라가 <c>assets/characters</c> 자리를 찍어 준다. 못 찾으면 빈 문자열.</summary>
    private static string FindRepoAssetsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "WarOfGenesis.Editor")))
                return Path.Combine(dir.FullName, "assets", "characters");
        }
        return "";
    }

    /// <summary>내보낸 것이 없을 때의 마지막 수단 — 실제 게임 폴더에서 읽는다.</summary>
    private static (string Name, int SpriteCode, string ObsPath) LoadFromGameFolder(int chrCode)
    {
        string chrFolder = Path.Combine(GameRoot, "Chr");
        string obsFolder = Path.Combine(GameRoot, "Obs");
        string txrPath = Path.Combine(GameRoot, "TXR", "Txr.dat");

        if (!Directory.EnumerateFiles(chrFolder, "*.chr").Any() && File.Exists(Path.Combine(chrFolder, "Chr.idx")))
            PakArchive.Extract(chrFolder, "Chr");

        var txr = TxrTable.Open(txrPath);
        var record = ChrTable.Scan(chrFolder).First(r => r.ChrCode == chrCode);
        string name = txr.TextOf(record.NameCode);
        int spriteCode = record.SpriteCode;

        string obsName = CharacterExport.ObsFileName(spriteCode);
        if (!PakArchive.EnsureFile(obsFolder, "Obs", obsName))
            throw new FileNotFoundException($"{obsName} 을(를) Obs00~03.pak 에서 못 찾았습니다.");

        return (name, spriteCode, Path.Combine(obsFolder, obsName));
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

        // 창을 먼저 보여 주고, 게임 자료 읽기는 뒤따로 돌린다 — Chr 578개·TXR
        // 이름표를 훑는 동안 창이 안 뜬 채 멈춰 있지 않게.
        System.Threading.Tasks.Task.Run(LoadCharacter);

        _running = true;
        var clock = Stopwatch.StartNew();
        double last = 0;

        while (_running)
        {
            while (Win32.PeekMessageW(out var msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
            {
                if (msg.Message == Win32.WM_QUIT) { _running = false; break; }
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessageW(ref msg);
            }
            if (!_running) break;

            _ = clock.Elapsed.TotalSeconds - last;
            last = clock.Elapsed.TotalSeconds;

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
        _hwnd = Win32.CreateWindowExW(0, ClassName, "이동범위 (DirectX 데모)",
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
            case Win32.WM_LBUTTONDOWN:
                OnMouseDown(Win32.LowWord(lParam), Win32.HighWord(lParam));
                return IntPtr.Zero;
            case Win32.WM_KEYDOWN:
                OnKeyDown((int)wParam);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void OnMouseDown(int px, int py)
    {
        int bx = px / Zoom, by = py / Zoom;
        int col = bx / TileSize, row = (by - GridTop) / TileSize;

        // 캐릭터가 선 칸을 누르면 이동범위를 켜고 끈다 — "캐릭터 차례가 오면(고르면)
        // 이동범위가 깔린다"는 원래 게임 동작의 스위치 자리다.
        if (col == _charCol && row == _charRow) { _showRange = !_showRange; return; }
        if (_showRange && col >= 0 && col < Cols && row >= 0 && row < Rows && InRange(col, row))
        {
            _charCol = col;
            _charRow = row;
            _showRange = false;
        }
    }

    private void OnKeyDown(int vk)
    {
        if (vk == Win32.VK_ESCAPE) { _running = false; return; }
        if (vk is Win32.VK_RETURN or Win32.VK_SPACE) { _showRange = !_showRange; return; }

        int dc = 0, dr = 0;
        switch (vk)
        {
            case Win32.VK_LEFT: dc = -1; break;
            case Win32.VK_RIGHT: dc = 1; break;
            case Win32.VK_UP: dr = -1; break;
            case Win32.VK_DOWN: dr = 1; break;
            default: return;
        }

        // 방향키로 커서를 옮기다가, 이동범위가 켜져 있고 그 칸이 범위 안이면 그리로 선다.
        int nc = Math.Clamp(_charCol + dc, 0, Cols - 1);
        int nr = Math.Clamp(_charRow + dr, 0, Rows - 1);
        if (_showRange && InRange(nc, nr)) { _charCol = nc; _charRow = nr; _showRange = false; }
    }

    /// <summary>
    /// 이 칸이 이동 가능 범위 안인가 — <b>임시로 맨해튼 거리</b>만 본다.
    /// 지형 비용·장애물은 아직 반영 안 됨(모션분석.md 참고).
    /// </summary>
    private bool InRange(int col, int row)
    {
        int dist = Math.Abs(col - _charCol) + Math.Abs(row - _charRow);
        return dist > 0 && dist <= MovePoints;
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
        DrawStatus();
        DrawGrid();
        DrawCharacter();
    }

    private void DrawStatus()
    {
        if (_loading)
        {
            DrawText("게임 자료를 읽는 중...", 4, 4, Yellow);
            return;
        }

        string rangeState = _showRange ? "표시 중" : "숨김";
        DrawText($"{_charName} (sprite {_charSpriteCode})   이동력 {MovePoints}칸   이동범위: {rangeState}",
                 4, 4, Yellow);
        if (_loadError.Length > 0) DrawText($"그림을 못 읽었습니다: {_loadError}", 4, 18, 0xFFD05050);
        else DrawText("스페이스/캐릭터 칸 클릭 = 이동범위 토글, 방향키/범위 안 클릭 = 이동", 4, 18, DimGray);
    }

    private void DrawGrid()
    {
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                int x = c * TileSize, y = GridTop + r * TileSize;
                uint tile = (c + r) % 2 == 0 ? TileA : TileB;
                FillRect(x, y, TileSize, TileSize, tile);
                StrokeRect(x, y, TileSize, TileSize, TileEdge);

                if (_showRange && InRange(c, r))
                    FillRect(x + 2, y + 2, TileSize - 4, TileSize - 4, RangeTint);
            }
        }

        int cx = _charCol * TileSize, cy = GridTop + _charRow * TileSize;
        FillRect(cx + 2, cy + 2, TileSize - 4, TileSize - 4, CharTileTint);
    }

    private void DrawCharacter()
    {
        if (_charFrame.Length == 0) return;

        int bottomY = GridTop + (_charRow + 1) * TileSize;
        int x = _charCol * TileSize + TileSize / 2 - _charFrameW / 2;
        int y = bottomY - _charFrameH;
        BlitMasked(_charFrame, _charFrameW, _charFrameH, x, y);
    }

    // ── 글자 ─────────────────────────────────────────────────────────────────

    private (uint[] Px, int W, int H) GetText(string text, uint argb)
    {
        string key = text + ":" + argb;
        if (_textCache.TryGetValue(key, out var cached)) return cached;

        var color = System.Drawing.Color.FromArgb((int)argb);
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
        var vsBlob = Compiler.Compile(shader, "VS", "moverange.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(shader, "PS", "moverange.hlsl", "ps_4_0");
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

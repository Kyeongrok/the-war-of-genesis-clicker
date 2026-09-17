using System.Diagnostics;
using DuelDx.Engine;
using DuelDx.Native;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DuelDx;

/// <summary>
/// 「일기토」 화면을 <b>완전히 독립된 창</b>에 DirectX(D3D11 + DXGI)로 그린다.
/// </summary>
/// <remarks>
/// 이 프로젝트는 cds-helper 를 전혀 참조하지 않는다 — 셈(<see cref="Duel"/>)도, 그림도
/// 죄다 이 저장소 안에서 새로 지었다. 사람은 진짜 스프라이트 대신 <b>도형(캡슐 모양
/// 몸통 + 머리)</b>으로, 글자는 <see cref="TextRaster"/> 가 GDI+ 로 구운 비트맵으로
/// 대신한다.
///
/// 화면(384x?? 대신 480x320)을 매 틱 CPU 에서 BGRA 한 장으로 합성해 동적 텍스처에
/// 얹고, 화면 가득 사각형 한 장을 D3D11 로 찍는다(점 샘플링, 2배 확대) — 창도 순수
/// Win32 HWND 라 WPF 든 무엇이든 다른 UI 프레임워크와 안 엮인다.
/// </remarks>
internal sealed unsafe class DuelWindow : IDisposable
{
    private const int BoardWidth = 480, BoardHeight = 320, Zoom = 2;
    private const int ArenaHeight = 200;

    private const int GroundY = ArenaHeight - 10;
    private const int BodyW = 46, BodyH = 100, HeadR = 14;
    private const double RivalIdleX = 100, PlayerIdleX = BoardWidth - 100 - BodyW;
    private const double LungeDistance = 26;
    private const int TurnAnimTicks = 18, EndAnimTicks = 24;
    private const double TickSeconds = 0.067;

    private const int BarW = 150, BarH = 14;
    private const int RivalBarX = 16, PlayerBarX = BoardWidth - 16 - BarW;
    private static readonly int[] BarY = [ArenaHeight + 14, ArenaHeight + 36, ArenaHeight + 58];

    private const uint BgColor = 0xFF241010;
    private const uint ArenaColor = 0xFF3A1E1E;
    private const uint GroundColor = 0xFF2A1616;
    private const uint PanelColor = 0xFF201010;
    private const uint PanelEdge = 0xFF6A5A46;
    private const uint RivalColor = 0xFF4C78A8;
    private const uint PlayerColor = 0xFFB84C3C;
    private const uint FlashColor = 0xFFF0F0F0;
    private const uint BarBackColor = 0xFF201010;
    private const uint BarLostColor = 0xFF702018;
    private const uint MenuFill = 0xFF33231A;
    private const uint MenuFillOn = 0xFF6A4A2E;
    private const uint MenuEdge = 0xFFB8A278;
    private const uint White = 0xFFF2EAD6;
    private const uint Yellow = 0xFFE8C864;

    private Duel _duel = null!;
    private readonly Duel.Fighter _mineTemplate;
    private readonly Duel.Fighter _theirsTemplate;
    private readonly Random _rng;

    private readonly double[] _dispMine = [Duel.Full, Duel.Full, Duel.Full];
    private readonly double[] _dispTheirs = [Duel.Full, Duel.Full, Duel.Full];

    private bool _animating;
    private bool _ending;
    private int _animTick;
    private bool _attackerIsMine;
    private bool _clash;
    private bool _foeHit, _meHit;
    private int _flashMine, _flashTheirs;

    private string[] _choices = [];
    private int _focus, _hover = -1;
    private bool _useBlow;
    private (int X, int Y, int W, int H)[] _menuRects = [];

    private readonly uint[] _fb = new uint[BoardWidth * BoardHeight];
    private readonly Dictionary<string, (uint[] Px, int W, int H)> _textCache = [];

    private double _accum;
    private bool _running;

    private IntPtr _hwnd;
    private static readonly Win32.WndProc StaticWndProcDelegate = StaticWndProcTrampoline;
    private static DuelWindow? _active;
    private static ushort _classAtom;
    private const string ClassName = "DuelDxDemo";

    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _ctx = null!;
    private IDXGISwapChain1 _swapChain = null!;
    private ID3D11RenderTargetView _backBufferRtv = null!;
    private ID3D11Texture2D _boardTex = null!;
    private ID3D11ShaderResourceView _boardSrv = null!;
    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;

    public DuelWindow(Duel.Fighter mine, Duel.Fighter theirs, Random rng)
    {
        _mineTemplate = mine;
        _theirsTemplate = theirs;
        _rng = rng;
        NewDuel();
    }

    private void NewDuel()
    {
        var mine = new Duel.Fighter(_mineTemplate.Name, Duel.Full, _mineTemplate.Might, _mineTemplate.Sword,
                                    _mineTemplate.Luck, _mineTemplate.Weapon, _mineTemplate.Armour);
        var theirs = new Duel.Fighter(_theirsTemplate.Name, Duel.Full, _theirsTemplate.Might, _theirsTemplate.Sword,
                                      _theirsTemplate.Luck, _theirsTemplate.Weapon, _theirsTemplate.Armour);
        _duel = new Duel(mine, theirs, _rng);
        Array.Fill(_dispMine, Duel.Full);
        Array.Fill(_dispTheirs, Duel.Full);
        _animating = false;
        _ending = false;
        _useBlow = false;
        Rebuild();
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

            double now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(now - last, 0.25);
            last = now;

            Tick(dt);
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

        // WS_OVERLAPPEDWINDOW 는 창틀을 포함한 크기다 — 클라이언트가 정확히
        // pixelW x pixelH 가 되도록 미리 바깥 크기를 셈해 둔다.
        var rect = new Win32.Rect { Left = 0, Top = 0, Right = pixelW, Bottom = pixelH };
        Win32.AdjustWindowRect(ref rect, Win32.WS_OVERLAPPEDWINDOW, false);

        _active = this;
        _hwnd = Win32.CreateWindowExW(0, ClassName, "일기토 (DirectX 데모)",
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
            case Win32.WM_MOUSEMOVE:
                OnMouseMove(Win32.LowWord(lParam), Win32.HighWord(lParam));
                return IntPtr.Zero;
            case Win32.WM_LBUTTONDOWN:
                OnMouseDown(Win32.LowWord(lParam), Win32.HighWord(lParam));
                return IntPtr.Zero;
            case Win32.WM_KEYDOWN:
                OnKeyDown((int)wParam);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void OnMouseMove(int px, int py)
    {
        int bx = px / Zoom, by = py / Zoom;
        int hit = HitMenu(bx, by);
        if (hit != _hover) { _hover = hit; if (hit >= 0) _focus = hit; }
    }

    private void OnMouseDown(int px, int py)
    {
        if (_duel.Over != null) { NewDuel(); return; }

        int bx = px / Zoom, by = py / Zoom;
        int hit = HitMenu(bx, by);
        if (hit == _menuRects.Length - 1) { ToggleBlow(); return; }   // 필살 단추
        if (hit >= 0) Pick(hit);
    }

    private int HitMenu(int bx, int by)
    {
        if (_animating) return -1;
        for (int i = 0; i < _menuRects.Length; i++)
        {
            var (x, y, w, h) = _menuRects[i];
            if (bx >= x && bx < x + w && by >= y && by < y + h) return i;
        }
        return -1;
    }

    private void OnKeyDown(int vk)
    {
        if (vk == Win32.VK_ESCAPE) { _running = false; return; }
        if (_duel.Over != null) { if (vk is Win32.VK_RETURN or Win32.VK_SPACE) NewDuel(); return; }
        if (_animating || _choices.Length == 0) return;

        switch (vk)
        {
            case Win32.VK_LEFT or Win32.VK_UP:
                _focus = (_focus - 1 + _choices.Length) % _choices.Length;
                break;
            case Win32.VK_RIGHT or Win32.VK_DOWN:
                _focus = (_focus + 1) % _choices.Length;
                break;
            case Win32.VK_RETURN or Win32.VK_SPACE:
                Pick(_focus);
                break;
        }
    }

    // ── 판 진행 ──────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        _choices = _duel.Now == Duel.Step.Guard ? Duel.GuardNames : Duel.ZoneNames;
        _focus = 0;
        _hover = -1;
        LayoutMenu();
    }

    private void ToggleBlow()
    {
        if (!_duel.CanBlow) return;
        _useBlow = !_useBlow;
    }

    private void Pick(int pick)
    {
        if (_duel.Over != null || _animating) return;

        var was = _duel.Now;
        var beforeMine = (int[])_duel.Mine.Health.Clone();
        var beforeTheirs = (int[])_duel.Theirs.Health.Clone();

        _duel.Play(pick, _useBlow);
        _useBlow = false;

        _foeHit = !beforeTheirs.SequenceEqual(_duel.Theirs.Health);
        _meHit = !beforeMine.SequenceEqual(_duel.Mine.Health);
        _clash = was == Duel.Step.First;
        _attackerIsMine = was != Duel.Step.Guard;

        _animating = true;
        _animTick = 0;
        _flashMine = _flashTheirs = 0;
    }

    private void LayoutMenu()
    {
        const int rowH = 26, rowW = 90, gap = 6, pad = 8;
        int n = _choices.Length + 1;   // 자리 셋 + 필살

        // 판(panel) 위의 부위 막대와 안 겹치게, 마당(arena) 오른쪽 위 빈자리에 띄운다.
        int boxRight = BoardWidth - 16, boxTop = 16;
        int boxW = rowW + pad * 2;

        var rects = new (int, int, int, int)[n];
        for (int i = 0; i < n; i++)
            rects[i] = (boxRight - boxW + pad, boxTop + pad + i * (rowH + gap), rowW, rowH);
        _menuRects = rects;
    }

    // ── 시간 ─────────────────────────────────────────────────────────────────

    private void Tick(double dt)
    {
        _accum += dt;
        while (_accum >= TickSeconds)
        {
            _accum -= TickSeconds;
            MasterTick();
        }
    }

    private void MasterTick()
    {
        Ease(_dispMine, _duel.Mine.Health);
        Ease(_dispTheirs, _duel.Theirs.Health);
        if (_flashMine > 0) _flashMine--;
        if (_flashTheirs > 0) _flashTheirs--;

        if (!_animating) return;

        _animTick++;
        int impact = (_ending ? EndAnimTicks : TurnAnimTicks) / 2;
        if (_animTick == impact)
        {
            if (_foeHit) _flashTheirs = 5;
            if (_meHit) _flashMine = 5;
        }

        int total = _ending ? EndAnimTicks : TurnAnimTicks;
        if (_animTick < total) return;

        _animating = false;
        if (_duel.Over != null && !_ending) { _ending = true; }
        else if (!_ending) Rebuild();
    }

    private static void Ease(double[] disp, int[] actual)
    {
        for (int i = 0; i < disp.Length; i++)
        {
            double d = disp[i] - actual[i];
            if (Math.Abs(d) < 0.5) { disp[i] = actual[i]; continue; }
            disp[i] -= d / 6.0;
        }
    }

    private static double Phase(int tick, int total)
    {
        double t = Math.Clamp(tick / (double)total, 0, 1);
        double s = t < 0.35 ? t / 0.35 : t < 0.65 ? 1.0 : Math.Max(0, (1 - t) / 0.35);
        return s;
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
        FillRect(0, 0, BoardWidth, ArenaHeight, ArenaColor);
        FillRect(0, GroundY, BoardWidth, ArenaHeight - GroundY, GroundColor);
        FillRect(0, ArenaHeight, BoardWidth, BoardHeight - ArenaHeight, PanelColor);
        StrokeRect(0, ArenaHeight, BoardWidth, 1, PanelEdge);

        DrawFighters();
        DrawBars();
        DrawStatus();
        if (!_animating) DrawMenu();
        if (_duel.Over != null) DrawEndBanner();
    }

    private void DrawFighters()
    {
        double lunge = 0;
        if (_animating) lunge = LungeDistance * Phase(_animTick, _ending ? EndAnimTicks : TurnAnimTicks);

        double rivalX = RivalIdleX, playerX = PlayerIdleX;
        if (_animating && !_ending)
        {
            if (_clash) { rivalX += lunge; playerX -= lunge; }
            else if (_attackerIsMine) playerX -= lunge;
            else rivalX += lunge;
        }

        int jitterTheirs = _flashTheirs > 0 && !_ending ? (_rngShake.Next(-3, 4)) : 0;
        int jitterMine = _flashMine > 0 && !_ending ? (_rngShake.Next(-3, 4)) : 0;

        bool rivalDown = _ending && _duel.Over == true;
        bool playerDown = _ending && _duel.Over == false;

        DrawFighter((int)rivalX + jitterTheirs, RivalColor, down: rivalDown, victory: _ending && _duel.Over == false,
                   flashed: _flashTheirs > 0);
        DrawFighter((int)playerX + jitterMine, PlayerColor, down: playerDown, victory: _ending && _duel.Over == true,
                   flashed: _flashMine > 0);
    }

    private readonly Random _rngShake = new();

    private void DrawFighter(int x, uint color, bool down, bool victory, bool flashed)
    {
        uint body = flashed ? FlashColor : color;
        int bodyH = down ? BodyH / 2 : BodyH;
        int top = GroundY - bodyH;
        int headTop = down ? top - HeadR : top - HeadR * 2;

        FillRoundedRect(x, top, BodyW, bodyH, 10, body, 0xFF10080A);
        FillCircle(x + BodyW / 2, headTop + HeadR, HeadR, body, 0xFF10080A);

        if (victory)
        {
            // 이긴 쪽은 팔을 치켜든다 — 머리 위로 짧은 선 둘.
            FillRect(x - 4, headTop - 10, 4, 14, body);
            FillRect(x + BodyW, headTop - 10, 4, 14, body);
        }
    }

    private void DrawBars()
    {
        // 상대 쪽 이름표·눈금은 막대 오른쪽에, 내 쪽은 막대 왼쪽에 적는다 — 내 쪽을
        // 오른쪽에 적으면 판 오른쪽 위의 명령 창과 겹친다.
        DrawBarSet(RivalBarX, _dispTheirs, _duel.Theirs.Health, labelLeft: false);
        DrawBarSet(PlayerBarX, _dispMine, _duel.Mine.Health, labelLeft: true);

        DrawText(_duel.Theirs.Name, RivalBarX, ArenaHeight, White);
        DrawTextRightAligned(_duel.Mine.Name, PlayerBarX + BarW, ArenaHeight, White);
    }

    private void DrawBarSet(int x, double[] displayed, int[] actual, bool labelLeft)
    {
        for (int i = 0; i < Duel.Zones; i++)
        {
            int y = BarY[i];
            FillRect(x, y, BarW, BarH, BarBackColor);
            int have = (int)Math.Round(displayed[i] * BarW / Duel.Full);
            have = Math.Clamp(have, 0, BarW);
            uint fill = displayed[i] > 60 ? 0xFF4C9C4C : displayed[i] > 30 ? 0xFFC8A028 : 0xFFB03028;
            FillRect(x, y, have, BarH, fill);
            if (have < BarW) FillRect(x + have, y, BarW - have, BarH, BarLostColor);
            StrokeRect(x, y, BarW, BarH, PanelEdge);

            string label = $"{Duel.ZoneNames[i]} {Math.Max(0, actual[i])}";
            if (labelLeft) DrawTextRightAligned(label, x - 6, y - 3, White);
            else DrawText(label, x + BarW + 6, y - 3, White);
        }
    }

    private void DrawStatus()
    {
        string phase = _duel.Now switch
        {
            Duel.Step.First => "자리를 골라 선제를 가린다",
            Duel.Step.Strike => "내가 친다" + (_useBlow ? "  [필살]" : ""),
            _ => "막는다",
        };
        DrawText($"{_duel.Moves + 1}수   {phase}", 12, 6, Yellow);
        if (_duel.Line.Length > 0) DrawText(_duel.Line, 12, 24, White);
    }

    private void DrawMenu()
    {
        if (_menuRects.Length == 0) return;
        var (bx, by, bw, bh) = _menuRects[0];
        const int pad = 8;
        int boxLeft = bx - pad, boxTop = by - pad;
        int boxRight = _menuRects[0].X + bw + pad;
        int boxBottom = _menuRects[^1].Y + _menuRects[^1].H + pad;
        FillRect(boxLeft, boxTop, boxRight - boxLeft, boxBottom - boxTop, MenuFill);
        StrokeRect(boxLeft, boxTop, boxRight - boxLeft, boxBottom - boxTop, MenuEdge);

        for (int i = 0; i < _choices.Length; i++)
        {
            var (x, y, w, h) = _menuRects[i];
            bool on = i == _focus || i == _hover;
            FillRect(x, y, w, h, on ? MenuFillOn : MenuFill);
            StrokeRect(x, y, w, h, MenuEdge);
            DrawTextCentered(_choices[i], x, y, w, h, White);
        }

        var blow = _menuRects[^1];
        bool blowOn = _menuRects.Length - 1 == _hover;
        bool canBlow = _duel.CanBlow;
        string blowLabel = _useBlow ? "필살 *" : canBlow ? "필살" : "필살(불가)";
        FillRect(blow.X, blow.Y, blow.W, blow.H, _useBlow ? Yellow : (blowOn ? MenuFillOn : MenuFill));
        StrokeRect(blow.X, blow.Y, blow.W, blow.H, MenuEdge);
        DrawTextCentered(blowLabel, blow.X, blow.Y, blow.W, blow.H, _useBlow ? 0xFF201010 : White);
    }

    private void DrawEndBanner()
    {
        string text = _duel.Over == true ? "이겼다!" : "졌다.";
        var (px, w, h) = GetText(text, _duel.Over == true ? Yellow : 0xFFD05050);
        int x = (BoardWidth - w) / 2, y = ArenaHeight / 2 - h / 2;
        FillRect(x - 10, y - 6, w + 20, h + 12, 0xC0000000);
        BlitMasked(px, w, h, x, y);
        DrawTextCentered("클릭하거나 Enter 를 누르면 다시 합니다", 0, ArenaHeight + 90, BoardWidth, 20, White);
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

    private void DrawTextCentered(string text, int x, int y, int w, int h, uint argb)
    {
        if (text.Length == 0) return;
        var (px, tw, th) = GetText(text, argb);
        if (tw == 0) return;
        BlitMasked(px, tw, th, x + Math.Max(0, (w - tw) / 2), y + Math.Max(0, (h - th) / 2));
    }

    /// <summary>글이 <paramref name="rightEdge"/> 에서 왼쪽으로 자라도록 찍는다.</summary>
    private void DrawTextRightAligned(string text, int rightEdge, int y, uint argb)
    {
        if (text.Length == 0) return;
        var (px, w, h) = GetText(text, argb);
        if (w == 0) return;
        BlitMasked(px, w, h, rightEdge - w, y);
    }

    // ── 그림 원소 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 한 점을 찍는다. <c>_fb</c> 는 늘 완전 불투명하다고 보고, 색의 알파만큼
    /// 섞는다(끝맺음 배너의 어두운 막이나 글자의 안티에일리어싱 가장자리가 이것을 쓴다).
    /// </summary>
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

    private void FillRoundedRect(int x, int y, int w, int h, int radius, uint fill, uint edge)
    {
        for (int yy = 0; yy < h; yy++)
            for (int xx = 0; xx < w; xx++)
            {
                if (!InsideRounded(xx, yy, w, h, radius)) continue;
                bool border = !InsideRounded(xx, yy, w, h, radius, 1) || xx == 0 || yy == 0 || xx == w - 1 || yy == h - 1;
                SetPixel(x + xx, y + yy, border ? edge : fill);
            }
    }

    private void FillCircle(int cx, int cy, int r, uint fill, uint edge)
    {
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                int dd = x * x + y * y;
                if (dd > r * r) continue;
                bool border = dd > (r - 1) * (r - 1);
                SetPixel(cx + x, cy + y, border ? edge : fill);
            }
    }

    private static bool InsideRounded(int x, int y, int w, int h, int r, int shrink = 0)
    {
        int rr = Math.Max(0, r - shrink);
        int x0 = shrink, y0 = shrink, x1 = w - 1 - shrink, y1 = h - 1 - shrink;
        if (x < x0 || y < y0 || x > x1 || y > y1) return false;
        int cx = x < x0 + rr ? x0 + rr : x > x1 - rr ? x1 - rr : x;
        int cy = y < y0 + rr ? y0 + rr : y > y1 - rr ? y1 - rr : y;
        if (x == cx || y == cy) return true;
        int dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= rr * rr;
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
        var vsBlob = Compiler.Compile(shader, "VS", "duel.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(shader, "PS", "duel.hlsl", "ps_4_0");
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

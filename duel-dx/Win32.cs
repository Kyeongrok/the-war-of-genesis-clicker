using System.Runtime.InteropServices;

namespace DuelDx.Native;

/// <summary>순수 네이티브(Win32) 창 하나를 만드는 데 필요한 최소한의 P/Invoke 묶음.</summary>
internal static unsafe class Win32
{
    public const int CS_HREDRAW = 0x0002, CS_VREDRAW = 0x0001;
    public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const int WS_VISIBLE = 0x10000000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);

    public const uint WM_DESTROY = 0x0002;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_ACTIVATEAPP = 0x001C;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const int MK_LBUTTON = 0x0001;
    public const uint WM_SETCURSOR = 0x0020;
    public const int HTCLIENT = 1;

    // 글자 입력 — 캐릭터 에디터의 이름 찾기가 쓴다. 한글은 IME 가 조합하는 동안 WM_IME_COMPOSITION(GCS_COMPSTR)으로, 다 된 글자는 WM_CHAR 로 온다.
    public const uint WM_CHAR = 0x0102;
    public const uint WM_IME_STARTCOMPOSITION = 0x010D, WM_IME_ENDCOMPOSITION = 0x010E, WM_IME_COMPOSITION = 0x010F;
    public const int GCS_COMPSTR = 0x0008;
    public const int VK_BACK = 0x08;

    [DllImport("imm32.dll")]
    public static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr context);

    /// <summary>조합 중인 글자를 바이트 수로 돌려준다 — <paramref name="buffer"/> 가 null 이면 길이만.</summary>
    [DllImport("imm32.dll", CharSet = CharSet.Unicode)]
    public static extern int ImmGetCompositionStringW(IntPtr context, int index, char[]? buffer, int bufferBytes);

    /// <summary>창의 조합 중 글자(한글 한 자 따위) — 없으면 빈 문자열.</summary>
    public static string CompositionString(IntPtr hWnd)
    {
        IntPtr imc = ImmGetContext(hWnd);
        if (imc == IntPtr.Zero) return "";
        try
        {
            int bytes = ImmGetCompositionStringW(imc, GCS_COMPSTR, null, 0);
            if (bytes <= 0) return "";
            var buffer = new char[bytes / 2];
            ImmGetCompositionStringW(imc, GCS_COMPSTR, buffer, bytes);
            return new string(buffer);
        }
        finally { ImmReleaseContext(hWnd, imc); }
    }

    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr cursor);

    // 끌어서 화면 밀기(BattleDragScroll) — 누른 채 창 밖으로 나가도 WM_LBUTTONUP 을 받는다.
    [DllImport("user32.dll", EntryPoint = "SetCapture")]
    private static extern IntPtr SetCaptureNative(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "ReleaseCapture")]
    private static extern bool ReleaseCaptureNative();

    /// <summary>
    /// 마우스 잡기 — 없어도 끌기는 된다(창 밖에서 떼면 놓칠 뿐). 다른 기기용 바꿔 끼움(Unix 의 user32 대역)에는 이 입구가 없어
    /// 누르는 순간 DllNotFoundException 으로 꺼졌다(사용자 제보, v1.0.7 · Unix) — 한 번 실패하면 다시 부르지 않는다.
    /// </summary>
    private static bool _noCapture;

    public static void SetCapture(IntPtr hWnd)
    {
        if (_noCapture) return;
        try { SetCaptureNative(hWnd); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { _noCapture = true; }
    }

    public static void ReleaseCapture()
    {
        if (_noCapture) return;
        try { ReleaseCaptureNative(); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { _noCapture = true; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr Mask;
        public IntPtr Color;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr CreateIconIndirect(ref IconInfo info);

    [DllImport("user32.dll")]
    public static extern bool DestroyCursor(IntPtr cursor);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, void* bits);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref Point point);

    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_INITMENUPOPUP = 0x0117;
    public const uint MF_GRAYED = 0x0001, MF_BYPOSITION = 0x0400;

    [DllImport("user32.dll")]
    public static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll")]
    public static extern bool DeleteMenu(IntPtr menu, uint position, uint flags);
    public const uint WM_MOUSEWHEEL = 0x020A;
    /// <summary>테스트용 — 받으면 지금 화면을 %TEMP%\dueldx_snapshot.png 로 저장한다(창이 가려져 있어도 된다).</summary>
    public const uint WM_APP_SNAPSHOT = 0x8001;
    public const uint MF_STRING = 0x0000, MF_POPUP = 0x0010, MF_SEPARATOR = 0x0800;
    public const uint MF_BYCOMMAND = 0x0000, MF_CHECKED = 0x0008, MF_UNCHECKED = 0x0000;

    [DllImport("user32.dll")]
    public static extern IntPtr CreateMenu();

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(IntPtr menu, uint flags, nuint idOrSubMenu, string? text);

    [DllImport("user32.dll")]
    public static extern uint CheckMenuItem(IntPtr menu, uint id, uint check);

    public const uint SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

    // 전체화면(테두리 없는 창) — 창 꼴을 바꾸고 모니터 크기를 잰다.
    public const int GWL_STYLE = -16, WS_POPUP = unchecked((int)0x80000000);
    public const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, SM_CYMENU = 15;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern bool SetMenu(IntPtr hWnd, IntPtr menu);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetMenu(IntPtr hWnd);

    public const uint PM_REMOVE = 0x0001;

    public const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    public const int VK_LBUTTON = 0x01, VK_RETURN = 0x0D, VK_SPACE = 0x20, VK_ESCAPE = 0x1B, VK_F11 = 0x7A;
    public const int VK_TAB = 0x09;

    public const int IDC_ARROW = 32512;
    public const uint SPI_GETWORKAREA = 0x0030;

    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfoW(uint action, uint param, ref Rect rect, uint winIni);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public WndProc WindowProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSm;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("kernel32.dll")]
    public static extern uint WaitForSingleObjectEx(IntPtr handle, uint milliseconds, [MarshalAs(UnmanagedType.Bool)] bool alertable);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr handle);

    /// <summary>모니터 DPI 인식 — 이걸 안 하면 고배율 화면에서 창이 통째로 다시 늘려져
    /// 우리가 이미 2배로 그린 그림이 또 늘어나 뭉갠다(<c>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2</c>).</summary>
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WndClassEx wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(int exStyle, string className, string? windowName,
        int style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    [DllImport("user32.dll")]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool AdjustWindowRect(ref Rect rect, int style, [MarshalAs(UnmanagedType.Bool)] bool menu);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursorW(IntPtr instance, IntPtr cursorId);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadIconW(IntPtr instance, IntPtr iconId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessageW(out Msg msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    public static extern bool TranslateMessage(ref Msg msg);

    [DllImport("user32.dll")]
    public static extern IntPtr DispatchMessageW(ref Msg msg);

    [DllImport("user32.dll")]
    public static extern void PostQuitMessage(int exitCode);

    /// <summary>lParam 의 아래 16비트 — 마우스 메시지의 클라이언트 x.</summary>
    public static int LowWord(IntPtr lParam) => unchecked((short)(long)lParam);

    /// <summary>lParam 의 위 16비트 — 마우스 메시지의 클라이언트 y.</summary>
    public static int HighWord(IntPtr lParam) => unchecked((short)((long)lParam >> 16));
}

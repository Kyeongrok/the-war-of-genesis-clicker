using System.Runtime.InteropServices;
using DuelDx.Native;

namespace DuelDx;

/// <summary>
/// 게임 패드(XInput — 엑스박스 패드와 그 호환 패드) 입력. 틀마다 상태를 읽어 <b>키보드 키를 누른 것처럼</b> 넘긴다(사용자 요청 fg-26, 원본에 없다).
/// </summary>
/// <remarks>
/// 키보드로 되는 것만 된다 — 전투의 걷기·확인·취소·링 커맨드·단축 동작, 대사 넘기기, 창 닫기. 마우스로만 되는 화면(모세스 메뉴·상점 따위)은 아직 아니다.
/// 배치: 십자키·왼쪽 스틱 = 방향(화살표 키), A = Enter, B = Esc, X = 링 커맨드, Y = 상태, LB = 휴식, RB = 다음 인물,
/// LT = 어빌리티, RT = 공격, Back = 아이템, Start = 시스템 메뉴. 방향 말고는 키 설정(<see cref="KeyBindings"/>)에 묶인 그 동작의 키를 보낸다.
/// 창이 앞에 있을 때만 받는다. 패드가 없으면 1초에 한 번만 다시 찾는다(없는 패드를 틀마다 묻는 것은 느리다).
/// </remarks>
internal sealed class GamePad(GameWindow host)
{
    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LX, LY, RX, RY;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState14(uint index, out State state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState910(uint index, out State state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private const int StickDeadZone = 16000, TriggerOn = 100;

    // XInput 단추 비트 + 방아쇠 둘을 위에 얹은 것
    private const int Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008, Start = 0x0010, Back = 0x0020,
                      LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000, LT = 0x10000, RT = 0x20000;

    private int _held;                 // 지난 틀에 눌려 있던 것
    private int _pad = -1;             // 쓰는 패드 번호(없으면 −1)
    private double _lookAgainAt;
    private bool _noLibrary;

    private bool Read(int index, out State state)
    {
        state = default;
        if (_noLibrary) return false;
        try { return GetState14((uint)index, out state) == 0; }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            try { return GetState910((uint)index, out state) == 0; }
            catch (Exception inner) when (inner is DllNotFoundException or EntryPointNotFoundException) { _noLibrary = true; return false; }
        }
    }

    /// <summary>그 단추가 보내는 가상 키 — 방향은 화살표, 나머지는 그 동작에 묶인 키.</summary>
    private int KeyOf(int button) => button switch
    {
        Up => Win32.VK_UP, Down => Win32.VK_DOWN, Left => Win32.VK_LEFT, Right => Win32.VK_RIGHT,
        A => Win32.VK_RETURN, B => Win32.VK_ESCAPE,
        X => host._keys[KeyAction.Ring], Y => host._keys[KeyAction.Status],
        LB => host._keys[KeyAction.Rest], RB => host._keys[KeyAction.NextUnit],
        LT => host._keys[KeyAction.Ability], RT => host._keys[KeyAction.Attack],
        Back => host._keys[KeyAction.Item], Start => host._keys[KeyAction.System],
        _ => 0,
    };

    private static readonly int[] Buttons = [Up, Down, Left, Right, A, B, X, Y, LB, RB, LT, RT, Back, Start];

    /// <summary>틀마다 한 번 — 새로 눌린 단추는 키 누름으로, 뗀 방향은 걷기 멈춤으로 넘긴다.</summary>
    internal void Poll()
    {
        if (GameWindow.Offscreen || _noLibrary) return;
        int now = 0;
        if (GetForegroundWindow() == host._hwnd)
        {
            State state = default;
            if (_pad < 0 && host._realTime >= _lookAgainAt)
            {
                _lookAgainAt = host._realTime + 1;
                for (int i = 0; i < 4 && _pad < 0; i++) if (Read(i, out state)) _pad = i;
            }
            else if (_pad >= 0 && !Read(_pad, out state)) _pad = -1;
            if (_pad >= 0)
            {
                now = state.Buttons & (Up | Down | Left | Right | Start | Back | LB | RB | A | B | X | Y);
                if (state.LY > StickDeadZone) now |= Up;
                if (state.LY < -StickDeadZone) now |= Down;
                if (state.LX < -StickDeadZone) now |= Left;
                if (state.LX > StickDeadZone) now |= Right;
                if (state.LeftTrigger > TriggerOn) now |= LT;
                if (state.RightTrigger > TriggerOn) now |= RT;
            }
        }
        int pressed = now & ~_held, released = _held & ~now;
        _held = now;
        foreach (int button in Buttons)
        {
            if ((released & button) != 0 && button is Up or Down or Left or Right) host.Btl._heldMoveKeys.Remove(KeyOf(button));
            if ((pressed & button) != 0 && KeyOf(button) is > 0 and var key) host.OnKeyDown(key);
        }
    }
}

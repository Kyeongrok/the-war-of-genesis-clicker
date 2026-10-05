using System.Runtime.InteropServices;
using System.Text.Json;
using DuelDx.Native;

namespace DuelDx;

/// <summary>패드 단추에 묶을 수 있는 동작 — 방향(십자키·왼쪽 스틱)은 늘 걷기라 여기 없다.</summary>
internal enum PadAction { Confirm, Cancel, Ring, Status, Rest, NextUnit, Ability, Attack, Item, System }

/// <summary>
/// 게임 패드(XInput — 엑스박스 패드와 그 호환 패드) 입력. 틀마다 상태를 읽어 <b>키보드 키를 누른 것처럼</b> 넘긴다(사용자 요청 fg-26, 원본에 없다).
/// </summary>
/// <remarks>
/// 키보드로 되는 것만 된다 — 전투의 걷기·확인·취소·링 커맨드·단축 동작, 대사 넘기기, 창 닫기. 마우스로만 되는 화면(모세스 메뉴·상점 따위)은 아직 아니다.
/// 십자키·왼쪽 스틱 = 방향(화살표 키, 고정). 나머지 단추는 동작(<see cref="PadAction"/>)에 묶고 설정 &gt; 패드에서 바꾼다 —
/// 묶음은 <c>%APPDATA%\DuelDx\pad.json</c> 에 남는다. 확인 = Enter · 취소 = Esc, 그 밖은 키 설정(<see cref="KeyBindings"/>)에 묶인 그 동작의 키를 보낸다.
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
    private const int Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008, Start = 0x0010, Back = 0x0020, LS = 0x0040, RS = 0x0080,
                      LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000, LT = 0x10000, RT = 0x20000;

    /// <summary>동작에 묶을 수 있는 단추와 그 이름.</summary>
    internal static readonly (int Button, string Name)[] Buttons =
        [(A, "A"), (B, "B"), (X, "X"), (Y, "Y"), (LB, "LB"), (RB, "RB"), (LT, "LT"), (RT, "RT"), (Back, "Back"), (Start, "Start"), (LS, "L 스틱 누름"), (RS, "R 스틱 누름")];

    internal static readonly (PadAction Action, string Label, int DefaultButton)[] All =
    [
        (PadAction.Confirm, "확인", A), (PadAction.Cancel, "취소", B), (PadAction.Ring, "링 커맨드 열기·닫기", X), (PadAction.Status, "상태(Status)", Y),
        (PadAction.Rest, "휴식", LB), (PadAction.NextUnit, "다음 인물 보기", RB), (PadAction.Ability, "어빌리티", LT), (PadAction.Attack, "공격", RT),
        (PadAction.Item, "아이템", Back), (PadAction.System, "시스템 메뉴", Start),
    ];

    internal static string FilePath => UserDataFolder.File("pad.json");

    private readonly Dictionary<PadAction, int> _map = LoadMap();

    /// <summary>설정 창이 「단추를 누르세요」로 기다리는 동작 — 다음에 눌린 단추를 여기에 묶는다.</summary>
    internal PadAction? _capture;

    private int _held;                 // 지난 틀에 눌려 있던 것
    private int _pad = -1;             // 쓰는 패드 번호(없으면 −1)
    private double _lookAgainAt;
    private bool _noLibrary;

    internal bool Connected => _pad >= 0;

    internal static string ButtonName(int button) => Buttons.FirstOrDefault(b => b.Button == button).Name ?? "(없음)";

    internal int ButtonOf(PadAction action) => _map.GetValueOrDefault(action);

    private static Dictionary<PadAction, int> Defaults() => All.ToDictionary(a => a.Action, a => a.DefaultButton);

    private static Dictionary<PadAction, int> LoadMap()
    {
        var map = Defaults();
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) is { } saved)
                foreach (var (name, buttonName) in saved)
                    if (Enum.TryParse<PadAction>(name, out var action) && Buttons.FirstOrDefault(b => b.Name == buttonName) is { Button: > 0 } found)
                        map[action] = found.Button;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* 못 읽으면 기본 배치 */ }
        return map;
    }

    private void SaveMap()
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder.Path);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_map.ToDictionary(p => p.Key.ToString(), p => ButtonName(p.Value)), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 못 적어도 이번에는 바뀐 대로 돈다 */ }
    }

    /// <summary>그 동작에 단추를 묶는다 — 그 단추를 쓰던 다른 동작은 이 동작이 쓰던 단추를 받는다(맞바꿈).</summary>
    internal void Assign(PadAction action, int button)
    {
        int old = _map[action];
        foreach (var other in _map.Keys.ToList())
            if (other != action && _map[other] == button) _map[other] = old;
        _map[action] = button;
        SaveMap();
    }

    internal void ResetDefaults()
    {
        foreach (var (action, button) in Defaults()) _map[action] = button;
        SaveMap();
    }

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

    /// <summary>그 동작이 보내는 가상 키 — 확인·취소는 Enter·Esc, 나머지는 키 설정에 묶인 그 동작의 키.</summary>
    private int KeyOf(PadAction action) => action switch
    {
        PadAction.Confirm => Win32.VK_RETURN,
        PadAction.Cancel => Win32.VK_ESCAPE,
        PadAction.Ring => host._keys[KeyAction.Ring],
        PadAction.Status => host._keys[KeyAction.Status],
        PadAction.Rest => host._keys[KeyAction.Rest],
        PadAction.NextUnit => host._keys[KeyAction.NextUnit],
        PadAction.Ability => host._keys[KeyAction.Ability],
        PadAction.Attack => host._keys[KeyAction.Attack],
        PadAction.Item => host._keys[KeyAction.Item],
        _ => host._keys[KeyAction.System],
    };

    private static readonly (int Button, int Key)[] Directions = [(Up, Win32.VK_UP), (Down, Win32.VK_DOWN), (Left, Win32.VK_LEFT), (Right, Win32.VK_RIGHT)];

    /// <summary>틀마다 한 번 — 새로 눌린 단추는 키 누름으로, 뗀 방향은 걷기 멈춤으로 넘긴다. 설정 창이 기다리는 중이면 눌린 단추를 그 동작에 묶는다.</summary>
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
                now = state.Buttons & (Up | Down | Left | Right | Start | Back | LS | RS | LB | RB | A | B | X | Y);
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

        if (_capture is { } waiting)
        {
            // 묶을 단추를 기다리는 중 — 방향 말고 처음 눌린 단추를 받는다. 이 틀의 입력은 게임에 안 넘긴다.
            if (Buttons.FirstOrDefault(b => (pressed & b.Button) != 0) is { Button: > 0 } picked)
            {
                Assign(waiting, picked.Button);
                _capture = null;
            }
            return;
        }

        foreach (var (button, key) in Directions)
        {
            if ((released & button) != 0) host.Btl._heldMoveKeys.Remove(key);
            if ((pressed & button) != 0) host.OnKeyDown(key);
        }
        foreach (var (action, _, _) in All)
            if ((pressed & _map[action]) != 0) host.OnKeyDown(KeyOf(action));
    }
}

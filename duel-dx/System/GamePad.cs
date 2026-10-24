using System.Runtime.InteropServices;
using System.Text.Json;
using DuelDx.Native;

namespace DuelDx;

/// <summary>패드 단추에 묶을 수 있는 동작 — 방향(십자키·왼쪽 스틱)은 늘 걷기라 여기 없다.</summary>
internal enum PadAction { Confirm, Cancel, Ring, Status, Rest, NextUnit, Ability, Attack, Item, System, Help, PrevUnit }

/// <summary>
/// 게임 패드(XInput — 엑스박스 패드와 그 호환 패드) 입력. 틀마다 상태를 읽어 <b>키보드 키를 누른 것처럼</b> 넘긴다(사용자 요청 fg-26, 원본에 없다).
/// </summary>
/// <remarks>
/// 키보드로 되는 것만 된다 — 전투의 걷기·확인·취소·링 커맨드·단축 동작, 대사 넘기기, 창 닫기. 마우스로만 되는 화면(모세스 메뉴·상점 따위)은 아직 아니다.
/// 십자키·왼쪽 스틱 = 방향(화살표 키, 고정). 나머지 단추는 동작(<see cref="PadAction"/>)에 묶고 설정 &gt; 패드에서 바꾼다 —
/// 묶음은 <c>%APPDATA%\DuelDx\pad.json</c> 에 남는다. 확인 = Enter · 취소 = Esc, 그 밖은 키 설정(<see cref="KeyBindings"/>)에 묶인 그 동작의 키를 보낸다.
/// <b>오른쪽 스틱은 마우스 커서</b>를 움직인다. 마우스로만 되는 창(Status · 모세스 · 시스템 메뉴 · 설정, <see cref="PointerMode"/>)에서는 십자키 · 왼쪽 스틱이
/// 누를 수 있는 자리 사이를 건너뛰며 커서를 옮기고(<see cref="Snap"/>), 확인 = 왼쪽 클릭 · 링 = 오른쪽 클릭(설명) · 휴식/다음 인물 = 휠 · 어빌리티 = Shift 가 된다
/// (사용자 요청 st-9 · st-10 — 모세스는 단추가 몇 개 안 되니 커서를 끌지 않고 단추 사이를 건너뛴다).
/// 패드를 잡고 있으면 화면 아래에 지금 누를 수 있는 것을 보이고, 조작 안내 단추를 누르면 안내 창을 띄운다(<see cref="DrawGuide"/>, fa-28 · fa-44).
/// 창이 앞에 있을 때만 받는다. 패드가 없으면 1초에 한 번만 다시 찾는다(없는 패드를 틀마다 묻는 것은 느리다).
/// </remarks>
internal sealed partial class GamePad(GameWindow host)
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

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int StickDeadZone = 16000, TriggerOn = 100;

    // XInput 단추 비트 + 방아쇠 둘을 위에 얹은 것
    private const int Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008, Start = 0x0010, Back = 0x0020, LS = 0x0040, RS = 0x0080,
                      LB = 0x0100, RB = 0x0200, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000, LT = 0x10000, RT = 0x20000;

    /// <summary>동작에 묶을 수 있는 단추와 그 이름.</summary>
    internal static readonly (int Button, string Name)[] Buttons =
        [(A, "A"), (B, "B"), (X, "X"), (Y, "Y"), (LB, "LB"), (RB, "RB"), (LT, "LT"), (RT, "RT"), (Back, "Back"), (Start, "Start"), (LS, "L 스틱 누름"), (RS, "R 스틱 누름")];

    internal static readonly (PadAction Action, string Label, int DefaultButton)[] All =
    [
        (PadAction.Confirm, "확인", A), (PadAction.Cancel, "취소", B), (PadAction.Ring, "링 커맨드 열기·닫기", X), (PadAction.Status, "상태(Status)", Back),
        (PadAction.PrevUnit, "이전 인물 보기", LB), (PadAction.NextUnit, "다음 인물 보기", RB), (PadAction.Ability, "어빌리티", LT), (PadAction.Attack, "공격", RT),
        (PadAction.Rest, "휴식", Y), (PadAction.Item, "아이템", RS), (PadAction.System, "시스템 메뉴", Start), (PadAction.Help, "조작 안내", LS),
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

    /// <summary>마지막 입력이 패드였는지 — 키보드를 누르면 꺼진다.</summary>
    internal bool Active;

    /// <summary>조작 안내 창이 떠 있는지 — 아무 단추나 누르면 닫힌다.</summary>
    internal bool _guideOpen;

    /// <summary>마우스로만 되는 창에서 어빌리티 단추를 누르고 있는지 — Shift 를 누른 것으로 친다(레벨 내리기 · 상점 10개씩).</summary>
    /// <summary>마우스 커서를 숨기나 — 패드 단추 · 스틱을 쓰면 켜지고, 마우스를 손으로 움직이면 꺼진다(GameWindow 의 틀 고리가 끈다).</summary>
    internal bool HideMouse;

    internal static bool ShiftHeld;

    /// <summary>
    /// 창 조작 중에 휴식 단추(기본 Y)를 누르고 있나 — 모세스의 올림 설명(행성 · 도크 아이콘)은 패드에서는 이 동안만 보인다(사용자 요청 mo-22).
    /// 패드 커서는 늘 어느 자리 위에 서 있어서, 마우스처럼 올리기만 해도 띄우면 설명이 화면을 내내 가렸다.
    /// </summary>
    internal static bool TipHeld;

    /// <summary>마우스로만 되는 창이 떠 있는지 — 이때는 왼쪽 스틱도 커서를 움직이고 단추가 마우스 단추 노릇을 한다.</summary>
    internal bool PointerMode => host._statusUnit >= 0 || host.Mos._mosesOpen || host.Sys.SystemOpen || host.SettingsScr._open
                                 || host.TitleScr._titleOpen || host.Fld._fieldChoices is { Count: > 0 } || host.Btl._itemMenu
                                 || host.TuningScr._tuningOpen || host.TimedEditScr._open || host.EpisodesScr._episodesOpen;

    private const double CursorSpeed = 900;   // 스틱을 끝까지 밀었을 때 커서가 1초에 가는 화면 점
    private double _lastPoll, _cursorX, _cursorY;
    private bool _rightDown;

    /// <summary>방아쇠를 누른 채 다른 것을 했는지 — 그러면 떼도 어빌리티 · 공격이 안 나간다.</summary>
    private bool _chordUsed;

    /// <summary>방아쇠와 함께 누르면 어빌리티 줄이 되는 네 단추 — 차례대로 1~4(LT) · Q~R(RT).</summary>
    private static readonly int[] ChordButtons = [A, B, X, Y];

    /// <summary>어빌리티 목록 줄 i 의 패드 단축 — 「LT+A」 꼴. 여덟 줄 너머는 빈 글.</summary>
    internal string AbilityChordLabel(int i) =>
        i < 0 || i >= 8 ? "" : $"{ButtonName(_map[i < 4 ? PadAction.Ability : PadAction.Attack])}+{ButtonName(ChordButtons[i % 4])}";

    internal static string ButtonName(int button) => Buttons.FirstOrDefault(b => b.Button == button).Name ?? "(없음)";

    /// <summary>화면에 보이는 단추 이름 — Back 은 「Select」로 더 알려져 있어 둘 다 적는다(적어 두는 이름 <see cref="ButtonName"/> 은 그대로).</summary>
    internal static string ButtonLabel(int button) => button == Back ? "Select(Back)" : ButtonName(button);

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
        // 옛 판에서 적어 둔 묶음(휴식 = LB)이 새 동작(이전 인물 = LB)과 겹치면 뒤에 오는 동작을 빈 단추로 보낸다.
        foreach (var (action, _, _) in All)
        {
            bool taken = All.TakeWhile(a => a.Action != action).Any(a => map[a.Action] == map[action]);
            if (taken && Buttons.Select(b => b.Button).FirstOrDefault(b => !map.ContainsValue(b)) is var free and > 0) map[action] = free;
        }
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
        PadAction.NextUnit or PadAction.PrevUnit => host._keys[KeyAction.NextUnit],
        PadAction.Ability => host._keys[KeyAction.Ability],
        PadAction.Attack => host._keys[KeyAction.Attack],
        PadAction.Item => host._keys[KeyAction.Item],
        _ => host._keys[KeyAction.System],
    };

    /// <summary>
    /// 그 동작의 단추가 <b>지금</b> 눌려 있나 — 패드를 바로 읽는다. 스팀 데스크톱 구성 같은 단추 → 키 바꾸개가 켜져 있으면 A 가 패드 단추로도,
    /// Enter 키로도 한꺼번에 들어온다. 그 겹친 키를 걸러 내는 데 쓴다(사용자 보고 fa-62: 배치 명단에서 A 가 배치종료까지 눌렀다).
    /// </summary>
    internal bool HeldNow(PadAction action)
    {
        if (GameWindow.Offscreen || _noLibrary || _pad < 0 || !Read(_pad, out var state)) return false;
        int now = state.Buttons;
        if (state.LeftTrigger > TriggerOn) now |= LT;
        if (state.RightTrigger > TriggerOn) now |= RT;
        return (now & _map[action]) != 0;
    }

    private static readonly (int Button, int Key)[] Directions =[(Up, Win32.VK_UP), (Down, Win32.VK_DOWN), (Left, Win32.VK_LEFT), (Right, Win32.VK_RIGHT)];

    /// <summary>틀마다 한 번 — 새로 눌린 단추는 키 누름으로, 뗀 방향은 걷기 멈춤으로 넘긴다. 설정 창이 기다리는 중이면 눌린 단추를 그 동작에 묶는다.</summary>
    /// <summary>DUELDX_PAD=1 — 패드가 꽂혀 있는 것처럼 군다(화면 밖 시험용 — 노란 테 · 안내 줄). DUELDX_PADGUIDE=1 이면 안내 창을 띄운 채.</summary>
    private static readonly bool Fake = Environment.GetEnvironmentVariable("DUELDX_PAD") == "1";
    private bool _fakeDone;

    internal void Poll()
    {
        if (GameWindow.Offscreen || _noLibrary)
        {
            if (!Fake) return;
            Active = true;
            _pad = 0;
            if (!_fakeDone) { _fakeDone = true; _guideOpen = Environment.GetEnvironmentVariable("DUELDX_PADGUIDE") == "1"; }
            if (PointerMode && !_guideOpen) AutoFocus();
            return;
        }
        int now = 0, rx = 0, ry = 0, lx = 0, ly = 0;
        bool pointer = PointerMode;
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
                (rx, ry, lx, ly) = (state.RX, state.RY, state.LX, state.LY);
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
        int lt = _map[PadAction.Ability], rt = _map[PadAction.Attack];

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

        // 뗀 방향은 안내 창이 떠 있어도 걷기에서 뺀다 — 안 그러면 누른 채로 남는다.
        foreach (var (button, key) in Directions)
            if ((released & button) != 0) host.Btl._heldMoveKeys.Remove(key);
        if (_rightDown && released != 0 && (now & (_map[PadAction.Ring] | _map[PadAction.Attack])) == 0) { _rightDown = false; PostMouse(Win32.WM_RBUTTONUP); }
        ShiftHeld = pointer && (now & _map[PadAction.Ability]) != 0;
        TipHeld = pointer && (now & _map[PadAction.Rest]) != 0;

        double dt = Math.Clamp(host._realTime - _lastPoll, 0, 0.1);
        _lastPoll = host._realTime;
        bool cursorMoved = !pointer && host._battleLoaded && (now & _map[PadAction.Ability]) != 0 ? ScrollMap(rx, ry, dt) : MoveCursor(rx, ry, dt);
        if (Active && !pointer) PointAtChest();   // 상자 옆에 서면 커서를 상자로(fa-35)
        if (pressed != 0 || cursorMoved) Active = true;
        // 오른쪽 스틱으로 커서를 직접 끄는 동안은 커서가 보여야 한다 — 단추 · 십자키를 누르면 다시 숨는다.
        if (cursorMoved) HideMouse = false;
        else if (pressed != 0) HideMouse = true;
        if (Active && pointer && !_guideOpen) AutoFocus();   // 창 · 페이지가 바뀌면 첫 항목에 커서를 올린다
        if (!pointer) RingByStick(lx, ly, now);
        // 타이밍 크리티컬이 단추를 기다리는 중 — 어빌리티 · 공격 단추(기본 LT · RT) 어느 쪽이든 그리로 가고 다른 일은 안 한다.
        // 그 누름을 떼도 어빌리티 목록 · 공격은 안 나간다.
        if (host.TimedHitFx.Running)
        {
            if ((pressed & lt) != 0) host.TimedHitFx.Press(0);   // 박자마다 어빌리티 단추 · 공격 단추가 번갈아 온다
            if ((pressed & rt) != 0) host.TimedHitFx.Press(1);
            if ((pressed & (lt | rt)) != 0) _chordUsed = true;
            else if ((released & (lt | rt)) != 0 && (now & (lt | rt)) == 0) _chordUsed = false;
            return;
        }

        // 전투에서 방아쇠는 <b>뗄 때</b> 어빌리티 · 공격이다 — 누른 채 A·B·X·Y 를 누르면 그 줄의 어빌리티(1~4 · Q~R)를 바로 고르고(fa-37),
        // 누른 채 오른쪽 스틱을 밀면 맵 스크롤이라(fa-33) 그때는 떼도 아무 일도 없다. 떼는 틀은 새로 눌린 것이 없으므로 아래의 이른 반환보다 먼저 본다.
        if (!pointer)
        {
            if (cursorMoved && (now & lt) != 0) _chordUsed = true;
            foreach (int trig in new[] { lt, rt })
                if ((released & trig) != 0)
                {
                    // 목록이 열린 채 LT 를 떼면 목록을 닫는다(토글) — 키로 보내면 어빌리티 키(기본 R)가 8번째 줄 단축키로 먹혀 엉뚱한 줄이 골라졌다(fa-45).
                    if (!_chordUsed && !_guideOpen && trig == lt && host.Btl._abilityMenu) { host.Btl._abilityMenu = false; host.Btl.CancelTargeting(refund: true); }
                    else if (!_chordUsed && !_guideOpen) host.OnKeyDown(KeyOf(trig == lt ? PadAction.Ability : PadAction.Attack));
                    _chordUsed = false;
                }
        }

        // 조작 안내 단추를 누르면 안내 창을 띄운다. 떠 있으면 아무 단추나 닫는다 — 그 누름은 게임에 안 넘긴다.
        // 처음 잡았을 때 저절로 띄우던 것은 뺐다(fa-44) — 타이틀에서 A 를 누르면 그게 먼저 떠 NEW GAME 누름을 삼켰다.
        if (pressed == 0) { if (pointer && !_guideOpen) SnapByPad(0, now); return; }
        if (_guideOpen) { _guideOpen = false; return; }
        if ((pressed & _map[PadAction.Help]) != 0)
        {
            _guideOpen = true;
            host.Btl._heldMoveKeys.Clear();
            return;
        }

        // 캐릭터 배치 창(fa-42 · fa-43) — 단추를 바로 묶는다. 십자키는 명단 줄(칸 고르기 중엔 초록 칸), A 고르기 · 세우기, B 돌아가기,
        // X 배치취소, Y 자동배치, LB 군단사용, Start 배치종료.
        if (host.Btl._deployOpen && !pointer)
        {
            var btl = host.Btl;
            // 파트1 「용병 배치」 칸 창 — 위 · 아래 용병 목록, 왼 · 오른 칸, A 넣기, X 비우기, B 닫기, Start 배치종료.
            if (host.MercDeploy.Open)
            {
                var merc = host.MercDeploy;
                if ((pressed & Up) != 0) merc.ListStep(-1);
                else if ((pressed & Down) != 0) merc.ListStep(1);
                else if ((pressed & Left) != 0) merc.SlotStep(-1);
                else if ((pressed & Right) != 0) merc.SlotStep(1);
                if ((pressed & _map[PadAction.Confirm]) != 0) merc.AssignFocused();
                if ((pressed & _map[PadAction.Ring]) != 0) merc.Clear();
                if ((pressed & _map[PadAction.Cancel]) != 0 || (pressed & _map[PadAction.PrevUnit]) != 0) merc.Leader = null;
                if ((pressed & _map[PadAction.System]) != 0) btl.FinishDeploy();
                return;
            }
            if (btl._deployCellMode) SnapByPad(pressed, now);
            else if ((pressed & Up) != 0) btl.DeployFocusMove(-1);
            else if ((pressed & Down) != 0) btl.DeployFocusMove(1);
            if ((pressed & _map[PadAction.Confirm]) != 0)
            {
                if (btl._deployCellMode) PostMouse(Win32.WM_LBUTTONDOWN);
                else if (btl.DeployPadPick() is { } cell) MoveCursorTo(cell.Col * GameWindow.TileW + GameWindow.TileW / 2, host.CellTop(cell.Col, cell.Row) + GameWindow.TileH / 2);
            }
            if ((pressed & _map[PadAction.Cancel]) != 0) btl.DeployPadCancel();
            if ((pressed & _map[PadAction.Ring]) != 0) btl.DeployPadUnplace();
            if ((pressed & _map[PadAction.Rest]) != 0) btl.AutoDeploy();
            if ((pressed & _map[PadAction.PrevUnit]) != 0) btl.DeployPadLegion();
            if ((pressed & _map[PadAction.System]) != 0) btl.FinishDeploy();
            return;
        }

        // 마우스로만 되는 창에서는 방향이 커서를 단추 사이로 건너뛰게 한다 — 어빌리티 단추를 누른 채면 키로 넘긴다(Status 에서 ←·→ 로 다른 인물).
        if (pointer && !(ShiftHeld && host._statusUnit >= 0) && (_guideOpen || SnapByPad(pressed, now))) { }
        else
            foreach (var (button, key) in Directions)
                if ((pressed & button) != 0) host.OnKeyDown(key);
        // 방아쇠를 누른 채 A·B·X·Y — 그 줄의 어빌리티(fa-37). 방아쇠 자체의 누름은 전투에서 아무 일도 안 한다(뗄 때 위에서).
        if (!pointer)
        {
            int chordAt = (now & lt) != 0 ? 0 : (now & rt) != 0 ? 4 : -1;
            int abxy = pressed & (A | B | X | Y);
            if (chordAt >= 0 && abxy != 0)
            {
                int slot = Array.IndexOf(ChordButtons, abxy & -abxy);
                if (slot >= 0) host.OnKeyDown(BattleScene.AbilityHotkeys[chordAt + slot]);
                _chordUsed = true;
                pressed &= ~(A | B | X | Y);
            }
            pressed &= ~(lt | rt);
        }
        foreach (var (action, _, _) in All)
        {
            if ((pressed & _map[action]) == 0 || action == PadAction.Help) continue;
            // 필드에서는 메뉴 단추가 안 먹는다(사용자 요청 menu-39) — 원본 필드에는 시스템 메뉴가 없고, 대사를 넘기다 잘못 눌러 메뉴가 뜨곤 했다.
            if (action == PadAction.System && host.FieldOpen && !host.Sys.SystemOpen) continue;
            // 모세스에서는 메뉴 단추가 어느 페이지에서든 시스템 메뉴를 바로 열고 닫는다(사용자 보고 menu-51) — Esc 로 넘기면 페이지(메일 · 파티 · 상점)에서는
            // 「뒤로」가 되어 메뉴가 안 열렸다. 다른 창(Status · 설정 · 모드)이 떠 있으면 전처럼 그 창의 Esc 다.
            if (action == PadAction.System && host.Mos._mosesOpen && host._statusUnit < 0 && !host.SettingsScr._open && !host.TuningScr._tuningOpen
                && !host.TimedEditScr._open && !host.EpisodesScr._episodesOpen)
            {
                if (host.Sys.SystemOpen) host.Sys.CloseSystemWindow();
                else if (!host.Sys.ChapterEventRunning) { host.Play(578); host.Sys.OpenSystemMenu(); }
                continue;
            }
            if (action is PadAction.PrevUnit or PadAction.NextUnit && !pointer && !host.Btl._abilityMenu && host.Btl._targetWork < 0 && host._battleLoaded)
            {
                // 차례를 기다리는(▼) 다른 인물에게 차례를 넘긴다 — 넘기면 그 인물이 부르는 목소리가 난다(fa-39 · fa-46). ▼ 없는 인물은 안 고른다.
                host.Btl.CycleReadyUnit(action == PadAction.PrevUnit ? -1 : 1);
                continue;
            }
            if (pointer)
            {
                // 마우스로만 되는 창 — 단추가 마우스 단추 노릇을 한다.
                if (action == PadAction.Confirm) { PostMouse(Win32.WM_LBUTTONDOWN); continue; }
                if (action == PadAction.Ring) { _rightDown = true; PostMouse(Win32.WM_RBUTTONDOWN); continue; }
                if (action is PadAction.PrevUnit or PadAction.NextUnit)
                {
                    // 모드 창에서는 LB · RB 가 이전 · 다음 탭(사용자 요청 menu-35) — 끝에서 반대쪽으로 돈다.
                    if (host.TuningScr._tuningOpen && !host.TimedEditScr._open)
                    {
                        int tabs = TuningScreen.TabNames.Length;
                        host.TuningScr._tuningTab = (host.TuningScr._tuningTab + (action == PadAction.PrevUnit ? tabs - 1 : 1)) % tabs;
                        host.TuningScr._tuningListOpen = false;
                        // 커서도 새 탭의 단추 위로 — 자리 목록의 앞쪽이 탭들이다.
                        if (Targets() is var tabTargets && host.TuningScr._tuningTab < tabTargets.Count)
                        {
                            var tab = tabTargets[host.TuningScr._tuningTab];
                            MoveCursorTo(tab.X + tab.W / 2, tab.Y + tab.H / 2);
                        }
                        continue;
                    }
                    // 연대표에서는 LB · RB 가 목록을 한 줄씩 올리고 내린다(마우스 휠이 없는 화면이다).
                    if (host.EpisodesScr._episodesOpen && !host.Sys.SystemOpen && !host.SettingsScr._open)
                    {
                        host.EpisodesScr.ScrollEpisodes(action == PadAction.PrevUnit ? -1 : 1);
                        continue;
                    }
                    // 성도(행성 고르기)에서는 LB · RB 가 이전 · 다음 행성(사용자 요청 mo-14). 타이틀에서는 단추 옮기기(fa-31).
                    if (CycleStarMap(action == PadAction.PrevUnit ? -1 : 1)) continue;
                    // 시스템 메뉴(와 그 확인창)에서도 LB · RB 가 고르는 칸을 위 · 아래로 옮긴다(사용자 요청 menu-31) — 슬롯 목록은 전처럼 굴린다.
                    if (host.TitleScr._titleOpen || ((host.Sys._systemMenu || host.Sys._confirm != null) && !host.SlotsScr.SlotsOpen))
                    {
                        CycleTargets(action == PadAction.PrevUnit ? -1 : 1);
                        continue;
                    }
                    PostMouse(Win32.WM_MOUSEWHEEL, action == PadAction.PrevUnit ? 120 : -120);
                    continue;
                }
                // 성도에서는 LT · RT 가 이전 · 다음 항성계 — 바로 가지 않고 물어본다(사용자 요청 mo-13 · mo-14).
                if (action is PadAction.Ability or PadAction.Attack && host.Mos.AskTurnSystem(action == PadAction.Ability ? -1 : 1)) continue;
                if (action == PadAction.Ability) continue;
                if (action == PadAction.Rest) continue;   // 누르고 있는 동안 올림 설명(TipHeld) — 키로는 안 넘긴다
                // 공격 단추(기본 RT)도 누르고 있는 동안 설명 보기 — 커맨드 단추(X)와 같다(사용자 요청 st-11).
                if (action == PadAction.Attack) { _rightDown = true; PostMouse(Win32.WM_RBUTTONDOWN); continue; }
            }
            // 겨누는 중에는 LB · RB 가 이전 · 다음 적(사용자 요청 fa-32).
            if (host.Btl._targetWork >= 0 && host.Btl.HasAttackCursor && action is PadAction.PrevUnit or PadAction.NextUnit) { host.Btl.CycleAttackCursor(action == PadAction.PrevUnit ? -1 : 1); continue; }
            // 커서가 손댈 수 있는 물체(상자 · 문) 위면 확인이 그 물체를 누른다(fa-35) — 걸어가서 연다.
            if (action == PadAction.Confirm && TouchableUnderCursor()) { PostMouse(Win32.WM_LBUTTONDOWN); continue; }
            // 어빌리티 목록에서는 LB · RB 도 줄을 위 · 아래로 옮긴다(사용자 요청 fa-30) — 십자키와 같은 일이다.
            if (host.Btl._abilityMenu && action is PadAction.PrevUnit or PadAction.NextUnit) { host.OnKeyDown(action == PadAction.PrevUnit ? Win32.VK_UP : Win32.VK_DOWN); continue; }
            host._padCancel = action == PadAction.Cancel;
            host.OnKeyDown(KeyOf(action));
            host._padCancel = false;
        }
    }

    /// <summary>링 커맨드에서 왼쪽 스틱 · 십자키가 지금 가리키는 항목 — 가리키는 것이 없으면 −1.</summary>
    private int _ringStickItem = -1;
    private bool _ringWasIdle;

    /// <summary>
    /// 링 커맨드가 열려 있으면 왼쪽 스틱(십자키)을 민 방향에 가장 가까운 항목으로 커서를 보낸다(사용자 요청 cmd-3) — 확인 단추가 그 항목을 고른다.
    /// 가리키는 항목이 바뀔 때만 옮겨, 오른쪽 스틱으로 커서를 따로 움직이는 것을 막지 않는다.
    /// </summary>
    private void RingByStick(int lx, int ly, int now)
    {
        var btl = host.Btl;
        if (btl._ringUnit < 0 || btl._ringPhase != BattleScene.RingPhase.Idle || _guideOpen) { _ringStickItem = -1; _ringWasIdle = false; return; }
        if (!_ringWasIdle)
        {
            // 링이 막 다 열렸다 — 미리 짚인 항목(쉴 일밖에 없을 때의 REST, cmd-4)이 있으면 커서를 그리로 보낸다.
            _ringWasIdle = true;
            if (Active && btl._ringHover >= 0)
            {
                var (rcx, rcy) = btl.RingCenter(host._units[btl._ringUnit]);
                var (rox, roy) = btl.RingItemOffset(btl._ringHover);
                MoveCursorTo(rcx + rox, rcy + roy);
            }
        }
        double x = Axis(lx), y = -Axis(ly);   // 스틱은 위가 +, 화면은 아래가 +
        if (x == 0 && y == 0)
        {
            x = ((now & Right) != 0 ? 1 : 0) - ((now & Left) != 0 ? 1 : 0);
            y = ((now & Down) != 0 ? 1 : 0) - ((now & Up) != 0 ? 1 : 0);
        }
        if (x == 0 && y == 0) { _ringStickItem = -1; return; }
        int best = -1;
        double bestDot = double.MinValue;
        for (int i = 0; i < btl.Items.Length; i++)
        {
            var (ox, oy) = btl.RingItemOffset(i);
            double dot = (ox * x + oy * y) / Math.Max(1, Math.Sqrt(ox * ox + oy * oy));
            if (dot > bestDot) (best, bestDot) = (i, dot);
        }
        if (best == _ringStickItem) return;
        _ringStickItem = best;
        var (cx, cy) = btl.RingCenter(host._units[btl._ringUnit]);
        var (tx, ty) = btl.RingItemOffset(best);
        MoveCursorTo(cx + tx, cy + ty);
        btl.OnRingMouseMove(cx + tx, cy + ty);
        Active = true;
    }

    /// <summary>스틱이 기운 만큼 마우스 커서를 옮긴다. 옮겼으면 true.</summary>
    private bool MoveCursor(int x, int y, double dt)
    {
        double ax = Axis(x), ay = Axis(y);
        if (ax == 0 && ay == 0) return false;
        _cursorX += ax * CursorSpeed * dt;
        _cursorY -= ay * CursorSpeed * dt;   // 스틱은 위가 +, 화면은 아래가 +
        int stepX = (int)_cursorX, stepY = (int)_cursorY;
        if (stepX == 0 && stepY == 0) return true;
        _cursorX -= stepX;
        _cursorY -= stepY;
        if (Win32.GetCursorPos(out var at)) SetCursorPos(at.X + stepX, at.Y + stepY);
        return true;
    }

    /// <summary>스틱 기울기(−1~1) — 조금 기울이면 천천히, 끝까지 밀면 빠르게. 죽은 구역 안은 0.</summary>
    private static double Axis(int v)
    {
        double n = (Math.Abs(v) - StickDeadZone) / (32767.0 - StickDeadZone);
        return n <= 0 ? 0 : Math.Sign(v) * n * (0.25 + 0.75 * n);
    }

    /// <summary>커서 자리에 마우스 메시지를 보낸다 — 휠이면 <paramref name="wheel"/> 이 굴린 만큼(위가 +).</summary>
    private void PostMouse(uint message, int wheel = 0)
    {
        if (!Win32.GetCursorPos(out var at)) return;
        Win32.ScreenToClient(host._hwnd, ref at);
        PostMessageW(host._hwnd, message, wheel << 16, ((at.Y & 0xFFFF) << 16) | (at.X & 0xFFFF));
    }

    /// <summary>그 동작에 묶인 단추 이름.</summary>
    internal string Name(PadAction action) => ButtonLabel(_map[action]);

    /// <summary>
    /// 알림 글의 키 이름을 패드 단추 이름으로 바꾼다 — 패드를 잡고 있을 때만(fa-28). 「Enter: 쓰기 … Esc 취소」 → 「A: 쓰기 … B 취소」.
    /// </summary>
    internal string Words(string text)
    {
        if (!Active) return text;
        return text.Replace("우클릭·Esc", Name(PadAction.Cancel)).Replace("Esc", Name(PadAction.Cancel))
                   .Replace("Enter", Name(PadAction.Confirm)).Replace("Tab", Name(PadAction.NextUnit))
                   .Replace("방향키", "십자키·스틱").Replace("클릭", "오른쪽 스틱");
    }

    /// <summary>지금 화면에서 패드로 누르는 것 — 제목과 (단추, 하는 일) 줄.</summary>
    private (string Title, List<(string Button, string Does)> Lines) Guide()
    {
        var btl = host.Btl;
        if (btl._deployOpen)
            return ("캐릭터 배치",
            [
                ("십자키 · 왼쪽 스틱", "명단에서 인물 짚기 / 칸 고르기 중엔 초록 칸 옮기기"), (Name(PadAction.Confirm), "인물 고르기 → 초록 칸에 세우기"),
                (Name(PadAction.Cancel), "칸 고르기 그만두고 명단으로"), (Name(PadAction.Ring), "짚은 인물 배치취소"), (Name(PadAction.Rest), "자동배치"),
                (Name(PadAction.PrevUnit), "짚은 인물 군단사용 켬 · 끔"), (Name(PadAction.System), "배치종료"), ("오른쪽 스틱", "커서 자유롭게"),
            ]);
        if (host.Fld._fieldChoices is { Count: > 0 } || btl._itemMenu || host.TitleScr._titleOpen)
            return (host.TitleScr._titleOpen ? "타이틀" : btl._itemMenu ? "아이템 목록" : "고르기",
            [
                ("십자키 · 왼쪽 스틱", "줄 옮기기 (테두리)"), ($"{Name(PadAction.PrevUnit)} · {Name(PadAction.NextUnit)}", "줄 옮기기"),
                (Name(PadAction.Confirm), "고르기"), (Name(PadAction.Cancel), "취소 · 닫기"),
            ]);
        if (host.EpisodesScr._episodesOpen && !host.Sys.SystemOpen && !host.SettingsScr._open && !host.TuningScr._tuningOpen)
            return ("연대표",
            [
                ("십자키 · 왼쪽 스틱", "에피소드 옮기기"), (Name(PadAction.Confirm), "고르기 (두 번)"),
                ($"{Name(PadAction.PrevUnit)} · {Name(PadAction.NextUnit)}", "목록 굴리기"), (Name(PadAction.Cancel), "메뉴"),
            ]);
        if (PointerMode)
            return ("창 조작 (Status · 모세스 · 메뉴 · 설정)",
            [
                ("십자키 · 왼쪽 스틱", "단추 사이 옮기기 (테두리)"),
                ("오른쪽 스틱", "커서 자유롭게 움직이기"),
                (Name(PadAction.Confirm), "누르기 (장비 바꾸기 · 어빌리티 배우기 · 고르기)"),
                ($"{Name(PadAction.Ability)} 누른 채 {Name(PadAction.Confirm)}", "어빌리티 레벨 내리기 · 상점 10개씩"),
                ($"{Name(PadAction.Attack)} / {Name(PadAction.Ring)}", "누르고 있는 동안 설명 보기"),
                (Name(PadAction.Rest), "누르고 있는 동안 행성 · 아이콘 설명 보기 (모세스)"),
                ($"{Name(PadAction.PrevUnit)} · {Name(PadAction.NextUnit)}", "목록 위 · 아래로 굴리기 (성도에서는 이전 · 다음 행성)"),
                ($"{Name(PadAction.Ability)} · {Name(PadAction.Attack)}", "성도에서 이전 · 다음 항성계 (물어봅니다)"),
                (Name(PadAction.Cancel), "닫기 · 뒤로"),
                (Name(PadAction.Status), "Status 열기 · 닫기"),
                ($"{Name(PadAction.Ability)} 누른 채 십자키 좌우", "모세스 Status 에서 다른 인물"),
            ]);
        if (btl._abilityMenu)
            return ("어빌리티 목록",
            [
                ("십자키 · 스틱 위아래", "줄 옮기기"), ($"{Name(PadAction.PrevUnit)} · {Name(PadAction.NextUnit)}", "줄 위 · 아래로"),
                (Name(PadAction.Confirm), "고르기"), (Name(PadAction.Cancel), "취소"),
            ]);
        if (btl._targetWork >= 0)
            return ("대상 고르기",
            [
                ("십자키 · 왼쪽 스틱", "겨눈 칸 · 적 옮기기"), ("오른쪽 스틱", "커서로 겨누기"), (Name(PadAction.Confirm), "쓰기"),
                ($"{Name(PadAction.PrevUnit)} · {Name(PadAction.NextUnit)}", "이전 · 다음 적"), (Name(PadAction.Cancel), "취소"),
            ]);
        return ("전투",
        [
            ("십자키 · 왼쪽 스틱", "걷기"), ("오른쪽 스틱", "마우스 커서"), ($"{Name(PadAction.Ability)} 누른 채 오른쪽 스틱", "맵 스크롤"),
            (Name(PadAction.Confirm), "확인"), (Name(PadAction.Cancel), "취소 · 어빌리티 목록"),
            (Name(PadAction.Ring), "링 커맨드 (왼쪽 스틱 방향으로 항목 짚기)"), (Name(PadAction.Ability), "어빌리티 목록 (떼면)"), (Name(PadAction.Attack), "공격 (떼면)"),
            ($"{Name(PadAction.Ability)} 누른 채 A · B · X · Y", "어빌리티 1 · 2 · 3 · 4"), ($"{Name(PadAction.Attack)} 누른 채 A · B · X · Y", "어빌리티 5 · 6 · 7 · 8"),
            (Name(PadAction.Rest), "휴식"), (Name(PadAction.Item), "아이템"), (Name(PadAction.Status), "상태(Status)"),
            ($"{Name(PadAction.PrevUnit)} · {Name(PadAction.NextUnit)}", "차례 기다리는(▼) 다른 인물로 넘기기"), (Name(PadAction.System), "시스템 메뉴"),
        ]);
    }

    /// <summary>
    /// 창 조작의 한 줄 — 늘 보이던 넷(3~6번 줄)에서 행성 설명(Y)과 목록 굴리기(LB · RB)를 뺀다. 행성 설명은 성도에 행성이 있을 때만
    /// 「(Push) 설명 보기」로 짧게 붙인다(사용자 요청 mo-26). 풀어 쓴 설명은 안내 창에 그대로 있다.
    /// </summary>
    private List<(string Button, string Does)> PointerBar(List<(string Button, string Does)> lines)
    {
        string rest = Name(PadAction.Rest), scroll = $"{Name(PadAction.PrevUnit)} · {Name(PadAction.NextUnit)}";
        var bar = lines.Skip(3).Take(4).Where(l => l.Button != rest && l.Button != scroll).ToList();
        var mos = host.Mos;
        if (mos._mosesOpen && mos._mosesPage == 0 && mos._mosesStep == 1 && !host.Sys.SystemOpen && host._statusUnit < 0 && mos.MosesSystemPlanets().Count > 0)
            bar.Add((rest, "(Push) 설명 보기"));
        return bar;
    }

    /// <summary>
    /// 패드 조작 안내 — 패드를 잡고 있는 동안 화면 아래에 지금 누를 수 있는 것을 한 줄로 보이고, 안내 창이 열려 있으면 가운데에 줄마다 풀어 보인다.
    /// 단추 이름은 설정 &gt; 패드의 묶음을 따른다.
    /// </summary>
    internal void DrawGuide()
    {
        if (!Active || !Connected) return;
        var (title, lines) = Guide();
        if (!_guideOpen)
        {
            // 한 줄 — 전투의 긴 목록은 자주 쓰는 것만 추린다. 단추는 그림(<see cref="DrawKeys"/>)으로, 하는 일은 글로.
            // 전투 줄은 짧은 말로 따로 적는다(사용자 요청 fa-57 · fa-59) — 확인 · 취소 · 커맨드 · 조작. 풀어 쓴 설명은 안내 창에 있다.
            var shown = title == "전투"
                ? [(Name(PadAction.Confirm), "확인"), (Name(PadAction.Cancel), "취소"), (Name(PadAction.Ring), "커맨드")]
                : PointerMode ? PointerBar(lines)
                : lines.Count > 6 ? lines.Skip(3).Take(4).ToList() : lines;
            // 조작 안내 단추는 짧게 — 「L 스틱 누름」은 「L」, 「R 스틱 누름」은 「R」.
            shown = [.. shown, (Name(PadAction.Help).Replace(" 스틱 누름", ""), "조작")];
            int total = shown.Sum(l => DrawKeys(l.Button, 0, 0, CapSmall, measure: true) + 4 + TextSize(l.Does, 0xFFFFFFFF, 11f).W + 14) - 14;
            int sx = host._camX + host.ViewWidth - total - 10, sy = host._camY + host.ViewHeight - CapSmall - 6;
            // 필드에서는 그림 아래의 검은 띠에 딱 붙여 그림을 안 가리게 한다(사용자 요청 fa-61) — 바탕 네모도 안 깐다(이미 검다).
            if (host.FieldOpen) sy = host._camY + host.ViewHeight - CapSmall - 2;
            else if (OnLayer) host.Hi.FillRect(sx - 6, sy - 3, total + 12, CapSmall + 6, 0xA0000000);
            else host.FillRect(sx - 6, sy - 3, total + 12, CapSmall + 6, 0xA0000000);
            foreach (var (button, does) in shown)
            {
                sx += DrawKeys(button, sx, sy, CapSmall) + 4;
                Text(does, sx, sy + (CapSmall - TextSize(does, 0xFFFFFFFF, 11f).H) / 2, 0xFFD0E8D0, 11f);
                sx += TextSize(does, 0xFFFFFFFF, 11f).W + 14;
            }
            return;
        }

        const int RowH = 26;
        lines.Add((Name(PadAction.Help), "이 안내 다시 보기"));
        int keyW = lines.Max(l => DrawKeys(l.Button, 0, 0, CapLarge, measure: true)) + 16;
        int w = Math.Max(520, keyW + 16 + lines.Max(l => TextSize(l.Does, 0xFFFFFFFF, 12).W) + 32), h = 44 + lines.Count * RowH + 30;
        int x = host._camX + (host.ViewWidth - w) / 2, y = host._camY + (host.ViewHeight - h) / 2;
        FillRound(x, y, w, h, 10, 0xF0101828);
        Text($"패드 조작 — {title}", x + 16, y + 12, FocusColor, 14);
        for (int i = 0; i < lines.Count; i++)
        {
            int ry = y + 44 + i * RowH;
            DrawKeys(lines[i].Button, x + 16, ry, CapLarge);
            Text(lines[i].Does, x + 16 + keyW, ry + (CapLarge - TextSize(lines[i].Does, 0xFFFFFFFF, 12).H) / 2, 0xFFFFFFFF, 12);
        }
        Text("아무 단추나 누르면 닫힙니다 · 단추 배치는 설정 > 패드", x + 16, y + h - 24, 0xFF9098A8, 11);
    }

    /// <summary>
    /// 새 UI 층에 그린 창(Status · 시스템 메뉴 · 설정 …)이 떠 있으면 안내도 그 층에 그린다 — 판에 그리면 창 밑에 깔린다(menu-57).
    /// 층에 그린 것이 없는 틀(전투 · 필드)에서는 전처럼 판에 그려, 안내 한 줄 때문에 층을 올리지 않는다.
    /// </summary>
    private bool OnLayer => host._hiResUi && host.Hi.Used;

    private (int W, int H) TextSize(string text, uint color, float size)
    {
        if (OnLayer) return host.Hi.Measure(text, color, size);
        var (_, w, h) = host.GetText(text, color, size);
        return (w, h);
    }

    private void Text(string text, int x, int y, uint color, float size)
    {
        if (OnLayer) host.Hi.DrawText(text, x, y, color, size); else host.DrawText(text, x, y, color, size);
    }

    // ── 단추 그림 ─────────────────────────────────────────────────────────────

    /// <summary>커서가 선 자리의 테 색 — 안내 창 제목과 단추 테두리도 이 색이다.</summary>
    internal const uint FocusColor = 0xFFD9CDB5;   // 노랑은 어색하다는 사용자 요청으로 글씨색에 가까운 베이지

    /// <summary>단추 그림 높이 — 화면 아래 한 줄용과 안내 창용.</summary>
    private const int CapSmall = 16, CapLarge = 20;

    /// <summary>
    /// 단추 이름의 바탕색 — 엑스박스 패드의 A 초록 · B 빨강 · X 파랑 · Y 노랑. 그 밖(어깨 · 방아쇠 · Select · Start · 스틱 · 십자키)은 쥐색.
    /// </summary>
    private static uint CapColor(string key) => key switch
    {
        "A" => 0xFF4FA83C, "B" => 0xFFD9432E, "X" => 0xFF3A7BD5, "Y" => 0xFFE3B824,
        _ => 0xFF4A5064,
    };

    /// <summary>글씨색 — 노랑 바탕만 어두운 글씨.</summary>
    private static uint CapText(string key) => key == "Y" ? 0xFF201800 : 0xFFFFFFFF;

    /// <summary>
    /// 단추 설명(「LT 누른 채 A」 · 「십자키 · 왼쪽 스틱」 · 「LB · RB」)을 <b>둥근 단추 그림</b>으로 그린다 — 「누른 채」는 +, 「·」는 /. 그린(또는 잴) 너비를 돌려준다.
    /// </summary>
    private int DrawKeys(string spec, int x, int y, int h, bool measure = false)
    {
        int at = x;
        float size = h >= CapLarge ? 12f : 11f;
        var holds = spec.Split(" 누른 채 ");
        for (int g = 0; g < holds.Length; g++)
        {
            if (g > 0) at += Joiner("+");
            var alts = holds[g].Split(" · ");
            for (int a = 0; a < alts.Length; a++)
            {
                if (a > 0) at += Joiner("/");
                at += DrawCap(alts[a].Trim(), at, y, h, size, measure) + 2;
            }
            at -= 2;
        }
        return at - x;

        int Joiner(string mark)
        {
            var (jw, jh) = TextSize(mark, 0xFF9098A8, size);
            if (!measure) Text(mark, at + 3, y + (h - jh) / 2, 0xFF9098A8, size);
            return jw + 6;
        }
    }

    /// <summary>둥근 단추 하나 — 글자 너비에 맞춘 알약 꼴(한 글자면 동그라미에 가깝다). 너비를 돌려준다.</summary>
    private int DrawCap(string key, int x, int y, int h, float size, bool measure)
    {
        var (tw, th) = TextSize(key, CapText(key), size);
        int w = Math.Max(h, tw + 10);
        if (measure) return w;
        uint fill = CapColor(key);
        FillRound(x, y + 1, w, h - 1, (h - 1) / 2, 0x80000000);                 // 그림자 — 한 점 아래
        FillRound(x, y, w, h - 1, (h - 1) / 2, fill);
        FillRound(x + 1, y + 1, w - 2, (h - 1) / 2, (h - 1) / 4, Lighten(fill)); // 윗면 빛
        Text(key, x + (w - tw) / 2, y + (h - 1 - th) / 2, CapText(key), size);
        return w;
    }

    private static uint Lighten(uint c) =>
        0x60000000 | (uint)Math.Min(255, (int)(c >> 16 & 0xFF) + 70) << 16 | (uint)Math.Min(255, (int)(c >> 8 & 0xFF) + 70) << 8 | (uint)Math.Min(255, (int)(c & 0xFF) + 70);

    /// <summary>모서리가 둥근 네모 — 가운데 네모 둘과 모서리 원 넷.</summary>
    private void FillRound(int x, int y, int w, int h, int r, uint color)
    {
        if (OnLayer) { host.Hi.FillRound(x, y, w, h, r, color); return; }
        r = Math.Min(r, Math.Min(w, h) / 2);
        host.FillRect(x + r, y, w - 2 * r, h, color);
        host.FillRect(x, y + r, w, h - 2 * r, color);
        host.FillCircle(x + r, y + r, r, color);
        host.FillCircle(x + w - r - 1, y + r, r, color);
        host.FillCircle(x + r, y + h - r - 1, r, color);
        host.FillCircle(x + w - r - 1, y + h - r - 1, r, color);
    }
}

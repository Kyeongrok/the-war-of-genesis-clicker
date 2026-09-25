using System.IO;
using System.Text.Json;
using DuelDx.Native;

namespace DuelDx;

/// <summary>단축키로 바꿀 수 있는 동작.</summary>
internal enum KeyAction { MoveUp, MoveDown, MoveLeft, MoveRight, Ring, Attack, Ability, Rest, Status, Item, System, NextUnit, Grid, Gauges }

/// <summary>
/// 동작 ↔ 가상 키 표. <c>%APPDATA%\DuelDx\keys.json</c> 에 저장한다. 방향키(걷기)와 Esc(취소)는 늘 고정.
/// </summary>
/// <remarks>원본 링 커맨드 키는 Q 어빌·W 공격·E 휴식·Z 아이템·X 시스템·C 상태(분석-링커맨드)지만, 기본값은 사용자가 정한 E 공격·Q 휴식을 따른다.</remarks>
internal sealed class KeyBindings
{
    public static readonly (KeyAction Action, string Label, int DefaultKey)[] All =
    [
        (KeyAction.MoveUp, "위로 걷기", 'W'),
        (KeyAction.MoveDown, "아래로 걷기", 'S'),
        (KeyAction.MoveLeft, "왼쪽으로 걷기", 'A'),
        (KeyAction.MoveRight, "오른쪽으로 걷기", 'D'),
        (KeyAction.Ring, "링 커맨드 열기·닫기", Win32.VK_SPACE),
        (KeyAction.Attack, "공격", 'E'),
        (KeyAction.Ability, "어빌리티", 'R'),
        (KeyAction.Rest, "휴식", 'Q'),
        (KeyAction.Status, "상태(Status)", 'C'),
        (KeyAction.Item, "아이템", 'Z'),
        (KeyAction.System, "시스템 메뉴", 'X'),
        (KeyAction.NextUnit, "다음 인물 보기", Win32.VK_TAB),
        (KeyAction.Grid, "격자 켜기·끄기", 'G'),
        (KeyAction.Gauges, "체력바 켜기·끄기", 'H'),
    ];

    private readonly Dictionary<KeyAction, int> _keys = [];

    private static string FilePath => UserDataFolder.File("keys.json");

    public KeyBindings() => ResetDefaults();

    public int this[KeyAction action] => _keys[action];

    public void ResetDefaults()
    {
        foreach (var (action, _, key) in All) _keys[action] = key;
    }

    public KeyAction? ActionFor(int vk) => _keys.FirstOrDefault(p => p.Value == vk) is { Value: not 0 } hit ? hit.Key : null;

    /// <summary>동작에 키를 준다. 그 키를 쓰던 동작은 이 동작의 옛 키를 받는다(맞바꿈).</summary>
    public void Assign(KeyAction action, int vk)
    {
        int old = _keys[action];
        if (ActionFor(vk) is { } other && other != action) _keys[other] = old;
        _keys[action] = vk;
    }

    public static KeyBindings Load()
    {
        var keys = new KeyBindings();
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath)) is { } saved)
                foreach (var (name, vk) in saved)
                    if (Enum.TryParse<KeyAction>(name, out var action) && vk > 0) keys._keys[action] = vk;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return keys;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_keys.ToDictionary(p => p.Key.ToString(), p => p.Value)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static string KeyName(int vk) => vk switch
    {
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)vk).ToString(),
        Win32.VK_SPACE => "Space",
        Win32.VK_TAB => "Tab",
        Win32.VK_RETURN => "Enter",
        0x10 => "Shift",
        0x11 => "Ctrl",
        0x08 => "Backspace",
        >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
        >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
        0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".", 0xBF => "/", 0xC0 => "`",
        0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
        _ => $"키 {vk:X2}",
    };
}

/// <summary>상단 메뉴(설정 > 단축키 설정 …)와 단축키 설정 창.</summary>
internal sealed unsafe partial class BattleSceneWindow
{
    private readonly KeyBindings _keys = KeyBindings.Load();
    private bool _keysOpen;
    private int _keysCapture = -1;

    private const int MenuKeys = 1001, MenuGrid = 1002, MenuGauges = 1003, MenuExit = 1004, MenuChapters = 1005;
    private const int MenuAllyAi = 1006, MenuHints = 1007, MenuLevelUpWindow = 1009, MenuKeepJobExp = 1100, MenuStatusBar = 1101;

    /// <summary>「대사 사이 멈춤」 줄 번호 — 1130 + <see cref="TalkPauseChoices"/> 순번.</summary>
    private const int MenuTalkPauseBase = 1130;

    private const int MenuSceneTag = 1103, MenuProgress = 1104, MenuChestContents = 1105, MenuFullSoul = 1106;

    /// <summary>대사 사이 멈춤(초) 고르기 — −1 은 원본대로(스크립트 값, 보통 1초).</summary>
    private static readonly double[] TalkPauseChoices = [0, 0.1, 0.2, 0.3, 0.5, -1];
    /// <summary>도구 > 적 정리 — 시험용: 적을 다 쓰러뜨리고 경험치를 내가 움직이는 동료끼리 나눈다.</summary>
    private const int MenuClearEnemies = 1008;

    /// <summary>「게임 속도」 줄 번호 — 1110 + <see cref="SpeedChoices"/> 순번.</summary>
    private const int MenuSpeedBase = 1110;

    private static readonly int[] SpeedChoices = [100, 150, 200];

    /// <summary>「다녀온 장소 다시 열기」 하위 메뉴 줄 번호 — 1200 + 목록 순번.</summary>
    private const int MenuReplayBase = 1200;

    /// <summary>「다녀온 장소 다시 열기」 하위 메뉴 — 펼칠 때마다 지금 챕터에서 다녀온 장소로 다시 채운다.</summary>
    private static IntPtr _replayMenu;

    /// <summary>하위 메뉴에 지금 올라 있는 장소들(줄 순번 차례).</summary>
    private readonly List<(int Chapter, int Place)> _replayItems = [];
    /// <summary>설정 > 해상도 — 자동, 100·150·200·300·400 %.</summary>
    private const int MenuZoomAuto = 1010;
    private static readonly int[] ZoomChoices = [0, 100, 150, 200, 300, 400];
    /// <summary>설정 > 해상도 — 보이는 영역 크기. 원본 640×480 부터.</summary>
    private const int MenuResBase = 1020;
    private static readonly (int W, int H)[] ResChoices =
        [(640, 480), (800, 600), (1024, 768), (1280, 720), (1280, 960), (1600, 900), (1920, 1080), (2560, 1440), (2560, 1600), (3440, 1440), (3840, 2160)];

    /// <summary>동맹(편 3)을 AI 가 움직이나 — 끄면 내가 직접 움직인다(설정 > 모드).</summary>
    private bool _allyAi = UserSettings.Current.AllyAi;

    /// <summary>창 위 메뉴 막대 — 설정(단축키 설정·격자·체력바·끝내기).</summary>
    private static IntPtr CreateMenuBar()
    {
        IntPtr bar = Win32.CreateMenu(), settings = Win32.CreatePopupMenu(), game = Win32.CreatePopupMenu(), mode = Win32.CreatePopupMenu();
        Win32.AppendMenuW(mode, Win32.MF_STRING | (UserSettings.Current.AllyAi ? Win32.MF_CHECKED : 0u), MenuAllyAi, "동맹을 AI 가 움직임(&A)");
        Win32.AppendMenuW(mode, Win32.MF_STRING | (UserSettings.Current.ShowChestContents ? Win32.MF_CHECKED : 0u), MenuChestContents, "상자 내용물 보기(&C)");
        Win32.AppendMenuW(mode, Win32.MF_STRING | (UserSettings.Current.FullSoulAtStart ? Win32.MF_CHECKED : 0u), MenuFullSoul, "전투 시작 시 소울 가득(&S)");
        Win32.AppendMenuW(game, Win32.MF_STRING, MenuChapters, "챕터 고르기(&C)...");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)game, "게임(&G)");
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuKeys, "단축키 설정(&K)...");
        Win32.AppendMenuW(settings, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuGrid, "격자 켜기·끄기(&G)");
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuGauges, "체력바 켜기·끄기(&H)");
        Win32.AppendMenuW(settings, Win32.MF_STRING | (UserSettings.Current.ShowHints ? Win32.MF_CHECKED : 0u), MenuHints, "조작 안내 글 보이기(&T)");
        Win32.AppendMenuW(settings, Win32.MF_STRING | (UserSettings.Current.ShowLevelUp ? Win32.MF_CHECKED : 0u), MenuLevelUpWindow, "레벨업 창 보이기(&L)");
        Win32.AppendMenuW(settings, Win32.MF_STRING | (UserSettings.Current.KeepExpOnJobChange ? Win32.MF_CHECKED : 0u), MenuKeepJobExp, "전직할 때 EXP 유지(&E)");
        Win32.AppendMenuW(settings, Win32.MF_STRING | (UserSettings.Current.ShowStatusBar ? Win32.MF_CHECKED : 0u), MenuStatusBar, "상단 상태 줄 보이기(&B)");
        IntPtr pause = Win32.CreatePopupMenu();
        for (int i = 0; i < TalkPauseChoices.Length; i++)
            Win32.AppendMenuW(pause, Win32.MF_STRING | (TalkPauseIndex(UserSettings.Current.TalkPauseSeconds) == i ? Win32.MF_CHECKED : 0u), (nuint)(MenuTalkPauseBase + i),
                              TalkPauseLabel(TalkPauseChoices[i]));
        Win32.AppendMenuW(settings, Win32.MF_POPUP, (nuint)pause, "대사 사이 멈춤(&W)");
        Win32.AppendMenuW(settings, Win32.MF_STRING | (UserSettings.Current.ShowSceneTag ? Win32.MF_CHECKED : 0u), MenuSceneTag, "장면 번호 보이기(&N)");
        Win32.AppendMenuW(settings, Win32.MF_SEPARATOR, 0, null);
        // 배율 — 자동이면 판이 창보다 작을 때 창을 채운다. 창 크기는 아래 「해상도」가 정한다.
        IntPtr res = Win32.CreatePopupMenu();
        for (int i = 0; i < ResChoices.Length; i++)
        {
            var (w, h) = ResChoices[i];
            bool chosen = UserSettings.Current.ViewW == w && UserSettings.Current.ViewH == h;
            Win32.AppendMenuW(res, Win32.MF_STRING | (chosen ? Win32.MF_CHECKED : 0u), (nuint)(MenuResBase + i), i == 0 ? $"{w}×{h} (원본)" : $"{w}×{h}");
        }
        Win32.AppendMenuW(settings, Win32.MF_POPUP, (nuint)res, "해상도(&R)");
        IntPtr zoom = Win32.CreatePopupMenu();
        for (int i = 0; i < ZoomChoices.Length; i++)
        {
            bool chosen = UserSettings.Current.ZoomPercent == ZoomChoices[i];
            string label = ZoomChoices[i] == 0 ? "자동(화면에 맞춤)(&A)" : $"{ZoomChoices[i]}%";
            Win32.AppendMenuW(zoom, Win32.MF_STRING | (chosen ? Win32.MF_CHECKED : 0u), (nuint)(MenuZoomAuto + i), label);
        }
        Win32.AppendMenuW(settings, Win32.MF_POPUP, (nuint)zoom, "배율(&Z)");
        IntPtr speed = Win32.CreatePopupMenu();
        for (int i = 0; i < SpeedChoices.Length; i++)
            Win32.AppendMenuW(speed, Win32.MF_STRING | (UserSettings.Current.GameSpeed == SpeedChoices[i] ? Win32.MF_CHECKED : 0u), (nuint)(MenuSpeedBase + i),
                              SpeedChoices[i] == 100 ? "보통(원본)(&1)" : $"{SpeedChoices[i] / 100.0:0.#}배(&{i + 1})");
        Win32.AppendMenuW(settings, Win32.MF_POPUP, (nuint)speed, "게임 속도(&P)");
        Win32.AppendMenuW(settings, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuExit, "끝내기(&X)");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)mode, "모드(&M)");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)settings, "설정(&S)");
        IntPtr tools = Win32.CreatePopupMenu();
        Win32.AppendMenuW(tools, Win32.MF_STRING, MenuClearEnemies, "적 정리(&K)");
        _replayMenu = Win32.CreatePopupMenu();
        Win32.AppendMenuW(tools, Win32.MF_POPUP, (nuint)_replayMenu, "다녀온 장소 다시 열기(&R)");
        Win32.AppendMenuW(tools, Win32.MF_STRING, MenuProgress, "진행 상태 보기(&P)");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)tools, "도구(&T)");
        return bar;
    }

    /// <summary>
    /// 「다녀온 장소 다시 열기」를 펼칠 때 — 지금 챕터에서 다녀온 장소(전투·필드)를 줄로 올린다.
    /// 이야기 사슬이 끊겨(예: 전멸 승리가 전투 137 의 필드 55 행을 건너뛰던 버그) 다시 해야 할 때 쓴다(사용자 요청).
    /// </summary>
    private void RebuildReplayMenu()
    {
        while (Win32.GetMenuItemCount(_replayMenu) > 0) Win32.DeleteMenu(_replayMenu, 0, Win32.MF_BYPOSITION);
        _replayItems.Clear();
        if (_mosesChp is { } chp)
            foreach (var place in chp.Places)
                if (_placesUsed.Contains((chp.Id, place.No)) && _replayItems.Count < 500)
                {
                    string kind = place.Value >= 20000 ? "상점" : place.Value >= 10000 ? $"필드 {place.Value - 10000:D4}" : $"전투 {place.Value:D4}";
                    string name = _db?.T((ushort)place.NameText) is { Length: > 0 } n ? n : $"장소 {place.No}";
                    Win32.AppendMenuW(_replayMenu, Win32.MF_STRING, (nuint)(MenuReplayBase + _replayItems.Count), $"{name} — {kind}");
                    _replayItems.Add((chp.Id, place.No));
                }
        if (_replayItems.Count == 0)
            Win32.AppendMenuW(_replayMenu, Win32.MF_STRING | Win32.MF_GRAYED, 0, _mosesChp == null ? "(챕터 안이 아닙니다)" : "(이 챕터에서 다녀온 장소가 없습니다)");
    }

    /// <summary>다녀온 장소의 표시를 지워 항행에서 다시 고를 수 있게 한다 — 진행 깃발은 건드리지 않는다.</summary>
    private void ReopenPlace(int index)
    {
        if ((uint)index >= _replayItems.Count) return;
        var (chapter, place) = _replayItems[index];
        if (!_placesUsed.Remove((chapter, place))) return;
        _chapterDone = false;
        string name = _mosesChp?.PlaceOf(place) is { } p && _db?.T((ushort)p.NameText) is { Length: > 0 } n ? n : $"장소 {place}";
        Toast($"「{name}」 을(를) 다시 열었습니다 — 항행에서 다시 고를 수 있습니다");
    }

    /// <summary>모드·격자·체력바를 바꾸면 바로 적어 다음에 켤 때도 그대로 두게 한다.</summary>
    private void SaveSettings() => UserSettings.Save(new UserSettings(_allyAi, _showGrid, _showGauges, _zoomPercent, _viewW, _viewH, _showHints, _showLevelUp, _keepJobExp, _showStatusBar, _gameSpeed, _talkPauseSeconds, _showSceneTag, _showChestContents, _fullSoulAtStart));

    /// <summary>전투를 시작할 때 내 편 SOUL 을 가득 채우나 — 모드 > 전투 시작 시 소울 가득(원본에 없는 편의 기능, 기본 끔).</summary>
    private bool _fullSoulAtStart = UserSettings.Current.FullSoulAtStart;

    /// <summary>지금 전투의 상자에 든 것을 왼쪽 위에 보이나 — 모드 > 상자 내용물 보기(원본에 없는 도움 기능, 기본 끔).</summary>
    private bool _showChestContents = UserSettings.Current.ShowChestContents;

    /// <summary>화면 맨 위 상태 줄을 보일지 — 설정 > 상단 상태 줄 보이기(기본 끔).</summary>
    private bool _showStatusBar = UserSettings.Current.ShowStatusBar;

    /// <summary>대사와 대사 사이의 멈춤(초) — 설정 > 대사 사이 멈춤. 음수면 원본대로 스크립트 값.</summary>
    private double _talkPauseSeconds = UserSettings.Current.TalkPauseSeconds;

    /// <summary>화면 왼쪽 아래 장면 번호를 보이나 — 설정 > 장면 번호 보이기.</summary>
    private bool _showSceneTag = UserSettings.Current.ShowSceneTag;

    /// <summary>그 값에 해당하는 메뉴 줄 — 목록에 없는 값(settings.json 을 손으로 고친 것)이면 −1(아무 줄도 체크 안 함).</summary>
    private static int TalkPauseIndex(double seconds) =>
        Array.FindIndex(TalkPauseChoices, c => c < 0 ? seconds < 0 : Math.Abs(c - seconds) < 1e-6);

    private static string TalkPauseLabel(double seconds) => seconds < 0 ? "원본대로(보통 1초)" : $"{seconds:0.0#}초";

    /// <summary>게임 속도 %(100 = 원본). 게임 시계가 실제 시간의 이만큼 흐른다.</summary>
    private int _gameSpeed = UserSettings.Current.GameSpeed is 100 or 150 or 200 ? UserSettings.Current.GameSpeed : 100;

    /// <summary>전직할 때 EXP 를 남길지 — 설정 > 전직할 때 EXP 유지. 원본은 0 으로 비운다.</summary>
    private bool _keepJobExp = UserSettings.Current.KeepExpOnJobChange;

    private void OnMenuCommand(int id)
    {
        switch (id)
        {
            case MenuChapters: _chaptersOpen = true; _chaptersHover = -1; break;
            case MenuClearEnemies: ClearEnemiesForTest(); break;
            case >= MenuReplayBase and < MenuReplayBase + 500:
                ReopenPlace(id - MenuReplayBase);
                break;
            case MenuAllyAi:
                // 동맹(편 3)의 제어권을 AI 에 줄지 — 켜면 AI 가, 끄면 내가 움직인다.
                _allyAi = !_allyAi;
                Win32.CheckMenuItem(Win32.GetMenu(_hwnd), MenuAllyAi, Win32.MF_BYCOMMAND | (_allyAi ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                Toast(_allyAi ? "동맹은 AI 가 움직입니다" : "동맹도 내가 움직입니다");
                SaveSettings();
                break;
            case MenuHints:
                _showHints = !_showHints;
                Win32.CheckMenuItem(Win32.GetMenu(_hwnd), MenuHints, Win32.MF_BYCOMMAND | (_showHints ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                if (!_showHints) _toast = "";
                Toast(_showHints ? "조작 안내 글을 보입니다" : "조작 안내 글을 숨깁니다");
                SaveSettings();
                break;
            case MenuLevelUpWindow:
                _showLevelUp = !_showLevelUp;
                Win32.CheckMenuItem(Win32.GetMenu(_hwnd), MenuLevelUpWindow, Win32.MF_BYCOMMAND | (_showLevelUp ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                if (!_showLevelUp && LevelUpOpen) CloseLevelUp();
                Toast(_showLevelUp ? "레벨업 창을 보입니다" : "레벨업 창을 숨깁니다 — 레벨은 그대로 오릅니다");
                SaveSettings();
                break;
            case >= MenuTalkPauseBase and < MenuTalkPauseBase + 6:
                _talkPauseSeconds = TalkPauseChoices[id - MenuTalkPauseBase];
                for (int i = 0; i < TalkPauseChoices.Length; i++)
                    Win32.CheckMenuItem(Win32.GetMenu(_hwnd), (uint)(MenuTalkPauseBase + i), Win32.MF_BYCOMMAND | (i == id - MenuTalkPauseBase ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                Toast($"대사 사이 멈춤: {TalkPauseLabel(_talkPauseSeconds)}");
                SaveSettings();
                break;
            case MenuProgress: ToggleProgress(); break;
            case MenuFullSoul:
                _fullSoulAtStart = !_fullSoulAtStart;
                Win32.CheckMenuItem(Win32.GetMenu(_hwnd), MenuFullSoul, Win32.MF_BYCOMMAND | (_fullSoulAtStart ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                Toast(_fullSoulAtStart ? "다음 전투부터 내 편 소울을 가득 채워 시작합니다" : "전투를 원본대로 시작 소울로 시작합니다");
                SaveSettings();
                break;
            case MenuChestContents:
                _showChestContents = !_showChestContents;
                Win32.CheckMenuItem(Win32.GetMenu(_hwnd), MenuChestContents, Win32.MF_BYCOMMAND | (_showChestContents ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                Toast(_showChestContents ? "상자 내용물을 왼쪽 위에 보입니다" : "상자 내용물을 숨깁니다");
                SaveSettings();
                break;
            case MenuSceneTag:
                _showSceneTag = !_showSceneTag;
                Win32.CheckMenuItem(Win32.GetMenu(_hwnd), MenuSceneTag, Win32.MF_BYCOMMAND | (_showSceneTag ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                SaveSettings();
                break;
            case MenuStatusBar:
                _showStatusBar = !_showStatusBar;
                Win32.CheckMenuItem(Win32.GetMenu(_hwnd), MenuStatusBar, Win32.MF_BYCOMMAND | (_showStatusBar ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                SaveSettings();
                break;
            case MenuKeepJobExp:
                _keepJobExp = !_keepJobExp;
                Win32.CheckMenuItem(Win32.GetMenu(_hwnd), MenuKeepJobExp, Win32.MF_BYCOMMAND | (_keepJobExp ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                Toast(_keepJobExp ? "전직해도 EXP 를 남깁니다" : "전직하면 EXP 가 0 이 됩니다(원본대로)");
                SaveSettings();
                break;
            case MenuKeys: _keysOpen = true; _keysCapture = -1; _heldMoveKeys.Clear(); break;
            case MenuGrid: _showGrid = !_showGrid; SaveSettings(); break;
            case MenuGauges: _showGauges = !_showGauges; SaveSettings(); break;
            case >= MenuSpeedBase and < MenuSpeedBase + 3:
                _gameSpeed = SpeedChoices[id - MenuSpeedBase];
                for (int i = 0; i < SpeedChoices.Length; i++)
                    Win32.CheckMenuItem(Win32.GetMenu(_hwnd), (uint)(MenuSpeedBase + i), Win32.MF_BYCOMMAND | (i == id - MenuSpeedBase ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                SaveSettings();
                Toast(_gameSpeed == 100 ? "게임 속도: 보통(원본)" : $"게임 속도: {_gameSpeed / 100.0:0.#}배");
                break;
            case >= MenuResBase and < MenuResBase + 11:
            {
                (_viewW, _viewH) = ResChoices[id - MenuResBase];
                for (int i = 0; i < ResChoices.Length; i++)
                    Win32.CheckMenuItem(Win32.GetMenu(_hwnd), (uint)(MenuResBase + i), Win32.MF_BYCOMMAND | (i == id - MenuResBase ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                SaveSettings();
                ApplyZoom(force: true);      // 보이는 영역이 바뀌면 텍스처·창을 새로 잡는다
                Toast($"해상도: {_viewW}×{_viewH}");
                break;
            }
            case >= MenuZoomAuto and < MenuZoomAuto + 6:
            {
                _zoomPercent = ZoomChoices[id - MenuZoomAuto];
                for (int i = 0; i < ZoomChoices.Length; i++)
                    Win32.CheckMenuItem(Win32.GetMenu(_hwnd), (uint)(MenuZoomAuto + i), Win32.MF_BYCOMMAND | (i == id - MenuZoomAuto ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                SaveSettings();
                ApplyZoom();
                Toast(_zoomPercent == 0 ? "배율: 자동(창을 채움)" : $"배율: {_zoomPercent}%");
                break;
            }
            case MenuExit: _running = false; break;
        }
    }

    // ── 단축키 설정 창 ───────────────────────────────────────────────────────

    private const int KeysW = 420, KeysRowH = 26, KeysTop = 40;

    private (int X, int Y, int H) KeysPanel()
    {
        int h = KeysTop + KeyBindings.All.Length * KeysRowH + 56;
        return (_camX + (ViewWidth - KeysW) / 2, _camY + GridTop + (ViewHeight - GridTop - h) / 2, h);
    }

    private void OnKeysKey(int key)
    {
        if (_keysCapture < 0)
        {
            if (key == Win32.VK_ESCAPE) _keysOpen = false;
            return;
        }
        if (key != Win32.VK_ESCAPE)
        {
            _keys.Assign(KeyBindings.All[_keysCapture].Action, key);
            _keys.Save();
        }
        _keysCapture = -1;
    }

    private bool OnKeysClick(int bx, int by)
    {
        if (!_keysOpen) return false;
        var (x, y, h) = KeysPanel();
        int row = (by - y - KeysTop) / KeysRowH;
        if (by >= y + KeysTop && row < KeyBindings.All.Length && bx >= x && bx < x + KeysW) { _keysCapture = row; return true; }

        int buttonsY = y + h - 40;
        if (by >= buttonsY && by < buttonsY + 28)
        {
            if (bx >= x + 16 && bx < x + 136) { _keys.ResetDefaults(); _keys.Save(); _keysCapture = -1; }
            else if (bx >= x + KeysW - 116 && bx < x + KeysW - 16) _keysOpen = false;
        }
        return true;
    }

    private void DrawKeysPanel()
    {
        if (!_keysOpen) return;
        var (x, y, h) = KeysPanel();
        FillRect(x, y, KeysW, h, PanelBg);
        StrokeRect(x, y, KeysW, h, BoxLine);
        FillRect(x, y, KeysW, 28, HeadBg);
        DrawText("단축키 설정 — 줄을 누르고 새 키를 누르세요 (Esc: 그만)", x + 10, y + 6, White);

        for (int i = 0; i < KeyBindings.All.Length; i++)
        {
            var (action, label, _) = KeyBindings.All[i];
            int ry = y + KeysTop + i * KeysRowH;
            bool capturing = i == _keysCapture;
            if (capturing) FillRect(x + 4, ry - 2, KeysW - 8, KeysRowH - 2, 0xFF2A4A8A);
            DrawText(label, x + 16, ry + 3, White);
            string key = capturing ? "키를 누르세요…" : KeyBindings.KeyName(_keys[action]);
            if (action is KeyAction.MoveUp or KeyAction.MoveDown or KeyAction.MoveLeft or KeyAction.MoveRight && !capturing) key += "  (방향키도)";
            FillRect(x + 220, ry, 184, KeysRowH - 6, BoxBg);
            StrokeRect(x + 220, ry, 184, KeysRowH - 6, BoxLine);
            DrawText(key, x + 228, ry + 3, capturing ? 0xFFFFE070 : White);
        }

        int by = y + h - 40;
        FillRect(x + 16, by, 120, 28, HeadBg);
        DrawText("기본값으로", x + 42, by + 6, White);
        FillRect(x + KeysW - 116, by, 100, 28, HeadBg);
        DrawText("닫기", x + KeysW - 80, by + 6, White);
        DrawText("Esc: 취소(고정)", x + 150, by + 7, DimGray);
    }
}

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

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuelDx", "keys.json");

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
    private const int MenuAllyAi = 1006;

    /// <summary>동맹(편 3)을 AI 가 움직이나 — 끄면 내가 직접 움직인다(설정 > 모드).</summary>
    private bool _allyAi = UserSettings.Current.AllyAi;

    /// <summary>창 위 메뉴 막대 — 설정(단축키 설정·격자·체력바·끝내기).</summary>
    private static IntPtr CreateMenuBar()
    {
        IntPtr bar = Win32.CreateMenu(), settings = Win32.CreatePopupMenu(), game = Win32.CreatePopupMenu(), mode = Win32.CreatePopupMenu();
        Win32.AppendMenuW(mode, Win32.MF_STRING | (UserSettings.Current.AllyAi ? Win32.MF_CHECKED : 0u), MenuAllyAi, "동맹을 AI 가 움직임(&A)");
        Win32.AppendMenuW(game, Win32.MF_STRING, MenuChapters, "챕터 고르기(&C)...");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)game, "게임(&G)");
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuKeys, "단축키 설정(&K)...");
        Win32.AppendMenuW(settings, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuGrid, "격자 켜기·끄기(&G)");
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuGauges, "체력바 켜기·끄기(&H)");
        Win32.AppendMenuW(settings, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenuW(settings, Win32.MF_STRING, MenuExit, "끝내기(&X)");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)mode, "모드(&M)");
        Win32.AppendMenuW(bar, Win32.MF_POPUP, (nuint)settings, "설정(&S)");
        return bar;
    }

    /// <summary>모드·격자·체력바를 바꾸면 바로 적어 다음에 켤 때도 그대로 두게 한다.</summary>
    private void SaveSettings() => UserSettings.Save(new UserSettings(_allyAi, _showGrid, _showGauges));

    private void OnMenuCommand(int id)
    {
        switch (id)
        {
            case MenuChapters: _chaptersOpen = true; _chaptersHover = -1; break;
            case MenuAllyAi:
                // 동맹(편 3)의 제어권을 AI 에 줄지 — 켜면 AI 가, 끄면 내가 움직인다.
                _allyAi = !_allyAi;
                Win32.CheckMenuItem(Win32.GetMenu(_hwnd), MenuAllyAi, Win32.MF_BYCOMMAND | (_allyAi ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
                Toast(_allyAi ? "동맹은 AI 가 움직입니다" : "동맹도 내가 움직입니다");
                SaveSettings();
                break;
            case MenuKeys: _keysOpen = true; _keysCapture = -1; _heldMoveKeys.Clear(); break;
            case MenuGrid: _showGrid = !_showGrid; SaveSettings(); break;
            case MenuGauges: _showGauges = !_showGauges; SaveSettings(); break;
            case MenuExit: _running = false; break;
        }
    }

    // ── 단축키 설정 창 ───────────────────────────────────────────────────────

    private const int KeysW = 420, KeysRowH = 26, KeysTop = 40;

    private (int X, int Y, int H) KeysPanel()
    {
        int h = KeysTop + KeyBindings.All.Length * KeysRowH + 56;
        return ((BoardWidth - KeysW) / 2, _camY + GridTop + (ViewHeight - GridTop - h) / 2, h);
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

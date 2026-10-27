using System.IO;
using System.Text.Json;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 링 커맨드 "시스템" 메뉴(menu-1)와 저장·불러오기(menu-2)·끝내기(menu-3).
/// </summary>
/// <remarks>
/// 옵시디안 분석-시스템메뉴 그대로: 항목 여섯 개(MISSION·RESTART·LOAD·SAVE·VOLUME·EXIT GAME), 칸 190×37,
/// 안쪽 여백 12, 글자는 TXR 이 아니라 <c>Obs 0894</c> 그림(모션 11·3·1·0·2·4)이다. 마우스로만 고른다.
/// RESTART·EXIT GAME 은 확인 창을 거치고, 저장에 성공하면 Snd 579 가 난다. 음량 창은 B.G.M·S.E 막대 20칸(5~100).
/// <para>
/// 원본 저장도 파티뿐 아니라 <b>전투판 전체</b>(전투 장면 자기 저장 <c>vt+0x18 = 0x10061960</c>)를 담고, 불러오면 저장했던
/// 인물의 차례 처음(상태 22)으로 돌아온다(분석-시스템메뉴 2.4b — 예전 노트의 「처음부터 다시 연다」는 틀렸다).
/// 이 데모는 같은 것을 JSON 으로 <c>%APPDATA%\DuelDx\battle-save-NN.json</c> 에 적는다.
/// EXIT GAME 은 바탕화면이 아니라 <b>타이틀 화면</b>으로 나간다 — 게임을 진짜 끝내는 곳은 타이틀의 EXIT 뿐이다.
/// </para>
/// </remarks>
internal sealed unsafe partial class SystemMenu(GameWindow host)
{
    internal const int SystemObs = 894;
    internal const int SystemW = 214, SystemRowH = 37, SystemRowW = 190, SystemPad = 12;
    internal const int SoundSaved = 579;
    /// <summary>목록 줄 바탕 그림 — 분석-시스템메뉴·분석-모션 「Obs 파일 갈래」.</summary>
    internal const int ListRowObs = 471;

    /// <summary>시스템 메뉴 항목 — 원본 차례대로(위에서 아래), 글자는 Obs 0894 모션.</summary>
    internal enum SystemItem { Mission, Restart, Load, Save, Volume, Mods, Settings, Exit }

    internal static readonly (SystemItem Item, int Motion, string Label)[] SystemItems =
    [
        (SystemItem.Mission, 11, "MISSION"),
        (SystemItem.Restart, 3, "RESTART"),
        (SystemItem.Load, 1, "LOAD"),
        (SystemItem.Save, 0, "SAVE"),
        (SystemItem.Volume, 2, "VOLUME"),
        (SystemItem.Mods, -1, "MODE"),          // 원본에 없는 줄(사용자 요청 menu-34) — 모드 창을 연다. 글자 그림은 다른 줄에서 따와 만든다(DrawModeLabel)
        (SystemItem.Settings, -2, "SETTING"),   // 원본에 없는 줄(사용자 요청 menu-41) — 설정 창을 연다. 전체화면에서는 메뉴 막대가 없어 여기가 설정으로 가는 길이다
        (SystemItem.Exit, 4, "EXIT GAME"),
    ];

    /// <summary>
    /// 지금 화면의 시스템 메뉴 항목 — 모세스(분석-모세스 13절)와 연대표에서는 MISSION·RESTART 가 없다. 연대표 ESC(<c>0x10106e50</c>)도
    /// 모세스와 같은 <c>0x10102ba0</c> 로 LOAD·SAVE·VOLUME·EXIT GAME 넷만 만든다(<c>0x10102c3c</c>~<c>0x10102d0f</c>, 감사 F2).
    /// </summary>
    /// <remarks>
    /// 필드(장면 3 <c>0x100eb9e0</c>)에는 원본에 시스템 메뉴가 아예 없다(감사5 S3). 데모는 편의로 메뉴를 열어 주되 모세스와 같은 넷만 두고
    /// (뒤에 남은 옛 전투의 MISSION·RESTART 는 뜻이 없다) SAVE 는 꺼 둔다(<see cref="SaveBlockedReason"/>).
    /// </remarks>
    internal (SystemItem Item, int Motion, string Label)[] MenuItems =>
        host.Mos._mosesOpen || host.EpisodesScr._episodesOpen || host.FieldOpen ? [.. SystemItems.Where(i => i.Item is not (SystemItem.Mission or SystemItem.Restart))] : SystemItems;

    /// <summary>
    /// 전투 시스템 메뉴를 열어도 되나 — 원본은 <b>플레이어 조종 유닛 차례의 상태 22</b>(<c>0x1006acc0</c>, 입력 깃발 <c>+0x4d17</c>
    /// → 메뉴 <c>0x100e3700</c> → 상태 25, <c>0x1006ae3b~0x1006ae8f</c>)나 그 유닛의 링(<c>0x10068cb2</c>·<c>0x100e1e6c</c>)에서만 연다.
    /// AI 차례·행동 연출·이벤트(<c>0x10066197</c>)·배치(상태 2)·레벨업·결과 중에는 같은 키가 취소/무시일 뿐이다(감사5 S1·S8).
    /// </summary>
    internal bool CanOpenBattleMenu =>
        host.Btl.IsPlayerTurn && !host._units[host.Btl._turn].IsBusy && !host.Btl.EventsBusy && !host.Btl._deployOpen && host.Btl._levelUpUnit < 0 && host.Btl._levelUpQueue.Count == 0
        && !host.Btl.Part1PickPending && host.Btl._delayedHits.Count == 0 && !host.SceneFading;

    /// <summary>모세스 위에서 챕터 사건이 도는 중인가 — 원본 챕터 장면 Esc 는 <c>[0x101bffac]</c>(도는 사건 수)가 0 일 때만 메뉴를 연다(<c>0x100f78ec~0x100f7922</c>).</summary>
    internal bool ChapterEventRunning => host.Mos._mosesOpen && (host.Fld._fieldEvent >= 0 || host.Tlk._talk != null || host.Fld._fieldChoices != null);

    /// <summary>
    /// 지금 SAVE 를 못 하는 까닭 — 할 수 있으면 null. 필드(원본에 저장 없음, 감사5 S3)·모세스 챕터 사건 도중(S4)·
    /// 전투에서 내 차례 조종 상태가 아닐 때(S1 방어 — 메뉴는 그때만 열리지만 창이 떠 있는 사이 바뀌었을 수 있다).
    /// </summary>
    internal string? SaveBlockedReason =>
        // 파트1 챕터 지도 — 사건이 멎어 있으면 저장한다(장면 갈래 4, SavePart1ChapterTo).
        host.Part1TitleScr.TimelineOpen ? null
        : host.Part1TitleScr.ChapterMapOpen ? (host.Part1TitleScr.Chapter.CanSave ? null : "사건이 진행 중이라 저장할 수 없습니다")
        // 파트1 전투도 저장한다 — 파트1 진행(일행 · 성장 · 챕터 · 남은 사건)을 세이브에 같이 싣는다(SaveState.Part1, 사용자 요청).
        : host.FieldOpen ? "필드에서는 저장할 수 없습니다"
        // 원본은 챕터 79 · 80(튜토리얼) · 55 전투에서 시스템 메뉴의 SAVE 를 끈다(0x10096567). 진행 자동 저장(part1.json)은 그대로 한다(사용자 결정).
        // 튜토리얼(79 · 80)은 모드 > 일반 「튜토리얼에서도 저장」이 켜져 있으면 저장한다(사용자 요청 menu-104 · menu-105, 기본 켬) — 55 는 원본대로 막는다.
        : host._battleLoaded && host._scene.IsPart1 && (host.Part1.ChapterId == 55 || (host.Part1.ChapterId is 79 or 80 && !host._part1TutorialSave)) ? "이 전투에서는 저장할 수 없습니다"
        : ChapterEventRunning ? "사건이 진행 중이라 저장할 수 없습니다"
        : !host.Mos._mosesOpen && !host.EpisodesScr._episodesOpen && !host.TitleScr._titleOpen && !host.RecordsScr._recordsOpen && host._battleLoaded && !CanOpenBattleMenu ? "지금은 저장할 수 없습니다"
        : null;

    internal bool _systemMenu;
    internal bool _missionWindow, _volumeWindow;
    internal (string Title, string Text, Action Yes)? _confirm;

    internal void OpenSystemMenu()
    {
        _systemMenu = true;
        _missionWindow = _volumeWindow = false;
        _confirm = null;
    }

    internal bool SystemOpen => _systemMenu || _missionWindow || _volumeWindow || _confirm != null || host.SlotsScr.SlotsOpen;

    internal (int X, int Y, int H) SystemMenuRect()
    {
        int h = SystemPad * 2 + MenuItems.Length * SystemRowH;
        // 연대표·모세스의 System Menu(0x10102ba0 → 0x10046410(…, 0x44c, 창, 100, 100, "System Menu", …))는 원본에서 640×480 틀의 (100,100) 에
        // 섰다(감사 F16) — 왼쪽 위로 치우쳐 보여 틀 가운데로 옮겼다(사용자 요청 menu-47). 제목줄(창 y−22)까지 넣어 가운데를 잡는다.
        if (host.EpisodesScr._episodesOpen || host.Mos._mosesOpen)
        {
            var (ox, oy) = host.Mos.MosesOrigin();
            return (ox + (MosesScene.MosesW - SystemW) / 2, oy + (MosesScene.MosesH - h - 22) / 2 + 22, h);
        }
        return (host._camX + (host.ViewWidth - SystemW) / 2, host._camY + (host.ViewHeight - h) / 2, h);
    }

    internal void RunSystemItem(SystemItem item)
    {
        // MISSION·SAVE·LOAD 는 메뉴를 숨기기만 하고, 그 창이 닫히면 메뉴가 다시 보인다(0x100e39f0 → vt+0xcc). fg-22.
        _systemMenuReturn = item is SystemItem.Mission or SystemItem.Volume or SystemItem.Save or SystemItem.Load or SystemItem.Restart or SystemItem.Exit;   // 확인창 「아니오」도 메뉴로(0x100e3ce9)
        switch (item)
        {
            case SystemItem.Mission: _missionWindow = true; break;
            case SystemItem.Volume: _volumeWindow = true; break;
            case SystemItem.Mods:
                _systemMenuReturn = false;
                (host.TuningScr._tuningOpen, host.TuningScr._tuningListOpen) = (true, false);
                break;
            case SystemItem.Settings:
                _systemMenuReturn = false;
                host.SettingsScr.Open();
                break;
            case SystemItem.Save:
                // 꺼진 SAVE — 메뉴는 그대로 두고 까닭만 알린다(감사5 S3·S4).
                if (SaveBlockedReason is { } why) { host.Toast(why); _systemMenuReturn = false; _systemMenu = true; break; }
                host.SlotsScr.OpenSlots(0);
                break;
            case SystemItem.Load: host.SlotsScr.OpenSlots(1); break;
            case SystemItem.Restart:
                _confirm = ("RESTART", "전투를 다시 시작하시겠습니까?", RestartFromDeploy);
                break;
            case SystemItem.Exit:
                _confirm = ("EXIT GAME", "창세기전3 PartII를 종료하시겠습니까?", () => host.LeaveScene(BackToTitle));   // 16틀 검게 나간다(ba-21 outer #2)
                break;
        }
    }

    /// <summary>
    /// EXIT GAME — <b>바탕화면이 아니라 타이틀 화면</b>으로 나간다. 원본이 그렇게 한다.
    /// </summary>
    /// <remarks>
    /// 게임을 진짜로 끝내는 곳은 <b>타이틀의 EXIT</b> 뿐이다. 나가면서 전투·필드·모세스 창을 다 닫고,
    /// 다시 CONTINUE 로 들어올 때 전투를 새로 읽도록 <see cref="_battleLoaded"/> 를 내린다.
    /// </remarks>
    internal void BackToTitle()
    {
        _systemMenu = _missionWindow = _volumeWindow = false;
        host.SlotsScr._slotsMode = -1;
        host.Fld.CloseField();
        host.Mos._mosesOpen = false;
        host.Mos._mosesPage = -1;
        host.EpisodesScr._episodesOpen = false;          // 연대표에서 EXIT GAME — 연대표 장면을 끝낸다(0x10106530, 감사 F3)
        host._battleLoaded = false;
        host.ResizeBoard(TitleBoardCols, TitleBoardRows);
        host.TitleScr.OpenTitle();
    }

    /// <summary>열린 창이 있으면 클릭을 처리하고 true.</summary>
    internal bool OnSystemClick(int bx, int by)
    {
        if (_confirm is not null && OnConfirmClick()) return true;
        if (host.SlotsScr.OnSlotsClick(bx, by)) return true;
        return OnMenuClick(bx, by);

        bool OnConfirmClick()
        {
            var confirm = _confirm!;
            var (cx, cy, cw, ch) = ConfirmRect();
            if (by >= cy + ch - 34 && by < cy + ch - 8)
            {
                if (bx >= cx + cw / 2 - 86 && bx < cx + cw / 2 - 10) { _confirm = null; bool slots = host.SlotsScr.SlotsOpen; confirm.Value.Yes(); if (!slots || !host.SlotsScr.SlotsOpen) _systemMenuReturn = false; return true; }
                if (bx >= cx + cw / 2 + 10 && bx < cx + cw / 2 + 86) { _confirm = null; ReturnToSystemMenu(); return true; }
            }
            return true;
        }
    }

    /// <summary>하위 창(MISSION·SAVE·LOAD)이 닫히면 시스템 메뉴를 다시 보일지.</summary>
    internal bool _systemMenuReturn;

    /// <summary>하위 창이 닫혔다 — 메뉴에서 열었으면 메뉴로 돌아간다.</summary>
    internal void ReturnToSystemMenu()
    {
        // 슬롯 창 위의 확인창이 닫혔을 뿐이면 슬롯 창이 그대로 남는다 — 메뉴는 슬롯 창이 닫힐 때 돌아온다(ba-20 G3).
        if (!_systemMenuReturn || host.SlotsScr.SlotsOpen) return;
        _systemMenuReturn = false;
        if (!host.TitleScr._titleOpen && !host.RecordsScr._recordsOpen) _systemMenu = true;
    }

    internal bool OnMenuClick(int bx, int by)
    {
        if (_missionWindow || _volumeWindow)
        {
            if (_volumeWindow) OnVolumeClick(bx, by);
            else { _missionWindow = false; ReturnToSystemMenu(); }
            return true;
        }
        if (!_systemMenu) return false;

        var (x, y, _) = SystemMenuRect();
        int index = (by - y - SystemPad) / SystemRowH;
        if (bx < x + SystemPad || bx >= x + SystemPad + SystemRowW || index < 0 || index >= MenuItems.Length) { _systemMenu = false; return true; }
        _systemMenu = false;
        RunSystemItem(MenuItems[index].Item);
        return true;
    }

    /// <summary>Esc — 열린 창을 하나씩 닫는다. 닫았으면 true.</summary>
    internal bool CloseSystemWindow()
    {
        if (_confirm != null) { _confirm = null; ReturnToSystemMenu(); return true; }
        // 타이틀 LOAD GAME 의 기록 화면에서는 목록만 닫지 않고 타이틀로 돌아간다 — 목록만 닫히면 빈 화면에 남아 다음 조작이 끊겼다(사용자 보고 menu-32).
        if (host.SlotsScr.SlotsOpen && host.RecordsScr._recordsOpen) { host.RecordsScr.CloseRecords(); return true; }
        if (host.SlotsScr.SlotsOpen) { host.SlotsScr._slotsMode = -1; ReturnToSystemMenu(); return true; }
        if (_missionWindow || _volumeWindow) { _missionWindow = _volumeWindow = false; ReturnToSystemMenu(); return true; }
        if (_systemMenu) { _systemMenu = false; return true; }
        return false;
    }

    internal void DrawSystem()
    {
        host.SlotsScr.DrawSlots();
        host.SlotsScr.DrawNotice();
        if (_systemMenu) DrawSystemMenu();
        if (_missionWindow) DrawMissionWindow();
        if (_volumeWindow) DrawVolumeWindow();
        if (_confirm != null) DrawConfirm();
    }

    internal void DrawSystemMenu()
    {
        var (x, y, h) = SystemMenuRect();
        // 모든 창은 원본 틀(Obs 0970 — 바탕 모션 7, 귀퉁이 0~3, 제목줄 4, 바깥 1px 흰 선)이다(ba-20 G13). 전에는 단색 네모였다.
        host.UiFrame(x, y, SystemW, h, "System Menu");

        var items = MenuItems;
        for (int i = 0; i < items.Length; i++)
        {
            var (item, motion, label) = items[i];
            int rx = x + SystemPad, ry = y + SystemPad + i * SystemRowH;
            // 평소 칸은 바탕 + 테두리(원본 갈래 0 단추 0x100432e0). Obs 0471 모션 7(190×37 파란 빛)은 <b>보조 그림</b>(단추 +0x10c)이라
            // 마우스가 올라간 줄에만 덧그린다 — 전에는 모든 줄에 그려 전부 올림 상태로 보였다.
            host.UiFill(rx, ry, SystemRowW, SystemRowH - 3, StatusScreen.BoxBg);
            host.UiStroke(rx, ry, SystemRowW, SystemRowH - 3, StatusScreen.BoxLine);
            bool disabled = item == SystemItem.Save && SaveBlockedReason != null;
            bool hover = !disabled && host._mouse.X >= rx && host._mouse.X < rx + SystemRowW && host._mouse.Y >= ry && host._mouse.Y < ry + SystemRowH - 3;
            if (hover) host.UiSprite(ListRowObs, 7, 0, rx, ry, loop: false);
            // 새 UI 는 글자 그림(점으로 된 글씨라 늘리면 깍두기가 된다) 대신 아이콘만 그림에서 따오고 글은 창 해상도로 쓴다(menu-57).
            if (host._hiResUi)
            {
                if (motion == -2) DrawSettingLabel(rx + 20, ry + 10, iconOnly: true);
                else if (motion < 0) DrawModeLabel(rx + 20, ry + 10, iconOnly: true);
                else if (host.UiFor(SystemObs)?.FrameAt(motion, 0, loop: false) is { } f)
                    host.Hi.DrawImage(f.Px, f.W, Math.Min(f.W, LabelIconW), f.H, rx + 20 + f.X, ry + 10 + f.Y);
                var (_, _, th) = host.UiGetText(label, White, LabelSize);
                host.UiText(label, rx + 20 + LabelTextX, ry + (SystemRowH - 3 - th) / 2, disabled ? DimGray : White, LabelSize);
            }
            // 원본은 칸 안 (20,10) 자리에 Obs 0894 글자 그림을 찍는다.
            else if (!(motion == -2 ? DrawSettingLabel(rx + 20, ry + 10) : motion < 0 ? DrawModeLabel(rx + 20, ry + 10) : host.DrawUi(SystemObs, motion, 0, rx + 20, ry + 10, GameWindow.UiBlend.Alpha, loop: false)))
                host.DrawText(label, rx + 20, ry + 9, disabled ? DimGray : White);
            // 꺼진 SAVE(필드·챕터 사건 도중)는 어둡게 덮는다.
            if (disabled) host.UiFill(rx, ry, SystemRowW, SystemRowH - 3, 0xA0000000);
        }
    }

    /// <summary>새 UI 의 줄 글 — 글자 그림에서 아이콘으로 치는 왼쪽 너비, 글이 시작하는 자리(글자 그림의 글자 자리와 같다), 글 크기.</summary>
    private const int LabelIconW = 24, LabelTextX = 32;
    private const float LabelSize = 15f;

    /// <summary>「MODE」 줄 그림 — 처음 그릴 때 한 번 만든다(너비 81 · 높이 22, 기준점은 (2, 3)).</summary>
    private uint[]? _modeLabel;
    private const int ModeLabelW = 81, ModeLabelH = 22;

    /// <summary>
    /// 「MODE」 줄을 다른 줄과 같은 글씨로 그린다 — Obs 0894 에 그 그림이 없어 있는 그림에서 따온다:
    /// 아이콘은 S.E 줄(모션 6)의 반짝이, 글자 M · O · E 는 VOLUME(모션 2), D 는 LOAD(모션 1)에서. 그림 크기가 다르면(다른 판 자료) false — 글자로 그린다.
    /// </summary>
    private bool DrawModeLabel(int x, int y, bool iconOnly = false)
    {
        if (_modeLabel == null)
        {
            if (host.UiFor(SystemObs) is not { } obs || obs.FrameAt(2, 0) is not { W: 98, H: 22 } volume
                || obs.FrameAt(1, 0) is not { W: 76, H: 17 } load || obs.FrameAt(6, 0) is not { W: 53, H: 20 } se) return false;
            var canvas = new uint[ModeLabelW * ModeLabelH];
            void Copy(uint[] px, int w, int fromX, int fromY, int cols, int rows, int toX, int toY)
            {
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++) canvas[(toY + r) * ModeLabelW + toX + c] = px[(fromY + r) * w + fromX + c];
            }
            Copy(se.Px, se.W, 0, 0, 20, 20, 0, 1);              // 아이콘
            Copy(volume.Px, volume.W, 75, 5, 13, 12, 34, 5);    // M
            Copy(volume.Px, volume.W, 44, 5, 11, 12, 48, 5);    // O
            Copy(load.Px, load.W, 65, 2, 11, 12, 60, 5);        // D
            Copy(volume.Px, volume.W, 89, 5, 9, 12, 72, 5);     // E
            _modeLabel = canvas;
        }
        if (iconOnly) host.Hi.DrawImage(_modeLabel, ModeLabelW, LabelIconW, ModeLabelH, x - 2, y - 3);
        else host.BlitMasked(_modeLabel, ModeLabelW, ModeLabelH, x - 2, y - 3);
        return true;
    }

    private uint[]? _settingLabel;
    private const int SettingLabelW = 107;

    /// <summary>톱니 아이콘 — 다른 줄의 아이콘과 같은 색으로 찍는다(그림에 톱니가 없어 점 그림으로 그렸다).</summary>
    private static readonly string[] GearIcon =
    [
        ".....###.....", "..#..###..#..", ".###.###.###.", "..#########..", "...##...##...", "####.....####", "####.....####",
        "####.....####", "...##...##...", "..#########..", ".###.###.###.", "..#..###..#..", ".....###.....",
    ];

    /// <summary>
    /// 「SETTING」 줄 — 「MODE」와 같은 꼴로 만든다: 글자 S 는 SAVE(모션 0), E 는 VOLUME(2), T 는 RESTART(3), I · N 은 MISSION(11), G 는 EXIT GAME(4)에서 따오고,
    /// 아이콘은 톱니를 점으로 그린다. 그림 크기가 다르면 false — 글자로 그린다.
    /// </summary>
    private bool DrawSettingLabel(int x, int y, bool iconOnly = false)
    {
        if (_settingLabel == null)
        {
            if (host.UiFor(SystemObs) is not { } obs || obs.FrameAt(0, 0) is not { W: 75, H: 17 } save || obs.FrameAt(2, 0) is not { W: 98, H: 22 } volume
                || obs.FrameAt(3, 0) is not { W: 108, H: 18 } restart || obs.FrameAt(11, 0) is not { W: 93, H: 23 } mission
                || obs.FrameAt(4, 0) is not { W: 120, H: 19 } exit) return false;
            var canvas = new uint[SettingLabelW * ModeLabelH];
            void Copy(uint[] px, int w, int fromX, int fromY, int cols, int rows, int toX, int toY)
            {
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++) canvas[(toY + r) * SettingLabelW + toX + c] = px[(fromY + r) * w + fromX + c];
            }
            // 아이콘 색은 SAVE 줄 아이콘의 밝은 점에서 가져온다.
            uint ink = 0xFF80F0C0;
            for (int i = 0; i < 17 * save.W && i < save.Px.Length; i++)
                if (i % save.W < 17 && (save.Px[i] & 0xFF000000) != 0 && (save.Px[i] >> 8 & 0xFF) > 150) { ink = save.Px[i] | 0xFF000000; break; }
            for (int r = 0; r < GearIcon.Length; r++)
                for (int c = 0; c < GearIcon[r].Length; c++)
                    if (GearIcon[r][c] == '#') canvas[(5 + r) * SettingLabelW + 3 + c] = ink;
            Copy(save.Px, save.W, 33, 2, 10, 12, 34, 5);          // S
            Copy(volume.Px, volume.W, 89, 5, 9, 12, 46, 5);       // E
            Copy(restart.Px, restart.W, 63, 4, 10, 12, 57, 5);    // T
            Copy(restart.Px, restart.W, 63, 4, 10, 12, 69, 5);    // T
            Copy(mission.Px, mission.W, 44, 8, 2, 12, 81, 5);     // I
            Copy(mission.Px, mission.W, 83, 8, 10, 12, 85, 5);    // N
            Copy(exit.Px, exit.W, 73, 4, 10, 12, 97, 5);          // G
            _settingLabel = canvas;
        }
        if (iconOnly) host.Hi.DrawImage(_settingLabel, SettingLabelW, LabelIconW, ModeLabelH, x - 2, y - 3);
        else host.BlitMasked(_settingLabel, SettingLabelW, ModeLabelH, x - 2, y - 3);
        return true;
    }

    /// <summary>MISSION — 승리·패배 조건(Btl 머리 워드 5·6).</summary>
    internal void DrawMissionWindow()
    {
        int w = 400, h = 180, x = host._camX + (host.ViewWidth - w) / 2, y = host._camY + (host.ViewHeight - h) / 2;
        host.UiFrame(x, y + 26, w, h - 26, host._scene.Title);
        // 원본 0x100e3d60(400×180): 깃발 Obs 0894 모션 9 @ (20,20), 「승리 조건」 @ (50,20) 노랑, 조건 글 @ (50,50) 흰,
        // 해골 모션 10 @ (20,100), 「패배 조건」 @ (50,100) 노랑, 글 @ (50,130) 흰(ba-21 battle-flow 4). 전에는 라벨이 하늘색·붉은색이고 글이 같은 줄이었다.
        int by = y + 26;
        host.UiSprite(BattleScene.HudObs, 9, 0, x + 20, by + 20, loop: false);
        host.UiText("승리 조건", x + 50, by + 14, BattleScene.HudYellow);
        host.UiText(host._db?.T(host._scene.WinTextId) ?? "", x + 50, by + 44, White);
        host.UiSprite(BattleScene.HudObs, 10, 0, x + 20, by + 100, loop: false);
        host.UiText("패배 조건", x + 50, by + 94, BattleScene.HudYellow);
        host.UiText(host._db?.T(host._scene.LoseTextId) ?? "", x + 50, by + 124, White);

    }

    // ── 음량 창 ──────────────────────────────────────────────────────────────

    internal int _bgmVolume = Math.Clamp(UserSettings.Current.BgmVolume, 5, 100), _seVolume = Math.Clamp(UserSettings.Current.SeVolume, 5, 100);

    /// <summary>「배경음악」 체크 상자(원본 0x2728, (78,120)) — 끄면 음악을 멈추고 켜면 지금 곡을 다시 튼다.</summary>
    internal bool _bgmOn = UserSettings.Current.BgmOn;

    internal const int VolumeW = 220, VolumeH = 160, VolumeCells = 20;

    internal (int X, int Y) VolumeOrigin() => (host._camX + (host.ViewWidth - VolumeW) / 2, host._camY + (host.ViewHeight - VolumeH) / 2);

    internal void OnVolumeClick(int bx, int by)
    {
        var (x, y) = VolumeOrigin();
        foreach (var (rowY, setter) in new (int, Action<int>)[] { (40, v => _bgmVolume = v), (90, v => _seVolume = v) })
        {
            if (by < y + rowY || by >= y + rowY + 13) continue;
            int cell = (bx - (x + 20)) / 9;
            if (cell < 0 || cell >= VolumeCells) continue;
            setter(5 * cell + 5);   // 원본 값 = 5n+5 (5~100)
            ApplyVolumes();
            host.SaveSettings();
            return;
        }
        // 「배경음악」 체크 상자 — (78,120) 언저리.
        if (bx >= x + 70 && bx < x + 200 && by >= y + 116 && by < y + 134)
        {
            _bgmOn = !_bgmOn;
            if (!_bgmOn) host._mixer.StopMusic();   // 곡 번호는 남긴다 — 켜면 되살린다
            else if (host._musicId > 0) host.PlayMusicFile(host._musicId, loop: true);
            host.SaveSettings();
            return;
        }
        // 닫으면 메뉴로 돌아간다 — 다른 하위 창과 같다(0x100e39f0 → vt+0xcc, ba-20 G22). 전에는 메뉴까지 닫혔다.
        if (bx >= x + VolumeW - 24 && by < y + 26 || by >= y + VolumeH - 30) { _volumeWindow = false; ReturnToSystemMenu(); }
    }

    /// <summary>
    /// 음량 막대를 믹서에 넣는다 — B.G.M 은 믹서 전체 크기라 <b>모든 곡·페이드·줄이기</b>에 곱해진다(감사4 V1).
    /// S.E 는 음성의 선형 크기이고, Snd 효과음은 <see cref="SndGain"/> 이 막대 1점당 −0.5 dB 로 셈한다(V3). 시작 때도 한 번 부른다(V2).
    /// </summary>
    internal void ApplyVolumes()
    {
        host._mixer.MusicVolume = _bgmVolume / 100f;
        host._effectGain = _seVolume / 100f;
    }

    internal void DrawVolumeWindow()
    {
        var (x, y) = VolumeOrigin();
        host.UiFrame(x, y + 26, VolumeW, VolumeH - 26, "Volume");

        foreach (var (rowY, label, value) in new (int, string, int)[] { (40, "B.G.M", _bgmVolume), (90, "S.E", _seVolume) })
        {
            host.UiText(label, x + 20, y + rowY - 18, White);
            for (int i = 0; i < VolumeCells; i++)
            {
                int cx = x + 20 + i * 9;
                bool on = 5 * i + 5 <= value;
                host.UiFill(cx, y + rowY, 8, 13, on ? 0xFF6AA8FF : 0xFF203050);
                host.UiStroke(cx, y + rowY, 8, 13, StatusScreen.BoxLine);
            }
            host.UiRightText($"{value}", x + VolumeW - 16, y + rowY - 18, DimGray);
        }
        // 「배경음악」 체크 상자(Obs 806 모션 1/2 자리) — 그림 대신 네모.
        host.UiStroke(x + 78, y + 118, 14, 14, White);
        if (_bgmOn) host.UiFill(x + 81, y + 121, 8, 8, White);
        host.UiText("배경음악", x + 98, y + 117, White);
        host.UiText("X", x + VolumeW - 18, y + 5, White, 15);
        host.UiText("닫으려면 아래를 누르세요", x + 20, y + VolumeH - 26, DimGray);
    }

    // ── 확인 창 ──────────────────────────────────────────────────────────────

    internal (int X, int Y, int W, int H) ConfirmRect()
    {
        // 높이는 본문 줄 수에 맞춘다 — 고정 120 이면 세 줄짜리 전직 확인(「…사라집니다 / 전직할까요?」)의 끝 줄이 단추에 가렸다.
        int textH = _confirm is { } cf ? host.UiGetText(cf.Text, White).H : 16;
        int w = 340, h = Math.Max(120, 48 + textH + 14 + 34);
        return (host._camX + (host.ViewWidth - w) / 2, host._camY + (host.ViewHeight - h) / 2, w, h);
    }

    internal void DrawConfirm()
    {
        if (_confirm is not { } confirm) return;
        var (x, y, w, h) = ConfirmRect();
        host.UiFrame(x, y + 26, w, h - 26, confirm.Title);
        var (_, tw, _) = host.UiGetText(confirm.Text, White);
        host.UiText(confirm.Text, x + (w - tw) / 2, y + 48, White);

        foreach (var (bx, label) in new (int, string)[] { (x + w / 2 - 86, "예"), (x + w / 2 + 10, "아니오") })
        {
            host.UiFill(bx, y + h - 34, 76, 26, StatusScreen.HeadBg);
            host.UiStroke(bx, y + h - 34, 76, 26, StatusScreen.BoxLine);
            var (_, lw, _) = host.UiGetText(label, White);
            host.UiText(label, bx + (76 - lw) / 2, y + h - 30, White);
        }
    }

    /// <summary>RESTART — 같은 전투를 처음부터(원본은 전투 번호 그대로 장면 1 을 다시 연다).</summary>
    /// <summary>
    /// 다른 전투를 건다 — 그 <c>Btl</c> 자료를 읽어 맵·인물·배경음악을 갈아 끼운다(전투 이벤트 행동 10 「다음 전투」도 이 길로 온다).
    /// </summary>
    /// <summary>지금이 챕터 장면인가 — 모세스 주 화면·필드·연대표는 모두 한 챕터 안이다.</summary>
    internal bool InChapterScene => host.Mos._mosesOpen || host.EpisodesScr._episodesOpen || host.FieldOpen;

    /// <param name="rememberParty">
    /// 지금 판의 아군을 파티에 담고 시작할지 — 불러오기는 <b>false</b> 다. 불러오기는 세이브로 파티를 먼저 되살리는데,
    /// 여기서 담으면 불러오기 전 판(타이틀 뒤 데모 전투, 하던 전투)의 유닛이 되살린 파티를 덮어써서
    /// 그 전투에 안 선 파티원의 레벨·장착 어빌리티·어빌리티 레벨이 처음 값으로 돌아갔다(사용자 보고).
    /// </param>
    /// <summary>전투에 들어올 때의 진행 깃발 — RESTART 가 되돌린다.</summary>
    internal byte[]? _entryFlags;

    internal bool StartBattle(int id, bool rememberParty = true)
    {
        if (DemoScene.Load(id, host._db) is not { } scene)
        {
            host.Toast($"전투 {id:D4} 자료가 assets 에 없습니다");
            return false;
        }
        // 어빌리티 시연 — 그 전투의 적은 두고 아군은 시연할 인물 하나만 세운다(SkillDemo.cs).
        if (host.Btl._demo is { } demo) scene = GameWindow.ArenaScene(scene, demo.Chr) with { Title = "어빌리티 시연" };
        try
        {
            host._map = DemoScene.LoadMap(scene);
            host.ResizeBoard(host._map.Cols, host._map.Rows);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            host.Toast($"전투 {id:D4} 맵을 못 읽었습니다: {ex.Message}");
            return false;
        }

        if (rememberParty) host.Btl.RememberParty();          // 앞 전투에서 오른 레벨·경험치를 들고 간다
        host._battleLoaded = true;
        host.Btl._resumeTurn = -1;         // 새 판 — 불러오기는 판을 세운 뒤 다시 정한다
        host.TitleScr._titleOpen = false;       // 타이틀·기록 화면에서 왔으면 이제 전투가 앞이다
        host.RecordsScr._recordsOpen = false;
        host.EpisodesScr._episodesOpen = false;
        host._scene = scene;
        host._units = host.Btl.BuildUnits(scene);
        host.Btl.LoadEvents(scene.Id);
        host.LoadRosterSprites();
        host.Mos._mosesOpen = false;
        host._statusUnit = -1;
        host.Btl._infoUnit = -1;
        host.Btl._ringUnit = -1;
        // RESTART 가 되돌릴 가방·GP(원본은 전투 전 파티 상태로 되돌린다) — fg-21 ⑰.
        _restartInventory = [.. host.StatusScr._inventory];
        _entryFlags = (byte[])host.FlagSt._flags.Clone();   // RESTART 는 진행 깃발도 전투 전으로(0x10064800 · 0x100646d0, ba-20 T1)           // 들어온 차례 그대로(감사3 I4)
        _restartMoney = host.Mos._shopMoney;
        // 파트1 판은 가방 · eld 가 따로(Part1Progress) — 상자 · 행동 503 · 505 로 얻은 것도 RESTART 가 되돌린다.
        _restartPart1 = scene.IsPart1 ? ([.. host.Part1.Bag], host.Part1.Eld) : null;
        // 전투 전 명부·군단도 — 원본 세이브의 전역 본문은 전투 들어가기 직전 파티다(전투 안 값은 판 부분의 복사본, 감사5 S5).
        _entryRoster = host._party.ToDictionary(p => p.Key, p => CopyChar(p.Value));
        _entryLegions = new Dictionary<int, int>(host.Mos._unitLegion);
        _entryOwnedLegions = [.. host.Mos._ownedLegions];
        RestartBattle();
        // 새로 거는 전투면 배치 단계(원본 상태 2) — 불러오기(rememberParty: false)는 저장된 판으로 바로 돌아간다.
        // 파트1 판은 파트2 파티를 안 들고 가도(rememberParty: false) 새로 거는 판이다 — 전투 세이브를 불러온 것(_keepBenchForLoad)만 빼고.
        host.Btl.BeginDeployOrDrop(scene, fresh: rememberParty || (scene.IsPart1 && !host.Btl._keepBenchForLoad));
        host.Toast($"{scene.Title} — 전투 Btl {scene.Id:D4}");
        return true;
    }

    /// <summary>전투에 들어올 때의 가방·GP — RESTART 가 되돌린다.</summary>
    internal List<KeyValuePair<int, int>>? _restartInventory;
    internal int _restartMoney;

    /// <summary>파트1 판에 들어올 때의 가방 · eld — RESTART 가 되돌린다(불러온 판은 기준이 없어 안 되돌린다).</summary>
    internal (List<int> Bag, int Eld)? _restartPart1;

    /// <summary>
    /// 전투에 들어올 때의 명부(Chr → 인물)·군단 배속·가진 군단 — RESTART 가 되돌린다. 전투 세이브에도 실려서 불러온 뒤 RESTART 도
    /// 전투 전 파티로 시작한다(원본 RESTART 결과 7 은 소멸자 되돌리기 <c>0x10064920</c> 를 건너뛰고 전역 본문 = 전투 전 파티로 다시 연다, 감사5 S5).
    /// </summary>
    internal Dictionary<int, CharacterData>? _entryRoster;
    internal Dictionary<int, int>? _entryLegions;
    internal List<int>? _entryOwnedLegions;

    /// <summary>인물 자료 복사 — 배열 칸(아이템·장착·어빌리티)까지 새로 떠서 전투 중 고친 것이 스냅숏에 번지지 않게.</summary>
    internal static CharacterData CopyChar(CharacterData c) =>
        c with { Items = [.. c.Items], Passives = [.. c.Passives], Abilities = [.. c.Abilities] };

    /// <summary>
    /// 시스템 메뉴의 RESTART — 판을 되돌린 뒤 <b>캐릭터 배치부터</b> 다시 한다(사용자 요청: 군단사용·자리를 다시 고르고 싶다).
    /// 배치종료가 안 세운 파티원과 끈 군단의 부하를 지워 버리므로, 전투 전 명부·군단으로 유닛을 새로 만든다. 배치칸이 없는 전투는 전처럼 바로 시작한다.
    /// </summary>
    internal void RestartFromDeploy()
    {
        RestartBattle();                                   // 명부·군단·가방·깃발을 전투 전으로 — 유닛을 새로 만들 바탕
        host._units = host.Btl.BuildUnits(host._scene);
        host.LoadRosterSprites();
        host._statusUnit = -1;
        host.Btl._infoUnit = -1;
        host.Btl._ringUnit = -1;
        RestartBattle();
        host.Btl.BeginDeployOrDrop(host._scene, fresh: true);
    }

    internal void RestartBattle()
    {
        foreach (var unit in host._units) unit.ResetTo(unit.StartCol, unit.StartRow);
        // 물체도 처음으로 — 전에는 RESTART 뒤에도 부순 물체는 부서진 채, 연 상자는 열린 채였다(가방은 전투 전으로 돌아가 상자 아이템이 사라졌다).
        foreach (var obj in host.Btl.Objects)
            (obj.Hp, obj.Team, obj.Charge, obj.Charged) = (obj.Data.MaxHp, obj.Record.Team, 0, false);
        host.Btl._opened.Clear();
        host.Btl._openedAt.Clear();
        host.Btl._closedAt.Clear();
        host.Btl._wokenAt.Clear();
        host.Btl._objGrowth.Clear();
        host.Btl._objGrowthFor = null;
        host.Btl.RestampObjects();
        // 사건 발동 횟수·전투 변수·타이머도 처음으로 — 안 그러면 시작 사건(최대 발동 1)이 RESTART 뒤에 다시 안 터진다.
        host.Btl.LoadEvents(host._scene.Id);
        // 진행 깃발도 전투 전으로 — 원본은 깃발 사본(CBattle+0xa4)에 쓰고 RESTART(결과 7)면 되돌려 적지 않는다.
        // 전에는 시작 사건이 「깃발 += 1」인 Btl 0239·0240(깃발 110)·0179·0180(깃발 208)이 RESTART 마다 한 번씩 더 올랐다.
        if (_entryFlags is { } entryFlags) Array.Copy(entryFlags, host.FlagSt._flags, Math.Min(entryFlags.Length, host.FlagSt._flags.Length));
        // 전투 중에 쓰거나 얻은 아이템·GP 는 전투 전으로(원본 RESTART 는 파티를 통째로 되돌린다).
        if (_restartInventory != null)
        {
            host.StatusScr._inventory.Clear();
            foreach (var (id, n) in _restartInventory) host.StatusScr._inventory[id] = n;
            host.Mos._shopMoney = _restartMoney;
        }
        if (host._scene.IsPart1 && _restartPart1 is { } part1)
        {
            host.Part1.Bag = [.. part1.Bag];
            host.Part1.Eld = part1.Eld;
        }
        // 명부·군단도 전투 전으로 — 이것이 없으면 불러온 뒤 RESTART 가 그 전투에서 얻은 EXP·레벨·상자 아이템을 들고 다시 시작했다(감사5 S5).
        if (_entryRoster != null)
            foreach (var (chr, c) in _entryRoster) host._party[chr] = CopyChar(c);
        if (_entryLegions != null)
        {
            host.Mos._unitLegion.Clear();
            foreach (var (chr, legion) in _entryLegions) host.Mos._unitLegion[chr] = legion;
        }
        if (_entryOwnedLegions != null)
        {
            host.Mos._ownedLegions.Clear();
            foreach (int id in _entryOwnedLegions) host.Mos._ownedLegions.Add(id);
        }
        // 가방은 챕터 스크립트가 채운 것이 옳다 — 그것이 있으면 비우지도, 데모 아이템으로 덮지도 않는다.
        if (host.Fld._chapterFired.Count == 0)
        {
            host.StatusScr._inventory.Clear();
            host.Btl.InitBattle();
            host.StatusScr.FillDemoInventory();
        }
        else host.Btl.InitBattle();
        // 원본은 전투를 만들 때 이 값을 <b>1</b> 로 놓는다(0x100644d4) — 0 이면 표시가 늘 하나씩 작다.
        host.Btl._tick = 1;
        host.Btl._turn = -1;
        host._selected = -1;
        host.Btl._outcome = "";
        host.Stats.Reset();
        host.Btl._eventCheckDue = 0xF;
        host.Btl._objectsDue = false;
        host.Btl._outcomeAfterLevelUp = false;
        host.Btl._routine = null;
        host.Btl._levelUpQueue.Clear();
        host.Btl._formationAt.Clear(); host.Btl._followerTarget.Clear(); host.Btl._nextFollowerRetry.Clear();   // 앞 판의 진형 기록(fg-31)
        host.Btl._levelUpUnit = -1;
        host.Btl._numbers.Clear();
        host.Btl._effects.Clear();
        host.Btl._delayedHits.Clear();
        host.Mov._movies.Clear();
        host.Btl._bodyClones.Clear();  host.Btl._bodyShapes.Clear(); host.Btl._bodyShots.Clear();
        host.Btl._blinks.Clear();
        host.Btl._pendingExits.Clear();
        host.Btl._exitOutcomeDue = false;
        host.Btl._brokenAt.Clear();
        host.Btl._objActing.Clear();
        host.Btl._objectRoutine = false;
        host.Btl._blinkGhosts.Clear();
        host.LegionStageAb._legionGhosts.Clear();
        host.LegionStageAb._legionFades.Clear();
        host.UnitFxAb._unitFx.Clear();
        host.LegionStageAb._legionLater.Clear();
        host.LegionStageAb._legionStageEnd = 0;
        host.StagingAb._stageDraws.Clear();
        host.Mos._planetZoomAt = -1;
        host.Btl._flyingEffects.Clear();
        host.Btl._shots.Clear();
        host.Btl._movers.Clear(); host.Btl._fallers.Clear(); host.Btl._ringTrails.Clear(); host.Btl._chasers.Clear(); host.Btl._scripted.Clear(); host.Btl._pieces.Clear(); host.Btl._cheer.Clear();
        host.Btl._fxLatestStart = 0;
        host.Btl._effectMirrors.Clear();
        host.NineCrusaderAb._swords.Clear();
        host.FinisherPreludeAb._preludeDots.Clear();
        host.FinisherPreludeAb._fxFlights.Clear();
        host.HeavenEarthAb._timedFx.Clear();
        host.HeavenEarthAb._debris.Clear();
        host.HeavenEarthAb._shakes.Clear();
        host.AcrostAb._fireBalls.Clear();
        host.FxLine.Clear();
        host.PushAb._thrown = null;
        host.Mov._ripples.Clear();
        host.Btl._nextTickAt = 0;
        host.Btl.CancelTargeting();
        host.StartBattleMusic();
    }

    // ── 저장 · 불러오기 ──────────────────────────────────────────────────────

    internal sealed record SaveAbility(int Id, int Level);

    /// <param name="Char">
    /// 인물 레코드의 나머지 — 직업(전직·세부 체질)·체질·그림·이름·칭호·WEAPON 띠와 레벨업으로 오른 능력치.
    /// 예전 세이브에는 없어서 불러오면 .chr 처음 값으로 돌아갔다 — 공격형으로 바꾼 체질이 일반형으로, 오른 LP·TP 가 처음 값으로(사용자 보고).
    /// </param>
    internal sealed record SaveUnit(int ChrCode, int Col, int Row, int Facing, int Hp, int Tp, int Soul,
                                   bool Alive, bool HasTurn, int Level, int CumExp, int Exp,
                                   ushort[] Items, ushort[] Passives, SaveAbility[] Abilities,
                                   byte[]? StatusId = null, short[]? StatusValue = null, int Side = -1, SaveChar? Char = null,
                                   bool? OnField = null, int[]? Bonus = null, int Stance = 0, bool? Awake = null, int LastHitBy = -1,
                                   int? LeaderIndex = null, int? FormationSlot = null, int? LegionId = null, int? LegionPower = null,
                                   int? OriginCol = null, int? OriginRow = null, bool? Detached = null,
                                   int? StartCol = null, int? StartRow = null, int[]? StatusBy = null, int? StartFacing = null,
                                   int? P1Heat = null, int[]? P1Ailments = null, int[]? P1Buffs = null, int? P1Enchant = null, int? P1Stance = null);

    // P1 칸 = 파트1 전투 유닛 상태 — 원본 유닛 기록(읽기 0x100340b0)이 온도 +0x4a(바탕 0x10099980)와 상태 바이트 +0x246~+0x25c 를 싣는다.
    // P1Heat 온도, P1Ailments 동결 1 · 쇼크 2 · 최면 14 · 컨디션이상 15 · 혼란 16, P1Buffs (세기 식 · 남은 길이) 짝(집중 1022 · 아이스 실드 14 …),
    // P1Enchant 속성 부여 1~3, P1Stance 자세 76~80. 없으면(옛 세이브 · 파트2) 예전처럼 80 · 모두 풀린 채.

    // StartCol·StartRow = 배치 단계가 옮긴 처음 자리(불러온 뒤 RESTART 가 Btl 기본 줄로 돌아가지 않게), StatusBy = 상태를 건 유닛의 자리 번호
    // (매 턴 피해로 쓰러뜨렸을 때 EXP 를 받을 사람 — 불러오면 사라졌다). ba-21 outer-rules 7.

    // OriginCol·OriginRow = 기준 칸 +0x4b8/+0x4ba(원본 유닛 기록 0x1007c710 이 차례 시작 TP +0x4da 와 함께 적는다, ba-15 Q5).
    // 걸음 비용은 행동할 때 한꺼번에 빠지므로(CommitMove) 기준 칸이 없으면 걸은 뒤 저장·불러오기로 걸음이 공짜가 됐다(감사5 S2).

    // 위 꼬리 칸(형식 10 에 덧붙임, 없으면 옛 세이브라 예전처럼 둔다) — 원본 유닛 기록 0x1007c710 이 적는 것(ba-15 Q5):
    // Bonus = 전투 보정 +0x4c8 DEX · +0x4ca PSY · +0x4cc DEP · +0x4d0 최대TP · +0x4d2 최대SOUL · +0x4ce 최대HP(이 차례로),
    // Stance = 자세 +0x4d4, Awake = 깨어남 +0x4e8, LastHitBy = 마지막 때린 자 +0xfc(자리 번호, 없으면 −1),
    // LeaderIndex·FormationSlot·LegionId·LegionPower = 군단 +0x4ea~+0x4fd 와 CChr 세력 — 대장이 죽어 물려받은 뒤를 되살린다.

    /// <summary>
    /// 물체 하나의 세이브 칸 — 원본 물체 기록 <c>0x100e80f0</c>(편 <c>+0x78</c>, 부서짐/열림 <c>+0x101</c>, HP <c>+0x13c~</c>, 충전 <c>+0x15c</c>·다 참 <c>+0x160</c>).
    /// 안 적으면 불러올 때 판을 새로 세워 부순 물체·연 상자가 되살아나 상자를 또 열 수 있었다(ba-15 Q5 #1).
    /// </summary>
    internal sealed record SaveObject(int Index, int No, int Hp, int Team, bool Opened, int Charge, bool Charged);

    /// <summary>인물 레코드에서 세이브가 따로 적는 칸들(<see cref="SaveUnit.Char"/>).</summary>
    internal sealed record SaveChar(ushort NameId, ushort Name2Id, ushort SpriteId, ushort FaceId, ushort TitleId, byte Body, ushort JobId,
                                   ushort BasicWorkId, uint Lp, ushort Psy, ushort Tp, ushort TpDivisor, ushort Ctp, ushort Dep, ushort Dex,
                                   byte WeaponBand, byte WeaponType, ushort? VoiceSet = null);

    internal static SaveChar? SaveCharOf(CharacterData? c) => c == null ? null
        : new SaveChar(c.NameId, c.Name2Id, c.SpriteId, c.FaceId, c.TitleId, c.Body, c.JobId, c.BasicWorkId,
                       c.Lp, c.Psy, c.Tp, c.TpDivisor, c.Ctp, c.Dep, c.Dex, c.WeaponBand, c.WeaponType, c.VoiceSet);

    /// <summary>
    /// 세이브의 인물 칸을 바탕 자료 위에 되살린다. <paramref name="regrow"/> 면 인물 레코드가 안 적힌 옛 세이브에서
    /// 레벨업으로 오른 능력치를 <b>쌓인 경험치로 다시 키운다</b>(직업은 옛 세이브에 없어 못 되살린다).
    /// </summary>
    internal CharacterData Restored(CharacterData baseData, SaveUnit s, bool regrow)
    {
        var c = baseData with
        {
            CumExp = s.CumExp, Exp = s.Exp,
            Items = s.Items.Length == baseData.Items.Length ? s.Items : baseData.Items,
            Passives = s.Passives.Length == 3 ? s.Passives : baseData.Passives,
            // 파트1 인물의 어빌리티는 진행(직업 · 배운 레벨 · 장비)에서 늘 새로 만든다 — 세이브에 적힌 목록을 쓰면 그 뒤에 배운 기술(혼 따위)이
            // 그 세이브를 불러온 판 내내 안 나왔다(사용자 보고). 진행은 판을 세우기 전에 되살린다(LoadBattle → Part1.FromJson).
            Abilities = baseData.Code >= Part1Game.ChrBase ? baseData.Abilities : [.. s.Abilities.Select(a => ((ushort)a.Id, (ushort)a.Level))],
        };
        // 형식 9 이하의 능력치는 복리로 불어난 옛 레벨업 식으로 쌓인 것이라 믿지 않는다 — 아군은 아래에서 다시 센다.
        bool trustStats = _restoreVersion >= 10;
        if (s.Char is { } k)
        {
            c = c with
            {
                NameId = k.NameId, Name2Id = k.Name2Id, SpriteId = k.SpriteId, FaceId = Part1Game.FixFace(k.FaceId), TitleId = k.TitleId, Body = k.Body,
                JobId = k.JobId, BasicWorkId = k.BasicWorkId, TpDivisor = k.TpDivisor, WeaponBand = k.WeaponBand, WeaponType = k.WeaponType,
                VoiceSet = k.VoiceSet ?? baseData.VoiceSet,     // 캐릭터 에디터 「외형」이 목소리도 바꾼다 — 옛 세이브에는 칸이 없다
            };
            if (trustStats) c = c with { Lp = k.Lp, Psy = k.Psy, Tp = k.Tp, Ctp = k.Ctp, Dep = k.Dep, Dex = k.Dex };
        }
        if (regrow && (s.Char == null || !trustStats) && host._db?.Character(c.Code) is { } b)
        {
            // .chr 처음 능력치에서 쌓인 경험치만큼 원본 식(기본값 기준, 레벨마다 같은 양)으로 다시 키운다.
            // 스크립트 702 가 바꾼 능력치는 여기서 되살리지 못한다(드물다).
            c = host._db.LevelUp(c with { Lp = b.Lp, Psy = b.Psy, Tp = b.Tp, Ctp = b.Ctp, Dep = b.Dep, Dex = b.Dex, Level = b.Level }, out _)
                with { CumExp = s.CumExp };
        }
        return c with { Level = (ushort)s.Level };
    }

    /// <summary>세이브 머리 — 원본처럼 <b>저장할 때 장면 이름 TXR·장면 갈래·논 시간</b>을 함께 적는다(분석-시스템메뉴 2.1b).</summary>
    /// <param name="Flags">
    /// 진행 깃발 — 「어느 장소가 열렸나」를 정하는 값이라 <b>저장에 반드시 들어가야 한다</b>(원본도 2000바이트를 통째로 적는다, <c>0x1004d9de</c>).
    /// 0 이 아닌 칸만 (번호 → 값) 으로 적는다.
    /// </param>
    internal sealed record SaveState(int Version, string SavedAt, int Tick, int Turn, SaveUnit[] Units, Dictionary<string, int> Inventory,
                                    int SceneText = 0, int SceneKind = 1, long PlayMs = 0,
                                    int Money = 0, Dictionary<string, int>? Legions = null, int Battle = 0,
                                    Dictionary<string, int>? Flags = null,
                                    string[]? DonePlaces = null, int[]? DoneChapters = null,
                                    string[]? DoneEvents = null, string[]? UsedPlaces = null,
                                    int[]? EventFired = null, int TurnNo = 0, Dictionary<string, int>? BattleVars = null,
                                    int[]? EventTimer = null, bool[]? EventTimerRun = null,
                                    int EventNextBattle = 0, int EventNextField = 0,
                                    bool InMoses = false, int Chapter = 0,
                                    bool ChapterDone = false, int PartyNo = 0, int[]? Members = null,
                                    SaveUnit[]? Party = null, int[]? OwnedLegions = null, PartyStore.SaveParty[]? Bank = null,
                                    int[]? Mailbox = null, int[]? MailRead = null, string[]? PlanetVisits = null,
                                    Dictionary<string, int>? ChapterVars = null, int CurrentChapter = 0,
                                    int[]? EpisodesPicked = null,
                                    SaveObject[]? Objects = null, int FoundA = -1, int FoundB = -1, bool ObjectsDue = false,
                                    int[]? NavStart = null, int[]? NavAt = null,
                                    SaveEntry? Entry = null, int[]? Camera = null, int? EventCheckDue = null,
                                    bool MosesAltVoice = false,    // 행동 910 의 깃발([챕터+0x2ec4]) — 원본도 진행 세이브에 싣는다
                                    int? Bgm = null,
                                    string? Part1 = null,
                                    int[]? Part1Objects = null,    // 파트1 맵 물체마다 (동작, 장, 찍힌 장, 높이, 기준 x, 기준 y) — 행동 707 로 연 벽이 불러온 뒤 다시 닫히지 않게
                                    int[]? Part1Range = null,      // 파트1 전장 범위(행동 103 — 왼, 위, 오른, 아래) — 시작 사건이 정한 범위가 불러온 뒤 맵 전체로 풀리지 않게
                                    int? Part1Ambient = null,      // 파트1 기온(행동 102 가 바꾼 것, 안 바꿨으면 null) — 원본 전투 세이브의 +0xb96e(0x10033625)
                                    int[]? Part1Lingering = null,  // 파트1 칸의 기운(땅불 · 얼음 덩이)마다 (열, 줄, 알림, 모션, 놓은 지, 남은 틱) — 원본 맵 층 저장(0x100901c0)
                                    int[]? Part1Energy = null,     // 파트1 에너지 필드 기운마다 (열, 줄, 모션, 깐 지) — 같은 맵 층 저장의 기술 152~160 갈래
                                    string? SceneTitle = null,
                                    int[]? Part1Riders = null,     // 파트1 탈것 짝마다 (탈것 자리, 탄 이 자리, 무리 탈것 1/0) — 원본 유닛 기록의 +0x1cc · +0x1d0 · +0x1c0
                                    int[]? Part1LostMounts = null);   // 이 판에서 쓰러진 무리 탈것(Chr) — 전투가 끝날 때 무리에서 뺀다(SettlePart1Losses)    // 장면 이름 글 그대로 — 파트1 이름 번호(64000~)는 켤 때마다 새로 매겨져 SceneText 로는 나중에 못 찾는다(사용자 보고: 불러오기 목록에 이름이 안 뜸)
                                                                   // ↑ Part1: 파트1 전투면 파트1 진행 전부(Part1Progress.ToJson) — 불러오면 되살리고 전투가 끝나면 그 챕터로              // 사건(행동 512)이 바꾼 전투 곡 — 원본 전투 세이브는 곡 번호(CBattle+0x3c64)를 싣는다(0x1006f051 · 0x1006f42e, ba-22)

    // 꼬리 셋(없으면 옛 세이브라 예전처럼):
    // Entry = 전투 들어가기 직전 가방·GP·명부·군단 — 원본 전투 세이브의 전역 본문(판 부분과 따로, 분석-시스템메뉴 2.3/2.4b). RESTART 기준(감사5 S5).
    // Camera = 스크롤 CBattle +0x3cae/+0x3cb0(0x1006efe0 적기 · 0x1006f3a0 읽기, 감사5 S7).
    // EventCheckDue = 아직 안 본 이벤트 갈래 깃발(감사5 S6).

    /// <summary>전투 전 스냅숏(<see cref="SaveState.Entry"/>).</summary>
    internal sealed record SaveEntry(Dictionary<string, int> Inventory, int Money, SaveUnit[] Roster,
                                    Dictionary<string, int>? Legions = null, int[]? OwnedLegions = null,
                                    Dictionary<string, int>? Flags = null);   // 전투 전 진행 깃발(0 이 아닌 것만) — 불러온 뒤 RESTART 의 기준

    internal const int SaveVersion = 10;  // 9: 메일을 챕터 메일 표로 배달한다 — 8 이하는 불러올 때 우편함을 걷어 낸다
                                         // 10: 레벨업 성장을 원본대로(기본값 기준) — 9 이하는 불러올 때 아군 능력치를 다시 셈한다

    /// <summary>지금 불러오는 세이브의 형식 — <see cref="Restored"/> 가 옛 형식의 능력치를 다시 셀지 정한다.</summary>
    internal int _restoreVersion = SaveVersion;

    /// <summary>
    /// 판을 세우기 전에 되살려야 하는 것 — <b>파티·동료·깃발·군단·돈</b>.
    /// </summary>
    /// <remarks>
    /// 아군을 미리 안 세운 전투는 <see cref="BuildUnits"/> 가 배치 칸에 <c>_members ∩ _party</c> 를 세운다.
    /// 그래서 이것들은 <see cref="StartBattle"/> <b>앞</b>에서 채워야 한다 — 뒤에 채우면 타이틀에서 불러왔을 때
    /// 둘 다 비어 기본 파티가 서고, 저장 당시 전투에 있던 인물(크리스티앙)이 빠진다.
    /// 전투에 <b>서 있던</b> 아군은 <c>state.Party</c> 가 아니라 <c>state.Units</c> 에 적히므로 거기서도 파티를 채운다.
    /// </remarks>
    internal void RestorePartyBeforeBoard(SaveState state)
    {
        host.StatusScr._inventory.Clear();
        foreach (var (id, count) in state.Inventory)
            if (int.TryParse(id, out int itemId)) host.StatusScr._inventory[itemId] = count;

        host.Mos._shopMoney = state.Money;
        host.Mos._unitLegion.Clear();
        foreach (var (index, legion) in state.Legions ?? [])
            if (int.TryParse(index, out int chrCode)) host.Mos._unitLegion[chrCode] = legion;   // Chr 번호 → 군단(옛 세이브의 자리 번호는 그냥 안 맞는다)

        host.EpisodesScr._chapterDone = state.ChapterDone;
        host.Mos._mosesAltVoice = state.MosesAltVoice;
        host.Mos._mosesAltVoiceLoaded = state.MosesAltVoice && state.InMoses;   // 전투 세이브면 OpenMoses 를 안 거친다 — 다음 챕터로 새지 않게
        host.EpisodesScr._partyNo = state.PartyNo;
        // 파티 번호가 없던 옛 세이브 — 모세스에서 저장한 챕터의 주인 파티(Episode.dat 칸 8)로 맞춘다. 안 맞추면 OpenMoses 의 파티 바꾸기가
        // 지금 인원을 은행으로 치워 버린다.
        if (state.InMoses && state.Chapter > 0 && host.EpisodesScr.Episodes().FirstOrDefault(e => e.Chapter == state.Chapter) is { } owner) host.EpisodesScr._partyNo = owner.Party;
        host.Mos._members.Clear();
        foreach (int chr in state.Members ?? []) host.Mos._members.Add(chr);

        // 진행 깃발 — 어느 장소가 열렸는지가 여기 담긴다.
        Array.Clear(host.FlagSt._flags);
        foreach (var (number, value) in state.Flags ?? [])
            if (int.TryParse(number, out int flag) && (uint)flag < host.FlagSt._flags.Length) host.FlagSt._flags[flag] = (byte)Math.Clamp(value, 0, 255);

        host.Mos._autoPlacesDone.Clear();
        foreach (string pair in state.DonePlaces ?? [])
            if (pair.Split(':') is [var a, var b] && int.TryParse(a, out int chapter) && int.TryParse(b, out int place))
                host.Mos._autoPlacesDone.Add((chapter, place));
        host.Fld._chapterFired.Clear();
        foreach (string triple in state.DoneEvents ?? [])
            if (triple.Split(':') is [var c, var e, var n] && int.TryParse(c, out int chp)
                && int.TryParse(e, out int ev) && int.TryParse(n, out int count))
                host.Fld._chapterFired[(chp, ev)] = count;
        // 사건별 횟수가 없던 옛 세이브는 「그 챕터는 다 돌았다」로만 안다 — 사건 −1 에 표시를 남긴다.
        if (state.DoneEvents == null)
            foreach (int chapter in state.DoneChapters ?? []) host.Fld._chapterFired[(chapter, -1)] = 1;
        // 동료 목록이 없는 옛 세이브 — 이미 돌린 챕터 스크립트의 801/802 로 되살린다(크리스티앙이 빠지고 전투의 제이슨·스턴이
        // 동료로 보이던 문제). 필드 스크립트의 801 은 어느 사건이 돌았는지 안 남아 못 되살린다.
        // 목록이 비어 있어도(고치기 전 판이 빈 목록을 적은 세이브) 되살리고, 적힌 목록과 합친다.
        var saved = host.Mos._members.ToList();
        RebuildMembersFromChapters();
        foreach (int chr in saved) host.Mos._members.Add(chr);
        host.Mos._ownedLegions.Clear();
        foreach (int id in state.OwnedLegions ?? []) host.Mos._ownedLegions.Add(id);
        host.Mos._legionsKnown = state.OwnedLegions != null;
        host.PartySt.RestoreBank(state.Bank);
        host.Mos._mailbox.Clear();
        foreach (int id in state.Mailbox ?? []) host.Mos._mailbox.Add(id);
        host.Mos._mailRead.Clear();
        foreach (int id in state.MailRead ?? []) host.Mos._mailRead.Add(id);
        if (state.Version < 9)
            host.Mos.PruneLegacyMailbox(host.Fld._chapterFired.Keys.Select(k => k.Chapter).Concat(state.DoneChapters ?? [])
                                .Append(state.CurrentChapter).Append(state.Chapter).Where(c => c > 0));
        Array.Clear(host.Fld._chapterVars);
        foreach (var (number, value) in state.ChapterVars ?? [])
            if (int.TryParse(number, out int slot) && (uint)slot < host.Fld._chapterVars.Length) host.Fld._chapterVars[slot] = (byte)Math.Clamp(value, 0, 255);
        host.Mos._planetVisits.Clear();
        host.EpisodesScr._episodesPicked.Clear();
        if (state.EpisodesPicked is { } picked) foreach (int no in picked) host.EpisodesScr._episodesPicked.Add(no);
        else
            // 표시를 안 적던 옛 세이브 — 챕터 사건이 한 번이라도 돈 에피소드와 지금 챕터의 에피소드를 고른 것으로 본다.
            foreach (var ep in host.EpisodesScr.Episodes())
                if (host.Fld._chapterFired.Keys.Any(k => k.Chapter == ep.Chapter) || ep.Chapter == state.CurrentChapter) host.EpisodesScr._episodesPicked.Add(ep.No);
        foreach (string pair in state.PlanetVisits ?? [])
            if (pair.Split(':') is [var a, var b] && int.TryParse(a, out int pc) && int.TryParse(b, out int pn)) host.Mos._planetVisits.Add((pc, pn));
        host.Mos._placesUsed.Clear();
        foreach (string pair in state.UsedPlaces ?? [])
            if (pair.Split(':') is [var a, var b] && int.TryParse(a, out int chapter) && int.TryParse(b, out int place))
                host.Mos._placesUsed.Add((chapter, place));
        // 인물 명부(원본 0x101b6884 — 파티와 무관한 전 인물)를 세이브 값으로 새로 채운다. 전에 하던 판의 인물이 남지 않게 비우고 시작한다.
        host._party.Clear();
        // 전투에 서 있던 아군도 명부에 넣는다 — 이것이 없으면 배치 칸 고르기가 그 인물을 못 본다.
        // 편을 안 적던 옛 세이브는 −1 이라 아군·적군을 못 가린다 — 그때는 넣지 않는다(적이 파티에 들어가느니 예전대로).
        // 군단 부하는 명부에 넣지 않는다 — 부하의 적힌 레벨은 절반 값이라, 명부에 들어가면 다음 전투가 그것을 「이어받은 인물」로 보고
        // 파티 레벨로 안 키운 채 다시 절반으로 깎았다(불러올 때마다 9 → 4 → 2 로 줄었다 — 사용자 보고: 대장 21 에 용병 3~4).
        // 레벨 0 은 자료 없는 유닛(NEW GAME 이 비운 앞 판 유닛)을 적은 것이다 — 넣으면 살라딘이 레벨 0 · 어빌리티 없이 돌아왔다(사용자 보고).
        // 빼 두면 아래 동료 목록이 .chr 처음 값으로 채운다.
        foreach (var s in state.Units)
            if (s.Side == 4 && (s.LeaderIndex ?? -1) < 0 && s.Level > 0 && host._db?.Character(s.ChrCode) is { } bc)
                host._party[s.ChrCode] = Restored(bc, s, regrow: true);
        // 전투 밖 인물 — 이제는 명부 전체(어느 파티에 있든, 파티에서 빠졌든)가 여기 실린다. 옛 세이브는 지금 파티 사람만 적혀 있다.
        foreach (var s in state.Party ?? [])
            if (host._db?.Character(s.ChrCode) is { } pc)
                host._party[s.ChrCode] = Restored(pc, s, regrow: true);
        ReturnStrayMembers();                // 다른 파티 사람은 제 파티로(파티를 안 가리던 옛 판의 세이브)
        host.PartySt.MergeLegacyBankCopies();             // 파티마다 인물 사본을 들던 옛 세이브(은행 Units)를 명부 하나로 합친다
        foreach (int chr in host.Mos._members)
            if (!host._party.ContainsKey(chr) && host._db?.Character(chr) is { } fresh) host._party[chr] = fresh;
        // 항행 시작(911) — 옛 세이브는 없다(null). 모세스 세이브는 OpenMoses(챕터) 가 지우므로 거기서 한 번 더 넣는다(감사 R2).
        host.Mos._navStart = state.NavStart is [var navChp, var navStep, var navNo] ? (navChp, navStep, navNo) : null;
    }

    /// <summary>
    /// 다른 파티 사람이 지금 파티에 끼어 있으면 제 파티(은행)로 돌려보낸다 — 위 버그로 적힌 세이브를 고친다.
    /// 끼어 있던 동안 더 자랐을 수 있으니 누적 경험치가 큰 쪽 자료를 명부에 남긴다.
    /// </summary>
    internal void ReturnStrayMembers()
    {
        foreach (var (no, bank) in host.PartySt._partyBank)
            foreach (int chr in bank.Members.Where(host.Mos._members.Contains).ToList())
            {
                host.Mos._members.Remove(chr);
                if (bank.Party.Remove(chr, out var there) && (!host._party.TryGetValue(chr, out var here) || there.CumExp > here.CumExp))
                    host._party[chr] = there;
            }
    }

    /// <summary>돌린 챕터 사건의 801(동료 넣기)·802(빼기)로 동료 목록을 다시 만든다 — 사건 −1 표시(옛 세이브)는 그 챕터 사건 전부로 본다.</summary>
    internal void RebuildMembersFromChapters()
    {
        host.Mos._members.Clear();
        // 사건 차례대로 801 이 넣고 802 가 뺀다 — 챕터 스크립트의 조건(깃발)을 채워 돈 사건만 본다.
        foreach (var group in host.Fld._chapterFired.Where(f => f.Value > 0).GroupBy(f => f.Key.Chapter))
        {
            if (MosesScene.LoadChapterFile(group.Key) is not { } chp) continue;
            bool whole = group.Any(f => f.Key.Event == -1);
            for (int ev = 0; ev < chp.Events.Count; ev++)
            {
                if (!whole && !group.Any(f => f.Key.Event == ev)) continue;
                foreach (var a in chp.Events[ev].Actions)
                {
                    if (a.Args.Length < 2 || a.Args[1] <= 0) continue;
                    // 인자0 은 <b>파티 번호</b>다 — 지금 파티 것만 센다. 전에는 파티를 안 가려서, 베라모드 파티(1)로 불러오면
                    // 챕터 10·11 이 파티 0 에 넣은 살라딘·죠안·크리스티앙까지 끼어들었다(사용자 보고, Chp 0019 전직 화면).
                    if (a.Args[0] != host.EpisodesScr._partyNo) continue;
                    if (a.Code == 801) host.Mos._members.Add(a.Args[1]);
                    else if (a.Code == 802) host.Mos._members.Remove(a.Args[1]);
                }
            }
        }
    }

    /// <summary>
    /// 세이브가 가리키는 챕터 — 적혀 있으면 그것, 모세스 세이브면 그 챕터. 챕터를 안 적던 옛 전투 세이브는
    /// <b>마지막으로 다녀온 장소의 챕터</b>로 본다(장소는 다녀온 차례대로 적힌다). 모르면 0.
    /// </summary>
    internal static int SavedChapter(SaveState state)
    {
        if (state.CurrentChapter > 0) return state.CurrentChapter;
        if (state.InMoses && state.Chapter > 0) return state.Chapter;
        foreach (var list in new[] { state.UsedPlaces, state.DonePlaces })
            if (list is { Length: > 0 } && list[^1].Split(':') is [var a, _] && int.TryParse(a, out int chapter)) return chapter;
        return 0;
    }

    /// <summary>읽을 수 있는 가장 오래된 저장 형식 — 빠진 칸은 기본값으로 채운다(형식이 바뀌어도 옛 저장을 버리지 않는다).</summary>
    internal const int OldestSaveVersion = 2;

    /// <summary>불러온 판을 이어 세는 논 시간 바탕(밀리초).</summary>
    internal double _playBase;

    internal long PlayMs => (long)(host._realTime * 1000 + _playBase);   // 실제 시간 — 게임 속도를 올려도 플레이 시간은 제대로 흐른다

    /// <summary>연대표 장면의 이름 TXR — 원본 상수 2557(빈 글, 장면 7 vt+0x1c <c>0x10106420</c>).</summary>
    internal const int EpisodesSceneText = 2557;

    internal static readonly JsonSerializerOptions SaveJson = new() { WriteIndented = true };

    /// <summary>판 밖 인물 한 명의 세이브 칸(자리·HP 없음) — 명부·전투 전 스냅숏에 쓴다.</summary>
    internal static SaveUnit PartyUnit(int chr, CharacterData c) =>
        new(chr, 0, 0, 0, 0, 0, 0, true, false, c.Level, c.CumExp, c.Exp, c.Items, c.Passives,
            [.. c.Abilities.Select(a => new SaveAbility(a.Ability, a.Level))], Char: SaveCharOf(c));

    /// <summary>전투판을 그 파일에 적는다. 적었으면 true.</summary>
    internal bool SaveBattleTo(string path)
    {
        if (host.Part1TitleScr.TimelineOpen) return SavePart1ChapterTo(path, 7);
        if (host.Part1TitleScr.ChapterMapOpen) return SavePart1ChapterTo(path, 4);
        bool battleSave = !InChapterScene && !host.TitleScr._titleOpen && !host.RecordsScr._recordsOpen && host._battleLoaded;
        try
        {
            var state = new SaveState(SaveVersion, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), host.Btl._tick, host.Btl._turn,
                [.. host._units.Select(u => new SaveUnit(u.ChrCode, u.Col, u.Row, (int)u.Facing, u.Hp, u.Tp, u.Soul, u.Alive, u.HasTurn,
                    u.Data?.Level ?? 0, u.Data?.CumExp ?? 0, u.Data?.Exp ?? 0,
                    u.Data?.Items ?? [], u.Data?.Passives ?? [],
                    [.. (u.Data?.Abilities ?? []).Select(a => new SaveAbility(a.Ability, a.Level))],
                    [.. u.StatusId], [.. u.StatusValue], u.Side, SaveCharOf(u.Data), u.OnField,   // 편도 적는다 — 이벤트 708 로 넘어온 사람이 불러오면 적으로 돌아가지 않게
                    [u.BonusDex, u.BonusPsy, u.BonusDep, u.BonusMaxTp, u.BonusMaxSoul, u.BonusMaxHp], u.Stance, u.Awake,
                    u.LastHitBy is { } hitter ? Array.IndexOf(host._units, hitter) : -1,
                    u.LeaderIndex, u.FormationSlot, u.LegionId, u.LegionPowerPercent, u.OriginCol, u.OriginRow, u.Detached ? true : null,
                    u.StartCol, u.StartRow, [.. u.StatusSource.Select(src => src is { } by ? Array.IndexOf(host._units, by) : -1)],
                    (int)u.StartFacing)).Select((s, i) => WithPart1(s, host._units[i], battleSave))],
                host.StatusScr._inventory.ToDictionary(p => p.Key.ToString(), p => p.Value),
                // 챕터 안이면 장면 갈래 4(챕터)·챕터 제목으로 적고, 불러올 때 그 챕터로 돌아간다(원본 세이브 머리와 같다).
                // 모세스 주 화면뿐 아니라 <b>필드·연대표</b>도 챕터 안이다 — 거기서 저장하면 마지막 전투 이름이 적혀
                // 샤이닝 스타 챕터인데 「코어헌터」로 보였다(사용자 보고).
                // 연대표에서 저장하면 원본처럼 갈래 7 「[NN:연대표]」 + 이름 TXR 2557(빈 글, 0x10106420) — 앞 챕터 이름이 뜨던 것(사용자 보고).
                host.EpisodesScr._episodesOpen ? EpisodesSceneText
                : InChapterScene && host.Mos._mosesChp is { } savedChp ? savedChp.TitleText : host._scene.TitleTextId,
                host.EpisodesScr._episodesOpen ? 7 : InChapterScene && host.Mos._mosesChp != null ? 4 : 1, PlayMs,
                host.Mos._shopMoney, host.Mos._unitLegion.ToDictionary(p => p.Key.ToString(), p => p.Value), host._scene.Id,
                Enumerable.Range(0, host.FlagSt._flags.Length).Where(i => host.FlagSt._flags[i] != 0)
                          .ToDictionary(i => i.ToString(), i => (int)host.FlagSt._flags[i]),
                // 이미 겪은 자동 발생 장소와 이미 돌린 챕터 사건 — 안 적으면 불러올 때마다 프롤로그가 되풀이되고
                // 챕터 사건이 동료·돈을 두 번 준다. 다녀온 장소도 적어야 목록에 되살아나지 않는다.
                [.. host.Mos._autoPlacesDone.Select(p => $"{p.Chapter}:{p.Place}")],
                [.. host.Fld._chapterFired.Keys.Select(k => k.Chapter).Distinct()],
                [.. host.Fld._chapterFired.Select(p => $"{p.Key.Chapter}:{p.Key.Event}:{p.Value}")],
                [.. host.Mos._placesUsed.Select(p => $"{p.Chapter}:{p.Place}")],
                // 전투 이벤트 상태 — 안 적으면 불러올 때마다 시작 대사(조건 0·1)가 다시 뜨고 타이머·국소 변수가 처음으로 돌아간다.
                // 원본도 전투판을 통째로 저장하고 불러오면 저장했던 차례로 돌아온다(분석-시스템메뉴 2.4b) — 사건 횟수도 같이 싣는다.
                // 차례 도중이면 불러올 때 그 차례를 이어 받으며 턴 수가 하나 오르니 미리 뺀다(타이머는 이어 받을 때 안 센다).
                [.. host.Btl._eventFired], host.Btl._turn >= 0 ? host.Btl._turnNo - 1 : host.Btl._turnNo,
                Enumerable.Range(0, host.Btl._battleVars.Length).Where(i => host.Btl._battleVars[i] != 0)
                          .ToDictionary(i => i.ToString(), i => (int)host.Btl._battleVars[i]),
                [.. host.Btl._eventTimer], [.. host.Btl._eventTimerRun], host.Btl._eventNextBattle, host.Btl._eventNextField,
                // 필드·연대표에서 저장해도 챕터로 돌아가게 적는다 — 그때 전투 번호로 돌아가면 엉뚱한 옛 전투가 열린다.
                InChapterScene && host.Mos._mosesChp != null, InChapterScene ? host.Mos._mosesChp?.Id ?? 0 : 0,
                host.EpisodesScr._chapterDone, host.EpisodesScr._partyNo, [.. host.Mos._members],
                // 전투에 안 선 인물의 레벨·장비·어빌리티 — 안 적으면 불러올 때 사라진다. _party 는 이제 전역 명부(원본 0x101b6884,
                // 감사 F7·R4 G1)라 다른 파티 사람과 파티에서 빠진 사람까지 여기 다 실린다(은행에는 인원 번호만).
                // 자료 없는 유닛(NEW GAME 이 비운 앞 판 유닛)은 빼고 본다 — 그것이 명부의 같은 인물을 가려 레벨 0 · 어빌리티 없이 적혔다(사용자 보고).
                [.. host._party.Where(p => !host._units.Any(u => u.ChrCode == p.Key && u.Data != null)).Select(p => PartyUnit(p.Key, p.Value))],
                [.. host.Mos._ownedLegions], host.PartySt.SaveBank(),
                [.. host.Mos._mailbox], [.. host.Mos._mailRead], [.. host.Mos._planetVisits.Select(v => $"{v.Chapter}:{v.Planet}")],
                Enumerable.Range(0, host.Fld._chapterVars.Length).Where(i => host.Fld._chapterVars[i] != 0).ToDictionary(i => i.ToString(), i => (int)host.Fld._chapterVars[i]),
                // 전투·필드 한가운데서 저장해도 <b>지금 챕터</b>를 적는다 — 안 적으면 불러온 전투가 끝난 뒤 모세스가 기본 챕터(10)로 돌아가
                // 샤이닝 스타(11)에서 필라이프 항성계로 못 갔다(사용자 보고, Btl 0136).
                host.Mos._mosesChp?.Id ?? 0,
                [.. host.EpisodesScr._episodesPicked],
                // 물체(0x100e80f0)·찾은 사람 20010/20011(0x101b6994/0x101b6996)·이 틱에 남은 물체 차례도 싣는다(ba-15 Q5).
                [.. host.Btl.Objects.Select((o, i) => new SaveObject(i, o.Record.No, o.Hp, o.Team, host.Btl._opened.Contains(o), o.Charge, o.Charged))],
                host.Btl._eventFoundA is { } fa ? Array.IndexOf(host._units, fa) : -1,
                host.Btl._eventFoundB is { } fb ? Array.IndexOf(host._units, fb) : -1,
                host.Btl._objectsDue,
                // 항행 시작(911) — 원본 챕터 레코드 +0x190/+0x192(0x1004e2e3/0x1004e2f4). 장면 7(연대표) 저장에는 레코드가 없다(감사 R2).
                NavStart: !host.EpisodesScr._episodesOpen && host.Mos._navStart is { } nav ? [nav.Chapter, nav.Step, nav.Number] : null,
                // 항행에서 떠난 자리(챕터 · 단계 · 행성 · 성계) — 전투 세이브를 불러와 이긴 뒤에도 그 행성으로 돌아가게(사용자 보고: 필라이프에서 싸웠는데 리치로 돌아옴).
                NavAt: host.Mos._mosesNavVisited && host.Mos._mosesChp is { } navChapter ? [navChapter.Id, host.Mos._mosesStep, host.Mos._mosesPlanet, host.Mos._mosesSystem] : null,
                // 전투 전 스냅숏·카메라·이벤트 갈래 깃발은 전투 판에서 저장할 때만(챕터·연대표 세이브는 전투가 없다).
                Entry: battleSave && _entryRoster != null && _restartInventory != null
                    ? new SaveEntry(_restartInventory.ToDictionary(p => p.Key.ToString(), p => p.Value), _restartMoney,
                                    [.. _entryRoster.Select(p => PartyUnit(p.Key, p.Value))],
                                    _entryLegions?.ToDictionary(p => p.Key.ToString(), p => p.Value), _entryOwnedLegions?.ToArray(),
                                    _entryFlags?.Select((v, i) => (v, i)).Where(x => x.v != 0).ToDictionary(x => x.i.ToString(), x => (int)x.v))
                    : null,
                Camera: battleSave ? [host._camX, host._camY] : null,
                EventCheckDue: battleSave ? host.Btl._eventCheckDue : null,
                MosesAltVoice: host.Mos._mosesAltVoice, Bgm: host._battleLoaded && !host.Mos._mosesOpen ? host.Btl._eventBgm : null,
                Part1: battleSave && host._scene.IsPart1 ? host.Part1.ToJson(host.FlagSt._flags) : null,
                Part1Objects: battleSave && host._scene.IsPart1 ? host.Btl.Part1Objects.Save() : null,
                Part1Range: battleSave && host._scene.IsPart1 ? host.Btl.Part1Range.Save() : null,
                Part1Ambient: battleSave && host._scene.IsPart1 ? host.Btl.Part1Objects.SavedAmbient : null,
                Part1Lingering: battleSave && host._scene.IsPart1 ? host.Btl.Part1Objects.SaveLingering() : null,
                Part1Energy: battleSave && host._scene.IsPart1 ? host.Btl.Part1Fields.Save() : null,
                SceneTitle: host._scene.IsPart1 ? host._scene.Title : null,
                Part1Riders: battleSave && host._scene.IsPart1 ? host.Btl.SaveRiders() : null,
                Part1LostMounts: battleSave && host._scene.IsPart1 ? [.. host.Btl._part1LostMounts] : null);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, SaveJson));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            host.Toast($"저장하지 못했습니다: {ex.Message}");
            return false;
        }
    }

    /// <summary>파트1 전투판이면 유닛 칸에 파트1 상태(온도 · 상태 · 강화 · 속성 부여 · 자세)를 붙인다 — <see cref="SaveUnit"/> 의 P1 칸.</summary>
    private SaveUnit WithPart1(SaveUnit s, UnitState u, bool battleSave)
    {
        if (!battleSave || !host._scene.IsPart1) return s;
        var (ailments, buffs, enchant, stance) = host.Btl.Part1.SaveUnit(u);
        return s with { P1Heat = host.Btl.Part1Objects.SaveUnitHeat(u), P1Ailments = ailments, P1Buffs = buffs, P1Enchant = enchant, P1Stance = stance };
    }

    /// <summary>
    /// 파트1 챕터 지도 세이브 — 전투판 없이 장면 갈래 4(칸 목록 「챕  터」) · 파트1 진행 전부(팀 자리 · 턴 · 남은 사건) · 이름 = 지역 이름.
    /// 불러오면(<see cref="LoadBattleFrom"/>) 그 챕터 지도를 연다. <paramref name="kind"/> 7 = 연대표에서(이름 「연대표」, 불러오면 연대표).
    /// </summary>
    private bool SavePart1ChapterTo(string path, int kind)
    {
        try
        {
            if (kind == 4) host.Part1TitleScr.Chapter.SaveProgress();   // 지금 팀 자리 · 남은 사건을 진행에 맞춘다
            var state = new SaveState(SaveVersion, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), 0, 0, [], [], SceneKind: kind, PlayMs: PlayMs,
                                      Part1: host.Part1.ToJson(host.FlagSt._flags), SceneTitle: kind == 7 ? "연대표" : host.Part1TitleScr.Chapter.RegionName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, SaveJson));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            host.Toast($"저장하지 못했습니다: {ex.Message}");
            return false;
        }
    }

    /// <summary>그 파일에서 전투판을 되살린다. 되살렸으면 true.</summary>
    internal bool LoadBattleFrom(string path)
    {
        SaveState? state;
        try
        {
            if (!File.Exists(path)) { host.Toast("저장한 전투가 없습니다"); return false; }
            state = JsonSerializer.Deserialize<SaveState>(File.ReadAllText(path), SaveJson);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            host.Toast($"불러오지 못했습니다: {ex.Message}");
            return false;
        }
        // 파트1 챕터 지도 세이브(장면 갈래 4 · 전투판 없음) — 진행을 되살리고 그 지도를 연다.
        if (state is { SceneKind: 4 or 7, Units.Length: 0, Part1: { } mapPart1 } && state.Version is >= OldestSaveVersion and <= SaveVersion)
        {
            if (!host.Part1.FromJson(mapPart1, host.FlagSt._flags)) { host.Toast("저장 파일을 읽을 수 없습니다"); return false; }
            _playBase = state.PlayMs - host._realTime * 1000;
            _systemMenu = false;
            if (state.SceneKind == 7) host.Part1TitleScr.LoadTimelineSave();   // 연대표 세이브(파트1)
            else host.Part1TitleScr.LoadChapterSave();
            host.Toast($"불러왔습니다 — {state.SavedAt}");
            return true;
        }
        // 연대표(장면 7) 세이브는 전투판이 비어 있어도 된다 — 새 게임 직후에는 아직 전투가 없다.
        if (state is not { } || state.Version is < OldestSaveVersion or > SaveVersion || (state.Units.Length == 0 && state.SceneKind != 7))
        {
            host.Toast("저장 파일을 읽을 수 없습니다");
            return false;
        }
        // 다른 전투에서 저장한 것이면 그 전투를 먼저 연다(옛 저장은 전투 번호가 없어 첫 전투로 본다).
        int battle = state.Battle > 0 ? state.Battle : DemoScene.Fallback.Id;
        // 타이틀에서 왔으면 아직 아무 전투도 안 읽었다 — 번호가 같아 보여도 반드시 한 번은 열어야 한다.
        // 모세스·필드·연대표가 떠 있으면 판이 640×480 틀이라 같은 전투라도 다시 연다(전투판 크기로 되돌리기).
        bool fromMoses = host.Mos._mosesOpen || host.FieldOpen || host.EpisodesScr._episodesOpen;
        // 판을 세우기 <b>전에</b> 파티·동료를 되살린다 — 아군을 미리 안 세운 전투는 BuildUnits 가 배치 칸에
        // <c>_members ∩ _party</c> 를 세우기 때문이다. 차례가 뒤집혀 있어서, 타이틀에서 불러오면 그 둘이 아직 비어
        // 기본 파티가 섰고, 전투에 있던 크리스티앙 대신 제이슨이 나왔다(사용자 보고).
        _restoreVersion = state.Version;
        // 파트1 전투 세이브 — 판을 세우기 전에 파트1 진행을 되살린다(배치 · 인물 성장이 그것을 읽는다). 이기면 그 챕터의 남은 사건을 잇는다.
        if (state.Part1 is { } part1 && host.Part1.FromJson(part1, host.FlagSt._flags)) host.Part1TitleScr.PrepareBattleLoad();
        RestorePartyBeforeBoard(state);
        // 연대표에서 저장한 것(머리 장면 7) — 원본은 챕터 상태를 안 싣고 불러오면 0x10007030(7, …) 로 연대표를 연다.
        // 전에는 InMoses 만 보아서 새 게임 직후면 옛 전투(state.Battle)가, 앞 판을 했으면 그 챕터 모세스가 열렸다(감사 F6).
        if (state.SceneKind == 7)
        {
            _playBase = state.PlayMs - host._realTime * 1000;
            host.TitleScr._titleOpen = false;
            host.Fld.CloseField();
            host.Mos._mosesOpen = false;
            host.Mos._mosesPage = -1;
            host.Mos._mosesChp = null;                // 챕터 상태 없음 — 연대표에서 에피소드를 고르면 새로 정한다
            host.Mos._navStart = null;
            host.Btl._outcome = "";
            if (host.Cols != TitleBoardCols || host.Rows != TitleBoardRows) host.ResizeBoard(TitleBoardCols, TitleBoardRows);
            host._battleLoaded = false;           // 뒤에 남은 전투판은 버린다 — 다음 전투 세이브를 부르면 판을 새로 세운다
            // 판의 유닛 자료도 버린다(NEW GAME 과 같다) — 켜자마자 깔린 첫 전투(Btl 0045)의 Lv1 살라딘·죠안이 남아 있으면 챕터 사건 701·704(UpdateCharacter)와
            // 다음 전투의 RememberParty 가 그 자료를 명부에 덮어써, 연대표 세이브로 이어 할 때마다 레벨·장비·어빌리티가 처음으로 돌아갔다(카페 제보 mo-30).
            foreach (var old in host._units) old.Data = null;
            host.StopMusic();
            host.EpisodesScr.OpenEpisodes();
            _restoreVersion = SaveVersion;
            host.Toast($"불러왔습니다 — {state.SavedAt}");
            return true;
        }
        // 지금 챕터를 되살린다 — 전투가 끝나면 OpenMoses 가 이 챕터로 돌아간다. 모세스 세이브는 아래에서 OpenMoses 가 다시 정한다.
        if (SavedChapter(state) is > 0 and var chapterId && MosesScene.LoadChapterFile(chapterId) is { } savedChp)
        {
            host.Mos._mosesChp = savedChp;
            // 자료를 받아 쓰는 판 — 불러온 챕터의 줄에서 세 줄 앞까지 미리 받는다(menu-22).
            if (host.EpisodesScr.Episodes().FirstOrDefault(e => e.Chapter == chapterId) is { } savedEp) AssetPack.Reach(savedEp.No / 2);
        }
        // 항행에서 떠난 자리를 되살린다 — 전투가 끝나 모세스로 돌아갈 때 그 행성·성계에 선다(LeaveFinishedBattleNow 가 「다녀온 항행」일 때만 되돌린다).
        if (state.NavAt is [var atChapter, var atStep, var atPlanet, var atSystem] && host.Mos._mosesChp?.Id == atChapter)
            (host.Mos._mosesNavVisited, host.Mos._mosesStep, host.Mos._mosesPlanet, host.Mos._mosesSystem) = (true, atStep, atPlanet, atSystem);
        // 자리를 안 적은 옛 세이브(전투 중 저장·자동 저장) — 그 전투를 여는 장소가 딸린 행성·성계를 챕터 자료에서 찾아 선다. 이어지는 전투(Btl 0135 → 0136)는 장소에 없으므로
        // 번호가 바로 앞(셋 안쪽)인 전투 장소로 본다(가설 — 이어지는 전투는 번호가 잇달아 있다). 필드를 거친 전투는 못 찾는다.
        else if (state.NavAt == null && !state.InMoses && host.Mos._mosesChp is { } chp
                 && chp.Places.Where(p => p.Kind == ChapterFile.PlaceKind.Battle && p.Target <= battle && battle - p.Target <= 3).MaxBy(p => p.Target) is { } from
                 && chp.Planets.FirstOrDefault(p => p.Places.Contains(from.No)) is { } on)
            (host.Mos._mosesNavVisited, host.Mos._mosesStep, host.Mos._mosesPlanet, host.Mos._mosesSystem) =
                (true, 2, on.No, chp.Systems.FirstOrDefault(s => s.Planets.Contains(on.No))?.No ?? host.Mos._mosesSystem);
        // 시험 전용: 되살린 항행 자리와 그 챕터의 전투 장소들(DUELDX_TESTRUN 일 때만).
        TestRunTrace($"load nav chp {host.Mos._mosesChp?.Id} navAt {(state.NavAt == null ? "-" : string.Join(',', state.NavAt))} → visited {host.Mos._mosesNavVisited} step {host.Mos._mosesStep} planet {host.Mos._mosesPlanet} system {host.Mos._mosesSystem}; battle places {string.Join(' ', host.Mos._mosesChp?.Places.Where(p => p.Kind == ChapterFile.PlaceKind.Battle).Select(p => $"{p.No}:{p.Target}") ?? [])}");
        if (!host._battleLoaded || battle != host._scene.Id || fromMoses || Part1RidersMissing(state))
        {
            // 판을 새로 세울 때 맵 밖 여분(벤치)을 바로 지우지 않는다 — 저장할 때 벤치에서 꺼내 세운 인물이 사라지고, 뺀 인물이 새 몸으로 서고,
            // 「군단사용」을 끈 대장의 부하가 되살아났다(ba-21 outer-rules 2.1). 세이브에 적힌 인물만 남기고 나머지 배치 인물을 뺀다.
            bool byRecord = !state.InMoses && state.Units.Length > 0;
            host.Btl._keepBenchForLoad = byRecord;
            bool started;
            try { started = StartBattle(battle, rememberParty: false); }
            finally { host.Btl._keepBenchForLoad = false; }
            if (!started) return false;
            if (byRecord)
            {
                var saved = state.Units.GroupBy(r => r.ChrCode).ToDictionary(g => g.Key, g => g.Count());
                var keep = new HashSet<UnitState>();
                var movable = host.Btl._deployMovable.ToHashSet();
                // 같은 Chr 는 세이브에 적힌 수만큼만 남긴다 — 먼저 대장·홑 유닛, 그다음 부하는 <b>대장이 남는 것만</b>(빠질 대장의 부하가 자리를 먼저 먹으면
                // 벤치에서 꺼낸 대장의 부하가 지워졌다).
                foreach (var u in host._units.Where(u => u.LeaderIndex < 0))
                    if (saved.TryGetValue(u.ChrCode, out int n) && n > 0) { keep.Add(u); saved[u.ChrCode] = n - 1; }
                foreach (var u in host._units.Where(u => u.LeaderIndex >= 0 && u.LeaderIndex < host._units.Length))
                {
                    var leader = host._units[u.LeaderIndex];
                    if (movable.Contains(leader) && !keep.Contains(leader)) continue;
                    if (saved.TryGetValue(u.ChrCode, out int n) && n > 0) { keep.Add(u); saved[u.ChrCode] = n - 1; }
                }
                // 배치로 세운 사람과 그 부하 가운데 세이브에 없는 것만 지운다 — Btl 에 박힌 유닛은 건드리지 않는다(부하는 DropUnits 가 대장을 따라 뺀다).
                host.Btl.DropUnits(u => !keep.Contains(u) && movable.Contains(u));
                host.Btl.DropUnits(u => !keep.Contains(u) && u.LeaderIndex >= 0 && u.LeaderIndex < host._units.Length && movable.Contains(host._units[u.LeaderIndex]));
                host.Btl._deployBench.Clear();
            }
        }
        // 인물 수가 달라도(부대가 생기는 등 판이 바뀌었을 수 있다) 같은 Chr 끼리 짝지어 되살린다.

        host.Btl._routine = null;
        host.Btl._outcomeAfterLevelUp = false;
        host.Btl.CancelTargeting();
        host.Btl._ringUnit = -1;
        host._statusUnit = -1;
        host.Btl._levelUpQueue.Clear();
        host.Btl._formationAt.Clear(); host.Btl._followerTarget.Clear(); host.Btl._nextFollowerRetry.Clear();   // 앞 판의 진형 기록(fg-31)
        host.Btl._levelUpUnit = -1;
        host.Btl._numbers.Clear();
        host.Btl._effects.Clear();
        host.Mov._movies.Clear();
        host.Btl._bodyClones.Clear();  host.Btl._bodyShapes.Clear(); host.Btl._bodyShots.Clear();
        host.Btl._blinks.Clear();
        host.Btl._pendingExits.Clear();
        host.Btl._exitOutcomeDue = false;
        host.Btl._brokenAt.Clear();
        host.Btl._objActing.Clear();
        host.Btl._objectRoutine = false;
        host.Btl._blinkGhosts.Clear();
        host.LegionStageAb._legionGhosts.Clear();
        host.LegionStageAb._legionFades.Clear();
        host.UnitFxAb._unitFx.Clear();
        host.LegionStageAb._legionLater.Clear();
        host.LegionStageAb._legionStageEnd = 0;
        host.StagingAb._stageDraws.Clear();
        host.Mos._planetZoomAt = -1;
        host.Btl._flyingEffects.Clear();
        host.Btl._shots.Clear();
        host.Btl._movers.Clear(); host.Btl._fallers.Clear(); host.Btl._ringTrails.Clear(); host.Btl._chasers.Clear(); host.Btl._scripted.Clear(); host.Btl._pieces.Clear(); host.Btl._cheer.Clear();
        host.Btl._fxLatestStart = 0;
        host.Btl._effectMirrors.Clear();
        host.NineCrusaderAb._swords.Clear();
        host.FinisherPreludeAb._preludeDots.Clear();
        host.FinisherPreludeAb._fxFlights.Clear();
        host.HeavenEarthAb._timedFx.Clear();
        host.HeavenEarthAb._debris.Clear();
        host.HeavenEarthAb._shakes.Clear();
        host.AcrostAb._fireBalls.Clear();
        host.FxLine.Clear();
        host.PushAb._thrown = null;
        host.Mov._ripples.Clear();
        host.Btl._heldMoveKeys.Clear();

        var pool = state.Units.ToList();
        // 세이브 자리 번호 → 지금 판 자리 번호 — 마지막 때린 자·군단 대장·찾은 사람은 자리 번호로 적혀 있다.
        var indexMap = new Dictionary<int, int>();
        int Mapped(int saved) => indexMap.TryGetValue(saved, out int now) ? now : -1;
        var restored = new List<(UnitState Unit, SaveUnit Save)>();
        for (int i = 0; i < host._units.Length; i++)
        {
            var u = host._units[i];
            // 같은 자리의 기록을 먼저 보고, 안 맞으면 같은 Chr 번호의 기록을 찾아 쓴다. 「같은 자리」는 세이브 배열의 i 번째다 —
            // 전에는 쓴 기록을 빼 가며 줄어든 목록의 i 번째를 봐서, 같은 Chr 가 여럿인 판(Btl 0298 해적 셋)에서 둘째·셋째가 뒤바뀌었다.
            var s = i < state.Units.Length && state.Units[i].ChrCode == u.ChrCode && pool.Any(r => ReferenceEquals(r, state.Units[i]))
                ? state.Units[i] : pool.FirstOrDefault(r => r.ChrCode == u.ChrCode);
            if (s == null) continue;
            pool.RemoveAt(pool.FindIndex(r => ReferenceEquals(r, s)));   // 기록은 값 비교 record 라 Remove 는 쌍둥이 기록을 지울 수 있다
            indexMap[Array.FindIndex(state.Units, r => ReferenceEquals(r, s))] = i;
            restored.Add((u, s));
            u.WarpTo(s.Col, s.Row);
            // 전장에 섰는지도 되살린다 — 배치 자료로만 정하면, 같은 Chr 가 둘일 때(한 명은 대기 (0,0)) 칸 차례로 짝지으며 뒤바뀌어
            // 보이지 않는 「전장의 적」이 (0,0)에 남아 전멸이 안 됐다(사용자 보고: Btl 0155). 옛 세이브는 (0,0)이면 전장 밖으로 본다.
            u.OnField = s.OnField ?? (s.Col != 0 || s.Row != 0);
            // 기준 칸(+0x4b8/+0x4ba)은 적힌 값으로 — 걸은 뒤 저장했으면 걸음 비용이 그대로 남는다(감사5 S2). 옛 세이브는 지금 자리.
            u.OriginCol = s.OriginCol ?? s.Col;
            u.OriginRow = s.OriginRow ?? s.Row;
            u.Facing = (Facing)s.Facing;
            // 자세(+0x4d4)도 되살린다 — 전에는 0 으로 덮어 방어·회피 자세로 차례를 넘긴 인물이 불러오면 자세를 잃었다(ba-15 Q5 #4).
            (u.Hp, u.Tp, u.Soul, u.Alive, u.HasTurn, u.Stance) = (s.Hp, s.Tp, s.Soul, s.Alive, s.HasTurn, s.Stance);
            u.Fade = 1;   // 쓰러짐·순간이동 페이드 도중에 불러와도 흐린 채 남지 않게
            u.BeginEntry(0, 0);   // 걸어 들어오던 중이었어도 선 자리에
            if (s.Side >= 0) u.Side = s.Side;
            u.Detached = s.Detached ?? false;
            if (s.Awake is { } awake) u.Awake = awake;   // 깨어남 +0x4e8 — 안 적으면 거리 조건으로 깼던 적이 다시 잠들었다(Q5 #3)
            // 아군은 위에서 되살린 파티 자료가, 적은 파티 레벨에 맞춰 자란 자료가 바탕이다 — 인물 칸이 적혀 있으면 그것으로 덮는다.
            // 부하의 적힌 레벨은 판을 세울 때 새로 셈한 값(파티 레벨의 절반)을 둔다 — 줄어든 값이 적힌 세이브도 불러오면 바로잡힌다.
            // 레벨 0 칸은 자료 없이 적힌 것(NEW GAME 이 비운 앞 판 유닛)이라 덮지 않는다 — 덮으면 레벨 0 · 어빌리티 없음이 됐다(사용자 보고).
            if (u.Data is { } c && s.Level > 0) u.Data = (s.LeaderIndex ?? -1) >= 0 ? Restored(c, s, regrow: false) with { Level = c.Level, CumExp = c.CumExp } : Restored(c, s, regrow: false);
            u.ClearStatus();
            // 모세스에서 적은 세이브는 끝난 전투의 상태이상을 들고 있을 수 있다(고치기 전 판) — 되살리지 않는다.
            for (int k = 0; k < 3 && !state.InMoses; k++)
            {
                if (s.StatusId is { } ids && k < ids.Length) u.StatusId[k] = ids[k];
                if (s.StatusValue is { } values && k < values.Length) u.StatusValue[k] = values[k];
            }
            // 전투 보정 +0x4c8~+0x4d2(상태 30·31·32·33·37·48 이 더한 값) — ClearStatus 가 지운 뒤에 채운다(Q5 #2).
            if (!state.InMoses && s.Bonus is [var dex, var psy, var dep, var maxTp, var maxSoul, var maxHp, ..])
                (u.BonusDex, u.BonusPsy, u.BonusDep, u.BonusMaxTp, u.BonusMaxSoul, u.BonusMaxHp) = (dex, psy, dep, maxTp, maxSoul, maxHp);
        }
        // 자리 번호로 적힌 것들은 모두 짝지은 뒤에 — 마지막 때린 자(+0xfc, 조건 301)와 군단 물려받기(+0x4ea~, Legion.cs PromoteFollower).
        foreach (var (u, s) in restored)
        {
            u.LastHitBy = Mapped(s.LastHitBy) is >= 0 and var hit ? host._units[hit] : null;
            for (int k = 0; k < u.StatusSource.Length; k++)
                u.StatusSource[k] = s.StatusBy is { } by && k < by.Length && Mapped(by[k]) is >= 0 and var src ? host._units[src] : null;
            if (s.StartCol is { } startCol && s.StartRow is { } startRow) (u.StartCol, u.StartRow) = (startCol, startRow);
            if (s.StartFacing is { } startFacing) u.StartFacing = (Facing)startFacing;
            if (s.LeaderIndex is { } leader) u.LeaderIndex = leader < 0 ? -1 : Mapped(leader);
            if (s.FormationSlot is { } slot) u.FormationSlot = slot;
            if (s.LegionId is { } legion) u.LegionId = legion;
            if (s.LegionPower is { } power) u.LegionPowerPercent = power;
        }
        // 최대치는 군단 대장·보정을 다 놓은 뒤에 센다 — 먼저 세면 HP 가 틀린 최대치로 잘린다.
        foreach (var (u, s) in restored)
        {
            (u.Hp, u.Tp, u.Soul) = (s.Hp, s.Tp, s.Soul);
            host.StatusScr.RefreshUnitStats(u);
        }
        host.Btl._eventFoundA = Mapped(state.FoundA) is >= 0 and var foundA ? host._units[foundA] : null;
        host.Btl._eventFoundB = Mapped(state.FoundB) is >= 0 and var foundB ? host._units[foundB] : null;
        RestoreObjects(state);
        if (host._scene.IsPart1) RestorePart1Battle(state, restored, Mapped);
        _restartPart1 = null;

        // 전투 전 스냅숏(감사5 S5) — 원본 세이브의 전역 본문은 전투 들어가기 직전 파티이고 전투 안 값은 판 부분(유닛)에 따로 있다.
        // 스냅숏이 있으면 명부를 그것으로 두고(전투 안 값은 유닛이 들고 있다) RESTART 기준도 그것으로 한다.
        if (!state.InMoses && state.Entry is { } entry)
        {
            _restartInventory = [.. entry.Inventory.Where(p => int.TryParse(p.Key, out _)).Select(p => KeyValuePair.Create(int.Parse(p.Key), p.Value))];
            // 전투 전 깃발 — 적혀 있으면 그것이 RESTART 기준이다. 전에는 불러온 시점 깃발을 기준으로 삼아, 전투 중 오른 깃발
            // (Btl 0239·0240·0179·0180 의 110·208)이 「저장 → 불러오기 → RESTART」 에서 한 번 더 올랐다(ba-21 outer-rules 2.2). 옛 세이브는 전처럼.
            _entryFlags = (byte[])host.FlagSt._flags.Clone();
            if (entry.Flags is { } entryFlagsSaved)
            {
                Array.Clear(_entryFlags);
                foreach (var (number, value) in entryFlagsSaved)
                    if (int.TryParse(number, out int flag) && (uint)flag < _entryFlags.Length) _entryFlags[flag] = (byte)Math.Clamp(value, 0, 255);
            }
            _restartMoney = entry.Money;
            _entryRoster = [];
            foreach (var s in entry.Roster)
                if (host._db?.Character(s.ChrCode) is { } bc) _entryRoster[s.ChrCode] = Restored(bc, s, regrow: true);
            // 명부 = 전투 전 값(스냅숏에 없는 사람 — 전투 중에 합류한 사람 — 은 판 값 그대로).
            foreach (var (chr, c) in _entryRoster) host._party[chr] = CopyChar(c);
            _entryLegions = entry.Legions?.Where(p => int.TryParse(p.Key, out _)).ToDictionary(p => int.Parse(p.Key), p => p.Value);
            _entryOwnedLegions = entry.OwnedLegions?.ToList();
        }
        else
        {
            // 스냅숏이 없는 옛 세이브 — 예전처럼 전투에 선 아군의 되살린 값을 명부에 담고, RESTART 는 저장 시점 가방·GP 로 한다
            // (같은 전투에서 불러오면 앞 세션의 기준이 남던 것도 막는다).
            host.Btl.RememberParty();
            _restartInventory = [.. host.StatusScr._inventory];
        _entryFlags = (byte[])host.FlagSt._flags.Clone();   // RESTART 는 진행 깃발도 전투 전으로(0x10064800 · 0x100646d0, ba-20 T1)
            _restartMoney = host.Mos._shopMoney;
            _entryRoster = null;
            _entryLegions = null;
            _entryOwnedLegions = null;
        }

        // 전투 이벤트 상태를 되살린다 — 이미 터진 사건은 다시 안 터진다.
        if (state.EventFired is { } fired && fired.Length == host.Btl._eventFired.Length)
        {
            Array.Copy(fired, host.Btl._eventFired, fired.Length);
            host.Btl._turnNo = state.TurnNo;
        }
        else
        {
            // 사건 횟수가 없던 옛 세이브(또는 이벤트 수가 달라진 전투) — 시작 방아쇠(조건 0·1)만 있는 사건은
            // 이미 본 것으로 치고, 턴 수도 인트로(턴 2)를 지난 값으로 둔다.
            for (int i = 0; i < host.Btl._events.Count; i++)
                if (host.Btl._events[i].MaxFire > 0 && host.Btl._events[i].Conditions.Count > 0
                    && host.Btl._events[i].Conditions.All(c => c.Code is 0 or 1))
                    host.Btl._eventFired[i] = host.Btl._events[i].MaxFire;
            host.Btl._turnNo = Math.Max(state.TurnNo, 3);
        }
        Array.Clear(host.Btl._battleVars);
        foreach (var (number, value) in state.BattleVars ?? [])
            if (int.TryParse(number, out int slot) && (uint)slot < host.Btl._battleVars.Length) host.Btl._battleVars[slot] = (byte)Math.Clamp(value, 0, 255);
        Array.Clear(host.Btl._eventTimer);
        Array.Clear(host.Btl._eventTimerRun);
        for (int i = 0; i < host.Btl._eventTimer.Length; i++)
        {
            if (state.EventTimer is { } timer && i < timer.Length) host.Btl._eventTimer[i] = timer[i];
            if (state.EventTimerRun is { } run && i < run.Length) host.Btl._eventTimerRun[i] = run[i];
        }
        host.Btl._eventNextBattle = state.EventNextBattle;
        host.Btl._eventNextField = state.EventNextField;

        host.Btl._tick = Math.Max(1, state.Tick);          // 옛 세이브는 0 에서 세던 것이라 하나 올려 받는다
        host.Btl._objectsDue = state.ObjectsDue;           // 그 틱의 물체 차례가 아직 안 돌았으면 이어서 돈다(Q6)
        host.Btl._turn = -1;
        host.Btl._outcome = "";
        host._selected = state.Turn >= 0 && state.Turn < host._units.Length ? state.Turn : -1;
        host.Btl._resumeTurn = host._selected;            // 저장했던 인물의 차례로 곧장 돌아간다(UpdateTurn)
        host.Btl._nextTickAt = 0;
        _playBase = state.PlayMs - host._realTime * 1000;
        // 아직 안 본 이벤트 갈래 깃발(감사5 S6) — 옛 세이브는 지금 값(판을 새로 세웠으면 0xF) 그대로.
        if (state.EventCheckDue is { } due) host.Btl._eventCheckDue = due;
        // 카메라 스크롤(원본 +0x3cae/+0x3cb0, 감사5 S7) — 판을 새로 세웠으면 시작 카메라·페이드인이 먼저 서므로 되살린 차례가 시작될 때 놓는다.
        _loadCamera = state.Camera is [var camX, var camY, ..] ? (camX, camY) : null;
        _loadCameraPending = !state.InMoses;
        if (_loadCameraPending && !host._battleIntroPending && _loadCamera is { } cam) { ApplySavedCamera(cam); _loadCameraPending = false; }
        // 모세스에서 저장한 것이면 모세스로 돌아간다 — 챕터 BGM 은 OpenMoses 가 튼다.
        if (state.InMoses)
        {
            host.TitleScr._titleOpen = false;
            host.StopMusic();
            var loadedChp = state.Chapter > 0 ? MosesScene.LoadChapterFile(state.Chapter) : null;
            // 장소 조건을 안 거르던 판의 세이브(파티 칸이 없다) — 열린 전투·필드 장소가 하나도 없으면 챕터를 다 돈 것으로 본다.
            // 그때는 장소를 순서 없이 겪을 수 있어 깃발이 원본 순서와 어긋나, 지금 규칙으로는 상점만 남아 갇힌다.
            if (state.Party == null && loadedChp is { } oc
                && !oc.Places.Any(p => p.Value < 20000 && p.Auto == 0 && !host.Mos._placesUsed.Contains((oc.Id, p.No)) && host.FlagSt.FlagsAllow(p.Conditions)))
                host.EpisodesScr._chapterDone = true;
            host.Mos.OpenMoses(loadedChp);
            // OpenMoses(챕터) 가 항행 시작을 파일 값으로 지우므로 그 뒤에 세이브 값으로 덮는다 — 원본도 −1 복원이 파일 값 읽기(0x100f58af)
            // 뒤에 +0x2e40/+0x2e42 를 덮는다(0x100f5994/0x100f59ab, 감사 R2).
            host.Mos._navStart = state.NavStart is [var navChp, var navStep, var navNo] ? (navChp, navStep, navNo) : null;
            // OpenMoses 가 항행 페이지로 열면서 파일 값으로 자리를 놓았다 — 세이브 값으로 다시 놓는다.
            if (host.Mos._mosesOpen && host.Mos._mosesPage == 0)
            {
                host.Mos.ResetNavToStart();
                host.Mos.ShowMosesBackground(host.Mos.MosesSystem()?.Background ?? 70);
            }
            _restoreVersion = SaveVersion;
            host.Toast($"불러왔습니다 — {state.SavedAt}");
            return true;
        }
        // 타이틀·모세스·필드에서 불러왔으면 그 화면을 내리고 전투 음악으로 바꾼다 — 모세스에서 전투 세이브를 부르면
        // 모세스 음악이 그대로 흐르던 문제(사용자 보고).
        if (host.TitleScr._titleOpen || fromMoses)
        {
            host.TitleScr._titleOpen = false;
            host.Fld.CloseField();
            host.Mos._mosesOpen = false;
            host.EpisodesScr._episodesOpen = false;
            host.StopMusic();
            host.StartBattleMusic();
        }
        // 세이브의 인물 기록(701 로 바뀐 그림 +0xc 따위)은 판을 세운 <b>뒤에</b> 덮여서, 판을 세울 때 읽은 그림과 어긋날 수 있다 —
        // 바뀐 인물만 다시 읽는다(건슬라이서 살라딘, Btl 0150).
        // 사건이 곡을 바꾼 뒤 저장한 판이면 그 곡으로 — 원본은 판에 실린 곡 번호로 음악을 건다(0x10061bf0 이 +0x3c64 를 읽는다). 전에는 전투 기본 곡으로 돌아갔다.
        host.Btl._eventBgm = state.Bgm;
        if (state.Bgm is { } song && !state.InMoses && song != host._musicId)
        {
            host.StopMusic();
            if (song > 1 && song != 0xffff) host.PlayMusicFile(song, loop: true);
        }
        host.LoadRosterSprites();
        _restoreVersion = SaveVersion;
        host.Toast($"불러왔습니다 — {state.SavedAt}");
        return true;
    }

    /// <summary>불러온 세이브의 카메라 스크롤(없으면 null) · 아직 안 놓았나 — 되살린 차례가 시작될 때(StartTurn) 놓는다.</summary>
    internal (int X, int Y)? _loadCamera;
    internal bool _loadCameraPending;

    /// <summary>스크롤을 그 자리로 바로(보간·명령 없이) — 원본 불러오기 <c>0x1006f3a0</c> 이 +0x3cae/+0x3cb0 을 그대로 넣는 것.</summary>
    internal void ApplySavedCamera((int X, int Y) cam)
    {
        host.Btl._camGoal = null;
        host.Btl._camPosX = host.Btl._camTargetX = host.Btl.ClampCamX(cam.X);
        host.Btl._camPos = host.Btl._camTarget = host.Btl.ClampCamY(cam.Y);
        host._camX = (int)host.Btl._camPosX;
        host._camY = (int)host.Btl._camPos;
    }

    /// <summary>
    /// 같은 판에서 불러오는데 세이브의 탈것 짝에 든 유닛(탈것 · 탄 이)이 지금 판에 모자라는가 — 저장 뒤 RESTART 하고 배치에서 내렸으면 탈것 유닛이 없다.
    /// 그러면 판을 새로 세운다(세이브의 파트1 진행 · 탑승 지정대로 탈것과 탄 이가 다시 선다).
    /// </summary>
    private bool Part1RidersMissing(SaveState state) =>
        host._battleLoaded && host._scene.IsPart1 && state.Part1Riders is { } pairs
        && pairs.Where((_, k) => k % 3 != 2).Where(i => i >= 0 && i < state.Units.Length).Select(i => state.Units[i].ChrCode).Distinct()
                .Any(chr => host._units.Count(u => u.ChrCode == chr) < state.Units.Count(s => s.ChrCode == chr));

    /// <summary>
    /// 파트1 전투 상태를 되살린다 — 기온(행동 102) · 칸의 기운(땅불 · 얼음 덩이) · 에너지 필드 · 유닛마다 상태 · 강화 · 속성 부여 · 자세 · 온도(그다음 동결 표지).
    /// 같은 판에서 불러오면 판을 새로 안 세우므로 먼저 모두 비운다. 칸이 없는 옛 세이브는 맵 기온 · 기운 없음 · 80 · 모두 풀림(예전과 같다).
    /// 시간 틱 바탕은 세이브 틱(아래에서 <c>_tick</c> 에 넣는 값)이다.
    /// </summary>
    private void RestorePart1Battle(SaveState state, List<(UnitState Unit, SaveUnit Save)> restored, Func<int, int> mapped)
    {
        // 탈것 짝(+0x1cc · +0x1d0) · 무리 탈것(+0x1c0) · 이 판에서 잃은 무리 탈것 — 판을 세운 짝 대신 세이브의 짝으로.
        if (!state.InMoses) host.Btl.RestoreRiders(state.Part1Riders, state.Part1LostMounts, mapped);
        int tick = Math.Max(1, state.Tick);
        var btl = host.Btl;
        btl.Part1Objects.RestoreAmbient(state.Part1Ambient);
        btl.Part1Objects.RestoreLingering(state.Part1Lingering, tick);
        btl.Part1Fields.Restore(state.Part1Energy, tick);
        btl.Part1.ClearUnitStates();
        foreach (var (u, s) in restored)
            if (!state.InMoses) btl.Part1.RestoreUnit(u, s.P1Ailments, s.P1Buffs, s.P1Enchant, s.P1Stance);
        btl.Part1Objects.RestoreUnitHeat(restored.Select(r => (r.Unit, state.InMoses ? null : r.Save.P1Heat)));
        foreach (var (u, _) in restored) host.StatusScr.RefreshUnitStats(u);
    }

    /// <summary>
    /// 세이브의 물체 칸을 판에 되살린다(원본 <c>0x100e80f0</c> 의 짝) — HP·편·열림/부서짐·충전.
    /// 물체 칸이 없는 옛 세이브는 판을 세운 그대로 둔다. 같은 번호(배치 +0x140)끼리 짝짓고, 안 맞으면 자리 순번으로 본다.
    /// </summary>
    internal void RestoreObjects(SaveState state)
    {
        if (host._scene.IsPart1) host.Btl.Part1Objects.Restore(state.Part1Objects, Math.Max(1, state.Tick));   // 불러온 시간 틱(아래에서 _tick 에 넣는 값)
        if (host._scene.IsPart1) host.Btl.Part1Range.Restore(state.Part1Range);
        if (state.Objects is not { } saved) return;
        var objects = host.Btl.Objects;
        for (int i = 0; i < objects.Count; i++)
        {
            var o = objects[i];
            var s = saved.FirstOrDefault(x => x.Index == i && x.No == o.Record.No) ?? saved.FirstOrDefault(x => x.No == o.Record.No && x.Index >= 0);
            if (s == null) continue;
            (o.Hp, o.Team, o.Charge, o.Charged) = (s.Hp, s.Team, s.Charge, s.Charged);
            if (s.Opened)
            {
                host.Btl._opened.Add(o);
                host.Btl._openedAt[o] = host._lastTime - 1000;     // 여는 모션은 이미 끝났다 — 문은 곧장 열린 모션 2 로 선다
            }
            else { host.Btl._opened.Remove(o); host.Btl._openedAt.Remove(o); }
        }
    }
}

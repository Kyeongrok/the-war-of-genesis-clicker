using System.IO;
using System.Text.Json;
using WarOfGenesis.Assets;

namespace DuelDx;

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
internal sealed unsafe partial class BattleSceneWindow
{
    private const int SystemObs = 894;
    private const int SystemW = 214, SystemRowH = 37, SystemRowW = 190, SystemPad = 12;
    private const int SoundSaved = 579;
    /// <summary>목록 줄 바탕 그림 — 분석-시스템메뉴·분석-모션 「Obs 파일 갈래」.</summary>
    private const int ListRowObs = 471;

    /// <summary>시스템 메뉴 항목 — 원본 차례대로(위에서 아래), 글자는 Obs 0894 모션.</summary>
    private enum SystemItem { Mission, Restart, Load, Save, Volume, Exit }

    private static readonly (SystemItem Item, int Motion, string Label)[] SystemItems =
    [
        (SystemItem.Mission, 11, "MISSION"),
        (SystemItem.Restart, 3, "RESTART"),
        (SystemItem.Load, 1, "LOAD"),
        (SystemItem.Save, 0, "SAVE"),
        (SystemItem.Volume, 2, "VOLUME"),
        (SystemItem.Exit, 4, "EXIT GAME"),
    ];

    /// <summary>지금 화면의 시스템 메뉴 항목 — 모세스에서는 MISSION·RESTART 가 없다(분석-모세스 13절).</summary>
    private (SystemItem Item, int Motion, string Label)[] MenuItems =>
        _mosesOpen ? [.. SystemItems.Where(i => i.Item is not (SystemItem.Mission or SystemItem.Restart))] : SystemItems;

    private bool _systemMenu;
    private bool _missionWindow, _volumeWindow;
    private (string Title, string Text, Action Yes)? _confirm;

    private void OpenSystemMenu()
    {
        _systemMenu = true;
        _missionWindow = _volumeWindow = false;
        _confirm = null;
    }

    private bool SystemOpen => _systemMenu || _missionWindow || _volumeWindow || _confirm != null || SlotsOpen;

    private (int X, int Y, int H) SystemMenuRect()
    {
        int h = SystemPad * 2 + MenuItems.Length * SystemRowH;
        return (_camX + (ViewWidth - SystemW) / 2, _camY + (ViewHeight - h) / 2, h);
    }

    private void RunSystemItem(SystemItem item)
    {
        switch (item)
        {
            case SystemItem.Mission: _missionWindow = true; break;
            case SystemItem.Volume: _volumeWindow = true; break;
            case SystemItem.Save: OpenSlots(0); break;
            case SystemItem.Load: OpenSlots(1); break;
            case SystemItem.Restart:
                _confirm = ("RESTART", "전투를 다시 시작하시겠습니까?", RestartBattle);
                break;
            case SystemItem.Exit:
                _confirm = ("EXIT GAME", "창세기전3 PartII를 종료하시겠습니까?", BackToTitle);
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
    private void BackToTitle()
    {
        _systemMenu = _missionWindow = _volumeWindow = false;
        _slotsMode = -1;
        CloseField();
        _mosesOpen = false;
        _mosesPage = -1;
        _battleLoaded = false;
        ResizeBoard(TitleBoardCols, TitleBoardRows);
        OpenTitle();
    }

    /// <summary>열린 창이 있으면 클릭을 처리하고 true.</summary>
    private bool OnSystemClick(int bx, int by)
    {
        if (_confirm is not null && OnConfirmClick()) return true;
        if (OnSlotsClick(bx, by)) return true;
        return OnMenuClick(bx, by);

        bool OnConfirmClick()
        {
            var confirm = _confirm!;
            var (cx, cy, cw, ch) = ConfirmRect();
            if (by >= cy + ch - 34 && by < cy + ch - 8)
            {
                if (bx >= cx + cw / 2 - 86 && bx < cx + cw / 2 - 10) { _confirm = null; confirm.Value.Yes(); return true; }
                if (bx >= cx + cw / 2 + 10 && bx < cx + cw / 2 + 86) { _confirm = null; return true; }
            }
            return true;
        }
    }

    private bool OnMenuClick(int bx, int by)
    {
        if (_missionWindow || _volumeWindow)
        {
            if (_volumeWindow) OnVolumeClick(bx, by);
            else _missionWindow = false;
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
    private bool CloseSystemWindow()
    {
        if (_confirm != null) { _confirm = null; return true; }
        if (SlotsOpen) { _slotsMode = -1; return true; }
        if (_missionWindow || _volumeWindow) { _missionWindow = _volumeWindow = false; return true; }
        if (_systemMenu) { _systemMenu = false; return true; }
        return false;
    }

    private void DrawSystem()
    {
        DrawSlots();
        DrawNotice();
        if (_systemMenu) DrawSystemMenu();
        if (_missionWindow) DrawMissionWindow();
        if (_volumeWindow) DrawVolumeWindow();
        if (_confirm != null) DrawConfirm();
    }

    private void DrawSystemMenu()
    {
        var (x, y, h) = SystemMenuRect();
        FillRect(x - 4, y - 4, SystemW + 8, h + 8, 0x80000000);
        FillRect(x, y, SystemW, h, 0xD00A1428);
        StrokeRect(x, y, SystemW, h, BoxLine);
        DrawText("System Menu", x + SystemPad, y - 22, White, 15);

        var items = MenuItems;
        for (int i = 0; i < items.Length; i++)
        {
            var (item, motion, label) = items[i];
            int rx = x + SystemPad, ry = y + SystemPad + i * SystemRowH;
            // 평소 칸은 바탕 + 테두리(원본 갈래 0 단추 0x100432e0). Obs 0471 모션 7(190×37 파란 빛)은 <b>보조 그림</b>(단추 +0x10c)이라
            // 마우스가 올라간 줄에만 덧그린다 — 전에는 모든 줄에 그려 전부 올림 상태로 보였다.
            FillRect(rx, ry, SystemRowW, SystemRowH - 3, BoxBg);
            StrokeRect(rx, ry, SystemRowW, SystemRowH - 3, BoxLine);
            bool hover = _mouse.X >= rx && _mouse.X < rx + SystemRowW && _mouse.Y >= ry && _mouse.Y < ry + SystemRowH - 3;
            if (hover) DrawUi(ListRowObs, 7, 0, rx, ry, UiBlend.Alpha, loop: false);
            // 원본은 칸 안 (20,10) 자리에 Obs 0894 글자 그림을 찍는다.
            if (!DrawUi(SystemObs, motion, 0, rx + 20, ry + 10, UiBlend.Alpha, loop: false))
                DrawText(label, rx + 20, ry + 9, White);
        }
    }

    /// <summary>MISSION — 승리·패배 조건(Btl 머리 워드 5·6).</summary>
    private void DrawMissionWindow()
    {
        int w = 400, h = 180, x = _camX + (ViewWidth - w) / 2, y = _camY + (ViewHeight - h) / 2;
        FillRect(x, y, w, h, PanelBg);
        StrokeRect(x, y, w, h, BoxLine);
        FillRect(x, y, w, 26, HeadBg);
        DrawText(_scene.Title, x + 12, y + 4, White, 15);
        DrawText("승리 조건", x + 20, y + 44, 0xFF80D0FF);
        DrawText(_db?.T(_scene.WinTextId) ?? "", x + 110, y + 44, White);
        DrawText("패배 조건", x + 20, y + 96, 0xFFE08080);
        DrawText(_db?.T(_scene.LoseTextId) ?? "", x + 110, y + 96, White);
        DrawText("아무 곳이나 누르면 닫힙니다", x + 20, y + h - 28, DimGray);
    }

    // ── 음량 창 ──────────────────────────────────────────────────────────────

    private int _bgmVolume = 90, _seVolume = 90;
    private const int VolumeW = 220, VolumeH = 160, VolumeCells = 20;

    private (int X, int Y) VolumeOrigin() => (_camX + (ViewWidth - VolumeW) / 2, _camY + (ViewHeight - VolumeH) / 2);

    private void OnVolumeClick(int bx, int by)
    {
        var (x, y) = VolumeOrigin();
        foreach (var (rowY, setter) in new (int, Action<int>)[] { (40, v => _bgmVolume = v), (90, v => _seVolume = v) })
        {
            if (by < y + rowY || by >= y + rowY + 13) continue;
            int cell = (bx - (x + 20)) / 9;
            if (cell < 0 || cell >= VolumeCells) continue;
            setter(5 * cell + 5);   // 원본 값 = 5n+5 (5~100)
            ApplyVolumes();
            return;
        }
        if (by >= y + VolumeH - 30) _volumeWindow = false;
    }

    private void ApplyVolumes()
    {
        _mixer.SetMusicGain(_bgmVolume / 100f);
        _effectGain = _seVolume / 100f;
    }

    private void DrawVolumeWindow()
    {
        var (x, y) = VolumeOrigin();
        FillRect(x, y, VolumeW, VolumeH, PanelBg);
        StrokeRect(x, y, VolumeW, VolumeH, BoxLine);
        FillRect(x, y, VolumeW, 26, HeadBg);
        DrawText("Volume", x + 12, y + 4, White, 15);

        foreach (var (rowY, label, value) in new (int, string, int)[] { (40, "B.G.M", _bgmVolume), (90, "S.E", _seVolume) })
        {
            DrawText(label, x + 20, y + rowY - 18, White);
            for (int i = 0; i < VolumeCells; i++)
            {
                int cx = x + 20 + i * 9;
                bool on = 5 * i + 5 <= value;
                FillRect(cx, y + rowY, 8, 13, on ? 0xFF6AA8FF : 0xFF203050);
                StrokeRect(cx, y + rowY, 8, 13, BoxLine);
            }
            RightText($"{value}", x + VolumeW - 16, y + rowY - 18, DimGray);
        }
        DrawText("닫으려면 아래를 누르세요", x + 20, y + VolumeH - 26, DimGray);
    }

    // ── 확인 창 ──────────────────────────────────────────────────────────────

    private (int X, int Y, int W, int H) ConfirmRect()
    {
        int w = 340, h = 120;
        return (_camX + (ViewWidth - w) / 2, _camY + (ViewHeight - h) / 2, w, h);
    }

    private void DrawConfirm()
    {
        if (_confirm is not { } confirm) return;
        var (x, y, w, h) = ConfirmRect();
        FillRect(x - 4, y - 4, w + 8, h + 8, 0x80000000);
        FillRect(x, y, w, h, PanelBg);
        StrokeRect(x, y, w, h, BoxLine);
        FillRect(x, y, w, 26, HeadBg);
        DrawText(confirm.Title, x + 12, y + 4, White, 15);
        var (_, tw, _) = GetText(confirm.Text, White);
        DrawText(confirm.Text, x + (w - tw) / 2, y + 48, White);

        foreach (var (bx, label) in new (int, string)[] { (x + w / 2 - 86, "예"), (x + w / 2 + 10, "아니오") })
        {
            FillRect(bx, y + h - 34, 76, 26, HeadBg);
            StrokeRect(bx, y + h - 34, 76, 26, BoxLine);
            var (_, lw, _) = GetText(label, White);
            DrawText(label, bx + (76 - lw) / 2, y + h - 30, White);
        }
    }

    /// <summary>RESTART — 같은 전투를 처음부터(원본은 전투 번호 그대로 장면 1 을 다시 연다).</summary>
    /// <summary>
    /// 다른 전투를 건다 — 그 <c>Btl</c> 자료를 읽어 맵·인물·배경음악을 갈아 끼운다(전투 이벤트 행동 10 「다음 전투」도 이 길로 온다).
    /// </summary>
    /// <summary>지금이 챕터 장면인가 — 모세스 주 화면·필드·연대표는 모두 한 챕터 안이다.</summary>
    private bool InChapterScene => _mosesOpen || _episodesOpen || FieldOpen;

    /// <param name="rememberParty">
    /// 지금 판의 아군을 파티에 담고 시작할지 — 불러오기는 <b>false</b> 다. 불러오기는 세이브로 파티를 먼저 되살리는데,
    /// 여기서 담으면 불러오기 전 판(타이틀 뒤 데모 전투, 하던 전투)의 유닛이 되살린 파티를 덮어써서
    /// 그 전투에 안 선 파티원의 레벨·장착 어빌리티·어빌리티 레벨이 처음 값으로 돌아갔다(사용자 보고).
    /// </param>
    private bool StartBattle(int id, bool rememberParty = true)
    {
        if (DemoScene.Load(id, _db) is not { } scene)
        {
            Toast($"전투 {id:D4} 자료가 assets 에 없습니다");
            return false;
        }
        try
        {
            _map = ObtMap.Load(Path.Combine(AssetsFolder.Find("maps"), scene.MapFile));
            ResizeBoard(_map.Cols, _map.Rows);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            Toast($"전투 {id:D4} 맵을 못 읽었습니다: {ex.Message}");
            return false;
        }

        if (rememberParty) RememberParty();          // 앞 전투에서 오른 레벨·경험치를 들고 간다
        _battleLoaded = true;
        _resumeTurn = -1;         // 새 판 — 불러오기는 판을 세운 뒤 다시 정한다
        _titleOpen = false;       // 타이틀·기록 화면에서 왔으면 이제 전투가 앞이다
        _recordsOpen = false;
        _episodesOpen = false;
        _scene = scene;
        _units = BuildUnits(scene);
        LoadEvents(scene.Id);
        LoadRosterSprites();
        _mosesOpen = false;
        _statusUnit = -1;
        _infoUnit = -1;
        _ringUnit = -1;
        RestartBattle();
        Toast($"{scene.Title} — 전투 Btl {scene.Id:D4}");
        return true;
    }

    private void RestartBattle()
    {
        foreach (var unit in _units) unit.ResetTo(unit.StartCol, unit.StartRow);
        // 가방은 챕터 스크립트가 채운 것이 옳다 — 그것이 있으면 비우지도, 데모 아이템으로 덮지도 않는다.
        if (_chapterFired.Count == 0)
        {
            _inventory.Clear();
            InitBattle();
            FillDemoInventory();
        }
        else InitBattle();
        // 원본은 전투를 만들 때 이 값을 <b>1</b> 로 놓는다(0x100644d4) — 0 이면 표시가 늘 하나씩 작다.
        _tick = 1;
        _turn = -1;
        _selected = -1;
        _outcome = "";
        _routine = null;
        _levelUpQueue.Clear();
        _levelUpUnit = -1;
        _numbers.Clear();
        _effects.Clear();
        _movies.Clear();
        _bodyClones.Clear();
        _flyingEffects.Clear();
        _ripples.Clear();
        _nextTickAt = 0;
        CancelTargeting();
        StartBattleMusic();
    }

    // ── 저장 · 불러오기 ──────────────────────────────────────────────────────

    private sealed record SaveAbility(int Id, int Level);

    /// <param name="Char">
    /// 인물 레코드의 나머지 — 직업(전직·세부 체질)·체질·그림·이름·칭호·WEAPON 띠와 레벨업으로 오른 능력치.
    /// 예전 세이브에는 없어서 불러오면 .chr 처음 값으로 돌아갔다 — 공격형으로 바꾼 체질이 일반형으로, 오른 LP·TP 가 처음 값으로(사용자 보고).
    /// </param>
    private sealed record SaveUnit(int ChrCode, int Col, int Row, int Facing, int Hp, int Tp, int Soul,
                                   bool Alive, bool HasTurn, int Level, int CumExp, int Exp,
                                   ushort[] Items, ushort[] Passives, SaveAbility[] Abilities,
                                   byte[]? StatusId = null, short[]? StatusValue = null, int Side = -1, SaveChar? Char = null);

    /// <summary>인물 레코드에서 세이브가 따로 적는 칸들(<see cref="SaveUnit.Char"/>).</summary>
    private sealed record SaveChar(ushort NameId, ushort Name2Id, ushort SpriteId, ushort FaceId, ushort TitleId, byte Body, ushort JobId,
                                   ushort BasicWorkId, uint Lp, ushort Psy, ushort Tp, ushort TpDivisor, ushort Ctp, ushort Dep, ushort Dex,
                                   byte WeaponBand, byte WeaponType);

    private static SaveChar? SaveCharOf(CharacterData? c) => c == null ? null
        : new SaveChar(c.NameId, c.Name2Id, c.SpriteId, c.FaceId, c.TitleId, c.Body, c.JobId, c.BasicWorkId,
                       c.Lp, c.Psy, c.Tp, c.TpDivisor, c.Ctp, c.Dep, c.Dex, c.WeaponBand, c.WeaponType);

    /// <summary>
    /// 세이브의 인물 칸을 바탕 자료 위에 되살린다. <paramref name="regrow"/> 면 인물 레코드가 안 적힌 옛 세이브에서
    /// 레벨업으로 오른 능력치를 <b>쌓인 경험치로 다시 키운다</b>(직업은 옛 세이브에 없어 못 되살린다).
    /// </summary>
    private CharacterData Restored(CharacterData baseData, SaveUnit s, bool regrow)
    {
        var c = baseData with
        {
            CumExp = s.CumExp, Exp = s.Exp,
            Items = s.Items.Length == baseData.Items.Length ? s.Items : baseData.Items,
            Passives = s.Passives.Length == 3 ? s.Passives : baseData.Passives,
            Abilities = [.. s.Abilities.Select(a => ((ushort)a.Id, (ushort)a.Level))],
        };
        // 형식 9 이하의 능력치는 복리로 불어난 옛 레벨업 식으로 쌓인 것이라 믿지 않는다 — 아군은 아래에서 다시 센다.
        bool trustStats = _restoreVersion >= 10;
        if (s.Char is { } k)
        {
            c = c with
            {
                NameId = k.NameId, Name2Id = k.Name2Id, SpriteId = k.SpriteId, FaceId = k.FaceId, TitleId = k.TitleId, Body = k.Body,
                JobId = k.JobId, BasicWorkId = k.BasicWorkId, TpDivisor = k.TpDivisor, WeaponBand = k.WeaponBand, WeaponType = k.WeaponType,
            };
            if (trustStats) c = c with { Lp = k.Lp, Psy = k.Psy, Tp = k.Tp, Ctp = k.Ctp, Dep = k.Dep, Dex = k.Dex };
        }
        if (regrow && (s.Char == null || !trustStats) && _db?.Character(c.Code) is { } b)
        {
            // .chr 처음 능력치에서 쌓인 경험치만큼 원본 식(기본값 기준, 레벨마다 같은 양)으로 다시 키운다.
            // 스크립트 702 가 바꾼 능력치는 여기서 되살리지 못한다(드물다).
            c = _db.LevelUp(c with { Lp = b.Lp, Psy = b.Psy, Tp = b.Tp, Ctp = b.Ctp, Dep = b.Dep, Dex = b.Dex, Level = b.Level }, out _)
                with { CumExp = s.CumExp };
        }
        return c with { Level = (ushort)s.Level };
    }

    /// <summary>세이브 머리 — 원본처럼 <b>저장할 때 장면 이름 TXR·장면 갈래·논 시간</b>을 함께 적는다(분석-시스템메뉴 2.1b).</summary>
    /// <param name="Flags">
    /// 진행 깃발 — 「어느 장소가 열렸나」를 정하는 값이라 <b>저장에 반드시 들어가야 한다</b>(원본도 2000바이트를 통째로 적는다, <c>0x1004d9de</c>).
    /// 0 이 아닌 칸만 (번호 → 값) 으로 적는다.
    /// </param>
    private sealed record SaveState(int Version, string SavedAt, int Tick, int Turn, SaveUnit[] Units, Dictionary<string, int> Inventory,
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
                                    SaveUnit[]? Party = null, int[]? OwnedLegions = null, SaveParty[]? Bank = null,
                                    int[]? Mailbox = null, int[]? MailRead = null, string[]? PlanetVisits = null,
                                    Dictionary<string, int>? ChapterVars = null, int CurrentChapter = 0,
                                    int[]? EpisodesPicked = null);

    private const int SaveVersion = 10;  // 9: 메일을 챕터 메일 표로 배달한다 — 8 이하는 불러올 때 우편함을 걷어 낸다
                                         // 10: 레벨업 성장을 원본대로(기본값 기준) — 9 이하는 불러올 때 아군 능력치를 다시 셈한다

    /// <summary>지금 불러오는 세이브의 형식 — <see cref="Restored"/> 가 옛 형식의 능력치를 다시 셀지 정한다.</summary>
    private int _restoreVersion = SaveVersion;

    /// <summary>
    /// 판을 세우기 전에 되살려야 하는 것 — <b>파티·동료·깃발·군단·돈</b>.
    /// </summary>
    /// <remarks>
    /// 아군을 미리 안 세운 전투는 <see cref="BuildUnits"/> 가 배치 칸에 <c>_members ∩ _party</c> 를 세운다.
    /// 그래서 이것들은 <see cref="StartBattle"/> <b>앞</b>에서 채워야 한다 — 뒤에 채우면 타이틀에서 불러왔을 때
    /// 둘 다 비어 기본 파티가 서고, 저장 당시 전투에 있던 인물(크리스티앙)이 빠진다.
    /// 전투에 <b>서 있던</b> 아군은 <c>state.Party</c> 가 아니라 <c>state.Units</c> 에 적히므로 거기서도 파티를 채운다.
    /// </remarks>
    private void RestorePartyBeforeBoard(SaveState state)
    {
        _inventory.Clear();
        foreach (var (id, count) in state.Inventory)
            if (int.TryParse(id, out int itemId)) _inventory[itemId] = count;

        _shopMoney = state.Money;
        _unitLegion.Clear();
        foreach (var (index, legion) in state.Legions ?? [])
            if (int.TryParse(index, out int chrCode)) _unitLegion[chrCode] = legion;   // Chr 번호 → 군단(옛 세이브의 자리 번호는 그냥 안 맞는다)

        _chapterDone = state.ChapterDone;
        _partyNo = state.PartyNo;
        // 파티 번호가 없던 옛 세이브 — 모세스에서 저장한 챕터의 주인 파티(Episode.dat 칸 8)로 맞춘다. 안 맞추면 OpenMoses 의 파티 바꾸기가
        // 지금 인원을 은행으로 치워 버린다.
        if (state.InMoses && state.Chapter > 0 && Episodes().FirstOrDefault(e => e.Chapter == state.Chapter) is { } owner) _partyNo = owner.Party;
        _members.Clear();
        foreach (int chr in state.Members ?? []) _members.Add(chr);

        // 진행 깃발 — 어느 장소가 열렸는지가 여기 담긴다.
        Array.Clear(_flags);
        foreach (var (number, value) in state.Flags ?? [])
            if (int.TryParse(number, out int flag) && (uint)flag < _flags.Length) _flags[flag] = (byte)Math.Clamp(value, 0, 255);

        _autoPlacesDone.Clear();
        foreach (string pair in state.DonePlaces ?? [])
            if (pair.Split(':') is [var a, var b] && int.TryParse(a, out int chapter) && int.TryParse(b, out int place))
                _autoPlacesDone.Add((chapter, place));
        _chapterFired.Clear();
        foreach (string triple in state.DoneEvents ?? [])
            if (triple.Split(':') is [var c, var e, var n] && int.TryParse(c, out int chp)
                && int.TryParse(e, out int ev) && int.TryParse(n, out int count))
                _chapterFired[(chp, ev)] = count;
        // 사건별 횟수가 없던 옛 세이브는 「그 챕터는 다 돌았다」로만 안다 — 사건 −1 에 표시를 남긴다.
        if (state.DoneEvents == null)
            foreach (int chapter in state.DoneChapters ?? []) _chapterFired[(chapter, -1)] = 1;
        // 동료 목록이 없는 옛 세이브 — 이미 돌린 챕터 스크립트의 801/802 로 되살린다(크리스티앙이 빠지고 전투의 제이슨·스턴이
        // 동료로 보이던 문제). 필드 스크립트의 801 은 어느 사건이 돌았는지 안 남아 못 되살린다.
        // 목록이 비어 있어도(고치기 전 판이 빈 목록을 적은 세이브) 되살리고, 적힌 목록과 합친다.
        var saved = _members.ToList();
        RebuildMembersFromChapters();
        foreach (int chr in saved) _members.Add(chr);
        _ownedLegions.Clear();
        foreach (int id in state.OwnedLegions ?? []) _ownedLegions.Add(id);
        _legionsKnown = state.OwnedLegions != null;
        RestoreBank(state.Bank);
        _mailbox.Clear();
        foreach (int id in state.Mailbox ?? []) _mailbox.Add(id);
        _mailRead.Clear();
        foreach (int id in state.MailRead ?? []) _mailRead.Add(id);
        if (state.Version < 9)
            PruneLegacyMailbox(_chapterFired.Keys.Select(k => k.Chapter).Concat(state.DoneChapters ?? [])
                                .Append(state.CurrentChapter).Append(state.Chapter).Where(c => c > 0));
        Array.Clear(_chapterVars);
        foreach (var (number, value) in state.ChapterVars ?? [])
            if (int.TryParse(number, out int slot) && (uint)slot < _chapterVars.Length) _chapterVars[slot] = (byte)Math.Clamp(value, 0, 255);
        _planetVisits.Clear();
        _episodesPicked.Clear();
        if (state.EpisodesPicked is { } picked) foreach (int no in picked) _episodesPicked.Add(no);
        else
            // 표시를 안 적던 옛 세이브 — 챕터 사건이 한 번이라도 돈 에피소드와 지금 챕터의 에피소드를 고른 것으로 본다.
            foreach (var ep in Episodes())
                if (_chapterFired.Keys.Any(k => k.Chapter == ep.Chapter) || ep.Chapter == state.CurrentChapter) _episodesPicked.Add(ep.No);
        foreach (string pair in state.PlanetVisits ?? [])
            if (pair.Split(':') is [var a, var b] && int.TryParse(a, out int pc) && int.TryParse(b, out int pn)) _planetVisits.Add((pc, pn));
        _placesUsed.Clear();
        foreach (string pair in state.UsedPlaces ?? [])
            if (pair.Split(':') is [var a, var b] && int.TryParse(a, out int chapter) && int.TryParse(b, out int place))
                _placesUsed.Add((chapter, place));
        // 전투에 서 있던 아군도 파티에 넣는다 — 이것이 없으면 배치 칸 고르기가 그 인물을 못 본다.
        // 편을 안 적던 옛 세이브는 −1 이라 아군·적군을 못 가린다 — 그때는 넣지 않는다(적이 파티에 들어가느니 예전대로).
        foreach (var s in state.Units)
            if (s.Side == 4 && _db?.Character(s.ChrCode) is { } bc)
                _party[s.ChrCode] = Restored(bc, s, regrow: true);
        foreach (var s in state.Party ?? [])
            if (_db?.Character(s.ChrCode) is { } pc)
                _party[s.ChrCode] = Restored(pc, s, regrow: true);
        ReturnStrayMembers();                // 다른 파티 사람은 제 파티로(파티를 안 가리던 옛 판의 세이브)
        foreach (int chr in _members)
            if (!_party.ContainsKey(chr) && _db?.Character(chr) is { } fresh) _party[chr] = fresh;
    }

    /// <summary>
    /// 다른 파티 사람이 지금 파티에 끼어 있으면 제 파티(은행)로 돌려보낸다 — 위 버그로 적힌 세이브를 고친다.
    /// 끼어 있던 동안 더 자랐을 수 있으니 누적 경험치가 큰 쪽 자료를 남긴다.
    /// </summary>
    private void ReturnStrayMembers()
    {
        foreach (var (no, bank) in _partyBank)
            foreach (int chr in bank.Members.Where(_members.Contains).ToList())
            {
                _members.Remove(chr);
                if (_party.Remove(chr, out var here) && (!bank.Party.TryGetValue(chr, out var there) || here.CumExp > there.CumExp))
                    bank.Party[chr] = here;
            }
    }

    /// <summary>돌린 챕터 사건의 801(동료 넣기)·802(빼기)로 동료 목록을 다시 만든다 — 사건 −1 표시(옛 세이브)는 그 챕터 사건 전부로 본다.</summary>
    private void RebuildMembersFromChapters()
    {
        _members.Clear();
        // 사건 차례대로 801 이 넣고 802 가 뺀다 — 챕터 스크립트의 조건(깃발)을 채워 돈 사건만 본다.
        foreach (var group in _chapterFired.Where(f => f.Value > 0).GroupBy(f => f.Key.Chapter))
        {
            if (LoadChapterFile(group.Key) is not { } chp) continue;
            bool whole = group.Any(f => f.Key.Event == -1);
            for (int ev = 0; ev < chp.Events.Count; ev++)
            {
                if (!whole && !group.Any(f => f.Key.Event == ev)) continue;
                foreach (var a in chp.Events[ev].Actions)
                {
                    if (a.Args.Length < 2 || a.Args[1] <= 0) continue;
                    // 인자0 은 <b>파티 번호</b>다 — 지금 파티 것만 센다. 전에는 파티를 안 가려서, 베라모드 파티(1)로 불러오면
                    // 챕터 10·11 이 파티 0 에 넣은 살라딘·죠안·크리스티앙까지 끼어들었다(사용자 보고, Chp 0019 전직 화면).
                    if (a.Args[0] != _partyNo) continue;
                    if (a.Code == 801) _members.Add(a.Args[1]);
                    else if (a.Code == 802) _members.Remove(a.Args[1]);
                }
            }
        }
    }

    /// <summary>
    /// 세이브가 가리키는 챕터 — 적혀 있으면 그것, 모세스 세이브면 그 챕터. 챕터를 안 적던 옛 전투 세이브는
    /// <b>마지막으로 다녀온 장소의 챕터</b>로 본다(장소는 다녀온 차례대로 적힌다). 모르면 0.
    /// </summary>
    private static int SavedChapter(SaveState state)
    {
        if (state.CurrentChapter > 0) return state.CurrentChapter;
        if (state.InMoses && state.Chapter > 0) return state.Chapter;
        foreach (var list in new[] { state.UsedPlaces, state.DonePlaces })
            if (list is { Length: > 0 } && list[^1].Split(':') is [var a, _] && int.TryParse(a, out int chapter)) return chapter;
        return 0;
    }

    /// <summary>읽을 수 있는 가장 오래된 저장 형식 — 빠진 칸은 기본값으로 채운다(형식이 바뀌어도 옛 저장을 버리지 않는다).</summary>
    private const int OldestSaveVersion = 2;

    /// <summary>불러온 판을 이어 세는 논 시간 바탕(밀리초).</summary>
    private double _playBase;

    private long PlayMs => (long)(_realTime * 1000 + _playBase);   // 실제 시간 — 게임 속도를 올려도 플레이 시간은 제대로 흐른다

    /// <summary>연대표 장면의 이름 TXR — 원본 상수 2557(빈 글, 장면 7 vt+0x1c <c>0x10106420</c>).</summary>
    private const int EpisodesSceneText = 2557;

    private static readonly JsonSerializerOptions SaveJson = new() { WriteIndented = true };

    /// <summary>전투판을 그 파일에 적는다. 적었으면 true.</summary>
    private bool SaveBattleTo(string path)
    {
        try
        {
            var state = new SaveState(SaveVersion, DateTime.Now.ToString("yyyy-MM-dd HH:mm"), _tick, _turn,
                [.. _units.Select(u => new SaveUnit(u.ChrCode, u.Col, u.Row, (int)u.Facing, u.Hp, u.Tp, u.Soul, u.Alive, u.HasTurn,
                    u.Data?.Level ?? 0, u.Data?.CumExp ?? 0, u.Data?.Exp ?? 0,
                    u.Data?.Items ?? [], u.Data?.Passives ?? [],
                    [.. (u.Data?.Abilities ?? []).Select(a => new SaveAbility(a.Ability, a.Level))],
                    [.. u.StatusId], [.. u.StatusValue], u.Side, SaveCharOf(u.Data)))],   // 편도 적는다 — 이벤트 708 로 넘어온 사람이 불러오면 적으로 돌아가지 않게
                _inventory.ToDictionary(p => p.Key.ToString(), p => p.Value),
                // 챕터 안이면 장면 갈래 4(챕터)·챕터 제목으로 적고, 불러올 때 그 챕터로 돌아간다(원본 세이브 머리와 같다).
                // 모세스 주 화면뿐 아니라 <b>필드·연대표</b>도 챕터 안이다 — 거기서 저장하면 마지막 전투 이름이 적혀
                // 샤이닝 스타 챕터인데 「코어헌터」로 보였다(사용자 보고).
                // 연대표에서 저장하면 원본처럼 갈래 7 「[NN:연대표]」 + 이름 TXR 2557(빈 글, 0x10106420) — 앞 챕터 이름이 뜨던 것(사용자 보고).
                _episodesOpen ? EpisodesSceneText
                : InChapterScene && _mosesChp is { } savedChp ? savedChp.TitleText : _scene.TitleTextId,
                _episodesOpen ? 7 : InChapterScene && _mosesChp != null ? 4 : 1, PlayMs,
                _shopMoney, _unitLegion.ToDictionary(p => p.Key.ToString(), p => p.Value), _scene.Id,
                Enumerable.Range(0, _flags.Length).Where(i => _flags[i] != 0)
                          .ToDictionary(i => i.ToString(), i => (int)_flags[i]),
                // 이미 겪은 자동 발생 장소와 이미 돌린 챕터 사건 — 안 적으면 불러올 때마다 프롤로그가 되풀이되고
                // 챕터 사건이 동료·돈을 두 번 준다. 다녀온 장소도 적어야 목록에 되살아나지 않는다.
                [.. _autoPlacesDone.Select(p => $"{p.Chapter}:{p.Place}")],
                [.. _chapterFired.Keys.Select(k => k.Chapter).Distinct()],
                [.. _chapterFired.Select(p => $"{p.Key.Chapter}:{p.Key.Event}:{p.Value}")],
                [.. _placesUsed.Select(p => $"{p.Chapter}:{p.Place}")],
                // 전투 이벤트 상태 — 안 적으면 불러올 때마다 시작 대사(조건 0·1)가 다시 뜨고 타이머·국소 변수가 처음으로 돌아간다.
                // 원본도 전투판을 통째로 저장하고 불러오면 저장했던 차례로 돌아온다(분석-시스템메뉴 2.4b) — 사건 횟수도 같이 싣는다.
                // 차례 도중이면 불러올 때 그 차례를 이어 받으며 턴 수가 하나 오르니 미리 뺀다(타이머는 이어 받을 때 안 센다).
                [.. _eventFired], _turn >= 0 ? _turnNo - 1 : _turnNo,
                Enumerable.Range(0, _battleVars.Length).Where(i => _battleVars[i] != 0)
                          .ToDictionary(i => i.ToString(), i => (int)_battleVars[i]),
                [.. _eventTimer], [.. _eventTimerRun], _eventNextBattle, _eventNextField,
                // 필드·연대표에서 저장해도 챕터로 돌아가게 적는다 — 그때 전투 번호로 돌아가면 엉뚱한 옛 전투가 열린다.
                InChapterScene && _mosesChp != null, InChapterScene ? _mosesChp?.Id ?? 0 : 0,
                _chapterDone, _partyNo, [.. _members],
                // 전투에 안 선 파티원(크리스티앙처럼 이번 전투에 없는 동료)의 레벨·장비·어빌리티 — 안 적으면 불러올 때 사라진다.
                [.. _party.Where(p => !_units.Any(u => u.ChrCode == p.Key))
                          .Select(p => new SaveUnit(p.Key, 0, 0, 0, 0, 0, 0, true, false, p.Value.Level, p.Value.CumExp, p.Value.Exp,
                                                    p.Value.Items, p.Value.Passives, [.. p.Value.Abilities.Select(a => new SaveAbility(a.Ability, a.Level))],
                                                    Char: SaveCharOf(p.Value)))],
                [.. _ownedLegions], SaveBank(),
                [.. _mailbox], [.. _mailRead], [.. _planetVisits.Select(v => $"{v.Chapter}:{v.Planet}")],
                Enumerable.Range(0, _chapterVars.Length).Where(i => _chapterVars[i] != 0).ToDictionary(i => i.ToString(), i => (int)_chapterVars[i]),
                // 전투·필드 한가운데서 저장해도 <b>지금 챕터</b>를 적는다 — 안 적으면 불러온 전투가 끝난 뒤 모세스가 기본 챕터(10)로 돌아가
                // 샤이닝 스타(11)에서 필라이프 항성계로 못 갔다(사용자 보고, Btl 0136).
                _mosesChp?.Id ?? 0,
                [.. _episodesPicked]);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(state, SaveJson));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toast($"저장하지 못했습니다: {ex.Message}");
            return false;
        }
    }

    /// <summary>그 파일에서 전투판을 되살린다. 되살렸으면 true.</summary>
    private bool LoadBattleFrom(string path)
    {
        SaveState? state;
        try
        {
            if (!File.Exists(path)) { Toast("저장한 전투가 없습니다"); return false; }
            state = JsonSerializer.Deserialize<SaveState>(File.ReadAllText(path), SaveJson);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Toast($"불러오지 못했습니다: {ex.Message}");
            return false;
        }
        if (state is not { } || state.Version is < OldestSaveVersion or > SaveVersion || state.Units.Length == 0)
        {
            Toast("저장 파일을 읽을 수 없습니다");
            return false;
        }
        // 다른 전투에서 저장한 것이면 그 전투를 먼저 연다(옛 저장은 전투 번호가 없어 첫 전투로 본다).
        int battle = state.Battle > 0 ? state.Battle : DemoScene.Fallback.Id;
        // 타이틀에서 왔으면 아직 아무 전투도 안 읽었다 — 번호가 같아 보여도 반드시 한 번은 열어야 한다.
        // 모세스가 떠 있으면 판이 640×480 틀이라 같은 전투라도 다시 연다(전투판 크기로 되돌리기).
        bool fromMoses = _mosesOpen || FieldOpen || _episodesOpen;
        // 판을 세우기 <b>전에</b> 파티·동료를 되살린다 — 아군을 미리 안 세운 전투는 BuildUnits 가 배치 칸에
        // <c>_members ∩ _party</c> 를 세우기 때문이다. 차례가 뒤집혀 있어서, 타이틀에서 불러오면 그 둘이 아직 비어
        // 기본 파티가 섰고, 전투에 있던 크리스티앙 대신 제이슨이 나왔다(사용자 보고).
        _restoreVersion = state.Version;
        RestorePartyBeforeBoard(state);
        // 지금 챕터를 되살린다 — 전투가 끝나면 OpenMoses 가 이 챕터로 돌아간다. 모세스 세이브는 아래에서 OpenMoses 가 다시 정한다.
        if (SavedChapter(state) is > 0 and var chapterId && LoadChapterFile(chapterId) is { } savedChp) _mosesChp = savedChp;
        if ((!_battleLoaded || battle != _scene.Id || _mosesOpen) && !StartBattle(battle, rememberParty: false)) return false;
        // 인물 수가 달라도(부대가 생기는 등 판이 바뀌었을 수 있다) 같은 Chr 끼리 짝지어 되살린다.

        _routine = null;
        CancelTargeting();
        _ringUnit = -1;
        _statusUnit = -1;
        _levelUpQueue.Clear();
        _levelUpUnit = -1;
        _numbers.Clear();
        _effects.Clear();
        _movies.Clear();
        _bodyClones.Clear();
        _flyingEffects.Clear();
        _ripples.Clear();
        _heldMoveKeys.Clear();

        var pool = state.Units.ToList();
        for (int i = 0; i < _units.Length; i++)
        {
            var u = _units[i];
            // 같은 자리의 기록을 먼저 보고, 안 맞으면 같은 Chr 번호의 기록을 찾아 쓴다.
            var s = i < pool.Count && pool[i].ChrCode == u.ChrCode ? pool[i] : pool.FirstOrDefault(r => r.ChrCode == u.ChrCode);
            if (s == null) continue;
            pool.Remove(s);
            u.WarpTo(s.Col, s.Row);
            u.OriginCol = s.Col;
            u.OriginRow = s.Row;
            u.Facing = (Facing)s.Facing;
            (u.Hp, u.Tp, u.Soul, u.Alive, u.HasTurn, u.Stance) = (s.Hp, s.Tp, s.Soul, s.Alive, s.HasTurn, 0);
            if (s.Side >= 0) u.Side = s.Side;
            // 아군은 위에서 되살린 파티 자료가, 적은 파티 레벨에 맞춰 자란 자료가 바탕이다 — 인물 칸이 적혀 있으면 그것으로 덮는다.
            if (u.Data is { } c) u.Data = Restored(c, s, regrow: false);
            u.ClearStatus();
            for (int k = 0; k < 3; k++)
            {
                if (s.StatusId is { } ids && k < ids.Length) u.StatusId[k] = ids[k];
                if (s.StatusValue is { } values && k < values.Length) u.StatusValue[k] = values[k];
            }
            RefreshUnitStats(u);
        }

        // 전투에 선 아군의 되살린 값을 파티에 담는다 — 위에서 채워 둔 값을 지금 판의 값으로 덮어쓴다.
        RememberParty();

        // 전투 이벤트 상태를 되살린다 — 이미 터진 사건은 다시 안 터진다.
        if (state.EventFired is { } fired && fired.Length == _eventFired.Length)
        {
            Array.Copy(fired, _eventFired, fired.Length);
            _turnNo = state.TurnNo;
        }
        else
        {
            // 사건 횟수가 없던 옛 세이브(또는 이벤트 수가 달라진 전투) — 시작 방아쇠(조건 0·1)만 있는 사건은
            // 이미 본 것으로 치고, 턴 수도 인트로(턴 2)를 지난 값으로 둔다.
            for (int i = 0; i < _events.Count; i++)
                if (_events[i].MaxFire > 0 && _events[i].Conditions.Count > 0
                    && _events[i].Conditions.All(c => c.Code is 0 or 1))
                    _eventFired[i] = _events[i].MaxFire;
            _turnNo = Math.Max(state.TurnNo, 3);
        }
        Array.Clear(_battleVars);
        foreach (var (number, value) in state.BattleVars ?? [])
            if (int.TryParse(number, out int slot) && (uint)slot < _battleVars.Length) _battleVars[slot] = (byte)Math.Clamp(value, 0, 255);
        Array.Clear(_eventTimer);
        Array.Clear(_eventTimerRun);
        for (int i = 0; i < _eventTimer.Length; i++)
        {
            if (state.EventTimer is { } timer && i < timer.Length) _eventTimer[i] = timer[i];
            if (state.EventTimerRun is { } run && i < run.Length) _eventTimerRun[i] = run[i];
        }
        _eventNextBattle = state.EventNextBattle;
        _eventNextField = state.EventNextField;

        _tick = Math.Max(1, state.Tick);          // 옛 세이브는 0 에서 세던 것이라 하나 올려 받는다
        _turn = -1;
        _outcome = "";
        _selected = state.Turn >= 0 && state.Turn < _units.Length ? state.Turn : -1;
        _resumeTurn = _selected;            // 저장했던 인물의 차례로 곧장 돌아간다(UpdateTurn)
        _nextTickAt = 0;
        _playBase = state.PlayMs - _realTime * 1000;
        // 모세스에서 저장한 것이면 모세스로 돌아간다 — 챕터 BGM 은 OpenMoses 가 튼다.
        if (state.InMoses)
        {
            _titleOpen = false;
            _mixer.StopMusic();
            var loadedChp = state.Chapter > 0 ? LoadChapterFile(state.Chapter) : null;
            // 장소 조건을 안 거르던 판의 세이브(파티 칸이 없다) — 열린 전투·필드 장소가 하나도 없으면 챕터를 다 돈 것으로 본다.
            // 그때는 장소를 순서 없이 겪을 수 있어 깃발이 원본 순서와 어긋나, 지금 규칙으로는 상점만 남아 갇힌다.
            if (state.Party == null && loadedChp is { } oc
                && !oc.Places.Any(p => p.Value < 20000 && p.Auto == 0 && !_placesUsed.Contains((oc.Id, p.No)) && FlagsAllow(p.Conditions)))
                _chapterDone = true;
            OpenMoses(loadedChp);
            _restoreVersion = SaveVersion;
            Toast($"불러왔습니다 — {state.SavedAt}");
            return true;
        }
        // 타이틀·모세스·필드에서 불러왔으면 그 화면을 내리고 전투 음악으로 바꾼다 — 모세스에서 전투 세이브를 부르면
        // 모세스 음악이 그대로 흐르던 문제(사용자 보고).
        if (_titleOpen || fromMoses)
        {
            _titleOpen = false;
            CloseField();
            _mosesOpen = false;
            _episodesOpen = false;
            _mixer.StopMusic();
            StartBattleMusic();
        }
        _restoreVersion = SaveVersion;
        Toast($"불러왔습니다 — {state.SavedAt}");
        return true;
    }
}

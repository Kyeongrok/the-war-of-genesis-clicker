using System.Runtime.InteropServices;
using DuelDx.Native;

namespace DuelDx;

/// <summary>
/// 마우스로만 되는 창(Status · 모세스 · 시스템 메뉴 · 설정)을 패드로 — <b>십자키 · 왼쪽 스틱이 누를 수 있는 자리 사이를 건너뛰며</b> 마우스 커서를
/// 그 자리로 옮기고, 확인 단추가 거기를 누른다(사용자 요청 st-9 · st-10, 원본에 없다). 커서를 자유롭게 끄는 것은 오른쪽 스틱이다.
/// </summary>
/// <remarks>
/// 창마다 누를 수 있는 자리(<see cref="Targets"/>)를 그 창의 클릭 처리기와 같은 칸으로 모은다 — 새 칸을 누르는 길을 따로 두지 않고 <b>커서를 옮겨 놓고
/// 왼쪽 클릭을 보내는</b> 것이라, 소리 · 올림 그림 · 설명이 마우스로 누를 때와 같다. 커서가 선 자리는 노란 테로 보인다(<see cref="DrawFocus"/>).
/// </remarks>
internal sealed partial class GamePad
{
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Win32.Point point);

    /// <summary>방향을 누르고 있을 때 처음 기다리는 시간과 그 뒤 되풀이 간격(초).</summary>
    private const double SnapDelay = 0.35, SnapEvery = 0.12;

    private double _snapNext;

    /// <summary>
    /// 지금 창에서 누를 수 있는 자리들(판 좌표). 위에 뜬 창이 있으면 그것만 — 확인창 · 슬롯 · 음량 · 시스템 메뉴 · 설정 · Status · 모세스 차례다.
    /// 비어 있으면 아무 데나 눌러도 되는 창(편지 뷰어 · 말풍선 · MISSION)이거나 패드로 못 다루는 창이다.
    /// </summary>
    internal List<(int X, int Y, int W, int H)> Targets()
    {
        var list = new List<(int X, int Y, int W, int H)>();
        if (host.TimedEditScr._open) { host.TimedEditScr.AddTargets(list); return list; }   // 개발 > 타이밍 어택 수정 창
        // 모드 창은 어느 화면 위에서든 먼저 받는다 — 탭, 그 탭의 줄, 닫기(시스템 메뉴의 MODE 로 패드에서도 연다, menu-34).
        var tuning = host.TuningScr;
        if (tuning._tuningOpen)
        {
            var (x, y) = tuning.TuningOrigin();
            int boxX = x + TuningScreen.TuningBoxX, boxY = y + tuning.TuningBoxY;
            if (tuning._tuningListOpen)
            {
                for (int i = 0; i < TuningScreen.SoulWeightChoices.Length; i++)
                    list.Add((boxX, boxY + TuningScreen.TuningRowH * (i + 1), TuningScreen.TuningBoxW, TuningScreen.TuningRowH));
                return list;
            }
            for (int t = 0; t < TuningScreen.TabNames.Length; t++)
                list.Add((x + 12 + t * (TuningScreen.TabW + 4), y + TuningScreen.TabY, TuningScreen.TabW, TuningScreen.TabH));
            if (tuning._tuningTab == TuningScreen.TabStory)
                for (int i = 0; i < GameWindow.TalkPauseChoices.Length; i++)
                    list.Add((x + 16 + i * (TuningScreen.PauseW + TuningScreen.PauseGap), y + TuningScreen.PauseY, TuningScreen.PauseW, TuningScreen.PauseH));
            else
            {
                int rows = tuning.TuningChecks().Length;
                for (int i = 0; i < rows; i++)
                    list.Add((x + 12, y + TuningScreen.TuningCheckY + i * TuningScreen.TuningCheckH, TuningScreen.TuningW - 24, TuningScreen.TuningCheckH - 4));
                if (tuning.ShowsVoiceVolume)
                    for (int i = 0; i < TuningScreen.VoiceChoices.Length; i++) list.Add(tuning.VoiceButton(i));
                if (tuning.ShowsSoulWeight) list.Add((boxX, boxY, TuningScreen.TuningBoxW, TuningScreen.TuningRowH));
            }
            list.Add((x + TuningScreen.TuningW - 116, y + TuningScreen.TuningH - 40, 100, 28));   // 닫기
            return list;
        }
        var sys = host.Sys;
        if (sys._confirm != null)
        {
            var (cx, cy, cw, ch) = sys.ConfirmRect();
            list.Add((cx + cw / 2 - 86, cy + ch - 34, 76, 26));   // YES
            list.Add((cx + cw / 2 + 10, cy + ch - 34, 76, 26));   // NO
            return list;
        }
        var slots = host.SlotsScr;
        if (slots.SlotsOpen)
        {
            var (x, y) = slots.SlotsOrigin();
            for (int r = 0; r < SlotsScreen.SlotsVisible && slots._slotsTop + r < slots.SlotRows; r++)
                list.Add((x + SlotsScreen.SlotPad, y + SlotsScreen.SlotPad + r * SlotsScreen.SlotRowH, SlotsScreen.SlotRowW, SlotsScreen.SlotRowH));
            list.Add((x + 296, y - 24, 18, 18));                                            // 닫기 X
            return list;
        }
        if (sys._volumeWindow)
        {
            var (x, y) = sys.VolumeOrigin();
            foreach (int rowY in new[] { 40, 90 })
                for (int cell = 0; cell < SystemMenu.VolumeCells; cell++) list.Add((x + 20 + cell * 9, y + rowY, 9, 13));
            list.Add((x + 70, y + 116, 130, 18));                                           // 배경음악 체크
            return list;
        }
        if (sys._missionWindow) return list;
        if (sys._systemMenu)
        {
            var (x, y, _) = sys.SystemMenuRect();
            for (int i = 0; i < sys.MenuItems.Length; i++)
                list.Add((x + SystemMenu.SystemPad, y + SystemMenu.SystemPad + i * SystemMenu.SystemRowH, SystemMenu.SystemRowW, SystemMenu.SystemRowH));
            return list;
        }
        var settings = host.SettingsScr;
        if (settings._open)
        {
            var (x, y) = settings.Origin();
            for (int i = 0; i < SettingsScreen.Pages.Length; i++)
                list.Add((x + 12, y + 40 + i * SettingsScreen.SideRowH, SettingsScreen.SideW, SettingsScreen.SideRowH - 4));
            int px = x + SettingsScreen.SideW + 30, top = y + 44;
            if (settings._page == 3)
            {
                for (int i = 0; i < All.Length; i++)
                    list.Add((px, top + 26 + i * SettingsScreen.PadRowH, SettingsScreen.W - SettingsScreen.SideW - 46, SettingsScreen.PadRowH - 4));
                list.Add((px, y + SettingsScreen.H - 40, 120, 28));                          // 기본값으로
                list.Add((x + SettingsScreen.W - 116, y + SettingsScreen.H - 40, 100, 28));    // 닫기
                return list;
            }
            var rows = settings.Rows();
            for (int i = 0; i < rows.Length; i++)
            {
                int ry = top + i * SettingsScreen.RowH;
                if (rows[i].Step != null) { list.Add((px + 170, ry, 24, 24)); list.Add((px + 170 + 24 + 184, ry, 24, 24)); }
                else if (rows[i].Toggle != null) list.Add((px, ry, 200, 24));
                else if (rows[i].Press != null) list.Add((px, ry, 200, 26));
            }
            list.Add((x + SettingsScreen.W - 116, y + SettingsScreen.H - 40, 100, 28));    // 닫기
            return list;
        }
        var st = host.StatusScr;
        if (host._statusUnit >= 0)
        {
            if (st._statusTip != null) return list;
            if (st._popup is { } popup)
            {
                var (px, py, _) = st.PopupRect();
                int lx = px + StatusScreen.PopupListX, lw = st._popupW - 2 * StatusScreen.PopupListX - StatusScreen.PopupScrollW, top = py + 30;
                for (int r = 0; r < st._popupRows && st._popupTop + r < popup.Count; r++) list.Add((lx, top + r * StatusScreen.PopupRowH, lw, StatusScreen.PopupRowH));
                list.Add((px + st._popupW - StatusScreen.PopupCloseW - 6, py + 5, StatusScreen.PopupCloseW, StatusScreen.PopupCloseW));
                foreach (var (x, y, w, h, _) in st._popupHits) list.Add((x, y, w, h));
                return list;
            }
            var (ox, oy) = st.StatusOrigin();
            foreach (var (x, y, w, h, _) in st._statusHits) list.Add((x, y, w, h));
            list.Add((ox + StatusScreen.CloseX, oy + StatusScreen.CloseY, StatusScreen.CloseW, StatusScreen.CloseH));
            return list;
        }
        if (host.Btl._deployOpen)
        {
            // 캐릭터 배치(fa-42 · fa-43) — 칸 고르기 중에만 초록 칸 사이를 옮긴다. 명단 · 단추는 단추에 바로 묶여 있다(Poll).
            if (host.Btl._deployCellMode)
                foreach (var (col, row, _) in host.Btl._deploySpots)
                    list.Add((col * GameWindow.TileW, host.CellTop(col, row), GameWindow.TileW, GameWindow.TileH));
            return list;
        }
        if (host.Btl._itemMenu)
        {
            var (x, y, _) = host.Btl.ItemMenuRect();
            int count = host.Btl.ItemRowsList().Count;
            for (int r = 0; r < BattleScene.ItemRows && host.Btl._itemTop + r < count; r++)
                list.Add((x + 8, y + 12 + r * BattleScene.ItemRowH, BattleScene.ItemRowW + 8, BattleScene.ItemRowH));
            return list;
        }
        if (host.Fld._fieldChoices is { Count: > 0 } choices)
        {
            for (int k = 0; k < choices.Count; k++) { var (x, y) = host.Fld.FieldChoiceBand(k, choices.Count); list.Add((x, y, 620, 32)); }
            return list;
        }
        // 연대표 — 고를 수 있는(열려 있고 아직 안 고른, 지금 보이는) 에피소드 칸들과 스크롤 화살표 둘(사용자 보고 menu-40: 패드가 안 먹었다).
        var episodes = host.EpisodesScr;
        if (episodes._episodesOpen)
        {
            var (ox, oy) = host.Mos.MosesOrigin();
            foreach (var e in episodes.Episodes())
            {
                if (!episodes.EpisodeOpen(e) || episodes._episodesPicked.Contains(e.No) || !episodes.EpisodeRowShown(e.No)) continue;
                var (x, y) = episodes.EpisodeCell(e.No);
                list.Add((ox + x, oy + y, EpisodesScreen.EpisodeCellW, EpisodesScreen.EpisodeCellH));
            }
            if (episodes.EpisodeRowCount() > EpisodesScreen.EpisodeRows)
            {
                list.Add((ox + EpisodesScreen.EpisodeBarX, oy + EpisodesScreen.EpisodeBarY, EpisodesScreen.EpisodeArrowW, EpisodesScreen.EpisodeArrowH));
                list.Add((ox + EpisodesScreen.EpisodeBarX, oy + EpisodesScreen.EpisodeBarY + EpisodesScreen.EpisodeBarH - EpisodesScreen.EpisodeArrowH,
                          EpisodesScreen.EpisodeArrowW, EpisodesScreen.EpisodeArrowH));
            }
            return list;
        }
        if (host.TitleScr._titleOpen)
        {
            var (ox, oy) = host.Mos.MosesOrigin();
            foreach (var (x, y, _) in TitleScreen.TitleButtons) list.Add((ox + x, oy + y, TitleScreen.TitleButtonW, TitleScreen.TitleButtonH));
            var (bx, by, bw, bh) = TitleScreen.BugButton;   // 오른쪽 아래 「버그 리포트」
            list.Add((ox + bx, oy + by, bw, bh));
            return list;
        }
        if (host.Mos._mosesOpen) MosesTargets(list);
        return list;
    }

    /// <summary>모세스 페이지마다의 자리 — 각 페이지의 클릭 처리기(<c>OnMoses…Click</c>)가 보는 칸 그대로다.</summary>
    private void MosesTargets(List<(int X, int Y, int W, int H)> list)
    {
        var mos = host.Mos;
        var (ox, oy) = mos.MosesOrigin();
        void Add(int x, int y, int w, int h) => list.Add((ox + x, oy + y, w, h));
        void PartyFaces(int count)
        {
            for (int i = 0; i < count && i < 5; i++) Add(70 * i + 48, 330, 64, 130);
            if (mos.StyleParty().Count >= 6) { Add(20, 370, 20, 50); Add(400, 370, 20, 50); }
        }
        void Scroll(int x, int y, int h) { Add(x, y, 16, h / 2); Add(x, y + h / 2, 16, h - h / 2); }

        switch (mos._mosesPage)
        {
            case 3 or 4 when mos._shop != null:
                for (int i = 0; i < MosesScene.ShopLists.Length; i++)
                {
                    var (lx, ly, lw) = MosesScene.ShopLists[i];
                    int count = mos.ShopListItems(i).Count;
                    for (int r = 0; r < MosesScene.ShopRows && mos._shopTop[i] + r < count; r++) Add(lx, ly + r * MosesScene.ShopRowH, lw, MosesScene.ShopRowH);
                    if (count > MosesScene.ShopRows) Scroll(lx + lw, ly, MosesScene.ShopRows * MosesScene.ShopRowH);
                }
                if (mos.ShopCompares && mos.StyleParty().Count >= 6) { Add(20, 370, 20, 50); Add(400, 370, 20, 50); }
                Add(418, 294, 68, 27);                                                      // 취소
                Add(522, 294, 68, 27);                                                      // 결제
                Add(455, 430, 163, 27);                                                     // 나가기
                return;
            case 7:
                PartyFaces(mos.StylePartyPage().Count);
                for (int i = 0; i < mos.StyleFamilies().Count && i < MosesScene.StyleFamilyCells.Length; i++)
                {
                    var (fx, fy, fw, fh) = MosesScene.StyleFamilyCells[i];
                    Add(fx, fy, fw, fh);
                }
                foreach (var (slot, _) in mos.StyleTierJobs())
                {
                    var cell = MosesScene.StyleTierCells[slot];
                    Add(cell.X, cell.Y, cell.W, cell.H);
                }
                for (int i = 0; i < mos.StyleJobs().Count && i < MosesScene.StyleBodyCells.Length; i++)
                {
                    var (cx, cy) = MosesScene.StyleBodyCells[i];
                    Add(cx, cy, 68, 28);
                }
                Add(455, 390, 68, 27);                                                      // STATUS
                Add(455, 430, 178, 27);                                                     // 나가기
                return;
            case 6:
            {
                PartyFaces(mos.StylePartyPage().Count);
                int count = mos.LegionList().Count;
                for (int r = 0; r < MosesScene.LegionRows && mos._legionTop + r < count; r++) Add(445, 70 + r * MosesScene.LegionRowH, MosesScene.LegionRowW, MosesScene.LegionRowH);
                if (count > MosesScene.LegionRows) Scroll(445 + MosesScene.LegionRowW, 70, MosesScene.LegionRows * MosesScene.LegionRowH);
                Add(70, 293, 68, 28);                                                       // 해제
                Add(160, 293, 68, 28);                                                      // 배속
                Add(456, 430, 178, 27);                                                     // 나가기
                return;
            }
            case 1:
            {
                if (mos._mailOpen >= 0) return;                                             // 뷰어는 아무 데나 누르면 닫힌다
                int count = mos.Mails().Count;
                for (int r = 0; r < MosesScene.MailRows && mos._mailTop + r < count; r++) Add(MosesScene.MailListX, MosesScene.MailListY + r * MosesScene.MailRowH, MosesScene.MailRowW, MosesScene.MailRowH);
                if (count > MosesScene.MailRows)
                {
                    Add(MosesScene.MailBarX, MosesScene.MailBarY, MosesScene.MailArrowW, MosesScene.MailArrowH);
                    Add(MosesScene.MailBarX, MosesScene.MailBarY + MosesScene.MailBarH - MosesScene.MailArrowH, MosesScene.MailArrowW, MosesScene.MailArrowH);
                }
                Add(455, 430, 163, 27);                                                     // 나가기
                break;
            }
            case 2:
            {
                if (mos._talkPick >= 0) return;                                             // 말풍선은 아무 데나 누르면 접힌다
                var people = mos.TalkPeople();
                for (int i = 0; i < people.Count && i < 8; i++)
                {
                    var (px, py) = mos.TalkSpot(people[i]);
                    Add(px - 20, py - 24, 40, 48);
                }
                Add(46, 244, 25, 26);                                                       // 뒤로
                break;
            }
            case 0 when mos._mosesStep == 1:
            {
                foreach (var planet in mos.MosesSystemPlanets()) Add(planet.X - 20, planet.Y - 20, 40, 40);
                var others = mos.MosesOtherSystems();
                for (int i = 0; i < others.Count; i++) list.Add(mos.MosesSystemButtonRect(i, ox, oy));
                Add(46, 244, 25, 26);
                break;
            }
            case 0 or 5:
                foreach (var cell in mos.MosesCellList()) Add(cell.X, cell.Y, 178, 27);
                Add(46, 244, 25, 26);
                break;
        }
        if (mos.MosesDockShown)
            foreach (var icon in MosesScene.MosesIcons) Add(icon.X + MosesScene.DockHitDx, icon.Y, MosesScene.DockHitW, MosesScene.DockHitH);
    }

    /// <summary>
    /// 창이나 페이지가 바뀌었으면 커서를 <b>첫 항목</b>에 올린다 — 행성에서 장소 목록으로 들어가면 첫 장소가, Status 를 열면 첫 장비 칸이 바로
    /// 골라져 있다(사용자 요청 mo-12). 자리 목록은 「항목 → 단추(STATUS · 나가기 · 닫기) → 도크」 차례라 첫 자리가 곧 첫 항목이다.
    /// </summary>
    internal void AutoFocus()
    {
        var targets = Targets();
        var first = targets.Count == 0 ? default : targets[0];
        // 모세스는 페이지가 달라도 첫 칸 자리가 같을 수 있다(장소 목록 ↔ PARTY 의 전직 · 용병관리) — 페이지 · 단계도 함께 본다(사용자 보고 mo-18).
        int page = host.Mos._mosesOpen ? host.Mos._mosesPage * 8 + host.Mos._mosesStep : -1;
        if (first == _lastFirst && page == _lastPage) return;   // 같은 창이다 — 목록을 굴려 줄 수만 달라진 것도 첫 자리는 그대로다
        _lastFirst = first;
        _lastPage = page;
        if (targets.Count == 0) return;
        // 성도에서는 첫 행성이 아니라 <b>전투 · 필드 장소가 있는 행성</b>부터(사용자 요청 mo-15) — 상점뿐인 행성은 뒤로 미룬다.
        int pick = PreferredPlanet();
        var at = pick >= 0 && pick < targets.Count ? targets[pick] : first;
        MoveCursorTo(at.X + at.W / 2, at.Y + at.H / 2);
    }

    /// <summary>성도에서 먼저 고를 행성의 차례(행성은 자리 목록의 맨 앞이다) — 장소가 있는 첫 행성, 없으면 0. 성도가 아니면 −1.</summary>
    private int PreferredPlanet()
    {
        var mos = host.Mos;
        if (!mos._mosesOpen || mos._mosesPage != 0 || mos._mosesStep != 1) return -1;
        var planets = mos.MosesSystemPlanets();
        for (int i = 0; i < planets.Count; i++) if (mos.PlanetHasMission(planets[i])) return i;
        return planets.Count > 0 ? 0 : -1;
    }

    /// <summary>지난 틀의 첫 자리 — 달라지면 다른 창(또는 다른 페이지)이 뜬 것이다.</summary>
    private (int X, int Y, int W, int H) _lastFirst;

    /// <summary>지난 틀의 모세스 페이지 · 단계(모세스가 아니면 −1).</summary>
    private int _lastPage = -1;

    /// <summary>
    /// 성도(행성 고르기)에서 LB · RB — 커서를 이전 · 다음 행성으로(끝에서 반대쪽으로 돈다). 행성 위가 아니면 장소가 있는 첫 행성으로. 성도가 아니면 false.
    /// </summary>
    internal bool CycleStarMap(int step)
    {
        var mos = host.Mos;
        if (!mos._mosesOpen || mos._mosesPage != 0 || mos._mosesStep != 1 || host.Sys.SystemOpen || host._statusUnit >= 0) return false;
        var planets = mos.MosesSystemPlanets();
        if (planets.Count == 0) return true;
        var (ox, oy) = mos.MosesOrigin();
        var (mx, my) = host._mouse;
        int cur = -1;
        for (int i = 0; i < planets.Count && cur < 0; i++)
            if (Math.Abs(mx - ox - planets[i].X) <= 20 && Math.Abs(my - oy - planets[i].Y) <= 20) cur = i;
        int next = cur < 0 ? Math.Max(0, PreferredPlanet()) : ((cur + step) % planets.Count + planets.Count) % planets.Count;
        MoveCursorTo(ox + planets[next].X, oy + planets[next].Y);
        return true;
    }

    /// <summary>자리 목록을 차례대로 돈다(LB · RB) — 커서가 어느 자리에도 없으면 첫 자리로. 끝에서 반대쪽으로 돈다.</summary>
    internal void CycleTargets(int step)
    {
        var targets = Targets();
        if (targets.Count == 0) return;
        int cur = At(targets, host._mouse.X, host._mouse.Y);
        int next = cur < 0 ? 0 : ((cur + step) % targets.Count + targets.Count) % targets.Count;
        MoveCursorTo(targets[next].X + targets[next].W / 2, targets[next].Y + targets[next].H / 2);
    }

    /// <summary>LT 를 누른 채 오른쪽 스틱 — 전투 맵을 민다(사용자 요청 fa-33). 밀었으면 true.</summary>
    private bool ScrollMap(int x, int y, double dt)
    {
        double ax = Axis(x), ay = Axis(y);
        if (ax == 0 && ay == 0) return false;
        host.Btl.ScrollCameraX(ax * MapScrollSpeed * dt);
        host.Btl.ScrollCamera(-ay * MapScrollSpeed * dt);   // 스틱은 위가 +, 화면은 아래가 +
        return true;
    }

    private const double MapScrollSpeed = 700;   // 스틱을 끝까지 밀었을 때 1초에 미는 판 점

    /// <summary>커서 아래 칸에 걸어가서 손댈 수 있는 물체(상자 · 문)가 있나 — 커서 모양이 주먹이 되는 것과 같은 판정.</summary>
    private bool TouchableUnderCursor()
    {
        var btl = host.Btl;
        var (mx, my) = host._mouse;
        if (!host._battleLoaded || !btl.IsPlayerTurn || btl._turn < 0 || my < GameWindow.GridTop || btl._targetWork >= 0 || btl._ringUnit >= 0 || btl._abilityMenu) return false;
        int row = host.RowAt(mx, my);
        return row >= 0 && btl.ObjectAt(mx / GameWindow.TileW, row) is { } obj && btl.FindTouchPath(host._units[btl._turn], obj) != null;
    }

    private DemoObject? _pointedObj;

    /// <summary>
    /// 내 차례의 인물이 상자 · 문 <b>바로 옆</b>에 서면 커서를 그 물체 위로 옮긴다 — 커서가 주먹으로 바뀌고 확인이 연다(사용자 요청 fa-35).
    /// 한 물체에 한 번만 — 그 뒤 커서를 딴 데로 옮겨도 다시 끌어오지 않는다. 물체에서 떨어지면 다시 잰다.
    /// </summary>
    internal void PointAtChest()
    {
        var btl = host.Btl;
        if (!host._battleLoaded || !btl.IsPlayerTurn || btl._turn < 0 || btl._targetWork >= 0 || btl._ringUnit >= 0 || btl._abilityMenu || host._units[btl._turn].IsBusy)
            return;
        var u = host._units[btl._turn];
        DemoObject? near = null;
        foreach (var o in btl.Objects)
            if (o.Alive && !btl._opened.Contains(o) && o.Data.Kind is 2 or 6 or 8 && Math.Abs(o.Col - u.Col) + Math.Abs(o.Row - u.Row) == 1
                && btl.FindTouchPath(u, o) is { Count: 0 }) { near = o; break; }
        if (near == _pointedObj) return;
        _pointedObj = near;
        if (near != null) MoveCursorTo(near.Col * GameWindow.TileW + GameWindow.TileW / 2, host.CellTop(near.Col, near.Row) + GameWindow.TileH / 2);
    }

    /// <summary>커서가 선 자리 — 없으면 −1.</summary>
    private static int At(List<(int X, int Y, int W, int H)> targets, int mx, int my)
    {
        // 칸이 겹치면(전직 화면의 부채꼴 계열 단추) 가운데가 더 가까운 칸이다 — 앞 칸이 먼저 받으면 겹친 칸으로는 못 넘어간다(mo-20).
        int best = -1;
        long bestFar = long.MaxValue;
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (mx < t.X || mx >= t.X + t.W || my < t.Y || my >= t.Y + t.H) continue;
            long ax = mx - (t.X + t.W / 2), ay = my - (t.Y + t.H / 2), far = ax * ax + ay * ay;
            if (far < bestFar) { bestFar = far; best = i; }
        }
        return best;
    }

    /// <summary>
    /// 커서를 그 쪽의 다음 자리로 옮긴다 — 커서가 어느 자리에도 없으면 가장 가까운 자리로. 그 쪽에 자리가 없으면 그대로 둔다.
    /// 누를 수 있는 자리가 하나도 없는 창이면 false(방향을 키로 넘긴다).
    /// </summary>
    internal bool Snap(int dx, int dy)
    {
        var targets = Targets();
        if (targets.Count == 0) return false;
        var (mx, my) = host._mouse;
        int cur = At(targets, mx, my), best = -1, aside_ = -1;
        double bestScore = double.MaxValue, asideScore = double.MaxValue;
        for (int i = 0; i < targets.Count; i++)
        {
            if (i == cur) continue;
            var t = targets[i];
            int tx = t.X + t.W / 2, ty = t.Y + t.H / 2;
            if (cur < 0)
            {
                double near = Math.Abs(tx - mx) + Math.Abs(ty - my);
                if (near < bestScore) { bestScore = near; best = i; }
                continue;
            }
            var c = targets[cur];
            int ax = tx - (c.X + c.W / 2), ay = ty - (c.Y + c.H / 2);
            // 그 쪽으로 간 만큼이 앞이고, 옆으로 벗어난 만큼은 벌점 — 나란한 줄은 바로 다음 줄이, 아니면 가장 가까운 것이 이긴다.
            int along = ax * dx + ay * dy, aside = Math.Abs(ax * dy) + Math.Abs(ay * dx);
            if (along <= 0) continue;
            double score = along + aside * (aside <= along ? 1 : 3);
            // 그 쪽 부채꼴(옆으로 벗어난 것이 간 것의 두 곱절 안) 밖은 뒷순위 — 목록을 굴릴 수 있으면 굴리는 것이 먼저다(menu-29: 로드 목록 맨 윗줄에서
            // ↑ 가 저 멀리 오른쪽 위 X 로 갔다).
            if (aside <= along * 2) { if (score < bestScore) { bestScore = score; best = i; } }
            else if (score < asideScore) { asideScore = score; aside_ = i; }
        }
        if (best >= 0) MoveCursorTo(targets[best].X + targets[best].W / 2, targets[best].Y + targets[best].H / 2);
        // 시스템 메뉴는 한 줄짜리 목록이라 끝에서 반대쪽 끝으로 돈다 — 맨 위 MISSION 에서 ↑ 는 EXIT GAME, 맨 아래에서 ↓ 는 맨 위(사용자 요청 menu-66).
        else if (dy != 0 && cur >= 0 && host.Sys._systemMenu && targets.Count == host.Sys.MenuItems.Length)
        {
            var end = targets[dy < 0 ? targets.Count - 1 : 0];
            MoveCursorTo(end.X + end.W / 2, end.Y + end.H / 2);
        }
        // 그 방향에 자리가 없으면 목록을 한 줄 굴린다 — 로드 슬롯처럼 보이는 줄 너머가 있는 목록은 커서 밑에서 다음 줄이 올라온다(menu-27).
        else if (dy != 0 && cur >= 0 && CanScroll(dy)) PostMouse(Win32.WM_MOUSEWHEEL, dy < 0 ? 120 : -120);
        else if (aside_ >= 0) MoveCursorTo(targets[aside_].X + targets[aside_].W / 2, targets[aside_].Y + targets[aside_].H / 2);
        else if (dy != 0 && cur >= 0) PostMouse(Win32.WM_MOUSEWHEEL, dy < 0 ? 120 : -120);
        return true;
    }

    /// <summary>지금 창의 목록이 그 쪽(위 −1 · 아래 +1)으로 더 굴러갈 수 있나 — 모르는 창이면 false.</summary>
    private bool CanScroll(int dy)
    {
        var slots = host.SlotsScr;
        if (slots.SlotsOpen) return dy < 0 ? slots._slotsTop > 0 : slots._slotsTop < slots.SlotRows - SlotsScreen.SlotsVisible;
        var st = host.StatusScr;
        if (host._statusUnit >= 0) return st._popup is { } popup && (dy < 0 ? st._popupTop > 0 : st._popupTop + st._popupRows < popup.Count);
        var mos = host.Mos;
        if (mos._mosesOpen)
            switch (mos._mosesPage)
            {
                case 1: return dy < 0 ? mos._mailTop > 0 : mos._mailTop + MosesScene.MailRows < mos.Mails().Count;
                case 6: return dy < 0 ? mos._legionTop > 0 : mos._legionTop + MosesScene.LegionRows < mos.LegionList().Count;
                case 3 or 4:
                {
                    int list = mos.ShopRowAt(host._mouse.X, host._mouse.Y).List;
                    return list >= 0 && (dy < 0 ? mos._shopTop[list] > 0 : mos._shopTop[list] + MosesScene.ShopRows < mos.ShopListItems(list).Count);
                }
            }
        if (host.Btl._itemMenu) return dy < 0 ? host.Btl._itemTop > 0 : host.Btl._itemTop + BattleScene.ItemRows < host.Btl.ItemRowsList().Count;
        return false;
    }

    /// <summary>마우스 커서를 판의 그 자리로 옮긴다.</summary>
    private void MoveCursorTo(int boardX, int boardY)
    {
        var p = new Win32.Point
        {
            X = (int)Math.Round((boardX - host._camX) * host._zoom) + host.ViewOffsetX,
            Y = (int)Math.Round((boardY - host._camY) * host._zoom) + host.ViewOffsetY,
        };
        if (!GameWindow.Offscreen && ClientToScreen(host._hwnd, ref p)) SetCursorPos(p.X, p.Y);   // 화면 밖 시험에서는 진짜 커서를 안 건드린다
        host._mouse = (boardX, boardY);   // 같은 틀에 누르는 것도 이 자리로
    }

    /// <summary>방향이 눌리거나(바로) 눌린 채면(간격마다) 커서를 건너뛴다. 패드가 창을 다루는 중이면 true.</summary>
    private bool SnapByPad(int pressed, int held)
    {
        int dir = (pressed & (Up | Down | Left | Right)) != 0 ? pressed : held;
        dir &= Up | Down | Left | Right;
        if (dir == 0) return false;
        bool repeat = (pressed & dir) == 0;
        if (repeat && host._realTime < _snapNext) return true;
        _snapNext = host._realTime + (repeat ? SnapEvery : SnapDelay);
        int dx = (dir & Right) != 0 ? 1 : (dir & Left) != 0 ? -1 : 0, dy = (dir & Down) != 0 ? 1 : (dir & Up) != 0 ? -1 : 0;
        if (!Snap(dx, dy)) return false;
        return true;
    }

    /// <summary>커서가 선 자리에 노란 테 — 패드를 잡고 있을 때만. 모든 창 위에 그린다.</summary>
    internal void DrawFocus()
    {
        if (!Active || !Connected || !(PointerMode || host.Btl._deployCellMode)) return;
        // 모세스 화면이 아직 나오는 중(페이드 · 들어오는 단추)이면 테를 안 그린다 — 다 나온 뒤에(mo-16). 위에 뜬 창(Status · 메뉴 · 설정 · 모드)은 바로 그린다.
        if (host.Mos._mosesOpen && host._statusUnit < 0 && !host.Sys.SystemOpen && !host.SettingsScr._open && !host.TuningScr._tuningOpen && !host.TimedEditScr._open
            && host.Mos.UiSettling) return;
        var targets = Targets();
        int cur = At(targets, host._mouse.X, host._mouse.Y);
        if (cur < 0) return;
        var (x, y, w, h) = targets[cur];
        // 새 UI 로 그린 창(설정 · 모드 …)이 떠 있으면 테도 그 층에 그린다 — 판에 그리면 창 밑에 깔린다.
        if (host._hiResUi && host.Hi.Used)
        {
            host.Hi.StrokeRect(x - 2, y - 2, w + 4, h + 4, FocusColor);
            host.Hi.StrokeRect(x - 3, y - 3, w + 6, h + 6, 0x80000000 | (FocusColor & 0xFFFFFF));
            return;
        }
        StrokeFocus(x, y, w, h);
    }

    /// <summary>커서 테 — 모서리를 3 만큼 둥글린 테(사용자 요청). 안쪽은 또렷하게, 바깥쪽은 반투명으로 한 겹 더. 다른 창(배치 명단)도 같은 테를 쓴다.</summary>
    internal void StrokeFocus(int x, int y, int w, int h)
    {
        StrokeRound(x - 2, y - 2, w + 4, h + 4, 3, FocusColor);
        StrokeRound(x - 3, y - 3, w + 6, h + 6, 4, 0x80000000 | (FocusColor & 0xFFFFFF));
    }

    /// <summary>모서리가 둥근 한 점 두께의 테 — 네 변은 모서리만큼 비우고, 모서리는 사분원 점으로 잇는다.</summary>
    private void StrokeRound(int x, int y, int w, int h, int r, uint color)
    {
        r = Math.Min(r, Math.Min(w, h) / 2);
        host.FillRect(x + r, y, w - 2 * r, 1, color);
        host.FillRect(x + r, y + h - 1, w - 2 * r, 1, color);
        host.FillRect(x, y + r, 1, h - 2 * r, color);
        host.FillRect(x + w - 1, y + r, 1, h - 2 * r, color);
        // 사분원 — 중심에서 r 만큼 떨어진 점을 한 바퀴의 1/4 씩 찍는다(r 이 작아 점 몇 개면 이어진다).
        foreach (var (cx, cy, sx, sy) in new[] { (x + r, y + r, -1, -1), (x + w - 1 - r, y + r, 1, -1), (x + r, y + h - 1 - r, -1, 1), (x + w - 1 - r, y + h - 1 - r, 1, 1) })
            for (int i = 0; i <= r * 2; i++)
            {
                double a = Math.PI / 2 * i / (r * 2);
                host.SetPixel(cx + sx * (int)Math.Round(r * Math.Cos(a)), cy + sy * (int)Math.Round(r * Math.Sin(a)), color);
            }
    }
}

using System.IO;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 저장·불러오기 슬롯 화면(an-menu-1) — 시스템 메뉴 SAVE·LOAD 를 누르면 뜨는 목록.
/// </summary>
/// <remarks>
/// 옵시디안 분석-시스템메뉴 「2.1b 슬롯 화면 자세한 배치」 그대로:
/// 창 <b>320×264</b>(원본 화면 자리 (160,108)), 제목줄에 "Save"/"Load", 줄 <b>280×24</b>, <b>한 번에 열 줄</b>,
/// 줄 수는 Save 20(슬롯 0~19)·Load 21(20 = 자동 저장). 오른쪽 (304,0) 에 16×264 스크롤 막대(Obs 0071 — 모션 2 위 화살표·4 아래 화살표·6 손잡이, 손잡이 길 232),
/// 닫기 X 는 (296,−24) 18×18(Obs 0970 모션 5). 줄 글월 셋:
/// <list type="bullet">
/// <item>가운데 — 저장할 때 박아 둔 장면 이름 TXR(전투면 Btl 머리 워드 4), 흰색</item>
/// <item>왼끝 x=10 — <c>[%02d:전  투]</c>(챕터면 「챕  터」), 색 <c>0x64ffff</c></item>
/// <item>오른끝 x=270 — <c>%3d:%02d:%02d</c> 논 시간, 색 <c>0x64ff64</c></item>
/// </list>
/// 빈 슬롯은 TXR 0 「없음」 한 줄이고 <b>Load 에서만</b> 그 줄이 꺼진다. 줄 강조는 Obs 0471 모션 20 가로 띠다.
/// 저장하면 Snd 579 와 「저장되었습니다.」 알림창(원본 120틱 — 데모는 40틱, 클릭으로 닫힘).
/// 원본처럼 전투판 전체를 담는다(원본은 이진 파일, 이 데모는 <c>%APPDATA%\DuelDx\battle-save-NN.json</c>).
/// 자동 저장(슬롯 20)은 원본대로 <b>내가 조종하는 인물의 차례가 시작될 때마다</b> 적는다 — DLL 안에서 저장을 부르는 곳은
/// 손 저장 둘과 이것 하나뿐이고, 모세스·필드·챕터 전환·전투 끝에는 자동 저장이 없다(분석-시스템메뉴 2.4·2.4b).
/// </remarks>
internal sealed unsafe class SlotsScreen(GameWindow host)
{
    internal const int SlotsW = 320, SlotsH = 264, SlotRowH = 24, SlotRowW = 280, SlotPad = 12;
    internal const int SlotsVisible = 10, SaveSlots = 20, AutoSlot = 20;
    internal const int SlotScrollObs = 71, SlotHighlightObs = 471, SlotHighlightMotion = 20;
    internal const uint SlotLabelColor = 0xFFFFFF64, SlotTimeColor = 0xFF64FF64;

    /// <summary>−1 닫힘 · 0 Save · 1 Load.</summary>
    internal int _slotsMode = -1;
    internal int _slotsTop, _slotsHover = -1;
    internal (string Title, double Until)? _notice;

    internal bool SlotsOpen => _slotsMode >= 0;
    /// <summary>자동 저장 칸 수 — 20 = 이번 차례 시작, 21 · 22 = 그 앞 두 번의 차례 시작(실수를 되돌리려고 부를 때 쓴다).</summary>
    internal const int AutoSlots = 3;

    internal int SlotRows => _slotsMode == 1 ? SaveSlots + AutoSlots : SaveSlots;

    /// <summary>
    /// 목록 줄 → 슬롯 번호. 원본은 자동 저장(20)을 Load 목록 <b>맨 끝(21번째)</b>에 표시 없이 두는데, 열 줄씩만 보이고
    /// 화살표로 한 줄씩만 내려가서 사실상 안 보였다(사용자 보고). 데모는 Load 목록 <b>맨 위</b>에 「자동 저장」으로 둔다.
    /// </summary>
    /// <remarks>다시 <b>맨 아래</b>에 둔다(사용자 요청) — 20 · 21 · 22 차례로, 「자동 저장」 표시를 달아서.</remarks>
    internal int SlotOfRow(int row) => row;

    /// <summary>슬롯 목록을 줄 단위로 굴린다 — 화살표·마우스 휠.</summary>
    internal void ScrollSlots(int rows) => _slotsTop = Math.Clamp(_slotsTop + rows, 0, Math.Max(0, SlotRows - SlotsVisible));

    internal static string SlotPath(int slot) =>
        UserDataFolder.File($"battle-save-{slot:D2}.json");

    /// <summary>DUELDX_SLOTS=load|save 면 시작하자마자 그 슬롯 목록을 연다(화면 밖 시험용).</summary>
    internal void OpenSlotsIfAsked()
    {
        switch (Environment.GetEnvironmentVariable("DUELDX_SLOTS"))
        {
            case "load": OpenSlots(1); break;
            case "save": OpenSlots(0); break;
        }
    }

    internal void OpenSlots(int mode)
    {
        _slotsMode = mode;
        _slotsTop = 0;
        _slotsHover = -1;
    }

    /// <summary>
    /// 슬롯 창 자리 — 보통은 화면 가운데지만, 「Select your record」 화면에서는 원본대로 <b>(160, 148)</b> 이다.
    /// </summary>
    internal (int X, int Y) SlotsOrigin()
    {
        if (host.RecordsScr._recordsOpen)
        {
            var (ox, oy) = host.Mos.MosesOrigin();
            return (ox + 160, oy + 148);
        }
        return (host._camX + (host.ViewWidth - SlotsW) / 2, host._camY + (host.ViewHeight - SlotsH) / 2 + FrameTitleH / 2);
    }

    /// <summary>그 슬롯에 적힌 머리 — 없으면 null.</summary>
    internal SystemMenu.SaveState? SlotHead(int slot)
    {
        try
        {
            string path = SlotPath(slot);
            return File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<SystemMenu.SaveState>(File.ReadAllText(path), SystemMenu.SaveJson) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return null; }
    }

    internal int SlotAt(int bx, int by)
    {
        var (x, y) = SlotsOrigin();
        int row = (by - y - SlotPad) / SlotRowH;
        if (bx < x + SlotPad || bx >= x + SlotPad + SlotRowW || row < 0 || row >= SlotsVisible) return -1;
        int index = _slotsTop + row;
        return index < SlotRows ? SlotOfRow(index) : -1;
    }

    /// <summary>슬롯 화면이 떠 있으면 클릭을 처리하고 true.</summary>
    internal bool OnSlotsClick(int bx, int by)
    {
        if (!SlotsOpen) return false;
        var (x, y) = SlotsOrigin();

        if (bx >= x + 296 && bx < x + 314 && by >= y - 24 && by < y - 6) { _slotsMode = -1; host.Sys.ReturnToSystemMenu(); return true; }   // 닫기 X

        // 스크롤 막대 — 위·아래 화살표만 다룬다(손잡이 끌기는 없다). 누르고 있으면 되풀이한다(UpdateSlotArrows).
        if (bx >= x + 304 && bx < x + 320 && by >= y && by < y + SlotsH)
        {
            int arrow = SlotArrowAt(bx, by);
            if (arrow != 0)
            {
                ScrollSlots(arrow);
                (_slotArrow, _slotArrowAt, _slotArrowTicks) = (arrow, host._lastTime, 0);
            }
            return true;
        }

        int slot = SlotAt(bx, by);
        if (slot < 0) return true;
        var head = SlotHead(slot);
        if (_slotsMode == 0)
        {
            if (head != null) host.Sys._confirm = ("Save", "이미 저장된 파일이 있습니다.\n덮어쓰시겠습니까?", () => SaveSlot(slot));
            else SaveSlot(slot);
        }
        else
        {
            if (head == null) return true;                                    // 빈 줄은 꺼져 있다
            // 원본 문구는 「데이타」지만 맞춤법대로 「데이터」로 적는다(사용자 요청 menu-13).
            host.Sys._confirm = ("Load", "저장하지 않은 데이터는 없어집니다.\n로드하시겠습니까?", () => LoadSlot(slot));
        }
        return true;
    }

    internal void SaveSlot(int slot)
    {
        // 저장한 뒤에도 슬롯 창은 열린 채다 — 이어서 다른 칸에 저장할 수 있다(원본 vt[0x40] 다시 보이기, fg-22).
        // 실패 알림은 원본 문구(0x100372ae·0x10037693, 120틱): 빈 칸이면 「Error」, 덮어쓰기면 「Save」 머리에 「저장되지 않았습니다.」
        // 저장할 수 없는 때(필드·챕터 사건 도중·내 차례 조종 상태가 아님)는 막는다 — 메뉴가 SAVE 를 꺼 두지만 한 번 더(감사5 S1 방어·S3·S4).
        if (host.Sys.SaveBlockedReason is { } why) { _notice = (why, host._lastTime + 40 / TicksPerSecond); return; }
        if (!host.Sys.SaveBattleTo(SlotPath(slot))) { _notice = ($"{(SlotHead(slot) != null ? "Save" : "Error")} — 저장되지 않았습니다.", host._lastTime + 120 / TicksPerSecond); return; }
        host.Play(SystemMenu.SoundSaved);
        // 원본은 120틱(4초)인데 너무 오래 떠 있다는 요청으로 40틱(약 1.3초)만 띄운다. 클릭하면 바로 닫힌다.
        _notice = ("저장되었습니다.", host._lastTime + 40 / TicksPerSecond);
    }

    internal void LoadSlot(int slot)
    {
        _slotsMode = -1;
        // 깨진 파일 문구(0x1003730e, 120틱).
        if (!host.Sys.LoadBattleFrom(SlotPath(slot))) _notice = ("세이브 파일에 오류가 생겼거나 허가없이 변경되었습니다.\n로드할 수 없습니다.", host._lastTime + 120 / TicksPerSecond);
    }

    /// <summary>자동 저장 슬롯(Load 목록 21번째 줄) — 내 차례가 시작될 때마다 적는다(원본 상태 22, 분석-시스템메뉴 2.4).</summary>
    /// <summary>
    /// 자동 저장 — 원본은 내 인물의 차례가 시작될 때마다 슬롯 20 하나에 덮어쓴다(0x1006acc0). 그러면 실수한 바로 다음 차례에 이미 덮여
    /// 되돌릴 수가 없어서(사용자 보고), 덮어쓰기 전에 앞의 것을 21 · 22 로 한 칸씩 물린다 — 세 차례 전까지 남는다.
    /// </summary>
    internal void AutoSave()
    {
        try
        {
            for (int k = AutoSlot + AutoSlots - 1; k > AutoSlot; k--)
                if (File.Exists(SlotPath(k - 1))) File.Copy(SlotPath(k - 1), SlotPath(k), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 물리기에 실패해도 이번 저장은 한다 */ }
        host.Sys.SaveBattleTo(SlotPath(AutoSlot));
    }

    /// <summary>누르고 있는 스크롤 화살표(−1 위 · +1 아래 · 0 없음) · 누른 때 · 누른 뒤 센 틀 수.</summary>
    internal int _slotArrow;
    internal double _slotArrowAt;
    internal int _slotArrowTicks;

    /// <summary>그 점이 스크롤 막대의 위(−1)·아래(+1) 화살표인가 — 아니면 0.</summary>
    internal int SlotArrowAt(int bx, int by)
    {
        var (x, y) = SlotsOrigin();
        if (bx < x + 304 || bx >= x + 320 || by < y || by >= y + SlotsH) return 0;
        return by < y + 16 ? -1 : by >= y + SlotsH - 16 ? 1 : 0;
    }

    /// <summary>
    /// 화살표 누르고 있기 — 원본 화살표 단추(<c>0x10044960</c>/<c>0x10044b60</c>)는 누를 때 <c>0x10044a80</c> 이 <c>[+0x13c]=1</c> 과 0x2724 한 번,
    /// 틀마다 <c>0x10044b00</c> 이 <c>[+0x13c]++</c> 해서 <b>10 을 넘으면 틀마다</b> 0x2724(<c>0x10044b0e cmp eax,0xa</c>), 떼면 0(감사5 S9).
    /// 데모는 틀을 틱(초당 30)으로 세고, 메시지 하나를 한 줄로 본다(가설). 단추를 떼거나 화살표 밖으로 나가면 멈춘다.
    /// </summary>
    internal void UpdateSlotArrows()
    {
        if (_slotArrow == 0) return;
        if (!SlotsOpen || (Native.Win32.GetKeyState(0x01) & 0x8000) == 0 || SlotArrowAt(host._mouse.X, host._mouse.Y) != _slotArrow) { _slotArrow = 0; return; }
        int held = (int)((host._lastTime - _slotArrowAt) * TicksPerSecond);
        // 누른 틀의 셈이 1 이고 틀마다 하나씩 오른다 — 셈 1 + t 가 10 을 넘는 틀(t ≥ 10)부터 틀마다 한 줄. 늦은 틀은 몇 줄 몰아서(최대 6).
        for (int guard = 0; _slotArrowTicks < held && guard < 6; guard++)
            if (1 + ++_slotArrowTicks > 10) ScrollSlots(_slotArrow);
        if (_slotArrowTicks < held) _slotArrowTicks = held;
    }

    internal void UpdateSlotsHover(int bx, int by)
    {
        if (SlotsOpen) _slotsHover = SlotAt(bx, by);
    }

    internal void DrawSlots()
    {
        if (!SlotsOpen) return;
        var (x, y) = SlotsOrigin();
        int tick = (int)(host._lastTime * TicksPerSecond);

        // 「Select your record」 화면에서는 배경 글씨가 제목 노릇을 해서 제목줄 글자를 안 그린다.
        host.DrawGameFrame(x, y, SlotsW, SlotsH, host.RecordsScr._recordsOpen ? "" : _slotsMode == 0 ? "Save" : "Load");
        if (!host.DrawUi(FrameObs, 5, 0, x + 296, y - 24, GameWindow.UiBlend.Alpha))
            host.StrokeRect(x + 296, y - 24, 18, 18, White);

        for (int i = 0; i < SlotsVisible; i++)
        {
            if (_slotsTop + i >= SlotRows) break;
            int slot = SlotOfRow(_slotsTop + i);
            int rx = x + SlotPad, ry = y + SlotPad + i * SlotRowH;
            var head = SlotHead(slot);
            bool dim = _slotsMode == 1 && head == null;

            if (slot == _slotsHover && !dim && !host.DrawUi(SlotHighlightObs, SlotHighlightMotion, tick, rx, ry, GameWindow.UiBlend.Alpha))
                host.FillRect(rx, ry, SlotRowW, SlotRowH, 0x4060A0FF);

            if (head == null)
            {
                string none = host._db?.T(0) is { Length: > 0 } t ? t : "없음";
                if (slot >= AutoSlot) none = $"자동 저장{(slot > AutoSlot ? $" {slot - AutoSlot}차례 전" : "")} — {none}";
                var (_, nw, nh) = host.GetText(none, White);
                host.DrawText(none, rx + (SlotRowW - nw) / 2, ry + (SlotRowH - nh) / 2, dim ? DimGray : White);
                continue;
            }

            string title = host._db?.T((ushort)head.SceneText) is { Length: > 0 } s ? s : "";
            var (_, tw, _) = host.GetText(title, White);
            string label = slot == AutoSlot ? "[자동 저장]" : slot > AutoSlot ? $"[자동 {slot - AutoSlot}차례 전]" : $"[{slot:D2}:{head.SceneKind switch { 4 => "챕  터", 7 => "연대표", _ => "전  투" }}]";
            // 줄 높이는 표시 글로 잰다 — 연대표 세이브는 이름이 빈 글(TXR 2557)이라 이름으로 재면 높이 0 이 되어 그 줄만 아래로 처졌다.
            var (_, lw, th) = host.GetText(label, SlotLabelColor);
            // 이름은 가운데지만, 표시 글(특히 「[자동 저장]」)과 겹치면 그 뒤로 민다.
            host.DrawText(title, Math.Max(rx + (SlotRowW - tw) / 2, rx + 10 + lw + 8), ry + (SlotRowH - th) / 2, White);
            host.DrawText(label, rx + 10, ry + (SlotRowH - th) / 2, SlotLabelColor);

            long ms = head.PlayMs;
            string time = $"{ms / 3600000,3}:{ms % 3600000 / 60000:D2}:{ms % 60000 / 1000:D2}";
            var (_, pw, _) = host.GetText(time, White);
            host.DrawText(time, rx + SlotRowW - 10 - pw, ry + (SlotRowH - th) / 2, SlotTimeColor);
        }

        DrawSlotScrollbar(x + 304, y, tick);
    }

    /// <summary>스크롤 막대 — 위·아래 화살표와 손잡이(움직이는 길 232).</summary>
    internal void DrawSlotScrollbar(int x, int y, int tick)
    {
        if (!host.DrawUi(SlotScrollObs, 2, tick, x, y, GameWindow.UiBlend.Alpha)) host.StrokeRect(x, y, 16, 16, White);
        if (!host.DrawUi(SlotScrollObs, 4, tick, x, y + SlotsH - 16, GameWindow.UiBlend.Alpha)) host.StrokeRect(x, y + SlotsH - 16, 16, 16, White);

        int track = SlotsH - 32, span = Math.Max(1, SlotRows - SlotsVisible);
        int handleH = Math.Max(16, track * SlotsVisible / SlotRows);
        int hy = y + 16 + (track - handleH) * _slotsTop / span;
        if (!host.DrawUi(SlotScrollObs, 6, tick, x, hy, GameWindow.UiBlend.Alpha)) host.FillRect(x + 2, hy, 12, handleH, StatusScreen.BoxLine);
    }

    /// <summary>「저장되었습니다.」 같은 알림창 — 원본 메시지 창과 같은 틀.</summary>
    internal void DrawNotice()
    {
        if (_notice is not { } notice) return;
        if (host._lastTime >= notice.Until) { _notice = null; return; }
        var (_, tw, th) = host.GetText(notice.Title, White);
        int w = tw + 60, h = th + 40;
        int x = host._camX + (host.ViewWidth - w) / 2, y = host._camY + (host.ViewHeight - h) / 2;
        host.DrawGameFrame(x, y, w, h);
        host.DrawText(notice.Title, x + (w - tw) / 2, y + (h - th) / 2, White);
    }
}

using System.IO;

namespace DuelDx;

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
/// 저장하면 Snd 579 와 「저장되었습니다.」 알림창(120틱).
/// 원본 세이브는 파티만 담지만 이 데모는 전투판 그대로를 <c>%APPDATA%\DuelDx\battle-save-NN.json</c> 에 담는다.
/// 자동 저장(슬롯 20)은 전투를 시작할 때 쓴다 — 원본이 어디서 쓰는지는 아직 안 봤다(데모 나름).
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int SlotsW = 320, SlotsH = 264, SlotRowH = 24, SlotRowW = 280, SlotPad = 12;
    private const int SlotsVisible = 10, SaveSlots = 20, AutoSlot = 20;
    private const int SlotScrollObs = 71, SlotHighlightObs = 471, SlotHighlightMotion = 20;
    private const uint SlotLabelColor = 0xFFFFFF64, SlotTimeColor = 0xFF64FF64;

    /// <summary>−1 닫힘 · 0 Save · 1 Load.</summary>
    private int _slotsMode = -1;
    private int _slotsTop, _slotsHover = -1;
    private (string Title, double Until)? _notice;

    private bool SlotsOpen => _slotsMode >= 0;
    private int SlotRows => _slotsMode == 1 ? AutoSlot + 1 : SaveSlots;

    private static string SlotPath(int slot) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuelDx", $"battle-save-{slot:D2}.json");

    private void OpenSlots(int mode)
    {
        _slotsMode = mode;
        _slotsTop = 0;
        _slotsHover = -1;
    }

    /// <summary>
    /// 슬롯 창 자리 — 보통은 화면 가운데지만, 「Select your record」 화면에서는 원본대로 <b>(160, 148)</b> 이다.
    /// </summary>
    private (int X, int Y) SlotsOrigin()
    {
        if (_recordsOpen)
        {
            var (ox, oy) = MosesOrigin();
            return (ox + 160, oy + 148);
        }
        return (_camX + (ViewWidth - SlotsW) / 2, _camY + (ViewHeight - SlotsH) / 2 + FrameTitleH / 2);
    }

    /// <summary>그 슬롯에 적힌 머리 — 없으면 null.</summary>
    private SaveState? SlotHead(int slot)
    {
        try
        {
            string path = SlotPath(slot);
            return File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<SaveState>(File.ReadAllText(path), SaveJson) : null;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { return null; }
    }

    private int SlotAt(int bx, int by)
    {
        var (x, y) = SlotsOrigin();
        int row = (by - y - SlotPad) / SlotRowH;
        if (bx < x + SlotPad || bx >= x + SlotPad + SlotRowW || row < 0 || row >= SlotsVisible) return -1;
        int slot = _slotsTop + row;
        return slot < SlotRows ? slot : -1;
    }

    /// <summary>슬롯 화면이 떠 있으면 클릭을 처리하고 true.</summary>
    private bool OnSlotsClick(int bx, int by)
    {
        if (!SlotsOpen) return false;
        var (x, y) = SlotsOrigin();

        if (bx >= x + 296 && bx < x + 314 && by >= y - 24 && by < y - 6) { _slotsMode = -1; return true; }   // 닫기 X

        // 스크롤 막대 — 위·아래 화살표만 다룬다(손잡이 끌기는 없다)
        if (bx >= x + 304 && bx < x + 320 && by >= y && by < y + SlotsH)
        {
            if (by < y + 16) _slotsTop = Math.Max(0, _slotsTop - 1);
            else if (by >= y + SlotsH - 16) _slotsTop = Math.Min(SlotRows - SlotsVisible, _slotsTop + 1);
            return true;
        }

        int slot = SlotAt(bx, by);
        if (slot < 0) return true;
        var head = SlotHead(slot);
        if (_slotsMode == 0)
        {
            if (head != null) _confirm = ("Save", "이미 저장된 파일이 있습니다.\n덮어쓰시겠습니까?", () => SaveSlot(slot));
            else SaveSlot(slot);
        }
        else
        {
            if (head == null) return true;                                    // 빈 줄은 꺼져 있다
            _confirm = ("Load", "저장하지 않은 데이타는 없어집니다.\n로드하시겠습니까?", () => LoadSlot(slot));
        }
        return true;
    }

    private void SaveSlot(int slot)
    {
        _slotsMode = -1;
        if (!SaveBattleTo(SlotPath(slot))) { _notice = ("Error", _lastTime + 4); return; }
        Play(SoundSaved);
        _notice = ("저장되었습니다.", _lastTime + 120 / TicksPerSecond);
    }

    private void LoadSlot(int slot)
    {
        _slotsMode = -1;
        if (!LoadBattleFrom(SlotPath(slot))) _notice = ("Error", _lastTime + 4);
    }

    /// <summary>자동 저장 슬롯(Load 목록 21번째 줄) — 내 차례가 시작될 때마다 적는다(원본 상태 22, 분석-시스템메뉴 2.4).</summary>
    private void AutoSave() => SaveBattleTo(SlotPath(AutoSlot));

    private void UpdateSlotsHover(int bx, int by)
    {
        if (SlotsOpen) _slotsHover = SlotAt(bx, by);
    }

    private void DrawSlots()
    {
        if (!SlotsOpen) return;
        var (x, y) = SlotsOrigin();
        int tick = (int)(_lastTime * TicksPerSecond);

        // 「Select your record」 화면에서는 배경 글씨가 제목 노릇을 해서 제목줄 글자를 안 그린다.
        DrawGameFrame(x, y, SlotsW, SlotsH, _recordsOpen ? "" : _slotsMode == 0 ? "Save" : "Load");
        if (!DrawUi(FrameObs, 5, 0, x + 296, y - 24, UiBlend.Alpha))
            StrokeRect(x + 296, y - 24, 18, 18, White);

        for (int i = 0; i < SlotsVisible; i++)
        {
            int slot = _slotsTop + i;
            if (slot >= SlotRows) break;
            int rx = x + SlotPad, ry = y + SlotPad + i * SlotRowH;
            var head = SlotHead(slot);
            bool dim = _slotsMode == 1 && head == null;

            if (slot == _slotsHover && !dim && !DrawUi(SlotHighlightObs, SlotHighlightMotion, tick, rx, ry, UiBlend.Alpha))
                FillRect(rx, ry, SlotRowW, SlotRowH, 0x4060A0FF);

            if (head == null)
            {
                string none = _db?.T(0) is { Length: > 0 } t ? t : "없음";
                var (_, nw, nh) = GetText(none, White);
                DrawText(none, rx + (SlotRowW - nw) / 2, ry + (SlotRowH - nh) / 2, dim ? DimGray : White);
                continue;
            }

            string title = _db?.T((ushort)head.SceneText) is { Length: > 0 } s ? s : "";
            var (_, tw, th) = GetText(title, White);
            DrawText(title, rx + (SlotRowW - tw) / 2, ry + (SlotRowH - th) / 2, White);

            string label = $"[{slot:D2}:{(head.SceneKind == 4 ? "챕  터" : "전  투")}]";
            DrawText(label, rx + 10, ry + (SlotRowH - th) / 2, SlotLabelColor);

            long ms = head.PlayMs;
            string time = $"{ms / 3600000,3}:{ms % 3600000 / 60000:D2}:{ms % 60000 / 1000:D2}";
            var (_, pw, _) = GetText(time, White);
            DrawText(time, rx + SlotRowW - 10 - pw, ry + (SlotRowH - th) / 2, SlotTimeColor);
        }

        DrawSlotScrollbar(x + 304, y, tick);
    }

    /// <summary>스크롤 막대 — 위·아래 화살표와 손잡이(움직이는 길 232).</summary>
    private void DrawSlotScrollbar(int x, int y, int tick)
    {
        if (!DrawUi(SlotScrollObs, 2, tick, x, y, UiBlend.Alpha)) StrokeRect(x, y, 16, 16, White);
        if (!DrawUi(SlotScrollObs, 4, tick, x, y + SlotsH - 16, UiBlend.Alpha)) StrokeRect(x, y + SlotsH - 16, 16, 16, White);

        int track = SlotsH - 32, span = Math.Max(1, SlotRows - SlotsVisible);
        int handleH = Math.Max(16, track * SlotsVisible / SlotRows);
        int hy = y + 16 + (track - handleH) * _slotsTop / span;
        if (!DrawUi(SlotScrollObs, 6, tick, x, hy, UiBlend.Alpha)) FillRect(x + 2, hy, 12, handleH, BoxLine);
    }

    /// <summary>「저장되었습니다.」 같은 알림창 — 원본 메시지 창과 같은 틀.</summary>
    private void DrawNotice()
    {
        if (_notice is not { } notice) return;
        if (_lastTime >= notice.Until) { _notice = null; return; }
        var (_, tw, th) = GetText(notice.Title, White);
        int w = tw + 60, h = th + 40;
        int x = _camX + (ViewWidth - w) / 2, y = _camY + (ViewHeight - h) / 2;
        DrawGameFrame(x, y, w, h);
        DrawText(notice.Title, x + (w - tw) / 2, y + (h - th) / 2, White);
    }
}

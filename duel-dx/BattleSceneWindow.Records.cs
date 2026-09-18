namespace DuelDx;

/// <summary>
/// 「Select your record」 화면(장면 9) — 타이틀에서 CONTINUE 를 누르면 나오는 <b>불러오기 전용 화면</b>.
/// </summary>
/// <remarks>
/// 원본은 CONTINUE 가 슬롯 창만 띄우는 것이 아니라 <b>따로 된 장면</b>으로 간다(<c>0x10105cd3</c> 이 장면 9 를 건다).
/// <list type="bullet">
/// <item>배경 <c>Bgr 0131</c> 한 장 — 「Select your record」 글자와 아래 EXIT 알약이 <b>그림에 들어 있다</b>.</item>
/// <item>불러오기 슬롯 목록(전투 시스템 메뉴의 그것과 같은 창)을 <b>(160, 148)</b> 에 놓고,
/// <b>제목 「Load」 는 지운다</b> — 배경 글씨가 제목 노릇을 한다(<c>0x10104a50</c> 이 라벨을 놓아 버린다).</item>
/// <item>EXIT 단추는 <b>(246, 441) 148×27</b>, 마우스를 올리면 <c>Obs 0287</c> 모션 0(29틱 반복 알약)을 덧그린다.
/// 누르면 <b>타이틀로 돌아간다</b> — 프로그램이 끝나지 않는다(<c>0x10104c60</c>).</item>
/// <item><b>음악은 안 끊긴다</b> — 타이틀과 이 화면 사이에서는 BGM 21 을 그대로 이어 튼다
/// (타이틀은 다음이 9 면 페이드아웃을 건너뛰고, 이 화면은 다음이 6 이 아닐 때만 소리를 죽인다).</item>
/// </list>
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int RecordsBackground = 131, RecordsExitObs = 287;
    private static readonly (int X, int Y, int W, int H) RecordsExit = (246, 441, 148, 27);

    private bool _recordsOpen;
    private bool _recordsExitHover;

    /// <summary>타이틀 CONTINUE — 불러오기 전용 화면으로 간다.</summary>
    private void OpenRecords()
    {
        _recordsOpen = true;
        _titleOpen = false;
        _recordsExitHover = false;
        ShowMosesBackground(RecordsBackground);
        OpenSlots(1);                                  // 불러오기 목록(같은 창을 그대로 쓴다)
    }

    private void CloseRecords()
    {
        _recordsOpen = false;
        CloseSystemWindow();
        OpenTitle();                                   // 음악은 이어진다 — OpenTitle 이 같은 곡을 다시 걸지 않게 본다
    }

    /// <summary>이 화면이 떠 있으면 클릭을 처리하고 true.</summary>
    private bool OnRecordsClick(int bx, int by)
    {
        if (!_recordsOpen) return false;
        var (ox, oy) = MosesOrigin();
        var (ex, ey, ew, eh) = RecordsExit;
        if (bx >= ox + ex && bx < ox + ex + ew && by >= oy + ey && by < oy + ey + eh) { CloseRecords(); return true; }
        if (SystemOpen) return OnSystemClick(bx, by);
        return true;
    }

    private void UpdateRecordsHover(int bx, int by)
    {
        if (!_recordsOpen) return;
        var (ox, oy) = MosesOrigin();
        var (ex, ey, ew, eh) = RecordsExit;
        _recordsExitHover = bx >= ox + ex && bx < ox + ex + ew && by >= oy + ey && by < oy + ey + eh;
    }

    private void DrawRecords()
    {
        if (!_recordsOpen) return;
        var (ox, oy) = MosesOrigin();
        int tick = (int)(_lastTime * TicksPerSecond);

        FillRect(0, _camY, BoardWidth, ViewHeight, 0xFF000000);
        if (_mosesBg is { } bg)
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    SetPixel(ox + x, oy + y, bg[y * MosesW + x] | 0xFF000000);

        var (ex, ey, _, _) = RecordsExit;
        if (_recordsExitHover) DrawUi(RecordsExitObs, 0, tick, ox + ex, oy + ey, UiBlend.Add);

        DrawSystem();
        DrawToast();
    }
}

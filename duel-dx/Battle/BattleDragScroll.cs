using DuelDx.Native;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 끌어서 화면 밀기 — 전투 판(과 파트1 지도)을 왼쪽 단추로 누른 채 끌면 카메라가 따라 움직인다.
/// 원본에 없는 리메이크 편의(사용자 요청: 「화면 끝에 가면 전장이 스크롤 됐는데 마우스 드래그앤 드롭으로도 스크롤 되게 해줘」).
/// </summary>
/// <remarks>
/// 왼쪽 단추는 원래 누르는 순간(WM_LBUTTONDOWN) 클릭이 된다. 판 위에서 끌 수 있는 때에는 그 클릭을 <b>뗄 때까지 미뤄 두고</b>,
/// 누른 채 <see cref="Threshold"/> 화면 픽셀을 넘게 움직이면 끌기로 바꾼다 — 끌기로 바뀐 누름은 떼어도 클릭이 안 된다.
/// 안 넘고 떼면 누른 자리의 클릭을 그대로 보낸다(누르는 동안은 가장자리 스크롤을 쉬어 카메라가 안 움직이므로 누른 칸 그대로).
/// 끌 수 있는 때는 가장자리 스크롤과 같다(<see cref="BattleScene.EdgeScrollAllowed"/> — 내 차례 · 배치). 창(링 · 어빌리티 · 상태 ·
/// 도구 창들) · 미니맵 · 상자 목록 · 배치 창 위의 누름은 미루지 않고 예전처럼 바로 클릭이 된다. 패드가 보내는 누름(wParam 에 MK_LBUTTON 이 없다)도 바로.
/// 오른쪽 단추는 링 · 취소 · 설명에 쓰여 끌기에 안 쓴다.
/// </remarks>
internal sealed class BattleDragScroll(GameWindow host)
{
    /// <summary>이만큼(화면 픽셀) 넘게 움직여야 끌기 — 그 안의 떨림은 클릭이다.</summary>
    internal const int Threshold = 7;

    /// <summary>누른 자리(창 픽셀) — 안 눌렀으면 null.</summary>
    private (int X, int Y)? _press;

    /// <summary>무엇을 끄나 — 전투 판인가, 파트1 지도인가.</summary>
    private bool _chapter;

    /// <summary>끌기로 바뀐 뒤인가 — 그러면 떼어도 클릭이 아니다.</summary>
    internal bool Dragging { get; private set; }

    /// <summary>끌기를 시작할 때의 카메라(판 픽셀).</summary>
    private double _camX0, _camY0;

    /// <summary>왼쪽 단추를 판 위에서 누르고 있나 — 그동안 가장자리 스크롤은 쉰다.</summary>
    internal bool Held => _press != null;

    /// <summary>
    /// WM_LBUTTONDOWN — 끌 수 있는 판 위면 클릭을 미루고 true(부르는 쪽은 클릭을 안 보낸다). 아니면 false(바로 클릭).
    /// <paramref name="mouseButton"/> = wParam 의 MK_LBUTTON — 패드가 보낸 누름은 떼는 메시지가 안 오므로 미루지 않는다.
    /// </summary>
    internal bool OnDown(int clientX, int clientY, bool mouseButton)
    {
        Cancel();
        if (!mouseButton) return false;
        var (bx, by) = host.BoardPoint(clientX, clientY);
        if (ChapterAllowed(bx, by)) _chapter = true;
        else if (BattleAllowed(bx, by)) _chapter = false;
        else return false;
        _press = (clientX, clientY);
        Win32.SetCapture(host._hwnd);   // 창 밖에서 떼어도 WM_LBUTTONUP 이 오게
        return true;
    }

    /// <summary>WM_MOUSEMOVE — 누른 채 문턱을 넘으면 끌기로 바꾸고, 끄는 동안은 카메라를 커서에 붙여 옮긴다.</summary>
    internal void OnMove(int clientX, int clientY)
    {
        if (_press is not var (px, py)) return;
        int dx = clientX - px, dy = clientY - py;
        if (!Dragging)
        {
            if (dx * dx + dy * dy <= Threshold * Threshold) return;
            Dragging = true;
            if (_chapter) (_camX0, _camY0) = host.Part1TitleScr.Chapter._cam;
            else
            {
                var btl = host.Btl;
                btl._camGoal = null;          // 가운데로 가던 카메라 명령은 손이 이긴다
                btl._showTurnUnit = false;
                (_camX0, _camY0) = (btl._camPosX, btl._camPos);
            }
        }
        double zoom = host._zoom > 0 ? host._zoom : 1;
        double ox = dx / zoom, oy = dy / zoom;   // 판 픽셀 — 잡은 자리가 커서 밑에 남게 반대로 민다
        if (_chapter)
        {
            if (host.Part1TitleScr.ChapterOnTop) host.Part1TitleScr.Chapter.DragCamera((int)Math.Round(_camX0 - ox), (int)Math.Round(_camY0 - oy));
            return;
        }
        var b = host.Btl;
        if (!b.EdgeScrollAllowed) return;      // 끄는 사이 차례가 넘어가면 카메라를 놓는다(떼기 전까지 클릭도 안 된다)
        b._camPosX = b._camTargetX = b.ClampCamX(_camX0 - ox);
        b._camPos = b._camTarget = b.ClampCamY(_camY0 - oy);
        host._camX = (int)Math.Round(b._camPosX);
        host._camY = (int)Math.Round(b._camPos);
    }

    /// <summary>WM_LBUTTONUP — 끌기가 아니었으면 미뤄 둔 클릭 자리(창 픽셀)를 돌려준다. 끌기였거나 안 눌렀으면 null.</summary>
    internal (int X, int Y)? OnUp()
    {
        var press = _press;
        bool dragged = Dragging;
        Cancel();
        return press is { } p && !dragged ? p : null;
    }

    /// <summary>누름을 버린다(포커스를 잃었을 때 따위) — 클릭도 안 보낸다.</summary>
    internal void Cancel()
    {
        bool had = _press != null;
        _press = null;
        Dragging = false;
        if (had) Win32.ReleaseCapture();
    }

    /// <summary>전투 판을 끌 수 있는 누름인가 — 가장자리 스크롤이 되는 때, 판이 보이는 영역보다 크고, 위에 창이 없고, 판 위(미니맵 · 상자 목록 · 배치 창 밖)일 때.</summary>
    private bool BattleAllowed(int bx, int by)
    {
        var btl = host.Btl;
        if (!host._battleLoaded || host._loading || host.BattleCovered || host.Part1TitleScr._open || AnyToolWindow()) return false;
        if (host.Cut.Playing || host.ProgressScr._progressOpen || host.Tlk._talk != null || host.Part1PickOpen || host.SlotsScr._notice != null) return false;
        if (btl._itemMenu || !btl.EdgeScrollAllowed || (btl.CamMaxX <= 0 && host.CamMax <= 0)) return false;
        if (by < host._camY + GridTop || host.MiniMap.Contains(bx, by)) return false;
        if (btl._chestFoldButton is var (fx, fy, fw, fh) && bx >= fx && bx < fx + fw && by >= fy && by < fy + fh) return false;
        if (btl._deployOpen)
        {
            if (host._scene.IsPart1 && (host.MercDeploy.Open || host.MountDeploy.Open)) return false;
            var (px, py) = btl.DeployPanel();
            if (bx >= px && bx < px + BattleScene.DeployW && by >= py && by < py + btl.DeployPanelHeight()) return false;
        }
        return true;
    }

    /// <summary>파트1 지도를 끌 수 있는 누름인가 — 지도만 떠 있고(창 · 메뉴 · 대사 · 고르기 없음) 지도 화면 안일 때.</summary>
    private bool ChapterAllowed(int bx, int by) =>
        host.Part1TitleScr.ChapterOnTop && !AnyToolWindow() && host.Part1TitleScr.Chapter.DragScrollAllowed(bx, by);

    /// <summary>메뉴 막대에서 여는 창(맨 위에 그린다)이 떠 있나 — 그 위의 누름은 끌기가 아니다.</summary>
    private bool AnyToolWindow() =>
        host.NotesScr.Open || host.ConsentScr.Open || host.SettingsScr._open || host.TimedEditScr._open || host.DevScr._open
        || host.StatsScr._open || host.SkillBookScr._open || host.Part1TablesOpen
        || host.ItemBookScr._open || host.CharEditScr._open || host.TuningScr._tuningOpen || host._keysOpen;
}

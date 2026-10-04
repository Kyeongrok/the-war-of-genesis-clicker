using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 카메라 — 창은 판의 일부(<see cref="ViewHeight"/>·<see cref="ViewWidth"/>)만 보여 주고, 원본처럼 <b>카메라 명령</b>으로
/// 대상을 화면 한가운데에 둔다(감사4 C1). 마우스 휠로 직접 올리고 내릴 수 있다. 머리 줄·알림·창들은 보이는 영역 기준으로 그린다.
/// </summary>
internal sealed unsafe partial class BattleScene
{
    /// <summary>보이는 영역의 맨 윗줄(판 픽셀).</summary>
    internal double _camPos, _camTarget;

    /// <summary>보이는 영역의 맨 왼쪽(판 픽셀) — 640 보다 넓은 맵에서만 0 이 아니다.</summary>
    internal double _camPosX, _camTargetX;

    /// <summary>카메라가 오른쪽으로 갈 수 있는 끝 — 맵 그림 너비까지.</summary>
    internal int CamMaxX => Math.Max(0, Math.Min(host.BoardWidth, host.BoardIsMap ? Math.Max(host.ViewWidth, host._map!.Width) : host.BoardWidth) - host.ViewWidth);
    internal void ScrollCamera(double delta) => _camTarget = Math.Clamp(_camTarget + delta, 0, host.CamMax);

    internal void ScrollCameraX(double delta) => _camTargetX = Math.Clamp(_camTargetX + delta, 0, CamMaxX);

    // ── 카메라 명령 (원본 CBattle +0x3ccc 큐의 축소판, 감사4 C1·C19) ─────────────────────────────
    // 원본은 「이 월드 점을 화면 한가운데에」 명령을 큐 머리에 덮어쓰고(0x1006e570), 틀마다 0x1006dcb0 이
    // 속도 0 이면 max(4, ⌊거리/5⌋) px 씩 곧은 선으로 다가간다. 거리 < 속도면 도착 — 점 명령(종류 0)은 빠지고,
    // 따라가기(종류 1, 0x1006e730)는 주인 자리를 틀마다 다시 읽으며 저절로 안 끝난다. 여백 규칙은 없다.
    // 상태·이벤트는 큐가 빌 때까지(0x1006e850) 다음 단계로 안 간다 — 그래서 행동·대사·창은 카메라가 선 뒤에 나온다.

    /// <summary>지금 카메라 명령 — 가운데에 둘 판 픽셀(X, Y), 속도(0 = 자동), 따라갈 유닛 번호(−1 = 점 명령).</summary>
    internal (double X, double Y, int Speed, int Follow)? _camGoal;

    /// <summary>카메라가 점 명령을 수행 중인가 — 기다리는 조건(0x1006e850). 따라가기는 저절로 안 끝나므로 기다림에서 뺀다.</summary>
    internal bool CameraBusy => _camGoal is { Follow: < 0 };

    /// <summary>틱 단위로 움직이려고 모아 두는 틱 조각.</summary>
    internal double _camTickAcc;

    /// <summary>판 픽셀 (x, y) 를 화면 가운데로(0x1006e570 — 머리 칸을 덮어쓴다, 쌓지 않음).</summary>
    internal void CenterOn(double x, double y, int speed = 0)
    {
        _camGoal = (x, y, speed, -1);
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"{host._lastTime:F2} camera centre ({x:F0},{y:F0}) from ({_camPosX:F0},{_camPos:F0})" + Environment.NewLine);
    }

    /// <summary>칸 가운데를 화면 가운데로 — 칸은 판 안으로 자른다.</summary>
    internal void CenterOnCell(int col, int row)
    {
        col = Math.Clamp(col, 0, host.Cols - 1);
        row = Math.Clamp(row, 0, host.Rows - 1);
        CenterOn(col * TileW + TileW / 2, host.CellCenterY(col, row));
    }

    /// <summary>유닛 발 자리를 한 번 가운데로(0x100eabe0 — 종류 0, 따라가기 아님).</summary>
    internal void CenterOnUnit(UnitState u)
    {
        var (fx, fy) = host.UnitFoot(u);
        CenterOn(fx, fy);
    }

    /// <summary>걷는 유닛 따라가기(0x100eac40(8) — 틱당 최대 8px). 도착하면 <see cref="UpdateCamera"/> 가 풀어 준다(0x100eac60).</summary>
    internal void FollowUnit(int index, int speed = 8)
    {
        if ((uint)index < host._units.Length) _camGoal = (0, 0, speed, index);
    }

    /// <summary>
    /// 점 명령을 걸고 멈출 때까지 기다리는 루틴 조각 — 원본 상태 하위 0 의 「가운데 → 0x1006e850 == 0 까지 머묾」.
    /// 막히는 일이 없게 3초에서 끊는다.
    /// </summary>
    internal IEnumerable<bool> CenterAndWait(double x, double y)
    {
        CenterOn(x, y);
        for (double end = host._lastTime + 3; CameraBusy && host._lastTime < end;) yield return true;
    }

    internal IEnumerable<bool> CenterUnitAndWait(UnitState u)
    {
        var (fx, fy) = host.UnitFoot(u);
        return CenterAndWait(fx, fy);
    }

    /// <summary>AI 차례(상태 8 → 14 CHRWORK 0x10069df0) — 그 인물을 가운데로 보내고 멈춘 뒤에 명령을 실행한다(감사4 C4).</summary>
    internal IEnumerator<bool> CameraThen(UnitState u, IEnumerator<bool> inner)
    {
        foreach (bool b in CenterUnitAndWait(u)) yield return b;
        while (inner.MoveNext()) yield return inner.Current;
    }

    /// <summary>링(상태 9)·대상 고르기(10·11·12)에 들어설 때 행동 인물을 가운데로 — 지난 틀에 본 값.</summary>
    internal int _camSeenRing = -1;
    internal bool _camSeenAim;

    /// <summary>
    /// 플레이어가 움직일 수 있는 상태인가 — 원본 가장자리 스크롤(0x1006d910)은 상태 2(배치)·3·7·10·11·12·22 에서만 부른다(감사4 C10).
    /// AI 차례·행동·이벤트·결과 중에는 안 민다.
    /// </summary>
    internal bool EdgeScrollAllowed =>
        !host.Mos._mosesOpen && !host.FieldOpen && !host.TitleScr._titleOpen && !host.EpisodesScr._episodesOpen && _ringUnit < 0 && !_abilityMenu && host._statusUnit < 0 && !host.SystemOpen
        && !host.SceneFading && !EventsBusy && _outcome.Length == 0 && !LevelUpOpen
        && (_deployOpen || (IsPlayerTurn && !host._units[_turn].IsBusy));

    internal void UpdateCamera(double dt)
    {
        // 링이 열릴 때(상태 9 0x1006904c)·대상 고르기를 시작할 때(10·11·12) 행동 인물을 가운데로. 차례 시작(상태 22)은 안 옮긴다(감사4 C3).
        if (_ringUnit != _camSeenRing)
        {
            _camSeenRing = _ringUnit;
            if ((uint)_ringUnit < host._units.Length) CenterOnUnit(host._units[_ringUnit]);
        }
        bool aiming = _targetWork >= 0 || _abilityMenu;
        if (aiming && !_camSeenAim && (uint)_turn < host._units.Length) CenterOnUnit(host._units[_turn]);
        _camSeenAim = aiming;

        // 틱마다(초당 30) 한 번씩 — 원본 이동은 틀 단위다.
        _camTickAcc += dt * TicksPerSecond;
        int ticks = Math.Min(6, (int)_camTickAcc);
        _camTickAcc -= (int)_camTickAcc;
        for (int i = 0; i < ticks; i++)
        {
            StepCameraGoal();
            StepEdgeScroll();
        }

        // 휠 스크롤(리메이크 편의)만 부드럽게 따라간다 — 명령·가장자리 스크롤은 _camPos 를 바로 옮기고 목표도 같이 맞춘다.
        _camPos += (_camTarget - _camPos) * Math.Min(1, dt * 8);
        if (Math.Abs(_camTarget - _camPos) < 0.5) _camPos = _camTarget;
        host._camY = Math.Clamp((int)Math.Round(_camPos), 0, host.CamMax);
        _camPosX += (_camTargetX - _camPosX) * Math.Min(1, dt * 8);
        if (Math.Abs(_camTargetX - _camPosX) < 0.5) _camPosX = _camTargetX;
        host._camX = Math.Clamp((int)Math.Round(_camPosX), 0, CamMaxX);
        // 기술의 화면 흔들림 — 원본 흔들림 개체(0x100c7040)는 지금 스크롤 기준 +세기 → −세기 로 번갈아 카메라 명령을 걸어
        // 스크롤이 원자리 ↔ 원자리+세기 를 오간다(한쪽, 폭 = 세기, 감사4 C18). ShakeOffset 의 ±세기 중 음수 쪽을 0 으로 접는다.
        var (sx, sy) = host.HeavenEarthAb.ShakeOffset();
        sx = Math.Max(0, sx);
        sy = Math.Max(0, sy);
        if (sx != 0 || sy != 0)
        {
            host._camX = Math.Clamp(host._camX + sx, 0, CamMaxX);
            host._camY = Math.Clamp(host._camY + sy, 0, host.CamMax);
        }
    }

    /// <summary>카메라 명령 한 틱(0x1006dcb0).</summary>
    internal void StepCameraGoal()
    {
        if (_camGoal is not { } g) return;
        double gx = g.X, gy = g.Y;
        if (g.Follow >= 0)
        {
            if (g.Follow >= host._units.Length) { _camGoal = null; return; }
            var u = host._units[g.Follow];
            // 걷기가 끝나면 따라가기를 뺀다(0x10077085 → 0x100eac60) — 카메라는 그 자리에 선다.
            if (!u.IsMoving && u.Path.Count == 0) { _camGoal = null; return; }
            (int fx, int fy) = host.UnitFoot(u);
            (gx, gy) = (fx, fy);
        }
        double tx = Math.Clamp(gx - host.ViewWidth / 2.0, 0, CamMaxX), ty = Math.Clamp(gy - host.ViewHeight / 2.0, 0, host.CamMax);
        double dx = tx - _camPosX, dy = ty - _camPos, dist = Math.Sqrt(dx * dx + dy * dy);
        int step = g.Speed > 0 ? g.Speed : Math.Max(4, (int)(dist / 5));
        if (dist < step)
        {
            (_camPosX, _camPos) = (tx, ty);
            if (g.Follow < 0) _camGoal = null;          // 점 명령은 도착하면 빠진다(0x1006e7b0)
        }
        else
        {
            _camPosX += dx / dist * step;                // 곧은 선(0x1001e3a0)
            _camPos += dy / dist * step;
        }
        (_camTargetX, _camTarget) = (_camPosX, _camPos);
    }

    /// <summary>
    /// 가장자리 스크롤 한 틱 — 원본 0x1006d910 은 틱마다 가로 20·세로 16px 을 <b>바로</b> 더한다(초당 600/480px, 감사4 C11).
    /// 원본은 끝 픽셀에서만 밀지만 창 모드라 24px 띠는 그대로 둔다(의도). 방향키는 걷기 그대로(사용자 결정).
    /// </summary>
    internal void StepEdgeScroll()
    {
        if (!EdgeScrollAllowed) return;
        const int edge = 24;
        // 마우스는 화면(보이는 영역) 자리로 본다 — 판 자리로 보면 카메라가 한 번 밀린 뒤 마우스가 띠 밖으로 나간 셈이 되어 멈췄다.
        var (mx, my) = _mouseView;
        if (host._mouse.X < 0 || mx < 0 || mx >= host.ViewWidth || my < 0 || my >= host.ViewHeight) return;
        double ddx = mx < edge ? -20 : mx >= host.ViewWidth - edge ? 20 : 0;
        double ddy = my < GridTop + edge && my >= GridTop ? -16 : my >= host.ViewHeight - edge ? 16 : 0;
        if (ddx == 0 && ddy == 0) return;
        _camPosX = _camTargetX = Math.Clamp(_camPosX + ddx, 0, CamMaxX);
        _camPos = _camTarget = Math.Clamp(_camPos + ddy, 0, host.CamMax);
        host._mouse = (mx + (int)Math.Round(_camPosX), my + (int)Math.Round(_camPos));   // 마우스 밑의 판 자리도 따라 옮긴다
    }

    /// <summary>마우스의 보이는 영역 안 자리(판 픽셀, 카메라 뺀 것) — WM_MOUSEMOVE 가 적는다.</summary>
    internal (int X, int Y) _mouseView = (-1, -1);

    /// <summary>
    /// 새 판의 카메라를 지운다 — 앞 전투의 세로 자리·명령이 남지 않게(감사4 C5: 예전엔 가로만 지웠다).
    /// </summary>
    internal void ResetCamera()
    {
        _camGoal = null;
        _camTickAcc = 0;
        _camSeenRing = -1;
        _camSeenAim = false;
        host._camX = host._camY = 0;
        _camPos = _camTarget = _camPosX = _camTargetX = 0;
    }

    /// <summary>
    /// 시작 카메라 — Btl 머리 워드 2·3 칸을 가운데에 두고 맵 그림 안으로 자른다, 보간 없이 바로(0x10063e44~0x10063f5x, 감사4 C5).
    /// </summary>
    internal void PlaceStartCamera()
    {
        ResetCamera();
        if (host._scene.StartCol < 0 || host._scene.StartRow < 0 || !host.BoardIsMap) return;
        int col = Math.Clamp(host._scene.StartCol, 0, host.Cols - 1), row = Math.Clamp(host._scene.StartRow, 0, host.Rows - 1);
        _camPosX = _camTargetX = Math.Clamp(col * TileW + TileW / 2 - host.ViewWidth / 2, 0, CamMaxX);
        _camPos = _camTarget = Math.Clamp(host.CellCenterY(col, row) - host.ViewHeight / 2, 0, host.CamMax);
        host._camX = (int)_camPosX;
        host._camY = (int)_camPos;
    }

}

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DuelDx;

/// <summary>
/// 세로 스크롤 — 창은 판 높이의 80%(<see cref="ViewHeight"/>)만 보여 주고, 차례인(또는 고른) 인물이 화면 안에 들어오도록
/// 따라간다. 마우스 휠로 직접 올리고 내릴 수 있다. 머리 줄·알림·창들은 보이는 영역 기준으로 그린다.
/// </summary>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>보이는 영역의 맨 윗줄(판 픽셀).</summary>
    private int _camY;
    private double _camPos, _camTarget;

    /// <summary>보이는 영역의 맨 왼쪽(판 픽셀) — 640 보다 넓은 맵에서만 0 이 아니다.</summary>
    private int _camX;
    private double _camPosX, _camTargetX;
    private const int CamMarginX = 120;

    /// <summary>카메라가 오른쪽으로 갈 수 있는 끝 — 맵 그림 너비까지.</summary>
    private int CamMaxX => Math.Max(0, Math.Min(BoardWidth, BoardIsMap ? Math.Max(ViewWidth, _map!.Width) : BoardWidth) - ViewWidth);
    private (int Unit, int Col, int Row) _camFocus = (-1, -1, -1);

    private const int CamMarginTop = 120, CamMarginBottom = 140;

    private void ScrollCamera(double delta) => _camTarget = Math.Clamp(_camTarget + delta, 0, CamMax);

    private void ScrollCameraX(double delta) => _camTargetX = Math.Clamp(_camTargetX + delta, 0, CamMaxX);

    private void UpdateCamera(double dt)
    {
        // 따라갈 인물: 대사 중이면 말하는 이, 아니면 차례인 인물(없으면 고른 인물).
        // 원본도 대사 창을 띄우기 전에 말하는 이에게 카메라를 옮기고 멈출 때까지 기다린다(0x100eabe0).
        int focus = _talk is { Speaker: >= 0 } t ? t.Speaker : _turn >= 0 ? _turn : _selected;
        if ((uint)focus < _units.Length)
        {
            var u = _units[focus];
            if (focus != _camFocus.Unit || u.Col != _camFocus.Col || u.Row != _camFocus.Row)
            {
                _camFocus = (focus, u.Col, u.Row);
                int foot = CellCenterY(u.Col, u.Row);
                if (foot < _camTarget + GridTop + CamMarginTop) _camTarget = foot - GridTop - CamMarginTop;
                else if (foot > _camTarget + ViewHeight - CamMarginBottom) _camTarget = foot - ViewHeight + CamMarginBottom;
                _camTarget = Math.Clamp(_camTarget, 0, CamMax);
                // 좌우도 같은 식으로 — 인물이 화면 가장자리 120픽셀 안에 들면 따라간다.
                int footX = u.Col * TileW + TileW / 2;
                if (footX < _camTargetX + CamMarginX) _camTargetX = footX - CamMarginX;
                else if (footX > _camTargetX + ViewWidth - CamMarginX) _camTargetX = footX - ViewWidth + CamMarginX;
                _camTargetX = Math.Clamp(_camTargetX, 0, CamMaxX);
            }
        }

        // 마우스가 보이는 영역 가장자리(24픽셀 안)에 닿으면 그쪽으로 민다 — 원본처럼 화면 끝으로 가면 스크롤된다.
        // 창·메뉴가 떠 있거나 모세스·필드·타이틀에서는 안 민다.
        if (!_mosesOpen && !FieldOpen && !_titleOpen && !_episodesOpen && _ringUnit < 0 && !_abilityMenu && _statusUnit < 0 && !SystemOpen)
        {
            const int edge = 24; double speed = 480 * dt;
            int mx = _mouse.X - _camX, my = _mouse.Y - _camY;
            if (mx >= 0 && mx < ViewWidth && my >= 0 && my < ViewHeight)
            {
                if (mx < edge) _camTargetX = Math.Clamp(_camTargetX - speed, 0, CamMaxX);
                else if (mx >= ViewWidth - edge) _camTargetX = Math.Clamp(_camTargetX + speed, 0, CamMaxX);
                if (my < GridTop + edge && my >= GridTop) _camTarget = Math.Clamp(_camTarget - speed, 0, CamMax);
                else if (my >= ViewHeight - edge) _camTarget = Math.Clamp(_camTarget + speed, 0, CamMax);
            }
        }

        _camPos += (_camTarget - _camPos) * Math.Min(1, dt * 8);
        if (Math.Abs(_camTarget - _camPos) < 0.5) _camPos = _camTarget;
        _camY = Math.Clamp((int)Math.Round(_camPos), 0, CamMax);
        _camPosX += (_camTargetX - _camPosX) * Math.Min(1, dt * 8);
        if (Math.Abs(_camTargetX - _camPosX) < 0.5) _camPosX = _camTargetX;
        _camX = Math.Clamp((int)Math.Round(_camPosX), 0, CamMaxX);
        // 기술의 화면 흔들림(천지 파열무 등) — 틀마다 ±세기로 번갈아 민다.
        var (sx, sy) = ShakeOffset();
        if (sx != 0 || sy != 0)
        {
            _camX = Math.Clamp(_camX + sx, 0, CamMaxX);
            _camY = Math.Clamp(_camY + sy, 0, CamMax);
        }
    }

    /// <summary>지금 보이는 영역을 <c>%TEMP%\dueldx_snapshot.png</c> 로 저장한다 — 창을 화면에 띄우지 않고 확인하는 테스트용.</summary>
    private void SaveSnapshot()
    {
        using var bmp = new Bitmap(ViewWidth, ViewHeight, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, ViewWidth, ViewHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < ViewHeight; y++)
                _fb.AsSpan((_camY + y) * BoardWidth + _camX, ViewWidth).CopyTo(new Span<uint>((void*)(data.Scan0 + y * data.Stride), ViewWidth));
        }
        finally { bmp.UnlockBits(data); }
        bmp.Save(Path.Combine(Path.GetTempPath(), "dueldx_snapshot.png"), ImageFormat.Png);
    }
}

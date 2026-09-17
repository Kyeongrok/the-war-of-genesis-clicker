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
    private (int Unit, int Col, int Row) _camFocus = (-1, -1, -1);

    private const int CamMarginTop = 120, CamMarginBottom = 140;

    private void ScrollCamera(double delta) => _camTarget = Math.Clamp(_camTarget + delta, 0, BoardHeight - ViewHeight);

    private void UpdateCamera(double dt)
    {
        // 따라갈 인물: 차례인 인물(없으면 고른 인물). 그 인물이 바뀌거나 칸을 옮기면 화면 안으로 당긴다 — 휠로 옮긴 건 그대로 둔다.
        int focus = _turn >= 0 ? _turn : _selected;
        if ((uint)focus < _units.Length)
        {
            var u = _units[focus];
            if (focus != _camFocus.Unit || u.Col != _camFocus.Col || u.Row != _camFocus.Row)
            {
                _camFocus = (focus, u.Col, u.Row);
                int foot = GridTop + u.Row * TileH + TileH / 2;
                if (foot < _camTarget + GridTop + CamMarginTop) _camTarget = foot - GridTop - CamMarginTop;
                else if (foot > _camTarget + ViewHeight - CamMarginBottom) _camTarget = foot - ViewHeight + CamMarginBottom;
                _camTarget = Math.Clamp(_camTarget, 0, BoardHeight - ViewHeight);
            }
        }

        _camPos += (_camTarget - _camPos) * Math.Min(1, dt * 8);
        if (Math.Abs(_camTarget - _camPos) < 0.5) _camPos = _camTarget;
        _camY = Math.Clamp((int)Math.Round(_camPos), 0, BoardHeight - ViewHeight);
    }

    /// <summary>지금 보이는 영역을 <c>%TEMP%\dueldx_snapshot.png</c> 로 저장한다 — 창을 화면에 띄우지 않고 확인하는 테스트용.</summary>
    private void SaveSnapshot()
    {
        using var bmp = new Bitmap(BoardWidth, ViewHeight, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, BoardWidth, ViewHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < ViewHeight; y++)
                _fb.AsSpan((_camY + y) * BoardWidth, BoardWidth).CopyTo(new Span<uint>((void*)(data.Scan0 + y * data.Stride), BoardWidth));
        }
        finally { bmp.UnlockBits(data); }
        bmp.Save(Path.Combine(Path.GetTempPath(), "dueldx_snapshot.png"), ImageFormat.Png);
    }
}

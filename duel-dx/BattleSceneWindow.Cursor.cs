using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 게임 고유 마우스 커서(an-ui-1)와 전투 끝 배너(ba-9).
/// </summary>
/// <remarks>
/// 옵시디안 분석-UI "마우스 커서": 커서는 윈도 커서가 아니라 마우스 자리에 그리는 Obs 애니다 —
/// 0044 기본 화살표, 0048 가리키는 손(단추·갈 수 있는 칸), 0045 검(공격 대상), 0049 지팡이(회복·보조 대상),
/// 0053 금지. 핫스팟은 컷 기준점이고 한 화면이 1틱이다.
/// 분석-전투 "승리 뒤 진행": 마지막 적이 죽으면 화면을 어둡게 하고 화면 한가운데(320,220 = 640×480 기준)에
/// <b>Obs 0491</b> 배너를 띄운다 — 승리 모션 0·1·2「任務終了 / Mission Complete」, 패배 10·11·12「Game Over」.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int CursorArrow = 44, CursorHand = 48, CursorAttack = 45, CursorSupport = 49;
    private const int BannerObs = 491, BannerWin = 0, BannerLose = 10;

    private (int X, int Y) _mouse = (-1, -1);

    /// <summary>지금 마우스 자리에 맞는 커서 그림.</summary>
    private int CursorFor(int bx, int by)
    {
        if (_keysOpen || SystemOpen || _statusUnit >= 0 || _abilityMenu || _ringUnit >= 0) return CursorHand;
        if (_targetWork >= 0 && Work(_targetWork) is { } w)
        {
            if (LiveUnitAt(bx / TileW, (by - GridTop) / TileH) is not { } t) return CursorArrow;
            return w.IsDamage ? CursorAttack : CursorSupport;
        }
        if (_rangeUnit >= 0 && _range is { } range && by >= GridTop)
        {
            int index = (by - GridTop) / TileH * Cols + bx / TileW;
            if (range.CanReach(index)) return CursorHand;
        }
        return CursorArrow;
    }

    private void DrawCursor()
    {
        var (bx, by) = _mouse;
        if (bx < 0 || by < 0) return;
        int tick = (int)(_lastTime * TicksPerSecond);
        if (!DrawUi(CursorFor(bx, by), 0, tick, bx, by, UiBlend.Alpha))
            StrokeRect(bx - 3, by - 3, 7, 7, White);   // 그림이 없으면 작은 네모
    }

    /// <summary>전투 끝 배너 — 화면을 어둡게 하고 가운데에 任務終了 / Game Over 를 띄운다.</summary>
    private void DrawOutcomeBanner()
    {
        if (_outcome.Length == 0) return;
        bool win = _outcome.StartsWith('승');

        // 화면 전체를 절반 밝기로(원본 0x1002e8d0(2, 16))
        for (int y = _camY; y < _camY + ViewHeight; y++)
            for (int x = 0; x < BoardWidth; x++)
            {
                int i = y * BoardWidth + x;
                uint c = _fb[i];
                _fb[i] = 0xFF000000 | (c >> 16 & 0xFF) / 2 << 16 | (c >> 8 & 0xFF) / 2 << 8 | (c & 0xFF) / 2;
            }

        int tick = (int)((_lastTime - _outcomeAt) * TicksPerSecond);
        int cx = BoardWidth / 2, cy = _camY + ViewHeight / 2;
        if (DrawUi(BannerObs, win ? BannerWin : BannerLose, tick, cx, cy, UiBlend.Alpha, loop: false)) return;

        // 배너 그림이 없으면 글자로
        var (_, w, h) = GetText(_outcome, 0xFFFFE070, 32);
        DrawText(_outcome, cx - w / 2, cy - h / 2, 0xFFFFE070, 32);
    }
}

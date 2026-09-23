using DuelDx.Native;
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

    /// <summary>물체(상자·문)를 만질 수 있는 칸의 커서 — 주먹(<c>Obs 0050</c>).</summary>
    private const int CursorTouch = 50;
    private const int BannerObs = 491, BannerWin = 0, BannerLose = 10;

    private (int X, int Y) _mouse = (-1, -1);

    /// <summary>지금 마우스 자리에 맞는 커서 그림.</summary>
    private int CursorFor(int bx, int by)
    {
        if (_keysOpen || SystemOpen || _statusUnit >= 0 || _abilityMenu || _ringUnit >= 0) return CursorHand;
        if (_targetWork >= 0 && Work(_targetWork) is { } w)
        {
            // 사거리 안이고 그 work 의 대상 방식에 맞는 칸에서만 칼·지팡이가 된다 — 아무 유닛 위나 아니다.
            int col = bx / TileW, row = RowAt(bx, by);
            if (by < GridTop || row < 0 || _turn < 0 || !CanAimAt(w, _units[_turn], col, row)) return CursorArrow;
            return w.IsDamage ? CursorAttack : CursorSupport;
        }
        // 걸어가서 손댈 수 있는 물체(상자·문)면 주먹 커서 — 원본 「닿을 수 있는 오브젝트 칸」(층 1, 0x1006d0f0 갈래 4).
        if (IsPlayerTurn && by >= GridTop && ObjectAt(bx / TileW, RowAt(bx, by)) is { } near
            && FindTouchPath(_units[_turn], near) != null) return CursorTouch;

        // 내 차례에 적 위에 있으면 칼 커서 — 걸어가서 칠 수 있는 적이면 그렇다(fa-12 의 클릭 공격과 같은 판정).
        if (IsPlayerTurn && by >= GridTop && LiveUnitAt(bx / TileW, RowAt(bx, by)) is { } who)
        {
            if (!who.IsAlly && FindAttackPath(_turn, Array.IndexOf(_units, who)) != null) return CursorAttack;
            if (who.IsAlly) return CursorHand;
        }
        if (_rangeUnit >= 0 && _range is { } range && by >= GridTop)
        {
            int row = RowAt(bx, by);
            if (row >= 0 && range.CanReach(row * Cols + bx / TileW)) return CursorHand;
        }
        return CursorArrow;
    }

    /// <summary>
    /// 커서 컷을 판에 그리지 않고 <b>윈도 하드웨어 커서</b>로 건다 — 판에 그리면 60Hz 로만 움직이고
    /// 스왑체인을 거치느라 2~3프레임 늦으며, 판을 키운 화면(모세스 640×480)에서는 배율만큼 칸칸이 뛰었다.
    /// 컷마다 배율로 키운 커서를 한 번 만들어 두고, 컷 기준점을 핫스팟으로 삼는다.
    /// </summary>
    private readonly Dictionary<(SpriteFrame Frame, double Zoom), IntPtr> _hwCursors = [];
    private static readonly IntPtr ArrowCursor = Win32.LoadCursorW(IntPtr.Zero, Win32.IDC_ARROW);
    private IntPtr _hwCursor = ArrowCursor;

    /// <summary>프레임마다 — 지금 커서 컷(모양·애니)이 바뀌었으면 윈도 커서를 갈아 건다.</summary>
    private void UpdateCursor()
    {
        var (bx, by) = _mouse;
        int obs = bx < 0 || by < 0 ? CursorArrow : CursorFor(bx, by);
        int tick = (int)(_lastTime * TicksPerSecond);
        IntPtr want = UiFor(obs)?.FrameAt(0, tick) is { } f ? HardwareCursor(f) : ArrowCursor;
        if (want == _hwCursor) return;
        _hwCursor = want;
        if (CursorOverClient()) Win32.SetCursor(want);
    }

    /// <summary>마우스가 이 창의 판(클라이언트) 위에 있나 — 메뉴·테두리·다른 창 위의 커서는 건드리지 않는다.</summary>
    private bool CursorOverClient()
    {
        if (!Win32.GetCursorPos(out var p) || Win32.WindowFromPoint(p) != _hwnd) return false;
        Win32.ScreenToClient(_hwnd, ref p);
        return Win32.GetClientRect(_hwnd, out var rc) && p.X >= 0 && p.Y >= 0 && p.X < rc.Right && p.Y < rc.Bottom;
    }

    private IntPtr HardwareCursor(SpriteFrame f)
    {
        if (_hwCursors.TryGetValue((f, _zoom), out var cached)) return cached;
        int w = Math.Max(1, (int)Math.Round(f.W * _zoom)), h = Math.Max(1, (int)Math.Round(f.H * _zoom));
        var color = new uint[w * h];
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Min(f.H - 1, (int)(y / _zoom));
            for (int x = 0; x < w; x++)
            {
                uint c = f.Px[sy * f.W + Math.Min(f.W - 1, (int)(x / _zoom))];
                color[y * w + x] = (c & 0xFF000000) == 0 ? 0 : c | 0xFF000000;   // 판에 그릴 때처럼 알파는 있고 없고 둘뿐
            }
        }
        // 32비트 색 비트맵에 알파가 있으면 마스크는 안 쓰인다 — 그래도 꼭 넘겨야 해서 0 으로 채운 1비트 비트맵.
        var mask = new byte[(w + 15) / 16 * 2 * h];
        IntPtr colorBmp, maskBmp;
        fixed (uint* pc = color) colorBmp = Win32.CreateBitmap(w, h, 1, 32, pc);
        fixed (byte* pm = mask) maskBmp = Win32.CreateBitmap(w, h, 1, 1, pm);
        var info = new Win32.IconInfo
        {
            IsIcon = false,
            HotspotX = Math.Clamp((int)(-f.X * _zoom), 0, w - 1),
            HotspotY = Math.Clamp((int)(-f.Y * _zoom), 0, h - 1),
            Mask = maskBmp,
            Color = colorBmp,
        };
        IntPtr cursor = Win32.CreateIconIndirect(ref info);
        Win32.DeleteObject(colorBmp);
        Win32.DeleteObject(maskBmp);
        if (cursor == IntPtr.Zero) return ArrowCursor;
        _hwCursors[(f, _zoom)] = cursor;
        return cursor;
    }

    private void DestroyCursors()
    {
        foreach (var cursor in _hwCursors.Values) Win32.DestroyCursor(cursor);
        _hwCursors.Clear();
    }

    /// <summary>전투 끝 배너 — 화면을 어둡게 하고 가운데에 任務終了 / Game Over 를 띄운다.</summary>
    private void DrawOutcomeBanner()
    {
        if (_outcome.Length == 0) return;
        bool win = _outcome.StartsWith('승');

        // 화면 전체를 절반 밝기로(원본 0x1002e8d0(2, 16))
        for (int y = _camY; y < _camY + ViewHeight; y++)
            for (int x = _camX; x < _camX + ViewWidth; x++)
            {
                int i = y * BoardWidth + x;
                uint c = _fb[i];
                _fb[i] = 0xFF000000 | (c >> 16 & 0xFF) / 2 << 16 | (c >> 8 & 0xFF) / 2 << 8 | (c & 0xFF) / 2;
            }

        // 배너는 모션 0·1·2(승리) / 10·11·12(패배) 세 조각을 겹쳐 그린다 — 각 모션은 컷 하나(길이 0)다.
        int cx = _camX + ViewWidth / 2, cy = _camY + ViewHeight / 2;
        bool drawn = false;
        for (int i = 0; i < 3; i++)
            drawn |= DrawUi(BannerObs, (win ? BannerWin : BannerLose) + i, 0, cx, cy, UiBlend.Alpha);
        if (drawn) return;

        // 배너 그림이 없으면 글자로
        var (_, w, h) = GetText(_outcome, 0xFFFFE070, 32);
        DrawText(_outcome, cx - w / 2, cy - h / 2, 0xFFFFE070, 32);
    }
}

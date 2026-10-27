using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 전투 화면 왼쪽 위의 <b>미니맵</b> — 판 전체를 줄여 보이고, 인물 점(내 편 파랑 · 동맹 초록 · 적 빨강)과 지금 보이는 네모를 얹는다.
/// 원본에 없는 도움 기능이다(사용자 요청: 「btl 20089 왼쪽 위에 미니맵 나오게 해주고 접을 수 있게 해줘」).
/// </summary>
/// <remarks>
/// 판이 보이는 영역보다 클 때(카메라가 움직일 수 있을 때)만 뜬다 — 파트1 · 파트2 전투 모두. 미니맵 안을 누르면 그 자리로 카메라가 간다.
/// 머리 줄의 접기 단추(− 접기 · + 펴기)로 머리 줄만 남길 수 있고, 접은 상태는 설정(<see cref="UserSettings.MiniMapFolded"/>)에 남아 다음 전투 · 다음 실행에도 이어진다.
/// 그리기는 CPU 라 줄인 지형 그림은 <b>판(맵 그림)마다 한 번</b>만 만들어 두고, 틀마다는 점과 네모만 다시 그린다.
/// 상자 목록(모드 &gt; 상자 내용물 보기)도 왼쪽 위라 그 목록은 미니맵 아래로 내려 쌓는다(<see cref="StackHeight"/>).
/// </remarks>
internal sealed class BattleMiniMap(GameWindow host)
{
    /// <summary>줄인 그림이 들어갈 가장 큰 네모(판 좌표).</summary>
    private const int MaxW = 168, MaxH = 132;

    private const int HeaderH = 17, Pad = 4;

    /// <summary>접어 두었나 — 머리 줄만 남긴다. 설정 파일에 남는다.</summary>
    internal bool Folded = UserSettings.Current.MiniMapFolded;

    /// <summary>줄인 지형 그림과 그것을 만든 맵 그림 — 맵이 바뀌면 다시 만든다.</summary>
    private uint[]? _image;
    private ObtMapImage? _imageFor;
    private int _imgW, _imgH;

    /// <summary>줄인 그림이 비추는 판 영역(판 좌표)과 줄인 배율.</summary>
    private int _srcX0, _srcY0, _srcW, _srcH;
    private double _scale;

    /// <summary>지난 그리기에서 놓인 자리(판 좌표) — 안 떴으면 null. 클릭 · 상자 목록 쌓기가 쓴다.</summary>
    private (int X, int Y, int W, int H)? _box, _fold, _body;

    /// <summary>상자 목록을 이만큼 아래로 내린다 — 미니맵이 안 떠 있으면 0.</summary>
    internal int StackHeight => _box is { } b ? b.H + 6 : 0;

    /// <summary>미니맵을 띄울 때인가 — 창 · 배치 · 대사 · 모세스 · 결과 따위가 떠 있으면 숨긴다.</summary>
    private bool Visible =>
        host._battleLoaded && host.BoardIsMap && host._map != null && !host._loading
        && !host.BattleCovered && !host.ProgressScr._progressOpen && !host.Cut.Showing
        && !host.Btl._deployOpen && !host.Btl.EventsBusy && host.Btl._outcome.Length == 0 && !host.Btl.LevelUpOpen
        && host._statusUnit < 0 && !host.Sys.SystemOpen && !host.Part1PickOpen
        && (host.Btl.CamMaxX > 0 || host.CamMax > 0);

    /// <summary>판 좌표 (bx, by) 가 미니맵 위인가 — 가장자리 스크롤이 미니맵 위에서는 안 밀게 쓴다.</summary>
    internal bool Contains(int bx, int by) => _box is var (x, y, w, h) && bx >= x && bx < x + w && by >= y && by < y + h;

    /// <summary>미니맵을 눌렀으면 true — 접기 단추면 접거나 펴고, 그림이면 그 자리로 카메라를 옮긴다.</summary>
    /// <summary>접기 · 펴기 — 접기 단추와 M 키(KeyAction.MiniMap)가 같이 쓴다. 다음 전투 · 다음 실행에도 이어진다.</summary>
    internal void ToggleFold()
    {
        Folded = !Folded;
        host.SaveSettings();
    }

    internal bool OnClick(int bx, int by)
    {
        if (!Contains(bx, by)) return false;
        if (_fold is var (fx, fy, fw, fh) && bx >= fx && bx < fx + fw && by >= fy && by < fy + fh)
        {
            ToggleFold();
            return true;
        }
        if (_body is var (ix, iy, iw, ih) && bx >= ix && bx < ix + iw && by >= iy && by < iy + ih && _scale > 0
            && host.Btl.EdgeScrollAllowed)   // 내 차례 · 배치처럼 플레이어가 카메라를 움직일 수 있을 때만 — AI · 이벤트의 카메라 명령을 끊지 않는다
        {
            double x = _srcX0 + (bx - ix + 0.5) / _scale, y = _srcY0 + (by - iy + 0.5) / _scale;
            host.Btl.CenterNow(x, y);
        }
        return true;                         // 미니맵 위의 클릭은 밑의 판으로 안 내려보낸다
    }

    /// <summary>맵 그림을 줄여 둔다(맵마다 한 번) — 칸 넓이마다 평균을 낸다.</summary>
    private void EnsureImage(ObtMapImage map)
    {
        if (ReferenceEquals(_imageFor, map) && _image != null) return;
        _imageFor = map;
        _srcX0 = 0;
        _srcW = Math.Max(1, Math.Min(map.Width, host.BoardWidth));
        _srcY0 = GridTop + host.BoardPad + map.OriginY;
        _srcH = Math.Max(1, Math.Min(map.Height, host.BoardHeight - _srcY0));
        _scale = Math.Min((double)MaxW / _srcW, (double)MaxH / _srcH);
        _imgW = Math.Max(1, (int)Math.Round(_srcW * _scale));
        _imgH = Math.Max(1, (int)Math.Round(_srcH * _scale));
        var img = new uint[_imgW * _imgH];
        var src = map.Bgra;
        for (int y = 0; y < _imgH; y++)
        {
            int sy0 = y * _srcH / _imgH, sy1 = Math.Max(sy0 + 1, (y + 1) * _srcH / _imgH);
            for (int x = 0; x < _imgW; x++)
            {
                int sx0 = x * _srcW / _imgW, sx1 = Math.Max(sx0 + 1, (x + 1) * _srcW / _imgW);
                // 넓은 칸은 다 더하지 않고 2점 걸러 본다 — 큰 맵(2000×2000)도 한 번에 몇 ms.
                int stepX = Math.Max(1, (sx1 - sx0) / 4), stepY = Math.Max(1, (sy1 - sy0) / 4);
                long r = 0, g = 0, b = 0, n = 0;
                for (int sy = sy0; sy < sy1; sy += stepY)
                    for (int sx = sx0; sx < sx1; sx += stepX)
                    {
                        int i = (sy * map.Width + sx) * 4;
                        if (src[i + 3] == 0) continue;          // 그림 밖(투명)은 빼고 센다
                        b += src[i]; g += src[i + 1]; r += src[i + 2]; n++;
                    }
                img[y * _imgW + x] = n == 0 ? 0xFF101010 : 0xFF000000 | (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
            }
        }
        _image = img;
    }

    internal void Draw()
    {
        _box = _fold = _body = null;
        if (!Visible || host._map is not { } map) return;
        EnsureImage(map);
        if (_image is not { } img) return;

        int x = host._camX + 8, y = host._camY + (host._showStatusBar ? GridTop + 4 : 8);
        int w = _imgW + Pad * 2, h = HeaderH + (Folded ? 2 : _imgH + Pad);
        _box = (x - 4, y - 3, w, h + 3);
        host.UiFill(x - 4, y - 3, w, h + 3, 0xB0000000);
        host.UiText("미니맵", x + 2, y, 0xFFFFE070, 12);

        // 오른쪽 위 접기 단추 — 상자 목록과 같은 꼴(− 접기 · + 펴기).
        var fold = (X: x - 4 + w - 19, Y: y - 1, W: 16, H: 15);
        _fold = fold;
        bool over = host._mouse.X >= fold.X && host._mouse.X < fold.X + fold.W && host._mouse.Y >= fold.Y && host._mouse.Y < fold.Y + fold.H;
        host.UiFill(fold.X, fold.Y, fold.W, fold.H, over ? 0xFF2A4A8A : 0xFF303030);
        host.UiStroke(fold.X, fold.Y, fold.W, fold.H, 0xFF909090);
        host.UiFill(fold.X + 4, fold.Y + 7, 8, 1, White);
        if (Folded) { host.UiFill(fold.X + 7, fold.Y + 4, 1, 8, White); return; }

        int ix = x - 4 + Pad, iy = y - 3 + HeaderH + 2;
        _body = (ix, iy, _imgW, _imgH);
        if (host._hiResUi) host.Hi.DrawImage(img, _imgW, _imgW, _imgH, ix, iy, opaque: true);
        else CopyToBoard(img, ix, iy);
        host.UiStroke(ix - 1, iy - 1, _imgW + 2, _imgH + 2, 0xFF606060);

        // 상자 — 아직 안 연 것만 노란 네모(잠긴 것은 주황), 인물 점보다 먼저(사람이 서 있으면 사람이 보인다, 사용자 요청 fa-69).
        // 파트1 은 Obt 상자(Part1MapObjects.Chests), 파트2 는 물체 갈래 2(폭탄 8 은 뺀다).
        var chests = host._scene.IsPart1
            ? host.Btl.Part1Objects.Chests().Where(c => !c.Opened).Select(c => (c.Col, c.Row, c.Locked))
            : host.Btl.Objects.Where(o => o.Data.Kind == 2 && !host.Btl._opened.Contains(o)).Select(o => (o.Col, o.Row, Locked: false));
        foreach (var (col, row, locked) in chests)
        {
            int px = ix + (int)((col * TileW + TileW / 2 - _srcX0) * _scale), py = iy + (int)((host.CellCenterY(col, row) - _srcY0) * _scale);
            if (px < ix + 1 || py < iy + 1 || px >= ix + _imgW - 1 || py >= iy + _imgH - 1) continue;
            host.UiFill(px - 2, py - 2, 5, 5, 0xFF000000);
            host.UiFill(px - 1, py - 1, 3, 3, locked ? 0xFFFF9020 : 0xFFFFE040);
        }

        // 인물 점 — 쓰러졌거나 아직 안 나온 사람은 뺀다. 지금 차례인 사람은 흰 테를 두른다.
        var units = host._units;
        for (int i = 0; i < units.Length; i++)
        {
            var u = units[i];
            if (!u.Alive || !u.OnField || u.Fade <= 0) continue;
            var (fx, fy) = host.Btl.UnitFoot(u);
            int px = ix + (int)((fx - _srcX0) * _scale), py = iy + (int)((fy - TileH / 2 - _srcY0) * _scale);
            if (px < ix || py < iy || px >= ix + _imgW || py >= iy + _imgH) continue;
            uint color = u.Side == 4 ? 0xFF40A0FF : u.IsAlly ? 0xFF50E050 : 0xFFFF4040;
            if (i == host.Btl._turn) host.UiFill(px - 2, py - 2, 5, 5, White);
            host.UiFill(px - 1, py - 1, 3, 3, color);
        }

        // 지금 보이는 네모 — 미니맵 밖으로 나가는 쪽은 자른다.
        int rx0 = Math.Clamp(ix + (int)((host._camX - _srcX0) * _scale), ix, ix + _imgW - 1);
        int ry0 = Math.Clamp(iy + (int)((host._camY - _srcY0) * _scale), iy, iy + _imgH - 1);
        int rx1 = Math.Clamp(ix + (int)Math.Ceiling((host._camX + host.ViewWidth - _srcX0) * _scale), rx0 + 2, ix + _imgW);
        int ry1 = Math.Clamp(iy + (int)Math.Ceiling((host._camY + host.ViewHeight - _srcY0) * _scale), ry0 + 2, iy + _imgH);
        host.UiStroke(rx0, ry0, rx1 - rx0, ry1 - ry0, 0xFFFFFFFF);
    }

    /// <summary>옛 UI(판에 그리기) — 줄인 그림을 판 버퍼에 그대로 베낀다(판 밖은 자른다).</summary>
    private void CopyToBoard(uint[] img, int x, int y)
    {
        var fb = host._fb;
        int bw = host.BoardWidth, bh = host.BoardHeight;
        int x0 = Math.Max(0, x), x1 = Math.Min(bw, x + _imgW);
        if (x0 >= x1) return;
        for (int row = 0; row < _imgH; row++)
        {
            int by = y + row;
            if ((uint)by >= bh) continue;
            img.AsSpan(row * _imgW + (x0 - x), x1 - x0).CopyTo(fb.AsSpan(by * bw + x0, x1 - x0));
        }
    }
}

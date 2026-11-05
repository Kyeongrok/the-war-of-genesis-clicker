using System.Collections.Concurrent;
using System.Drawing.Imaging;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투 맵의 장식(배경) 그림 — 맵 그림(Obt)의 검은 자리 뒤에 깔리는 하늘 · 우주 · 먼 풍경(<see cref="BattleFile.DecorOfMap"/>).
/// </summary>
/// <remarks>
/// 버그 리포트 #46: Btl 0139 보아즈교 성지(Map 0122 · Obt 145)의 절벽 뒤 붉은 하늘(Bgr 0060)이 안 나오고 까맸다 — 이 덩이를 아예 안 읽었다.
/// 같은 덩이를 가진 맵이 20개, 전투가 쓰는 것은 18개다(Btl 0017 · 0051 · 0072 · 0077 · 0078 · 0096 · 0131 · 0137 · 0139 · 0157 · 0158 · 0168 · 0182~0186 · 0210 · 0257 · 0270 · 0272 · 0273 · 0280 · 0292 · 0298 · 0305 · 0315 · 0316 · 0318 · 0324 · 0325).
/// 원본 그리기 <c>0x100d6c00</c> 은 장식(<c>0x100d6cc0</c>)을 먼저 그리고 Obt(<c>0x10027c20</c>)를 그 위에 얹는다 — Obt 의 검은(555 값 0) 자리로 장식이 보인다.
/// <list type="bullet">
/// <item>네모(맵 그림 기준) = 칸 (x, y) ~ (x+폭, y+높이) × 40·32 — 맵 그림 자리에서 Obt 머리의 세로 자리(<c>+0x1a</c> × 8)를 뺀다(<c>0x100d67d2~0x100d6854</c>).
/// 판 자리로 옮기면 그냥 칸 × 40·32 다.</item>
/// <item>그림은 제 크기(폭 · 높이) 한 장만 그린다. 네모가 그림보다 크면 카메라를 따라 시차로 밀린다 —
/// <c>밀림 = (카메라 − 아래끝) × (네모폭 − 그림폭) / (위끝 − 아래끝)</c>, 아래끝 = max(네모 왼쪽 − 화면폭, 카메라 최소), 위끝 = min(카메라 최대, 네모 오른쪽)
/// (<c>0x100d9339~0x100d93cb</c> 이 끝을 정하고 <c>0x100d6d84~0x100d6e22</c> 가 민다). 세로도 같다.</item>
/// <item>틱마다 흐름(낱말 2·3)만큼 그림 안 자리가 돌아 감긴다(<c>0x100d6b74~</c>, 그리기 <c>0x100d6eda~</c> 가 나머지로 두 번 찍는다).</item>
/// <item>그림 번호 ≥ 10000 은 <c>Mov</c> 동영상이다 — 쓰는 맵이 없어 안 옮겼다.</item>
/// </list>
/// 카메라 끝은 우리 카메라(<see cref="BattleScene.CamMaxX"/> · <see cref="CamMax"/>)로 잡는다 — 원본은 맵 그림 크기 − 640×480 이다.
/// </remarks>
internal sealed unsafe partial class GameWindow
{
    /// <summary>읽어 둔 장식 그림(Bgr 번호 → 픽셀) — 못 읽었으면 null. 뒤 스레드가 채운다.</summary>
    private static readonly ConcurrentDictionary<int, (uint[] Px, int W, int H)?> DecorPictures = new();

    /// <summary>지금 판의 장식 흐름이 시작된 때(<see cref="_lastTime"/>) — 판이 바뀌면 다시 0 틱부터.</summary>
    private DemoScene? _decorScene;
    private double _decorStart;

    /// <summary>장식 그림을 뒤에서 읽게 건다 — 그리기가 기다리지 않게(받는 길이면 Fetch 가 몇 초 걸린다).</summary>
    private static (uint[] Px, int W, int H)? DecorPicture(int id)
    {
        if (DecorPictures.TryGetValue(id, out var got)) return got;
        if (DecorPictures.TryAdd(id, null))
            Task.Run(() =>
            {
                var loaded = LoadDecorPicture(id);
                if (loaded != null) DecorPictures[id] = loaded;
                else if (AssetPack.MayCome("moses/bgr", $"{id:D4}.bgr")) DecorPictures.TryRemove(id, out _);   // 아직 받는 중 — 다음에 다시
            });
        return null;
    }

    private static (uint[] Px, int W, int H)? LoadDecorPicture(int id)
    {
        try
        {
            if (AssetPack.Fetch("moses/bgr", $"{id:D4}.bgr") is not { } path) return null;
            using var bitmap = new System.Drawing.Bitmap(path);
            int w = bitmap.Width, h = bitmap.Height;
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var px = new uint[w * h];
                for (int y = 0; y < h; y++)
                    new Span<uint>((void*)(data.Scan0 + y * data.Stride), w).CopyTo(px.AsSpan(y * w, w));
                for (int i = 0; i < px.Length; i++) px[i] |= 0xFF000000;
                return (px, w, h);
            }
            finally { bitmap.UnlockBits(data); }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>
    /// 카메라가 비추는 네모(<paramref name="vx0"/>~<paramref name="vx1"/> × <paramref name="vy0"/>~<paramref name="vy1"/>, 판 자리)에 장식을 깐다.
    /// 하나라도 그렸으면 참 — 그러면 <see cref="DrawBackground"/> 가 맵의 검은 점을 뚫고 얹는다.
    /// </summary>
    private bool DrawMapDecor(int vx0, int vx1, int vy0, int vy1)
    {
        var decor = _scene.MapDecor;
        if (decor.Count == 0) return false;
        if (!ReferenceEquals(_decorScene, _scene)) { _decorScene = _scene; _decorStart = _lastTime; }
        long ticks = (long)Math.Max(0, (_lastTime - _decorStart) * TicksPerSecond);
        int top0 = GridTop + BoardPad;
        bool drew = false;
        fixed (uint* fb = _fb)
            foreach (var d in decor)
            {
                if (d.Picture >= 10000 || DecorPicture(d.Picture) is not { } pic) continue;
                int rx1 = d.Col * ObtMap.CellWidth, rx2 = (d.Col + d.Cols) * ObtMap.CellWidth;
                int ry1 = top0 + d.Row * ObtMap.CellHeight, ry2 = top0 + (d.Row + d.Rows) * ObtMap.CellHeight;
                // 네모가 화면에 걸칠 때만(0x100d6d00~0x100d6d48).
                if (_camX > rx2 || _camY > ry2 || _camX + ViewWidth <= rx1 || _camY + ViewHeight <= ry1) continue;
                int left = rx1 + Parallax(_camX, rx1, rx2, pic.W, ViewWidth, Btl.CamMaxX);
                int top = ry1 + Parallax(_camY, ry1, ry2, pic.H, ViewHeight, CamMax);
                // 흐름 — 음수는 원본처럼 그림 크기를 더해 양수로 돌린 뒤 틱마다 더한다(0x100d6865~0x100d68b8).
                int sx = (int)(((d.FlowX % pic.W + pic.W) % pic.W * ticks) % pic.W);
                int sy = (int)(((d.FlowY % pic.H + pic.H) % pic.H * ticks) % pic.H);
                int x0 = Math.Max(Math.Max(vx0, left), 0), x1 = Math.Min(Math.Min(vx1, left + pic.W), BoardWidth);
                int y0 = Math.Max(Math.Max(vy0, top), 0), y1 = Math.Min(Math.Min(vy1, top + pic.H), BoardHeight);
                if (x0 >= x1 || y0 >= y1) continue;
                fixed (uint* src = pic.Px)
                    for (int y = y0; y < y1; y++)
                    {
                        uint* row = src + (y - top + sy) % pic.H * pic.W;
                        uint* dst = fb + y * BoardWidth;
                        for (int x = x0; x < x1; x++) dst[x] = row[(x - left + sx) % pic.W];
                    }
                drew = true;
            }
        return drew;
    }

    /// <summary>네모가 그림보다 크면 카메라가 아래끝 → 위끝으로 갈 때 그림을 0 → (네모 − 그림)만큼 민다(<c>0x100d6d84~0x100d6e22</c>).</summary>
    private static int Parallax(int cam, int r1, int r2, int pic, int view, int camMax)
    {
        int spare = r2 - r1 - pic;
        if (spare <= 0) return 0;
        int lo = Math.Max(r1 - view, 0), hi = Math.Min(camMax, r2);
        return hi > lo ? (cam - lo) * spare / (hi - lo) : 0;
    }
}

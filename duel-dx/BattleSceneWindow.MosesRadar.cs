namespace DuelDx;

/// <summary>
/// 항행 단계 2(장소 고르기)의 레이더 — 행성 구체 위를 도는 초록 격자 구와 장소 조각(분석-모세스 6절 「레이더가 그리는 것」, 클래스 <c>0x10103880</c>).
/// </summary>
/// <remarks>
/// 반지름 74 의 구를 위도 11줄(0~180°, 18° 씩, 0 = 북극) × 경도 10줄(36° 씩) 격자로 잡아 (320,220)에 놓고, x 30°·z 160° 기울인 채
/// 세로축으로 틱마다 3° 돌린다(120틱에 한 바퀴). 계산은 원본처럼 10비트 고정소수점이고 sin·cos 표도 같은 식으로 만든다(<c>0x10103bf0</c>).
/// 앞면 선만 깊이에 따라 밝은 초록으로 긋고, 후보 장소는 장소 레코드의 경도칸(<c>+0x08</c>)·위도칸(<c>+0x0a</c>) 격자 한 칸을
/// 노란 조각으로, 마우스가 올라간 장소는 빨간 조각으로 숨쉬듯(약 67틱 주기) 채운다. 구체 그림 위에 그리는 차례는 가설이다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int RadarCenterX = 320 << 10, RadarCenterY = 220 << 10;
    private const double RadarRadius = 74.0;
    private const int RadarTiltX = 30, RadarTiltZ = 160;

    private static readonly int[] RadarSin = new int[360], RadarCos = new int[360];
    private static readonly (int X, int D, int Y)[] RadarLattice = new (int, int, int)[110];

    static BattleSceneWindow()
    {
        // 0x10103bf0 — sin·cos 표(×1024, +0.5 뒤 0 쪽으로 자름)와 격자점 110개(경도칸×11 + 위도칸).
        for (int i = 0; i < 360; i++)
        {
            RadarSin[i] = (int)(Math.Sin(i * 0.01745328888888889) * 1024.0 + 0.5);
            RadarCos[i] = (int)(Math.Cos(i * 0.01745328888888889) * 1024.0 + 0.5);
        }
        for (int col = 0; col < 10; col++)
        {
            double b = 2 * col * 0.3141592;
            for (int row = 0; row < 11; row++)
            {
                double a = row * 0.3141592;
                int y = (int)(Math.Cos(a) * 75776.0) + RadarCenterY;          // 74·1024
                double r = Math.Sin(a) * RadarRadius;
                int x = RadarCenterX - (int)(Math.Cos(b) * r * -1024.0);
                int d = (int)(Math.Sin(b) * r * 1024.0);
                RadarLattice[col * 11 + row] = (x, d, y);
            }
        }
    }

    /// <summary>0x101039e0 — 세 각(도)으로 격자점을 돌린 것. 식은 원본 그대로(&gt;&gt; 는 버림 시프트).</summary>
    private static (int X, int D, int Y)[] RadarRotate(int a1, int a2, int a3)
    {
        int s1 = RadarSin[((a1 % 360) + 360) % 360], c1 = RadarCos[((a1 % 360) + 360) % 360];
        int s2 = RadarSin[((a2 % 360) + 360) % 360], c2 = RadarCos[((a2 % 360) + 360) % 360];
        int s3 = RadarSin[((a3 % 360) + 360) % 360], c3 = RadarCos[((a3 % 360) + 360) % 360];
        var outPts = new (int, int, int)[RadarLattice.Length];
        for (int i = 0; i < RadarLattice.Length; i++)
        {
            var (x, d, y) = RadarLattice[i];
            long xr = x - RadarCenterX, yr = y - RadarCenterY;
            long ox = ((xr * ((c3 * c2) >> 10) - d * ((s3 * c2) >> 10) + yr * s2) >> 10) + RadarCenterX;
            long od = (xr * (((s1 * s2) * c3 >> 20) + ((c1 * s3) >> 10))
                       + d * (((c1 * c3) >> 10) - ((s1 * s2) * s3 >> 20))
                       - yr * ((s1 * c2) >> 10)) >> 10;
            long oy = ((xr * (((s1 * s3) >> 10) - ((c1 * s2) * c3 >> 20))
                        + d * (((c1 * s2) * s3 >> 20) + ((s1 * c3) >> 10))
                        + yr * ((c1 * c2) >> 10)) >> 10) + RadarCenterY;
            outPts[i] = ((int)ox, (int)od, (int)oy);
        }
        return outPts;
    }

    /// <summary>0x10103d10 — 두 끝 깊이의 평균을 반지름 74 에 대한 백분율로 바꿔 127 을 더한 값. 128 미만이면 뒷면이라 안 그린다.</summary>
    private static int? RadarShade(int dSum)
    {
        int v = (dSum >> 11) * 100;
        v = v >= 0 ? v / 74 : -((-v) / 74);
        v = (v + 0x7f) & 0xff;
        return (v & 0x80) != 0 ? v : null;
    }

    /// <summary>레이더를 (ox, oy) 모세스 틀 안에 그린다 — places = 후보 장소의 (경도칸, 위도칸) 후보 순서, hover = 마우스 아래 칸 번호.</summary>
    private void DrawMosesRadar(int ox, int oy, int tick, IReadOnlyList<(int Lon, int Lat)> places, int hover)
    {
        var pts = RadarRotate(RadarTiltX, RadarTiltZ, 3 * (tick % 120));
        int Px(int v) => v >> 10;

        void Line(int k1, int k2)
        {
            if (RadarShade(pts[k1].D + pts[k2].D) is not { } v) return;
            uint g = (uint)(v & 0xf8);
            DrawSegment(ox + Px(pts[k1].X), oy + Px(pts[k1].Y), ox + Px(pts[k2].X), oy + Px(pts[k2].Y), 0xFF000000 | 0x080008 | (g << 8));
        }

        for (int col = 0; col < 10; col++)                          // 경선 (0x10103d28)
            for (int row = 0; row < 10; row++)
                Line(col * 11 + row, col * 11 + row + 1);
        for (int row = 0; row < 10; row++)                          // 위선 (0x10103e05) — 남극 줄은 안 잇는다
            for (int col = 0; col < 10; col++)
                Line(col * 11 + row, ((col + 1) % 10) * 11 + row);

        int pulse = Math.Abs(100 - (3 * tick) % 200);
        uint c = (uint)((0xff - pulse) & 0xf8);
        for (int i = 0; i < places.Count; i++)
        {
            var (col, row) = places[i];
            if (col < 0 || row < 0) continue;
            int k00 = col * 11 + row, k01 = col * 11 + (row + 1) % 10;
            int k10 = ((col + 1) % 10) * 11 + row, k11 = ((col + 1) % 10) * 11 + (row + 1) % 10;
            if (pts[k00].D + pts[k01].D + pts[k10].D + pts[k11].D < 0) continue;
            uint colour = 0xFF000000 | (c << 16) | (i == hover ? 0u : c << 8);
            FillQuad([(ox + Px(pts[k00].X), oy + Px(pts[k00].Y)), (ox + Px(pts[k01].X), oy + Px(pts[k01].Y)),
                      (ox + Px(pts[k11].X), oy + Px(pts[k11].Y)), (ox + Px(pts[k10].X), oy + Px(pts[k10].Y))], colour);
        }
    }

    /// <summary>브레젠험 선 — 원본은 <c>0x1000dfe0</c> 로 긋는다.</summary>
    private void DrawSegment(int x0, int y0, int x1, int y1, uint colour)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        for (int guard = 0; guard < 4096; guard++)
        {
            SetPixel(x0, y0, colour);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    /// <summary>볼록 네모 조각을 가로줄로 채운다 — 원본은 다각형 객체(<c>0x1000a760</c>)를 만들어 <c>0x1000aee0</c> 로 채운다.</summary>
    private void FillQuad((int X, int Y)[] quad, uint colour)
    {
        int minY = quad.Min(p => p.Y), maxY = quad.Max(p => p.Y);
        for (int y = minY; y <= maxY; y++)
        {
            int left = int.MaxValue, right = int.MinValue;
            for (int i = 0; i < quad.Length; i++)
            {
                var (ax, ay) = quad[i];
                var (bx, by) = quad[(i + 1) % quad.Length];
                if (ay == by) { if (y == ay) { left = Math.Min(left, Math.Min(ax, bx)); right = Math.Max(right, Math.Max(ax, bx)); } continue; }
                if (y < Math.Min(ay, by) || y > Math.Max(ay, by)) continue;
                int x = ax + (bx - ax) * (y - ay) / (by - ay);
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
            for (int x = left; x <= right; x++) SetPixel(x, y, colour);
        }
    }
}

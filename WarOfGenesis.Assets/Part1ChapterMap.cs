using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1 챕터(지역) 지도 그림 — <c>obts\NNNN.obt</c>(챕터 머리 낱말 0)를 40×8 띠로 깐 것. 바다 자리는 알파 0 으로 비워 둔다.
/// </summary>
/// <param name="Px">ARGB. 알파 0 인 픽셀은 바다(그리는 쪽이 <c>BGRs\0000.bgr</c> 물결 무늬로 채운다).</param>
public sealed record Part1ChapterMap(int Width, int Height, uint[] Px)
{
    /// <summary>
    /// 지도 객체 <c>alpha.dll 0x100c3960</c>(장면 4 처음 꾸미기 <c>0x100b2940</c> 이 머리 낱말 0 으로 부른다)이 읽는 차례 — 전투 맵(<see cref="Part1Map"/>)과 같은 파일 꼴:
    /// <code>
    /// u32 장 수, s16 cols, s16 rows, cols*rows*2 바이트(높이 · 지형 — 지도는 건너뛴다)
    /// s16 W(+0x48), H(+0x4a), layerX, layerY; W*H × u32 띠(위 낱말 = 타일 번호)
    /// u32 윗층 수(지도 파일은 0), u32 t, t × 640 바이트(40×8 RGB555), t × 1 바이트(1 = 꽉 찬 타일)
    /// u32, u32 주기(+0xc968), u32 k, k × 18 바이트(+0xc970 — 시각 사건: 갈래 1 소리 · 2 그림 띄우기)
    /// </code>
    /// 그리기 <c>0x100c3e10</c>: 띠 타일 번호 0 → 바다만, 타일 바이트 0 → 바다를 깔고 그 위에 타일(색 0 투명), 아니면 타일만.
    /// </summary>
    public static Part1ChapterMap? Load(int id) =>
        id > 0 && Part1Game.Read("Obts", $"{id:D4}.obt") is { } bytes ? Parse(bytes) : null;

    public static Part1ChapterMap? Parse(byte[] bytes)
    {
        const int SW = 40, SH = 8, TileBytes = SW * SH * 2;
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            if (reader.ReadInt32() < 1) return null;
            int cols = reader.ReadInt16(), rows = reader.ReadInt16();
            if (cols < 0 || rows < 0) return null;
            reader.BaseStream.Seek((long)cols * rows * 2, SeekOrigin.Current);
            int w = reader.ReadInt16(), h = reader.ReadInt16();
            reader.ReadInt16(); reader.ReadInt16();
            if (w <= 0 || h <= 0 || w > 400 || h > 2000) return null;
            var ids = new int[w * h];
            for (int i = 0; i < ids.Length; i++) { reader.ReadInt16(); ids[i] = reader.ReadUInt16(); }
            int uppers = reader.ReadInt32();   // 원본 지도 객체는 읽고 버린다(지도 파일은 모두 0) — 혹시 있으면 건너뛴다
            for (int i = 0; i < uppers; i++)
            {
                reader.ReadInt32();
                int uw = reader.ReadInt16(), uh = reader.ReadInt16();
                reader.BaseStream.Seek((long)uw * uh * 4, SeekOrigin.Current);
            }
            int count = reader.ReadInt32();
            if (count < 0 || (long)count * (TileBytes + 1) > bytes.Length) return null;
            byte[] tiles = reader.ReadBytes(count * TileBytes), solid = reader.ReadBytes(count);
            if (solid.Length != count) return null;

            int width = w * SW, height = h * SH;
            var px = new uint[width * height];   // 0 = 바다
            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                if (id == 0 || id >= count) continue;
                bool keyed = solid[id] == 0;
                int sx = i % w * SW, sy = i / w * SH, src = id * TileBytes;
                for (int py = 0; py < SH; py++)
                    for (int x = 0; x < SW; x++, src += 2)
                    {
                        int c = tiles[src] | tiles[src + 1] << 8;
                        if (keyed && c == 0) continue;
                        int r = (c >> 10 & 0x1F) * 255 / 31, g = (c >> 5 & 0x1F) * 255 / 31, b = (c & 0x1F) * 255 / 31;
                        px[(sy + py) * width + sx + x] = 0xFF000000u | (uint)r << 16 | (uint)g << 8 | (uint)b;
                    }
            }
            return new Part1ChapterMap(width, height, px);
        }
        catch (EndOfStreamException) { return null; }
    }

    /// <summary>물결 표 — <c>0x100c4110</c>: 64칸, (int)(cos(i·2π/64)·4).</summary>
    public static readonly int[] Wave = [.. Enumerable.Range(0, 64).Select(i => (int)(Math.Cos(i * (float)(Math.PI * 2 / 64)) * 4.0))];

    /// <summary>
    /// 그 틱의 바다 무늬(128×128, <c>0x100c3d40</c>) — 바다 그림 (128×128) 을 출렁여 옮긴다:
    /// 칸 (x, y) ← 그림 ((x + 2·물결[(f/4 + 2y) &amp; 63]) &amp; 127, (y − 물결[(f/2 − 32 + x) &amp; 63]) &amp; 127).
    /// 띠는 세계 자리 &amp; 127 로 이 무늬를 찍는다(원본 버퍼는 160×160 이지만 128 마다 되풀이된다).
    /// </summary>
    public static void Ripple(uint[] sea, int seaW, int seaH, int frame, uint[] into)
    {
        for (int y = 0; y < 128; y++)
        {
            int dx = 2 * Wave[((frame >> 2) + 2 * y) & 63];
            for (int x = 0; x < 128; x++)
            {
                int sx = (x + dx) & 127, sy = (y - Wave[((frame >> 1) - 32 + x) & 63]) & 127;
                into[y * 128 + x] = sea[sy % seaH * seaW + sx % seaW];
            }
        }
    }

    /// <summary>
    /// 구름 한 장(<c>0x100c43b0</c> → <c>0x100c44b0</c>) — 128×128 회색(0~31) 중점 변위 무늬, 가장자리는 0.
    /// 칸 사이 가운데 = 두(네) 끝 평균 + 2·((난수 % 64 − 32) · 폭 / 128), 0~31 로 자른다(<c>0x100c45c0</c>, 넷으로 나눠 되풀이).
    /// </summary>
    public static byte[] Cloud(Random rng)
    {
        const int N = 128, Unset = 255;
        var v = new byte[N * N];
        Array.Fill(v, (byte)Unset);
        for (int i = 0; i < N; i++) v[i] = v[(N - 1) * N + i] = v[i * N] = v[i * N + N - 1] = 0;
        int Disp(int span) => (rng.Next(64) - 32) * span / N * 2;
        void Set(int x, int y, int value) { if (v[y * N + x] == Unset) v[y * N + x] = (byte)Math.Clamp(value, 0, 31); }
        void Split(int x0, int y0, int x1, int y1)
        {
            if (x1 - x0 <= 1 && y1 - y0 <= 1) return;
            int mx = (x0 + x1) >> 1, my = (y0 + y1) >> 1;
            int a = v[y0 * N + x0], b = v[y0 * N + x1], c = v[y1 * N + x0], d = v[y1 * N + x1];
            Set(mx, y0, (a + b) / 2 + Disp(x1 - x0));
            Set(x1, my, (b + d) / 2 + Disp(y1 - y0));
            Set(mx, y1, (c + d) / 2 + Disp(x1 - x0));
            Set(x0, my, (a + c) / 2 + Disp(y1 - y0));
            Set(mx, my, (a + b + c + d) / 4 + Disp(x1 - x0));
            Split(x0, y0, mx, my); Split(mx, y0, x1, my); Split(x0, my, mx, y1); Split(mx, my, x1, y1);
        }
        Split(0, 0, N - 1, N - 1);
        for (int i = 0; i < v.Length; i++) if (v[i] == Unset) v[i] = 0;
        return v;
    }
}

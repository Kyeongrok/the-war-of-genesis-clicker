namespace WarOfGenesis.Assets;

/// <summary>
/// 사용자 그림 만들기 — PNG 시트(줄 = 방향, 칸 = 장)를 잘라 배경을 지우고 게임 키로 줄여 파트2 꼴 Obs(동작 × 방향 모션표)로 싼다
/// (사용자 요청 menu-103 · ed-4). <c>tools/custom-sprite/sheet_to_obs.py</c> 를 C# 으로 옮긴 것 — 편집기가 파이썬 없이 쓴다.
/// </summary>
/// <remarks>
/// 그림은 부르는 쪽이 BGRA(B, G, R, A 바이트 순)로 풀어 준다(<see cref="Image"/>). 배경은 색 키(그 색에서 <c>KeyTol</c> 안) 또는 칸 가장자리에서
/// 이웃 색 차가 <c>Flood</c> 이하인 곳으로 번져 지운다. 장마다 발밑(기준 <see cref="Anchor"/>)을 원점에 두고, 모든 장을 한 팔레트(255색 + 투명 0)로 싼다.
/// Obs 꼴은 <see cref="Part1Game.ToPart2Obs"/> 가 쓰는 것과 같다 — 그림 자료는 건너뜀 0 레코드로 칸을 차례로 다 적는다.
/// 모션 번호 = 동작 × 3 + 방향(0 뒷모습 · 1 옆모습(왼쪽) · 2 앞모습). 걷기만 있고 서기가 없으면 걷기 첫 장을 서기로.
/// </remarks>
public static class CustomSpriteBuilder
{
    public sealed record Image(int Width, int Height, byte[] Bgra);

    public enum Anchor { Body, Foot, Cell }

    public enum Direction { Back = 0, Side = 1, Front = 2 }

    public sealed record Row(Direction Dir, int Top, int Bottom);

    /// <summary>시트 한 장의 설정. <paramref name="Key"/> 가 있으면 색 키(R, G, B), 없으면 번짐.</summary>
    /// <param name="OriginX">칸 안 원점(발밑) — 3D 렌더처럼 모든 칸을 같은 카메라로 찍은 시트는 렌더러가 안다(tools/custom-sprite/render_3d.py, ed-7).
    /// 0 이상이면 그림에서 재지 않고 이 자리를 원점으로 쓴다(<see cref="Anchor"/> 무시). −1 이면 예전처럼 잰다.</param>
    /// <param name="PixelHeight">시트에서 인물 키(픽셀) — 0 보다 크면 배율 = 게임 키 ÷ 이것(시트마다 재지 않아 동작마다 몸 크기가 같다).</param>
    public sealed record Part(Image Sheet, int Action, IReadOnlyList<Row> Rows, int ColX, int ColW, int Cols,
                              (byte R, byte G, byte B)? Key, int KeyTol = 40, int Flood = 5, Anchor Anchor = Anchor.Body,
                              IReadOnlyList<int>? Hits = null, int Ability = 25, IReadOnlyList<(int Sound, int Frame)>? Sounds = null, int Ticks = 0,
                              int OriginX = -1, int OriginY = -1, double PixelHeight = 0);

    public sealed record Result(byte[] Obs, Image Preview, int Frames, IReadOnlyList<int> Motions, IReadOnlyList<double> Scales);

    /// <summary>발밑이 그림 원점보다 아래로 내려오는 픽셀(파트1 · 파트2 인물 그림이 대개 이렇다).</summary>
    private const int FootBelow = 12;

    private sealed class Rgba(int w, int h)
    {
        public readonly int W = w, H = h;
        public readonly byte[] Px = new byte[w * h * 4];   // B G R A
        public bool Opaque(int x, int y) => Px[(y * W + x) * 4 + 3] != 0;
        public (int L, int T, int R, int B)? Bbox()
        {
            int l = W, t = H, r = -1, b = -1;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    if (Opaque(x, y)) { l = Math.Min(l, x); r = Math.Max(r, x); t = Math.Min(t, y); b = Math.Max(b, y); }
            return r < 0 ? null : (l, t, r + 1, b + 1);
        }
        public Rgba Crop(int l, int t, int r, int b)
        {
            var c = new Rgba(r - l, b - t);
            for (int y = 0; y < c.H; y++) Array.Copy(Px, ((t + y) * W + l) * 4, c.Px, y * c.W * 4, c.W * 4);
            return c;
        }
    }

    public static Result Build(IReadOnlyList<Part> parts, int height = 100, int walkTicks = 3, int standTicks = 8)
    {
        var frames = new List<(Rgba Piece, int Ox, int Oy)>();
        var keyed = new List<(int Action, int Dir, List<int> Ids, Part Part)>();
        var scales = new List<double>();
        foreach (var part in parts)
        {
            var cut = new List<(int Dir, List<Rgba> Cells, int Foot)>();
            foreach (var row in part.Rows)
            {
                var cells = Enumerable.Range(0, part.Cols).Select(i => Cut(part, part.ColX + i * part.ColW, row.Top, part.ColX + (i + 1) * part.ColW, row.Bottom)).ToList();
                var boxes = cells.Select(c => c.Bbox()).Where(b => b != null).ToList();
                if (boxes.Count == 0) { cut.Add(((int)row.Dir, cells, 0)); continue; }
                cut.Add(((int)row.Dir, cells, boxes.Max(b => b!.Value.B)));
            }
            var heights = cut.SelectMany(r => r.Cells.Select(c => c.Bbox()).Where(b => b != null).Select(b => r.Foot - b!.Value.T)).Order().ToList();
            if (heights.Count == 0) continue;
            double scale = height / (part.PixelHeight > 0 ? part.PixelHeight : heights[heights.Count / 2]);
            scales.Add(scale);
            foreach (var (dir, cells, foot) in cut)
            {
                int rowTop = part.Rows.FirstOrDefault(r => (int)r.Dir == dir)?.Top ?? 0;
                var ids = new List<int>();
                double? rowShift = null;
                for (int ci = 0; ci < cells.Count; ci++)
                {
                    var cell = cells[ci];
                    if (cell.Bbox() == null) continue;
                    // Cut 은 시트 가장자리 2픽셀을 빼므로 맨 왼 칸 · 맨 윗줄은 칸 시작이 그만큼 밀린다 — 주어진 원점을 그만큼 당긴다.
                    int cutDx = Math.Max(0, 2 - (part.ColX + ci * part.ColW)), cutDy = Math.Max(0, 2 - rowTop);
                    var small = Resize(cell, Math.Max(1, (int)Math.Round(cell.W * scale)), Math.Max(1, (int)Math.Round(cell.H * scale)));
                    if (small.Bbox() is not { } bb) continue;
                    double fx, fy;
                    if (part.OriginX >= 0 && part.OriginY >= 0) { fx = (part.OriginX - cutDx) * scale; fy = (part.OriginY - cutDy) * scale; }   // 렌더러가 준 원점(칸 기준)
                    else switch (part.Anchor)
                    {
                        case Anchor.Cell: fx = small.W / 2.0; fy = foot * scale; break;
                        case Anchor.Foot:
                            // 디딘 뒷발 — 첫 장(서기 자세)에서 뒷발과 몸 가운데의 차를 재 두고 더해, 첫 장이 서기 · 걷기와 같은 자리에 서게 한다.
                            double rear = RearFoot(small, bb, dir);
                            rowShift ??= (bb.L + bb.R) / 2.0 - rear;
                            fx = rear + rowShift.Value; fy = bb.B; break;
                        default: fx = (bb.L + bb.R) / 2.0; fy = bb.B; break;   // AI 시트는 칸 안 자리가 장마다 달라 칸 가운데를 기준으로 하면 흔들린다
                    }
                    ids.Add(frames.Count);
                    frames.Add((small.Crop(bb.L, bb.T, bb.R, bb.B), (int)Math.Round(bb.L - fx), (int)Math.Round(bb.T - fy) + FootBelow));
                }
                keyed.Add((part.Action, dir, ids, part));
            }
        }
        if (frames.Count == 0) throw new InvalidOperationException("시트에서 그림을 하나도 못 잘랐습니다 — 줄 · 칸 · 배경을 보세요.");

        var (palette, index) = Quantize(frames.Select(f => f.Piece).ToList());

        // 모션 — 키: (종류, 시작, 길이, 인자) — 종류 0 그림(벌 0, 장), 1 소리, 6 타격(어빌리티 25 접근 · 26 원거리).
        var motions = new List<(int Id, int Length, List<(int Kind, int Start, int Len, short[] Args)> Keys)>();
        foreach (var (action, dir, ids, part) in keyed)
        {
            if (ids.Count == 0) continue;
            if (action == 1 && !keyed.Any(k => k.Action == 0 && k.Dir == dir))
                motions.Add((dir, 1, [(0, 0, 1, [0, (short)ids[0]])]));
            int t = part.Ticks > 0 ? part.Ticks : action == 0 ? standTicks : walkTicks;
            var keys = ids.Select((f, k) => (0, k * t, t, new short[] { 0, (short)f })).ToList();
            foreach (int n in part.Hits ?? []) if (n >= 1 && n <= ids.Count) keys.Add((6, (n - 1) * t, 1, [(short)part.Ability]));
            foreach (var (sound, n) in part.Sounds ?? []) if (n >= 1 && n <= ids.Count) keys.Add((1, (n - 1) * t, 1, [(short)sound]));
            keys.Sort((a, b) => a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : a.Item1.CompareTo(b.Item1));
            int id = action * 3 + dir;
            motions.RemoveAll(m => m.Id == id);   // 같은 동작 · 방향을 또 주면 뒤의 것
            motions.Add((id, t * ids.Count, keys));
        }
        motions.Sort((a, b) => a.Id.CompareTo(b.Id));

        byte[] obs = WriteObs(frames.Select(f => (f.Piece, f.Ox, f.Oy)).ToList(), palette, index, motions);
        return new Result(obs, Preview(frames, keyed.Select(k => k.Ids).ToList()), frames.Count, [.. motions.Select(m => m.Id)], scales);
    }

    // ── 자르기 · 배경 ──────────────────────────────────────────────────────

    private static Rgba Cut(Part part, int x0, int y0, int x1, int y1)
    {
        var s = part.Sheet;
        // 시트 가장자리 2픽셀은 뺀다 — AI 시트는 끝 테두리에 바탕이 아닌 선이 남아 마지막 칸에 세로줄로 붙었다.
        x0 = Math.Max(x0, 2); y0 = Math.Max(y0, 2); x1 = Math.Min(x1, s.Width - 2); y1 = Math.Min(y1, s.Height - 2);
        int w = Math.Max(0, x1 - x0), h = Math.Max(0, y1 - y0);
        var cell = new Rgba(w, h);
        if (w == 0 || h == 0) return cell;
        var bg = new bool[w * h];
        int Diff(int a, int b) => Math.Max(Math.Abs(s.Bgra[a] - s.Bgra[b]), Math.Max(Math.Abs(s.Bgra[a + 1] - s.Bgra[b + 1]), Math.Abs(s.Bgra[a + 2] - s.Bgra[b + 2])));
        int At(int x, int y) => ((y0 + y) * s.Width + x0 + x) * 4;
        if (part.Key is { } key)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int o = At(x, y);
                    bg[y * w + x] = Math.Max(Math.Abs(s.Bgra[o + 2] - key.R), Math.Max(Math.Abs(s.Bgra[o + 1] - key.G), Math.Abs(s.Bgra[o] - key.B))) <= part.KeyTol;
                }
        }
        else
        {
            // 칸 가장자리에서 번지며 이웃과 색 차가 Flood 이하인 곳을 바탕으로(칠해진 그라데이션 · 빛 번짐 바탕).
            var q = new Queue<(int, int)>();
            void Seed(int x, int y) { if (!bg[y * w + x]) { bg[y * w + x] = true; q.Enqueue((x, y)); } }
            for (int x = 0; x < w; x++) { Seed(x, 0); Seed(x, h - 1); }
            for (int y = 0; y < h; y++) { Seed(0, y); Seed(w - 1, y); }
            while (q.Count > 0)
            {
                var (x, y) = q.Dequeue();
                foreach (var (nx, ny) in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h && !bg[ny * w + nx] && Diff(At(x, y), At(nx, ny)) <= part.Flood)
                    { bg[ny * w + nx] = true; q.Enqueue((nx, ny)); }
            }
        }
        // 앞그림 덩어리(8방향) 가운데 가장 큰 것의 5% 이상만 — 칸 귀퉁이 부스러기를 버린다(떨어진 팔 · 무기는 남긴다).
        var label = new int[w * h];
        var sizes = new List<int> { 0 };
        for (int i = 0; i < w * h; i++)
        {
            if (bg[i] || label[i] != 0) continue;
            int id = sizes.Count, n = 0;
            var stack = new Stack<int>();
            stack.Push(i); label[i] = id;
            while (stack.Count > 0)
            {
                int p = stack.Pop(); n++;
                int px = p % w, py = p / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = px + dx, ny = py + dy;
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        int ni = ny * w + nx;
                        if (!bg[ni] && label[ni] == 0) { label[ni] = id; stack.Push(ni); }
                    }
            }
            sizes.Add(n);
        }
        int big = sizes.Max();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (label[i] == 0 || sizes[label[i]] * 20 < big) continue;
                int o = At(x, y), d = i * 4;
                cell.Px[d] = s.Bgra[o]; cell.Px[d + 1] = s.Bgra[o + 1]; cell.Px[d + 2] = s.Bgra[o + 2]; cell.Px[d + 3] = 255;
            }
        return cell;
    }

    /// <summary>발끝 띠(그림 아래 6%, 적어도 3줄)의 불투명 픽셀 x — 옆모습은 오른쪽 끝(왼쪽을 보는 그림의 뒷발), 그 밖은 가운데.</summary>
    private static double RearFoot(Rgba img, (int L, int T, int R, int B) bb, int dir)
    {
        int band = Math.Max(3, (bb.B - bb.T) * 6 / 100), lo = int.MaxValue, hi = -1;
        for (int y = bb.B - band; y < bb.B; y++)
            for (int x = bb.L; x < bb.R; x++)
                if (img.Opaque(x, y)) { lo = Math.Min(lo, x); hi = Math.Max(hi, x); }
        if (hi < 0) return (bb.L + bb.R) / 2.0;
        return dir == (int)Direction.Side ? hi : (lo + hi) / 2.0;
    }

    /// <summary>줄이기 — 칸 넓이 평균(알파를 곱해 섞는다), 알파 128 이상만 남긴다(테두리를 딱 끊는다).</summary>
    private static Rgba Resize(Rgba src, int w, int h)
    {
        var dst = new Rgba(w, h);
        double sx = src.W / (double)w, sy = src.H / (double)h;
        for (int y = 0; y < h; y++)
        {
            int y0 = (int)(y * sy), y1 = Math.Max(y0 + 1, (int)Math.Ceiling((y + 1) * sy));
            for (int x = 0; x < w; x++)
            {
                int x0 = (int)(x * sx), x1 = Math.Max(x0 + 1, (int)Math.Ceiling((x + 1) * sx));
                double b = 0, g = 0, r = 0, a = 0; int n = 0;
                for (int yy = y0; yy < Math.Min(y1, src.H); yy++)
                    for (int xx = x0; xx < Math.Min(x1, src.W); xx++)
                    {
                        int o = (yy * src.W + xx) * 4;
                        double al = src.Px[o + 3] / 255.0;
                        b += src.Px[o] * al; g += src.Px[o + 1] * al; r += src.Px[o + 2] * al; a += al; n++;
                    }
                if (n == 0 || a / n < 0.5) continue;
                int d = (y * w + x) * 4;
                dst.Px[d] = (byte)Math.Round(b / a); dst.Px[d + 1] = (byte)Math.Round(g / a); dst.Px[d + 2] = (byte)Math.Round(r / a); dst.Px[d + 3] = 255;
            }
        }
        return dst;
    }

    // ── 팔레트 ─────────────────────────────────────────────────────────────

    /// <summary>모든 장의 색을 중간값 나누기(median cut)로 255색에 — 팔레트 0 은 투명(핑크 ff00ff 로 적는다). (팔레트 768, 색 → 번호)</summary>
    private static (byte[] Palette, Func<int, byte> Index) Quantize(List<Rgba> pieces)
    {
        var counts = new Dictionary<int, int>();
        foreach (var p in pieces)
            for (int i = 0; i < p.W * p.H; i++)
                if (p.Px[i * 4 + 3] != 0)
                {
                    int c = p.Px[i * 4 + 2] << 16 | p.Px[i * 4 + 1] << 8 | p.Px[i * 4];
                    counts[c] = counts.GetValueOrDefault(c) + 1;
                }
        var boxes = new List<List<KeyValuePair<int, int>>> { counts.ToList() };
        static int Ch(int c, int k) => c >> (16 - 8 * k) & 0xff;   // k 0 R · 1 G · 2 B
        while (boxes.Count < 255)
        {
            // 가장 넓은 칸(색 범위 × 픽셀 수가 큰 것)을 그 넓은 채널의 가운데(픽셀 수로)에서 나눈다.
            int best = -1, bestK = 0; long bestScore = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                for (int k = 0; k < 3; k++)
                {
                    int lo = boxes[i].Min(e => Ch(e.Key, k)), hi = boxes[i].Max(e => Ch(e.Key, k));
                    long score = (long)(hi - lo) * boxes[i].Sum(e => (long)e.Value);
                    if (score > bestScore) (best, bestK, bestScore) = (i, k, score);
                }
            }
            if (best < 0) break;
            var box = boxes[best].OrderBy(e => Ch(e.Key, bestK)).ToList();
            long half = box.Sum(e => (long)e.Value) / 2, run = 0;
            int cut = 1;
            for (int i = 0; i < box.Count - 1; i++) { run += box[i].Value; if (run >= half) { cut = i + 1; break; } }
            boxes[best] = box[..cut];
            boxes.Add(box[cut..]);
        }
        var palette = new byte[768];
        (palette[0], palette[1], palette[2]) = (255, 0, 255);
        var colors = new List<(int R, int G, int B)>();
        foreach (var box in boxes)
        {
            long n = box.Sum(e => (long)e.Value);
            if (n == 0) continue;
            colors.Add(((int)(box.Sum(e => (long)Ch(e.Key, 0) * e.Value) / n), (int)(box.Sum(e => (long)Ch(e.Key, 1) * e.Value) / n), (int)(box.Sum(e => (long)Ch(e.Key, 2) * e.Value) / n)));
        }
        for (int i = 0; i < colors.Count; i++) (palette[(i + 1) * 3], palette[(i + 1) * 3 + 1], palette[(i + 1) * 3 + 2]) = ((byte)colors[i].R, (byte)colors[i].G, (byte)colors[i].B);
        var cache = new Dictionary<int, byte>();
        byte Index(int c)
        {
            if (cache.TryGetValue(c, out byte got)) return got;
            int r = Ch(c, 0), g = Ch(c, 1), b = Ch(c, 2), bestI = 0, bestD = int.MaxValue;
            for (int i = 0; i < colors.Count; i++)
            {
                int dr = colors[i].R - r, dg = colors[i].G - g, db = colors[i].B - b, d = dr * dr + dg * dg + db * db;
                if (d < bestD) (bestD, bestI) = (d, i);
            }
            return cache[c] = (byte)(bestI + 1);
        }
        return (palette, Index);
    }

    // ── Obs 쓰기 ───────────────────────────────────────────────────────────

    private static byte[] WriteObs(List<(Rgba Piece, int Ox, int Oy)> frames, byte[] palette, Func<int, byte> index,
                                   List<(int Id, int Length, List<(int Kind, int Start, int Len, short[] Args)> Keys)> motions)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)0);
        long subTable = ms.Position;
        w.Write((ushort)0); w.Write(0u);
        w.Write((ushort)motions.Count); w.Write((ushort)motions.Max(m => m.Id));
        long motionTable = ms.Position;
        foreach (var m in motions) { w.Write((ushort)m.Id); w.Write(0u); }
        var motionAt = new long[motions.Count];
        for (int i = 0; i < motions.Count; i++)
        {
            motionAt[i] = ms.Position;
            var (id, length, keys) = motions[i];
            w.Write((ushort)id); w.Write((ushort)0); w.Write((ushort)keys.Count); w.Write((ushort)length); w.Write((ushort)0);
            foreach (var (kind, start, len, args) in keys)
            {
                w.Write((ushort)kind); w.Write((ushort)start); w.Write((ushort)len);
                for (int a = 0; a < 10; a++) w.Write(a < args.Length ? args[a] : (short)0);
            }
        }
        long subAt = ms.Position;
        w.Write((ushort)0); w.Write((ushort)frames.Count); w.Write((ushort)(frames.Count - 1)); w.Write((byte)0); w.Write(palette);
        long slotTable = ms.Position;
        for (int f = 0; f < frames.Count; f++) { w.Write((ushort)f); w.Write(0u); }
        for (int f = 0; f < frames.Count; f++)
        {
            long at = ms.Position;
            var (piece, ox, oy) = frames[f];
            var px = new byte[piece.W * piece.H];
            for (int i = 0; i < px.Length; i++)
                px[i] = piece.Px[i * 4 + 3] == 0 ? (byte)0 : index(piece.Px[i * 4 + 2] << 16 | piece.Px[i * 4 + 1] << 8 | piece.Px[i * 4]);
            // 그림 자료: u16 레코드 수, 레코드마다 (u16 건너뜀 0, u8 넷 묶음 수, u8 낱 수, 색인) — 한 레코드에 1020 바이트까지.
            using var body = new MemoryStream();
            using (var bw = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                int records = (px.Length + 1019) / 1020;
                bw.Write((ushort)records);
                for (int i = 0; i < px.Length; i += 1020)
                {
                    int n = Math.Min(1020, px.Length - i);
                    bw.Write((ushort)0); bw.Write((byte)(n / 4)); bw.Write((byte)(n % 4)); bw.Write(px, i, n);
                }
            }
            w.Write((ushort)f); w.Write((uint)body.Length); w.Write((ushort)piece.W); w.Write((ushort)piece.H); w.Write((short)ox); w.Write((short)oy);
            w.Write(body.ToArray());
            long end = ms.Position;
            ms.Position = slotTable + 6 * f + 2; w.Write((uint)at); ms.Position = end;
        }
        ms.Position = subTable + 2; w.Write((uint)subAt);
        for (int i = 0; i < motions.Count; i++) { ms.Position = motionTable + 6 * i + 2; w.Write((uint)motionAt[i]); }
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>미리보기 — 줄마다 장을 늘어놓되 원점(발밑)을 칸마다 같은 자리에(게임에서처럼 흔들림이 보인다), 핑크 바탕.</summary>
    private static Image Preview(List<(Rgba Piece, int Ox, int Oy)> frames, List<List<int>> rows)
    {
        int left = -frames.Min(f => f.Ox) + 4, top = -frames.Min(f => f.Oy) + 4;
        int cw = frames.Max(f => left + f.Ox + f.Piece.W) + 4, ch = frames.Max(f => top + f.Oy + f.Piece.H) + 4;
        int cols = Math.Max(1, rows.Max(r => r.Count)), w = cw * cols, h = ch * Math.Max(1, rows.Count);
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4] = 255; px[i * 4 + 1] = 0; px[i * 4 + 2] = 255; px[i * 4 + 3] = 255; }
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < rows[r].Count; c++)
            {
                var (piece, ox, oy) = frames[rows[r][c]];
                int bx = c * cw + left + ox, by = r * ch + top + oy;
                for (int y = 0; y < piece.H; y++)
                    for (int x = 0; x < piece.W; x++)
                    {
                        int s = (y * piece.W + x) * 4;
                        if (piece.Px[s + 3] == 0) continue;
                        int d = ((by + y) * w + bx + x) * 4;
                        px[d] = piece.Px[s]; px[d + 1] = piece.Px[s + 1]; px[d + 2] = piece.Px[s + 2];
                    }
            }
        return new Image(w, h, px);
    }
}

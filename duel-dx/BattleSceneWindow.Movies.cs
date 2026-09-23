using System.Drawing;
using System.Drawing.Imaging;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 기술 영상 — 시전 불기둥(<c>Mov 0041</c>·<c>0042</c>)·리 바이블(<c>0034</c>)·어스퀘이크(<c>0040</c>)·강림의 밤(<c>0061</c>).
/// </summary>
/// <remarks>
/// 옵시디안 분석-스킬 「소리 껍데기만 남은 기술의 그림 (fx-189)」: 준비 동작 <c>+0x3f</c> 가 2·5 인 기술은 <c>Mov 0041</c>, 3·6·7 은 <c>0042</c> 를
/// 시전자에게 붙인다(<c>0x1007e01b</c> → <c>0x100d0770</c> → <c>0x100d08b0(0x11, 0, 0, dx, dy, 0)</c>). 자리는 붙은 유닛 발밑 + (dx, dy) 가
/// 영상 왼쪽 위이고, 섞기 0x11 은 더하기로 본다(가설 — 영상 바탕이 검정이라 더하기면 바탕이 사라진다).
/// 영상은 Bink 라 게임에서 바로 못 읽어 <c>assets/effects/mov/NNNN/NNN.png</c> 로 풀어 둔다(<c>tools/re/mov_frames.py</c> 와 같은 PyAV 풀이).
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>work 에 붙는 영상 하나 — 영상 번호, 준비 동작에서 띄우나(아니면 치는 순간), 대상에 붙나, 왼쪽 위까지의 거리.</summary>
    private readonly record struct MovieFx(int Movie, bool Prelude, bool OnTarget, int Dx, int Dy);

    /// <summary>재생 중인 영상 — (번호, 시작 시각, 왼쪽 위 x, y).</summary>
    private readonly List<(int Movie, double Start, int X, int Y)> _movies = [];

    /// <summary>풀어 둔 영상 컷(번호 → 컷들). 처음 쓸 때 읽는다. 없으면 빈 배열.</summary>
    private readonly Dictionary<int, SpriteFrame[]> _movieFrames = [];

    /// <summary>영상 초당 컷 — 0034·0061 은 15, 나머지는 30(PyAV 로 잰 값).</summary>
    private static double MovieFps(int movie) => movie is 34 or 61 ? 15 : 30;

    private SpriteFrame[] MovieFrames(int movie)
    {
        if (_movieFrames.TryGetValue(movie, out var cached)) return cached;
        var frames = new List<SpriteFrame>();
        try
        {
            string dir = Path.Combine(AssetsFolder.Find("effects"), "mov", $"{movie:D4}");
            foreach (string path in Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png").Order().ToArray() : [])
            {
                using var bitmap = new Bitmap(path);
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var px = new uint[bitmap.Width * bitmap.Height];
                    for (int y = 0; y < bitmap.Height; y++)
                        new Span<uint>((void*)(data.Scan0 + y * data.Stride), bitmap.Width).CopyTo(px.AsSpan(y * bitmap.Width, bitmap.Width));
                    frames.Add(new SpriteFrame(px, bitmap.Width, bitmap.Height, 0, 0));
                }
                finally { bitmap.UnlockBits(data); }
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or DirectoryNotFoundException) { }
        return _movieFrames[movie] = [.. frames];
    }

    /// <summary>그 work 의 영상을 띄운다 — <paramref name="prelude"/> 면 준비 동작(시전 시작)의 것, 아니면 치는 순간의 것.</summary>
    private void SpawnWorkMovies(WorkData w, UnitState user, int col, int row, bool prelude)
    {
        if (!WorkMovies.TryGetValue(w.Id, out var list)) return;
        foreach (var m in list)
        {
            if (m.Prelude != prelude) continue;
            var (x, y) = m.OnTarget ? (col * TileW + TileW / 2, CellCenterY(col, row)) : UnitFoot(user);
            _movies.Add((m.Movie, _lastTime, x + m.Dx, y + m.Dy));
        }
    }

    /// <summary>영상 컷을 더하기로 얹는다 — 검은 바탕은 +0 이라 안 보인다. 끝난 영상은 지운다.</summary>
    private void DrawMovies()
    {
        _movies.RemoveAll(m =>
        {
            var frames = MovieFrames(m.Movie);
            int index = (int)((_lastTime - m.Start) * MovieFps(m.Movie));
            if (index >= frames.Length) return true;
            var f = frames[index];
            for (int y = 0; y < f.H; y++)
            {
                int dy = m.Y + y;
                if ((uint)dy >= BoardHeight) continue;
                for (int x = 0; x < f.W; x++)
                {
                    int dx = m.X + x;
                    if ((uint)dx >= BoardWidth) continue;
                    uint c = f.Px[y * f.W + x];
                    if ((c & 0xFFFFFF) == 0) continue;
                    int at = dy * BoardWidth + dx;
                    _fb[at] = AddColor(_fb[at], c, 256);
                }
            }
            return false;
        });
    }
}

using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 모드 &gt; 실험 &gt; 고해상도 모세스 — 모세스 배경(640×480 JPEG)을 업스케일 모델로 네 배 키워 판 밑에 깐다(사용자 요청 menu-74).
/// </summary>
/// <remarks>
/// <para>모델은 Rebout Blend 4x(LyonHrt, CC BY-NC-SA 4.0, ESRGAN 64nf 23nb)를 ONNX 로 바꾼 것으로, 대항해시대(cds-remake)가 올려 둔 것을 받는다(64MB).
/// 키운 그림은 원본 그림에서 나온 것이라 배포하지 않고 <b>그 PC 에서</b> 만든다 — 세이브 폴더의 <c>hires</c> 에 PNG 로 쌓는다.</para>
/// <para>만들기는 뒤 스레드에서 그래픽 카드(DirectML)로 — RTX 3070 에서 한 장 6~9초. 안 되면 CPU(한 장 몇 분). 지금 보는 화면의 배경을 먼저 만든다.</para>
/// <para>그리기: 판에는 지금처럼 원본 배경을 찍고, 그 위의 단추 · 글 · 효과도 판에 그린다. 판을 화면에 올릴 때 원본 배경 그대로인 점만 투명하게 해
/// 밑에 깐 큰 그림이 비치게 한다(<see cref="HiCanvas.DrawUnder"/>) — 그래서 그리는 코드는 그대로 두고 배경만 또렷해진다.</para>
/// </remarks>
internal sealed class HiResBackdrop(GameWindow host)
{
    internal const string ModelFile = "4x-ReboutBlend.onnx";
    internal const string ModelUrl = "https://github.com/Kyeongrok/cds-remake/releases/download/model-assets/" + ModelFile;
    internal const string Credit = "Rebout Blend 4x — LyonHrt, CC BY-NC-SA 4.0";
    internal const int Scale = 4, Tile = 128, Pad = 12;

    /// <summary>모세스 화면이 쓰는 배경 — 챕터 · 항성계 배경과 메일 94 · 상점 40 · 군단 39 · 전직 42 · Status 58(원본 챕터 자료를 훑어 모은 16장).</summary>
    internal static readonly int[] MosesBackgrounds = [70, 52, 12, 13, 39, 40, 42, 58, 65, 66, 67, 68, 69, 71, 72, 94];

    internal static string Folder => UserDataFolder.File("hires");
    internal static string ModelPath => Path.Combine(Folder, "model", ModelFile);
    internal static string CachePath(int id) => Path.Combine(Folder, "moses-bgr", $"{id:D4}.png");

    internal bool Enabled => host._hiResMoses;

    /// <summary>모드 창에 보이는 지금 상태 — 「모델 받는 중 37%」 · 「만드는 중 7 / 16 (그래픽 카드)」 따위.</summary>
    internal volatile string _status = "";
    private volatile bool _running;
    private volatile int _wanted = -1;

    /// <summary>읽어 둔 큰 그림 — 화면에 쓰는 것 몇 장만(한 장 20MB).</summary>
    private readonly ConcurrentDictionary<int, (uint[] Px, int W, int H)> _loaded = new();
    private readonly ConcurrentDictionary<int, bool> _loading = new();

    internal int Made => MosesBackgrounds.Count(id => File.Exists(CachePath(id)));

    internal string StatusLine => !Enabled ? "모세스 배경을 4배로 키워 또렷하게 · 처음엔 모델(64MB)을 받고 이 PC 에서 만든다"
                                : _status.Length > 0 ? _status
                                : $"{Made} / {MosesBackgrounds.Length} 장 만들어 둠";

    /// <summary>모드를 켰을 때 — 다 만들어 두지 않았으면 뒤에서 만들기 시작한다.</summary>
    internal void Start()
    {
        if (!Enabled || _running || Made == MosesBackgrounds.Length) return;
        _running = true;
        Task.Run(Work);
    }

    /// <summary>모드를 껐을 때 — 메모리의 큰 그림만 버린다(만든 파일은 남긴다).</summary>
    internal void Drop() => _loaded.Clear();

    /// <summary>
    /// 모세스 화면이 배경을 판에 찍은 뒤 부른다 — 큰 그림이 있으면 판 밑에 깔고, 아직 없으면 만들기를 그 배경부터 하게 한다.
    /// </summary>
    internal void Mark(int id, uint[] low, int x, int y)
    {
        if (!Enabled || low.Length != MosesScene.MosesW * MosesScene.MosesH) return;
        if (_loaded.TryGetValue(id, out var hi)) { host.Hi.DrawUnder(hi.Px, hi.W, hi.H, low, x, y, MosesScene.MosesW, MosesScene.MosesH); return; }
        if (File.Exists(CachePath(id))) { LoadLater(id); return; }
        if (!MosesBackgrounds.Contains(id)) return;
        _wanted = id;
        Start();
    }

    /// <summary>PNG 를 뒤에서 읽어 둔다 — 한 장 100ms 남짓이라 그리는 틀을 멈추지 않게.</summary>
    private void LoadLater(int id)
    {
        if (!_loading.TryAdd(id, true)) return;
        Task.Run(() =>
        {
            try
            {
                if (ReadPng(CachePath(id)) is { } hi)
                {
                    // 화면에 쓰는 것만 — 지금 것과 바로 앞 것 두 장 남짓만 둔다.
                    foreach (int old in _loaded.Keys.Where(k => k != id).Skip(1).ToList()) _loaded.TryRemove(old, out _);
                    _loaded[id] = hi;
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException) { }
            finally { _loading.TryRemove(id, out _); }
        });
    }

    private static unsafe (uint[] Px, int W, int H)? ReadPng(string path)
    {
        using var bitmap = new Bitmap(path);
        int w = bitmap.Width, h = bitmap.Height;
        var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var px = new uint[w * h];
            new ReadOnlySpan<uint>((void*)data.Scan0, w * h).CopyTo(px);
            return (px, w, h);
        }
        finally { bitmap.UnlockBits(data); }
    }

    // ── 만들기 ────────────────────────────────────────────────────────────────

    private async Task Work()
    {
        try
        {
            if (!File.Exists(ModelPath) && !await DownloadModel()) return;
            using var session = OpenSession(out bool gpu, out string why);
            string input = session.InputMetadata.Keys.First();
            string device = gpu ? "그래픽 카드" : why.Length > 0 ? $"CPU — 느림 · 그래픽 카드 못 씀: {why}" : "CPU — 느림";
            while (Enabled)
            {
                int wanted = _wanted;
                int id = wanted >= 0 && !_missing.Contains(wanted) && !File.Exists(CachePath(wanted)) ? wanted
                       : MosesBackgrounds.FirstOrDefault(b => !_missing.Contains(b) && !File.Exists(CachePath(b)), -1);
                if (id < 0) break;
                if (id == wanted) _wanted = -1;
                _status = $"만드는 중 {Made + 1} / {MosesBackgrounds.Length} ({device})";
                if (ReadSource(id) is not { } src) { MarkMissing(id); continue; }
                var big = Upscale(session, input, src.Px, src.W, src.H);
                SavePng(CachePath(id), big, src.W * Scale, src.H * Scale);
            }
            _status = "";
            if (Made == MosesBackgrounds.Length) host.Toast($"고해상도 모세스 배경 {MosesBackgrounds.Length}장을 다 만들었습니다");
        }
        catch (Exception ex)   // 뒤 스레드라 무엇이 터져도 게임은 가야 한다 — 까닭은 모드 창에 보인다
        {
            _status = $"못 만들었습니다 — {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
        }
        finally { _running = false; }
    }

    private readonly HashSet<int> _missing = [];

    /// <summary>원본 배경을 못 읽은 번호 — 이번에는 다시 고르지 않는다.</summary>
    private void MarkMissing(int id)
    {
        _missing.Add(id);
        if (_wanted == id) _wanted = -1;
    }

    /// <summary>그래픽 카드(DirectML)로 열고, 안 되면 CPU 로.</summary>
    private static InferenceSession OpenSession(out bool gpu, out string why)
    {
        why = "";
        try
        {
            PreloadDirectML();
            var options = new SessionOptions();
            options.AppendExecutionProvider_DML(0);
            gpu = true;
            return new InferenceSession(ModelPath, options);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException or BadImageFormatException)
        {
            gpu = false;
            why = ex.Message.Split('\n')[0];
            return new InferenceSession(ModelPath);
        }
    }

    /// <summary>
    /// 꾸러미의 DirectML.dll 을 먼저 올려 둔다 — 단일 exe 로 배포하면 DLL 이 임시 폴더에 풀리는데, onnxruntime 이 이름으로 찾으면 exe 옆에는 없어
    /// Windows 에 딸린 옛 DirectML(System32)을 잡아 그래픽 카드를 못 쓰고 CPU 로 떨어졌다. 같은 이름이 이미 올라 있으면 그것을 쓴다.
    /// </summary>
    private static void PreloadDirectML()
    {
        foreach (string? dir in new[] { Path.GetDirectoryName(typeof(InferenceSession).Assembly.Location), AppContext.BaseDirectory })
            if (!string.IsNullOrEmpty(dir) && Path.Combine(dir, "DirectML.dll") is var path && File.Exists(path))
            {
                NativeLibrary.TryLoad(path, out _);
                return;
            }
    }

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>모델을 받는다 — 옆에 임시로 써 두고 다 받으면 제 이름으로 옮긴다.</summary>
    private async Task<bool> DownloadModel()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);
        string temp = ModelPath + ".part";
        _status = "모델 받는 중";
        using (var response = await Client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var from = await response.Content.ReadAsStreamAsync();
            await using var to = File.Create(temp);
            var buffer = new byte[1 << 16];
            long got = 0;
            int read;
            while ((read = await from.ReadAsync(buffer)) > 0)
            {
                if (!Enabled) return false;
                await to.WriteAsync(buffer.AsMemory(0, read));
                got += read;
                if (total > 0) _status = $"모델 받는 중 {got * 100 / total.Value}% ({total.Value >> 20}MB)";
            }
        }
        File.Move(temp, ModelPath, overwrite: true);
        return true;
    }

    /// <summary>원본 배경(.bgr = JPEG)을 통째로 읽는다.</summary>
    private static unsafe (uint[] Px, int W, int H)? ReadSource(int id)
    {
        if (AssetPack.Fetch("moses/bgr", $"{id:D4}.bgr", 60) is not { } path) return null;
        using var bitmap = new Bitmap(path);
        int w = bitmap.Width, h = bitmap.Height;
        var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var px = new uint[w * h];
            new ReadOnlySpan<uint>((void*)data.Scan0, w * h).CopyTo(px);
            return (px, w, h);
        }
        finally { bitmap.UnlockBits(data); }
    }

    /// <summary>
    /// 그림을 128 조각으로 나눠 키운다 — 조각마다 둘레 12점을 더 넣고 가운데만 써서 이음매가 안 남게(통째로 넣으면 그래픽 카드 메모리가 모자라기 쉽다).
    /// </summary>
    internal static uint[] Upscale(InferenceSession session, string input, uint[] px, int w, int h)
    {
        int bw = w * Scale;
        var outPx = new uint[bw * h * Scale];
        for (int ty = 0; ty < h; ty += Tile)
            for (int tx = 0; tx < w; tx += Tile)
            {
                int x0 = Math.Max(0, tx - Pad), y0 = Math.Max(0, ty - Pad);
                int x1 = Math.Min(w, tx + Tile + Pad), y1 = Math.Min(h, ty + Tile + Pad);
                int tw = x1 - x0, th = y1 - y0;
                var t = new DenseTensor<float>([1, 3, th, tw]);
                for (int y = 0; y < th; y++)
                    for (int x = 0; x < tw; x++)
                    {
                        uint c = px[(y0 + y) * w + x0 + x];
                        t[0, 0, y, x] = (c >> 16 & 255) / 255f;
                        t[0, 1, y, x] = (c >> 8 & 255) / 255f;
                        t[0, 2, y, x] = (c & 255) / 255f;
                    }
                using var results = session.Run([NamedOnnxValue.CreateFromTensor(input, t)]);
                var o = results.First().AsTensor<float>();
                int cx0 = (tx - x0) * Scale, cy0 = (ty - y0) * Scale;
                int cw = Math.Min(Tile, w - tx) * Scale, ch = Math.Min(Tile, h - ty) * Scale;
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        uint B(int k) => (uint)Math.Clamp((int)MathF.Round(o[0, k, cy0 + y, cx0 + x] * 255f), 0, 255);
                        outPx[(ty * Scale + y) * bw + tx * Scale + x] = 0xFF000000 | B(0) << 16 | B(1) << 8 | B(2);
                    }
            }
        return outPx;
    }

    private static unsafe void SavePng(string path, uint[] px, int w, int h)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { px.AsSpan().CopyTo(new Span<uint>((void*)data.Scan0, w * h)); }
        finally { bitmap.UnlockBits(data); }
        string temp = path + ".tmp";
        bitmap.Save(temp, ImageFormat.Png);
        File.Move(temp, path, overwrite: true);
    }
}

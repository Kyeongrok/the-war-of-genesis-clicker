using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 음악(<c>assets/bgm</c>)과 전투 맵(<c>assets/maps</c>) — 설치 꾸러미에는 타이틀·연대표 음악만 싣고, 나머지는 GitHub 릴리즈 <c>assets-pack-1</c> 에
/// <b>파일 하나씩</b> 올려 두었다가 게임을 켰을 때 뒤에서 받아 사용자 폴더(<c>%APPDATA%\DuelDx\pack</c>)에 둔다(사용자 요청 — 설치판이 651MB 였다).
/// </summary>
/// <remarks>
/// 받는 차례는 목록(<c>pack.json</c>)에 적힌 차례 — 코어헌터 1챕터, 베라모드 1챕터, 그 뒤 챕터 순이다(<c>tools/make_asset_pack.py</c>).
/// 게임이 아직 없는 파일을 찾으면(<see cref="Want"/>) 그것을 맨 앞으로 당긴다. 맵은 없으면 전투를 못 여니 받을 때까지 기다린다(<see cref="EnsureNow"/>),
/// 음악은 받아지면 그때 튼다. 저장소에서 바로 돌릴 때(개발)는 <c>assets</c> 에 다 있어서 아무것도 받지 않는다.
/// 화면 밖 시험(<c>DUELDX_OFFSCREEN=1</c>)에서는 받지 않는다 — <c>DUELDX_PACK=1</c> 이면 시험 중에도 받고, <c>0</c> 이면 끈다.
/// </remarks>
internal static class AssetPack
{
    private const string BaseUrl = "https://github.com/Kyeongrok/the-war-of-genesis-clicker/releases/download/assets-pack-1/";
    private const string ManifestName = "pack.json";

    /// <summary>받은 파일이 놓이는 폴더 — 그 아래 <c>bgm</c> · <c>maps</c>.</summary>
    public static string Folder { get; } = Path.Combine(UserDataFolder.Path, "pack");

    /// <summary>화면에 보일 진행 글 — 받을 것이 없거나 다 받았으면 빈 글.</summary>
    public static string Status { get; private set; } = "";

    private sealed record Entry(string Name, string Asset, long Size, string Sha256);

    private static readonly object Gate = new();
    private static List<Entry> _todo = [];
    private static readonly List<string> _wanted = [];
    private static bool _running, _ready;

    /// <summary>받을 목록에 아직 남아 있나 — 목록을 못 읽은 동안에는 모른다(false).</summary>
    private static bool Pending(string name) { lock (Gate) return _current == name || _todo.Any(e => e.Name == name); }

    /// <summary>지금 받고 있는 파일 — 목록에서는 이미 뺐지만 아직 다 안 온 것.</summary>
    private static string _current = "";

    /// <summary>그 파일의 자리 — 게임에 실린 것(<c>assets/…</c>)이 먼저고, 없으면 받은 폴더. 둘 다 없으면 받은 폴더 쪽 경로(없는 파일)를 돌려주고 먼저 받게 당긴다.</summary>
    public static string PathOf(string kind, string file)
    {
        string shipped = Path.Combine(AssetsFolder.Find(kind), file);
        if (File.Exists(shipped)) return shipped;
        string got = Path.Combine(Folder, kind, file);
        if (!File.Exists(got)) Want($"{kind}/{file}");
        return got;
    }

    /// <summary>이 파일을 먼저 받는다(이름은 <c>bgm/0043.bgm</c> 꼴).</summary>
    public static void Want(string name)
    {
        lock (Gate)
        {
            _wanted.Remove(name);
            _wanted.Add(name);
        }
    }

    /// <summary>그 파일이 올 때까지 기다린다 — 맵처럼 없으면 못 나아가는 것. 받는 중이 아니거나 시간이 다 되면 false.</summary>
    public static bool EnsureNow(string kind, string file, int seconds = 120)
    {
        string path = PathOf(kind, file);
        if (File.Exists(path)) return true;
        EnsureInBackground();
        for (var until = DateTime.UtcNow.AddSeconds(seconds); DateTime.UtcNow < until;)
        {
            if (File.Exists(path)) return true;
            lock (Gate) if (!_running) return File.Exists(path);
            // 목록에 없는 이름(대사 음성 번호 따위)은 기다려도 안 온다.
            if (_ready && !Pending($"{kind}/{file}")) return File.Exists(path);
            Thread.Sleep(100);
        }
        return File.Exists(path);
    }

    /// <summary>전투 맵 파일 자리 — 없으면 받을 때까지(최대 2분) 기다린다. 그래도 없으면 없는 경로를 돌려준다(부르는 쪽이 「못 읽음」으로 다룬다).</summary>
    public static string MapPath(string file)
    {
        EnsureNow("maps", file);
        return PathOf("maps", file);
    }

    public static void EnsureInBackground()
    {
        string? flag = Environment.GetEnvironmentVariable("DUELDX_PACK");
        if (flag == "0") return;
        if (flag != "1" && Environment.GetEnvironmentVariable("DUELDX_OFFSCREEN") == "1") return;
        lock (Gate)
        {
            if (_running) return;
            _running = true;
        }
        _ = Task.Run(RunAsync);
    }

    private static string LocalPath(Entry e) => Path.Combine(Folder, e.Name.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>이미 있나 — 게임에 실렸거나, 받은 것의 크기가 목록과 같다.</summary>
    private static bool Have(Entry e)
    {
        int slash = e.Name.IndexOf('/');
        if (slash > 0 && File.Exists(Path.Combine(AssetsFolder.Find(e.Name[..slash]), e.Name[(slash + 1)..]))) return true;
        var info = new FileInfo(LocalPath(e));
        return info.Exists && info.Length == e.Size;
    }

    private static async Task RunAsync()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WarOfGenesis-DuelDx");

            // 목록 — 한 번 받은 것을 곁에 두고, 다 받았으면 다음부터는 인터넷을 안 건드린다.
            string manifestPath = Path.Combine(Folder, ManifestName);
            List<Entry>? entries = File.Exists(manifestPath) ? Entries(File.ReadAllText(manifestPath)) : null;
            if (entries is not { Count: > 0 } || !entries.All(Have))
            {
                string json = await http.GetStringAsync(BaseUrl + ManifestName);
                if (Entries(json) is not { Count: > 0 } fresh) return;
                File.WriteAllText(manifestPath, json);
                entries = fresh;
            }
            lock (Gate) { _todo = [.. entries.Where(e => !Have(e))]; _ready = true; }
            long total = _todo.Sum(e => e.Size), got = 0;

            while (true)
            {
                Entry? next;
                lock (Gate)
                {
                    // 게임이 찾은 것이 있으면 그것부터(가장 최근에 찾은 것 먼저), 없으면 목록 차례대로.
                    next = null;
                    for (int i = _wanted.Count - 1; i >= 0 && next == null; i--)
                    {
                        next = _todo.FirstOrDefault(e => e.Name == _wanted[i]);
                        if (next == null) _wanted.RemoveAt(i);
                    }
                    next ??= _todo.FirstOrDefault();
                    if (next == null) break;
                    _todo.Remove(next);
                    _current = next.Name;
                }
                if (Have(next) || next.Name.Contains("..") || Path.IsPathRooted(next.Name) || next.Asset != Path.GetFileName(next.Asset)) continue;

                string to = LocalPath(next), part = to + ".part";
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                using (var response = await http.GetAsync(BaseUrl + next.Asset, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    await using var from = await response.Content.ReadAsStreamAsync();
                    await using var file = File.Create(part);
                    var buffer = new byte[1 << 16];
                    while (true)
                    {
                        // 본문이 멈추면 60초 뒤 그만둔다 — 전체 시간 제한은 머리글까지만 걸린다.
                        using var stall = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                        int n = await from.ReadAsync(buffer, stall.Token);
                        if (n <= 0) break;
                        await file.WriteAsync(buffer.AsMemory(0, n));
                        got += n;
                        Status = $"음악·맵 받는 중 {got * 100 / Math.Max(1, total)}% ({got >> 20}/{total >> 20}MB)";
                    }
                }
                // 깨진 파일은 두지 않는다 — 지우고 다음에 켤 때 다시 받는다.
                bool good;
                await using (var check = File.OpenRead(part))
                    good = Convert.ToHexString(await SHA256.HashDataAsync(check)).Equals(next.Sha256, StringComparison.OrdinalIgnoreCase);
                if (!good) { File.Delete(part); lock (Gate) _current = ""; continue; }
                File.Move(part, to, overwrite: true);
                lock (Gate) _current = "";
            }
            Status = "";
        }
        catch (Exception ex)
        {
            // 인터넷 없음·디스크 꽉 참·GitHub 막힘 — 조용히 넘어가고, 다음에 찾을 때(또는 다음에 켤 때) 다시 받는다.
            System.Diagnostics.Debug.WriteLine($"[Pack] {ex.GetType().Name}: {ex.Message}");
            Status = "";
        }
        finally
        {
            lock (Gate) { _running = false; _ready = false; _current = ""; }
        }
    }

    private static List<Entry>? Entries(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return [.. doc.RootElement.GetProperty("files").EnumerateArray().Select(p => new Entry(
                p.GetProperty("name").GetString() ?? "", p.GetProperty("asset").GetString() ?? "",
                p.GetProperty("size").GetInt64(), p.GetProperty("sha256").GetString() ?? ""))];
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }
}

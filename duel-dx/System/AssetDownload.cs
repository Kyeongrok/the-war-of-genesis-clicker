using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 원본 게임 폴더를 「모른다」고 한 사용자의 자료 — GitHub 릴리즈에서 받아 <c>%APPDATA%\DuelDx\assets</c> 에 저장소의 <c>assets</c> 와
/// 같은 꼴로 차린다(사용자 요청 menu-22). 꾸러미는 <c>tools/make_asset_pack.py</c> 가 만든다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>바탕</b>(<c>base-NN.zip</c>) — 켤 때 폴더를 훑어 읽는 것들. 없으면 게임이 못 켜지므로 처음 한 번 기다려 받는다(<see cref="PrepareBase"/>).</item>
/// <item><b>낱장</b> — 음악 · 전투 맵 · 인물 그림 · 모세스 그림 · 큰 효과음. 뒤에서 하나씩 받고, 게임이 없는 파일을 찾으면(<see cref="Fetch"/>) 그것부터 받는다.</item>
/// <item><b>대사 음성</b>(<c>voices-tNN-NN.zip</c>) — 장마다 한 덩이. 낱장을 다 받은 뒤에 받는다(없으면 목소리만 안 난다).</item>
/// </list>
/// 낱장과 음성에는 <b>장</b>(연대표의 줄 0~14)이 적혀 있고, 뒤에서 받는 것은 <b>지금까지 들어가 본 줄 + <see cref="Ahead"/></b> 까지다 —
/// 끝까지 하지 않을 사람이 다 받지 않게. 그 너머의 파일도 게임이 찾으면 그때 받는다.
/// 인터넷이 없거나 GitHub 이 막히면 조용히 쉬었다가 다시 해 본다. 화면 밖 시험에서는 <c>DUELDX_PACK=1</c> 일 때만 이 길로 온다.
/// </remarks>
internal static class AssetDownload
{
    /// <summary>릴리즈 내려받기 주소 — <c>DUELDX_PACKURL</c> 로 바꿀 수 있다(시험이 제 서버에서 받게).</summary>
    private static readonly string BaseUrl = Environment.GetEnvironmentVariable("DUELDX_PACKURL") is { Length: > 0 } url
        ? url.TrimEnd('/') + "/" : "https://github.com/Kyeongrok/the-war-of-genesis-clicker/releases/download/";
    private const string BaseRelease = "assets-base-1";
    private const string ManifestName = "pack.json";

    /// <summary>들어가 본 줄에서 몇 줄 앞까지 미리 받나.</summary>
    public const int Ahead = 3;

    /// <summary>이 장 번호부터는 미리 받지 않는다 — 게임이 찾을 때만 받는 것(기술 컷신 영상, <see cref="FetchExtra"/>).</summary>
    public const int OnDemandTier = 90;

    private static string Folder => OriginalAssets.LocalFolder;
    private static string PartsFolder => Path.Combine(Folder, ".parts");
    private static readonly string ChoicePath = UserDataFolder.File("assetsource.txt");
    private static readonly string TierPath = UserDataFolder.File("packtier.txt");

    /// <summary>사용자가 전에 「모른다 — 내려받기」를 골랐나.</summary>
    public static bool Chosen
    {
        get
        {
            try { return File.Exists(ChoicePath) && File.ReadAllText(ChoicePath).Trim() == "download"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    public static void Choose()
    {
        Directory.CreateDirectory(UserDataFolder.Path);
        File.WriteAllText(ChoicePath, "download");
    }

    /// <summary>이번 판이 받은 자료로 도나 — 바탕을 차린 뒤부터.</summary>
    public static bool Active { get; private set; }

    /// <summary>화면에 보일 진행 글 — 받을 것이 없으면 빈 글.</summary>
    public static string Status { get; private set; } = "";

    private sealed record Part(string Asset, long Size, string Sha256, int Tier);
    private sealed record Entry(string Name, string Asset, long Size, string Sha256, int Tier, string Release);

    private static readonly object Gate = new();
    private static readonly HttpClient Http = CreateClient();
    /// <summary>아직 못 받은 낱장 — 받는 차례대로.</summary>
    private static readonly List<Entry> _pending = [];
    private static readonly Dictionary<string, Entry> _byName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Part> _voices = [];
    private static readonly List<string> _wanted = [];
    private static int _reached;
    private static DateTime _retryAt = DateTime.MinValue;

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WarOfGenesis-DuelDx");
        return http;
    }

    // ── 켤 때 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 바탕을 차리고 뒤에서 받기를 건다 — 이미 받았으면 인터넷 없이 금방 끝난다. 못 받으면 예외(<see cref="HttpRequestException"/> · <see cref="IOException"/>).
    /// </summary>
    /// <param name="progress">(받은 바이트, 받을 바이트).</param>
    public static void PrepareBase(Action<long, long>? progress = null)
    {
        string shipped = AssetsFolder.Shipped() ?? throw new DirectoryNotFoundException("assets 폴더(assets/characters)를 못 찾았습니다.");
        OriginalAssets.LayOutShipped(shipped);
        Directory.CreateDirectory(PartsFolder);

        using var doc = JsonDocument.Parse(Manifest().GetAwaiter().GetResult());
        var bases = Parts(doc.RootElement, "base");
        var todo = bases.Where(p => !Done(p)).ToList();
        long total = todo.Sum(p => p.Size), got = 0;
        foreach (var part in todo)
        {
            long before = got;
            GetPart(BaseRelease, part, null, n => progress?.Invoke(got = before + n, total)).GetAwaiter().GetResult();
            got = before + part.Size;
        }

        lock (Gate)
        {
            _pending.Clear();
            _byName.Clear();
            foreach (var e in Entries(doc.RootElement))
            {
                if (e.Name.Contains("..") || Path.IsPathRooted(e.Name) || e.Asset != Path.GetFileName(e.Asset) || Have(e)) continue;
                _pending.Add(e);
                _byName[e.Name] = e;
            }
            _voices.Clear();
            _voices.AddRange(Parts(doc.RootElement, "voices").Where(p => !Done(p)));
            try { _reached = File.Exists(TierPath) && int.TryParse(File.ReadAllText(TierPath), out int saved) ? saved : 0; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _reached = 0; }
        }
        AssetsFolder.Use(Folder);
        Active = true;
        _ = Task.Run(RunAsync);
    }

    /// <summary>목록(<c>pack.json</c>) — 새 것을 받아 보고, 못 받으면 전에 받아 둔 것으로 간다. 둘 다 없으면 <see cref="HttpRequestException"/>.</summary>
    private static async Task<string> Manifest()
    {
        Directory.CreateDirectory(PartsFolder);
        string manifestPath = Path.Combine(PartsFolder, ManifestName);
        try
        {
            // 인터넷이 먹통이면 오래 붙잡지 않는다 — 받아 둔 목록이 있으면 그것으로 바로 간다.
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            string json = await Http.GetStringAsync($"{BaseUrl}{BaseRelease}/{ManifestName}", patience.Token);
            File.WriteAllText(manifestPath, json);
            return json;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (!File.Exists(manifestPath)) throw new HttpRequestException("자료 목록을 받지 못했습니다 — 인터넷 연결을 확인해 주세요.", ex);
            return File.ReadAllText(manifestPath);
        }
    }

    private static int _extrasStarted;

    /// <summary>
    /// 원본 게임 폴더에서 만들 수 없는 파일(기술 컷신 영상) — 있으면 그 자리, 없으면 뒤에서 받게 걸어 두고 null(기다리지 않는다).
    /// 원본 폴더로 도는 판도 이것만은 받는다. 저장소에서 돌릴 때(개발)와 화면 밖 시험(<c>DUELDX_PACK=1</c> 이나 <c>DUELDX_PACKURL</c> 이 없으면)에서는 받지 않는다.
    /// </summary>
    public static string? FetchExtra(string kind, string file)
    {
        if (Active) return Fetch(kind, file, 0);
        string path = Path.Combine(Folder, kind.Replace('/', Path.DirectorySeparatorChar), file), name = $"{kind}/{file}";
        if (File.Exists(path)) return path;
        if (!OriginalAssets.NeedsGame) return null;
        if (Environment.GetEnvironmentVariable("DUELDX_OFFSCREEN") == "1" && Environment.GetEnvironmentVariable("DUELDX_PACK") != "1"
            && Environment.GetEnvironmentVariable("DUELDX_PACKURL") is not { Length: > 0 }) return null;
        lock (Gate)
        {
            _wanted.Remove(name);
            _wanted.Add(name);
        }
        if (Interlocked.Exchange(ref _extrasStarted, 1) == 0) _ = Task.Run(RunExtrasAsync);
        return null;
    }

    /// <summary>원본 폴더로 도는 판의 받기 — 목록에서 「찾을 때만 받는 것」만 걸어 두고 같은 고리를 돌린다(찾은 것만 받는다).</summary>
    private static async Task RunExtrasAsync()
    {
        try
        {
            using var doc = JsonDocument.Parse(await Manifest());
            lock (Gate)
                foreach (var e in Entries(doc.RootElement))
                {
                    if (e.Tier < OnDemandTier || e.Name.Contains("..") || Path.IsPathRooted(e.Name) || e.Asset != Path.GetFileName(e.Asset) || Have(e)) continue;
                    _pending.Add(e);
                    _byName[e.Name] = e;
                }
            await RunAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException)
        {
            // 목록을 못 받았다 — 다음에 찾을 때 다시 해 본다.
            System.Diagnostics.Debug.WriteLine($"[Pack] {ex.GetType().Name}: {ex.Message}");
            Volatile.Write(ref _extrasStarted, 0);
        }
    }

    /// <summary>연대표의 그 줄(0~14)에 들어갔다 — 뒤에서 받는 범위가 그 줄 + <see cref="Ahead"/> 까지로 넓어진다.</summary>
    public static void Reach(int row)
    {
        if (!Active) return;
        lock (Gate)
        {
            if (row <= _reached) return;
            _reached = row;
        }
        try { File.WriteAllText(TierPath, row.ToString()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 못 적어도 이번 판은 받는다 */ }
    }

    // ── 게임이 찾을 때 ────────────────────────────────────────────────────────

    /// <summary>그 파일(<c>bgm/0043.bgm</c> 꼴)이 앞으로 올 수 있나 — 목록에 있고 아직 못 받았다.</summary>
    public static bool MayCome(string name)
    {
        lock (Gate) return Active && _byName.ContainsKey(name);
    }

    /// <summary>이 파일을 먼저 받는다.</summary>
    public static void Want(string name)
    {
        lock (Gate)
        {
            if (!_byName.ContainsKey(name)) return;
            _wanted.Remove(name);
            _wanted.Add(name);
        }
    }

    /// <summary>
    /// 그 파일의 자리 — 아직 없으면 먼저 받게 당기고 올 때까지(최대 <paramref name="seconds"/>초) 기다린다. 끝내 없으면 null.
    /// 받기가 막혀 쉬는 중이면(인터넷 없음) 기다리지 않는다.
    /// </summary>
    public static string? Fetch(string kind, string file, int seconds)
    {
        string path = Path.Combine(Folder, kind.Replace('/', Path.DirectorySeparatorChar), file), name = $"{kind}/{file}";
        if (File.Exists(path)) return path;
        Want(name);
        for (var until = DateTime.UtcNow.AddSeconds(seconds); DateTime.UtcNow < until; Thread.Sleep(50))
        {
            lock (Gate)
                if (!_byName.ContainsKey(name) || DateTime.UtcNow < _retryAt) break;
        }
        return File.Exists(path) ? path : null;
    }

    // ── 뒤에서 받기 ──────────────────────────────────────────────────────────

    private static string LocalPath(Entry e) => Path.Combine(Folder, e.Name.Replace('/', Path.DirectorySeparatorChar));

    private static bool Have(Entry e) => new FileInfo(LocalPath(e)) is { Exists: true } info && info.Length == e.Size;

    private static string Marker(Part part) => Path.Combine(PartsFolder, part.Asset + ".done");

    /// <summary>그 덩이를 다 풀었나 — 표식에 적힌 해시가 목록과 같아야 한다(덩이를 새로 올리면 다시 받는다).</summary>
    private static bool Done(Part part) =>
        File.Exists(Marker(part)) && File.ReadAllText(Marker(part)).Trim().Equals(part.Sha256, StringComparison.OrdinalIgnoreCase);

    private static async Task RunAsync()
    {
        long got = 0;
        while (true)
        {
            Entry? file = null;
            Part? voices = null;
            long left;
            lock (Gate)
            {
                int limit = _reached + Ahead;
                // 게임이 찾은 것이 있으면 그것부터(가장 최근에 찾은 것 먼저), 없으면 범위 안의 것을 차례대로.
                for (int i = _wanted.Count - 1; i >= 0 && file == null; i--)
                    if (!_byName.TryGetValue(_wanted[i], out file)) _wanted.RemoveAt(i);
                file ??= _pending.FirstOrDefault(e => e.Tier <= limit);
                if (file == null) voices = _voices.FirstOrDefault(p => p.Tier <= limit);
                left = _pending.Where(e => e.Tier <= limit).Sum(e => e.Size) + _voices.Where(p => p.Tier <= limit).Sum(p => p.Size);
            }
            if (file == null && voices == null)
            {
                Status = "";
                got = 0;
                await Task.Delay(500);
                continue;
            }
            try
            {
                long before = got, all = before + Math.Max(left, file?.Size ?? voices!.Size);
                void Show(long n)
                {
                    got = before + n;
                    Status = $"자료 받는 중 {got * 100 / Math.Max(1, all)}% ({got >> 20}/{all >> 20}MB)";
                }
                if (file != null) await GetFile(file, Show);
                else await GetPart(BaseRelease, voices!, "bgm", Show);
                lock (Gate)
                {
                    if (file != null) Forget(file);
                    else _voices.Remove(voices!);
                }
            }
            catch (InvalidDataException)
            {
                // 깨진 파일 — 이번 판에는 다시 받지 않는다(다음에 켤 때 다시 본다).
                lock (Gate)
                {
                    if (file != null) Forget(file);
                    else _voices.Remove(voices!);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                // 인터넷 없음·디스크 꽉 참·GitHub 막힘 — 조용히 쉬었다가 다시 한다. 쉬는 동안 찾는 파일은 「없음」으로 바로 돌려준다.
                System.Diagnostics.Debug.WriteLine($"[Pack] {ex.GetType().Name}: {ex.Message}");
                Status = "";
                lock (Gate) { _retryAt = DateTime.UtcNow.AddSeconds(30); _wanted.Clear(); }
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        }
    }

    private static void Forget(Entry e)
    {
        _pending.Remove(e);
        _byName.Remove(e.Name);
        _wanted.Remove(e.Name);
    }

    /// <summary>낱장 하나를 받아 제자리에 둔다 — 해시가 다르면 <see cref="InvalidDataException"/>.</summary>
    private static async Task GetFile(Entry e, Action<long> progress)
    {
        string to = LocalPath(e), part = to + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        string release = e.Release.Length > 0 && e.Release.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-') ? e.Release : BaseRelease;
        await Download($"{BaseUrl}{release}/{e.Asset}", part, e.Sha256, progress);
        File.Move(part, to, overwrite: true);
    }

    /// <summary>
    /// 덩이(zip) 하나를 받아 푼다 — <paramref name="into"/> 가 있으면 그 폴더에 이름만 따서(음성), 없으면 덩이 안의 자리대로(바탕).
    /// </summary>
    private static async Task GetPart(string release, Part part, string? into, Action<long> progress)
    {
        if (part.Asset != Path.GetFileName(part.Asset)) throw new InvalidDataException(part.Asset);
        string zip = Path.Combine(PartsFolder, part.Asset + ".part");
        Directory.CreateDirectory(PartsFolder);
        await Download($"{BaseUrl}{release}/{part.Asset}", zip, part.Sha256, progress);
        string root = Path.GetFullPath(into == null ? Folder : Path.Combine(Folder, into)) + Path.DirectorySeparatorChar;
        using (var archive = ZipFile.OpenRead(zip))
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length == 0) continue;
                // 덩이 안의 경로가 폴더 밖을 가리켜도 자료 폴더 안에만 푼다.
                string to = Path.GetFullPath(Path.Combine(root, into == null ? entry.FullName : entry.Name));
                if (!to.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                // 잘린 파일이 읽히지 않게 임시 이름으로 푼 뒤 옮긴다.
                entry.ExtractToFile(to + ".tmp", overwrite: true);
                File.Move(to + ".tmp", to, overwrite: true);
            }
        File.Delete(zip);
        File.WriteAllText(Marker(part), part.Sha256);
    }

    private static async Task Download(string url, string to, string sha256, Action<long> progress)
    {
        using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var from = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(to);
            var buffer = new byte[1 << 16];
            long got = 0;
            while (true)
            {
                // 본문이 멈추면 60초 뒤 그만둔다 — 전체 시간 제한은 머리글까지만 걸린다.
                using var stall = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                int n = await from.ReadAsync(buffer, stall.Token);
                if (n <= 0) break;
                await file.WriteAsync(buffer.AsMemory(0, n));
                progress(got += n);
            }
        }
        bool good;
        await using (var check = File.OpenRead(to))
            good = Convert.ToHexString(await SHA256.HashDataAsync(check)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
        if (!good)
        {
            File.Delete(to);
            throw new InvalidDataException($"{Path.GetFileName(url)} 이(가) 깨져서 왔습니다.");
        }
    }

    private static List<Part> Parts(JsonElement root, string name) =>
        root.TryGetProperty(name, out var list)
            ? [.. list.EnumerateArray().Select(p => new Part(p.GetProperty("asset").GetString() ?? "", p.GetProperty("size").GetInt64(),
                                                             p.GetProperty("sha256").GetString() ?? "", p.TryGetProperty("tier", out var tier) ? tier.GetInt32() : 0))]
            : [];

    private static List<Entry> Entries(JsonElement root) =>
        [.. root.GetProperty("files").EnumerateArray().Select(p => new Entry(
            p.GetProperty("name").GetString() ?? "", p.GetProperty("asset").GetString() ?? "", p.GetProperty("size").GetInt64(),
            p.GetProperty("sha256").GetString() ?? "", p.GetProperty("tier").GetInt32(), p.GetProperty("release").GetString() ?? ""))];
}

using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 필드·챕터 대사 음성(<c>Bgm\NNNN.bgm</c> 2,935개, 약 263MB) — 설치 꾸러미에 싣지 않고 GitHub 릴리즈 <c>assets-voices-1</c> 에 몇 덩이로 나눠 올려 두었다가,
/// 게임을 켰을 때 없으면 뒤에서 받아 사용자 폴더(<c>%APPDATA%\DuelDx\voices</c>)에 푼다.
/// </summary>
/// <remarks>
/// 사용자 폴더에 두는 까닭: 설치판은 업데이트 때 앱 폴더를 통째로 갈아 끼우고, 단일 exe 는 assets 를 임시 폴더에 푼다 — 어느 쪽에 받아도 다음에 사라진다.
/// 인터넷이 없거나 GitHub 이 막혀도 게임은 그대로 돈다(음성만 없다) — 다음에 켤 때 남은 덩이부터 다시 받는다.
/// 화면 밖 시험(<c>DUELDX_OFFSCREEN=1</c>)에서는 받지 않는다. <c>DUELDX_VOICES=0</c> 으로 끌 수 있고, <c>DUELDX_VOICES=1</c> 이면 시험 중에도 받는다.
/// </remarks>
internal static class VoicePack
{
    /// <summary>음성 덩이가 올라가 있는 릴리즈.</summary>
    private const string BaseUrl = "https://github.com/Kyeongrok/the-war-of-genesis-clicker/releases/download/assets-voices-1/";

    private const string ManifestName = "voices.json";

    /// <summary>받은 음성이 놓이는 폴더.</summary>
    public static string Folder { get; } = Path.Combine(UserDataFolder.Path, "voices");

    /// <summary>화면에 보일 진행 글 — 받을 것이 없거나 다 받았으면 빈 글.</summary>
    public static string Status { get; private set; } = "";

    /// <summary>
    /// <c>NNNN.bgm</c> 의 자리 — 게임에 실린 것(<c>assets/bgm</c>)이 먼저고, 없으면 받은 음성 폴더. 둘 다 없으면 실린 쪽 경로를 돌려준다(없는 파일).
    /// </summary>
    public static string BgmPath(int id)
    {
        string shipped = Path.Combine(AssetsFolder.Find("bgm"), $"{id:D4}.bgm");
        if (File.Exists(shipped)) return shipped;
        string voice = Path.Combine(Folder, $"{id:D4}.bgm");
        if (File.Exists(voice)) return voice;
        // 음악은 따로 받는 꾸러미에 있다(AssetPack) — 받았으면 그것, 아직이면 먼저 받게 당겨 둔다.
        string packed = AssetPack.PathOf("bgm", $"{id:D4}.bgm");
        return File.Exists(packed) ? packed : shipped;
    }

    public static void EnsureInBackground()
    {
        string? flag = Environment.GetEnvironmentVariable("DUELDX_VOICES");
        if (flag == "0") return;
        if (flag != "1" && Environment.GetEnvironmentVariable("DUELDX_OFFSCREEN") == "1") return;
        _ = Task.Run(EnsureAsync);
    }

    private sealed record Part(string Name, long Size, string Sha256, int Count);

    private static async Task EnsureAsync()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WarOfGenesis-DuelDx");

            // 목록 — 한 번 받은 것을 곁에 두고, 덩이를 다 받았으면 다음부터는 인터넷을 안 건드린다.
            string manifestPath = Path.Combine(Folder, ManifestName);
            if (File.Exists(manifestPath) && Parts(File.ReadAllText(manifestPath)) is { Count: > 0 } known && known.All(Done)) return;
            string json = await http.GetStringAsync(BaseUrl + ManifestName);
            if (Parts(json) is not { Count: > 0 } parts) return;
            File.WriteAllText(manifestPath, json);

            var todo = parts.Where(p => !Done(p)).ToList();
            long total = todo.Sum(p => p.Size), got = 0;
            foreach (var part in todo)
            {
                if (part.Name != Path.GetFileName(part.Name)) continue;     // 목록의 이름은 파일 이름만
                string zip = Path.Combine(Folder, part.Name + ".part");
                using (var response = await http.GetAsync(BaseUrl + part.Name, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    await using var from = await response.Content.ReadAsStreamAsync();
                    await using var to = File.Create(zip);
                    var buffer = new byte[1 << 16];
                    while (true)
                    {
                        // 본문이 멈추면 60초 뒤 그만둔다 — 전체 시간 제한은 머리글까지만 걸린다.
                        using var stall = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                        int n = await from.ReadAsync(buffer, stall.Token);
                        if (n <= 0) break;
                        await to.WriteAsync(buffer.AsMemory(0, n));
                        got += n;
                        Status = $"대사 음성 받는 중 {got * 100 / Math.Max(1, total)}% ({got >> 20}/{total >> 20}MB)";
                    }
                }
                // 깨진 덩이는 풀지 않는다 — 지우고 다음에 다시 받는다.
                await using (var check = File.OpenRead(zip))
                    if (!Convert.ToHexString(await SHA256.HashDataAsync(check)).Equals(part.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        check.Close();
                        File.Delete(zip);
                        Status = "";
                        return;
                    }
                using (var archive = ZipFile.OpenRead(zip))
                    foreach (var entry in archive.Entries)
                    {
                        // 이름만 쓴다 — 덩이 안의 경로가 폴더 밖을 가리켜도 음성 폴더 안에만 푼다.
                        string name = Path.GetFileName(entry.FullName);
                        if (name.Length == 0 || !name.EndsWith(".bgm", StringComparison.OrdinalIgnoreCase)) continue;
                        // 잘린 파일이 음성으로 읽히지 않게 임시 이름으로 푼 뒤 옮긴다.
                        string to = Path.Combine(Folder, name);
                        entry.ExtractToFile(to + ".tmp", overwrite: true);
                        File.Move(to + ".tmp", to, overwrite: true);
                    }
                File.Delete(zip);
                File.WriteAllText(Marker(part), part.Sha256);
            }
            Status = "";
        }
        catch (Exception ex)
        {
            // 인터넷 없음·디스크 꽉 참·GitHub 막힘 — 조용히 넘어가고 다음에 켤 때 다시 본다.
            System.Diagnostics.Debug.WriteLine($"[Voices] {ex.GetType().Name}: {ex.Message}");
            Status = "";
        }
    }

    private static string Marker(Part part) => Path.Combine(Folder, part.Name + ".done");

    /// <summary>그 덩이를 다 풀었나 — 표식에 적힌 해시가 목록과 같아야 한다(덩이를 새로 올리면 다시 받는다).</summary>
    private static bool Done(Part part) =>
        File.Exists(Marker(part)) && File.ReadAllText(Marker(part)).Trim().Equals(part.Sha256, StringComparison.OrdinalIgnoreCase);

    private static List<Part>? Parts(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return [.. doc.RootElement.GetProperty("parts").EnumerateArray().Select(p => new Part(
                p.GetProperty("name").GetString() ?? "", p.GetProperty("size").GetInt64(),
                p.GetProperty("sha256").GetString() ?? "", p.GetProperty("count").GetInt32()))];
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }
}

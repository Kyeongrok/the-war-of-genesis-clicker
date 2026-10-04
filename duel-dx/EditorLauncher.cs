using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 편집기(<c>WarOfGenesis.Editor.exe</c>)를 연다 — 설치판은 편집기를 싣지 않는다(사용자 요청: 설치 용량). 게임 곁에 있으면(저장소·옛 설치) 그것을,
/// 없으면 사용자 폴더(<c>%APPDATA%\DuelDx\editor</c>)에 받아 둔 것을, 그것도 없으면 최신 릴리즈의 <c>WarOfGenesis.Editor.zip</c> 을 받아 풀고 연다.
/// 편집기는 게임의 <c>assets</c> 를 고치므로 그 자리(<c>WAROFGENESIS_ASSETS</c>)와 게임 실행 파일(<c>WAROFGENESIS_GAME</c> — 「게임에서 실험」)을 알려 준다.
/// </summary>
internal static class EditorLauncher
{
    private const string Url = "https://github.com/Kyeongrok/the-war-of-genesis-clicker/releases/latest/download/WarOfGenesis.Editor.zip";
    private static readonly string Folder = Path.Combine(UserDataFolder.Path, "editor");
    private static bool _busy;

    public static void Open(Action<string> say)
    {
        string beside = Path.Combine(AppContext.BaseDirectory, "WarOfGenesis.Editor.exe");
        string fetched = Path.Combine(Folder, "WarOfGenesis.Editor.exe");
        string? exe = File.Exists(beside) ? beside : File.Exists(fetched) ? fetched : FindInRepo();
        if (exe != null) { Start(exe, say); return; }
        if (_busy) { say("편집기를 받는 중입니다…"); return; }
        _busy = true;
        say("편집기를 받습니다 — 다 받으면 열립니다");
        _ = Task.Run(async () =>
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string zip = Path.Combine(Folder, "editor.zip.part");
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("WarOfGenesis-DuelDx");
                    await using var from = await http.GetStreamAsync(Url);
                    await using var to = File.Create(zip);
                    await from.CopyToAsync(to);
                }
                ZipFile.ExtractToDirectory(zip, Folder, overwriteFiles: true);
                File.Delete(zip);
                if (File.Exists(fetched)) Start(fetched, say); else say("편집기 꾸러미에 실행 파일이 없습니다");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
            {
                say($"편집기를 못 받았습니다: {ex.Message}");
            }
            finally { _busy = false; }
        });
    }

    /// <summary>저장소에서 돌릴 때 — 위로 올라가며 편집기 빌드를 찾는다.</summary>
    private static string? FindInRepo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string bin = Path.Combine(dir.FullName, "WarOfGenesis.Editor", "bin");
            if (!Directory.Exists(bin)) continue;
            return new[] { "Debug", "Release" }.Select(c => Path.Combine(bin, c)).Where(Directory.Exists)
                .SelectMany(c => Directory.EnumerateFiles(c, "WarOfGenesis.Editor.exe", SearchOption.AllDirectories))
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        return null;
    }

    private static void Start(string exe, Action<string> say)
    {
        try
        {
            var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false };
            start.Environment["WAROFGENESIS_ASSETS"] = Path.GetDirectoryName(AssetsFolder.Find("data"))!;
            if (Environment.ProcessPath is { } game) start.Environment["WAROFGENESIS_GAME"] = game;
            Process.Start(start);
            say("편집기를 열었습니다");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            say($"편집기를 못 열었습니다: {ex.Message}");
        }
    }
}

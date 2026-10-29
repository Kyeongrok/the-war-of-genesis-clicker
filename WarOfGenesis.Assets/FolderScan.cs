using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 이 PC 에서 원본 게임 폴더를 찾아본다 — 파트1(<see cref="Part1Game.Scan"/>) · 파트2(<see cref="OriginalGame.Scan"/>)가 함께 쓴다.
/// 압축을 풀어 둔 원본이 흔히 있는 곳(<c>nearFirst</c> · 바탕 화면 · 다운로드 · 문서 · Program Files · 드라이브\Games)을 먼저 깊이 4 까지,
/// 그다음 고정 드라이브마다 깊이 3 까지 훑는다. 시스템 폴더는 건너뛰고 <c>limit</c> 가 지나면 그만둔다. 못 찾으면 null.
/// </summary>
public static class FolderScan
{
    public static string? Find(Func<string, bool> isValid, IEnumerable<string> nearFirst, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Home(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        string profile = Home(Environment.SpecialFolder.UserProfile);
        var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList();
        var near = new List<string>(nearFirst)
        {
            Home(Environment.SpecialFolder.DesktopDirectory), Path.Combine(profile, "Downloads"), Home(Environment.SpecialFolder.MyDocuments), profile,
            Home(Environment.SpecialFolder.ProgramFilesX86), Home(Environment.SpecialFolder.ProgramFiles),
        };
        near.AddRange(drives.Select(d => Path.Combine(d, "Games")));
        foreach (string start in near)
            if (Search(start, 4) is { } found) return found;
        foreach (string drive in drives)
            if (Search(drive, 3) is { } found) return found;
        return null;

        string? Search(string start, int depth)
        {
            var queue = new Queue<(string Dir, int Depth)>([(start, 0)]);
            while (queue.Count > 0 && DateTime.UtcNow < until)
            {
                var (dir, d) = queue.Dequeue();
                if (dir.Length == 0 || !seen.Add(dir)) continue;
                if (isValid(dir)) return dir;
                if (d >= depth) continue;
                try
                {
                    foreach (string sub in Directory.EnumerateDirectories(dir))
                        if (!Skipped(Path.GetFileName(sub))) queue.Enqueue((sub, d + 1));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
            return null;
        }

        static bool Skipped(string name) => name.StartsWith('$') || name.StartsWith('.') || name is "Windows" or "AppData" or "node_modules" or "ProgramData"
            or "System Volume Information" or "Recovery" or "PerfLogs" or "WinSxS" or "Microsoft" or "obj" or "bin";
    }
}

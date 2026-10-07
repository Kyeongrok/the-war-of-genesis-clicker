using System.IO;
using System.Reflection;

namespace WarOfGenesis.Assets;

/// <summary>
/// 배포판에는 원본 게임의 자료를 싣지 않는다 — 우리 것(스킬·직업 JSON, 인물 목록)만 싣고, 원본 자료는 사용자의 원본 게임 폴더
/// (<see cref="OriginalGame"/>)에서 꺼내 <c>%APPDATA%\DuelDx\assets</c> 에 저장소의 <c>assets</c> 와 같은 꼴로 차려 놓는다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>켤 때 한 번(<see cref="Prepare"/>): 실린 우리 파일을 옮기고, 목록(<c>assets/originals.txt</c>)의 원본 파일 중 없는 것을 꺼낸다 —
/// 폴더를 훑어 읽는 것들(자료 표·효과 그림·효과음)이라 미리 있어야 한다.</item>
/// <item>쓸 때(<see cref="Fetch"/>): 목록에 없는 큰 것(음악·맵·인물 그림·모세스 그림)은 찾는 그때 꺼낸다. 원본 폴더에 낱장으로 있으면
/// 옮기지 않고 그 자리를 그대로 가리킨다.</item>
/// </list>
/// 저장소에서 돌릴 때(개발)는 <c>assets</c> 에 원본 자료까지 다 있어(git 은 무시한다) 아무것도 안 한다.
/// 우리가 원본 파일을 고친 곳은 고친 파일을 싣지 않고 바뀐 바이트만 <c>assets/patches.txt</c> 에 적어 꺼낼 때 얹는다.
/// </remarks>
public static class OriginalAssets
{
    public const string ManifestName = "originals.txt";
    public const string PatchesName = "patches.txt";
    private const string StampName = ".shipped";

    /// <summary>원본 자료를 차려 놓는 폴더 — 세이브 폴더 옆(<c>DUELDX_SAVEDIR</c> 로 옮기면 따라간다: 화면 밖 시험이 사람 폴더를 안 건드리게).</summary>
    public static string LocalFolder { get; } = Path.Combine(
        Environment.GetEnvironmentVariable("DUELDX_SAVEDIR") is { Length: > 0 } dir
            ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuelDx"),
        "assets");

    /// <summary>그 <c>assets</c> 폴더에 원본 자료까지 다 있나(저장소에서 돌릴 때).</summary>
    public static bool IsComplete(string assets) => File.Exists(Path.Combine(assets, "data", "TXR", "Txr.dat"));

    /// <summary>원본 게임 폴더가 있어야 도나 — 실린 <c>assets</c> 에 원본 자료가 없을 때.</summary>
    public static bool NeedsGame => AssetsFolder.Given == null && AssetsFolder.Shipped() is { } shipped && !IsComplete(shipped);

    /// <summary>
    /// 원본 게임 폴더에서 자료를 차린다 — 이미 있는 것은 건너뛰어 두 번째부터는 금방 끝난다.
    /// </summary>
    /// <param name="progress">(끝낸 수, 전체 수).</param>
    /// <returns>원본 폴더에서 못 찾은 파일 수.</returns>
    public static int Prepare(string gameRoot, Action<int, int>? progress = null)
    {
        string shipped = AssetsFolder.Shipped() ?? throw new DirectoryNotFoundException("assets 폴더(assets/characters)를 못 찾았습니다.");
        if (IsComplete(shipped)) return 0;
        OriginalGame.Use(gameRoot);
        var files = OriginalGame.Files!;
        Directory.CreateDirectory(LocalFolder);

        // 실린 우리 파일 — 판이 바뀌었을 때만 다시 옮긴다(편집기로 고친 것이 켤 때마다 되돌아가지 않게).
        string stampPath = Path.Combine(LocalFolder, StampName);
        string stamp = $"{Assembly.GetEntryAssembly()?.GetName().Version}|{shipped}|{File.GetLastWriteTimeUtc(Path.Combine(shipped, ManifestName)).Ticks}";
        if (!File.Exists(stampPath) || File.ReadAllText(stampPath) != stamp)
        {
            foreach (string from in Directory.EnumerateFiles(shipped, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(LocalFolder, Path.GetRelativePath(shipped, from));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to, overwrite: true);
            }
            // 우리가 고친 원본 파일은 새 판의 고침으로 다시 꺼낸다.
            foreach (string patched in Patches(LocalFolder).Keys)
                if (Path.Combine(LocalFolder, patched.Replace('/', Path.DirectorySeparatorChar)) is var old && File.Exists(old)) File.Delete(old);
            File.WriteAllText(stampPath, stamp);
        }

        string manifest = Path.Combine(LocalFolder, ManifestName);
        string[] wanted = File.Exists(manifest) ? [.. File.ReadLines(manifest).Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#')] : [];
        var patches = Patches(LocalFolder);
        int done = 0, missing = 0;
        foreach (string rel in wanted)
        {
            progress?.Invoke(done++, wanted.Length);
            string to = Path.Combine(LocalFolder, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(to)) continue;
            if (!Extract(files, rel, to, patches)) missing++;
        }
        progress?.Invoke(wanted.Length, wanted.Length);
        AssetsFolder.Use(LocalFolder);
        return missing;
    }

    /// <summary>
    /// 그 파일을 쓸 수 있는 자리 — 차려 둔 것이 있으면 그것, 없으면 원본 게임 폴더에서(낱장이면 그 자리 그대로, 묶음 안이면 꺼내 놓고). 끝내 없으면 null.
    /// </summary>
    /// <param name="kind"><c>assets</c> 밑 폴더(<c>bgm</c> · <c>moses/obs</c> · <c>characters/0221_죠안</c> 꼴).</param>
    public static string? Fetch(string kind, string file)
    {
        string here;
        try { here = Path.Combine(AssetsFolder.Find(kind), file); }
        catch (DirectoryNotFoundException) { return null; }
        if (File.Exists(here)) return here;
        if (OriginalGame.Files is not { } files) return null;

        string rel = $"{kind}/{file}";
        var patches = Patches(Path.GetDirectoryName(AssetsFolder.Find("data"))!);
        if (!patches.ContainsKey(rel) && OriginalGame.Locate(rel) is var (folder, name) && files.LoosePath(folder, name) is { } loose) return loose;
        return Extract(files, rel, here, patches) ? here : null;
    }

    private static bool Extract(GameFiles files, string rel, string to, Dictionary<string, (int Offset, byte Value)[]> patches)
    {
        if (OriginalGame.Locate(rel) is not var (folder, name)) return false;
        try
        {
            if (files.Read(folder, name) is not { } bytes) return false;
            if (patches.TryGetValue(rel, out var patch))
                foreach (var (offset, value) in patch)
                    if (offset < bytes.Length) bytes[offset] = value;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            // 잘린 파일이 읽히지 않게 임시 이름으로 쓴 뒤 옮긴다(같은 파일을 두 스레드가 찾아도 된다).
            string part = $"{to}.{Environment.CurrentManagedThreadId}.part";
            File.WriteAllBytes(part, bytes);
            File.Move(part, to, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return File.Exists(to); }
    }

    private static (string Folder, Dictionary<string, (int Offset, byte Value)[]> Table)? _patches;

    /// <summary><c>patches.txt</c> — 한 줄에 <c>파일 자리 위치=값 위치=값 …</c>(십진수). 원본 파일에서 우리가 바꾼 바이트.</summary>
    private static Dictionary<string, (int Offset, byte Value)[]> Patches(string assets)
    {
        if (_patches is { } cached && cached.Folder == assets) return cached.Table;
        var table = new Dictionary<string, (int, byte)[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string path = Path.Combine(assets, PatchesName);
            foreach (string line in File.Exists(path) ? File.ReadAllLines(path) : [])
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[0][0] == '#') continue;
                table[parts[0]] = [.. parts.Skip(1).Select(p => p.Split('=')).Where(p => p.Length == 2)
                                           .Select(p => (int.Parse(p[0]), byte.Parse(p[1])))];
            }
        }
        catch (Exception ex) when (ex is IOException or FormatException or OverflowException) { }
        _patches = (assets, table);
        return table;
    }
}

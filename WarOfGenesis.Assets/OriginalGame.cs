using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 사용자가 가진 <b>원본 게임 폴더</b>(<c>TXR</c>·<c>Chr</c>·<c>Obs</c> … 가 든 곳) — 원본 자료는 배포하지 않고 여기서 읽는다.
/// </summary>
/// <remarks>
/// 자리는 <c>%APPDATA%\DuelDx\gameroot.txt</c> 에 적어 둔다(편집기가 예전부터 쓰던 <c>%APPDATA%\WarOfGenesis.Editor\gameroot.txt</c> 도 읽는다).
/// <c>WAROFGENESIS_ORIGINAL=&lt;폴더&gt;</c> 가 있으면 그것이 먼저다. 게임 폴더에는 아무것도 쓰지 않는다.
/// </remarks>
public static class OriginalGame
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string SettingsPath = Path.Combine(AppData, "DuelDx", "gameroot.txt");
    private static readonly string EditorSettingsPath = Path.Combine(AppData, "WarOfGenesis.Editor", "gameroot.txt");

    private static string? _root;
    private static GameFiles? _files;

    /// <summary>원본 게임 폴더처럼 보이나 — <c>TXR\Txr.dat</c> 와 <c>Chr</c>·<c>Obs</c> 폴더가 있다.</summary>
    public static bool IsValid(string? root) =>
        root is { Length: > 0 } && File.Exists(Path.Combine(root, "TXR", "Txr.dat"))
        && Directory.Exists(Path.Combine(root, "Chr")) && Directory.Exists(Path.Combine(root, "Obs"));

    /// <summary>적어 둔 원본 게임 폴더 — 없거나 이제 못 쓰는 자리면 null.</summary>
    public static string? Root
    {
        get
        {
            if (_root != null) return _root;
            string?[] candidates = [Environment.GetEnvironmentVariable("WAROFGENESIS_ORIGINAL"), ReadSaved(SettingsPath), ReadSaved(EditorSettingsPath)];
            return _root = candidates.FirstOrDefault(IsValid);
        }
    }

    /// <summary>원본 게임 폴더의 파일들(낱장 + <c>.idx/.pak</c>) — 폴더를 모르면 null.</summary>
    public static GameFiles? Files => _files ??= Root is { } root ? GameFiles.FromGameRoot(root) : null;

    /// <summary>그 폴더를 원본 게임 폴더로 쓰고 적어 둔다.</summary>
    public static void Use(string root)
    {
        _root = root;
        _files = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 못 적어도 이번 판은 돈다 */ }
    }

    private static string? ReadSaved(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>우리 폴더에서만 나는 자료 — 원본 게임 폴더에는 없다.</summary>
    private static readonly string[] OwnDataFolders = ["jobs", "skills", "chr-edits"];

    /// <summary>
    /// <c>assets</c> 안의 자리(<c>moses/obs/0018.obs</c> 꼴)가 원본 게임 폴더의 어느 파일인가 — 우리 것(JSON 따위)이면 null.
    /// </summary>
    /// <remarks>
    /// 그림(<c>.obs</c>)은 어느 폴더에 있든 <c>Obs</c> 한 곳에서 온다. 효과음은 이름만 다르다(<c>sounds/0000.wav</c> = <c>Snd\0000.snd</c>, 내용은 같은 WAV).
    /// </remarks>
    public static (string Folder, string Name)? Locate(string assetPath)
    {
        string[] parts = assetPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        string top = parts[0], name = parts[^1], ext = Path.GetExtension(name).ToLowerInvariant();
        string? folder = ext switch
        {
            ".obs" => "Obs",
            ".bgm" => "BGM",
            ".obt" => "Obt",
            ".wav" when top == "sounds" => "Snd",
            ".chr" when top == "characters" => "Chr",
            _ when top == "moses" && parts.Length == 3 => parts[1] switch { "bgr" => "Bgr", "chp" => "Chp", "shp" => "Shp", "tlk" => "Tlk", _ => null },
            _ when top == "data" && parts.Length == 3 && !OwnDataFolders.Contains(parts[1]) && ext != ".json" => parts[1],
            _ => null,
        };
        if (folder == null) return null;
        return (folder, folder == "Snd" ? Path.ChangeExtension(name, ".snd") : name);
    }
}

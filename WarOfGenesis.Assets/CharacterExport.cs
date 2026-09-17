using System.IO;
using System.Text.Json;

namespace WarOfGenesis.Assets;

/// <summary>
/// 뽑아 둔 인물 하나 — 이름과 Chr·몸짓(sprite)·초상(face) 번호. <c>character.json</c> 로 적힌다.
/// </summary>
public sealed record ExportedCharacter(string Name, int ChrCode, int SpriteCode, int FaceCode);

/// <summary>
/// 인물 하나를 <b>원본 그대로의 파일 꼴</b>(<c>.chr</c>·<c>.obs</c>)로 폴더에 내보내고 읽는다.
/// </summary>
/// <remarks>
/// PNG 로 다시 구워 내지 않는다 — <c>&lt;ChrCode&gt;.chr</c> 와
/// <c>&lt;SpriteCode&gt;.obs</c>(초상이 다르면 <c>&lt;FaceCode&gt;.obs</c> 도)를 게임 폴더에서
/// <b>그대로 복사</b>해 옮긴다. 그러면 <see cref="ObsSprite"/>·<see cref="ChrTable"/> 를
/// <b>한 글자도 안 고치고</b> 이 폴더에도 그대로 쓸 수 있다 — 원본 게임을 안 갖고 있어도
/// 이 폴더 하나로 그 인물을 그릴 수 있다.
/// </remarks>
public static class CharacterExport
{
    public const string ManifestFile = "character.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void SaveManifest(string folder, ExportedCharacter data)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ManifestFile), JsonSerializer.Serialize(data, JsonOptions));
    }

    /// <summary>그 폴더에서 읽는다. <c>character.json</c> 이 없으면 null.</summary>
    public static ExportedCharacter? LoadManifest(string folder)
    {
        string path = Path.Combine(folder, ManifestFile);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<ExportedCharacter>(File.ReadAllText(path), JsonOptions);
    }

    /// <summary>그 인물의 <c>.obs</c> 파일이 이 폴더 안에 있는 이름. <c>0323.obs</c> 꼴.</summary>
    public static string ObsFileName(int code) => $"{code:D4}.obs";

    /// <summary>그 인물의 <c>.chr</c> 파일이 이 폴더 안에 있는 이름. <c>0007.chr</c> 꼴.</summary>
    public static string ChrFileName(int chrCode) => $"{chrCode:D4}.chr";

    /// <summary>인물 폴더 이름 — <c>0007_나야트레이</c> 꼴. 파일 이름에 못 쓰는 글자는 걷어낸다.</summary>
    public static string FolderNameFor(int chrCode, string name)
    {
        var safe = new string([.. name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))]);
        return safe.Length > 0 ? $"{chrCode:D4}_{safe}" : $"{chrCode:D4}";
    }
}

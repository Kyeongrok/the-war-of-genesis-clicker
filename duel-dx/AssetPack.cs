using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 켤 때 미리 차려 두지 않는 큰 자료 — 음악(<c>bgm</c>)·전투 맵(<c>maps</c>)·인물 그림·모세스 그림·큰 효과음 — 을 <b>찾는 그때</b> 사용자의
/// 원본 게임 폴더에서 가져온다(<see cref="OriginalAssets.Fetch"/>). 원본 자료는 배포하지 않는다 — 전에는 GitHub 릴리즈에 올려 두고 받았다.
/// </summary>
/// <remarks>
/// 원본 폴더에 낱장으로 있는 것(음악·맵)은 옮기지 않고 그 자리를 그대로 쓰고, 묶음(<c>.pak</c>) 안의 것은 <c>%APPDATA%\DuelDx\assets</c> 에 꺼내 둔다.
/// 저장소에서 바로 돌릴 때(개발)는 <c>assets</c> 에 다 있어서 아무것도 가져오지 않는다. DUELDX_ASSETLOG=파일 이면 찾은 이름을 적는다.
/// </remarks>
internal static class AssetPack
{
    /// <summary>원본 게임 폴더에서 꺼낸 자료가 놓이는 폴더.</summary>
    public static string Folder => OriginalAssets.LocalFolder;

    private static readonly object LogGate = new();

    /// <summary>그 파일을 쓸 수 있는 자리 — 없으면 null.</summary>
    public static string? Fetch(string kind, string file)
    {
        if (Environment.GetEnvironmentVariable("DUELDX_ASSETLOG") is { Length: > 0 } log)
            try { lock (LogGate) File.AppendAllText(log, $"{kind}/{file}" + Environment.NewLine); } catch (IOException) { }
        return OriginalAssets.Fetch(kind, file);
    }

    /// <summary>그 파일의 자리 — 없으면 <c>assets</c> 쪽 경로(없는 파일)를 돌려준다(부르는 쪽이 「못 읽음」으로 다룬다).</summary>
    public static string PathOf(string kind, string file) => OriginalAssets.Fetch(kind, file) ?? Path.Combine(AssetsFolder.Find(kind), file);

    /// <summary>전투 맵 파일 자리.</summary>
    public static string MapPath(string file) => PathOf("maps", file);
}

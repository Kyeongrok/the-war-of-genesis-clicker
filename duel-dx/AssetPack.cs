using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 켤 때 미리 차려 두지 않는 큰 자료 — 음악(<c>bgm</c>)·전투 맵(<c>maps</c>)·인물 그림·모세스 그림·큰 효과음 — 을 <b>찾는 그때</b> 가져온다.
/// </summary>
/// <remarks>
/// 가져오는 곳은 둘 중 하나다(켤 때 <see cref="OriginalGameSetup"/> 가 정한다).
/// <list type="bullet">
/// <item>사용자의 <b>원본 게임 폴더</b>(<see cref="OriginalAssets.Fetch"/>) — 낱장으로 있는 것(음악·맵)은 옮기지 않고 그 자리를 그대로 쓰고,
/// 묶음(<c>.pak</c>) 안의 것은 <c>%APPDATA%\DuelDx\assets</c> 에 꺼내 둔다.</item>
/// <item>원본 폴더를 모르는 사용자는 <b>GitHub 릴리즈</b>(<see cref="AssetDownload"/>) — 뒤에서 받고 있다가, 찾는 파일이 아직 없으면 그것부터 받아 온다.
/// 그래서 이쪽은 기다리는 시간(초)이 있다.</item>
/// </list>
/// 저장소에서 바로 돌릴 때(개발)는 <c>assets</c> 에 다 있어서 아무것도 가져오지 않는다. DUELDX_ASSETLOG=파일 이면 찾은 이름을 적는다.
/// </remarks>
internal static class AssetPack
{
    /// <summary>가져온 자료가 놓이는 폴더.</summary>
    public static string Folder => OriginalAssets.LocalFolder;

    /// <summary>화면에 보일 받기 진행 글 — 받는 중이 아니면 빈 글.</summary>
    public static string Status => AssetDownload.Status;

    private static readonly object LogGate = new();

    /// <summary>그 파일이 앞으로 올 수 있나 — 받는 길이고 아직 못 받았을 때만(부르는 쪽이 「없는 그림」으로 기억하면 안 된다).</summary>
    public static bool MayCome(string kind, string file) => AssetDownload.MayCome($"{kind}/{file}");

    /// <summary>그 파일을 쓸 수 있는 자리 — 받는 길이면 올 때까지(최대 <paramref name="seconds"/>초) 기다린다. 끝내 없으면 null.</summary>
    public static string? Fetch(string kind, string file, int seconds = 15)
    {
        if (Environment.GetEnvironmentVariable("DUELDX_ASSETLOG") is { Length: > 0 } log)
            try { lock (LogGate) File.AppendAllText(log, $"{kind}/{file}" + Environment.NewLine); } catch (IOException) { }
        return AssetDownload.Active ? AssetDownload.Fetch(kind, file, seconds) : OriginalAssets.Fetch(kind, file);
    }

    /// <summary>
    /// 그 파일의 자리 — 없으면 <c>assets</c> 쪽 경로(없는 파일)를 돌려준다(부르는 쪽이 「못 읽음」으로 다룬다). 기다리지 않는다 —
    /// 받는 길이면 먼저 받게 당겨만 둔다.
    /// </summary>
    public static string PathOf(string kind, string file) =>
        (AssetDownload.Active ? AssetDownload.Fetch(kind, file, 0) : OriginalAssets.Fetch(kind, file)) ?? Path.Combine(AssetsFolder.Find(kind), file);

    /// <summary>그 파일이 올 때까지 기다린다 — 음악처럼 뒤 스레드에서 기다려도 되는 것. 있으면 true.</summary>
    public static bool EnsureNow(string kind, string file, int seconds) => Fetch(kind, file, seconds) != null;

    /// <summary>전투 맵 파일 자리 — 받는 길이면 받을 때까지(최대 2분) 기다린다. 맵이 없으면 전투를 못 연다.</summary>
    public static string MapPath(string file) => Fetch("maps", file, 120) ?? Path.Combine(AssetsFolder.Find("maps"), file);

    /// <summary>
    /// 원본 게임 폴더에서 만들 수 없는 우리 파일(기술 컷신 영상 <c>effects/cut</c>) — 있으면 그 자리, 없으면 뒤에서 받게 걸어 두고 null.
    /// 원본 폴더로 도는 판도 이것은 릴리즈에서 받는다(<see cref="AssetDownload.FetchExtra"/>). 기다리지 않는다.
    /// </summary>
    public static string? FetchExtra(string kind, string file)
    {
        try
        {
            string here = Path.Combine(AssetsFolder.Find(kind), file);
            if (File.Exists(here)) return here;
        }
        catch (DirectoryNotFoundException) { return null; }
        return AssetDownload.FetchExtra(kind, file);
    }

    /// <summary>연대표의 그 줄(0~14)에 들어갔다 — 받는 길이면 그 줄 + <see cref="AssetDownload.Ahead"/> 까지 미리 받는다.</summary>
    public static void Reach(int row) => AssetDownload.Reach(row);
}

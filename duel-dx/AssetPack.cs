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

    /// <summary>대사 음성이 아직 덜 와서 목소리가 안 날 때 보일 글 — 그럴 일이 없으면 빈 글.</summary>
    public static string VoiceStatus => AssetDownload.VoiceStatus;

    /// <summary>목소리 없는 대사가 떴다 — 받는 길이면 대사 음성을 남은 자료보다 먼저 받는다.</summary>
    public static void WantVoices() => AssetDownload.WantVoices();

    private static readonly object LogGate = new();

    /// <summary>그 파일이 앞으로 올 수 있나 — 받는 길이고 아직 못 받았을 때만(부르는 쪽이 「없는 그림」으로 기억하면 안 된다).</summary>
    // 파트1 번호대는 파트1 폴더가 없으면 릴리즈에서 올 수 있다 — 「없는 그림」으로 굳히지 않는다(menu-91).
    public static bool MayCome(string kind, string file) =>
        Part1Game.GameNumberOf(file) != null ? Part1Game.Root == null : AssetDownload.MayCome($"{kind}/{file}");

    /// <summary>
    /// 파트1(전작) 그림·소리를 게임이 읽는 꼴로 만들어 두는 폴더 — 번호가 <see cref="Part1Game.ObsBase"/> 위인 것은 사용자의 파트1 폴더에서 만든다(사용자 요청 menu-76).
    /// </summary>
    public static string Part1Folder => Path.Combine(Folder, "part1");

    /// <summary>
    /// 파트1 번호대 파일 — 파트1 폴더가 있으면 거기서 만들고, 없으면 릴리즈(pack.json 의 「찾을 때만 받는 것」, <c>assets-part1-1</c>)에서 받는다(사용자 요청 menu-80).
    /// 기다리는 것은 부르는 쪽이 준 <paramref name="seconds"/> 만큼이다 — 0 이면 받게 걸어만 두고 바로 돌아온다. 전에는 적어도 20초를 기다려,
    /// 파트1 이 없는 PC 에서 그리기(<c>UiFor</c>)가 처음 보는 파트1 이펙트마다 20초씩 멈췄다(menu-91).
    /// </summary>
    private static string? Part1(string file, int seconds)
    {
        if (Part1Game.FilePath(file, Part1Folder) is { } made) return made;
        string kind = $"part1/{Part1Game.CacheSub(file)}", path = Path.Combine(Folder, kind.Replace('/', Path.DirectorySeparatorChar), file);
        if (AssetDownload.FetchExtra(kind, file) is { } got) return got;
        for (var until = DateTime.UtcNow.AddSeconds(seconds); DateTime.UtcNow < until; Thread.Sleep(100))
            if (File.Exists(path)) return path;
        return null;
    }

    /// <summary>그 파일을 쓸 수 있는 자리 — 받는 길이면 올 때까지(최대 <paramref name="seconds"/>초) 기다린다. 끝내 없으면 null.</summary>
    public static string? Fetch(string kind, string file, int seconds = 15)
    {
        if (Environment.GetEnvironmentVariable("DUELDX_ASSETLOG") is { Length: > 0 } log)
            try { lock (LogGate) File.AppendAllText(log, $"{kind}/{file}" + Environment.NewLine); } catch (IOException) { }
        if (Part1Game.GameNumberOf(file) != null) return Part1(file, seconds);
        return AssetDownload.Active ? AssetDownload.Fetch(kind, file, seconds) : OriginalAssets.Fetch(kind, file);
    }

    /// <summary>
    /// 그 파일의 자리 — 없으면 <c>assets</c> 쪽 경로(없는 파일)를 돌려준다(부르는 쪽이 「못 읽음」으로 다룬다). 기다리지 않는다 —
    /// 받는 길이면 먼저 받게 당겨만 둔다.
    /// </summary>
    public static string PathOf(string kind, string file) =>
        (Part1Game.GameNumberOf(file) != null ? Part1(file, 0) : AssetDownload.Active ? AssetDownload.Fetch(kind, file, 0) : OriginalAssets.Fetch(kind, file)) ?? Path.Combine(AssetsFolder.Find(kind), file);

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

    /// <summary>파트1 시디 트랙을 우리가 바꿔 올린 WAV(<c>tools/part1-cd-pack</c>)가 놓이는 자리 — 실린 <c>assets</c> 와 받은 자료 폴더 밑에서 같다.</summary>
    public const string Part1CdKind = "part1/cd";

    /// <summary>
    /// 파트1 시디 트랙 <paramref name="track"/> 의 WAV(<c>part1/cd/TrackNN.wav</c>) — 실린 것이 있으면 그 자리, 없으면 릴리즈(<c>assets-part1cd-1</c>,
    /// pack.json 의 「찾을 때만 받는 것」)에서 받게 걸고 올 때까지 최대 <paramref name="seconds"/>초 기다린다. 받지 않는 판(저장소 개발 · 화면 밖 시험에서
    /// <c>DUELDX_PACK</c> · <c>DUELDX_PACKURL</c> 이 없을 때)이거나 받기가 막혔으면 바로 null — 뒤 스레드(곡 풀기)에서만 부른다.
    /// </summary>
    public static string? Part1CdTrack(int track, int seconds)
    {
        string file = $"Track{track:D2}.wav";
        return FetchExtra(Part1CdKind, file) ?? AssetDownload.WaitExtra(Part1CdKind, file, seconds);
    }

    /// <summary>연대표의 그 줄(0~14)에 들어갔다 — 받는 길이면 그 줄 + <see cref="AssetDownload.Ahead"/> 까지 미리 받는다.</summary>
    public static void Reach(int row) => AssetDownload.Reach(row);
}

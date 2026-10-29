namespace WarOfGenesis.Assets;

/// <summary>
/// <c>DUELDX_ASSETLOG=파일</c> 이면 실제로 읽은 자료 자리(<c>data/Dat/Job.dat</c> 꼴)를 한 줄씩 적는다 — 파트1 만 도는 판이 파트2 자료 중 무엇을
/// 쓰는지 재는 데 쓴다(사용자 요청 menu-109). 켤 때 폴더째 훑는 것은 적지 않고, 그 파일을 쓸 때 적는다.
/// </summary>
public static class AssetTrace
{
    private static readonly string? Log = Environment.GetEnvironmentVariable("DUELDX_ASSETLOG") is { Length: > 0 } log ? log : null;
    private static readonly object Gate = new();

    public static bool On => Log != null;

    public static void Note(string rel)
    {
        if (Log == null) return;
        try { lock (Gate) File.AppendAllText(Log, rel + Environment.NewLine); }
        catch (IOException) { }
    }
}

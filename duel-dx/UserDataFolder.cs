namespace DuelDx;

/// <summary>
/// 저장·설정·키 배치를 두는 폴더 — 평소에는 <c>%APPDATA%\DuelDx</c>.
/// </summary>
/// <remarks>
/// <c>DUELDX_SAVEDIR=&lt;경로&gt;</c> 면 그 폴더를 쓴다. 화면 밖 시험이 <b>사람이 쓰는 저장 파일을 덮어쓰지 않게</b> 둔 문이다
/// (시험이 자동 저장 슬롯을 날린 일이 있었다). 게임을 그냥 켜면 이 변수가 없어 예전과 같다.
/// </remarks>
internal static class UserDataFolder
{
    public static string Path { get; } =
        Environment.GetEnvironmentVariable("DUELDX_SAVEDIR") is { Length: > 0 } dir
            ? dir
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuelDx");

    public static string File(string name) => System.IO.Path.Combine(Path, name);
}

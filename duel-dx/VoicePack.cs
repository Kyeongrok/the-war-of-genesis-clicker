namespace DuelDx;

/// <summary>
/// 음악과 필드·챕터 대사 음성(<c>Bgm\NNNN.bgm</c>) — 사용자의 원본 게임 폴더에서 그대로 읽는다(<see cref="AssetPack"/>).
/// </summary>
internal static class VoicePack
{
    /// <summary><c>NNNN.bgm</c> 의 자리 — 없으면 <c>assets/bgm</c> 쪽 경로(없는 파일)를 돌려준다.</summary>
    public static string BgmPath(int id) => AssetPack.PathOf("bgm", $"{id:D4}.bgm");
}

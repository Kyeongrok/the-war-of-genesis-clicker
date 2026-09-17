using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 저장소에 커밋해 둔 <c>assets</c> 폴더(<c>characters</c>·<c>backgrounds</c>)를 찾는다.
/// </summary>
/// <remarks>
/// 실행 파일 자리에서 위로 거슬러 올라가며 <c>assets/characters</c> 가 있는 첫 폴더를 쓴다.
/// 저장소에서 돌리면 <c>bin/Debug/...</c> 위의 저장소 뿌리에서 찾고, 배포용 단일 exe
/// (<c>IncludeAllContentForSelfExtract</c>)는 <see cref="AppContext.BaseDirectory"/> 가 exe 가
/// 풀린 임시 폴더라 그 안에 함께 묶여 온 <c>assets</c> 를 찾는다.
/// </remarks>
public static class AssetsFolder
{
    public static string Find(string subFolder)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            string assets = Path.Combine(dir.FullName, "assets");
            if (Directory.Exists(Path.Combine(assets, "characters")))
                return Path.Combine(assets, subFolder);
        }
        throw new DirectoryNotFoundException("assets 폴더(assets/characters)를 못 찾았습니다.");
    }
}

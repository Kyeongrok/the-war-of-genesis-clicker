using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 게임이 읽는 <c>assets</c> 폴더(<c>characters</c>·<c>data</c>·<c>effects</c> …)를 찾는다.
/// </summary>
/// <remarks>
/// 실행 파일 자리에서 위로 거슬러 올라가며 <c>assets/characters</c> 가 있는 첫 폴더가 <b>실린</b> 폴더다(<see cref="Shipped"/>).
/// 저장소에서 돌리면 <c>bin/Debug/...</c> 위의 저장소 뿌리에서 찾고(원본 자료까지 다 있다), 배포판은 함께 묶여 온 <c>assets</c> 인데
/// 거기에는 우리 파일만 있어서 원본 게임 폴더에서 차려 놓은 폴더(<see cref="OriginalAssets.LocalFolder"/>)를 쓴다.
/// </remarks>
public static class AssetsFolder
{
    private static string? _root;

    /// <summary>WAROFGENESIS_ASSETS=&lt;assets 폴더&gt; — 따로 받은 편집기(게임 폴더 밖에 풀린다)를 게임이 띄울 때 제 assets 자리를 알려 준다.</summary>
    public static string? Given =>
        Environment.GetEnvironmentVariable("WAROFGENESIS_ASSETS") is { Length: > 0 } given && Directory.Exists(Path.Combine(given, "characters")) ? given : null;

    /// <summary>실행 파일과 함께 온 <c>assets</c> 폴더 — 없으면 null.</summary>
    public static string? Shipped()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            string assets = Path.Combine(dir.FullName, "assets");
            if (Directory.Exists(Path.Combine(assets, "characters"))) return assets;
        }
        return null;
    }

    /// <summary>이 폴더를 <c>assets</c> 로 쓴다(원본 자료를 차려 놓은 뒤).</summary>
    public static void Use(string assets) => _root = assets;

    public static string Find(string subFolder)
    {
        if (_root != null) return Path.Combine(_root, subFolder);
        if (Given is { } given) return Path.Combine(_root = given, subFolder);
        string shipped = Shipped() ?? throw new DirectoryNotFoundException("assets 폴더(assets/characters)를 못 찾았습니다.");
        if (OriginalAssets.IsComplete(shipped)) return Path.Combine(_root = shipped, subFolder);
        // 배포판 — 전에 차려 둔 것이 있으면 그것(아직 안 차렸으면 실린 폴더: 우리 파일만 있다).
        return Path.Combine(OriginalAssets.IsComplete(OriginalAssets.LocalFolder) ? OriginalAssets.LocalFolder : shipped, subFolder);
    }
}

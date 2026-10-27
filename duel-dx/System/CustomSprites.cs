using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 사용자 그림 — 내 PC 의 <c>%APPDATA%\DuelDx\sprites\</c> 에 둔 전투 그림(<c>번호_이름.obs</c>, 번호 30000~30999)을 캐릭터 에디터 「외형」 줄로 입힌다
/// (사용자 요청 menu-103). 그림은 편집기 「모션 > 사용자 그림 만들기」(CustomSpriteBuilder)나 <c>tools/custom-sprite/sheet_to_obs.py</c> 가 PNG 시트에서 만든다. 릴리즈에 싣지 않는다 — 남의 저작물일 수 있어서.
/// </summary>
/// <remarks>
/// 번호는 파트1 그림 자리(<see cref="Part1Game.ObsBase"/> 위)와 겹치지만 파트1 그림 번호는 10000 을 넘지 않아 따로 가린다. 그림 찾기(<see cref="AssetPack.Fetch"/>)가
/// 이 자리를 먼저 본다. 없는 동작(맞기 · 공격 …)은 게임이 서기로 대신한다.
/// </remarks>
internal static class CustomSprites
{
    internal const int First = 30000, Last = 30999;

    private static string Folder => UserDataFolder.File("sprites");

    /// <summary>
    /// 실린 예제(<c>assets/custom-sprites</c> — 에바)를 사용자 그림 폴더로 한 번 옮긴다 — 게임 「외형」과 편집기 「사용자 그림 만들기」가 같은 폴더를 본다.
    /// 옮긴 이름은 <c>.examples</c> 에 적어, 사용자가 지우면 다시 안 생긴다. 시트(<c>src/</c>)와 설정(.json, 시트 길은 그 폴더 기준)도 같이.
    /// </summary>
    internal static void InstallExamples()
    {
        try
        {
            string src = AssetsFolder.Find("custom-sprites");
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(Folder);
            string marker = Path.Combine(Folder, ".examples");
            var done = File.Exists(marker) ? File.ReadAllLines(marker).ToHashSet() : [];
            foreach (string obs in Directory.EnumerateFiles(src, "*.obs"))
            {
                string stem = Path.GetFileNameWithoutExtension(obs);
                if (!done.Add(stem)) continue;
                File.Copy(obs, Path.Combine(Folder, Path.GetFileName(obs)), true);
                string json = Path.ChangeExtension(obs, ".json");
                if (File.Exists(json)) File.Copy(json, Path.Combine(Folder, Path.GetFileName(json)), true);
                string sheets = Path.Combine(src, "src", stem.Split('_', 2).Last());
                if (Directory.Exists(sheets))
                {
                    string to = Path.Combine(Folder, "src", Path.GetFileName(sheets));
                    Directory.CreateDirectory(to);
                    foreach (string f in Directory.EnumerateFiles(sheets)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
                }
            }
            File.WriteAllLines(marker, done);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>사용자 그림 번호인가.</summary>
    internal static bool Is(int obs) => obs is >= First and <= Last;

    /// <summary>그 파일 이름(<c>NNNNN.obs</c>)의 사용자 그림 자리 — 사용자 그림 번호가 아니거나 파일이 없으면 null.</summary>
    internal static string? PathOf(string file) =>
        file.EndsWith(".obs", StringComparison.OrdinalIgnoreCase) && int.TryParse(Path.GetFileNameWithoutExtension(file), out int n) && Is(n) ? PathOf(n) : null;

    internal static string? PathOf(int obs) => List().FirstOrDefault(s => s.Id == obs).Path;

    /// <summary>폴더의 사용자 그림 — (번호, 이름, 자리). 이름은 파일 이름의 「_」 뒤(없으면 「사용자 그림 번호」).</summary>
    internal static List<(int Id, string Name, string Path)> List()
    {
        try
        {
            if (!Directory.Exists(Folder)) return [];
            return [.. Directory.EnumerateFiles(Folder, "*.obs")
                .Select(f => (File: f, Stem: Path.GetFileNameWithoutExtension(f)))
                .Select(f => (f.File, Head: f.Stem.Split('_', 2)))
                .Where(f => int.TryParse(f.Head[0], out int n) && Is(n))
                .Select(f => (int.Parse(f.Head[0]), f.Head.Length > 1 && f.Head[1].Length > 0 ? f.Head[1] : $"사용자 그림 {f.Head[0]}", f.File))
                .OrderBy(s => s.Item1)];
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }
}

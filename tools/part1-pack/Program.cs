// 파트1(전작) 인물 모습의 릴리즈 에셋 팩을 만든다 — 파트1 이 없는 PC 도 캐릭터 에디터에서 그 모습을 쓰게(사용자 요청 menu-80).
//
//   dotnet run --project tools/part1-pack -- <내보낼 폴더> [파트1 그림 번호 …]   (기본: Part1Game.Shipped 의 인물 그림 모두)
//
// 사용자의 파트1 폴더(Part1Game.Root — 파트2 폴더 옆 「…파트1…」 또는 WAROFGENESIS_PART1)에서 그 그림을 게임과 똑같이 변환하고
// (Part1Game.FilePath), 모션이 부르는 소리·자식 그림(연 따위)까지 따라가 모은다. 내보낼 폴더/assets-part1-1 에 올릴 파일을,
// tools/part1_pack_entries.json 에 pack.json 의 files 줄(tier 90 = 찾을 때만 받는 것)을 쓴다 — make_asset_pack.py 가 이 줄을 덧붙인다.
// Part1Game.ConvertVersion 을 올리면 자리(part1/vN/…)가 바뀌므로 이것을 다시 돌려 올려야 한다.
using System.Security.Cryptography;
using System.Text.Json;
using WarOfGenesis.Assets;

const string Release = "assets-part1-1";
const int OnDemandTier = 90;

if (args.Length < 1) { Console.Error.WriteLine("사용법: part1-pack <내보낼 폴더> [파트1 그림 번호 …]"); return 1; }
if (Part1Game.Root == null) { Console.Error.WriteLine("파트1 폴더를 못 찾았습니다 — WAROFGENESIS_PART1=<폴더> 로 알려 주세요."); return 1; }
string outRoot = Path.GetFullPath(args[0]), cache = Path.Combine(outRoot, "cache"), upload = Path.Combine(outRoot, Release);
Directory.CreateDirectory(upload);
// 인물마다 전투 그림과 Status 얼굴(menu-84) — 그림이 부르는 소리·자식은 아래에서 따라간다.
// 파트1 검 기술의 이펙트 그림과 핸들러가 내는 소리(menu-85)도 함께.
int[] sprites = args.Length > 1 ? [.. args.Skip(1).Select(int.Parse)] : [.. Part1Game.Shipped.SelectMany(p => new[] { (int)p.SpriteId, p.FaceId }), .. Part1Game.SkillObs];
string[] sounds = args.Length > 1 ? [] : [.. Part1Game.SkillSounds.Select(s => $"{Part1Game.ObsBase + s}.wav")];

var todo = new Queue<string>([.. sprites.Select(s => $"{Part1Game.ObsBase + s}.obs"), .. sounds]);
var seen = new HashSet<string>(todo);
var entries = new List<Dictionary<string, object>>();
while (todo.Count > 0)
{
    string file = todo.Dequeue();
    if (Part1Game.FilePath(file, cache) is not { } path) { Console.Error.WriteLine($"{file}: 파트1 폴더에 없습니다 — 건너뜀"); continue; }
    string name = $"part1/{Part1Game.CacheSub(file)}/{file}", asset = name.Replace('/', '_');
    File.Copy(path, Path.Combine(upload, asset), overwrite: true);
    byte[] bytes = File.ReadAllBytes(path);
    entries.Add(new() { ["name"] = name, ["asset"] = asset, ["size"] = bytes.LongLength, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)), ["tier"] = OnDemandTier, ["release"] = Release });
    Console.WriteLine($"{name}  {bytes.Length:N0}");
    if (!file.EndsWith(".obs")) continue;
    foreach (var clip in ObsMotionTable.Parse(bytes)?.Clips.Values ?? [])
        foreach (string next in clip.Sounds.Select(s => $"{s.Sound}.wav").Concat(clip.Children.Select(c => $"{c.Obs}.obs")))
            if (Part1Game.GameNumberOf(next) != null && seen.Add(next)) todo.Enqueue(next);
}

string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string list = Path.Combine(repo, "tools", "part1_pack_entries.json");
File.WriteAllText(list, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{entries.Count}개 → {upload}");
Console.WriteLine($"줄 목록 → {list}");
Console.WriteLine($"올리기: gh release create {Release} --prerelease --latest=false --title \"자산 꾸러미 (파트1)\" --notes \"게임이 받는 파일\" {upload}/*");
return 0;

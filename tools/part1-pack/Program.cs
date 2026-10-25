// 파트1(전작) 그림·소리의 릴리즈 에셋 팩을 만든다 — 파트1 이 없는 PC 도 캐릭터 에디터에서 그 모습 · 기술을 쓰게(사용자 요청 menu-80).
//
//   dotnet run --project tools/part1-pack -- <내보낼 폴더> [파트1 그림 번호 …]   (기본: 아래 목록 모두)
//
// 싣는 것(번호는 파트1 번호):
//   * Part1Game.Shipped 인물의 전투 그림 · 얼굴(menu-84)
//   * tools/part1_needs.json — 게임이 쓰는 목록(모든 파트1 기술의 NeedsFor · 기본 공격 · 소리 표 · 아이콘 · 판 UI). 게임을
//     DUELDX_P1NEEDS=tools/part1_needs.json 으로 한 번 켜서 다시 쓴다(duel-dx/Ability/Part1Skill.PackList.cs).
//   * duel-dx/Ability/Part1Skill*.cs 를 훑은 글자 그대로의 번호 — Sound(N, …) · Put(N, …) · Along(N, …) 따위(N + Rand(k) 는 N ~ N+k−1)
//   * 그림의 모션이 부르는 소리 · 자식 그림(따라간다)
// 사용자의 파트1 폴더(Part1Game.Root — 파트2 폴더 옆 「…파트1…」 또는 WAROFGENESIS_PART1)에서 게임과 똑같이 변환한다(Part1Game.FilePath).
// 내보낼 폴더/<릴리즈> 에 올릴 파일을(릴리즈 하나에 PerRelease 개까지 — GitHub 은 1,000 개가 끝), tools/part1_pack_entries.json 에
// pack.json 의 files 줄(tier 90 = 찾을 때만 받는 것)을 쓴다 — make_asset_pack.py 가 이 줄을 덧붙인다(--part1-only 면 그것만 갈아 끼운다).
// Part1Game.ConvertVersion 을 올리면 자리(part1/vN/…)가 바뀌므로 이것을 다시 돌려 올려야 한다. 릴리즈 이름에 그 판을 넣어(assets-part1-v9-1)
// 옛 판의 파일(옛 게임이 쓰는 것)을 지우지 않고 새 릴리즈에 올린다.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using WarOfGenesis.Assets;

const int OnDemandTier = 90, PerRelease = 900;
string ReleaseOf(int index) => $"assets-part1-v{Part1Game.ConvertVersion}-{1 + index / PerRelease}";

if (args.Length < 1) { Console.Error.WriteLine("사용법: part1-pack <내보낼 폴더> [파트1 그림 번호 …]"); return 1; }
if (Part1Game.Root == null) { Console.Error.WriteLine("파트1 폴더를 못 찾았습니다 — WAROFGENESIS_PART1=<폴더> 로 알려 주세요."); return 1; }
string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string outRoot = Path.GetFullPath(args[0]), cache = Path.Combine(outRoot, "cache");

var spriteSet = new SortedSet<int>();
var soundSet = new SortedSet<int>();
if (args.Length > 1) spriteSet.UnionWith(args.Skip(1).Select(int.Parse));
else
{
    spriteSet.UnionWith(Part1Game.Shipped.SelectMany(p => new[] { (int)p.SpriteId, p.FaceId }));
    spriteSet.UnionWith(Part1Game.SkillObs);
    soundSet.UnionWith(Part1Game.SkillSounds);
    string needs = Path.Combine(repo, "tools", "part1_needs.json");
    if (File.Exists(needs))
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(needs));
        spriteSet.UnionWith(doc.RootElement.GetProperty("obs").EnumerateArray().Select(e => e.GetInt32()));
        soundSet.UnionWith(doc.RootElement.GetProperty("sounds").EnumerateArray().Select(e => e.GetInt32()));
    }
    else Console.Error.WriteLine($"{needs} 이 없습니다 — DUELDX_P1NEEDS={needs} 로 게임을 한 번 켜 만들어 주세요(기술 목록이 빠진다).");
    var (scanObs, scanSounds) = ScanSources(Path.Combine(repo, "duel-dx", "Ability"));
    Console.WriteLine($"소스에서 더 찾은 것 — 그림 {string.Join(" ", scanObs.Except(spriteSet))} · 소리 {string.Join(" ", scanSounds.Except(soundSet))}");
    spriteSet.UnionWith(scanObs);
    soundSet.UnionWith(scanSounds);
}
spriteSet.RemoveWhere(n => n <= 0);
soundSet.RemoveWhere(n => n <= 0);

var todo = new Queue<string>([.. spriteSet.Select(s => $"{Part1Game.ObsBase + s}.obs"), .. soundSet.Select(s => $"{Part1Game.ObsBase + s}.wav")]);
var seen = new HashSet<string>(todo);
var made = new List<(string File, string Path)>();
int missing = 0;
while (todo.Count > 0)
{
    string file = todo.Dequeue();
    if (Part1Game.FilePath(file, cache) is not { } path) { Console.Error.WriteLine($"{file}: 파트1 폴더에 없습니다 — 건너뜀"); missing++; continue; }
    made.Add((file, path));
    if (!file.EndsWith(".obs")) continue;
    foreach (var clip in ObsMotionTable.Parse(File.ReadAllBytes(path))?.Clips.Values ?? [])
        foreach (string next in clip.Sounds.Select(s => $"{s.Sound}.wav").Concat(clip.Children.Select(c => $"{c.Obs}.obs")))
            if (Part1Game.GameNumberOf(next) != null && seen.Add(next)) todo.Enqueue(next);
}

// 그림 먼저, 번호 차례 — 다시 돌려도 같은 파일이 같은 릴리즈에 간다(목록이 늘면 뒤 릴리즈로 밀릴 수 있다).
made.Sort((a, b) => (a.File.EndsWith(".wav"), Part1Game.GameNumberOf(a.File)).CompareTo((b.File.EndsWith(".wav"), Part1Game.GameNumberOf(b.File))));
var entries = new List<Dictionary<string, object>>();
foreach (string dir in Directory.Exists(outRoot) ? Directory.GetDirectories(outRoot, "assets-part1-*") : []) Directory.Delete(dir, true);
for (int i = 0; i < made.Count; i++)
{
    var (file, path) = made[i];
    string release = ReleaseOf(i), name = $"part1/{Part1Game.CacheSub(file)}/{file}", asset = name.Replace('/', '_');
    if (!asset.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '.' or '-')) throw new InvalidDataException($"ASCII 가 아닌 이름: {asset}");
    string upload = Path.Combine(outRoot, release);
    Directory.CreateDirectory(upload);
    File.Copy(path, Path.Combine(upload, asset), overwrite: true);
    byte[] bytes = File.ReadAllBytes(path);
    entries.Add(new() { ["name"] = name, ["asset"] = asset, ["size"] = bytes.LongLength, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)), ["tier"] = OnDemandTier, ["release"] = release });
}

string list = Path.Combine(repo, "tools", "part1_pack_entries.json");
File.WriteAllText(list, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
long total = entries.Sum(e => (long)e["size"]);
Console.WriteLine($"{entries.Count}개 {total / 1048576.0:F1}MB (그림 {made.Count(m => m.File.EndsWith(".obs"))} · 소리 {made.Count(m => m.File.EndsWith(".wav"))}, 못 찾음 {missing}) → {outRoot}");
Console.WriteLine($"줄 목록 → {list}");
foreach (string release in entries.Select(e => (string)e["release"]).Distinct())
    Console.WriteLine($"올리기: gh release create {release} --prerelease --latest=false --title \"자산 꾸러미 (파트1 v{Part1Game.ConvertVersion})\" --notes \"게임이 받는 파일\" {Path.Combine(outRoot, release)}/*");
return 0;

// 기술 핸들러가 글자 그대로 부르는 파트1 번호 — Sound(N, …) 는 소리, 그림을 까는 도우미(Put · Along · Screen …)와 P(N) 은 그림.
// 첫 인자의 정수 글자를 모은다: 「N + Rand(k)」 는 N ~ N+k−1, Rand(…) 안의 수와 rawObs(시전자 그림 그대로) 줄은 뺀다.
static (SortedSet<int> Obs, SortedSet<int> Sounds) ScanSources(string folder)
{
    var obs = new SortedSet<int>();
    var sounds = new SortedSet<int>();
    var call = new Regex(@"(?<![\w.])(Sound|Put|Along|Screen|Rise|Debris|Length|Necro|BigBeam|FireMissile|Leg|S2Burst|Fly|Drift|Fall|P)\(");
    var declared = new Regex(@"(void|int|double|bool|static)\s+$");
    foreach (string path in Directory.GetFiles(folder, "Part1Skill*.cs"))
    {
        string text = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
        foreach (Match m in call.Matches(text))
        {
            if (declared.IsMatch(text[Math.Max(0, m.Index - 12)..m.Index])) continue;
            int open = m.Index + m.Length - 1, depth = 0, end = open, firstComma = -1;
            for (; end < text.Length; end++)
            {
                char c = text[end];
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') { if (--depth == 0) break; }
                else if (c == ',' && depth == 1 && firstComma < 0) firstComma = end;
            }
            string all = text[(open + 1)..end], first = text[(open + 1)..(firstComma < 0 ? end : firstComma)];
            if (all.Contains("rawObs: true")) continue;
            var ids = new List<int>();
            first = Regex.Replace(first, @"(\d+)\s*\+\s*Rand\((\d+)\)", r =>
            {
                ids.AddRange(Enumerable.Range(int.Parse(r.Groups[1].Value), int.Parse(r.Groups[2].Value)));
                return "";
            });
            first = Regex.Replace(first, @"Rand\([^)]*\)\s*(==|!=|<|>|<=|>=|%)?\s*\d*", "");
            ids.AddRange(Regex.Matches(first, @"\b0x[0-9a-fA-F]+\b|(?<![\w.])\d+(?![\w.])").Select(n => n.Value.StartsWith("0x") ? Convert.ToInt32(n.Value, 16) : int.Parse(n.Value)));
            (m.Groups[1].Value == "Sound" ? sounds : obs).UnionWith(ids.Where(n => n > 0));
        }
    }
    return (obs, sounds);
}

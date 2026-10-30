using System.IO;
using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>
/// 파트1(전작) 인물 레코드에서 읽는 것 — 이름(한글), 전투 그림, 얼굴(Status 칸의 작은 것, 50×50 가운데 기준 — 파트2 FaceId 와 같은 갈래),
/// 반신 초상(대사용 큰 그림, 발밑 기준).
/// </summary>
public sealed record Part1Character(int Code, string Name, ushort SpriteId, ushort FaceId, ushort BustId);

/// <summary>
/// 사용자가 가진 <b>창세기전3 파트1</b> 폴더(<c>Obss</c>·<c>Chars</c> 가 든 곳) — 캐릭터 에디터가 전작 인물의 모습을 빌려 쓸 때 읽는다(사용자 요청 menu-76).
/// </summary>
/// <remarks>
/// 게임 본체는 <c>alpha.dll</c>(ImageBase <c>0x10000000</c>)이다. 그림 로더 <c>0x100a6da0</c>(<c>Obss\%04d.obs</c>):
/// <code>
/// u32 몸짓벌 수
/// 벌마다: 머리 0x80 바이트(+0x11 투명 색인, +0x42 u16 장 수) · 장 × 장 수 · RGB 팔레트 768
///   장: u32 자료 크기, u16 너비, u16 높이, i16 x, i16 y, 자료(u16 레코드 수 + 레코드들)
/// u32 모션 수, 모션마다 u32 길이(틱), u32 키 수, 키 18바이트 × 키 수 — i16 시작틱, i16 길이, i16 종류, i16 × 6
/// </code>
/// 장 자료의 RLE(<c>0x100a69d0</c>)는 파트2 와 바이트 하나 안 다르다 — 그래서 파트2 꼴 Obs 로 다시 싸기만 하면(<see cref="ToPart2Obs"/>)
/// 게임의 그림 읽기(<see cref="ObsSprite"/>·<see cref="ObsMotionTable"/>)가 그대로 읽는다. 모션 번호도 파트2 처럼 동작 × 3 + 방향이다.
/// 키 종류는 그림(0)·소리(1)·자식 Obs(2)만 옮긴다 — 소리·자식은 파트1 번호라 <see cref="ObsBase"/> 를 더해 파트1 것을 부르게 한다(마르자나의 연 = 자식 Obs 291).
/// 자식 키의 반전 깃발 자리는 파트1 이 거의 9(안 씀)로 채워 두어 0(주인과 같이 뒤집기)으로 읽는다.
/// 섞기(3)는 <see cref="BlendOf"/> 로 파트2 섞기 번호로 옮긴다. 키 종류 4·9 는 뜻을 몰라 아직 뺀다.
/// 동작 번호는 파트2 와 뜻이 달라(파트1 동작 2 = 공격, 파트2 동작 2 = 맞기) <see cref="ActionMap"/> 으로 바꿔 쓴다.
/// 효과음(<c>Snd</c> 의 <c>NNNN.snd</c>)은 WAV 지만 파트2(8비트 PCM)와 달리 MS ADPCM(형식 2, 44.1kHz 스테레오)이라 게임이 읽는 PCM 으로 푼다. 묶음 색인(<c>Obss.idx</c>·<c>Snd.idx</c>): u32 수, 30바이트 레코드(파일 이름 9 · pak 이름 13 · u32 시작 · u32 끝) — 파트2 와 달리 XOR 이 없다.
/// 인물 레코드 <c>Chars\NNNN.chr</c>(175바이트): 0x00 한글 이름(cp949, XOR 0xFF), 0x18 영문 이름, 0x3a u16 전투 그림, 0x3c u16 반신 초상, 0x3e u16 얼굴.
/// </remarks>
public static class Part1Game
{
    /// <summary>파트1 그림 번호를 파트2 번호와 안 겹치게 옮겨 쓰는 자리 — 파트1 Obs 352 는 게임 안에서 20352 다.</summary>
    public const int ObsBase = 20000;

    /// <summary>
    /// 파트1 인물·전투를 이 게임 안에서 부르는 번호 자리 — 파트1 Chr 387 은 Chr 20387, 파트1 전투 0010 은 전투 20010 이다.
    /// 그림 번호(<see cref="ObsBase"/>)와 같은 값이라 헷갈리지 않는다(파트2 번호는 모두 이보다 작다).
    /// </summary>
    public const int ChrBase = 20000, BattleBase = 20000;

    /// <summary>파트1 인물 이름의 글 번호 자리 — 파트2 글 표(TXR)는 이보다 훨씬 작다. <c>GameDatabase.T</c> 가 이 위를 파트1 이름으로 돌려준다.</summary>
    public const int NameTextBase = 60000;

    /// <summary>파트1 배경음악 번호 자리 — 곡 n(<c>BGMs\NNNN.bgm</c>)은 이 게임에서 BGM 20000 + n 이다(파트2 BGM 번호는 이보다 작다).</summary>
    public const int BgmBase = 20000;

    /// <summary>
    /// 파트1 배경음악 <c>BGMs\NNNN.bgm</c> — 머리가 RIFF WAVE 형식 2(MS ADPCM, 44.1kHz 스테레오)라 <see cref="AdpcmToPcm"/> 로 풀어 읽는다. 없으면 null.
    /// </summary>
    public static PcmSound? Music(int n) =>
        n >= 0 && Read("BGMs", $"{n:D4}.bgm") is { } bytes ? WaveSound.Parse(AdpcmToPcm(bytes)) : null;

    /// <summary>
    /// 곡 n 을 원본이 <b>시디 곡</b>으로 트는가 — 그 시디 트랙 번호(2~), 아니면 null(<c>BGMs\NNNN.bgm</c> 파일 곡).
    /// </summary>
    /// <remarks>
    /// 원본 곡 걸기 <c>0x100094e0(곡, 되풀이)</c> 은 먼저 <c>0x100094a0</c> 으로 시디 표를 뒤진다: 표 <c>0x1011fcac</c> 의 줄 r(30바이트,
    /// r = 꽂힌 시디 <c>[0x1011f2f8]</c>)에서 곡 + 1 이 i 번째 칸에 있으면 <b>트랙 i + 2</b> 를 MCI <c>cdaudio</c> 로 튼다(<c>0x100093f0</c>:
    /// TMSF 로 그 트랙부터 다음 트랙까지, 되풀이는 곡 고리 <c>0x100096f0</c> 이 다시 건다). 표에 없으면 파일 곡(<c>0x10008e00</c>).
    /// 표는 <c>Tracks.ini</c>(<c>0x10008d80</c>: <c>%d</c> 를 −1 이 나올 때까지 줄마다, 여섯 줄)이고 줄 r 은 시디 r 이다 —
    /// r 은 <c>0x10004190</c> 이 시디 뿌리(레지스트리 CDRoot 의 드라이브 글자, 비면 게임 폴더)에서 <c>G3CD_0</c>~<c>G3CD_3</c> 을 찾아 k + 1 로 둔다.
    /// 이 설치본은 게임 폴더에 빈 <c>G3CD_0</c> 이 있어 줄 1 = 「15 29」: 곡 15 → 트랙 2, 곡 29 → 트랙 3. 곡 32 는 어느 줄에도 없다(파일 곡 — <c>0032.bgm</c> 이 없다).
    /// </remarks>
    public static int? CdTrack(int n)
    {
        if (n < 0 || Root is not { } root) return null;
        int disc = -1;
        for (int k = 0; k < 4 && disc < 0; k++)
            if (File.Exists(Path.Combine(root, $"G3CD_{k}"))) disc = k + 1;
        if (disc < 0) return null;
        string[] rows;
        try { rows = File.ReadAllLines(Path.Combine(root, "Tracks.ini")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        // 원본은 줄을 가리지 않고 −1 까지를 한 줄로 센다(fscanf %d) — 줄바꿈이 아니라 −1 로 나눈다.
        var lists = new List<List<int>> { new() };
        foreach (string word in string.Join(' ', rows).Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(word, out int v)) break;
            if (v == -1) { if (lists.Count == 6) break; lists.Add([]); continue; }
            lists[^1].Add(v);
        }
        if (disc >= lists.Count) return null;
        int i = lists[disc].IndexOf(n);
        return i is >= 0 and < 30 ? i + 2 : null;
    }

    /// <summary>
    /// 시디 트랙을 대신하는 파일들(있는 것만, 먼저 쓸 것부터) — 이 설치본의 시디 흉내(<c>_inmm.dll</c>)가 읽는 <c>_inmm.ini</c> 는 줄마다 파일 하나,
    /// 첫 줄이 트랙 2 다(<c>bgm\Track02.mp3</c> · <c>Track03.mp3</c> · <c>Track04.mp3</c>). 그 이름 그대로 → 같은 이름의 .wav · .mp3 · .ogg
    /// (mp3 를 wav 로 바꿔 넣고 ini 는 안 고친 경우 — 제보자: 「wav 로 바꾸면 잘 나온다」) → <c>bgm\TrackNN</c> · 폴더 바로 밑 <c>TrackNN</c>(.wav · .mp3 · .ogg).
    /// </summary>
    public static IReadOnlyList<string> CdTrackFiles(int track)
    {
        var found = new List<string>();
        if (Root is not { } root || track < 2) return found;
        string[] exts = [".wav", ".mp3", ".ogg"];
        void Try(string p) { if (File.Exists(p) && !found.Contains(p, StringComparer.OrdinalIgnoreCase)) found.Add(p); }
        try
        {
            string ini = Path.Combine(root, "_inmm.ini");
            if (File.Exists(ini))
            {
                var lines = File.ReadAllLines(ini).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith(';')).ToList();
                if (track - 2 < lines.Count)
                {
                    string mapped = Path.Combine(root, lines[track - 2]);
                    Try(mapped);
                    foreach (string ext in exts) Try(Path.ChangeExtension(mapped, ext));
                }
            }
            foreach (string dir in (string[])["bgm", ""])
                foreach (string ext in exts)
                    Try(Path.Combine(root, dir, $"Track{track:D2}{ext}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return found;
    }

    /// <summary>「/m0[말하는 이]/m1」 머리의 이름 — 없으면 빈 글.</summary>
    public static string Speaker(string text) =>
        System.Text.RegularExpressions.Regex.Match(text, @"/m0\[([^\]]*)\]/m1") is { Success: true } m ? m.Groups[1].Value : "";

    /// <summary>
    /// 파트1 글(<c>.dlg</c>·<c>.clg</c>·<c>.flg</c>) 표시를 보통 글로 — 「/m0[말하는 이]/m1」 머리는 떼고, 「/n」 은 줄 바꿈,
    /// 나머지 표시(/cN 색 · /dN 쉼 · /tN · /p 기다림 · /sN · /wN · /fN, 짝 없는 /m0 · /m1)는 지운다.
    /// </summary>
    public static string PlainText(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(text, @"/m0\[[^\]]*\]/m1", ""), @"/(?:[cdtpswf]\d*|m[01])", "").Replace("/n", "\n").Trim('\n');

    /// <summary>다시 싸는 셈(<see cref="ToPart2Obs"/>)을 고치면 올린다 — 만들어 둔 파일 자리가 바뀌어 옛 것을 안 쓴다.</summary>
    public const int ConvertVersion = 9;

    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuelDx", "part1root.txt");
    private static string? _root;
    private static bool _searched;
    private static readonly Dictionary<string, Dictionary<string, (string Pak, uint Start, uint End)>> _index = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>그 폴더를 파트1 폴더로 쓰고 적어 둔다(<c>%APPDATA%\DuelDx\part1root.txt</c>) — 사용자가 PART 1 단추에서 골랐을 때.</summary>
    public static void Use(string root)
    {
        _root = root;
        _searched = true;
        _bodySprites = null;
        lock (_index) _index.Clear();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 못 적어도 이번 판은 돈다 */ }
    }

    /// <summary>
    /// 파트1 폴더를 찾아본다 — 압축을 풀어 둔 원본이 흔히 있는 곳(파트2 옆 · 바탕 화면 · 다운로드 · 문서 · Program Files · 드라이브\Games)을 먼저 깊이 4 까지,
    /// 그다음 고정 드라이브마다 깊이 3 까지 훑는다. 시스템 폴더는 건너뛰고 <paramref name="limit"/> 가 지나면 그만둔다. 못 찾으면 null(사용자 요청).
    /// </summary>
    public static string? Scan(TimeSpan limit) => FolderScan.Find(IsValid, Siblings(), limit);

    /// <summary>파트1 폴더처럼 보이나 — <c>Obss\Obss.idx</c> 와 <c>Chars</c> 가 있다.</summary>
    public static bool IsValid(string? root) =>
        root is { Length: > 0 } && File.Exists(Path.Combine(root, "Obss", "Obss.idx")) && Directory.Exists(Path.Combine(root, "Chars"));

    /// <summary>
    /// 파트1 폴더 — <c>WAROFGENESIS_PART1</c>, <c>%APPDATA%\DuelDx\part1root.txt</c>, 그다음 파트2 폴더 옆의 「…파트1…」 폴더. 없으면 null.
    /// </summary>
    public static string? Root
    {
        get
        {
            if (_searched) return _root;
            _searched = true;
            string? saved = null;
            try { saved = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            // WAROFGENESIS_PART1=none 이면 파트1 이 없는 PC 처럼 군다(화면 밖 시험 — 릴리즈 에셋 팩으로 받는 길).
            if (Environment.GetEnvironmentVariable("WAROFGENESIS_PART1") == "none") return _root = null;
            string?[] candidates = [Environment.GetEnvironmentVariable("WAROFGENESIS_PART1"), saved, .. Siblings()];
            return _root = candidates.FirstOrDefault(IsValid);
        }
    }

    private static IEnumerable<string> Siblings()
    {
        if (Path.GetDirectoryName(OriginalGame.Root ?? "") is not { Length: > 0 } parent) return [];
        try { return Directory.EnumerateDirectories(parent).Where(d => Path.GetFileName(d).Contains("파트1") || Path.GetFileName(d).Contains("part1", StringComparison.OrdinalIgnoreCase)).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>파트1 폴더(<c>Obss</c>·<c>Snd</c>)의 파일 하나 — 낱장이 먼저, 없으면 묶음(<c>폴더.idx</c>)에서.</summary>
    public static byte[]? Read(string folder, string name)
    {
        if (Root is not { } root) return null;
        string loose = Path.Combine(root, folder, name);
        try
        {
            if (File.Exists(loose)) return File.ReadAllBytes(loose);
            lock (_index)
            {
                if (!_index.TryGetValue(folder, out var index))
                    _index[folder] = index = ReadIndex(Path.Combine(root, folder, folder + ".idx"));
                if (!index.TryGetValue(name, out var at)) return null;
                using var pak = File.OpenRead(Path.Combine(root, folder, at.Pak));
                var bytes = new byte[at.End - at.Start + 1];
                pak.Position = at.Start;
                pak.ReadExactly(bytes);
                return bytes;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException) { return null; }
    }

    private static Dictionary<string, (string Pak, uint Start, uint End)> ReadIndex(string idx)
    {
        var index = new Dictionary<string, (string, uint, uint)>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(idx)) return index;
        byte[] b = File.ReadAllBytes(idx);
        int count = BitConverter.ToInt32(b, 0);
        for (int i = 0, o = 4; i < count && o + 30 <= b.Length; i++, o += 30)
            index[Text(b, o, 9)] = (Text(b, o + 9, 13), BitConverter.ToUInt32(b, o + 22), BitConverter.ToUInt32(b, o + 26));
        return index;
        static string Text(byte[] b, int o, int n) => Encoding.ASCII.GetString(b, o, Math.Max(0, Array.IndexOf(b, (byte)0, o, n) is var z and >= 0 ? z - o : n));
    }

    /// <summary>
    /// 캐릭터 에디터에 올리는 파트1 인물 — 파트1 이 없는 PC 는 이 표의 레코드를 쓰고 그림·소리는 릴리즈 에셋 팩으로 받는다(<c>tools/part1-pack</c>).
    /// 값은 파트1 <c>Chars\NNNN.chr</c> 그대로(이름 0x00 · 그림 0x3a · 얼굴 0x3e · 반신 0x3c). 마르자나(menu-80)에 주역들을 더했다(menu-81).
    /// </summary>
    public static readonly IReadOnlyList<Part1Character> Shipped =
    [
        new(1, "마르자나", 352, 384, 167), new(0, "살라딘", 196, 478, 158), new(16, "얀 지슈카", 513, 484, 166), new(30, "버몬트 대공", 5, 422, 130),
        new(32, "어윈 록슬리", 213, 380, 157), new(36, "해럴드 깁슨", 280, 466, 137), new(64, "알바티니 데 메디치", 239, 431, 126),
        new(65, "크리스티앙 메디치", 206, 453, 132), new(71, "죠안 카트라이트", 345, 446, 145),
        new(58, "철가면", 605, 449, 141),   // 킹 — 아수라파천무가 이 그림의 모션 132~138 로 분신을 만든다(menu-89)
    ];

    /// <summary>
    /// 세이브의 얼굴 번호 — v0.46.0 까지는 반신 초상(0x3c)을 얼굴로 넣어 Status 칸 밖으로 크게 찍혔다(사용자 요청 menu-84).
    /// 그때 넣은 파트1 반신 번호면 그 인물의 얼굴로 바꾼다.
    /// </summary>
    public static ushort FixFace(ushort face) =>
        face > ObsBase && Shipped.FirstOrDefault(p => ObsBase + p.BustId == face) is { } p ? (ushort)(ObsBase + p.FaceId) : face;

    private static HashSet<int>? _bodySprites;

    /// <summary>파트1 인물 레코드가 전투 그림으로 쓰는 Obs 번호들 — 이것만 동작 번호를 바꾼다(<see cref="ToPart2Obs"/>).</summary>
    public static HashSet<int> BodySprites()
    {
        if (_bodySprites != null) return _bodySprites;
        var set = new HashSet<int>();
        try
        {
            if (Root is { } root)
                foreach (string f in Directory.EnumerateFiles(Path.Combine(root, "Chars"), "*.chr"))
                    if (File.ReadAllBytes(f) is { Length: >= 0x3c } b && BitConverter.ToUInt16(b, 0x3a) is > 0 and var sprite) set.Add(sprite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return _bodySprites = set;
    }

    /// <summary>파트1 인물 레코드 — 없거나 못 읽으면 null.</summary>
    public static Part1Character? Character(int code)
    {
        if (Root is not { } root) return Shipped.FirstOrDefault(p => p.Code == code);
        try
        {
            byte[] b = File.ReadAllBytes(Path.Combine(root, "Chars", $"{code:D4}.chr"));
            if (b.Length < 0x40) return null;
            var name = b.Take(0x18).Select(x => (byte)(x ^ 0xFF)).TakeWhile(x => x != 0).ToArray();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return new Part1Character(code, Encoding.GetEncoding(949).GetString(name), BitConverter.ToUInt16(b, 0x3a), BitConverter.ToUInt16(b, 0x3e), BitConverter.ToUInt16(b, 0x3c));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// 파트1 Obs 를 파트2 꼴로 다시 싼다 — 장 자료(RLE)와 팔레트는 그대로 옮기고, 머리·색인표·모션표만 파트2 배치로 쓴다.
    /// 벌 번호 = 파트1 벌 순번, 장 번호 = 벌 안 순번(파트1 모션 키가 이 둘로 장을 가리킨다).
    /// <paramref name="body"/> 면 인물 전투 그림이라 동작 번호를 파트2 뜻으로 바꾼다(<see cref="ActionMap"/>). 무기 층·이펙트(마르자나의 연 291)는
    /// 주인 모션의 자식 키가 제 모션 번호로 부르므로 번호를 그대로 둔다.
    /// </summary>
    public static byte[] ToPart2Obs(byte[] p1, bool body = true)
    {
        var subs = new List<(byte Trans, byte[] Palette, List<(short W, short H, short X, short Y, byte[] Data)> Frames)>();
        int o = 0;
        int subCount = I32(p1, ref o);
        if (subCount is < 1 or > 512) throw new InvalidDataException($"파트1 몸짓 수가 이상합니다: {subCount}");
        for (int s = 0; s < subCount; s++)
        {
            byte trans = p1[o + 0x11];
            int frameCount = BitConverter.ToUInt16(p1, o + 0x42);
            if (frameCount > 256) throw new InvalidDataException($"파트1 장 수가 이상합니다: {frameCount}");
            o += 0x80;
            var frames = new List<(short, short, short, short, byte[])>();
            for (int f = 0; f < frameCount; f++)
            {
                int size = I32(p1, ref o);
                short w = I16(p1, ref o), h = I16(p1, ref o), x = I16(p1, ref o), y = I16(p1, ref o);
                frames.Add((w, h, x, y, p1[o..(o + size)]));
                o += size;
            }
            subs.Add((trans, p1[o..(o + 768)], frames));
            o += 768;
        }
        var part1 = new List<(int Length, List<short[]> Keys)>();
        int motionCount = o + 4 <= p1.Length ? I32(p1, ref o) : 0;
        for (int m = 0; m < motionCount; m++)
        {
            int length = I32(p1, ref o), keyCount = I32(p1, ref o);
            var keys = new List<short[]>();
            for (int k = 0; k < keyCount; k++)
            {
                var key = new short[9];
                for (int i = 0; i < 9; i++) key[i] = I16(p1, ref o);
                keys.Add(key);
            }
            part1.Add((length, keys));
        }

        // 파트2 동작마다 파트1 의 어느 동작을 쓰나(ActionMap) — 방향 셋을 함께 옮긴다. 키는 그림 · 소리 · 자식 Obs 만 옮기고(머리 주석)
        // 소리·자식 번호는 파트1 번호대로 옮긴다. 파트1 키 종류 5(때리는 틱 — 첫 인자 0·1 은 손, 29·30 은 무기·연 쪽)는 파트2 타격 키(종류 6)로 바꾼다.
        var motions = new List<(int Id, int Length, List<short[]> Keys)>();
        var plan = body ? ActionMap : [.. Enumerable.Range(0, (part1.Count + 2) / 3).Select(a => (a, (a, 0, 0)))];
        foreach (var (action, (from, ability, hits)) in plan)
            for (int dir = 0; dir < 3; dir++)
            {
                if (from * 3 + dir >= part1.Count || part1[from * 3 + dir] is not { Keys.Count: > 0 } src) continue;
                var keys = new List<short[]>();
                int struck = 0;
                foreach (var raw in src.Keys)
                {
                    var key = (short[])raw.Clone();
                    if (key[2] is 1 or 2) key[3] += ObsBase;
                    if (key[2] == 2 && key[8] == 9) key[8] = 0;
                    if (key[2] == 3) (key[3], key[4]) = (BlendOf(key[3], key[4]), 0);
                    if (key[2] is 0 or 1 or 2 || key[2] == 3 && key[3] != 0) keys.Add(key);
                    else if (key[2] == 5 && ability > 0 && struck++ < hits) keys.Add([key[0], 1, 6, (short)ability, 0, 0, 0, 0, 0]);
                }
                motions.Add((action * 3 + dir, src.Length, keys));
            }

        // 파트2 배치: 머리 · 벌 색인표 · 모션 색인표 · 모션들 · 벌들(벌 머리 바로 뒤에 그 벌의 장들 — ObsSprite 가 앞 벌 끝에서 다음 벌을 찾는다).
        using var ms = new MemoryStream();
        using var w2 = new BinaryWriter(ms);
        w2.Write((ushort)0); w2.Write((ushort)subs.Count); w2.Write((ushort)(subs.Count - 1));
        long subTable = ms.Position;
        for (int s = 0; s < subs.Count; s++) { w2.Write((ushort)s); w2.Write(0u); }
        w2.Write((ushort)motions.Count); w2.Write((ushort)motions.Select(m => m.Id).DefaultIfEmpty(0).Max());
        long motionTable = ms.Position;
        foreach (var motion in motions) { w2.Write((ushort)motion.Id); w2.Write(0u); }
        var motionAt = new long[motions.Count];
        for (int m = 0; m < motions.Count; m++)
        {
            motionAt[m] = ms.Position;
            var (id, length, keys) = motions[m];
            w2.Write((ushort)id); w2.Write((ushort)0); w2.Write((ushort)keys.Count); w2.Write((ushort)length); w2.Write((ushort)0);
            foreach (var key in keys)
            {
                w2.Write((ushort)key[2]); w2.Write((ushort)key[0]); w2.Write((ushort)key[1]);
                for (int i = 0; i < 10; i++) w2.Write(i < 6 ? key[3 + i] : (short)0);
            }
        }
        var subAt = new long[subs.Count];
        for (int s = 0; s < subs.Count; s++)
        {
            subAt[s] = ms.Position;
            var (trans, palette, frames) = subs[s];
            w2.Write((ushort)s); w2.Write((ushort)frames.Count); w2.Write((ushort)Math.Max(0, frames.Count - 1)); w2.Write(trans); w2.Write(palette);
            long slotTable = ms.Position;
            for (int f = 0; f < frames.Count; f++) { w2.Write((ushort)f); w2.Write(0u); }
            for (int f = 0; f < frames.Count; f++)
            {
                long at = ms.Position;
                var (fw, fh, fx, fy, data) = frames[f];
                w2.Write((ushort)f); w2.Write((uint)data.Length); w2.Write(fw); w2.Write(fh); w2.Write(fx); w2.Write(fy); w2.Write(data);
                long end = ms.Position;
                ms.Position = slotTable + 6 * f + 2; w2.Write((uint)at); ms.Position = end;
            }
        }
        for (int s = 0; s < subs.Count; s++) { ms.Position = subTable + 6 * s + 2; w2.Write((uint)subAt[s]); }
        for (int m = 0; m < motions.Count; m++) { ms.Position = motionTable + 6 * m + 2; w2.Write((uint)motionAt[m]); }
        w2.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// 파트1 섞기 키(종류 3)의 첫 인자 → 파트2 섞기 번호(0 = 안 옮김). 9 는 검은 바탕의 빛(Obs 0010·0014·0017 · 마르자나의 연 291)이라 가산(17).
    /// 11(검 기술의 얼음 기둥 118 · 연기 173 · 꽃잎 190 — 역시 검은 바탕)은 스크린(12), 10(맞는 빛 101:2·6)은 닷지(10),
    /// 12(번개 터짐 71 — 원본은 뒤집는 섞기)도 스크린(12)으로(menu-89). 13 은 둘째 인자 1~8 이 옅어지는 단계라 파트2 반투명 1~7 로 옮긴다(menu-85 — 눈으로 맞춘 것, 가설). 14 는 뜻을 몰라 뺀다.
    /// </summary>
    private static short BlendOf(short kind, short level) => kind switch
    {
        9 => 17,
        10 => 10,
        11 or 12 => 12,
        13 => (short)Math.Clamp((int)level, 1, 7),
        _ => 0,
    };

    /// <summary>
    /// 파트2 동작 ← 파트1 동작(마르자나 Obs 352 의 그림을 보고 고른 것, 사용자 요청 menu-77). 타격 키 어빌리티(25 접근 · 26 원거리 · 0 없음)와 최대 타 수.
    /// 파트2: 0 서기 · 1 걷기 · 2 맞기·죽기 · 5 공격 준비 · 6 시전 자세 · 7 기 모으기 · 8 기본 공격 · 9 원거리 · 13 2타 · 14 3타 · 15 발동(분석-모션).
    /// 파트1: 0 서기 · 1 걷기 · 2 기본 공격 · 3 원거리 · 4 숨 고르기(54틱) · 5 맞기(떨림 키) · 6 팔짱 · 9 공격 자세 ·
    /// 11~15 연속 공격 2타~6타(타격 키 2·3·4·5·6개 — 아홉 인물이 모두 같다). 전에는 마르자나만 보고 2타 ← 12(3타)·3타 ← 14(5타)로 붙여
    /// 휘두르는 수가 맞는 수보다 많았다(얀 지슈카, 사용자 요청 menu-83).
    /// </summary>
    internal static readonly (int Action, (int From, int Ability, int Hits) Source)[] ActionMap =
    [
        (0, (0, 0, 0)), (1, (1, 0, 0)), (2, (5, 0, 0)), (5, (9, 0, 0)), (6, (6, 0, 0)), (7, (4, 0, 0)),
        (8, (2, 25, 1)), (9, (3, 26, 1)), (13, (11, 25, 2)), (14, (12, 25, 3)), (15, (11, 25, 1)),
        // 4타 · 5타 · 6타(파트1 동작 13~15 — 연 · 참 Lv3~5) — 파트2 에 없는 동작이라 40 을 더한 빈 자리에(53~55).
        (ExtraBase + 13, (13, 25, 4)), (ExtraBase + 14, (14, 25, 5)), (ExtraBase + 15, (15, 25, 6)),
        .. Enumerable.Range(ExtraFrom, 58 - ExtraFrom).Select(a => (ExtraBase + a, (a, 0, 0))),
    ];

    /// <summary>
    /// 파트1 기술이 시전자에게 거는 동작(menu-85 · menu-89) — 동작 16 이상(던지기 17 · 돌진 18 · 시전 24 · 아수라파천무의 132~138 모션 …)은
    /// 파트2 에 없는 것이라 동작 번호에 40 을 더한 비어 있는 자리에 그대로 옮겨 둔다. 파트1 모션 m(48 이상)은 이 게임에서 m + 120 이다.
    /// </summary>
    public const int ExtraFrom = 16, ExtraBase = 40;

    /// <summary>파트1 기술(검 기술 · 마법 · 소환)이 쓰는 이펙트 그림 — 파트1 팩에 함께 싣는다(<c>tools/part1-pack</c>). 자식·소리 키는 따라간다.</summary>
    public static readonly int[] SkillObs =
    [
        395, 37, 39, 897, 101, 768, 787, 118, 190, 173, 767,                       // 블리자드 스탐 · 설화난영참
        55, 49, 91, 50, 503, 230, 959, 75, 72, 379, 76, 67, 10, 36,               // 소환 앞머리 · 헬카이트 · 어비스 네레이드 · 머스펠 헤임
        497, 212, 194, 210, 71, 667, 282, 56, 526, 192,                           // 천수관음 · 타이타니아 · 묵호자
        277, 77, 124, 832, 228, 38, 846, 696, 518, 519, 83, 843, 862,             // 썬더스탐 · 헬 스트라이크 · 아수라파천무
        103, 671, 23, 93, 114, 65, 51, 211,                                       // 헬카이트 · 어비스 네레이드 · 머스펠 헤임(진짜 핸들러, menu-92)
        HitSparkObs,                                                              // 맞은 자리의 타격 불꽃(피해 적용 0x1003df4e)
    ];

    /// <summary>그 기술의 핸들러가 직접 내는 소리(그림 키에 없는 것).</summary>
    public static readonly int[] SkillSounds =
    [
        2026, 2027, 2028, 2029, 2030, 2031, 1934, 70,
        2079, 2080, 2081, 2082, 1935, 2045, 2046, 2047, 2048, 2049, 2050, 2051, 2039, 2040, 2041, 2042, 2052, 2067,
        194, 2025, 2020, 2085, 2086, 2087, 2088, 2089,
        1929, 2066, 2065, 1930, 60, 61, 62, 1931, 1932, 1933, 2003, 2004, 2005,
        DeathSnd,                                                                 // 쓰러질 때(죽음 루틴 0x1003e51d)
    ];

    /// <summary>
    /// 맞은 자리의 타격 불꽃 — 피해 적용(<c>0x1003dc30</c>)이 결과 2(맞음)·피해 &gt; 0 이고 무기로 친 것(선택자 0 · 1 · 3 · 29 · 30)이면
    /// 화면 개체 <c>0x10085e00(Obs 0x31c, rand()%3, 0, 0, z 30, 0, 맞은 이)</c> 를 띄운다(쓰러지면 모션 0). 모션 셋 20~22틱, 섞기 9(더하기), 소리 키 없음.
    /// 파트2 의 Obs 73 과 같은 자리·꼴이다(분석-파트1-전투 「맞는 효과」).
    /// </summary>
    public const int HitSparkObs = 796;

    /// <summary>쓰러질 때 소리 — 죽음 루틴 <c>0x1003e480</c> 이 동작 15(맞기)를 걸고 <c>0x1001a640(0x7f0)</c> 로 낸다. 파트2 의 Snd 106 자리.</summary>
    public const int DeathSnd = 2032;

    /// <summary>
    /// 파트1 그림에 기본 공격 동작(파트1 동작 2 = 모션 6)이 있나 — 다시 싸지 않고(<see cref="ToPart2Obs"/>) 모션표만 훑는다. 캐릭터 에디터 「외형」이
    /// 파트1 인물 모습 수십 개를 고를 때 쓴다(다시 싸면 처음 한 번 몇 초 걸린다). 파트1 폴더가 없거나 못 읽으면 null.
    /// </summary>
    public static bool? HasBasicAttack(int part1Obs)
    {
        if (Root == null || Read("Obss", $"{part1Obs:D4}.obs") is not { } p1) return null;
        try { return RawMotion(p1, 6) is { Count: > 0 }; }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { return false; }
    }

    /// <summary>
    /// 파트1 맞기 동작(동작 5 = 모션 15~17)의 번쩍임·떨림 — 파트1 키 종류 3 의 섞기 <b>14</b>(세기 5 · 15 · 30 · 15 · 5, 색표 = 픽셀 + 세기 × (31 − 픽셀) / 31,
    /// 회색 스크린 = 하얗게 뜸 — 색표 생성기 <c>0x1000b8d0</c> 를 돌려 읽은 값)와 종류 9 의 자리 <b>더하기</b>(<c>0x1009fa6a</c> — +2 −4 +4 … −2, 2픽셀 좌우 떨림).
    /// 그림을 파트2 꼴로 쌀 때(<see cref="ToPart2Obs"/>)는 이 둘을 버려 맞아도 몸이 그대로였다 — 다시 싸면 팩도 새로 올려야 해서(<see cref="ConvertVersion"/>)
    /// 읽은 모션표에 덧입힌다: 파트2 동작 2(모션 6~8)에 물들이기(방식 <see cref="ScreenTint"/>) · 자리 키로. 파트1 폴더가 있으면 그 그림의 키를, 없으면
    /// 인물 244 가운데 195 가 쓰는 표를 쓴다.
    /// </summary>
    public static (List<(int Start, int Mode, int Strength)> Tints, List<(int Start, int X, int Y)> Offsets)? HurtKeys(int part1Obs, int dir)
    {
        if (Root != null)
        {
            if (!BodySprites().Contains(part1Obs) || Read("Obss", $"{part1Obs:D4}.obs") is not { } p1) return null;
            try { return HurtKeysOf(RawMotion(p1, 15 + dir)); }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { return null; }
        }
        var tints = new List<(int, int, int)> { (0, ScreenTint, 5), (1, ScreenTint, 15), (2, ScreenTint, 30), (3, ScreenTint, 15), (4, ScreenTint, 5) };
        var offsets = new List<(int, int, int)>();
        for (int t = 3, x = 0; t <= 12; t++) offsets.Add((t, x += t == 3 ? 2 : t == 12 ? -2 : t % 2 == 0 ? -4 : 4, 0));
        return (tints, offsets);
    }

    /// <summary>
    /// 맞기 몸짓(모션 15 + 방향) 안의 타격 키(종류 5) — 원본은 그 틱에 맞은 이 둘레로 물리 타격을 보낸다(유닛 가상함수 +0xb4 = <c>0x10036130</c>:
    /// 서술 <c>0x1003f3c0(선택자, …, 명중 100)</c> → 범위 <c>0x1008e9f0(모양, …, 크기)</c> 칸마다 <c>0x1009d440</c> 보통 피해). 인자 (선택자, 모양, 크기 4분의 1칸,
    /// 거르기 0 적만 · 1 같은 편 · 2 누구나, 높이 · 시야 [H]). 파트1 인물 그림 가운데 맞기 동작에 이 키가 있는 것은 해파리(Obs 70, Chr 498)뿐 —
    /// 틱 21, (0, 1 십자, 12 = 3칸, 0 적만). 싸서 옮긴 그림(<see cref="ToPart2Obs"/>)은 이 키를 버려(사용자 제보 fg-84) 여기서 따로 읽는다.
    /// 파트1 폴더가 없으면 그 값을 그대로 쓴다.
    /// </summary>
    public static (int Tick, int Selector, int Shape, int Size, int Filter)? HurtStrike(int part1Obs, int dir)
    {
        if (Root == null) return part1Obs == 70 ? (21, 0, 1, 12, 0) : null;
        if (!BodySprites().Contains(part1Obs) || Read("Obss", $"{part1Obs:D4}.obs") is not { } p1) return null;
        try
        {
            return RawMotion(p1, 15 + dir)?.Where(k => k[2] == 5).OrderBy(k => k[0]).Select(k => ((int)k[0], (int)k[3], (int)k[4], (int)k[5], (int)k[6]))
                       .Cast<(int, int, int, int, int)?>().FirstOrDefault();
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { return null; }
    }

    /// <summary>파트1 물들이기 14(회색 스크린)를 파트2 물들이기 방식 번호로 둘 자리 — 파트2 방식은 1~9 라 겹치지 않는다.</summary>
    public const int ScreenTint = 14;

    private static (List<(int Start, int Mode, int Strength)> Tints, List<(int Start, int X, int Y)> Offsets)? HurtKeysOf(List<short[]>? keys)
    {
        if (keys == null) return null;
        var tints = new List<(int, int, int)>();
        var offsets = new List<(int, int, int)>();
        int x = 0, y = 0;
        foreach (var k in keys.OrderBy(k => k[0]))
        {
            if (k[2] == 3 && k[3] == ScreenTint) tints.Add((k[0], ScreenTint, k[4]));
            else if (k[2] == 9) offsets.Add((k[0], x += k[3], y += k[4]));
        }
        return tints.Count + offsets.Count > 0 ? (tints, offsets) : null;
    }

    /// <summary>
    /// 파트1 섞기 키를 파트2 섞기 번호와 안 겹치게 담는 자리 — <c>BlendKeyBase | 방식 &lt;&lt; 5 | 세기</c>(방식 0~30, 세기 0~31).
    /// 파트2 섞기 번호는 모두 이보다 작다. 그리는 쪽(<c>Part1Blend</c>)이 원본 색표 식으로 섞는다(볼트 분석-파트1-전투 「섞기 색표」).
    /// </summary>
    public const int BlendKeyBase = 0x1000;

    /// <summary>파트1 섞기 (방식, 세기) → 담는 값. 원본 그리개(<c>0x1000c4f0</c>)가 안 그리는 값(방식 &gt; 30 · 세기 &gt; 31)은 방식 31(안 그림)로.</summary>
    public static int BlendKey(int mode, int level) =>
        mode is < 0 or > 30 || level is < 0 or > 31 ? BlendKeyBase | 31 << 5 : BlendKeyBase | mode << 5 | level;

    /// <summary>
    /// 다시 싼 파트1 그림(<see cref="ToPart2Obs"/>)의 모션마다 원본 섞기 키(종류 3)를 <see cref="BlendKey"/> 값으로 — 싸면서 (방식, 세기)를 파트2 번호로
    /// 줄여 버린 것(<see cref="BlendOf"/>)을 되살린다. 파트1 폴더가 없으면 null(그때는 <see cref="BlendKeyFromPart2"/> 로 되짚는다).
    /// 인물 그림의 방식 14(맞을 때 하얗게)는 <see cref="HurtKeys"/> 가 물들이기로 덧입히므로 뺀다.
    /// </summary>
    public static Dictionary<int, List<(int Start, int Key)>>? RawBlends(int part1Obs)
    {
        if (Root == null || Read("Obss", $"{part1Obs:D4}.obs") is not { } p1) return null;
        try
        {
            var raw = RawMotions(p1);
            bool body = BodySprites().Contains(part1Obs);
            var plan = body ? ActionMap : [.. Enumerable.Range(0, (raw.Count + 2) / 3).Select(a => (a, (a, 0, 0)))];
            var result = new Dictionary<int, List<(int, int)>>();
            foreach (var (action, (from, _, _)) in plan)
                for (int dir = 0; dir < 3; dir++)
                {
                    if (from * 3 + dir >= raw.Count) continue;
                    var blends = raw[from * 3 + dir].Where(k => k[2] == 3 && !(body && k[3] == ScreenTint))
                                                    .OrderBy(k => k[0]).Select(k => ((int)k[0], BlendKey(k[3], k[4]))).ToList();
                    result[action * 3 + dir] = blends;
                }
            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { return null; }
    }

    /// <summary>
    /// 다시 싼 파트1 그림(<see cref="ToPart2Obs"/>)의 모션마다 자식 키(종류 2)가 주인 방향을 물려받나와 뒤집기 키(종류 10)가 있나 — 싸면서 깃발 9 만 0 으로 바꾸고
    /// 종류 10 은 버렸다. 원본 키 처리(<c>0x10098370</c>): 종류 2 는 깃발 ≠ 0 이면 자식이 주인 방향(+0x4e)을 물려받고 주인이 뒤집혀 있으면(3) x 를 뒤집는다,
    /// 깃발 0 이면 방향 없이(0xff) 만든다. 종류 10(<c>0x1009869c</c>)은 인자 ≠ 0 이면 제 방향을 3(뒤집힘)으로, 0 이면 1 로 둔다.
    /// 충격파 124 는 모션 18 이 이 키로 모션 0(왼쪽 반)을 뒤집은 오른쪽 반이다. 파트1 폴더가 없으면 null(그때는 <see cref="MirroredMotions"/>).
    /// </summary>
    public static Dictionary<int, (bool Mirror, List<bool> Inherit)>? RawChildKeys(int part1Obs)
    {
        if (Root == null || Read("Obss", $"{part1Obs:D4}.obs") is not { } p1) return null;
        try
        {
            var raw = RawMotions(p1);
            var plan = BodySprites().Contains(part1Obs) ? ActionMap : [.. Enumerable.Range(0, (raw.Count + 2) / 3).Select(a => (a, (a, 0, 0)))];
            var result = new Dictionary<int, (bool, List<bool>)>();
            foreach (var (action, (from, _, _)) in plan)
                for (int dir = 0; dir < 3; dir++)
                {
                    if (from * 3 + dir >= raw.Count) continue;
                    var keys = raw[from * 3 + dir];
                    bool mirror = false;
                    foreach (var k in keys.Where(k => k[2] == 10).OrderBy(k => k[0])) mirror = k[3] != 0;
                    result[action * 3 + dir] = (mirror, [.. keys.Where(k => k[2] == 2).Select(k => k[8] != 0)]);
                }
            return result;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { return null; }
    }

    /// <summary>
    /// 파트1 폴더 없이 받은 팩의 그림에서 뒤집기 키(종류 10, 인자 1)가 있던 모션 — 팩은 그 키를 버리고 쌌다. 파트1 Obs 전부를 훑은 것(키가 있는 그림은 이 둘뿐):
    /// 충격파 124 모션 18(모션 0 의 오른쪽 반), 마르자나의 연 291 모션 9~11.
    /// </summary>
    public static readonly IReadOnlyDictionary<int, int[]> MirroredMotions = new Dictionary<int, int[]> { [124] = [18], [291] = [9, 10, 11] };

    /// <summary>
    /// 파트1 폴더 없이 받은 팩의 그림 — 싸면서 남긴 파트2 번호(<see cref="BlendOf"/>)에서 파트1 방식을 되짚는다: 17 → 9, 10 → 10, 12 → 11(12 도 12 로 쌌지만 11 이 훨씬 많다),
    /// 1~7 → 13 의 그 세기(8 은 7 로 · 0 은 1 로 줄어 있다). 그 밖은 그대로(0 = 키 없음).
    /// </summary>
    public static int BlendKeyFromPart2(int key) => key switch
    {
        17 => BlendKey(9, 0),
        10 => BlendKey(10, 0),
        12 => BlendKey(11, 0),
        >= 1 and <= 7 => BlendKey(13, key),
        _ => key,
    };

    /// <summary>파트1 Obs 의 모든 모션 키(9칸).</summary>
    private static List<List<short[]>> RawMotions(byte[] p1)
    {
        int o = 0, subCount = I32(p1, ref o);
        if (subCount is < 1 or > 512) throw new InvalidDataException($"파트1 몸짓 수가 이상합니다: {subCount}");
        for (int s = 0; s < subCount; s++)
        {
            int frameCount = BitConverter.ToUInt16(p1, o + 0x42);
            o += 0x80;
            for (int f = 0; f < frameCount; f++) o += 12 + BitConverter.ToInt32(p1, o);
            o += 768;
        }
        var motions = new List<List<short[]>>();
        int motionCount = o + 4 <= p1.Length ? I32(p1, ref o) : 0;
        for (int m = 0; m < motionCount; m++)
        {
            o += 4;
            int keyCount = I32(p1, ref o);
            var keys = new List<short[]>();
            for (int k = 0; k < keyCount; k++) { var key = new short[9]; for (int i = 0; i < 9; i++) key[i] = I16(p1, ref o); keys.Add(key); }
            motions.Add(keys);
        }
        return motions;
    }

    /// <summary>파트1 Obs 의 모션 하나의 키(9칸: 틱 · 길이 · 종류 · 인자 6) — 없으면 null.</summary>
    private static List<short[]>? RawMotion(byte[] p1, int motion)
    {
        int o = 0, subCount = I32(p1, ref o);
        if (subCount is < 1 or > 512) throw new InvalidDataException($"파트1 몸짓 수가 이상합니다: {subCount}");
        for (int s = 0; s < subCount; s++)
        {
            int frameCount = BitConverter.ToUInt16(p1, o + 0x42);
            o += 0x80;
            for (int f = 0; f < frameCount; f++) o += 12 + BitConverter.ToInt32(p1, o);
            o += 768;
        }
        int motionCount = o + 4 <= p1.Length ? I32(p1, ref o) : 0;
        for (int m = 0; m < motionCount; m++)
        {
            o += 4;
            int keyCount = I32(p1, ref o);
            if (m == motion)
            {
                var keys = new List<short[]>();
                for (int k = 0; k < keyCount; k++) { var key = new short[9]; for (int i = 0; i < 9; i++) key[i] = I16(p1, ref o); keys.Add(key); }
                return keys;
            }
            o += 18 * keyCount;
        }
        return null;
    }

    /// <summary>
    /// 파트1 번호대(<see cref="ObsBase"/> 위) 파일(<c>20352.obs</c> · <c>20026.wav</c>)을 게임이 읽는 꼴로 <paramref name="cacheFolder"/> 에 만들어 둔다 —
    /// 그림은 파트2 꼴로 다시 싸고(<see cref="ToPart2Obs"/>), 소리는 PCM 으로 푼다(<see cref="AdpcmToPcm"/>). 파트1 이 없거나 못 읽으면 null.
    /// </summary>
    public static string? FilePath(string file, string cacheFolder)
    {
        if (GameNumberOf(file) is not { } n) return null;
        bool obs = file.EndsWith(".obs", StringComparison.OrdinalIgnoreCase);
        string path = Path.Combine(cacheFolder, CacheSub(file), file);
        // 파트1 이 없는 PC 는 릴리즈에서 받아 둔 것(assets-part1-1)을 같은 자리에서 쓴다.
        if (File.Exists(path)) return path;
        if (Root == null) return null;
        if (Read(obs ? "Obss" : "Snd", $"{n - ObsBase:D4}.{(obs ? "obs" : "snd")}") is not { } p1) return null;
        try
        {
            byte[] converted = obs ? ToPart2Obs(p1, body: BodySprites().Contains(n - ObsBase)) : AdpcmToPcm(p1);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, converted);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { return null; }
    }

    /// <summary>
    /// 만들어 둔 파일이 <see cref="FilePath"/> 의 캐시 폴더 안에서 놓이는 자리(<c>v5/obs</c> 꼴). 릴리즈 에셋 팩(pack.json 의 <c>part1/v5/…</c>)도 이 자리로 받는다 —
    /// <see cref="ConvertVersion"/> 을 올리면 팩도 새로 만들어 올려야 한다(<c>tools/part1-pack</c>).
    /// </summary>
    public static string CacheSub(string file) =>
        $"v{ConvertVersion}/{(file.EndsWith(".obs", StringComparison.OrdinalIgnoreCase) ? "obs" : "sounds")}";

    /// <summary>파일 이름(<c>20352.obs</c> · <c>20026.wav</c>)이 파트1 그림·소리면 그 게임 안 번호.</summary>
    public static int? GameNumberOf(string file) =>
        (file.EndsWith(".obs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        && int.TryParse(Path.GetFileNameWithoutExtension(file), out int n) && n > ObsBase ? n : null;

    private static readonly int[] AdpcmAdapt = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];

    /// <summary>
    /// MS ADPCM WAV(형식 2)를 16비트 PCM WAV 로 푼다 — 게임의 소리 읽기(<see cref="WaveSound"/>)는 PCM 만 받는다. 이미 PCM 이거나 꼴이 다르면 그대로 돌려준다.
    /// 블록마다 채널별 예측 번호 · 델타 · 앞 두 표본을 읽고, 바이트마다 높은 4비트가 앞 채널이다(스테레오면 왼쪽 · 오른쪽).
    /// </summary>
    public static byte[] AdpcmToPcm(byte[] wav)
    {
        int fmt = -1, data = -1, dataLen = 0;
        for (int o = 12; o + 8 <= wav.Length;)
        {
            string id = Encoding.ASCII.GetString(wav, o, 4);
            int len = BitConverter.ToInt32(wav, o + 4);
            if (id == "fmt ") fmt = o + 8;
            else if (id == "data") { data = o + 8; dataLen = Math.Min(len, wav.Length - data); }
            o += 8 + len + (len & 1);
        }
        if (fmt < 0 || data < 0 || BitConverter.ToUInt16(wav, fmt) != 2) return wav;
        int channels = BitConverter.ToUInt16(wav, fmt + 2), rate = BitConverter.ToInt32(wav, fmt + 4), blockAlign = BitConverter.ToUInt16(wav, fmt + 12);
        int perBlock = BitConverter.ToUInt16(wav, fmt + 18), coefCount = BitConverter.ToUInt16(wav, fmt + 20);
        var coef = new (int C1, int C2)[coefCount];
        for (int i = 0; i < coefCount; i++) coef[i] = (BitConverter.ToInt16(wav, fmt + 22 + 4 * i), BitConverter.ToInt16(wav, fmt + 24 + 4 * i));
        if (channels is < 1 or > 2 || blockAlign < 7 * channels || coefCount == 0) return wav;

        var pcm = new List<short>();
        for (int block = data; block + 7 * channels <= data + dataLen; block += blockAlign)
        {
            int end = Math.Min(block + blockAlign, data + dataLen), p = block;
            var c = new (int C1, int C2)[channels];
            var delta = new int[channels];
            var s1 = new int[channels];
            var s2 = new int[channels];
            for (int ch = 0; ch < channels; ch++) c[ch] = coef[Math.Min(wav[p++], coefCount - 1)];
            for (int ch = 0; ch < channels; ch++, p += 2) delta[ch] = BitConverter.ToInt16(wav, p);
            for (int ch = 0; ch < channels; ch++, p += 2) s1[ch] = BitConverter.ToInt16(wav, p);
            for (int ch = 0; ch < channels; ch++, p += 2) s2[ch] = BitConverter.ToInt16(wav, p);
            for (int ch = 0; ch < channels; ch++) pcm.Add((short)s2[ch]);
            for (int ch = 0; ch < channels; ch++) pcm.Add((short)s1[ch]);
            int left = (perBlock - 2) * channels;
            for (int ch = 0; p < end && left > 0; p++)
                foreach (int nibble in new[] { wav[p] >> 4, wav[p] & 15 })
                {
                    if (left-- <= 0) break;
                    int signed = nibble >= 8 ? nibble - 16 : nibble;
                    int predicted = Math.Clamp((s1[ch] * c[ch].C1 + s2[ch] * c[ch].C2) / 256 + signed * delta[ch], short.MinValue, short.MaxValue);
                    (s2[ch], s1[ch]) = (s1[ch], predicted);
                    delta[ch] = Math.Max(16, delta[ch] * AdpcmAdapt[nibble] / 256);
                    pcm.Add((short)predicted);
                    ch = (ch + 1) % channels;
                }
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int bytes = pcm.Count * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((ushort)1); w.Write((ushort)channels); w.Write(rate); w.Write(rate * channels * 2); w.Write((ushort)(channels * 2)); w.Write((ushort)16);
        w.Write("data"u8); w.Write(bytes);
        foreach (short v in pcm) w.Write(v);
        w.Flush();
        return ms.ToArray();
    }

    private static int I32(byte[] b, ref int o) { int v = BitConverter.ToInt32(b, o); o += 4; return v; }
    private static short I16(byte[] b, ref int o) { short v = BitConverter.ToInt16(b, o); o += 2; return v; }
}

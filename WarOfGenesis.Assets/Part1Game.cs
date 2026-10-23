using System.IO;
using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>파트1(전작) 인물 레코드에서 읽는 것 — 이름(한글), 전투 그림, 초상.</summary>
public sealed record Part1Character(int Code, string Name, ushort SpriteId, ushort FaceId);

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
/// 자식 키의 반전 깃발 자리는 파트1 이 거의 9(안 씀)로 채워 두어 0(주인과 같이 뒤집기)으로 읽는다. 3·4·9 는 뜻이 파트2 와 달라 아직 뺀다.
/// 동작 번호는 파트2 와 뜻이 달라(파트1 동작 2 = 공격, 파트2 동작 2 = 맞기) <see cref="ActionMap"/> 으로 바꿔 쓴다.
/// 효과음(<c>Snd</c> 의 <c>NNNN.snd</c>)은 파트2 처럼 WAV 그대로다. 묶음 색인(<c>Obss.idx</c>·<c>Snd.idx</c>): u32 수, 30바이트 레코드(파일 이름 9 · pak 이름 13 · u32 시작 · u32 끝) — 파트2 와 달리 XOR 이 없다.
/// 인물 레코드 <c>Chars\NNNN.chr</c>(175바이트): 0x00 한글 이름(cp949, XOR 0xFF), 0x18 영문 이름, 0x3a u16 전투 그림, 0x3c u16 초상.
/// </remarks>
public static class Part1Game
{
    /// <summary>파트1 그림 번호를 파트2 번호와 안 겹치게 옮겨 쓰는 자리 — 파트1 Obs 352 는 게임 안에서 20352 다.</summary>
    public const int ObsBase = 20000;

    /// <summary>다시 싸는 셈(<see cref="ToPart2Obs"/>)을 고치면 올린다 — 만들어 둔 파일 자리가 바뀌어 옛 것을 안 쓴다.</summary>
    public const int ConvertVersion = 3;

    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuelDx", "part1root.txt");
    private static string? _root;
    private static bool _searched;
    private static readonly Dictionary<string, Dictionary<string, (string Pak, uint Start, uint End)>> _index = new(StringComparer.OrdinalIgnoreCase);

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
        if (Root is not { } root) return null;
        try
        {
            byte[] b = File.ReadAllBytes(Path.Combine(root, "Chars", $"{code:D4}.chr"));
            if (b.Length < 0x3e) return null;
            var name = b.Take(0x18).Select(x => (byte)(x ^ 0xFF)).TakeWhile(x => x != 0).ToArray();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return new Part1Character(code, Encoding.GetEncoding(949).GetString(name), BitConverter.ToUInt16(b, 0x3a), BitConverter.ToUInt16(b, 0x3c));
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
        // 소리·자식 번호는 파트1 번호대로 옮긴다. 파트1 키 종류 5 중 첫 인자 0·1 인 것(연을 내지르는 틱)은 파트2 타격 키(종류 6)로 바꾼다.
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
                    if (key[2] is 0 or 1 or 2) keys.Add(key);
                    else if (key[2] == 5 && key[3] is 0 or 1 && ability > 0 && struck++ < hits) keys.Add([key[0], 1, 6, (short)ability, 0, 0, 0, 0, 0]);
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
    /// 파트2 동작 ← 파트1 동작(마르자나 Obs 352 의 그림을 보고 고른 것, 사용자 요청 menu-77). 타격 키 어빌리티(25 접근 · 26 원거리 · 0 없음)와 최대 타 수.
    /// 파트2: 0 서기 · 1 걷기 · 2 맞기·죽기 · 5 공격 준비 · 6 시전 자세 · 7 기 모으기 · 8 기본 공격 · 9 원거리 · 13 2타 · 14 3타 · 15 발동(분석-모션).
    /// 파트1: 0 서기 · 1 걷기 · 2·3 연 던지기 · 4 숨 고르기(54틱) · 5 맞기(떨림 키) · 6 팔짱 · 9 연 든 자세 · 11 연 던지기 · 12 2타 · 14 3타.
    /// </summary>
    internal static readonly (int Action, (int From, int Ability, int Hits) Source)[] ActionMap =
    [
        (0, (0, 0, 0)), (1, (1, 0, 0)), (2, (5, 0, 0)), (5, (9, 0, 0)), (6, (6, 0, 0)), (7, (4, 0, 0)),
        (8, (2, 25, 1)), (9, (3, 26, 1)), (13, (12, 25, 2)), (14, (14, 25, 3)), (15, (11, 25, 1)),
    ];

    /// <summary>
    /// 파트1 번호대(<see cref="ObsBase"/> 위) 파일(<c>20352.obs</c> · <c>20026.wav</c>)을 게임이 읽는 꼴로 <paramref name="cacheFolder"/> 에 만들어 둔다 —
    /// 그림은 파트2 꼴로 다시 싸고(<see cref="ToPart2Obs"/>), 소리는 그대로 옮긴다. 파트1 이 없거나 못 읽으면 null.
    /// </summary>
    public static string? FilePath(string file, string cacheFolder)
    {
        if (GameNumberOf(file) is not { } n || Root == null) return null;
        bool obs = file.EndsWith(".obs", StringComparison.OrdinalIgnoreCase);
        string path = Path.Combine(cacheFolder, $"v{ConvertVersion}", obs ? "obs" : "sounds", file);
        if (File.Exists(path)) return path;
        if (Read(obs ? "Obss" : "Snd", $"{n - ObsBase:D4}.{(obs ? "obs" : "snd")}") is not { } p1) return null;
        try
        {
            byte[] converted = obs ? ToPart2Obs(p1, body: BodySprites().Contains(n - ObsBase)) : p1;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, converted);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { return null; }
    }

    /// <summary>파일 이름(<c>20352.obs</c> · <c>20026.wav</c>)이 파트1 그림·소리면 그 게임 안 번호.</summary>
    public static int? GameNumberOf(string file) =>
        (file.EndsWith(".obs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        && int.TryParse(Path.GetFileNameWithoutExtension(file), out int n) && n > ObsBase ? n : null;

    private static int I32(byte[] b, ref int o) { int v = BitConverter.ToInt32(b, o); o += 4; return v; }
    private static short I16(byte[] b, ref int o) { short v = BitConverter.ToInt16(b, o); o += 2; return v; }
}

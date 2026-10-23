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
/// 키 종류는 아직 그림(0)만 옮긴다 — 소리(1)·자식 Obs(2)는 파트1 번호라 파트2 것을 잘못 부르고, 4·5 는 뜻이 파트2 와 다르다.
/// 묶음 색인 <c>Obss.idx</c>: u32 수, 30바이트 레코드(파일 이름 9 · pak 이름 13 · u32 시작 · u32 끝) — 파트2 와 달리 XOR 이 없다.
/// 인물 레코드 <c>Chars\NNNN.chr</c>(175바이트): 0x00 한글 이름(cp949, XOR 0xFF), 0x18 영문 이름, 0x3a u16 전투 그림, 0x3c u16 초상.
/// </remarks>
public static class Part1Game
{
    /// <summary>파트1 그림 번호를 파트2 번호와 안 겹치게 옮겨 쓰는 자리 — 파트1 Obs 352 는 게임 안에서 20352 다.</summary>
    public const int ObsBase = 20000;

    private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuelDx", "part1root.txt");
    private static string? _root;
    private static bool _searched;
    private static readonly Dictionary<string, (string Pak, uint Start, uint End)> _index = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary><c>Obss</c> 의 파일 하나 — 낱장이 먼저, 없으면 묶음에서.</summary>
    public static byte[]? ReadObs(int obs)
    {
        if (Root is not { } root) return null;
        string name = $"{obs:D4}.obs", loose = Path.Combine(root, "Obss", name);
        try
        {
            if (File.Exists(loose)) return File.ReadAllBytes(loose);
            lock (_index)
            {
                if (_index.Count == 0) ReadIndex(Path.Combine(root, "Obss", "Obss.idx"));
                if (!_index.TryGetValue(name, out var at)) return null;
                using var pak = File.OpenRead(Path.Combine(root, "Obss", at.Pak));
                var bytes = new byte[at.End - at.Start + 1];
                pak.Position = at.Start;
                pak.ReadExactly(bytes);
                return bytes;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException) { return null; }
    }

    private static void ReadIndex(string idx)
    {
        byte[] b = File.ReadAllBytes(idx);
        int count = BitConverter.ToInt32(b, 0);
        for (int i = 0, o = 4; i < count && o + 30 <= b.Length; i++, o += 30)
            _index[Text(b, o, 9)] = (Text(b, o + 9, 13), BitConverter.ToUInt32(b, o + 22), BitConverter.ToUInt32(b, o + 26));
        static string Text(byte[] b, int o, int n) => Encoding.ASCII.GetString(b, o, Math.Max(0, Array.IndexOf(b, (byte)0, o, n) is var z and >= 0 ? z - o : n));
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
    /// </summary>
    public static byte[] ToPart2Obs(byte[] p1)
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
        var motions = new List<(int Length, List<short[]> Keys)>();
        int motionCount = o + 4 <= p1.Length ? I32(p1, ref o) : 0;
        for (int m = 0; m < motionCount; m++)
        {
            int length = I32(p1, ref o), keyCount = I32(p1, ref o);
            var keys = new List<short[]>();
            for (int k = 0; k < keyCount; k++)
            {
                var key = new short[9];
                for (int i = 0; i < 9; i++) key[i] = I16(p1, ref o);
                if (key[2] == 0) keys.Add(key);   // 그림 키만 — 머리 주석
            }
            motions.Add((length, keys));
        }

        // 파트2 배치: 머리 · 벌 색인표 · 모션 색인표 · 모션들 · 벌들(벌 머리 바로 뒤에 그 벌의 장들 — ObsSprite 가 앞 벌 끝에서 다음 벌을 찾는다).
        using var ms = new MemoryStream();
        using var w2 = new BinaryWriter(ms);
        w2.Write((ushort)0); w2.Write((ushort)subs.Count); w2.Write((ushort)(subs.Count - 1));
        long subTable = ms.Position;
        for (int s = 0; s < subs.Count; s++) { w2.Write((ushort)s); w2.Write(0u); }
        w2.Write((ushort)motions.Count); w2.Write((ushort)Math.Max(0, motions.Count - 1));
        long motionTable = ms.Position;
        for (int m = 0; m < motions.Count; m++) { w2.Write((ushort)m); w2.Write(0u); }
        var motionAt = new long[motions.Count];
        for (int m = 0; m < motions.Count; m++)
        {
            motionAt[m] = ms.Position;
            var (length, keys) = motions[m];
            w2.Write((ushort)m); w2.Write((ushort)0); w2.Write((ushort)keys.Count); w2.Write((ushort)length); w2.Write((ushort)0);
            foreach (var key in keys)
            {
                w2.Write((ushort)0); w2.Write((ushort)key[0]); w2.Write((ushort)key[1]);
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
    /// 게임 안 그림 번호(<see cref="ObsBase"/> 위)의 파트2 꼴 Obs 파일 — 처음 부를 때 <paramref name="cacheFolder"/> 에 만들어 둔다. 파트1 이 없거나 못 읽으면 null.
    /// </summary>
    public static string? ObsPath(int gameObs, string cacheFolder)
    {
        if (gameObs <= ObsBase || Root == null) return null;
        string path = Path.Combine(cacheFolder, $"{gameObs}.obs");
        if (File.Exists(path)) return path;
        if (ReadObs(gameObs - ObsBase) is not { } p1) return null;
        try
        {
            byte[] converted = ToPart2Obs(p1);
            Directory.CreateDirectory(cacheFolder);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, converted);
            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException) { return null; }
    }

    /// <summary>파일 이름(<c>20352.obs</c>)이 파트1 그림이면 그 게임 안 번호.</summary>
    public static int? GameObsOf(string file) =>
        file.EndsWith(".obs", StringComparison.OrdinalIgnoreCase) && int.TryParse(Path.GetFileNameWithoutExtension(file), out int n) && n > ObsBase ? n : null;

    private static int I32(byte[] b, ref int o) { int v = BitConverter.ToInt32(b, o); o += 4; return v; }
    private static short I16(byte[] b, ref int o) { short v = BitConverter.ToInt16(b, o); o += 2; return v; }
}

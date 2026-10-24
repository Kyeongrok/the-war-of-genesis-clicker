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

    /// <summary>다시 싸는 셈(<see cref="ToPart2Obs"/>)을 고치면 올린다 — 만들어 둔 파일 자리가 바뀌어 옛 것을 안 쓴다.</summary>
    public const int ConvertVersion = 8;

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
    ];

    /// <summary>그 기술의 핸들러가 직접 내는 소리(그림 키에 없는 것).</summary>
    public static readonly int[] SkillSounds =
    [
        2026, 2027, 2028, 2029, 2030, 2031, 1934, 70,
        2079, 2080, 2081, 2082, 1935, 2045, 2046, 2047, 2048, 2049, 2050, 2051, 2039, 2040, 2041, 2042, 2052, 2067,
        194, 2025, 2020, 2085, 2086, 2087, 2088, 2089,
        1929, 2066, 2065, 1930, 60, 61, 62, 1931, 1932, 1933, 2003, 2004, 2005,
    ];

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

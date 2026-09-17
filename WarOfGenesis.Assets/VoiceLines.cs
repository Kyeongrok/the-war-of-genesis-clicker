using System.IO;
using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>음성 파일 하나에 붙는 대사 — 말하는 이, 대사가 든 곳(스크립트·Tlk 줄), 대사 글.</summary>
public sealed record VoiceLine(string Speaker, string Source, string Text);

/// <summary>
/// 음성(<c>BGM/NNNN.bgm</c> 모노) 번호 → 말하는 이·대사. 번호는 식으로 셈하지 않고 이벤트 스크립트의 대사 명령에 적혀 있다 —
/// 전투 <c>Btl/NNNN.btl</c>, 필드 <c>Fld/NNNN.fld</c> 의 동작(18바이트: u16 명령 + 16바이트 인자) 중 대사 명령이
/// (말하는 이, Tlk 줄, 음성 번호)를 함께 갖는다. 대사 글은 그 스크립트와 같은 번호의 <c>Tlk/NNNN.tlb</c>(전투)·<c>.tlf</c>(필드).
/// </summary>
/// <remarks>
/// <para>
/// 대사 명령(인자 자리는 u16 칸 번호): Btl 600(대사 상자 <c>0x10053680</c>) 말하는 이 0 · 줄 2 · 음성 3.
/// Fld 600/601/602(<c>0x100eeef0</c>/<c>0x100ef1e0</c>/<c>0x100ef540</c>) 말하는 이 0 · 줄 1 · 음성 2, Fld 603(해설) 줄 0 · 음성 1,
/// Fld 609 말하는 이 0 · 이름 Txr 1 · 줄 2 · 음성 3. 음성 ≤ 0 이면 목소리 없음. 음성은 <c>0x100f5170</c> 이 <c>Bgm\%04d.bgm</c> 으로 튼다.
/// 스크립트 소리 명령 500/501 은 인자 0 이 음성(대사 글 없음).
/// </para>
/// <para>
/// 말하는 이: 10000 미만 = Chr 번호, 10000 이상 = 그 스크립트의 배치 유닛(Btl 은 A 레코드 no, Fld 는 유닛 id) → 그 Chr,
/// Btl 20000 이상 = 실행 중에 정해짐. 이름 = Txr[Chr +2]. 대사 길이와 음성 길이 상관 r = 0.946(3003줄), 번호를 ±1 밀면 0.03 — 분석-사운드 노트.
/// </para>
/// </remarks>
public static class VoiceLines
{
    public const string SourceNote = "Btl/Fld 이벤트 스크립트의 대사 명령(600번대)에 적힌 음성 번호로 이었다.";

    private sealed record Unit(int No, int Chr);

    private sealed record Script(IReadOnlyList<Unit> Units, IReadOnlyList<(short Op, short[] W)> Actions);

    public static IReadOnlyDictionary<int, VoiceLine> Load(GameFiles files)
    {
        var result = new Dictionary<int, VoiceLine>();
        var txrBytes = files.Read("TXR", "Txr.dat");
        if (txrBytes == null) return result;
        var txr = TxrTable.Parse(txrBytes);
        var chrNames = new Dictionary<int, string>();

        string ChrName(int code)
        {
            if (chrNames.TryGetValue(code, out var n)) return n;
            var b = files.Read("Chr", $"{code:D4}.chr");
            n = b is { Length: >= 4 } ? txr.TextOf(BitConverter.ToUInt16(b, 2)) : "";
            return chrNames[code] = n.Length > 0 ? n : $"Chr {code}";
        }

        foreach (var (kind, ext, tlkExt) in new[] { ("Btl", ".btl", "tlb"), ("Fld", ".fld", "tlf") })
        {
            foreach (var name in files.List(kind, ext).Keys)
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(name), out int id)) continue;
                Script? script;
                try
                {
                    var data = files.Read(kind, name);
                    script = data == null ? null : kind == "Btl" ? ParseBtl(data) : ParseFld(data);
                }
                catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { script = null; }
                if (script == null) continue;   // 몇 백 바이트짜리 빈 전투 파일 10개는 틀이 안 맞는다

                Dictionary<int, string>? tlk = null;
                string src = $"{kind} {id:D4}";

                foreach (var (op, w) in script.Actions)
                {
                    if (op is 500 or 501)
                    {
                        if (w[0] > 0 && !result.ContainsKey(w[0])) result[w[0]] = new VoiceLine("", $"{src} 소리 명령 {op}", "");
                        continue;
                    }
                    int? speakerSlot, textSlot, voiceSlot;
                    (speakerSlot, textSlot, voiceSlot) = (kind, op) switch
                    {
                        ("Btl", 600) => (0, 2, 3),
                        ("Fld", 600 or 601 or 602) => (0, 1, 2),
                        ("Fld", 603) => (null, 0, 1),
                        ("Fld", 609) => (0, 2, 3),
                        _ => ((int?)null, (int?)null, (int?)null),
                    };
                    if (voiceSlot is not int vs || textSlot is not int ts) continue;
                    int voice = w[vs];
                    if (voice <= 0) continue;

                    tlk ??= ReadTlk(files.Read("Tlk", $"{id:D4}.{tlkExt}"));
                    int line = w[ts];
                    string text = tlk.GetValueOrDefault(line, "").Replace("$n", " ");

                    string speaker;
                    if (speakerSlot is not int ss) speaker = "(해설)";
                    else if (kind == "Fld" && op == 609 && txr.TextOf((ushort)w[1]) is { Length: > 0 } label) speaker = label;
                    else speaker = Speaker(w[ss], kind, script.Units, ChrName);

                    // 같은 번호를 두 스크립트가 쓰면(10개, 대개 같은 줄) 먼저 찾은 것. 소리 명령으로만 찾은 것은 대사로 덮는다.
                    if (result.TryGetValue(voice, out var old) && old.Text.Length > 0) continue;
                    result[voice] = new VoiceLine(speaker, $"{src} · {id:D4}.{tlkExt} #{line}", text);
                }
            }
        }
        return result;
    }

    private static string Speaker(int s, string kind, IReadOnlyList<Unit> units, Func<int, string> chrName)
    {
        if (s > 0 && s < 10000) return chrName(s);
        if (s >= 10000 && (kind == "Fld" || s < 20000))
        {
            var unit = units.FirstOrDefault(u => u.No == s - 10000);
            return unit != null ? chrName(unit.Chr) : $"유닛 {s - 10000}";
        }
        return s >= 20000 ? "(실행 중 결정)" : "?";
    }

    /// <summary>Tlk 글 표: u16 0 · u16 개수 n · u16 최대 번호 · u32 글 크기, 0x0A 부터 n×(u16 번호, u32 위치, u32 길이), 글은 (n+1)×10 부터.</summary>
    public static Dictionary<int, string> ReadTlk(byte[]? d)
    {
        var map = new Dictionary<int, string>();
        if (d == null || d.Length < 10) return map;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        int n = BitConverter.ToUInt16(d, 2), textBase = (n + 1) * 10;
        for (int k = 0; k < n && 10 + k * 10 + 10 <= d.Length; k++)
        {
            int o = 10 + k * 10;
            int index = BitConverter.ToUInt16(d, o);
            long off = textBase + (long)BitConverter.ToUInt32(d, o + 2), len = BitConverter.ToUInt32(d, o + 6);
            if (off >= d.Length) continue;
            var raw = d.AsSpan((int)off, (int)Math.Min(len, d.Length - off));
            int zero = raw.IndexOf((byte)0);
            map[index] = cp949.GetString(zero >= 0 ? raw[..zero] : raw);
        }
        return map;
    }

    private ref struct Reader(byte[] data)
    {
        private readonly byte[] _d = data;
        public int Pos;
        public short H() { if (Pos + 2 > _d.Length) throw new InvalidDataException(); short v = BitConverter.ToInt16(_d, Pos); Pos += 2; return v; }
        public void Skip(int n) { if (Pos + n > _d.Length) throw new InvalidDataException(); Pos += n; }
        public (short, short[]) Action()
        {
            short op = H();
            var w = new short[8];
            for (int i = 0; i < 8; i++) w[i] = H();
            return (op, w);
        }
    }

    private static int Count(short v) => v & 0xFFFF;

    /// <summary>Btl: 머리 u16×10 · (nA, u16) + nA×29 · (nB, u16) + nB×22 · (nC, u16) + nC×12 · 이벤트 수 · 이벤트(u16, u16, 조건 수 + ×18, 동작 수 + ×18).</summary>
    private static Script ParseBtl(byte[] b)
    {
        var r = new Reader(b);
        r.Skip(20);
        int na = Count(r.H()); r.H();
        var units = new List<Unit>();
        for (int i = 0; i < na; i++)
        {
            int no = r.H(), chr = r.H();
            r.Skip(25);
            units.Add(new Unit(no, chr));
        }
        int nb = Count(r.H()); r.H(); r.Skip(22 * nb);
        int nc = Count(r.H()); r.H(); r.Skip(12 * nc);
        var actions = new List<(short, short[])>();
        int nd = r.H();
        for (int i = 0; i < nd; i++)
        {
            r.H(); r.H();
            int conds = r.H(); r.Skip(18 * Math.Max(0, conds));
            int acts = r.H();
            for (int k = 0; k < acts; k++) actions.Add(r.Action());
        }
        return new Script(units, actions);
    }

    /// <summary>Fld: 머리 u16×6 · (n1, u16) + n1×16 · (n2, u16) + n2×16 · (n3, u16) + n3×10(유닛 id, Chr, …) · 이벤트 수 · 이벤트(u16, 조건 수 + ×18, 동작 수 + ×18).</summary>
    private static Script ParseFld(byte[] b)
    {
        var r = new Reader(b);
        r.Skip(12);
        int n1 = r.H(); r.H(); r.Skip(16 * Math.Max(0, n1));
        int n2 = r.H(); r.H(); r.Skip(16 * Math.Max(0, n2));
        int n3 = r.H(); r.H();
        var units = new List<Unit>();
        for (int i = 0; i < n3; i++)
        {
            int id = r.H(), chr = r.H();
            r.Skip(6);
            units.Add(new Unit(id, chr));
        }
        var actions = new List<(short, short[])>();
        int ne = r.H();
        for (int i = 0; i < ne; i++)
        {
            r.H();
            int conds = r.H(); r.Skip(18 * Math.Max(0, conds));
            int acts = r.H();
            for (int k = 0; k < acts; k++) actions.Add(r.Action());
        }
        return new Script(units, actions);
    }
}

using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 게임 자료 파일 읽기 — 게임 폴더(낱장 우선, 없으면 <c>.idx/.pak</c> 에서 메모리로)나 저장소 <c>assets/data</c>
/// (폴더 구조를 그대로 둔 낱장들) 어느 쪽에서든 <c>(폴더, 파일)</c> 로 꺼낸다. 게임 폴더에는 쓰지 않는다.
/// </summary>
public sealed class GameFiles
{
    private readonly string _root;
    private readonly bool _pak;
    private readonly Dictionary<string, Dictionary<string, (string Pak, int Start, int End)>> _index = [];

    private GameFiles(string root, bool pak) { _root = root; _pak = pak; }

    public static GameFiles FromGameRoot(string root) => new(root, pak: true);
    public static GameFiles FromFolder(string folder) => new(folder, pak: false);

    public byte[]? Read(string folder, string name)
    {
        string loose = Path.Combine(_root, folder, name);
        if (File.Exists(loose)) return File.ReadAllBytes(loose);
        if (!_pak) return null;

        if (!_index.TryGetValue(folder, out var map))
        {
            map = new(StringComparer.OrdinalIgnoreCase);
            string dir = Path.Combine(_root, folder);
            if (Directory.Exists(dir))
            {
                foreach (var idx in Directory.EnumerateFiles(dir, "*.idx"))
                {
                    byte[] d = File.ReadAllBytes(idx);
                    int total = (int)(BitConverter.ToUInt32(d, 0) / 0x10000);
                    for (int i = 0, o = 6; i < total && o + 36 <= d.Length; i++, o += 36)
                    {
                        string fn = XorName(d, o + 14, 9), pak = XorName(d, o + 23, 13);
                        map[fn] = (pak, (int)BitConverter.ToUInt32(d, o + 2), (int)BitConverter.ToUInt32(d, o + 8));
                    }
                }
            }
            _index[folder] = map;
        }
        if (!map.TryGetValue(name, out var ent)) return null;
        using var f = File.OpenRead(Path.Combine(_root, folder, ent.Pak));
        f.Seek(ent.Start, SeekOrigin.Begin);
        var buf = new byte[ent.End - ent.Start + 1];
        f.ReadExactly(buf);
        return buf;
    }

    private static string XorName(byte[] d, int o, int n)
    {
        var bytes = new List<byte>();
        for (int i = 0; i < n; i++)
        {
            byte b = (byte)(d[o + i] ^ 0xFF);
            if (b == 0) break;
            bytes.Add(b);
        }
        return System.Text.Encoding.ASCII.GetString([.. bytes]);
    }
}

/// <summary><c>Chr/NNNN.chr</c> 90바이트 전체 (<c>CChr.Load 0x10031530</c> 읽는 순서).</summary>
public sealed record CharacterData(
    int Code, ushort NameId, ushort Name2Id, ushort SpriteId, ushort FaceId, ushort TitleId,
    byte Body, ushort JobId, ushort BasicWorkId, ushort Level, uint Lp,
    ushort Psy, ushort Tp, ushort TpDivisor, ushort Ctp, ushort Dep, ushort Dex,
    ushort[] Items, (ushort Ability, ushort Level)[] Abilities)
{
    public static CharacterData? Parse(int code, byte[]? b)
    {
        if (b == null || b.Length != 90) return null;
        ushort U(int o) => BitConverter.ToUInt16(b, o);
        return new CharacterData(code, U(2), U(4), U(8), U(10), U(12), b[14], U(15), U(19), U(21), BitConverter.ToUInt32(b, 23),
            U(27), U(29), U(31), U(33), U(35), U(37),
            [.. Enumerable.Range(0, 6).Select(i => U(42 + 2 * i))],
            [.. Enumerable.Range(0, 8).Select(i => (U(56 + 4 * i), U(58 + 4 * i))).Where(a => a.Item1 != 0)]);
    }
}

/// <summary><c>Dat/Job.dat</c> 레코드(파일 67바이트). 53 오프셋 이름 6칸 = 체질(0 무속성 … 5 메텔)별 직업 이름.</summary>
public sealed record JobData(int Id, ushort[] Growth, ushort[] AbilityList, ushort[] NamesByBody, ushort DescriptionId);

/// <summary><c>Dat/Dep.dat</c> 레코드 — 직업 묶음(계열 이름, 단계, 직업 번호들).</summary>
public sealed record DepData(int Id, ushort NameId, byte Tier, ushort[] Jobs);

/// <summary><c>Dat/Itm.dat</c> 레코드(파일 48바이트). 종류 0 VES … 14 일반검, 15 대검 …; <see cref="Bonuses"/> = (능력치 번호, 값).</summary>
public sealed record ItemData(int Id, ushort NameId, uint Price, byte Type, ushort Attack, ushort Defense, (ushort Stat, ushort Value)[] Bonuses);

/// <summary><c>Abi/NNNN.abi</c> 레코드(26바이트)와 레벨별 work 번호.</summary>
public sealed record AbilityData(int Id, ushort NameId, ushort MaxLevel, Dictionary<int, int> WorkByLevel);

/// <summary><c>Dat/NNNN.att</c> work 레코드(62바이트) 중 쓰는 칸. 파일 오프셋: 41 +0x2e, 43 +0x30 EXP 비용, 45 +0x32 TP, 47 +0x34 SOUL, 57 +0x3f 준비 동작.</summary>
public sealed record WorkData(int Id, ushort AbilityId, byte Level, ushort HpFactor, ushort ExpCost, ushort TpBase, ushort SoulBase, byte Prepare);

/// <summary>
/// 캐릭터 화면에 필요한 게임 표 묶음 — 텍스트(TXR), 직업·계열·아이템·어빌리티·work·수치(Num) — 와 능력치 식.
/// </summary>
/// <remarks>
/// 식은 <c>G3PartII.dll</c> 에서 옮겼다(옵시디안 분석-캐릭터·분석-전투). 전투 유닛 쪽 버프(+0x4c8 …)와
/// 어빌리티 패시브 보너스(<c>0x10032af0</c>)는 넣지 않았다 — 파일 기본값 + 장비 보너스까지만이다.
/// </remarks>
public sealed class GameDatabase
{
    public TxrTable Text { get; }
    public IReadOnlyDictionary<int, JobData> Jobs { get; }
    public IReadOnlyList<DepData> Deps { get; }
    public IReadOnlyDictionary<int, ItemData> Items { get; }
    public IReadOnlyDictionary<int, AbilityData> Abilities { get; }
    public IReadOnlyDictionary<int, WorkData> Works { get; }
    public IReadOnlyDictionary<int, int> Num { get; }
    private readonly GameFiles _files;

    private GameDatabase(GameFiles files, TxrTable text, Dictionary<int, JobData> jobs, List<DepData> deps,
                         Dictionary<int, ItemData> items, Dictionary<int, AbilityData> abilities,
                         Dictionary<int, WorkData> works, Dictionary<int, int> num)
    {
        _files = files; Text = text; Jobs = jobs; Deps = deps; Items = items; Abilities = abilities; Works = works; Num = num;
    }

    public static readonly int[] WorkFiles = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12];
    public static readonly int[] AbilityFiles = [12, 14, 15, 16, 17, 18, 19, 20, 26];

    public static GameDatabase Load(GameFiles files)
    {
        byte[] Need(string folder, string name) =>
            files.Read(folder, name) ?? throw new FileNotFoundException($"{folder}\\{name} 을(를) 못 찾았습니다.");

        var text = TxrTable.Parse(Need("TXR", "Txr.dat"));

        var jobs = new Dictionary<int, JobData>();
        byte[] d = Need("Dat", "Job.dat");
        for (int i = 0, n = U16(d, 2), o = 6; i < n; i++, o += 67)
            jobs[U16(d, o)] = new JobData(U16(d, o), Words(d, o + 7, 12), Words(d, o + 31, 11), Words(d, o + 53, 6), U16(d, o + 65));

        var deps = new List<DepData>();
        d = Need("Dat", "Dep.dat");
        for (int i = 0, n = U16(d, 4), o = 6; i < n; i++, o += 33)
            deps.Add(new DepData(U16(d, o), U16(d, o + 2), d[o + 4],
                [.. Enumerable.Range(0, 7).Select(k => U16(d, o + 5 + 4 * k)).Where(v => v != 0)]));

        var items = new Dictionary<int, ItemData>();
        d = Need("Dat", "itm.dat");
        for (int i = 0, n = U16(d, 2), o = 6; i < n; i++, o += 48)
        {
            var w = Words(d, o + 18, 15);
            var bonuses = Enumerable.Range(0, 7).Select(k => (w[2 * k], w[2 * k + 1])).Where(p => p.Item1 != 0).ToArray();
            items[U16(d, o)] = new ItemData(U16(d, o), U16(d, o + 2), BitConverter.ToUInt32(d, o + 4), d[o + 8],
                                            U16(d, o + 11), U16(d, o + 13), bonuses);
        }

        var works = new Dictionary<int, WorkData>();
        foreach (int f in WorkFiles)
        {
            if (files.Read("Dat", $"{f:D4}.att") is not { } a) continue;
            for (int i = 0, n = U16(a, 2), o = 6; i < n; i++, o += 62)
                works[U16(a, o)] = new WorkData(U16(a, o), U16(a, o + 2), a[o + 4], U16(a, o + 41), U16(a, o + 43),
                                                U16(a, o + 45), U16(a, o + 47), a[o + 57]);
        }

        var abilities = new Dictionary<int, AbilityData>();
        foreach (int f in AbilityFiles)
        {
            if (files.Read("Abi", $"{f:D4}.abi") is not { } a) continue;
            for (int i = 0, n = U16(a, 2), o = 6; i < n; i++, o += 26)
                abilities[U16(a, o)] = new AbilityData(U16(a, o), U16(a, o + 2), U16(a, o + 4), []);
        }
        foreach (var w in works.Values)
            if (w.AbilityId != 0 && w.Level != 0 && abilities.TryGetValue(w.AbilityId, out var ab))
                ab.WorkByLevel[w.Level] = w.Id;

        var num = new Dictionary<int, int>();
        d = Need("Dat", "Num.dat");
        for (int i = 0, n = U16(d, 2), o = 6; i < n; i++, o += 6)
            num[U16(d, o)] = BitConverter.ToInt32(d, o + 2);

        return new GameDatabase(files, text, jobs, deps, items, abilities, works, num);
    }

    public CharacterData? Character(int code) => CharacterData.Parse(code, _files.Read("Chr", $"{code:D4}.chr"));

    public string T(ushort id) => Text.TextOf(id);

    // ── 이름 ─────────────────────────────────────────────────────────────────

    /// <summary>체질 이름: 0 무속성(162), 1~5 = TXR 151~155(에텔체·맨탈체·아스트럴체·코절체·메텔체).</summary>
    public string BodyName(byte body) => body switch
    {
        0 => T(162),
        >= 1 and <= 5 => T((ushort)(150 + body)),
        _ => "",
    };

    /// <summary>직업 이름 = Job[직업].이름[체질].</summary>
    public string JobName(CharacterData c) =>
        Jobs.TryGetValue(c.JobId, out var j) && c.Body < 6 ? T(j.NamesByBody[c.Body]) : "";

    /// <summary>계열 이름 = 직업 번호를 품은 Dep 레코드의 이름.</summary>
    public string FamilyName(CharacterData c) =>
        Deps.FirstOrDefault(d => d.Jobs.Contains(c.JobId)) is { } dep ? T(dep.NameId) : "";

    // ── 능력치 식 ────────────────────────────────────────────────────────────

    public int N(int index) => Num.GetValueOrDefault(index);

    /// <summary>장비 보너스 합(<c>0x10032c60</c>): 아이템 (능력치 번호, 값) 짝 중 그 번호. 0x30 HP, 0x1f PSY, 0x1e DEX, 0x21 TP, 0x25 SOUL.</summary>
    public int EquipBonus(CharacterData c, int stat) =>
        c.Items.Where(i => i != 0 && Items.ContainsKey(i)).SelectMany(i => Items[i].Bonuses).Where(b => b.Stat == stat).Sum(b => b.Value);

    public int MaxHp(CharacterData c) => (int)c.Lp + EquipBonus(c, 0x30);
    public int Psy(CharacterData c) => c.Psy + EquipBonus(c, 0x1f);
    public int Dex(CharacterData c) => c.Dex + EquipBonus(c, 0x1e);
    public int MaxTp(CharacterData c) => c.Tp + EquipBonus(c, 0x21);
    public int Stp(CharacterData c) => c.TpDivisor == 0 ? 0 : MaxTp(c) / c.TpDivisor;
    public int SoulStart => N(20);
    public int MaxSoul(CharacterData c) => N(19) + EquipBonus(c, 0x25);

    /// <summary>ACR <c>0x1007ab90</c> = (2×CTP + 최대TP + 현재TP) / Num[9] + DEX / Num[8].</summary>
    public int Acr(CharacterData c, int currentTp) => (2 * c.Ctp + MaxTp(c) + currentTp) / N(9) + Dex(c) / N(8);

    /// <summary>ATK <c>0x1007ab20</c> = ((무기 공격 + Num[1]) × PSY / Num[25]) × (SOUL + Num[2]) × Num[42] / Num[85].</summary>
    public int Atk(CharacterData c, int soul)
    {
        int weapon = c.Items[0] != 0 && Items.TryGetValue(c.Items[0], out var w) ? w.Attack : 0;
        int v = (weapon + N(1)) * Psy(c) / N(25);
        return v * (soul + N(2)) * N(42) / N(85);
    }

    /// <summary>RDP <c>0x1007abf0</c> — HP 가 가득이면 DEP 그대로다(HP 가 줄면 커진다; 줄었을 때 식은 아직 안 옮김).</summary>
    public int RdpAtFullHp(CharacterData c) => c.Dep;

    /// <summary>무기 종류 이름(장비 1번 칸 아이템 종류).</summary>
    public static string WeaponTypeName(byte type) => type switch
    {
        0 => "VES", 8 => "Ribbon", 9 => "Yoyo", 10 => "Camera", 11 => "Claw", 12 => "Gun",
        13 => "Cigar", 14 => "Sword", 15 => "Great Sword", 17 => "Poison", 19 => "Weapon", _ => "",
    };

    private static ushort U16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
    private static ushort[] Words(byte[] b, int o, int n) => [.. Enumerable.Range(0, n).Select(i => U16(b, o + 2 * i))];
}

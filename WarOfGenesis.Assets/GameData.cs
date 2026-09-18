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

    public byte[]? Read(string folder, string name) => ReadCore(folder, name, int.MaxValue);

    /// <summary>파일 앞 <paramref name="count"/> 바이트까지만 읽는다(머리만 볼 때 — 목록을 채울 때 쓴다).</summary>
    public byte[]? ReadHead(string folder, string name, int count) => ReadCore(folder, name, count);

    /// <summary>폴더에 든 파일 이름과 크기 — 낱장과 <c>.idx</c> 색인을 합친 것(같은 이름이면 낱장 크기).</summary>
    public IReadOnlyDictionary<string, long> List(string folder, string extension)
    {
        var result = new SortedDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (_pak)
            foreach (var (fn, ent) in IndexOf(folder))
                if (fn.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) result[fn] = ent.End - ent.Start + 1;
        string dir = Path.Combine(_root, folder);
        if (Directory.Exists(dir))
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*" + extension)) result[f.Name] = f.Length;
        return result;
    }

    private byte[]? ReadCore(string folder, string name, int maxBytes)
    {
        string loose = Path.Combine(_root, folder, name);
        if (File.Exists(loose))
        {
            if (maxBytes == int.MaxValue) return File.ReadAllBytes(loose);
            using var lf = File.OpenRead(loose);
            var head = new byte[(int)Math.Min(maxBytes, lf.Length)];
            lf.ReadExactly(head);
            return head;
        }
        if (!_pak) return null;

        if (!IndexOf(folder).TryGetValue(name, out var ent)) return null;
        using var f = File.OpenRead(Path.Combine(_root, folder, ent.Pak));
        f.Seek(ent.Start, SeekOrigin.Begin);
        var buf = new byte[Math.Min(ent.End - ent.Start + 1, maxBytes)];
        f.ReadExactly(buf);
        return buf;
    }

    private Dictionary<string, (string Pak, int Start, int End)> IndexOf(string folder)
    {
        lock (_index)
        {
            if (_index.TryGetValue(folder, out var map)) return map;
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
            return map;
        }
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
    /// <summary>Status 화면 "WEAPON ○○" 띠 그림(파일 40, <c>CChr+0x48</c>) — Obs 0326 모션. 0 이면 62.</summary>
    public byte WeaponBand { get; init; }

    /// <summary>무기 종류(파일 39, <c>CChr+0x4a</c>) — 무기 칸에는 이 종류 아이템만 낀다(<c>0x10032d70</c>).</summary>
    public byte WeaponType { get; init; }

    /// <summary>장착 어빌리티 칸(<c>CChr+0x142</c>, 최대 3) — 패시브(분류 3) 어빌리티 번호, 0 은 빈 칸. .chr 파일에는 없어 처음엔 비어 있다.</summary>
    public ushort[] Passives { get; init; } = [0, 0, 0];

    /// <summary>쌓인 경험치(<c>CChr+0x2e</c>) — 레벨 = 이 값 ÷ 100. .chr 파일에는 없다.</summary>
    public int CumExp { get; init; }

    /// <summary>목소리 묶음 번호(파일 6) — <c>Dat/Dmg.dat</c> 레코드(맞을 때·차례 부르는 목소리).</summary>
    public ushort VoiceSet { get; init; }

    /// <summary>EXP(<c>CChr+0x30</c>) — 어빌리티를 올리고 배우는 데 쓴다. .chr 파일에는 없다.</summary>
    public int Exp { get; init; }

    public static CharacterData? Parse(int code, byte[]? b)
    {
        if (b == null || b.Length != 90) return null;
        ushort U(int o) => BitConverter.ToUInt16(b, o);
        return new CharacterData(code, U(2), U(4), U(8), U(10), U(12), b[14], U(15), U(19), U(21), BitConverter.ToUInt32(b, 23),
            U(27), U(29), U(31), U(33), U(35), U(37),
            [.. Enumerable.Range(0, 6).Select(i => U(42 + 2 * i))],
            [.. Enumerable.Range(0, 8).Select(i => (U(56 + 4 * i), U(58 + 4 * i))).Where(a => a.Item1 != 0)])
        {
            WeaponType = b[39],
            WeaponBand = b[40],
            VoiceSet = U(6),
        };
    }

    public int AbilityLevel(int abilityId) => Abilities.FirstOrDefault(a => a.Ability == abilityId).Level;

    /// <summary>
    /// 그 어빌리티를 <b>가지고 있나</b> — 원본은 칸이 비었는지(<c>0xff</c>)로 보지 레벨로 보지 않는다(<c>0x100324e0</c>).
    /// 레벨 0 으로 가진 것도 「가진 것」이라 다시 배울 수 없다.
    /// </summary>
    public bool HasAbility(int abilityId) => Abilities.Any(a => a.Ability == abilityId);
}

/// <summary><c>Dat/Job.dat</c> 레코드(파일 67바이트). 53 오프셋 이름 6칸 = 체질(0 무속성 … 5 메텔)별 직업 이름.</summary>
/// <param name="NeedLevel">파일 2(메모리 <c>+4</c>) — 이 직업으로 바꾸려면 있어야 하는 레벨. 전직 화면이 보는 <b>유일한</b> 조건이다(<c>0x100f9aae</c>).</param>
/// <param name="NeedAbility">파일 4(메모리 <c>+6</c>) — 3단계 직업이 요구하는 어빌리티 번호. <b>가지고만 있으면 된다</b>(<c>0x100fa1b4</c>).</param>
/// <param name="NeedAbilityLevel">파일 6(메모리 <c>+8</c>) — 자료에는 다 5 인데 <b>엔진이 한 번도 안 읽는다</b>.</param>
public sealed record JobData(int Id, ushort[] Growth, ushort[] AbilityList, ushort[] NamesByBody, ushort DescriptionId,
                             ushort NeedLevel = 0, ushort NeedAbility = 0, byte NeedAbilityLevel = 0);

/// <summary><c>Dat/Dep.dat</c> 레코드 — 직업 묶음(계열 이름, 단계, 직업 번호들).</summary>
public sealed record DepData(int Id, ushort NameId, byte Tier, ushort[] Jobs);

/// <summary>
/// <c>Dat/Sta.dat</c> 레코드(머리 6바이트 + 18바이트 × 45) — 상태이상 번호마다 아이콘과 설명글.
/// </summary>
/// <param name="Icon">
/// 파일 +2 — 상태이상 칸에 그릴 <c>Obs 0489</c> 모션 번호. <b>0 이면 빈 칸(모션 0 이 곧 「EMPTY」 판이다)</b>.
/// 번호 44·45·46(「없음」)과 30~33·37·39·48 이 0 이다.
/// </param>
/// <param name="DescriptionId">파일 +5(짝수 자리가 아니다) — 설명 TXR. 32~70 이 대부분이고 41·42·43·47 만 835·850·851·2066 이다.</param>
/// <remarks>
/// 배치는 자료로 확정했다 — 설명글이 [[분석-전투]] 의 「번호 → 뜻」 표와 한 줄도 어긋나지 않고(예: 2 = 화염, 41 = 반격),
/// 아이콘 번호 0~31 이 <c>Obs 0489</c> 의 장 번호 0~31 과 딱 맞는다. 파일 +4 한 바이트(0/1)는 아직 뜻을 모른다.
/// </remarks>
public sealed record StatusData(int Id, ushort Icon, ushort DescriptionId);

/// <summary>
/// <c>Dat/Itm.dat</c> 레코드(파일 48바이트, 메모리 56바이트). 종류 0 VES … 14 일반검, 15 대검 …
/// <para>
/// 로더 <c>0x1004b120</c> 가 칸마다 한 번씩 읽는 것을 그대로 옮겼다. 파일에는 <b>(번호, 값) 짝이 두 무리</b> 있는데 뜻이 전혀 다르다 —
/// <b>18/22/26 · 20/24/28 이 장비 보정</b>(<see cref="Bonuses"/>, 메모리 <c>+0x18</c>·<c>+0x1e</c>, 읽는 곳 <c>0x10032c60</c>)이고,
/// <b>30/34/38 · 32/36/40 이 기본공격이 거는 상태이상</b>(<see cref="AttackEffects"/>, 메모리 <c>+0x24</c>·<c>+0x2a</c>, 읽는 곳 <c>0x1007bdd1</c>)이다.
/// 무리마다 <b>셋</b>이고 값은 <b>부호 있는 수</b>다(리볼버발렌타인의 LP −10 처럼).
/// 파일 15·44 는 엔진이 한 번도 안 읽는 죽은 칸이다.
/// </para>
/// </summary>
/// <param name="Picture">
/// 파일 9 — 아이템 그림(<c>Obs 0326</c> 모션 번호). <c>0xffff</c> 면 <see cref="Type"/> 를 그림 번호로 쓴다(분석-캐릭터 "아이템 그림").
/// </param>
public sealed record ItemData(int Id, ushort NameId, uint Price, byte Type, ushort Attack, ushort Defense,
                              (ushort Stat, short Value)[] Bonuses, ushort Picture = 0xFFFF,
                              ushort UseWork = 0, ushort DescriptionId = 0, ushort Range = 0,
                              (byte Status, short Value)[]? AttackEffects = null)
{
    /// <summary>전투에서 쓸 수 있는 소모품인가 — 종류 7(캡슐)이고 쓰는 work 이 있는 것(분석-전투 「전투 중 아이템 쓰기」).</summary>
    public bool IsConsumable => Type == 7 && UseWork != 0;

    /// <summary>실제로 그릴 Obs 0326 모션 번호.</summary>
    public int PictureMotion => Picture == 0xFFFF ? Type : Picture;
}

/// <summary>
/// <c>Abi/NNNN.abi</c> 레코드(26바이트)와 레벨별 work 번호. 파일 오프셋: 6 선행1·8 그 레벨, 9 선행2·11 그 레벨,
/// 12 분류(0 전투 밖, 1 전투, 2 군단기, 3 패시브), 18·20·22 목록 아이콘(A·B·C), 24 설명 TXR.
/// </summary>
/// <param name="IconSide">18(메모리 +0x16) 아이콘 색 무리 — 0 아군(파랑)·1 적(빨강)·2 피아무관(금색). 0xFFFF 면 아이콘 없음. 색 뜻은 가설.</param>
/// <param name="IconArea">20(+0x18) 0 단일·1 광역 — 가설.</param>
/// <param name="IconKind">22(+0x1a) 0 攻·1 回·2 異·3 軍(군단기, 확정)·4 必.</param>
public sealed record AbilityData(int Id, ushort NameId, ushort MaxLevel, Dictionary<int, int> WorkByLevel,
                                 ushort Prereq1 = 0, byte Prereq1Level = 0, ushort Prereq2 = 0, byte Prereq2Level = 0,
                                 byte Category = 0, ushort DescriptionId = 0,
                                 ushort IconSide = 0xFFFF, ushort IconArea = 0, ushort IconKind = 0)
{
    public bool IsPassive => Category == 3;

    /// <summary>목록 왼쪽 아이콘(종류) — Obs 0488 모션 <c>A×10 + C</c>. 아이콘이 없으면 −1.</summary>
    public int IconKindMotion => IconSide == 0xFFFF ? -1 : IconSide * 10 + IconKind;

    /// <summary>목록 오른쪽 아이콘(대상) — Obs 0488 모션 <c>A×10 + B + 5</c>. 아이콘이 없으면 −1.</summary>
    public int IconTargetMotion => IconSide == 0xFFFF ? -1 : IconSide * 10 + IconArea + 5;
}

/// <summary>
/// <c>Dat/NNNN.att</c> work 레코드(62바이트) 중 쓰는 칸. 이름 옆은 메모리 칸(파일 오프셋).
/// </summary>
/// <param name="RangeKind">+0x8(6) 사거리 종류 — 0 없음, 1·3 자기 값, <b>2 무기 사거리</b>(Itm 파일 16 ×4), 4 무기 + 자기 값.</param>
/// <param name="HeightRange">+0xf(12) 사거리 거리에 높이 차를 더한다.</param>
/// <param name="Sight">+0x10(13) 시야 — 사이에 더 높은 칸이 있으면 못 겨눈다.</param>
/// <param name="SameHeightRange">+0x11(14) 같은 높이 칸만.</param>
/// <param name="HeightGraded">+0x12(15) 높이 차등 — 올려치기 +3/층, 내려치기 −2/층(끄면 +2×|층차|).</param>
/// <param name="HeightArea">+0x16(19) 효과 범위 거리에 높이 차를 더한다.</param>
/// <param name="SameHeightArea">+0x18(21) 효과 범위: 같은 높이 칸만.</param>
/// <param name="RangeShape">+0x7(5) 사거리 모양 1~9 — 1 마름모, 2 십자, 3 부채꼴, 4 화면 전체, 5 직선, 6 폭3 줄, 7 폭5 줄, 8 대각선 X, 9 45° 삼각형. 0 = 자기 자리.</param>
/// <param name="RangeMin">+0xa(7) 사거리 최소 — <b>4분의 1칸 단위</b>(로더: 값 ? 값×4−3 : 0).</param>
/// <param name="RangeMax">+0xc(9) 사거리 최대 — 4분의 1칸 단위(로더: 값×4). 한 칸 = 4.</param>
/// <param name="TargetMode">+0x13(16) 대상 방식 — 0·2 자기 자리, 1 적, 3·6 아무 칸, 4 아군, 5 아무 유닛, 7 빈 칸, 8 오브젝트.</param>
/// <param name="AreaShape">+0x14(17) 효과 범위 모양 1~9(사거리와 같은 표).</param>
/// <param name="AreaArg">+0x1a(22) 효과 범위 최대 — 칸 수(거리로는 ×4).</param>
/// <param name="AreaMin">+0x1c(24) 효과 범위 최소 — c×(4c−3).</param>
/// <param name="AreaMode">+0x1e(26) 효과 범위 안에서 누구를 맞히나(대상 방식과 같은 값).</param>
/// <param name="Kind">+0x1f(27) 0 피해, 1·5 회복, 2·3 보조(HP 변화 없음), 4 오브젝트.</param>
/// <param name="Power">+0x2a(37) 피해면 공격력 ×(200+값)/2000, 회복이면 최대 HP %.</param>
/// <param name="Accuracy">+0x2c(39) 명중 바탕.</param>
/// <param name="Critical">+0x2d(40) 치명 확률 − 1 (%).</param>
/// <param name="HpFactor">+0x2e(41) TP 비용 체질 항.</param>
/// <param name="ExpCost">+0x30(43) 다음 레벨 EXP.</param>
/// <param name="TpBase">+0x32(45) TP 비용.</param>
/// <param name="SoulBase">+0x34(47) SOUL 비용.</param>
/// <param name="MinTargets">+0x3d(55) AI 가 쓸 만하다고 보는 최소 대상 수 − 1(분석-전투 ba-11).</param>
/// <param name="AiCriterion">+0x3e(56) AI 의 칸 점수 기준: 값&gt;&gt;1 = 0 현재HP·1 SOUL·2 ATK·3 TP·4 RDP·5 ACR·6 잃은HP·7 세력점수·8 위험도, 짝수 = 최댓값·홀수 = 최솟값.</param>
/// <param name="Prepare">+0x3f(57) 준비 동작 종류.</param>
/// <param name="Bonuses">+0x20/+0x24 … (파일 28/29, 31/32, 34/35) (능력치 번호, 값) 세 짝 — 패시브 보너스(<c>0x10032af0</c>).</param>
public sealed record WorkData(int Id, ushort AbilityId, byte Level, byte RangeShape, ushort RangeMin, ushort RangeMax,
                              byte TargetMode, byte AreaShape, short AreaArg, byte Kind, short Power, byte Accuracy, byte Critical,
                              ushort HpFactor, ushort ExpCost, ushort TpBase, ushort SoulBase, byte MinTargets, byte AiCriterion, byte Prepare,
                              (byte Stat, short Value)[] Bonuses, int AreaMin = 0, byte AreaMode = 0,
                              byte RangeKind = 0, byte HeightRange = 0, byte Sight = 0, byte SameHeightRange = 0,
                              byte HeightGraded = 0, byte HeightArea = 0, byte SameHeightArea = 0, byte SoulSpend = 0)
{
    public bool IsDamage => Kind == 0;
    public bool IsHeal => Kind is 1 or 5;

    /// <summary>겨냥 없이 자기 자리에 쓰는 work(모드 0·2) — 크래쉬 봄처럼 자기를 가운데로 터진다.</summary>
    public bool SelfCentred => TargetMode is 0 or 2;

    /// <summary>사거리 최소·최대(4분의 1칸)로 고친 값.</summary>
    public int RangeMinQuarters => RangeMin == 0 ? 0 : RangeMin * 4 - 3;
    public int RangeMaxQuarters => RangeMax * 4;

    /// <summary>효과 범위 최대·최소(4분의 1칸).</summary>
    public int AreaMaxQuarters => AreaArg * 4;
    public int AreaMinQuarters => AreaMin * (4 * AreaMin - 3);
}

/// <summary>
/// 캐릭터 화면에 필요한 게임 표 묶음 — 텍스트(TXR), 직업·계열·아이템·어빌리티·work·수치(Num) — 와 능력치 식.
/// </summary>
/// <remarks>
/// 식은 <c>G3PartII.dll</c> 에서 옮겼다(옵시디안 분석-캐릭터·분석-전투). 파일 기본값 + 장비 보너스 + 장착 어빌리티(패시브) 보너스까지다.
/// 전투 유닛 쪽 버프(+0x4c8 …)는 넣지 않았다.
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
    /// <summary><c>Dat/Sta.dat</c> — 상태이상 번호 → 아이콘·설명.</summary>
    public IReadOnlyDictionary<int, StatusData> Statuses { get; }
    /// <summary><c>Dat/Lev.dat</c> — 레벨마다 붙는 성장률(%). 색인은 레벨−1.</summary>
    public IReadOnlyList<LevelGrowth> LevelGrowth => _levelGrowth ??= WarOfGenesis.Assets.LevelGrowth.Parse(_files.Read("Dat", "Lev.dat"));

    /// <summary><c>Dat/0002.nch</c> — 파티 레벨 성장을 안 받는 Chr 번호들.</summary>
    public IReadOnlySet<int> LevelExempt => _levelExempt ??= WarOfGenesis.Assets.LevelGrowth.ParseExempt(_files.Read("Dat", "0002.nch"));

    private IReadOnlyList<LevelGrowth>? _levelGrowth;
    private HashSet<int>? _levelExempt;

    /// <summary>이 표들을 읽은 자료 묶음 — 다른 파일(Dmg.dat 등)을 더 읽을 때 쓴다.</summary>
    public GameFiles Files => _files;

    private readonly GameFiles _files;

    private GameDatabase(GameFiles files, TxrTable text, Dictionary<int, JobData> jobs, List<DepData> deps,
                         Dictionary<int, ItemData> items, Dictionary<int, AbilityData> abilities,
                         Dictionary<int, WorkData> works, Dictionary<int, int> num,
                         Dictionary<int, StatusData> statuses)
    {
        _files = files; Text = text; Jobs = jobs; Deps = deps; Items = items; Abilities = abilities; Works = works; Num = num;
        Statuses = statuses;
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
            jobs[U16(d, o)] = new JobData(U16(d, o), Words(d, o + 7, 12), Words(d, o + 31, 11), Words(d, o + 53, 6), U16(d, o + 65),
                                          U16(d, o + 2), U16(d, o + 4), d[o + 6]);

        var deps = new List<DepData>();
        d = Need("Dat", "Dep.dat");
        for (int i = 0, n = U16(d, 4), o = 6; i < n; i++, o += 33)
            deps.Add(new DepData(U16(d, o), U16(d, o + 2), d[o + 4],
                [.. Enumerable.Range(0, 7).Select(k => U16(d, o + 5 + 4 * k)).Where(v => v != 0)]));

        var items = new Dictionary<int, ItemData>();
        d = Need("Dat", "itm.dat");
        for (int i = 0, n = U16(d, 2), o = 6; i < n; i++, o += 48)
        {
            // 장비 보정 셋 — 파일 18/22/26 이 능력치 번호, 20/24/28 이 값(부호 있는 수).
            var bonuses = Enumerable.Range(0, 3)
                .Select(k => (Stat: U16(d, o + 18 + 4 * k), Value: (short)U16(d, o + 20 + 4 * k)))
                .Where(p => p.Stat != 0).ToArray();
            // 기본공격이 거는 상태이상 셋 — 파일 30/34/38 이 번호(엔진은 낮은 바이트만 쓴다), 32/36/40 이 값.
            var attackEffects = Enumerable.Range(0, 3)
                .Select(k => (Status: d[o + 30 + 4 * k], Value: (short)U16(d, o + 32 + 4 * k)))
                .Where(p => p.Status != 0).ToArray();
            items[U16(d, o)] = new ItemData(U16(d, o), U16(d, o + 2), BitConverter.ToUInt32(d, o + 4), d[o + 8],
                                            U16(d, o + 11), U16(d, o + 13), bonuses, U16(d, o + 9),
                                            U16(d, o + 42), U16(d, o + 46), U16(d, o + 16), attackEffects);
        }

        var works = new Dictionary<int, WorkData>();
        foreach (int f in WorkFiles)
        {
            if (files.Read("Dat", $"{f:D4}.att") is not { } a) continue;
            for (int i = 0, n = U16(a, 2), o = 6; i < n; i++, o += 62)
                works[U16(a, o)] = new WorkData(U16(a, o), U16(a, o + 2), a[o + 4], a[o + 5], U16(a, o + 7), U16(a, o + 9),
                                                a[o + 16], a[o + 17], (short)U16(a, o + 22), a[o + 27], (short)U16(a, o + 37), a[o + 39], a[o + 40],
                                                U16(a, o + 41), U16(a, o + 43), U16(a, o + 45), U16(a, o + 47), a[o + 55], a[o + 56], a[o + 57],
                                                [.. new[] { 28, 31, 34 }.Select(k => (a[o + k], (short)U16(a, o + k + 1))).Where(p => p.Item1 != 0)],
                                                U16(a, o + 24), a[o + 26],
                                                a[o + 6], a[o + 12], a[o + 13], a[o + 14], a[o + 15], a[o + 19], a[o + 21], a[o + 49]);
        }

        var abilities = new Dictionary<int, AbilityData>();
        foreach (int f in AbilityFiles)
        {
            if (files.Read("Abi", $"{f:D4}.abi") is not { } a) continue;
            for (int i = 0, n = U16(a, 2), o = 6; i < n; i++, o += 26)
                abilities[U16(a, o)] = new AbilityData(U16(a, o), U16(a, o + 2), U16(a, o + 4), [],
                                                       U16(a, o + 6), a[o + 8], U16(a, o + 9), a[o + 11], a[o + 12], U16(a, o + 24),
                                                       U16(a, o + 18), U16(a, o + 20), U16(a, o + 22));
        }
        foreach (var w in works.Values)
            if (w.AbilityId != 0 && w.Level != 0 && abilities.TryGetValue(w.AbilityId, out var ab))
                ab.WorkByLevel[w.Level] = w.Id;

        var num = new Dictionary<int, int>();
        d = Need("Dat", "Num.dat");
        for (int i = 0, n = U16(d, 2), o = 6; i < n; i++, o += 6)
            num[U16(d, o)] = BitConverter.ToInt32(d, o + 2);

        // 상태이상 표 — 머리 6바이트 + 18바이트 레코드. 아이콘(+2)은 Obs 0489 모션, 설명(+5)은 TXR 이다.
        var statuses = new Dictionary<int, StatusData>();
        d = Need("Dat", "Sta.dat");
        for (int i = 0, n = U16(d, 2), o = 6; i < n && o + 18 <= d.Length; i++, o += 18)
            statuses[U16(d, o)] = new StatusData(U16(d, o), U16(d, o + 2), U16(d, o + 5));

        return new GameDatabase(files, text, jobs, deps, items, abilities, works, num, statuses);
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
        c.Items.Where(i => i != 0 && Items.ContainsKey(i)).SelectMany(i => Items[i].Bonuses).Where(b => b.Stat == stat).Sum(b => b.Value)
        + PassiveBonus(c, stat);

    /// <summary>장착 어빌리티 보너스 <c>0x10032af0</c> — 칸마다 그 어빌리티 지금 레벨 work 의 (능력치, 값) 짝을 더한다.</summary>
    public int PassiveBonus(CharacterData c, int stat)
    {
        int sum = 0;
        foreach (ushort id in c.Passives)
        {
            if (id == 0 || !Abilities.TryGetValue(id, out var ab) || !ab.WorkByLevel.TryGetValue(c.AbilityLevel(id), out int wid)
                || !Works.TryGetValue(wid, out var w)) continue;
            sum += w.Bonuses.Where(b => b.Stat == stat).Sum(b => b.Value);
        }
        return sum;
    }

    /// <summary>장착 어빌리티 칸 수 = 직업이 든 Dep 레코드 파일 4바이트(1~3). 직업 37 은 늘 3.</summary>
    public int PassiveSlotCount(CharacterData c) =>
        c.JobId == 37 ? 3 : Math.Clamp((int)(Deps.FirstOrDefault(d => d.Jobs.Contains(c.JobId))?.Tier ?? 1), 1, 3);

    /// <summary>
    /// 아이템을 끼울 수 있는 칸(<c>0x10032d70</c>/<c>0x10032cc0</c>): 0 무기 = 캐릭터 무기 종류와 같을 때, 1 갑옷 = 종류 2,
    /// 2 목걸이 = 6, 3 반지 = 5, 4 벨트 = 4, 5 신발 = 3. 캡슐(7)은 못 낀다.
    /// </summary>
    public bool FitsSlot(CharacterData c, ItemData item, int slot) => slot switch
    {
        0 => item.Type == WeaponTypeOf(c),
        1 => item.Type == 2,
        2 => item.Type == 6,
        3 => item.Type == 5,
        4 => item.Type == 4,
        5 => item.Type == 3,
        _ => false,
    };

    /// <summary>
    /// 무기 칸에 낄 종류. .chr 파일 39 가 0 인데 무기를 들고 있으면(제이슨 0062 가 일반 검을 들고 0) 든 무기 종류를 쓴다 —
    /// 원본은 저장 파일의 캐릭터 기록(CChr+0x4a)을 쓰므로 .chr 값이 비어 있을 수 있다(가설).
    /// </summary>
    public int WeaponTypeOf(CharacterData c) =>
        c.WeaponType == 0 && c.Items[0] != 0 && Items.TryGetValue(c.Items[0], out var w) ? w.Type : c.WeaponType;

    /// <summary>그 레벨 work 의 EXP(+0x30) — Status 화면 빨간 숫자. 최대 레벨이거나 work 가 없으면 0.</summary>
    public int AbilityExpCost(AbilityData ab, int level) =>
        level < ab.MaxLevel && ab.WorkByLevel.TryGetValue(level, out int wid) && Works.TryGetValue(wid, out var w) ? w.ExpCost : 0;

    /// <summary>
    /// 배울 수 있는 어빌리티(<c>0x100324e0</c>) — 직업 어빌리티 목록 중 아직 안 배웠고, 선행 어빌리티 두 개의 레벨 조건을 채운 것.
    /// </summary>
    public IEnumerable<AbilityData> Learnable(CharacterData c)
    {
        if (!Jobs.TryGetValue(c.JobId, out var job)) yield break;
        foreach (ushort id in job.AbilityList)
        {
            if (id == 0 || c.HasAbility(id) || !Abilities.TryGetValue(id, out var ab)) continue;
            if (ab.Prereq1 != 0 && c.AbilityLevel(ab.Prereq1) < ab.Prereq1Level) continue;
            if (ab.Prereq2 != 0 && c.AbilityLevel(ab.Prereq2) < ab.Prereq2Level) continue;
            yield return ab;
        }
    }

    /// <summary>
    /// 쓰러뜨렸을 때 받는 경험치(<c>0x10033020</c>) = Num[14] + Num[15] × (쓰러뜨린 적 레벨 − 내 레벨), Num[16]~Num[17] 로 자른다.
    /// </summary>
    public int ExpForKill(CharacterData killer, int targetLevel) =>
        Math.Clamp(N(14) + N(15) * (targetLevel - killer.Level), N(16), N(17));

    /// <summary>
    /// 레벨업(<c>0x100318d0</c>) — 쌓인 경험치 ÷ 100 이 새 레벨. 오름 = 직업 성장률% × 기본값 / 100 × 오른 레벨 수(난수 없음).
    /// TP 는 CTP 에서 옮겨 온다(CTP 바닥 100). HP·현재 TP 회복은 없다. 오른 능력치 목록을 <paramref name="gains"/> 로 준다.
    /// </summary>
    public CharacterData LevelUp(CharacterData c, out List<(string Stat, int Amount)> gains)
    {
        gains = [];
        int level = Math.Max(1, c.CumExp / 100);
        int d = level - c.Level;
        if (d <= 0 || !Jobs.TryGetValue(c.JobId, out var job) || job.Growth.Length < 12) return c with { Level = (ushort)Math.Max(c.Level, level) };

        int Grow(int index, int baseValue) => job.Growth[index] * baseValue / 100 * d;
        int lp = Grow(6, (int)c.Lp), tp = Grow(7, c.Tp), psy = Grow(9, c.Psy), dep = Grow(10, c.Dep), dex = Grow(11, c.Dex);

        int ctp = c.Ctp, gainTp = 0;
        if (ctp - tp >= 100) { gainTp = tp; ctp -= tp; }
        else if (ctp > 100) { gainTp = ctp - 100; ctp = 100; }

        if (lp > 0) gains.Add(("LP", lp));
        if (psy > 0) gains.Add(("PSY", psy));
        if (gainTp > 0) gains.Add(("TP", gainTp));
        if (dep > 0) gains.Add(("DEP", dep));
        if (dex > 0) gains.Add(("DEX", dex));

        return c with
        {
            Level = (ushort)level, Lp = c.Lp + (uint)lp, Psy = (ushort)(c.Psy + psy), Tp = (ushort)(c.Tp + gainTp),
            Ctp = (ushort)ctp, Dep = (ushort)(c.Dep + dep), Dex = (ushort)(c.Dex + dex),
        };
    }

    /// <summary>DEP = 파일 DEP + 장비·패시브 보너스(0x20).</summary>
    public int Dep(CharacterData c) => c.Dep + EquipBonus(c, 0x20);

    /// <summary>갑옷 배율 <c>0x1007b020</c> = 장비 2칸(갑옷) 아이템 방어값(Itm 파일 +13).</summary>
    public int ArmorRate(CharacterData c) => c.Items[1] != 0 && Items.TryGetValue(c.Items[1], out var it) ? it.Defense : 0;

    /// <summary>
    /// 화면 최대 HP <c>0x1007ad60</c> = 내부 최대 HP(LP + 장비 0x30) × (Num[6] + 갑옷 배율) / Num[6].
    /// 죠안 LP 800 · 갑옷 100 → 1600 (게임 화면과 같음). 전투는 이 화면 값으로 셈한다 — 원본은 내부 HP 에서
    /// 피해 × 100/(100+갑옷) 을 빼지만 화면에 보이는 감소량은 거의 같다.
    /// </summary>
    public int MaxHp(CharacterData c)
    {
        int inner = (int)c.Lp + EquipBonus(c, 0x30);
        int armor = ArmorRate(c);
        return armor == 0 || N(6) == 0 ? inner : inner * (armor + N(6)) / N(6);
    }
    public int Psy(CharacterData c) => c.Psy + EquipBonus(c, 0x1f);
    /// <summary>DEX <c>0x1007ae50</c> — 걸음 비용·ACR 에 쓰는 값(파일 DEX + 장비 보너스; 전투 버프·효과 1 은 뺌).</summary>
    public int Dex(CharacterData c) => c.Dex + EquipBonus(c, 0x1e);
    public int MaxTp(CharacterData c) => c.Tp + EquipBonus(c, 0x21);
    /// <summary>
    /// work 의 TP 비용 <c>0x10072610</c> = att+0x32 + att+0x2e × Num[43 + 3×체질] / 100 / Num[34] (체질 1~5, 무속성은 앞 항만).
    /// 효과 0x14 가감은 넣지 않았다.
    /// </summary>
    public int WorkTpCost(CharacterData c, int workId)
    {
        if (!Works.TryGetValue(workId, out var w)) return 0;
        int cost = w.TpBase;
        if (c.Body is >= 1 and <= 5 && N(34) != 0) cost += w.HpFactor * N(43 + 3 * c.Body) / 100 / N(34);
        return cost;
    }

    /// <summary>
    /// 그 work 을 쓰려면 있어야 하는 SOUL(<c>0x10072440</c>) = <c>+0x34</c> + 체질 덧붙임.
    /// 체질 덧붙임 = <c>+0x2e × Num[45+3(체질−1)]% ÷ Num[33]</c> — 체질마다 HP·SOUL·TP 로 나뉘는 비율이 다르다(분석-전투 ba-4).
    /// </summary>
    public int WorkSoulNeed(CharacterData c, int workId) =>
        !Works.TryGetValue(workId, out var w) ? 0 : w.SoulBase + BodyShare(c, w, 45, 33);

    /// <summary>
    /// 그 work 을 쓰고 <b>실제로 깎이는</b> SOUL(<c>0x10072510</c>) — <c>+0x36</c> 이 0 이면 바탕은 안 깎이고 체질 덧붙임만 깎인다.
    /// 그래서 사이클론(에텔)·포스트럴(코절) 체질은 SOUL 이 거의 안 줄고 HP·TP 로 낸다.
    /// </summary>
    public int WorkSoulCost(CharacterData c, int workId) =>
        !Works.TryGetValue(workId, out var w) ? 0 : (w.SoulSpend != 0 ? w.SoulBase : 0) + BodyShare(c, w, 45, 33);

    /// <summary>그 work 을 쓰고 깎이는 HP(<c>0x100726e0</c>) — 바탕 없이 체질 덧붙임뿐이다.</summary>
    public int WorkHpCost(CharacterData c, int workId) =>
        !Works.TryGetValue(workId, out var w) ? 0 : BodyShare(c, w, 44, 41);

    /// <summary>체질 덧붙임 — <c>+0x2e × Num[표+3(체질−1)]% ÷ Num[나눗수]</c>.</summary>
    private int BodyShare(CharacterData c, WorkData w, int table, int divisor) =>
        c.Body is >= 1 and <= 5 && N(divisor) != 0 ? w.HpFactor * N(table + 3 * (c.Body - 1)) / 100 / N(divisor) : 0;

    /// <summary>이동 예산(상태 12 <c>0x10069cc9</c>) = 현재TP + min(0, CTP − 기본공격 TP 비용).</summary>
    public int MoveBudget(CharacterData c, int currentTp) => currentTp + Math.Min(0, c.Ctp - WorkTpCost(c, c.BasicWorkId));

    /// <summary>STP <c>0x1007acf0</c> = 최대 TP / TP 나눗수 — 시간 한 칸마다 차는 TP.</summary>
    public int Stp(CharacterData c) => c.TpDivisor == 0 ? 0 : MaxTp(c) / c.TpDivisor;
    public int SoulStart => N(20);
    public int MaxSoul(CharacterData c) => N(19) + EquipBonus(c, 0x25);

    /// <summary>ACR <c>0x1007ab90</c> = (2×CTP + 최대TP + 현재TP) / Num[9] + DEX / Num[8].</summary>
    public int Acr(CharacterData c, int currentTp) => (2 * c.Ctp + MaxTp(c) + currentTp) / N(9) + Dex(c) / N(8);

    /// <summary>
    /// ATK <c>0x1007ab20</c>·공격력 <c>0x1007aa90</c> = ((무기 공격 + Num[1]) × PSY / Num[25]) × (SOUL + Num[2]) × (Num[42] + work 위력) / Num[85].
    /// 화면 ATK 는 위력 0.
    /// </summary>
    /// <param name="weaponPercent">상태이상 <b>29(무기 공격력 %d 변화)</b> — 무기 공격력에만 먹는다(<c>0x1007afe5</c>).</param>
    public int Atk(CharacterData c, int soul, int workPower = 0, int weaponPercent = 0)
    {
        int weapon = c.Items[0] != 0 && Items.TryGetValue(c.Items[0], out var w) ? w.Attack : 0;
        if (weaponPercent != 0) weapon = Math.Max(0, weapon + weapon * weaponPercent / 100);
        int v = (weapon + N(1)) * Psy(c) / N(25);
        return v * (soul + N(2)) * (N(42) + workPower) / N(85);
    }

    /// <summary>RDP <c>0x1007abf0</c> — HP 가 가득이면 DEP 그대로다.</summary>
    public int RdpAtFullHp(CharacterData c) => Dep(c);

    /// <summary>RDP <c>0x1007abf0</c> = trunc(DEP × (1 + Num[39]% × (1 − HP/최대HP)²)) — HP 가 줄수록 단단해진다(0 이면 ×1.3).</summary>
    public int Rdp(CharacterData c, int hp, int maxHp)
    {
        double t = maxHp <= 0 ? 0 : 1.0 - (double)hp / maxHp;
        return (int)(Dep(c) * (t * N(39) * t * 0.01 + 1.0));
    }

    /// <summary>명중률 <c>0x1007b580</c>(%) = att+0x2c × 8/10 + 2 × (Num[7] + (공DEX − 방DEX)/Num[8] + (공TP − 방TP)/Num[9]) / 10.</summary>
    public int HitChance(CharacterData a, int attackerTp, CharacterData d, int defenderTp, WorkData w, int defenderStance = 0)
    {
        int s = (Dex(a) - Dex(d)) / N(8) + (attackerTp - defenderTp) / N(9) + N(7);
        int hit = w.Accuracy * 8 / 10 + s * 2 / 10;
        if (defenderStance == 2) hit -= Dex(d) / N(10);   // 회피 자세(work 515)
        return hit;
    }

    /// <summary>
    /// 한 번의 판정 <c>0x1007b6f0</c>. 반환: (양, 결과 1 회복 / 2 맞음 / 3 빗나감, 치명).
    /// 피해 = (Num[3] − RDP) × 공격력 / Num[3] → 흔들기 ±Num[22]/2 % → 치명(rand%100 ≤ att+0x2d) × Num[23]/100.
    /// 회복 = 최대 HP × 위력 / 100. 자세(방어·회피)는 넣었고 상태이상 보정은 뺐다.
    /// </summary>
    public (int Amount, int Result, bool Critical) Resolve(Random rng, CharacterData a, int aTp, int aSoul,
                                                          CharacterData d, int dTp, int dHp, int dMaxHp, WorkData w, int defenderStance = 0,
                                                          int attackerWeaponPercent = 0)
    {
        if (w.IsHeal) return (dMaxHp * w.Power / 100, 1, false);
        if (!w.IsDamage) return (0, 2, false);
        if (rng.Next(100) >= HitChance(a, aTp, d, dTp, w, defenderStance)) return (0, 3, false);

        int dmg = (N(3) - Rdp(d, dHp, dMaxHp)) * Atk(a, aSoul, w.Power, attackerWeaponPercent) / N(3);
        // 방어 자세(work 516): (DEX/Num11 + Num12)% 로 한 번 더 깎인다.
        if (defenderStance == 1 && Dex(d) / N(11) + N(12) > rng.Next(100)) dmg = (N(3) - Rdp(d, dHp, dMaxHp)) * dmg / N(3);
        int v = dmg * N(22) / 100;
        if (v > 0) dmg += rng.Next(v) - dmg * N(22) / 200;
        bool crit = rng.Next(100) <= w.Critical;
        if (crit) dmg = dmg * N(23) / 100;
        return (dmg, 2, crit);
    }

    /// <summary>무기 종류 이름(장비 1번 칸 아이템 종류).</summary>
    public static string WeaponTypeName(byte type) => type switch
    {
        0 => "VES", 8 => "Ribbon", 9 => "Yoyo", 10 => "Camera", 11 => "Claw", 12 => "Gun",
        13 => "Cigar", 14 => "Sword", 15 => "Great Sword", 17 => "Poison", 19 => "Weapon", _ => "",
    };

    private static ushort U16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
    private static ushort[] Words(byte[] b, int o, int n) => [.. Enumerable.Range(0, n).Select(i => U16(b, o + 2 * i))];
}

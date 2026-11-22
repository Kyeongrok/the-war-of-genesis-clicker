using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarOfGenesis.Assets;

/// <summary>타워 디펜스 무기 갈래의 수치 — 사거리 · 간격은 칸(40px) · 초. <see cref="Key"/> 는 게임의 갈래 이름(Sword, Spear …).</summary>
public sealed class TdWeaponEntry
{
    public string Key { get; set; } = "Sword";
    public string Name { get; set; } = "";
    public double Range { get; set; }
    public int Damage { get; set; }
    public double Interval { get; set; } = 1;
    /// <summary>맞은 자리에서 이 반경(칸) 안의 적도 맞는다(0 = 하나만).</summary>
    public double Splash { get; set; }
    /// <summary>겨눈 적 둘레 이만큼 더 꿰뚫는다.</summary>
    public int Pierce { get; set; }
    /// <summary>탄 빠르기(칸/초) — 0 이면 근접.</summary>
    public double Shot { get; set; }
    /// <summary>탄 · 빔 색(AARRGGBB 16진).</summary>
    public string ShotColor { get; set; } = "00000000";
    /// <summary>맞은 적을 이만큼 느리게(0.45 = 45%).</summary>
    public double Slow { get; set; }
    public double SlowSeconds { get; set; }
}

/// <summary>지을 수 있는 타워 하나 — 인물 번호(파트1 은 20000+), 무기 갈래, 값. 영웅은 하나씩만 짓고 오라를 두른다.</summary>
public sealed class TdTowerEntry
{
    public int Chr { get; set; }
    public string Name { get; set; } = "";
    public string Weapon { get; set; } = "Sword";
    public int Cost { get; set; } = 50;
    /// <summary>갈래 피해 · 사거리에 곱한다.</summary>
    public double Power { get; set; } = 1;
    public bool Hero { get; set; }
    /// <summary>None · Power(공격력) · Haste(공격 속도) · Range(사거리).</summary>
    public string Aura { get; set; } = "None";
    public double AuraValue { get; set; }
}

/// <summary>적 하나 — <see cref="Fallback"/> 은 파트1 그림이 없을 때 대신 쓰는 파트2 인물.</summary>
public sealed class TdEnemyEntry
{
    public int Chr { get; set; }
    public int Fallback { get; set; }
    public string Name { get; set; } = "";
    public int Hp { get; set; } = 50;
    /// <summary>칸/초.</summary>
    public double Speed { get; set; } = 1;
    public int Bounty { get; set; } = 5;
    /// <summary>끝까지 가면 깎는 생명.</summary>
    public int Leak { get; set; } = 1;
    public int Armor { get; set; }
}

/// <summary>웨이브 한 묶음 — 스테이지 적 목록의 <see cref="Enemy"/> 번째 적을 <see cref="Count"/> 마리, <see cref="Every"/> 초 간격으로.</summary>
public sealed class TdSpawnEntry
{
    public int Enemy { get; set; }
    public int Count { get; set; } = 5;
    public double Every { get; set; } = 1;
}

/// <summary>스테이지 하나 — 맵, 보는 자리, 길, 시작 골드 · 생명, 적과 웨이브.</summary>
public sealed class TdStageEntry
{
    /// <summary>기록(towerdefense.json)의 열쇠 — 바꾸면 그 스테이지의 최고 기록이 새로 시작한다.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>「part1:NNNN」(파트1 Obt) 또는 「NNNN.obt」(파트2 Obt).</summary>
    public string Map { get; set; } = "";
    /// <summary>보는 네모(640×364)의 맵 그림 왼위 — -1 이면 가운데.</summary>
    public int CamX { get; set; } = -1;
    public int CamY { get; set; } = -1;
    /// <summary>길이 거쳐 가는 칸 [열, 행] — 첫 · 끝은 입구 · 출구, 사이는 걸을 수 있는 칸으로 잇는다. 비면 저절로 고른다.</summary>
    public List<int[]> Waypoints { get; set; } = [];
    /// <summary>싸움터 네모 크기(px) — 0 이면 게임 화면 크기(640×364). 크면 게임에서 굴려 본다(td-8).</summary>
    public int AreaW { get; set; }
    public int AreaH { get; set; }
    /// <summary>맵 자료와 달리 손으로 연 칸 · 막은 칸 [열, 행](td-10).</summary>
    public List<int[]> Open { get; set; } = [];
    public List<int[]> Blocked { get; set; } = [];
    /// <summary>참이면 <see cref="Waypoints"/> 가 보는 네모 안 칸(옛 꼴), 거짓이면 맵 칸.</summary>
    public bool ViewCells { get; set; }
    public int Bgm { get; set; } = BattleDemoScene.Bgm;
    public int StartGold { get; set; } = 250;
    public int StartLives { get; set; } = 20;
    /// <summary>웨이브 n 의 적 HP 는 1 + n × 이 값 배.</summary>
    public double HpPerWave { get; set; } = 0.10;
    public List<TdEnemyEntry> Enemies { get; set; } = [];
    public List<List<TdSpawnEntry>> Waves { get; set; } = [];
}

/// <summary>towers.json — 무기 갈래 수치와 지을 수 있는 타워 목록.</summary>
public sealed class TdTowersFile
{
    public List<TdWeaponEntry> Weapons { get; set; } = [];
    public List<TdTowerEntry> Towers { get; set; } = [];
}

/// <summary>
/// 타워 디펜스 자료 — 편집기(타워 디펜스 편집, 사용자 요청 td-4)가 <c>assets/data/towerdefense/</c> 에 <c>towers.json</c> · <c>stage-NN.json</c> 을
/// 적고 게임은 타워 디펜스를 열 때 읽는다. 파일이 없으면 <see cref="DefaultTowers"/> · <see cref="DefaultStages"/> 를 쓴다.
/// </summary>
public static class TowerDefenseBook
{
    public const string Folder = "towerdefense";
    public const string TowersFile = "towers.json";
    public const string StagePrefix = "stage-";

    /// <summary>파트1 인물 번호의 시작(게임의 Part1Game.ChrBase 와 같다).</summary>
    public const int Part1Base = 20000;

    public static readonly string[] WeaponKeys = ["Sword", "Spear", "DualBlade", "GreatSword", "Bow", "Gun", "Beam", "Magic", "Frost"];
    public static readonly string[] AuraKeys = ["None", "Power", "Haste", "Range"];

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string StageFileName(int index) => $"{StagePrefix}{index + 1:D2}.json";

    public static string ToJson(TdTowersFile file) => JsonSerializer.Serialize(file, Json);
    public static string ToJson(TdStageEntry stage) => JsonSerializer.Serialize(stage, Json);

    /// <summary>towers.json 이 있으면 그것(빠진 갈래는 바탕 값), 없거나 깨졌으면 바탕 값.</summary>
    public static TdTowersFile LoadTowers(GameFiles files)
    {
        var def = DefaultTowers();
        try
        {
            if (files.Read(Folder, TowersFile) is not { } b || JsonSerializer.Deserialize<TdTowersFile>(Encoding.UTF8.GetString(b), Json) is not { } f) return def;
            foreach (var w in def.Weapons.Where(w => f.Weapons.All(x => x.Key != w.Key))) f.Weapons.Add(w);
            f.Weapons.RemoveAll(w => !WeaponKeys.Contains(w.Key));
            f.Towers.RemoveAll(t => !WeaponKeys.Contains(t.Weapon));
            foreach (var t in f.Towers.Where(t => !AuraKeys.Contains(t.Aura))) t.Aura = "None";
            return f.Towers.Count > 0 ? f : def;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return def; }
    }

    /// <summary><c>stage-*.json</c> 이 하나라도 있으면 그것들(파일 이름 차례), 없으면 바탕 스테이지.</summary>
    public static List<TdStageEntry> LoadStages(GameFiles files)
    {
        var stages = new List<TdStageEntry>();
        try
        {
            foreach (var name in files.List(Folder, ".json").Keys.Where(n => n.StartsWith(StagePrefix, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (files.Read(Folder, name) is { } b && JsonSerializer.Deserialize<TdStageEntry>(Encoding.UTF8.GetString(b), Json) is { } s && Valid(s))
                    {
                        if (s.Id.Length == 0) s.Id = Path.GetFileNameWithoutExtension(name);
                        stages.Add(s);
                    }
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { }
        return stages.Count > 0 ? stages : DefaultStages();
    }

    /// <summary>쓸 수 있는 스테이지인가 — 맵이 있고, 적 번호가 목록 안이고, 웨이브가 하나는 있다(틀린 묶음은 버린다).</summary>
    public static bool Valid(TdStageEntry s)
    {
        if (s.Map.Length == 0 || s.Enemies.Count == 0) return false;
        s.Waypoints = s.Waypoints.Where(p => p is { Length: 2 }).ToList();
        foreach (var w in s.Waves) w.RemoveAll(g => g.Enemy < 0 || g.Enemy >= s.Enemies.Count || g.Count <= 0);
        s.Waves.RemoveAll(w => w.Count == 0);
        s.StartGold = Math.Max(0, s.StartGold);
        s.StartLives = Math.Max(1, s.StartLives);
        return s.Waves.Count > 0;
    }

    private static string Hex(uint c) => c.ToString("X8");

    public static TdTowersFile DefaultTowers() => new()
    {
        Weapons =
        [
            new() { Key = "Sword", Name = "장검", Range = 1.6, Damage = 16, Interval = 0.80 },
            new() { Key = "Spear", Name = "창", Range = 2.1, Damage = 13, Interval = 0.95, Pierce = 2 },
            new() { Key = "DualBlade", Name = "쌍검", Range = 1.5, Damage = 8, Interval = 0.40 },
            new() { Key = "GreatSword", Name = "대검", Range = 1.6, Damage = 30, Interval = 1.50, Splash = 0.9 },
            new() { Key = "Bow", Name = "활", Range = 4.2, Damage = 10, Interval = 0.70, Shot = 11, ShotColor = Hex(0xFFE8D8A0) },
            new() { Key = "Gun", Name = "총", Range = 3.6, Damage = 24, Interval = 1.10, Shot = 22, ShotColor = Hex(0xFFFFF070) },
            new() { Key = "Beam", Name = "빔", Range = 3.4, Damage = 20, Interval = 1.20, Pierce = 2, ShotColor = Hex(0xFF80FFFF) },
            new() { Key = "Magic", Name = "마법", Range = 3.0, Damage = 20, Interval = 1.60, Splash = 1.1, Shot = 7, ShotColor = Hex(0xFF90C0FF) },
            new() { Key = "Frost", Name = "냉기", Range = 3.2, Damage = 7, Interval = 1.20, Splash = 0.8, Shot = 7, ShotColor = Hex(0xFF90E8FF), Slow = 0.45, SlowSeconds = 2.0 },
        ],
        // 병종은 파트1 병사를 그 인물의 기본 공격(칸 9 · 무기 갈래)으로 골랐고 이름도 그 병사 이름(td-2 · td-3). 궁병 자리는 한제국 활 직업 조의선인(Grand Archer, 파트1 484 — 활 4, td-5).
        Towers =
        [
            T(Part1Base + 155, "나이트", "Sword", 50),
            T(Part1Base + 159, "파이크", "Spear", 60),
            T(Part1Base + 387, "어쌔신", "DualBlade", 70),
            T(Part1Base + 192, "알가지", "GreatSword", 90),
            T(Part1Base + 484, "조의선인", "Bow", 70),
            T(Part1Base + 144, "솔져", "Gun", 80),
            T(Part1Base + 256, "스파히", "Beam", 100),
            T(Part1Base + 168, "위자드", "Magic", 110),
            T(Part1Base + 539, "아이스 마스터", "Frost", 90),
            T(Part1Base + 0, "살라딘", "DualBlade", 450, 1.9, "Haste", 0.25),
            T(Part1Base + 30, "버몬트", "GreatSword", 500, 1.8, "Power", 0.30),
            T(Part1Base + 65, "크리스티앙", "Gun", 450, 1.9, "Range", 0.20),
            T(Part1Base + 10, "셰라자드", "Magic", 520, 1.8, "Power", 0.25),
            T(Part1Base + 32, "어윈 록슬리", "Magic", 420, 1.8, "Range", 0.15),
            T(Part1Base + 16, "얀 지슈카", "GreatSword", 420, 1.8, "Haste", 0.20),
            T(219, "살라딘(2부)", "DualBlade", 480, 2.0, "Haste", 0.25),
            T(220, "크리스티앙(2부)", "Gun", 480, 2.0, "Power", 0.25),
            T(221, "죠안", "Magic", 480, 1.9, "Range", 0.20),
            T(223, "베라모드", "GreatSword", 520, 1.9, "Power", 0.30),
        ],
    };

    private static TdTowerEntry T(int chr, string name, string weapon, int cost, double power = 1, string? aura = null, double auraValue = 0) =>
        new() { Chr = chr, Name = name, Weapon = weapon, Cost = cost, Power = power, Hero = aura != null, Aura = aura ?? "None", AuraValue = auraValue };

    private static TdEnemyEntry E(int chr, int fallback, string name, int hp, double speed, int bounty, int leak = 1, int armor = 0) =>
        new() { Chr = Part1Base + chr, Fallback = fallback, Name = name, Hp = hp, Speed = speed, Bounty = bounty, Leak = leak, Armor = armor };

    private static List<TdSpawnEntry> W(params (int Enemy, int Count, double Every)[] groups) =>
        groups.Select(g => new TdSpawnEntry { Enemy = g.Enemy, Count = g.Count, Every = g.Every }).ToList();

    /// <summary>바탕 스테이지 — 지하 미궁(파트1 Btl 0415 의 Obt 86, 「ㄷ」 길), 파트1 병사 10종 · 웨이브 10개(5 · 10 우두머리).</summary>
    public static List<TdStageEntry> DefaultStages() =>
    [
        new()
        {
            Id = "part1:0086", Name = "지하 미궁", Map = "part1:0086", ViewCells = true,
            Waypoints = [[0, 6], [1, 1], [15, 1], [15, 6]],
            Enemies =
            [
                E(144, 230, "솔져", 60, 1.30, 6),
                E(145, 311, "도적", 45, 1.90, 6),
                E(132, 363, "좀비", 120, 0.80, 8),
                E(130, 265, "나이트", 160, 1.10, 11, 1, 3),
                E(153, 231, "파이크", 110, 1.20, 9, 1, 2),
                E(208, 251, "위자드", 90, 1.20, 10),
                E(131, 300, "캡틴 스켈톤", 260, 0.95, 16, 2, 4),
                E(229, 362, "드래군", 220, 1.70, 16, 2, 2),
                E(136, 255, "리치", 900, 0.70, 80, 5, 4),
                E(221, 304, "장군", 1600, 0.75, 150, 10, 6),
            ],
            Waves =
            [
                W((0, 8, 1.1)),
                W((0, 6, 0.9), (1, 6, 0.7)),
                W((2, 8, 1.2), (1, 6, 0.6)),
                W((4, 8, 1.0), (5, 4, 1.4)),
                W((3, 6, 1.2), (8, 1, 1)),
                W((1, 14, 0.45), (4, 8, 0.9)),
                W((3, 8, 1.0), (5, 6, 1.0), (6, 3, 2)),
                W((7, 10, 0.8), (2, 10, 0.6)),
                W((6, 6, 1.2), (3, 10, 0.8), (7, 6, 0.8)),
                W((7, 8, 0.7), (6, 6, 1.0), (9, 1, 1), (8, 2, 3)),
            ],
        },
    ];

    // ── 길 — 게임과 편집기가 같은 셈을 쓴다 ──

    /// <summary>칸 크기와 높이 한 칸의 픽셀(판과 같다).</summary>
    public const int Cell = 40, CellH = 32, HeightStep = 12;

    /// <summary>게임 화면에서 맵이 보이는 네모(위 막대 · 아래 짓기 칸을 뺀 자리).</summary>
    public const int ViewW = 640, ViewH = 364;

    /// <summary>칸 가운데(발 자리)의 맵 그림 좌표.</summary>
    public static (double X, double Y) CellFoot(ObtMapImage m, int col, int row) =>
        (col * Cell + Cell / 2.0, row * CellH + CellH / 2.0 - m.HeightAt(col, row) * HeightStep - m.OriginY);

    /// <summary>
    /// 스테이지의 싸움터 네모(맵 그림 좌표) — 왼위가 -1 이면 맵 가운데, 크기가 0 이면 게임 화면 크기(640×364).
    /// 화면보다 크면 게임에서 화살표 키 · 화면 가장자리로 굴려 본다(td-8).
    /// </summary>
    public static (int X, int Y, int W, int H) Area(ObtMapImage m, TdStageEntry s) => Area(m, s.CamX, s.CamY, s.AreaW, s.AreaH);

    public static (int X, int Y, int W, int H) Area(ObtMapImage m, int camX, int camY, int areaW, int areaH)
    {
        int w = areaW > 0 ? areaW : ViewW, h = areaH > 0 ? areaH : ViewH;
        return (camX >= 0 ? camX : Math.Max(0, (m.Width - w) / 2), camY >= 0 ? camY : Math.Max(0, (m.Height - h) / 2), w, h);
    }

    /// <summary>옛 꼴(보는 네모 안 칸) 경유점을 맵 칸으로.</summary>
    public static (int Col, int Row) ViewToMap(ObtMapImage m, int camX, int camY, int vc, int vr) =>
        ((camX + vc * Cell + Cell / 2) / Cell, (camY + vr * CellH + CellH / 2 + m.OriginY) / CellH);
}

/// <summary>
/// 싸움터 — 맵, 싸움터 네모, 스테이지가 손으로 연 · 막은 칸(td-10). 걸을 수 있는 칸 · 길 잇기를 게임과 편집기가 같이 쓴다.
/// </summary>
public sealed class TdField(ObtMapImage map, (int X, int Y, int W, int H) area, IEnumerable<int[]>? open = null, IEnumerable<int[]>? blocked = null)
{
    public ObtMapImage Map { get; } = map;
    public (int X, int Y, int W, int H) Area { get; } = area;
    private readonly HashSet<(int, int)> _open = [.. (open ?? []).Where(p => p is { Length: 2 }).Select(p => (p[0], p[1]))];
    private readonly HashSet<(int, int)> _blocked = [.. (blocked ?? []).Where(p => p is { Length: 2 }).Select(p => (p[0], p[1]))];

    public static TdField Of(ObtMapImage m, TdStageEntry s) => new(m, TowerDefenseBook.Area(m, s), s.Open, s.Blocked);

    /// <summary>그 칸이 싸움터 네모 안에 다 들어오는가.</summary>
    public bool Inside(int col, int row)
    {
        var (x, y) = TowerDefenseBook.CellFoot(Map, col, row);
        return x - TowerDefenseBook.Cell / 2 >= Area.X && x + TowerDefenseBook.Cell / 2 <= Area.X + Area.W
            && y - TowerDefenseBook.CellH / 2 >= Area.Y && y + TowerDefenseBook.CellH / 2 <= Area.Y + Area.H;
    }

    /// <summary>맵 자료로 걸을 수 있는가(막힘 깃발 1 · 8 없음) — 손으로 연 · 막은 칸이 먼저.</summary>
    public bool Ground(int col, int row) =>
        col >= 0 && row >= 0 && col < Map.Cols && row < Map.Rows
        && (_blocked.Contains((col, row)) ? false : _open.Contains((col, row)) || (Map.FlagsAt(col, row) & 0x9) == 0);

    /// <summary>걸을 수 있고 싸움터 안인 칸 — 길과 타워 자리.</summary>
    public bool Walkable(int col, int row) => Ground(col, row) && Inside(col, row);

    /// <summary>
    /// 경유점(맵 칸)을 걸을 수 있는 칸으로 잇는 길. 경유점은 둘레에서 가장 가까운 걸을 수 있는 칸으로 옮기고(첫 · 끝은 그 열에서만),
    /// 칸 사이는 높이 차 1 이하로 잇는 가장 짧은 길(BFS) — 손으로 연 칸은 높이 차를 안 따진다. 못 이으면 곧은 줄.
    /// 경유점이 없으면 왼 가운데 → 위 → 아래 → 오른 가운데로 저절로.
    /// </summary>
    public List<(int Col, int Row)> BuildPath(IReadOnlyList<(int Col, int Row)> waypoints)
    {
        var marks = new List<(int, int)>();
        if (waypoints.Count > 0)
            for (int i = 0; i < waypoints.Count; i++)
                marks.Add(Nearest(waypoints[i].Col, waypoints[i].Row, edge: i == 0 || i == waypoints.Count - 1));
        else
        {
            var (ax, ay, aw, ah) = Area;
            int c0 = (int)Math.Ceiling(ax / (double)TowerDefenseBook.Cell), c1 = (ax + aw) / TowerDefenseBook.Cell - 1;
            int midRow = (ay + ah / 2 + Map.OriginY) / TowerDefenseBook.CellH;
            int topRow = (ay + ah / 5 + Map.OriginY) / TowerDefenseBook.CellH, bottomRow = (ay + ah * 4 / 5 + Map.OriginY) / TowerDefenseBook.CellH;
            marks.Add(Nearest(c0, midRow, edge: true));
            marks.Add(Nearest(c0 + (c1 - c0) / 3, topRow));
            marks.Add(Nearest(c0 + (c1 - c0) * 2 / 3, bottomRow));
            marks.Add(Nearest(c1, midRow, edge: true));
        }
        var order = new List<(int Col, int Row)>();
        for (int i = 0; i < marks.Count; i++)
        {
            var leg = i == 0 ? [marks[0]] : Route(marks[i - 1], marks[i]);
            foreach (var c in leg)
                if (order.Count == 0 || order[^1] != c) order.Add(c);
        }
        return order;
    }

    private (int, int) Nearest(int col, int row, bool edge = false)
    {
        if (Walkable(col, row)) return (col, row);
        (int, int) best = (col, row);
        int bestD = int.MaxValue;
        for (int c = col - (edge ? 0 : 3); c <= col + (edge ? 0 : 3); c++)
            for (int r = row - 8; r <= row + 8; r++)
            {
                if (!Walkable(c, r)) continue;
                int d = Math.Abs(c - col) * 3 + Math.Abs(r - row);
                if (d < bestD) (bestD, best) = (d, (c, r));
            }
        return best;
    }

    private List<(int Col, int Row)> Route((int Col, int Row) from, (int Col, int Row) to)
    {
        var prev = new Dictionary<(int, int), (int, int)>();
        var queue = new Queue<(int Col, int Row)>();
        queue.Enqueue(from);
        prev[from] = from;
        (int, int)[] steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == to) break;
            foreach (var (dc, dr) in steps)
            {
                var next = (cur.Col + dc, cur.Row + dr);
                if (prev.ContainsKey(next) || !Walkable(next.Item1, next.Item2)) continue;
                if (!_open.Contains(next) && !_open.Contains(cur) && Math.Abs(Map.HeightAt(next.Item1, next.Item2) - Map.HeightAt(cur.Col, cur.Row)) > 1) continue;
                prev[next] = cur;
                queue.Enqueue(next);
            }
        }
        var route = new List<(int, int)>();
        if (!prev.ContainsKey(to))
        {
            int n = Math.Max(Math.Abs(to.Col - from.Col), Math.Abs(to.Row - from.Row));
            for (int i = 1; i <= n; i++) route.Add((from.Col + (to.Col - from.Col) * i / n, from.Row + (to.Row - from.Row) * i / n));
            return route;
        }
        for (var c = to; c != from; c = prev[c]) route.Add(c);
        route.Reverse();
        return route;
    }
}

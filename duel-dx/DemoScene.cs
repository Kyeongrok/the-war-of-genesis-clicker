using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 데모가 지금 벌이는 전투 한 판 — <c>Btl\NNNN.btl</c> 에서 그대로 읽는다.
/// </summary>
/// <remarks>
/// 전에는 Btl 0045 를 상수로 박아 두었지만(<see cref="BattleDemoScene"/>), 이제 자료에서 읽어 <b>다음 전투도 이어서</b> 할 수 있다.
/// 배경 맵은 <c>Btl</c> 머리의 맵 번호 → <c>Map\NNNN.map</c> 둘째 워드 → <c>Obt\NNNN.obt</c> 로 이어 찾는다(분석-첫전투).
/// 편 번호는 4 = 내가 움직이는 부대, 3 = 같은 편 AI, 0~2 = 적이다([[분석-전투]] ba-6).
/// <c>NextBattle</c> 은 그 전투의 이벤트 행동 <b>10(다음 전투)</b> 이다 — 0045 는 0046 으로 이어진다.
/// </remarks>
internal sealed record DemoUnit(int ChrCode, int Col, int Row, int Side, int Legion, Facing Facing)
{
    /// <summary>
    /// <c>Btl</c> 배치 레코드가 <b>파일에 직접 들고 있는 번호</b>(레코드 첫 낱말) — 줄 순서가 아니다.
    /// </summary>
    /// <remarks>
    /// 이벤트가 사람을 <c>10000+N</c> 으로 가리킬 때 보는 번호다. <c>Btl 0045</c> 는 줄 순서가 0~5 인데 번호는 7·10·13·16·17·19 이라,
    /// 줄 순서로 찾으면 엉뚱한 사람이 말한다.
    /// </remarks>
    public int Record { get; init; } = -1;

    /// <summary>배치 레코드의 레벨 보정 — 파티 레벨에 이만큼 더한 값이 그 인물의 레벨이 된다.</summary>
    public int LevelOffset { get; init; }

    /// <summary>AI 이동 방식 — 제 차례에 어디로 가려 하는지.</summary>
    public int AiMove { get; init; }

    /// <summary>깨어남 조건과 그 값 — 못 채우면 그 자리에서 쉰다.</summary>
    public int WakeCondition { get; init; }

    public int WakeValue { get; init; }

    /// <summary>
    /// 파트1 부대 — 대장의 인물 차례 번호(파일 칸 9, <c>0x10036790(대장, 이, 1)</c>). 제 차례(<see cref="Record"/>)면 대장, 다른 차례면 그 대장의 부하. 파트2 판은 −1.
    /// </summary>
    public int Part1Leader { get; init; } = -1;

    /// <summary>파트1 — 이 칸은 탈것(<see cref="ChrCode"/>)이고 탄 사람은 이 Chr(20000 + 번호, Btl 인물 칸 15 · <c>0x100364b0</c>). 0 이면 탄 사람 없음.</summary>
    public int Part1Rider { get; init; }

    /// <summary>파트1 — AI 표 <c>Dats\AI.dat</c> 번호(유닛 <c>+0x262</c>, Btl 인물 칸 13 · 행동 705). 사람이 움직이는 편(4)과 파트2 판은 −1.</summary>
    public int Part1AiType { get; init; } = -1;

    /// <summary>편 4 = 내가 움직이는 부대, 3 = 같은 편 AI(동맹), 0~2 = 적.</summary>
    public bool IsAlly => Side >= 3;

    /// <summary>플레이어가 직접 움직이는가 — 동맹 AI(3)는 제 차례에 스스로 움직인다.</summary>
    public bool PlayerControlled => Side == 4;
}

/// <summary>
/// 판에 놓인 물체 하나 — 상자·문·포탑·바리케이트. 규칙은 <see cref="Data"/> 의 종류가 가른다.
/// </summary>
/// <remarks>분석-전투 「물체(오브젝트) 배열 <c>+0x3c74</c>」. 전투 중에 새로 생기지 않는다 — 파일이 놓는 것이 전부다.</remarks>
internal sealed class DemoObject(BattleObjectRecord record, ObjFile data)
{
    public BattleObjectRecord Record { get; } = record;
    public ObjFile Data { get; } = data;
    public int Col => Record.X;
    public int Row => Record.Y;
    public int Hp { get; set; } = data.MaxHp;

    /// <summary>지금 편(<c>+0x78</c>) — 중립(−1) 포탑·힐 크리스탈은 손을 댄 쪽 편이 된다(명령 0x2717).</summary>
    public int Team { get; set; } = record.Team;

    /// <summary>힐 크리스탈(종류 10)의 충전(<c>+0x15c</c>, 차는 한도 = .obj 의 차례 간격)과 다 찼나(<c>+0x160</c>).</summary>
    public int Charge { get; set; }
    public bool Charged { get; set; }
    public bool Alive => !Data.Breakable || Hp > 0;

    /// <summary>
    /// 판에 찍는 발자국 — <c>.obj +12</c> 의 Obt(<c>assets/maps/NNNN.obt</c>). 기준점 = 물체 (x, y) 가 Obt 왼쪽 위(<c>0x10028620</c>, 감사3 R1).
    /// 파일이 없으면 null(그림 폭으로 짐작한다).
    /// </summary>
    public ObtFootprint? Footprint { get; init; }
}

internal sealed record DemoScene(int Id, string Title, string MapFile, int Bgm,
                                 ushort TitleTextId, ushort WinTextId, ushort LoseTextId,
                                 DemoUnit[] Roster, int NextBattle,
                                 IReadOnlyList<(int Col, int Row, Facing Facing)>? Placement = null,
                                 IReadOnlyList<DemoObject>? Objects = null, bool EngineJudgesWipe = true)
{
    /// <summary>Btl 머리 워드 7 — 군단을 쓸 수 있는 전투인가(<see cref="BattleFile.LegionsAllowed"/>, 감사3 L4).</summary>
    public bool LegionsAllowed { get; init; }

    /// <summary>파트1 판의 맵 물체(절 A — Obt 번호 · 칸 x · y, <see cref="Part1MapObjects"/>). 파트2 판은 null — 파트2 물체는 <see cref="Objects"/>.</summary>
    public IReadOnlyList<(int Obt, int X, int Y)>? Part1Objects { get; init; }

    /// <summary>
    /// 파트1 머리 낱말 6(전투 <c>+0xb96e</c>, 기본 80) — 기온. 맵 물체 · 유닛의 온도(<c>+0x4a</c>)가 시간 틱마다 이 값 쪽으로 2 씩 간다
    /// (<c>0x10099f60</c> · <c>0x100351b9</c>). 파트2 판은 0.
    /// </summary>
    public int Part1Ambient { get; init; }

    /// <summary>파트1 머리 낱말 7(전투 <c>+0xb970</c>) — 전장 지형 0 평지 · 1 사막 · 2 정글 · 3 동굴 · 4 산지 · 5 극지 · 6 바다. 1~6 이면 그 적응 어빌리티(85 + 지형)를 가진 유닛이
    /// SPD +8 이고 온도가 기온 대신 80 쪽으로 간다(<c>0x10042930</c>). 파트2 판은 0.</summary>
    public int Part1Terrain { get; init; }

    /// <summary>Btl 머리 워드 2·3 — 첫 화면 가운데 칸(<see cref="BattleFile.StartX"/>, 감사4 C5). 없으면 −1(예전처럼 왼쪽 위).</summary>
    public int StartCol { get; init; } = -1;
    public int StartRow { get; init; } = -1;

    /// <summary>자료를 못 읽을 때 쓰는 첫 전투(예전 상수 그대로).</summary>
    public static DemoScene Fallback { get; } = new(
        BattleDemoScene.BtlId, BattleDemoScene.Title, BattleDemoScene.MapFile, BattleDemoScene.Bgm,
        BattleDemoScene.TitleTextId, BattleDemoScene.WinTextId, BattleDemoScene.LoseTextId,
        [.. BattleDemoScene.Roster.Select(u => new DemoUnit(u.ChrCode, u.Col, u.Row, u.IsAlly ? 4 : 0, 0, u.IsAlly ? Facing.Right : Facing.Left))],
        NextBattle: 46);

    /// <summary>
    /// 판의 맵 그림 — 파트1 판(<see cref="Part1MapPrefix"/>)이면 사용자의 파트1 폴더 <c>Obts</c> 에서(<see cref="Part1Map"/>), 아니면 파트2 Obt 파일에서.
    /// </summary>
    public static ObtMapImage LoadMap(DemoScene scene) =>
        scene.MapFile.StartsWith(Part1MapPrefix, StringComparison.Ordinal)
            ? Part1Map.Load(int.Parse(scene.MapFile[Part1MapPrefix.Length..])) ?? throw new InvalidDataException($"파트1 맵 {scene.MapFile} 을 못 읽었습니다")
            : ObtMap.Load(AssetPack.MapPath(scene.MapFile));

    /// <summary>파트1 판의 맵 이름 앞머리 — 「part1:0173」 은 파트1 <c>Obts3.obt</c>.</summary>
    public const string Part1MapPrefix = "part1:";

    /// <summary>파트1 판인가 — 전투 번호가 <see cref="Part1Game.BattleBase"/> 위다.</summary>
    public bool IsPart1 => Id >= Part1Game.BattleBase;

    /// <summary>
    /// 파트1 전투(<see cref="Part1Battle"/>)를 판으로 — 인물 번호는 <see cref="Part1Game.ChrBase"/> 를 더하고, 안 쓰는 레코드는 뺀다.
    /// 이벤트가 사람을 10000+n(파일 차례)으로 가리키므로 <see cref="DemoUnit.Record"/> 는 파일 차례다. 사건·대사는 BattleScene.LoadEvents 가 Part1Script 로 붙인다.
    /// </summary>
    private static DemoScene? LoadPart1(int id, GameDatabase? db)
    {
        if (Part1Battle.Load(id - Part1Game.BattleBase) is not { } battle) return null;
        static Facing FacingOf(int direction) => direction switch { 0 => Facing.Up, 2 => Facing.Down, 3 => Facing.Right, _ => Facing.Left };
        // AI 표(Dats/AI.dat, 유닛 칸의 AI 번호)를 파트2 AI 칸으로 — 깨어나는 조건(+0x1d: 0 처음 · 1 깨어난 같은 편 거리 · 2 적 거리 · 3 틱)은 번호가 같다.
        // 이동 방식(+0)은 3 「움직이지 않음」만 파트2 의 「목표 없음(제자리)」으로 옮기고, 0 정한 칸 · 1 따라가기 · 2 보통은 보통으로 친다.
        var ais = Part1Ai.LoadAll();
        DemoUnit Unit(Part1BattleUnit u, int index)
        {
            // 증원은 칸 (1000, 1000)에 두었다가 행동 300 이 불러들인다(분석-파트1-전투 2.2) — 파트2 처럼 (0, 0) 대기로 둔다.
            // 그대로 두면 판 위로 쳐져 차례가 오고 카메라가 판 귀퉁이로 갔다(사용자 보고: Btl 20087 적 차례 뒤 카메라가 아래를 비춤).
            if (u.X >= 1000 || u.Y >= 1000) u = u with { X = 0, Y = 0 };
            // 탈것이 적힌 사람은 탈것으로 선다 — 차례 번호(사건이 가리키는 것)는 그대로라 조건 200·201 이 원본처럼 탈것을 본다.
            var unit = u.MountChr > 0
                ? new DemoUnit(Part1Game.ChrBase + u.MountChr, u.X, u.Y, u.Side, 0, FacingOf(u.Direction)) { Record = index, Part1Leader = u.Leader, Part1Rider = Part1Game.ChrBase + u.ChrCode }
                : new DemoUnit(Part1Game.ChrBase + u.ChrCode, u.X, u.Y, u.Side, 0, FacingOf(u.Direction)) { Record = index, Part1Leader = u.Leader };
            if (u.Side == 4 || u.AiType < 0 || u.AiType >= ais.Count) return unit;
            var ai = ais[u.AiType];
            return unit with { AiMove = ai.MoveMode == 3 ? 6 : 0, WakeCondition = ai.Wake, WakeValue = ai.Wake == 3 ? ai.WakeTick : ai.WakeDistance, Part1AiType = u.AiType };
        }
        var roster = battle.Units
            .Select((u, index) => (u, index))
            .Where(x => x.u.Disabled == 0 && x.u.ChrCode >= 0)
            .Select(x => Unit(x.u, x.index))
            .ToArray();
        if (roster.Length == 0) return null;
        // 배치 칸(절 C) — BuildUnits 가 파트1 일행(Part1Progress) 가운데 Btl 에 없는 사람을 세운다.
        var placement = battle.Placement.Select(p => (p.X, p.Y, FacingOf(p.Direction))).ToList();
        string title = battle.Line(battle.TitleLine) is { Length: > 0 } t ? t : $"파트1 전투 {id - Part1Game.BattleBase}";
        int bgm = battle.Bgm >= 0 ? Part1Game.BgmBase + battle.Bgm : 0;   // 머리 낱말 8 — BGMs\NNNN.bgm, -1 은 없음
        // 제목 · 승리 · 패배 조건 줄(머리 낱말 3·4·5, .dlg 줄)은 파트1 글이라 글 번호를 따로 붙인다 — 시스템 메뉴 「전투 조건」이 보인다.
        ushort Text(int line) => db?.Part1TextId(Part1Game.PlainText(battle.Line(line)).Replace('\n', ' ')) ?? 0;
        return new DemoScene(id, title, $"{Part1MapPrefix}{battle.ObtId:D4}", bgm, Text(battle.TitleLine), Text(battle.WinLine), Text(battle.LoseLine),
                             roster, 0, placement, [], EngineJudgesWipe: false)
        {
            StartCol = battle.StartX, StartRow = battle.StartY,
            Part1Objects = battle.Objects,       // 절 A — 원본 로더 0x1002d27b 가 판에 찍는 Obt 물체(보이지 않는 벽 · 통 · 상자 · 문 · 나무 …)
            Part1Ambient = battle.Ambient,       // 머리 낱말 6 — 기온(물체 · 유닛 온도가 이쪽으로 간다)
            Part1Terrain = battle.Terrain,       // 머리 낱말 7 — 전장 지형(적응 어빌리티 86~91)
        };
    }

    /// <summary>그 번호의 전투를 assets 에서 읽는다. 자료가 없으면 null.</summary>
    public static DemoScene? Load(int id, GameDatabase? db)
    {
        try
        {
            if (id >= Part1Game.BattleBase) return LoadPart1(id, db);
            var files = GameFiles.FromFolder(AssetsFolder.Find("data"));
            if (BattleFile.Parse(id, files.Read("Btl", $"{id:D4}.btl")) is not { } battle) return null;
            if (BattleFile.ObtOfMap(files.Read("Map", $"{battle.MapId:D4}.map")) is not { } obt) return null;

            string mapFile = $"{obt:D4}.obt";
            if (!File.Exists(AssetPack.MapPath(mapFile))) return null;

            var roster = battle.Units
                .Where(u => u.ChrCode > 0)
                .Select(u => new DemoUnit(u.ChrCode, u.X, u.Y, u.Side, u.Squad, u.Facing)
                {
                    Record = u.No,
                    LevelOffset = u.LevelOffset,
                    AiMove = u.AiMove,
                    WakeCondition = u.WakeCondition,
                    WakeValue = u.WakeValue,
                })
                .ToArray();
            if (roster.Length == 0) return null;

            int next = 0;
            if (BattleEvents.Parse(files.Read("Btl", $"{id:D4}.btl"), out _) is { } events)
                foreach (var action in events.SelectMany(e => e.Actions))
                    if (action.Code == 10 && next == 0) next = action.Args[0];

            string title = db?.T(battle.TitleId) is { Length: > 0 } t ? t : $"전투 {id}";
            // 아군을 미리 안 세운 전투는 「배치 칸」에 파티를 세운다(원본 배치 창 자리).
            var placement = battle.Placement
                .Select(p => (p.X, p.Y, p.Direction switch { 0 => Facing.Up, 2 => Facing.Down, 3 => Facing.Right, _ => Facing.Left }))
                .ToList();
            // 판에 놓인 물체 — <c>Obj</c> 파일이 없는 것은 그리지도 못하니 뺀다.
            // 맵이 놓는 물체(문·장식)도 같이 세운다 — Btl 것과 번호가 겹치지 않게 100 을 더해 온다.
            // 물체마다 제 Obt 발자국도 읽는다 — 원본 LoadBtl 이 판에 찍는 것(0x10062b11, 감사3 R1).
            ObtFootprint? FootprintOf(int obtId)
            {
                string path = AssetPack.MapPath($"{obtId:D4}.obt");
                return File.Exists(path) ? ObtMap.ReadFootprint(File.ReadAllBytes(path)) : null;
            }
            var objects = battle.Objects.Concat(BattleFile.ObjectsOfMap(files.Read("Map", $"{battle.MapId:D4}.map")))
                .Select(o => ObjFile.Parse(o.ObjId, files.Read("Obj", $"{o.ObjId:D4}.obj")) is { } data
                             ? new DemoObject(o, data) { Footprint = FootprintOf(data.ObtId) } : null)
                .OfType<DemoObject>()
                .ToList();

            return new DemoScene(id, title, mapFile, battle.Bgm, battle.TitleId, battle.WinId, battle.LoseId,
                                 roster, next, placement, objects, battle.EngineJudgesWipe) { LegionsAllowed = battle.LegionsAllowed, StartCol = battle.StartX, StartRow = battle.StartY };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }
}

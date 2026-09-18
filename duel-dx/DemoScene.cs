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

    /// <summary>편 4 = 내가 움직이는 부대, 3 = 같은 편 AI(동맹), 0~2 = 적.</summary>
    public bool IsAlly => Side >= 3;

    /// <summary>플레이어가 직접 움직이는가 — 동맹 AI(3)는 제 차례에 스스로 움직인다.</summary>
    public bool PlayerControlled => Side == 4;
}

internal sealed record DemoScene(int Id, string Title, string MapFile, int Bgm,
                                 ushort TitleTextId, ushort WinTextId, ushort LoseTextId,
                                 DemoUnit[] Roster, int NextBattle,
                                 IReadOnlyList<(int Col, int Row, Facing Facing)>? Placement = null)
{
    /// <summary>자료를 못 읽을 때 쓰는 첫 전투(예전 상수 그대로).</summary>
    public static DemoScene Fallback { get; } = new(
        BattleDemoScene.BtlId, BattleDemoScene.Title, BattleDemoScene.MapFile, BattleDemoScene.Bgm,
        BattleDemoScene.TitleTextId, BattleDemoScene.WinTextId, BattleDemoScene.LoseTextId,
        [.. BattleDemoScene.Roster.Select(u => new DemoUnit(u.ChrCode, u.Col, u.Row, u.IsAlly ? 4 : 0, 0, u.IsAlly ? Facing.Right : Facing.Left))],
        NextBattle: 46);

    /// <summary>그 번호의 전투를 assets 에서 읽는다. 자료가 없으면 null.</summary>
    public static DemoScene? Load(int id, GameDatabase? db)
    {
        try
        {
            var files = GameFiles.FromFolder(AssetsFolder.Find("data"));
            if (BattleFile.Parse(id, files.Read("Btl", $"{id:D4}.btl")) is not { } battle) return null;
            if (BattleFile.ObtOfMap(files.Read("Map", $"{battle.MapId:D4}.map")) is not { } obt) return null;

            string mapFile = $"{obt:D4}.obt";
            if (!File.Exists(Path.Combine(AssetsFolder.Find("maps"), mapFile))) return null;

            var roster = battle.Units
                .Where(u => u.ChrCode > 0)
                .Select(u => new DemoUnit(u.ChrCode, u.X, u.Y, u.Side, u.Squad, u.Facing) { Record = u.No })
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
            return new DemoScene(id, title, mapFile, battle.Bgm, battle.TitleId, battle.WinId, battle.LoseId, roster, next, placement);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }
}

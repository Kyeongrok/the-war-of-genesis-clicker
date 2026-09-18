namespace WarOfGenesis.Assets;

/// <summary>Btl 인물 레코드(29바이트) — 번호, Chr, 칸, 방향, 편(4 플레이어·3 동맹 AI·0~2 적), 레벨 보정.</summary>
public sealed record BattleUnitRecord(int No, int ChrCode, int X, int Y, int Direction, int Side, int LevelOffset)
{
    /// <summary>For.dat 편대 번호(파일 15) — 0 이 아니면 편대원이 대장 옆에 붙는다.</summary>
    public int Squad { get; init; }

    /// <summary>AI 이동 방식(꼬리 12B 의 첫 워드, 유닛 <c>+0x4dc</c>) 0~5 — ba-6, 이름은 가설.</summary>
    public int AiMove { get; init; }

    /// <summary>AI 깨어나는 조건(꼬리 +8, 유닛 <c>+0x4e4</c>): 0 처음부터, 1 깨어난 같은 편과 거리 ≤ 값, 2 적과 거리 ≤ 값, 3 시간 틱 ≥ 값.</summary>
    public int WakeCondition { get; init; }

    /// <summary>깨어나는 조건 값(꼬리 +10, 유닛 <c>+0x4e6</c>).</summary>
    public int WakeValue { get; init; }

    /// <summary>방향 바이트(0 뒤, 1 옆(왼쪽), 2 앞, 3 반대쪽 옆) → 바라보는 쪽(<c>SetAction 0x10072820</c>).</summary>
    public Facing Facing => Direction switch { 0 => Facing.Up, 2 => Facing.Down, 3 => Facing.Right, _ => Facing.Left };
}

/// <summary>Btl 오브젝트 레코드(11워드) — Obj 번호, 칸, 편.</summary>
/// <summary>
/// <c>Btl</c> 물체 레코드(11낱말) — 어느 <c>Obj</c> 를 어느 칸에 어느 편으로 놓을지.
/// </summary>
/// <remarks>
/// 상자는 <see cref="ItemId"/> 나 <see cref="Gold"/> 둘 중 하나를 들고 있다(자료 541칸이 빠짐없이 그렇다).
/// 남은 낱말 셋은 「이어진 물체」 자리인데 시판 자료에서는 다 비어 있다.
/// </remarks>
public sealed record BattleObjectRecord(int No, int ObjId, int X, int Y, int Team)
{
    /// <summary>상자 속 아이템(<c>Itm</c> 번호), 없으면 0.</summary>
    public int ItemId { get; init; }

    /// <summary>상자 속 돈, 없으면 0.</summary>
    public int Gold { get; init; }
}

/// <summary>아군 배치 칸(6워드) — 칸과 방향.</summary>
public sealed record BattlePlacementCell(int X, int Y, int Direction);

/// <summary>
/// <c>Btl/NNNN.btl</c> 한 판의 머리·인물·오브젝트·배치 칸. 이벤트 절은 <see cref="BattleEvents"/> 가 읽는다.
/// </summary>
/// <remarks>
/// 배치는 옵시디안 분석-전투(ba-1·ba-6)·<c>tools/re/btl_dump.py</c>:
/// 머리 u16×10(판, 맵, 초기 위치 x·y, 이름 txr, 승리 txr, 패배 txr, ?, BGM, ?) → u16 n, u16, n×인물 29B
/// (u16 번호, u16 Chr, u16 x, u16 y, u8 방향, u16 편, u16 ?, u16 레벨 보정, u16 편대, 12B) → u16 n, u16, n×11워드 오브젝트
/// → u16 n, u16, n×6워드 배치 칸. 옛 형식 파일 11개는 이 꼴과 안 맞아 <see cref="Units"/> 가 비고 <see cref="ParseError"/> 가 찬다.
/// </remarks>
public sealed record BattleFile(int Id, int MapId, ushort TitleId, ushort WinId, ushort LoseId, int Bgm,
                                IReadOnlyList<BattleUnitRecord> Units, IReadOnlyList<BattleObjectRecord> Objects,
                                IReadOnlyList<BattlePlacementCell> Placement, string ParseError)
{
    public static BattleFile? Parse(int id, byte[]? b)
    {
        if (b == null || b.Length < 20) return null;
        int o = 0;
        int H() { int v = BitConverter.ToInt16(b, o); o += 2; return v; }
        int U() { int v = BitConverter.ToUInt16(b, o); o += 2; return v; }

        var hdr = Enumerable.Range(0, 10).Select(_ => U()).ToArray();
        var units = new List<BattleUnitRecord>();
        var objects = new List<BattleObjectRecord>();
        var placement = new List<BattlePlacementCell>();
        string error = "";
        try
        {
            int na = U(); U();
            for (int i = 0; i < na; i++)
            {
                int no = H(), chr = H(), x = H(), y = H();
                int dir = b[o++];
                int side = H(); H(); int lv = H(); int squad = H();
                int aiMove = BitConverter.ToInt16(b, o), wake = BitConverter.ToInt16(b, o + 8), wakeValue = BitConverter.ToInt16(b, o + 10);
                o += 12;
                units.Add(new BattleUnitRecord(no, chr, x, y, dir, side, lv)
                {
                    Squad = squad, AiMove = aiMove, WakeCondition = wake, WakeValue = wakeValue,
                });
            }
            int nb = U(); U();
            for (int i = 0; i < nb; i++)
            {
                var w = Enumerable.Range(0, 11).Select(_ => H()).ToArray();
                objects.Add(new BattleObjectRecord(w[0], w[1], w[2], w[3], w[4]) { ItemId = w[5], Gold = w[7] });
            }
            int nc = U(); U();
            for (int i = 0; i < nc; i++)
            {
                var w = Enumerable.Range(0, 6).Select(_ => H()).ToArray();
                placement.Add(new BattlePlacementCell(w[2], w[3], w[4]));
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "레코드가 파일 끝을 넘습니다(옛 형식으로 보임)";
            units.Clear(); objects.Clear(); placement.Clear();
        }
        catch (ArgumentException)
        {
            error = "레코드가 파일 끝을 넘습니다(옛 형식으로 보임)";
            units.Clear(); objects.Clear(); placement.Clear();
        }
        return new BattleFile(id, hdr[1], (ushort)hdr[4], (ushort)hdr[5], (ushort)hdr[6], hdr[8], units, objects, placement, error);
    }

    /// <summary><c>Map/NNNN.map</c> 둘째 워드 = 배경 Obt 번호(<c>btl_dump.parse_map</c>).</summary>
    public static int? ObtOfMap(byte[]? mapFile) => mapFile is { Length: >= 4 } ? BitConverter.ToUInt16(mapFile, 2) : null;

    public static string SideName(int side) => side switch
    {
        4 => "플레이어",
        3 => "동맹(AI)",
        _ => $"적({side})",
    };
}

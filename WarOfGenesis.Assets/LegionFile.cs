namespace WarOfGenesis.Assets;

/// <summary>
/// <c>Dat\For.dat</c> 의 군단 하나 — 대장 밑에 부하 최대 여섯이 진형을 지어 함께 싸우는 무리.
/// </summary>
/// <remarks>
/// 옵시디안 분석-군단 1절: 파일 머리 <c>u16 0 · u16 레코드 수 · u16 최대 번호</c>, 레코드 <b>59바이트</b> —
/// 0 번호 · 2 이름 TXR · 4 부하 Chr 여섯 · 16 진형(0~5) · 17·19·21·23·25 부하 보정(LP·TP·PSY·DEX·DEP) ·
/// 27 군단기 (어빌리티, 필요 세력, 대장 Chr)×5 · 57 설명 TXR.
/// </remarks>
/// <param name="Formation">0 학익진 · 1 일자형 · 2 십자형 · 3 역학익진 · 4 젓가락형 · 5 이자형 (이름 TXR 813 + 진형).</param>
public sealed record LegionData(int Id, ushort NameId, ushort[] Members, byte Formation,
                                ushort LpBonus, ushort PsyBonus, ushort DepBonus,
                                (ushort Ability, ushort Power, ushort Leader)[] Skills, ushort DescriptionId)
{
    /// <summary>진형별 부하 여섯의 칸 자리(방향 0 기준) — DLL 표 <c>0x10164838</c>.</summary>
    public static readonly (int Dx, int Dy)[][] FormationCells =
    [
        [(0, -2), (-1, -1), (1, -1), (-2, 0), (2, 0), (0, -1)],   // 0 학익진
        [(-1, 0), (1, 0), (-2, 0), (2, 0), (0, -2), (0, 2)],      // 1 일자형
        [(0, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)],      // 2 십자형
        [(0, 2), (-1, 1), (1, 1), (-2, 0), (2, 0), (0, 1)],       // 3 역학익진
        [(-1, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (1, 1)],    // 4 젓가락형
        [(0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)],    // 5 이자형
    ];

    /// <summary>진형 이름 TXR — 813 + 진형.</summary>
    public ushort FormationNameId => (ushort)(813 + Formation);

    /// <summary>파일을 통째로 읽어 번호로 찾을 수 있게 돌려준다.</summary>
    public static Dictionary<int, LegionData> ParseAll(byte[]? b)
    {
        var result = new Dictionary<int, LegionData>();
        if (b == null || b.Length < 6) return result;
        ushort U(int o) => BitConverter.ToUInt16(b, o);
        int count = U(2);
        for (int i = 0, o = 6; i < count && o + 59 <= b.Length; i++, o += 59)
        {
            int id = U(o);
            result[id] = new LegionData(
                id, U(o + 2),
                [.. Enumerable.Range(0, 6).Select(k => U(o + 4 + 2 * k)).Where(m => m != 0)],
                b[o + 16], U(o + 17), U(o + 21), U(o + 25),
                [.. Enumerable.Range(0, 5).Select(k => (U(o + 27 + 6 * k), U(o + 29 + 6 * k), U(o + 31 + 6 * k)))
                    .Where(s => s.Item1 != 0)],
                U(o + 57));
        }
        return result;
    }
}

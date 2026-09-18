namespace WarOfGenesis.Assets;

/// <summary>
/// <c>Obj\NNNN.obj</c> — 전투판에 놓이는 물체(상자·문·포탑·바리케이트)의 낱낱 규격.
/// </summary>
/// <remarks>
/// 분석-전투 「물체(오브젝트) 배열 <c>+0x3c74</c>」. 로더는 <c>0x100e5840</c>, 종류표는 <c>0x1016f438</c>(24바이트 레코드).
/// <para>
/// <b>종류</b>가 규칙을 다 가른다 — 0 장식 · 1 문 · 2 보물 상자 · 3 움직이는 함정 · 4 스위치문 · 6 스위치 ·
/// 7 바리케이트(적·중립이면 때릴 수 있다) · 8 폭탄 상자 · 9 포탑(차례를 받는다) · 10 아군 힐 크리스탈.
/// 문(1·4)만 <b>걸어 들어갈 목표 칸</b>이 될 수 없고, 나머지는 길을 안 막는다(<c>0x100746b0</c>).
/// </para>
/// </remarks>
public sealed record ObjFile(int Id, int NameId, int Kind, int Level, int MaxHp,
                             int ObtId, int SpriteId, int DrawW, int DrawH,
                             int Attack, int Radius, int TurnEvery, int WorkId)
{
    /// <summary>그 종류가 걸어 들어갈 목표 칸을 막나 — 문과 스위치문뿐이다.</summary>
    public bool BlocksStanding => Kind is 1 or 4;

    /// <summary>때릴 수 있는 종류인가 — 바리케이트·포탑·힐 크리스탈.</summary>
    public bool Breakable => Kind is 7 or 9 or 10;

    /// <summary>제 차례를 받는 종류인가 — 움직이는 함정·포탑·힐 크리스탈.</summary>
    public bool Acts => Kind is 3 or 9 or 10;

    public static ObjFile? Parse(int id, byte[]? b)
    {
        if (b == null || b.Length < 33) return null;
        ushort U(int o) => BitConverter.ToUInt16(b, o);
        return new ObjFile(id, U(2), U(4), U(6), (int)BitConverter.ToUInt32(b, 8),
                           U(12), U(14), U(16), U(18), U(23), U(25), U(27), U(31));
    }
}

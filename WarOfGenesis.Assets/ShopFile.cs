using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 상점 파일 <c>Shp\NNNN.shp</c> — 모세스 상점 페이지가 읽는 레코드 하나가 파일 하나다.
/// </summary>
/// <remarks>
/// 옵시디안 <b>분석-모세스 12절「상점(SHOP) 페이지」</b> 그대로(파일 끝까지 확인한 배치):
/// <c>0x00</c> 안 씀 · <c>0x02</c> 상점 이름 TXR · <c>0x04</c> 종류(<b>2 = 장비 상점</b>, 그 밖 1) ·
/// <c>0x06</c> 점주 Obs · <c>0x08</c> 점주 이름 TXR · <c>0x0a</c> <b>가격률 %</b>(1~999, 그 밖이면 100) ·
/// <c>0x0c</c> 부터 <b>파는 아이템 번호 15칸</b>(0 은 빈 칸) — 모두 합쳐 42바이트다.
/// 파는 값은 <c>Itm 가격 × 가격률 / 100</c>, 되파는 값은 그 절반.
/// 상점 이름·점주 이름 TXR 은 원본 코드가 부르지 않는다(배경 그림에 인쇄돼 있다는 것이 분석의 가설) — 자료에는 들어 있어 여기서는 보여 준다.
/// </remarks>
public sealed record ShopFile(int Id, int NameText, int Kind, int OwnerObs, int OwnerNameText, int Rate, IReadOnlyList<int> Items)
{
    /// <summary>종류 2 — 장비 상점(원본은 캐릭터 비교창을 함께 연다).</summary>
    public bool IsEquipment => Kind == 2;

    /// <summary>파는 값 = 아이템 가격 × 가격률 / 100.</summary>
    public int PriceOf(uint price) => (int)(price * Rate / 100);

    public static ShopFile? Parse(int id, byte[]? b)
    {
        if (b == null || b.Length < 42) return null;
        short H(int o) => BitConverter.ToInt16(b, o);
        int rate = H(10) is > 0 and < 1000 ? H(10) : 100;
        return new ShopFile(id, H(2), H(4), H(6), H(8), rate,
                            [.. Enumerable.Range(0, 15).Select(i => (int)H(12 + 2 * i)).Where(v => v > 0)]);
    }

    /// <summary>게임 자료의 <c>Shp</c> 폴더를 통째로 읽는다(번호 → 상점).</summary>
    public static Dictionary<int, ShopFile> LoadAll(GameFiles files)
    {
        var map = new Dictionary<int, ShopFile>();
        foreach (string name in files.List("Shp", ".shp").Keys)
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(name), out int id)) continue;
            if (Parse(id, files.Read("Shp", name)) is { } shop) map[id] = shop;
        }
        return map;
    }
}

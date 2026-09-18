using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>
/// <c>Tlk\NNNN.Tlc</c>(챕터 대사) — 번호로 찾는 글 표. <c>Txr.dat</c> 과 서식이 같다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-모세스 11절(2026-09-19 개정): 모세스 통신 페이지의 대사는 TXR 이 아니라 <b>그 챕터와 같은 번호의 Tlk 파일</b>에서 온다
/// (<c>0x1004a650</c> 이 Tlk 표를 본다 — TXR 을 보는 <c>0x1004a3f0</c> 과 다른 함수다. 챕터 장면이 <c>0x1004a420(갈래 4, 챕터번호)</c> 로 읽는다).
/// 배치: 머리 10바이트(<c>u16 서식</c>(TXR 1 · Tlk 0), <c>u16 글 수</c>, <c>u16 최대 번호</c>, <c>u32 글뭉치 크기</c>) +
/// <c>수 ×(u16 번호, u32 자리, u32 길이)</c> + 글뭉치(<c>10×(수+1)</c> 부터). 글은 <b>cp949 널 끝 문자열</b>이다.
/// </remarks>
public sealed class TalkTable
{
    private readonly Dictionary<int, string> _byId = [];

    private static readonly Lazy<Encoding> Cp949 = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    });

    public int Count => _byId.Count;

    /// <summary>그 번호의 대사. 없으면 빈 글.</summary>
    public string this[int id] => _byId.GetValueOrDefault(id, "");

    public static TalkTable? Parse(byte[]? b)
    {
        if (b == null || b.Length < 10) return null;
        var table = new TalkTable();
        try
        {
            int count = BitConverter.ToUInt16(b, 2);
            int body = 10 * (count + 1);
            for (int i = 0; i < count; i++)
            {
                int o = 10 + 10 * i;
                int id = BitConverter.ToUInt16(b, o);
                int at = body + (int)BitConverter.ToUInt32(b, o + 2);
                if (at < 0 || at >= b.Length) continue;
                int end = Array.IndexOf(b, (byte)0, at);
                if (end < 0) end = b.Length;
                table._byId[id] = Cp949.Value.GetString(b, at, end - at);
            }
        }
        catch (ArgumentException) { /* 배치가 안 맞으면 읽은 데까지 */ }
        return table;
    }
}

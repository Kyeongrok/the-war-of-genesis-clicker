using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>
/// 편지 하나 — <c>Dat\MAIL.DAT</c> 한 레코드.
/// </summary>
/// <remarks>
/// 옵시디안 <b>분석-모세스 10절「메일(MAIL BOX) 페이지」</b>: 머리 <c>u16, u16 편지 수, u16 최대 번호</c> 뒤에
/// <c>번호 · 보낸이 Chr · 제목 TXR · ? · 길이 · 본문 바이트 · 조건 세 칸 · ?</c> 이 이어진다.
/// <b>본문만은 TXR 이 아니라 파일 안에 그대로(CP949) 들어 있다.</b>
/// 목록 한 줄은 원본에서 <c>"보낸이 이름 : 제목"</c> 으로 그린다.
/// </remarks>
public sealed record MailEntry(int Id, int Sender, ushort TitleText, string Body, int Unknown, IReadOnlyList<int> Conditions)
{
    /// <summary>목록·표에 쓸 본문 앞부분 — 줄바꿈을 띄어쓰기로 바꿔 한 줄로 만든다.</summary>
    public string Preview(int length = 80)
    {
        string flat = Body.Replace("\r", " ").Replace("\n", " ").Replace("$n", " ").Trim();
        while (flat.Contains("  ")) flat = flat.Replace("  ", " ");
        return flat.Length <= length ? flat : flat[..length] + "…";
    }
}

/// <summary><c>Dat\MAIL.DAT</c> 전체.</summary>
public static class MailFile
{
    private static readonly Lazy<Encoding> Cp949 = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    });

    /// <summary>게임 자료에서 편지함을 읽는다 — 파일이 없으면 빈 목록.</summary>
    public static List<MailEntry> Load(GameFiles files) => ParseAll(files.Read("Dat", "MAIL.DAT"));

    public static List<MailEntry> ParseAll(byte[]? b)
    {
        var list = new List<MailEntry>();
        if (b == null || b.Length < 6) return list;
        try
        {
            int count = BitConverter.ToUInt16(b, 2), o = 6;
            for (int i = 0; i < count && o + 10 <= b.Length; i++)
            {
                int id = BitConverter.ToUInt16(b, o), sender = BitConverter.ToUInt16(b, o + 2);
                ushort title = BitConverter.ToUInt16(b, o + 4);
                int unknown = BitConverter.ToUInt16(b, o + 6);
                int length = BitConverter.ToUInt16(b, o + 8);
                if (o + 10 + length + 8 > b.Length) break;
                string body = Cp949.Value.GetString(b, o + 10, length).TrimEnd('\0');
                int tail = o + 10 + length;
                list.Add(new MailEntry(id, sender, title, body, unknown,
                                       [.. Enumerable.Range(0, 3).Select(k => (int)BitConverter.ToInt16(b, tail + 2 * k))]));
                o = tail + 8;                       // 본문 뒤에 조건 세 칸 + 아직 모르는 한 워드
            }
        }
        catch (ArgumentException) { /* 배치가 안 맞으면 읽은 데까지만 */ }
        return list;
    }
}

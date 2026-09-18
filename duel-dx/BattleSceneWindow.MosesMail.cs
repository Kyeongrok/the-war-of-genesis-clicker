using System.Text;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 모세스 메일 페이지(mo-1, 페이지 1) — 편지함과 편지 보기.
/// </summary>
/// <remarks>
/// 옵시디안 분석-모세스 10절: 배경은 Bgr 0094(「Mail Box · Mail List · Mosses Mail System M.M.S · Exit」 글자가 그림에 있다).
/// 목록은 <b>(151, 70)</b> 에 <b>427×19 열일곱 줄</b>, 줄 틀은 Obs 1291 모션 0 @ (13,−2), 줄 글은 <c>"%s : %s"</c>(보낸이 이름 : 제목).
/// 안 읽은 편지와 읽은 편지는 그리기 세기(2 / 0xff)로 갈린다 — 여기서는 안 읽은 것을 밝게, 읽은 것을 흐리게 그린다.
/// 줄을 누르면 뷰어 <b>(164, 120) 313×239</b> 가 뜨고 닫으면 읽음 표시. 나가기 단추는 Obs 0287 @ (455, 430).
/// 본문은 TXR 이 아니라 <c>Dat\MAIL.DAT</c> 안에 그대로 들어 있다 —
/// 머리 <c>u16, u16 편지 수, u16 최대 번호</c>, 레코드 <c>번호·보낸이 Chr·발신지 TXR·Bgm 번호·길이·본문 바이트·조건 3칸·안 쓰는 칸</c>.
/// 원본은 챕터의 메일 트리거로 우편함에 하나씩 쌓지만, 이 데모에는 우편함이 없어 <b>파일에 든 편지를 모두</b> 보여 준다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int MailListX = 151, MailListY = 70, MailRowW = 427, MailRowH = 19, MailRows = 17;
    private const int MailViewX = 164, MailViewY = 120, MailViewW = 313, MailViewH = 239;
    private const int MailRowObs = 1291;

    private List<MosesMail>? _mails;
    private readonly HashSet<int> _mailRead = [];
    private int _mailTop, _mailOpen = -1;

    private List<MosesMail> Mails()
    {
        if (_mails != null) return _mails;
        return _mails = _db?.Files.Read("Dat", "MAIL.DAT") is { } bytes ? MosesMail.ParseAll(bytes) : [];
    }

    private string SenderName(int chrCode) =>
        _db?.Character(chrCode) is { } c ? _db.T(c.NameId) : $"Chr {chrCode}";

    private int MailRowAt(int bx, int by)
    {
        var (ox, oy) = MosesOrigin();
        if (bx < ox + MailListX || bx >= ox + MailListX + MailRowW) return -1;
        int row = (by - oy - MailListY) / MailRowH;
        if (row < 0 || row >= MailRows) return -1;
        int index = _mailTop + row;
        return index < Mails().Count ? index : -1;
    }

    /// <summary>메일 페이지가 열려 있으면 클릭을 처리하고 true.</summary>
    private bool OnMosesMailClick(int bx, int by)
    {
        if (_mosesPage != 1) return false;
        var (ox, oy) = MosesOrigin();
        int x = bx - ox, y = by - oy;

        if (_mailOpen >= 0)                                   // 뷰어는 아무 데나 누르면 닫히고 읽음이 된다
        {
            _mailRead.Add(_mailOpen);
            _mailOpen = -1;
            return true;
        }
        if (x >= 455 && x < 618 && y >= 430 && y < 457) { Play(576); MosesGoBack(); return true; }

        int index = MailRowAt(bx, by);
        if (index >= 0) { _mailOpen = index; Play(MosesClickSound); }
        return true;
    }

    /// <summary>바퀴 대신 — 목록 위·아래 끝을 누르면 한 쪽씩 넘긴다.</summary>
    private void ScrollMail(int delta)
    {
        int max = Math.Max(0, Mails().Count - MailRows);
        _mailTop = Math.Clamp(_mailTop + delta, 0, max);
    }

    private void DrawMosesMail(int ox, int oy, int tick)
    {
        var mails = Mails();
        for (int r = 0; r < MailRows; r++)
        {
            int index = _mailTop + r;
            if (index >= mails.Count) break;
            var mail = mails[index];
            int rx = ox + MailListX, ry = oy + MailListY + r * MailRowH;
            DrawUi(MailRowObs, 0, tick, rx + 13, ry - 2, UiBlend.Alpha);
            bool read = _mailRead.Contains(index);
            string line = $"{SenderName(mail.Sender)} : {_db?.T(mail.OriginText)}";
            DrawText(line, rx + 24, ry + 2, read ? DimGray : 0xFFFFFF80, 11);
        }

        if (!DrawUi(MosesExitObs, 0, tick, ox + 455, oy + 430, UiBlend.Alpha))
            DrawText("EXIT", ox + 455, oy + 434, White);

        if (_mailOpen >= 0 && _mailOpen < mails.Count) DrawMailView(ox, oy, mails[_mailOpen]);
    }

    /// <summary>편지 보기 — 원본 창 틀에 보낸이·제목·본문.</summary>
    private void DrawMailView(int ox, int oy, MosesMail mail)
    {
        int x = ox + MailViewX, y = oy + MailViewY + FrameTitleH;
        DarkenRect(x - 1, y - FrameTitleH - 1, MailViewW + 2, MailViewH + FrameTitleH + 2);
        DrawGameFrame(x, y, MailViewW, MailViewH, _db?.T(mail.OriginText) ?? "");
        DrawText($"From. {SenderName(mail.Sender)}", x + 12, y + 8, 0xFFFFFF80, 12);

        int ty = y + 30;
        foreach (string line in WrapText(mail.Body, MailViewW - 24, 12f))
        {
            if (ty > y + MailViewH - 16) break;
            DrawText(line, x + 12, ty, White, 12);
            ty += 16;
        }
    }

    /// <summary>글을 칸 너비에 맞춰 줄로 나눈다(원본은 여러 줄 글 객체가 한다).</summary>
    private List<string> WrapText(string text, int width, float size)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (char ch in paragraph)
            {
                line.Append(ch);
                if (GetText(line.ToString(), White, size).W <= width) continue;
                line.Length--;
                lines.Add(line.ToString());
                line.Clear().Append(ch);
            }
            lines.Add(line.ToString());
        }
        return lines;
    }
}

/// <summary><c>Dat\MAIL.DAT</c> 의 편지 하나.</summary>
internal sealed record MosesMail(int Id, int Sender, ushort OriginText, ushort Bgm, string Body)
{
    private static readonly Lazy<Encoding> Cp949 = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    });

    public static List<MosesMail> ParseAll(byte[] b)
    {
        var list = new List<MosesMail>();
        try
        {
            int count = BitConverter.ToUInt16(b, 2), o = 6;
            for (int i = 0; i < count && o + 10 <= b.Length; i++)
            {
                int id = BitConverter.ToUInt16(b, o), sender = BitConverter.ToUInt16(b, o + 2);
                ushort origin = BitConverter.ToUInt16(b, o + 4);     // 「제목」이 아니라 발신지 TXR(11절 정정)
                ushort bgm = BitConverter.ToUInt16(b, o + 6);        // 편지를 열 때 같이 트는 Bgm 번호(자료에는 쓰는 편지가 없다)
                int length = BitConverter.ToUInt16(b, o + 8);
                if (o + 10 + length > b.Length) break;
                string body = Cp949.Value.GetString(b, o + 10, length).TrimEnd('\0');
                list.Add(new MosesMail(id, sender, origin, bgm, body));
                o += 10 + length + 8;                      // 본문 뒤에 조건 3칸 + ?
            }
        }
        catch (ArgumentException) { /* 배치가 안 맞으면 읽은 데까지 */ }
        return list;
    }
}

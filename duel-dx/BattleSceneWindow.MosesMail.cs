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
/// 우편함은 메일 페이지에 들어갈 때 <see cref="DeliverMail"/> 가 <b>지금 챕터 Chp 의 메일 표</b>에 든 편지 가운데 조건(깃발)을 채운 것을
/// 넣어 채운다(원본 <c>0x100fc6e0</c>, 분석-모세스 「메일 배달과 진행 연동 (mo-mail)」). 목록은 최근 편지가 맨 위다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int MailListX = 151, MailListY = 70, MailRowW = 427, MailRowH = 19, MailRows = 17;
    private const int MailViewX = 164, MailViewY = 120, MailViewW = 313, MailViewH = 239;
    private const int MailRowObs = 1291;

    private List<MosesMail>? _mails;

    /// <summary>우편함 — 도착한 편지 번호(도착 차례). 원본 파티 <c>+0xa98/+0xa9a</c>. 세이브에 실린다.</summary>
    private readonly List<int> _mailbox = [];

    /// <summary>읽은 편지 번호(원본 파티 <c>+0xe9a</c>). 세이브에 실린다.</summary>
    private readonly HashSet<int> _mailRead = [];
    private int _mailTop, _mailOpen = -1;

    /// <summary><c>MAIL.DAT</c> 의 편지 전부(번호 차례).</summary>
    private List<MosesMail> AllMails()
    {
        if (_mails != null) return _mails;
        return _mails = _db?.Files.Read("Dat", "MAIL.DAT") is { } bytes ? MosesMail.ParseAll(bytes) : [];
    }

    /// <summary>우편함에 든 편지 — 목록 차례로(최근 편지가 맨 위, 원본 줄 i = 우편함[개수−1−i]).</summary>
    private List<MosesMail> Mails()
    {
        var all = AllMails();
        return [.. Enumerable.Reverse(_mailbox).Select(id => all.FirstOrDefault(m => m.Id == id)).Where(m => m != null)!];
    }

    /// <summary>우편함 상한 — 원본은 0x1fe 칸까지만 넣는다.</summary>
    private const int MailboxLimit = 0x1ff;

    /// <summary>
    /// 새 편지 배달 — <b>지금 챕터 Chp 의 메일 표</b>(파일 차례)를 훑어, 아직 안 온 편지 가운데 조건을 채운 것을 우편함에 넣는다.
    /// 메일 페이지에 들어갈 때 한 번뿐이다(<c>0x100febef</c> → <c>0x100fc6e0</c>). 늘어난 수를 돌려준다(늘었으면 Snd 571).
    /// </summary>
    /// <remarks>
    /// 예전에는 <c>MAIL.DAT</c> 92통을 전부 훑어 시험 챕터(0001·0040)에만 든 「디에네 : 테스트방어구」 같은 편지까지 처음부터 다 왔다(사용자 보고).
    /// 편지를 넣는 스크립트 행동은 없다 — 우편함에 넣는 곳은 이 배달과 파티 합치기(행동 803)뿐이다.
    /// </remarks>
    private int DeliverMail()
    {
        if (_mosesChp is not { } chp) return 0;
        var all = AllMails();
        int added = 0;
        foreach (var (_, id) in chp.MailTriggers)
        {
            if (_mailbox.Contains(id) || _mailbox.Count >= MailboxLimit) continue;
            if (all.FirstOrDefault(m => m.Id == id) is not { } mail || !MailArrives(mail)) continue;
            _mailbox.Add(id);
            added++;
        }
        return added;
    }

    /// <summary>
    /// 편지의 도착 조건 (깃발, 값, 연산자) — 깃발이 0xffff 면 조건 없이 온다. 아니면 <c>0x100fda40(flags[깃발], 값, 연산자)</c>:
    /// 0 == · 1 != · 2 &lt; · 3 &lt;= · 4 &gt; · 5 &gt;=, 6 이상은 거짓. 깃발 0 도 진짜 깃발로 본다(<see cref="FlagAllows"/> 는 0 을 「조건 없음」으로 흘린다).
    /// </summary>
    private bool MailArrives(MosesMail mail)
    {
        if (mail.CondVar is -1 or 0xffff) return true;
        if ((uint)mail.CondVar >= _flags.Length) return false;
        int now = _flags[mail.CondVar];
        return mail.CondOp switch
        {
            0 => now == mail.CondValue,
            1 => now != mail.CondValue,
            2 => now < mail.CondValue,
            3 => now <= mail.CondValue,
            4 => now > mail.CondValue,
            5 => now >= mail.CondValue,
            _ => false,
        };
    }

    /// <summary>
    /// 옛 세이브(형식 8 이하)의 우편함 — 예전 배달이 챕터를 안 가려 미리 넣은 편지를 걷어 낸다. 들어가 본 챕터의 메일 표에 있고
    /// 지금 조건을 채우는 편지만 남긴다(원본이라면 그때까지 왔을 편지). 읽음 표시는 남은 편지만 둔다.
    /// </summary>
    private void PruneLegacyMailbox(IEnumerable<int> chapters)
    {
        var all = AllMails();
        var allowed = new HashSet<int>();
        foreach (int id in chapters.Distinct())
            if (LoadChapterFile(id) is { } chp)
                foreach (var (_, mail) in chp.MailTriggers)
                    if (all.FirstOrDefault(m => m.Id == mail) is { } m && MailArrives(m)) allowed.Add(mail);
        _mailbox.RemoveAll(id => !allowed.Contains(id));
        _mailRead.RemoveWhere(id => !allowed.Contains(id));
    }

    /// <summary>
    /// 스크립트 조건 503 [방아쇠] — 챕터 메일 방아쇠 표에서 그 방아쇠가 맞는 <b>마지막</b> 칸의 편지가 지금 파티 우편함에 있고 읽혔으면 참(<c>0x100edc40</c>).
    /// </summary>
    private bool MailTriggerRead(int trigger)
    {
        if (_mosesChp is not { } chp) return false;
        int mail = -1;
        foreach (var (id, m) in chp.MailTriggers)
            if (id == trigger) mail = m;
        return mail >= 0 && _mailbox.Contains(mail) && _mailRead.Contains(mail);
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

        if (_mailOpen >= 0)                                   // 뷰어는 아무 데나 누르면 닫히고 읽음이 된다(닫을 때 0x100f809a) — 그 밖에 깃발·돈은 없다
        {
            var opened = Mails();
            if (_mailOpen < opened.Count) _mailRead.Add(opened[_mailOpen].Id);
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
        // 줄 틀(Obs 1291 모션 0)은 목록 덧그림(0x10043810)이라 <b>마우스가 올라간 줄에만</b> 그린다 — 상점 목록·세이브 슬롯 강조와 같다.
        // 예전에는 모든 줄에 그려 목록이 줄무늬 표처럼 보였다(사용자 보고).
        int hover = _mailOpen < 0 ? MailRowAt(_mouse.X, _mouse.Y) : -1;
        for (int r = 0; r < MailRows; r++)
        {
            int index = _mailTop + r;
            if (index >= mails.Count) break;
            var mail = mails[index];
            int rx = ox + MailListX, ry = oy + MailListY + r * MailRowH;
            if (index == hover) DrawUi(MailRowObs, 0, tick, rx + 13, ry - 2, UiBlend.Alpha);
            bool read = _mailRead.Contains(mail.Id);
            string line = $"{SenderName(mail.Sender)} : {_db?.T(mail.OriginText)}";
            DrawText(line, rx + 24, ry + 2, read ? DimGray : 0xFFFFFF80, 11);
        }

        // Exit 글자는 배경 그림(Bgr 0094)에 있다 — 알약 Obs 287 은 마우스 올림에만.
        if (_mouse.X - ox >= 455 && _mouse.X - ox < 633 && _mouse.Y - oy >= 430 && _mouse.Y - oy < 457)
            DrawUi(MosesExitObs, 0, tick, ox + 455, oy + 430, UiBlend.Alpha);

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
/// <param name="CondVar">도착 조건 (깃발, 값, 연산자) — 깃발이 0xffff(−1) 이면 조건 없이 온다(<c>0x100fc6e0</c>).</param>
internal sealed record MosesMail(int Id, int Sender, ushort OriginText, ushort Bgm, string Body, int CondVar = -1, int CondValue = 0, int CondOp = 0)
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
                int c = o + 10 + length;
                int condVar = c + 6 <= b.Length ? BitConverter.ToInt16(b, c) : -1;
                int condValue = c + 6 <= b.Length ? BitConverter.ToInt16(b, c + 2) : 0;
                int condOp = c + 6 <= b.Length ? BitConverter.ToInt16(b, c + 4) : 0;
                list.Add(new MosesMail(id, sender, origin, bgm, body, condVar, condValue, condOp));
                o += 10 + length + 8;                      // 본문 뒤에 조건 3칸 + 안 쓰는 칸
            }
        }
        catch (ArgumentException) { /* 배치가 안 맞으면 읽은 데까지 */ }
        return list;
    }
}

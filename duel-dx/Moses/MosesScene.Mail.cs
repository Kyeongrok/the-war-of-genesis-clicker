using System.Text;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

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
internal sealed unsafe partial class MosesScene
{
    internal const int MailListX = 151, MailListY = 70, MailRowW = 427, MailRowH = 19, MailRows = 17;
    internal const int MailViewX = 164, MailViewY = 120, MailViewW = 313, MailViewH = 239;
    internal const int MailRowObs = 1291;

    internal List<MosesMail>? _mails;

    /// <summary>우편함 — 도착한 편지 번호(도착 차례). 원본 파티 <c>+0xa98/+0xa9a</c>. 세이브에 실린다.</summary>
    internal readonly List<int> _mailbox = [];

    /// <summary>읽은 편지 번호(원본 파티 <c>+0xe9a</c>). 세이브에 실린다.</summary>
    internal readonly HashSet<int> _mailRead = [];
    internal int _mailTop, _mailOpen = -1;

    /// <summary><c>MAIL.DAT</c> 의 편지 전부(번호 차례).</summary>
    internal List<MosesMail> AllMails()
    {
        if (_mails != null) return _mails;
        return _mails = host._db?.Files.Read("Dat", "MAIL.DAT") is { } bytes ? MosesMail.ParseAll(bytes) : [];
    }

    /// <summary>우편함에 든 편지 — 목록 차례로(최근 편지가 맨 위, 원본 줄 i = 우편함[개수−1−i]).</summary>
    internal List<MosesMail> Mails()
    {
        var all = AllMails();
        return [.. Enumerable.Reverse(_mailbox).Select(id => all.FirstOrDefault(m => m.Id == id)).Where(m => m != null)!];
    }

    /// <summary>
    /// 새 편지가 있나 — 우편함에 안 읽은 편지가 있거나, 메일 페이지에 들어가면 배달될 편지(조건을 채운 지금 챕터의 편지)가 있으면.
    /// 모세스 첫 화면 MAIL 아이콘의 빨간 점(원본에 없는 데모 표시, 사용자 요청). 다 읽으면 사라진다.
    /// </summary>
    internal bool HasNewMail()
    {
        if (_mailbox.Any(id => !_mailRead.Contains(id))) return true;
        if (_mosesChp is not { } chp || _mailbox.Count >= MailboxLimit) return false;
        var all = AllMails();
        return chp.MailTriggers.Any(t => !_mailbox.Contains(t.Item2) && all.FirstOrDefault(m => m.Id == t.Item2) is { } mail && MailArrives(mail));
    }

    /// <summary>우편함 상한 — 원본은 0x1fe 칸까지만 넣는다.</summary>
    internal const int MailboxLimit = 0x1ff;

    /// <summary>
    /// 새 편지 배달 — <b>지금 챕터 Chp 의 메일 표</b>(파일 차례)를 훑어, 아직 안 온 편지 가운데 조건을 채운 것을 우편함에 넣는다.
    /// 메일 페이지에 들어갈 때 한 번뿐이다(<c>0x100febef</c> → <c>0x100fc6e0</c>). 늘어난 수를 돌려준다(늘었으면 Snd 571).
    /// </summary>
    /// <remarks>
    /// 예전에는 <c>MAIL.DAT</c> 92통을 전부 훑어 시험 챕터(0001·0040)에만 든 「디에네 : 테스트방어구」 같은 편지까지 처음부터 다 왔다(사용자 보고).
    /// 편지를 넣는 스크립트 행동은 없다 — 우편함에 넣는 곳은 이 배달과 파티 합치기(행동 803)뿐이다.
    /// </remarks>
    internal int DeliverMail()
    {
        // DUELDX_MAILBOX=all 이면 MAIL.DAT 편지를 모두 우편함에 넣는다(화면 밖 시험용 — 17통이 넘는 목록의 스크롤).
        if (Environment.GetEnvironmentVariable("DUELDX_MAILBOX") == "all")
            foreach (var m in AllMails())
                if (!_mailbox.Contains(m.Id)) _mailbox.Add(m.Id);
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
    internal bool MailArrives(MosesMail mail)
    {
        if (mail.CondVar is -1 or 0xffff) return true;
        if ((uint)mail.CondVar >= host.FlagSt._flags.Length) return false;
        int now = host.FlagSt._flags[mail.CondVar];
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
    internal void PruneLegacyMailbox(IEnumerable<int> chapters)
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
    internal bool MailTriggerRead(int trigger)
    {
        if (_mosesChp is not { } chp) return false;
        int mail = -1;
        foreach (var (id, m) in chp.MailTriggers)
            if (id == trigger) mail = m;
        return mail >= 0 && _mailbox.Contains(mail) && _mailRead.Contains(mail);
    }

    internal string SenderName(int chrCode) =>
        host._db?.Character(chrCode) is { } c ? host._db.T(c.NameId) : $"Chr {chrCode}";

    internal int MailRowAt(int bx, int by)
    {
        var (ox, oy) = MosesOrigin();
        if (bx < ox + MailListX || bx >= ox + MailListX + MailRowW) return -1;
        int row = (by - oy - MailListY) / MailRowH;
        if (row < 0 || row >= MailRows) return -1;
        int index = _mailTop + row;
        return index < Mails().Count ? index : -1;
    }

    /// <summary>뷰어를 닫는다 — 읽음 표시는 닫을 때 선다(0x1003cce0). 우클릭·Esc 로 나갈 때도 같아야 조건 503 이 안 어긋난다(ba-20 G5).</summary>
    internal void CloseMailViewer()
    {
        if (_mailOpen < 0) return;
        _mailViewTop = 0;
        var opened = Mails();
        if (_mailOpen < opened.Count) _mailRead.Add(opened[_mailOpen].Id);
        _mailOpen = -1;
    }

    /// <summary>메일 페이지가 열려 있으면 클릭을 처리하고 true.</summary>
    internal bool OnMosesMailClick(int bx, int by)
    {
        if (_mosesPage != 1) return false;
        var (ox, oy) = MosesOrigin();
        int x = bx - ox, y = by - oy;

        if (_mailOpen >= 0)                                   // 뷰어는 아무 데나 누르면 닫히고 읽음이 된다(닫을 때 0x100f809a) — 그 밖에 깃발·돈은 없다
        {
            CloseMailViewer();
            host.Play(95);                                         // 뷰어(말풍선 클래스) 닫기 vt+0xdc = 0x1003cce0 → Snd 95(감사5 L3)
            return true;
        }
        // 나가기(10002, 0x10100e1b) = 뒤로 처리기 페이지 1 갈래(0x10101ba0) — 소리 없음(감사5 D3). 전에는 576 + 569 두 번 났다.
        if (x >= 455 && x < 618 && y >= 430 && y < 457) { MosesGoBack(); return true; }
        if (OnMailBarClick(x, y)) return true;

        // 줄 누름은 목록 창 메시지 0x272a — 소리가 없다(0x100fff86, 목록 클래스에 0x10028850 호출 0개, 감사5 L2).
        int index = MailRowAt(bx, by);
        if (index >= 0) { _mailOpen = index; _mailViewTop = 0; }
        return true;
    }

    /// <summary>
    /// 목록 스크롤 막대 — 원본 목록 창(<c>0x100482e0(…, 1열, 17행, 427, 19, …, 스크롤 갈래 2)</c>, <c>0x100fc84e</c>)은 17줄을 넘으면
    /// 오른쪽 가장자리에 공용 막대를 단다(분석-UI 2.5). 연대표와 같은 <c>Obs 0979</c> 장 61(위)·63(아래) 화살표 19×45 와 손잡이 65 로 그린다.
    /// 전에는 <see cref="ScrollMail"/> 을 부르는 곳이 없어 18번째(가장 오래된) 편지부터 영영 못 읽었다(감사5 M1).
    /// </summary>
    internal const int MailBarX = MailListX + MailRowW, MailBarY = MailListY, MailBarH = MailRows * MailRowH, MailArrowW = 19, MailArrowH = 45;

    /// <summary>막대 누름 — 화살표는 한 줄, 손잡이 위·아래 빈 곳은 한 쪽(17줄)씩. 모세스 화면 좌표. 막대를 눌렀으면 true.</summary>
    internal bool OnMailBarClick(int x, int y)
    {
        int count = Mails().Count;
        if (count <= MailRows) return false;
        x -= MailBarX;
        y -= MailBarY;
        if (x < 0 || x >= MailArrowW || y < 0 || y >= MailBarH) return false;
        if (y < MailArrowH) { ScrollMail(-1); return true; }
        if (y >= MailBarH - MailArrowH) { ScrollMail(1); return true; }
        var (thumbY, thumbH) = MailThumb(count);
        ScrollMail(y < thumbY + thumbH / 2 ? -MailRows : MailRows);
        return true;
    }

    /// <summary>손잡이 자리(막대 위에서부터)와 높이.</summary>
    internal (int Y, int H) MailThumb(int count)
    {
        int thumbH = host.UiFor(EpisodesScreen.EpisodeObs)?.FrameAt(65, 0) is { H: > 0 } f ? f.H : 20;
        int track = MailBarH - 2 * MailArrowH - thumbH;
        int span = Math.Max(1, count - MailRows);
        return (MailArrowH + track * Math.Clamp(_mailTop, 0, span) / span, thumbH);
    }

    /// <summary>
    /// 편지 목록을 <paramref name="delta"/> 줄 굴린다 — 막대 화살표·빈 곳이 부른다.
    /// 마우스 휠은 WndProc(GameWindow.cs)가 메일 페이지에서 <c>ScrollMail(-휠/120)</c> 으로 보내야 한다.
    /// </summary>
    internal void ScrollMail(int delta)
    {
        int max = Math.Max(0, Mails().Count - MailRows);
        _mailTop = Math.Clamp(_mailTop + delta, 0, max);
    }

    /// <summary>메일 페이지가 떠 있고 뷰어가 닫혀 있으면 휠로 목록을 굴린다 — WndProc 의 WM_MOUSEWHEEL 이 부른다. 받았으면 true.</summary>
    internal bool OnMosesMailWheel(int notches)
    {
        if (!_mosesOpen || _mosesPage != 1 || host.Sys.SystemOpen) return false;
        if (_mailOpen >= 0) { _mailViewTop = Math.Max(0, _mailViewTop - notches); return true; }   // 뷰어가 열려 있으면 본문을 굴린다
        ScrollMail(-notches);
        return true;
    }

    /// <summary>줄 글 색 — 원본 0xFFFF(COLORREF) = 노랑 (255,255,0), 읽음·안 읽음 같다(0x100fca31~0x100fca97).</summary>
    internal const uint MailLineColor = 0xFFFFFF00;

    internal void DrawMosesMail(int ox, int oy, int tick)
    {
        var mails = Mails();
        _mailTop = Math.Clamp(_mailTop, 0, Math.Max(0, mails.Count - MailRows));
        // 줄 틀(Obs 1291 모션 0)은 목록 덧그림(0x10043810)이라 <b>마우스가 올라간 줄에만</b> 그린다 — 상점 목록·세이브 슬롯 강조와 같다.
        // 예전에는 모든 줄에 그려 목록이 줄무늬 표처럼 보였다(사용자 보고).
        int hover = _mailOpen < 0 ? MailRowAt(host._mouse.X, host._mouse.Y) : -1;
        for (int r = 0; r < MailRows; r++)
        {
            int index = _mailTop + r;
            if (index >= mails.Count) break;
            var mail = mails[index];
            int rx = ox + MailListX, ry = oy + MailListY + r * MailRowH;
            if (index == hover) host.DrawUi(MailRowObs, 0, tick, rx + 13, ry - 2, GameWindow.UiBlend.Alpha);
            // 줄 글 = 0x10040600(글, 가로 1(가운데), 0, 세로 1(가운데), 0, 글꼴, 0xFFFF, …) — 가운데 정렬 노랑.
            // 글꼴은 읽음 표시가 0 이면 2(굴림 9 굵게), 아니면 기본 글꼴 — 뷰어를 여는 순간 그 줄은 기본 글꼴로 바뀐다(0x10100129~0x10100166).
            // 데모 글꼴은 늘 굵어서, 안 읽은 줄은 한 픽셀 옆에 한 번 더 찍어 더 굵게 한다(감사5 L1).
            bool read = _mailRead.Contains(mail.Id) || index == _mailOpen;
            string line = $"{SenderName(mail.Sender)} : {host._db?.T(mail.OriginText)}";
            var (_, w, h) = host.GetText(line, MailLineColor, 12);
            int tx = rx + (MailRowW - w) / 2, ty = ry + (MailRowH - h) / 2;
            host.DrawText(line, tx, ty, MailLineColor, 12);
            if (!read) host.DrawText(line, tx + 1, ty, MailLineColor, 12);
        }

        if (mails.Count > MailRows)
        {
            host.DrawUi(EpisodesScreen.EpisodeObs, 61, 0, ox + MailBarX, oy + MailBarY, GameWindow.UiBlend.Alpha);
            host.DrawUi(EpisodesScreen.EpisodeObs, 63, 0, ox + MailBarX, oy + MailBarY + MailBarH - MailArrowH, GameWindow.UiBlend.Alpha);
            var (thumbY, _) = MailThumb(mails.Count);
            host.DrawUi(EpisodesScreen.EpisodeObs, 65, 0, ox + MailBarX, oy + MailBarY + thumbY, GameWindow.UiBlend.Alpha);
        }

        // Exit 글자는 배경 그림(Bgr 0094)에 있다 — 알약 Obs 287 은 마우스 올림에만.
        if (host._mouse.X - ox >= 455 && host._mouse.X - ox < 633 && host._mouse.Y - oy >= 430 && host._mouse.Y - oy < 457)
            host.DrawUi(MosesExitObs, 0, tick, ox + 455, oy + 430, GameWindow.UiBlend.Alpha);

        if (_mailOpen >= 0 && _mailOpen < mails.Count) DrawMailView(ox, oy, mails[_mailOpen]);
    }

    /// <summary>편지 보기 — 원본 창 틀에 보낸이·제목·본문.</summary>
    internal void DrawMailView(int ox, int oy, MosesMail mail)
    {
        // 뷰어 0x1003c870(그리기 0x1003cd90, ba-20 S 4) — 창 (164,100) 313×239, 틀 Obs 226 넉 장(바탕 모션 2·3 은 효과 6 = 24/31, 테두리 0·1),
        // 얼굴 60×60 @ (+4,+8), 첫 줄 「이름」(+73,+12) / 보낸이 이름 오른끝 +303, 둘째 줄 발신지 (+73,+54) / 「LOCATION」 오른끝 +303,
        // 본문 (+15,+76) 폭 298 · 10줄. 원본은 3틱에 한 글자씩 흘리며 줄을 올리는데, 여기서는 한꺼번에 띄우고 휠로 굴린다.
        int x = ox + MailViewX, y = oy + MailViewY - 20;
        const uint tag = 0xFFF2DB6F, value = 0xFFDAE6FC;
        if (host.UiFor(226) != null)
        {
            host.DrawUi(226, 2, 0, x, y, GameWindow.UiBlend.Alpha, fade: 24 / 31.0);
            host.DrawUi(226, 3, 0, x, y, GameWindow.UiBlend.Alpha, fade: 24 / 31.0);
            host.DrawUi(226, 0, 0, x, y, GameWindow.UiBlend.Alpha);
            host.DrawUi(226, 1, 0, x, y, GameWindow.UiBlend.Alpha);
        }
        else
        {
            host.DarkenRect(x - 1, y - 1, MailViewW + 2, MailViewH + 2);
            host.StrokeRect(x, y, MailViewW, MailViewH, White);
        }
        if (host._db?.Character(mail.Sender) is { } sender)
        {
            host.Fld.LoadFieldFace(sender);
            if (host._faces.TryGetValue(sender.Code, out var face)) host.BlitScaled(face, x + 4, y + 8, 60, 60);
        }
        host.DrawText("이름", x + 73, y + 12, tag, 12);
        host.RightText(SenderName(mail.Sender), x + 303, y + 12, value, 12);
        host.DrawText(host._db?.T(mail.OriginText) ?? "", x + 73, y + 54, value, 12);
        host.RightText("LOCATION", x + 303, y + 54, tag, 12);

        var lines = WrapText(mail.Body, 298 - 6, 12f);
        const int rows = 10;
        _mailViewTop = Math.Clamp(_mailViewTop, 0, Math.Max(0, lines.Count - rows));
        int ty = y + 76;
        for (int i = _mailViewTop; i < lines.Count && i < _mailViewTop + rows; i++, ty += 16)
            host.DrawText(lines[i], x + 15, ty, White, 12);
        if (_mailViewTop + rows < lines.Count) host.DrawUi(TalkBox.TalkNextObs, 0, (int)(host._lastTime * TicksPerSecond), x + 300, y + 232, GameWindow.UiBlend.Alpha);
        if (_mailViewTop > 0) host.DrawText("▲", x + 296, y + 76, tag, 11);
    }

    /// <summary>편지 뷰어에서 맨 위에 보이는 줄 — 휠로 굴린다.</summary>
    internal int _mailViewTop;

    /// <summary>
    /// 글을 칸 너비에 맞춰 줄로 나눈다(원본은 여러 줄 글 객체가 한다). 글에 박힌 <c>$n</c> 따위 강제 줄바꿈 표시도
    /// 대사와 같이 줄을 바꾼다 — 전에는 편지 본문에 「$n」이 글자 그대로 찍혔다(사용자 보고).
    /// </summary>
    internal List<string> WrapText(string text, int width, float size)
    {
        var lines = new List<string>();
        foreach (string paragraph in TalkBox.TalkLines(text.Replace("\r", "")).SelectMany(p => p.Split('\n')).Select(p => p.Trim()))
        {
            var line = new StringBuilder();
            foreach (char ch in paragraph)
            {
                line.Append(ch);
                if (host.GetText(line.ToString(), White, size).W <= width) continue;
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
    internal static readonly Lazy<Encoding> Cp949 = new(() =>
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

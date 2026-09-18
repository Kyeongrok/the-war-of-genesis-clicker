using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 모세스 통신(MESSAGE) 페이지(mo-1, 페이지 2) — 성도 위를 걸어 다니는 사람들과 이야기한다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-모세스 11절: 배경은 항행과 같은 항성계 배경이고, 지금 항성계의 <b>성도 점이 둘 이상</b>이어야 열린다.
/// 조건을 통과한 인물(최대 여덟)마다 <c>Obs 1330</c> 스프라이트를 성도 점 두 곳 <b>사이를 3600프레임에 걸쳐 오가며</b> 그리고,
/// 그 아래에 이름표(인물의 이름 TXR)를 붙인다. 누르면 174×60 말풍선과 초상화(<c>CChr+0x0E</c>, 없으면 <b>Obs 0229</b>)와
/// 대사 TXR(인물 레코드의 대사 셋을 돌아가며)이 나온다.
/// Chp 인물 레코드(파일 30바이트)의 워드: 0 번호 · 1 Chr · 8·9·10 대사 TXR 셋 · 11~13 조건.
/// 모션 번호는 파일에 없고(메모리 <c>+0x22</c>) 자료가 모두 0 이라 <b>모션 0</b> 을 쓴다.
/// Chp 0010(코어헌터)에는 인물이 없어 이 페이지가 비어 있다 — 챕터 고르기로 0011 같은 챕터를 열면 보인다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int TalkObs = 1330, TalkFaceFallbackObs = 229;
    private const int TalkWalkFrames = 3600, TalkBubbleW = 174, TalkBubbleH = 60;

    private int _talkPick = -1, _talkWord;

    private List<ChapterFile.Person> TalkPeople() => [.. _mosesChp?.People ?? []];

    /// <summary>그 인물이 지금 서 있는 자리 — 성도 점 두 곳 사이를 오간다.</summary>
    private (int X, int Y) TalkSpot(int index)
    {
        var marks = _mosesChp?.Landmarks ?? [];
        if (marks.Count == 0) return (320, 240);
        var a = marks[index % marks.Count];
        var b = marks[(index + 1) % marks.Count];
        int t = ((int)(_lastTime * TicksPerSecond) + index * 137) % (2 * TalkWalkFrames);
        int walk = t < TalkWalkFrames ? t : 2 * TalkWalkFrames - t;     // 갔다가 되돌아온다
        return (a.X + (b.X - a.X) * walk / TalkWalkFrames, a.Y + (b.Y - a.Y) * walk / TalkWalkFrames);
    }

    /// <summary>통신 페이지가 열려 있으면 클릭을 처리하고 true.</summary>
    private bool OnMosesTalkClick(int bx, int by)
    {
        if (_mosesPage != 2) return false;
        var (ox, oy) = MosesOrigin();
        int x = bx - ox, y = by - oy;

        if (MosesBackAt(bx, by)) { MosesGoBack(); return true; }

        var people = TalkPeople();
        for (int i = 0; i < people.Count && i < 8; i++)
        {
            var (px, py) = TalkSpot(i);
            if (Math.Abs(x - px) > 20 || Math.Abs(y - py) > 24) continue;
            _talkWord = _talkPick == i ? _talkWord + 1 : 0;              // 같은 사람을 또 누르면 다음 대사
            _talkPick = i;
            Play(MosesClickSound);
            return true;
        }
        _talkPick = -1;
        return true;
    }

    private void DrawMosesTalk(int ox, int oy, int tick)
    {
        var people = TalkPeople();
        if (people.Count == 0)
        {
            var (_, w, h) = GetText("이 챕터에는 통신할 사람이 없습니다", White, 15);
            DrawText("이 챕터에는 통신할 사람이 없습니다", ox + (MosesW - w) / 2, oy + (MosesH - h) / 2, White, 15);
        }

        for (int i = 0; i < people.Count && i < 8; i++)
        {
            var (px, py) = TalkSpot(i);
            DrawUi(TalkObs, 0, tick, ox + px, oy + py, UiBlend.Alpha);
            string name = _db?.Character(people[i].ChrCode) is { } c ? _db.T(c.NameId) : "";
            if (name.Length == 0) continue;
            var (_, nw, _) = GetText(name, White, 11);
            DrawText(name, ox + px - nw / 2, oy + py + 8, i == _talkPick ? 0xFF00FF00 : White, 11);
        }

        if (_talkPick >= 0 && _talkPick < people.Count) DrawTalkBubble(ox, oy, tick, people[_talkPick]);

        if (!DrawUi(MosesBackObs, 0, tick, ox + 46, oy + 244, UiBlend.Alpha))
            DrawText("BACK", ox + 46, oy + 248, White);
    }

    /// <summary>말풍선 — 초상화와 대사 한 줄(인물 레코드의 대사 셋을 돌아가며).</summary>
    private void DrawTalkBubble(int ox, int oy, int tick, ChapterFile.Person person)
    {
        // 대사가 어디서 오는지 아직 못 밝혔다 — 노트가 가리키는 워드 8~10 을 TXR 로 풀면 「맨탈체2」 처럼 엉뚱한 글이 나온다.
        // 밝혀질 때까지는 이름만 띄운다(분석 진행 중: 분석-모세스 11절 정정 예정).
        string line = _db?.Character(person.ChrCode) is { } who ? _db.T(who.NameId) : "";
        var (px, py) = TalkSpot(_talkPick);
        int x = Math.Clamp(ox + px - TalkBubbleW / 2, ox + 4, ox + MosesW - TalkBubbleW - 4);
        int y = Math.Clamp(oy + py - TalkBubbleH - 30, oy + 4, oy + MosesH - TalkBubbleH - 4);

        DarkenRect(x - 1, y - 1, TalkBubbleW + 2, TalkBubbleH + 2);
        DrawGameFrame(x, y, TalkBubbleW, TalkBubbleH);

        // 초상화 — 그 인물의 얼굴 그림이 assets 에 없으면 원본처럼 Obs 0229 로
        if (_db?.Character(person.ChrCode) is { } c && _faces.TryGetValue(c.Code, out var face))
            BlitScaled(face, x + 4, y + 4, 48, 52);
        else DrawUi(TalkFaceFallbackObs, 0, tick, x + 28, y + 30, UiBlend.Alpha);

        int ty = y + 8;
        foreach (string text in WrapText(line, TalkBubbleW - 64, 11f))
        {
            if (ty > y + TalkBubbleH - 14) break;
            DrawText(text, x + 58, ty, White, 11);
            ty += 14;
        }
    }
}

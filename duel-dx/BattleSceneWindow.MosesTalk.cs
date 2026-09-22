using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 모세스 통신(MESSAGE) 페이지(mo-1, 페이지 2) — 성도 위를 걸어 다니는 사람들과 이야기한다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-모세스 11절: 배경은 항행과 같은 항성계 배경이고, 지금 항성계의 <b>성도 점이 둘 이상</b>이어야 열린다.
/// 조건을 통과한 인물(최대 여덟)마다 <c>Obs 1330</c> 스프라이트를 성도 점 두 곳 <b>사이를 3600프레임에 걸쳐 오가며</b> 그리고,
/// 그 아래에 이름표(인물의 이름 TXR)를 붙인다. 누르면 174×60 말풍선과 초상화(<c>CChr+0x0E</c>, 없으면 <b>Obs 0229</b>)와 대사가 나온다.
/// <b>대사는 TXR 이 아니라 그 챕터와 같은 번호의 Tlk 파일(<c>Tlk\NNNN.Tlc</c>)</b> 에서 온다(11절 2026-09-19 정정) —
/// 인물 레코드(파일 30바이트)의 워드 8·9·10 이 그 표의 번호이고, 누를 때마다 −1 이 아닌 다음 칸으로 넘어간다.
/// 그림 모션은 파일에 없다 — 원본은 인물마다 <c>rand()%12</c> 를 한 번 뽑아 둔다.
/// Chp 0010(코어헌터)에는 인물이 없어 이 페이지가 비어 있다 — 챕터 고르기로 0011 같은 챕터를 열면 보인다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int TalkObs = 1330, TalkFaceFallbackObs = 229;
    private const int TalkWalkFrames = 3600, TalkBubbleW = 174, TalkBubbleH = 60;

    private int _talkPick = -1;
    private TalkTable? _talkTable;
    private int _talkTableChapter = -1;
    private readonly Dictionary<int, int> _talkSlot = [];      // 인물 번호 -> 지금 대사 칸(0~2)
    private readonly Dictionary<int, int> _talkMotion = [];    // 인물 번호 -> 뽑아 둔 Obs 1330 모션

    /// <summary>통신 페이지가 보는 항성계 — 고른 행성이 속한 성계, 없으면 첫 성계(항행 배경과 같은 규칙).</summary>
    private ChapterFile.StarSystem? TalkSystem()
    {
        if (_mosesChp is not { } chp) return null;
        return chp.Systems.FirstOrDefault(s => s.Planets.Contains(_mosesPlanet)) ?? chp.Systems.FirstOrDefault();
    }

    /// <summary>이 항성계의 성도 점들 — 사람은 이 점들 사이를 오간다. 원본은 <b>둘 이상</b>일 때만 페이지를 연다.</summary>
    private List<ChapterFile.Landmark> TalkMarks()
    {
        if (_mosesChp is not { } chp || TalkSystem() is not { } system) return [];
        return [.. chp.Landmarks.Where(l => l.SystemNo == system.No)];
    }

    /// <summary>
    /// 화면에 나올 사람들 — 원본(<c>0x100fdc80</c>)처럼 <b>지금 항성계의 인물 목록</b>(성계 레코드 <c>+0x30</c>)에서
    /// 나타날 조건(인물 워드 11·12·13 = 변수·값·연산자, 변수가 0 이하면 조건 없음)을 통과한 사람만, 여덟까지.
    /// 챕터의 모든 인물을 다 세우면 다른 성계 사람까지 나오고, 성도 점이 없는 성계에서는 전부 한자리에 겹친다.
    /// </summary>
    private List<ChapterFile.Person> TalkPeople()
    {
        if (_mosesChp is not { } chp || TalkSystem() is not { } system || TalkMarks().Count < 2) return [];
        var people = new List<ChapterFile.Person>();
        foreach (int no in system.People)
        {
            if (no < 0) continue;
            var person = chp.People.FirstOrDefault(p => p.No == no) ?? (no < chp.People.Count ? chp.People[no] : null);
            if (person == null || people.Contains(person)) continue;
            int variable = person.Words.Count > 13 ? person.Words[11] : 0;
            if (variable > 0 && !FlagsAllow([(variable, person.Words[12], person.Words[13])])) continue;
            people.Add(person);
            if (people.Count == 8) break;
        }
        return people;
    }

    /// <summary>사람마다 한 번 뽑아 두는 길 — 서로 다른 성도 점 둘과 출발 위상(원본은 <c>rand()</c>, 창 <c>+0x148+6i</c>).</summary>
    private readonly Dictionary<(int Chapter, int Person), (int A, int B, int Phase)> _talkPath = [];

    /// <summary>그 챕터의 대사 표 — 챕터와 같은 번호의 Tlk 파일.</summary>
    private TalkTable? TalkTableFor()
    {
        int id = _mosesChp?.Id ?? -1;
        if (id < 0) return null;
        if (_talkTableChapter == id) return _talkTable;
        _talkTableChapter = id;
        string path = Path.Combine(AssetsFolder.Find("moses"), "tlk", $"{id:D4}.tlc");
        return _talkTable = File.Exists(path) ? TalkTable.Parse(File.ReadAllBytes(path)) : null;
    }

    /// <summary>인물마다 한 번 뽑아 두는 그림 모션(원본은 rand()%12).</summary>
    private int TalkMotion(ChapterFile.Person person)
    {
        if (_talkMotion.TryGetValue(person.No, out int motion)) return motion;
        return _talkMotion[person.No] = _ailmentRandom.Next(12);
    }

    /// <summary>그 인물의 대사 번호들 — 워드 8·9·10 중 −1 이 아닌 것.</summary>
    private static List<int> TalkWords(ChapterFile.Person person) =>
        [.. person.Words.Skip(8).Take(3).Where(v => v > 0)];

    private void NextTalkSlot(ChapterFile.Person person)
    {
        int count = TalkWords(person).Count;
        if (count > 0) _talkSlot[person.No] = (_talkSlot.GetValueOrDefault(person.No) + 1) % count;
    }

    /// <summary>그 인물이 지금 서 있는 자리 — 제 성도 점 두 곳 사이를 3600프레임에 걸쳐 오간다.</summary>
    private (int X, int Y) TalkSpot(ChapterFile.Person person)
    {
        var marks = TalkMarks();
        if (marks.Count < 2) return (320, 240);
        var key = (_mosesChp?.Id ?? 0, person.No);
        if (!_talkPath.TryGetValue(key, out var path) || path.A >= marks.Count || path.B >= marks.Count)
        {
            int a = _ailmentRandom.Next(marks.Count), b = _ailmentRandom.Next(marks.Count - 1);
            if (b >= a) b++;                                              // 서로 다른 두 곳
            _talkPath[key] = path = (a, b, _ailmentRandom.Next(TalkWalkFrames));
        }
        var from = marks[path.A];
        var to = marks[path.B];
        int t = ((int)(_lastTime * TicksPerSecond) + path.Phase) % (2 * TalkWalkFrames);
        int walk = t < TalkWalkFrames ? t : 2 * TalkWalkFrames - t;     // 갔다가 되돌아온다
        return (from.X + (to.X - from.X) * walk / TalkWalkFrames, from.Y + (to.Y - from.Y) * walk / TalkWalkFrames);
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
            var (px, py) = TalkSpot(people[i]);
            if (Math.Abs(x - px) > 20 || Math.Abs(y - py) > 24) continue;
            if (_talkPick == i) NextTalkSlot(people[i]);                  // 같은 사람을 또 누르면 다음 대사
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
            var (px, py) = TalkSpot(people[i]);
            DrawUi(TalkObs, TalkMotion(people[i]), tick, ox + px, oy + py, UiBlend.Alpha);
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
        var words = TalkWords(person);
        string line = TalkTableFor() is { } table && words.Count > 0
            ? table[words[_talkSlot.GetValueOrDefault(person.No) % words.Count]] : "";
        var (px, py) = TalkSpot(person);
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

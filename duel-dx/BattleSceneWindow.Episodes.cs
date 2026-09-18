using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 연대표 화면(an-ui-4, 장면 7) — 타이틀에서 NEW GAME 을 누르면 나오는 에피소드 고르기.
/// </summary>
/// <remarks>
/// 옵시디안 분석-UI 「연대표 화면 (an-ui-4)」:
/// <list type="bullet">
/// <item>배경은 <c>Bgr 0113</c> 한 장 — 로고와 「Episode 4 / 영혼의 검」·「Episode 5 / 뫼비우스의 우주」 머리글이 그림에 들어 있다.</item>
/// <item>목록은 <b>(75,174) 에 2열 6행, 칸 240×46</b> — 칸 왼위 = <c>(80 + 240×(i%2), 179 + 46×(i/2))</c>.
/// <b>짝수 번호가 왼쪽(Episode 4 줄기), 홀수가 오른쪽(Episode 5 줄기)</b>이다.</item>
/// <item>이름판은 <c>Obs 0979</c> 한 장에 다 들어 있다 — 모션은 짝수 <c>i+1</c>, 홀수 <c>i+30</c>, 고른 표시 <c>[ ]</c> 는 모션 4.
/// 이름판 가운데 x = 왼쪽 160 · 오른쪽 480, 윗변 y = 186 + 46×줄. <b>TXR·글꼴은 한 번도 안 쓴다.</b></item>
/// <item><b>두 번 눌러야 시작</b>한다 — 첫 누름은 고른 표시, 그 줄을 다시 누르면 간다. 마우스 올림 반응도 효과음도 없다.</item>
/// <item>음악은 <c>BGM 3391</c> 반복. ESC 는 시스템 메뉴(EXIT GAME 은 타이틀로 돌아간다).</item>
/// </list>
/// <c>EPS\Episode.dat</c>(604바이트): 머리 <c>u16 ?, u16 15</c> + 40바이트 레코드 15개. 레코드 하나가 에피소드 둘(왼·오른쪽)이고
/// 낱말 2~10 이 짝수 쪽, 11~19 가 홀수 쪽 칸 0~8 이다 — 칸 0~3 잠금 깃발(0xffff 면 조건 없음), <b>칸 7 = Chp 번호</b>, 칸 8 = 파티.
/// 새 게임이면 깃발이 모두 0 이라 <b>0번 「코어헌터」(Chp 0010)와 1번 「홍련의 예언」(Chp 0019)</b> 둘을 고를 수 있다.
/// 고르면 원본은 모세스(장면 4)를 그 챕터로 연다 — 이 데모도 같다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int EpisodeBackground = 113, EpisodeObs = 979, EpisodeBgm = 3391;
    private const int EpisodeRows = 6, EpisodeCellW = 240, EpisodeCellH = 46;

    /// <summary>연대표 한 줄 — 에피소드 번호와 그 챕터·파티.</summary>
    private sealed record EpisodeEntry(int No, int Chapter, int Party, bool Open);

    private bool _episodesOpen;
    private List<EpisodeEntry>? _episodes;
    private int _episodePick = -1;

    private List<EpisodeEntry> Episodes()
    {
        if (_episodes != null) return _episodes;
        var list = new List<EpisodeEntry>();
        try
        {
            string path = Path.Combine(AssetsFolder.Find("data"), "EPS", "Episode.dat");
            if (File.Exists(path))
            {
                byte[] b = File.ReadAllBytes(path);
                int count = BitConverter.ToUInt16(b, 2);
                for (int r = 0; r < count && 4 + 40 * r + 40 <= b.Length; r++)
                    for (int side = 0; side < 2; side++)
                    {
                        int o = 4 + 40 * r + 18 * side;          // 낱말 2~10(짝수) · 11~19(홀수)
                        short Word(int k) => BitConverter.ToInt16(b, o + 4 + 2 * k);
                        // 잠금 깃발 넷 — 0xffff(−1)이 아니면 진행 깃발이 서 있어야 열린다. 새 게임이면 모두 0 이라 닫힌다.
                        bool open = Enumerable.Range(0, 4).All(k => Word(k) < 0);
                        int chapter = Word(7), party = Word(8);
                        if (chapter <= 0) continue;
                        list.Add(new EpisodeEntry(2 * r + side, chapter, party, open));
                    }
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { /* 자료가 없으면 빈 목록 */ }
        return _episodes = list;
    }

    private void OpenEpisodes()
    {
        _episodesOpen = true;
        _titleOpen = false;
        _episodePick = -1;
        ShowMosesBackground(EpisodeBackground);
        _mixer.StopMusic();
        PlayMusicFile(EpisodeBgm, loop: true);
    }

    /// <summary>그 에피소드 줄이 놓이는 칸(왼위).</summary>
    private static (int X, int Y) EpisodeCell(int no) => (80 + EpisodeCellW * (no % 2), 179 + EpisodeCellH * (no / 2));

    private int EpisodeAt(int bx, int by)
    {
        var (ox, oy) = MosesOrigin();
        var list = Episodes();
        for (int i = 0; i < list.Count; i++)
        {
            if (!list[i].Open) continue;
            var (x, y) = EpisodeCell(list[i].No);
            if (list[i].No / 2 >= EpisodeRows) continue;
            if (bx >= ox + x && bx < ox + x + EpisodeCellW && by >= oy + y && by < oy + y + EpisodeCellH) return i;
        }
        return -1;
    }

    /// <summary>연대표가 떠 있으면 클릭을 처리하고 true. 원본처럼 <b>두 번 눌러야</b> 그 에피소드로 간다.</summary>
    private bool OnEpisodesClick(int bx, int by)
    {
        if (!_episodesOpen) return false;
        if (SystemOpen) return OnSystemClick(bx, by);

        int index = EpisodeAt(bx, by);
        if (index < 0) return true;
        if (_episodePick != index) { _episodePick = index; return true; }

        var entry = Episodes()[index];
        _episodesOpen = false;
        _party.Clear();
        string path = Path.Combine(AssetsFolder.Find("moses"), "chp", $"{entry.Chapter:D4}.chp");
        var chapter = File.Exists(path) ? ChapterFile.Parse(entry.Chapter, File.ReadAllBytes(path)) : null;
        OpenMoses(chapter);
        return true;
    }

    private void DrawEpisodes()
    {
        if (!_episodesOpen) return;
        var (ox, oy) = MosesOrigin();
        int tick = (int)(_lastTime * TicksPerSecond);

        FillRect(0, _camY, BoardWidth, ViewHeight, 0xFF000000);
        if (_mosesBg is { } bg)
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    SetPixel(ox + x, oy + y, bg[y * MosesW + x] | 0xFF000000);

        var list = Episodes();
        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            int row = entry.No / 2;
            if (row >= EpisodeRows) break;
            // 원본은 조건을 통과한 줄까지만 보이고, 못 여는 줄은 이름 없이 빈 채로 둔다.
            if (!entry.Open) continue;
            // 이름판 — 짝수 번호는 모션 i+1, 홀수는 i+30. 가운데 x 는 왼쪽 160 · 오른쪽 480.
            int motion = entry.No % 2 == 0 ? entry.No + 1 : entry.No + 30;
            // 이름판 그림은 <b>화면 가운데(320)를 기준점</b>으로 왼·오른쪽 자리를 스스로 들고 있고(장 5 = −196, 장 20 = +112),
            // 세로는 기준점이 −58 이라 줄 자리에 58 을 더해 찍는다.
            int cx = ox + 320, cy = oy + 186 + EpisodeCellH * row + 58;
            if (!DrawUi(EpisodeObs, motion, tick, cx, cy, UiBlend.Alpha) && entry.Open)
                DrawText($"Episode {entry.No} — Chp {entry.Chapter:D4}", cx - 80, cy, White, 12);
            if (i == _episodePick) DrawUi(EpisodeObs, 4, tick, cx, cy, UiBlend.Alpha);   // 고른 표시 [ ]
        }

        DrawSystem();
        DrawToast();
    }
}

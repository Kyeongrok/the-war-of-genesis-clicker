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
/// <item>이름판은 <c>Obs 0979</c> 한 장에 다 들어 있다 — 모션은 짝수 <c>i+1</c>, 홀수 <c>i+30</c>, 고른 표시 <c>[ ]</c> 는 모션 0,
/// 6줄이 넘으면 오른쪽 스크롤 막대(모션 61~66).
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

    /// <summary>연대표 한 줄 — 에피소드 번호와 그 챕터·파티, 잠금 깃발 넷(−1 = 조건 없음).</summary>
    private sealed record EpisodeEntry(int No, int Chapter, int Party, int[] Locks);

    /// <summary>
    /// 그 줄이 열렸나 — 잠금 깃발 넷이 <b>모두</b> −1 이거나 진행 깃발이 0 이 아니어야 한다(<c>0x10106f50</c> → <c>0x10106f20</c>, AND).
    /// 새 게임이면 깃발이 다 0 이라 조건 없는 0번(코어헌터)·1번(홍련의 예언)만 열리고, 코어헌터의 챕터 스크립트가 깃발 14 를 세우면
    /// 2번(샤이닝 스타)이 열린다.
    /// </summary>
    private bool EpisodeOpen(EpisodeEntry e) => e.Locks.All(f => f < 0 || (f < _flags.Length && _flags[f] != 0));

    /// <summary>
    /// 이미 고른 에피소드(번호) — 원본 <c>0x101b68a0[i]</c>. <b>고르는 순간</b> 1 이 되고(<c>0x10106722</c>·<c>0x1010689e</c>), NEW GAME 만 지운다(<c>0x1004d870</c>).
    /// 고른 줄은 이름판을 약 절반 밝기(색마다 ×15/31, <c>0x1000c650</c> 섞기 2·세기 16)로 그리고 눌러도 아무 일이 없다(<c>0x10043ab0</c>: <c>+0x44</c> 면 되돌아감).
    /// 세이브에 실린다. 분석-UI 「8. 이미 고른 에피소드 줄은 어떻게 그리나」.
    /// </summary>
    private readonly HashSet<int> _episodesPicked = [];

    /// <summary>
    /// 챕터가 끝났다는 표시 — 필드 행동 11 이 세우고(원본 챕터 상태 <c>+0x10</c>, <c>0x1004e6c0</c>), 모세스에 들어올 때 이것이 서 있으면
    /// 항행 대신 연대표로 간다(<c>0x100f5b07</c>). 연대표에서 에피소드를 고르면 내린다. 세이브에 실린다.
    /// </summary>
    private bool _chapterDone;

    /// <summary>지금 파티 번호(0 살라딘 · 1 베라모드 · 2 크리스티앙, Episode.dat 칸 8). 다른 파티의 에피소드로 가면 인물 상태를 새로 꾸린다.</summary>
    private int _partyNo;

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
                        // 잠금 깃발 넷 — 0xffff(−1)이 아니면 진행 깃발이 서 있어야 열린다(<see cref="EpisodeOpen"/>).
                        int[] locks = [.. Enumerable.Range(0, 4).Select(k => (int)Word(k))];
                        int chapter = Word(7), party = Word(8);
                        if (chapter <= 0) continue;
                        list.Add(new EpisodeEntry(2 * r + side, chapter, party, locks));
                    }
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { /* 자료가 없으면 빈 목록 */ }
        return _episodes = list;
    }

    /// <summary>
    /// 목록의 맨 윗줄(스크롤) — 원본 목록 창 <c>0x100482e0(…, 2열, 6행, 240, 46, …, 스크롤 갈래 3)</c> 은 6행이 넘으면 스크롤 막대
    /// (id 0x2718)로 넘긴다. 전에는 이것이 없어 7번째 줄(에피소드 12 우주의 슈미터)부터 그리지도 누르지도 못해 본편 후반이 막혔다(감사 F1).
    /// </summary>
    private int _episodeTop;

    /// <summary>스크롤 막대 — 화면 (567,174), 높이 286. 위 화살표·아래 화살표는 <c>Obs 0979</c> 장 2·3(19×45), 손잡이는 장 0·1(분석-UI 2.5).</summary>
    private const int EpisodeBarX = 567, EpisodeBarY = 174, EpisodeBarH = 286, EpisodeArrowW = 19, EpisodeArrowH = 45;

    /// <summary>
    /// 목록 줄 수 — 원본 <c>0x10106fe0</c> 은 <b>열리는 가장 큰 번호 + 1</b> 만큼 칸을 넣는다(<c>0x1010716e</c>~<c>0x10107290</c>).
    /// 2열이니 줄 수는 그 절반(올림).
    /// </summary>
    private int EpisodeRowCount()
    {
        int visible = Episodes().Where(EpisodeOpen).Select(e => e.No + 1).DefaultIfEmpty(0).Max();
        return (visible + 1) / 2;
    }

    private void ScrollEpisodes(int delta) =>
        _episodeTop = Math.Clamp(_episodeTop + delta, 0, Math.Max(0, EpisodeRowCount() - EpisodeRows));

    private void OpenEpisodes()
    {
        _episodesOpen = true;
        _titleOpen = false;
        _recordsOpen = false;
        _episodePick = -1;
        _episodeTop = 0;
        ShowMosesBackground(EpisodeBackground);
        StopMusic();
        PlayMusicFile(EpisodeBgm, loop: true);
    }

    /// <summary>
    /// DUELDX_EPISODES=1 이면 곧장 연대표를 연다(화면 밖 시험용). DUELDX_FLAGS 로 깃발을 세워 뒤 에피소드를 열고,
    /// DUELDX_EPISODETOP=&lt;줄&gt; 이면 그만큼 내려 둔다.
    /// </summary>
    private void OpenEpisodesIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_EPISODES") != "1") return;
        foreach (string pair in (Environment.GetEnvironmentVariable("DUELDX_FLAGS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (pair.Split('=') is [var f, var v] && int.TryParse(f, out int flag) && int.TryParse(v, out int val) && (uint)flag < _flags.Length)
                _flags[flag] = (byte)val;
        if (Environment.GetEnvironmentVariable("DUELDX_FLAGS") == "all")
            foreach (var e in Episodes()) foreach (int f in e.Locks) if ((uint)f < _flags.Length) _flags[f] = 1;
        if (Cols != TitleBoardCols || Rows != TitleBoardRows) { ResizeBoard(TitleBoardCols, TitleBoardRows); _battleLoaded = false; }
        OpenEpisodes();
        if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_EPISODETOP"), out int top)) ScrollEpisodes(top);
    }

    /// <summary>그 에피소드 줄이 놓이는 칸(왼위) — 스크롤한 만큼 올린다.</summary>
    private (int X, int Y) EpisodeCell(int no) => (80 + EpisodeCellW * (no % 2), 179 + EpisodeCellH * (no / 2 - _episodeTop));

    /// <summary>그 줄이 지금 보이는 6행 안에 있나.</summary>
    private bool EpisodeRowShown(int no) => no / 2 >= _episodeTop && no / 2 < _episodeTop + EpisodeRows;

    private int EpisodeAt(int bx, int by)
    {
        var (ox, oy) = MosesOrigin();
        var list = Episodes();
        for (int i = 0; i < list.Count; i++)
        {
            if (!EpisodeOpen(list[i]) || _episodesPicked.Contains(list[i].No)) continue;   // 고른 줄은 못 누른다
            if (!EpisodeRowShown(list[i].No)) continue;
            var (x, y) = EpisodeCell(list[i].No);
            if (bx >= ox + x && bx < ox + x + EpisodeCellW && by >= oy + y && by < oy + y + EpisodeCellH) return i;
        }
        return -1;
    }

    /// <summary>
    /// 스크롤 막대 누름 — 위·아래 화살표는 한 줄, 손잡이 위·아래 빈 곳은 한 쪽(6줄)씩(흔한 목록 막대 동작, 원본 갈래 3 의 세부는 가설).
    /// 손잡이 끌기와 마우스 휠은 넣지 않았다(원본 확인 안 함). 막대를 눌렀으면 true.
    /// </summary>
    private bool OnEpisodeBarClick(int bx, int by)
    {
        int rows = EpisodeRowCount();
        if (rows <= EpisodeRows) return false;
        var (ox, oy) = MosesOrigin();
        int x = bx - ox - EpisodeBarX, y = by - oy - EpisodeBarY;
        // 화살표 그림은 장 자리만큼 비껴 찍히므로 누름 칸도 그림 칸으로 본다(그림이 없으면 19×45).
        bool Hit(int motion, int top) => UiFor(EpisodeObs)?.FrameAt(motion, 0) is { W: > 0 } f
            ? x >= f.X && x < f.X + f.W && y >= top + f.Y && y < top + f.Y + f.H
            : x >= 0 && x < EpisodeArrowW && y >= top && y < top + EpisodeArrowH;
        if (Hit(61, 0)) { ScrollEpisodes(-1); return true; }
        if (Hit(63, EpisodeBarH - EpisodeArrowH)) { ScrollEpisodes(1); return true; }
        if (x < 0 || x >= EpisodeArrowW || y < 0 || y >= EpisodeBarH) return false;
        // 길(화살표 사이)을 누르면 손잡이 그림 가운데보다 위면 한 쪽 위로, 아래면 한 쪽 아래로.
        var (thumbY, thumbH) = EpisodeThumb(rows);
        int thumbMid = thumbY + (UiFor(EpisodeObs)?.FrameAt(65, 0)?.Y ?? 0) + thumbH / 2;
        ScrollEpisodes(y < thumbMid ? -EpisodeRows : EpisodeRows);
        return true;
    }

    /// <summary>손잡이 자리(막대 윗변 기준 y)와 높이 — 화살표 둘 사이 길에서 맨 윗줄 비율만큼 내려 놓는다.</summary>
    private (int Y, int H) EpisodeThumb(int rows)
    {
        int thumbH = UiFor(EpisodeObs)?.FrameAt(65, 0) is { H: > 0 } f ? f.H : 20;
        int track = EpisodeBarH - 2 * EpisodeArrowH - thumbH;
        int span = Math.Max(1, rows - EpisodeRows);
        return (EpisodeArrowH + track * Math.Clamp(_episodeTop, 0, span) / span, thumbH);
    }

    /// <summary>연대표가 떠 있으면 클릭을 처리하고 true. 원본처럼 <b>두 번 눌러야</b> 그 에피소드로 간다.</summary>
    private bool OnEpisodesClick(int bx, int by)
    {
        if (!_episodesOpen) return false;
        if (SystemOpen) return OnSystemClick(bx, by);
        if (OnEpisodeBarClick(bx, by)) return true;

        int index = EpisodeAt(bx, by);
        if (index < 0) return true;
        if (_episodePick != index) { _episodePick = index; return true; }

        var entry = Episodes()[index];
        _episodesPicked.Add(entry.No);
        _episodesOpen = false;
        _chapterDone = false;
        // 연대표가 에피소드를 고르면 챕터 상태를 버린다(0x101066a0~) — 새 챕터의 스크립트 변수(0x101bfeac)는 0 에서 시작한다(감사 F10).
        Array.Clear(_chapterVars);
        // 원본은 명부(인물 상태)를 그대로 두고 파티 번호만 바꾼다([0x101b6894] = 파티) — 같은 파티로 이어지면 레벨·장비가 남고,
        // 다른 파티(살라딘 ↔ 베라모드)로 가면 그쪽 인원은 챕터 스크립트(801)가 넣는다 — 인물 자료는 명부 하나라 다른 파티에서 겪은 것이 그대로다.
        SwitchParty(entry.Party);           // 다른 파티면 지금 파티(인원·돈·가방·군단·우편)를 은행에 넣고 그 파티를 꺼낸다
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

        FillRect(_camX, _camY, ViewWidth, ViewHeight, 0xFF000000);
        if (_mosesBg is { } bg)
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    SetPixel(ox + x, oy + y, bg[y * MosesW + x] | 0xFF000000);

        var list = Episodes();
        for (int i = 0; i < list.Count; i++)
        {
            var entry = list[i];
            if (!EpisodeRowShown(entry.No)) continue;
            int row = entry.No / 2 - _episodeTop;           // 보이는 6행 안의 줄
            // 원본은 조건을 통과한 줄까지만 보이고, 못 여는 줄은 이름 없이 빈 채로 둔다.
            if (!EpisodeOpen(entry)) continue;
            // 이름판 — 짝수 번호는 모션 i+1, 홀수는 i+30. 가운데 x 는 왼쪽 160 · 오른쪽 480.
            int motion = entry.No % 2 == 0 ? entry.No + 1 : entry.No + 30;
            // 이름판 그림은 <b>화면 가운데(320)를 기준점</b>으로 왼·오른쪽 자리를 스스로 들고 있고(장 5 = −196, 장 20 = +112),
            // 세로는 기준점이 −58 이라 줄 자리에 58 을 더해 찍는다.
            int cx = ox + 320, cy = oy + 186 + EpisodeCellH * row + 58;
            bool picked = _episodesPicked.Contains(entry.No);
            if (!DrawUi(EpisodeObs, motion, tick, cx, cy, picked ? UiBlend.Dim : UiBlend.Alpha))
                DrawText($"Episode {entry.No} — Chp {entry.Chapter:D4}", cx - 80, cy, White, 12);
            // 고른 표시 [ ] — 원본은 줄마다 0x10043810(x, 65, Obs 0979, 모션 0) 이고 x 는 짝수 237(0x101071de)·홀수 315(0x10107213).
            // 모션 0 = 장 4(163×29, 자리 (−241,−59)) → 화면 (76 또는 394, 185 + 46×줄). 줄 +0x58 이 설 때만 그린다(0x10043040).
            // (전에는 스크롤 화살표 그림인 모션 62·64 를 이름판 양옆에 찍었다 — 감사 F4.)
            if (i == _episodePick && !picked)
            {
                var (x, y) = EpisodeCell(entry.No);
                DrawUi(EpisodeObs, 0, tick, ox + x + (entry.No % 2 == 0 ? 237 : 315), oy + y + 65, UiBlend.Alpha);
            }
        }

        // 스크롤 막대 — 6줄이 넘을 때만(원본 목록 창 갈래 3). 위 화살표 모션 61 @ (567,174), 아래 63 @ (567,415), 손잡이 65.
        int rows = EpisodeRowCount();
        if (rows > EpisodeRows)
        {
            DrawUi(EpisodeObs, 61, tick, ox + EpisodeBarX, oy + EpisodeBarY, UiBlend.Alpha);
            DrawUi(EpisodeObs, 63, tick, ox + EpisodeBarX, oy + EpisodeBarY + EpisodeBarH - EpisodeArrowH, UiBlend.Alpha);
            var (thumbY, _) = EpisodeThumb(rows);
            DrawUi(EpisodeObs, 65, tick, ox + EpisodeBarX, oy + EpisodeBarY + thumbY, UiBlend.Alpha);
        }

        DrawSystem();
        DrawToast();
    }
}

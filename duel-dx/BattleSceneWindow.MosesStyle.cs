using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 모세스 전직(STYLE CHANGE) 페이지(mo-1, 페이지 7) — 파티원의 체질을 바꾼다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-모세스 8절: 배경 Bgr 0042(「Style Change · Ok」 글자는 그림에 있다).
/// <list type="bullet">
/// <item>인물 단추 다섯 — <c>Obs 302</c> 모션 <c>i+4</c>(평소)·<c>i+15</c>(고름) @ <b>(70i+48, 330)</b>, 초상화는 그 위 +(32,50)</item>
/// <item>능력치 패널 (100,100) · 어빌리티 미리보기 (395,70) 164×20 여섯 줄</item>
/// <item>체질·형 단추 다섯 — <c>Obs 283</c> 모션 0, 68×28 @ x {36,73,146,220,257} y {200,244,280,244,200},
/// 글자 TXR 878~882, 지금 것은 초록 <c>0x00FF00</c> 나머지는 회색 <c>0xB4B4B4</c></item>
/// <item>직업 계열 단추 — <c>Obs 329</c> 모션 2i/2i+1 네 자리(1차), 나가기 <c>Obs 287</c> @ (455,430)</item>
/// </list>
/// 고르면 확인창(제목 TXR 932 「전직」, 본문 930) → 승인하면 <b>Snd 582</b> 와 알림창(TXR 934 「전직되었습니다.」).
/// 다섯 단추는 체질(사이클론·오즈마 …)이 아니라 <b>계열 안의 형</b>이다 — <c>Dep.dat</c> 한 계열의 직업 다섯(일반·공격·방어·고속·보조)이
/// 순서대로 그 다섯 칸이고, 인물의 체질(<c>CChr</c> 14)은 그대로 둔 채 <b>직업 번호만</b> 그 형의 것으로 바꾼다.
/// 계열(Dep) 자체를 바꾸는 일(위 계열 단추)은 아직 안 만들었다 — 지금 계열을 보여 주기만 한다. 파티는 이 전투의 아군 셋이다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int StyleBackground = 42, StylePortraitObs = 302, StyleBodyObs = 283, StyleFamilyObs = 329;
    private const int SoundStyleDone = 582;

    /// <summary>체질 단추 다섯 자리(분석-모세스 8절) — 글자는 TXR 878~882.</summary>
    private static readonly (int X, int Y)[] StyleBodyCells = [(36, 200), (73, 244), (146, 280), (220, 244), (257, 200)];

    /// <summary>직업 계열 단추 네 자리(1차) — (x, y, 너비, 높이).</summary>
    private static readonly (int X, int Y, int W, int H)[] StyleFamilyCells =
        [(70, 107, 79, 71), (107, 70, 71, 78), (184, 70, 70, 78), (213, 107, 78, 71)];

    private int _styleUnit;

    private List<int> StyleParty() => [.. Enumerable.Range(0, _units.Length).Where(i => _units[i].IsAlly)];

    private void OpenMosesStyle()
    {
        _mosesPage = 7;
        _mosesPageAt = _lastTime;
        _mosesFade = MosesFadeTicks;
        _mosesHover = -1;
        _styleUnit = StyleParty().FirstOrDefault();
        Play(581);
        ShowMosesBackground(StyleBackground);
    }

    /// <summary>전직 페이지가 열려 있으면 클릭을 처리하고 true.</summary>
    private bool OnMosesStyleClick(int bx, int by)
    {
        if (_mosesPage != 7) return false;
        var (ox, oy) = MosesOrigin();
        int x = bx - ox, y = by - oy;

        if (x >= 455 && x < 633 && y >= 430 && y < 457) { MosesGoBack(); return true; }        // 나가기

        var party = StyleParty();
        for (int i = 0; i < party.Count && i < 5; i++)
            if (x >= 70 * i + 48 - 32 && x < 70 * i + 48 + 32 && y >= 330 - 10 && y < 330 + 70)
            {
                _styleUnit = party[i];
                Play(MosesClickSound);
                return true;
            }

        var jobs = StyleJobs();
        for (int i = 0; i < StyleBodyCells.Length; i++)
        {
            var (cx, cy) = StyleBodyCells[i];
            if (x < cx || x >= cx + 68 || y < cy || y >= cy + 28) continue;
            if (i >= jobs.Count || _units[_styleUnit].Data is not { } c || c.JobId == jobs[i]) return true;
            ushort pick = jobs[i];
            string title = _db?.T(932) is { Length: > 0 } t ? t : "전직";
            string text = _db?.T(930) is { Length: > 0 } b2 ? b2 : "전직하시겠습니까?";
            _confirm = (title, text, () => ChangeJob(pick));
            return true;
        }
        return true;
    }

    /// <summary>고른 인물의 계열에 딸린 직업들 — 칸 다섯(일반·공격·방어·고속·보조)의 차례가 곧 이 차례다.</summary>
    private List<ushort> StyleJobs()
    {
        if (_db is not { } db || _units[_styleUnit].Data is not { } c) return [];
        return [.. db.Deps.FirstOrDefault(d => d.Jobs.Contains(c.JobId))?.Jobs ?? []];
    }

    private void ChangeJob(ushort jobId)
    {
        var unit = _units[_styleUnit];
        if (unit.Data is not { } c) return;
        unit.Data = c with { JobId = jobId };
        RefreshUnitStats(unit);
        Play(SoundStyleDone);
        _notice = (_db?.T(934) is { Length: > 0 } t ? t : "전직되었습니다.", _lastTime + 60 / TicksPerSecond);
    }

    private void DrawMosesStyle(int ox, int oy, int tick)
    {
        var party = StyleParty();
        for (int i = 0; i < party.Count && i < 5; i++)
        {
            int cx = ox + 70 * i + 48, cy = oy + 330;
            DrawUi(StylePortraitObs, party[i] == _styleUnit ? i + 15 : i + 4, tick, cx, cy, UiBlend.Alpha);
            // 초상화는 단추보다 커서 칸(60×60)에 맞춰 줄여 그린다 — 원본은 같은 크기라 그대로 얹는다.
            if (_units[party[i]].Data is { } pc && _faces.TryGetValue(pc.Code, out var face))
                BlitScaled(face, cx - 30, cy + 20, 60, 60);
        }

        if (_units[_styleUnit].Data is not { } c || _db is not { } db) return;

        // 능력치 패널 — 원본은 (100,100) 이지만 거기는 계열 그림과 겹쳐 글이 안 읽혀, 데모는 오른쪽 빈 곳에 놓는다.
        DarkenRect(ox + 387, oy + 232, 180, 96);
        DrawGameFrame(ox + 395, oy + 240, 164, 80, db.T(c.NameId));
        string[] stats =
        [
            $"LEVEL {c.Level}",
            $"체질  {db.BodyName(c.Body)}",
            $"계열  {db.FamilyName(c)}",
            $"직업  {db.JobName(c)}",
        ];
        for (int i = 0; i < stats.Length; i++) DrawText(stats[i], ox + 405, oy + 246 + i * 17, White, 12);

        // 어빌리티 미리보기 (395,70) 164×20 여섯 줄
        DarkenRect(ox + 391, oy + 66, 172, 128);
        for (int i = 0; i < Math.Min(6, c.Abilities.Length); i++)
        {
            var (abilityId, level) = c.Abilities[i];
            if (!db.Abilities.TryGetValue(abilityId, out var ab)) continue;
            DrawText($"{db.T(ab.NameId)} Lv{level}", ox + 397, oy + 74 + i * 20, White, 12);
        }

        // 형 단추 다섯 — 지금 직업인 칸은 초록
        var styleJobs = StyleJobs();
        for (int i = 0; i < StyleBodyCells.Length; i++)
        {
            var (cx, cy) = StyleBodyCells[i];
            DrawUi(StyleBodyObs, 0, tick, ox + cx, oy + cy, UiBlend.Alpha);
            string label = db.T((ushort)(878 + i));
            var (_, lw, lh) = GetText(label, White, 12);
            bool now = i < styleJobs.Count && styleJobs[i] == c.JobId;
            DrawText(label, ox + cx + (68 - lw) / 2, oy + cy + (28 - lh) / 2, now ? 0xFF00FF00 : 0xFFB4B4B4, 12);
        }

        // 직업 계열 단추(1차) — 지금은 보여 주기만 한다
        for (int i = 0; i < StyleFamilyCells.Length; i++)
        {
            var (fx, fy, _, _) = StyleFamilyCells[i];
            DrawUi(StyleFamilyObs, 2 * i, tick, ox + fx, oy + fy, UiBlend.Alpha);
        }

        if (!DrawUi(MosesExitObs, 0, tick, ox + 455, oy + 430, UiBlend.Alpha))
            DrawText("EXIT", ox + 455, oy + 434, White);
    }
}

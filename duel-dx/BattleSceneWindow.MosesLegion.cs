using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 모세스 용병관리(MERCENARY) 페이지(mo-1, 페이지 6) — 파티원에게 군단을 붙이고 뗀다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-모세스 9절 배치 그대로: 배경 Bgr 0039(「Mercenary · Reset · Set」 글자는 그림에 있다).
/// <list type="bullet">
/// <item>인물 단추 다섯 — <c>Obs 302</c> 모션 <c>i+4</c> @ <b>(70i+48, 330)</b>, 배속돼 있으면 모션 14 를 +(20,84) 에</item>
/// <item>군단 목록 — <b>(445, 70)</b> 에 <b>117×23 다섯 줄</b>, 줄 아이콘 <c>Obs 1291</c> 모션 5</item>
/// <item>해제 / 배속 단추 — <c>Obs 283</c> 68×28 @ (70,293) · (160,293), 나가기 <c>Obs 287</c> @ (456,430)</item>
/// <item>진형 칸 — 화면 <c>x = 40·dx + 150</c>, <c>y = 32·dy + 186</c>. 진형 여섯의 칸 표는 <see cref="LegionData.FormationCells"/></item>
/// <item>오른쪽 글 — 군단 이름 (450,233) · 설명 (450,268) · 구분선 (450,360) · <c>진형 : %s</c> (450,372)</item>
/// </list>
/// 배속에 성공하면 <b>Snd 584</b>. 원본은 파티가 <b>가진</b> 군단만 목록에 올리지만(스크립트 행동 713 으로 얻는다),
/// 이 데모에는 파티 명부가 없어 <c>For.dat</c> 의 군단을 모두 올린다. 부하 그림(대장 밑 여섯)은 그 인물들의 Obs 가
/// assets 에 없어 칸과 이름만 그린다 — 원본은 <c>CChr+0x0C</c> 모션 2 로 부하를 세운다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int LegionBackground = 39, LegionRowObs = 1291, LegionRowIconMotion = 5;
    private const int LegionRows = 5, LegionRowW = 117, LegionRowH = 23;
    private const int SoundLegionSet = 584;

    private Dictionary<int, LegionData>? _legions;
    private int _legionUnit, _legionTop, _legionPick = -1;

    /// <summary>인물마다 붙인 군단 번호(원본 <c>CChr+0x1c</c>) — 이 데모는 전투판 인물 단위로 기억한다.</summary>
    private readonly Dictionary<int, int> _unitLegion = [];

    private Dictionary<int, LegionData> Legions() =>
        _legions ??= LegionData.ParseAll(_db?.Files.Read("Dat", "For.dat"));

    private void OpenMosesLegion()
    {
        _mosesPage = 6;
        _mosesPageAt = _lastTime;
        _mosesFade = MosesFadeTicks;
        _mosesHover = -1;
        _legionUnit = StyleParty().FirstOrDefault();
        _legionTop = 0;
        _legionPick = -1;
        Play(583);
        ShowMosesBackground(LegionBackground);
    }

    private List<LegionData> LegionList() => [.. Legions().Values.OrderBy(l => l.Id)];

    /// <summary>용병관리 페이지가 열려 있으면 클릭을 처리하고 true.</summary>
    private bool OnMosesLegionClick(int bx, int by)
    {
        if (_mosesPage != 6) return false;
        var (ox, oy) = MosesOrigin();
        int x = bx - ox, y = by - oy;
        var list = LegionList();

        if (x >= 456 && x < 634 && y >= 430 && y < 457) { MosesGoBack(); return true; }           // 나가기
        if (x >= 70 && x < 138 && y >= 293 && y < 321)                                            // 해제
        {
            _unitLegion.Remove(_legionUnit);
            Play(MosesClickSound);
            return true;
        }
        if (x >= 160 && x < 228 && y >= 293 && y < 321)                                           // 배속
        {
            if (_legionPick >= 0 && _legionPick < list.Count)
            {
                _unitLegion[_legionUnit] = list[_legionPick].Id;
                Play(SoundLegionSet);
            }
            return true;
        }

        var party = StyleParty();
        for (int i = 0; i < party.Count && i < 5; i++)
            if (x >= 70 * i + 48 - 32 && x < 70 * i + 48 + 32 && y >= 330 - 10 && y < 330 + 70)
            {
                _legionUnit = party[i];
                Play(MosesClickSound);
                return true;
            }

        if (x >= 445 && x < 445 + LegionRowW && y >= 70 && y < 70 + LegionRows * LegionRowH)
        {
            int row = _legionTop + (y - 70) / LegionRowH;
            if (row < list.Count) _legionPick = row;
            return true;
        }
        // 목록 오른쪽 가장자리를 누르면 한 줄씩 넘긴다(원본은 스크롤 막대)
        if (x >= 445 + LegionRowW && x < 445 + LegionRowW + 16 && y >= 70 && y < 70 + LegionRows * LegionRowH)
        {
            int max = Math.Max(0, list.Count - LegionRows);
            _legionTop = y < 70 + LegionRows * LegionRowH / 2 ? Math.Max(0, _legionTop - 1) : Math.Min(max, _legionTop + 1);
        }
        return true;
    }

    private void DrawMosesLegion(int ox, int oy, int tick)
    {
        if (_db is not { } db) return;
        var list = LegionList();
        var party = StyleParty();

        // 인물 단추 — 배속된 인물은 표(모션 14)를 단추 아래에
        for (int i = 0; i < party.Count && i < 5; i++)
        {
            int cx = ox + 70 * i + 48, cy = oy + 330;
            DrawUi(StylePortraitObs, party[i] == _legionUnit ? i + 15 : i + 4, tick, cx, cy, UiBlend.Alpha);
            if (_units[party[i]].Data is { } pc && _faces.TryGetValue(pc.Code, out var face))
                BlitScaled(face, cx - 30, cy + 20, 60, 60);
            if (_unitLegion.ContainsKey(party[i])) DrawUi(StylePortraitObs, 14, tick, cx + 20, cy + 84, UiBlend.Alpha);
        }

        // 군단 목록
        for (int r = 0; r < LegionRows; r++)
        {
            int index = _legionTop + r;
            if (index >= list.Count) break;
            int rx = ox + 445, ry = oy + 70 + r * LegionRowH;
            DrawUi(LegionRowObs, LegionRowIconMotion, tick, rx, ry, UiBlend.Alpha);
            DrawText(db.T(list[index].NameId), rx + 18, ry + 4, index == _legionPick ? 0xFF00FF00 : White, 11);
        }

        var shown = _legionPick >= 0 && _legionPick < list.Count ? list[_legionPick]
                  : _unitLegion.TryGetValue(_legionUnit, out int id) ? Legions().GetValueOrDefault(id) : null;
        if (shown != null) DrawLegionDetail(ox, oy, db, shown);

        DrawUi(StyleBodyObs, 0, tick, ox + 70, oy + 293, UiBlend.Alpha);
        DrawUi(StyleBodyObs, 0, tick, ox + 160, oy + 293, UiBlend.Alpha);
        DrawText("해제", ox + 88, oy + 300, White, 12);
        DrawText("배속", ox + 178, oy + 300, White, 12);
        if (!DrawUi(MosesExitObs, 0, tick, ox + 456, oy + 430, UiBlend.Alpha))
            DrawText("EXIT", ox + 456, oy + 434, White);
    }

    /// <summary>고른 군단 — 진형 칸에 대장·부하를 놓고 오른쪽에 이름·설명·진형을 적는다.</summary>
    private void DrawLegionDetail(int ox, int oy, GameDatabase db, LegionData legion)
    {
        // 진형 칸 — 가운데(0,0)가 대장, 나머지는 진형표대로
        DarkenRect(ox + 60, oy + 100, 260, 190, 12);
        DrawLegionCell(ox, oy, 0, 0, db.T(_units[_legionUnit].Data?.NameId ?? 0), 0xFFFFE070);
        for (int i = 0; i < legion.Members.Length && i < LegionData.FormationCells[legion.Formation].Length; i++)
        {
            var (dx, dy) = LegionData.FormationCells[legion.Formation][i];
            string name = db.Character(legion.Members[i]) is { } m ? db.T(m.NameId) : $"Chr {legion.Members[i]}";
            DrawLegionCell(ox, oy, dx, dy, name, White);
        }

        DarkenRect(ox + 440, oy + 225, 190, 165, 12);
        DrawText(db.T(legion.NameId), ox + 450, oy + 233, 0xFFFFE070, 13);
        foreach (var (line, i) in WrapText(db.T(legion.DescriptionId), 180, 11f).Select((l, i) => (l, i)))
        {
            if (i >= 5) break;
            DrawText(line, ox + 450, oy + 268 + i * 15, White, 11);
        }
        for (int xx = ox + 450; xx < ox + 620; xx++) SetPixel(xx, oy + 360, BoxLine);
        DrawText($"진형 : {db.T(legion.FormationNameId)}", ox + 450, oy + 372, White, 12);
    }

    private void DrawLegionCell(int ox, int oy, int dx, int dy, string name, uint color)
    {
        int x = ox + 40 * dx + 150, y = oy + 32 * dy + 186;
        StrokeRect(x - 18, y - 14, 36, 28, BoxLine);
        // 칸이 40픽셀이라 이름이 길면 잘라 넣는다(원본은 이 자리에 부하 그림을 세운다).
        string label = name.Length > 5 ? name[..5] : name;
        var (_, w, h) = GetText(label, color, 10);
        DrawText(label, x - w / 2, y - h / 2, color, 10);
    }
}

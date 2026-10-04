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

    /// <summary>인물마다 붙인 군단 번호(원본 <c>CChr+0x1c</c>) — <b>Chr 번호</b>로 기억한다(전투마다 명부가 다시 만들어지므로 자리 번호로는 못 잇는다).</summary>
    private readonly Dictionary<int, int> _unitLegion = [];

    /// <summary>파티 목록(<see cref="StyleParty"/>)은 Chr 번호라 그대로 열쇠다.</summary>
    private static int LegionKey(int chr) => chr;

    private Dictionary<int, LegionData> Legions() =>
        // 원본 For.dat 위에 편집기가 고친 군단(assets/data/legions/*.json)을 얹는다.
        _legions ??= _db is { } db ? LegionBook.LoadAll(db.Files) : [];

    private void OpenMosesLegion()
    {
        _mosesPage = 6;
        _mosesPageAt = _lastTime;
        StartFade();
        _mosesHover = -1;
        _legionUnit = StyleParty().FirstOrDefault();
        _legionTop = 0;
        _legionPick = -1;
        Play(583);
        ShowMosesBackground(LegionBackground);
    }

    /// <summary>
    /// 용병관리에 나오는 군단 — 스크립트 713 으로 <b>얻은 것</b>만(원본 파티 객체 `+0x910` 목록, 분석-군단). 얻은 기록이 없는 옛 세이브는 예전처럼 전부.
    /// 배속할 수 있는 군단 — 얻은 것 가운데 <b>아직 아무에게도 배속 안 된 것</b>(원본은 배속하면 파티 군단 목록에서 빼고 해제·교체 때 되돌린다, 0x1010106f·0x10100ed0, ba-15).</summary>
    /// <remarks>
    /// 원본 파티 군단 목록은 <b>(군단, 개수)</b>다(<c>0x1004df50</c>: 같은 군단을 또 얻으면 개수 +1) — 31 스트라이커즈(Chp 0016·0061)·74 해커(Chp 0049·Btl 0305)는
    /// 두 번 얻으므로 두 인물에게 하나씩 붙일 수 있다. 남은 개수 = 얻은 개수 − 이 파티의 다른 인물이 붙인 수(감사3 L8). 전에는 개수가 없어 한 명만 붙였다.
    /// </remarks>
    private List<LegionData> LegionList()
    {
        // 이 파티 동료만 센다 — 다른 파티 인물의 배속은 그 파티 목록에서 빠진 것이다(파티 객체마다 +0x910 목록).
        bool InParty(int chr) => _members.Count == 0 || _members.Contains(chr);
        return [.. Legions().Values.Where(l => (_legionsKnown ? _ownedLegions.CountOf(l.Id) : 1)
                                             > _unitLegion.Count(p => p.Value == l.Id && InParty(p.Key))).OrderBy(l => l.Id)];
    }

    /// <summary>파티가 얻은 군단 번호와 개수(스크립트 713). 세이브에 실린다 — 개수만큼 번호를 되풀이해 적는다(옛 세이브는 모두 1개).</summary>
    private readonly LegionCounts _ownedLegions = new();

    /// <summary>
    /// 파티 군단 목록 — 원본 파티 객체 <c>+0x910</c> (군단, 개수) 최대 32가지(<c>0x1004df50</c>). 새 군단이 33가지째면 버린다.
    /// 훑으면 번호를 개수만큼 되풀이해 낸다(세이브·파티 합치기가 그대로 옮기게).
    /// </summary>
    private sealed class LegionCounts : IEnumerable<int>
    {
        private const int MaxKinds = 32;
        private readonly Dictionary<int, int> _count = [];

        /// <summary>하나 더 얻는다(713). 늘었으면 true.</summary>
        public bool Add(int id)
        {
            if (_count.TryGetValue(id, out int n)) { _count[id] = n + 1; return true; }
            if (_count.Count >= MaxKinds) return false;
            _count[id] = 1;
            return true;
        }

        public bool Contains(int id) => _count.ContainsKey(id);
        public int CountOf(int id) => _count.GetValueOrDefault(id);
        public void Clear() => _count.Clear();

        public IEnumerator<int> GetEnumerator()
        {
            foreach (var (id, n) in _count)
                for (int i = 0; i < n; i++) yield return id;
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>얻은 군단을 세고 있나 — 새 게임과 그 뒤 세이브는 참(얻은 것만 보인다), 그 전 세이브는 거짓(전부 보인다).</summary>
    private bool _legionsKnown;

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
            _legionPick = -1;                              // 목록이 바뀐다(해제한 군단이 돌아온다)
            _unitLegion.Remove(LegionKey(_legionUnit));   // 해제는 소리가 없다(0x10100e53, ba-20 S L2)
            return true;
        }
        if (x >= 160 && x < 228 && y >= 293 && y < 321)                                           // 배속
        {
            if (_legionPick >= 0 && _legionPick < list.Count)
            {
                _unitLegion[LegionKey(_legionUnit)] = list[_legionPick].Id;
                _legionPick = -1;                // 목록이 바뀌었다(배속한 군단은 빠진다)
                Play(SoundLegionSet);
            }
            return true;
        }

        if (OnStylePartyArrow(x, y)) return true;
        var party = StylePartyPage();
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
        var party = StylePartyPage();
        DrawStylePartyArrows(ox, oy);

        // 인물 단추 — 배속된 인물은 표(모션 14)를 단추 아래에
        for (int i = 0; i < party.Count && i < 5; i++)
        {
            int cx = ox + 70 * i + 48, cy = oy + 330;
            DrawUi(StylePortraitObs, party[i] == _legionUnit ? i + 15 : i + 4, tick, cx, cy, UiBlend.Alpha);
            if (PartyData(party[i]) is { } pc)
            {
                Fld.LoadFieldFace(pc);
                if (_faces.TryGetValue(pc.Code, out var face)) BlitScaled(face, cx + 2, cy + 20, 60, 60);
            }
            if (_unitLegion.ContainsKey(LegionKey(party[i]))) DrawUi(StylePortraitObs, 14, tick, cx + 20, cy + 84, UiBlend.Alpha);
        }

        int mx = _mouse.X - ox, my = _mouse.Y - oy;
        // 군단 목록
        for (int r = 0; r < LegionRows; r++)
        {
            int index = _legionTop + r;
            if (index >= list.Count) break;
            int rx = ox + 445, ry = oy + 70 + r * LegionRowH;
            // 줄 틀 Obs 1291 모션 5(138×28)는 0x10043810 으로 단 덧그림 — 마우스가 올라간 줄에만(0x100fae6b).
            if (mx >= 445 && mx < 445 + LegionRowW && my >= 70 + r * LegionRowH && my < 70 + (r + 1) * LegionRowH)
                DrawUi(LegionRowObs, LegionRowIconMotion, tick, rx, ry, UiBlend.Alpha);
            DrawText(db.T(list[index].NameId), rx + 18, ry + 4, index == _legionPick ? 0xFF00FF00 : White, 11);
        }

        var shown = _legionPick >= 0 && _legionPick < list.Count ? list[_legionPick]
                  : _unitLegion.TryGetValue(LegionKey(_legionUnit), out int id) ? Legions().GetValueOrDefault(id) : null;
        if (shown != null) DrawLegionDetail(ox, oy, db, shown);

        // 해제(Reset)·배속(Set)·Ok 글자는 배경 그림(Bgr 0039)에 있다 — 알약 Obs 283·287 은 마우스가 올라갔을 때만 덧그리는 보조 그림(상점과 같다).
        bool Over(int x, int y, int w, int h) => mx >= x && mx < x + w && my >= y && my < y + h;
        if (Over(70, 293, 68, 28)) DrawUi(StyleBodyObs, 0, tick, ox + 70, oy + 293, UiBlend.Alpha);
        if (Over(160, 293, 68, 28)) DrawUi(StyleBodyObs, 0, tick, ox + 160, oy + 293, UiBlend.Alpha);
        if (Over(456, 430, 178, 27)) DrawUi(MosesExitObs, 0, tick, ox + 456, oy + 430, UiBlend.Alpha);
    }

    /// <summary>고른 군단 — 진형 칸에 대장·부하를 놓고 오른쪽에 이름·설명·진형을 적는다.</summary>
    private void DrawLegionDetail(int ox, int oy, GameDatabase db, LegionData legion)
    {
        // 진형 칸 — 가운데(0,0)가 대장, 나머지는 진형표대로
        // 원본은 이 자리에 전투 유닛 그림(모션 2 = 앞모습 서기)을 세운다 — 대장 @ (150,186), 부하 @ (40dx+150, 32dy+186),
        // 아래 칸이 앞에 오게(0x1010072c~0x10100abf, ba-20 G9). 그림이 assets 에 없는 인물만 예전처럼 네모+이름.
        DarkenRect(ox + 60, oy + 100, 260, 190, 12);
        var cells = new List<(int Dx, int Dy, int Chr, string Name, uint Color)> { (0, 0, _legionUnit, db.T(PartyData(_legionUnit)?.NameId ?? 0), 0xFFFFE070) };
        for (int i = 0; i < legion.Members.Length && i < LegionData.FormationCells[legion.Formation].Length; i++)
        {
            var (dx, dy) = LegionData.FormationCells[legion.Formation][i];
            if (legion.Members[i] == 0) continue;
            cells.Add((dx, dy, legion.Members[i], db.Character(legion.Members[i]) is { } m ? db.T(m.NameId) : $"Chr {legion.Members[i]}", White));
        }
        foreach (var (dx, dy, chr, name, color) in cells.OrderBy(c => c.Dy))
        {
            if (PreviewSprite(chr)?.FrameOfMotion(2, 0, false) is { } frame)
                BlitMasked(frame.Px, frame.W, frame.H, ox + 40 * dx + 150 + frame.X, oy + 32 * dy + 186 + frame.Y);
            else DrawLegionCell(ox, oy, dx, dy, name, color);
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
        // 부하 칸 도움말(0x100f7b50, ba-20 S 6) — 마우스가 얹힌 진형 칸(dx = ⌊(mx−130)/40⌋, dy = ⌊(my−170)/32⌋, |dx|+|dy| ≤ 2, 대장 칸 제외)에
        // 부하가 있으면 화면 (296,75) 에 틀 없이 흰 글: 이름·소속·직업 / LP·PSY·DEP·DEX / [어빌리티] 최대 다섯 줄. 딱지는 TXR 827~834.
        int hx = (int)Math.Floor((_mouse.X - ox - 130) / 40.0), hy = (int)Math.Floor((_mouse.Y - oy - 170) / 32.0);
        if (Math.Abs(hx) + Math.Abs(hy) > 2 || (hx, hy) == (0, 0)) return;
        var formation = LegionData.FormationCells[legion.Formation];
        for (int i = 0; i < legion.Members.Length && i < formation.Length; i++)
        {
            if (formation[i] != (hx, hy) || legion.Members[i] == 0 || db.Character(legion.Members[i]) is not { } m) continue;
            string T(ushort id, string fallback) => db.T(id) is { Length: > 0 } t ? t : fallback;
            var lines = new List<string>
            {
                $"{T(827, "이름")} : {db.T(m.NameId)}",
                $"{T(828, "소속")} : {db.T(legion.NameId)}",
                $"{T(829, "직업")} : {db.JobName(m)}",
                "---------------------",
                $"{T(830, "LP")} : {m.Lp}",
                $"{T(831, "PSY")} : {db.Psy(m)}",
                $"{T(832, "DEP")} : {db.Dep(m)}",
                $"{T(833, "DEX")} : {db.Dex(m)}",
                "---------------------",
                $"[ {T(834, "어빌리티")} ]",
            };
            foreach (var (ability, level) in m.Abilities.Where(a => a.Ability > 0).Take(5))
                if (db.Abilities.TryGetValue(ability, out var ab)) lines.Add($"{db.T(ab.NameId)} Lv {level}");
            for (int l = 0; l < lines.Count; l++) DrawText(lines[l], ox + 296, oy + 75 + l * 14, White, 11);
            return;
        }
    }

    private readonly Dictionary<int, UnitSprite?> _previewSprites = [];
    private Dictionary<int, (string Name, ushort SpriteCode)>? _previewManifests;

    /// <summary>모세스 화면에서 세워 보이는 인물 그림 — 전투에 이미 읽은 것을 쓰고, 없으면 assets/characters 에서 처음 쓸 때 읽는다. 없으면 null.</summary>
    private UnitSprite? PreviewSprite(int chr)
    {
        if (_sprites.TryGetValue(chr, out var loaded)) return loaded;
        if (_previewSprites.TryGetValue(chr, out var cached)) return cached;
        UnitSprite? sprite = null;
        try
        {
            string root = FindRepoAssetsRoot();
            _previewManifests ??= CollectExportedManifests(root).ToDictionary(kv => kv.Key, kv => (kv.Value.Name, (ushort)kv.Value.SpriteCode));
            if (_previewManifests.TryGetValue(chr, out var m))
            {
                string path = Path.Combine(root, CharacterExport.FolderNameFor(chr, m.Name), CharacterExport.ObsFileName(m.SpriteCode));
                var motions = ObsSprite.Decode(path);
                if (motions.Count > 0) sprite = new UnitSprite(motions, ObsMotionTable.Load(path));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { }
        return _previewSprites[chr] = sprite;
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

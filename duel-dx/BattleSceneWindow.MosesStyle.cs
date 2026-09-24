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
/// 다섯 단추는 체질(사이클론·오즈마 …)이 아니라 <b>계열 안의 형</b>이다 — <c>Dep.dat</c> 한 계열의 직업 다섯(일반·공격·방어·고속·보조)이
/// 순서대로 그 다섯 칸이고, 인물의 체질(<c>CChr</c> 14)은 그대로 둔 채 <b>직업 번호만</b> 그 형의 것으로 바꾼다.
/// <para>
/// 후보를 거르는 규칙(<c>0x100f99c0</c>, <c>0x100f9aae</c>): <b>「레벨 ≥ Job +4」 하나뿐</b>이다 — 체질도 돈도 어빌리티도 안 본다.
/// 통과 못 한 형은 흐려지는 것이 아니라 <b>단추가 아예 안 만들어진다</b>. 그래서 1레벨이면 단추가 하나(지금 직업)뿐이고
/// 바꿀 수 있는 직업이 0개다. 3단계 직업이면 단추를 하나도 안 만든다(<c>0x100fa43a</c>).
/// 색은 셋 — 지금 직업 초록 <c>0x00FF00</c>, 고른(미리보기) 칸 청록 <c>0x00FFFF</c>, 나머지 회색 <c>0xB4B4B4</c>.
/// <b>형 단추는 두 번 눌러야</b> 확인창이 뜬다(첫 클릭은 미리보기, <c>0x10100afa</c>).
/// </para>
/// 확인창 제목 TXR 932 「전직」, 본문은 <b>남은 EXP 가 0 이면 930, 아니면 931</b>(「… 경험치는 사라집니다」).
/// 승인하면 <b>Snd 582</b> 와 알림창(TXR 934). 전직이 바꾸는 것은 <b>직업 번호와 남은 EXP = 0</b> 뿐이다 —
/// 레벨·누적 경험치·능력치·배운 어빌리티는 하나도 다시 계산하지 않는다(<c>0x10101215</c>).
/// <b>계열 갈아타기</b>(위 단추 넷)는 <b>1단계이고 레벨 30 이상</b>일 때만 나오고, 제 계열을 뺀 네 계열의 <b>2단계 일반형</b> 하나로 간다 —
/// 한 번 누르면 바로 확인창이고, 2단계에서는 단추가 아예 없다(계열 갈아타기는 평생 한 번).
/// 3단계 단추 둘(레벨 60 + 필요 어빌리티, 「처음 계열」의 3단계)은 화면 자리를 아직 몰라 안 만들었다.
/// 파티는 이 전투의 아군 셋이다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int StyleBackground = 42, StylePortraitObs = 302, StyleBodyObs = 283, StyleFamilyObs = 329;
    /// <summary>3단계 단추 안의 계열 아이콘(Obs 1334, 5장)과 「M.G」 표시(Obs 1426, 2장) — 분석-모세스 8절 「3단계 단추 둘」.</summary>
    private const int StyleIconObs = 1334, StyleMarkObs = 1426;

    /// <summary>3단계 단추 둘 — id 91 (107,70) 71×78 · id 92 (184,70) 70×78. 1차 계열 단추의 두 번째·세 번째 부채꼴 자리다(0x100fa1f5·0x100fa33a).</summary>
    private static readonly (int X, int Y, int W, int H, int IconX, int IconY, int MarkX, int MarkY)[] StyleTierCells =
        [(107, 70, 71, 78, 38, 31, 38, 26), (184, 70, 70, 78, 30, 31, 30, 26)];

    /// <summary>계열 아이콘 Obs 1334 의 모션 — 0 PSYCLON · 1 FORCETRAL · 2 OZMA · 3 TAKIRION · 4 ARKLOST 를 계열 차례(사이클론·타키리온·포스트럴·아크로스트·오즈마)로.</summary>
    private static readonly int[] StyleIconMotion = [0, 3, 1, 4, 2];
    private const int SoundStyleDone = 582;

    /// <summary>체질 단추 다섯 자리(분석-모세스 8절) — 글자는 TXR 878~882.</summary>
    private static readonly (int X, int Y)[] StyleBodyCells = [(36, 200), (73, 244), (146, 280), (220, 244), (257, 200)];

    /// <summary>직업 계열 단추 네 자리(1차) — (x, y, 너비, 높이).</summary>
    private static readonly (int X, int Y, int W, int H)[] StyleFamilyCells =
        [(70, 107, 79, 71), (107, 70, 71, 78), (184, 70, 70, 78), (213, 107, 78, 71)];

    /// <summary>전직 화면에서 고른 인물의 <b>Chr 번호</b> — 전투 판의 자리 번호가 아니다. 파티원은 전투에 안 서 있어도 여기 나온다.</summary>
    private int _styleUnit;

    /// <summary>고른 인물의 자료 — 전투 판에 서 있으면 그 유닛의 것(살아 있는 값), 아니면 파티가 들고 있는 것.</summary>
    private CharacterData? StyleData() => _units.FirstOrDefault(u => u.ChrCode == _styleUnit)?.Data ?? _party.GetValueOrDefault(_styleUnit);

    /// <summary>고른 인물의 자료를 바꾼다 — 파티와 (있으면) 전투 판의 유닛 둘 다.</summary>
    private void SetStyleData(CharacterData c)
    {
        _party[_styleUnit] = c;
        foreach (var u in _units) if (u.ChrCode == _styleUnit) u.Data = c;
    }

    private CharacterData? PartyData(int chr) => _units.FirstOrDefault(u => u.ChrCode == chr)?.Data ?? _party.GetValueOrDefault(chr);

    /// <summary>미리보기 중인 형 칸(원본 <c>+0x2de0</c>) — 같은 칸을 다시 눌러야 확인창이 뜬다.</summary>
    private int _stylePick = -1;

    /// <summary>
    /// 전직 단추에 서는 인물 — 스크립트 801 로 들어온 <b>동료(주인공들)</b>만. 전투 자료가 내 편에 끼워 준 코어헌터 용병 따위는 빼야 한다(사용자 지적).
    /// 원본은 파티 객체(<c>0x101b6888</c>, `+8` Chr 번호 u32×64)의 인원을 보여 준다 — 그 목록은 801 이 채운다.
    /// 옛 세이브(동료 목록이 없음)면 예전처럼 내 편 전부.
    /// </summary>
    private List<int> StyleParty()
    {
        // 원본 파티 객체의 인원(801 로 들어온 동료) 차례 — 레이토스 길드처럼 주인공이 안 서는 전투 뒤에도 파티가 그대로 보여야 한다(사용자 지적).
        if (_members.Count > 0) return [.. _members.Where(c => _party.ContainsKey(c) || _units.Any(u => u.ChrCode == c))];
        // 동료 목록을 못 만든 옛 세이브 — 내가 움직이는 내 부대(편 4)만. 편 3 동맹 NPC(제이슨)는 뺀다.
        return [.. _units.Where(u => u.PlayerControlled).Select(u => u.ChrCode).Distinct()];
    }

    /// <summary>스크립트 801 로 들어온 동료의 Chr 번호(802 로 빠진다) — 원본 파티 객체의 인원 목록. 세이브에 실린다.</summary>
    private readonly HashSet<int> _members = [];

    private void OpenMosesStyle()
    {
        _mosesPage = 7;
        _mosesPageAt = _lastTime;
        StartFade();
        _mosesHover = -1;
        _styleUnit = StyleParty().FirstOrDefault();
        _stylePick = StyleCurrentCell();
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
        // STATUS — 고른 인물의 스테이터스 창(장비·장착 어빌리티·어빌리티 올리기)을 모세스 위에 연다.
        if (x >= 455 && x < 523 && y >= 390 && y < 417)
        {
            OpenStatusFor(_styleUnit);      // 전투에 안 선 파티원도 임시 유닛으로 연다
            Play(MosesClickSound);
            return true;
        }

        var party = StyleParty();
        for (int i = 0; i < party.Count && i < 5; i++)
            if (x >= 70 * i + 48 - 32 && x < 70 * i + 48 + 32 && y >= 330 - 10 && y < 330 + 70)
            {
                _styleUnit = party[i];
                _stylePick = StyleCurrentCell();
                Play(MosesClickSound);
                return true;
            }

        // 계열 단추 넷 — 한 번 누르면 바로 확인창(0x10100c4e).
        var families = StyleFamilies();
        for (int i = 0; i < families.Count && i < StyleFamilyCells.Length; i++)
        {
            var (fx, fy, fw, fh) = StyleFamilyCells[i];
            if (x < fx || x >= fx + fw || y < fy || y >= fy + fh) continue;
            ushort target = families[i].Job;
            string ftitle = _db?.T(932) is { Length: > 0 } ft ? ft : "전직";
            string ftext = _db?.T(930) is { Length: > 0 } fb ? fb : "전직하시겠습니까?";
            _confirm = (ftitle, ftext, () => ChangeJob(target));
            return true;
        }

        // 3단계 단추 둘 — 계열 단추처럼 한 번에 확인창(0x100fa1f5·0x100fa33a).
        var tiers = StyleTierJobs();
        for (int i = 0; i < tiers.Count && i < StyleTierCells.Length; i++)
        {
            var cell = StyleTierCells[tiers[i].Slot];
            if (x < cell.X || x >= cell.X + cell.W || y < cell.Y || y >= cell.Y + cell.H) continue;
            ushort target = tiers[i].Job;
            string ttitle = _db?.T(932) is { Length: > 0 } tt ? tt : "전직";
            string ttext = _db?.T(930) is { Length: > 0 } tb ? tb : "전직하시겠습니까?";
            _confirm = (ttitle, ttext, () => ChangeJob(target));
            return true;
        }

        var jobs = StyleJobs();
        for (int i = 0; i < StyleBodyCells.Length; i++)
        {
            var (cx, cy) = StyleBodyCells[i];
            if (x < cx || x >= cx + 68 || y < cy || y >= cy + 28) continue;
            // 만들어지지 않은 단추와 지금 직업 칸은 아무 일도 안 한다.
            if (i >= jobs.Count || StyleData() is not { } c || c.JobId == jobs[i]) return true;
            // 첫 클릭은 미리보기만, 같은 칸을 다시 눌러야 확인창이 뜬다(0x10100afa).
            if (_stylePick != i) { _stylePick = i; Play(MosesClickSound); return true; }
            ushort pick = jobs[i];
            string title = _db?.T(932) is { Length: > 0 } t ? t : "전직";
            // 남은 EXP 가 있으면 「사라집니다」 본문(931), 없으면 930. 설정으로 EXP 를 남기면 사라질 것이 없으니 930.
            string text = c.Exp == 0 || _keepJobExp
                ? _db?.T(930) is { Length: > 0 } b2 ? b2 : "전직하시겠습니까?"
                : $"{_db?.T(c.NameId)}의 경험치가 {c.Exp} 남았습니다.\n전직하면 {c.Exp} 의 경험치는 사라집니다.\n전직할까요?";
            _confirm = (title, text, () => ChangeJob(pick));
            return true;
        }
        return true;
    }

    /// <summary>
    /// 고른 인물이 <b>지금 고를 수 있는</b> 형들 — 칸 다섯(일반·공격·방어·고속·보조)의 차례 그대로,
    /// <b>레벨이 닿는 데까지만</b>. 3단계 직업이면 빈 목록이다.
    /// </summary>
    private List<ushort> StyleJobs()
    {
        if (_db is not { } db || StyleData() is not { } c) return [];
        if (db.Deps.FirstOrDefault(d => d.Jobs.Contains(c.JobId)) is not { } dep) return [];
        if ((dep.Id - 1) % 3 + 1 == 3) return [];          // 3단계는 더 갈 곳이 없다(0x100fa43a)
        int count = 0;
        for (int i = 0; i < dep.Jobs.Length; i++)
            if (db.Jobs.GetValueOrDefault(dep.Jobs[i]) is { } job && c.Level >= job.NeedLevel) count = i + 1;
        return [.. dep.Jobs.Take(count)];
    }

    /// <summary>고른 인물의 계열 레코드 — 직업 번호를 품은 <c>Dep</c>.</summary>
    private DepData? StyleDep() =>
        _db is { } db && StyleData() is { } c ? db.Deps.FirstOrDefault(d => d.Jobs.Contains(c.JobId)) : null;

    /// <summary>계열 안 단계 — 1·2·3. <c>Dep</c> 번호 셋이 한 계열이다.</summary>
    private static int StyleTier(DepData dep) => (dep.Id - 1) % 3 + 1;

    /// <summary>
    /// 지금 고를 수 있는 <b>다른 계열</b>들 — 제 계열을 뺀 넷, 저마다 그 계열 <b>2단계 일반형</b> 하나로 간다.
    /// </summary>
    /// <remarks>
    /// <b>1단계이고 레벨 30 이상</b>일 때만 만든다(<c>0x100f9cd8</c>) — 2단계에서는 단추를 부수고 다시 안 만들어,
    /// <b>계열 갈아타기는 평생 한 번</b>이다. 제 계열은 목록에서 빠지므로 <b>제 계열의 2단계로는 갈 수 없다</b>(<c>0x10101434</c>).
    /// </remarks>
    private List<(int Family, ushort Job)> StyleFamilies()
    {
        if (_db is not { } db || StyleData() is not { } c) return [];
        if (StyleDep() is not { } dep || StyleTier(dep) != 1 || c.Level < 30) return [];
        int mine = (dep.Id - 1) / 3;
        var list = new List<(int, ushort)>();
        for (int i = 0; i < 4; i++)
        {
            int family = i < mine ? i : i + 1;
            if (db.Deps.FirstOrDefault(d => d.Id == 3 * family + 2) is { Jobs.Length: > 0 } target)
                list.Add((family, target.Jobs[0]));
        }
        return list;
    }

    /// <summary>
    /// 3단계 후보 둘 — <b>2단계이고 레벨 ≥ 60</b>일 때, 제 계열 3단계 Dep(3f+3)의 직업 가운데 <b>Job `+6` 어빌리티를 가진 것</b>만
    /// (0x100fa167: 레벨 < 60 이면 둘 다 없음, 0x100fa1c5: `CChr+0x1bc6[어빌리티]` 가 0·0xff 면 그 단추 없음). 칸 번호는 0·1 그대로 남긴다.
    /// 계열은 <b>처음 계열</b>(<c>CChr+0x3a0</c> — 로드 때 .chr 직업의 Dep 을 한 번 베낀 값)이다. 2단계는 늘 다른 계열이라 지금 계열로 보면
    /// 죠안(메텔 → 오즈클론)이 엘샤루핌·가프리스 대신 엉뚱한 3단계를 찾았다(분석-체질 「전직과 배운 어빌리티」).
    /// </summary>
    private List<(int Slot, ushort Job)> StyleTierJobs()
    {
        if (_db is not { } db || StyleData() is not { } c) return [];
        if (StyleDep() is not { } dep || StyleTier(dep) != 2 || c.Level < 60) return [];
        int family = StyleOriginFamily(c);
        if (db.Deps.FirstOrDefault(d => d.Id == 3 * family + 3) is not { } third) return [];
        var list = new List<(int, ushort)>();
        for (int k = 0; k < third.Jobs.Length && k < StyleTierCells.Length; k++)
        {
            if (db.Jobs.GetValueOrDefault(third.Jobs[k]) is not { } job) continue;
            // 필요 어빌리티는 레벨이 0 도 0xff 도 아니어야 한다(0x100fa1c5).
            if (job.NeedAbility != 0 && !c.Abilities.Any(a => a.Ability == job.NeedAbility && a.Level is > 0 and < 0xff)) continue;
            list.Add((k, third.Jobs[k]));
        }
        return list;
    }

    /// <summary>처음 계열(0~4) — 그 인물 .chr 직업이 든 Dep 의 계열. 못 찾으면 지금 계열.</summary>
    private int StyleOriginFamily(CharacterData c)
    {
        if (_db is { } db && db.Character(c.Code) is { } baseChr
            && db.Deps.FirstOrDefault(d => d.Jobs.Contains(baseChr.JobId)) is { } origin)
            return (origin.Id - 1) / 3;
        return StyleDep() is { } d ? (d.Id - 1) / 3 : 0;
    }

    /// <summary>미리보기 중인 직업 — 형 칸을 골랐으면 그 직업, 아니면 지금 직업(원본 0x100fa4e0 이 채우는 목록의 주인).</summary>
    private ushort StylePreviewJob()
    {
        var jobs = StyleJobs();
        return StyleData() is { } c && _stylePick >= 0 && _stylePick < jobs.Count ? jobs[_stylePick] : StyleData()?.JobId ?? 0;
    }

    /// <summary>지금 직업이 앉아 있는 칸 번호 — 없으면 −1.</summary>
    private int StyleCurrentCell()
    {
        if (StyleData() is not { } c) return -1;
        var jobs = StyleJobs();
        return jobs.FindIndex(j => j == c.JobId);
    }

    /// <summary>전직 — 원본은 <b>직업 번호와 남은 EXP 만</b> 건드린다(능력치·레벨·어빌리티는 그대로). 설정 「전직할 때 EXP 유지」면 EXP 도 남긴다.</summary>
    private void ChangeJob(ushort jobId)
    {
        if (StyleData() is not { } c) return;
        SetStyleData(c with { JobId = jobId, Exp = _keepJobExp ? c.Exp : 0 });
        _stylePick = StyleCurrentCell();
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
            // 초상화는 단추(64×130, 왼위 기준) 안 +(32,50) 을 가운데로 — 60×60 이니 왼위는 +(2,20). 전에는 30픽셀 왼쪽에 찍혀 틀과 어긋났다.
            if (PartyData(party[i]) is { } pc)
            {
                LoadFieldFace(pc);      // 전투에 안 선 동료는 얼굴을 아직 안 읽었을 수 있다
                if (_faces.TryGetValue(pc.Code, out var face)) BlitScaled(face, cx + 2, cy + 20, 60, 60);
            }
        }

        if (StyleData() is not { } c || _db is not { } db) return;

        // 능력치 패널(0x10034030: 이름 · 직업 · 체질 · LEVEL(TXR 160) · EXP(161) · LP(34) · TP(38)) — 원본 자리 (100,100) 140×164 는
        // 형 단추 다섯과 겹쳐 글이 안 읽히므로, 데모는 오른쪽 어빌리티 목록 아래 빈 칸(395,232)에 둔다.
        DarkenRect(ox + 387, oy + 224, 180, 126);
        DrawGameFrame(ox + 395, oy + 232, 164, 110, db.T(c.NameId));
        string[] stats =
        [
            $"{db.JobName(c)} · {db.BodyName(c.Body)}",
            $"{db.T(160)} {c.Level}",
            $"{db.T(161)} {c.Exp}",
            $"{db.T(34)} {c.Lp}",
            $"{db.T(38)} {c.Tp}",
        ];
        for (int i = 0; i < stats.Length; i++) DrawText(stats[i], ox + 405, oy + 240 + i * 18, White, 12);

        // 어빌리티 미리보기 (395,70) 164×20 여섯 줄 — 원본(0x100fa4e0)은 <b>미리보기 중인 직업</b>의 Job.dat 어빌리티 11칸을 이름만 나열한다.
        // 형 칸을 누르면 그 직업 것으로 바뀐다.
        DarkenRect(ox + 391, oy + 66, 172, 128);
        var previewList = db.Jobs.GetValueOrDefault(StylePreviewJob())?.AbilityList.Where(a => a != 0).ToList() ?? [];
        for (int i = 0; i < Math.Min(6, previewList.Count); i++)
        {
            if (!db.Abilities.TryGetValue(previewList[i], out var ab)) continue;
            string mark = c.HasAbility(previewList[i]) ? $" Lv{c.AbilityLevel(previewList[i])}" : "";
            DrawText($"{db.T(ab.NameId)}{mark}", ox + 397, oy + 74 + i * 20, c.HasAbility(previewList[i]) ? White : 0xFFB4B4B4, 12);
        }
        if (previewList.Count > 6) DrawText($"… 외 {previewList.Count - 6}", ox + 397, oy + 74 + 6 * 20 - 6, 0xFFB4B4B4, 11);

        // 형 단추 — 레벨이 닿는 칸까지만 만든다(못 고르는 칸은 흐려지는 것이 아니라 아예 없다).
        var styleJobs = StyleJobs();
        for (int i = 0; i < styleJobs.Count && i < StyleBodyCells.Length; i++)
        {
            var (cx, cy) = StyleBodyCells[i];
            DrawUi(StyleBodyObs, 0, tick, ox + cx, oy + cy, UiBlend.Alpha);
            string label = db.T((ushort)(878 + i));
            var (_, lw, lh) = GetText(label, White, 12);
            uint colour = styleJobs[i] == c.JobId ? 0xFF00FF00 : i == _stylePick ? 0xFF00FFFF : 0xFFB4B4B4;
            DrawText(label, ox + cx + (68 - lw) / 2, oy + cy + (28 - lh) / 2, colour, 12);
        }

        // 3단계 단추 둘 — 2단계·레벨 60·필수 어빌리티가 있을 때만. 조각은 계열 단추처럼 제 자리를 들고 있어 (320,240)에 찍는다.
        int mx = _mouse.X - ox, my = _mouse.Y - oy;
        foreach (var (slot, tierJob) in StyleTierJobs())
        {
            var cell = StyleTierCells[slot];
            bool over = mx >= cell.X && mx < cell.X + cell.W && my >= cell.Y && my < cell.Y + cell.H;
            DrawUi(StyleFamilyObs, 2 + 2 * slot + (over ? 1 : 0), tick, ox + 320, oy + 240, UiBlend.Alpha);
            int family = StyleData() is { } oc ? StyleOriginFamily(oc) : 0;
            DrawUi(StyleIconObs, StyleIconMotion[Math.Clamp(family, 0, 4)], tick, ox + cell.X + cell.IconX, oy + cell.Y + cell.IconY, UiBlend.Alpha);
            DrawUi(StyleMarkObs, slot, tick, ox + cell.X + cell.MarkX, oy + cell.Y + cell.MarkY, UiBlend.Alpha);
        }

        // 계열 단추 넷 — 1단계·레벨 30 일 때만 나온다(못 가는 동안은 아예 없다).
        var familyCells = StyleFamilies();
        for (int i = 0; i < familyCells.Count && i < StyleFamilyCells.Length; i++)
        {
            // 이 그림들은 조각마다 <b>제 자리를 스스로 들고 있다</b> — 연대표 이름판과 같은 꼴이라
            // 칸 자리가 아니라 <b>화면 가운데(320,240)</b>에 찍어야 제자리에 온다. 누르는 칸만 StyleFamilyCells 로 잡는다.
            DrawUi(StyleFamilyObs, 2 * i, tick, ox + 320, oy + 240, UiBlend.Alpha);
        }

        // STATUS 단추 — 원본에는 없는 데모 단추. 형 단추와 같은 알약(Obs 283)에 글자를 얹는다.
        {
            int bx0 = ox + 455, by0 = oy + 390;
            if (!DrawUi(StyleBodyObs, 0, tick, bx0, by0, UiBlend.Alpha)) StrokeRect(bx0, by0, 68, 28, White);
            var (_, sw, sh) = GetText("STATUS", White, 12);
            DrawText("STATUS", bx0 + (68 - sw) / 2, by0 + (28 - sh) / 2, White, 12);
        }

        // Ok 글자는 배경 그림(Bgr 0042)에 있다 — 알약 Obs 287 은 마우스 올림에만.
        if (mx >= 455 && mx < 633 && my >= 430 && my < 457) DrawUi(MosesExitObs, 0, tick, ox + 455, oy + 430, UiBlend.Alpha);
    }
}

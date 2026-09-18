using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투가 끝나면 나오는 모세스 단말 화면(mo-1) — 주 화면·항행·파티 페이지와 ESC 시스템 메뉴.
/// </summary>
/// <remarks>
/// 옵시디안 분석-모세스: 모세스는 챕터 장면(장면 4)이고 <b>배경은 영상이 아니라 <c>Bgr\NNNN.bgr</c>(640×480 JPEG) 한 장</b>이다.
/// <list type="bullet">
/// <item>주 화면 — 배경은 Chp 머리 둘째 워드(0010 → 52), 로고 Obs 0291 모션 1 @ (10,10),
/// 아이콘 여섯 개 Obs 0291 모션 4·6·8·10·12·23 을 칸 (50/105/160,400)·(25/80/135,435) 안 (+14,+19) 에. 칸 틀은 Obs 0246 세 조각.
/// 설명(TXR 485~490)은 마우스 +(16,16) 툴팁, 누르면 Snd 66.</item>
/// <item>항행 — 단계는 0 성계 · 1 행성 · 2 장소인데 챕터가 최저 단계를 정한다(Chp 머리 22번째 워드, 0010 = 2 → <b>장소 고르기부터</b>).
/// 배경은 성계 레코드 끝 워드(70), 행성 구체는 행성 레코드의 Obs·모션(0010 리치 = 0630 모션 1) @ (320,220),
/// 장소 칸은 여덟 자리 고정표에 Obs 0643 모션 0 단추 + Obs 0498 모션 2 아이콘 @ +(20,14) + 오른쪽 정렬 이름,
/// 칸은 10프레임 세로 와이프로 나타난다. 장소 값 v 는 &lt;10000 전투 · 10000+ 필드 · 20000+ 상점.</item>
/// <item>파티 — 배경은 주 화면과 같고 같은 칸 표의 두 칸(전직 TXR 1326 · 용병관리 1327, 아이콘 Obs 0498 모션 0·1).</item>
/// <item>ESC — 모세스 전용 시스템 메뉴 네 줄(LOAD·SAVE·VOLUME·EXIT GAME, 전투와 달리 MISSION·RESTART 가 없다), Snd 578.</item>
/// </list>
/// 페이지를 바꿀 때 나는 소리: 항행 564 · 파티 580 · 상점 572 · 전직 581 · 용병관리 583 · 뒤로 569/570.
/// 원본은 페이지 사이에 Mov 영상과 UI 전환 효과(15틱 알파 크로스페이드)를 거는데, 데모는 <b>15틱 검은 페이드</b>로만 흉내 낸다(가설 아님, 생략).
/// 640×480 화면을 우리 판 가운데에 1배로 놓는다. 메일·통신·상점·전직·용병관리 페이지는 아직 없다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int MosesW = 640, MosesH = 480;
    private const int MosesObs = 291, MosesFrameObs = 246, MosesBackObs = 248;
    private const int MosesCellObs = 643, MosesCellIconObs = 498, MosesMarkObs = 163;
    private const int MosesExitObs = 287, MosesMailBackground = 94;
    private const int MosesClickSound = 66, MosesFadeTicks = 15;
    private const int MosesChapter = 10;
    /// <summary>이 데모가 가진 단 하나의 전투 — Chp 0010 리치의 「코어헌터 훈련장」(장소 값 45).</summary>
    private const int DemoBattle = 45;

    /// <summary>주 화면 아이콘 — 칸 왼위 자리와 Obs 0291 모션, 설명 TXR, 누르면 가는 페이지.</summary>
    private static readonly (int X, int Y, int Motion, ushort Text, string Name, int Page)[] MosesIcons =
    [
        (50, 400, 4, 485, "NAVIGATION", 0),
        (105, 400, 6, 486, "MAIL", 1),
        (160, 400, 8, 487, "MESSAGE", 2),
        (25, 435, 10, 488, "ITEM SHOP", 3),
        (80, 435, 12, 489, "VT SHOP", 4),
        (135, 435, 23, 490, "PARTY", 5),
    ];

    /// <summary>항행·파티가 함께 쓰는 여덟 칸 자리(코드 안 상수) — 위아래로 퍼지는 활 모양.</summary>
    private static readonly (int X, int Y)[] MosesCells =
        [(438, 180), (442, 220), (421, 140), (434, 260), (383, 100), (412, 300), (335, 60), (360, 340)];

    /// <summary>파티 페이지의 두 칸 — Obs 0498 아이콘 모션, 이름 TXR, 들어갈 때 소리.</summary>
    private static readonly (int Icon, ushort Text, string Name, int Sound)[] MosesPartyItems =
        [(0, 1326, "전직", 581), (1, 1327, "용병관리", 583)];

    private bool _mosesOpen;
    private uint[]? _mosesBg;
    private int _mosesBgId = -1;
    private int _mosesHover = -1;
    private int _mosesPage = -1;          // -1 주 화면 · 0 항행 · 5 파티
    private int _mosesStep = 2;           // 항행 단계 — 1 행성 고르기 · 2 장소 고르기
    private int _mosesPlanet;             // 고른 행성 번호
    private int _mosesFade;               // 남은 페이드 틱
    private double _mosesPageAt;          // 페이지를 연 때(칸 와이프용)
    private MosesChapterFile? _mosesChp;

    private (int X, int Y) MosesOrigin() => ((BoardWidth - MosesW) / 2, _camY + (ViewHeight - MosesH) / 2);

    /// <summary>DUELDX_MOSES=1 이면 전투를 기다리지 않고 바로 모세스 화면을 연다(화면 밖 시험용).</summary>
    private void OpenMosesIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_MOSES") == "1") OpenMoses();
    }

    /// <summary>전투가 끝나고 배너를 넘기면 모세스 화면으로 간다. 챕터를 주면 그 챕터로.</summary>
    private void OpenMoses(MosesChapterFile? chapter = null)
    {
        _mosesOpen = true;
        _mosesHover = -1;
        _mosesPage = -1;
        if (chapter != null) _mosesChp = chapter;
        LoadMosesChapter();
        ShowMosesBackground(_mosesChp?.Background ?? 52);
        _mixer.StopMusic();
        PlayMusicFile(_mosesChp?.Bgm ?? 19, loop: true);   // 챕터 BGM — Chp 머리 셋째 워드
    }

    private void LoadMosesChapter()
    {
        if (_mosesChp != null) return;
        string path = Path.Combine(AssetsFolder.Find("moses"), "chp", $"{MosesChapter:D4}.chp");
        if (File.Exists(path)) _mosesChp = MosesChapterFile.Parse(File.ReadAllBytes(path));
    }

    /// <summary>배경 그림(.bgr 은 그냥 JPEG)을 읽어 둔다.</summary>
    private void ShowMosesBackground(int id)
    {
        if (_mosesBgId == id) return;
        try
        {
            string path = Path.Combine(AssetsFolder.Find("moses"), "bgr", $"{id:D4}.bgr");
            if (!File.Exists(path)) return;
            using var bitmap = new Bitmap(path);
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var pixels = new uint[MosesW * MosesH];
                for (int y = 0; y < Math.Min(MosesH, bitmap.Height); y++)
                {
                    var row = new Span<uint>((void*)(data.Scan0 + y * data.Stride), Math.Min(MosesW, bitmap.Width));
                    row.CopyTo(pixels.AsSpan(y * MosesW, row.Length));
                }
                _mosesBg = pixels;
                _mosesBgId = id;
            }
            finally { bitmap.UnlockBits(data); }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            _loadError = $"모세스 배경: {ex.Message}";
        }
    }

    /// <summary>지금 페이지에 보이는 칸들 — (자리, 이름, 아이콘 모션, 누르면 할 일).</summary>
    private List<(int X, int Y, string Name, int Icon, Action Click)> MosesCellList()
    {
        var list = new List<(int, int, string, int, Action)>();
        if (_mosesPage == 0 && _mosesStep == 2 && _mosesChp is { } chp && chp.PlanetOf(_mosesPlanet) is { } planet)
        {
            foreach (int placeNo in planet.Places)
            {
                if (chp.PlaceOf(placeNo) is not { } place || place.Auto != 0) continue;   // 자동 발생 장소는 목록에 없다
                int i = list.Count;
                if (i >= MosesCells.Length) break;
                var (x, y) = MosesCells[i];
                int value = place.Value;
                list.Add((x, y, Text(place.NameText), 2, () => MosesEnterPlace(value)));
            }
        }
        else if (_mosesPage == 5)
            foreach (var (icon, text, name, sound) in MosesPartyItems)
            {
                int i = list.Count;
                var (x, y) = MosesCells[i];
                string label = Text(text) is { Length: > 0 } t ? t : name;
                list.Add((x, y, label, icon, () => { Play(sound); Toast($"{label} — 아직 만들지 않았습니다"); }));
            }
        return list;
    }

    private string Text(int id) => id > 0 && _db is { } db ? db.T((ushort)id) : "";

    /// <summary>장소를 누르면 — 값 &lt;10000 전투 · 10000+ 필드 · 20000+ 상점.</summary>
    private void MosesEnterPlace(int value)
    {
        if (value >= 20000) { OpenMosesShop(0, value - 20000); return; }
        if (value >= 10000) { Toast($"필드 {value - 10000} 은 아직 만들지 않았습니다"); return; }
        if (value != DemoBattle) { Toast($"전투 {value} 은 이 데모에 없습니다"); return; }
        _mosesOpen = false;
        _mixer.StopMusic();
        RestartBattle();
    }

    private void MosesGoPage(int page)
    {
        switch (page)
        {
            case 0:
                Play(564);                                             // NAVIGATION
                // 챕터가 정한 최저 단계에서 시작한다 — Chp 0010 은 2(장소 고르기)라 행성 고르기를 지나간다.
                _mosesStep = Math.Max(1, _mosesChp?.StartStep ?? 1);
                _mosesPlanet = _mosesStep == 2 ? _mosesChp?.StartNumber ?? 0 : 0;
                break;
            case 1: Play(571); break;                                  // MAIL — 새 편지가 있으면 나는 소리
            case 2: Toast("통신은 아직 만들지 않았습니다"); return;
            case 3 or 4: OpenMosesShop(page - 3); return;
            case 5: Play(580); break;                                  // PARTY
        }
        _mosesPage = page;
        _mosesPageAt = _lastTime;
        _mosesFade = MosesFadeTicks;
        _mosesHover = -1;
        // 항행은 성계 배경, 메일은 94, 파티는 주 화면과 같은 챕터 배경
        ShowMosesBackground(page switch
        {
            0 => _mosesChp?.SystemBackground ?? 70,
            1 => MosesMailBackground,
            _ => _mosesChp?.Background ?? 52,
        });
    }

    /// <summary>뒤로 단추 — 단계가 최저면 주 화면으로. 소리는 단계별 569(장소→행성)·570(행성→성계).</summary>
    private void MosesGoBack()
    {
        // 항행 단계 2 에서 최저 단계가 그보다 낮으면 행성 고르기로 한 단계만 내려간다.
        if (_mosesPage == 0 && _mosesStep == 2 && (_mosesChp?.StartStep ?? 2) < 2)
        {
            Play(569);
            _mosesStep = 1;
            _mosesFade = MosesFadeTicks;
            _mosesHover = -1;
            return;
        }
        Play(_mosesPage == 0 ? 570 : 569);
        _mosesPage = -1;
        _mosesFade = MosesFadeTicks;
        _mosesHover = -1;
        ShowMosesBackground(_mosesChp?.Background ?? 52);
    }

    private int MosesIconAt(int bx, int by)
    {
        var (ox, oy) = MosesOrigin();
        if (_mosesPage == -1)
        {
            for (int i = 0; i < MosesIcons.Length; i++)
            {
                var icon = MosesIcons[i];
                int x = ox + icon.X, y = oy + icon.Y;
                if (bx >= x && bx < x + 46 && by >= y && by < y + 33) return i;
            }
            return -1;
        }
        if (_mosesPage == 0 && _mosesStep == 1)
        {
            var planets = _mosesChp?.Planets ?? [];
            for (int i = 0; i < planets.Count; i++)
                if (Math.Abs(bx - ox - planets[i].X) <= 20 && Math.Abs(by - oy - planets[i].Y) <= 20) return i;
            return -1;
        }
        var cells = MosesCellList();
        for (int i = 0; i < cells.Count; i++)
            if (bx >= ox + cells[i].X && bx < ox + cells[i].X + 178 && by >= oy + cells[i].Y && by < oy + cells[i].Y + 27)
                return i;
        return -1;
    }

    private bool MosesBackAt(int bx, int by)
    {
        var (ox, oy) = MosesOrigin();
        return _mosesPage != -1 && bx >= ox + 46 && bx < ox + 71 && by >= oy + 244 && by < oy + 270;
    }

    /// <summary>모세스 화면이 떠 있으면 클릭을 처리하고 true.</summary>
    private bool OnMosesClick(int bx, int by)
    {
        if (!_mosesOpen) return false;
        if (SystemOpen) return OnSystemClick(bx, by);
        if (_mosesFade > 0) return true;
        if (OnMosesShopClick(bx, by)) return true;
        if (OnMosesMailClick(bx, by)) return true;
        if (MosesBackAt(bx, by)) { MosesGoBack(); return true; }

        int index = MosesIconAt(bx, by);
        if (index < 0) return true;
        if (_mosesPage == -1)
        {
            Play(MosesClickSound);
            MosesGoPage(MosesIcons[index].Page);
        }
        else if (_mosesPage == 0 && _mosesStep == 1)
        {
            if (_mosesChp is { } chp && index < chp.Planets.Count)
            {
                _mosesPlanet = chp.Planets[index].No;
                _mosesStep = 2;
                _mosesPageAt = _lastTime;
                _mosesFade = MosesFadeTicks;
                _mosesHover = -1;
            }
        }
        else
        {
            var cells = MosesCellList();
            if (index < cells.Count) cells[index].Click();
        }
        return true;
    }

    private void UpdateMosesHover(int bx, int by)
    {
        if (_mosesOpen) _mosesHover = MosesIconAt(bx, by);
    }

    private void DrawMoses()
    {
        if (!_mosesOpen) return;
        var (ox, oy) = MosesOrigin();
        int tick = (int)(_lastTime * TicksPerSecond);

        // 화면 밖은 검게, 가운데에 640×480 단말 화면
        FillRect(0, _camY, BoardWidth, ViewHeight, 0xFF000000);
        if (_mosesBg is { } bg)
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    SetPixel(ox + x, oy + y, bg[y * MosesW + x] | 0xFF000000);

        if (_mosesPage == -1) DrawMosesDesktop(ox, oy, tick);
        else DrawMosesPage(ox, oy, tick);

        DrawMosesTooltip();
        DrawSystem();
        DrawToast();   // 알림은 모세스 화면 위에 — Compose 의 DrawToast 는 이 화면에 가린다

        // 페이지 전환 — 원본은 15틱 알파 크로스페이드, 여기서는 같은 길이의 검은 페이드로 흉내
        if (_mosesFade > 0)
        {
            int dark = 31 * _mosesFade / MosesFadeTicks;
            for (int y = _camY; y < _camY + ViewHeight; y++)
                for (int x = 0; x < BoardWidth; x++)
                {
                    int i = y * BoardWidth + x;
                    _fb[i] = ScaleColor(_fb[i], 31 - dark, 31);
                }
            _mosesFade--;
        }
    }

    private void DrawMosesDesktop(int ox, int oy, int tick)
    {
        DrawUi(MosesObs, 1, tick, ox + 10, oy + 10, UiBlend.Alpha);   // 로고
        for (int i = 0; i < MosesIcons.Length; i++)
        {
            var icon = MosesIcons[i];
            int x = ox + icon.X, y = oy + icon.Y;
            // 칸 틀 Obs 0246 세 조각 — 마우스를 올린 칸만 밝게
            if (i == _mosesHover)
                foreach (var (dx, motion) in new[] { (-8, 0), (2, 1), (26, 2) })
                    DrawUi(MosesFrameObs, motion, tick, x + dx, y, UiBlend.Alpha);
            DrawUi(MosesObs, icon.Motion, tick, x + 14, y + 19, UiBlend.Alpha);
        }
    }

    private void DrawMosesPage(int ox, int oy, int tick)
    {
        if (_mosesPage is 3 or 4)
        {
            DrawMosesShop(ox, oy, tick);
            return;
        }
        if (_mosesPage == 1)
        {
            DrawMosesMail(ox, oy, tick);
            return;
        }

        // 항행 단계 1 — 성도 위 행성들. 마우스를 올린 행성에는 표 Obs 0163 모션 3 과 이름.
        if (_mosesPage == 0 && _mosesStep == 1)
        {
            var planets = _mosesChp?.Planets ?? [];
            for (int i = 0; i < planets.Count; i++)
            {
                var p = planets[i];
                DrawUi(p.MapObs, p.MapMotion, tick, ox + p.X, oy + p.Y, UiBlend.Alpha);
                if (i != _mosesHover) continue;
                DrawUi(MosesMarkObs, 3, tick, ox + p.X, oy + p.Y - 20, UiBlend.Alpha);
                string planetName = Text(p.NameText);
                if (planetName.Length == 0) continue;
                var (_, nw, _) = GetText(planetName, White);
                DrawText(planetName, ox + p.X - nw / 2, oy + p.Y + 34, White);   // 그림에 든 로마자 이름 아래
            }
            if (!DrawUi(MosesBackObs, 0, tick, ox + 46, oy + 244, UiBlend.Alpha))
                DrawText("BACK", ox + 46, oy + 248, White);
            return;
        }

        // 행성 구체 — 항행 단계 2 에서 화면 가운데 조금 위
        if (_mosesPage == 0 && _mosesStep == 2 && _mosesChp is { } chp && chp.PlanetOf(_mosesPlanet) is { } planet)
            DrawUi(planet.GlobeObs, planet.GlobeMotion, tick, ox + 320, oy + 220, UiBlend.Alpha);

        var cells = MosesCellList();
        // 칸은 10프레임 세로 와이프로 나타난다 — 여기서는 칸마다 한 틱씩 늦게 나오게 한다
        int since = (int)((_lastTime - _mosesPageAt) * TicksPerSecond);
        for (int i = 0; i < cells.Count; i++)
        {
            if (since < i + 1) continue;
            var (cx, cy, name, icon, _) = cells[i];
            int x = ox + cx, y = oy + cy;
            DrawUi(MosesCellObs, 0, tick, x, y, UiBlend.Alpha);
            if (i == _mosesHover)
                foreach (var (dx, motion) in new[] { (0, 0), (10, 1), (168, 2) })
                    DrawUi(MosesFrameObs, motion, tick, x + dx, y, UiBlend.Alpha);
            DrawUi(MosesCellIconObs, icon, tick, x + 20, y + 14, UiBlend.Alpha);
            if (name.Length > 0)
            {
                var (_, w, h) = GetText(name, White);
                DrawText(name, x + 178 - 15 - w, y + (27 - h) / 2 + 2, White);   // 오른쪽 정렬 x−15
            }
        }

        if (!DrawUi(MosesBackObs, 0, tick, ox + 46, oy + 244, UiBlend.Alpha))
            DrawText("BACK", ox + 46, oy + 248, White);
    }

    private void DrawMosesTooltip()
    {
        if (_mosesPage != -1 || _mosesHover < 0 || _mosesFade > 0) return;
        string text = Text(MosesIcons[_mosesHover].Text);
        if (text.Length == 0) text = MosesIcons[_mosesHover].Name;
        var (_, w, h) = GetText(text, White);
        int tx = _mouse.X + 16, ty = _mouse.Y + 16;
        FillRect(tx - 4, ty - 2, w + 8, h + 4, 0xD00A1428);
        StrokeRect(tx - 4, ty - 2, w + 8, h + 4, BoxLine);
        DrawText(text, tx, ty, White);
    }
}

/// <summary>모세스가 쓰는 만큼의 <c>Chp\NNNN.chp</c> — 머리·항성계·행성·장소.</summary>
/// <remarks>
/// 파일 배치는 <c>BattleChapters.ParseChp</c> 와 같다. 머리 워드: 1 배경 Bgr · 2 BGM · 21 최저 항행 단계 · 22 그 단계의 번호 · 23 제목 TXR.
/// 행성 84바이트: 워드 0 번호 · 1 지도 Obs · 2 이름 TXR · 4 지도 모션 · 8~15 장소 번호 · 34·35 구체 Obs·모션 · 40·41 성도 자리.
/// 항성계 66바이트: 워드 0 번호 · 2 이름 TXR · 8~15 행성 번호 · 32 배경 Bgr. 장소 20바이트: 0 번호 · 1 이름 TXR · 2 값 · 3 설명 TXR · 9 자동 발생.
/// </remarks>
internal sealed class MosesChapterFile
{
    public sealed record Planet(int No, int NameText, int MapObs, int MapMotion, int X, int Y, int GlobeObs, int GlobeMotion, int[] Places);
    public sealed record Place(int No, int NameText, int Value, int DescText, int Auto);

    public int Id { get; set; }
    public int TitleText { get; private init; }
    public int Background { get; private init; }
    public int Bgm { get; private init; }
    public int ItemShop { get; private init; }
    public int VtShop { get; private init; }
    public int StartStep { get; private init; }
    public int StartNumber { get; private init; }
    public int SystemBackground { get; private init; }
    public IReadOnlyList<Planet> Planets { get; private init; } = [];
    public IReadOnlyList<Place> Places { get; private init; } = [];

    public Planet? PlanetOf(int no) => Planets.FirstOrDefault(p => p.No == no) ?? Planets.FirstOrDefault();
    public Place? PlaceOf(int no) => Places.FirstOrDefault(p => p.No == no);

    public static MosesChapterFile? Parse(byte[] b)
    {
        try
        {
            short H(int o) => BitConverter.ToInt16(b, o);
            int o = 42;
            int n1 = H(o + 6);
            o += 10 + 12 * n1;                  // 성도 점 12바이트
            o += 4 + 30 * H(o);                 // 인물 30바이트
            int systems = H(o);
            o += 4;
            int systemBg = systems > 0 ? H(o + 64) : 70;
            o += 66 * systems;
            int planetCount = H(o);
            o += 4;
            var planets = new List<Planet>();
            for (int i = 0; i < planetCount; i++, o += 84)
                planets.Add(new Planet(H(o), H(o + 4), H(o + 2), H(o + 8), H(o + 80), H(o + 82), H(o + 68), H(o + 70),
                                       [.. Enumerable.Range(0, 8).Select(k => (int)H(o + 16 + 2 * k)).Where(v => v >= 0)]));
            int placeCount = H(o);
            o += 4;
            var places = new List<Place>();
            for (int i = 0; i < placeCount; i++, o += 20)
                places.Add(new Place(H(o), H(o + 2), H(o + 4), H(o + 6), H(o + 18)));
            return new MosesChapterFile
            {
                TitleText = H(46),
                Background = H(2),
                ItemShop = H(6),
                VtShop = H(8),
                Bgm = H(4),
                StartStep = H(42),
                StartNumber = H(44),
                SystemBackground = systemBg,
                Planets = planets,
                Places = places,
            };
        }
        catch (ArgumentException) { return null; }
    }
}

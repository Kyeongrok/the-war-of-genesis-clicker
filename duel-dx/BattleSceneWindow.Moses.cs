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
/// 원본은 페이지 사이에 Mov 영상과 UI 전환 효과를 거는데, 데모는 페이드로만 흉내 낸다 —
/// 보통은 <b>15틱 검은 페이드</b>(효과 2·3)이고, <b>항행 진입(효과 1)만 30틱에 걸쳐 파랑 채널을 씻어 낸다</b>.
/// 640×480 화면을 우리 판 가운데에 1배로 놓는다. 페이지마다 그리는 코드는 BattleSceneWindow.Moses*.cs 로 나눠 두었다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int MosesW = 640, MosesH = 480;
    private const int MosesObs = 291, MosesFrameObs = 246, MosesBackObs = 248;
    private const int MosesCellObs = 643, MosesCellIconObs = 498, MosesMarkObs = 163;
    private const int MosesExitObs = 287, MosesMailBackground = 94;
    private const int MosesClickSound = 66, MosesFadeTicks = 15;

    /// <summary>항행 진입(효과 1)만 <b>30틱</b>에 걸쳐 파랑을 씻어 낸다 — 나머지는 15틱 검정([[분석-모세스]] 2절).</summary>
    private const int MosesBlueFadeTicks = 30;
    private const int MosesChapter = 10;

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

    /// <summary>파티 칸을 누르면 하는 일 — 0 전직 페이지, 1 용병관리 페이지.</summary>
    private void RunPartyItem(int index, string label)
    {
        if (index == 0) { OpenMosesStyle(); return; }
        if (index == 1) { OpenMosesLegion(); return; }
        Play(MosesPartyItems[index].Sound);
        Toast($"{label} — 아직 만들지 않았습니다");
    }

    private bool _mosesOpen;
    private uint[]? _mosesBg;
    private int _mosesBgId = -1;
    private (int X, int Y) _mosesBgAt;
    private int _mosesHover = -1;
    private int _mosesPage = -1;          // -1 주 화면 · 0 항행 · 5 파티
    private int _mosesStep = 2;           // 항행 단계 — 1 행성 고르기 · 2 장소 고르기
    private int _mosesPlanet;             // 고른 행성 번호
    private int _mosesFade;               // 남은 페이드 틱
    private bool _mosesBlueFade;          // 항행 진입(효과 1)은 검정이 아니라 파랑 씻김이다
    private double _mosesPageAt;          // 페이지를 연 때(칸 와이프용)
    private ChapterFile? _mosesChp;

    /// <summary>페이지 전환 페이드를 건다 — <paramref name="blue"/> 면 항행 진입용 30틱 파랑 씻김.</summary>
    private void StartFade(bool blue = false)
    {
        _mosesBlueFade = blue;
        _mosesFade = blue ? MosesBlueFadeTicks : MosesFadeTicks;
    }

    private (int X, int Y) MosesOrigin() => (_camX + (ViewWidth - MosesW) / 2, _camY + (ViewHeight - MosesH) / 2);

    /// <summary>DUELDX_MOSES=1 이면 전투를 기다리지 않고 바로 모세스 화면을 연다(화면 밖 시험용).</summary>
    private void OpenMosesIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_MOSES") != "1") return;
        // DUELDX_FLAGS=5=1,7=2 면 진행 깃발을 미리 세운다(화면 밖 시험용 — 깃발로 잠긴 챕터 사건·장소를 본다).
        foreach (string pair in (Environment.GetEnvironmentVariable("DUELDX_FLAGS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (pair.Split('=') is [var f, var v] && int.TryParse(f, out int flag) && int.TryParse(v, out int val) && (uint)flag < _flags.Length)
                _flags[flag] = (byte)val;
        // DUELDX_CHAPTER=<Chp 번호> 면 그 챕터로 연다(화면 밖 시험용) — 없으면 첫 전투의 챕터.
        OpenMoses(int.TryParse(Environment.GetEnvironmentVariable("DUELDX_CHAPTER"), out int chapterId) ? LoadChapterFile(chapterId) : null);
        // DUELDX_MOSESPAGE=<페이지> 면 프롤로그를 건너뛰고 그 페이지를 바로 연다(화면 밖 시험용) — 7 전직 · 6 용병관리 · 3 상점.
        if (!int.TryParse(Environment.GetEnvironmentVariable("DUELDX_MOSESPAGE"), out int page)) return;
        if (FieldOpen) LeaveField();
        switch (page)
        {
            case 7: OpenMosesStyle(); break;
            case 6: OpenMosesLegion(); break;
            case 3: OpenMosesShop(0); break;
            case 4: OpenMosesShop(1); break;                            // VT 상점
            case 1 or 2: MosesGoPage(page); _mosesPage = page; break;   // 메일 · 통신
            case 0: MosesGoPage(0); _mosesPageAt = _lastTime - 30.0 / TicksPerSecond; break;   // 항행 — 칸이 다 나온 뒤 모습
        }
    }

    /// <summary>전투가 끝나고 배너를 넘기면 모세스 화면으로 간다. 챕터를 주면 그 챕터로.</summary>
    private void OpenMoses(ChapterFile? chapter = null)
    {
        _mosesOpen = true;
        _mosesHover = -1;
        _mosesPage = -1;
        // 전투는 여기서 닫힌다 — 판을 전투 맵 크기에서 640×480 틀로 되돌려 모세스만 남긴다.
        // (안 그러면 전투 맵 크기 창 한가운데에 모세스가 뜨고 둘레가 검게 남는다.)
        if (Cols != TitleBoardCols || Rows != TitleBoardRows) ResizeBoard(TitleBoardCols, TitleBoardRows);
        if (chapter != null) _mosesChp = chapter;
        // 챕터마다 주인 파티가 있다(Episode.dat 칸 8) — 연대표를 거치지 않고 열어도(챕터 고르기·시험 훅) 그 파티로 바꾼다.
        if (_mosesChp is { } owner && Episodes().FirstOrDefault(e => e.Chapter == owner.Id) is { } ep) SwitchParty(ep.Party);
        // 챕터가 끝났으면(필드 행동 11) 항행 화면 대신 연대표로 — 원본 0x100f5b07: 챕터 상태 +0x10 이 서 있으면 장면 7.
        // 다음 에피소드는 진행 깃발(Episode.dat 잠금 깃발 넷)이 다 서 있어야 열린다. 표시는 에피소드를 고를 때 내린다.
        // 모든 전투·필드 장소를 겪었으면(상점은 소모되지 않는다) 챕터가 끝난 것으로 본다 — 행동 11 을 그냥 「모세스로」로 돌리던 판의
        // 세이브와, 끝 필드의 행동 11 이 실행기를 못 탄 경우를 구제하는 데모 규칙(원본에는 없다).
        if (!_chapterDone && _mosesChp is { } done && done.Places.Any(p => p.Value < 20000 && p.Auto == 0)
            && done.Places.Where(p => p.Value < 20000).All(p => _placesUsed.Contains((done.Id, p.No)) || _autoPlacesDone.Contains((done.Id, p.No))))
            _chapterDone = true;
        if (_chapterDone)
        {
            _mosesOpen = false;
            _mixer.StopMusic();
            OpenEpisodes();
            return;
        }
        LoadMosesChapter();
        _fieldEvent = -1;                                    // 필드에서 돌던 실행기 자리를 비운다 — 챕터 스크립트가 처음부터 고른다
        _fieldPc = 0;
        _fieldReturn.Clear();
        _fieldWaitUntil = 0;
        _fieldChoices = null;
        if (_mosesChp is { } chp) RunChapterScript(chp);     // 동료·돈·아이템·깃발은 챕터 스크립트가 준다(대사 든 사건은 실행기가)
        if (EnterAutoPlace()) return;                       // 저절로 일어나는 장소(프롤로그 따위)가 먼저다
        ShowMosesBackground(_mosesChp?.Background ?? 52);
        _mixer.StopMusic();
        PlayMusicFile(_mosesChp?.Bgm ?? 19, loop: true);   // 챕터 BGM — Chp 머리 셋째 워드
    }

    /// <summary>아직 안 겪었고 조건이 열린 「자동 발생」 장소가 있으면 거기로 들어간다(<c>0x100fdd40</c>).</summary>
    /// <remarks>
    /// <c>Chp 0010</c> 은 이렇게 프롤로그(<c>Fld 0019</c>)로 먼저 들어가고, 그것이 첫 진행 깃발을 세운다.
    /// 한 번 겪은 장소는 다시 안 일어난다 — 원본은 레코드에 표시를 남기고, 데모는 <see cref="_autoPlacesDone"/> 에 적어 둔다.
    /// </remarks>
    private bool EnterAutoPlace()
    {
        if (_mosesChp is not { } chp) return false;
        foreach (var place in chp.Places)
        {
            if (!place.IsAuto || !_autoPlacesDone.Add((chp.Id, place.No))) continue;
            if (!FlagsAllow(place.Conditions)) continue;
            if (place.Value >= 10000 && place.Value < 20000 && OpenField(place.Value - 10000)) return true;
            if (place.Value > 0 && place.Value < 10000 && StartBattle(place.Value)) return true;
        }
        return false;
    }

    /// <summary>방문 표시가 선 행성 — (챕터, 행성). 스크립트 조건 <c>505 [행성]</c> 이 한 번 참이 되면서 지운다(<c>0x100edcd0</c>). 세이브에 실린다.</summary>
    private readonly HashSet<(int Chapter, int Planet)> _planetVisits = [];

    /// <summary>이미 겪은 자동 발생 장소 — (챕터, 장소).</summary>
    private readonly HashSet<(int Chapter, int Place)> _autoPlacesDone = [];

    /// <summary>그 번호의 챕터 파일을 읽는다 — 없으면 null.</summary>
    private static ChapterFile? LoadChapterFile(int id)
    {
        string path = Path.Combine(AssetsFolder.Find("moses"), "chp", $"{id:D4}.chp");
        return File.Exists(path) ? ChapterFile.Parse(id, File.ReadAllBytes(path)) : null;
    }

    private void LoadMosesChapter()
    {
        if (_mosesChp != null) return;
        string path = Path.Combine(AssetsFolder.Find("moses"), "chp", $"{MosesChapter:D4}.chp");
        if (File.Exists(path)) _mosesChp = ChapterFile.Parse(MosesChapter, File.ReadAllBytes(path));
    }

    /// <summary>
    /// 배경 그림(.bgr 은 그냥 JPEG)을 읽어 둔다. 필드 배경은 640×480 보다 넓어서
    /// <paramref name="srcX"/>·<paramref name="srcY"/> 부터 잘라 온다(필드 머리의 첫 화면 자리).
    /// </summary>
    private void ShowMosesBackground(int id, int srcX = 0, int srcY = 0)
    {
        if (_mosesBgId == id && (srcX, srcY) == _mosesBgAt) return;
        if (ReadBackground(id, srcX, srcY) is not { } pixels) return;
        _mosesBgAt = (srcX, srcY);
        _mosesBg = pixels;
        _mosesBgId = id;
    }

    /// <summary>배경 그림 한 장을 640×480 낱칸으로 읽어 온다 — 화면에 걸지는 않는다(전환이 쓴다).</summary>
    private uint[]? ReadBackground(int id, int srcX = 0, int srcY = 0)
    {
        try
        {
            string path = Path.Combine(AssetsFolder.Find("moses"), "bgr", $"{id:D4}.bgr");
            if (!File.Exists(path)) return null;
            using var bitmap = new Bitmap(path);
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int left = Math.Clamp(srcX, 0, Math.Max(0, bitmap.Width - MosesW));
                int top = Math.Clamp(srcY, 0, Math.Max(0, bitmap.Height - MosesH));
                var pixels = new uint[MosesW * MosesH];
                for (int y = 0; y + top < bitmap.Height && y < MosesH; y++)
                {
                    int width = Math.Min(MosesW, bitmap.Width - left);
                    var row = new Span<uint>((void*)(data.Scan0 + (y + top) * data.Stride + 4 * left), width);
                    row.CopyTo(pixels.AsSpan(y * MosesW, row.Length));
                }
                return pixels;
            }
            finally { bitmap.UnlockBits(data); }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            _loadError = $"모세스 배경: {ex.Message}";
            return null;
        }
    }

    /// <summary>지금 페이지에 보이는 칸들 — (자리, 이름, 아이콘 모션, 누르면 할 일).</summary>
    /// <summary>항행 단계 2 후보 장소들의 레이더 자리(경도칸, 위도칸) — <see cref="MosesCellList"/> 와 같은 차례.</summary>
    private List<(int Lon, int Lat)> MosesPlacePatches(ChapterFile chp, ChapterFile.Planet planet) =>
        [.. planet.Places.Select(chp.PlaceOf).OfType<ChapterFile.Place>()
                         .Where(p => p.Auto == 0 && !PlaceUsed(p) && PlaceOpen(p))
                         .Take(MosesCells.Length).Select(p => (p.Lon, p.Lat))];

    private List<(int X, int Y, string Name, int Icon, Action Click)> MosesCellList()
    {
        var list = new List<(int, int, string, int, Action)>();
        if (_mosesPage == 0 && _mosesStep == 2 && _mosesChp is { } chp && chp.PlanetOf(_mosesPlanet) is { } planet)
        {
            // 자동 발생 장소는 목록에 없고, 진행 깃발 조건이 안 맞는 장소도 아직 안 열린 것이다(0x100fdaf0).
            foreach (var place in planet.Places.Select(chp.PlaceOf).OfType<ChapterFile.Place>()
                                               .Where(p => p.Auto == 0 && !PlaceUsed(p) && PlaceOpen(p)))
            {
                int i = list.Count;
                if (i >= MosesCells.Length) break;
                var (x, y) = MosesCells[i];
                int value = place.Value, no = place.No;
                list.Add((x, y, Text(place.NameText), 2, () => MosesEnterPlace(value, no)));
            }
        }
        else if (_mosesPage == 5)
            foreach (var (icon, text, name, sound) in MosesPartyItems)
            {
                int i = list.Count;
                var (x, y) = MosesCells[i];
                string label = Text(text) is { Length: > 0 } t ? t : name;
                int index = list.Count;
                list.Add((x, y, label, icon, () => RunPartyItem(index, label)));
            }
        return list;
    }

    private string Text(int id) => id > 0 && _db is { } db ? db.T((ushort)id) : "";

    /// <summary>장소를 누르면 — 값 &lt;10000 전투 · 10000+ 필드 · 20000+ 상점.</summary>
    /// <remarks>
    /// 전투·필드로 들어간 장소는 <b>그 자리에서 소모</b>된다(<c>0x10101e20</c> 이 장소 <c>+0x14</c> 를 1 로 두고 세이브에 싣는다) —
    /// 그래서 다녀오면 목록에서 사라진다. 상점은 장면이 안 바뀌므로 소모하지 않는다.
    /// </remarks>
    private void MosesEnterPlace(int value, int no = -1)
    {
        if (value >= 20000) { OpenMosesShop(0, value - 20000); return; }
        if (value >= 10000)
        {
            if (OpenField(value - 10000)) { UsePlace(no); return; }
            Toast($"필드 {value - 10000} 자료가 assets 에 없습니다");
            return;
        }
        if (!StartBattle(value)) return;                  // 자료가 없으면 모세스에 그대로 남는다
        UsePlace(no);
        _mixer.StopMusic();
        StartBattleMusic();
    }

    /// <summary>한 번 다녀온 장소 — (챕터, 장소 번호). 원본의 장소 <c>+0x14</c> 소모 표시다.</summary>
    private readonly HashSet<(int Chapter, int Place)> _placesUsed = [];

    private bool PlaceUsed(ChapterFile.Place place) => _placesUsed.Contains((_mosesChp?.Id ?? 0, place.No));

    private void UsePlace(int no)
    {
        if (no >= 0 && _mosesChp is { } chp) _placesUsed.Add((chp.Id, no));
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
            case 1: if (DeliverMail() > 0) Play(571); break;           // MAIL — 새 편지가 왔으면 나는 소리(0x100fc6e0)
            case 2: break;                                             // MESSAGE — 소리 없음
            case 3 or 4: OpenMosesShop(page - 3); return;
            case 5: Play(580); break;                                  // PARTY
        }
        _mosesPage = page;
        _mosesPageAt = _lastTime;
        _talkPick = -1;
        StartFade(page == 0);
        _mosesHover = -1;
        // 항행은 성계 배경, 메일은 94, 파티는 주 화면과 같은 챕터 배경
        ShowMosesBackground(page switch
        {
            0 or 2 => _mosesChp?.Systems.FirstOrDefault()?.Background ?? 70,
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
            StartFade();
            _mosesHover = -1;
            return;
        }
        Play(_mosesPage == 0 ? 570 : 569);
        _mosesPage = -1;
        StartFade();
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
        if (_statusUnit >= 0) return OnStatusClick(bx, by);     // 전직 페이지의 STATUS 로 연 스테이터스 창이 먼저 받는다
        if (_mosesFade > 0) return true;
        if (OnMosesShopClick(bx, by)) return true;
        if (OnMosesMailClick(bx, by)) return true;
        if (OnMosesStyleClick(bx, by)) return true;
        if (OnMosesLegionClick(bx, by)) return true;
        if (OnMosesTalkClick(bx, by)) return true;
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
                _planetVisits.Add((chp.Id, chp.Planets[index].No));   // 행성 +0x5c 방문 표시(가설: 고를 때 선다) — 조건 505 가 한 번 먹고 지운다
                _mosesStep = 2;
                _mosesPageAt = _lastTime;
                StartFade();
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
        FillRect(_camX, _camY, ViewWidth, ViewHeight, 0xFF000000);
        if (_mosesBg is { } bg)
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    SetPixel(ox + x, oy + y, bg[y * MosesW + x] | 0xFF000000);

        if (_mosesPage == -1) DrawMosesDesktop(ox, oy, tick);
        else DrawMosesPage(ox, oy, tick);

        DrawMosesTooltip();
        // 챕터 스크립트의 대사·고르기 — 모세스 화면 위에(원본 창 +0x2ee0 「대사·말풍선 묶음」)
        _uiClip = (ox, oy, MosesW, MosesH);
        DrawTalk();
        DrawFieldChoices();
        _uiClip = null;
        DrawSystem();
        DrawStatusScreen();   // 전직 페이지의 STATUS — 스테이터스 창도 모세스 위에 그린다
        DrawToast();   // 알림은 모세스 화면 위에 — Compose 의 DrawToast 는 이 화면에 가린다

        // 페이지 전환 — 원본 색표(분석-모세스 2절)대로: 보통은 검정 페이드(방식 2),
        // 항행 진입(효과 1)만 파랑 채널을 흰색 쪽으로 씻었다가 되돌린다(방식 5).
        if (_mosesFade > 0)
        {
            int total = _mosesBlueFade ? MosesBlueFadeTicks : MosesFadeTicks;
            int alpha = 31 * _mosesFade / total;
            // 원본은 640×480 이 화면 전부라 씻김도 화면 전부다 — 우리 판은 더 넓으니 그 네모 안에서만 씻는다.
            var (fx, fy) = MosesOrigin();
            int x0 = _mosesBlueFade ? Math.Max(0, fx) : 0;
            int x1 = _mosesBlueFade ? Math.Min(BoardWidth, fx + MosesW) : BoardWidth;
            int y0 = _mosesBlueFade ? Math.Max(_camY, fy) : _camY;
            int y1 = _mosesBlueFade ? Math.Min(_camY + ViewHeight, fy + MosesH) : _camY + ViewHeight;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    int i = y * BoardWidth + x;
                    uint c = _fb[i];
                    if (_mosesBlueFade)
                    {
                        uint blue = c & 0xFF;
                        blue += (255 - blue) * (uint)alpha / 31;
                        _fb[i] = c & 0xFFFFFF00 | blue;
                    }
                    else _fb[i] = ScaleColor(c, 31 - alpha, 31);
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
        if (_mosesPage == 7)
        {
            DrawMosesStyle(ox, oy, tick);
            return;
        }
        if (_mosesPage == 6)
        {
            DrawMosesLegion(ox, oy, tick);
            return;
        }
        if (_mosesPage == 2)
        {
            DrawMosesTalk(ox, oy, tick);
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

        var cells = MosesCellList();
        // 칸은 10프레임 세로 와이프로 나타난다 — 여기서는 칸마다 한 틱씩 늦게 나오게 한다
        int since = (int)((_lastTime - _mosesPageAt) * TicksPerSecond);

        // 행성 구체 — 항행 단계 2 에서 화면 가운데 조금 위. 그 위에 레이더 `+0x2f14`(초록 격자 구 + 장소 조각)를 돌린다.
        if (_mosesPage == 0 && _mosesStep == 2 && _mosesChp is { } chp && chp.PlanetOf(_mosesPlanet) is { } planet)
        {
            DrawUi(planet.GlobeObs, planet.GlobeMotion, tick, ox + 320, oy + 220, UiBlend.Alpha);
            DrawMosesRadar(ox, oy, since, MosesPlacePatches(chp, planet), _mosesHover);
        }
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

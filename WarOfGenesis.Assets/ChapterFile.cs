using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// 챕터 파일 <c>Chp\NNNN.chp</c> 를 모세스(챕터) 화면이 쓰는 만큼 다 읽는다 — 머리·성도 점·인물·항성계·행성·장소.
/// </summary>
/// <remarks>
/// <para>
/// 옵시디안 <b>분석-모세스 6절「Chp 레코드」</b> 와 <c>tools/re/moses_render.py</c> 의 <c>Chapter</c> 로더(원본 <c>0x100f6c80</c>)를 옮긴 것이다.
/// 파일은 처음부터 끝까지 int16 이 줄지어 있고, 묶음마다 <c>수, 여벌 워드</c> 뒤에 레코드가 이어진다:
/// </para>
/// <list type="bullet">
/// <item>머리 24워드 — 0 지역 · <b>1 배경 Bgr</b> · <b>2 BGM</b> · 3 기본 아이템 상점 · 4 기본 VT(무기) 상점 ·
/// <b>21 최저 항행 단계</b>(0 성계 · 1 행성 · 2 장소) · 22 그 단계에서 시작할 번호 · <b>23 제목 TXR</b>.</item>
/// <item>성도 점 <b>12바이트</b>(6워드) — 번호 · Obs · 모션 · x · y · 항성계 번호.</item>
/// <item>인물 <b>30바이트</b>(15워드) — 1워드가 Chr 번호(분석-모세스 11절 통신 페이지).</item>
/// <item>항성계 <b>66바이트</b>(머리 8워드 + 8워드짜리 칸 세 벌 + 꼬리 1워드) — 0 번호 · 2 이름 TXR · 3 설명 TXR ·
/// 5~7 조건 · 칸 벌 0 = 행성 번호 여덟 · 칸 벌 2 = 인물 번호 여덟(가설, 메모리 <c>+0x30</c> 자리) · 꼬리 = <b>배경 Bgr</b>.</item>
/// <item>행성 <b>84바이트</b>(머리 8워드 + 칸 세 벌 + 꼬리 10워드) — 0 번호 · <b>1 성도 그림 Obs</b> · 2 이름 TXR · 3 설명 TXR ·
/// <b>4 성도 모션</b> · 5~7 조건 · 칸 벌 0 = 장소 번호 여덟 · 꼬리 2·3 = <b>행성 구체 Obs·모션</b> · 꼬리 4~7 = 줌 연출 Obs·모션 두 쌍 ·
/// 꼬리 8·9 = <b>성도 자리 x·y</b>.</item>
/// <item>장소 <b>20바이트</b>(10워드) — 0 번호 · 1 이름 TXR · <b>2 값</b> · 3 설명 TXR · 6~8 조건(가설) · <b>9 자동 발생</b>.</item>
/// </list>
/// <para>
/// 칸에 적힌 것은 <i>인덱스가 아니라 번호</i>다(원본 로더 <c>0x100f68c0</c> 가 읽은 뒤 인덱스로 바꾼다) — 여기서는
/// <see cref="SystemOf"/>·<see cref="PlanetOf"/>·<see cref="PlaceOf"/> 로 번호를 찾아 쓴다.
/// 장소 값은 <b>10000 미만 전투 · 10000+ 필드 · 20000+ 상점</b>(분석-모세스 6절「장소를 누르면」).
/// </para>
/// </remarks>
public sealed class ChapterFile
{
    /// <summary>성도 점(랜드마크) 하나 — 항행 단계 0 에서 누르면 그 항성계를 고른다.</summary>
    public sealed record Landmark(int No, int Obs, int Motion, int X, int Y, int SystemNo);

    /// <summary>통신 페이지에 걸어 다니는 인물 하나 — 지금은 Chr 번호만 쓴다.</summary>
    public sealed record Person(int No, int ChrCode, IReadOnlyList<int> Words);

    /// <summary>항성계 하나.</summary>
    public sealed record StarSystem(int No, int NameText, int DescText, int Background,
                                    IReadOnlyList<int> Planets, IReadOnlyList<int> People,
                                    IReadOnlyList<(int Variable, int Value, int Operator)> Conditions);

    /// <summary>행성 하나.</summary>
    public sealed record Planet(int No, int NameText, int DescText, int MapObs, int MapMotion, int X, int Y,
                                int GlobeObs, int GlobeMotion, IReadOnlyList<int> Places,
                                IReadOnlyList<(int Variable, int Value, int Operator)> Conditions);

    /// <summary>장소 하나 — 항행 단계 2 의 칸.</summary>
    public sealed record Place(int No, int NameText, int Value, int DescText, int Auto,
                               IReadOnlyList<(int Variable, int Value, int Operator)> Conditions)
    {
        /// <summary>값이 가리키는 곳: 전투 · 필드 · 상점(분석-모세스 6절).</summary>
        public PlaceKind Kind => Value >= 20000 ? PlaceKind.Shop : Value >= 10000 ? PlaceKind.Field : PlaceKind.Battle;

        /// <summary>그 갈래 안에서의 번호 — 전투 v · 필드 v−10000 · 상점 v−20000.</summary>
        public int Target => Value >= 20000 ? Value - 20000 : Value >= 10000 ? Value - 10000 : Value;

        /// <summary>챕터를 열자마자 저절로 일어나는 장소(항행 목록에는 안 나온다).</summary>
        public bool IsAuto => Auto != 0;
    }

    public enum PlaceKind { Battle, Field, Shop }

    public int Id { get; private init; }
    /// <summary>
    /// 머리 워드 0 — <b>파일 판 번호</b>. 지금 게임이 읽는 것은 <b>3</b> 뿐이고, 0·1·2 는 배치가 달라 본체도 못 읽는 옛 자료다(가설).
    /// </summary>
    /// <remarks>로더 <c>0x100f6c80</c> 는 이 워드를 읽어 스택에 버린다 — 검사 코드가 없어 "판 번호"는 자료 상관으로만 뒷받침된다.</remarks>
    public int Version { get; private init; }
    /// <summary>머리 워드 1 — 주 화면·파티 페이지 배경 <c>Bgr\NNNN.bgr</c>.</summary>
    public int Background { get; private init; }
    /// <summary>머리 워드 2 — 챕터 배경음악.</summary>
    public int Bgm { get; private init; }
    /// <summary>머리 워드 3 — 이 챕터의 기본 아이템 상점 <c>Shp</c> 번호.</summary>
    public int ItemShop { get; private init; }
    /// <summary>머리 워드 4 — 이 챕터의 기본 VT(무기) 상점 <c>Shp</c> 번호.</summary>
    public int VtShop { get; private init; }
    /// <summary>
    /// 머리 워드 21 — 최저 항행 단계(1 행성 · 2 장소). 뒤로 단추는 여기까지만 내려간다.
    /// 파일에 0(성도)이 적혀 있어도 로더가 1 로 올리므로(<c>0x100f7318</c>) <b>여기서 0 은 나오지 않는다</b>.
    /// </summary>
    public int StartStep { get; private init; }
    /// <summary>머리 워드 22 — 그 단계에서 시작할 항성계(단계 1) 또는 행성(단계 2) 번호.</summary>
    public int StartNumber { get; private init; }
    /// <summary>머리 워드 23 — 챕터 제목 TXR. 2284~2313 이 이야기 차례다. 23워드짜리 옛 파일에는 없어 −1 이다.</summary>
    public int TitleText { get; private init; }

    /// <summary>머리 워드 5~12 — 인물 번호 여덟 칸(빈 칸은 −1). 로더 도우미 <c>0x100f6880</c> 이 한 번에 읽어 개수를 센다.</summary>
    public IReadOnlyList<int> HeadPeopleA { get; private init; } = [];
    /// <summary>머리 워드 13~20 — 인물 번호 여덟 칸(빈 칸은 −1). 정규화기 <c>0x100f68c0</c> 가 둘 다 인물 표에 맞춘다.</summary>
    public IReadOnlyList<int> HeadPeopleB { get; private init; } = [];

    public IReadOnlyList<Landmark> Landmarks { get; private init; } = [];
    public IReadOnlyList<Person> People { get; private init; } = [];
    public IReadOnlyList<StarSystem> Systems { get; private init; } = [];
    public IReadOnlyList<Planet> Planets { get; private init; } = [];
    public IReadOnlyList<Place> Places { get; private init; } = [];

    /// <summary>
    /// 챕터 스크립트 — 필드와 <b>같은 꼴</b>이라 실행기도 같다(<see cref="FieldEvent"/>).
    /// </summary>
    /// <remarks>
    /// 챕터에 들어갈 때 한 번 돌며 <b>동료·돈·아이템·진행 깃발</b>을 넣는다.
    /// 예: <c>Chp 0010</c> 은 깃발 107·113·116·117·118·14 를 세우고 동료 둘(219·221)과 3000GP, 아이템 다섯 가지를 준다.
    /// </remarks>
    public IReadOnlyList<FieldEvent> Events { get; private init; } = [];

    /// <summary>레코드와 스크립트까지 다 읽고 남은 바이트 수 — <b>0 이어야</b> 레코드 크기를 옳게 잡은 것이다.</summary>
    public int TailBytes { get; private init; }

    /// <summary>파일 끝까지 배치가 맞았는가(<see cref="TailBytes"/> 가 0).</summary>
    public bool Exact => TailBytes == 0;

    public StarSystem? SystemOf(int no) => Systems.FirstOrDefault(s => s.No == no);
    public Planet? PlanetOf(int no) => Planets.FirstOrDefault(p => p.No == no);
    public Place? PlaceOf(int no) => Places.FirstOrDefault(p => p.No == no);

    /// <summary>이야기 차례 — 제목 TXR 2284~2313 이 앞, 그 밖은 뒤로(분석-전투목록 과 같은 규칙).</summary>
    public int StoryRank => TitleText is >= 2284 and <= 2313 ? TitleText : 9999;

    /// <summary>게임 자료의 <c>Chp</c> 폴더를 통째로 읽어 이야기 차례로 늘어놓는다.</summary>
    public static List<ChapterFile> LoadAll(GameFiles files)
    {
        var list = new List<ChapterFile>();
        foreach (string name in files.List("Chp", ".chp").Keys)
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(name), out int id)) continue;
            if (Parse(id, files.Read("Chp", name)) is { } chapter) list.Add(chapter);
        }
        return [.. list.OrderBy(c => c.StoryRank).ThenBy(c => c.Id)];
    }

    /// <summary>
    /// 머리를 <b>24워드</b>로 읽고, 파일 끝과 딱 안 맞으면 <b>23워드</b>로 한 번 더 읽는다.
    /// </summary>
    /// <remarks>
    /// 원본 로더 <c>0x100f6c80</c> 는 머리를 24번 조건 없이 읽으므로 24가 맞는 배치다.
    /// 그런데 자료에는 <b>제목 TXR 워드가 없는 23워드짜리</b>가 셋 있고(<c>0038</c>·<c>0059</c>·<c>0065</c>),
    /// 그것만 23으로 읽어야 파일 끝과 맞는다. 판 번호(<see cref="Version"/>)가 3 이 아닌 옛 파일 다섯은
    /// 어떤 배치로도 안 맞는다 — 게임 본체도 못 읽는 자료라 <see cref="Exact"/> 가 false 로 남는다.
    /// </remarks>
    public static ChapterFile? Parse(int id, byte[]? b)
    {
        if (b == null || b.Length < 50) return null;
        var full = ParseWith(id, b, 24);
        if (full is { Exact: true }) return full;
        var short23 = ParseWith(id, b, 23);
        return short23 is { Exact: true } ? short23 : full ?? short23;
    }

    private static ChapterFile? ParseWith(int id, byte[] b, int headWords)
    {
        try
        {
            int o = 0;
            short W() { short v = BitConverter.ToInt16(b, o); o += 2; return v; }
            int[] Words(int n) { var a = new int[n]; for (int i = 0; i < n; i++) a[i] = W(); return a; }
            // 묶음 머리 = 수 한 워드 + 여벌 한 워드. 음수가 나오면 배치가 어긋난 것이라 읽기를 접는다.
            int Count() { int n = W(); W(); return n >= 0 ? n : throw new ArgumentException("묶음 개수가 음수입니다."); }

            var head = Words(headWords);
            // 23워드짜리에는 제목 TXR 이 없다 — 없는 자리는 −1 로 채워 뒤 코드가 그대로 돌게 한다.
            if (head.Length < 24) head = [.. head, .. Enumerable.Repeat(-1, 24 - head.Length)];
            int landmarkCount = Count();
            var landmarks = new List<Landmark>();
            for (int i = 0; i < landmarkCount; i++)
            {
                var w = Words(6);
                landmarks.Add(new Landmark(w[0], w[1], w[2], w[3], w[4], w[5]));
            }

            int peopleCount = Count();
            var people = new List<Person>();
            for (int i = 0; i < peopleCount; i++)
            {
                var w = Words(15);
                people.Add(new Person(w[0], w[1], w));
            }

            int systemCount = Count();
            var systems = new List<StarSystem>();
            for (int i = 0; i < systemCount; i++)
            {
                var w = Words(8);
                var slots = new[] { Words(8), Words(8), Words(8) };
                var tail = Words(1);
                systems.Add(new StarSystem(w[0], w[2], w[3], tail[0],
                                           [.. slots[0].Where(v => v >= 0)], [.. slots[2].Where(v => v >= 0)],
                                           [(w[5], w[6], w[7])]));
            }

            int planetCount = Count();
            var planets = new List<Planet>();
            for (int i = 0; i < planetCount; i++)
            {
                var w = Words(8);
                var slots = new[] { Words(8), Words(8), Words(8) };
                var tail = Words(10);
                planets.Add(new Planet(w[0], w[2], w[3], w[1], w[4], tail[8], tail[9], tail[2], tail[3],
                                       [.. slots[0].Where(v => v >= 0)], [(w[5], w[6], w[7])]));
            }

            int placeCount = Count();
            var places = new List<Place>();
            for (int i = 0; i < placeCount; i++)
            {
                var w = Words(10);
                // 워드 6~8 = 조건 (변수, 값, 연산자) — 안 쓰면 −1 셋이다(가설: 항성계·행성 조건과 같은 꼴이고 값도 그렇게 들어 있다).
                places.Add(new Place(w[0], w[1], w[2], w[3], w[9], [(w[6], w[7], w[8])]));
            }

            // 장소 뒤에 4바이트 레코드 표 하나와 스크립트가 더 있다(분석-전투목록 ba-7 의 Chp 파서와 같다).
            // 여기까지 읽어 파일이 딱 끝나야 레코드 크기를 옳게 잡은 것이다.
            int tailRecords = Count();
            o += 4 * tailRecords;
            var events = ReadScript(b, ref o);

            return new ChapterFile
            {
                Id = id,
                Version = head[0],
                Background = head[1],
                Bgm = head[2],
                ItemShop = head[3],
                VtShop = head[4],
                // 로더 0x100f7318 — 행성을 다 읽은 뒤 최저 단계가 1 보다 작으면 1(행성 고르기)로 올리고 첫 행성을 고른다.
                StartStep = head[21] < 1 && planets.Count > 0 ? 1 : head[21],
                StartNumber = head[21] < 1 && planets.Count > 0 ? planets[0].No : head[22],
                TitleText = head[23],
                HeadPeopleA = [.. head[5..13].Where(v => v >= 0)],
                HeadPeopleB = [.. head[13..21].Where(v => v >= 0)],
                Landmarks = landmarks,
                People = people,
                Systems = systems,
                Planets = planets,
                Places = places,
                Events = events,
                TailBytes = b.Length - o,
            };
        }
        catch (ArgumentException) { return null; }   // 배치가 안 맞으면(파일 끝을 넘으면) 안 읽은 것으로 친다
    }

    /// <summary>스크립트: 수 한 워드, 이벤트마다 (최대 발동 수, 조건 수, 조건 18바이트씩, 행동 수, 행동 18바이트씩).</summary>
    private static List<FieldEvent> ReadScript(byte[] b, ref int o)
    {
        int n = BitConverter.ToInt16(b, o);
        o += 2;
        if (n < 0) throw new ArgumentException("스크립트 이벤트 수가 음수입니다.");
        var events = new List<FieldEvent>(n);
        for (int i = 0; i < n; i++)
        {
            int maxFire = BitConverter.ToInt16(b, o);
            o += 2;
            var conditions = ReadCommands(b, ref o);
            var actions = ReadCommands(b, ref o);
            events.Add(new FieldEvent(i, maxFire, conditions, actions));
        }
        if (o > b.Length) throw new ArgumentException("스크립트가 파일 끝을 넘습니다.");
        return events;
    }

    private static List<ScriptCommand> ReadCommands(byte[] b, ref int o)
    {
        int n = BitConverter.ToInt16(b, o);
        o += 2;
        if (n < 0) throw new ArgumentException("명령 수가 음수입니다.");
        var list = new List<ScriptCommand>(n);
        for (int i = 0; i < n; i++)
        {
            int code = BitConverter.ToInt16(b, o);
            var args = new short[8];
            for (int k = 0; k < 8; k++) args[k] = BitConverter.ToInt16(b, o + 2 + 2 * k);
            o += 18;
            list.Add(new ScriptCommand(code, args));
        }
        return list;
    }
}

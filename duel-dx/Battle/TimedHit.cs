using System.Text.Json;
using System.Text.Json.Serialization;
using DuelDx.Native;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>박자 하나 — 칸 가운데의 때(시작에서 초)와 칸의 반 너비(초).</summary>
internal sealed class TimedBeat
{
    public double At { get; set; }
    public double Half { get; set; }
}

/// <summary>박자 패턴 하나 — 띠 길이와 박자들. 여러 개를 등록해 두고 골라 쓴다(사용자 요청).</summary>
internal sealed class TimedPattern
{
    /// <summary>띠가 다 지나가는 시간(초).</summary>
    public double Seconds { get; set; } = 1.2;

    /// <summary>박자들 — 때가 이른 것부터. 하나에서 <see cref="TimedHitConfig.MaxBeats"/> 개까지, 박자마다 칸 길이가 따로다.</summary>
    public List<TimedBeat> Beats { get; set; } = [];

    /// <summary>
    /// 이 패턴을 다 맞추면 <b>다음 공격에 나올 패턴</b>의 번호(0 부터) — −1 이면 이 패턴이 그대로 이어진다(사용자 요청: 성공할 때마다 횟수가
    /// 저절로 느는 것이 아니라, 내가 정해 둔 패턴이 차례로 나오게).
    /// </summary>
    public int Next { get; set; } = -1;

    internal static TimedPattern Default() => new()
    {
        Seconds = 1.2, Beats = [new TimedBeat { At = 0.45, Half = 0.18 }, new TimedBeat { At = 0.95, Half = 0.18 }],
    };

    internal TimedPattern Copy() => new() { Seconds = Seconds, Next = Next, Beats = [.. Beats.Select(b => new TimedBeat { At = b.At, Half = b.Half })] };
}

/// <summary>
/// 타이밍 어택의 수치 — 개발 &gt; 타이밍 어택 수정 창(<see cref="TimedHitEditScreen"/>)에서 고치며 맞춘다. <c>%APPDATA%\DuelDx\timedhit.json</c> 에 남는다.
/// 박자 패턴은 여러 개를 등록해 두고(<see cref="Patterns"/>) 그 가운데 고른 것(<see cref="Selected"/>)이 게임에 쓰인다.
/// </summary>
internal sealed class TimedHitConfig
{
    internal const int MaxBeats = 6, MaxPatterns = 8;

    /// <summary>등록해 둔 패턴들과, 수정 창에서 지금 보고 있는(고치는) 것의 번호.</summary>
    public List<TimedPattern> Patterns { get; set; } = [];
    public int Selected { get; set; }

    /// <summary>
    /// 전투가 <b>처음에 띄우는</b> 패턴의 번호 — 적이 바뀌거나 틀리면 여기로 돌아온다. 수정 창에서 보고 있는 패턴(<see cref="Selected"/>)과는 따로다 —
    /// 전에는 하나로 써서, 2번을 고치려고 눌러 두면 「처음」이 2번이 되어 적을 바꿔도 2번이 나왔다(사용자 보고).
    /// </summary>
    public int Start { get; set; }

    /// <summary>판정은 그린 칸보다 앞뒤로 이만큼 더 받아 준다(초) — 칸 끝에 걸친 누름이 빗나가지 않게(사용자 요청: 넓게 · 후하게).</summary>
    public double Grace { get; set; } = 0.04;
    /// <summary>결과 글을 보이는 시간(초).</summary>
    public double ShowResultSeconds { get; set; } = 0.7;
    /// <summary>다 맞췄는데 두 번 치는 동작이 없는 인물일 때의 피해 배율(%).</summary>
    public int DamagePercent { get; set; } = 200;
    /// <summary>띠 너비(픽셀).</summary>
    public int BarWidth { get; set; } = 240;

    /// <summary>지금 고른 패턴 — 게임과 수정 창이 이것을 본다.</summary>
    [JsonIgnore] public TimedPattern Current => Patterns[Math.Clamp(Selected, 0, Patterns.Count - 1)];
    [JsonIgnore] public double Seconds { get => Current.Seconds; set => Current.Seconds = value; }
    [JsonIgnore] public List<TimedBeat> Beats => Current.Beats;

    // 패턴이 하나뿐이던 때의 칸(v0.31.x 의 timedhit.json) — 읽기만 하고 다시 적지 않는다.
    [JsonPropertyName("Seconds"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? OldSeconds { get; set; }
    [JsonPropertyName("Beats"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<TimedBeat>? OldBeats { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Beat1 { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Beat2 { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Tolerance { get; set; }

    internal static string FilePath => UserDataFolder.File("timedhit.json");

    /// <summary>
    /// 처음 깔았을 때의 패턴들 — 사용자가 수정 창에서 맞춘 값(2026-10-12): 1번은 박자 둘, 다 맞추면 2번(박자 셋)이 나온다.
    /// </summary>
    internal static List<TimedPattern> ShippedPatterns() =>
    [
        new() { Seconds = 1.22, Next = 1, Beats = [new TimedBeat { At = 0.45, Half = 0.18 }, new TimedBeat { At = 0.95, Half = 0.12 }] },
        new()
        {
            Seconds = 1.51, Next = -1,
            Beats = [new TimedBeat { At = 0.37, Half = 0.18 }, new TimedBeat { At = 0.89, Half = 0.12 }, new TimedBeat { At = 1.31, Half = 0.08 }],
        },
    ];

    internal static TimedHitConfig Load()
    {
        TimedHitConfig? cfg = null;
        try
        {
            if (File.Exists(FilePath)) cfg = JsonSerializer.Deserialize<TimedHitConfig>(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* 못 읽으면 기본값 */ }
        cfg ??= new TimedHitConfig();
        cfg.Normalize();
        return cfg;
    }

    /// <summary>패턴이 비었으면(새 설치 · 옛 파일) 하나를 세우고, 패턴마다 박자를 때 차례로 줄 세운다.</summary>
    internal void Normalize()
    {
        Patterns ??= [];
        Patterns.RemoveAll(p => p == null);
        if (Patterns.Count == 0)
        {
            // 옛 파일의 값이 있으면 그것으로 첫 패턴을 만들고, 아무것도 없으면(새 설치) 처음 패턴들을 세운다.
            if (OldSeconds == null && OldBeats == null && Beat1 == null && Beat2 == null && Tolerance == null) Patterns.AddRange(ShippedPatterns());
            else
            {
                var first = TimedPattern.Default();
                if (OldSeconds is { } seconds) first.Seconds = seconds;
                if (OldBeats is { Count: > 0 } beats) first.Beats = beats;
                else if (Beat1 != null || Beat2 != null || Tolerance != null)
                {
                    double half = Tolerance ?? 0.18;
                    first.Beats = [new TimedBeat { At = Beat1 ?? 0.45, Half = half }, new TimedBeat { At = Beat2 ?? 0.95, Half = half }];
                }
                Patterns.Add(first);
            }
        }
        if (Patterns.Count > MaxPatterns) Patterns.RemoveRange(MaxPatterns, Patterns.Count - MaxPatterns);
        foreach (var pattern in Patterns)
        {
            pattern.Beats ??= [];
            pattern.Beats.RemoveAll(b => b == null);
            if (pattern.Beats.Count == 0) pattern.Beats = TimedPattern.Default().Beats;
            if (pattern.Beats.Count > MaxBeats) pattern.Beats.RemoveRange(MaxBeats, pattern.Beats.Count - MaxBeats);
            pattern.Beats.Sort((a, b) => a.At.CompareTo(b.At));
            if (pattern.Next < -1 || pattern.Next >= Patterns.Count) pattern.Next = -1;
        }
        Selected = Math.Clamp(Selected, 0, Patterns.Count - 1);
        Start = Math.Clamp(Start, 0, Patterns.Count - 1);
        (OldSeconds, OldBeats, Beat1, Beat2, Tolerance) = (null, null, null, null, null);
    }

    /// <summary>패턴 하나를 지운다 — 그것을 「다음」으로 가리키던 패턴은 「그대로」로, 뒤 번호를 가리키던 것은 하나씩 당긴다.</summary>
    internal void RemovePattern(int index)
    {
        if (Patterns.Count <= 1 || (uint)index >= (uint)Patterns.Count) return;
        Patterns.RemoveAt(index);
        foreach (var pattern in Patterns)
            pattern.Next = pattern.Next == index ? -1 : pattern.Next > index ? pattern.Next - 1 : pattern.Next;
        if (Selected > index || Selected >= Patterns.Count) Selected = Math.Max(0, Selected - 1);
        Start = Start == index ? 0 : Start > index ? Start - 1 : Start;
    }

    internal void Save()
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder.Path);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 못 적어도 이번에는 바뀐 대로 돈다 */ }
    }
}

/// <summary>
/// 타이밍 어택(모드 &gt; 밸런스, 사용자 요청 fg-32 — 원본에 없다). 내가 움직이는 인물이 기본공격을 하기 바로 앞에 띠가 뜨고,
/// 띠 위를 달리는 표시가 <b>박자 칸</b>을 지날 때마다 단추를 하나씩 누른다 — <b>맞춘 박자만큼 친다</b>(박자 하나가 한 대, 「연」처럼 여섯 대까지),
/// <b>하나만 맞추면 한 대를 크리티컬로</b> 친다. 어느 쪽이든 빗나가지 않는다. 하나도 못 맞추면 보통 판정 그대로다(벌은 없다).
/// 패드는 박자마다 어빌리티 단추 · 공격 단추가 번갈아(기본 LT → RT → LT …), 키보드는 박자마다 Space.
/// </summary>
/// <remarks>
/// 박자의 수 · 때 · 칸 길이는 <see cref="TimedHitConfig"/> 에 있다(기본은 둘 — 0.45초 · 0.95초, 앞뒤 0.18 + 0.04초).
/// 박자 하나를 놓쳐도 다음 박자는 그대로 받는다. 칸보다 일찍 누르면 그 박자는 놓친 것이다.
/// 시간은 게임 속도와 상관없이 실제 시간으로 잰다. AI 가 움직이는 편 · 어빌리티 · 군단 부하의 타격에는 안 뜬다.
/// </remarks>
internal sealed class TimedHit(GameWindow host)
{
    /// <summary>지금 수치 — 수정 창이 바로 고친다.</summary>
    internal TimedHitConfig Cfg = TimedHitConfig.Load();

    private UnitState? _unit;

    /// <summary>미리 보기(수정 창) — 인물 대신 띠를 놓을 자리(띠 왼위, 판 픽셀)를 돌려준다.</summary>
    private Func<(int X, int Y)>? _anchor;

    private double _startAt, _resultAt;
    private bool? _result;

    /// <summary>이번 띠의 박자들(띠가 뜰 때의 수치를 붙들어 둔다 — 도는 중에 수정 창에서 고쳐도 안 흔들리게)과 박자마다의 결과(0 아직 · 1 맞춤 · 2 놓침).</summary>
    private (double At, double Half)[] _beats = [];

    /// <summary>이번 띠의 길이(초) — 뜰 때 쓴 패턴의 것.</summary>
    private double _seconds = 1.2;

    /// <summary>
    /// <b>인물 × 대상</b>마다 — 그 인물이 그 대상을 다음에 칠 때 나올 패턴(사용자 결정: 캐릭터별 · 적별). 패턴을 다 맞추면 그 패턴의 「다음」으로
    /// 넘어가고, 덜 맞추면 그 짝은 시작 패턴으로 돌아간다. 다른 적을 치면 그 적의 것은 시작 패턴부터고, 먼저 치던 적의 것은 남아 있다.
    /// 쓰러진 대상의 것은 버리고, 새 전투에서는 모두 처음부터.
    /// </summary>
    private readonly Dictionary<(UnitState Attacker, UnitState Target), int> _chain = [];
    private object? _chainBattle;
    private int[] _beat = [];

    /// <summary>단추를 기다리는(또는 결과를 잠깐 보이는) 중인가 — 그동안 키보드와 패드 단추는 여기로만 온다.</summary>
    internal bool Running => _unit != null || _anchor != null;

    /// <summary>방금 끝난 것에서 맞춘 박자 수와, 박자를 다 맞췄는지.</summary>
    internal int Hits { get; private set; }
    internal bool AllHit { get; private set; }

    /// <summary>
    /// 방금 것으로 칠 횟수 — <b>맞춘 박자 수</b>다(박자를 하나 더할 때마다 한 대 더, 사용자 요청). 둘 이상이면 그만큼 여러 번 치고,
    /// 하나면 한 번을 크리티컬로 친다. 전에는 박자 둘에 「연속 성공마다 +1, 3번까지」였는데 박자 수가 곧 횟수가 되면서 그 규칙은 뺐다.
    /// </summary>
    internal int AttackCount => ForcedHits > 0 ? ForcedHits : Math.Max(1, Hits);

    /// <summary>시험용 DUELDX_TIMEDHITS=&lt;n&gt; — 띠 없이 박자 n 개를 맞춘 것으로 친다(화면 밖에서 연속 공격을 보려고).</summary>
    internal static readonly int ForcedHits = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_TIMEDHITS"), out int n) ? n : 0;

    /// <summary>시험용 DUELDX_TIMEDAIM=&lt;박자 수&gt; — 타이밍 명중을 띄우지 않고 그만큼 맞춘 것으로 친다(명중 보정을 화면 밖에서 보려고). 타이밍 명중이 켜져 있어야 한다.</summary>
    internal static readonly int ForcedAimHits = int.TryParse(Environment.GetEnvironmentVariable("DUELDX_TIMEDAIM"), out int m) ? m : 0;

    /// <summary>아직 안 끝난 첫 박자 — 다 끝났으면 박자 수.</summary>
    private int Pending => Array.IndexOf(_beat, 0) is var i and >= 0 ? i : _beat.Length;

    /// <summary>이 공격에 띄울 것인가 — 켜져 있고, 내가 움직이는 인물의 기본공격일 때만.</summary>
    internal bool Wanted(UnitState a, WarOfGenesis.Assets.WorkData w) =>
        host._timedHit && !host._skippingAction && host.Btl.IsMine(a) && a.LeaderIndex < 0 && w.IsDamage && a.Data?.BasicWorkId == w.Id;

    /// <summary>타이밍 명중(모드 &gt; 밸런스, 사용자 요청 fg-34 · fg-35) — 박자 하나를 맞추면 오르는 명중률(%)과, 하나 더 맞출 때마다 더 오르는 몫. 하나면 10, 둘이면 15.</summary>
    internal const int AimPercentFirst = 10, AimPercentMore = 5;

    /// <summary>파트1 마법(세기 식 5 · 6 · 8 · 10~24)은 덜 오른다 — 하나면 5, 둘이면 10(사용자 결정 fg-68).</summary>
    internal const int MagicAimPercentFirst = 5, MagicAimPercentMore = 5;

    /// <summary>맞춘 박자 수만큼 오르는 명중률(%) — 0 · 10 · 15 · 20 …, 파트1 마법은 0 · 5 · 10 · 15 …</summary>
    internal static int AimBonusFor(int hits, bool magic = false) =>
        hits <= 0 ? 0 : magic ? MagicAimPercentFirst + (hits - 1) * MagicAimPercentMore : AimPercentFirst + (hits - 1) * AimPercentMore;

    /// <summary>안내 글 — 「하나 +10% · 둘 +15%」.</summary>
    internal static string AimGuideFor(bool magic) => $"박자 하나 맞추면 명중률 +{AimBonusFor(1, magic)}% · 둘이면 +{AimBonusFor(2, magic)}%";

    internal static string AimGuide => AimGuideFor(false);

    /// <summary>
    /// 타이밍 명중을 띄울 것인가 — 켜져 있고, 내가 움직이는 인물이 <b>명중률이 있는 어빌리티</b>를 쓸 때. 기본공격은 타이밍 어택의 몫이라 뺀다.
    /// 명중 굴림이 뜻이 있는 것만: 피해 어빌리티, 그리고 적에게 거는 보조(종류 2·3). 회복과 아군 보조에는 안 뜬다.
    /// </summary>
    internal bool WantedAim(UnitState a, WarOfGenesis.Assets.WorkData w) =>
        host._timedAim && !host._skippingAction && host.Btl.IsMine(a) && a.LeaderIndex < 0 && w.AbilityId != 0 && a.Data?.BasicWorkId != w.Id
        && w.Accuracy > 0 && (w.IsDamage || (w.Kind is 2 or 3 && (w.TargetMode == 1 || w.AreaMode == 1))) && !Part1Skill.HitsNoFoe(w.Id);

    /// <summary>이번 띠가 타이밍 명중의 것인가 — 안내 글과 결과 글이 달라진다.</summary>
    private bool _aim, _aimMagic;

    /// <summary>이번 띠의 안내 글 — 대상을 치는 타이밍 어택이면 예상 피해(사용자 요청), 아니면 null(규칙 안내).</summary>
    private string? _guide;

    /// <summary>
    /// 띠를 띄우고 박자가 다 눌리거나 놓칠 때까지 기다린다 — 끝나면 <see cref="Hits"/> · <see cref="AllHit"/> 에 결과가 있다.
    /// <paramref name="a"/> 가 없으면 미리 보기 — <paramref name="anchor"/> 자리에 그린다.
    /// </summary>
    /// <param name="target">치려는 대상 — 패턴 잇기는 같은 대상을 칠 때만 이어진다.</param>
    /// <param name="aim">타이밍 명중의 띠인가 — 늘 시작 패턴을 띄우고(패턴 잇기 없음) 타이밍 어택 통계에 안 센다.</param>
    internal IEnumerable<bool> Prompt(UnitState? a, Func<(int X, int Y)>? anchor = null, UnitState? target = null, bool aim = false, string? guide = null, bool magic = false)
    {
        _aim = aim;
        _aimMagic = magic;
        _guide = guide;
        Cfg.Normalize();
        // 어느 패턴을 띄울까 — 미리 보기는 수정 창에서 보고 있는 패턴, 전투는 이 인물이 같은 대상에게 이어 가던 패턴(없으면 시작 패턴).
        if (!ReferenceEquals(_chainBattle, host._units)) { _chainBattle = host._units; _chain.Clear(); }
        int index = a == null ? Cfg.Selected : Cfg.Start;
        if (a != null && target != null && _chain.TryGetValue((a, target), out int kept) && target.Alive && target.Hp > 0
            && (uint)kept < (uint)Cfg.Patterns.Count) index = kept;
        var pattern = Cfg.Patterns[index];
        _seconds = Math.Max(0.1, pattern.Seconds);
        _beats = [.. pattern.Beats.Select(b => (b.At, Math.Max(0.01, b.Half)))];
        _beat = new int[_beats.Length];
        (_unit, _anchor, _startAt, _result, Hits, AllHit) = (a, a == null ? anchor : null, host._realTime, null, 0, false);
        if (BattleScene.Trace && a != null)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"), $"timed hit prompt: {a.ChrCode} pattern {index + 1} target {(target == null ? -1 : Array.IndexOf(host._units, target))}{(guide != null ? $" guide {guide.Replace('\n', '|')}" : "")}" + Environment.NewLine);
        // 박자 칸이 지나가도록 안 누르면 그 박자는 놓친 것 — 박자가 다 끝날 때까지 돈다.
        while (Pending is var next && next < _beat.Length)
        {
            if (host._realTime > _startAt + _beats[next].At + _beats[next].Half + Cfg.Grace) _beat[next] = 2;
            else yield return true;
        }
        Hits = _beat.Count(b => b == 1);
        AllHit = Hits == _beat.Length;
        if (a != null && !aim)
        {
            host.Stats.Timed(Math.Min(Hits, 2));   // 통계 — 둘 이상(여러 번 치기) · 하나(크리티컬) · 없음
            // 다 맞췄으면 다음 공격은 이 패턴이 가리키는 패턴(없으면 이 패턴 그대로), 아니면 처음으로.
            if (target != null)
            {
                if (AllHit) _chain[(a, target)] = pattern.Next >= 0 && pattern.Next < Cfg.Patterns.Count ? pattern.Next : index;
                else _chain.Remove((a, target));
            }
        }
        _result = Hits > 0;
        _resultAt = host._realTime;
        if (Hits > 0) host.Play(BattleScene.SoundPick);
        while (host._realTime < _resultAt + Cfg.ShowResultSeconds) yield return true;
        (_unit, _anchor) = (null, null);
    }

    /// <summary>박자 i 에 누를 패드 단추 — 0 은 어빌리티 단추(기본 LT), 1 은 공격 단추(기본 RT). 번갈아 온다.</summary>
    internal static int ButtonOfBeat(int i) => i & 1;

    /// <summary>
    /// 단추를 눌렀다 — 0 은 어빌리티 단추, 1 은 공격 단추, −1 은 키보드(Space — 어느 박자에나 맞는다).
    /// 지금 박자 칸 안이면 그 박자를 맞춘 것이고, 칸보다 일찍이면 그 박자를 놓친 것이다.
    /// </summary>
    /// <remarks>
    /// 칸 안에서 <b>다른 단추</b>를 눌러도 그 박자는 맞춘 것으로 친다(사용자 요청 — 실패가 아니다). 다만 그것으로 끝낸다 — 남은 박자는 접어
    /// 「덜 맞춘 것」(크리티컬)이 된다.
    /// </remarks>
    internal void Press(int which)
    {
        int now = Pending;
        if (!Running || _result != null || now >= _beat.Length) return;
        double off = host._realTime - _startAt - _beats[now].At, reach = _beats[now].Half + Cfg.Grace;
        if (off < -reach) { _beat[now] = 2; return; }   // 너무 일찍 — 이 박자는 놓쳤다
        if (off > reach) return;                        // 칸이 지났다(곧 놓침으로 넘어간다)
        _beat[now] = 1;
        host.Play(BattleScene.SoundHover);
        if (which >= 0 && which != ButtonOfBeat(now))
            for (int i = now + 1; i < _beat.Length; i++) _beat[i] = 2;   // 다른 단추로 맞췄다 — 여기서 끝
    }

    /// <summary>키보드 — Space 를 박자마다 한 번씩. 다른 키는 무시한다.</summary>
    internal void PressKey(int key)
    {
        if (key == Win32.VK_SPACE) Press(-1);
    }

    /// <summary>띠 위(안내 글 · 단추)와 아래(결과 글)에 드는 높이 — 미리 보기 자리를 잡을 때 쓴다.</summary>
    internal const int AboveBar = 56, BelowBar = 46;

    /// <param name="preview">수정 창의 미리 보기를 그릴 차례인가 — 전투 그리기는 인물 것만, 수정 창은 미리 보기만 그린다.</param>
    // 수정 창의 미리 보기는 그 창과 같은 층(새 UI 면 창 해상도 층)에 그린다 — 판에 그리면 창 밑에 깔린다. 전투의 띠는 늘 판에 그린다.
    private bool _onUi;
    private void Fill(int x, int y, int w, int h, uint color) { if (_onUi) host.UiFill(x, y, w, h, color); else host.FillRect(x, y, w, h, color); }
    private void Stroke(int x, int y, int w, int h, uint color) { if (_onUi) host.UiStroke(x, y, w, h, color); else host.StrokeRect(x, y, w, h, color); }
    private void Text(string text, int x, int y, uint color, float size = 13f) { if (_onUi) host.UiText(text, x, y, color, size); else host.DrawText(text, x, y, color, size); }
    private (uint[] Px, int W, int H) Measure(string text, uint color, float size = 13f) => _onUi ? host.UiGetText(text, color, size) : host.GetText(text, color, size);

    internal void Draw(bool preview = false)
    {
        if (preview ? _anchor == null : _unit == null) return;
        _onUi = preview;
        int BarW = Math.Clamp(Cfg.BarWidth, 60, 600);
        const int BarH = 12, Cap = 24;
        double seconds = _seconds;
        int x, y;
        if (_unit is { } u)
        {
            var (fx, fy) = host.Btl.UnitFoot(u);
            x = Math.Clamp(fx - BarW / 2, host._camX + 8, Math.Max(host._camX + 8, host._camX + host.ViewWidth - BarW - 8));
            y = Math.Clamp(fy - 112, host._camY + GridTop + Cap + 30, host._camY + host.ViewHeight - 44);
        }
        else (x, y) = _anchor!();
        int cx = x + BarW / 2;
        double at = Math.Clamp(((_result == null ? host._realTime : _resultAt) - _startAt) / seconds, 0, 1);

        // 바탕 — 안내 글 · 단추 · 띠를 한 판에.
        string guide = _guide ?? (_aim ? AimGuideFor(_aimMagic) : _beat.Length == 1 ? "박자에 맞추면 크리티컬" : "맞춘 박자만큼 공격 · 하나면 크리티컬");
        // 안내 글은 여러 줄일 수 있다 — 예상 피해 아래 명중률 · 셈의 바탕(fg-48). 둘째 줄부터는 조금 작고 흐리게.
        var lines = guide.Split('\n');
        var sizes = lines.Select((l, i) => Measure(l, 0xFFFFFFFF, i == 0 ? 11f : 9.5f)).ToArray();
        int gw = sizes.Max(m => m.W), gh = sizes.Sum(m => m.H + 2) - 2;
        int top = y - Cap - 8 - gh - 4;
        Fill(Math.Min(x, cx - gw / 2) - 8, top - 4, Math.Max(BarW, gw) + 16, y + BarH + 6 - top + 4, 0xC0000000);
        for (int i = 0, ly = top; i < lines.Length; ly += sizes[i].H + 2, i++)
            Text(lines[i], cx - sizes[i].W / 2, ly, i == 0 ? 0xFFFFFFFF : 0xFFC8D0E0, i == 0 ? 11f : 9.5f);

        // 띠와 박자 칸 — 맞춘 칸은 초록, 놓친 칸은 붉게, 달리는 표시는 흰 줄. 칸 위에 그 박자에 누를 단추.
        Fill(x, y, BarW, BarH, 0xFF303848);
        bool pad = host.Pad.Active;
        string space = _beat.Length <= 2 ? "Space" : "Sp";
        for (int i = 0; i < _beat.Length; i++)
        {
            int zx = x + (int)(BarW * (_beats[i].At - _beats[i].Half) / seconds), zw = Math.Max(2, (int)(BarW * 2 * _beats[i].Half / seconds));
            int z0 = Math.Clamp(zx, x, x + BarW), z1 = Math.Clamp(zx + zw, x, x + BarW);
            bool done = _beat[i] == 1, missed = _beat[i] == 2;
            Fill(z0, y, z1 - z0, BarH, done ? 0xFF60FF60 : missed ? 0xFF804030 : 0xFFFFD040);
            string label = pad ? GamePad.ButtonLabel(host.Pad.ButtonOf(ButtonOfBeat(i) == 0 ? PadAction.Ability : PadAction.Attack)) : space;
            var (_, tw, th) = Measure(label, 0xFFFFFFFF, 13f);
            int bw = Math.Max(Cap, tw + 10), bx = zx + zw / 2 - bw / 2, by = y - Cap - 5;
            Fill(bx, by, bw, Cap, done ? 0xFF60D060 : missed ? 0xFF907060 : 0xFFE3B824);
            Stroke(bx, by, bw, Cap, 0xFFFFFFFF);
            Text(label, bx + (bw - tw) / 2, by + (Cap - th) / 2, 0xFF201800, 13f);
        }
        Fill(x + (int)(BarW * at) - 1, y - 4, 3, BarH + 8, 0xFFFFFFFF);

        // 결과 — 눌린 것이 바로 보이게 크게.
        if (_result is { } hit)
        {
            string word = !hit ? "놓침" : _aim ? $"명중률 +{AimBonusFor(Hits, _aimMagic)}%" : Hits == 1 ? "크리티컬!" : (AllHit ? "성공! " : "") + $"{Hits}번 공격";
            uint color = hit ? 0xFFFFE040 : 0xFFFF7060;
            var (_, ww, wh) = Measure(word, color, 18f);
            int wy = y + BarH + 10;
            Fill(cx - ww / 2 - 8, wy - 3, ww + 16, wh + 6, hit ? 0xE0204010 : 0xE0401810);
            Stroke(cx - ww / 2 - 8, wy - 3, ww + 16, wh + 6, color);
            Text(word, cx - ww / 2, wy, color, 18f);
        }
    }
}

using System.Text.Json;
using DuelDx.Native;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 타이밍 어택의 수치 — 개발 &gt; 타이밍 어택 수정 창(<see cref="TimedHitEditScreen"/>)에서 고치며 맞춘다. <c>%APPDATA%\DuelDx\timedhit.json</c> 에 남는다.
/// </summary>
internal sealed class TimedHitConfig
{
    /// <summary>띠가 다 지나가는 시간(초).</summary>
    public double Seconds { get; set; } = 1.2;
    /// <summary>첫 박자 · 둘째 박자의 때(시작에서 초).</summary>
    public double Beat1 { get; set; } = 0.45;
    public double Beat2 { get; set; } = 0.95;
    /// <summary>박자 칸의 반 너비(초) — 띠에 그리는 폭.</summary>
    public double Tolerance { get; set; } = 0.18;
    /// <summary>판정은 그린 칸보다 앞뒤로 이만큼 더 받아 준다(초) — 칸 끝에 걸친 누름이 빗나가지 않게(사용자 요청: 넓게 · 후하게).</summary>
    public double Grace { get; set; } = 0.04;
    /// <summary>결과 글을 보이는 시간(초).</summary>
    public double ShowResultSeconds { get; set; } = 0.7;
    /// <summary>둘 다 맞췄는데 두 번 치는 동작이 없는 인물일 때의 피해 배율(%).</summary>
    public int DamagePercent { get; set; } = 200;
    /// <summary>띠 너비(픽셀).</summary>
    public int BarWidth { get; set; } = 240;

    internal static string FilePath => UserDataFolder.File("timedhit.json");

    internal static TimedHitConfig Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<TimedHitConfig>(File.ReadAllText(FilePath)) is { } loaded) return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* 못 읽으면 기본값 */ }
        return new TimedHitConfig();
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
/// 타이밍 어택(모드 &gt; 실험, 사용자 요청 fg-32 — 원본에 없다). 내가 움직이는 인물이 기본공격을 하기 바로 앞에 띠가 뜨고,
/// 띠 위를 달리는 표시가 <b>두 박자 칸</b>을 지날 때마다 단추를 하나씩 누른다 — <b>둘 다 맞추면 두 번 치고</b>(합쳐 피해 2배),
/// <b>하나만 맞추면 크리티컬</b>이다. 어느 쪽이든 빗나가지 않는다. 하나도 못 맞추면 보통 판정 그대로다(벌은 없다).
/// 패드는 어빌리티 단추 → 공격 단추(기본 LT → RT), 키보드는 Space 두 번(사용자 요청 — E 로 공격을 건 뒤 Space · Space).
/// </summary>
/// <remarks>
/// 박자의 때와 칸 너비 따위는 <see cref="TimedHitConfig"/> 에 있다(기본 0.45초 · 0.95초, 앞뒤 0.18 + 0.04초).
/// 순서만 맞으면 되던 것을 박자에 맞춰 누르게 바꿨다(사용자 요청). 박자 하나를 놓쳐도 다음 박자는 그대로 받는다. 칸보다 일찍 누르면 그 박자는 놓친 것이다.
/// 시간은 게임 속도와 상관없이 실제 시간으로 잰다. AI 가 움직이는 편 · 어빌리티 · 군단 부하의 타격에는 안 뜬다.
/// </remarks>
internal sealed class TimedHit(GameWindow host)
{
    internal const int BeatCount = 2;

    /// <summary>지금 수치 — 수정 창이 바로 고친다.</summary>
    internal TimedHitConfig Cfg = TimedHitConfig.Load();

    private double Beat(int i) => i == 0 ? Cfg.Beat1 : Cfg.Beat2;

    private UnitState? _unit;

    /// <summary>미리 보기(수정 창) — 인물 대신 띠를 놓을 자리(띠 왼위, 판 픽셀)를 돌려준다.</summary>
    private Func<(int X, int Y)>? _anchor;

    private double _startAt, _resultAt;
    private bool? _result;

    /// <summary>박자마다 — 0 아직 · 1 맞춤 · 2 놓침.</summary>
    private readonly int[] _beat = new int[BeatCount];

    /// <summary>단추를 기다리는(또는 결과를 잠깐 보이는) 중인가 — 그동안 키보드와 패드 단추는 여기로만 온다.</summary>
    internal bool Running => _unit != null || _anchor != null;

    /// <summary>방금 끝난 것에서 맞춘 박자 수 — 2 면 두 번 치기, 1 이면 크리티컬, 0 이면 보통.</summary>
    internal int Hits { get; private set; }

    /// <summary>
    /// 박자 둘을 다 맞춘 것이 몇 번 이어졌나(이번 것 포함) — 이어질 때마다 공격 횟수가 하나씩 는다(사용자 요청).
    /// <b>인물마다, 같은 대상 하나를 치고 있는 동안</b>만 센다 — 다른 대상을 치면 처음부터, 그 대상이 쓰러져도 처음부터.
    /// 하나만 맞추거나 놓쳐도 0 으로, 새 전투에서도 0 으로 돌아간다.
    /// </summary>
    internal int Streak { get; private set; }
    private object? _streakBattle;

    /// <summary>인물마다 — 지금 연속으로 치고 있는 대상과 그 횟수.</summary>
    private readonly Dictionary<UnitState, (UnitState? Target, int Count)> _streaks = [];

    /// <summary>연속 성공으로 늘어나는 공격 횟수의 위 끝 — 3번에 닿으면 틀리지 않는 한 3번이 이어진다(사용자 결정).</summary>
    internal const int MaxAttacks = 3;

    /// <summary>방금 성공으로 칠 횟수 — 첫 성공 2번, 이어서 성공하면 3번, 그 뒤로는 3번 그대로.</summary>
    internal int AttackCount => Math.Clamp(1 + Streak, 2, MaxAttacks);

    /// <summary>아직 안 끝난 첫 박자 — 다 끝났으면 박자 수.</summary>
    private int Pending => Array.IndexOf(_beat, 0) is var i and >= 0 ? i : BeatCount;

    /// <summary>이 공격에 띄울 것인가 — 켜져 있고, 내가 움직이는 인물의 기본공격일 때만.</summary>
    internal bool Wanted(UnitState a, WarOfGenesis.Assets.WorkData w) =>
        host._timedHit && !host._skippingAction && host.Btl.IsMine(a) && a.LeaderIndex < 0 && w.IsDamage && a.Data?.BasicWorkId == w.Id;

    /// <summary>
    /// 띠를 띄우고 두 박자가 다 눌리거나 놓칠 때까지 기다린다 — 끝나면 <see cref="Hits"/> 에 결과가 있다.
    /// <paramref name="a"/> 가 없으면 미리 보기 — <paramref name="anchor"/> 자리에 그린다.
    /// </summary>
    /// <param name="target">치려는 대상 — 연속 성공은 같은 대상을 칠 때만 이어진다.</param>
    internal IEnumerable<bool> Prompt(UnitState? a, Func<(int X, int Y)>? anchor = null, UnitState? target = null)
    {
        (_unit, _anchor, _startAt, _result, Hits) = (a, a == null ? anchor : null, host._realTime, null, 0);
        Array.Clear(_beat);
        if (BattleScene.Trace && a != null)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"), $"timed hit prompt: {a.ChrCode}" + Environment.NewLine);
        // 박자 칸이 지나가도록 안 누르면 그 박자는 놓친 것 — 두 박자가 다 끝날 때까지 돈다.
        while (Pending is var next && next < BeatCount)
        {
            if (host._realTime > _startAt + Beat(next) + Cfg.Tolerance + Cfg.Grace) _beat[next] = 2;
            else yield return true;
        }
        Hits = _beat.Count(b => b == 1);
        if (a != null)
        {
            // 연속 성공 — 전투가 바뀌었으면(유닛 배열이 새것) 모두 버린다. 같은 대상이 살아 있는 채로 이어 칠 때만 앞의 횟수를 잇는다.
            if (!ReferenceEquals(_streakBattle, host._units)) { _streakBattle = host._units; _streaks.Clear(); }
            int before = _streaks.TryGetValue(a, out var kept) && target != null && kept.Target == target && target.Alive && target.Hp > 0 ? kept.Count : 0;
            Streak = Hits == BeatCount ? before + 1 : 0;
            _streaks[a] = (target, Streak);
            host.Stats.Timed(Hits);
        }
        _result = Hits > 0;
        _resultAt = host._realTime;
        if (Hits > 0) host.Play(BattleScene.SoundPick);
        while (host._realTime < _resultAt + Cfg.ShowResultSeconds) yield return true;
        (_unit, _anchor) = (null, null);
    }

    /// <summary>
    /// 단추를 눌렀다 — 0 은 첫 박자의 단추(어빌리티 단추), 1 은 둘째 박자의 단추(공격 단추). 그 박자 칸 안이면 맞춘 것이고,
    /// 칸 밖(일찍)이면 그 박자를 놓친 것이다. 이미 끝난 박자의 단추는 무시한다.
    /// </summary>
    /// <remarks>
    /// 지금 박자 칸 안에서 <b>다른 단추</b>를 눌러도 그 박자는 맞춘 것으로 친다(사용자 요청 — 실패가 아니다). 첫 칸에서 둘째 단추(RT)를 누른 것이면
    /// 그것으로 끝낸다 — 하나만 맞춘 것(크리티컬)이 된다.
    /// </remarks>
    internal void Press(int which)
    {
        if (!Running || _result != null || (uint)which >= BeatCount) return;
        bool InZone(int i) => Math.Abs(host._realTime - _startAt - Beat(i)) <= Cfg.Tolerance + Cfg.Grace;
        int now = Pending;
        if (now < BeatCount && which != now && InZone(now))
        {
            _beat[now] = 1;
            if (which > now) for (int i = now + 1; i < BeatCount; i++) _beat[i] = 2;   // 뒤 박자의 단추를 써 버렸다 — 여기서 끝
            host.Play(BattleScene.SoundHover);
            return;
        }
        if (_beat[which] != 0) return;
        bool hit = InZone(which);
        _beat[which] = hit ? 1 : 2;
        if (hit) host.Play(BattleScene.SoundHover);
    }

    /// <summary>키보드 — Space 를 박자마다 한 번씩(아직 안 끝난 첫 박자에 든다). 다른 키는 무시한다.</summary>
    internal void PressKey(int key)
    {
        if (key == Win32.VK_SPACE) Press(Pending);
    }

    /// <summary>띠 위(안내 글 · 단추)와 아래(결과 글)에 드는 높이 — 미리 보기 자리를 잡을 때 쓴다.</summary>
    internal const int AboveBar = 56, BelowBar = 46;

    /// <param name="preview">수정 창의 미리 보기를 그릴 차례인가 — 전투 그리기는 인물 것만, 수정 창은 미리 보기만 그린다.</param>
    internal void Draw(bool preview = false)
    {
        if (preview ? _anchor == null : _unit == null) return;
        int BarW = Math.Clamp(Cfg.BarWidth, 60, 600);
        const int BarH = 12, Cap = 24;
        double seconds = Math.Max(0.1, Cfg.Seconds);
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
        const string guide = "박자 둘 = 두 번 공격 · 하나 = 크리티컬";
        var (_, gw, gh) = host.GetText(guide, 0xFFFFFFFF, 11f);
        int top = y - Cap - 8 - gh - 4;
        host.FillRect(Math.Min(x, cx - gw / 2) - 8, top - 4, Math.Max(BarW, gw) + 16, y + BarH + 6 - top + 4, 0xC0000000);
        host.DrawText(guide, cx - gw / 2, top, 0xFFFFFFFF, 11f);

        // 띠와 박자 칸 둘 — 맞춘 칸은 초록, 놓친 칸은 붉게, 달리는 표시는 흰 줄. 칸 위에 그 박자에 누를 단추.
        host.FillRect(x, y, BarW, BarH, 0xFF303848);
        for (int i = 0; i < BeatCount; i++)
        {
            int zx = x + (int)(BarW * (Beat(i) - Cfg.Tolerance) / seconds), zw = (int)(BarW * 2 * Cfg.Tolerance / seconds);
            int z0 = Math.Clamp(zx, x, x + BarW), z1 = Math.Clamp(zx + zw, x, x + BarW);
            bool done = _beat[i] == 1, missed = _beat[i] == 2;
            host.FillRect(z0, y, z1 - z0, BarH, done ? 0xFF60FF60 : missed ? 0xFF804030 : 0xFFFFD040);
            string label = host.Pad.Active ? GamePad.ButtonLabel(host.Pad.ButtonOf(i == 0 ? PadAction.Ability : PadAction.Attack)) : "Space";
            var (_, tw, th) = host.GetText(label, 0xFFFFFFFF, 13f);
            int bw = Math.Max(Cap, tw + 12), bx = zx + zw / 2 - bw / 2, by = y - Cap - 5;
            host.FillRect(bx, by, bw, Cap, done ? 0xFF60D060 : missed ? 0xFF907060 : 0xFFE3B824);
            host.StrokeRect(bx, by, bw, Cap, 0xFFFFFFFF);
            host.DrawText(label, bx + (bw - tw) / 2, by + (Cap - th) / 2, 0xFF201800, 13f);
        }
        host.FillRect(x + (int)(BarW * at) - 1, y - 4, 3, BarH + 8, 0xFFFFFFFF);

        // 결과 — 눌린 것이 바로 보이게 크게.
        if (_result is { } hit)
        {
            string word = Hits == 2 ? (_unit != null ? $"성공! {AttackCount}번 공격" + (Streak > 1 ? $" · 연속 {Streak}" : "") : "성공! 두 번 공격") : Hits == 1 ? "크리티컬!" : "놓침";
            uint color = hit ? 0xFFFFE040 : 0xFFFF7060;
            var (_, ww, wh) = host.GetText(word, color, 18f);
            int wy = y + BarH + 10;
            host.FillRect(cx - ww / 2 - 8, wy - 3, ww + 16, wh + 6, hit ? 0xE0204010 : 0xE0401810);
            host.StrokeRect(cx - ww / 2 - 8, wy - 3, ww + 16, wh + 6, color);
            host.DrawText(word, cx - ww / 2, wy, color, 18f);
        }
    }
}

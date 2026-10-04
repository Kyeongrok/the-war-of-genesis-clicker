using System.IO;

namespace DuelDx;

/// <summary>
/// 화면 밖 전수 시험용 훅 — <c>DUELDX_TESTRUN=&lt;N&gt;</c> 일 때만 켜진다(기본 꺼짐, 게임 동작은 그대로).
/// </summary>
/// <remarks>
/// 시험 전용: 모든 전투를 자동 진행(DUELDX_AUTOPLAY)으로 끝까지 돌려 끝나지 않는 전투를 찾으려고 둔 것이다.
/// N 은 한 프레임에 게임 갱신(Update)을 몇 번 돌릴지 — 그리지 않고 시계만 빨리 돌린다.
/// <c>DUELDX_TESTSKIP=1</c> 이면 전투 사건(대사·기다림)을 Esc 처럼 곧바로 건너뛴다.
/// 몇 초마다 차례 수·편별 생존 수를 <c>%TEMP%\dueldx_trace.log</c> 에 「testrun」 줄로 남긴다.
/// </remarks>
internal sealed unsafe partial class GameWindow
{
    /// <summary>시험 전용: 한 프레임에 Update 를 몇 번 — 변수가 없으면 1(평소대로).</summary>
    internal static readonly int TestRunSteps =
        int.TryParse(Environment.GetEnvironmentVariable("DUELDX_TESTRUN"), out int n) && n > 0 ? Math.Min(n, 64) : 1;

    /// <summary>시험 전용 훅이 켜졌나.</summary>
    internal static readonly bool TestRun = Environment.GetEnvironmentVariable("DUELDX_TESTRUN") is { Length: > 0 };

    /// <summary>시험 전용: 전투 사건을 곧바로 건너뛸지.</summary>
    internal static readonly bool TestSkip = Environment.GetEnvironmentVariable("DUELDX_TESTSKIP") == "1";

    internal double _testRunStatAt;

    /// <summary>시험 전용: 대사를 눌러 넘길지(DUELDX_TESTSKIP=2)와 다음에 누를 때.</summary>
    internal static readonly bool TestClick = Environment.GetEnvironmentVariable("DUELDX_TESTSKIP") == "2";
    internal double _testClickAt;

    /// <summary>
    /// 시험 전용: <c>DUELDX_CLICKS=x,y@초;x,y@초…</c> — 그 때(실제 초) <b>보이는 화면 좌표</b>(스냅숏 그림의 좌표)를 왼쪽 클릭한다.
    /// 창 클라이언트 좌표는 배율·여백 때문에 스냅숏과 달라 PostMessage 클릭이 빗나갔다 — 여기서는 화면 좌표를 클라이언트 좌표로 바꿔 넣는다.
    /// </summary>
    internal List<(double At, int X, int Y)>? _testClicks;

    internal void TestClicksTick()
    {
        if (_testClicks == null)
        {
            _testClicks = [];
            foreach (string item in (Environment.GetEnvironmentVariable("DUELDX_CLICKS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                if (item.Split('@') is [var at, var when] && at.Split(',') is [var sx, var sy]
                    && int.TryParse(sx, out int x) && int.TryParse(sy, out int y)
                    && double.TryParse(when, System.Globalization.CultureInfo.InvariantCulture, out double seconds))
                    _testClicks.Add((seconds, x, y));
        }
        while (_testClicks.Count > 0 && _testClicks[0].At <= _realTime)
        {
            var (_, x, y) = _testClicks[0];
            _testClicks.RemoveAt(0);
            int cx = ViewOffsetX + (int)(x * _zoom), cy = ViewOffsetY + (int)(y * _zoom);
            var (bx, by) = BoardPoint(cx, cy);
            _mouse = (bx, by);
            OnClick(cx, cy);
        }
    }

    /// <summary>시험 전용: 갱신 한 번 뒤 — 사건 건너뛰기, 몇 초마다 상태 줄.</summary>
    internal void TestRunTick()
    {
        TestClicksTick();
        if (!TestRun) return;
        if (TestSkip && _battleLoaded && !FieldOpen && !Mos._mosesOpen && !TitleScr._titleOpen && Btl._outcome.Length == 0)
        {
            if (Btl._runningEvent >= 0 && !Tlk._talkSkip) Tlk.SkipScene();
            else if (Tlk._talks.Count > 0) Tlk.OnTalkInput(skipAll: true);
        }
        // DUELDX_TESTSKIP=2 — 건너뛰지 않고 대사만 0.5초마다 한 번씩 눌러 넘긴다(건너뛰지 않는 사건 진행 길을 시험하려고).
        if (TestClick && Tlk._talks.Count > 0 && _lastTime >= _testClickAt && Btl._outcome.Length == 0)
        {
            _testClickAt = _lastTime + 0.5;
            Tlk.OnTalkInput();
        }
        if (_lastTime < _testRunStatAt) return;
        _testRunStatAt = _lastTime + 10;
        string scene = TitleScr._titleOpen ? "title" : Mos._mosesOpen ? "moses" : FieldOpen ? "field" : _battleLoaded ? $"btl {_scene.Id}" : "none";
        string alive = string.Join(" ", _units.Where(u => u.Alive && u.OnField).GroupBy(u => u.Side).OrderBy(g => g.Key).Select(g => $"s{g.Key}={g.Count()}"));
        TestRunTrace($"stat t {_lastTime:F1} scene {scene} turnNo {Btl._turnNo} tick {Btl._tick} turn {Btl._turn} ev {Btl._runningEvent} talk {Tlk._talks.Count} outcome '{Btl._outcome}' alive {alive}");
    }

    /// <summary>시험 전용: 결과·행선지 줄 — 켜졌을 때만 적는다.</summary>
    internal static void TestRunTrace(string line)
    {
        if (!TestRun) return;
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), "testrun " + line + Environment.NewLine); }
        catch (IOException) { }
    }
}

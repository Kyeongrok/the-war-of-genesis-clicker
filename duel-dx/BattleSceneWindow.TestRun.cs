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
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>시험 전용: 한 프레임에 Update 를 몇 번 — 변수가 없으면 1(평소대로).</summary>
    private static readonly int TestRunSteps =
        int.TryParse(Environment.GetEnvironmentVariable("DUELDX_TESTRUN"), out int n) && n > 0 ? Math.Min(n, 64) : 1;

    /// <summary>시험 전용 훅이 켜졌나.</summary>
    private static readonly bool TestRun = Environment.GetEnvironmentVariable("DUELDX_TESTRUN") is { Length: > 0 };

    /// <summary>시험 전용: 전투 사건을 곧바로 건너뛸지.</summary>
    private static readonly bool TestSkip = Environment.GetEnvironmentVariable("DUELDX_TESTSKIP") == "1";

    private double _testRunStatAt;

    /// <summary>시험 전용: 갱신 한 번 뒤 — 사건 건너뛰기, 몇 초마다 상태 줄.</summary>
    private void TestRunTick()
    {
        if (!TestRun) return;
        if (TestSkip && _battleLoaded && !FieldOpen && !_mosesOpen && !_titleOpen && _outcome.Length == 0)
        {
            if (_runningEvent >= 0 && !_talkSkip) SkipScene();
            else if (_talks.Count > 0) OnTalkInput(skipAll: true);
        }
        if (_lastTime < _testRunStatAt) return;
        _testRunStatAt = _lastTime + 10;
        string scene = _titleOpen ? "title" : _mosesOpen ? "moses" : FieldOpen ? "field" : _battleLoaded ? $"btl {_scene.Id}" : "none";
        string alive = string.Join(" ", _units.Where(u => u.Alive && u.OnField).GroupBy(u => u.Side).OrderBy(g => g.Key).Select(g => $"s{g.Key}={g.Count()}"));
        TestRunTrace($"stat t {_lastTime:F1} scene {scene} turnNo {_turnNo} tick {_tick} turn {_turn} ev {_runningEvent} talk {_talks.Count} outcome '{_outcome}' alive {alive}");
    }

    /// <summary>시험 전용: 결과·행선지 줄 — 켜졌을 때만 적는다.</summary>
    private static void TestRunTrace(string line)
    {
        if (!TestRun) return;
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), "testrun " + line + Environment.NewLine); }
        catch (IOException) { }
    }
}

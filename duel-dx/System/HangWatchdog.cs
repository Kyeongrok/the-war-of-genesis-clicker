using System.IO;
using System.Threading;

namespace DuelDx;

/// <summary>
/// 멈춤 감시 — 주 루프(<see cref="GameWindow.Run"/>)가 틀마다 <see cref="Beat"/> 를 치고, 배경 스레드가 그 박동이
/// <see cref="StallSeconds"/> 초 넘게 끊기면 그때 무엇을 하던 중인지(주 루프 단계 · 장면 · 전투 · 차례 · 돌던 행동)를 오류 기록
/// (<see cref="BugReport.CrashLogPath"/>)에 덧붙인다. 다시 돌면 몇 초 멈췄는지도 적는다.
/// </summary>
/// <remarks>
/// 버그 리포트 #100(파트1 「흑태자교 봉기」 전투가 몇 차례 뒤 늘 멈춘다, 크래시 로그 없음)을 화면 밖 자동 진행 · 휴식 진행으로 재현하지 못해서 둔 것이다 —
/// 멈춤은 예외가 아니라 오류 기록이 비어 있었다. 버그 리포트는 오류 기록 끝을 저절로 붙이므로(<see cref="BugReport.CrashLogTail"/>), 다음 제보에는 멈춘 자리가 실린다.
/// 감시는 읽기만 한다(값을 바꾸지 않는다). 창을 끌거나 크기를 바꾸는 동안(Windows 의 모달 루프)도 박동이 끊기므로 문턱을 넉넉히 두고,
/// 컴퓨터가 잠들었다 깬 것(감시 스레드 자신이 늦게 깬 것)은 멈춤으로 치지 않는다.
/// </remarks>
internal static class HangWatchdog
{
    /// <summary>이만큼(초) 박동이 없으면 멈춤으로 적는다.</summary>
    internal const int StallSeconds = 15;

    /// <summary>주 루프 단계 — 멈춘 곳을 가르려고.</summary>
    internal enum Stage { Messages = 1, Update, SkipEnemyAction, Cutscene, Render }

    private static long _beatAt;
    private static int _stage;
    private static GameWindow? _host;
    private static Thread? _thread;

    /// <summary>감시 스레드를 띄운다 — 한 번만.</summary>
    internal static void Start(GameWindow host)
    {
        if (_thread != null) return;
        _host = host;
        Volatile.Write(ref _beatAt, Environment.TickCount64);
        _thread = new Thread(Watch) { IsBackground = true, Name = "HangWatchdog", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>주 루프가 지금 이 단계에 들어선다 — 박동도 함께 친다.</summary>
    internal static void Beat(Stage stage)
    {
        Volatile.Write(ref _stage, (int)stage);
        Volatile.Write(ref _beatAt, Environment.TickCount64);
    }

    private static void Watch()
    {
        long lastWake = Environment.TickCount64, reportedFor = -1;
        while (true)
        {
            Thread.Sleep(1000);
            long now = Environment.TickCount64, beat = Volatile.Read(ref _beatAt);
            // 감시 스레드가 한참 늦게 깼으면(잠자기 · 최대 절전) 그동안 끊긴 박동은 멈춤이 아니다 — 박동 시각을 지금으로 본다.
            if (now - lastWake > 5000) { Interlocked.CompareExchange(ref _beatAt, now, beat); lastWake = now; continue; }
            lastWake = now;
            long stalled = now - beat;
            if (stalled >= StallSeconds * 1000L && reportedFor != beat)
            {
                reportedFor = beat;
                Append($"멈춤 감지 — 주 루프가 {stalled / 1000}초째 안 돈다 · {Describe()}");
            }
            else if (reportedFor >= 0 && beat != reportedFor)
            {
                Append($"멈춤 풀림 — {(beat - reportedFor) / 1000.0:0.0}초 멈췄다가 다시 돈다");
                reportedFor = -1;
            }
        }
    }

    /// <summary>지금 상태 한 줄 — 다른 스레드에서 읽으므로 값이 어긋날 수 있고, 읽다 깨지면 그만큼만 적는다.</summary>
    private static string Describe()
    {
        var parts = new List<string> { $"단계 {(Stage)Volatile.Read(ref _stage)}" };
        try
        {
            if (_host is not { } host) return string.Join(" · ", parts);
            parts.Add($"장면 {host.SceneTagText() ?? "?"}");
            if (host.Part1.ChapterId >= 0) parts.Add($"파트1 챕터 {host.Part1.ChapterId}");
            if (host._battleLoaded)
            {
                var btl = host.Btl;
                parts.Add($"전투 {host._scene.Id} 차례 {btl._turnNo} 틱 {btl._tick}");
                int turn = btl._turn;
                if ((uint)turn < (uint)host._units.Length && host._units[turn] is { } u)
                    parts.Add($"차례 인물 {u.ChrCode} 편 {u.Side} ({u.Col},{u.Row}) 내 조종 {btl.IsMine(u)}");
                if (btl._routine is { } routine) parts.Add($"행동 {routine.GetType().Name}");
                if (btl._runningEvent >= 0) parts.Add($"사건 {btl._runningEvent}");
                if (btl._outcome.Length > 0) parts.Add($"결과 {btl._outcome}");
                parts.Add($"살아 있음 {string.Join(" ", host._units.Where(x => x.Alive && x.OnField).GroupBy(x => x.Side).OrderBy(g => g.Key).Select(g => $"편{g.Key}={g.Count()}"))}");
            }
        }
        catch (Exception ex) { parts.Add($"(상태를 다 못 읽음: {ex.GetType().Name})"); }
        return string.Join(" · ", parts);
    }

    private static void Append(string line)
    {
        try { File.AppendAllText(BugReport.CrashLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {GameWindow.AppVersion}  {line}{Environment.NewLine}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        GameWindow.TestRunTrace("hang " + line);   // 시험 전용 줄
    }
}

using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DuelDx.Native;

namespace DuelDx;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 설치판(Setup.exe)의 메인 exe 가 이것이다 — Velopack 이 설치·업데이트·제거 때 특수 인자로 불러
        // 곧바로 끝나기를 기다리므로 <b>맨 앞</b>에서 받아 준다. 그냥 켰으면 아무 일 없이 지나간다.
        Velopack.VelopackApp.Build().Run();
        Updater.CheckInBackground();
        VoicePack.EnsureInBackground();   // 대사 음성이 없으면 뒤에서 받는다

        // 게임 창 스레드가 아닌 곳에서 죽어도 같은 창을 띄운다.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) ReportCrash(ex); };

        // 모니터 배율(150%·200% 따위)을 창이 직접 알게 한다 — 안 그러면 OS 가 우리
        // 창을 통째로 다시 늘려, 이미 2배로 그린 그림이 또 늘어나 뭉갠다.
        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        try
        {
            using var window = new GameWindow();
            window.Run();
        }
        catch (Exception ex)
        {
            ReportCrash(ex);
            Environment.Exit(1);
        }
    }

    private static int _reported;

    /// <summary>
    /// 잡히지 않은 예외를 <c>%TEMP%\dueldx_crash.log</c> 에 남기고 오류 창을 띄운다.
    /// 전에는 로그만 쓰고 조용히 꺼져, 설치판을 받은 사람에게는 「실행이 안 된다」로만 보였다(사용자 보고) —
    /// 창에 오류와 로그 자리를 적어 스크린샷으로 제보할 수 있게 한다. 화면 밖 시험(DUELDX_OFFSCREEN)에서는 창을 안 띄운다.
    /// </summary>
    private static void ReportCrash(Exception ex)
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0) return;
        string logPath = Path.Combine(Path.GetTempPath(), "dueldx_crash.log");
        string version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        try { File.WriteAllText(logPath, $"v{version}  {Environment.OSVersion}\n{ex}"); } catch (IOException) { }
        if (Environment.GetEnvironmentVariable("DUELDX_OFFSCREEN") == "1") return;

        var root = ex;
        while (root.InnerException != null) root = root.InnerException;
        MessageBoxW(IntPtr.Zero,
            $"게임을 시작하지 못했습니다.\n\n{root.GetType().Name}: {root.Message}\n\n"
            + $"버전 v{version} · {Environment.OSVersion}\n\n"
            + $"이 창을 스크린샷으로 찍어 제보해 주세요. 자세한 기록은 아래 파일에 있습니다.\n{logPath}",
            "창세기전3 파트2 — 오류", MB_OK | MB_ICONERROR | MB_TOPMOST | MB_SETFOREGROUND);
    }

    private const uint MB_OK = 0x0, MB_ICONERROR = 0x10, MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}

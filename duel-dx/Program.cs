using System.IO;
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

        // 모니터 배율(150%·200% 따위)을 창이 직접 알게 한다 — 안 그러면 OS 가 우리
        // 창을 통째로 다시 늘려, 이미 2배로 그린 그림이 또 늘어나 뭉갠다.
        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        try
        {
            using var window = new BattleSceneWindow();
            window.Run();
        }
        catch (Exception ex)
        {
            // 잡히지 않은 예외는 %TEMP%\dueldx_crash.log 에 남긴다 — 화면 밖 시험에서도 무엇에 죽었는지 볼 수 있게.
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "dueldx_crash.log"), ex.ToString()); } catch (IOException) { }
            throw;
        }
    }
}

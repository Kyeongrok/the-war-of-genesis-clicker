using DuelDx.Engine;
using DuelDx.Native;

namespace DuelDx;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 모니터 배율(150%·200% 따위)을 창이 직접 알게 한다 — 안 그러면 OS 가 우리
        // 창을 통째로 다시 늘려, 이미 2배로 그린 그림이 또 늘어나 뭉갠다.
        Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        var mine = new Duel.Fighter("나", body: 100, might: 60, sword: 2, luck: 40, weapon: 20, armour: 15);
        var rival = new Duel.Fighter("라이벌", body: 100, might: 65, sword: 2, luck: 35, weapon: 18, armour: 12);

        using var window = new DuelWindow(mine, rival, new Random());
        window.Run();
    }
}

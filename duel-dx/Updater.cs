using System.Runtime.InteropServices;
using Velopack;
using Velopack.Sources;

namespace DuelDx;

/// <summary>
/// 설치판(Setup.exe)의 자동 업데이트 — GitHub 릴리즈에서 새 버전을 뒤에서 받아 두고, 지금 다시 시작할지 묻는다.
/// </summary>
/// <remarks>
/// Setup.exe 로 깔지 않은 판(단일 파일 exe · zip · 개발 중 빌드)은 <see cref="UpdateManager.IsInstalled"/> 가 거짓이라 아무것도 안 한다.
/// 「아니오」면 게임을 끌 때 적용된다(<see cref="UpdateManager.WaitExitThenApplyUpdates"/>).
/// 인터넷이 없거나 GitHub 이 막혀도 게임은 그대로 돈다 — 조용히 넘어간다.
/// </remarks>
internal static class Updater
{
    /// <summary>릴리즈가 올라가는 저장소.</summary>
    private const string RepoUrl = "https://github.com/Kyeongrok/the-war-of-genesis-clicker";

    public static void CheckInBackground() => _ = Task.Run(CheckAsync);

    private static async Task CheckAsync()
    {
        try
        {
            var manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
            if (!manager.IsInstalled) return;

            var update = await manager.CheckForUpdatesAsync();
            if (update == null) return;

            await manager.DownloadUpdatesAsync(update);
            var target = update.TargetFullRelease;

            // 게임 창은 제 스레드에서 돈다 — 여기(뒤 스레드)서 띄우는 창이 게임 창 뒤에 묻히지 않게 맨 위로 올린다.
            bool now = MessageBoxW(IntPtr.Zero,
                $"새 버전 v{target.Version} 을(를) 받았습니다.\n지금 다시 시작해 적용하겠습니까?\n\n"
                + "(저장하지 않은 진행은 사라집니다. 아니오를 고르면 게임을 끌 때 적용됩니다.)",
                "창세기전3 파트2 — 업데이트", MB_YESNO | MB_ICONQUESTION | MB_TOPMOST | MB_SETFOREGROUND) == IDYES;
            if (now) manager.ApplyUpdatesAndRestart(target);
            else manager.WaitExitThenApplyUpdates(target, silent: true, restart: false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] {ex.GetType().Name}: {ex.Message}");
        }
    }

    private const uint MB_YESNO = 0x4, MB_ICONQUESTION = 0x20, MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000;
    private const int IDYES = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}

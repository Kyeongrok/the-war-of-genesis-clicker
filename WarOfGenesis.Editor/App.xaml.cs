using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace WarOfGenesis.Editor;

public partial class App : Application
{
    /// <summary>오류 기록 파일 — 편집기가 처리 못 한 오류를 여기 남긴다.</summary>
    internal static string CrashLogPath => Path.Combine(Path.GetTempPath(), "wog_editor_crash.log");

    /// <summary>
    /// 화면 쪽에서 난 처리 못 한 오류로 편집기가 통째로 꺼지지 않게 한다 — 무슨 오류인지 알리고 기록한 뒤 계속 돈다.
    /// 전에는 표 칸을 고치다 난 오류 하나에 고치던 것을 저장도 못 하고 닫혔다(사용자 보고).
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        // 따로 받은 편집기에는 원본 게임의 자료가 없다 — 원본 게임 폴더를 이미 알면 거기서 차려 둔다(모르면 첫 화면에서 폴더를 열 때).
        try { if (WarOfGenesis.Assets.OriginalAssets.NeedsGame && WarOfGenesis.Assets.OriginalGame.Root is { } root) WarOfGenesis.Assets.OriginalAssets.Prepare(root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 못 차려도 편집기는 뜬다 — 자료를 읽는 곳에서 알린다 */ }
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try { File.AppendAllText(CrashLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 기록을 못 해도 알림은 띄운다 */ }
        MessageBox.Show($"오류가 났습니다. 편집기는 계속 쓸 수 있지만, 고치던 것을 먼저 저장해 두세요.\n\n{e.Exception.GetType().Name}: {e.Exception.Message}\n\n기록: {CrashLogPath}",
                        "편집기 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}

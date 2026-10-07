using System.Windows.Forms;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 켤 때 사용자의 <b>원본 게임 폴더</b>를 정하고 거기서 자료를 차린다 — 배포판에는 원본 게임의 자료가 없다(<see cref="OriginalAssets"/>).
/// </summary>
/// <remarks>
/// 폴더를 아직 모르거나 적어 둔 자리가 없어졌으면 폴더 고르기 창을 띄운다. 처음 한 번은 자료를 꺼내느라 진행 창이 뜨고, 다음부터는 금방 지나간다.
/// 저장소에서 돌릴 때(개발)는 <c>assets</c> 에 다 있어 아무것도 묻지 않는다. 화면 밖 시험에서는 창을 띄우지 않고 그만둔다.
/// </remarks>
internal static class OriginalGameSetup
{
    private const string Caption = "창세기전3 파트2";

    /// <summary>자료를 쓸 수 있게 됐으면 true — 사용자가 그만두면 false.</summary>
    public static bool Ensure()
    {
        if (!OriginalAssets.NeedsGame) return true;
        bool offscreen = Environment.GetEnvironmentVariable("DUELDX_OFFSCREEN") == "1";

        string? root = OriginalGame.Root;
        if (root == null)
        {
            if (offscreen) throw new DirectoryNotFoundException("원본 게임 폴더를 모릅니다 — WAROFGENESIS_ORIGINAL 로 알려 주세요.");
            Application.EnableVisualStyles();
            MessageBox.Show("이 게임은 원본 「창세기전3 파트2」의 그림·소리·자료를 그대로 읽습니다.\n\n"
                            + "원본 게임이 설치된 폴더(TXR · Chr · Obs 폴더가 든 곳)를 골라 주세요.\n원본 폴더에는 아무것도 쓰지 않습니다.",
                            Caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
            while (root == null)
            {
                using var dialog = new FolderBrowserDialog { Description = "원본 창세기전3 파트2 폴더 (TXR · Chr · Obs 가 든 곳)", UseDescriptionForTitle = true };
                if (dialog.ShowDialog() != DialogResult.OK) return false;
                if (OriginalGame.IsValid(dialog.SelectedPath)) root = dialog.SelectedPath;
                else if (MessageBox.Show($"'{dialog.SelectedPath}' 에서 TXR\\Txr.dat · Chr · Obs 를 못 찾았습니다.\n다른 폴더를 고르시겠습니까?",
                                         Caption, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return false;
            }
        }

        int done = 0, total = 0;
        var work = Task.Run(() => OriginalAssets.Prepare(root, (d, t) => { done = d; total = t; }));
        // 이미 차려 둔 뒤라면 금방 끝난다 — 그때는 창을 띄우지 않는다.
        if (!offscreen && !Wait(work, 400)) ShowProgress(work, () => (done, total));
        try
        {
            int missing = work.GetAwaiter().GetResult();
            if (missing > 0 && !offscreen)
                MessageBox.Show($"원본 게임 폴더에서 파일 {missing}개를 못 찾았습니다 — 그 그림·소리는 빠진 채로 켭니다.\n{root}", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (offscreen) throw;
            MessageBox.Show($"원본 게임 폴더에서 자료를 꺼내지 못했습니다.\n\n{ex.Message}", Caption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private static bool Wait(Task work, int milliseconds)
    {
        try { return work.Wait(milliseconds); }
        catch (AggregateException) { return true; }      // 결과를 받을 때 다시 던져진다
    }

    private static void ShowProgress(Task work, Func<(int Done, int Total)> state)
    {
        Application.EnableVisualStyles();
        using var form = new Form
        {
            Text = Caption, ClientSize = new System.Drawing.Size(420, 84), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false, ControlBox = false,
        };
        var label = new Label { Left = 16, Top = 14, Width = 388, Text = "원본 게임 폴더에서 자료를 꺼내는 중…" };
        var bar = new ProgressBar { Left = 16, Top = 44, Width = 388, Height = 20 };
        form.Controls.Add(label);
        form.Controls.Add(bar);
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            var (done, total) = state();
            if (total > 0)
            {
                bar.Maximum = total;
                bar.Value = Math.Min(done, total);
                label.Text = $"원본 게임 폴더에서 자료를 꺼내는 중… {done}/{total}";
            }
            if (work.IsCompleted) form.Close();
        };
        timer.Start();
        form.ShowDialog();
    }
}

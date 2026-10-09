using System.Windows.Forms;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 켤 때 자료를 어디서 가져올지 정하고 차린다 — 배포판에는 원본 게임의 자료가 없다(<see cref="OriginalAssets"/>).
/// </summary>
/// <remarks>
/// 사용자의 <b>원본 게임 폴더</b>를 아직 모르면 묻는다 — 폴더를 고르거나, 「모른다」를 누르면 자료를 인터넷에서 받는다(<see cref="AssetDownload"/>,
/// 사용자 요청 menu-22). 고른 것은 적어 두어 다음부터는 안 묻는다. 처음 한 번은 자료를 차리느라 진행 창이 뜨고, 다음부터는 금방 지나간다.
/// 저장소에서 돌릴 때(개발)는 <c>assets</c> 에 다 있어 아무것도 묻지 않는다. 화면 밖 시험에서는 창을 띄우지 않는다 —
/// <c>WAROFGENESIS_ORIGINAL</c> 로 폴더를 알려 주거나 <c>DUELDX_PACK=1</c> 로 받는 길을 고른다.
/// </remarks>
internal static class OriginalGameSetup
{
    private const string Caption = "창세기전3 파트2";

    /// <summary>자료를 쓸 수 있게 됐으면 true — 사용자가 그만두면 false.</summary>
    public static bool Ensure()
    {
        if (!OriginalAssets.NeedsGame) return true;
        bool offscreen = Environment.GetEnvironmentVariable("DUELDX_OFFSCREEN") == "1";

        // DUELDX_PACK=1 은 원본 폴더를 알아도 받는 길로 간다(시험).
        bool forced = Environment.GetEnvironmentVariable("DUELDX_PACK") == "1";
        string? root = forced ? null : OriginalGame.Root;
        bool download = forced || (root == null && AssetDownload.Chosen);
        if (root == null && !download)
        {
            if (offscreen) throw new DirectoryNotFoundException("원본 게임 폴더를 모릅니다 — WAROFGENESIS_ORIGINAL 로 알려 주거나 DUELDX_PACK=1 로 받게 하세요.");
            Application.EnableVisualStyles();
            while (root == null && !download)
            {
                switch (AskSource())
                {
                    case DialogResult.Yes:
                        using (var dialog = new FolderBrowserDialog { Description = "원본 창세기전3 파트2 폴더 (TXR · Chr · Obs 가 든 곳)", UseDescriptionForTitle = true })
                        {
                            if (dialog.ShowDialog() != DialogResult.OK) break;
                            if (OriginalGame.IsValid(dialog.SelectedPath)) root = dialog.SelectedPath;
                            else MessageBox.Show($"'{dialog.SelectedPath}' 에서 TXR\\Txr.dat · Chr · Obs 를 못 찾았습니다.", Caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                        break;
                    case DialogResult.No:
                        download = true;
                        break;
                    default:
                        return false;
                }
            }
        }
        return root != null ? FromGameFolder(root, offscreen) : FromDownload(offscreen);
    }

    /// <summary>자료를 어디서 가져올지 묻는다 — Yes 폴더 고르기 · No 모른다(받기) · Cancel 끝내기.</summary>
    private static DialogResult AskSource()
    {
        using var form = new Form
        {
            Text = Caption, ClientSize = new System.Drawing.Size(460, 212), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false,
        };
        form.Controls.Add(new Label
        {
            Left = 16, Top = 14, Width = 428, Height = 136,
            Text = "이 게임은 원본 「창세기전3 파트2」의 그림·소리·자료를 씁니다.\n\n"
                   + "원본 게임이 설치된 폴더(TXR · Chr · Obs 폴더가 든 곳)를 아시면 골라 주세요. 원본 폴더에는 아무것도 쓰지 않습니다.\n\n"
                   + "어디인지 모르면 「모른다」를 눌러 주세요 — 자료를 인터넷에서 받습니다(게임을 하는 동안 뒤에서 이어 받습니다).",
        });
        var pick = new Button { Text = "폴더 고르기", Left = 16, Top = 164, Width = 140, Height = 32, DialogResult = DialogResult.Yes };
        var unknown = new Button { Text = "모른다 (내려받기)", Left = 164, Top = 164, Width = 160, Height = 32, DialogResult = DialogResult.No };
        var quit = new Button { Text = "끝내기", Left = 332, Top = 164, Width = 112, Height = 32, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange([pick, unknown, quit]);
        form.AcceptButton = pick;
        form.CancelButton = quit;
        return form.ShowDialog();
    }

    private static bool FromGameFolder(string root, bool offscreen)
    {
        int done = 0, total = 0;
        var work = Task.Run(() => OriginalAssets.Prepare(root, (d, t) => { done = d; total = t; }));
        // 이미 차려 둔 뒤라면 금방 끝난다 — 그때는 창을 띄우지 않는다.
        if (!offscreen && !Wait(work, 400))
            ShowProgress(work, () => total > 0 ? (done, total, $"원본 게임 폴더에서 자료를 꺼내는 중… {done}/{total}") : (0, 0, "원본 게임 폴더에서 자료를 꺼내는 중…"));
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

    /// <summary>바탕 자료를 받아 차린다 — 못 받으면 다시 해 볼지 묻는다. 나머지는 게임을 하는 동안 뒤에서 받는다.</summary>
    private static bool FromDownload(bool offscreen)
    {
        while (true)
        {
            long got = 0, total = 0;
            var work = Task.Run(() => AssetDownload.PrepareBase((g, t) => { got = g; total = t; }));
            if (!offscreen && !Wait(work, 400))
                ShowProgress(work, () => total > 0 ? ((int)(got >> 10), (int)(total >> 10), $"게임 자료를 받는 중… {got >> 20}/{total >> 20}MB") : (0, 0, "게임 자료를 받는 중…"));
            try
            {
                work.GetAwaiter().GetResult();
                AssetDownload.Choose();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException
                                           or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException)
            {
                if (offscreen) throw;
                if (MessageBox.Show($"게임 자료를 받지 못했습니다 — 인터넷 연결을 확인해 주세요.\n\n{ex.Message}", Caption, MessageBoxButtons.RetryCancel, MessageBoxIcon.Error) != DialogResult.Retry)
                    return false;
            }
        }
    }

    private static bool Wait(Task work, int milliseconds)
    {
        try { return work.Wait(milliseconds); }
        catch (AggregateException) { return true; }      // 결과를 받을 때 다시 던져진다
    }

    private static void ShowProgress(Task work, Func<(int Done, int Total, string Text)> state)
    {
        Application.EnableVisualStyles();
        using var form = new Form
        {
            Text = Caption, ClientSize = new System.Drawing.Size(420, 84), FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen, MaximizeBox = false, MinimizeBox = false, ControlBox = false,
        };
        var label = new Label { Left = 16, Top = 14, Width = 388, Text = state().Text };
        var bar = new ProgressBar { Left = 16, Top = 44, Width = 388, Height = 20 };
        form.Controls.Add(label);
        form.Controls.Add(bar);
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            var (done, total, text) = state();
            if (total > 0)
            {
                bar.Maximum = total;
                bar.Value = Math.Clamp(done, 0, total);
            }
            label.Text = text;
            if (work.IsCompleted) form.Close();
        };
        timer.Start();
        form.ShowDialog();
    }
}

using System.Globalization;
using System.Windows.Forms;

namespace DuelDx;

/// <summary>
/// 내 리포트 — 버그 리포트 창의 「내 리포트」 단추가 띄운다(사용자 요청). 이 설치가 보낸 리포트마다 번호 · 상태(접수 · 확인함 · 고침 · 고치지 않음) ·
/// 보낸 때 · 판 · 글 앞부분 · 받은 답을 보인다(<see cref="BugReport.LoadMine"/>). 답은 통계 대시보드 버그 리포트 카드의 「답 달기」로 단다.
/// </summary>
/// <remarks>
/// 설치 번호는 <c>%APPDATA%\DuelDx\install-id.txt</c> 라 업데이트해도 그대로 보이고, PC 를 옮기면 예전 것은 안 보인다.
/// 칸은 버그 리포트 창과 같은 꼴(글꼴은 창 DPI 의 점, 폭 · 글 칸 높이만 DPI 로 키움)이다.
/// 화면 밖 시험: <c>DUELDX_MYBUGS_SHOT=&lt;png&gt;</c> 면 창을 띄우지 않고 그려서 그 PNG 로 남긴다(<see cref="ShotIfAsked"/>).
/// </remarks>
internal static class MyBugReportsDialog
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private static readonly Color ReplyInk = Color.FromArgb(0x2A, 0x5F, 0xD0);

    /// <summary>받는 곳의 상태 이름을 보낸 분이 읽는 말로.</summary>
    internal static string StatusName(string status) => status switch
    {
        "new" => "접수",
        "seen" => "확인함",
        "fixed" => "고침",
        "wontfix" => "고치지 않음",
        _ => status,
    };

    /// <summary>창을 만든다 — 목록은 뜬 뒤에 받아 <see cref="Fill"/> 로 채운다.</summary>
    private static Form Build(float k, out RichTextBox list, out Label status)
    {
        int S(int v) => (int)Math.Round(v * k);
        var font = new Font("Malgun Gothic", 12f * k, GraphicsUnit.Pixel);
        int width = S(540);
        var form = new Form
        {
            Text = "내 리포트", FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            Font = font, AutoScaleMode = AutoScaleMode.None,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(S(12)),
        };
        form.Disposed += (_, _) => font.Dispose();
        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        var head = new Label
        {
            AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, S(6)),
            Text = "이 PC 에서 보내신 버그 리포트입니다. 고치거나 답을 달면 여기에 보입니다.",
        };
        list = new RichTextBox
        {
            Width = width, Height = S(360), ReadOnly = true, TabStop = false, Margin = new Padding(0),
            ScrollBars = RichTextBoxScrollBars.Vertical, BackColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle, DetectUrls = false,
            Text = "불러오는 중입니다…",
        };
        status = new Label { AutoSize = true, ForeColor = Color.DimGray, Anchor = AnchorStyles.Left };
        var close = new Button { Text = "닫기", AutoSize = true, MinimumSize = new Size(S(96), 0), DialogResult = DialogResult.Cancel };
        var bottom = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Width = width, Margin = new Padding(0, S(10), 0, 0), Dock = DockStyle.Fill };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(status, 0, 0);
        bottom.Controls.Add(close, 1, 0);
        layout.Controls.AddRange([head, list, bottom]);
        form.Controls.Add(layout);
        form.CancelButton = close;
        return form;
    }

    /// <summary>받은 목록을 글 칸에 적는다 — 리포트마다 굵은 머리 줄, 글, 답(파란 글).</summary>
    private static void Fill(RichTextBox list, Label status, List<BugReport.Mine>? mine, int queued)
    {
        string waiting = queued > 0 ? $"아직 못 보낸 것 {queued}건" : "";
        list.Clear();
        if (mine == null)
        {
            list.Text = "불러오지 못했습니다. 인터넷 연결을 확인해 주세요.";
            status.Text = waiting;
            return;
        }
        status.Text = $"{mine.Count}건" + (waiting.Length > 0 ? " · " + waiting : "");
        if (mine.Count == 0) { list.Text = "아직 보내신 리포트가 없습니다."; return; }
        var bold = new Font(list.Font, FontStyle.Bold);
        list.Disposed += (_, _) => bold.Dispose();
        void Add(string text, Color ink, Font? font = null)
        {
            list.SelectionStart = list.TextLength;
            list.SelectionLength = 0;
            list.SelectionColor = ink;
            list.SelectionFont = font ?? list.Font;
            list.AppendText(text);
        }
        foreach (var b in mine)
        {
            string when = DateTime.TryParse(b.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
                ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : b.CreatedAt;
            Add($"#{b.Id}  ", Color.Black, bold);
            Add(StatusName(b.Status), b.Status == "fixed" ? ReplyInk : Color.Black, bold);
            Add($"  ·  {when}" + (b.Version.Length > 0 ? $"  ·  v{b.Version}" : "") + Environment.NewLine, Color.DimGray);
            Add((b.Text.Length >= 300 ? b.Text + "…" : b.Text) + Environment.NewLine, Color.Black);
            if (b.Reply is { } reply) Add("답: " + reply + Environment.NewLine, ReplyInk, bold);
            Add(Environment.NewLine, Color.Black);
        }
        list.SelectionStart = 0;
        list.ScrollToCaret();
    }

    /// <summary>창을 띄우고 뒤에서 목록을 받아 채운다.</summary>
    internal static void Show(IWin32Window owner)
    {
        float k = Math.Max(1f, GetDpiForWindow(owner.Handle) / 96f);
        using var form = Build(k, out var list, out var status);
        form.Shown += async (_, _) =>
        {
            var mine = await BugReport.LoadMine();
            if (!form.IsDisposed) Fill(list, status, mine, BugReport.QueuedCount());
        };
        form.ShowDialog(owner);
    }

    /// <summary>
    /// 화면 밖 시험 — <c>DUELDX_MYBUGS_SHOT=&lt;png&gt;</c> 면 창을 띄우지 않고 목록을 받아 채운 그림을 그 PNG 로 남긴다(받는 곳은 DUELDX_STATS_URL).
    /// 했으면 true.
    /// </summary>
    internal static bool ShotIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_MYBUGS_SHOT") is not { Length: > 0 } png) return false;
        // 받기를 창보다 먼저 — 창을 만들면 WinForms 동기화 문맥이 서서 여기서 기다리면 멈춘다.
        var mine = Task.Run(BugReport.LoadMine).GetAwaiter().GetResult();
        using var form = Build(1f, out var list, out var status);
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-8000, -8000);
        form.ShowInTaskbar = false;
        form.Show();
        Fill(list, status, mine, BugReport.QueuedCount());
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
        bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        form.Close();
        return true;
    }
}

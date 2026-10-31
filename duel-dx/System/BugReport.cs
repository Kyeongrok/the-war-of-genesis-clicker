using System.Net.Http;
using System.Text;
using System.Text.Json;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 버그 리포트(사용자 요청 menu-118) — 타이틀 · 파트1 타이틀 · 시스템 메뉴의 「버그 리포트」가 글 쓰는 창을 띄우고, 쓴 글을
/// 전투 통계를 받는 곳(<c>tools/stats-worker</c> 의 <c>POST /v1/bug</c>)으로 보낸다.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>창은 WinForms 의 여러 줄 글 칸이다 — 한글 IME · 붙여 넣기 · 여러 줄이 그대로 된다. 게임 화면 위에 게임 창을 주인으로 뜬다(파트1 폴더 묻기 창과 같은 길).</item>
/// <item>게임이 저절로 붙이는 것(<see cref="Context"/>): 판 번호 · OS · 기기 갈래 · 지금 화면(장면 꼬리표) · 전투 · 챕터 · 난이도 · 켠 모드 · 오류 기록의 끝.
/// 창 아래에 그 요약을 보인다. 전체 경로 · 사용자 이름은 싣지 않는다(<see cref="Scrub"/>).</item>
/// <item>사용자가 「보내기」를 눌렀으니 「익명 전투 통계 보내기」가 꺼져 있어도 보낸다. 못 보내면 줄(<see cref="QueueFolder"/>)에 두었다가 다음에 켤 때 다시 보낸다(<see cref="BattleStats"/> 와 같은 꼴).</item>
/// <item>화면 밖 시험(<c>DUELDX_OFFSCREEN=1</c>)에서는 창을 안 띄운다 — <c>DUELDX_BUGREPORT_TEXT</c> 에 글이 있으면 그 글을 바로 보낸다(시험 문).
/// 시험 실행에서는 <c>DUELDX_STATS_URL</c>(로컬 <c>wrangler dev</c> 주소도 된다)을 줄 때만 실제로 보낸다.</item>
/// </list>
/// </remarks>
internal sealed class BugReport(GameWindow host)
{
    internal const int MaxText = 8000;
    private const int LogTailBytes = 20 * 1024, LogTailLines = 200;

    internal static string QueueFolder => UserDataFolder.File("bug-queue");

    /// <summary>원본 오류 기록 — 게임이 죽을 때 <c>Program.ReportCrash</c> 가 적는 곳.</summary>
    internal static string CrashLogPath => Path.Combine(Path.GetTempPath(), "dueldx_crash.log");

    /// <summary>
    /// 받는 곳 — <c>DUELDX_STATS_URL</c> 이 있으면 그것(시험용, http://127.0.0.1 · localhost 도 된다), 없으면 <see cref="BattleStats.UploadUrl"/>.
    /// 화면 밖 시험 · 시험 실행은 <c>DUELDX_STATS_URL</c> 을 줄 때만 보낸다(시험이 진짜 받는 곳에 글을 남기지 않게).
    /// </summary>
    internal static string Url
    {
        get
        {
            string over = Environment.GetEnvironmentVariable("DUELDX_STATS_URL")?.Trim() ?? "";
            if (over.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || over.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase) || over.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase))
                return over;
            return GameWindow.Offscreen || GameWindow.TestRun ? "" : BattleStats.UploadUrl;
        }
    }

    /// <summary>메뉴의 「버그 리포트」 — 글 쓰는 창을 띄우고, 보내기를 누르면 보낸다.</summary>
    internal void Open()
    {
        var context = Context();
        if (GameWindow.Offscreen)
        {
            // 화면 밖 시험 — 창을 안 띄운다. 시험 문의 글이 있으면 그 글로 보내는 길을 탄다.
            if (Environment.GetEnvironmentVariable("DUELDX_BUGREPORT_TEXT") is { Length: > 0 } text) Submit(text, context);
            else host.Toast("화면 밖 시험에서는 버그 리포트 창을 띄우지 않습니다");
            return;
        }
        var owner = new System.Windows.Forms.NativeWindow();
        owner.AssignHandle(host._hwnd);
        try
        {
            if (BugReportDialog.Ask(owner, Summary(context)) is { } written) Submit(written, context);
        }
        finally { owner.ReleaseHandle(); }
    }

    /// <summary>글 하나를 줄에 세우고 뒤에서 보낸다 — 결과는 토스트로 알린다.</summary>
    internal void Submit(string text, Dictionary<string, object?> context)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (text.Length > MaxText) text = text[..MaxText];
        try
        {
            Directory.CreateDirectory(QueueFolder);
            var body = new { v = 1, install = BattleStats.InstallId(), version = GameWindow.AppVersion.TrimStart('v'), text, context };
            // 이름은 적은 때 — 오래된 것부터 보낸다.
            string name = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json";
            File.WriteAllText(Path.Combine(QueueFolder, name), JsonSerializer.Serialize(body));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            host.Toast("버그 리포트를 적어 두지 못했습니다");
            return;
        }
        UploadInBackground(announce: true);
    }

    private static readonly object Gate = new();
    private static bool _uploading, _again, _announce;

    /// <summary>
    /// 줄에 선 버그 리포트를 뒤에서 보낸다 — 켤 때와 보내기를 누른 뒤. 받은 것 · 받는 쪽이 못 쓴다고 한 것(4xx, 429 빼고)은 지우고,
    /// 그 밖의 실패는 남겨 다음에 다시. <paramref name="announce"/> 면 「보냈습니다」 · 「나중에 다시 보냅니다」를 알린다.
    /// 이미 보내는 중이면 그 일이 끝난 뒤 줄을 한 번 더 훑는다(방금 세운 글도 같이 간다).
    /// </summary>
    internal void UploadInBackground(bool announce = false)
    {
        string url = Url;
        if (url.Length == 0 || !Directory.Exists(QueueFolder))
        {
            if (announce) host.Toast("나중에 다시 보냅니다");
            return;
        }
        lock (Gate)
        {
            _announce |= announce;
            if (_uploading) { _again = true; return; }
            _uploading = true;
        }
        _ = Task.Run(async () =>
        {
            while (true)
            {
                bool sent = await UploadQueue(url);
                bool tell;
                lock (Gate)
                {
                    if (_again) { _again = false; continue; }
                    tell = _announce;
                    _announce = _uploading = false;
                }
                if (tell) host.Toast(sent ? "보냈습니다 — 고맙습니다" : "나중에 다시 보냅니다");
                return;
            }
        });
    }

    /// <summary>줄을 한 번 훑어 보낸다 — 남은 것 없이 다 보냈으면 true.</summary>
    private static async Task<bool> UploadQueue(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var files = new DirectoryInfo(QueueFolder).GetFiles("*.json").OrderBy(f => f.Name).ToList();
            foreach (var old in files.SkipLast(30)) old.Delete();   // 오래 못 보낸 것이 한없이 쌓이지 않게 새 것 30 개만 둔다
            bool all = true;
            foreach (var file in files.TakeLast(30))
            {
                using var content = new StringContent(await File.ReadAllTextAsync(file.FullName), Encoding.UTF8, "application/json");
                using var reply = await http.PostAsync(url.TrimEnd('/') + "/v1/bug", content);
                int code = (int)reply.StatusCode;
                // 받는 쪽이 「못 쓸 글」이라 하면(4xx) 다시 보내도 같다 — 지운다. 너무 잦다(429) · 받는 쪽 고장(5xx)은 다음에 다시.
                if (reply.IsSuccessStatusCode || code is >= 400 and < 500 and not 429) file.Delete();
                if (code is 429 or >= 500) { all = false; break; }
            }
            return all;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Bug] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>게임이 저절로 붙이는 것 — 받는 곳의 <c>bug_reports.context</c> 에 JSON 으로 들어간다.</summary>
    internal Dictionary<string, object?> Context()
    {
        bool battle = host._battleLoaded && !host.TitleScr._titleOpen;
        var c = new Dictionary<string, object?>
        {
            ["game"] = GameWindow.AppVersion,
            ["os"] = $"{Environment.OSVersion.VersionString}{(Environment.Is64BitOperatingSystem ? " x64" : "")}",
            ["platform"] = PlatformInfo.Current.Kind,
            ["screen"] = PlatformInfo.Current.Screen,
            ["where"] = host.SceneTagText() ?? (host.Part1TitleScr._open ? "P1 타이틀" : host.TitleScr._titleOpen ? "타이틀" : "?"),
            ["part"] = host.Part1TitleScr._open || battle && host._scene.IsPart1 ? 1 : 2,
            ["battle"] = battle ? host._scene.Id : null,
            ["battleTitle"] = battle ? host._scene.Title : null,
            ["turn"] = battle ? host.Btl._turnNo : null,
            ["field"] = host.Fld._field?.Id,
            ["chapter"] = host.Mos._mosesChp?.Id,
            ["part1Chapter"] = host.Part1.ChapterId >= 0 ? host.Part1.ChapterId : null,
            ["difficulty"] = host.Btl._difficulty,
            ["mods"] = host.TuningScr.EnabledMods(),
            ["res"] = $"{host._viewW}x{host._viewH}",
            ["fullscreen"] = host._fullscreen,
            ["hiResUi"] = host._hiResUi,
            ["part1Only"] = OriginalGameSetup.Part1Only,
            ["part2Dir"] = BattleStats.DirName(OriginalGame.Root),
            ["part1Dir"] = BattleStats.DirName(Part1Game.Root),
        };
        if (host._loadError.Length > 0) c["loadError"] = Scrub(host._loadError);
        if (CrashLogTail() is { } crash) { c["crashLogAt"] = crash.At; c["crashLog"] = crash.Tail; }
        return c;
    }

    /// <summary>오류 기록(<see cref="CrashLogPath"/>)의 끝 — 마지막 200 줄 · 20KB 까지. 없으면 null.</summary>
    internal static (string At, string Tail)? CrashLogTail()
    {
        try
        {
            var info = new FileInfo(CrashLogPath);
            if (!info.Exists || info.Length == 0) return null;
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long start = Math.Max(0, stream.Length - LogTailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string text = reader.ReadToEnd();
            if (start > 0 && text.IndexOf('\n') is var nl and >= 0) text = text[(nl + 1)..];   // 잘린 첫 줄은 버린다
            var lines = text.Split('\n');
            if (lines.Length > LogTailLines) text = string.Join('\n', lines[^LogTailLines..]);
            text = Scrub(text);
            if (Encoding.UTF8.GetByteCount(text) > LogTailBytes) text = text[^(LogTailBytes / 3)..];   // 경로를 바꿔 늘어난 만큼
            return (info.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"), text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>개인 정보를 지운다 — 사용자 폴더 · 임시 폴더 · 저장 폴더 경로와 사용자 이름을 자리 이름으로 바꾼다.</summary>
    internal static string Scrub(string text)
    {
        static string Trim(string p) => Path.TrimEndingDirectorySeparator(p);
        foreach (var (path, label) in new[]
                 {
                     (Trim(UserDataFolder.Path), "%DUELDX%"), (Trim(Path.GetTempPath()), "%TEMP%"),
                     (Trim(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "%USERPROFILE%"),
                 })
            if (path.Length > 3) text = text.Replace(path, label, StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        if (user.Length >= 3) text = text.Replace(user, "<user>", StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>창 아래에 보이는 요약 — 무엇을 같이 보내는지.</summary>
    internal static string Summary(Dictionary<string, object?> c)
    {
        var lines = new List<string>
        {
            $"게임 {c["game"]} · {c["os"]} · {c["platform"]} · 화면 {c["res"]}{(c["fullscreen"] is true ? " 전체화면" : "")}",
            $"지금 자리: {c["where"]} · 파트{c["part"]} · 난이도 {c["difficulty"]}",
        };
        if (c["mods"] is List<string> { Count: > 0 } mods) lines.Add("켠 모드: " + string.Join(", ", mods));
        if (c.TryGetValue("loadError", out var err)) lines.Add($"읽기 오류: {err}");
        if (c.TryGetValue("crashLogAt", out var at)) lines.Add($"오류 기록(dueldx_crash.log)의 끝 — {at} 에 적힌 것");
        lines.Add("이름 · 세이브 · 폴더 경로는 보내지 않습니다. 설치마다 하나인 무작위 번호만 같이 갑니다.");
        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>버그 리포트 글 쓰는 창 — 여러 줄 글 칸 · 같이 보내는 것 요약 · 보내기 / 취소.</summary>
internal static class BugReportDialog
{
    /// <summary>창을 띄워 글을 받는다 — 취소하거나 비워 두면 null.</summary>
    internal static string? Ask(System.Windows.Forms.IWin32Window owner, string summary)
    {
        using var font = new Font("Malgun Gothic", 9.5f);
        using var form = new System.Windows.Forms.Form
        {
            Text = "버그 리포트", ClientSize = new Size(560, 470), FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            Font = font, AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi, AutoScaleDimensions = new SizeF(96, 96),
        };
        var head = new System.Windows.Forms.Label
        {
            Left = 12, Top = 10, Width = 536, Height = 38,
            Text = "어떤 일이 있었는지 적어 주세요 — 무엇을 하다가(어느 전투 · 어떤 기술), 무엇이 보였고, 원래는 어땠어야 하는지.",
        };
        var box = new System.Windows.Forms.TextBox
        {
            Left = 12, Top = 50, Width = 536, Height = 230, Multiline = true, AcceptsReturn = true, AcceptsTab = false,
            ScrollBars = System.Windows.Forms.ScrollBars.Vertical, MaxLength = BugReport.MaxText, WordWrap = true,
        };
        var count = new System.Windows.Forms.Label { Left = 12, Top = 284, Width = 536, Height = 18, Text = $"0 / {BugReport.MaxText}", ForeColor = Color.DimGray };
        var info = new System.Windows.Forms.TextBox
        {
            Left = 12, Top = 306, Width = 536, Height = 110, Multiline = true, ReadOnly = true, TabStop = false,
            ScrollBars = System.Windows.Forms.ScrollBars.Vertical, Text = "같이 보내는 것:" + Environment.NewLine + summary,
            BackColor = System.Drawing.SystemColors.Control, ForeColor = Color.DimGray,
        };
        var send = new System.Windows.Forms.Button { Text = "보내기", Left = 340, Top = 426, Width = 100, Height = 32, Enabled = false, DialogResult = System.Windows.Forms.DialogResult.OK };
        var cancel = new System.Windows.Forms.Button { Text = "취소", Left = 448, Top = 426, Width = 100, Height = 32, DialogResult = System.Windows.Forms.DialogResult.Cancel };
        var hint = new System.Windows.Forms.Label { Left = 12, Top = 434, Width = 320, Height = 18, Text = "Ctrl+Enter 로도 보냅니다", ForeColor = Color.DimGray };
        box.TextChanged += (_, _) =>
        {
            count.Text = $"{box.TextLength} / {BugReport.MaxText}";
            send.Enabled = box.Text.Trim().Length > 0;
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == System.Windows.Forms.Keys.Enter && send.Enabled) { e.SuppressKeyPress = true; form.DialogResult = System.Windows.Forms.DialogResult.OK; }
        };
        form.Controls.AddRange([head, box, count, info, hint, send, cancel]);
        form.CancelButton = cancel;
        form.Shown += (_, _) => box.Focus();
        if (form.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return null;
        string text = box.Text.Trim();
        return text.Length > 0 ? text : null;
    }
}

using System.Text;
using DuelDx.Native;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 릴리즈 노트(<c>릴리즈노트.md</c>) — 업데이트한 뒤 처음 켤 때 그 사이 판들의 대목을 보여 준다(사용자 요청 menu-9, 대항해시대 cds-helper 와 같은 방식).
/// </summary>
/// <remarks>
/// 파일은 저장소 뿌리에 있고 exe 옆에 같이 놓인다. 판마다 <c>## v0.21.0</c> 줄로 대목을 열고 그 아래에 바뀐 것을 적는다.
/// 릴리즈 빌드(<c>release.yml</c>)도 같은 대목을 GitHub 릴리즈 본문으로 쓴다.
/// </remarks>
internal static class ReleaseNotes
{
    /// <summary>exe 옆에 놓이는 파일 이름.</summary>
    public const string FileName = "릴리즈노트.md";

    /// <summary>한 번에 보여 주는 판 수 — 여러 판을 건너뛰어도 새 것부터 이만큼만 낸다.</summary>
    private const int MaxSections = 3;

    /// <summary>exe 옆의 노트 글. 없거나 못 읽으면 빈 글이다.</summary>
    public static string Read()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, FileName);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>
    /// <paramref name="seen"/> 뒤부터 <paramref name="current"/> 까지의 대목을 새 판부터 이어 낸다. 보여 줄 것이 없으면 빈 글.
    /// </summary>
    /// <remarks>
    /// <b>지금 판의 대목이 노트에 있을 때만</b> 낸다 — 개발 중 빌드처럼 노트에 없는 판은 아무것도 안 띄운다.
    /// <paramref name="seen"/> 이 비었으면(처음 깔았거나 이 기능이 없던 판에서 올라왔으면) 지금 판 대목 하나만 낸다.
    /// </remarks>
    public static string Since(string text, string seen, Version current)
    {
        var sections = Parse(text);
        current = Trim(current);
        if (!sections.Exists(s => s.Version == current)) return "";

        Version? from = Version.TryParse(seen, out var parsed) ? Trim(parsed) : null;
        var shown = sections
            .Where(s => s.Version <= current && (from == null ? s.Version == current : s.Version > from))
            .OrderByDescending(s => s.Version)
            .Take(MaxSections);
        return string.Join("\n\n", shown.Select(s => $"v{s.Version}\n{s.Body}"));
    }

    /// <summary>판 번호를 세 자리로 맞춘다 — 어셈블리 판은 넷째 자리(0)가 붙어 온다.</summary>
    public static Version Trim(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    private static List<(Version Version, string Body)> Parse(string text)
    {
        var sections = new List<(Version, string)>();
        Version? version = null;
        var body = new StringBuilder();

        void Close()
        {
            if (version != null) sections.Add((version, body.ToString().Trim()));
            body.Clear();
        }

        bool comment = false;
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            // 파일 머리의 적는 법 안내(<!-- ... -->)는 건너뛴다.
            if (line.StartsWith("<!--")) comment = true;
            if (comment) { if (line.EndsWith("-->")) comment = false; continue; }

            if (line.StartsWith("## "))
            {
                Close();
                version = Version.TryParse(line[3..].Trim().TrimStart('v', 'V'), out var v) ? Trim(v) : null;
                continue;
            }
            if (version != null) body.Append(line).Append('\n');
        }
        Close();
        return sections;
    }
}

/// <summary>릴리즈 노트 창 — 켤 때 한 번 <see cref="OpenIfUpdated"/> 가 띄울지 정한다. 닫기 단추 · Esc · Enter 로 닫는다.</summary>
internal sealed unsafe class ReleaseNotesScreen(GameWindow host)
{
    internal const int W = 560, H = 400, LineH = 18, Rows = (H - 28 - 12 - 50) / LineH;

    /// <summary>띄울 글 — 비었으면 닫힌 것.</summary>
    internal string _text = "";
    internal int _top;
    private List<string>? _lines;

    internal bool Open => _text.Length > 0;

    /// <summary>마지막으로 노트를 보여 준 판(「0.21.0」 꼴)을 적어 두는 파일.</summary>
    internal static string SeenPath => UserDataFolder.File("notes-seen.txt");

    /// <summary>
    /// 업데이트한 뒤 처음 켰으면 노트를 띄운다 — 「업데이트됐다」는 것은 <b>켠 판이 마지막으로 노트를 보여 준 판과 다르다</b>로 안다.
    /// 노트에 지금 판 대목이 없으면(개발 중 빌드) 아무것도 안 한다. DUELDX_NOTES=&lt;판&gt; 이면 그 판인 셈 치고 띄운다(화면 밖 시험용, 본 판은 안 적는다).
    /// </summary>
    internal void OpenIfUpdated()
    {
        if (Version.TryParse(Environment.GetEnvironmentVariable("DUELDX_NOTES"), out var asked))
        {
            Show(ReleaseNotes.Since(ReleaseNotes.Read(), Environment.GetEnvironmentVariable("DUELDX_NOTES_SEEN") ?? "", asked));
            return;
        }
        if (typeof(GameWindow).Assembly.GetName().Version is not { } version) return;
        string current = ReleaseNotes.Trim(version).ToString();
        try
        {
            string seen = File.Exists(SeenPath) ? File.ReadAllText(SeenPath).Trim() : "";
            if (seen == current) return;
            string notes = ReleaseNotes.Since(ReleaseNotes.Read(), seen, version);
            if (notes.Length == 0) return;
            Directory.CreateDirectory(UserDataFolder.Path);
            File.WriteAllText(SeenPath, current);
            Show(notes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 못 읽거나 못 적으면 이번에는 안 띄운다 */ }
    }

    internal void Show(string notes) => (_text, _top, _lines) = (notes, 0, null);

    internal void Close() => (_text, _lines) = ("", null);

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    /// <summary>창 너비에 맞춰 줄을 바꾼 글 — 글자 너비를 재는 일이라 띄울 때 한 번만 한다.</summary>
    internal List<string> Lines() => _lines ??= [.. _text.Split('\n').SelectMany(line => line.Length == 0 ? [""] : host.Mos.WrapText(line, W - 40, 12f))];

    /// <summary>열려 있으면 클릭을 먹는다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!Open) return false;
        var (x, y) = Origin();
        if (bx >= x + W - 116 && bx < x + W - 16 && by >= y + H - 40 && by < y + H - 12) Close();
        return true;
    }

    internal void OnWheel(int delta) => _top = Math.Clamp(_top - delta, 0, Math.Max(0, Lines().Count - Rows));

    internal void OnKey(int key)
    {
        if (key is Win32.VK_ESCAPE or Win32.VK_RETURN) Close();
    }

    internal void Draw()
    {
        if (!Open) return;
        var (x, y) = Origin();
        host.FillRect(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.FillRect(x, y, W, H, StatusScreen.PanelBg);
        host.StrokeRect(x, y, W, H, StatusScreen.BoxLine);
        host.FillRect(x, y, W, 28, StatusScreen.HeadBg);
        host.DrawText("릴리즈 노트 — 이번 업데이트로 바뀐 것", x + 10, y + 6, White);

        var lines = Lines();
        for (int i = 0; i < Rows && _top + i < lines.Count; i++)
        {
            string line = lines[_top + i];
            // 판 번호 줄(「v0.21.0」)은 머리 글로 띄운다.
            bool head = line.Length > 1 && line[0] == 'v' && char.IsDigit(line[1]);
            host.DrawText(line, x + 20, y + 40 + i * LineH, head ? 0xFFFFE070 : White, head ? 14 : 12);
        }

        int fy = y + H - 40;
        host.FillRect(x + W - 116, fy, 100, 28, StatusScreen.HeadBg);
        host.DrawText("닫기", x + W - 80, fy + 6, White);
        host.DrawText(lines.Count > Rows ? "Esc · Enter: 닫기 · 휠: 더 보기" : "Esc · Enter: 닫기", x + 16, fy + 7, DimGray);
    }
}

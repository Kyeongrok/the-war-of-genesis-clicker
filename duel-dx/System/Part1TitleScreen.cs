using System.Drawing.Imaging;
using System.IO;
using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 타이틀 — 파트2 타이틀 왼쪽 아래의 「PART 1」 단추로 들어간다(파트1 을 파트2 엔진으로 옮기는 첫걸음).
/// </summary>
/// <remarks>
/// 원본 파트1 본체 <c>alpha.dll</c> 의 장면 표(<c>0x10005f60</c> → <c>[0x1011f938 + 4·장면]</c>)에서 타이틀은 장면 7(<c>0x100d5600</c>, 짓기 <c>0x100d5660</c>)이다.
/// 전에 타이틀로 알았던 <c>0x100d47e0</c>(<c>BGRs\0329</c> · 1월 1일이면 <c>0372</c>)은 장면 9 「불러오기」다 — 그래서 로고가 없었다.
/// <list type="bullet">
/// <item>배경 <c>BGRs\0015.bgr</c>(640×480 JPEG, 어두운 붉은 바탕 · 가운데 빛나는 눈 · 붉은 줄기) — 색을 바꾸지 않는다.</item>
/// <item>그림은 모두 (원본 좌표) 한 창의 자식 Obs 로, 만든 차례대로 그린다(<c>0x1009d110</c> 이 꼬리에 붙인다):
/// <c>Obs 0361</c> 모션 0 (320,260) 글자 흐름(143틱, 더하기) → <c>Obs 0360</c> 모션 0 (320,244) 로고 왼쪽 「THE WAR OF」 →
/// <c>Obs 0751</c> 모션 4 (320,240) 저작권 「©1995,1996,1999 Softmax」 → <c>Obs 0360</c> 모션 1 로고 오른쪽 「창세기전 3 / GENESIS III」 →
/// <c>Obs 0362</c> 모션 0 (420,180) 불티(101틱, 더하기) → <c>Obs 0390</c> 모션 0 (320,253) 불꽃 핵(108틱 되풀이, 더하기) →
/// 메뉴 단추 넷(<c>Obs 0751</c> 모션 10~13, 섞기 13·4 = 16/31 비침, 마우스를 올리면 모션 0~3 더하기) → 「v1.04」 글(5,10, <c>0x100a2570</c>).</item>
/// <item>차례(<c>0x100d5e60</c>): 처음엔 글자 흐름만. 그것이 끝나면 지우고 불티 · 핵을 틀고 로고 둘을 밝힌다(<c>vt+0x6c</c> — 섞기 13 세기 1→7, 한 틱에 하나).
/// 불티가 끝나면 저작권 · 단추 · 판 번호를 밝힌다. Esc(<c>0x100d5f80</c>)는 글자 흐름 · 불티를 지우고 모두 곧바로 밝힌다.</item>
/// <item>단추 맞는 칸(<c>0x1009f1b0</c>, (320,240) 기준): Start (−49,101)~(46,116) · Load (−51,124)~(48,139) · Tutorial (−33,147)~(31,162) · Exit (−15,170)~(16,184).
/// Load 는 세이브가 없으면, Tutorial 은 시디 1 이 없으면 누를 수 없는 그림(같은 모션)만 둔다. 누르면 효과음 <c>Snd 2034</c>.</item>
/// <item>단추 알림(<c>0x100d61f0</c>): 단추 2 = Tutorial → 상태 3(<c>0x100d5df2</c>) — 새 게임과 같은 준비(<c>0x100ad560</c> + <c>0x100ad4c0</c>: 깃발 비우기 ·
/// 무리 다시 채우기 · 돈 0)를 하고 장면 4(챕터 지도)를 챕터 80(<c>Chps\0080.cpr</c>, 무리 13 · 지도 136)으로 연다.</item>
/// <item>음악 <c>BGMs\0022</c> 되풀이(12월 25일이면 <c>0059</c>, <c>0x100d5c90</c>). 곡이 끝나면 오프닝으로 가는 길은 옮기지 않는다(사용자 결정).</item>
/// </list>
/// 리메이크: Start Game = NEW GAME · Load Game = LOAD GAME · Tutorial = TUTORIAL · Exit = EXIT 로 원본 줄을 그대로 쓴다.
/// 시디가 없으니 TUTORIAL 은 늘 누를 수 있다.
/// 원본에 없는 CONTINUE · PART 2 는 로고와 메뉴를 가리지 않게 오른쪽 아래에 작은 글 단추로 둔다.
/// 그림은 사용자의 파트1 폴더(<see cref="Part1Game.Root"/>)에서 읽는다.
/// </remarks>
internal sealed unsafe class Part1TitleScreen(GameWindow host)
{
    internal const int TitleBackground = 15, TitleBgm = 22, ChristmasBgm = 59, PressSound = 2034;
    internal const int IntroObs = 361, LogoObs = 360, SparkObs = 362, CoreObs = 390, MenuObs = 751;

    /// <summary>글자 흐름(Obs 0361 모션 0) · 불티(Obs 0362 모션 0)의 길이 — 원본 모션표 그대로.</summary>
    internal const int IntroTicks = 143, SparkTicks = 101;

    /// <summary>원본 타이틀 그림들의 기준점(640×480 화면) — 메뉴 · 저작권 · 단추 맞는 칸이 모두 여기서 잰다.</summary>
    private const int Cx = 320, Cy = 240;

    /// <summary>파트2 타이틀 위에 얹는 들어가기 단추(왼위·크기) — 640×480 화면 기준.</summary>
    internal static readonly (int X, int Y, int W, int H) EntryButton = (16, 440, 124, 26);

    /// <summary>
    /// 파트1 타이틀의 단추 — <c>Row</c> 는 원본 메뉴 그림 줄(0 Start · 1 Load · 3 Exit, −1 은 리메이크 글 단추), 맞는 칸은 640×480 화면 좌표.
    /// LOAD GAME 은 파트2 타이틀처럼 저장 칸을 골라 부른다(사용자 요청 — 파트1 세이브 칸은 따로, part1-battle-save-NN).
    /// </summary>
    internal static readonly (string Name, int Row, int X0, int Y0, int X1, int Y1)[] Buttons =
    [
        ("NEW GAME", 0, Cx - 49, Cy + 101, Cx + 46, Cy + 116),
        ("LOAD GAME", 1, Cx - 51, Cy + 124, Cx + 48, Cy + 139),
        ("CONTINUE", 2, Cx - 46, Cy + 147, Cx + 46, Cy + 162),   // 원본에 없는 줄 — LOAD GAME 아래(사용자 요청). 튜토리얼 · EXIT 는 한 줄씩 내린다
        ("PART 2", -1, 530, 450, 630, 468),
        ("EXIT", 4, Cx - 15, Cy + 193, Cx + 16, Cy + 207),
        ("TUTORIAL", 3, Cx - 33, Cy + 170, Cx + 31, Cy + 185),   // 원본 단추 2 — CONTINUE 아래, EXIT 위
    ];

    /// <summary>위·아래 키로 옮기는 차례 — 가운데 메뉴 줄 먼저, 그다음 오른쪽 아래 PART 2.</summary>
    private static readonly int[] KeyOrder = [0, 1, 2, 5, 4, 3];

    /// <summary>글 단추 크기 — 연대표 · 마을 · 일행 창이 <see cref="DrawButton"/> 과 함께 쓴다.</summary>
    internal const int ButtonW = 140, ButtonH = 24;

    internal const int ButtonNewGame = 0, ButtonLoad = 1, ButtonContinue = 2, ButtonPart2 = 3, ButtonExit = 4, ButtonTutorial = 5;

    /// <summary>튜토리얼 챕터 — 원본 상태 3(<c>0x100d5df2</c>)이 장면 4 에 넘기는 챕터 번호(0x50).</summary>
    internal const int TutorialChapter = 80;

    internal bool _open;
    internal bool _entryHover;

    /// <summary>파트1 챕터 지도 · 연대표 — 이 화면 위에 열려 타이틀 장면(TitleScreen 의 문)을 그대로 쓴다.</summary>
    internal Part1ChapterScreen Chapter => _chapter ??= new(host, this);

    /// <summary>파트1 지도가 맨 위에 떠 있나 — 일행 · 마을 · 필드 화면이 그 위에 없을 때(끌어서 밀기가 본다).</summary>
    internal bool ChapterOnTop => _open && _chapter is { _open: true } && _party is not { _open: true } && _town is not { _open: true } && _field is not { _open: true };
    internal Part1TimelineScreen Timeline => _timeline ??= new(host, this);
    internal Part1FieldScreen Field => _field ??= new(host, this);
    internal Part1TownScreen Town => _town ??= new(host, this);
    internal Part1PartyScreen Party => _party ??= new(host, this);
    private Part1PartyScreen? _party;
    private Part1TownScreen? _town;
    private Part1FieldScreen? _field;
    private Part1ChapterScreen? _chapter;
    private Part1TimelineScreen? _timeline;
    internal int _hover = -1;
    private uint[]? _bg;

    /// <summary>타이틀 그림 차례를 처음 그린 때(<c>_lastTime</c>) — 열고 아직 안 그렸으면 −1. 챕터 · 필드로 곧바로 가면 차례가 돌지 않는다.</summary>
    private double _shownAt = -1;

    /// <summary>Esc · 누름으로 글자 흐름 · 불티를 건너뛴 틱(그린 때부터) — 안 건너뛰었으면 −1.</summary>
    private double _skipTick = -1;

    /// <summary>타이틀 차례의 지금 틱(그린 때부터, 30틱/초).</summary>
    private double TitleTick => _shownAt < 0 ? 0 : (host._lastTime - _shownAt) * GameWindow.TicksPerSecond;

    /// <summary>글자 흐름이 끝나 로고 · 핵이 나오는 틱.</summary>
    private double LogoTick => _skipTick >= 0 ? Math.Min(_skipTick, IntroTicks) : IntroTicks;

    /// <summary>불티가 끝나 저작권 · 단추가 나오는 틱.</summary>
    private double MenuTick => _skipTick >= 0 ? Math.Min(_skipTick, IntroTicks + SparkTicks) : IntroTicks + SparkTicks;

    /// <summary>단추가 나왔나 — 그 전에는 원본처럼 단추가 숨어 있어 누를 수 없다(Esc · 누름은 건너뛰기).</summary>
    internal bool MenuShown => _shownAt >= 0 && TitleTick >= MenuTick;

    /// <summary>글자 흐름 · 불티를 건너뛴다(원본 0x100d5f80 의 Esc) — 단추가 곧바로 밝아진다.</summary>
    private void SkipIntro()
    {
        if (_shownAt < 0) _shownAt = host._lastTime;
        _skipTick = TitleTick;
    }

    /// <summary>
    /// 그때부터 밝아지는 그림의 비침 — 원본 vt+0x54 · 0x6c 가 섞기 13 의 세기를 한 틱에 하나씩 1→7 로 올리고 8 에서 그대로 그린다(0x1009fc00).
    /// <paramref name="cap"/> 은 그림 자신의 섞기 13 세기(메뉴 글자 4) — 그 위로는 안 오른다.
    /// </summary>
    private double FadeFrom(double startTick, int cap = 8)
    {
        int level = Math.Min(cap, (int)(TitleTick - startTick) + 1);
        return level >= 8 ? 1 : GameWindow.BlendFade(Math.Max(1, level));
    }

    /// <summary>파트2 타이틀의 「PART 1」 단추를 누른다 — 파트1 폴더가 없으면 알리고 머문다.</summary>
    internal void PressEntry()
    {
        if (Part1Game.Root is null && !AskPart1Folder()) return;
        host.LeaveScene(Open);
    }

    /// <summary>
    /// 파트1 폴더를 못 찾았다 — 원본이 있어야 한다고 알리고, 압축을 풀어 둔 원본을 찾아본 뒤(<see cref="Part1Game.Scan"/>) 찾으면 쓸지 묻고,
    /// 못 찾거나 마다하면 설치 폴더를 고르게 한다(파트2 의 OriginalGameSetup 과 같은 방식, 사용자 요청).
    /// 고른 폴더는 적어 두어 다음부터는 안 묻는다. 파트1 자료는 배포판에 없고 사용자의 원본에서 읽는다. 화면 밖 시험에서는 창을 띄우지 않는다.
    /// </summary>
    private bool AskPart1Folder()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_OFFSCREEN") == "1") { host.Toast("파트1 폴더를 찾지 못했습니다 — WAROFGENESIS_PART1 로 알려 주세요"); return false; }
        var owner = new System.Windows.Forms.NativeWindow();
        owner.AssignHandle(host._hwnd);
        try
        {
            const string Title = "창세기전3 파트1";
            var answer = System.Windows.Forms.MessageBox.Show(owner,
                "파트1 을 하려면 원본 「창세기전3 파트1」이 있어야 합니다 — 전투 · 인물 · 지도 · 그림 · 소리를 원본에서 읽습니다.\n\n" +
                "확인을 누르면 이 PC 에서 압축을 풀어 둔 원본 폴더(Chars · Obss · Btls 폴더가 든 곳)를 찾아봅니다. 원본 폴더에는 아무것도 쓰지 않습니다.",
                Title, System.Windows.Forms.MessageBoxButtons.OKCancel, System.Windows.Forms.MessageBoxIcon.Information);
            if (answer != System.Windows.Forms.DialogResult.OK) return false;

            var cursor = System.Windows.Forms.Cursor.Current;
            System.Windows.Forms.Cursor.Current = System.Windows.Forms.Cursors.WaitCursor;
            string? found;
            try { found = Part1Game.Scan(TimeSpan.FromSeconds(20)); }
            finally { System.Windows.Forms.Cursor.Current = cursor; }
            if (found != null)
            {
                if (System.Windows.Forms.MessageBox.Show(owner, $"원본 파트1 을 찾았습니다.\n\n{found}\n\n이 폴더를 쓸까요?", Title,
                        System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.Yes)
                {
                    UsePart1Folder(found);
                    return true;
                }
                answer = System.Windows.Forms.MessageBox.Show(owner, "원본 파트1 폴더를 직접 골라 주세요.", Title,
                    System.Windows.Forms.MessageBoxButtons.OKCancel, System.Windows.Forms.MessageBoxIcon.Information);
            }
            else
            {
                // 못 찾았다 — 직접 고르기 · 구글에서 「창세기전3 파트1」 검색(웹 브라우저) · 취소(사용자 요청). 검색해도 창은 닫지 않고 다시 고르게 둔다.
                var pick = new System.Windows.Forms.TaskDialogButton("폴더 직접 고르기");
                var google = new System.Windows.Forms.TaskDialogButton("구글에서 「창세기전3 파트1」 찾아보기") { AllowCloseDialog = false };
                google.Click += (_, _) => OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString("창세기전3 파트1"));
                var page = new System.Windows.Forms.TaskDialogPage
                {
                    Caption = Title,
                    Heading = "원본 파트1 폴더를 찾지 못했습니다",
                    Text = "압축을 풀어 둔 원본 파트1 폴더(Chars · Obss · Btls 폴더가 든 곳)를 직접 골라 주세요.\n원본이 없으면 구글에서 찾아볼 수 있습니다.",
                    Icon = System.Windows.Forms.TaskDialogIcon.Warning,
                    Buttons = { pick, google, System.Windows.Forms.TaskDialogButton.Cancel },
                };
                answer = System.Windows.Forms.TaskDialog.ShowDialog(owner.Handle, page) == pick
                    ? System.Windows.Forms.DialogResult.OK : System.Windows.Forms.DialogResult.Cancel;
            }
            while (answer == System.Windows.Forms.DialogResult.OK)
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "원본 창세기전3 파트1 폴더 (Chars · Obss · Btls 가 든 곳)", UseDescriptionForTitle = true };
                if (dialog.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return false;
                if (Part1Game.IsValid(dialog.SelectedPath))
                {
                    UsePart1Folder(dialog.SelectedPath);
                    return true;
                }
                answer = System.Windows.Forms.MessageBox.Show(owner, $"'{dialog.SelectedPath}' 에서 Obss\\Obss.idx · Chars 를 못 찾았습니다. 다시 고를까요?",
                    "창세기전3 파트1", System.Windows.Forms.MessageBoxButtons.OKCancel, System.Windows.Forms.MessageBoxIcon.Warning);
            }
            return false;
        }
        finally { owner.ReleaseHandle(); }
    }

    /// <summary>
    /// 고른 파트1 폴더를 쓴다 — 게임 자료는 켤 때 이미 읽었고 그때 파트1 폴더를 몰라 파트1 어빌리티(연 · 댓쉬 · 화이어 애로우 …, Part1SkillBook)가
    /// 어빌리티 표에 없었다. 그대로면 인물의 전투 목록이 그 표에 있는 것만 남겨 비었다(사용자 보고: 처음 전투에 기본 어빌리티도 없다 · 껐다 켜면 나온다).
    /// 스킬 표를 다시 읽고 만들어 둔 파트1 인물도 버린다.
    /// </summary>
    private void UsePart1Folder(string root)
    {
        Part1Game.Use(root);
        host._db?.ReloadSkills();
        host._db?.ForgetPart1Characters();
    }

    /// <summary>기본 웹 브라우저로 주소를 연다 — 못 열어도 그냥 넘어간다.</summary>
    private static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    internal void Open()
    {
        _bg = ReadBackground(TitleBackground);
        _open = true;
        SlotsScreen.Part1Slots = true;
        _hover = -1;
        _shownAt = _skipTick = -1;
        host.StopMusic();
        host.EnterSceneFade();
    }

    /// <summary>타이틀 곡 — 12월 25일이면 캐럴(<c>0x100d5c90</c> 이 날짜 글을 <c>"12/25"</c> 와 5자 비교).</summary>
    private static int TitleMusic => Part1Game.BgmBase + (DateTime.Now.ToString("MM/dd") == "12/25" ? ChristmasBgm : TitleBgm);

    /// <summary>파트2 타이틀로 돌아간다.</summary>
    internal void Close()
    {
        _open = false;
        SlotsScreen.Part1Slots = false;
        if (_chapter != null) _chapter._open = false;
        if (_timeline != null) _timeline._open = false;
        if (_field != null) _field._open = false;
        if (_town != null) _town._open = false;
        if (_party != null) _party._open = false;
        _entryHover = false;
        _bg = null;
        host.TitleScr.OpenTitle();
    }

    /// <summary>파트1 <c>BGRs</c> 묶음의 그림 한 장(JPEG·GIF)을 640×480 낱칸으로.</summary>
    private uint[]? ReadBackground(int id)
    {
        if (Part1Game.Read("BGRs", $"{id:D4}.bgr") is not { } bytes) return null;
        try
        {
            using var bitmap = new Bitmap(new MemoryStream(bytes));
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                const int W = MosesScene.MosesW, H = MosesScene.MosesH;
                var pixels = new uint[W * H];
                for (int y = 0; y < Math.Min(H, bitmap.Height); y++)
                    new Span<uint>((void*)(data.Scan0 + y * data.Stride), Math.Min(W, bitmap.Width)).CopyTo(pixels.AsSpan(y * W));
                return pixels;
            }
            finally { bitmap.UnlockBits(data); }
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            host._loadError = $"파트1 타이틀 배경: {ex.Message}";
            return null;
        }
    }

    internal bool EntryAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        var (x, y, w, h) = EntryButton;
        return bx >= ox + x && bx < ox + x + w && by >= oy + y && by < oy + y + h;
    }

    /// <summary>그 자리의 단추 — 단추가 아직 안 나왔으면(글자 흐름 · 불티 동안) 없다.</summary>
    internal int ButtonAt(int bx, int by)
    {
        if (!MenuShown) return -1;
        var (ox, oy) = host.Mos.MosesOrigin();
        for (int i = 0; i < Buttons.Length; i++)
        {
            var (_, _, x0, y0, x1, y1) = Buttons[i];
            if (bx >= ox + x0 && bx < ox + x1 && by >= oy + y0 && by < oy + y1) return i;
        }
        return -1;
    }

    internal void UpdateHover(int bx, int by)
    {
        if (_open && _party is { _open: true }) _party.UpdateHover(bx, by);
        else if (_open && _town is { _open: true }) _town.UpdateHover(bx, by);
        else if (_open && _field is { _open: true }) _field.UpdateHover(bx, by);
        else if (_open && _chapter is { _open: true }) _chapter.UpdateHover(bx, by);
        else if (_open && _timeline is { _open: true }) _timeline.UpdateHover(bx, by);
        else if (_open) _hover = ButtonAt(bx, by);
        else _entryHover = EntryAt(bx, by);
    }

    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        if (_party is { _open: true }) _party.OnClick(bx, by);
        else if (_town is { _open: true }) _town.OnClick(bx, by);
        else if (_field is { _open: true }) _field.OnClick(bx, by);
        else if (_chapter is { _open: true }) _chapter.OnClick(bx, by);
        else if (_timeline is { _open: true }) _timeline.OnClick(bx, by);
        else if (!MenuShown) SkipIntro();   // 단추가 나오기 전 — 누르면 글자 흐름 · 불티를 건너뛴다(원본은 Esc 만)
        else Press(ButtonAt(bx, by));
        return true;
    }

    /// <summary>Esc 는 파트2 로, 위·아래로 단추를 옮기고 Enter · Space 로 누른다.</summary>
    internal bool OnKey(int key)
    {
        if (!_open) return false;
        // 저장 칸 창 · 확인 창이 떠 있으면 키는 그 창 몫이다(타이틀 길이 Esc 로 닫는다) — 전에는 확인 창에서 Enter 가 파트1 타이틀의 NEW GAME 을 눌렀다.
        if (host.Sys.SystemOpen) return false;
        if (_party is { _open: true }) _party.OnKey(key);
        else if (_town is { _open: true }) _town.OnKey(key);
        else if (_field is { _open: true }) _field.OnKey(key);
        else if (_chapter is { _open: true }) _chapter.OnKey(key);
        else if (_timeline is { _open: true }) _timeline.OnKey(key);
        else if (!MenuShown) { if (key is Win32.VK_ESCAPE or Win32.VK_RETURN or Win32.VK_SPACE) SkipIntro(); }   // 원본 0x100d5f80 — Esc 로 건너뛴다
        else if (key == Win32.VK_ESCAPE) Press(ButtonPart2);
        else if (host.MoveActionFor(key) is KeyAction.MoveUp or KeyAction.MoveDown || key is Win32.VK_UP or Win32.VK_DOWN)
        {
            bool up = key == Win32.VK_UP || host.MoveActionFor(key) == KeyAction.MoveUp;
            int at = Array.IndexOf(KeyOrder, _hover), n = KeyOrder.Length;
            _hover = KeyOrder[at < 0 ? (up ? n - 1 : 0) : (at + (up ? n - 1 : 1)) % n];
        }
        else if (key is Win32.VK_RETURN or Win32.VK_SPACE) Press(Math.Max(0, _hover));
        return true;
    }

    internal void Press(int button)
    {
        switch (button)
        {
            case ButtonNewGame: PlayPress(); host.LeaveScene(NewGame); break;
            case ButtonLoad:
                // 저장 칸 창(불러오기)을 연다 — 파트1 칸(SlotsScreen.Part1Slots)이 열려 있다. 고르면 SystemMenu 가 판을 불러오고 PrepareBattleLoad 가 파트1 진행을 되살린다.
                if (host.SlotsScr.LatestSlot() < 0) { host.Toast("파트1 저장 기록이 없습니다 — 전투 중 시스템 메뉴의 SAVE 로 저장합니다"); break; }
                PlayPress();
                host.SlotsScr.OpenSlots(1);
                break;
            case ButtonContinue:
                if (!Part1Progress.HasSave) { host.Toast("파트1 진행 기록이 없습니다 — NEW GAME 으로 시작하세요"); break; }
                PlayPress();
                host.LeaveScene(ContinueGame);
                break;
            case ButtonTutorial: PlayPress(); host.LeaveScene(StartTutorial); break;
            case ButtonPart2: PlayPress(); host.LeaveScene(Close); break;
            case ButtonExit: host._running = false; break;   // 게임 끝내기 — 파트2 타이틀의 EXIT 와 같다(사용자 요청)
        }
    }

    /// <summary>단추를 누른 소리 — 원본 타이틀 단추 알림(0x100d61f0)이 <c>Snd 2034</c> 를 한 번 낸다.</summary>
    private void PlayPress() => host.Play(Part1Game.ObsBase + PressSound);

    /// <summary>파트1 새 게임 — 진행을 비우고 연대표를 연다. 거기서 줄기마다 열린 첫 칸 가운데 어디서 시작할지 고른다.</summary>
    private void NewGame()
    {
        host.Part1.Reset();
        Chapter.Forget();
        HookGrowth();
        Array.Clear(host.FlagSt._flags);   // 파트1 진행 깃발은 파트2 와 같은 칸을 쓴다 — 새 게임은 비우고 시작한다
        // 원본처럼 연대표에서 어디서 시작할지 고른다 — 줄기마다 첫 칸(살라딘 「카디스 요새」 · 바이올라 「반기」 · 인페르노 「인페르노의 음모」)이 열려 있다.
        Timeline.Open();
    }

    /// <summary>
    /// 튜토리얼 — 원본 상태 3(<c>0x100d5df2</c>): 새 게임과 같은 준비(진행 · 깃발 비우기)를 하고 챕터 80 지도를 연다.
    /// 챕터 80 의 사건이 필드 11 대사 · 용병 · 아이템을 거쳐 전투 0415 → 0416 → 0417 → 0418 을 잇고 501 로 연대표에 닿는다.
    /// 진행 자동 저장(part1.json)은 평소대로 한다(사용자 결정) — 전투 SAVE 만 원본처럼 막는다(<see cref="SystemMenu.SaveBlockedReason"/>).
    /// </summary>
    private void StartTutorial()
    {
        host.Part1.Reset();
        Chapter.Forget();
        HookGrowth();
        Array.Clear(host.FlagSt._flags);
        if (!Chapter.Open(TutorialChapter)) Open();
    }

    /// <summary>파트1 이어 하기 — <c>part1.json</c> 의 챕터에서 남은 사건을 잇는다. 못 읽으면 알리고 타이틀에 머문다.</summary>
    private void ContinueGame()
    {
        bool loaded = host.Part1.Load(host.FlagSt._flags);
        HookGrowth();
        if (!loaded || host.Part1.ChapterId < 0 || !Chapter.Continue())
            host.Toast("파트1 진행 기록을 읽지 못했습니다");
    }

    /// <summary>파트1 전투 세이브를 불러올 때 — 성장을 다시 걸고, 전투가 끝나면 그 챕터의 남은 사건을 잇게 해 둔다(이어 하기와 같은 준비).</summary>
    internal void PrepareBattleLoad()
    {
        HookGrowth();
        Chapter.PrepareContinue(inBattle: true);
        // 파트1 타이틀 · 지도에서 불러왔으면 그 화면들을 닫는다(StartBattle 과 같이) — 열린 채로 남아 전투 중 T(전직 트리) · P(동료 정보)가
        // 「전투 판이 아니다」로 보고 안 열렸다(사용자 보고: 20089 에서 P · T 반응이 없다).
        _open = false;
        _bg = null;
        if (_chapter != null) _chapter._open = false;
        if (_timeline != null) _timeline._open = false;
        if (_field != null) _field._open = false;
        if (_town != null) _town._open = false;
        if (_party != null) _party._open = false;
    }

    /// <summary>챕터 지도가 떠 있나(필드 · 마을 · 일행 창이 아니라) — 지도 저장(장면 갈래 4)이 본다.</summary>
    internal bool ChapterMapOpen => _chapter is { _open: true } && _field is not { _open: true } && _town is not { _open: true } && _party is not { _open: true };

    /// <summary>연대표가 떠 있나 — 연대표 저장(장면 갈래 7)이 본다.</summary>
    internal bool TimelineOpen => _timeline is { _open: true } && _chapter is not { _open: true };

    /// <summary>연대표에서 적은 세이브(장면 갈래 7 + 파트1 진행)를 불러온 뒤 — 그 진행으로 연대표를 연다.</summary>
    internal void LoadTimelineSave()
    {
        HookGrowth();
        host.TitleScr.OpenTitle();
        Open();
        Chapter.Forget();
        Timeline.Open();
    }

    /// <summary>
    /// 지도에서 적은 세이브(장면 갈래 4)를 불러온 뒤 — 진행(<see cref="Part1Progress.FromJson"/>)은 이미 되살렸다. 전투 · 타이틀 어디서 불렀든 그 챕터 지도를 연다
    /// (팀 자리 · 지도 턴 · 남은 사건 그대로 — 이어 하기와 같은 길).
    /// </summary>
    internal void LoadChapterSave()
    {
        HookGrowth();
        host.TitleScr.OpenTitle();
        Open();
        Chapter.Forget();
        if (host.Part1.ChapterId < 0 || !Chapter.Continue()) host.Toast("파트1 진행 기록을 읽지 못했습니다");
    }

    /// <summary>파트1 인물을 읽을 때 진행의 자란 것(경험치 · 어빌리티)을 입히게 한다 — 만들어 둔 인물은 버린다.</summary>
    internal void HookGrowth()
    {
        if (host._db is not { } db) return;
        db.Part1Growth = host.Part1.Apply;
        db.ForgetPart1Characters();
        host.Btl.Part1.Forget();
    }

    /// <summary>
    /// 파트1 전투(파트1 번호)를 연다 — 판·맵·그림은 파트2 틀로, 인물은 파트1 레코드에서(<see cref="Part1Game.ChrBase"/>).
    /// 못 열면 챕터 지도에 머문다.
    /// </summary>
    internal bool StartBattle(int part1Battle)
    {
        if (host.Sys.StartBattle(Part1Game.BattleBase + part1Battle, rememberParty: false))
        {
            _open = false;
            _bg = null;
            if (_chapter != null) _chapter._open = false;
            if (_timeline != null) _timeline._open = false;
            if (_field != null) _field._open = false;
            if (_town != null) _town._open = false;
            if (_party != null) _party._open = false;
            return true;
        }
        host.Toast($"파트1 전투 {part1Battle} 를 열지 못했습니다");
        return false;
    }

    /// <summary>
    /// 전투 행동 104(결과 6 → 장면 5) — 전투를 마치고 그 필드로. 필드가 끝나면(500 → 장면 4) 챕터로 돌아가 남은 행동을 잇는다.
    /// </summary>
    internal void FieldFromBattle(int field)
    {
        ChainTo(2, field);
        host.TitleScr.OpenTitle();
        Open();
        Field.Open(field, () => ReturnFromBattle(true));
    }

    /// <summary>
    /// 사슬 — 챕터 사건이 연 장면이 다음 장면(전투 101 · 104, 필드 502 · 501)으로 넘어간다. 그 자리를 진행에 적어 둔다(<see cref="Part1Progress.Chain"/>):
    /// 이어 하기는 이 장면부터 열고, 이 장면 안의 전투 세이브는 챕터 남은 행동의 머리(사슬을 연 502 · 503)를 빼고 잇는다.
    /// </summary>
    internal void ChainTo(int kind, int id)
    {
        if (host.Part1.ChapterId < 0) return;
        host.Part1.Chain = [kind, id];
        host.Part1.Save(host.FlagSt._flags);
    }

    /// <summary>전투 행동 105(결과 7 → 장면 10) — 이기고 챕터를 끝내 연대표로.</summary>
    internal void EndChapterFromBattle()
    {
        host.TitleScr.OpenTitle();
        Open();
        Chapter.EndChapter();
    }

    /// <summary>파트1 전투를 마치고 — 이겼으면 그 챕터 지도로, 졌으면 파트1 타이틀로 돌아온다.</summary>
    internal void ReturnFromBattle(bool won)
    {
        host.TitleScr.OpenTitle();
        Open();
        if (won && host.Part1.ChapterId >= 0) Chapter.Open(host.Part1.ChapterId);
        else Chapter.Forget();   // 졌다 — 이어 돌던 사건은 버린다(이어 하기는 저장된 자리에서, 새 게임은 깨끗하게)
    }

    /// <summary>파트2 타이틀 위의 「PART 1 (BETA)」 단추 — 아직 옮기는 중이라 베타로 단다(사용자 요청).</summary>
    internal void DrawEntry()
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        var (x, y, w, h) = EntryButton;
        DrawButton(ox + x, oy + y, w, h, "PART 1 (BETA)", _entryHover);
    }

    /// <summary>
    /// 장면 번호 꼬리표(파트2 의 「Fld 0365 · 사건 3 · 줄 12」 자리) — 「P1 Fld 0072 · 사건 3 · 줄 12」 · 「P1 Fld 0138 · 마을」 · 「P1 Chp 0020」.
    /// 사건 · 줄은 사건이 도는 동안만(다 돌았으면 번호만). 타이틀 · 일행 창이면 null.
    /// </summary>
    internal string? SceneTag()
    {
        static string Place(Part1SceneScript s) => !s.Idle && s.Place is ( >= 0, >= 0) p ? $" · 사건 {p.Event} · 줄 {p.Line}" : "";
        if (_party is { _open: true }) return null;
        if (_town is { _open: true, FieldId: >= 0 } town) return $"P1 Fld {town.FieldId:D4} · 마을";
        if (_field is { _open: true, _field: { } f } field) return $"P1 Fld {f.Id:D4}" + Place(field.Script);
        if (_chapter is { _open: true, _chapter: { } c } chapter) return $"P1 Chp {c.Id:D4}" + Place(chapter.Events);
        if (_timeline is { _open: true }) return "P1 연대표";
        return null;
    }

    /// <summary>판 크기 — 챕터 지도를 그리는 동안은 창만큼 넓게, 다른 파트1 화면으로 가면 640 틀로 되돌린다(<see cref="Part1ChapterScreen.FitBoard"/>).</summary>
    internal void FitBoard() =>
        _chapter?.FitBoard(_open && _chapter._open && _party is not { _open: true } && _town is not { _open: true } && _field is not { _open: true });

    internal void Draw()
    {
        if (_party is { _open: true }) { _party.Draw(); return; }
        if (_town is { _open: true }) { _town.Draw(); return; }
        if (_field is { _open: true }) { _field.Draw(); return; }
        if (_chapter is { _open: true }) { _chapter.Draw(); return; }
        if (_timeline is { _open: true }) { _timeline.Draw(); return; }
        var (ox, oy) = host.Mos.MosesOrigin();
        if (_shownAt < 0) _shownAt = host._lastTime;
        // 타이틀 곡 — 장면을 떠나는 페이드 동안은 다시 걸지 않는다(PART 2 로 갈 때 곡이 꺼진 뒤 도로 울렸다).
        if (host._fadeOutStart < 0 && host._musicId != TitleMusic) host.PlayMusicFile(TitleMusic, loop: true);
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (_bg is { } bg)
            for (int y = 0; y < MosesScene.MosesH; y++)
                for (int x = 0; x < MosesScene.MosesW; x++)
                    host.SetPixel(ox + x, oy + y, bg[y * MosesScene.MosesW + x] | 0xFF000000);

        // 원본이 만든 차례(= 그리는 차례)대로 — 머리 주석. 글자 흐름 · 불티 · 핵은 모션의 섞기 키 9(더하기), 로고 · 저작권은 보통.
        double t = TitleTick, logoAt = LogoTick, menuAt = MenuTick;
        int x0 = ox + Cx, y0 = oy + Cy;
        const int P1 = Part1Game.ObsBase;
        if (t < logoAt) host.DrawUi(P1 + IntroObs, 0, (int)t, x0, oy + 260, GameWindow.UiBlend.Add, loop: false);
        bool logo = t >= logoAt, menu = t >= menuAt;
        if (logo) host.DrawUi(P1 + LogoObs, 0, 0, x0, oy + 244, GameWindow.UiBlend.Alpha, fade: FadeFrom(logoAt));
        if (menu) host.DrawUi(P1 + MenuObs, 4, 0, x0, y0, GameWindow.UiBlend.Alpha, fade: FadeFrom(menuAt));   // 저작권(왼쪽 아래)
        if (logo) host.DrawUi(P1 + LogoObs, 1, 0, x0, oy + 244, GameWindow.UiBlend.Alpha, fade: FadeFrom(logoAt));
        if (logo && !menu) host.DrawUi(P1 + SparkObs, 0, (int)(t - logoAt), ox + 420, oy + 180, GameWindow.UiBlend.Add, loop: false);
        if (logo) host.DrawUi(P1 + CoreObs, 0, (int)(t - logoAt), x0, oy + 253, GameWindow.UiBlend.Add);

        // 저장 칸 창(LOAD GAME)이 떠 있으면 단추는 숨긴다 — 창 위에 겹쳐 그려졌다.
        if (menu && !host.SlotsScr.SlotsOpen)
        {
            // 메뉴 줄 — 원본은 그림(Obs 0751 모션 10~13 · 짚으면 0~3)이지만 CONTINUE · PART 2 와 같은 글꼴의 글로 쓴다(사용자 요청).
            // 자리는 원본 맞는 칸 가운데. 밝아지는 것(FadeFrom)은 원본 차례 그대로.
            double dim = FadeFrom(menuAt, 4);
            for (int row = 0; row < 5; row++)
            {
                int button = Array.FindIndex(Buttons, b => b.Row == row);
                var (cx, cy) = ((Buttons[button].X0 + Buttons[button].X1) / 2, (Buttons[button].Y0 + Buttons[button].Y1) / 2);
                string label = Buttons[button].Name;
                bool hot = _hover == button && FadeFrom(menuAt) >= 1;
                uint color = hot ? 0xFFFFF0C8u : 0xFFA89C90u;
                color = (uint)(Math.Clamp(dim, 0, 1) * 255) << 24 | (color & 0xFFFFFF);
                var (_, w, h) = host.UiGetText(label, color, 14);
                host.UiText(label, ox + cx - w / 2, oy + cy - h / 2, color, 14);
            }
            // 원본에 없는 단추(리메이크) — 오른쪽 아래에 메뉴 글자 빛깔의 작은 글로.
            for (int i = 0; i < Buttons.Length; i++)
                if (Buttons[i] is { Row: < 0 } b)
                {
                    uint color = _hover == i ? 0xFFFFF0C8u : 0xFFA89C90u;
                    var (_, w, h) = host.UiGetText(b.Name, color, 12);
                    host.UiText(b.Name, ox + b.X1 - w, oy + (b.Y0 + b.Y1 - h) / 2, color, 12);
                }
            host.UiText("v1.04", ox + 5, oy + 10, GameWindow.White, 12);   // 원본 판 번호 글(0x100a2570, 「v1.04」)
        }
        host.DrawToast();
    }

    /// <summary>챕터·필드 대사 창 — 아래쪽에 말하는 이와 글(줄 바꿈 그대로).</summary>
    /// <summary>원본 대사 상자 셋의 자리(640×480) — 0 왼쪽 · 1 오른쪽 · 2 가운데, 모두 511×111(<c>0x100a9000</c>).</summary>
    internal static readonly (int X, int Y)[] TalkBoxes = [(16, 232), (112, 352), (64, 336)];

    /// <summary>일러스트를 둘 때 화면 가운데(320)에서 옮기는 거리 — 원본은 모션의 키(종류 9)가 정한다(살라딘 309: −204 · +152, 발라 305: −198 · +172). 어림값.</summary>
    internal static readonly int[] FaceShift = [-200, 160, 0];

    /// <summary>
    /// 필드 · 챕터 대사 — 떠 있는 상자의 일러스트를 먼저(뒤에) 깔고, 지금 대사를 그 상자 자리에 그린다. 일러스트는 네 조각으로 나뉜 한 장이라
    /// 벌 0 의 장을 모두 (320 + 옮김, 240) 기준으로 찍는다(아래 끝이 화면 아래 480 에 닿는다, 분석-파트1-챕터 11절).
    /// </summary>
    internal void DrawTalk(int ox, int oy, string speaker, string text, int slot, int[] faces)
    {
        for (int s = 0; s < faces.Length; s++)
        {
            if (faces[s] <= 0 || host.UiFor(Part1Game.ObsBase + faces[s]) is not { } sprite) continue;
            int cx = ox + 320 + FaceShift[s], cy = oy + 240;
            foreach (var ((sub, _), frame) in sprite._frames)
                if (sub == 0) host.BlitScaled(frame, cx + frame.X, cy + frame.Y, frame.W, frame.H);
        }
        var (bx, by) = TalkBoxes[Math.Clamp(slot, 0, 2)];
        DrawTalkBox(ox + bx, oy + by, 511, 111, speaker, text);
    }

    internal void DrawTalk(int ox, int oy, string speaker, string text) => DrawTalkBox(ox + 24, oy + 340, 592, 124, speaker, text);

    internal void DrawTalkBox(int bx, int by, int W, int H, string speaker, string text, bool arrow = true)
    {
        int ox = bx, oy = by;
        const int X = 0, Y = 0;
        host.UiFill(ox + X, oy + Y, W, H, 0xE8101828);
        host.UiStroke(ox + X, oy + Y, W, H, 0xFF8098C8);
        int y = oy + Y + 10;
        if (speaker.Length > 0) { host.UiText(speaker, ox + X + 14, y, 0xFFFFE080, 13); y += 22; }
        if (!_wrapped.TryGetValue(text, out var lines))
        {
            if (_wrapped.Count > 64) _wrapped.Clear();
            _wrapped[text] = lines = [.. text.Split('\n').SelectMany(l => Wrap(l, W - 28))];
        }
        // 상자에 드는 줄만 — 넘치면 장을 나눠 누를 때마다 다음 장(사용자 보고: 글이 상자 밖으로 흘렀다).
        int fit = Math.Max(1, (oy + Y + H - 8 - y) / 19);
        if (!ReferenceEquals(text, _pageText) && text != _pageText) { _pageText = text; _page = 0; }
        _pages = (lines.Length + fit - 1) / fit;
        foreach (string line in lines.Skip(_page * fit).Take(fit))
        {
            host.UiText(line, ox + X + 14, y, GameWindow.White, 13);
            y += 19;
        }
        // 넘김 표시 — 반짝인다(0.5초 켜짐 · 0.25초 꺼짐).
        if (arrow && host._lastTime % 0.75 < 0.5) host.UiText("▼", ox + X + W - 22, oy + Y + H - 20, 0xFFFFC060, 12);
    }

    private string? _pageText;
    private int _page, _pages;

    /// <summary>대사에 남은 장이 있으면 다음 장으로 넘기고 true — 화면들이 대사를 넘기기 전에 묻는다.</summary>
    internal bool NextTalkPage()
    {
        if (_page + 1 >= _pages) return false;
        _page++;
        return true;
    }

    /// <summary>접어 둔 대사(글 → 줄들) — 틀마다 글 폭을 다시 재지 않게.</summary>
    private readonly Dictionary<string, string[]> _wrapped = [];

    /// <summary>창 폭에 맞춰 줄을 접는다 — 띄어쓰기에서 끊고, 띄어쓰기 없이 긴 말은 글자에서 끊는다.</summary>
    private IEnumerable<string> Wrap(string line, int width)
    {
        while (line.Length > 0 && host.UiGetText(line, GameWindow.White, 13).W > width)
        {
            int cut = line.Length - 1;
            while (cut > 1 && host.UiGetText(line[..cut], GameWindow.White, 13).W > width) cut--;
            int space = line.LastIndexOf(' ', cut - 1);
            if (space > cut / 2) cut = space + 1;
            yield return line[..cut].TrimEnd();
            line = line[cut..].TrimStart();
        }
        yield return line;
    }

    internal void DrawButton(int x, int y, int w, int h, string label, bool hot)
    {
        host.UiFill(x, y, w, h, hot ? 0xC0305080u : 0x90101828u);
        host.UiStroke(x, y, w, h, hot ? 0xFFB0D0FFu : 0xFF6078A0u);
        var (_, tw, th) = host.UiGetText(label, GameWindow.White, 13);
        host.UiText(label, x + (w - tw) / 2, y + (h - th) / 2, GameWindow.White, 13);
    }
}

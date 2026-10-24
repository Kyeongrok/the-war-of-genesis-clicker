using System.Drawing.Imaging;
using System.IO;
using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 타이틀 — 파트2 타이틀 왼쪽 아래의 「PART 1」 단추로 들어간다(파트1 을 파트2 엔진으로 옮기는 첫걸음).
/// </summary>
/// <remarks>
/// 파트1 본체 <c>alpha.dll</c> 의 타이틀 장면(<c>0x100d47e0</c>)은 배경을 <c>BGRs\0329.bgr</c>(640×480 JPEG, 어둠 속의 검과 푸른 빛) 한 장으로 걸고,
/// 날짜가 <c>01/01</c> 이면 <c>BGRs\0372.bgr</c>(근하신년 GIF)로 바꾼다(<c>0x100d4950</c> 이 날짜 글을 <c>"01/01"</c> 과 5자 비교).
/// 파트2(<c>Bgr\0046</c>)와 달리 메뉴 글자가 그림에 없다 — 단추는 UI 꼴 <c>0x271b</c>(10011)을 화면 가운데에 세워 만든다(<c>0x100a45c0</c>).
/// 그 꼴은 아직 안 풀어서 단추는 글자로 그린다. 그림은 사용자의 파트1 폴더(<see cref="Part1Game.Root"/>)의 <c>BGRs.pak</c> 에서 읽는다.
/// </remarks>
internal sealed unsafe class Part1TitleScreen(GameWindow host)
{
    internal const int TitleBackground = 329, NewYearBackground = 372;

    /// <summary>파트2 타이틀 위에 얹는 들어가기 단추(왼위·크기) — 640×480 화면 기준.</summary>
    internal static readonly (int X, int Y, int W, int H) EntryButton = (16, 440, 84, 26);

    /// <summary>파트1 타이틀의 단추 — 가운데 아래.</summary>
    internal static readonly (int X, int Y, string Name)[] Buttons = [(250, 364, "NEW GAME"), (250, 396, "CONTINUE"), (250, 428, "PART 2")];

    internal const int ButtonW = 140, ButtonH = 24, ButtonNewGame = 0, ButtonContinue = 1, ButtonPart2 = 2;

    internal bool _open;
    internal bool _entryHover;

    /// <summary>파트1 챕터 지도 · 연대표 — 이 화면 위에 열려 타이틀 장면(TitleScreen 의 문)을 그대로 쓴다.</summary>
    internal Part1ChapterScreen Chapter => _chapter ??= new(host, this);
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

    /// <summary>파트2 타이틀의 「PART 1」 단추를 누른다 — 파트1 폴더가 없으면 알리고 머문다.</summary>
    internal void PressEntry()
    {
        if (Part1Game.Root is null && !AskPart1Folder()) return;
        host.LeaveScene(Open);
    }

    /// <summary>
    /// 파트1 폴더를 못 찾았다 — 원본 「창세기전3 파트1」 설치 폴더를 고르게 한다(파트2 의 OriginalGameSetup 과 같은 방식, 사용자 요청).
    /// 고른 폴더는 적어 두어 다음부터는 안 묻는다. 파트1 자료는 배포판에 없고 사용자의 원본에서 읽는다. 화면 밖 시험에서는 창을 띄우지 않는다.
    /// </summary>
    private bool AskPart1Folder()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_OFFSCREEN") == "1") { host.Toast("파트1 폴더를 찾지 못했습니다 — WAROFGENESIS_PART1 로 알려 주세요"); return false; }
        var owner = new System.Windows.Forms.NativeWindow();
        owner.AssignHandle(host._hwnd);
        try
        {
            var answer = System.Windows.Forms.MessageBox.Show(owner,
                "파트1 은 원본 「창세기전3 파트1」의 자료(전투 · 인물 · 지도 · 그림 · 소리)를 씁니다.\n\n" +
                "원본 파트1 이 설치된 폴더(Chars · Obss · Btls 폴더가 든 곳)를 골라 주세요. 원본 폴더에는 아무것도 쓰지 않습니다.",
                "창세기전3 파트1", System.Windows.Forms.MessageBoxButtons.OKCancel, System.Windows.Forms.MessageBoxIcon.Information);
            while (answer == System.Windows.Forms.DialogResult.OK)
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "원본 창세기전3 파트1 폴더 (Chars · Obss · Btls 가 든 곳)", UseDescriptionForTitle = true };
                if (dialog.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return false;
                if (Part1Game.IsValid(dialog.SelectedPath))
                {
                    Part1Game.Use(dialog.SelectedPath);
                    return true;
                }
                answer = System.Windows.Forms.MessageBox.Show(owner, $"'{dialog.SelectedPath}' 에서 Obss\\Obss.idx · Chars 를 못 찾았습니다. 다시 고를까요?",
                    "창세기전3 파트1", System.Windows.Forms.MessageBoxButtons.OKCancel, System.Windows.Forms.MessageBoxIcon.Warning);
            }
            return false;
        }
        finally { owner.ReleaseHandle(); }
    }

    internal void Open()
    {
        string day = DateTime.Now.ToString("MM/dd");
        _bg = ReadBackground(day == "01/01" ? NewYearBackground : TitleBackground) ?? ReadBackground(TitleBackground);
        _open = true;
        SlotsScreen.Part1Slots = true;
        _hover = -1;
        host.StopMusic();
        host.EnterSceneFade();
    }

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

    internal int ButtonAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        for (int i = 0; i < Buttons.Length; i++)
        {
            var (x, y, _) = Buttons[i];
            if (bx >= ox + x && bx < ox + x + ButtonW && by >= oy + y && by < oy + y + ButtonH) return i;
        }
        return -1;
    }

    internal void UpdateHover(int bx, int by)
    {
        if (_open && _party is { _open: true }) _party.UpdateHover(bx, by);
        else if (_open && _town is { _open: true }) _town.UpdateHover(bx, by);
        else if (_open && _field is { _open: true }) { }
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
        else if (_field is { _open: true }) _field.OnClick();
        else if (_chapter is { _open: true }) _chapter.OnClick(bx, by);
        else if (_timeline is { _open: true }) _timeline.OnClick(bx, by);
        else Press(ButtonAt(bx, by));
        return true;
    }

    /// <summary>Esc 는 파트2 로, 위·아래로 단추를 옮기고 Enter · Space 로 누른다.</summary>
    internal bool OnKey(int key)
    {
        if (!_open) return false;
        if (_party is { _open: true }) _party.OnKey(key);
        else if (_town is { _open: true }) _town.OnKey(key);
        else if (_field is { _open: true }) _field.OnKey(key);
        else if (_chapter is { _open: true }) _chapter.OnKey(key);
        else if (_timeline is { _open: true }) _timeline.OnKey(key);
        else if (key == Win32.VK_ESCAPE) Press(ButtonPart2);
        else if (host.MoveActionFor(key) is KeyAction.MoveUp or KeyAction.MoveDown || key is Win32.VK_UP or Win32.VK_DOWN)
            _hover = _hover < 0 ? 0 : (_hover + 1) % Buttons.Length;
        else if (key is Win32.VK_RETURN or Win32.VK_SPACE) Press(Math.Max(0, _hover));
        return true;
    }

    internal void Press(int button)
    {
        switch (button)
        {
            case ButtonNewGame: host.LeaveScene(NewGame); break;
            case ButtonContinue:
                if (!Part1Progress.HasSave) { host.Toast("파트1 진행 기록이 없습니다 — NEW GAME 으로 시작하세요"); break; }
                host.LeaveScene(ContinueGame);
                break;
            case ButtonPart2: host.LeaveScene(Close); break;
        }
    }

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
        Chapter.PrepareContinue();
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
    internal void StartBattle(int part1Battle)
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
            return;
        }
        host.Toast($"파트1 전투 {part1Battle} 를 열지 못했습니다");
    }

    /// <summary>파트1 전투를 마치고 — 이겼으면 그 챕터 지도로, 졌으면 파트1 타이틀로 돌아온다.</summary>
    internal void ReturnFromBattle(bool won)
    {
        host.TitleScr.OpenTitle();
        Open();
        if (won && host.Part1.ChapterId >= 0) Chapter.Open(host.Part1.ChapterId);
        else Chapter.Forget();   // 졌다 — 이어 돌던 사건은 버린다(이어 하기는 저장된 자리에서, 새 게임은 깨끗하게)
    }

    /// <summary>파트2 타이틀 위의 「PART 1」 단추.</summary>
    internal void DrawEntry()
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        var (x, y, w, h) = EntryButton;
        DrawButton(ox + x, oy + y, w, h, "PART 1", _entryHover);
    }

    internal void Draw()
    {
        if (_party is { _open: true }) { _party.Draw(); return; }
        if (_town is { _open: true }) { _town.Draw(); return; }
        if (_field is { _open: true }) { _field.Draw(); return; }
        if (_chapter is { _open: true }) { _chapter.Draw(); return; }
        if (_timeline is { _open: true }) { _timeline.Draw(); return; }
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (_bg is { } bg)
            for (int y = 0; y < MosesScene.MosesH; y++)
                for (int x = 0; x < MosesScene.MosesW; x++)
                    host.SetPixel(ox + x, oy + y, bg[y * MosesScene.MosesW + x] | 0xFF000000);
        for (int i = 0; i < Buttons.Length; i++)
        {
            var (x, y, name) = Buttons[i];
            DrawButton(ox + x, oy + y, ButtonW, ButtonH, name, _hover == i);
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

    internal void DrawTalkBox(int bx, int by, int W, int H, string speaker, string text)
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
        foreach (string line in lines)
        {
            host.UiText(line, ox + X + 14, y, GameWindow.White, 13);
            y += 19;
        }
        host.UiText("▼", ox + X + W - 22, oy + Y + H - 20, 0xFFFFC060, 12);
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

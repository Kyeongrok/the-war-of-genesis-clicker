using System.Drawing.Imaging;
using System.IO;
using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 필드 — 챕터 사건 503 이 여는 연출 장면(<see cref="Part1Field"/>). 배경 그림 위에서 사건 대사를 차례로 띄우고, 행동 500 이면 챕터로 돌아간다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 7 · 14절. 배경(<c>BGRs</c>, 0002 는 1300×500 파노라마 — 머리 낱말 1·2 자리에서 본다) · 인물 · 대사 ·
/// 그림 덮개(100 · 101 · 102)와 화면 바꾸기(103~107) · 그림자(307) · 뒤집기(306) · 고르기(900 · 901) · 글 쪽 창(904). Esc 는 필드를 건너뛴다.
/// </remarks>
internal sealed unsafe class Part1FieldScreen(GameWindow host, Part1TitleScreen shell)
{
    internal bool _open;
    internal Part1Field? _field;
    private uint[]? _bg;
    private int _bgW, _bgH;
    private Action? _onEnd;

    internal Part1SceneScript Script => _script ??= new(host)
    {
        Timed = true,
        Busy = () => _walks.Any(w => !w.Value.Done(host._lastTime)) || _pan is { } pan && (host._lastTime - pan.Start) * GameWindow.TicksPerSecond < pan.Ticks,
        SceneChange = OnSceneChange,
        BackgroundChange = id => (_bg, _bgW, _bgH) = ReadImage(id),
        SpritePlace = (n, x, y) => { if (n >= 0 && n < _spots.Length) { _walks.Remove(n); _spots[n] = (x, y); } },
        SpriteShift = (n, dx, dy, ticks) => { if (n >= 0 && n < _spots.Length) Walk(n, dx, dy, ticks); },
        SpriteMotion = (n, motion, mirror) => { if (n >= 0 && n < _spots.Length) { _motion[n] = (motion, mirror); _flip[n] = false; } },
        SpriteVisible = (n, shown) => { if (n >= 0 && n < _spots.Length) _hidden[n] = !shown; },
        CameraPan = (dx, dy, ticks) => { _cam = Camera(); _pan = (_cam.X, _cam.Y, dx, dy, Math.Max(1, ticks), host._lastTime); },
        IsField = true,
        PictureShow = (id, x, y, talk) => { var (px, w, h) = ReadImage(id); _cover = new Cover(px, w, h, x, y, 0, talk); },
        ColorShow = color => _cover = new Cover(null, 0, 0, 0, 0, color switch { 0 => 0xFFFFFFFFu, 2 => 0xFFFF0000u, _ => 0xFF000000u }, false),
        PictureClear = () => _cover = null,
        Transition = (mode, kind, fadeOut, fadeIn) =>
        {
            // 옛 화면 = 지난 틀에 보인 것(같은 틀에 앞서 돈 100 · 101 은 아직 안 보였다). 갈래 1 은 필드 그대로로 — 끝에 덮개를 지운다(0x100cf502).
            _transition = (_shown, kind == 0 ? 0xFFFFFFFFu : 0xFF000000u, fadeOut, fadeIn, host._lastTime);
            if (mode == 1) _cover = null;
        },
        SpriteFlip = n => { if (n >= 0 && n < _flip.Length) _flip[n] = !_flip[n]; },
        SpriteShadow = (n, on) => { if (n >= 0 && n < _shadow.Length) _shadow[n] = on; },
        CameraSet = (x, y) => { _pan = null; _cam = (x, y); },
    };

    /// <summary>화면 가득 덮는 것 — 그림(BGRs, 그림 안 자리 X·Y 부터) 또는 한 빛깔(그림이 없을 때). 덮인 동안 필드 배경·인물은 안 그린다(0x100c7cfd).</summary>
    private sealed record Cover(uint[]? Px, int W, int H, int X, int Y, uint Color, bool ShowTalk);

    /// <summary>지금 덮개(100 · 101 이 놓고 102 · 103 [1, …] 이 걷는다). null 이면 필드.</summary>
    private Cover? _cover;

    /// <summary>지난 틀에 보인 덮개 — 화면 바꾸기의 「옛 화면」.</summary>
    private Cover? _shown;

    /// <summary>화면 바꾸기 — (옛 덮개, 바랠 빛깔, 나감 틱, 들어옴 틱, 시작).</summary>
    private (Cover? From, uint Color, int Out, int In, double Start)? _transition;

    /// <summary>306 이 뒤집은 인물 — 300 이 모션을 새로 걸면 푼다(그 모션의 뒤집기를 쓴다).</summary>
    private bool[] _flip = [];

    /// <summary>307 이 켠 발밑 그림자.</summary>
    private bool[] _shadow = [];

    /// <summary>필드 인물 그림자 그림(0x100c7790: <c>0x1009f4d0(0x4a, …)</c>).</summary>
    private const int ShadowObs = 74;

    /// <summary>고르기에서 짚은 줄(마우스 · 화살표).</summary>
    private int _choiceHover;

    /// <summary>인물 자리(배경 그림 위 발끝) — 필드 인물 칸에서 시작해 행동 303 이 옮기고 302 가 걷게 한다.</summary>
    private (int X, int Y)[] _spots = [];

    /// <summary>걷는 중 — 틱마다 (dx, dy) 씩, 틱 수만큼(행동 302). 다 걸으면 그 자리에 선다.</summary>
    private sealed record Walking(int FromX, int FromY, int Dx, int Dy, int Ticks, double Start)
    {
        public int Elapsed(double now) => Math.Min(Ticks, (int)((now - Start) * GameWindow.TicksPerSecond));
        public bool Done(double now) => Elapsed(now) >= Ticks;
        public (int X, int Y) At(double now) => (FromX + Dx * Elapsed(now), FromY + Dy * Elapsed(now));
    }

    private readonly Dictionary<int, Walking> _walks = [];

    /// <summary>화면 왼위(배경 그림 위) — 처음은 머리 낱말 1·2, 401 이 밀면 그 끝자리로 굳는다.</summary>
    private (int X, int Y) _cam;

    /// <summary>미는 중(401) — 시작 자리, 틱마다 (Dx, Dy), 틱 수, 시작 시각.</summary>
    private (int X, int Y, int Dx, int Dy, int Ticks, double Start)? _pan;

    /// <summary>지금 화면 왼위 — 미는 중이면 지난 틱만큼, 그림 끝에서 자른다(0x100cd98a~0x100cda06).</summary>
    private (int X, int Y) Camera()
    {
        int x = _cam.X, y = _cam.Y;
        if (_pan is { } pan)
        {
            int n = Math.Min(pan.Ticks, (int)((host._lastTime - pan.Start) * GameWindow.TicksPerSecond));
            (x, y) = (pan.X + pan.Dx * n, pan.Y + pan.Dy * n);
        }
        return (Math.Clamp(x, 0, Math.Max(0, _bgW - MosesScene.MosesW)), Math.Clamp(y, 0, Math.Max(0, _bgH - MosesScene.MosesH)));
    }

    /// <summary>바라보는 쪽(걸으면 걷는 쪽으로 돈다) — 인물 칸 방향에서 시작.</summary>
    private Facing[] _facing = [];

    /// <summary>행동 300 이 건 모션(번호, 뒤집기) — 있으면 방향·걷기로 고른 모션 대신 이것을 튼다.</summary>
    private (int Motion, bool Mirror)?[] _motion = [];

    /// <summary>행동 304 로 숨긴 인물.</summary>
    private bool[] _hidden = [];

    private void Walk(int n, int dx, int dy, int ticks)
    {
        var (x, y) = PositionOf(n);
        _spots[n] = (x, y);
        if (ticks <= 0) { _spots[n] = (x + dx, y + dy); return; }
        _walks[n] = new Walking(x, y, dx, dy, ticks, host._lastTime);
        if (dx != 0 || dy != 0)
            _facing[n] = Math.Abs(dx) >= Math.Abs(dy) ? dx > 0 ? Facing.Right : Facing.Left : dy > 0 ? Facing.Down : Facing.Up;
    }

    /// <summary>지금 자리 — 걷는 중이면 걸은 만큼, 다 걸었으면 도착 자리로 굳힌다.</summary>
    private (int X, int Y) PositionOf(int n)
    {
        if (!_walks.TryGetValue(n, out var walk)) return _spots[n];
        var at = walk.At(host._lastTime);
        if (walk.Done(host._lastTime)) { _walks.Remove(n); _spots[n] = at; }
        return at;
    }
    private Part1SceneScript? _script;

    /// <summary>그 필드를 연다 — 끝나면(행동 500 · Esc · 대사가 다 끝나고 누름) <paramref name="onEnd"/>. 못 읽으면 곧바로 <paramref name="onEnd"/>.</summary>
    internal void Open(int id, Action onEnd)
    {
        if (Part1Field.Load(id) is not { } field)
        {
            host.Toast($"파트1 필드 {id:D4} 를 못 읽었습니다");
            onEnd();
            return;
        }
        _field = field;
        _onEnd = onEnd;
        (_bg, _bgW, _bgH) = ReadImage(field.Background);
        _spots = [.. field.Sprites.Select(s => (s.X, s.Y))];
        _walks.Clear();
        _pan = null;
        _cam = (field.Header[1], field.Header[2]);
        _motion = new (int, bool)?[field.Sprites.Count];
        _hidden = new bool[field.Sprites.Count];
        _flip = new bool[field.Sprites.Count];
        _shadow = new bool[field.Sprites.Count];
        _cover = _shown = null;
        _transition = null;
        _choiceHover = 0;
        _facing = [.. field.Sprites.Select(s => s.Direction switch { 0 => Facing.Up, 2 => Facing.Down, 3 => Facing.Right, _ => Facing.Left })];
        _open = true;
        GameWindow.TestRunTrace($"part1 field {id} open");   // 시험 전용 줄
        Script.Start(field.Events, field.Lines, []);
    }

    private bool OnSceneChange(ScriptCommand c)
    {
        switch (c.Code)
        {
            case 500: End(); return true;
            case 502:
                int battle = c.Args[0];
                _open = false;
                host.LeaveScene(() => shell.StartBattle(battle));   // 필드 안의 전투 — 마치면 챕터로 돌아간다
                return true;
            case 501:                                                 // 다음 필드(0x100cfeb0 → 장면 5). 503 은 마을 갈래 낱말이라 필드를 안 넘긴다(0x100d09a0).
                int next = c.Args[0];
                var onEnd = _onEnd;
                Script.StopSounds();
                host.LeaveScene(() => Open(next, onEnd ?? (() => { })));
                return true;
            case 506:                                                 // 챕터 끝(0x100cfe60: 팀을 무리로 → 장면 10 연대표)
                GameWindow.TestRunTrace($"part1 field {_field?.Id} chapter end");   // 시험 전용 줄
                Script.StopVoice();
                Script.StopSounds();
                _onEnd = null;
                host.LeaveScene(() => { _open = false; _bg = null; shell.Chapter.EndChapter(); });
                return true;
        }
        return false;
    }

    private void End()
    {
        GameWindow.TestRunTrace($"part1 field {_field?.Id} end");   // 시험 전용 줄
        Script.StopVoice();                                          // Esc 로 끝내도 목소리가 이어 나오지 않게
        Script.StopSounds();
        var onEnd = _onEnd;
        _onEnd = null;
        host.LeaveScene(() => { _open = false; _bg = null; onEnd?.Invoke(); });
    }

    /// <summary>고르기 줄 자리(화면 640×480 기준) — 물음 상자 위에 쌓는다.</summary>
    private static (int X, int Y, int W, int H) ChoiceRow(int i, int count) => (120, 330 - (count - i) * 32, 400, 28);

    private int ChoiceAt(int bx, int by)
    {
        if (Script.Choice is not { } choice) return -1;
        var (ox, oy) = host.Mos.MosesOrigin();
        for (int i = 0; i < choice.Options.Count; i++)
        {
            var (x, y, w, h) = ChoiceRow(i, choice.Options.Count);
            if (bx >= ox + x && bx < ox + x + w && by >= oy + y && by < oy + y + h) return i;
        }
        return -1;
    }

    internal void UpdateHover(int bx, int by)
    {
        if (ChoiceAt(bx, by) is >= 0 and var i) _choiceHover = i;
    }

    internal void OnClick(int bx, int by)
    {
        if (Script.Choice != null && Script.Talk == null) { if (Script.WaitingOnChoice && ChoiceAt(bx, by) is >= 0 and var i) Script.Choose(i); return; }
        OnClick();
    }

    internal void OnClick()
    {
        // 고르기 — 짚은 줄을 고른다(자동 진행 · Enter). 고를 줄이 아직 다 안 떴으면(901 사이의 기다림) 안 받는다.
        if (Script.Choice is { } choice && Script.Talk == null)
        {
            if (choice.Options.Count > 0 && Script.WaitingOnChoice) Script.Choose(Math.Clamp(_choiceHover, 0, choice.Options.Count - 1));
            return;
        }
        if (Script.Talk != null) { if (!shell.NextTalkPage()) Script.Advance(); }
        else if (Script.Idle) End();                                        // 다 돌았는데 500 이 없으면 누르면 돌아간다(걷는 중 · 기다리는 중에는 안 받는다)
    }

    internal void OnKey(int key)
    {
        if (Script.Choice is { Options.Count: > 0 } choice && Script.Talk == null && key is Win32.VK_UP or Win32.VK_DOWN)
        {
            _choiceHover = (Math.Clamp(_choiceHover, 0, choice.Options.Count - 1) + (key == Win32.VK_UP ? choice.Options.Count - 1 : 1)) % choice.Options.Count;
            return;
        }
        if (key == Win32.VK_ESCAPE) End();
        else if (key is Win32.VK_RETURN or Win32.VK_SPACE) OnClick();
    }

    /// <summary>파트1 <c>BGRs</c> 그림 한 장(크기 그대로).</summary>
    internal (uint[]? Px, int W, int H) ReadImage(int id)
    {
        if (Part1Game.Read("BGRs", $"{id:D4}.bgr") is not { } bytes) return (null, 0, 0);
        try
        {
            using var bitmap = new Bitmap(new MemoryStream(bytes));
            var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var px = new uint[bitmap.Width * bitmap.Height];
                for (int y = 0; y < bitmap.Height; y++)
                    new Span<uint>((void*)(data.Scan0 + y * data.Stride), bitmap.Width).CopyTo(px.AsSpan(y * bitmap.Width));
                return (px, bitmap.Width, bitmap.Height);
            }
            finally { bitmap.UnlockBits(data); }
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            host.Toast($"파트1 필드 배경 {id}: {ex.Message}");
            return (null, 0, 0);
        }
    }

    internal void Draw()
    {
        Script.Pump();
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (_field is { } field)
        {
            const int W = MosesScene.MosesW, H = MosesScene.MosesH;
            // 화면 자리 — 머리 낱말 1·2 가 처음 자리, 행동 401 이 민다(Camera).
            var spots = Enumerable.Range(0, _spots.Length).Select(PositionOf).ToArray();
            var (camX, camY) = Camera();
            int top = Math.Max(0, (H - _bgH) / 2);   // 500 높이 그림은 가운데에
            // 덮개 · 화면 바꾸기 — 바꾸는 중이면 나감 동안 옛 것을 빛깔로 바래고, 들어옴 동안 새 것을 드러낸다(0x100ab380).
            var cover = _cover;
            uint fadeColor = 0;
            double fade = 0;
            if (_transition is { } tr)
            {
                double t = (host._lastTime - tr.Start) * GameWindow.TicksPerSecond;
                if (t < tr.Out) { cover = tr.From; fade = t / tr.Out; }
                else if (t < tr.Out + tr.In) fade = 1 - (t - tr.Out) / tr.In;
                else _transition = null;
                fadeColor = tr.Color;
            }
            _shown = cover;
            if (cover != null)
            {
                DrawCover(cover, ox, oy);
                if (fade > 0) host.FillRect(ox, oy, W, H, (uint)(Math.Clamp(fade, 0, 1) * 255) << 24 | fadeColor & 0xFFFFFF);
                DrawTalkLayer(ox, oy);
                host.DrawToast();
                return;
            }
            if (_bg is { } bg)
                for (int y = 0; y < Math.Min(H, _bgH); y++)
                    for (int x = 0; x < Math.Min(W, _bgW); x++)
                        host.SetPixel(ox + x, oy + top + y, bg[(camY + y) * _bgW + camX + x] | 0xFF000000);
            // 인물 — 서기(동작 0) · 걷는 중이면 걷기(동작 1) 모션을 방향대로(전투 인물 칸과 같은 방향 번호: 0 위 · 2 아래 · 3 오른쪽 · 그 밖 왼쪽).
            // 모션 번호 = 동작 × 3 + 방향(ObsMotionTable). 아래쪽에 선 사람을 나중에.
            int tick = (int)(host._lastTime * GameWindow.TicksPerSecond);
            foreach (int i in Enumerable.Range(0, Math.Min(spots.Length, field.Sprites.Count)).Where(i => !_hidden[i]).OrderBy(i => spots[i].Y))
            {
                var facing = _facing[i];
                int action = _walks.ContainsKey(i) ? ObsMotionTable.ActionWalk : ObsMotionTable.ActionStand;
                var (motion, mirror) = _motion[i] ?? (action * 3 + ObsMotionTable.DirectionOf(facing), facing == Facing.Right);
                var (x, y) = spots[i];
                if (_shadow[i])
                    host.DrawUi(Part1Game.ObsBase + ShadowObs, 0, tick, ox + x - camX, oy + top + y - camY, GameWindow.UiBlend.Darken, loop: true, fade: 0.55);   // 그림자 그림의 점은 어둡기 단계(0~31, 섞기 8) — 바탕을 어둡게
                host.DrawUi(Part1Game.ObsBase + field.Sprites[i].SpriteId, motion, tick, ox + x - camX, oy + top + y - camY,
                            GameWindow.UiBlend.Alpha, loop: true, mirror: mirror ^ _flip[i]);
            }
            if (fade > 0) host.FillRect(ox, oy, W, H, (uint)(Math.Clamp(fade, 0, 1) * 255) << 24 | fadeColor & 0xFFFFFF);
        }
        DrawTalkLayer(ox, oy);
        host.DrawToast();
    }

    /// <summary>덮개 — 한 빛깔이면 채우고, 그림이면 그림 안 (X, Y) 부터 화면 크기만큼 왼위에 찍는다(0x100c7d8f: OffsetRect).</summary>
    private void DrawCover(Cover cover, int ox, int oy)
    {
        const int W = MosesScene.MosesW, H = MosesScene.MosesH;
        if (cover.Px is not { } px) { host.FillRect(ox, oy, W, H, cover.Color); return; }
        int sx = Math.Clamp(cover.X, 0, Math.Max(0, cover.W - W)), sy = Math.Clamp(cover.Y, 0, Math.Max(0, cover.H - H));
        for (int y = 0; y < Math.Min(H, cover.H - sy); y++)
            for (int x = 0; x < Math.Min(W, cover.W - sx); x++)
                host.SetPixel(ox + x, oy + y, px[(sy + y) * cover.W + sx + x] | 0xFF000000);
    }

    /// <summary>대사 · 고르기 · 글 쪽 창.</summary>
    private void DrawTalkLayer(int ox, int oy)
    {
        if (Script.Talk is { } page && Script.TalkSlot == Part1SceneScript.PageSlot)
            shell.DrawTalkBox(ox + 70, oy + 100, 500, 285, "", page.Text);                          // 904 글 쪽 창(0x100d04b3)
        else if (Script.Talk is { } talk && Script.TalkSlot == Part1SceneScript.NarrationSlot)
            shell.DrawTalkBox(ox + 10, oy + 5, 619, 35, "", talk.Text);                            // 210 해설 띠(그림 없음)
        else if (Script.Talk is { } bubble && Script.TalkSlot == Part1SceneScript.BubbleSlot && _field is { } shown
                 && Script.TalkPerson >= 0 && Script.TalkPerson < _spots.Length)
        {
            // 209 말풍선 — 그 인물 머리 위(화면 안으로 민다).
            var (px, py) = PositionOf(Script.TalkPerson);
            var (camX, camY) = Camera();
            int top = Math.Max(0, (MosesScene.MosesH - _bgH) / 2);
            int bx = Math.Clamp(px - camX - 130, 4, MosesScene.MosesW - 264), by = Math.Clamp(py - camY + top - 150, 4, MosesScene.MosesH - 104);
            shell.DrawTalkBox(ox + bx, oy + by, 260, 100, bubble.Speaker, bubble.Text);
        }
        else if (Script.Talk is { } talk2) shell.DrawTalk(ox, oy, talk2.Speaker, talk2.Text, Script.TalkSlot, Script.Faces);
        else if (Script.Choice is { } choice)
        {
            // 900 물음 + 901 고를 줄 — 고르면 장면 변수 = 1부터 센 차례(0x100aa690).
            shell.DrawTalkBox(ox + 64, oy + 336, 511, 111, choice.Speaker, choice.Text);
            for (int i = 0; i < choice.Options.Count; i++)
            {
                var (x, y, w, h) = ChoiceRow(i, choice.Options.Count);
                shell.DrawButton(ox + x, oy + y, w, h, choice.Options[i], i == _choiceHover);
            }
        }
        else if (Script.Idle) host.UiText("누르면 돌아갑니다", ox + 500, oy + 456, 0xFFB0C0D8, 12);
    }
}

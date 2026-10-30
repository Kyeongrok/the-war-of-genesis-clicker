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
        SpriteMotion = StartMotion,
        EffectStart = StartEffect,
        SpriteFreeze = n => { if (n >= 0 && n < _spots.Length) _frozen[n] = MotionTick(n); },
        SpriteVisible = (n, shown) => { if (n >= 0 && n < _spots.Length) _hidden[n] = !shown; },
        CameraPan = (dx, dy, ticks) => { _cam = Camera(); _pan = (_cam.X, _cam.Y, dx, dy, Math.Max(1, ticks), host._lastTime); },
        IsField = true,
        PictureShow = (id, x, y, talk) => { var (px, w, h) = ReadImage(id); _cover = new Cover(px, w, h, x, y, 0, talk); },
        ColorShow = color => _cover = new Cover(null, 0, 0, 0, 0, color switch { 0 => 0xFFFFFFFFu, 2 => 0xFFFF0000u, _ => 0xFF000000u }, false),
        PictureClear = () => _cover = null,
        Wipe = StartWipe,
        SpriteFlip = n => { if (n >= 0 && n < _flip.Length) _flip[n] = !_flip[n]; },
        SpriteShadow = (n, on) => { if (n >= 0 && n < _shadow.Length) _shadow[n] = on; },
        CameraSet = (x, y) => { _pan = null; _cam = (x, y); },
    };

    /// <summary>화면 가득 덮는 것 — 그림(BGRs, 그림 안 자리 X·Y 부터) 또는 한 빛깔(그림이 없을 때). 덮인 동안 필드 배경·인물은 안 그린다(0x100c7cfd).</summary>
    private sealed record Cover(uint[]? Px, int W, int H, int X, int Y, uint Color, bool ShowTalk);

    /// <summary>지금 덮개(100 · 101 이 놓고 102 · 103 [1, …] 이 걷는다). null 이면 필드.</summary>
    private Cover? _cover;

    /// <summary>지난 틀에 그린 장면(대사 창 없이, 640×480) — 화면 바꾸기의 「옛 화면」(0x1009b0f0 이 뜨는 화면).</summary>
    private uint[] _lastScene = [];

    /// <summary>도는 화면 바꾸기(103~107)와 그 시작 시각 — 사는 동안 필드는 이것만 그린다(0x100c7ce0).</summary>
    private (Part1Wipe Wipe, double Start)? _wipe;

    /// <summary>
    /// 103~107 [갈래, …] — 옛 화면을 떠 두고 개체를 건다(처리기 0x100cf2c0 · cf540 · cf730 · cf920 · cfb20). 갈래 0 은 덮개(100 · 101)가 새 화면,
    /// 1 은 지금 필드를 그려 덮개로 삼았다가 끝에 지운다(0x100cf502) — 리메이크는 덮개를 곧 걷고 첫 틀의 필드를 떠 둔다. 돌려주는 값 = 칸에 남을 틱.
    /// </summary>
    private int StartWipe(int code, short[] args)
    {
        if (!Part1Wipe.Makes(code, args)) return 0;
        // 앞 바꾸기가 아직 돌면 그 끝 화면이 옛 화면이다(원본은 칸이 비어야 다음 행동이 돌아 겹치지 않는다).
        var old = _lastScene.Length == Part1Wipe.W * Part1Wipe.H ? (uint[])_lastScene.Clone() : new uint[Part1Wipe.W * Part1Wipe.H];
        _wipe = (new Part1Wipe(code, args, old), host._lastTime);
        if (args.Length > 0 && args[0] == 1) _cover = null;
        return Part1Wipe.Duration(code, args);
    }

    /// <summary>판의 (ox, oy) 640×480 을 <paramref name="px"/> 에 떠 온다(크기가 안 맞으면 새로).</summary>
    private uint[] CaptureScene(int ox, int oy, uint[] px)
    {
        const int W = Part1Wipe.W, H = Part1Wipe.H;
        if (px.Length != W * H) px = new uint[W * H];
        int stride = host.BoardWidth, rows = host.BoardHeight;
        for (int y = 0; y < H; y++)
        {
            int fy = oy + y;
            if ((uint)fy >= rows) { px.AsSpan(y * W, W).Fill(0xFF000000); continue; }
            for (int x = 0; x < W; x++) px[y * W + x] = (uint)(ox + x) < stride ? host._fb[fy * stride + ox + x] : 0xFF000000;
        }
        return px;
    }

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

    /// <summary>행동 300 이 건 모션(번호, 처음 뒤집기) — 있으면 방향·걷기로 고른 모션 대신 이것을 튼다. 뒤집기는 인물 칸 방향(3 = 오른쪽) ^ 306.</summary>
    private (int Motion, bool Mirror)?[] _motion = [];

    /// <summary>300 이 모션을 건 시각(모션은 0 틀부터) · 한 번만 돌고 끝 그림에서 멈추나(되풀이 ≠ 1).</summary>
    private double[] _motionStart = [];
    private bool[] _motionOnce = [];

    /// <summary>301 이 멈춘 그림 틱(멈추지 않았으면 null) — 300 이 새 모션을 걸면 푼다.</summary>
    private int?[] _frozen = [];

    /// <summary>300 [Obs &lt; 10000, …] 효과 그림 — 층(0 = 인물과 함께 아래부터 · 1 · 2 = 그 위), 자리(인물과 같은 배경 자리), 시작, 길이(틱).</summary>
    private sealed record Effect(int Obs, int Motion, int Layer, int X, int Y, double Start, int Length);
    private readonly List<Effect> _effects = [];

    private int SpriteObs(int n) => Part1Game.ObsBase + (_field is { } f && n < f.Sprites.Count ? f.Sprites[n].SpriteId : 0);

    /// <summary>지금 그릴 모션 틱 — 멈췄으면 그 틱, 300 이 건 모션이면 건 때부터(한 번이면 끝 그림에서 선다), 아니면 늘 도는 틱.</summary>
    private int MotionTick(int n)
    {
        if (_frozen[n] is int frozen) return frozen;
        int now = (int)(host._lastTime * GameWindow.TicksPerSecond);
        if (_motion[n] is not { } m) return now;
        int t = (int)((host._lastTime - _motionStart[n]) * GameWindow.TicksPerSecond);
        if (_motionOnce[n] && host.UiFor(SpriteObs(n))?.ShownLength(m.Motion) is > 0 and var len) t = Math.Min(t, len - 1);
        return t;
    }

    private double StartMotion(int n, int motion, bool loop)
    {
        if (n < 0 || n >= _spots.Length || _field is not { } field) return 0;
        motion = Part1Game.ConvertedMotion(field.Sprites[n].SpriteId, motion);   // 대본은 원본 모션 번호 — 인물 그림은 파트2 동작으로 옮겨 쌌다(mo-36)
        _motion[n] = (motion, field.Sprites[n].Direction == 3);
        _motionStart[n] = host._lastTime;
        _motionOnce[n] = !loop;
        _frozen[n] = null;
        if (loop) return 0;
        return (host.UiFor(SpriteObs(n))?.ShownLength(motion) ?? 0) / GameWindow.TicksPerSecond;
    }

    private double StartEffect(int obs, int motion, int layer, int x, int y)
    {
        if (obs <= 0) return 0;
        int len = Math.Max(1, host.UiFor(Part1Game.ObsBase + obs)?.ShownLength(motion) ?? 1);
        _effects.Add(new Effect(Part1Game.ObsBase + obs, motion, layer, x, y, host._lastTime, len));
        return len / GameWindow.TicksPerSecond;
    }

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

    /// <summary>시험용 <c>DUELDX_P1FIELD=&lt;필드&gt;</c> — 켜자마자 파트1 로 들어가 그 필드를 연다(화면 밖에서 말풍선 · 연출을 보려고).</summary>
    internal void OpenIfAsked()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("DUELDX_P1FIELD"), out int id) || id <= 0 || Part1Game.Root is null) return;
        host.LeaveScene(() => { shell.Open(); Open(id, () => { }); });
    }

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
        _motionStart = new double[field.Sprites.Count];
        _motionOnce = new bool[field.Sprites.Count];
        _frozen = new int?[field.Sprites.Count];
        _effects.Clear();
        _hidden = new bool[field.Sprites.Count];
        _flip = new bool[field.Sprites.Count];
        _shadow = new bool[field.Sprites.Count];
        _cover = null;
        _wipe = null;
        _lastScene = [];
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
                shell.ChainTo(1, battle);                                 // 사슬 자리 — 이 필드는 지났다(전투를 마치면 챕터로)
                host.LeaveScene(() => shell.StartBattle(battle));   // 필드 안의 전투 — 마치면 챕터로 돌아간다
                return true;
            case 501:                                                 // 다음 필드(0x100cfeb0 → 장면 5). 503 은 마을 갈래 낱말이라 필드를 안 넘긴다(0x100d09a0).
                int next = c.Args[0];
                var onEnd = _onEnd;
                Script.StopSounds();
                shell.ChainTo(2, next);
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
        if (BubbleTalk) { PumpBubble(); Bubble.Click(); }
        else if (Script.Talk != null) { if (!shell.NextTalkPage()) Script.Advance(); }
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
        PumpBubble();
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (_field is { } field)
        {
            // 화면 바꾸기(103~107)가 도는 동안은 그것만 그린다(0x100c7ce0) — 새 화면은 시작한 틀에 덮개 · 필드를 한 번 그려 떠 둔다.
            if (_wipe is { } wiping)
            {
                var wipe = wiping.Wipe;
                if (wipe.New == null) { DrawScene(field, ox, oy); wipe.New = CaptureScene(ox, oy, []); }
                for (int due = (int)((host._lastTime - wiping.Start) * GameWindow.TicksPerSecond); wipe.Ticks < due && !wipe.Done;) wipe.Step();
                if (wipe.Done) _wipe = null;
                else
                {
                    wipe.Draw(host._fb, host.BoardWidth, host.BoardHeight, ox, oy);
                    _lastScene = CaptureScene(ox, oy, _lastScene);
                    DrawTalkLayer(ox, oy);
                    host.DrawToast();
                    return;
                }
            }
            DrawScene(field, ox, oy);
            _lastScene = CaptureScene(ox, oy, _lastScene);
        }
        DrawTalkLayer(ox, oy);
        host.DrawToast();
    }

    /// <summary>장면 — 덮개(100 · 101)가 있으면 그것, 없으면 배경 · 인물 · 효과 그림.</summary>
    private void DrawScene(Part1Field field, int ox, int oy)
    {
        const int W = MosesScene.MosesW, H = MosesScene.MosesH;
        // 화면 자리 — 머리 낱말 1·2 가 처음 자리, 행동 401 이 민다(Camera).
        var spots = Enumerable.Range(0, _spots.Length).Select(PositionOf).ToArray();
        var (camX, camY) = Camera();
        int top = Math.Max(0, (H - _bgH) / 2);   // 500 높이 그림은 가운데에
        // 덮개가 있으면 그것만(0x100c7cfd) — 필드 배경 · 인물은 안 그린다.
        if (_cover is { } cover) { DrawCover(cover, ox, oy); return; }
        if (_bg is { } bg)
            for (int y = 0; y < Math.Min(H, _bgH); y++)
                for (int x = 0; x < Math.Min(W, _bgW); x++)
                    host.SetPixel(ox + x, oy + top + y, bg[(camY + y) * _bgW + camX + x] | 0xFF000000);
        // 인물 — 서기(동작 0) · 걷는 중이면 걷기(동작 1) 모션을 방향대로(전투 인물 칸과 같은 방향 번호: 0 위 · 2 아래 · 3 오른쪽 · 그 밖 왼쪽).
        // 모션 번호 = 동작 × 3 + 방향(ObsMotionTable). 아래쪽에 선 사람을 나중에.
        int tick = (int)(host._lastTime * GameWindow.TicksPerSecond);
        // 효과 그림(300 [Obs < 10000]) — 다 돈 것은 지운다. 층 0 은 인물과 함께 발끝(y) 차례로, 층 1 · 2 는 그 위에 차례로(0x100c8120 은 층마다 y 로 줄 세워 그린다).
        _effects.RemoveAll(e => (host._lastTime - e.Start) * GameWindow.TicksPerSecond >= e.Length);
        void DrawEffect(Effect e) =>
            DrawObs(e.Obs, e.Motion, (int)((host._lastTime - e.Start) * GameWindow.TicksPerSecond), ox + e.X - camX, oy + top + e.Y - camY, loop: false);
        var people = Enumerable.Range(0, Math.Min(spots.Length, field.Sprites.Count)).Where(i => !_hidden[i]).Select(i => (Y: spots[i].Y, Person: i, Fx: (Effect?)null))
                               .Concat(_effects.Where(e => e.Layer <= 0).Select(e => (Y: e.Y, Person: -1, Fx: (Effect?)e))).OrderBy(d => d.Y).ToList();
        foreach (var (_, i, fx) in people)
        {
            if (fx != null) { DrawEffect(fx); continue; }
            var facing = _facing[i];
            int action = _walks.ContainsKey(i) ? ObsMotionTable.ActionWalk : ObsMotionTable.ActionStand;
            var (motion, mirror) = _motion[i] ?? (action * 3 + ObsMotionTable.DirectionOf(facing), facing == Facing.Right);
            var (x, y) = spots[i];
            int personTick = MotionTick(i);
            if (_shadow[i])
                host.DrawUi(Part1Game.ObsBase + ShadowObs, 0, tick, ox + x - camX, oy + top + y - camY, GameWindow.UiBlend.Darken, loop: true, fade: 0.55);   // 그림자 그림의 점은 어둡기 단계(0~31, 섞기 8) — 바탕을 어둡게
            DrawObs(Part1Game.ObsBase + field.Sprites[i].SpriteId, motion, personTick, ox + x - camX, oy + top + y - camY,
                    loop: _motion[i] == null || !_motionOnce[i], mirror: mirror ^ _flip[i]);
        }
        foreach (var e in _effects.Where(e => e.Layer > 0).OrderBy(e => e.Layer).ThenBy(e => e.Y)) DrawEffect(e);
    }

    /// <summary>
    /// 필드 인물 · 효과 그림 한 장 — 그 모션 틱의 섞기 키(종류 3)대로. 파트1 섞기 9 는 더하기(<c>0x1000b8d0</c> 색표 = min(31, 바탕 + 그림)),
    /// 다시 쌀 때 파트2 17 로 옮겼다. 빛 기둥(Obs 389 · 388, 필드 0072 · 0078)은 모든 장이 섞기 9 라 보통으로 찍으면 검은 기둥이 된다(사용자 보고).
    /// </summary>
    private void DrawObs(int obs, int motion, int tick, int x, int y, bool loop, bool mirror = false)
    {
        int key = 0;
        if (host.UiFor(obs) is { } sprite)
        {
            int len = sprite.ShownLength(motion);
            key = sprite.BlendAt(motion, len > 0 ? loop ? (tick % len + len) % len : Math.Min(tick, len - 1) : tick);
        }
        host.DrawUi(obs, motion, tick, x, y, GameWindow.BlendOf(key), loop: loop, fade: GameWindow.BlendFade(key), mirror: mirror);
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

    /// <summary>209 말풍선 · 210 해설 띠 — 원본 창 0x100a7ef0(분석-파트1-챕터 11.5절).</summary>
    private Part1Bubble Bubble => _bubble ??= new(host, new Part1MapArt(host));
    private Part1Bubble? _bubble;

    /// <summary>지금 대사가 209 · 210 인가.</summary>
    private bool BubbleTalk => Script.Talk != null && Script.TalkSlot is Part1SceneScript.BubbleSlot or Part1SceneScript.NarrationSlot;

    /// <summary>
    /// 새 209 · 210 이면 말풍선을 연다 — 자리는 그 순간 인물의 화면 자리(발밑, 0x100d00b8 인물 +0x30/+0x32)에 고정(그 뒤 인물이 걸어도 안 따라간다).
    /// 말풍선이 쪽을 다 찍고 정한 틱이 지나 닫히면 다음 행동으로.
    /// </summary>
    private void PumpBubble()
    {
        if (!BubbleTalk) { _bubble?.Close(); return; }
        if (!Bubble.IsOpen(Script.TalkSerial))
        {
            bool narration = Script.TalkSlot == Part1SceneScript.NarrationSlot;
            int ax = MosesScene.MosesW / 2, ay = MosesScene.MosesH / 2;
            if (!narration && Script.TalkPerson >= 0 && Script.TalkPerson < _spots.Length)
            {
                var (px, py) = PositionOf(Script.TalkPerson);
                var (camX, camY) = Camera();
                (ax, ay) = (px - camX, py - camY + Math.Max(0, (MosesScene.MosesH - _bgH) / 2));
            }
            Bubble.Open(Script.TalkSerial, narration, Script.TalkRaw, Script.TalkFace, Script.TalkTicks, ax, ay);
        }
        // 왼쪽 단추(패드 A)를 누르고 있으면 빨리 찍고, 다 찍으면 저절로 넘긴다 — 파트2 필드 대사(TalkBox.TalkHeld)와 같게(사용자 요청 fa-72).
        bool held = !GameWindow.Offscreen && ((Win32.GetKeyState(Win32.VK_LBUTTON) & 0x8000) != 0 || host.Pad.HeldNow(PadAction.Confirm));
        if (held && _holdLast >= 0) Bubble.Hold((host._lastTime - _holdLast) * GameWindow.TicksPerSecond);
        _holdLast = held ? host._lastTime : -1;
        if (Bubble.Update()) { Bubble.Close(); Script.Advance(); }
    }

    /// <summary>넘김 단추를 누르고 있던 지난 틀의 시각(−1 = 안 누름).</summary>
    private double _holdLast = -1;

    /// <summary>대사 · 고르기 · 글 쪽 창.</summary>
    private void DrawTalkLayer(int ox, int oy)
    {
        if (Script.Choice == null) shell.ClearOptions();                                       // 고르기가 닫히면 줄 찍기를 잊는다
        if (Script.Talk != null && Script.TalkSlot == Part1SceneScript.PageSlot)
            shell.DrawTableWindow(ox + 70, oy + 100, Script.PageRows);                               // 904 표 창(0x100d0440 · 0x100d04b3)
        else if (BubbleTalk) Bubble.Draw(ox, oy);                                                  // 209 말풍선 · 210 해설 띠(Part1Bubble)
        else if (Script.Talk != null) shell.DrawTalk(ox, oy, Script.TalkRaw, Script.TalkSlot, Script.Faces);
        else if (Script.Choice is { } choice)
        {
            // 900 물음 + 901 고를 줄 — 고르면 장면 변수 = 1부터 센 차례(0x100aa690).
            shell.DrawFormattedBox(ox + 64, oy + 336, 511, 111, choice.Raw, arrow: false);   // 물음 창도 꾸밈대로 찍는다(틱 0x100aa7e0)
            for (int i = 0; i < choice.Options.Count; i++)
            {
                var (x, y, w, h) = ChoiceRow(i, choice.Options.Count);
                shell.DrawOptionLine(i, ox + x, oy + y, w, h, choice.RawOptions[i], i == _choiceHover);
            }
        }
        else if (Script.Idle) host.UiText("누르면 돌아갑니다", ox + 500, oy + 456, 0xFFB0C0D8, 12);
    }
}

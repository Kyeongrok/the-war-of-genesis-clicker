using System.Drawing.Imaging;
using System.IO;
using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 필드 — 챕터 사건 503 이 여는 연출 장면(<see cref="Part1Field"/>). 배경 그림 위에서 사건 대사를 차례로 띄우고, 행동 500 이면 챕터로 돌아간다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 7절. 지금은 배경(<c>BGRs</c>, 0002 는 1300×500 파노라마 — 머리 낱말 1·2 자리에서 본다)과 대사만 옮겼다.
/// 인물 그림·걷기(300대)·화면 이동(400대)·소리(700대)는 아직 없다. Esc 는 필드를 건너뛴다.
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
        SceneChange = OnSceneChange,
        BackgroundChange = id => (_bg, _bgW, _bgH) = ReadImage(id),
        SpritePlace = (n, x, y) => { if (n >= 0 && n < _spots.Length) _spots[n] = (x, y); },
        SpriteShift = (n, dx, dy) => { if (n >= 0 && n < _spots.Length) _spots[n] = (_spots[n].X + dx, _spots[n].Y + dy); },
    };

    /// <summary>인물 자리(배경 그림 위 발끝) — 필드 인물 칸에서 시작해 행동 303 이 옮긴다.</summary>
    private (int X, int Y)[] _spots = [];
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
            case 503:
                int next = c.Args[0];
                var onEnd = _onEnd;
                host.LeaveScene(() => Open(next, onEnd ?? (() => { })));
                return true;
        }
        return false;
    }

    private void End()
    {
        GameWindow.TestRunTrace($"part1 field {_field?.Id} end");   // 시험 전용 줄
        var onEnd = _onEnd;
        _onEnd = null;
        host.LeaveScene(() => { _open = false; _bg = null; onEnd?.Invoke(); });
    }

    internal void OnClick()
    {
        if (Script.Talk != null) Script.Advance();
        else End();                                                         // 대사가 다 끝났는데 500 이 없으면 누르면 돌아간다
    }

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE) End();
        else if (key is Win32.VK_RETURN or Win32.VK_SPACE) OnClick();
    }

    /// <summary>파트1 <c>BGRs</c> 그림 한 장(크기 그대로).</summary>
    private (uint[]? Px, int W, int H) ReadImage(int id)
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
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (_field is { } field)
        {
            const int W = MosesScene.MosesW, H = MosesScene.MosesH;
            // 화면 자리 — 머리 낱말 1·2 가 처음 자리이고, 넓은 그림(0002 의 1300 폭 파노라마)에서는 인물들 가운데를 따라간다(화면 이동 401 은 아직 안 풀었다).
            int followX = _spots.Length > 0 ? (int)_spots.Average(p => p.X) - W / 2 : field.Header[1];
            int camX = Math.Clamp(_bgW > W && _spots.Length > 0 ? followX : field.Header[1], 0, Math.Max(0, _bgW - W)), camY = Math.Clamp((int)field.Header[2], 0, Math.Max(0, _bgH - H));
            int top = Math.Max(0, (H - _bgH) / 2);   // 500 높이 그림은 가운데에
            if (_bg is { } bg)
                for (int y = 0; y < Math.Min(H, _bgH); y++)
                    for (int x = 0; x < Math.Min(W, _bgW); x++)
                        host.SetPixel(ox + x, oy + top + y, bg[(camY + y) * _bgW + camX + x] | 0xFF000000);
            // 인물 — 서기 모션(동작 0)을 방향대로(전투 인물 칸과 같은 방향 번호: 0 위 · 2 아래 · 3 오른쪽 · 그 밖 왼쪽). 아래쪽에 선 사람을 나중에.
            int tick = (int)(host._lastTime * GameWindow.TicksPerSecond);
            foreach (int i in Enumerable.Range(0, Math.Min(_spots.Length, field.Sprites.Count)).OrderBy(i => _spots[i].Y))
            {
                var sprite = field.Sprites[i];
                var facing = sprite.Direction switch { 0 => Facing.Up, 2 => Facing.Down, 3 => Facing.Right, _ => Facing.Left };
                var (x, y) = _spots[i];
                host.DrawUi(Part1Game.ObsBase + sprite.SpriteId, ObsMotionTable.DirectionOf(facing), tick, ox + x - camX, oy + top + y - camY,
                            GameWindow.UiBlend.Alpha, loop: true, mirror: facing == Facing.Right);
            }
        }
        if (Script.Talk is { } talk) shell.DrawTalk(ox, oy, talk.Speaker, talk.Text);
        else host.UiText("누르면 돌아갑니다", ox + 500, oy + 456, 0xFFB0C0D8, 12);
        host.DrawToast();
    }
}

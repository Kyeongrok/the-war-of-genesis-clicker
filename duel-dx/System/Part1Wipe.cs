namespace DuelDx;

/// <summary>
/// 파트1 필드 화면 바꾸기 103~107 — 원본 개체(밑 <c>0x100ab280</c>)를 틱마다 그대로 돌린다. 옛 화면(행동이 돌 때 보이던 것, <c>0x1009b0f0</c> 로 떠 둔 것)과
/// 새 화면(갈래 0 = 덮개 100 · 101, 1 = 그때의 필드를 한 장 그려 떠 둔 것 — <c>0x100c7fc0</c> → <c>0x1009b0f0</c>)을 섞는다.
/// 개체가 사는 동안 필드는 배경 · 인물을 안 그리고 이것만 그린다(<c>0x100c7ce0</c>: <c>[0x101b4550]</c> 이 있으면 vt+0x34 만) — 두 화면 모두 멈춘 그림이다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 14.1 · 17절. 처리기 <c>0x100cf2c0</c>(103) · <c>cf540</c>(104) · <c>cf730</c>(105) · <c>cf920</c>(106) · <c>cfb20</c>(107):
/// 칸에 개체를 넣고 다음 부름부터 <c>+0x68</c>(끝)을 보아 지운다 — 갈래 1 이면 덮개도 지운다(<c>0x100cf502</c>). 화면은 640×480.
/// <list type="bullet">
/// <item>103 [갈래, 빛깔, 나감, 들어옴] <c>0x100ab310</c>: 단계 0→31(31/나감 씩) 동안 옛 화면을 빛깔로 바래고, 31→63(31/들어옴 씩) 동안 새 화면을 63−단계로 드러낸다.</item>
/// <item>104 [갈래, 띠, 틱] <c>0x100ab550</c>: 경계 p 가 0 → 640+띠 를 (640+띠)/틱 씩 — 왼쪽 [0, p) 는 새 화면, 오른쪽은 옛 화면, 경계 뒤 띠 폭은
/// 8 조각으로 옛 화면을 섞기 13 단계 1~8 로 얹는다(왼쪽일수록 옅다, <c>0x100ab620</c>).</item>
/// <item>105 [갈래, 쪽, 틱] <c>0x100ab880</c>: 쪽 0 아래 · 1 위 · 2 오른쪽 · 3 왼쪽에서 새 화면이 p 만큼 드러나고, 남은 곳은 그 가장자리 한 줄(칸)을 늘여 채운다 —
/// 옛 화면은 안 그린다(<c>0x100ab9c0</c>). p 는 한 걸음에서 시작해 (480 · 640)/틱 씩.</item>
/// <item>106 [갈래, 쪽, 폭, 빠르기, 사이] <c>0x100abf70</c>: 화면을 폭마다 띠로 나눠(쪽 0 · 1 = 가로 띠 위 · 아래부터, 2 · 3 = 세로 띠 왼 · 오른쪽부터)
/// 띠 i 가 i × 사이 틱에 움직이기 시작해 틱마다 빠르기만큼 새 화면을 밀어 넣는다(가로 띠는 왼쪽에서, 세로 띠는 위에서 — <c>0x100ac1b0</c>).</item>
/// <item>107 [갈래, 쪽, 폭, 빠르기, 사이, 빛깔] <c>0x100ac870</c>: 106 과 같은 띠마다 옛 띠를 가운데 줄로 눌러 접고(빈 곳은 빛깔 0 흰 · 1 검 · 2 빨, <c>0x1000ca60</c>)
/// 반을 넘으면 새 띠를 가운데에서 펼친다(<c>0x100aca70</c>). 늘이기는 <c>0x1000c200</c> 의 크기 다른 네모 옮기기.</item>
/// </list>
/// </remarks>
internal sealed class Part1Wipe
{
    internal const int W = 640, H = 480;

    private readonly int _code;
    private readonly short[] _a;
    private short A(int i) => i < _a.Length ? _a[i] : (short)0;

    /// <summary>옛 화면(행동이 돌 때 보이던 그림).</summary>
    internal readonly uint[] Old;

    /// <summary>새 화면 — 시작한 틀에 덮개 · 필드를 한 번 그려 떠 둔다. 아직 없으면 null.</summary>
    internal uint[]? New;

    /// <summary>끝났다(<c>+0x68</c>) — 처리기가 다음 부름에 지운다.</summary>
    internal bool Done { get; private set; }

    /// <summary>돈 틱 수(갱신 vt+0x30 부른 수).</summary>
    internal int Ticks { get; private set; }

    // 103 · 104 · 105 — 단계(float, 원본 +0x78 · +0x74 · +0x70)와 걸음
    private float _step, _incOut, _incIn, _speed;
    private readonly int _band, _dir;
    // 106 · 107 — 띠 수(+0x7c) · 띠마다 간 거리(+0x80 · +0x84) · 다 갔다(+0x84 · +0x88)
    private readonly int[] _off = [];
    private readonly int _count, _strip, _move, _gap;
    private int _age;
    private bool _allIn;

    internal Part1Wipe(int code, short[] args, uint[] old)
    {
        _code = code;
        _a = args;
        Old = old;
        switch (code)
        {
            case 103:
                _incOut = 31f / A(2);                                  // fild · fdivr 31.0(0x1010ab50) — 0 이면 무한(곧바로 31)
                _incIn = 31f / A(3);
                break;
            case 104:
                _band = A(1);
                _speed = (float)(W + _band) / A(2);                    // (화면 폭 + 띠) / 틱(0x100ab59b)
                break;
            case 105:
                _dir = A(1);
                if (_dir is 0 or 1) _speed = (float)H / A(2);          // 쪽 0 · 1 = 높이 / 틱, 2 · 3 = 폭 / 틱(0x100ab8fc 표)
                else if (_dir is 2 or 3) _speed = (float)W / A(2);
                _step = _speed;                                        // +0x70 = +0x6c — 한 걸음 드러난 채 시작
                break;
            case 106 or 107:
                _dir = A(1);
                _strip = Math.Max(1, (int)A(2));                       // 원본은 0 이면 나눗셈에서 죽는다 — 1 로 막는다
                _move = A(3);
                _gap = Math.Max(1, (int)A(4));
                _count = (_dir is 0 or 1 ? H - 1 : W - 1) / _strip + 1;   // (높이 · 폭 − 1) / 폭 + 1(0x100ac001 · 0x100ac90b)
                _off = new int[_count];
                break;
        }
    }

    /// <summary>107 의 빈 곳 빛깔 — 인자 5: 0 흰(0x7fff) · 1 검(0) · 2 빨(0x7c00). 그 밖이면 처리기가 개체를 안 만든다(0x100cfc5d).</summary>
    internal static bool Makes(int code, short[] args) => code != 107 || args.Length > 5 && args[5] is >= 0 and <= 2;

    /// <summary>끝날 때까지 도는 틱 수 + 처리기가 지우는 한 틱 — 행동 1 이 그동안 기다린다. 너무 길면(틀린 인자) 30초로 자른다.</summary>
    internal static int Duration(int code, short[] args)
    {
        var probe = new Part1Wipe(code, args, []);
        while (!probe.Done && probe.Ticks < 900) probe.Step();
        return probe.Ticks + 1;
    }

    /// <summary>틱 하나(개체 갱신 — 103 <c>0x100ab380</c> · 104 <c>ab5b0</c> · 105 <c>ab920</c> · 106 <c>ac0f0</c> · 107 <c>ac9f0</c>).</summary>
    internal void Step()
    {
        if (Done) return;
        Ticks++;
        switch (_code)
        {
            case 103:
                if (_step == 63f) { Done = true; return; }
                if (_step < 31f) { _step += _incOut; if (_step >= 31f) _step = 31f; }
                else { _step += _incIn; if (_step > 63f) _step = 63f; }
                return;
            case 104:
            {
                float end = W + _band;
                if (end == _step) { Done = true; return; }
                _step += _speed;
                if (_step > end) _step = end;
                return;
            }
            case 105:
            {
                if (_dir is < 0 or > 3) { Done = true; return; }       // 표 밖 쪽 — 원본은 걸음이 정해지지 않는다(이 쪽은 자료에 없다)
                float end = _dir is 0 or 1 ? H : W;
                if (end == _step) { Done = true; return; }
                _step += _speed;
                if (_step > end) _step = end;
                return;
            }
            case 106 or 107:
            {
                if (_allIn) { Done = true; return; }
                _age++;                                                // 0x1009f130 — 미룸(+0x46) 0 이면 나이 +1
                int moving = Math.Min(_age / _gap + 1, _count), full = 0;
                // 106 은 띠가 화면 끝(가로 띠 640 · 세로 띠 480)까지, 107 은 띠 폭까지 간다. 끝을 넘은 띠만 센다(같으면 다음 틱에 센다).
                int limit = _code == 107 ? _strip : _dir is 0 or 1 ? W : H;
                for (int i = 0; i < moving; i++)
                {
                    _off[i] += _move;
                    if (_off[i] > limit) { _off[i] = limit; full++; }
                }
                if (full == _count) _allIn = true;
                return;
            }
            default:
                Done = true;
                return;
        }
    }

    /// <summary>원본 _ftol — 0 쪽으로 자른다.</summary>
    private static int Ftol(float v) => float.IsInfinity(v) || float.IsNaN(v) ? (v > 0 ? int.MaxValue / 4 : 0) : (int)v;

    /// <summary>
    /// 판(<paramref name="fb"/>, 폭 <paramref name="stride"/>)의 (<paramref name="ox"/>, <paramref name="oy"/>) 에 지금 틱의 화면을 그린다(vt+0x34).
    /// </summary>
    internal void Draw(uint[] fb, int stride, int rows, int ox, int oy)
    {
        var neu = New ?? Old;
        void Put(int x, int y, uint c)
        {
            int fx = ox + x, fy = oy + y;
            if ((uint)x < W && (uint)y < H && (uint)fx < stride && (uint)fy < rows) fb[fy * stride + fx] = c | 0xFF000000;
        }
        uint Px(uint[] src, int x, int y) => src.Length == W * H ? src[Math.Clamp(y, 0, H - 1) * W + Math.Clamp(x, 0, W - 1)] : 0xFF000000;
        void Copy(uint[] src, int x0, int y0, int x1, int y1, int sx, int sy)   // [x0, x1) × [y0, y1) ← src (sx, sy) 부터
        {
            for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++) Put(x, y, Px(src, sx + x - x0, sy + y - y0));
        }
        void Fill(int x0, int y0, int x1, int y1, uint c)
        {
            for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++) Put(x, y, c);
        }
        // 크기 다른 네모 옮기기(0x1000c200 → 늘이기 갈래) — 가장 가까운 점(가설: 원본 늘이기의 보간은 안 봤다).
        void Stretch(uint[] src, int dx0, int dy0, int dx1, int dy1, int sx0, int sy0, int sx1, int sy1)
        {
            int dw = dx1 - dx0, dh = dy1 - dy0, sw = sx1 - sx0, sh = sy1 - sy0;
            if (dw <= 0 || dh <= 0 || sw <= 0 || sh <= 0) return;
            for (int y = Math.Max(0, dy0); y < Math.Min(H, dy1); y++)
                for (int x = Math.Max(0, dx0); x < Math.Min(W, dx1); x++)
                    Put(x, y, Px(src, sx0 + (x - dx0) * sw / dw, sy0 + (y - dy0) * sh / dh));
        }

        switch (_code)
        {
            case 103:
            {
                // 단계 < 32 면 옛 화면을 빛깔 섞기 · 세기 단계로, 넘으면 새 화면을 63 − 단계로(0x100ab3e0). 31 을 넘는 세기는 원본이 안 그린다.
                // 빛깔 0 = 섞기 0xe(흰 스크린: c + L − c·L/31) · 1 = 0xf(검게) · 2 = 0x20(검게로 갈음 — 가설).
                bool oldSide = _step < 32f;
                int level = oldSide ? Ftol(_step) : 63 - Ftol(_step);
                if (level is < 0 or > 31) return;
                var src = oldSide ? Old : neu;
                bool white = A(1) == 0;
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        uint c = Px(src, x, y);
                        uint Ch(int s)
                        {
                            int v = (int)(c >> s & 0xFF);
                            return (uint)(white ? v + level * (255 - v) / 31 : v * (31 - level) / 31);
                        }
                        Put(x, y, Ch(16) << 16 | Ch(8) << 8 | Ch(0));
                    }
                return;
            }
            case 104:
            {
                int p = Ftol(_step);
                Copy(neu, 0, 0, Math.Min(p, W), H, 0, 0);
                Copy(Old, p, 0, W, H, p, 0);
                // 경계 뒤 띠 — 8 조각, 조각 i 는 옛 화면을 섞기 13 단계 i+1 로(단계 k = 그림 4k/31, 8 이면 거의 불투명).
                float back = _step - _band;
                for (int i = 0; i < 8; i++)
                {
                    int left = Ftol(_band * i / 8 + back), right = Ftol(_band * (i + 1) / 8 + back);
                    double alpha = Math.Min(1, 4.0 * (i + 1) / 31);
                    for (int y = 0; y < H; y++)
                        for (int x = Math.Max(0, left); x < Math.Min(W, right); x++)
                        {
                            uint o = Px(Old, x, y), n = Px(neu, x, y);
                            uint Mix(int s) => (uint)Math.Round((o >> s & 0xFF) * alpha + (n >> s & 0xFF) * (1 - alpha));
                            Put(x, y, Mix(16) << 16 | Mix(8) << 8 | Mix(0));
                        }
                }
                return;
            }
            case 105:
            {
                int p = Ftol(_step);
                switch (_dir)
                {
                    case 0:                                            // 아래에서 — [H − p, H) 새 화면, 위는 H − p 줄을 늘여
                        Copy(neu, 0, H - p, W, H, 0, H - p);
                        for (int y = 0; y < H - _step; y++) Copy(neu, 0, y, W, y + 1, 0, H - p);
                        break;
                    case 1:                                            // 위에서 — [0, p) 새 화면, 아래는 p − 1 줄을 늘여
                        Copy(neu, 0, 0, W, p, 0, 0);
                        for (int y = p; y < H; y++) Copy(neu, 0, y, W, y + 1, 0, p - 1);
                        break;
                    case 2:                                            // 오른쪽에서 — [W − p, W) 새 화면, 왼쪽은 W − p 칸을 늘여
                        Copy(neu, W - p, 0, W, H, W - p, 0);
                        for (int x = 0; x < W - p; x++) Copy(neu, x, 0, x + 1, H, W - p, 0);
                        break;
                    case 3:                                            // 왼쪽에서 — [0, p) 새 화면, 오른쪽은 p − 1 칸을 늘여
                        Copy(neu, 0, 0, p, H, 0, 0);
                        for (int x = p; x < W; x++) Copy(neu, x, 0, x + 1, H, p - 1, 0);
                        break;
                }
                return;
            }
            case 106:
                for (int i = 0; i < _count; i++)
                {
                    int k = _dir is 1 or 3 ? _count - 1 - i : i, off = _off[i];
                    int a0 = k * _strip, a1 = Math.Min((k + 1) * _strip, _dir is 0 or 1 ? H : W);
                    if (_dir is 0 or 1)
                    {
                        Copy(neu, 0, a0, off, a1, W - off, a0);        // 새 화면의 오른쪽 끝부터 왼쪽에서 밀려 들어온다
                        Copy(Old, off, a0, W, a1, off, a0);
                    }
                    else
                    {
                        Copy(neu, a0, 0, a1, off, a0, H - off);        // 새 화면의 아래 끝부터 위에서 밀려 내려온다
                        Copy(Old, a0, off, a1, H, a0, off);
                    }
                }
                return;
            case 107:
            {
                uint color = A(5) switch { 0 => 0xFFFFFFFF, 2 => 0xFFFF0000, _ => 0xFF000000 };
                bool across = _dir is 0 or 1;
                int span = across ? H : W;
                for (int i = 0; i < _count; i++)
                {
                    int k = _dir is 1 or 3 ? _count - 1 - i : i, off = _off[i];
                    int top = k * _strip, mid = Math.Min(_strip / 2 + top, span);
                    int d = Math.Abs(off - mid + top);
                    int lo = Math.Min(mid - d, span), hi = Math.Min(mid + d, span), end = Math.Min(top + _strip, span);
                    // 반을 못 갔으면 옛 띠를 눌러, 넘었으면 새 띠를 펼쳐 [lo, hi) 에 — 위 · 아래 남는 곳은 빛깔로.
                    var src = off + top < mid ? Old : neu;
                    if (across)
                    {
                        Fill(0, top, W, lo, color);
                        Stretch(src, 0, lo, W, hi, 0, top, W, end);
                        Fill(0, hi, W, end, color);
                    }
                    else
                    {
                        Fill(top, 0, lo, H, color);
                        Stretch(src, lo, 0, hi, H, top, 0, end, H);
                        Fill(hi, 0, end, H, color);
                    }
                }
                return;
            }
        }
    }
}

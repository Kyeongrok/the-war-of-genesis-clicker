using System.Drawing;
using System.Runtime.InteropServices;
using DuelDx.Native;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace DuelDx;

/// <summary>
/// 창 해상도로 그리는 덧그림 층(사용자 요청 menu-55) — 판(640×480 안팎)을 배율만큼 늘려 찍으면 글자가 깍두기가 되므로,
/// 새 UI 는 <b>창 픽셀 그대로</b>인 이 층에 그리고 판 그림 위에 겹친다. 글자는 「크기 × 배율」로 새로 구워 어느 해상도에서도 또렷하다.
/// </summary>
/// <remarks>
/// 자리와 크기는 <b>판 좌표</b>(옛 UI 와 같은 수)로 받는다 — 그리는 쪽은 배치를 그대로 두고 그릴 곳만 바꾸면 되고, 마우스 · 패드의 누르는 칸도 그대로 맞는다.
/// 그리기는 그 자리에서 하지 않고 <b>명령으로 적어 두었다가</b> 화면에 낼 때 GPU 가 사각형으로 그린다 — 글 · 그림은 한 번 올린 텍스처를 다시 쓴다.
/// 전에는 CPU 가 틀마다 창 크기 그림을 지우고 합성해 통째로 올렸는데, 창이 클수록 느렸다(사용자 보고 menu-57). 화면 밖 시험의 스냅샷만
/// 같은 명령을 CPU 로 다시 그려 만든다(<see cref="Composite"/>).
/// </remarks>
internal sealed unsafe class HiCanvas(GameWindow host) : IDisposable
{
    private enum Kind : byte { Fill, Text, Image, Gradient, Round }

    /// <summary>그리기 명령 하나 — 자리는 창 픽셀. <c>Data</c> 는 글 · 그림의 점들 또는 그라데이션 색 줄.</summary>
    private struct Cmd
    {
        public Kind Kind;
        public int X0, Y0, X1, Y1;
        public uint Color;
        public uint[]? Data;
        public int Stride, W, H;        // 그림: 한 줄 점 수 · 쓰는 너비 · 높이 / 글: W · H
        public bool Opaque, Dim;
        public int Radius16;            // 둥근 네모: 반지름 × 16(창 픽셀)
        public float Tint;              // 장면 페이드가 곱한 밝기
        public bool Smooth;             // 그림을 겹선형으로 늘리고 줄인다(판 밑에 까는 고해상도 배경)
    }

    private readonly List<Cmd> _cmds = [];

    /// <summary>
    /// 판 <b>밑</b>에 까는 그림 — 고해상도 모세스 배경(사용자 요청 menu-74). 판을 올릴 때 배경 원본 그대로인 점은 투명하게 해(<see cref="UnderLow"/>)
    /// 그 자리에만 이 그림이 비친다 — 단추 · 글 · 효과가 그려진 점은 판 그대로다.
    /// </summary>
    private readonly List<Cmd> _under = [];

    /// <summary>밑깔기가 덮는 판의 원본 배경(640×480)과 그 판 좌표 — 이 배열과 같은 점이 투명해진다.</summary>
    internal (uint[] Low, int X, int Y, int W, int H)? UnderLow { get; private set; }

    internal bool HasUnder => _under.Count > 0;
    private int _w, _h;

    /// <summary>이번 틀에 이 층에 그린 것이 있나.</summary>
    internal bool Used { get; private set; }

    private readonly Dictionary<string, (uint[] Px, int W, int H)> _text = [];

    /// <summary>틀을 시작할 때 — 지난 틀에 적은 명령을 지운다.</summary>
    internal void Begin()
    {
        _cmds.Clear();
        _under.Clear();
        UnderLow = null;
        Used = false;
    }

    /// <summary>지금까지 그린 것을 num/den 으로 어둡게 — 장면 페이드가 판과 함께 층도 어둡게 할 때. 덮는 세기(알파)는 그대로 둔다.</summary>
    internal void Dim(int num, int den)
    {
        var span = CollectionsMarshal.AsSpan(_cmds);
        for (int i = 0; i < span.Length; i++) span[i].Tint *= (float)num / den;
        var under = CollectionsMarshal.AsSpan(_under);
        for (int i = 0; i < under.Length; i++) under[i].Tint *= (float)num / den;
    }

    private void Add(Cmd cmd)
    {
        (_w, _h) = (host._resW, host._resH);
        if (cmd.X1 <= cmd.X0 || cmd.Y1 <= cmd.Y0 || cmd.X0 >= _w || cmd.Y0 >= _h || cmd.X1 <= 0 || cmd.Y1 <= 0) return;
        cmd.Tint = 1;
        _cmds.Add(cmd);
        Used = true;
    }

    private double Zoom => host._zoom;

    /// <summary>판 좌표 → 창 픽셀.</summary>
    internal int Sx(double boardX) => (int)Math.Round((boardX - host._camX) * Zoom) + host.ViewOffsetX;
    internal int Sy(double boardY) => (int)Math.Round((boardY - host._camY) * Zoom) + host.ViewOffsetY;

    /// <summary>선 굵기(창 픽셀) — 판의 한 점만큼.</summary>
    private int Line => Math.Max(1, (int)Math.Round(Zoom));

    internal void FillRect(int x, int y, int w, int h, uint color) => FillPx(Sx(x), Sy(y), Sx(x + w), Sy(y + h), color);

    private void FillPx(int x0, int y0, int x1, int y1, uint color)
    {
        if (color >> 24 != 0) Add(new Cmd { Kind = Kind.Fill, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Color = color });
    }

    internal void StrokeRect(int x, int y, int w, int h, uint color)
    {
        int x0 = Sx(x), y0 = Sy(y), x1 = Sx(x + w), y1 = Sy(y + h), t = Line;
        FillPx(x0, y0, x1, y0 + t, color);
        FillPx(x0, y1 - t, x1, y1, color);
        FillPx(x0, y0 + t, x0 + t, y1 - t, color);
        FillPx(x1 - t, y0 + t, x1, y1 - t, color);
    }

    /// <summary>
    /// 그림(Obs 한 장)을 판 좌표 자리에 찍는다 — 판의 한 점이 배율만큼의 네모가 된다(가장 가까운 점). 그림은 또렷해질 수 없으니 판에 찍던 것과 같은 모양이고,
    /// 층에 그린 창 위에 놓이게 하려는 것이다. 투명이 아닌 점은 불투명으로 덮는다(판의 <c>DrawUi</c> 와 같다).
    /// </summary>
    /// <param name="stride">그림 한 줄의 점 수 — <paramref name="w"/> 가 그보다 작으면 왼쪽 일부만 찍는다.</param>
    /// <param name="dw">찍을 크기(판 좌표) — 0 이면 그림 크기 그대로.</param>
    /// <param name="opaque">투명한 점도 덮는다(배경 그림).</param>
    /// <param name="dim">15/31 로 어둡게(꺼진 줄의 아이콘 — 판의 <c>UiBlend.Dim</c>).</param>
    /// <remarks>점들(<paramref name="src"/>)은 한 번 올린 뒤 다시 쓰므로, 같은 배열의 내용을 틀마다 바꾸는 그림에는 쓰지 않는다.</remarks>
    internal void DrawImage(uint[] src, int stride, int w, int h, int x, int y, int dw = 0, int dh = 0, bool opaque = false, bool dim = false)
    {
        if (w <= 0 || h <= 0 || stride <= 0 || src.Length < stride * h) return;
        int x0 = Sx(x), y0 = Sy(y);
        Add(new Cmd
        {
            Kind = Kind.Image, X0 = x0, Y0 = y0, X1 = Sx(x + (dw > 0 ? dw : w)), Y1 = Sy(y + (dh > 0 ? dh : h)),
            Data = src, Stride = stride, W = w, H = h, Opaque = opaque, Dim = dim, Color = 0xFFFFFFFF,
        });
    }

    /// <summary>
    /// 판 밑에 큰 그림(<paramref name="hi"/>, 키운 배경)을 깐다 — 판 좌표 (<paramref name="x"/>, <paramref name="y"/>) 부터 <paramref name="low"/> 크기만큼.
    /// 판에서 <paramref name="low"/> 와 같은 점만 투명해져 이 그림이 보인다.
    /// </summary>
    internal void DrawUnder(uint[] hi, int hiW, int hiH, uint[] low, int x, int y, int w, int h)
    {
        (_w, _h) = (host._resW, host._resH);
        var cmd = new Cmd
        {
            Kind = Kind.Image, X0 = Sx(x), Y0 = Sy(y), X1 = Sx(x + w), Y1 = Sy(y + h),
            Data = hi, Stride = hiW, W = hiW, H = hiH, Opaque = true, Smooth = true, Color = 0xFFFFFFFF, Tint = 1,
        };
        if (cmd.X1 <= cmd.X0 || cmd.Y1 <= cmd.Y0) return;
        _under.Add(cmd);
        UnderLow = (low, x, y, w, h);
    }

    /// <summary>위에서 아래로 색이 바뀌는 네모 — <paramref name="column"/> 의 색들을 높이에 맞춰 늘리고 사이를 부드럽게 잇는다. 알파(0~255)로 반투명하게 덮는다.</summary>
    internal void FillGradient(int x, int y, int w, int h, uint[] column, uint alpha)
    {
        if (column.Length == 0 || alpha == 0) return;
        Add(new Cmd { Kind = Kind.Gradient, X0 = Sx(x), Y0 = Sy(y), X1 = Sx(x + w), Y1 = Sy(y + h), Data = column, Color = alpha << 24 | 0xFFFFFF });
    }

    /// <summary>모서리가 둥근 네모 — 가장자리를 창 픽셀에서 부드럽게 깎는다(판에서는 원을 점으로 찍어 계단이 졌다).</summary>
    internal void FillRound(int x, int y, int w, int h, int r, uint color)
    {
        int x0 = Sx(x), y0 = Sy(y), x1 = Sx(x + w), y1 = Sy(y + h);
        double rad = Math.Min(r * Zoom, Math.Min(x1 - x0, y1 - y0) / 2.0);
        if (color >> 24 != 0) Add(new Cmd { Kind = Kind.Round, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Color = color, Radius16 = (int)Math.Round(rad * 16) });
    }

    /// <summary>글의 크기를 판 좌표로 — 가운데 · 오른쪽 맞춤 셈에 쓴다.</summary>
    internal (int W, int H) Measure(string text, uint color, float size = 13f, string? face = null, bool bold = true)
    {
        var (_, w, h) = Glyphs(text, color, size, face, bold);
        return ((int)Math.Ceiling(w / Zoom), (int)Math.Ceiling(h / Zoom));
    }

    internal void DrawText(string text, int x, int y, uint color, float size = 13f, string? face = null, bool bold = true)
    {
        if (text.Length == 0) return;
        var (px, w, h) = Glyphs(text, color, size, face, bold);
        if (w <= 0 || h <= 0) return;
        int left = Sx(x), top = Sy(y) + (int)Math.Round(host.FontShift(size) * Zoom);
        Add(new Cmd { Kind = Kind.Text, X0 = left, Y0 = top, X1 = left + w, Y1 = top + h, Data = px, W = w, H = h, Color = 0xFFFFFFFF });
    }

    /// <summary>글을 「크기 × 글자 크기 설정 × 배율」로 굽는다 — 배율이나 설정이 바뀌면 열쇠가 달라져 새로 굽는다.</summary>
    private (uint[] Px, int W, int H) Glyphs(string text, uint color, float size, string? face = null, bool bold = true)
    {
        float real = (float)Math.Round(host.FontSize(size) * Zoom * 4) / 4;
        string key = text + ":" + color + ":" + real + (face == null && bold ? "" : ":" + face + ":" + bold);
        if (_text.TryGetValue(key, out var cached)) return cached;
        var made = TextRaster.Render(text, Color.FromArgb((int)color), real, face, bold) ?? ([], 0, 0);
        if (_text.Count > 400) _text.Clear();
        return _text[key] = made;
    }

    // ── GPU 로 그리기 ─────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public float X, Y, U, V; public uint Color; }

    private sealed class Tex(ID3D11Texture2D texture, ID3D11ShaderResourceView view, int w, int h) : IDisposable
    {
        public readonly ID3D11ShaderResourceView View = view;
        public readonly int W = w, H = h;
        public void Dispose() { View.Dispose(); texture.Dispose(); }
    }

    private ID3D11VertexShader? _vs;
    private ID3D11PixelShader? _ps;
    private ID3D11InputLayout? _layout;
    private ID3D11BlendState? _blend;
    private ID3D11RasterizerState? _raster;
    private ID3D11SamplerState? _point, _linear;
    private ID3D11Buffer? _vb;
    private int _vbVerts;
    private Tex? _white;

    /// <summary>글 · 그림 · 그라데이션의 텍스처 — 점 배열(같은 배열이면 같은 그림)과 찍는 방식으로 찾는다.</summary>
    private readonly Dictionary<(uint[] Data, int Stride, int Mode), Tex> _textures = new(new DataKey());
    private readonly Dictionary<(int W, int H, int Radius16, uint Color), Tex> _rounds = [];

    private sealed class DataKey : IEqualityComparer<(uint[] Data, int Stride, int Mode)>
    {
        public bool Equals((uint[] Data, int Stride, int Mode) a, (uint[] Data, int Stride, int Mode) b) => ReferenceEquals(a.Data, b.Data) && a.Stride == b.Stride && a.Mode == b.Mode;
        public int GetHashCode((uint[] Data, int Stride, int Mode) k) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Data), k.Stride, k.Mode);
    }

    private void CreateDeviceObjects()
    {
        var device = host._device;
        const string shader = """
            Texture2D<float4> T : register(t0);
            SamplerState S : register(s0);
            struct VI { float2 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR0; };
            struct VO { float4 pos : SV_Position; float2 uv : TEXCOORD0; float4 col : COLOR0; };
            VO VS(VI i) { VO o; o.pos = float4(i.pos, 0, 1); o.uv = i.uv; o.col = i.col; return o; }
            float4 PS(VO i) : SV_Target { return T.Sample(S, i.uv) * i.col; }
            """;
        var vsBlob = Compiler.Compile(shader, "VS", "hicanvas.hlsl", "vs_4_0");
        var psBlob = Compiler.Compile(shader, "PS", "hicanvas.hlsl", "ps_4_0");
        _vs = device.CreateVertexShader(vsBlob.Span);
        _ps = device.CreatePixelShader(psBlob.Span);
        _layout = device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
            new InputElementDescription("COLOR", 0, Format.B8G8R8A8_UNorm, 16, 0),
        ], vsBlob.Span);
        _blend = device.CreateBlendState(BlendDescription.NonPremultiplied);
        _raster = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _point = device.CreateSamplerState(SamplerDescription.PointClamp);
        _linear = device.CreateSamplerState(SamplerDescription.LinearClamp);
        _white = Upload([0xFFFFFFFF], 1, 1);
    }

    private Tex Upload(ReadOnlySpan<uint> px, int w, int h)
    {
        var device = host._device;
        var texture = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.Write,
        });
        var map = host._ctx.Map(texture, 0, MapMode.WriteDiscard);
        try
        {
            for (int y = 0; y < h; y++) px.Slice(y * w, w).CopyTo(new Span<uint>((void*)(map.DataPointer + y * map.RowPitch), w));
        }
        finally { host._ctx.Unmap(texture, 0); }
        return new Tex(texture, device.CreateShaderResourceView(texture), w, h);
    }

    /// <summary>명령의 텍스처 — 처음 쓰는 것이면 올린다. 그림은 투명(알파 0)이 아닌 점을 불투명으로 바꿔 올린다(판의 가림 방식).</summary>
    private Tex TextureFor(in Cmd c)
    {
        switch (c.Kind)
        {
            case Kind.Fill: return _white!;
            case Kind.Round:
            {
                var key = (c.X1 - c.X0, c.Y1 - c.Y0, c.Radius16, c.Color);
                if (!_rounds.TryGetValue(key, out var round)) _rounds[key] = round = Upload(RoundPixels(key.Item1, key.Item2, c.Radius16 / 16.0, c.Color), key.Item1, key.Item2);
                return round;
            }
        }
        var data = c.Data!;
        int mode = c.Kind == Kind.Image ? 1 + (c.Opaque ? 1 : 0) + (c.Dim ? 2 : 0) : c.Kind == Kind.Gradient ? 5 : 0;
        int stride = c.Kind == Kind.Image ? c.Stride : c.Kind == Kind.Gradient ? 1 : c.W;
        if (_textures.TryGetValue((data, stride, mode), out var tex)) return tex;
        int rows = data.Length / stride;
        if (c.Kind == Kind.Text) tex = Upload(data, stride, rows);
        else
        {
            var fixedPx = new uint[stride * rows];
            for (int i = 0; i < fixedPx.Length; i++)
            {
                uint p = data[i];
                bool shown = c.Kind == Kind.Gradient || c.Opaque || (p & 0xFF000000) != 0;
                fixedPx[i] = !shown ? 0 : c.Dim ? GameWindow.ScaleColor(p, 15, 31) : p | 0xFF000000;
            }
            tex = Upload(fixedPx, stride, rows);
        }
        return _textures[(data, stride, mode)] = tex;
    }

    /// <summary>둥근 네모의 점들(곱하지 않은 알파) — 가장자리는 덮인 넓이만큼 흐리게.</summary>
    private static uint[] RoundPixels(int w, int h, double rad, uint color)
    {
        var px = new uint[w * h];
        double cx = w / 2.0, cy = h / 2.0, hx = w / 2.0 - rad, hy = h / 2.0 - rad;
        uint alpha = color >> 24;
        for (int y = 0; y < h; y++)
        {
            double dy = Math.Max(0, Math.Abs(y + 0.5 - cy) - hy);
            for (int x = 0; x < w; x++)
            {
                double dx = Math.Max(0, Math.Abs(x + 0.5 - cx) - hx);
                double cover = Math.Clamp(rad + 0.5 - Math.Sqrt(dx * dx + dy * dy), 0, 1);
                px[y * w + x] = (uint)(alpha * cover) << 24 | color & 0xFFFFFF;
            }
        }
        return px;
    }

    private Vertex[] _verts = [];

    /// <summary>판 그림을 그린 뒤, 화면에 내기 전에 — 적어 둔 명령을 창 전체에 차례대로 그린다.</summary>
    internal void Present()
    {
        if (!Used || _cmds.Count == 0) return;
        PresentCmds(_cmds);
    }

    /// <summary>판을 그리기 <b>전에</b> — 밑깔기를 그린다. 판은 그 뒤에 섞기(<see cref="Blend"/>)를 켜고 그린다.</summary>
    internal void PresentUnder()
    {
        if (_under.Count == 0) return;
        PresentCmds(_under);
    }

    /// <summary>알파 섞기 — 밑깔기 위에 판을 얹을 때 쓴다(밑깔기를 한 번 그린 뒤라 만들어져 있다).</summary>
    internal ID3D11BlendState? Blend => _blend;

    private void PresentCmds(List<Cmd> list)
    {
        if (_w <= 0 || _h <= 0) return;
        var device = host._device;
        var ctx = host._ctx;
        if (_vs == null) CreateDeviceObjects();
        // 텍스처가 너무 쌓이면 통째로 비운다(지나간 글 · 움직이는 그림의 장들) — 다음 틀부터 쓰는 것만 다시 올린다.
        if (_textures.Count > 1500) { foreach (var t in _textures.Values) t.Dispose(); _textures.Clear(); }
        if (_rounds.Count > 300) { foreach (var t in _rounds.Values) t.Dispose(); _rounds.Clear(); }

        var cmds = CollectionsMarshal.AsSpan(list);
        if (_verts.Length < cmds.Length * 6) _verts = new Vertex[cmds.Length * 6 * 2];
        var views = new ID3D11ShaderResourceView[cmds.Length];
        float sx = 2f / _w, sy = 2f / _h;
        for (int i = 0; i < cmds.Length; i++)
        {
            ref var c = ref cmds[i];
            var tex = TextureFor(c);
            views[i] = tex.View;
            float u1 = 1, v1 = 1;
            if (c.Kind == Kind.Image) (u1, v1) = ((float)c.W / tex.W, (float)c.H / tex.H);
            uint color = c.Color;
            float tint = c.Tint;
            if (tint < 1)
                color = color & 0xFF000000 | (uint)((color >> 16 & 0xFF) * tint) << 16 | (uint)((color >> 8 & 0xFF) * tint) << 8 | (uint)((color & 0xFF) * tint);
            float l = c.X0 * sx - 1, r = c.X1 * sx - 1, t = 1 - c.Y0 * sy, b = 1 - c.Y1 * sy;
            int at = i * 6;
            _verts[at] = new Vertex { X = l, Y = t, U = 0, V = 0, Color = color };
            _verts[at + 1] = new Vertex { X = r, Y = t, U = u1, V = 0, Color = color };
            _verts[at + 2] = new Vertex { X = l, Y = b, U = 0, V = v1, Color = color };
            _verts[at + 3] = _verts[at + 1];
            _verts[at + 4] = new Vertex { X = r, Y = b, U = u1, V = v1, Color = color };
            _verts[at + 5] = _verts[at + 2];
        }

        int count = cmds.Length * 6;
        if (_vb == null || _vbVerts < count)
        {
            _vb?.Dispose();
            _vbVerts = Math.Max(count * 2, 6 * 256);
            _vb = device.CreateBuffer(new BufferDescription((uint)(_vbVerts * sizeof(Vertex)), BindFlags.VertexBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        }
        var map = ctx.Map(_vb, 0, MapMode.WriteDiscard);
        try { _verts.AsSpan(0, count).CopyTo(new Span<Vertex>((void*)map.DataPointer, count)); }
        finally { ctx.Unmap(_vb, 0); }

        ctx.RSSetViewport(0, 0, _w, _h);
        ctx.RSSetState(_raster);
        ctx.IASetInputLayout(_layout);
        ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        ctx.IASetVertexBuffer(0, _vb, (uint)sizeof(Vertex));
        ctx.VSSetShader(_vs);
        ctx.PSSetShader(_ps);
        ctx.OMSetBlendState(_blend);
        // 같은 텍스처 · 같은 늘림 방식이 이어지는 명령은 한 번에 그린다.
        for (int i = 0; i < cmds.Length;)
        {
            bool smooth = cmds[i].Kind == Kind.Gradient || cmds[i].Smooth;
            int j = i + 1;
            while (j < cmds.Length && ReferenceEquals(views[j], views[i]) && (cmds[j].Kind == Kind.Gradient || cmds[j].Smooth) == smooth) j++;
            ctx.PSSetSampler(0, smooth ? _linear : _point);
            ctx.PSSetShaderResource(0, views[i]);
            ctx.Draw((uint)((j - i) * 6), (uint)(i * 6));
            i = j;
        }
        ctx.OMSetBlendState(null);
        ctx.RSSetState(null);
        ctx.IASetInputLayout(null);
    }

    // ── 스냅샷용 CPU 그리기 ───────────────────────────────────────────────────

    /// <summary>
    /// 판 그림과 이 층을 겹친 창 크기 그림(화면 밖 시험의 스냅샷용) — 판은 화면에 내는 것과 같이 가장 가까운 점으로 늘리고,
    /// 이번 틀의 명령을 CPU 로 차례대로 그려 얹는다.
    /// </summary>
    internal (uint[] Px, int W, int H) Composite()
    {
        var shot = new uint[_w * _h];
        int ox = host.ViewOffsetX, oy = host.ViewOffsetY;
        for (int y = 0; y < _h; y++)
        {
            int by = (int)Math.Floor((y - oy) / Zoom);
            for (int x = 0; x < _w; x++)
            {
                int bx = (int)Math.Floor((x - ox) / Zoom);
                shot[y * _w + x] = (uint)bx < host.ViewWidth && (uint)by < host.ViewHeight ? host._fb[(host._camY + by) * host.BoardWidth + host._camX + bx] | 0xFF000000 : 0xFF000000;
                // 밑깔기 — 판의 점이 원본 배경 그대로면 큰 그림의 그 자리(가장 가까운 점)를 보인다.
                if (UnderLow is { } low && _under.Count > 0 && (uint)bx < host.ViewWidth && (uint)by < host.ViewHeight)
                {
                    int lx = host._camX + bx - low.X, ly = host._camY + by - low.Y;
                    var u = _under[0];
                    if ((uint)lx < low.W && (uint)ly < low.H && (host._fb[(host._camY + by) * host.BoardWidth + host._camX + bx] | 0xFF000000) == (low.Low[ly * low.W + lx] | 0xFF000000)
                        && x >= u.X0 && x < u.X1 && y >= u.Y0 && y < u.Y1)
                        shot[y * _w + x] = u.Data![(y - u.Y0) * u.H / (u.Y1 - u.Y0) * u.Stride + (x - u.X0) * u.W / (u.X1 - u.X0)] | 0xFF000000;
                }
            }
        }
        foreach (var c in _cmds)
        {
            int pw = c.X1 - c.X0, ph = c.Y1 - c.Y0;
            uint[]? round = c.Kind == Kind.Round ? RoundPixels(pw, ph, c.Radius16 / 16.0, c.Color) : null;
            for (int py = Math.Max(0, c.Y0); py < Math.Min(_h, c.Y1); py++)
                for (int px = Math.Max(0, c.X0); px < Math.Min(_w, c.X1); px++)
                {
                    int rx = px - c.X0, ry = py - c.Y0;
                    uint src;
                    switch (c.Kind)
                    {
                        case Kind.Fill: src = c.Color; break;
                        case Kind.Round: src = round![ry * pw + rx]; break;
                        case Kind.Text: src = c.Data![ry * c.W + rx]; break;
                        case Kind.Image:
                            src = c.Data![ry * c.H / ph * c.Stride + rx * c.W / pw];
                            if (!c.Opaque && (src & 0xFF000000) == 0) continue;
                            src = c.Dim ? GameWindow.ScaleColor(src, 15, 31) : src | 0xFF000000;
                            break;
                        default:
                        {
                            double at = (ry + 0.5) * c.Data!.Length / ph - 0.5;
                            int i = Math.Clamp((int)Math.Floor(at), 0, c.Data.Length - 1), j = Math.Min(c.Data.Length - 1, i + 1);
                            int k = Math.Clamp((int)((at - i) * 256), 0, 256);
                            uint a = c.Data[i], b = c.Data[j];
                            uint Mix(int shift) => (uint)(((int)(a >> shift & 0xFF) * (256 - k) + (int)(b >> shift & 0xFF) * k) / 256);
                            src = c.Color & 0xFF000000 | Mix(16) << 16 | Mix(8) << 8 | Mix(0);
                            break;
                        }
                    }
                    uint alpha = src >> 24;
                    if (alpha == 0) continue;
                    uint dst = shot[py * _w + px];
                    uint Ch(int shift) => (uint)(((src >> shift & 0xFF) * c.Tint * alpha + (dst >> shift & 0xFF) * (255 - alpha)) / 255);
                    shot[py * _w + px] = 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
                }
        }
        return (shot, _w, _h);
    }

    public void Dispose()
    {
        foreach (var t in _textures.Values) t.Dispose();
        foreach (var t in _rounds.Values) t.Dispose();
        _textures.Clear();
        _rounds.Clear();
        _white?.Dispose();
        _vb?.Dispose();
        _linear?.Dispose();
        _point?.Dispose();
        _raster?.Dispose();
        _blend?.Dispose();
        _layout?.Dispose();
        _ps?.Dispose();
        _vs?.Dispose();
        (_white, _vb, _linear, _point, _raster, _blend, _layout, _ps, _vs) = (null, null, null, null, null, null, null, null, null);
        _vbVerts = 0;
    }
}

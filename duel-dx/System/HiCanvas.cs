using System.Drawing;
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
/// 픽셀은 알파를 미리 곱한 값으로 쌓는다(겹쳐 그려도 섞임이 맞는다). 그 틀에 아무것도 안 그렸으면 올리지도 겹치지도 않는다.
/// </remarks>
internal sealed unsafe class HiCanvas(GameWindow host) : IDisposable
{
    private uint[] _px = [];
    private int _w, _h;
    private bool _dirty;

    /// <summary>이번 틀에 이 층에 그린 것이 있나.</summary>
    internal bool Used { get; private set; }

    private ID3D11Texture2D? _tex;
    private ID3D11ShaderResourceView? _srv;
    private ID3D11PixelShader? _ps;
    private ID3D11BlendState? _blend;
    private int _texW, _texH;

    private readonly Dictionary<string, (uint[] Px, int W, int H)> _text = [];

    /// <summary>틀을 시작할 때 — 지난 틀에 그린 것을 지운다.</summary>
    internal void Begin()
    {
        if (_dirty) { Array.Clear(_px); _dirty = false; }
        Used = false;
    }

    private void Ensure()
    {
        if (_w != host._resW || _h != host._resH)
        {
            (_w, _h) = (host._resW, host._resH);
            _px = new uint[Math.Max(1, _w * _h)];
        }
        Used = _dirty = true;
    }

    private double Zoom => host._zoom;

    /// <summary>판 좌표 → 창 픽셀.</summary>
    internal int Sx(double boardX) => (int)Math.Round((boardX - host._camX) * Zoom) + host.ViewOffsetX;
    internal int Sy(double boardY) => (int)Math.Round((boardY - host._camY) * Zoom) + host.ViewOffsetY;

    /// <summary>선 굵기(창 픽셀) — 판의 한 점만큼.</summary>
    private int Line => Math.Max(1, (int)Math.Round(Zoom));

    internal void FillRect(int x, int y, int w, int h, uint color)
    {
        Ensure();
        FillPx(Sx(x), Sy(y), Sx(x + w), Sy(y + h), color);
    }

    internal void StrokeRect(int x, int y, int w, int h, uint color)
    {
        Ensure();
        int x0 = Sx(x), y0 = Sy(y), x1 = Sx(x + w), y1 = Sy(y + h), t = Line;
        FillPx(x0, y0, x1, y0 + t, color);
        FillPx(x0, y1 - t, x1, y1, color);
        FillPx(x0, y0 + t, x0 + t, y1 - t, color);
        FillPx(x1 - t, y0 + t, x1, y1 - t, color);
    }

    /// <summary>글의 크기를 판 좌표로 — 가운데 · 오른쪽 맞춤 셈에 쓴다.</summary>
    internal (int W, int H) Measure(string text, uint color, float size = 13f)
    {
        var (_, w, h) = Glyphs(text, color, size);
        return ((int)Math.Ceiling(w / Zoom), (int)Math.Ceiling(h / Zoom));
    }

    internal void DrawText(string text, int x, int y, uint color, float size = 13f)
    {
        if (text.Length == 0) return;
        Ensure();
        var (px, w, h) = Glyphs(text, color, size);
        int left = Sx(x), top = Sy(y);
        for (int yy = 0; yy < h; yy++)
        {
            int py = top + yy;
            if ((uint)py >= _h) continue;
            for (int xx = 0; xx < w; xx++)
            {
                int qx = left + xx;
                if ((uint)qx < _w) Over(ref _px[py * _w + qx], px[yy * w + xx]);
            }
        }
    }

    /// <summary>글을 「크기 × 배율」로 굽는다 — 배율이 바뀌면 열쇠가 달라져 새로 굽는다.</summary>
    private (uint[] Px, int W, int H) Glyphs(string text, uint color, float size)
    {
        float real = (float)Math.Round(size * Zoom * 4) / 4;
        string key = text + ":" + color + ":" + real;
        if (_text.TryGetValue(key, out var cached)) return cached;
        var made = TextRaster.Render(text, Color.FromArgb((int)color), real) ?? ([], 0, 0);
        if (_text.Count > 400) _text.Clear();
        return _text[key] = made;
    }

    private void FillPx(int x0, int y0, int x1, int y1, uint color)
    {
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(_w, x1); y1 = Math.Min(_h, y1);
        if (x0 >= x1 || y0 >= y1 || color >> 24 == 0) return;
        if (color >> 24 == 0xFF) { for (int y = y0; y < y1; y++) _px.AsSpan(y * _w + x0, x1 - x0).Fill(color); return; }
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++) Over(ref _px[y * _w + x], color);
    }

    /// <summary>알파를 곱해 둔 바탕 위에 곱하지 않은 색을 얹는다.</summary>
    private static void Over(ref uint dst, uint src)
    {
        uint a = src >> 24;
        if (a == 0) return;
        if (a == 0xFF) { dst = src; return; }
        uint ia = 255 - a, d = dst;
        uint Ch(int shift) => ((src >> shift & 0xFF) * a + (d >> shift & 0xFF) * ia + 127) / 255;
        uint oa = a + ((d >> 24) * ia + 127) / 255;
        dst = oa << 24 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>판 그림을 그린 뒤, 화면에 내기 전에 — 이 층을 창 전체에 겹쳐 그린다.</summary>
    internal void Present()
    {
        if (!Used) return;
        var device = host._device;
        var ctx = host._ctx;
        if (_ps == null)
        {
            const string shader = """
                Texture2D<float4> Layer : register(t0);
                float4 PS(float4 pos : SV_Position) : SV_Target { return Layer.Load(int3(int2(pos.xy), 0)); }
                """;
            _ps = device.CreatePixelShader(Compiler.Compile(shader, "PS", "hicanvas.hlsl", "ps_4_0").Span);
            _blend = device.CreateBlendState(BlendDescription.AlphaBlend);
        }
        if (_tex == null || _texW != _w || _texH != _h)
        {
            _srv?.Dispose();
            _tex?.Dispose();
            _tex = device.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)_w,
                Height = (uint)_h,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.Write,
            });
            _srv = device.CreateShaderResourceView(_tex);
            (_texW, _texH) = (_w, _h);
        }
        var map = ctx.Map(_tex, 0, MapMode.WriteDiscard);
        try
        {
            for (int y = 0; y < _h; y++)
                _px.AsSpan(y * _w, _w).CopyTo(new Span<uint>((void*)(map.DataPointer + y * map.RowPitch), _w));
        }
        finally { ctx.Unmap(_tex, 0); }

        ctx.RSSetViewport(0, 0, _w, _h);
        ctx.PSSetShader(_ps);
        ctx.PSSetShaderResources(0, [_srv!]);
        ctx.OMSetBlendState(_blend);
        ctx.Draw(3, 0);
        ctx.OMSetBlendState(null);
    }

    /// <summary>
    /// 판 그림과 이 층을 겹친 창 크기 그림(화면 밖 시험의 스냅샷용) — 판은 화면에 내는 것과 같이 가장 가까운 점으로 늘린다.
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
                uint under = (uint)bx < host.ViewWidth && (uint)by < host.ViewHeight ? host._fb[(host._camY + by) * host.BoardWidth + host._camX + bx] | 0xFF000000 : 0xFF000000;
                uint over = _px[y * _w + x], ia = 255 - (over >> 24);
                uint Ch(int shift) => Math.Min(255, (over >> shift & 0xFF) + ((under >> shift & 0xFF) * ia + 127) / 255);
                shot[y * _w + x] = 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
            }
        }
        return (shot, _w, _h);
    }

    public void Dispose()
    {
        _srv?.Dispose();
        _tex?.Dispose();
        _ps?.Dispose();
        _blend?.Dispose();
        (_srv, _tex, _ps, _blend) = (null, null, null, null);
    }
}

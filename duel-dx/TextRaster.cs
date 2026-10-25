using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace DuelDx.Native;

/// <summary>
/// 글자 한 줄을 BGRA 그림 한 장으로 굽는다. GDI+(System.Drawing)를 쓰는 것은 이 프로젝트가
/// 참값 비트맵 글꼴(게임 것)을 안 들고 있어서다 — 시스템 글꼴을 오프스크린으로 그려
/// 픽셀만 뽑아 쓰고, 화면 자체는 여전히 순수 Win32 창 + D3D11 로 찍는다.
/// </summary>
/// <remarks>
/// 글꼴은 기본 맑은 고딕 굵게. 파트1 글 꾸밈(<see cref="Part1Text"/>)은 원본 꼴 이름(굴림 · 궁서 · 바탕 · 가을체 · Premi)과 굵기를 넘긴다 —
/// 가을체 · Premi 는 원본이 깔아 두는 파트1 폴더의 <c>Font\G3Han.ttf</c> · <c>G3Eng.ttf</c> 이다(시스템에 없으면 거기서 읽는다). 못 찾으면 맑은 고딕.
/// </remarks>
internal static class TextRaster
{
    internal const string DefaultFace = "Malgun Gothic";

    private static readonly Dictionary<(string Face, float Size, bool Bold), Font> Fonts = [];
    private static readonly Dictionary<string, FontFamily> Families = [];
    private static readonly PrivateFontCollection Private = new();

    /// <summary>파트1 폴더 <c>Font</c> 의 글꼴 파일 — 원본 설치가 시스템에 넣는 것.</summary>
    private static readonly Dictionary<string, string> Part1Files = new() { ["가을체"] = "G3Han.ttf", ["Premi"] = "G3Eng.ttf" };

    private static FontFamily Family(string face)
    {
        if (Families.TryGetValue(face, out var f)) return f;
        FontFamily? found = null;
        try { found = new FontFamily(face); } catch (ArgumentException) { }
        if (found == null && Part1Files.TryGetValue(face, out var file) && WarOfGenesis.Assets.Part1Game.Root is { } root)
        {
            try
            {
                string path = Path.Combine(root, "Font", file);
                if (File.Exists(path))
                {
                    Private.AddFontFile(path);
                    found = Private.Families.FirstOrDefault(p => p.Name == face);
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException) { }
        }
        return Families[face] = found ?? new FontFamily(DefaultFace);
    }

    private static Font FontFor(string? face, float fontSize, bool bold)
    {
        var fkey = (face ?? DefaultFace, fontSize, bold);
        if (Fonts.TryGetValue(fkey, out var font)) return font;
        var family = Family(fkey.Item1);
        var style = bold && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
        return Fonts[fkey] = new Font(family, fontSize, style, GraphicsUnit.Pixel);
    }

    private static readonly Bitmap MeasureBitmap = new(1, 1);
    private static readonly Graphics MeasureGraphics = Graphics.FromImage(MeasureBitmap);
    private static readonly StringFormat Typographic = MakeTypographic();

    private static StringFormat MakeTypographic()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        return f;
    }

    /// <summary>글의 폭(픽셀) — 그림 없이 잰다(앞뒤 여백 없는 활자 폭, GDI <c>GetTextExtentPoint32</c> 처럼 글자 폭의 합에 가깝다).</summary>
    public static float Width(string text, float fontSize, string? face = null, bool bold = true)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return MeasureGraphics.MeasureString(text, FontFor(face, fontSize, bold), int.MaxValue, Typographic).Width;
    }

    public static (uint[] Px, int W, int H)? Render(string text, Color color, float fontSize = 13f, string? face = null, bool bold = true)
    {
        var Font = FontFor(face, fontSize, bold);
        if (string.IsNullOrEmpty(text)) return null;

        using var measure = new Bitmap(1, 1);
        using (var mg = Graphics.FromImage(measure))
        {
            var size = mg.MeasureString(text, Font);
            int mw = Math.Max(1, (int)Math.Ceiling(size.Width));
            int mh = Math.Max(1, (int)Math.Ceiling(size.Height));

            using var bmp = new Bitmap(mw, mh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using var brush = new SolidBrush(color);
                g.DrawString(text, Font, brush, 0, 0);
            }

            var rect = new Rectangle(0, 0, mw, mh);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var px = new uint[mw * mh];
                unsafe
                {
                    for (int y = 0; y < mh; y++)
                    {
                        uint* row = (uint*)(data.Scan0 + y * data.Stride);
                        for (int x = 0; x < mw; x++) px[y * mw + x] = row[x];
                    }
                }
                return (px, mw, mh);
            }
            finally { bmp.UnlockBits(data); }
        }
    }
}

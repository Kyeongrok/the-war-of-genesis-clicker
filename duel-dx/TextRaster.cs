using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace DuelDx.Native;

/// <summary>
/// 글자 한 줄을 BGRA 그림 한 장으로 굽는다. GDI+(System.Drawing)를 쓰는 것은 이 프로젝트가
/// 참값 비트맵 글꼴(게임 것)을 안 들고 있어서다 — 시스템 글꼴을 오프스크린으로 그려
/// 픽셀만 뽑아 쓰고, 화면 자체는 여전히 순수 Win32 창 + D3D11 로 찍는다.
/// </summary>
internal static class TextRaster
{
    private static readonly Font Font = new("Malgun Gothic", 13f, FontStyle.Bold, GraphicsUnit.Pixel);

    public static (uint[] Px, int W, int H)? Render(string text, Color color)
    {
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

using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투판 위에 겹쳐 그리는 Status 화면과 알림(링 커맨드는 <c>BattleSceneWindow.Ring.cs</c>).
/// </summary>
internal sealed unsafe partial class BattleSceneWindow
{
    private GameDatabase? _db;
    private readonly Dictionary<int, SpriteFrame> _faces = [];

    private int _statusUnit = -1;
    private string _toast = "";
    private double _toastUntil;

    private void Toast(string text)
    {
        _toast = text;
        _toastUntil = _lastTime + 2.5;
    }

    private string UnitName(int index) => _names.GetValueOrDefault(_units[index].ChrCode, "");

    private int UnitAtBoard(int bx, int by)
    {
        if (by < GridTop) return -1;
        int col = bx / TileW, row = RowAt(bx, by);
        if (row < 0) return -1;
        return Array.FindIndex(_units, u => u.Alive && u.Col == col && u.Row == row);
    }

    // ── Status 화면 그리기 도구 (화면은 BattleSceneWindow.Status.cs) ──────────

    private void Header(int x, int y, int w, string text)
    {
        FillRect(x, y, w, 20, HeadBg);
        StrokeRect(x, y, w, 20, 0xFF7FA6E8);
        var (_, tw, _) = GetText(text, White);
        DrawText(text, x + (w - tw) / 2, y + 2, White);
    }

    private void Box(int x, int y, int w, int h)
    {
        FillRect(x, y, w, h, BoxBg);
        StrokeRect(x, y, w, h, BoxLine);
    }

    private void Stat(int x, int y, int w, string label, string value)
    {
        DrawText(label, x + 8, y, White);
        RightText(value, x + w - 8, y, White);
    }

    private void StatBar(int x, int y, int w, string label, int value, int max)
    {
        Stat(x, y, w, label, $"{value} / {max}");
        FillRect(x + 8, y + 22, w - 16, 3, Bar);
        if (max > 0) FillRect(x + 8, y + 22, (w - 16) * Math.Clamp(value, 0, max) / max, 3, Red);
    }

    private void AbilityRow(int x, int y, int w, string name, string cost, bool affordable = true)
    {
        DrawText(name, x + 10, y, White);
        if (cost.Length > 0) RightText(cost, x + w - 10, y, affordable ? Red : 0xFF804848);
    }

    private void RightText(string text, int right, int y, uint color, float size = 13f)
    {
        var (_, w, _) = GetText(text, color, size);
        DrawText(text, right - w, y, color, size);
    }

    private void BlitClipped(SpriteFrame f, int x, int y, int w, int h)
    {
        int sx = Math.Max(0, (f.W - w) / 2), sy = Math.Max(0, (f.H - h) / 2);
        for (int yy = 0; yy < Math.Min(h, f.H); yy++)
            for (int xx = 0; xx < Math.Min(w, f.W); xx++)
            {
                uint px = f.Px[(sy + yy) * f.W + sx + xx];
                if ((px & 0xFF000000) != 0) SetPixel(x + xx, y + yy, px);
            }
    }

    private void DrawToast()
    {
        if (_toast.Length == 0 || _lastTime > _toastUntil) return;
        var (_, w, h) = GetText(_toast, White);
        int x = (BoardWidth - w) / 2, y = _camY + ViewHeight - h - 24;
        FillRect(x - 10, y - 6, w + 20, h + 12, 0xD0101828);
        DrawText(_toast, x, y, White);
    }

    // ── 원 ───────────────────────────────────────────────────────────────────

    private void FillCircle(int cx, int cy, int r, uint color)
    {
        for (int y = -r; y <= r; y++)
        {
            int half = (int)Math.Sqrt(r * r - y * y);
            for (int x = -half; x <= half; x++) SetPixel(cx + x, cy + y, color);
        }
    }

    private void StrokeCircle(int cx, int cy, int r, uint color, int thickness)
    {
        int outer = r * r, inner = (r - thickness) * (r - thickness);
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                int d = x * x + y * y;
                if (d <= outer && d > inner) SetPixel(cx + x, cy + y, color);
            }
    }
}

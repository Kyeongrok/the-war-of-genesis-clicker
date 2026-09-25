using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투판 위에 겹쳐 그리는 Status 화면과 알림(링 커맨드는 <c>BattleSceneWindow.Ring.cs</c>).
/// </summary>
internal sealed unsafe partial class BattleSceneWindow
{
    private GameDatabase? _db;
    private readonly Dictionary<int, SpriteFrame> _faces = [];

    /// <summary>
    /// 초상 Obs 에서 <b>60×60 얼굴 장</b>을 고른다 — 죠안·살라딘은 첫 장이 얼굴이지만 제이슨(Obs 0433)은 앞 네 장이
    /// 392×480 전신 그림의 네 조각이고 다섯째가 얼굴이다. 60×60 이 없으면 첫 장(가설: 원본 Status 초상은 60×60).
    /// </summary>
    private static ObsFrame? DecodeFaceFrame(string path)
    {
        try
        {
            var face = ObsSprite.Decode(path).SelectMany(m => m.Frames).FirstOrDefault(f => f.Width == 60 && f.Height == 60);
            return face ?? ObsSprite.DecodeFirstFrame(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return null; }
    }

    private int _statusUnit = -1;
    private string _toast = "";
    private double _toastUntil;

    private void Toast(string text)
    {
        _toast = text;
        _toastUntil = _lastTime + 2.5;
    }

    /// <summary>조작 안내 글 — 설정 > 안내 글 켜기·끄기로 숨길 수 있다(사용자 요청). 결과 알림(Toast)은 늘 보인다.</summary>
    private void Hint(string text) { if (_showHints) Toast(text); }

    private bool _showHints = UserSettings.Current.ShowHints;

    private string UnitName(int index) => _names.GetValueOrDefault(_units[index].ChrCode, "");

    private int UnitAtBoard(int bx, int by)
    {
        if (by < GridTop) return -1;
        int col = bx / TileW, row = RowAt(bx, by);
        if (row < 0) return -1;
        return Array.FindIndex(_units, u => u.Alive && u.OnField && u.Col == col && u.Row == row);   // 퇴장한 사람은 안 잡는다(LiveUnitAt 과 같다)
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
        int x = _camX + (ViewWidth - w) / 2, y = _camY + ViewHeight - h - 24;
        FillRect(x - 10, y - 6, w + 20, h + 12, 0xD0101828);
        DrawText(_toast, x, y, White);
    }

    /// <summary>
    /// 지금 장면의 번호 — 화면 왼쪽 아래 작은 글(「Fld 0365 · 사건 3 · 줄 12」, 「Btl 0137 · 턴 4」, 「Chp 0011」).
    /// 원본에는 없다 — 어느 장면 이야기인지 서로 짚기 쉽게(사용자 요청). 설정 > 장면 번호 보이기.
    /// </summary>
    private void DrawSceneTag()
    {
        if (!_showSceneTag || _titleOpen || _recordsOpen) return;
        string tag;
        if (_episodesOpen) tag = "연대표";
        else if (_field != null) tag = $"Fld {_field.Id:D4}" + ScriptPlace(_fieldEvent, _fieldPc);
        else if (_mosesOpen) tag = _mosesChp is { } chp ? $"Chp {chp.Id:D4}" + ScriptPlace(_fieldEvent, _fieldPc) : "모세스";
        else if (_battleLoaded) tag = $"Btl {_scene.Id:D4} · 턴 {_turnNo}" + (_runningEvent >= 0 ? $" · 사건 {_runningEvent}" : "");
        else return;
        var (_, w, h) = GetText(tag, White, 11f);
        int x = _camX + 4, y = _camY + ViewHeight - h - 4;
        FillRect(x - 2, y - 1, w + 4, h + 2, 0x90000000);
        DrawText(tag, x, y, 0xFFB0B8C8, 11f);

        static string ScriptPlace(int ev, int pc) => ev >= 0 ? $" · 사건 {ev} · 줄 {Math.Max(0, pc - 1)}" : "";
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

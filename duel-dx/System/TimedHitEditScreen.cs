using DuelDx.Native;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 타이밍 어택 수정 창 — 메뉴 막대 개발 &gt; 타이밍 어택 수정. 타이밍 어택(<see cref="TimedHit"/>)의 수치를 게임 안에서 고치며 맞춘다(사용자 요청 fg-32).
/// </summary>
/// <remarks>
/// 맨 위는 <b>패턴 줄</b> — 박자 패턴을 여러 개 등록해 두고 고른다(고른 것이 게임에 쓰인다). 「+ 새 패턴」은 지금 것을 베껴 새로 만든다.
/// 그 아래는 <b>시간 자</b> — 띠 길이가 밝은 막대로, 박자 칸이 그 위의 색 칸으로(옅은 끝은 판정 여유) 보인다. 값을 고치면 바로 따라 움직인다.
/// 왼쪽은 박자 목록 — 박자마다 때(칸 가운데)와 길이(칸 너비)를 따로 고치고, 박자를 더하거나 뺀다(하나에서 여섯까지).
/// 오른쪽은 띠 길이 · 판정 여유 따위 — 값 아래의 가는 막대가 범위 안에서 어디쯤인지 보인다.
/// 아래는 「미리 보기」 — 띠를 이 창 안에서 한 번 돌려 Space · LT · RT 로 눌러 본다. 고치면 바로 적용되고 <c>timedhit.json</c> 에 남는다.
/// 단추 자리는 그릴 때와 누를 때 한 곳(<see cref="Build"/>)에서 만든다.
/// </remarks>
internal sealed class TimedHitEditScreen(GameWindow host)
{
    internal const int MenuTimedEdit = 1173;
    internal const int W = 800, H = 508, PatternY = 34, RulerY = 76, RulerH = 22, RowsY = 140, RowH = 28, Btn = 26, PreviewY = 340, FootY = H - 40;

    /// <summary>DUELDX_TIMEDEDIT 가 있으면 열어 둔 채 시작한다(화면 밖 시험용).</summary>
    internal bool _open = Environment.GetEnvironmentVariable("DUELDX_TIMEDEDIT") != null;

    private IEnumerator<bool>? _preview;

    private TimedHitConfig Cfg => host.TimedHitFx.Cfg;

    /// <summary>누를 수 있는 자리들 — <see cref="Build"/> 가 채운다.</summary>
    private readonly List<(int X, int Y, int W, int H, Action Do)> _hot = [];

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    private bool MouseIn(int x, int y, int w, int h) => host._mouse.X >= x && host._mouse.X < x + w && host._mouse.Y >= y && host._mouse.Y < y + h;

    private static double Round2(double v) => Math.Round(v, 2);

    /// <summary>박자 칸의 색 — 어빌리티 단추(LT) 박자와 공격 단추(RT) 박자를 다른 색으로.</summary>
    private static uint BeatColor(int i) => TimedHit.ButtonOfBeat(i) == 0 ? 0xFFFFD040 : 0xFF60C8FF;

    private string BeatButton(int i) => GamePad.ButtonLabel(host.Pad.ButtonOf(TimedHit.ButtonOfBeat(i) == 0 ? PadAction.Ability : PadAction.Attack));

    /// <summary>패드 커서가 건너뛸 자리(<see cref="GamePad.Targets"/>).</summary>
    internal void AddTargets(List<(int X, int Y, int W, int H)> list)
    {
        Build(draw: false);
        foreach (var (x, y, w, h, _) in _hot) list.Add((x, y, w, h));
    }

    /// <summary>열려 있으면 클릭을 먹는다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        Build(draw: false);
        foreach (var (x, y, w, h, act) in _hot.ToArray())
        {
            if (bx < x || bx >= x + w || by < y || by >= y + h) continue;
            act();
            Cfg.Normalize();
            Cfg.Save();
            break;
        }
        return true;
    }

    /// <summary>띠를 이 창 안에서 한 번 돌린다 — 전투의 띠가 돌고 있으면 건드리지 않는다.</summary>
    internal void StartPreview()
    {
        if (host.TimedHitFx.Running) return;
        _preview = host.TimedHitFx.Prompt(null, () =>
        {
            var (x, y) = Origin();
            return (x + (W - Math.Clamp(Cfg.BarWidth, 60, 600)) / 2, y + PreviewY + TimedHit.AboveBar);
        }).GetEnumerator();
    }

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE) _open = false;
        else if (key is Win32.VK_RETURN or Win32.VK_SPACE) StartPreview();   // 띠가 도는 동안의 Space 는 띠가 먼저 받는다(GameWindow.OnKeyDown)
    }

    internal void Draw()
    {
        // 미리 보기는 창이 닫혀도 끝까지 돌려 입력을 놓아준다.
        if (_preview != null && !_preview.MoveNext()) _preview = null;
        if (_open) Build(draw: true);
    }

    /// <summary>창을 짠다 — <paramref name="draw"/> 면 그리기까지, 아니면 누를 자리만 만든다.</summary>
    private void Build(bool draw)
    {
        _hot.Clear();
        var (x, y) = Origin();
        Cfg.Normalize();
        var beats = Cfg.Beats;

        void Button(int bx, int by, int bw, int bh, string label, Action act, uint ink = White, uint back = 0)
        {
            _hot.Add((bx, by, bw, bh, act));
            if (!draw) return;
            host.FillRect(bx, by, bw, bh, MouseIn(bx, by, bw, bh) ? 0xFF2A4A8A : back != 0 ? back : StatusScreen.BoxBg);
            host.StrokeRect(bx, by, bw, bh, StatusScreen.BoxLine);
            var (_, tw, th) = host.GetText(label, ink);
            host.DrawText(label, bx + (bw - tw) / 2, by + (bh - th) / 2, ink);
        }
        void Value(int vx, int vy, int vw, string text, double fill = -1)
        {
            if (!draw) return;
            host.FillRect(vx, vy, vw, RowH - 6, 0xFF101828);
            host.DrawText(text, vx + (vw - host.GetText(text, White).W) / 2, vy + 3, 0xFFFFE070);
            // 값 아래의 가는 막대 — 범위 안에서 어디쯤인지.
            if (fill >= 0) host.FillRect(vx, vy + RowH - 9, (int)(vw * Math.Clamp(fill, 0, 1)), 3, 0xFF60C8FF);
        }

        if (draw)
        {
            host.FillRect(x - 4, y - 4, W + 8, H + 8, 0x80000000);
            host.FillRect(x, y, W, H, StatusScreen.PanelBg);
            host.StrokeRect(x, y, W, H, StatusScreen.BoxLine);
            host.FillRect(x, y, W, 28, StatusScreen.HeadBg);
            host.DrawText("타이밍 어택 수정 — 고치면 바로 적용 · timedhit.json 에 저장", x + 10, y + 6, White);
            DrawRuler(x + 16, y + RulerY, W - 32);
            host.DrawText("패턴", x + 16, y + PatternY + 3, 0xFFD0E8D0, 12);
        }

        // ── 패턴 줄: 등록해 둔 것들 · 새로 · 지우기 ───────────────────────────────
        int tx = x + 56;
        for (int i = 0; i < Cfg.Patterns.Count; i++)
        {
            int index = i;
            bool on = i == Cfg.Selected;
            // ★ = 전투가 처음에 띄우는 패턴. 누르면 그 패턴을 본다(고친다) — 시작 패턴은 「★ 여기서 시작」으로 따로 정한다.
            Button(tx, y + PatternY, 58, 22, i == Cfg.Start ? $"★{i + 1}번" : $"{i + 1}번", () => Cfg.Selected = index, on ? 0xFF00FFFF : White, on ? StatusScreen.HeadBg : 0);
            tx += 62;
        }
        if (Cfg.Patterns.Count < TimedHitConfig.MaxPatterns)
            Button(tx + 6, y + PatternY, 96, 22, "+ 새 패턴", () =>
            {
                Cfg.Patterns.Add(Cfg.Current.Copy());   // 지금 것을 베껴 새로 — 거기서 고쳐 나간다
                Cfg.Selected = Cfg.Patterns.Count - 1;
            }, 0xFF90E0A0);
        if (Cfg.Selected != Cfg.Start)
            Button(x + W - 16 - 110 - 8 - 124, y + PatternY, 124, 22, "★ 여기서 시작", () => Cfg.Start = Cfg.Selected, 0xFFFFE070);
        if (Cfg.Patterns.Count > 1)
            Button(x + W - 16 - 110, y + PatternY, 110, 22, $"{Cfg.Selected + 1}번 지우기", () => Cfg.RemovePattern(Cfg.Selected), 0xFFFF9080);

        // ── 왼쪽: 박자 목록 ─────────────────────────────────────────────────────
        int lx = x + 16, top = y + RowsY;
        if (draw) host.DrawText($"박자 {beats.Count}개 = 최대 {beats.Count}대 — 때(칸 가운데) · 길이(칸 너비)", lx, top - 20, 0xFFD0E8D0, 12);
        for (int i = 0; i < beats.Count; i++)
        {
            var beat = beats[i];
            int ry = top + i * RowH;
            if (draw)
            {
                host.FillRect(lx, ry + 4, 6, RowH - 14, BeatColor(i));
                host.DrawText($"{i + 1} {BeatButton(i)}", lx + 12, ry + 3, White);
            }
            void MoveAt(double d) => beat.At = Round2(Math.Clamp(beat.At + d, 0.05, 6));
            void Widen(double d) => beat.Half = Round2(Math.Clamp(beat.Half + d, 0.01, 1));
            Button(lx + 56, ry, Btn, RowH - 6, "−−", () => MoveAt(-0.05));
            Button(lx + 86, ry, Btn, RowH - 6, "−", () => MoveAt(-0.01));
            Value(lx + 116, ry, 62, $"{beat.At:0.00}초");
            Button(lx + 182, ry, Btn, RowH - 6, "+", () => MoveAt(0.01));
            Button(lx + 212, ry, Btn, RowH - 6, "++", () => MoveAt(0.05));
            if (draw) host.DrawText("길이", lx + 248, ry + 4, DimGray, 12);
            Button(lx + 280, ry, Btn, RowH - 6, "−", () => Widen(-0.01));
            Value(lx + 310, ry, 62, $"{beat.Half * 2:0.00}초");
            Button(lx + 376, ry, Btn, RowH - 6, "+", () => Widen(0.01));
            if (beats.Count > 1) Button(lx + 410, ry, Btn, RowH - 6, "×", () => beats.Remove(beat), 0xFFFF9080);
        }
        if (beats.Count < TimedHitConfig.MaxBeats)
            Button(lx, top + beats.Count * RowH, 150, RowH - 6, "+ 박자 추가", () =>
            {
                var last = beats[^1];
                var added = new TimedBeat { At = Round2(Math.Min(6, last.At + Math.Max(0.3, last.Half * 2 + 0.1))), Half = last.Half };
                beats.Add(added);
                // 새 칸이 띠 밖으로 나가면 띠를 그만큼 늘린다.
                if (added.At + added.Half > Cfg.Seconds) Cfg.Seconds = Round2(Math.Min(8, added.At + added.Half + 0.05));
            }, 0xFF90E0A0);

        // ── 오른쪽: 띠 · 판정 ────────────────────────────────────────────────────
        int rx = x + 476;
        if (draw) host.DrawText("띠 · 판정", rx, top - 20, 0xFFD0E8D0, 12);
        (string Label, Func<double> Get, Action<double> Set, double Step, double Big, double Min, double Max, string Format)[] rows =
        [
            ("띠 길이", () => Cfg.Seconds, v => Cfg.Seconds = v, 0.01, 0.1, 0.3, 8, "0.00초"),
            ("판정 여유", () => Cfg.Grace, v => Cfg.Grace = v, 0.01, 0.05, 0, 0.5, "+0.00초"),
            ("결과 표시", () => Cfg.ShowResultSeconds, v => Cfg.ShowResultSeconds = v, 0.05, 0.2, 0.1, 3, "0.00초"),
            ("한 타 배율", () => Cfg.DamagePercent, v => Cfg.DamagePercent = (int)Math.Round(v), 10, 50, 100, 500, "0'%'"),
            ("띠 너비", () => Cfg.BarWidth, v => Cfg.BarWidth = (int)Math.Round(v), 10, 40, 100, 500, "0px"),
        ];
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            int ry = top + i * RowH;
            if (draw) host.DrawText(row.Label, rx, ry + 3, White);
            void Step(double d) => row.Set(Round2(Math.Clamp(row.Get() + d, row.Min, row.Max)));
            Button(rx + 88, ry, Btn, RowH - 6, "−−", () => Step(-row.Big));
            Button(rx + 118, ry, Btn, RowH - 6, "−", () => Step(-row.Step));
            Value(rx + 148, ry, 76, row.Get().ToString(row.Format), (row.Get() - row.Min) / (row.Max - row.Min));
            Button(rx + 228, ry, Btn, RowH - 6, "+", () => Step(row.Step));
            Button(rx + 258, ry, Btn, RowH - 6, "++", () => Step(row.Big));
        }
        // 이 패턴을 다 맞추면 다음 공격에 나올 패턴 — 「그대로」(이 패턴이 이어짐)이거나 다른 번호.
        {
            int ry = top + rows.Length * RowH + 6;
            var current = Cfg.Current;
            int count = Cfg.Patterns.Count;
            if (draw)
            {
                host.FillRect(rx, ry - 4, 284, 1, StatusScreen.BoxLine);
                host.DrawText("성공하면 다음", rx, ry + 3, White);
            }
            // −1(그대로) · 0 … count−1 을 돈다.
            void Turn(int d) => current.Next = ((current.Next + 1 + d) % (count + 1) + count + 1) % (count + 1) - 1;
            Button(rx + 118, ry, Btn, RowH - 6, "◀", () => Turn(-1));
            Value(rx + 148, ry, 76, current.Next < 0 || current.Next == Cfg.Selected ? "그대로" : $"{current.Next + 1}번 패턴");
            if (draw && current.Next >= 0 && current.Next != Cfg.Selected && Cfg.Patterns[current.Next].Next == Cfg.Selected)
                host.DrawText("(서로 번갈아 나옵니다)", rx, ry + RowH - 2, DimGray, 11);
            Button(rx + 228, ry, Btn, RowH - 6, "▶", () => Turn(1));
        }

        // ── 미리 보기 칸 ─────────────────────────────────────────────────────────
        if (draw)
        {
            int py = y + PreviewY;
            host.FillRect(x + 12, py, W - 24, TimedHit.AboveBar + 12 + TimedHit.BelowBar, 0xFF0C1420);
            if (host.TimedHitFx.Running) host.TimedHitFx.Draw(preview: true);
            else
            {
                host.DrawText("「미리 보기」(Enter · Space)로 띠를 돌려 본다 — 도는 동안 Space(박자마다) · 패드 LT · RT(번갈아)로 눌러 본다", x + 24, py + 30, DimGray, 12);
                host.DrawText("맞춘 박자만큼 때린다(박자 하나 = 한 대, 여섯 대까지) — 하나만 맞추면 크리티컬, 하나도 못 맞추면 보통 공격", x + 24, py + 54, DimGray, 12);
                host.DrawText($"전투는 ★{Cfg.Start + 1}번 패턴에서 시작 — 다 맞추면 그 적에게는 「성공하면 다음」의 패턴이 나온다(인물 · 적마다 따로, 틀리면 ★{Cfg.Start + 1}번으로)", x + 24, py + 78, DimGray, 12);
            }
        }

        // ── 아래 단추 ───────────────────────────────────────────────────────────
        int fy = y + FootY;
        Button(x + 16, fy, 110, 28, "미리 보기", StartPreview);
        Button(x + 134, fy, 90, 28, "기본값", () =>
        {
            // 처음 깔았을 때의 패턴들과 띠 · 판정 값으로 모두 되돌린다.
            var first = new TimedHitConfig();
            first.Normalize();
            host.TimedHitFx.Cfg = first;
            host.Toast("타이밍 어택 패턴과 수치를 처음 값으로 되돌렸습니다");
        });
        Button(x + 232, fy, 190, 28, host._timedHit ? "타이밍 어택: 켜짐" : "타이밍 어택: 꺼짐", () => host.OnMenuCommand(MenuTimedHit), host._timedHit ? 0xFF00FFFF : White);
        Button(x + W - 116, fy, 100, 28, "닫기", () => _open = false, White, StatusScreen.HeadBg);
    }

    /// <summary>
    /// 시간 자 — 어두운 길이 자 전체(끝 눈금까지), 밝은 막대가 띠 길이, 그 위의 색 칸이 박자(옅은 끝은 판정 여유). 아래에 0.5초마다 눈금.
    /// 띠 길이를 고치면 밝은 막대가 늘고 줄어, 박자 칸이 띠 안에 드는지 한눈에 보인다.
    /// </summary>
    private void DrawRuler(int x, int y, int w)
    {
        var beats = Cfg.Beats;
        double end = Math.Max(2.0, Math.Ceiling(Math.Max(Cfg.Seconds, beats.Max(b => b.At + b.Half)) * 2) / 2 + 0.5);
        int At(double seconds) => x + (int)(w * Math.Clamp(seconds, 0, end) / end);

        host.FillRect(x, y, w, RulerH, 0xFF0C1420);
        host.StrokeRect(x, y, w, RulerH, StatusScreen.BoxLine);
        host.FillRect(x, y + 2, At(Cfg.Seconds) - x, RulerH - 4, 0xFF36466A);          // 띠 길이
        for (int i = 0; i < beats.Count; i++)
        {
            var b = beats[i];
            uint color = BeatColor(i);
            int g0 = At(b.At - b.Half - Cfg.Grace), g1 = At(b.At + b.Half + Cfg.Grace), z0 = At(b.At - b.Half), z1 = At(b.At + b.Half);
            host.FillRect(g0, y + 5, g1 - g0, RulerH - 10, 0x60000000 | (color & 0xFFFFFF));   // 판정 여유까지
            host.FillRect(z0, y + 3, Math.Max(2, z1 - z0), RulerH - 6, color);                  // 그리는 칸
            string n = $"{i + 1}";
            var (_, tw, th) = host.GetText(n, White, 12);
            host.DrawText(n, (z0 + z1) / 2 - tw / 2, y + (RulerH - th) / 2, 0xFF201800, 12);
        }
        // 띠 끝 표시와 눈금
        host.FillRect(At(Cfg.Seconds) - 1, y - 3, 2, RulerH + 6, White);
        for (double t = 0; t <= end + 1e-6; t += 0.5)
        {
            int tx = At(t);
            host.FillRect(Math.Min(tx, x + w - 1), y + RulerH, 1, 4, DimGray);
            string label = $"{t:0.0}";
            host.DrawText(label, Math.Clamp(tx - host.GetText(label, DimGray, 11).W / 2, x, x + w - 20), y + RulerH + 5, DimGray, 11);
        }
        string length = $"띠 {Cfg.Seconds:0.00}초";
        host.DrawText(length, Math.Min(At(Cfg.Seconds) + 6, x + w - host.GetText(length, White, 11).W - 4), y - 14, White, 11);

        // 겹침 · 띠 밖 경고
        string warn = "";
        for (int i = 0; i + 1 < beats.Count && warn.Length == 0; i++)
            if (beats[i].At + beats[i].Half + Cfg.Grace > beats[i + 1].At - beats[i + 1].Half - Cfg.Grace) warn = $"박자 {i + 1} · {i + 2} 의 판정 구간이 겹칩니다";
        if (warn.Length == 0 && beats[^1].At + beats[^1].Half > Cfg.Seconds) warn = "마지막 칸이 띠 밖으로 나갑니다";
        if (warn.Length > 0) host.DrawText(warn, x, y - 14, 0xFFFF7060, 11);
    }
}

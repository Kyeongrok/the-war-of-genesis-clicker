using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 챕터 지도 — 챕터(<see cref="Part1Chapter"/>)의 장소를 지도 자리대로 늘어놓고, 전투 장소를 누르면 그 전투를 연다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 1·2절. 장소 낱말 1·2(지도 자리)를 화면 칸에 맞춰 줄여 놓고, 낱말 9~13(잇닿은 장소 — 가설)을 점선으로 잇는다.
/// 챕터 사건(대사 · 502 전투로 · 503 필드로 · 깃발)은 <see cref="Part1SceneScript"/> 가 돌린다 — 전투·필드를 마치고 돌아오면 남은 행동을 잇는다. 지도 그림·인물 말은 아직 없다. 마을(종류 1)은 이름만 보인다.
/// </remarks>
internal sealed class Part1ChapterScreen(GameWindow host, Part1TitleScreen shell)
{
    /// <summary>장소를 늘어놓는 칸(640×480 기준).</summary>
    internal static readonly (int X, int Y, int W, int H) Area = (30, 78, 580, 330);

    internal const int SpotW = 116, SpotH = 22;

    /// <summary>아래 단추 — 연대표 · 파트1 타이틀.</summary>
    internal static readonly (int X, int Y, string Name)[] Buttons = [(90, 436, "연대표"), (250, 436, "일행"), (410, 436, "타이틀")];

    internal const int ButtonTimeline = 0, ButtonParty = 1, ButtonTitle = 2, ButtonHit = 1000;

    internal bool _open;
    internal Part1Chapter? _chapter;
    internal (int X, int Y)[] _spots = [];
    internal int _hover = -1;

    /// <summary>일행이 선 장소(차례) — 처음엔 첫 장소. 지도에 ◆ 로 보인다.</summary>
    internal int _party;

    /// <summary>챕터 사건 — 502 전투 · 503 필드로 가면 돌아와서 남은 행동을 잇는다.</summary>
    internal Part1SceneScript Events => _events ??= new(host) { SceneChange = OnSceneChange };
    private Part1SceneScript? _events;

    /// <summary>사건이 전투·필드로 보냈다 — 같은 챕터로 돌아오면 남은 행동을 잇는다.</summary>
    private bool _resume;

    private bool OnSceneChange(ScriptCommand c)
    {
        int target = c.Args[0];
        switch (c.Code)
        {
            case 502:
                _resume = true;
                host.LeaveScene(() => shell.StartBattle(target));
                return true;
            case 501:
                // 챕터 끝 — 장면 10(0x100c04b0 → 생성 0x100d2470 이 Eps\episode.elg 를 연다) = 연대표로. 끝낸 챕터는 연대표에 ✓.
                if (_chapter is { } done) host.Part1.FinishedChapters.Add(done.Id);
                host.LeaveScene(() => { _open = false; shell.Timeline.Open(); });
                return true;
            case 503:
                int chapterId = _chapter?.Id ?? -1;
                _resume = true;
                host.LeaveScene(() => { _open = false; shell.Field.Open(target, () => Open(chapterId)); });
                return true;
        }
        return false;
    }

    /// <summary>그 챕터를 연다 — 못 읽으면 알리고 false.</summary>
    internal bool Open(int chapterId)
    {
        bool resume = _resume && _chapter?.Id == chapterId;
        _resume = false;
        if (_chapter?.Id != chapterId) _party = 0;
        if (Part1Chapter.Load(chapterId) is not { Places.Count: > 0 } chapter)
        {
            host.Toast($"파트1 챕터 {chapterId} 를 못 읽었습니다");
            return false;
        }
        _chapter = chapter;
        host.Part1.ChapterId = chapterId;
        var places = chapter.Places;
        int minX = places.Min(p => p.X), maxX = places.Max(p => p.X), minY = places.Min(p => p.Y), maxY = places.Max(p => p.Y);
        static int Fit(int v, int min, int max, int start, int span) => start + (max == min ? span / 2 : (v - min) * span / (max - min));
        _spots = [.. places.Select(p => (Fit(p.X, minX, maxX, Area.X, Area.W - SpotW), Fit(p.Y, minY, maxY, Area.Y, Area.H - SpotH)))];
        // 같은 자리의 전투·마을(챕터 1 무타나비)이 겹치지 않게 — 앞 장소와 겹치면 한 칸씩 아래·위로 번갈아 비켜 본다(칸 안에서만).
        for (int i = 1; i < _spots.Length; i++)
        {
            var (x, y0) = _spots[i];
            foreach (int k in Enumerable.Range(1, 2 * _spots.Length).Select(n => n % 2 == 1 ? (n + 1) / 2 : -n / 2).Prepend(0))
            {
                int y = y0 + k * (SpotH + 2);
                if (y < Area.Y || y > Area.Y + Area.H - SpotH || Enumerable.Range(0, i).Any(j => Overlaps((x, y), _spots[j]))) continue;
                _spots[i] = (x, y);
                break;
            }
        }
        _open = true;
        _hover = -1;
        Events.Start(chapter.Events, chapter.Lines, host.Part1.ChapterFired(chapterId), resume);
        // 열 때마다 적어 둔다 — 사건이 전투·필드로 보냈으면 남은 행동까지(CONTINUE 가 그 자리에서 잇는다).
        host.Part1.PartyPlace = _party;
        host.Part1.PendingActions = [.. Events.SavePending];
        host.Part1.Save(host.FlagSt._flags);
        return true;
    }

    /// <summary>세이브에서 이어 간다 — 적어 둔 챕터를 열고 남은 행동을 잇는다.</summary>
    internal bool Continue()
    {
        _resume = host.Part1.PendingActions.Count > 0;
        Events.Restore(host.Part1.PendingActions);
        int place = host.Part1.PartyPlace;
        if (Part1Chapter.Load(host.Part1.ChapterId) is { } chapter) _chapter = chapter;   // 같은 챕터로 열어 resume 이 맞게
        if (!Open(host.Part1.ChapterId)) return false;
        _party = place;
        return true;
    }

    private static bool Overlaps((int X, int Y) a, (int X, int Y) b) => Math.Abs(a.X - b.X) < SpotW && Math.Abs(a.Y - b.Y) < SpotH;

    internal int HitAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        for (int i = 0; i < Buttons.Length; i++)
        {
            var (x, y, _) = Buttons[i];
            if (bx >= ox + x && bx < ox + x + Part1TitleScreen.ButtonW && by >= oy + y && by < oy + y + Part1TitleScreen.ButtonH) return ButtonHit + i;
        }
        // 나중에 그린 장소가 위에 있으니 뒤에서부터 본다.
        for (int i = _spots.Length - 1; i >= 0; i--)
        {
            var (x, y) = _spots[i];
            if (bx >= ox + x && bx < ox + x + SpotW && by >= oy + y && by < oy + y + SpotH) return i;
        }
        return -1;
    }

    internal void UpdateHover(int bx, int by) => _hover = Picking ? PickAt(bx, by) + PickHit : HitAt(bx, by);

    // ── 레벨업 고르기 — 전투에서 오른 레벨마다 직업 어빌리티 하나(원본 0x1009dac0 은 전투 안에서 고른다) ──

    internal const int PickHit = 2000, PickX = 150, PickY = 120, PickW = 340, PickRowH = 24;

    /// <summary>고를 레벨업이 있고 대사가 없다 — 지도 위에 고르기 창을 띄운다.</summary>
    internal bool Picking => host.Part1.LevelPicks.Count > 0 && Events.Talk == null;

    private (int Chr, List<(int Ability, int NewLevel, string Name)> Options)? _pickCache;

    /// <summary>지금 고를 인물과 후보 — 후보가 없는 것(다 올린 인물)은 건너뛴다.</summary>
    private (int Chr, int Level, List<(int Ability, int NewLevel, string Name)> Options)? CurrentPick()
    {
        while (host.Part1.LevelPicks.Count > 0)
        {
            var (chr, level) = host.Part1.LevelPicks[0];
            if (_pickCache is not { } cache || cache.Chr != chr) _pickCache = cache = (chr, host.Part1.PickCandidates(chr));
            if (cache.Options.Count > 0) return (chr, level, cache.Options);
            host.Part1.LevelPicks.RemoveAt(0);
            _pickCache = null;
        }
        return null;
    }

    private int PickAt(int bx, int by)
    {
        if (CurrentPick() is not { } pick) return -1;
        var (ox, oy) = host.Mos.MosesOrigin();
        for (int i = 0; i < pick.Options.Count; i++)
            if (bx >= ox + PickX && bx < ox + PickX + PickW && by >= oy + PickY + 40 + i * PickRowH && by < oy + PickY + 40 + (i + 1) * PickRowH - 2) return i;
        return -1;
    }

    private void PressPick(int i)
    {
        if (CurrentPick() is not { } pick || i < 0 || i >= pick.Options.Count) return;
        var option = pick.Options[i];
        host.Part1.Pick(pick.Chr, option.Ability, option.NewLevel);
        host.Part1.LevelPicks.RemoveAt(0);
        _pickCache = null;
        host._db?.ForgetPart1Characters();
        host.Btl.Part1.Forget(pick.Chr);
        string who = host._db?.Character(pick.Chr) is { } c ? host._db.T(c.NameId) : "";
        host.Toast($"{who} — {option.Name} Lv {option.NewLevel}");
        host.Part1.Save(host.FlagSt._flags);
    }

    private void DrawPick(int ox, int oy)
    {
        if (CurrentPick() is not { } pick) return;
        int h = 52 + pick.Options.Count * PickRowH;
        host.UiFill(ox + PickX - 10, oy + PickY - 10, PickW + 20, h, 0xF0101828);
        host.UiStroke(ox + PickX - 10, oy + PickY - 10, PickW + 20, h, 0xFFFFE080);
        string name = host._db?.Character(pick.Chr) is { } c ? host._db.T(c.NameId) : $"Chr {pick.Chr}";
        host.UiText($"{name} 레벨 {pick.Level} — 올릴 어빌리티를 고르세요", ox + PickX, oy + PickY, 0xFFFFE080, 13);
        for (int i = 0; i < pick.Options.Count; i++)
        {
            var (_, level, label) = pick.Options[i];
            int y = oy + PickY + 40 + i * PickRowH;
            bool hot = _hover == PickHit + i;
            host.UiFill(ox + PickX, y, PickW, PickRowH - 2, hot ? 0xF0305080u : 0xC0202838u);
            host.UiText(level == 1 ? $"{label} (새로 배움)" : $"{label} Lv {level - 1} → {level}", ox + PickX + 10, y + 3, GameWindow.White, 12);
        }
    }

    internal void OnClick(int bx, int by)
    {
        if (Events.Talk != null) { Events.Advance(); return; }   // 대사가 떠 있으면 누르면 넘어간다
        if (Picking) { PressPick(PickAt(bx, by)); return; }
        Press(HitAt(bx, by));
    }

    /// <summary>Esc 는 파트1 타이틀로, Enter · Space 는 짚은 것을 누른다.</summary>
    internal void OnKey(int key)
    {
        if (Events.Talk != null) { if (key is Win32.VK_RETURN or Win32.VK_SPACE or Win32.VK_ESCAPE) Events.Advance(); return; }
        if (Picking) { if (key is Win32.VK_RETURN or Win32.VK_SPACE) PressPick(Math.Max(0, _hover - PickHit)); return; }   // 고르기 창은 닫을 수 없다(짚은 것 없으면 첫 후보)
        if (key == Win32.VK_ESCAPE) Press(ButtonHit + ButtonTitle);
        else if (key is Win32.VK_RETURN or Win32.VK_SPACE && _hover >= 0) Press(_hover);
    }

    private void Press(int hit)
    {
        if (_chapter is not { } chapter) return;
        switch (hit)
        {
            case ButtonHit + ButtonTimeline: host.LeaveScene(() => { _open = false; shell.Timeline.Open(); }); break;
            case ButtonHit + ButtonTitle: host.LeaveScene(() => _open = false); break;
            case ButtonHit + ButtonParty:
            {
                int chapterId = chapter.Id;
                host.LeaveScene(() => { _open = false; shell.Party.Open(() => Open(chapterId)); });
                break;
            }
            case >= 0 when hit < chapter.Places.Count:
                // 일행을 그 장소로 옮긴다 — 닿으면 조건 200 사건(챕터 0 카디스요새 → 필드 2 · 전투 6 …)이 돈다.
                // 아무 사건도 없으면 전투 장소는 그 전투를, 마을은 아직 없다고 알린다.
                var place = chapter.Places[hit];
                _party = hit;
                if (Events.Arrive(hit)) break;
                if (place.IsBattle) host.LeaveScene(() => shell.StartBattle(place.Target));
                else if (place.IsTown)
                {
                    int chapterId = chapter.Id;
                    host.LeaveScene(() => { _open = false; shell.Town.Open(place.Target, () => Open(chapterId)); });   // 마을 = 그 번호의 필드(시설)
                }
                break;
        }
    }

    internal void Draw()
    {
        if (_chapter is not { } chapter) return;
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        host.UiFill(ox, oy, MosesScene.MosesW, MosesScene.MosesH, 0xFF141A24);
        host.UiFill(ox + Area.X - 8, oy + Area.Y - 8, Area.W + 16, Area.H + 16, 0xFF1C2433);
        host.UiStroke(ox + Area.X - 8, oy + Area.Y - 8, Area.W + 16, Area.H + 16, 0xFF3A4A66);

        string title = chapter.Title is { Length: > 0 } t ? Part1Game.PlainText(t).Replace('\n', ' ') : $"챕터 {chapter.Id}";
        var (_, tw, _) = host.UiGetText(title, GameWindow.White, 18);
        host.UiText(title, ox + (MosesScene.MosesW - tw) / 2, oy + 18, GameWindow.White, 18);
        if (Part1Game.PlainText(chapter.Region).Replace('\n', ' ') is { Length: > 0 } region)
        {
            var (_, rw, _) = host.UiGetText(region, 0xFFB0C0D8, 12);
            host.UiText(region, ox + (MosesScene.MosesW - rw) / 2, oy + 46, 0xFFB0C0D8, 12);
        }

        // 잇닿은 장소(낱말 9~13, 가설) — 점선.
        for (int i = 0; i < chapter.Places.Count; i++)
            foreach (short next in chapter.Places[i].Words[9..14])
                if (next > i && next < _spots.Length) DottedLine(ox, oy, _spots[i], _spots[next]);

        for (int i = 0; i < chapter.Places.Count; i++)
        {
            var place = chapter.Places[i];
            var (x, y) = _spots[i];
            bool cleared = place.IsBattle && host.Part1.Cleared.Contains(place.Target);
            uint fill = place.IsBattle ? cleared ? 0xD0404850u : 0xD0702828u : place.IsTown ? 0xD0284468u : 0xA0303030u;
            host.UiFill(ox + x, oy + y, SpotW, SpotH, _hover == i ? fill | 0xFF000000 : fill);
            host.UiStroke(ox + x, oy + y, SpotW, SpotH, _hover == i ? 0xFFFFE080u : place.IsBattle && !cleared ? 0xFFD07060u : 0xFF607090u);
            string name = Part1Game.PlainText(chapter.NameOf(place)).Replace('\n', ' ');
            if (name.Length == 0) name = place.IsBattle ? $"전투 {place.Target}" : "길목";
            if (cleared) name = "✓ " + name;
            if (i == _party) host.UiText("◆", ox + x - 12, oy + y + 4, 0xFFFFE080, 12);
            var (_, nw, nh) = host.UiGetText(name, GameWindow.White, 11);
            host.UiText(name, ox + x + Math.Max(2, (SpotW - nw) / 2), oy + y + (SpotH - nh) / 2, GameWindow.White, 11);
        }

        // 짚은 전투 장소의 전투 이름 — 아래 줄에.
        if (_hover >= 0 && _hover < chapter.Places.Count && chapter.Places[_hover] is { IsBattle: true } hovered
            && Part1Battle.Load(hovered.Target) is { } battle)
        {
            string line = $"전투 {hovered.Target} 「{battle.Line(battle.TitleLine)}」 — 승리: {Part1Game.PlainText(battle.Line(battle.WinLine)).Replace('\n', ' ')}";
            host.UiText(line, ox + Area.X, oy + Area.Y + Area.H + 10, 0xFFFFE080, 12);
        }

        for (int i = 0; i < Buttons.Length; i++)
        {
            var (x, y, name) = Buttons[i];
            shell.DrawButton(ox + x, oy + y, Part1TitleScreen.ButtonW, Part1TitleScreen.ButtonH, name, _hover == ButtonHit + i);
        }
        if (Events.Talk is { } talk) shell.DrawTalk(ox, oy, talk.Speaker, talk.Text);
        else if (Picking) DrawPick(ox, oy);
        host.DrawToast();
    }

    private void DottedLine(int ox, int oy, (int X, int Y) a, (int X, int Y) b)
    {
        int x0 = a.X + SpotW / 2, y0 = a.Y + SpotH / 2, x1 = b.X + SpotW / 2, y1 = b.Y + SpotH / 2;
        int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) / 6;
        for (int s = 0; s <= steps; s++)
        {
            int x = x0 + (x1 - x0) * s / Math.Max(1, steps), y = y0 + (y1 - y0) * s / Math.Max(1, steps);
            host.UiFill(ox + x - 1, oy + y - 1, 2, 2, 0xFF7088B0);
        }
    }
}

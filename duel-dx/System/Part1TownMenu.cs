using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 마을 첫 화면 — 필드 그림 위의 이름 띠 · 시설 단추(건물 아이콘) · 일행 얼굴 10칸 · EXIT. 원본 <c>alpha.dll</c> 필드 장면(분석-파트1-챕터 8.2절).
/// </summary>
/// <remarks>
/// <para>단추: 필드 인물 칸의 낱말 5 가 −1 이 아니면 인물 대신 시설 단추다(필드 로더 뒤 <c>0x100c763b</c>) — 단추 클래스 <c>0x1009fdd0</c>,
/// id 10000 + 칸, 자리 (491, 51 + 40 × 칸), 140×36, 바탕 Obs 678 모션 0 · 1(짚음) · 2(누름 — 리메이크는 안 그림), 글 = 필드 글 줄[낱말 5] 흰 글꼴 2 를 왼쪽 45 에서(<c>0x100a0490(2, 0, 45)</c>),
/// 아이콘 = 그 칸의 Obs(낱말 0) 모션 0 을 단추 (0, 0) 에. 낱말 4 가 −1 이 아니면 그 진행 깃발이 서 있을 때만 보인다(<c>0x100c8660</c>).</para>
/// <para>단추 칸 n 을 누르면 조건 200 [n] 사건이 돈다(<c>0x100c5ca5</c> → <c>0x101b4524</c> = n, 조건 <c>0x100cd040</c>) — 사건 차례가 아니라 칸 번호다(0109 는 사건 차례와 다르다).</para>
/// <para>틀(<c>0x100c82c0</c>): 위 창 (0, 7) 640×33 — Obs 678 모션 3 (0, 0) · 모션 4 (552, 33) 장식, 이름 = 글 줄[머리 낱말 3] 글꼴 6 을 (320, 23) 가운데.
/// 아래 창 (0, 360) 640×120 — Obs 678 모션 5 · 6 (75, 369) 틀, 얼굴 10칸 가운데 (72 + 55i, 420)(없으면 Obs 217 모션 2 「Empty」 (47 + 55i, 395)), EXIT 0x3e8 (577, 457) Obs 370.</para>
/// <para>얼굴 단추 5000 + i 를 누르면(<c>0x100c67e7</c>) 시설 단추를 숨기고 그 팀 사람의 인물 창(<c>0x100cb860</c>, 자리 (320, 130), 인물 레코드 <c>0x101ab2a8</c> + Chr × 0x154)을 연다 —
/// 리메이크는 챕터 지도 상태 창의 얼굴처럼 전투와 같은 상태 패널(<see cref="Part1StatusPanel"/>)을 띄운다(<see cref="Part1TownScreen"/>).</para>
/// </remarks>
internal sealed class Part1TownMenu(GameWindow host)
{
    internal const int ButtonX = 491, ButtonY = 51, ButtonStep = 40, ButtonW = 140, ButtonH = 36, IconObs = 678, ExitObs = 370;
    internal const int ExitX = 577, ExitY = 457, ExitW = 60, ExitH = 15, HitExit = -2;

    /// <summary>얼굴 단추 i 의 짚기 값 — 원본 단추 id 5000 + i(가운데 (72 + 55i, 420), 50×50).</summary>
    internal const int HitFace = 5000, FaceX = 72, FaceY = 420, FaceStep = 55;

    /// <summary>시설 단추 하나 — 필드 인물 칸 차례(= 조건 200 의 값), 아이콘 Obs, 글.</summary>
    internal sealed record Button(int Slot, int Obs, string Label, int Flag);

    private readonly Part1MapArt _art = new(host);
    private List<Button> _buttons = [];
    private List<Part1FieldSprite> _props = [];
    private string _name = "";

    internal void Open(Part1Field field)
    {
        string Line(int i) => i >= 0 && i < field.Lines.Count ? field.Lines[i] : "";
        _buttons = [];
        _props = [];
        for (int i = 0; i < field.Sprites.Count; i++)
        {
            var s = field.Sprites[i];
            if (s.Words[5] == -1) _props.Add(s);
            else _buttons.Add(new Button(i, s.SpriteId, Part1Game.PlainText(Line(s.Words[5])), s.Words[4]));
        }
        _name = field.Header.Length > 3 ? Part1Game.PlainText(Line(field.Header[3])) : "";
    }

    private bool Shown(Button b) => b.Flag == -1 || b.Flag >= 0 && b.Flag < host.FlagSt._flags.Length && host.FlagSt._flags[b.Flag] != 0;

    /// <summary>화면 자리(640×480 안)의 단추 — 칸 번호 · <see cref="HitExit"/> · 없으면 −1.</summary>
    internal int HitAt(int x, int y)
    {
        if (x >= ExitX && x < ExitX + ExitW && y >= ExitY && y < ExitY + ExitH) return HitExit;
        int people = Math.Min(10, host.Part1.ActiveMembers().Count);
        for (int i = 0; i < people; i++)
            if (Math.Abs(x - (FaceX + FaceStep * i)) < 25 && Math.Abs(y - FaceY) < 25) return HitFace + i;
        foreach (var b in _buttons)
        {
            int by = ButtonY + ButtonStep * b.Slot;
            if (Shown(b) && x >= ButtonX && x < ButtonX + ButtonW && y >= by && y < by + ButtonH) return b.Slot;
        }
        return -1;
    }

    /// <summary>모션 틱의 섞기 키대로 — 단추 바탕 · 틀 장식은 섞기 9(더하기)라 바탕 그림이 비친다.</summary>
    private void DrawObs(int obs, int motion, int x, int y)
    {
        int id = Part1Game.ObsBase + obs;
        int key = host.UiFor(id) is { } sprite ? sprite.BlendAt(motion, 0) : 0;
        host.DrawUi(id, motion, 0, x, y, GameWindow.BlendOf(key), loop: false, fade: GameWindow.BlendFade(key));
    }

    internal void Draw(int ox, int oy, int hover)
    {
        _art.Clip = (ox, oy, 640, 480);
        foreach (var p in _props) DrawObs(p.SpriteId, 0, ox + p.X, oy + p.Y);

        // 위 창 — 이름 띠.
        _art.Box(ox, oy + 7, 640, 33);
        DrawObs(IconObs, 3, ox, oy);
        DrawObs(IconObs, 4, ox + 552, oy + 33);
        if (_name.Length > 0) _art.TextCentered(_name, ox + 320, oy + 23, GameWindow.White, 16);

        // 시설 단추.
        foreach (var b in _buttons)
        {
            if (!Shown(b)) continue;
            int bx = ox + ButtonX, by = oy + ButtonY + ButtonStep * b.Slot;
            DrawObs(IconObs, hover == b.Slot ? 1 : 0, bx, by);
            DrawObs(b.Obs, 0, bx, by);
            var (_, _, th) = host.UiGetText(b.Label, GameWindow.White, 13);
            host.UiText(b.Label, bx + 45, by + (ButtonH - th) / 2, GameWindow.White, 13);
        }

        // 아래 창 — 일행 얼굴 10칸 · EXIT.
        _art.Box(ox, oy + 360, 640, 120);
        DrawObs(IconObs, 5, ox + 75, oy + 369);
        DrawObs(IconObs, 6, ox + 75, oy + 369);
        var members = host.Part1.ActiveMembers();
        for (int i = 0; i < 10; i++)
        {
            if (i >= members.Count || !_art.Face(members[i], ox + FaceX + FaceStep * i, oy + FaceY))
                _art.Blit(_art.Frame(217, 2, 0), ox + 47 + 55 * i, oy + 395);
            else if (hover == HitFace + i) _art.Stroke(ox + FaceX + FaceStep * i - 25, oy + FaceY - 25, 50, 50, 0xFFFFE080);
        }
        DrawObs(ExitObs, 0, ox + ExitX, oy + ExitY);
        _art.TextCentered("EXIT", ox + ExitX + ExitW / 2, oy + ExitY + ExitH / 2, hover == HitExit ? 0xFFFFE080u : GameWindow.White, 11);
    }
}

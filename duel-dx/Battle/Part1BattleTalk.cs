namespace DuelDx;

using WarOfGenesis.Assets;

/// <summary>
/// 파트1 전투 대사 — 행동 200 [말하는 이, 줄, 얼굴](<c>0x1001e200</c>). 필드 209 · 전투 202 와 같은 창 <c>0x100a7ef0</c>(<see cref="Part1Bubble"/>)를
/// 말하는 이 위에 띄운다. 파트2 말풍선(601, 틀 Obs 0221 · 이름 탭 · 작은 얼굴)이 아니다.
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-전투」 36절 · 「분석-파트1-챕터」 11.3 · 11.5절. <c>Part1Script</c> 가 200 을 601 [a0, 0, a1] 로 옮기고, <see cref="TalkBox.ShowTalk"/> 가
/// 파트1 판이면 여기로 보낸다 — 창은 <see cref="TalkBox.TalkWindow"/> 하나로 대사 목록에 들어가 기다림(행동 1) · 건너뛰기 · 누름은 파트2 말풍선과 같은 길을 탄다.
/// <list type="bullet">
/// <item>칸 단계 0(<c>0x1001e2a2</c>): 말하는 이가 판에 있으면 카메라를 그 유닛으로(<c>0x1009a080</c> → 판 <c>0x100325e0</c>), 단계 1 은 카메라가 설 때까지
/// (<c>0x1009a190</c>) — 리메이크는 601 의 카메라 줄(<c>EventCameraWaits</c>)이 그대로 한다. 유닛이 없으면 곧바로 단계 2.</item>
/// <item>단계 2(<c>0x1001e320</c>): 얼굴 = a2, 0 이면 CChr <c>+0x46</c>(칸 4 반신 초상). <b>편(<c>+0x1f9</c>) 4 · 3</b> 이면 틀 모션 <b>1</b>(Obs 111 벌 0 파란 돌) ·
/// 이름 빛깔 <b>7</b>(0, 255, 150), <b>그 밖(적)</b>은 틀 모션 <b>8</b>(벌 4 붉은 돌) · 이름 빛깔 <b>8</b>(255, 220, 0)(<c>0x1001e38d push 8</c> · <c>0x1001e3f5</c> ·
/// <c>0x1001e442 push 1</c> · <c>0x1001e4aa</c>). 닻 = 유닛 <c>+0x2a/+0x2c</c> − 카메라 <c>+0xb9b0/+0xb9b2</c>, 창 −1(글로 크기 · 닻 위 50).
/// 판에 없고 a0 &lt; 106 이면 전역 인물 칸(<c>0x101ab2a8 + a0×0x154</c>)의 짧은 이름 · 초상으로 창 (320 − 203 폭, 240 − 203 높이), 닻 (−10, −10), 틀 모션 9(벌 5),
/// 이름 빛깔은 창 기본 7(<c>0x100a7f84</c>). a0 ≥ 106 이면 창 없이 끝(<c>0x1001e265</c> → <c>0x1001e5b9</c>).</item>
/// <item>단계 3(<c>0x1001e581</c>): 창이 다 사라지면(<c>+0x40 == 0 · +0x69 == 0xff</c>) <c>0x10031b90</c> · 창 지우기 · 칸 끝. 창은 쪽을 다 찍은 뒤 150틱이면 넘긴다.</item>
/// <item><b>음성은 없다</b> — 200 은 인자 0 · 1 · 2 만 읽는다(<c>[칸 +0x18]</c> 낱말 0 · 2 · 4). 파트2 600 의 a3 음성 같은 칸이 없다.</item>
/// </list>
/// [H] 안 따른 것: 탄 유닛(<c>+0x1bc</c>)이면 자리는 탈것(<c>+0x1cc</c>) · 이름 · 초상은 탄 이 — 리메이크는 말하는 이 유닛 그대로 ·
/// a2 얼굴(시판 자료는 늘 0 — Part1Script 가 601 로 옮길 때 버린다) · <c>0x10031ac0</c>/<c>0x10031b90</c>(커서 그림) · 누름 처리(필드 말풍선처럼 [H]) ·
/// 닻 = 리메이크 유닛 발밑(<c>UnitFoot</c>)으로 봄 [H: <c>+0x2a/+0x2c</c> 가 발밑인지는 안 봤다].
/// </remarks>
internal sealed class Part1BattleTalk(GameWindow host)
{
    /// <summary>쪽을 다 찍은 뒤 넘기기까지의 틱(<c>0x1001e3d0 push 0x96</c>).</summary>
    private const int Ticks = 150;

    /// <summary>Obs 111 틀 벌 — 모션 1 = 벌 0(편 4 · 3) · 모션 8 = 벌 4(적) · 모션 9 = 벌 5(판에 없는 이).</summary>
    private const int AllySet = 0, EnemySet = 4, AbsentSet = 5;

    /// <summary>판에 없는 이를 전역 인물 칸에서 찾는 한계(<c>0x1001e261 cmp ax, 0x6a</c>).</summary>
    private const int PersonCount = 0x6a;

    private readonly Part1MapArt _art = new(host);
    private readonly Dictionary<int, Part1Chr?> _chrs = [];
    private int _serial;

    private Part1Chr? Chr(int code) => _chrs.TryGetValue(code, out var c) ? c : _chrs[code] = Part1Chr.Load(code);

    /// <summary>200 을 창으로 — 띄울 것이 없으면 null(줄을 그냥 지나간다).</summary>
    internal TalkBox.TalkWindow? Open(int speakerArg, int speaker, string raw)
    {
        var rows = Part1BattleNarration.Rows(raw);
        if (rows.Count == 0) return null;
        var bubble = new Part1Bubble(host, _art);
        var (ox, oy) = host.Mos.MosesOrigin();
        string name;
        if (speaker >= 0 && host._units[speaker] is { Alive: true, OnField: true } u)
        {
            var chr = u.ChrCode >= Part1Game.ChrBase ? Chr(u.ChrCode - Part1Game.ChrBase) : null;
            name = chr?.ShortName is { Length: > 0 } shortName ? shortName : host._names.GetValueOrDefault(u.ChrCode, "");
            bool ally = u.Side is 4 or 3;
            var (fx, fy) = host.Btl.UnitFoot(u);
            bubble.OpenBattle(++_serial, name, Part1BattleNarration.Colors[ally ? 7 : 8], rows, chr?.BustId ?? 0, Ticks,
                              fx - ox, fy - oy, -1, -1, ally ? AllySet : EnemySet);
        }
        else
        {
            if (speakerArg is < 0 or >= PersonCount || Chr(speakerArg) is not { } person) return null;
            name = person.ShortName.Length > 0 ? person.ShortName : person.Name;
            var (w, h) = host.Btl.Part1Narration.Size;
            bubble.OpenBattle(++_serial, name, Part1BattleNarration.Colors[7], rows, person.BustId, Ticks,
                              -10, -10, 320 - w, 240 - h, AbsentSet);
        }
        return new TalkBox.TalkWindow { Kind = 601, Speaker = speaker, Name = name, Text = Part1Game.PlainText(raw), Part1 = bubble };
    }

    /// <summary>창 해상도 640×480 원점(판 가운데의 원본 화면 네모 — 202 해설 상자와 같다)에.</summary>
    internal void Draw(TalkBox.TalkWindow w)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        w.Part1?.Draw(ox, oy);
    }
}

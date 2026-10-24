using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 챕터 사건 — 챕터를 열 때 사건 0 을 부르고, 사건 0 의 행동 0(사건 부르기)이 조건이 맞는 사건을 차례로 돌린다(전투 사건과 같은 꼴).
/// </summary>
/// <remarks>
/// 옵시디안 「분석-파트1-챕터」 4절. 옮긴 것: 조건 0(언제나) · 101 [깃발, 값, 연산](진행 깃발 비교 — 전투 401 과 같은 자리 차례) ·
/// 행동 0 사건 부르기 · 202 [Chr, 줄] 대사 · 205 대사 창 닫기 · 502 [전투] 그 전투로(마치면 남은 행동을 이어 돈다) · 802 [깃발, 값] 깃발 세우기.
/// 503 [n] 은 챕터가 아니라 <b>필드</b> <c>Flds/NNNN.fld</c> 로 간다(장면 종류 5 의 생성 <c>0x100c77f0</c> 이 그 파일을 연다) — 파트1 필드는 아직 안 옮겨 알리고 건너뛴다.
/// 나머지(900대 지도 연출 · 1000 인물 세우기 · 500대 소리 따위)는 건너뛴다. 발동 수는 <see cref="Part1Progress.Fired"/> 에 남아,
/// 전투를 마치고 지도로 돌아와도 들머리 대사가 다시 나오지 않는다. 진행 깃발은 전투 사건(702·703 → 파트2 102·103)과 같은 <see cref="FlagStore"/> 다.
/// </remarks>
internal sealed class Part1ChapterEvents(GameWindow host, Part1TitleScreen shell)
{
    private readonly List<ScriptCommand> _pending = [];
    private Part1Chapter? _chapter;

    /// <summary>떠 있는 대사(말하는 이, 글) — 누르면 다음으로.</summary>
    internal (string Speaker, string Text)? Talk { get; private set; }

    /// <summary>502 로 전투에 갔다 — 그 챕터로 돌아오면 남은 행동을 이어 돈다.</summary>
    private bool _resume;

    internal void Start(Part1Chapter chapter)
    {
        if (_resume && _chapter?.Id == chapter.Id)
        {
            _resume = false;
            _chapter = chapter;
            Run();
            return;
        }
        _resume = false;
        _chapter = chapter;
        _pending.Clear();
        Talk = null;
        Call(0);
        Run();
    }

    /// <summary>대사를 넘긴다.</summary>
    internal void Advance()
    {
        Talk = null;
        Run();
    }

    private void Call(int index)
    {
        if (_chapter is not { } chapter || chapter.Events.FirstOrDefault(e => e.Index == index) is not { } e) return;
        var key = (chapter.Id, index);
        int fired = host.Part1.Fired.GetValueOrDefault(key);
        if (e.MaxFire >= 0 && fired >= e.MaxFire) return;
        if (!e.Conditions.All(Holds)) return;
        host.Part1.Fired[key] = fired + 1;
        _pending.InsertRange(0, e.Actions);
    }

    private bool Holds(ScriptCommand c)
    {
        short[] a = c.Args;
        return c.Code switch
        {
            0 => true,
            101 => BattleScene.Compare(a[0] >= 0 && a[0] < host.FlagSt._flags.Length ? host.FlagSt._flags[a[0]] : 0, a[2], a[1]),
            _ => false,                                                     // 아직 안 푼 조건은 안 터뜨린다
        };
    }

    private void Run()
    {
        while (_pending.Count > 0 && Talk == null)
        {
            var c = _pending[0];
            _pending.RemoveAt(0);
            short[] a = c.Args;
            switch (c.Code)
            {
                case 0: Call(a[0]); break;
                case 202:
                {
                    string raw = _chapter is { } ch && a[1] >= 0 && a[1] < ch.Lines.Count ? ch.Lines[a[1]] : "";
                    if (raw.Length > 0) Talk = (Part1Game.Speaker(raw), Part1Game.PlainText(raw));
                    break;
                }
                case 205: Talk = null; break;
                case 502:
                {
                    int battle = a[0];
                    _resume = true;
                    host.LeaveScene(() => shell.StartBattle(battle));
                    return;
                }
                case 503: host.Toast($"파트1 필드 {a[0]:D4} — 아직 옮기지 않아 건너뜁니다"); break;
                case 802:
                    if (a[0] >= 0 && a[0] < host.FlagSt._flags.Length) host.FlagSt._flags[a[0]] = (byte)Math.Clamp((int)a[1], 0, 255);
                    break;
            }
        }
    }
}

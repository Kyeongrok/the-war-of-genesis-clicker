namespace WarOfGenesis.Assets;

/// <summary>
/// 원본에 없는 파트1 전투 대사 — 판을 읽은 뒤 대사 줄을 덧붙이고 그 줄을 말하는 행동을 사건 끝에 더한다(사용자 요청).
/// </summary>
/// <remarks>
/// 대사(<c>.dlg</c>)는 사용자의 파트1 폴더에서 읽으므로 고치지 않고, 줄은 원본 줄 뒤 번호로 붙인다. 행동은 원본 꼴 그대로 —
/// 200 [말하는 이 Chr, 줄] 대사 상자 · 1 기다림(<c>0x1001e200</c>, 분석-파트1-전투 6절).
/// </remarks>
public static class Part1BattleAdditions
{
    /// <summary>판 → (덧붙일 사건 번호, 말하는 이 Chr, 대사) 들.</summary>
    private static readonly Dictionary<int, (int Event, int Speaker, string Text)[]> Lines = new()
    {
        // 0012 「라쉬카의 역습」 — 시작 인트로(사건 3: 「모두들 정신 바짝차리고 적들을 상대하도록!」) 뒤에 라쉬카가 받는다.
        [12] = [(3, 27, "웬 놈들이냐! 모두 처리해라!")],
    };

    public static Part1Battle Apply(Part1Battle battle)
    {
        if (!Lines.TryGetValue(battle.Id, out var adds)) return battle;
        var lines = battle.Lines.ToList();
        var events = battle.Events.ToList();
        foreach (var (index, speaker, text) in adds)
        {
            int at = events.FindIndex(e => e.Index == index);
            if (at < 0) continue;
            lines.Add(text);
            var e = events[at];
            events[at] = e with
            {
                Actions = [.. e.Actions, new ScriptCommand(200, [(short)speaker, (short)(lines.Count - 1), 0, 0, 0, 0]),
                           new ScriptCommand(1, [0, 0, 0, 0, 0, 0])],
            };
        }
        return battle with { Lines = lines, Events = events };
    }
}

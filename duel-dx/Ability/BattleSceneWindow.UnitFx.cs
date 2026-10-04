using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 기술이 <b>유닛 자체</b>를 숨기거나 흐리게 하는 연출(ba-21 fx F13) — 원본 핸들러가 애니메이터의 「안 그림」 깃발(<c>0x100eadb0</c>)과
/// 밝기 0~9(<c>0x100d0470</c>)를 틱에 맞춰 건다. 표는 <see cref="WorkUnitFx"/>(도구 <c>tools/re/work_unit_fx.py</c>).
/// </summary>
/// <remarks>
/// 희생 · 익스플로젼(시전자가 40틱부터 옅어짐), 블라인드 · 오버 테이크(대상이 옅어졌다 돌아옴), 아스트럴 파이어(대상이 잠깐 안 보임),
/// 브레인 브레이크(시전자·대상 말고 화면의 모두가 밝기 2). 워핑 · 리콜 · 이스케이프 · 회피는 리메이크가 손으로 움직이므로 뺀다
/// (Push.cs · Teleport.cs · Turn.cs — 둘이 같은 <see cref="UnitState.Fade"/> 를 다투지 않게).
/// 밝기 값을 화면 밝기로 옮기는 식(값 ÷ 9)은 가설이다 — 원본은 그 값을 섞기 단계로 쓴다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>손으로 Fade 를 움직이는 work — 워핑 422 · 1301~1309, 리콜 421 · 1292~1300, 이스케이프 1583, 회피 515.</summary>
    internal static bool UnitFxByHand(int id) => id is 421 or 422 or 515 or 1583 || id is >= 1292 and <= 1309;

    /// <summary>돌고 있는 유닛 연출 — (유닛, 시작 때(게임 초), 그 유닛에 걸린 사건들).</summary>
    internal readonly List<(UnitState Unit, double Start, WorkUnitFx.Ev[] Events)> _unitFx = [];

    /// <summary>기술이 치는 순간(핸들러 단계 0)에 부른다 — 표에 있으면 시전자·대상·그 밖의 유닛에 사건을 건다.</summary>
    internal void StartUnitFx(WorkData w, UnitState user, int col, int row)
    {
        if (UnitFxByHand(w.Id)) return;
        WorkUnitFx.Table.TryGetValue(w.Id, out var own);
        WorkUnitFx.Others.TryGetValue(w.Id, out var others);
        if (own == null && others == null) return;
        var aimed = LiveUnitAt(col, row);
        var inArea = (_fxTargets ?? WorkTargets(w, user, col, row)).Select(i => _units[i]).ToList();
        void Add(UnitState u, WorkUnitFx.Ev[] events)
        {
            if (events.Length == 0 || !u.OnField) return;
            _unitFx.RemoveAll(f => f.Unit == u);
            _unitFx.Add((u, _lastTime, events));
        }
        if (own != null)
        {
            Add(user, [.. own.Where(e => e.Who == 0)]);
            if (own.Where(e => e.Who == 1).ToArray() is { Length: > 0 } onAimed && aimed != null && aimed != user) Add(aimed, onAimed);
            if (own.Where(e => e.Who == 2).ToArray() is { Length: > 0 } onEach)
                foreach (var t in inArea.Where(t => t != user)) Add(t, onEach);
        }
        if (others != null)
            foreach (var u in _units.Where(u => u.Alive && u.OnField && u != user && u != aimed)) Add(u, [.. others.Where(e => e.Who == 3)]);
    }

    /// <summary>매 갱신 — 사건을 틱에 맞춰 <see cref="UnitState.Fade"/> 로 옮긴다. 마지막 사건이 끝나면 또렷하게 되돌리고 뺀다.</summary>
    internal void StepUnitFx()
    {
        for (int i = _unitFx.Count - 1; i >= 0; i--)
        {
            var (unit, start, events) = _unitFx[i];
            int tick = (int)((_lastTime - start) * TicksPerSecond);
            int hiddenUntil = -1, level = 9, end = 0;
            foreach (var e in events)
            {
                int span = e.Kind == 2 ? Math.Abs(e.To - e.From) * e.Step : 0;
                end = Math.Max(end, e.Tick + span);
                if (e.Tick > tick) continue;
                switch (e.Kind)
                {
                    case 0: hiddenUntil = e.Tick + e.Ticks; break;
                    case 1: hiddenUntil = -1; break;
                    default:
                        int moved = e.Step > 0 ? Math.Min(Math.Abs(e.To - e.From), (tick - e.Tick) / e.Step) : Math.Abs(e.To - e.From);
                        level = e.From + Math.Sign(e.To - e.From) * moved;
                        break;
                }
            }
            bool done = tick > end || !unit.Alive;
            if (done)
            {
                if (unit.Alive) unit.Fade = 1;      // 쓰러진 유닛(희생·익스플로젼)은 옅은 채 둔다 — 원본도 되돌리지 않는다
                _unitFx.RemoveAt(i);
                continue;
            }
            unit.Fade = tick < hiddenUntil ? 0 : level / 9.0;
        }
    }
}

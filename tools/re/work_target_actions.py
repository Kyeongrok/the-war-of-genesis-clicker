"""work(기술)이 <b>대상에게</b> 거는 동작을 뽑아 duel-dx 가 읽는 C# 표(`AbilityTargetActions.g.cs`)로 적는다 (ba-20 X).

    python tools/re/work_target_actions.py "<게임 폴더>" [출력.cs]

원본 핸들러는 시전자뿐 아니라 겨눈 유닛(`[나+0x8c]`)에게도 SetAction(0x10072820)을 건다 — 맞음 자세 2 를 붙들었다가
(반복 1000) 판정 뒤 서기 0 으로 되돌리는 꼴이 대부분이다(비·쇼크·브레인 스톰·웹폰 크래쉬·다이나믹 크래쉬·블라인드·
안티 밸런싱·미라클·아이템 1609~1617). 이 동작들은 시전자 사슬(`AbilityScripts.g.cs` 의 Actions)에 넣으면 안 된다.

표: work → [(Tick, Action, Hold), …]
  Action = 동작 번호(SetAction 의 기준 번호 ÷ 3 — Actions 사슬과 같은 번호. 모션 = 동작 × 3 + 방향). 원시 모션(0x100e53c0)이면 1000 + 모션.
  Hold   = 반복 인자가 1000 이상(다음 동작이 올 때까지 붙든다).
  Tick   = 치는 순간(핸들러가 대상에게 메시지 1001 을 보내는 단계·틱) 기준 상대 틱. 음수 = 그 앞.
           같은 단계 안이면 틱 차이를 그대로 쓴다(확정). 앞 단계의 것은 사이 단계들이 「틱 N 까지 기다림」(cmp [+0x96], N)으로
           넘어갈 때만 그 합으로 셈한다. 사이 단계가 애니·이펙트가 끝나기를 기다리면 길이를 정적으로 못 구하므로
           <b>0 으로 두고 줄 끝에 「틱 가설」</b>을 적는다 — 그런 줄은 차례(앞의 것이 먼저)만 믿을 것.
           1001 을 안 보내는 핸들러(비 = 피해 슬롯, 쇼크 = SOUL 직접 쓰기)는 마지막 대상 동작의 단계를 기준으로 삼는다(가설).
단계·틱은 `work_fx_emu.analyze_all()`(단계·틱·방향을 정해 놓고 핸들러를 따라가는 기호 실행, 방향 1)에서 온다.
"""
import collections
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import work_script as ws                                    # noqa: E402
import work_fx_emu as emu                                   # noqa: E402


def is_target(this):
    return isinstance(this, str) and '대상' in this


def target_actions(r):
    """핸들러 결과 r → ([(틱, 코드, 붙듦, 단계, 반복, 주소)], 가설인가, 기준 설명)."""
    part = r['main'][1]
    evs, seen = [], set()
    for ev in part['events']:                       # 갈래 단계가 같은 호출 자리를 거듭 내면 한 번만
        key = (ev['va'], str(ev.get('path')), str(ev.get('base', ev.get('m'))))
        if ev['k'] in ('act', 'mot') and key in seen:
            continue
        seen.add(key)
        evs.append(ev)
    acts = []
    for ev in evs:
        if ev['k'] == 'act' and is_target(ev['this']) and isinstance(ev['base'], int):
            acts.append((ev, ev['base'] // 3, isinstance(ev['rep'], int) and ev['rep'] >= 1000, ev['rep']))
        elif ev['k'] == 'mot' and is_target(ev['this']) and isinstance(ev['m'], int):
            acts.append((ev, 1000 + ev['m'], False, None))
    if not acts:
        return [], False, ''
    order = part['order']
    pos = {st: i for i, st in enumerate(order)}
    dur = {}                                        # 단계 → 틱으로 정해진 길이(없으면 모름)
    for ev in evs:
        if ev['k'] == 'stage' and isinstance(ev['tick'], int) and ev['tick'] >= 2:
            dur.setdefault(ev['stage'], ev['tick'])
    hit = next((ev for ev in evs if ev['k'] == 'hit'), None)
    guess = hit is None
    ref = hit if hit is not None else acts[-1][0]
    rs, rt = ref['stage'], ref['tick'] or 0
    out = []
    for ev, code, hold, rep in acts:
        s, t = ev['stage'], ev['tick'] or 0
        if s == rs:
            tick, sure = t - rt, True
        else:
            a, b = sorted((pos.get(s, 0), pos.get(rs, 0)))
            between = order[a:b]
            if all(st in dur for st in between):
                span = sum(dur[st] for st in between)
                tick = -(span - t + rt) if pos.get(s, 0) < pos.get(rs, 0) else span - rt + t
                sure = True
            else:
                tick, sure = 0, False
        out.append((tick, code, hold, s, rep, ev['va'], sure))
    return out, guess, ('1001 @단계 %d' % rs) if hit is not None else ('1001 없음 — 단계 %d 기준' % rs)


def main():
    game = sys.argv[1]
    out_path = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, '..', '..', 'duel-dx', 'Ability', 'AbilityTargetActions.g.cs')
    res, e = emu.analyze_all(game)
    abis = ws.load_abis(e.g, e.works)
    text = {}
    try:
        import extract_character as ec
        for t in ec.parse_txr(e.g.read('TXR', 'Txr.dat')):
            text.setdefault(t['txr_id'], t['text'])
    except Exception:
        pass

    lines = [
        '// 이 파일은 tools/re/work_target_actions.py 가 만든다 — 손으로 고치지 말 것.',
        '// 원본 DLL 의 work 핸들러가 <대상 유닛에게> 거는 동작이다(ba-20 X, 옵시디안 분석/원본차이/ba20-fxtable.md).',
        '',
        'namespace DuelDx;',
        '',
        'internal sealed unsafe partial class BattleSceneWindow',
        '{',
        '    /// <summary>',
        '    /// work 번호 → 대상에게 거는 동작 (틱, 동작, 붙듦) — 시전자 사슬(<see cref="WorkScripts"/> 의 Actions)과 따로다.',
        '    /// </summary>',
        '    /// <remarks>',
        '    /// Tick 은 치는 순간(원본이 메시지 1001 을 보내는 단계) 기준 상대 틱(앞이면 음수).',
        '    /// 줄 끝에 「틱 가설」이 붙은 것은 단계 사이 기다림을 정적으로 못 구해 0 으로 둔 것 — 차례(앞의 것이 먼저)만 믿을 것.',
        '    /// Action 은 동작 번호(SetAction 기준 ÷ 3, 모션 = 동작 × 3 + 방향), 원시 모션이면 1000 + 모션.',
        '    /// Hold 는 반복 1000 이상 — 다음 동작이 올 때까지 붙든다(맞음 자세 2 를 붙들었다가 서기 0 으로 되돌리는 꼴).',
        '    /// </remarks>',
        '    private static readonly Dictionary<int, (int Tick, int Action, bool Hold)[]> WorkTargetActions = new()',
        '    {',
    ]
    n = 0
    stats = collections.Counter()
    for wid in sorted(res):
        acts, guess, ref = target_actions(res[wid])
        if not acts:
            continue
        n += 1
        w = e.works[wid]
        ab = abis.get(w[0x4])
        name = text.get(ab['name'], '') if ab else ''
        unsure = guess or any(not a[6] for a in acts)
        stats['가설' if unsure else '확정'] += 1
        body = ', '.join('(%d, %d, %s)' % (t, c, 'true' if h else 'false') for t, c, h, _, _, _, _ in acts)
        note = ' · '.join('단계 %d%s @0x%x' % (s, '' if rep in (None, 1) or rep >= 1000 else ' 반복 %d' % rep, va)
                          for _, _, _, s, rep, va, _ in acts)
        lines.append('        [%d] = [%s],   // %s핸들러 0x%x · %s · %s%s' % (
            wid, body, (name + ' Lv%d · ' % w[0x6]) if name else '', res[wid]['handler'], note, ref,
            ' · 틱 가설' if unsure else ''))
    lines += ['    };', '}', '']
    with open(out_path, 'w', encoding='utf-8-sig', newline='\n') as f:
        f.write('\n'.join(lines))
    print('work %d개(%s)를 %s 에 적었다' % (n, dict(stats), os.path.relpath(out_path, os.path.join(HERE, '..', '..'))))


if __name__ == '__main__':
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    main()

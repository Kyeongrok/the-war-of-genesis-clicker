"""work(기술)마다 <b>동작 차례와 이펙트</b>를 뽑아 duel-dx 가 읽는 C# 표로 적는다.

    python tools/re/work_fx_table.py "<게임 폴더>" [출력.cs]

`work_script.py` 가 이미 DLL 핸들러를 기호로 풀어 준다 — 여기서는 그 기록을 골라
`('act', 기준동작)` 은 동작 번호(기준÷3)로, `('eff', Obs, 모션, x, y)` 는 이펙트로 옮긴다.
이펙트 자리는 x 식으로 가른다: `나.대상.*` 이면 대상 자리, `나.*` 면 시전자 자리,
그 밖(`esp+0x20` 처럼 코드가 셈해 넣는 자리)은 <b>안 옮긴다</b> — 어디인지 모르니 넣으면 엉뚱한 데 뜬다.

손으로 맞춘 표(`AbilityMotions`)가 있는 work 은 그것이 이긴다 — 이 표는 <b>빈자리를 채우는 용도</b>다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import work_script as ws                                    # noqa: E402

GAME = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, '..', '..', 'duel-dx', 'AbilityScripts.g.cs')

game = ws.Game(GAME)
dll = ws.Analyzer(os.path.join(GAME, 'G3PartII.dll'))
works = ws.load_works(game)


def where_of(x):
    """이펙트 자리 — 'target' · 'self' · None(모르는 자리)."""
    if not isinstance(x, str):
        return None
    if '대상' in x:
        return 'target'
    if x.startswith('나.'):
        return 'self'
    return None


rows = {}
for wid in sorted(works):
    w = works[wid]
    try:
        seq = ([ws.PRELUDE[w[0x3f]][0]] if w and w[0x3f] in ws.PRELUDE else []) + [dll.handler(wid)]
    except Exception:
        continue
    acts, effs = [], []
    for h in seq:
        if not h:
            continue
        try:
            recs = list(dll.script(h))
        except Exception:
            continue
        for rec in recs:
            kind = rec[1]
            if kind == 'act' and isinstance(rec[2], int):
                acts.append(rec[2] // 3)
            elif kind == 'eff' and isinstance(rec[2], int) and isinstance(rec[3], int):
                place = where_of(rec[4])
                if place is None:
                    continue
                key = (rec[2], rec[3], place)
                if key not in effs:
                    effs.append(key)
    if not acts and not effs:
        continue
    rows[wid] = (acts[:8], effs[:6])

lines = [
    '// 이 파일은 tools/re/work_fx_table.py 가 만든다 — 손으로 고치지 말 것.',
    '// 원본 DLL 의 work 핸들러에서 뽑은 <동작 차례, 이펙트> 표다(분석-모션 ba-10).',
    '',
    'namespace DuelDx;',
    '',
    'internal sealed unsafe partial class BattleSceneWindow',
    '{',
    '    /// <summary>',
    '    /// work 번호 → (동작 차례, 이펙트) — <b>도구가 뽑은</b> 표. 손으로 맞춘 <see cref="AbilityMotions"/> 가 우선한다.',
    '    /// </summary>',
    '    /// <remarks>',
    '    /// 자리를 코드가 셈해 넣는 이펙트(메테오의 떨어지는 자리 같은 것)는 <b>안 들어 있다</b> — 어디인지 모르기 때문이다.',
    '    /// 띄우는 높이(Lift)도 모르니 0 이다. 그 둘이 중요한 기술은 손 표에 따로 적는다.',
    '    /// </remarks>',
    '    private static readonly Dictionary<int, (int[] Actions, AbilityEffect[] Effects)> WorkScripts = new()',
    '    {',
]
for wid, (acts, effs) in rows.items():
    a = ', '.join(str(x) for x in acts)
    e = ', '.join('new(%d, %d, %s, 0)' % (o, m, 'true' if p == 'target' else 'false') for o, m, p in effs)
    lines.append('        [%d] = ([%s], [%s]),' % (wid, a, e))
lines += ['    };', '}', '']
with open(OUT, 'w', encoding='utf-8-sig', newline='\n') as f:
    f.write('\n'.join(lines))
print('work %d개를 %s 에 적었다' % (len(rows), os.path.relpath(OUT, os.path.join(HERE, '..', '..'))))

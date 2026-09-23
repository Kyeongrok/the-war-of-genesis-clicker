"""work(기술)마다 <b>동작 차례와 이펙트</b>를 뽑아 duel-dx 가 읽는 C# 표로 적는다.

    python tools/re/work_fx_table.py "<게임 폴더>" [출력.cs]

`work_script.py` 가 이미 DLL 핸들러를 기호로 풀어 준다 — 여기서는 그 기록을 골라
`('act', 기준동작)` 은 동작 번호(기준÷3)로, `('eff', Obs, 모션, x, y)` 는 이펙트로 옮긴다.
이펙트 자리는 x 식으로 가른다: `나.대상.*` 이면 대상 자리, `나.*` 면 시전자 자리,
그 밖(`esp+0x20` 처럼 코드가 셈해 넣는 자리 — 메테오의 떨어지는 자리 따위)은 <b>대상 자리</b>로 둔다.
예전에는 이것을 버렸는데, 남은 것이 소리만 든 Obs(1338·311·1324 …)뿐인 기술이 87개나 되어 그림이 한 장도 안 나왔다.
자리가 조금 틀려도 안 보이는 것보다 낫다.

소리 껍데기(그림 컷 없는 Obs)도 <b>남긴다</b> — duel-dx 가 그 소리 키로 소리를 낸다. 그림은 자식 키를 따라가고, 파생 클래스 생성자로
만드는 이펙트·뿌리개(`work_fx_extra.ExtraAnalyzer`, 분석-스킬 fx-189)도 더한다.

손으로 맞춘 표(`AbilityMotions`)가 있는 work 은 그것이 이긴다 — 이 표는 <b>빈자리를 채우는 용도</b>다.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import work_script as ws                                    # noqa: E402
from obs_ui_dump import load_motions                        # noqa: E402
import work_fx_extra as wx                                  # noqa: E402

GAME = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, '..', '..', 'duel-dx', 'AbilityScripts.g.cs')

game = ws.Game(GAME)
dll = ws.Analyzer(os.path.join(GAME, 'G3PartII.dll'))
works = ws.load_works(game)


def where_of(x):
    """이펙트 자리 — 'target' · 'self'. 코드가 셈하는 자리(모르는 자리)는 대상 자리로 본다."""
    if isinstance(x, str) and x.startswith('나.') and '대상' not in x:
        return 'self'
    return 'target'


_pictures = {}


def pictures(obs, motion, depth=0):
    """그 Obs 모션이 실제로 그리는 (Obs, 모션)들 — 제 그림 컷(종류 0 키)이 있으면 자신, 자식 키(종류 2)가 부르는 Obs 는 따라간다.
    소리 키만 든 껍데기(1338 따위)는 빈 목록이다."""
    if (obs, motion) in _pictures:
        return _pictures[(obs, motion)]
    out = []
    try:
        d = game.read('Obs', '%04d.obs' % obs)
        m = load_motions(d).get(motion) if d else None
    except Exception:
        m = None
    if m:
        if any(k['kind'] == 0 for k in m['keys']):
            out.append((obs, motion))
        if depth < 3:
            for k in m['keys']:
                if k['kind'] == 2 and k['p'] and k['p'][0] > 0:
                    for child in pictures(int(k['p'][0]), int(k['p'][1]), depth + 1):
                        if child not in out:
                            out.append(child)
    _pictures[(obs, motion)] = out
    return out


extra = wx.ExtraAnalyzer(os.path.join(GAME, 'G3PartII.dll'))


def add(effs, key):
    if key not in effs:
        effs.append(key)


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
            recs = []
        for rec in recs:
            kind = rec[1]
            if kind == 'act' and isinstance(rec[2], int):
                acts.append(rec[2] // 3)
            elif kind == 'eff' and isinstance(rec[2], int) and isinstance(rec[3], int):
                place = where_of(rec[4])
                add(effs, (rec[2], rec[3], place))           # 소리 껍데기도 남긴다 — 소리 키로 소리를 낸다
                for o, mo in pictures(rec[2], rec[3]):
                    add(effs, (o, mo, place))
        # 파생 클래스 생성자로 만드는 이펙트·뿌리개(fx-189) — 직접 호출만 보는 script() 가 놓친다.
        try:
            items = extra.effects(h)
        except Exception:
            items = []
        for r in items:
            obs, mo = r.get('obs'), r.get('motion')
            if not isinstance(obs, int) or r['kind'] not in ('obs', 'emit'):
                continue
            place = 'self' if wx.where_of(r) == 'self' else 'target'
            if isinstance(mo, int):
                if r['kind'] == 'obs':
                    add(effs, (obs, mo, place))
                for o, m2 in pictures(obs, mo):
                    add(effs, (o, m2, place))
            else:                                            # 뿌리개 모션을 코드가 고른다 — 그림 있는 첫 모션 둘
                try:
                    mm = sorted(wx.load_motions(game.read('Obs', '%04d.obs' % obs)))
                except Exception:
                    mm = []
                n = 0
                for m2 in mm:
                    for o, m3 in pictures(obs, m2):
                        if n < 2:
                            add(effs, (o, m3, place))
                            n += 1
    if not acts and not effs:
        continue
    rows[wid] = (acts[:8], effs[:16])

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
    '    /// 자리를 코드가 셈해 넣는 이펙트(메테오의 떨어지는 자리 같은 것)는 <b>대상 자리</b>로 들어 있다(정확한 자리는 모른다).',
    '    /// 그림 컷이 없는 Obs(소리 껍데기)는 빠져 있다.',
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

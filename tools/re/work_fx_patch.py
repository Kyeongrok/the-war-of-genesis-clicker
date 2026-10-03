"""`duel-dx/AbilityScripts.g.cs` 의 이펙트 표(WorkScripts)를 원본 핸들러의 단계 순 실행 결과로 <b>바로잡는다</b> (ba-20 X).

    python tools/re/work_fx_patch.py "<게임 폴더>" [--dry] [--report 바뀐것.tsv] [--cs duel-dx/AbilityScripts.g.cs]

차례: ① `work_fx_table.py` 로 표를 뽑는다(동작 차례·영상·몸 복제 포함) → ② 이 도구로 이펙트 칸을 고친다.
`work_fx_table.py` 를 다시 돌렸으면 이 도구도 다시 돌려야 한다. 두 번 돌려도 결과는 같다(이미 맞으면 안 건드린다).

고치는 것 — `work_fx_emu.analyze_all()`(단계·틱·방향을 정해 놓고 핸들러를 따라가는 기호 실행)에서 <b>확정</b>으로 읽힌 것만:
  1. 지연(Delay, 0x100c24d0)·수명(Life, 0x100c2530) — 이펙트 객체를 따라가 붙인 값. 표 값이 원본 값 집합에 없고 5틱 넘게
     다르면 원본 값으로(원본 값이 여럿이면 가장 작은 것). 높이(Lift = 월드 z 치우침 × 0.6)도 6px 넘게 다르면 고친다.
  2. 자리(OnTarget) — asm 으로 확인한 것만(PLACE_FIX): 혼 379:1(붙일 곳 = 시전자, 0x1007f70f), 워핑 209:0(시전자 머리 위, 0x1009d85a).
  3. 원본이 띄우는데 표에 없는 (Obs, 모션) 을 줄 끝에 더한다 — 네 방향 모두에서 뜨는 것만(방향 가지는 `AbilityEffect` 에 방향 칸이
     없어 넣지 않는다). 자리를 코드가 셈하는 것은 `work_fx_table.py` 와 같이 대상 자리로 둔다. 예전 표의 16개 자르기로 빠진 것도 여기서 든다.
  4. 다크 스크림 1492 의 쓰레기 줄(300:53·300:52·0:52·9:52 — `push 0x12c; call new` 따위를 생성자로 잘못 읽은 것)을 뺀다.
  5. 아이템 work 1611·1618·1619 — 핸들러가 메테오·썬더 스톰·블리자드로 바로 뛴다(jmp 0x100866f0·0x10087ff0·0x1008b630).
     동작 `[]` + work 469·436·437 의 이펙트를 준다.
  6. 동작도 이펙트도 없는 공통 핸들러 work(EMPTY_ROWS)에 빈 줄 `([], [])` — 줄이 없으면 duel-dx 가 기본 사슬 [5,8,24] 로 칼을 휘두른다.
     인물의 기본공격 work 으로 쓰이는 번호(Chr +19 — 0·1·6·25·387·392·1479·1584)는 `BasicWorkActions`/기본 사슬에 기대므로 넣지 않는다.
안 건드리는 것: 동작 차례(Actions), 표에만 있는 이펙트(「덤」 — 실행기가 못 닿은 가지일 수 있다), 방향 가지, 영상·몸 복제 표.
근거: 옵시디안 분석/원본차이/ba20-fxtable.md
"""
import argparse
import collections
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import work_script as ws                                    # noqa: E402
import work_fx_emu as emu                                   # noqa: E402
from obs_ui_dump import load_motions                        # noqa: E402

FLYERS = {0x100c3340, 0x100c5940}
# (핸들러, Obs, 모션) → OnTarget — asm 으로 확인한 자리만
PLACE_FIX = {
    (0x1007f220, 379, 1): False,     # 혼: CEffect(379, 1, 0, 0, 0, 붙일 곳 = 나, …) 0x1007f70f
    (0x1009d100, 209, 0): False,     # 워핑: CEffect(209, 0, 나.x, 나.y, 나.z+150, …) 0x1009d85a
}
GARBAGE = {1492: {(300, 53), (300, 52), (0, 52), (9, 52)}}
ITEM_COPY = {1611: 469, 1618: 436, 1619: 437}
EMPTY_ROWS = [4, 5, 13, 386, 1468, 1472, 1473, 1474, 1475, 1476, 1477, 1525, 1526, 1527]   # 0 은 인물 넷의 기본공격 work 이라 뺀다
DEFAULT = (0, 1, False, 0)


def parse_effs(s):
    out = []
    for m in re.finditer(r'new\(([^)]*)\)', s):
        a = [x.strip() for x in m.group(1).split(',')]
        v = [int(a[0]), int(a[1]), a[2] == 'true', int(a[3]), 0, 1, False, 0]
        if len(a) > 4:
            v[4] = int(a[4])
        if len(a) > 5:
            v[5] = int(a[5])
        if len(a) > 6:
            v[6] = a[6] == 'true'
        if len(a) > 7:
            v[7] = int(a[7])
        out.append(v)
    return out


def fmt_eff(e):
    o, m, p, lf, d, c, f, li = e
    return 'new(%d, %d, %s, %d%s)' % (o, m, 'true' if p else 'false', lf,
                                      '' if (d, c, f, li) == DEFAULT else ', %d, %d, %s, %d' % (d, c, 'true' if f else 'false', li))


def where_of(rec):
    p, x = rec['parent'], rec['x']
    if isinstance(p, str) and '대상' in p:
        return 'target'
    if p == '나':
        return 'self'
    if isinstance(x, str) and '대상' in x:
        return 'target'
    if isinstance(x, str) and x.startswith('나.'):
        return 'self'
    return 'cell'


def zlift(z):
    if isinstance(z, int):
        v = z - (1 << 32) if z >= 1 << 31 else z
        return round(v * 0.6) if abs(v) < 2000 else 0
    if isinstance(z, str):
        m = re.search(r'\.z([+-]\d+)$', z)
        if m:
            return round(int(m.group(1)) * 0.6)
        if z.endswith('.z'):
            return 0
    return None


def rng(v, hi):
    return v if isinstance(v, int) and 0 < v < hi else 0


def original_effects(r):
    """원본 이펙트 — {(Obs, 모션): {place, delay, life, lift, dirs, count, fly, va, order}} (준비 핸들러 것 포함)."""
    out = collections.OrderedDict()
    n = 0
    for d in (1, 0, 2, 3):
        for part in ('pre', 'main'):
            if part not in r:
                continue
            for ev in r[part][d]['events']:
                if ev['k'] != 'fx' or ev['kind'] not in ('obs', 'emit'):
                    continue
                obs, mo = ev['obs'], ev['motion']
                if not isinstance(obs, int) or not isinstance(mo, int):
                    continue
                obs &= 0xffff
                mo &= 0xffff
                n += 1
                x = out.setdefault((obs, mo), dict(place=set(), delay=set(), life=set(), lift=set(), dirs=set(), count=set(),
                                                   fly=False, va=ev['va'], order=n, emit=False))
                x['place'].add(where_of(ev))
                x['delay'].add(None if ev['delay'] == '?' else rng(ev['delay'], 600))     # None = 값을 못 푼 호출
                x['life'].add(None if ev['life'] == '?' else rng(ev['life'], 3000))
                x['lift'].add(zlift(ev['z']))
                x['dirs'].add(d)
                if ev['kind'] == 'emit':
                    x['emit'] = True
                    if isinstance(ev.get('count'), int) and 1 < ev['count'] <= 16:
                        x['count'].add(ev['count'])
                if ev['ctor'] in FLYERS:
                    x['fly'] = True
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('--cs', default=os.path.join(HERE, '..', '..', 'duel-dx', 'AbilityScripts.g.cs'))
    ap.add_argument('--dry', action='store_true', help='파일을 안 고치고 바뀔 것만 센다')
    ap.add_argument('--report', help='바뀐 것을 TSV 로 (work, 종류, Obs, 모션, 전, 후, 근거 주소)')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

    res, e = emu.analyze_all(a.game)
    game = e.g
    motions = {}

    def has_motion(obs, mo):
        if obs not in motions:
            try:
                d = game.read('Obs', '%04d.obs' % obs)
                motions[obs] = set(load_motions(d)) if d else set()
            except Exception:
                motions[obs] = set()
        return mo in motions[obs]

    text = open(a.cs, encoding='utf-8-sig').read()
    lines = text.split('\n')
    start = next(i for i, ln in enumerate(lines) if 'WorkScripts = new()' in ln)
    end = next(i for i in range(start, len(lines)) if lines[i].strip() == '};')
    rowre = re.compile(r'^(\s*)\[(\d+)\] = \(\[([^\]]*)\], \[(.*)\]\),\s*$')
    rows, order = {}, []
    for i in range(start + 2, end):
        m = rowre.match(lines[i])
        if not m:
            raise SystemExit('모르는 줄 %d: %s' % (i + 1, lines[i][:80]))
        rows[int(m.group(2))] = [m.group(3), parse_effs(m.group(4))]
        order.append(int(m.group(2)))

    log = []
    for w in order:
        r = res.get(w)
        if r is None:
            continue
        effs = rows[w][1]
        if w in GARBAGE:
            for x in [x for x in effs if (x[0], x[1]) in GARBAGE[w]]:
                effs.remove(x)
                log.append((w, '쓰레기 뺌', x[0], x[1], fmt_eff(x), '', ''))
        orig = original_effects(r)
        have = {(x[0], x[1]) for x in effs}
        for x in effs:
            o = orig.get((x[0], x[1]))
            if o is None:
                continue                     # 표에만 있는 것(덤)은 그대로 둔다
            before = fmt_eff(x)
            # 값을 못 푼 호출(None)이 섞여 있으면 그 칸은 안 건드린다
            if None not in o['delay'] and x[4] not in o['delay'] and min(abs(x[4] - v) for v in o['delay']) >= 5:
                x[4] = min(o['delay'])
            if None not in o['life'] and x[7] not in o['life'] and min(abs(x[7] - v) for v in o['life']) >= 5:
                x[7] = min(o['life'])
            lf = {v for v in o['lift'] if v is not None}
            if lf and x[3] not in lf and min(abs(x[3] - v) for v in lf) >= 6:
                x[3] = min(lf, key=abs)
            pf = PLACE_FIX.get((r['handler'], x[0], x[1]))
            if pf is not None and not x[6]:
                x[2] = pf
            if fmt_eff(x) != before:
                log.append((w, '값 고침', x[0], x[1], before, fmt_eff(x), hex(o['va'])))
        for (obs, mo), o in sorted(orig.items(), key=lambda kv: kv[1]['order']):
            if (obs, mo) in have or len(o['dirs']) < 4 or not has_motion(obs, mo):
                continue
            place = o['place'] - {'cell'}
            lf = {v for v in o['lift'] if v is not None}
            x = [obs, mo, 'self' not in place if place else True, min(lf, key=abs) if lf else 0,
                 min(v or 0 for v in o['delay']), min(o['count']) if o['count'] else 1, o['fly'] and not o['emit'],
                 min(v or 0 for v in o['life'])]
            effs.append(x)
            log.append((w, '더함', obs, mo, '', fmt_eff(x), hex(o['va'])))

    for w, srcw in ITEM_COPY.items():
        if w not in rows and srcw in rows:
            rows[w] = ['', [list(x) for x in rows[srcw][1] if x[0] != 1338]]     # 준비 +0x3f = 0 — 시전 소리 1338 은 없다
            log.append((w, '새 줄', 0, 0, '', '([], work %d 의 이펙트 %d개)' % (srcw, len(rows[w][1])), hex(res[w]['handler']) if w in res else ''))
    for w in EMPTY_ROWS:
        if w not in rows:
            rows[w] = ['', []]
            log.append((w, '새 줄', 0, 0, '', '([], [])', hex(res[w]['handler']) if w in res else ''))

    body = ['        [%d] = ([%s], [%s]),' % (w, rows[w][0], ', '.join(fmt_eff(x) for x in rows[w][1])) for w in sorted(rows)]
    new = '\n'.join(lines[:start + 2] + body + lines[end:])
    changed = sorted({x[0] for x in log})
    kinds = collections.Counter(x[1] for x in log)
    print('바뀐 work %d개, 바뀐 항목 %s' % (len(changed), dict(kinds)))
    if a.report:
        with open(a.report, 'w', encoding='utf-8') as f:
            f.write('work\t종류\tObs\t모션\t전\t후\t근거 주소\n')
            for x in log:
                f.write('\t'.join(str(v) for v in x) + '\n')
    if not a.dry and new != text:
        with open(a.cs, 'w', encoding='utf-8-sig', newline='\n') as f:
            f.write(new)
        print('%s 를 고쳤다' % os.path.relpath(a.cs, os.path.join(HERE, '..', '..')))


if __name__ == '__main__':
    main()

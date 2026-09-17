"""
창세기전3 파트2 — 게임에 있는 어빌리티(스킬) 전부를 표로 뽑는다. Abi(어빌리티) + att(work, 레벨별 기술) + Job.dat(배우는 직업)
+ Dep.dat(계열) + For.dat(군단) + Chr(처음부터 가진 캐릭터).

사용법:
    python skill_list.py <게임 폴더>                       # 어빌리티 요약 표(마크다운)
    python skill_list.py <게임 폴더> --levels              # 레벨별 전체 표(마크다운)도 찍기
    python skill_list.py <게임 폴더> --csv <출력.csv>      # 레벨별 전체 표를 CSV 로
    python skill_list.py <게임 폴더> --jobs                # 직업(Job.dat) 표: 계열·형·체질별 이름·성장률·배우는 어빌리티

게임 폴더는 읽기만 한다(skill_motion.Game 이 pak 에서 메모리로 꺼낸다).

G3PartII.dll 근거 (옵시디안 분석-스킬.md):
  Abi 메모리 72바이트(0x101b686c), 로더 0x1004b860:
    +0x04 이름 TXR, +0x06 최대 레벨, +0x08 선행 어빌리티1, +0x0a 그 레벨, +0x0c 선행2, +0x0e 그 레벨,
    +0x0f 분류 (0 전투 목록에 안 나옴, 1 전투, 2 군단기(For.dat 로 얻음, 레벨 늘 1), 3 패시브(장착), 4 코드상 1 과 같음·데이터 없음),
    +0x10 군단 번호(분류 2), +0x12 ?, +0x14 ?, +0x16 아이콘 줄(0 아군 쪽, 1 적 쪽, 2 피아 구분 없음, 0xffff 아이콘 없음),
    +0x18 대상 수 그림(0 한 명, 1 여럿), +0x1a 글자 아이콘(0 攻, 1 回, 2 異, 3·4 기타), +0x1c 설명 TXR,
    +0x1e + (레벨-1)*2 = 그 레벨의 work 번호 (att 로더가 채움)
  CChr +0x7a[어빌리티] = 레벨(0xff 못 배움).  0x10032a30 레벨 읽기(분류 2 는 1),  0x10032a80 배우기/올리기
    (처음 배우면 선행1(+0x08)을 0xff 로 지움),  0x100324e0 배울 수 있는 목록 = Job[+0x16].+0x22 11칸 중 선행 조건을 채운 것,
  0x10032760 전투 어빌리티 목록 = 배운 것 중 분류 1·4 + 군단(For.dat) 조건을 채운 것,  0x10032950 장착 후보 = 분류 3,
  0x10032af0 패시브 합 = 장착 3칸(+0x142) 중 Dep[계열].+6 칸까지 work 효과(+0x20+i 종류, +0x24+2i 값) 합.
  0x100322e0 어빌리티→work: 25 접근 공격 → 1, 26 원거리 공격 → 6, 125 Hunter → 1468, 나머지는 abi 표.
  work(att) 칸 뜻은 skill_motion.py·soul_costs.py·분석-전투.md 참고.
"""
import argparse
import collections
import csv
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import skill_motion as sm  # noqa: E402
import exp_tables as et  # noqa: E402

CLASS = {0: '비전투', 1: '전투', 2: '군단기', 3: '패시브', 4: '분류4'}
ICON_ROW = {0: '아군', 1: '적', 2: '피아무관', 0xffff: '-'}
ICON_CHAR = {0: '攻', 1: '回', 2: '異', 3: '글자3(군단)', 4: '글자4(특기)', 0xffff: '-'}
ICON_MANY = {0: '한명', 1: '여럿', 0xffff: '-'}
KIND = {0: '피해', 1: '회복', 2: '보조', 3: '강화', 4: '오브젝트', 5: '회복', 7: '7'}
EFFECT = {0x1e: 'DEX', 0x1f: 'PSY', 0x20: 'DEP', 0x21: 'TP', 0x25: 'SOUL최대', 0x30: 'LP'}
TYPE_NAME = ['일반형', '공격형', '방어형', '고속형', '보조형']
CONST = ['무속성', '에텔', '멘탈', '아스트럴', '코절', '메텔']


def s16(v):
    return v - 0x10000 if v >= 0x8000 else v


def load_text(g):
    from extract_character import parse_txr
    t = {}
    for r in parse_txr(g.read('TXR', 'Txr.dat')):
        t.setdefault(r['txr_id'], r['text'])
    return t


def load_abi_records(g):
    """Abi 파일 레코드를 칸 이름으로."""
    out = {}
    size = struct.calcsize(sm.ABI_FMT)
    for fn in sm.ABI_FILES:
        d = g.read('Abi', fn)
        _, n, _ = struct.unpack_from('<3H', d)
        for i in range(n):
            v = struct.unpack_from(sm.ABI_FMT, d, 6 + i * size)
            out[v[0]] = dict(id=v[0], file=fn, name=v[1], maxlv=v[2], pre1=v[3], pre1lv=v[4], pre2=v[5], pre2lv=v[6],
                             cls=v[7], f10=v[8], f12=v[9], f14=v[10], row=v[11], many=v[12], char=v[13], desc=v[14])
    return out


def load_dep(g):
    d = g.read('Dat', 'Dep.dat')
    _, n, _ = struct.unpack_from('<3H', d)
    o, out = 6, {}
    for _ in range(n):
        idx, name = struct.unpack_from('<2H', d, o)
        slots = d[o + 4]
        p = o + 5
        jobs = []
        for _ in range(7):
            j, a, b = struct.unpack_from('<HBB', d, p)
            p += 4
            jobs.append(j)
        out[idx] = dict(name=name, slots=slots, jobs=jobs)
        o = p
    return out


def load_for(g):
    d = g.read('Dat', 'For.dat')
    _, n, _ = struct.unpack_from('<3H', d)
    o, out = 6, {}
    for _ in range(n):
        idx, name = struct.unpack_from('<2H', d, o)
        chrs = struct.unpack_from('<6H', d, o + 4)
        abis = [struct.unpack_from('<3H', d, o + 27 + 6 * k) for k in range(5)]
        out[idx] = dict(name=name, chrs=chrs, abis=[a for a in abis if a[0]])
        o += 59
    return out


def job_info(jobs, dep):
    """직업 번호 → (계열 Dep 번호, 단계 1~3, 형 0~4)."""
    info = {}
    for di, dp in dep.items():
        if not (1 <= di <= 15):
            continue
        stage = (di - 1) % 3 + 1
        for k, j in enumerate(dp['jobs']):
            if j:
                info[j] = (di, stage, k)
    return info


def job_label(j, jobs, text):
    r = jobs.get(j)
    if not r:
        return str(j)
    nm = next((text.get(c, '') for c in r['c'] if c), '')
    return '%d %s' % (j, nm) if nm else str(j)


def fmt_range(vals):
    vals = [v for v in vals if v is not None]
    if not vals:
        return ''
    lo, hi = vals[0], vals[-1]
    return str(lo) if lo == hi else '%s→%s' % (lo, hi)


def work_effects(w):
    es = []
    for i in range(3):
        t = w[0x20 + i]
        if t:
            es.append('%s:%d' % (EFFECT.get(t, str(t)), s16(w[0x24 + 2 * i])))
    return ' '.join(es)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('--levels', action='store_true')
    ap.add_argument('--csv')
    ap.add_argument('--jobs', action='store_true')
    a = ap.parse_args()
    sys.stdout.reconfigure(encoding='utf-8')

    g = sm.Game(a.game)
    text = load_text(g)
    works = sm.load_works(g)
    abis = load_abi_records(g)
    levels = sm.load_abis(g, works)
    jobs_rows, _ = et.parse_job(g.read('Dat', 'Job.dat'))
    jobs = {j['idx']: j for j in jobs_rows}
    dep = load_dep(g)
    forces = load_for(g)
    jinfo = job_info(jobs, dep)

    learn_by = collections.defaultdict(list)          # Job.dat +0x22 목록
    for j in jobs_rows:
        for aid in j['b']:
            if aid:
                learn_by[aid].append(j['idx'])
    force_by = collections.defaultdict(list)
    for fi, f in forces.items():
        for aid, req, chr_id in f['abis']:
            force_by[aid].append(fi)
    chr_has = collections.Counter()
    for n in range(0, 1000):
        b = g.read('Chr', '%04d.chr' % n)
        if not b or len(b) != 90:
            continue
        for k in range(8):
            aid, lv = struct.unpack_from('<2H', b, 56 + 4 * k)
            if aid:
                chr_has[aid] += 1

    if a.jobs:
        print('| 직업 | 계열(Dep) | 단계 | 형 | 필요 레벨(+4) | 필요 어빌리티(+6 Lv+8) | 이름(에텔/멘탈/아스트럴/코절/메텔) | 성장 LP/TP/PSY/DEP/DEX % | +0a..+14, +1a | 배우는 어빌리티(+22) |')
        print('|---|---|---|---|---|---|---|---|---|---|')
        for j in jobs_rows:
            f = j['f']
            di, st, k = jinfo.get(j['idx'], (0, 0, -1))
            names = '/'.join(text.get(c, '') if c else '-' for c in j['c'][1:])
            req = '%s Lv%d' % (text.get(abis[j['f6']]['name'], j['f6']) if j['f6'] in abis else j['f6'], j['f8']) if j['f6'] else ''
            ab = ', '.join(text.get(abis[x]['name'], str(x)) if x in abis else str(x) for x in j['b'] if x)
            print('| %d | %s | %s | %s | %d | %s | %s | %d/%d/%d/%d/%d | %s | %s |' % (
                j['idx'], '%d %s' % (di, text.get(dep[di]['name'], '')) if di else '', st or '', TYPE_NAME[k] if st in (1, 2) and k >= 0 else '',
                j['f4'], req, names, f[0x16], f[0x18], f[0x1c], f[0x1e], f[0x20],
                ' '.join(str(f[o]) for o in (0x0a, 0x0c, 0x0e, 0x10, 0x12, 0x14, 0x1a)), ab))
        return

    rows = []
    print('| 번호 | 이름 | 파일 | 분류 | 아이콘 | 최대Lv | 선행 조건 | 배우는 직업(Job) | 종류 | 위력 +2a | 명중 +2c | 치명 +2d | 사거리 +7/+8 최소~최대 | 대상 +13 | 범위 +14/+1a/+1c | TP 바탕 +32 | 체질값 +2e | SOUL +34(소모) | EXP Lv1 / 합 | 효과 | 처음 가진 Chr 수 | 설명 |')
    print('|' + '---|' * 22)
    for aid in sorted(abis):
        ab = abis[aid]
        nm = text.get(ab['name'], '')
        lv_w = sorted(levels.get(aid, {}).get('levels', {}).items())
        if aid == 25:
            lv_w = [(1, 1)]
        elif aid == 26:
            lv_w = [(1, 6)]
        elif aid == 125:
            lv_w = [(1, 1468)]
        ws = [(lv, wid, works[wid]) for lv, wid in lv_w if wid in works]
        pre = []
        for p, pl in ((ab['pre1'], ab['pre1lv']), (ab['pre2'], ab['pre2lv'])):
            if p:
                pre.append('%s Lv%d' % (text.get(abis[p]['name'], str(p)) if p in abis else p, pl))
        if ab['cls'] == 2 and force_by.get(aid):
            pre.append('군단 ' + ','.join('%d %s' % (fi, text.get(forces[fi]['name'], '')) for fi in force_by[aid]))
        jl = learn_by.get(aid, [])
        jtxt = ', '.join(job_label(j, jobs, text) for j in jl[:6]) + (' 외 %d' % (len(jl) - 6) if len(jl) > 6 else '')
        icon = '' if ab['row'] == 0xffff else '%s·%s·%s' % (ICON_ROW.get(ab['row'], ab['row']), ICON_MANY.get(ab['many'], ab['many']),
                                                          ICON_CHAR.get(ab['char'], ab['char']))
        if ws:
            w1 = ws[0][2]
            col = lambda o, sg=True: fmt_range([s16(w[o]) if sg else w[o] for _, _, w in ws])  # noqa: E731
            kinds = sorted({w[0x1f] for _, _, w in ws})
            rng = '%d/%d %d~%s' % (w1[0x07], w1[0x08], w1[0x0a], col(0x0c, False))
            area = '%d/%s/%d' % (w1[0x14], col(0x1a, False), w1[0x1c])
            soul = '%s(%s)' % (col(0x34), 'O' if w1[0x36] else 'X')
            exp1 = s16(w1[0x30])
            expsum = sum(s16(w[0x30]) for _, _, w in ws)
            effs = work_effects(ws[-1][2])
            print('| %d | %s | %s | %s | %s | %d | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %s | %d / %d | %s | %d | %s |' % (
                aid, nm, ab['file'][:4], CLASS.get(ab['cls'], ab['cls']), icon, s16(ab['maxlv']), ', '.join(pre), jtxt,
                '/'.join(KIND.get(k, str(k)) for k in kinds), col(0x2a), col(0x2c, False), col(0x2d, False), rng, w1[0x13], area,
                col(0x32), col(0x2e), soul, exp1, expsum, effs, chr_has[aid], text.get(ab['desc'], '').replace('$n', ' ').replace('|', '/') if ab['desc'] else ''))
        else:
            print('| %d | %s | %s | %s | %s | %d | %s | %s | (work 없음) |||||||||||| %d | %s |' % (
                aid, nm, ab['file'][:4], CLASS.get(ab['cls'], ab['cls']), icon, s16(ab['maxlv']), ', '.join(pre), jtxt, chr_has[aid],
                text.get(ab['desc'], '') if ab['desc'] else ''))
        for lv, wid, w in ws:
            rows.append([aid, nm, lv, wid, w['file'], w[0x1f], s16(w[0x2a]), w[0x2c], w[0x2d], w[0x07], w[0x08], w[0x0a], w[0x0c],
                         w[0x13], w[0x14], w[0x1a], w[0x1c], s16(w[0x32]), s16(w[0x2e]), s16(w[0x34]), w[0x36], s16(w[0x30]),
                         work_effects(w), w[0x3f]])

    head = ['어빌리티', '이름', '레벨', 'work', 'att파일', '종류+1f', '위력+2a', '명중+2c', '치명+2d', '사거리모양+7', '사거리방식+8',
            '최소+a', '최대+c', '대상+13', '범위모양+14', '범위+1a', '범위+1c', 'TP+32', '체질값+2e', 'SOUL+34', 'SOUL소모+36',
            'EXP+30', '효과', '준비동작+3f']
    if a.levels:
        print()
        print('| ' + ' | '.join(head) + ' |')
        print('|' + '---|' * len(head))
        for r in rows:
            print('| ' + ' | '.join(str(x) for x in r) + ' |')
    if a.csv:
        with open(a.csv, 'w', newline='', encoding='utf-8-sig') as f:
            wr = csv.writer(f)
            wr.writerow(head)
            wr.writerows(rows)


if __name__ == '__main__':
    main()

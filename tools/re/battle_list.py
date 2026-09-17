"""창세기전3 파트2 — 챕터(Chp)·필드(Fld) 자료를 DLL 로더 순서대로 풀어, 어느 전투(Btl)가 어디서 불리는지 표로 뽑는다.

쓰기:
    python tools/re/battle_list.py "<게임 폴더>" [--md 출력.md] [--chp N] [--fld N]

게임 폴더는 읽기만 한다(낱장 우선, 없으면 .idx/.pak). 근거는 분석/분석-전투목록.md.

Chp\\%04d.chp — 로더 0x100f6c80 (CChapter)
  머리: u16 ?, u16 +0x100, u16 +0x178(갈래), u16 +0x102, u16 +0x104,
        16B 칸 8개(+0x126, 0x2e94 번호), 16B 칸 8개(+0x138), u16 +0x2e40, u16 +0x2e42, u16 제목 txr(+0x116)
  u16 n1, u16 → n1 × 12B (6워드, +0x118)                  : 성계 지도 위 점
  u16 n2, u16 → n2 × 30B (+0x2e94)
  u16 n3, u16 → n3 × 66B (8워드 + 16B×3 + u16, +0x2e88)    : 항성계(이름 txr w2, 설명 w3)
  u16 n4, u16 → n4 × 84B (8워드 + 16B×3 + 10워드, +0x2e8c) : 행성(이름 txr w2, 설명 w3, 칸0 = 장소 번호들)
  u16 n5, u16 → n5 × 20B (10워드, +0x2e90)                 : 장소 — w0 번호, w1 이름 txr, w2 값
        값 v: 1~9999 = 전투 Btl v (장면 1), 10000~19999 = 필드 Fld v-10000 (장면 3), 20000~ = 0x100fb1f0(v-20000) 상점류
        (0x100ff031~0x100ff083, 0x100f5be2~0x100f5c1b)
  u16 n6, u16 → n6 × 4B
  스크립트(0x100ed1c0): u16 수, 각 (u16, u16 조건 수, (u16 코드+16B)×, u16 행동 수, (u16 코드+16B)×)
Fld\\%04d.fld — 로더 0x100eccc0
  u16 ?, u16×5, u16 n1, u16, n1×16B, u16 n2, u16, n2×16B, u16 n3, u16, n3×10B(인물), 스크립트(같은 꼴)
스크립트 행동 코드(실행 0x100f3f14): 10 = 전투 시작 Btl 인자0 (0x100f2e50), 6 = 필드로 Fld 인자0 (0x100f2df0)
"""
import argparse
import collections
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from btl_dump import load_txr, parse_btl, parse_map, read_game_file  # noqa: E402


def _h(b, o, n=1):
    v = struct.unpack_from('<%dh' % n, b, o)
    return list(v) if n > 1 else v[0]


def list_ids(game, folder, ext):
    ids = set()
    src = os.path.join(game, folder)
    for f in os.listdir(src):
        if f.lower().endswith(ext) and f[:4].isdigit():
            ids.add(int(f[:4]))
    for idx in [f for f in os.listdir(src) if f.lower().endswith('.idx')]:
        data = open(os.path.join(src, idx), 'rb').read()
        total = struct.unpack_from('<I', data, 0)[0] // 0x10000
        for k in range(total):
            rec = data[6 + 36 * k:42 + 36 * k]
            n = bytes(x ^ 0xFF for x in rec[14:23]).split(b'\0')[0].decode('cp949', 'replace')
            if n.lower().endswith(ext) and n[:4].isdigit():
                ids.add(int(n[:4]))
    return sorted(ids)


def parse_script(b, o):
    n = _h(b, o)
    o += 2
    ev = []
    for _ in range(max(n, 0)):
        a, nc = _h(b, o, 2)
        o += 4
        conds = [(_h(b, o + 18 * k), _h(b, o + 18 * k + 2, 8)) for k in range(nc)]
        o += 18 * nc
        na = _h(b, o)
        o += 2
        acts = [(_h(b, o + 18 * k), _h(b, o + 18 * k + 2, 8)) for k in range(na)]
        o += 18 * na
        ev.append(dict(a=a, conds=conds, acts=acts))
    return ev, o


def parse_chp(b):
    o = 10
    hdr = dict(w=_h(b, 0, 5))
    o += 32
    hdr['e40'], hdr['e42'], hdr['title'], n1, _ = _h(b, o, 5)
    o += 10
    nodes = [_h(b, o + 12 * i, 6) for i in range(n1)]
    o += 12 * n1
    n2 = _h(b, o)
    o += 4 + 30 * n2
    n3 = _h(b, o)
    o += 4
    systems = []
    for _ in range(n3):
        systems.append(dict(w=_h(b, o, 8), slots=[_h(b, o + 16 + 16 * k, 8) for k in range(3)]))
        o += 66
    n4 = _h(b, o)
    o += 4
    planets = []
    for _ in range(n4):
        planets.append(dict(w=_h(b, o, 8), slots=[_h(b, o + 16 + 16 * k, 8) for k in range(3)], tail=_h(b, o + 64, 10)))
        o += 84
    n5 = _h(b, o)
    o += 4
    places = [_h(b, o + 20 * i, 10) for i in range(n5)]
    o += 20 * n5
    n6 = _h(b, o)
    o += 4 + 4 * n6
    script, o = parse_script(b, o)
    return dict(hdr=hdr, nodes=nodes, systems=systems, planets=planets, places=places, script=script,
                used=o, size=len(b))


def parse_fld(b):
    o = 12
    n1 = _h(b, o)
    o += 4 + 16 * n1
    n2 = _h(b, o)
    o += 4 + 16 * n2
    n3 = _h(b, o)
    o += 4 + 10 * n3
    script, o = parse_script(b, o)
    return dict(script=script, used=o, size=len(b))


def script_targets(script):
    btl, fld = [], []
    for e in script:
        for code, args in e['acts']:
            if code == 10:
                btl.append(args[0])
            elif code == 6:
                fld.append(args[0])
    return btl, fld


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('--md')
    ap.add_argument('--chp', type=int)
    ap.add_argument('--fld', type=int)
    a = ap.parse_args()
    g = a.game
    txr = load_txr(g)
    t = lambda i: txr.get(i, '?') if i is not None and i >= 0 else '-'

    if a.chp is not None:
        c = parse_chp(read_game_file(g, 'Chp', f'{a.chp:04d}.chp'))
        print(f"Chp {a.chp:04d} 제목 {c['hdr']['title']}「{t(c['hdr']['title'])}」 머리 {c['hdr']['w']} {c['used']}/{c['size']}바이트")
        for s in c['systems']:
            print('  항성계', s['w'][:4], t(s['w'][2]))
        for p in c['planets']:
            print('  행성', p['w'][:5], t(p['w'][2]), '장소', [x for x in p['slots'][0] if x >= 0])
        for pl in c['places']:
            print('  장소', pl, t(pl[1]))
        print('  스크립트 전투/필드', script_targets(c['script']))
        return
    if a.fld is not None:
        f = parse_fld(read_game_file(g, 'Fld', f'{a.fld:04d}.fld'))
        print(f"Fld {a.fld:04d} {f['used']}/{f['size']}바이트, 이벤트 {len(f['script'])}, 전투/필드 {script_targets(f['script'])}")
        return

    # 1. 챕터
    chapters = {}
    for i in list_ids(g, 'Chp', '.chp'):
        b = read_game_file(g, 'Chp', f'{i:04d}.chp')
        try:
            c = parse_chp(b)
        except struct.error:
            continue
        if c['used'] != c['size']:
            continue
        chapters[i] = c
    # 2. 필드
    fields = {}
    for i in list_ids(g, 'Fld', '.fld'):
        try:
            f = parse_fld(read_game_file(g, 'Fld', f'{i:04d}.fld'))
        except struct.error:
            continue
        if f['used'] == f['size']:
            fields[i] = f
    # 3. 전투 파일 (이벤트 행동 6 = 끝나고 Fld, 10 = 이어서 Btl — 0x10056864 → 0x10050c00, 0x10056859 → 0x10050c40)
    battles = {}
    for bi in list_ids(g, 'Btl', '.btl'):
        b = read_game_file(g, 'Btl', f'{bi:04d}.btl')
        try:
            bt = parse_btl(b)
            battles[bi] = bt if bt['used'] == bt['size'] else None
        except struct.error:
            battles[bi] = None

    # 4. 부르는 관계: 노드 ('chp'|'fld'|'btl', 번호)
    edges = collections.defaultdict(set)
    src = collections.defaultdict(list)       # btl -> [(설명)]
    for ci, c in chapters.items():
        title = t(c['hdr']['title'])
        planet_of = {}
        for p in c['planets']:
            for pid in p['slots'][0]:
                if pid >= 0:
                    planet_of.setdefault(pid, t(p['w'][2]))
        for pl in c['places']:
            v = pl[2]
            if 0 < v < 10000:
                src[v].append(f"Chp {ci:04d}「{title}」 {planet_of.get(pl[0], '?')} / {t(pl[1])}")
                edges[('chp', ci)].add(('btl', v))
            elif 10000 <= v < 20000:
                edges[('chp', ci)].add(('fld', v - 10000))
        bt, ft = script_targets(c['script'])
        for v in bt:
            src[v].append(f"Chp {ci:04d}「{title}」 스크립트")
            edges[('chp', ci)].add(('btl', v))
        for v in ft:
            edges[('chp', ci)].add(('fld', v))
    for fi, f in fields.items():
        bt, ft = script_targets(f['script'])
        for v in bt:
            src[v].append(f"Fld {fi:04d} 스크립트")
            edges[('fld', fi)].add(('btl', v))
        for v in ft:
            edges[('fld', fi)].add(('fld', v))
    for bi, bt in battles.items():
        if not bt:
            continue
        for d in bt['D']:
            for code, args in d['acts']:
                v = struct.unpack_from('<h', bytes.fromhex(args), 0)[0]
                if code == 10:
                    src[v].append(f"Btl {bi:04d} 이벤트(이어서)")
                    edges[('btl', bi)].add(('btl', v))
                elif code == 6:
                    edges[('btl', bi)].add(('fld', v))
    # 챕터마다 닿는 노드
    reach = collections.defaultdict(set)      # node -> {chp}
    for ci in chapters:
        seen, stack = set(), [('chp', ci)]
        while stack:
            n = stack.pop()
            if n in seen:
                continue
            seen.add(n)
            reach[n].add(ci)
            stack.extend(edges.get(n, ()))

    # 5. 전투 파일 요약
    rows = []
    for bi, bt in battles.items():
        chs = sorted(reach.get(('btl', bi), ()), key=lambda c: chapters[c]['hdr']['title'])
        if not bt:
            rows.append(dict(id=bi, ok=False, src=src.get(bi, []), chs=chs))
            continue
        h = bt['hdr']
        cnt = collections.defaultdict(collections.Counter)
        chr_names = {}
        for u in bt['A']:
            if u['chr'] not in chr_names:
                c = read_game_file(g, 'Chr', f"{u['chr']:04d}.chr")
                chr_names[u['chr']] = t(struct.unpack_from('<H', c, 2)[0]) if c and len(c) > 4 else '?'
            cnt[u['army']][chr_names[u['chr']]] += 1
        m = read_game_file(g, 'Map', f"{h[1]:04d}.map")
        obt = None
        if m:
            try:
                obt = parse_map(m)['obt']
            except struct.error:
                pass
        rows.append(dict(id=bi, ok=True, name=t(h[4]), win=t(h[5]), lose=t(h[6]), map=h[1], obt=obt, bgm=h[8],
                         army=cnt, zones=len(bt['C']), squad=h[7], src=src.get(bi, []), chs=chs,
                         squads=sum(1 for u in bt['A'] if u['squad'] > 0)))

    def fmt_army(cnt, keys):
        parts = []
        for k in keys:
            for n, c in cnt.get(k, {}).items():
                parts.append(f"{n}×{c}" if c > 1 else n)
        return ', '.join(parts) or '-'

    def rank(ci):
        tt = chapters[ci]['hdr']['title']
        return tt if 2284 <= tt <= 2313 else 9999

    def key(r):
        return (min((rank(c) for c in r['chs']), default=99999), r['id'])

    rows.sort(key=key)
    out = []
    out.append('| 챕터 | Btl | 이름 | 불리는 곳 | 맵/Obt | 아군(부대 4) | 동맹 AI(부대 3) | 적(부대 0·1·2) | 배치칸 | 승리 조건 | 패배 조건 |')
    out.append('|---|---|---|---|---|---|---|---|---|---|---|')
    for r in rows:
        ch = ', '.join(f"{c:04d} {t(chapters[c]['hdr']['title'])}" for c in r['chs'][:3])
        if len(r['chs']) > 3:
            ch += f" 외 {len(r['chs']) - 3}"
        ch = ch or '(못 찾음)'
        where = '<br>'.join(dict.fromkeys(r['src'])) or '-'
        if not r['ok']:
            out.append(f"| {ch} | {r['id']:04d} | (옛 형식, 못 읽음) | {where} | | | | | | | |")
            continue
        out.append(f"| {ch} | {r['id']:04d} | {r['name']} | {where} | {r['map']}/{r['obt']} | {fmt_army(r['army'], [4])} | "
                   f"{fmt_army(r['army'], [3])} | {fmt_army(r['army'], [0, 1, 2])} | {r['zones'] or '-'} | {r['win']} | {r['lose']} |")
    text = '\n'.join(out)
    if a.md:
        open(a.md, 'w', encoding='utf-8').write(text + '\n')
        print(f"{len(rows)}개 → {a.md}")
    else:
        print(text)
    found = sum(1 for r in rows if r['src'])
    inch = sum(1 for r in rows if r['chs'])
    print(f"챕터 {len(chapters)}개, 필드 {len(fields)}개 해석. 전투 {len(rows)}개 중 부르는 곳을 찾은 것 {found}개, "
          f"챕터에서 닿는 것 {inch}개")


if __name__ == '__main__':
    main()

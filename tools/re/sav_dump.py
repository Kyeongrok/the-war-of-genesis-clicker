"""
창세기전3 파트2 — 세이브 파일(G3P_IINN.sav)에서 파티 캐릭터(CChr)·파티 인벤토리를 풀어 Status 편집에 쓰는 칸을 찍는다.

사용법:
    python sav_dump.py <게임 폴더> [세이브 파일 이름(기본: 폴더 안 첫 *.sav)] [--chr 221 219 ...]

게임 폴더는 읽기만 한다. skill_motion.py(Abi/att 읽기), extract_character.py(TXR) 를 함께 쓴다.

G3PartII.dll 근거 (옵시디안 분석-캐릭터.md "Status 편집 (구현 st-1·st-2·st-3 용)"):
  쓰기 0x10006160 / 읽기 0x10006210 = fwrite/fread + 바이트마다 NOT(= XOR 0xFF).
  머리 22바이트 (SaveGame 0x10112a70(슬롯, 장면객체)) — 2026-09-18 확정, 옵시디안 분석-시스템메뉴.md:
    u32 판 0x1017037c (7; 5 보다 커야 끝의 CD u16 이 있음)
    u32 플레이 시간(ms) 0x101737a4
    u32 장면객체 vt+0x1c() (목록에서 읽지만 안 씀)
    u32 장면객체 +4  = 장면 번호 (1 전투 · 4 챕터 · 7 연대표)
    u32 장면객체 +8  = 장면 인자 (전투 번호 / 챕터 번호)
    u16 CD 번호 0x101638e4
  본문 (0x1004d9d0(f, 장면번호)):
    2000바이트 전역 0x101b6050
    u16 캐릭터 수 N (0x101b6882), CChr 932바이트 × N (0x10033030, 표 0x101b6884)
    파티 객체 4272바이트 × 3 (0x1004e030, 0x101b6888[0..2] = 살라딘·베라모드·크리스티앙 파티)
    u16 지금 파티 번호 (0x101b6894), 200바이트 0x101b68a0,
    (장면번호 ≠ 7 이면) u16 + 챕터 화면 상태 0x1004e280
  ※ 전투 중 상태(유닛 자리·HP·TP·차례)는 저장하지 않는다. 전투 중 저장은 전투 시작 직전 파티 상태.
  CChr(메모리 = 파일): +4 Chr 번호, +6 이름 TXR, +0x12 체질, +0x14 Dep 번호, +0x16 직업, +0x2c 레벨,
    +0x2e 누적 EXP, +0x30 쓸 수 있는 EXP, +0x4a 무기 종류, +0x4c 장비 u16×6,
    +0x5a .chr 어빌리티 u16×8, +0x6a 그 레벨 u16×8, +0x7a 어빌리티 번호별 레벨 u8×200(0xff 없음),
    +0x142 장착 어빌리티 u16×3
  파티: +4 인원 u32, +8 Chr 번호 u32×64, +0x108 아이템 가짓수 u32, +0x10c 돈(가설), +0x110 (아이템 u32, 개수 u32)×256
"""
import argparse
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import skill_motion as sm  # noqa: E402
from extract_character import parse_txr  # noqa: E402

CHR_SIZE = 0x3a4
PARTY_SIZE = 0x10b0
PARTY_NAMES = ['살라딘 파티', '베라모드 파티', '크리스티앙 파티']


def load_tables(g):
    txr = {}
    for r in parse_txr(g.read('TXR', 'Txr.dat')):
        txr.setdefault(r['txr_id'], r['text'])
    works = sm.load_works(g)
    abis = {}
    for fn in sm.ABI_FILES:
        d = g.read('Abi', fn)
        _, n, _ = struct.unpack_from('<3H', d)
        for i in range(n):
            v = struct.unpack_from(sm.ABI_FMT, d, 6 + i * 26)
            abis[v[0]] = {'name': v[1], 'maxlv': v[2], 'pre1': v[3], 'lv1': v[4], 'pre2': v[5], 'lv2': v[6],
                          'type': v[7], 'levels': {}}
    for wid, w in works.items():
        if w[0x4] in abis and w[0x6]:
            abis[w[0x4]]['levels'][w[0x6]] = wid
    items = {}
    d = g.read('Dat', 'itm.dat')
    _, n, _ = struct.unpack_from('<3H', d)
    for i in range(n):
        o = 6 + i * 48
        items[struct.unpack_from('<H', d, o)[0]] = (struct.unpack_from('<H', d, o + 2)[0], d[o + 8])
    deps = []
    d = g.read('Dat', 'Dep.dat')
    o = 6
    while o + 33 <= len(d) - 2:
        deps.append((struct.unpack_from('<H', d, o + 2)[0], d[o + 4]))   # (이름 TXR, 장착 어빌리티 칸 수 = 메모리 +6)
        o += 33
    jobs = {}
    d = g.read('Dat', 'Job.dat')
    _, n, _ = struct.unpack_from('<3H', d)
    for i in range(n):
        o = 6 + i * 67
        jobs[struct.unpack_from('<H', d, o)[0]] = [a for a in struct.unpack_from('<11H', d, o + 31) if a]
    return txr, works, abis, items, deps, jobs


def parse_sav(path):
    d = bytes(b ^ 0xFF for b in open(path, 'rb').read())
    head = struct.unpack_from('<5IH', d, 0)
    o = 22 + 2000
    n = struct.unpack_from('<H', d, o)[0]
    o += 2
    chars = [d[o + i * CHR_SIZE:o + (i + 1) * CHR_SIZE] for i in range(n)]
    o += n * CHR_SIZE
    parties = [d[o + i * PARTY_SIZE:o + (i + 1) * PARTY_SIZE] for i in range(3)]
    o += 3 * PARTY_SIZE
    cur = struct.unpack_from('<H', d, o)[0]
    return head, chars, parties, cur


def learnable(c, abis, jobs):
    """0x100324e0: 직업 목록 중 아직 없고(0xff) 선행 조건 둘을 채운 것."""
    job = struct.unpack_from('<H', c, 0x16)[0]
    out = []
    for a in jobs.get(job, []):
        if c[0x7a + a] != 0xFF or a not in abis:
            continue
        ab = abis[a]
        if all(not p or (c[0x7a + p] != 0xFF and c[0x7a + p] >= lv) for p, lv in ((ab['pre1'], ab['lv1']), (ab['pre2'], ab['lv2']))):
            out.append(a)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('sav', nargs='?')
    ap.add_argument('--chr', type=int, nargs='*')
    a = ap.parse_args()
    g = sm.Game(a.game)
    txr, works, abis, items, deps, jobs = load_tables(g)
    t = lambda i: txr.get(i, '?%d' % i)  # noqa: E731
    name = a.sav or next(f for f in os.listdir(a.game) if f.lower().endswith('.sav'))
    head, chars, parties, cur = parse_sav(os.path.join(a.game, name))
    print(f'{name}: 머리 {head}, 캐릭터 {len(chars)}, 지금 파티 {cur}')
    H = lambda b, o: struct.unpack_from('<H', b, o)[0]  # noqa: E731
    for c in chars:
        code = H(c, 4)
        if a.chr and code not in a.chr:
            continue
        dep = H(c, 0x14)
        slots = deps[dep][1] if dep < len(deps) else '?'
        print(f'\n[{code}] {t(H(c, 6))}  체질 {c[0x12]}  직업 {H(c, 0x16)}  계열 {t(deps[dep][0]) if dep < len(deps) else dep}'
              f'  레벨 {H(c, 0x2c)}  EXP 누적 {H(c, 0x2e)} / 쓸 수 있음 {struct.unpack_from("<h", c, 0x30)[0]}  무기 종류 {c[0x4a]}')
        eq = [H(c, 0x4c + 2 * i) for i in range(6)]
        print('  장비:', ', '.join(f'{t(items[i][0])}(종류 {items[i][1]})' if i in items and i else '없음' for i in eq))
        print(f'  장착 어빌리티 ({slots}칸):', [t(abis[p]['name']) if p in abis else p for p in (H(c, 0x142 + 2 * i) for i in range(3))])
        exp = struct.unpack_from('<h', c, 0x30)[0]
        for ab_id in range(200):
            lv = c[0x7a + ab_id]
            if lv == 0xFF or ab_id not in abis:
                continue
            ab = abis[ab_id]
            nxt = ab['levels'].get(lv + 1) if ab['maxlv'] > lv else None
            cost = works[nxt][0x30] if nxt else None
            mark = '' if cost is None else (' (EXP 모자람)' if cost > exp else ' (올릴 수 있음)')
            print(f'  획득 {t(ab["name"])} Lv{lv}/{ab["maxlv"]} 종류 {ab["type"]} 다음 비용 {cost}{mark}')
        for ab_id in learnable(c, abis, jobs):
            w = abis[ab_id]['levels'].get(1)
            print(f'  획득 가능 {t(abis[ab_id]["name"])} 비용 {works[w][0x30] if w else "?"}')
    for k, p in enumerate(parties):
        n = struct.unpack_from('<I', p, 4)[0]
        members = struct.unpack_from('<%dI' % n, p, 8)
        ni = struct.unpack_from('<I', p, 0x108)[0]
        inv = [struct.unpack_from('<II', p, 0x110 + 8 * i) for i in range(ni)]
        print(f'\n{PARTY_NAMES[k]}: 인원 {list(members)}  +0x10c {struct.unpack_from("<I", p, 0x10c)[0]}')
        for it, cnt in inv:
            print(f'  {t(items[it][0]) if it in items else it} (종류 {items[it][1] if it in items else "?"}) × {cnt}')


if __name__ == '__main__':
    main()

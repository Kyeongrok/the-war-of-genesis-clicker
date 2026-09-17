"""
창세기전3 파트2 — 전투 유닛 하나의 이동 가능 영역(푸른 칸)과 일반공격 영역(붉은 칸)을 G3PartII.dll 식대로 계산해 글자 지도로 찍는다.

사용법:
    python tools/re/move_range.py <게임 폴더> <Btl 번호> <유닛 순서(Btl 캐릭터 목록 0부터)> [--tp N] [--work W]

게임 폴더는 읽기만 한다. 근거는 분석/분석-전투.md 의 「이동 가능 영역」 절.

코드로 확인한 식:
    예산   = 현재TP + min(0, CTP − TP비용(work))               ; 0x10069cc9 (상태 12), 0x1006954e (상태 10)
    한 걸음(4방향) 비용 = |dx|·Num[5]/DEX + |dy|·Num[5]/DEX + (|Δ높이|·Num[5]/DEX)/2   ; 0x100da89a
             (C 정수 나눗셈, Num[5]=4500, DEX = CChr+0x46 + 아이템 0x1e + 유닛+0x4c8 − 효과1)  ; 0x1007ae50
    걸음 가능 0x100da060 → 0x100d9a20:
        칸 안, 지형 플래그 & 0x9 == 0, 다른 유닛 없음,
        그 칸과 상하좌우 칸에 같은 높이의 적(편 행렬 0x3a94) 유닛 없음, |높이차| ≤ 2
    높이   = (Obt 칸 레코드 u16[0] + 19) / 20   ; 0x10028680
    플래그 = Obt 칸 레코드 뒤 u16 격자        ; 0x100287b2
    길찾기 0x100596a0: 비용 ≤ 예산 인 칸만 거리표(0x101be210)에 남김 → 층 2(파랑 100,100,255)에 비트.
    붉은 칸(층 12, 255,100,40): 푸른 칸 각각에서 work 모양·사거리로 칠함 (0x100749b0).
      여기서는 모양 2(십자) 만 구현: 같은 줄/열, min ≤ 4·맨해튼 ≤ max (min=+0xa 파일값×4−3, max=+0xc 파일값×4).

가설로 둔 것: 유닛 높이 = 서 있는 칸 높이, Btl 캐릭터의 '부대' 값 = 편(+0x78), 아이템 보너스 0,
Btl 오브젝트(B)는 길을 막지 않음(목표 칸 제외 여부는 종류 +0x116 을 몰라 표시만).
"""
import argparse
import heapq
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from skill_motion import Game, load_works  # noqa: E402
from exp_tables import parse_num  # noqa: E402
from tp_calc import load_chr_stats, tp_cost  # noqa: E402
from btl_dump import parse_btl, parse_map  # noqa: E402

# 싱글 플레이 편 행렬 0x1006b76f: 1 = 적대
HOSTILE = [
    [0, 1, 1, 1, 1],
    [1, 0, 1, 1, 1],
    [1, 1, 0, 1, 1],
    [1, 1, 1, 0, 0],
    [1, 1, 1, 0, 0],
]


def c_div(a, b):
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def load_obt(d):
    ver, cols, rows = struct.unpack_from('<Hhh', d, 0)
    rs = 10 + 1 + 16 + 2 + 2 + 2 + (12 if ver >= 4 else 0)
    o = 6
    h = []
    for i in range(cols * rows):
        v = struct.unpack_from('<H', d, o + i * rs)[0]
        if ver == 1:
            v = (v * 20) & 0xFFFF
        h.append(v)
    o += cols * rows * rs
    grid = list(struct.unpack_from('<%dH' % (cols * rows), d, o))
    height = [0] * (cols * rows)
    flags = [0] * (cols * rows)
    for i in range(cols * rows):
        if grid[i] & 0x10:
            continue
        if h[i]:
            height[i] = c_div(h[i] + 19, 20)
        flags[i] = grid[i]
    return cols, rows, height, flags


def hostile(a, b):
    if 0 <= a < 5 and 0 <= b < 5:
        return HOSTILE[a][b]
    return int(a != b)


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('btl', type=int)
    ap.add_argument('unit', type=int)
    ap.add_argument('--tp', type=int, help='현재 TP (기본: 최대 TP)')
    ap.add_argument('--work', type=int, help='예산에서 남겨 둘 work (기본: .chr 기본공격)')
    a = ap.parse_args()

    g = Game(a.game)
    num, _ = parse_num(g.read('Dat', 'Num.dat'))
    works = load_works(g)
    bt = parse_btl(g.read('Btl', '%04d.btl' % a.btl))
    mp = parse_map(g.read('Map', '%04d.map' % bt['hdr'][1]))
    cols, rows, H, F = load_obt(g.read('Obt', '%04d.obt' % mp['obt']))

    units = bt['A']
    me = units[a.unit]
    st = load_chr_stats(g, me['chr'])
    dex = st['DEX']
    tp = st['TP'] if a.tp is None else a.tp
    wid = st['basic'] if a.work is None else a.work
    w = works[wid]
    cost_w = tp_cost(w, st['body'], num)
    budget = tp + min(0, st['CTP'] - cost_w)
    n5 = num[5]

    occ = {}
    for i, u in enumerate(units):
        occ[(u['x'], u['y'])] = i

    def idx(x, y):
        return y * cols + x

    def inb(x, y):
        return 0 <= x < cols and 0 <= y < rows

    def enterable(x, y):                       # 0x100d9a20 (작은 유닛, 걷는 유닛)
        if (x, y) == (me['x'], me['y']):
            return True
        if not inb(x, y) or F[idx(x, y)] & 0x9:
            return False
        o = occ.get((x, y))
        if o is not None and o != a.unit:
            return False
        h = H[idx(x, y)]
        for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
            o = occ.get((nx, ny))
            if o is None or not inb(nx, ny):
                continue
            u = units[o]
            if abs(H[idx(nx, ny)] - h) < 1 and hostile(me['army'], u['army']):
                return False
        return True

    def step_cost(cx, cy, nx, ny):             # 0x100da760
        return (c_div(abs(cy - ny) * n5, dex) + c_div(abs(cx - nx) * n5, dex)
                + c_div(c_div(abs(H[idx(nx, ny)] - H[idx(cx, cy)]) * n5, dex), 2))

    dist = {(me['x'], me['y']): 0}
    pq = [(0, me['x'], me['y'])]
    while pq:
        d, x, y = heapq.heappop(pq)
        if d > dist[(x, y)]:
            continue
        for nx, ny in ((x, y - 1), (x + 1, y), (x, y + 1), (x - 1, y)):   # 0x10059a04 순서
            if not enterable(nx, ny) or abs(H[idx(x, y)] - H[idx(nx, ny)]) > 2:
                continue
            nd = d + step_cost(x, y, nx, ny)
            if nd > budget or nd >= dist.get((nx, ny), 1 << 30):
                continue
            dist[(nx, ny)] = nd
            heapq.heappush(pq, (nd, nx, ny))

    blue = {p for p in dist if occ.get(p, a.unit) == a.unit}
    red = set()
    lo, hi = w[0xa] * 4 - 3, w[0xc] * 4
    if w[0x7] == 2 and w[0x8] in (1, 3):
        for (x, y) in blue:
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                for k in range(1, hi // 4 + 1):
                    tx, ty = x + dx * k, y + dy * k
                    if inb(tx, ty) and lo <= 4 * k <= hi and not F[idx(tx, ty)] & 0x8:
                        red.add((tx, ty))

    objs = {(o['x'], o['y']) for o in bt['B']}
    print(f"Btl {a.btl:04d} Map {bt['hdr'][1]:04d} Obt {mp['obt']:04d} ({cols}x{rows})")
    print(f"유닛 {a.unit}: Chr {me['chr']} ({me['x']},{me['y']}) 편 {me['army']}  DEX {dex} TP {tp} CTP {st['CTP']}")
    print(f"work {wid}: TP비용 {cost_w}, 모양 {w[0x7]}, 사거리 {lo}~{hi}(4=한 칸)")
    print(f"예산 = {tp} + min(0, {st['CTP']} - {cost_w}) = {budget}, 평지 한 걸음 = {n5}//{dex} = {n5 // dex}")
    print("범례: @ 자신, A 아군, E 적, o 오브젝트, # 막힘(플래그&9), 숫자 = 경로 비용 ÷ 10 (파랑), + 붉은 칸만, . 그 밖")
    print('    ' + ''.join('%d' % (x // 10) for x in range(cols)))
    print('    ' + ''.join('%d' % (x % 10) for x in range(cols)))
    for y in range(rows):
        line = []
        for x in range(cols):
            p = (x, y)
            if p == (me['x'], me['y']):
                c = '@'
            elif p in occ:
                c = 'E' if hostile(me['army'], units[occ[p]]['army']) else 'A'
            elif p in objs:
                c = 'o'
            elif p in blue:
                c = str(min(dist[p] // 10, 9))
            elif p in red:
                c = '+'
            elif F[idx(x, y)] & 0x9:
                c = '#'
            else:
                c = '.'
            line.append(c)
        print('%3d ' % y + ''.join(line))
    print(f"푸른 칸 {len(blue)}개, 붉은 칸(푸른 칸 제외) {len(red - blue)}개")


if __name__ == '__main__':
    main()

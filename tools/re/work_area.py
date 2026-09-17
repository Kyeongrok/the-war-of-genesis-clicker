"""
창세기전3 파트2 — work(att) 하나의 **사거리 모양**(+0x7)과 **효과 범위 모양**(+0x14)을 평지 기준으로 그려 본다.

사용법:
    python work_area.py <게임 폴더> <work 번호 또는 어빌리티 이름> [...]
    python work_area.py <게임 폴더> --list            # 모양 번호별 work 개수

게임 폴더는 읽기만 한다. 규칙 근거는 옵시디안 [[분석-전투]] "어빌리티 범위·자세·상태이상" 절.

요약 (G3PartII.dll):
  거리 = 4×(|dx|+|dy|) + 높이항          (0x100daca0; 모양 3·6·7·9 는 축 거리만 0x100db310/0x100dafe0)
  사거리: 최소 = 파일 +0xa 값이 0 아니면 값×4−3, 최대 = +0x8 종류(1·3 = +0xc×4, 2 = 무기 사거리, 4 = 둘의 합)
  효과 범위: 최대 = 파일 +0x1a × 4, 최소 = c×(4c−3)
  모양: 1 마름모, 2 십자, 3 부채꼴(3칸마다 1칸씩 벌어짐), 4 화면 전체, 5 직선,
        6 폭 3 직선, 7 폭 5 직선, 8 대각선 X, 9 45° 삼각형
  (3·5·6·7·9 는 방향을 쓴다. 사거리 그리기는 네 방향을 다 합치고, 효과 범위는 시전자→목표 방향 하나만.)
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import skill_motion as sm  # noqa: E402
import skill_list as sl  # noqa: E402

DIRS = {0: (0, -1), 1: (-1, 0), 2: (0, 1), 3: (1, 0)}   # 0 위, 1 왼, 2 아래, 3 오른 (0x1001ed50)


def dist(dx, dy):
    return 4 * (abs(dx) + abs(dy))


def shape_cells(shape, lo, hi, direction):
    """평지(높이 차 없음) 기준으로 원점(0,0)에서 켜지는 칸 집합. lo/hi 는 4분의 1 칸 단위."""
    half = hi // 2
    out = set()
    dx, dy = DIRS[direction]
    for y in range(-half - 1, half + 2):
        for x in range(-half - 1, half + 2):
            d = dist(x, y)
            if shape == 1:                                  # 마름모 0x100db640
                ok = True
            elif shape == 2:                                # 십자 0x100dbe30
                ok = x == 0 or y == 0
            elif shape == 8:                                # 대각선 0x100dc1e0
                ok = abs(x) == abs(y)
            elif shape == 4:                                # 화면 전체 0x100de010
                ok = abs(x) <= 8 and abs(32 * y) <= 240
            elif shape in (3, 5, 6, 7, 9):                  # 방향 모양
                axis = x * dx + y * dy                      # 진행 방향 거리(칸)
                side = abs(x * dy - y * dx)                 # 옆으로 벌어진 거리(칸)
                d = 4 * axis                                # 이 모양들은 축 거리만 센다
                if shape == 3:
                    ok = axis >= 0 and side <= axis // 3
                elif shape == 5:
                    ok = axis >= 0 and side == 0
                elif shape == 6:
                    ok = axis >= 0 and side <= 1
                elif shape == 7:
                    ok = axis >= 0 and side <= 2
                else:  # 9
                    ok = axis >= 1 and side <= axis - 1
            else:
                ok = False
            if ok and lo <= d <= hi:
                out.add((x, y))
    return out


def draw(cells, mark='#'):
    if not cells:
        return ['(없음)']
    xs = [c[0] for c in cells] + [0]
    ys = [c[1] for c in cells] + [0]
    rows = []
    for y in range(min(ys), max(ys) + 1):
        row = ''
        for x in range(min(xs), max(xs) + 1):
            row += '@' if (x, y) == (0, 0) else (mark if (x, y) in cells else '.')
        rows.append(row)
    return rows


def work_shapes(w, weapon_range=0):
    """(사거리 칸들, 효과 범위 칸들). 효과 범위는 방향 2(아래) 기준."""
    rmin = w[0xa] * 4 - 3 if w[0xa] else 0
    kind = w[0x8]
    rmax = {0: 0, 1: w[0xc] * 4, 3: w[0xc] * 4, 2: weapon_range * 4, 4: weapon_range * 4 + w[0xc] * 4}.get(kind, 0)
    rshape = w[0x7]
    amin = w[0x1c] * (4 * w[0x1c] - 3)
    amax = w[0x1a] * 4
    ashape = w[0x14]
    rng = set()
    if rshape in (1, 2, 4, 8):
        rng = shape_cells(rshape, rmin, rmax, 2)
    elif rshape in (3, 5, 6, 7, 9):
        for d in range(4):
            rng |= shape_cells(rshape, rmin, rmax, d)
    area = shape_cells(ashape, amin, amax, 2) if ashape else set()
    return (rmin, rmax, rng), (amin, amax, area)


TARGET = {0: '자기 자리(겨냥 없음)', 1: '적 하나', 2: '자기 자리(겨냥 없음)', 3: '아무 칸',
          4: '아군 하나', 5: '아무 유닛 하나', 6: '아무 칸', 7: '빈 칸(같은 높이)', 8: '오브젝트'}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('what', nargs='*')
    ap.add_argument('--weapon-range', type=int, default=2, help='+0x8=2·4 일 때 쓸 무기 사거리(칸)')
    a = ap.parse_args()
    g = sm.Game(a.game)
    works = sm.load_works(g)
    abis = sm.load_abis(g, works)
    text = sl.load_text(g)

    def name(w):
        ab = abis.get(w[0x4])
        return '%s Lv%d' % (text.get(ab['name'], '?') if ab else '-', w[0x6])

    ids = []
    for t in a.what:
        if t.isdigit():
            ids.append(int(t))
        else:
            ids += [i for i in sorted(works) if t in name(works[i])][:3]
    for wid in ids:
        w = works[wid]
        (rmin, rmax, rng), (amin, amax, area) = work_shapes(w, a.weapon_range)
        print('work %d  %s' % (wid, name(w)))
        print('  대상 방식 +0x13=%d (%s), 효과 대상 +0x1e=%d, 종류 +0x1f=%d'
              % (w[0x13], TARGET.get(w[0x13], '?'), w[0x1e], w[0x1f]))
        print('  사거리: 모양 %d, 종류 %d, %d~%d(4분의1 칸) = %d칸 / 높이보정 %d, 시야 %d, 같은높이만 %d, 위아래차등 %d'
              % (w[0x7], w[0x8], rmin, rmax, rmax // 4, w[0xf], w[0x10], w[0x11], w[0x12]))
        for r in draw(rng, '+'):
            print('    ' + r)
        print('  효과 범위: 모양 %d, %d~%d = %d칸 / 높이보정 %d, 같은높이만 %d'
              % (w[0x14], amin, amax, amax // 4, w[0x16], w[0x18]))
        for r in draw(area, '*'):
            print('    ' + r)
        eff = [(w[0x20 + i], sl.s16(w[0x24 + 2 * i])) for i in range(3) if w[0x20 + i]]
        if eff:
            print('  상태이상: ' + ', '.join('%d(값 %d)' % e for e in eff))
        print()


if __name__ == '__main__':
    main()

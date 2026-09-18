"""
창세기전3 파트2 — `Dat/Itm.dat` 아이템 레코드(파일 48바이트)를 전부 풀고,
그 중 **전투에서 쓸 수 있는 아이템**(종류 7)과 그 아이템이 부르는 work(att) 를 짝지어 찍는다.

사용법:
    python item_use.py <게임 폴더>                 # 전투용 아이템(종류 7) 표
    python item_use.py <게임 폴더> --all           # 아이템 226개 전부
    python item_use.py <게임 폴더> --type 16       # 그 종류만
    python item_use.py <게임 폴더> --check         # 파일 크기·칸 분포 검산

게임 폴더는 읽기만 한다.

G3PartII.dll 근거 (옵시디안 [[분석-전투]] 「전투 중 아이템 쓰기」 절):
  LoadItemData 0x1004b120 의 fread 순서 그대로가 파일 배치다(메모리 레코드 56바이트, 표 0x101b687c).
    파일 0  u16  아이템 번호(색인)        메모리 +0x00 에 1(있음)
    파일 2  u16  이름 TXR                 +0x04
    파일 4  u32  가격                     +0x08
    파일 8  u8   종류                     +0x0c
    파일 9  u16  그림(Obs 0326 모션)       +0x0e   0xffff 면 종류를 그림 번호로
    파일 11 u16  무기 공격력               +0x10
    파일 13 u16  갑옷 배율                 +0x12
    파일 15 u8   ?                        +0x14
    파일 16 u16  무기 사거리               +0x16   ← 적재 때 ×4
    파일 18/22/26 u16  장비 효과 종류 ×3    +0x18/+0x1a/+0x1c
    파일 20/24/28 u16  장비 효과 값 ×3      +0x1e/+0x20/+0x22
    파일 30/34/38 u16  때릴 때 상태이상 ×3  +0x24/+0x26/+0x28
    파일 32/36/40 u16  그 값 ×3            +0x2a/+0x2c/+0x2e
    파일 42 u16  **쓸 때 도는 work 번호**   +0x30   0xffff = 못 씀
    파일 44 u16  ?                        +0x32
    파일 46 u16  설명 TXR                  +0x34
  전투 아이템 목록 창 0x100d3830 은 거르개 −1 로 만들어져 **종류(+0x0c) == 7** 만 줄로 만든다(0x100d39e4).
  고르면 창이 0x40f + 아이템 번호를 보내고, 0x10064b1f 가 `[CBattle+0x3ca4] = Itm[+0x30]`,
  `[CBattle+0x3ca6] = 아이템 번호` 로 넣는다 → 어빌리티와 똑같이 상태 11(대상 고르기)로 간다.
  실행이 정해지면 0x10069860 이 `0x1004ded0(파티, 아이템, 1)` 로 **개수를 1 줄인다**.
"""
import argparse
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import skill_motion as sm  # noqa: E402

# (파일 오프셋, 크기코드, 메모리 오프셋, 이름)
ITEM_FIELDS = [
    (0,  'H', 0x00, 'id'),
    (2,  'H', 0x04, 'name_txr'),
    (4,  'I', 0x08, 'price'),
    (8,  'B', 0x0c, 'type'),
    (9,  'H', 0x0e, 'pic'),
    (11, 'H', 0x10, 'atk'),
    (13, 'H', 0x12, 'armor'),
    (15, 'B', 0x14, 'f15'),
    (16, 'H', 0x16, 'range'),
    (18, 'H', 0x18, 'eff0'), (20, 'H', 0x1e, 'val0'),
    (22, 'H', 0x1a, 'eff1'), (24, 'H', 0x20, 'val1'),
    (26, 'H', 0x1c, 'eff2'), (28, 'H', 0x22, 'val2'),
    (30, 'H', 0x24, 'sta0'), (32, 'H', 0x2a, 'staval0'),
    (34, 'H', 0x26, 'sta1'), (36, 'H', 0x2c, 'staval1'),
    (38, 'H', 0x28, 'sta2'), (40, 'H', 0x2e, 'staval2'),
    (42, 'H', 0x30, 'work'),
    (44, 'H', 0x32, 'f44'),
    (46, 'H', 0x34, 'desc_txr'),
]
ITEM_FMT = '<HHIBHHHBHHHHHHHHHHHHHHHH'
ITEM_SIZE = 48

TYPE_NAME = {
    0: 'VES', 1: '머리', 2: '갑옷', 3: '신발', 4: '벨트', 5: '반지', 6: '아뮬렛',
    7: '캡슐(전투 소모)', 8: '리본', 9: '요요', 10: '사진기', 11: '크로', 12: '쌍권총',
    13: '시가', 14: '일반검', 15: '대검', 16: '사용아이템(전투 밖)', 17: '포이즌 포트',
    19: '적용 무기', 255: '없음/구분선',
}


def load_items(g):
    d = g.read('Dat', 'Itm.dat')
    _, n, _ = struct.unpack_from('<3H', d)
    assert struct.calcsize(ITEM_FMT) == ITEM_SIZE, struct.calcsize(ITEM_FMT)
    out, raw = {}, d
    for i in range(n):
        v = struct.unpack_from(ITEM_FMT, d, 6 + i * ITEM_SIZE)
        r = {name: x for (_, _, _, name), x in zip(ITEM_FIELDS, v)}
        r['_slot'] = i
        out[r['id']] = r
    return out, n, len(raw)


def load_text(g):
    from extract_character import parse_txr
    t = {}
    for r in parse_txr(g.read('TXR', 'Txr.dat')):
        t.setdefault(r['txr_id'], r['text'])
    return t


def work_line(w):
    if w is None:
        return '(work 없음)'
    f = lambda o: w.get(o)
    return ('종류 %s 위력 %s 대상 %s 범위모양 %s 범위 %s/%s 사거리 %s/%s(최소 %s 최대 %s) '
            'TP %s SOUL %s/%s 효과 %s' % (
                f(0x1f), f(0x2a), f(0x13), f(0x14), f(0x1a), f(0x1c), f(0x7), f(0x8), f(0xa), f(0xc),
                f(0x32), f(0x34), f(0x36),
                ','.join('%d:%d' % (f(0x20 + i), f(0x24 + 2 * i)) for i in range(3) if f(0x20 + i))))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('--all', action='store_true')
    ap.add_argument('--type', type=int, default=None)
    ap.add_argument('--check', action='store_true')
    a = ap.parse_args()

    g = sm.Game(a.game)
    items, n, size = load_items(g)
    txr = load_text(g)
    works = sm.load_works(g)

    if a.check:
        print('Itm.dat 파일 %d바이트, 개수 %d, 6 + %d×48 = %d' % (size, n, n, 6 + n * ITEM_SIZE))
        import collections
        c = collections.Counter(r['type'] for r in items.values())
        for t in sorted(c):
            usable = sum(1 for r in items.values() if r['type'] == t and r['work'] != 0xffff)
            print('  종류 %3d %-20s %3d개 (work 있는 것 %d)' % (t, TYPE_NAME.get(t, '?'), c[t], usable))
        bad = [r for r in items.values() if r['type'] != 7 and r['work'] != 0xffff]
        print('  종류 7 이 아닌데 work 가 있는 것: %d개 → %s' % (
            len(bad), ', '.join('%d %s(종류 %d, work %d)' % (r['id'], txr.get(r['name_txr'], '?'), r['type'], r['work'])
                                for r in bad[:20])))
        print('  파일 44(+0x32) 값 분포: %s' % dict(collections.Counter(r['f44'] for r in items.values())))
        print('  파일 15(+0x14) 값 분포: %s' % dict(collections.Counter(r['f15'] for r in items.values())))
        return

    sel = [r for r in items.values()
           if a.all or (r['type'] == a.type if a.type is not None else r['type'] == 7)]
    sel.sort(key=lambda r: r['id'])
    print('| 번호 | 이름 | 종류 | 가격 | 그림 | work | f44 | 효과(work) | 설명 |')
    print('|---|---|---|---|---|---|---|---|---|')
    for r in sel:
        w = works.get(r['work']) if r['work'] != 0xffff else None
        print('| %d | %s | %d %s | %d | %s | %s | %d | %s | %s |' % (
            r['id'], txr.get(r['name_txr'], '?'), r['type'], TYPE_NAME.get(r['type'], '?'), r['price'],
            '-' if r['pic'] == 0xffff else r['pic'],
            '-' if r['work'] == 0xffff else r['work'], r['f44'],
            work_line(w), txr.get(r['desc_txr'], '').replace('\n', ' ')))


if __name__ == '__main__':
    main()

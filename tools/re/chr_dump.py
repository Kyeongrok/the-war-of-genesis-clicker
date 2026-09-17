"""
창세기전3 파트2 — Chr/*.chr(90바이트)을 G3PartII.dll CChr.Load(VA 0x10031530) 읽는 순서대로 풀어 표로 뽑는다.

사용법:
    python chr_dump.py <게임 폴더> <풀어 둔 Chr 폴더>
    (Chr 폴더는 pak_extract.py <게임 폴더> <출력> Chr 로 만든다)

파일 바이트 배치(오프셋 → CChr 구조체 필드):
    0  u16 (안 씀)          2  u16 +0x06 이름 txr       4  u16 +0x08 이름2 txr
    6  u16 +0x0a            8  u16 +0x0c sprite(Obs)    10 u16 +0x0e face(Obs)
    12 u16 +0x10 txr(직업/칭호로 보임)                  14 u8  +0x12
    15 u16 +0x16 → +0x14 = Dep.dat 에서 이 값을 품은 레코드 번호(0x10032df0)
    17 u8  +0x18            18 u8  +0x19                19 u16 +0x1a
    21 u16 +0x2c            23 u32 +0x34                27 u16×6 +0x3c..+0x46
    39 u8  +0x4a            40 u16 +0x48                42 u16×6 +0x4c 아이템 번호(없는 아이템이면 경고)
    54 u16 +0x58            56 (u16 번호 <200, u16 값)×8 → +0x7a[번호] = 값
    88 u16 (나머지)
"""
import collections
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
import extract_character as e


def parse(b):
    o = 0

    def u8():
        nonlocal o
        v = b[o]; o += 1; return v

    def u16():
        nonlocal o
        v = struct.unpack_from('<H', b, o)[0]; o += 2; return v

    def u32():
        nonlocal o
        v = struct.unpack_from('<I', b, o)[0]; o += 4; return v

    r = {'f00': u16()}
    r['name'], r['name2'], r['f0a'], r['sprite'], r['face'], r['title'] = [u16() for _ in range(6)]
    r['f12'] = u8(); r['f16'] = u16(); r['f18'] = u8(); r['f19'] = u8()
    r['f1a'] = u16(); r['f2c'] = u16(); r['f34'] = u32()
    r['stats'] = [u16() for _ in range(6)]
    r['f4a'] = u8(); r['f48'] = u16()
    r['items'] = [u16() for _ in range(6)]
    r['f58'] = u16()
    r['abis'] = [(u16(), u16()) for _ in range(8)]
    r['tail'] = b[o:].hex()
    return r


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    game_root, chr_dir = sys.argv[1], sys.argv[2]
    rows = e.parse_txr(open(os.path.join(game_root, 'TXR', 'Txr.dat'), 'rb').read())
    text = {r['txr_id']: r['text'] for r in rows}

    def t(code):
        return f'{code}:{text[code][:10]}' if code in text else str(code)

    out = []
    for f in sorted(os.listdir(chr_dir)):
        b = open(os.path.join(chr_dir, f), 'rb').read()
        if len(b) == 90:
            out.append((f, parse(b)))

    for c in ['f0a', 'f12', 'f16', 'f18', 'f19', 'f1a', 'f2c', 'f34', 'f4a', 'f48', 'f58']:
        print(c, collections.Counter(r[c] for _, r in out).most_common(12))
    print()
    for f, r in out:
        abis = ' '.join(f'{a}/{l}' for a, l in r['abis'] if a)
        print(f[:4], t(r['name']), t(r['name2']), '|', t(r['title']), '| f0a', r['f0a'], 'f12', r['f12'], 'f16', r['f16'],
              'f18', r['f18'], 'f19', r['f19'], 'f1a', r['f1a'], 'f2c', r['f2c'], 'f34', r['f34'], '| st', r['stats'],
              '| f4a', r['f4a'], 'f48', r['f48'], '| it', r['items'], '| f58', r['f58'], '| ab', abis, '|', r['tail'])


if __name__ == '__main__':
    main()

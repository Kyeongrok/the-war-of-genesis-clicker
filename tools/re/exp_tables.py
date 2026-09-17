"""
창세기전3 파트2 — 경험치/레벨업에 쓰이는 표(Num.dat, Lev.dat, Job.dat 성장률)를 G3PartII.dll 로더 순서대로 푼다.

사용법:
    python exp_tables.py <게임 폴더> <풀어 둔 Dat 폴더>
    (Dat 폴더는 pak_extract.py <게임 폴더> <출력> Dat 로 만든다)

공통 머리(세 파일 모두): u16 (안 씀), u16 레코드 수 n, u16 최대 번호(표 크기 = 최대 번호+1)
그 뒤 n개 레코드, 끝에 2바이트(체크섬으로 보임).

Num.dat (LoadValueData, 표 0x101b6048, 조회 함수 0x1004a7c0(번호)):
    레코드 6바이트 = u16 번호, u32 값. 파일에 없는 번호는 100001.
    경험치 관련 번호: 14(기본 exp) 15(레벨 차 1당 exp) 16(최소) 17(최대)
                     30(처치 시 +0x4d6 게이지 증가) 86/87/88(0x101b5de0 모드 식)

Lev.dat (LoadForData, 표 0x101b6838, 16바이트 메모리 레코드):
    레코드 14바이트 = u16 번호, u16 레벨(+4), u16×5 퍼센트(+6 +8 +a +c +e)
    0x1007a8e0(레벨) 이 레벨로 레코드를 찾아(0x1004d490) 유닛 능력치 = 기본 + 기본*퍼센트/100.

Job.dat (LoadJobData, 표 0x101b6874, 72바이트 메모리 레코드):
    레코드 67바이트 = u16 번호, u16 +4, u16 +6, u8 +8, u16×12 +0x0a..+0x20,
                      u16×11 +0x22.., u16×6 +0x38..(0 아닌 값 = 이름 txr, 그룹 11812로 보임),
                      u16 +0x44 (설명 txr, 그룹 45968)
    레벨업(CChr 0x100318d0) 성장률(%) : +0x16 LP, +0x18 TP, +0x1c PSY, +0x1e DEP, +0x20 DEX
"""
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))


def head(d):
    return struct.unpack_from('<3H', d, 0)


def parse_num(d):
    _, n, mx = head(d)
    vals = {}
    o = 6
    for _ in range(n):
        i, v = struct.unpack_from('<Hi', d, o); o += 6
        vals[i] = v
    return vals, o


def parse_lev(d):
    _, n, mx = head(d)
    rows = []
    o = 6
    for _ in range(n):
        rows.append(struct.unpack_from('<7H', d, o)); o += 14
    return rows, o


def parse_job(d):
    _, n, mx = head(d)
    rows = []
    o = 6
    for _ in range(n):
        idx, f4, f6 = struct.unpack_from('<3H', d, o); o += 6
        f8 = d[o]; o += 1
        a = struct.unpack_from('<12H', d, o); o += 24   # +0x0a..+0x20
        b = struct.unpack_from('<11H', d, o); o += 22   # +0x22..
        c = struct.unpack_from('<6H', d, o); o += 12    # +0x38..
        f44 = struct.unpack_from('<H', d, o)[0]; o += 2
        f = {0x0a + 2 * k: v for k, v in enumerate(a)}
        rows.append({'idx': idx, 'f4': f4, 'f6': f6, 'f8': f8, 'f': f, 'b': b, 'c': c, 'f44': f44})
    return rows, o


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    game_root, dat = sys.argv[1], sys.argv[2]
    lookup = None
    try:
        import extract_character as e
        rows = e.parse_txr(open(os.path.join(game_root, 'TXR', 'Txr.dat'), 'rb').read())
        lookup = e.build_lookup(rows)
    except Exception:
        pass

    sys.stdout.reconfigure(encoding='utf-8')
    d = open(os.path.join(dat, 'Num.dat'), 'rb').read()
    num, end = parse_num(d)
    print(f'# Num.dat  head={head(d)} end={end}/{len(d)}')
    print('  ' + ' '.join(f'{k}={v}' for k, v in sorted(num.items())))

    d = open(os.path.join(dat, 'Lev.dat'), 'rb').read()
    lev, end = parse_lev(d)
    print(f'# Lev.dat  head={head(d)} end={end}/{len(d)}   (번호, 레벨, +6, +8, +a, +c, +e)')
    prev = None
    for r in lev:
        if prev is None or r[2:] != prev[2:] and (r[1] % 10 == 0 or r[1] <= 5):
            print('  ', r)
        prev = r
    print('  ', lev[-1])

    d = open(os.path.join(dat, 'Job.dat'), 'rb').read()
    jobs, end = parse_job(d)
    sys.stdout.reconfigure(encoding='utf-8')
    print(f'# Job.dat  head={head(d)} end={end}/{len(d)}')
    print('  번호  +4         LP%(+16) TP%(+18) PSY%(+1c) DEP%(+1e) DEX%(+20)  이름')
    for j in jobs:
        f = j['f']
        nid = next((v for v in j['c'] if v), 0)
        name = lookup(nid, (11812,)) if lookup and nid else ''
        print(f"  {j['idx']:4d} {j['f4']:6d}   {f[0x16]:5d} {f[0x18]:5d} {f[0x1c]:5d} {f[0x1e]:5d} {f[0x20]:5d}  {name}")


if __name__ == '__main__':
    main()

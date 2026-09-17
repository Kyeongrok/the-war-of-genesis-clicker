"""
창세기전3 파트2 — 게임 폴더의 .idx/.pak 묶음과 낱장 파일을 한 폴더로 풀어 놓는다.

사용법:
    python pak_extract.py <게임 폴더> <출력 폴더> Chp Btl Chr Dat ...

게임 폴더는 건드리지 않는다. 출력 폴더/<폴더명>/ 에 낱장 파일을 먼저 복사하고,
.idx 에 적힌 파일 중 아직 없는 것만 .pak 에서 잘라 쓴다(낱장이 pak 보다 우선).
.idx 레코드(36바이트): u16, u32 시작, u16, u32 끝, u16, 파일명 9바이트·pak 이름 13바이트(둘 다 XOR 0xFF).
"""
import os
import shutil
import struct
import sys


def extract(game_root, out_root, folder):
    src = os.path.join(game_root, folder)
    dst = os.path.join(out_root, folder)
    os.makedirs(dst, exist_ok=True)
    for f in os.listdir(src):
        p = os.path.join(src, f)
        if os.path.isfile(p) and not f.lower().endswith(('.idx', '.pak')):
            shutil.copy(p, dst)
    for idx in [f for f in os.listdir(src) if f.lower().endswith('.idx')]:
        data = open(os.path.join(src, idx), 'rb').read()
        total = struct.unpack_from('<I', data, 0)[0] // 0x10000
        off, cache = 6, {}
        for _ in range(total):
            rec = data[off:off + 36]
            off += 36
            name = bytes(b ^ 0xFF for b in rec[14:23]).split(b'\0')[0].decode('cp949', 'replace')
            pak = bytes(b ^ 0xFF for b in rec[23:36]).split(b'\0')[0].decode('cp949', 'replace')
            _, start, _, end, _ = struct.unpack_from('<HIHIH', rec, 0)
            out = os.path.join(dst, name)
            if os.path.exists(out):
                continue
            if pak not in cache:
                cache[pak] = open(os.path.join(src, pak), 'rb').read()
            open(out, 'wb').write(cache[pak][start:end + 1])
    print(folder, len(os.listdir(dst)))


if __name__ == '__main__':
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(1)
    for folder in sys.argv[3:]:
        extract(sys.argv[1], sys.argv[2], folder)

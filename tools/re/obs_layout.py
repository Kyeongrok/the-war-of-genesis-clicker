"""
창세기전3 파트2 — Obs 파일 배치를 DLL 로더(`LoadG4ObsFile` 0x1002f050) 그대로 읽어 확인하는 도구.

`tools/extract_character.py` 의 옛 파서는 몸짓벌 머리의 세 번째 값을 "장 수 - 1" 로 보고
`n3 + 1 == 장수` 일 때만 머리로 인정했다. 실제로 그 값은 **가장 큰 장(slot) 번호**다
(0x1002f26a `inc WORD PTR [edi]` 로 +1 해서 배열 칸 수로 쓴다). 장 번호에 구멍이 있는
UI 그림(`Obs/0471.obs` — O.K 단추·목록 줄 바탕 따위)은 그래서 통째로 안 읽혔다.

사용법:
    python obs_layout.py <게임 폴더> <출력 폴더> [Obs번호 ...] [--motions 20,31,32]

    python tools/re/obs_layout.py "C:/Users/ocean/Desktop/창세기전3 파트2" out 471 --motions 20,31,32

게임 폴더는 읽기만 한다(Obs.idx/.pak 을 그때그때 잘라 읽는다). 출력 폴더에
    <NNNN>/sub<벌>_slot<장번호>.png   — 장 한 장
    <NNNN>/motion<번호>.png           — 그 모션이 쓰는 컷을 시작틱 순서로 이어 붙인 띠
를 쓰고, 표준출력에 머리 값 표를 찍는다.

파일 배치(모두 little-endian, 오프셋은 파일 처음부터):
    머리          u16 ?(늘 0) | u16 벌수 | u16 최대벌번호 | (u16 벌번호, u32 오프셋) × 벌수
    모션표        u16 모션수 | u16 최대모션번호 | (u16 모션번호, u32 오프셋) × 모션수
    벌(subentry)  u16 벌번호 | u16 장수 | u16 최대장번호 | u8 투명색인 | u8[768] 팔레트(RGB)
                  | (u16 장번호, u32 오프셋) × 장수
    장(slot)      u16 장번호 | u32 자료크기 | u16 가로 | u16 세로 | s16 x | s16 y | u8[자료크기]
"""
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
from extract_character import decode_obs_payload, indexed_to_rgba  # noqa: E402


def u8(d, o):
    return d[o]


def u16(d, o):
    return struct.unpack_from('<H', d, o)[0]


def s16(d, o):
    return struct.unpack_from('<h', d, o)[0]


def u32(d, o):
    return struct.unpack_from('<I', d, o)[0]


# ---------------------------------------------------------------- 파일 읽기

def read_obs(game_root, number):
    """게임 폴더에서 Obs/NNNN.obs 를 읽는다 — 낱장이 없으면 .idx/.pak 에서 잘라 온다."""
    src = os.path.join(game_root, 'Obs')
    name = '%04d.obs' % number
    plain = os.path.join(src, name)
    if os.path.exists(plain):
        return open(plain, 'rb').read()
    for idx in [f for f in os.listdir(src) if f.lower().endswith('.idx')]:
        data = open(os.path.join(src, idx), 'rb').read()
        total = u32(data, 0) // 0x10000
        off = 6
        for _ in range(total):
            rec = data[off:off + 36]
            off += 36
            nm = bytes(b ^ 0xFF for b in rec[14:23]).split(b'\0')[0].decode('cp949', 'replace')
            if nm.lower() != name:
                continue
            pak = bytes(b ^ 0xFF for b in rec[23:36]).split(b'\0')[0].decode('cp949', 'replace')
            _, start, _, end, _ = struct.unpack_from('<HIHIH', rec, 0)
            with open(os.path.join(src, pak), 'rb') as f:
                f.seek(start)
                return f.read(end - start + 1)
    raise FileNotFoundError(name)


# ---------------------------------------------------------------- 배치 풀기

def parse_header(d):
    """머리 — 벌 색인표와 모션 색인표."""
    nsub, maxsub = u16(d, 2), u16(d, 4)
    subs = [(u16(d, 6 + 6 * i), u32(d, 8 + 6 * i)) for i in range(nsub)]
    o = 6 + 6 * nsub
    nmot, maxmot = u16(d, o), u16(d, o + 2)
    mots = [(u16(d, o + 4 + 6 * i), u32(d, o + 6 + 6 * i)) for i in range(nmot)]
    return {'nsub': nsub, 'maxsub': maxsub, 'subs': subs,
            'nmot': nmot, 'maxmot': maxmot, 'mots': mots}


def parse_subentry(d, off, want_id):
    """벌 머리 하나. 세 번째 값은 '장 수 - 1' 이 아니라 **가장 큰 장 번호**다."""
    sid, nslot, maxslot = u16(d, off), u16(d, off + 2), u16(d, off + 4)
    if sid != want_id or not 1 <= nslot <= 256 or maxslot + 1 < nslot or maxslot > 255:
        return None
    table = off + 7 + 768
    slots = []
    for i in range(nslot):
        if table + 6 * i + 6 > len(d):
            return None
        slot_id, so = u16(d, table + 6 * i), u32(d, table + 6 * i + 2)
        if so + 14 > len(d):
            return None
        size, w, h = u32(d, so + 2), u16(d, so + 6), u16(d, so + 8)
        if not 0 < w <= 2048 or not 0 < h <= 2048 or size > len(d) - so - 14:
            return None
        slots.append({'id': slot_id, 'off': so, 'size': size, 'w': w, 'h': h,
                      'x': s16(d, so + 10), 'y': s16(d, so + 12)})
    return {'id': sid, 'nslot': nslot, 'maxslot': maxslot, 'off': off,
            'transparent': u8(d, off + 6), 'palette': d[off + 7:off + 7 + 768], 'slots': slots}


def parse_all(d):
    """벌을 죄다 읽는다 — 색인표 값은 조금 어긋나 있어서 '앞 벌이 끝난 자리' 를 먼저 짚는다."""
    head = parse_header(d)
    out, nxt = [], head['subs'][0][1] if head['subs'] else 0
    for sid, listed in head['subs']:
        sub = None
        for base in (nxt, listed):
            for skip in range(0, 97):
                sub = parse_subentry(d, base + skip, sid)
                if sub:
                    break
            if sub:
                break
        if not sub:
            nxt = listed
            continue
        out.append(sub)
        last = sub['slots'][-1]
        nxt = last['off'] + 14 + last['size']
    return head, out


def parse_motions(d):
    head = parse_header(d)
    res = {}
    for mid, off in head['mots']:
        p = off
        _, na, nb, length, nu = struct.unpack_from('<5H', d, p)
        uses = [struct.unpack_from('<2H', d, p + 10 + 4 * k) for k in range(nu)]
        p += 10 + 4 * nu
        keys = []
        for k in range(na + nb):
            f = struct.unpack_from('<3H10h', d, p)
            p += 26
            keys.append({'list': 'A' if k < na else 'B', 'kind': f[0],
                         'start': f[1], 'len': f[2], 'p': f[3:]})
        res[mid] = {'len': length, 'uses': uses, 'keys': keys}
    return res


# ---------------------------------------------------------------- 그림

def decode(d, sub, slot):
    idx = decode_obs_payload(d[slot['off'] + 14:slot['off'] + 14 + slot['size']],
                             slot['w'], slot['h'], 0x280, sub['transparent'])
    return indexed_to_rgba(idx, slot['w'], slot['h'], sub['palette'], sub['transparent'])


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return
    game, out_root = sys.argv[1], sys.argv[2]
    nums, want_mot = [], None
    args = sys.argv[3:]
    if '--motions' in args:
        i = args.index('--motions')
        want_mot = {int(x) for x in args[i + 1].split(',')}
        args = args[:i] + args[i + 2:]
    nums = [int(a) for a in args] or [471]

    from PIL import Image
    for n in nums:
        d = read_obs(game, n)
        head, subs = parse_all(d)
        out = os.path.join(out_root, '%04d' % n)
        os.makedirs(out, exist_ok=True)
        print('== Obs %04d (%d 바이트) 벌수 %d 최대벌번호 %d · 모션 %d 개'
              % (n, len(d), head['nsub'], head['maxsub'], head['nmot']))
        images = {}
        for (sid, listed), sub in zip(head['subs'], subs):
            print('  벌 %d: 색인표 %d → 실제 %d (%+d) · 장 %d 개 · 최대장번호 %d · 투명 %d'
                  % (sub['id'], listed, sub['off'], sub['off'] - listed,
                     sub['nslot'], sub['maxslot'], sub['transparent']))
            for s in sub['slots']:
                try:
                    img = decode(d, sub, s)
                except Exception as e:
                    print('    장 %d 못 품: %s' % (s['id'], e))
                    continue
                images[(sub['id'], s['id'])] = (s, img)
                img.save(os.path.join(out, 'sub%02d_slot%03d.png' % (sub['id'], s['id'])))
            print('    장 번호: ' + ', '.join(str(s['id']) for s in sub['slots']))
        try:
            motions = parse_motions(d)
        except Exception as e:
            print('  모션표 없음:', e)
            continue
        for mid, m in sorted(motions.items()):
            if want_mot is not None and mid not in want_mot:
                continue
            print('  모션 %d: 길이 %d 틱, 쓰는 벌/장 %s' % (mid, m['len'], m['uses']))
            for k in m['keys']:
                print('    %s 종류 %d 시작 %d 길이 %d 인자 %s'
                      % (k['list'], k['kind'], k['start'], k['len'], list(k['p'])))
            fr = [k for k in m['keys'] if k['kind'] == 0 and (k['p'][0], k['p'][1]) in images]
            if not fr:
                continue
            fr.sort(key=lambda k: k['start'])
            boxes = [images[(k['p'][0], k['p'][1])][0] for k in fr]
            x0 = min([b['x'] for b in boxes] + [0])
            y0 = min([b['y'] for b in boxes] + [0])
            w = max(b['x'] + b['w'] for b in boxes) - x0
            h = max(b['y'] + b['h'] for b in boxes) - y0
            sheet = Image.new('RGBA', ((w + 4) * len(fr), h), (40, 40, 40, 255))
            for j, k in enumerate(fr):
                b, img = images[(k['p'][0], k['p'][1])]
                sheet.alpha_composite(img, (j * (w + 4) + b['x'] - x0, b['y'] - y0))
            sheet.save(os.path.join(out, 'motion%d.png' % mid))


if __name__ == '__main__':
    main()

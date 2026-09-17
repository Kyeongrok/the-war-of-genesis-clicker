"""
창세기전3 파트2 — UI용 Obs(링 커맨드 아이콘 같은 것)의 모션표를 글로 찍고, 모션마다 틱별 그림을 PNG 로 푼다.

사용법:
    python obs_ui_dump.py <Obs 폴더(풀어 둔 것)> <출력 폴더> 92 87 105 ...

출력:
    <출력>/<NNNN>/sub<벌>_slot<장>.png          — 장 그림 하나(투명 포함)
    <출력>/<NNNN>/motion<번호>.png               — 모션의 그림 키를 시작틱 순서로 가로로 이어 붙인 띠
                                                  (칸마다 기준점(0,0)을 가운데 두고 장의 x,y 자리대로 그림)
    표준출력                                      — 모션마다 길이·키(목록 A/B, 종류, 시작틱, 길이, 인자 10개)

Obs 배치는 WarOfGenesis.Assets/ObsSprite.cs·ObsMotionTable.cs, tools/extract_character.py 와 같다.
UI 에서 이 Obs 를 쓰는 곳: 애니 객체 0x10025b80(Obs 번호, 모션 번호) → SetObs 0x10026ed0 → 0x100301b0.
"""
import os
import struct
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))
from extract_character import (parse_obs_structure, parse_subentry, decode_obs_slot,  # noqa: E402
                               indexed_to_rgba, u32)
from PIL import Image  # noqa: E402


def load_motions(d):
    _, n, _ = struct.unpack_from('<3H', d)
    o = 6 + 6 * n
    m, _ = struct.unpack_from('<2H', d, o)
    o += 4
    res = {}
    for i in range(m):
        mid, off = struct.unpack_from('<HI', d, o + 6 * i)
        p = off
        _, na, nb, length, nu = struct.unpack_from('<5H', d, p)
        uses = [struct.unpack_from('<2H', d, p + 10 + 4 * k) for k in range(nu)]
        p += 10 + 4 * nu
        keys = []
        for k in range(na + nb):
            f = struct.unpack_from('<3H10h', d, p)
            p += 26
            keys.append({'list': 'A' if k < na else 'B', 'kind': f[0], 'start': f[1], 'len': f[2], 'p': f[3:]})
        res[mid] = {'len': length, 'uses': uses, 'keys': keys}
    return res


def load_slots(d):
    st = parse_obs_structure(d)
    out = {}
    nxt = st['subentries'][0]['offset']
    for ref in st['subentries']:
        sub = None
        for off in (nxt, ref['offset']):
            try:
                sub = parse_subentry(d, off, ref['id'])
                break
            except Exception:
                pass
        if sub is None:
            nxt = ref['offset']
            continue
        if sub['slots']:
            last = sub['slots'][-1]['offset']
            nxt = last + 14 + u32(d, last + 2)
        for i, s in enumerate(sub['slots']):
            try:
                dec = decode_obs_slot(d, s['offset'], sub['transparent_index'])
            except Exception as e:
                print(f'  slot error sub={sub["id"]} idx={i}: {e}')
                continue
            img = indexed_to_rgba(dec['indexed'], dec['width'], dec['height'], sub['palette'], sub['transparent_index'])
            out[(sub['id'], i)] = (dec, img)
    return out


def main():
    obs_dir, out_root = sys.argv[1], sys.argv[2]
    for n in [int(a) for a in sys.argv[3:]]:
        path = os.path.join(obs_dir, '%04d.obs' % n)
        d = open(path, 'rb').read()
        out = os.path.join(out_root, '%04d' % n)
        os.makedirs(out, exist_ok=True)
        slots = load_slots(d)
        print(f'== Obs {n:04d} ({len(d)} bytes), 장 {len(slots)}')
        for (sid, i), (dec, img) in sorted(slots.items()):
            print(f'  벌 {sid} 장 {i}: id {dec["slot_id"]} {dec["width"]}x{dec["height"]} 자리 ({dec["x"]},{dec["y"]})')
            img.save(os.path.join(out, f'sub{sid:02d}_slot{i:03d}.png'))
        try:
            motions = load_motions(d)
        except Exception as e:
            print('  모션표 없음:', e)
            continue
        for mid, m in sorted(motions.items()):
            print(f'  모션 {mid}: 길이 {m["len"]} 틱, 쓰는 장 {m["uses"]}')
            for k in m['keys']:
                print(f'    {k["list"]} 종류 {k["kind"]} 시작 {k["start"]} 길이 {k["len"]} 인자 {list(k["p"])}')
            frames = [k for k in m['keys'] if k['list'] == 'B' and k['kind'] == 0 and (k['p'][0], k['p'][1]) in slots]
            if not frames:
                continue
            frames.sort(key=lambda k: k['start'])
            # 기준점 둘레 크기 잡기
            xs, ys, xe, ye = [], [], [], []
            for k in frames:
                dec = slots[(k['p'][0], k['p'][1])][0]
                xs.append(dec['x']); ys.append(dec['y'])
                xe.append(dec['x'] + dec['width']); ye.append(dec['y'] + dec['height'])
            x0, y0 = min(xs + [0]), min(ys + [0])
            w, h = max(xe + [1]) - x0, max(ye + [1]) - y0
            sheet = Image.new('RGBA', ((w + 4) * len(frames), h), (40, 40, 40, 255))
            for j, k in enumerate(frames):
                dec, img = slots[(k['p'][0], k['p'][1])]
                sheet.alpha_composite(img, (j * (w + 4) + dec['x'] - x0, dec['y'] - y0))
            sheet.save(os.path.join(out, f'motion{mid}.png'))


if __name__ == '__main__':
    main()

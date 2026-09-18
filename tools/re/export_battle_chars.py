"""전투 하나에 서는 인물을 `assets/characters/` 로 뽑는다.

    python export_battle_chars.py <스크래치의 g 폴더> <전투번호> [<전투번호> ...]

`assets/characters/<코드>_<이름>/` 밑에 `.chr` 과 그 인물의 그림·얼굴 `.obs`, 그리고
duel-dx 가 읽는 `character.json` 을 놓는다. 게임 폴더는 안 건드린다 — pak_extract.py 로
미리 풀어 둔 `g/` 에서만 베낀다.

이름은 `TXR/Txr.dat` 에서 가져온다(extract_character.py 의 parse_txr 과 같은 표).
"""
import json
import os
import shutil
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..'))
from extract_character import parse_txr  # noqa: E402

REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
G = sys.argv[1]
BATTLES = [int(a) for a in sys.argv[2:]]


def chr_codes_of(bid):
    """Btl 머리 10낱말 → 인물 수 → 29바이트 레코드(+2 가 Chr 코드)."""
    path = os.path.join(G, 'Btl', '%04d.btl' % bid)
    if not os.path.exists(path):
        return []
    b = open(path, 'rb').read()
    count = struct.unpack_from('<H', b, 20)[0]
    out, o = [], 24
    for _ in range(count):
        if o + 29 > len(b):
            break
        out.append(struct.unpack_from('<H', b, o + 2)[0])
        o += 29
    return out


def main():
    names = {}
    txr = os.path.join(G, 'TXR', 'Txr.dat')
    if os.path.exists(txr):
        for row in parse_txr(open(txr, 'rb').read()):
            names[row['txr_id']] = row['text']

    made = []
    for bid in BATTLES:
        for code in sorted(set(chr_codes_of(bid))):
            src = os.path.join(G, 'Chr', '%04d.chr' % code)
            if code <= 0 or not os.path.exists(src):
                continue
            cb = open(src, 'rb').read()
            name_code = struct.unpack_from('<H', cb, 2)[0]
            sprite = struct.unpack_from('<H', cb, 8)[0]
            face = struct.unpack_from('<H', cb, 10)[0]
            name = names.get(name_code, '') or ('인물%04d' % code)
            folder = os.path.join(REPO, 'assets', 'characters', '%04d_%s' % (code, name))
            if os.path.isdir(folder):
                continue
            os.makedirs(folder, exist_ok=True)
            shutil.copy(src, os.path.join(folder, '%04d.chr' % code))
            for obs in (sprite, face):
                obs_src = os.path.join(G, 'Obs', '%04d.obs' % obs)
                if obs > 0 and os.path.exists(obs_src):
                    shutil.copy(obs_src, os.path.join(folder, '%04d.obs' % obs))
            with open(os.path.join(folder, 'character.json'), 'w', encoding='utf-8') as f:
                json.dump({'Name': name, 'ChrCode': code, 'SpriteCode': sprite, 'FaceCode': face},
                          f, ensure_ascii=False, indent=2)
            made.append(os.path.basename(folder))
    print('뽑음 %d: %s' % (len(made), ', '.join(made)))


main()

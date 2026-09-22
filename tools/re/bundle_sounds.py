"""assets/characters 에 묶인 인물이 내는 소리(몸짓 소리 키, 무기 층·이펙트 Obs 의 소리 키, Dmg.dat 목소리)를 찾아
assets/sounds 에 빠진 것을 게임의 Snd 폴더(.snd = 그냥 WAV)에서 <번호>.wav 로 복사한다.

사용법:
    python tools/re/bundle_sounds.py <풀어 둔 Snd 폴더 또는 게임 폴더>

근거: 분석-사운드 — 공격 소리는 몸 Obs 가 아니라 무기 층 Obs 의 소리 키에 있고(제이슨 368 → 426), 목소리는
Dmg.dat 레코드(.chr +6 묶음 번호 → 맞는 목소리 2·부르는 목소리 4)에서 온다.
"""
import glob
import os
import shutil
import struct
import sys

sys.path.insert(0, os.path.dirname(__file__))
from obs_ui_dump import load_motions  # noqa: E402

ROOT = os.path.join(os.path.dirname(__file__), '..', '..')


def sound_ids_of(obs_path):
    ids = set(); kids = set()
    try:
        for m in load_motions(open(obs_path, 'rb').read()).values():
            for k in m.get('keys') or []:
                if k.get('kind') == 1 and k.get('p'): ids.add(int(k['p'][0]))
                if k.get('kind') == 2 and k.get('p'): kids.add(int(k['p'][0]))
    except Exception as ex:  # noqa: BLE001
        print('못 읽음:', obs_path, ex)
    return ids, kids


def main():
    src = sys.argv[1]
    if os.path.isdir(os.path.join(src, 'Snd')):
        src = os.path.join(src, 'Snd')
    have = {int(os.path.basename(p)[:4]) for p in glob.glob(os.path.join(ROOT, 'assets', 'sounds', '*.wav'))}
    effect_dirs = [os.path.join(ROOT, 'assets', f) for f in ('effects', 'ui', os.path.join('moses', 'obs'))]
    dmg = open(os.path.join(ROOT, 'assets', 'data', 'Dat', 'Dmg.dat'), 'rb').read()
    voices = {}
    for o in range(6, len(dmg) - 13, 14):
        voices[struct.unpack_from('<H', dmg, o)[0]] = [struct.unpack_from('<H', dmg, o + 2 + 2 * i)[0] for i in range(6)]
    wanted = set()
    for folder in sorted(glob.glob(os.path.join(ROOT, 'assets', 'characters', '*'))):
        chrs = glob.glob(os.path.join(folder, '*.chr'))
        if chrs:
            d = open(chrs[0], 'rb').read()
            for v in voices.get(struct.unpack_from('<H', d, 6)[0], []):
                if 0 < v < 0xFFFF: wanted.add(v)
        for obs in glob.glob(os.path.join(folder, '*.obs')):
            ids, kids = sound_ids_of(obs)
            wanted |= ids
            for kid in kids:
                for ed in effect_dirs:
                    p = os.path.join(ed, '%04d.obs' % kid)
                    if os.path.exists(p):
                        wanted |= sound_ids_of(p)[0]
                        break
    missing = sorted(w for w in wanted if w not in have)
    copied = 0
    for w in missing:
        p = os.path.join(src, '%04d.snd' % w)
        if not os.path.exists(p):
            print('게임에도 없음:', w); continue
        shutil.copy(p, os.path.join(ROOT, 'assets', 'sounds', '%04d.wav' % w)); copied += 1
    print('필요한 소리', len(wanted), '개 · 빠진 것', len(missing), '개 · 복사', copied, '개')


if __name__ == '__main__':
    main()

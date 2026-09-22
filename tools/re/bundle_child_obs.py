"""assets/characters 에 묶인 인물 그림이 자식 키(종류 2)로 부르는 Obs(무기 층·검기·이펙트)를 찾아
assets/effects 에 빠진 것을 게임 폴더(또는 pak_extract 로 풀어 둔 폴더)에서 복사한다.

사용법:
    python tools/re/bundle_child_obs.py <풀어 둔 Obs 폴더 또는 게임 폴더>

근거: 분석-모션 「무기 층을 쓰는 몸 sprite」 — 잡병·용병의 무기는 장비가 아니라 몸 Obs 의 자식 키가 부르는
별도 Obs 라(코어헌터 sprite 18 → Obs 0060), 그 파일이 없으면 맨손으로 서 있다.
"""
import collections
import glob
import os
import shutil
import sys

sys.path.insert(0, os.path.dirname(__file__))
from obs_ui_dump import load_motions  # noqa: E402

ROOT = os.path.join(os.path.dirname(__file__), '..', '..')


def main():
    src_root = sys.argv[1]
    if os.path.isdir(os.path.join(src_root, 'Obs')):
        src_root = os.path.join(src_root, 'Obs')
    have = {os.path.basename(p) for f in ('effects', 'ui', os.path.join('moses', 'obs'))
            for p in glob.glob(os.path.join(ROOT, 'assets', f, '*.obs'))}
    missing = collections.defaultdict(set)
    for folder in sorted(glob.glob(os.path.join(ROOT, 'assets', 'characters', '*'))):
        for obs in glob.glob(os.path.join(folder, '*.obs')):
            for m in load_motions(open(obs, 'rb').read()).values():
                for k in m.get('keys') or []:
                    if k.get('kind') == 2 and k.get('p'):
                        name = '%04d.obs' % int(k['p'][0])
                        if name not in have:
                            missing[name].add(os.path.basename(folder))
    for name, who in sorted(missing.items()):
        src = os.path.join(src_root, name)
        if not os.path.exists(src):
            print('없음(게임에도):', name, sorted(who)[:3])
            continue
        shutil.copy(src, os.path.join(ROOT, 'assets', 'effects', name))
        print('복사:', name, '←', sorted(who)[:3])
    print('빠진 자식 Obs', len(missing), '개')


if __name__ == '__main__':
    main()

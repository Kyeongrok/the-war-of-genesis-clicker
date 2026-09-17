"""Obs 모션표의 소리 키(종류 1)를 모아 보인다 — 자식 모션(종류 2)까지 따라가 틱을 더한다.

G3PartII.dll 근거([[분석-사운드]] 전투 효과음 절):
- 애니메이터 0x10027090/0x10027250 이 키마다 주인 가상함수 [vt+4] 를 부르고,
  유닛·이펙트 공용 구현 0x100e5410 이 종류 1 → 소리 0x10028850(번호 = 키 인자0, 물체 화면 좌표),
  종류 2 → 자식 이펙트(인자0 Obs, 인자1 모션, 인자2·3 자리).
- 시간줄(B) 키는 시작 틱에 한 번, 시작(A) 키의 소리는 그 모션이 이어지는 동안 되풀이.

쓰기:
  python tools/re/obs_sounds.py <게임 폴더> 338:24,25,26 1338:* ...
  python tools/re/obs_sounds.py <게임 폴더> --chr 221 219 62 193     (sprite 모션 전부, 소리 있는 것만)
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from skill_motion import Game, load_chr, load_obs_motions  # noqa: E402

_cache = {}


def motions(g, obs):
    if obs not in _cache:
        _cache[obs] = load_obs_motions(g, obs)
    return _cache[obs]


def sounds(g, obs, mid, t0=0, depth=0):
    """[(틱, 소리 번호, 되풀이?, 경로)]"""
    ms = motions(g, obs)
    if not ms or mid not in ms:
        return []
    out = []
    for k in ms[mid]['keys']:
        t = t0 + (0 if k['list'] == 'A' else k['start'])
        if k['kind'] == 1:
            out.append((t, k['p'][0], k['list'] == 'A', '%d:%d' % (obs, mid)))
        elif k['kind'] == 2 and depth < 6:
            out += [(a, b, c, '%d:%d>%s' % (obs, mid, p)) for a, b, c, p in sounds(g, k['p'][0], k['p'][1], t, depth + 1)]
    return sorted(out)


def show(g, obs, mids, only_sound=False):
    ms = motions(g, obs)
    if ms is None:
        print('Obs %04d 없음' % obs)
        return
    for mid in (sorted(ms) if mids is None else mids):
        s = sounds(g, obs, mid)
        if only_sound and not s:
            continue
        length = ms[mid]['len'] if mid in ms else -1
        body = ', '.join('t%d 소리%d%s [%s]' % (t, n, '(반복)' if loop else '', p) for t, n, loop, p in s) or '-'
        print('Obs %04d 모션 %d (길이 %d): %s' % (obs, mid, length, body))


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return
    sys.stdout.reconfigure(encoding='utf-8')
    g = Game(sys.argv[1])
    args = sys.argv[2:]
    if args[0] == '--chr':
        for c in args[1:]:
            ch = load_chr(g, int(c))
            print('== Chr %s sprite %d' % (c, ch['sprite']))
            show(g, ch['sprite'], None, only_sound=True)
        return
    for a in args:
        obs, _, ms = a.partition(':')
        show(g, int(obs), None if ms in ('', '*') else [int(x) for x in ms.split(',')], only_sound=ms in ('', '*'))


if __name__ == '__main__':
    main()

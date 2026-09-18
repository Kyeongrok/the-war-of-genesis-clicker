"""창세기전3 파트2 — 타이틀 화면 New Game 뒤에 나오는 「연대표(에피소드 고르기)」 표를 뽑는다.

쓰기:
    python tools/re/episode_list.py "<게임 폴더>" [--md]

게임 폴더는 읽기만 한다. 근거는 분석/분석-UI.md 「타이틀 화면 (an-ui-3)」 절.

`EPS\\Episode.dat` — 로더 `0x101072e0` (장면 7 = 연대표 창 `0x10105e50`)
  머리 u16 ?, u16 레코드 수 n   (실제 파일: 0, 15)
  레코드 n × 40바이트 = u16 × 20. 로더는 파일에 적힌 차례대로 메모리 자리에 흩어 넣는다:
      파일 낱말 0,1,2      → 레코드 +0x00, +0x02, +0x04
      파일 낱말 3~10       → 레코드 +0x08, +0x0c, +0x10, +0x14, +0x18, +0x1c, +0x20, +0x24
      파일 낱말 11~19      → 레코드 +0x06, +0x0a, +0x0e, +0x12, +0x16, +0x1a, +0x1e, +0x22, +0x26
  고른 항목 i 를 챕터로 바꾸는 곳은 `0x101066bc`:
      챕터   = u16 [창 +0x130 + 40*(i/2) + 2*(i%2)]  → 짝수 i = 파일 낱말 9,  홀수 i = 파일 낱말 18
      파티   = u16 [창 +0x134 + 40*(i/2) + 2*(i%2)]  → 짝수 i = 파일 낱말 10, 홀수 i = 파일 낱말 19
  그래서 레코드 하나가 에피소드 **두 개**를 담는다(15 × 2 = 30개 = 챕터 제목 TXR 2284~2313 개수와 같다).
  고르면 `[0x10173424] = 4`(챕터 장면), `[0x10173420] = 챕터 번호`, `[0x101b6894] = 파티 번호`.
"""
import argparse
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from battle_list import parse_chp  # noqa: E402
from btl_dump import load_txr, read_game_file  # noqa: E402

CHAPTER_WORD = (9, 18)   # (짝수 i, 홀수 i)
PARTY_WORD = (10, 19)


def parse_episodes(data):
    n = struct.unpack_from('<h', data, 2)[0]
    rows = [struct.unpack_from('<20h', data, 4 + 40 * r) for r in range(n)]
    out = []
    for i in range(2 * n):
        r, odd = divmod(i, 2)
        out.append(dict(index=i, chapter=rows[r][CHAPTER_WORD[odd]], party=rows[r][PARTY_WORD[odd]]))
    return out


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('--md', action='store_true', help='마크다운 표로')
    a = ap.parse_args()

    with open(os.path.join(a.game, 'EPS', 'Episode.dat'), 'rb') as f:
        data = f.read()
    txr = load_txr(a.game)
    eps = parse_episodes(data)

    if a.md:
        print('| 번호 | Chp | 챕터 제목 | 파티 |')
        print('|---|---|---|---|')
    for e in eps:
        title = '?'
        try:
            c = parse_chp(read_game_file(a.game, 'Chp', '%04d.chp' % e['chapter']))
            title = txr.get(c['hdr']['title'], '?')
        except Exception:
            title = '(못 읽음)'
        if a.md:
            print('| %d | %04d | %s | %d |' % (e['index'], e['chapter'], title, e['party']))
        else:
            print('%2d  Chp %04d  파티 %d  %s' % (e['index'], e['chapter'], e['party'], title))


if __name__ == '__main__':
    main()

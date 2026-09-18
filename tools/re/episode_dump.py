"""창세기전3 파트2 — 연대표(장면 7) 의 `EPS\\Episode.dat` 를 **칸까지 전부** 펼친다.

쓰기:
    python tools/re/episode_dump.py "<게임 폴더>" [--md]

`tools/re/episode_list.py` 는 챕터·파티만 뽑는다. 이 도구는 잠금 조건(깃발 번호 넷)·CD 번호·
쓰지 않는 칸까지 같이 보여 주고, **새 게임(깃발 전부 0)일 때 고를 수 있는 항목**을 표시한다.
근거는 분석/분석-UI.md 「연대표 화면 (an-ui-4)」 절.

로더 `0x101072e0` — 창 `+0x108` 이 0 이면 `EPS\\Episode.dat`, 아니면 `EPS\\Episode1.dat`(이 판에는 없다).
    머리   u16 ?(0) → 창 +0x10c,  u16 레코드 수 n(15) → 창 +0x10e
    레코드 n × 40바이트. 레코드 하나가 **에피소드 두 개**(왼쪽 = 짝수 번호, 오른쪽 = 홀수 번호)를 담는다.
    파일 낱말 차례 → 메모리 자리(레코드 처음 기준):
        낱말 0,1            → +0x00, +0x02            (읽는 코드 없음, 자료도 전부 -1)
        낱말 2..10          → +0x04,+0x08,…,+0x24     (짝수 번호 에피소드의 칸 9개)
        낱말 11..19         → +0x06,+0x0a,…,+0x26     (홀수 번호 에피소드의 칸 9개)
    곧 에피소드 칸 k(0..8)는 `레코드 + 0x04 + 4k + 2*(번호%2)` 에 있다.
        칸 0~3 = 잠금 조건 — `0x10106f50` 이 넷 다 통과해야 고를 수 있다.
                 값이 0xffff 면 조건 없음, 아니면 **스크립트 깃발 배열 `0x101b6050[값] != 0`**(`0x10106f20`).
        칸 4~6 = 읽는 코드 없음(자료도 전부 -1)
        칸 7   = Chp 번호,  칸 8 = 파티 번호(0·1·2)
필요한 CD 번호는 파일이 아니라 **DLL 안에 박힌 30칸 표**(`0x101065cc`·`0x101067 7f`)다.
"""
import argparse
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from battle_list import parse_chp  # noqa: E402
from btl_dump import load_txr, read_game_file  # noqa: E402

# DLL 0x101065d7~0x1010664f 에 박힌 표 — 에피소드 번호 → 있어야 할 CD
CD = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1,
      2, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 3, 3, 3]

FIELD = 9          # 에피소드 한 개가 쓰는 칸 수
COND = (0, 1, 2, 3)  # 잠금 조건 칸
CHAPTER, PARTY = 7, 8


def parse(data):
    """레코드를 로더와 같은 자리로 펼쳐 에피소드 목록을 돌려준다."""
    n = struct.unpack_from('<h', data, 2)[0]
    out = []
    for r in range(n):
        w = struct.unpack_from('<20h', data, 4 + 40 * r)
        for odd in (0, 1):
            base = 2 if odd == 0 else 11        # 파일 낱말 시작 자리
            f = list(w[base:base + FIELD])
            out.append(dict(index=2 * r + odd, record=r, side=odd,
                            offset=4 + 40 * r + 2 * base, fields=f))
    return out


def unlocked(ep, flags):
    """`0x10106f50` — 조건 칸 네 개가 모두 통과해야 1."""
    for k in COND:
        v = ep['fields'][k]
        if v == -1:
            continue
        if not flags[v]:
            return False
    return True


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('--md', action='store_true', help='마크다운 표로')
    a = ap.parse_args()

    with open(os.path.join(a.game, 'EPS', 'Episode.dat'), 'rb') as f:
        data = f.read()
    txr = load_txr(a.game)
    eps = parse(data)
    flags = [0] * 2000                       # 새 게임 = 깃발 전부 0 (`0x1004d610`)

    if a.md:
        print('| 번호 | 쪽 | 파일 오프셋 | Chp | 챕터 제목 | 파티 | CD | 조건 깃발 | 새 게임에 열림 |')
        print('|---|---|---|---|---|---|---|---|---|')
    for e in eps:
        f = e['fields']
        try:
            c = parse_chp(read_game_file(a.game, 'Chp', '%04d.chp' % f[CHAPTER]))
            title = txr.get(c['hdr']['title'], '?')
        except Exception:
            title = '(못 읽음)'
        cond = ', '.join(str(f[k]) for k in COND if f[k] != -1) or '없음'
        side = 'Episode 4(왼)' if e['side'] == 0 else 'Episode 5(오)'
        open_ = 'O' if unlocked(e, flags) else '-'
        if a.md:
            print('| %d | %s | %d | %04d | %s | %d | %d | %s | %s |'
                  % (e['index'], side, e['offset'], f[CHAPTER], title, f[PARTY],
                     CD[e['index']], cond, open_))
        else:
            print('%2d  %s  off %3d  Chp %04d  파티 %d  CD %d  조건 %-10s %s  %s'
                  % (e['index'], side, e['offset'], f[CHAPTER], f[PARTY],
                     CD[e['index']], cond, open_, title))
    rest = {k: [e['fields'][k] for e in eps] for k in (4, 5, 6)}
    print('\n안 쓰는 칸 4·5·6 이 모두 -1 인가: %s'
          % all(all(v == -1 for v in vs) for vs in rest.values()))
    print('파일 크기 %d = 머리 4 + 40 × %d' % (len(data), len(eps) // 2))


if __name__ == '__main__':
    main()

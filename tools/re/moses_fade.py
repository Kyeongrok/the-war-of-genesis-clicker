"""
창세기전3 파트2 — 모세스 화면(과 게임 전체)의 UI 전환 효과 색을 원본과 똑같이 만든다.

색표는 DLL 이 시작할 때 `0x1000b7c0` 이 만드는 32x32 낱말 표다(찾는 자리 = 채널값*32 + 알파).
화면 대 화면 합성은 `0x1000c310` -> `0x10018730`(방식 1~17)이 표 한 벌(파랑/초록/빨강)을 고른다.

    방식 1  : (31a + c(31-a)) / 31      세 채널 모두   -> 흰색 쪽으로
    방식 2  : c(31-a) / 31              세 채널 모두   -> 검정 쪽으로
    방식 5  : 파랑만 방식 1 식, 빨강/초록은 그대로     -> 파랑 씻김

모세스 전환 큐(`+0x2e0c`)가 쓰는 효과:

    효과 1 = 방식 5, 들어오는틱 0 / 나가는틱 30   (항행 진입, Mov 0012 뒤)
    효과 2 = 방식 2, 들어오는틱 30 / 나가는틱 0   (화면 -> 검정)
    효과 3 = 방식 2, 들어오는틱 0 / 나가는틱 30   (검정 -> 화면)

사용법:
    python moses_fade.py table 2            # 방식 2 색표를 찍는다 (채널값 x 알파)
    python moses_fade.py frames 1           # 효과 1 이 프레임마다 쓰는 (방식, 알파)
    python moses_fade.py apply 1 in.png 출력폴더   # 효과 1 을 그림에 입혀 프레임을 뽑는다

자세히는 [[분석-모세스]] 2절 「UI 전환 효과」.
"""
import os
import sys


def table(mode, chan):
    """mode(1/2/5), chan('r'|'g'|'b') -> 32x32 표. 표[채널값][알파]."""
    if mode == 2 or (mode == 5 and chan != 'b'):
        f = (lambda c, a: c * (31 - a) // 31) if mode == 2 else (lambda c, a: c)
    else:                                   # 방식 1, 또는 방식 5 의 파랑
        f = lambda c, a: (31 * a + c * (31 - a)) // 31
    return [[f(c, a) for a in range(32)] for c in range(32)]


def blend(rgb, mode, a):
    """8비트 (r,g,b) 에 방식 mode, 알파 a(0~31) 를 입힌다. 원본은 5비트라 그대로 흉내 낸다."""
    out = []
    for chan, v in zip('rgb', rgb):
        c5 = v >> 3
        out.append(table(mode, chan)[c5][a] << 3)
    return tuple(min(255, x | (x >> 5)) for x in out)


def frames(effect):
    """효과 번호 -> [(방식, 알파)] 프레임 차례. 레벨은 0->31->63, 알파는 31 을 넘지 않는다."""
    mode, tin, tout = {1: (5, 0, 30), 2: (2, 30, 0), 3: (2, 0, 30)}[effect]
    out = []
    if tin:                                  # 앞화면을 알파 0 -> 31 로
        out += [(mode, 31 * k // tin) for k in range(tin + 1)]
    else:
        out.append((mode, 31))               # 레벨이 31 에서 시작 -> 첫 틱은 알파 31
    if tout:                                 # 뒤화면을 알파 31 -> 0 으로
        out += [(mode, 31 - 31 * k // tout) for k in range(tout + 1)]
    return out


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    cmd = sys.argv[1]
    if cmd == 'table':
        mode = int(sys.argv[2])
        for chan in 'bgr':
            print(f'-- 방식 {mode} {chan} 채널 (줄 = 채널값 0~31, 칸 = 알파 0~31)')
            for c in range(32):
                print(' '.join(f'{v:2d}' for v in table(mode, chan)[c]))
    elif cmd == 'frames':
        for i, (mode, a) in enumerate(frames(int(sys.argv[2]))):
            print(f'{i:3d}  방식 {mode}  알파 {a}')
    elif cmd == 'apply':
        from PIL import Image
        effect, src, dst = int(sys.argv[2]), sys.argv[3], sys.argv[4]
        os.makedirs(dst, exist_ok=True)
        im = Image.open(src).convert('RGB')
        for i, (mode, a) in enumerate(frames(effect)):
            lut = [table(mode, chan) for chan in 'rgb']
            out = im.point([v for chan in range(3)
                            for v in [min(255, (lut[chan][x >> 3][a] << 3) |
                                          (lut[chan][x >> 3][a] >> 2)) for x in range(256)]])
            out.save(os.path.join(dst, f'{i:03d}.png'))
        print(f'{len(frames(effect))}장 -> {dst}')
    else:
        print(__doc__)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())

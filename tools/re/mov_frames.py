"""창세기전3 파트2 — `Mov\\NNNN.mov`(Bink 1 영상) 프레임을 PNG 로 뽑는다.

모세스(챕터) 화면은 배경·전환을 이 영상으로 그린다([[분석-모세스]]).
`Bgm\\*.bgm` 과 같은 Bink 묶음이지만 이쪽은 640×480 영상이 들어 있다.

쓰기:
    python tools/re/mov_frames.py <Mov 폴더(풀어 둔 것)> <출력 폴더> 11 12 15 [--every 5]

필요: PyAV(`pip install av`) — FFmpeg 의 Bink 디코더를 쓴다. Pillow 는 PNG 저장용.
출력: <출력>/<NNNN>_<프레임번호>.png, 표준출력에 크기·프레임 수·fps.
"""
import argparse
import os
import sys


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('mov_dir')
    ap.add_argument('out_dir')
    ap.add_argument('numbers', nargs='+', type=int)
    ap.add_argument('--every', type=int, default=0, help='n 프레임마다 저장(0 = 처음·1/4·1/2·3/4·끝 5장)')
    a = ap.parse_args()

    import av  # noqa: E402
    from PIL import Image  # noqa: E402

    os.makedirs(a.out_dir, exist_ok=True)
    for n in a.numbers:
        path = os.path.join(a.mov_dir, '%04d.mov' % n)
        if not os.path.exists(path):
            print('%04d: 없음' % n, file=sys.stderr)
            continue
        c = av.open(path)
        v = c.streams.video[0]
        frames = [f.to_ndarray(format='rgb24') for f in c.decode(video=0)]
        total = len(frames)
        print('%04d: %dx%d %d프레임 %s fps%s' % (n, v.width, v.height, total,
                                                v.average_rate, ' +소리' if c.streams.audio else ''))
        if a.every > 0:
            idx = range(0, total, a.every)
        else:
            idx = sorted({0, total // 4, total // 2, 3 * total // 4, total - 1})
        for i in idx:
            arr = frames[i]
            Image.fromarray(arr).save(os.path.join(a.out_dir, '%04d_%03d.png' % (n, i)))


if __name__ == '__main__':
    main()

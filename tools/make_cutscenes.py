"""기술 컷신 영상(`Mov\\NNNN.mov`, Bink 640×480)을 게임이 읽는 꼴(`assets/effects/cut/NNNN.wgm`)로 바꾼다 — 게임 쪽은 `duel-dx/Ability/CutscenePlayer.cs`.

    python tools/make_cutscenes.py <원본 게임 폴더>

게임은 Bink 영상을 못 푼다(소리만 푼다). 그래서 컷마다 JPEG 로, 소리는 22,050Hz 16비트 PCM 으로 풀어 파일 하나에 담는다.
만든 파일은 저장소에 싣지 않고(.gitignore) `tools/make_asset_pack.py` 가 꾸러미의 「찾을 때만 받는 낱장」으로 올린다 —
원본 게임 폴더를 가진 사용자도 이 파일은 받아야 한다(원본 영상에서 만들 수 없으므로).

`.wgm` 배치(작은 끝):
    "WGM1" · u16 너비 · u16 높이 · u16 fps 분자 · u16 fps 분모 · u32 컷 수 · u32 소리 표본율 · u16 소리 채널 · u32 소리 바이트 수
    · 소리(PCM s16) · 컷마다 (u32 길이, JPEG)

어느 기술이 어느 영상을 트나(G3PartII.dll 의 work 핸들러가 `Mov\\NNNN.mov` 이름을 넘기는 곳 — 옵시디안 분석-스킬 「영상 이펙트」):
    헬 카이트 0006 · 사이킥 드라이브 0036 · 셰틀라이트 어텍 0038 · 버닝 웜 0037 · 타이타니아 슈발츠 0039→0043 · 코메트 0033→0046
    · 폭풍검 0047 · 아수라 파천무 0049→0050→0060 · 진무 천지파열 0059

필요: PyAV(`pip install av` — FFmpeg 의 Bink 디코더) · Pillow.
"""
import io
import os
import struct
import sys
import tempfile

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..'))
sys.path.insert(0, os.path.join(ROOT, 'tools', 're'))

MOVIES = [6, 33, 36, 37, 38, 39, 43, 46, 47, 49, 50, 59, 60]
RATE, QUALITY = 22050, 80


def convert(path, out):
    import av
    from PIL import Image

    frames = []
    with av.open(path) as c:
        v = c.streams.video[0]
        width, height, fps = v.width, v.height, v.average_rate
        for f in c.decode(video=0):
            buf = io.BytesIO()
            Image.fromarray(f.to_ndarray(format='rgb24')).save(buf, 'JPEG', quality=QUALITY)
            frames.append(buf.getvalue())
    pcm, channels = b'', 0
    with av.open(path) as c:
        if c.streams.audio:
            channels = min(2, c.streams.audio[0].channels)
            resampler = av.AudioResampler(format='s16', layout='stereo' if channels == 2 else 'mono', rate=RATE)
            parts = []
            for f in c.decode(audio=0):
                parts += [r.to_ndarray().tobytes() for r in resampler.resample(f)]
            parts += [r.to_ndarray().tobytes() for r in resampler.resample(None)]
            pcm = b''.join(parts)
    with open(out, 'wb') as f:
        f.write(b'WGM1' + struct.pack('<HHHHIIHI', width, height, fps.numerator, fps.denominator, len(frames), RATE, channels, len(pcm)))
        f.write(pcm)
        for jpeg in frames:
            f.write(struct.pack('<I', len(jpeg)) + jpeg)
    return len(frames), os.path.getsize(out)


def main():
    from pak_extract import extract
    game = sys.argv[1]
    out_dir = os.path.join(ROOT, 'assets', 'effects', 'cut')
    os.makedirs(out_dir, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        extract(game, tmp, 'Mov')          # 낱장 + Mov.idx/.pak — 원본 폴더는 건드리지 않는다
        total = 0
        for n in MOVIES:
            count, size = convert(os.path.join(tmp, 'Mov', '%04d.mov' % n), os.path.join(out_dir, '%04d.wgm' % n))
            total += size
            print('%04d: %d컷 %.1fMB' % (n, count, size / 1048576))
        print('모두 %.0fMB → %s' % (total / 1048576, out_dir))


if __name__ == '__main__':
    main()

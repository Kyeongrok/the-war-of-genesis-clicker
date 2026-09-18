"""창세기전3 파트2 — 모세스(챕터) 화면을 자료만으로 다시 그린다.

[[분석-모세스]] 에서 코드로 확정한 자리·그림 그대로 640x480 PNG 를 만든다.
배경 = `Bgr\\%04d.bgr`(640x480 JPEG), 위젯 = `Obs\\%04d.obs` 스프라이트(가산 합성),
글자 = 시스템 한글 글꼴(원작은 GDI TextOut 을 쓴다).

쓰기:
    python tools/re/pak_extract.py "<게임 폴더>" <자료> Obs Bgr Chp
    python tools/re/moses_render.py <자료> <출력 폴더> [--chp 10]

만드는 그림:
    moses-main.png        주 화면(데스크톱)
    moses-party.png       PARTY(신상관리) 페이지 — 활 목록 2칸
    moses-nav-planet.png  항행 단계 1(행성 고르기)
    moses-nav-place.png   항행 단계 2(장소 고르기)
"""
import argparse
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))

from obs_ui_dump import load_motions, load_slots  # noqa: E402
from PIL import Image, ImageChops, ImageDraw, ImageFont  # noqa: E402

from extract_character import parse_txr  # noqa: E402

# ── 코드에서 뽑은 상수 (0x100fdfe0 · 0x100fcf00 · 0x100fcb50) ─────────
ICON_X = [50, 105, 160, 25, 80, 135]
ICON_Y = [400, 400, 400, 435, 435, 435]
ICON_MOTION = [4, 6, 8, 10, 12, 23]              # Obs 291
CELLS = [(438, 180), (442, 220), (421, 140), (434, 260),
         (383, 100), (412, 300), (335, 60), (360, 340)]
LOGO = (291, 1, 10, 10)
BACK_BUTTON = (248, 0, 46, 244)
CELL_BUTTON = (643, 0)
CELL_BAR = 246                                    # 모션 0 / 1(가로 158) / 2
PLACE_MARKER = (498, 2)
PLANET_MARKER = (163, 3)
FONTS = [r"C:\Windows\Fonts\gulim.ttc", r"C:\Windows\Fonts\malgun.ttf"]


class Assets:
    def __init__(self, root):
        self.root = root
        self._slots, self._motions = {}, {}
        txr = os.path.join(root, 'TXR', 'Txr.dat')
        self.txr = {}
        if os.path.exists(txr):
            for r in parse_txr(open(txr, 'rb').read()):
                self.txr.setdefault(r['txr_id'], r['text'])
        self.font = None
        for f in FONTS:
            if os.path.exists(f):
                self.font = ImageFont.truetype(f, 12)
                break
        if self.font is None:
            self.font = ImageFont.load_default()

    def _obs(self, n):
        return open(os.path.join(self.root, 'Obs', '%04d.obs' % n), 'rb').read()

    def slots(self, n):
        if n not in self._slots:
            self._slots[n] = load_slots(self._obs(n))
        return self._slots[n]

    def motions(self, n):
        if n not in self._motions:
            self._motions[n] = load_motions(self._obs(n))
        return self._motions[n]

    def frame(self, n, motion, tick=0):
        keys = [k for k in self.motions(n)[motion]['keys']
                if k['kind'] == 0 and k['list'] == 'B']
        keys.sort(key=lambda k: k['start'])
        cur = keys[0]
        for k in keys:
            if k['start'] <= tick:
                cur = k
        return cur['p'][0], cur['p'][1]

    def size(self, n, motion, tick=0):
        _, img = self.slots(n)[self.frame(n, motion, tick)]
        return img.width, img.height

    def bgr(self, n):
        return Image.open(os.path.join(self.root, 'Bgr', '%04d.bgr' % n)).convert('RGBA')

    def text(self, txr_id):
        return self.txr.get(txr_id, '<TXR %d>' % txr_id)


def blit(a, canvas, n, motion, x, y, tick=0, width=None):
    """모션 키 종류 3 인자 17 = 가산 합성."""
    dec, img = a.slots(n)[a.frame(n, motion, tick)]
    if width and width != img.width:
        img = img.resize((width, img.height), Image.NEAREST)
    px, py = x + dec['x'], y + dec['y']
    base = canvas.crop((px, py, px + img.width, py + img.height)).convert('RGBA')
    r, g, b, alpha = img.split()
    br, bg, bb, _ = base.split()
    out = Image.merge('RGBA', (ImageChops.add(br, r), ImageChops.add(bg, g),
                               ImageChops.add(bb, b), base.split()[3]))
    base.paste(out, (0, 0), alpha)
    canvas.paste(base, (px, py))


def label(a, canvas, s, wx, wy, ww, wh, halign=2, xoff=-15, valign=1, yoff=2,
          colour=(255, 255, 255)):
    """0x10040600 + 0x10040970 의 정렬 규칙."""
    d = ImageDraw.Draw(canvas)
    tw, th = d.textlength(s, font=a.font), 12
    x = xoff if halign == 0 else (ww / 2 - tw / 2 + xoff if halign == 1 else ww + xoff - tw)
    y = yoff if valign == 0 else (wh / 2 - th / 2 + yoff if valign == 1 else wh + yoff - th)
    d.text((wx + x, wy + y), s, font=a.font, fill=colour)


# ── Chp 읽기 (로더 0x100f6c80, [[분석-모세스]] 6절) ────────────────────
class Chapter:
    def __init__(self, path):
        d = open(path, 'rb').read()
        self.o = 0
        self.d = d
        w = self._w
        self.region, self.bgr, self.bgm, self.item_shop, self.wt_shop = (w(), w(), w(), w(), w())
        [w() for _ in range(16)]
        self.start_step, self.start_id, self.title = w(), w(), w()
        n = w(); w()
        self.points = [[w() for _ in range(6)] for _ in range(n)]
        n = w(); w()
        self.people = [[w() for _ in range(15)] for _ in range(n)]
        n = w(); w()
        self.systems = [self._rec(8, 3, 1) for _ in range(n)]
        n = w(); w()
        self.planets = [self._rec(8, 3, 10) for _ in range(n)]
        n = w(); w()
        self.places = [[w() for _ in range(10)] for _ in range(n)]
        self._normalize()

    def _normalize(self):
        """로더 0x100f68c0 — 슬롯에 적힌 '번호'를 배열 '인덱스'로 바꾼다."""
        def idx(rows, num, key):
            for i, r in enumerate(rows):
                if key(r) == num:
                    return i
            return num
        for s in self.systems:
            s['slots'][0] = [idx(self.planets, v, lambda r: r['w'][0]) if v >= 0 else v
                             for v in s['slots'][0]]
        for p in self.planets:
            p['slots'][0] = [idx(self.places, v, lambda r: r[0]) if v >= 0 else v
                             for v in p['slots'][0]]
        for pt in self.points:
            pt[5] = idx(self.systems, pt[5], lambda r: r['w'][0])

    def _w(self):
        v = struct.unpack_from('<h', self.d, self.o)[0]
        self.o += 2
        return v

    def _rec(self, head, slot_groups, tail):
        return {'w': [self._w() for _ in range(head)],
                'slots': [[self._w() for _ in range(8)] for _ in range(slot_groups)],
                'tail': [self._w() for _ in range(tail)]}


def desktop(a, c):
    blit(a, c, *LOGO)
    for x, y in zip(ICON_X, ICON_Y):
        blit(a, c, CELL_BAR, 0, x - 8, y, tick=5)
        blit(a, c, CELL_BAR, 1, x + 2, y, tick=5)
        blit(a, c, CELL_BAR, 2, x + 26, y, tick=5)
    for i, (x, y) in enumerate(zip(ICON_X, ICON_Y)):
        blit(a, c, 291, ICON_MOTION[i], x + 14, y + 19)


def arc_item(a, c, i, marker_motion, caption, hover=False):
    x, y = CELLS[i]
    w, h = a.size(*CELL_BUTTON)
    if hover:
        blit(a, c, CELL_BAR, 0, x, y, tick=5)
        blit(a, c, CELL_BAR, 1, x + 10, y, tick=5, width=158)
        blit(a, c, CELL_BAR, 2, x + 168, y, tick=5)
    blit(a, c, CELL_BUTTON[0], CELL_BUTTON[1], x, y)
    blit(a, c, PLACE_MARKER[0], marker_motion, x + 20, y + 14)
    label(a, c, caption, x, y, w, h)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('assets', help='pak_extract.py 로 풀어 둔 폴더(Obs, Bgr, Chp, TXR)')
    ap.add_argument('out')
    ap.add_argument('--chp', type=int, default=10)
    args = ap.parse_args()

    a = Assets(args.assets)
    chp = Chapter(os.path.join(args.assets, 'Chp', '%04d.chp' % args.chp))
    os.makedirs(args.out, exist_ok=True)
    sysrec = chp.systems[0]
    sys_bgr = sysrec['tail'][0]                      # 항성계 +0x40 = 배경 번호

    # 1) 주 화면
    c = a.bgr(chp.bgr)
    desktop(a, c)
    c.convert('RGB').save(os.path.join(args.out, 'moses-main.png'))

    # 2) PARTY 페이지 — 활 목록 2칸 (전직 / 용병관리)
    c = a.bgr(chp.bgr)
    desktop(a, c)
    blit(a, c, *BACK_BUTTON, tick=5)
    arc_item(a, c, 0, 0, a.text(1326), hover=True)
    arc_item(a, c, 1, 1, a.text(1327))
    c.convert('RGB').save(os.path.join(args.out, 'moses-party.png'))

    # 3) 항행 단계 1 — 행성 고르기
    c = a.bgr(sys_bgr)
    shown = []
    for idx in sysrec['slots'][0]:
        if idx < 0:
            continue
        p = chp.planets[idx]
        obs, motion = p['w'][1], p['w'][4]
        x, y = p['tail'][8], p['tail'][9]
        blit(a, c, obs, motion, x, y)
        _, mh = a.size(*PLANET_MARKER)
        blit(a, c, PLANET_MARKER[0], PLANET_MARKER[1], x, y - 10 - mh // 2)
        shown.append((x, y))
    for pt in chp.points:                            # 자리가 안 맞는 성도 점은 남는다
        if pt[5] == 0 and (pt[3], pt[4]) not in shown:
            blit(a, c, pt[1], pt[2], pt[3], pt[4])
    desktop(a, c)
    blit(a, c, *BACK_BUTTON, tick=5)
    c.convert('RGB').save(os.path.join(args.out, 'moses-nav-planet.png'))

    # 4) 항행 단계 2 — 장소 고르기 (첫 행성 기준)
    planet = chp.planets[[i for i in sysrec['slots'][0] if i >= 0][2]]
    c = a.bgr(sys_bgr)
    blit(a, c, planet['tail'][2], planet['tail'][3], 320, 220)     # 행성 구체
    names = [a.text(chp.places[i][1]) for i in planet['slots'][0] if i >= 0][:8]
    for i, name in enumerate(names):
        arc_item(a, c, i, PLACE_MARKER[1], name, hover=(i == 0))
    desktop(a, c)
    blit(a, c, *BACK_BUTTON, tick=5)
    c.convert('RGB').save(os.path.join(args.out, 'moses-nav-place.png'))

    print('만듦:', args.out)


if __name__ == '__main__':
    main()

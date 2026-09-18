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
    """머리는 24워드 고정이다(로더 0x100f6c80 이 조건 없이 24번 읽는다).

    다만 자료에는 **제목 TXR 워드 하나가 없는 23워드짜리**가 셋(0038·0059·0065) 있어,
    24 로 읽어 파일 끝과 안 맞으면 23 으로 한 번 더 읽는다. 머리 워드 0 은 **판 번호**이고
    지금 게임이 읽는 것은 3 뿐이다 — 0·1·2 인 다섯(0002·0008·0009·0024·0037)은 배치가 아예 달라
    게임 본체도 못 읽는 옛 자료라 여기서도 거절한다.
    """

    def __init__(self, path):
        self.d = open(path, 'rb').read()
        if len(self.d) >= 2 and struct.unpack_from('<h', self.d, 0)[0] != 3:
            raise ValueError('%s: 판 번호가 3 이 아니다(게임도 못 읽는 옛 자료)' % os.path.basename(path))
        try:
            self._load(24)
            if self.o != len(self.d):
                raise ValueError('끝이 안 맞는다')
        except (ValueError, struct.error):
            self._load(23)                       # 제목 TXR 워드가 없는 중간 판
            if self.o != len(self.d):
                raise ValueError('%s: 어떤 머리 길이로도 파일 끝과 안 맞는다' % os.path.basename(path))
        self._normalize()

    def _load(self, head_words):
        self.o = 0
        w = self._w
        self.version, self.bgr, self.bgm, self.item_shop, self.wt_shop = (w(), w(), w(), w(), w())
        # 5~12 · 13~20 은 인물 번호 8칸짜리 목록 둘이다(도우미 0x100f6880, 정규화기 0x100f68c0 가 둘 다 인물 표에 맞춘다).
        self.people_a = [w() for _ in range(8)]
        self.people_b = [w() for _ in range(8)]
        self.start_step, self.start_id = w(), w()
        self.title = w() if head_words >= 24 else -1
        n = self._count()
        self.points = [[w() for _ in range(6)] for _ in range(n)]
        n = self._count()
        self.people = [[w() for _ in range(15)] for _ in range(n)]
        n = self._count()
        self.systems = [self._rec(8, 3, 1) for _ in range(n)]
        n = self._count()
        self.planets = [self._rec(8, 3, 10) for _ in range(n)]
        n = self._count()
        self.places = [[w() for _ in range(10)] for _ in range(n)]
        n = self._count()
        self.o += 4 * n                          # 꼬리 4바이트 레코드 표
        self._skip_script()
        # 로더 0x100f7318 — 최저 단계가 1 보다 작으면 1(행성 고르기)로 올리고 첫 행성을 고른다.
        if self.start_step < 1 and self.planets:
            self.start_step, self.start_id = 1, self.planets[0]['w'][0]

    def _count(self):
        """묶음 머리 = 수 한 워드 + 여벌 한 워드. 음수면 배치가 어긋난 것이다."""
        n = self._w(); self._w()
        if n < 0:
            raise ValueError('묶음 개수가 음수')
        return n

    def _skip_script(self):
        """수 한 워드, 이벤트마다 (워드1, 조건수, 조건 18바이트, 행동수, 행동 18바이트)."""
        n = self._w()
        if n < 0:
            raise ValueError('스크립트 이벤트 수가 음수')
        for _ in range(n):
            conditions = struct.unpack_from('<h', self.d, self.o + 2)[0]
            actions = struct.unpack_from('<h', self.d, self.o + 4 + 18 * conditions)[0]
            if conditions < 0 or actions < 0:
                raise ValueError('스크립트 조건·행동 수가 음수')
            self.o += 4 + 18 * conditions + 2 + 18 * actions

    def _normalize(self):
        """로더 0x100f68c0 — 슬롯에 적힌 '번호'를 배열 '인덱스'로 바꾼다."""
        def idx(rows, num, key):
            for i, r in enumerate(rows):
                if key(r) == num:
                    return i
            return num
        # 칸 벌 세 개의 뜻(정규화기 0x100f68c0 로 확정): 항성계 = 행성·인물·인물, 행성 = 장소·인물·인물.
        for s in self.systems:
            s['slots'][0] = [idx(self.planets, v, lambda r: r['w'][0]) if v >= 0 else v
                             for v in s['slots'][0]]
            for g in (1, 2):
                s['slots'][g] = [idx(self.people, v, lambda r: r[0]) if v >= 0 else v
                                 for v in s['slots'][g]]
        for p in self.planets:
            p['slots'][0] = [idx(self.places, v, lambda r: r[0]) if v >= 0 else v
                             for v in p['slots'][0]]
            for g in (1, 2):
                p['slots'][g] = [idx(self.people, v, lambda r: r[0]) if v >= 0 else v
                                 for v in p['slots'][g]]
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

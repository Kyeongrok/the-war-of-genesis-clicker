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
    moses-nav-radar.png   항행 단계 2 + 레이더(행성 구체 위를 도는 초록 격자 구와 장소 조각, 틱 20)
    moses-style3.png      전직(STYLE CHANGE) 페이지 — 3단계 단추 둘이 나온 모습
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


# ── 레이더 `+0x2f14` (클래스 0x10103880, [[분석-모세스]] 6절) ──────────────
# 반지름 74 의 구를 위도 11줄(0~180°, 18° 씩) × 경도 10줄(36° 씩) 격자로 잡아 (320,220) 에 놓고,
# x 30° · z 160° 기울인 채 세로축으로 틱마다 3° 돌린다(120틱에 한 바퀴). 계산은 전부 10비트 고정소수점.
RADAR_CENTER = (320 << 10, 220 << 10)
RADAR_R = 74.0
RADAR_TILT = (30, 160)                            # 0x101039e0(30, 160, 3*(틱%120))


def _ftol(v):
    """x87 `fistp`(0x10128f94)는 0 쪽으로 자른다."""
    return int(v)


def radar_tables():
    """0x10103bf0 앞부분 — sin/cos 표 360칸, 1024 배, +0.5 뒤 자름."""
    import math
    sin = [_ftol(math.sin(i * 0.01745328888888889) * 1024.0 + 0.5) for i in range(360)]
    cos = [_ftol(math.cos(i * 0.01745328888888889) * 1024.0 + 0.5) for i in range(360)]
    return sin, cos


def radar_lattice():
    """0x10103bf0 뒷부분 — 점 110개, 번호 = 경도칸*11 + 위도칸. (X, D, Y): X·Y 는 화면 자리, D 는 깊이."""
    import math
    pts = []
    for col in range(10):
        b = (2 * col) * 0.3141592
        for row in range(11):
            a = row * 0.3141592
            y = _ftol(math.cos(a) * 75776.0) + RADAR_CENTER[1]          # 74·1024
            r = math.sin(a) * RADAR_R
            x = RADAR_CENTER[0] - _ftol(math.cos(b) * r * -1024.0)
            d = _ftol(math.sin(b) * r * 1024.0)
            pts.append((x, d, y))
    return pts


def radar_rotate(pts, a1, a2, a3, tables=None):
    """0x101039e0 — 세 각(도)을 표로 찾아 고정소수점 회전. 원본 식 그대로(>> 는 버림 시프트)."""
    sin, cos = tables or radar_tables()
    s1, c1 = sin[a1 % 360], cos[a1 % 360]
    s2, c2 = sin[a2 % 360], cos[a2 % 360]
    s3, c3 = sin[a3 % 360], cos[a3 % 360]
    out = []
    for x, d, y in pts:
        xr, yr = x - RADAR_CENTER[0], y - RADAR_CENTER[1]
        ox = ((xr * ((c3 * c2) >> 10) - d * ((s3 * c2) >> 10) + yr * s2) >> 10) + RADAR_CENTER[0]
        od = (xr * (((s1 * s2) * c3 >> 20) + ((c1 * s3) >> 10))
              + d * (((c1 * c3) >> 10) - ((s1 * s2) * s3 >> 20))
              - yr * ((s1 * c2) >> 10)) >> 10
        oy = ((xr * (((s1 * s3) >> 10) - ((c1 * s2) * c3 >> 20))
               + d * (((c1 * s2) * s3 >> 20) + ((s1 * c3) >> 10))
               + yr * ((c1 * c2) >> 10)) >> 10) + RADAR_CENTER[1]
        out.append((ox, od, oy))
    return out


def _radar_shade(d_sum):
    """0x10103d10 — 두 끝 깊이의 평균을 반지름 74 에 대한 백분율로 바꿔 127 을 더한 값.
    128 미만이면 뒷면이라 안 그린다. 초록 5비트 = 값 >> 3."""
    v = (d_sum >> 11) * 100
    v = int(v / 74) if v >= 0 else -int(-v / 74)
    v = (v + 0x7f) & 0xff
    return v if v & 0x80 else None


def _px(v):
    return v >> 10


def draw_radar(canvas, places, hover=-1, tick=20):
    """places = [(경도칸, 위도칸), …] (장소 +0x08 · +0x0a, 후보 순서 = +0x2eae). hover = 마우스가 올라간 칸 번호.

    격자선은 앞면만, 깊이가 얕을수록 밝은 초록. 장소는 격자 한 칸을 채운 조각인데
    다른 것은 노랑, 마우스가 올라간 것은 빨강이고 둘 다 200틱 주기로 밝기가 오르내린다."""
    d = ImageDraw.Draw(canvas, 'RGBA')
    pts = radar_rotate(radar_lattice(), RADAR_TILT[0], RADAR_TILT[1], 3 * (tick % 120))

    def line(k1, k2):
        v = _radar_shade(pts[k1][1] + pts[k2][1])
        if v is None:
            return
        g = (v & 0xf8)
        d.line([(_px(pts[k1][0]), _px(pts[k1][2])), (_px(pts[k2][0]), _px(pts[k2][2]))],
               fill=(8, g, 8, 255))

    for col in range(10):                          # 경선 (0x10103d28)
        for row in range(10):
            line(col * 11 + row, col * 11 + row + 1)
    for row in range(10):                          # 위선 (0x10103e05) — 남극(11번째 줄)은 안 잇는다
        for col in range(10):
            line(col * 11 + row, ((col + 1) % 10) * 11 + row)

    pulse = abs(100 - (3 * tick) % 200)
    c = (0xff - pulse) & 0xf8
    for i, (col, row) in enumerate(places):
        if col < 0 or row < 0:
            continue
        corners = [col * 11 + row, col * 11 + (row + 1) % 10,
                   ((col + 1) % 10) * 11 + row, ((col + 1) % 10) * 11 + (row + 1) % 10]
        if sum(pts[k][1] for k in corners) < 0:
            continue
        quad = [corners[0], corners[1], corners[3], corners[2]]
        colour = (c, 0, 0, 255) if i == hover else (c, c, 0, 255)
        d.polygon([(_px(pts[k][0]), _px(pts[k][2])) for k in quad], fill=colour)


# ── 전직(STYLE CHANGE) 페이지 (0x100f9650, [[분석-모세스]] 8절 · [[분석-체질]]) ──
STYLE_BGR = 42
STYLE_FORM_XY = [(36, 200), (73, 244), (146, 280), (220, 244), (257, 200)]   # Obs 283, TXR 878~882
STYLE_STAGE3 = [((107, 70), 2, (38, 31), (38, 26), 0),       # id 91: Obs 329 m2/3, Obs 1334 @+(38,31), Obs 1426 m0 @+(38,26)
                ((184, 70), 4, (30, 31), (30, 26), 1)]       # id 92: Obs 329 m4/5, Obs 1334 @+(30,31), Obs 1426 m1 @+(30,26)
STYLE_ICON_MOTION = [0, 3, 1, 4, 2]                          # Obs 1334 모션 = 표[(직업−1)/3]


def style_stage3(a, c, icons=(0, 0), hover=1, current_form=0):
    """3단계 단추 둘이 보이는 전직 페이지. icons = 두 단추의 Obs 1334 모션(계열 아이콘)."""
    for i, (x, y) in enumerate(STYLE_FORM_XY):
        blit(a, c, 283, 0, x, y)
        colour = (0, 255, 0) if i == current_form else (0xb4, 0xb4, 0xb4)
        label(a, c, a.text(878 + i), x, y, 68, 28, halign=1, xoff=0, colour=colour)
    for k, ((x, y), m, icon_at, mark_at, mark_m) in enumerate(STYLE_STAGE3):
        blit(a, c, 329, m + (1 if k == hover else 0), x, y)
        blit(a, c, 1334, icons[k], x + icon_at[0], y + icon_at[1])
        blit(a, c, 1426, mark_m, x + mark_at[0], y + mark_at[1])
    blit(a, c, 302, 4, 48, 330)                    # 파티원 단추 0 (초상화는 Chr 자료가 있어야 한다)
    blit(a, c, 287, 0, 455, 430)                   # 나가기


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

    # 5) 항행 단계 2 + 레이더 — 장소 조각은 후보 순서(+0x2eae)대로, 칸 0 에 마우스가 올라간 상태
    coords = [(chp.places[i][4], chp.places[i][5]) for i in planet['slots'][0] if i >= 0][:8]
    draw_radar(c, coords, hover=0, tick=20)
    c.convert('RGB').save(os.path.join(args.out, 'moses-nav-radar.png'))

    # 6) 전직 페이지 — 3단계 단추 둘
    c = a.bgr(STYLE_BGR)
    style_stage3(a, c, icons=(STYLE_ICON_MOTION[0], STYLE_ICON_MOTION[0]))
    c.convert('RGB').save(os.path.join(args.out, 'moses-style3.png'))

    print('만듦:', args.out)


if __name__ == '__main__':
    main()

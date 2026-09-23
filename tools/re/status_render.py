"""창세기전3 파트2 — 전투 Status 창을 원본 자료만으로 다시 그린다 (st-5).

[[분석-캐릭터]] 「Status 창 그림과 설명 표시 (st-5)」 에서 코드로 확정한 자리·그림 그대로 640x480 PNG 를 만든다.

    python tools/re/status_render.py "<게임 폴더>" <출력 폴더> [--chr 221] [--sav G3P_II20.sav] [--tooltip 0]

게임 폴더는 읽기만 한다(낱장이 없으면 .idx/.pak 에서 잘라 읽는다). 만드는 그림:
    status.png          Status 창 (마우스 올림 없음) — 캡처 status-죠안.png 와 같은 장면
    status-hover.png    획득한 어빌리티 첫 줄·장비 첫 줄에 마우스를 올린 모습(Obs 0471 모션 4·1)
    status-tooltip.png  획득한 어빌리티 --tooltip 번째 줄을 오른쪽 단추로 누르고 있는 모습(설명 창)

근거(G3PartII.dll):
    창 0x100e0500 — 창 (1,21) 638x458, 배경 Bgr 0058 을 화면 (0,0) 에 통째로(0x100e09f0).
        칸 틀·칸 제목·「STATUS」·LEVEL 같은 글자·HP/SOUL/TP 밑 붉은 줄은 모두 Bgr 0058 안에 그려져 있다.
    CLOSE 0x100e05a2 — 창 안 (564,-20) 76x23, Obs 0471 모션 28(평소)/29(눌림), 그림 자리 (38,11).
    WEAPON 띠 0x100e06db — 창 안 (295,267), Obs 0326 모션 = chr 40(0 이면 62).
    능력치 0x100d4740 · 상태이상 0x100d5448 · 목록 0x10046410/0x10047000 · 줄 0x10035290/0x100361e0/0x10035890/0x100d3330.
    설명 창 0x10042c00 → 0x100429a0: 오른쪽 단추를 누르는 동안(vt+0x10 0x1003fc80) 마우스+(16,16), 뗄 때 숨김.
"""
import argparse
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))

import skill_motion as sm  # noqa: E402
import obs_layout as ol  # noqa: E402
from sav_dump import parse_sav  # noqa: E402
from extract_character import parse_txr  # noqa: E402
from PIL import Image, ImageDraw, ImageFont  # noqa: E402

WIN = (1, 21)                         # 창 왼위(화면)
WHITE = (248, 252, 248)               # 0xFFFFFF 를 16비트 화면에 찍은 값(캡처 실측)
DIM = 15 / 31                         # 꺼진 줄: 물들이기 방식 2 · 세기 16
RED, YELLOW = (255, 0, 0), (255, 255, 0)   # 비용 숫자 0xff / 0xffff (COLORREF)
TRACK = (99, 101, 99)                 # 스크롤 막대 바탕 0x632C (캡처 (96,100,96))
FONT_FILES = [r"C:\Windows\Fonts\gulim.ttc", r"C:\Windows\Fonts\malgun.ttf"]

# 칸 목록(0x10046410 인자 + 0x10047000(pad, gap)) — 창 안 좌표
PASSIVE = dict(x=210, y=120, rows=3, cw=162, ch=22, pad=8, gap=6)
EQUIP = dict(x=210, y=275, rows=6, cw=162, ch=22, pad=8, gap=4)
LEARNED = dict(x=400, y=42, rows=6, cw=190, ch=22, pad=6, gap=5, scroll=True)
LEARNABLE = dict(x=400, y=252, rows=4, cw=190, ch=37, pad=6, gap=9, scroll=True)
STAT_PANEL = (20, 40, 170, 400)       # 0x100d4680
STATE_PANEL = (210, 40, 170, 40)      # 0x100d53c0


def row_xy(lst, i):
    return (WIN[0] + lst['x'] + lst['pad'], WIN[1] + lst['y'] + lst['pad'] + i * (lst['ch'] + lst['gap']))


def list_size(lst):
    w = lst['cw'] + 2 * lst['pad']
    h = lst['rows'] * (lst['ch'] + lst['gap']) - lst['gap'] + 2 * lst['pad']
    return w, h


class Res:
    def __init__(self, game):
        self.g = sm.Game(game)
        self.game = game
        self._s, self._m = {}, {}
        self.txr = {}
        for r in parse_txr(self.g.read('TXR', 'Txr.dat')):
            self.txr.setdefault(r['txr_id'], r['text'])
        self.font = next((ImageFont.truetype(f, 12) for f in FONT_FILES if os.path.exists(f)), None) or ImageFont.load_default()

    def t(self, i):
        return self.txr.get(i, '')

    def obs(self, n):
        if n not in self._s:
            d = self.g.read('Obs', '%04d.obs' % n)
            _, subs = ol.parse_all(d)            # 장 번호 구멍(0471)까지 읽는 파서
            self._s[n] = {(sub['id'], sl['id']): (sl, ol.decode(d, sub, sl)) for sub in subs for sl in sub['slots']}
            self._m[n] = ol.parse_motions(d)
        return self._s[n], self._m[n]

    def sprite(self, n, motion):
        """모션 0틱 그림 키 — 목록 B 만(Obs 0326 의 목록 A 벌0 조각은 캡처에 안 보인다: 수련검 아이콘이 칼 하나뿐)."""
        slots, motions = self.obs(n)
        if motion not in motions:
            return []
        out = []
        for k in motions[motion]['keys']:
            key = (k['p'][0], k['p'][1])
            if k['kind'] == 0 and k['list'] == 'B' and k['start'] == 0 and key in slots and key not in out:
                out.append(key)
        return [slots[k] for k in out]


def draw_obs(r, canvas, n, motion, x, y, dim=False, stretch=None):
    for dec, img in r.sprite(n, motion):
        img = img.convert('RGBA')
        if stretch:
            img = img.resize(stretch, Image.BILINEAR)
            px, py = x, y
        else:
            px, py = x + dec['x'], y + dec['y']
        if dim:
            rgb = img.convert('RGB').point(lambda v: int(v * DIM))
            img = Image.merge('RGBA', (*rgb.split(), img.split()[3]))
        canvas.alpha_composite(img, (px, py))


def text(r, canvas, s, x, y, w, h, halign=0, xoff=0, valign=1, yoff=0, colour=WHITE, dim=False):
    """0x10040600/0x10040970 정렬: halign 0 왼·1 가운데·2 오른, valign 0 위·1 가운데·2 아래."""
    d = ImageDraw.Draw(canvas)
    tw, th = d.textlength(s, font=r.font), 12
    tx = xoff if halign == 0 else (w / 2 - tw / 2 + xoff if halign == 1 else w + xoff - tw)
    ty = yoff if valign == 0 else (h / 2 - th / 2 + yoff if valign == 1 else h + yoff - th)
    if dim:
        colour = tuple(int(c * DIM) for c in colour)
    d.text((x + tx, y + ty), s, font=r.font, fill=colour)


def load_game(r, chr_code, sav):
    _, chars, _, _ = parse_sav(os.path.join(r.game, sav))
    H = lambda b, o: struct.unpack_from('<H', b, o)[0]  # noqa: E731
    c = next(c for c in chars if H(c, 4) == chr_code)
    abis, works = {}, sm.load_works(r.g)
    for fn in sm.ABI_FILES:
        d = r.g.read('Abi', fn)
        _, n, _ = struct.unpack_from('<3H', d)
        for i in range(n):
            v = struct.unpack_from(sm.ABI_FMT, d, 6 + i * 26)
            abis.setdefault(v[0], dict(name=v[1], maxlv=v[2], pre1=v[3], lv1=v[4], pre2=v[5], lv2=v[6], type=v[7],
                                       group=v[11], many=v[12], kind=v[13], desc=v[14], levels={}))
    for wid, w in works.items():
        if w[0x4] in abis and w[0x6]:
            abis[w[0x4]]['levels'][w[0x6]] = wid
    items = {}
    d = r.g.read('Dat', 'itm.dat')
    _, n, _ = struct.unpack_from('<3H', d)
    for i in range(n):
        o = 6 + i * 48
        items[H(d, o)] = dict(name=H(d, o + 2), type=d[o + 8], pic=H(d, o + 9), desc=H(d, o + 46))
    deps = []
    d = r.g.read('Dat', 'Dep.dat')
    o = 6
    while o + 33 <= len(d) - 2:
        deps.append((H(d, o + 2), d[o + 4]))
        o += 33
    jobs = {}
    d = r.g.read('Dat', 'Job.dat')
    _, n, _ = struct.unpack_from('<3H', d)
    for i in range(n):
        o = 6 + i * 67
        jobs[H(d, o)] = dict(abilities=[a for a in struct.unpack_from('<11H', d, o + 31) if a],
                             names=struct.unpack_from('<6H', d, o + 53))
    return c, abis, works, items, deps, jobs


def ability_row(r, canvas, ab, level, cost, exp, x, y, w, h, icon_y, label=None):
    """0x10035290 / 0x100361e0 (선행 없는 줄): 글 (46, 가운데) · 아이콘 (14,·)·(34,·) · 비용 오른끝 w-10."""
    off = cost is None or cost > exp          # 0x1003fdc0 — 글·아이콘은 꺼진 색, 숫자는 꺼진 뒤 만들어 안 흐림
    text(r, canvas, label or '%s Lv%d' % (r.t(ab['name']), level), x, y, w, h, 0, 46, 1, 0, dim=off)
    if ab['group'] != 0xffff:
        draw_obs(r, canvas, 488, ab['group'] * 10 + ab['kind'], x + 14, y + icon_y, dim=off)
        draw_obs(r, canvas, 488, ab['group'] * 10 + ab['many'] + 5, x + 34, y + icon_y, dim=off)
    if cost is not None:
        text(r, canvas, '%d' % cost, x, y, w - 10, h, 2, 0, 1, 0, RED if cost > exp else YELLOW)


def scrollbar(r, canvas, lst):
    w, h = list_size(lst)
    x, y = WIN[0] + lst['x'] + w, WIN[1] + lst['y']
    ImageDraw.Draw(canvas).rectangle((x, y, x + 15, y + h - 1), fill=TRACK + (255,))
    draw_obs(r, canvas, 71, 2, x, y)             # 위 화살표 (눌림 3)
    draw_obs(r, canvas, 71, 4, x, y + h - 16)    # 아래 화살표 (눌림 5)
    draw_obs(r, canvas, 71, 6, x, y + 16)        # 손잡이 (눌림 7) — 맨윗줄 0


def frame(r, canvas, x, y, w, h):
    """메시지 창 틀(분석-시스템메뉴 fa-11): Obs 0970 모션 7 늘려 깔기 24/31 + 흰 테두리 + 네 귀퉁이, 제목줄 없음."""
    bg = r.sprite(970, 7)[0][1].convert('RGBA').resize((w, h), Image.BILINEAR)
    base = canvas.crop((x, y, x + w, y + h))
    canvas.paste(Image.blend(base, bg, 24 / 31), (x, y))
    d = ImageDraw.Draw(canvas)
    d.rectangle((x - 3, y - 3, x + w + 2, y + h + 2), outline=(82, 82, 132))
    d.rectangle((x - 1, y - 1, x + w, y + h), outline=(255, 255, 255))
    for m, (px, py) in enumerate([(x, y), (x + w - 1, y), (x, y + h - 1), (x + w - 1, y + h - 1)]):
        draw_obs(r, canvas, 970, m, px, py)


def tooltip(r, canvas, s, mx, my):
    """0x10042c00: 창 = (글 폭+16+40, 글 높이+16+40), 글 가운데, 자리 = 마우스+(16,16) 을 화면 안으로."""
    d = ImageDraw.Draw(canvas)
    lines = s.replace('$N', '$n').replace('$p', '$n').replace('$P', '$n').split('$n')
    tw = max(d.textlength(t, font=r.font) for t in lines)
    th = 12 * len(lines) + 4 * (len(lines) - 1)
    w, h = int(tw) + 56, th + 56
    x, y = max(1, min(mx + 16, 639 - w)), max(1, min(my + 16, 479 - h))
    frame(r, canvas, x, y, w, h)
    for i, t in enumerate(lines):
        d.text((x + (w - tw) / 2, y + (h - th) / 2 + 16 * i), t, font=r.font, fill=WHITE)


def render(r, c, abis, works, items, deps, jobs, hover=False, tip=None):
    H = lambda o: struct.unpack_from('<H', c, o)[0]  # noqa: E731
    exp = struct.unpack_from('<h', c, 0x30)[0]
    canvas = Image.open(__import__('io').BytesIO(r.g.read('Bgr', '0058.bgr'))).convert('RGBA')

    draw_obs(r, canvas, 471, 28, WIN[0] + 564 + 38, WIN[1] - 20 + 11)                 # CLOSE
    chr_file = r.g.read('Chr', '%04d.chr' % H(4))
    band = struct.unpack_from('<H', chr_file, 40)[0] if chr_file else 0
    draw_obs(r, canvas, 326, band or 62, WIN[0] + 295, WIN[1] + 267)                  # WEAPON 띠

    # 능력치 칸 — 줄 간격 e = (400-116)/25+4 = 15, 오른끝 x = 폭-10
    px, py, pw, ph = STAT_PANEL
    px, py = px + WIN[0], py + WIN[1]
    face = struct.unpack_from('<H', chr_file, 10)[0] if chr_file else 0
    draw_obs(r, canvas, face or 0xe5, 0, px + 41, py + 42)
    e = (ph - 116) // 25 + 4
    job = jobs.get(H(0x16))
    head = [r.t(H(6)), r.t(H(0x10)), r.t(deps[H(0x14)][0]) if H(0x14) < len(deps) else '',
            r.t(job['names'][c[0x12]]) if job else '']
    for k, s in enumerate(head):
        text(r, canvas, s, px, py + 20 + k * e - 6, pw - 10, 12, 2)
    # 수치는 status_calc.py 로 검산한 죠안 값(전투 시작 SOUL 40). 레벨·EXP 는 세이브에서.
    values = {5: '%d' % H(0x2c), 6: '%d' % exp, 8: '1600 / 1600', 10: '40 / 150', 12: '155 / 155',
              15: '100', 16: '28', 17: '130', 19: '800', 20: '180', 21: '19', 22: '165', 23: '130', 24: '135'}
    for k, s in values.items():
        text(r, canvas, s, px, py + 20 + k * e - 6, pw - 10, 12, 2)

    # 상태이상 — 칸 i 가운데 (31 + 53i, 20), Obs 0489 모션 = Sta[번호] 메모리 +4 (0 → EMPTY)
    sx, sy, sw, sh = STATE_PANEL
    for i in range(3):
        draw_obs(r, canvas, 489, 0, WIN[0] + sx + (sw - 40) // 3 // 2 + 10 + ((sw - 40) // 3 + 10) * i,
                 WIN[1] + sy + (sh - 20) // 2 + 10)

    # 장착 어빌리티 3줄 — 칸 수 = Dep +6, 넘는 줄은 꺼진 「없음」
    slots = deps[H(0x14)][1] if H(0x14) < len(deps) else 3
    for i in range(3):
        x, y = row_xy(PASSIVE, i)
        a = H(0x142 + 2 * i)
        if a and a in abis and i < slots:
            ability_row(r, canvas, abis[a], c[0x7a + a], None, exp, x, y, PASSIVE['cw'], PASSIVE['ch'], 11)
        else:
            text(r, canvas, r.t(0), x, y, PASSIVE['cw'], PASSIVE['ch'], 0, 46, 1, 0, dim=i >= slots)

    # 장비 6줄 — 이름 오른쪽 맞춤 −16, 그림 (8,2) Obs 0326
    for i in range(6):
        x, y = row_xy(EQUIP, i)
        if hover and i == 0:
            draw_obs(r, canvas, 471, 1, x - 4, y)
        it = items.get(H(0x4c + 2 * i), items.get(0))
        text(r, canvas, r.t(it['name']), x, y, EQUIP['cw'], EQUIP['ch'], 2, -16, 1, 0)
        if H(0x4c + 2 * i):
            draw_obs(r, canvas, 326, it['pic'] if it['pic'] != 0xffff else it['type'], x + 8, y + 2)

    # 획득한 어빌리티 — 0x10032580 순서(번호 오름차순)
    learned = [a for a in range(200) if c[0x7a + a] != 0xff and a in abis]
    rows = []
    for i, a in enumerate(learned[:LEARNED['rows']]):
        x, y = row_xy(LEARNED, i)
        if hover and i == 0:
            draw_obs(r, canvas, 471, 4, x, y)
        lv, ab = c[0x7a + a], abis[a]
        nxt = ab['levels'].get(lv + 1) if ab['maxlv'] > lv else None
        ability_row(r, canvas, ab, lv, works[nxt][0x30] if nxt else None, exp, x, y, LEARNED['cw'], LEARNED['ch'], 11)
        rows.append((ab, x, y))
    scrollbar(r, canvas, LEARNED)

    # 획득할 수 있는 어빌리티 — 선행 없는 줄만 그린다(선행 있는 두 줄 꼴은 노트 참고)
    job_abs = job['abilities'] if job else []
    for i, a in enumerate([a for a in job_abs if a in abis and c[0x7a + a] == 0xff
                           and all(not p or (c[0x7a + p] != 0xff and c[0x7a + p] >= lv)
                                   for p, lv in ((abis[a]['pre1'], abis[a]['lv1']), (abis[a]['pre2'], abis[a]['lv2'])))][:4]):
        x, y = row_xy(LEARNABLE, i)
        w1 = abis[a]['levels'].get(1)
        ability_row(r, canvas, abis[a], 1, works[w1][0x30] if w1 else None, exp, x, y,
                    LEARNABLE['cw'], LEARNABLE['ch'], 18, label='%s Lv1' % r.t(abis[a]['name']))
    scrollbar(r, canvas, LEARNABLE)

    if tip is not None and tip < len(rows):
        ab, x, y = rows[tip]
        tooltip(r, canvas, r.t(ab['desc']), x + 60, y + 11)
    return canvas.convert('RGB')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('out')
    ap.add_argument('--chr', type=int, default=221)
    ap.add_argument('--sav', default='G3P_II20.sav')
    ap.add_argument('--tooltip', type=int, default=0)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    r = Res(a.game)
    data = load_game(r, a.chr, a.sav)
    render(r, *data).save(os.path.join(a.out, 'status.png'))
    render(r, *data, hover=True).save(os.path.join(a.out, 'status-hover.png'))
    render(r, *data, tip=a.tooltip).save(os.path.join(a.out, 'status-tooltip.png'))
    print('wrote', a.out)


if __name__ == '__main__':
    main()

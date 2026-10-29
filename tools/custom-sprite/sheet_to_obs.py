"""사용자 그림 시트(PNG) → 게임 전투 그림(파트2 꼴 Obs) — 캐릭터 에디터 「외형」의 「사용자 그림」 줄로 입힌다(사용자 요청 menu-103).

시트는 줄마다 한 방향 · 칸마다 한 장이다. 줄 이름은 back(위로 걷는 뒷모습) · front(아래로 걷는 앞모습) · side(왼쪽을 보는 옆모습)이고,
오른쪽은 게임이 옆모습을 뒤집어 그리므로 줄이 있어도 안 쓴다. 지금 만드는 동작은 서기(동작 0 — 걷기 첫 장)와 걷기(동작 1)뿐이고,
게임은 없는 동작(맞기 · 공격 …)을 서기로 대신한다. 다른 동작 줄은 --action 으로 더한다(줄 이름 뒤 @동작번호).

배경은 --key 색(쨍한 핑크 ff00ff 따위, --key-tol 만큼 가까운 색까지)을 투명으로 친다. --key 가 없으면 칸 가장자리에서
이웃과 색 차가 --flood 이하인 칸을 따라 번져 지운다(칠해진 그라데이션 · 빛 번짐 바탕용 — 결과를 --preview 로 꼭 볼 것).

Obs 꼴(ObsSprite · ObsMotionTable 이 읽는 것, Part1Game.ToPart2Obs 가 쓰는 것과 같다):
  u16 0, u16 벌 수, u16 벌 수 − 1, (u16 번호, u32 자리) × 벌     ← 몸짓벌 색인
  u16 모션 수, u16 가장 큰 모션 번호, (u16 번호, u32 자리) × 모션
  모션: u16 번호, u16 0, u16 키 수, u16 길이(틱), u16 0, 키 × (u16 종류, u16 시작, u16 길이, i16 × 10)  — 종류 0 그림(벌, 장)
  벌: u16 번호, u16 장 수, u16 장 수 − 1, u8 투명 색, 팔레트 768, (u16 장, u32 자리) × 장, 장마다 (u16 장, u32 크기, u16 w, u16 h, i16 x, i16 y, 그림)
  그림: u16 레코드 수, 레코드마다 (u16 건너뜀, u8 넷 묶음 수, u8 낱 수, 색인 바이트) — 여기서는 건너뜀 0 으로 칸을 차례로 다 적는다.
모션 번호 = 동작 × 3 + 방향(0 뒷모습 · 1 옆모습 · 2 앞모습).

예: python tools/custom-sprite/sheet_to_obs.py eva.png --rows back:40:280,front:285:515,side:520:755 --col-x 165 --col-w 154 --cols 8 \\
        --id 30000 --name 에바 --flood 14 --height 100 --out 30000_에바.obs --preview preview.png
서기 시트를 더하려면: --add "stand.png|back:0:418@0,side:418:836@0,front:836:1254@0|0|313|4|key=ff00ff"
공격(동작 8, 5번째 장에서 맞음): --add "attack.png|back:0:296@8,side:296:591@8,front:591:887@8|0|222|8|key=ff00ff|anchor=foot|hit=5|ticks=4"
연 2타(동작 13, 3 · 6번째 장): --add "combo.png|back:20:227@13,side:227:394@13,front:394:573@13|0|192|8|key=ff00ff|anchor=foot|hit=3+6|ticks=4"
소리: |sound=20022@4+20026@5 — 파트1 버몬트 칼 공격의 휘두름(22) · 벰(26), 연타는 칠 때마다 20023(버몬트 연과 같다).
만든 파일은 %APPDATA%\\DuelDx\\sprites\\ 에 넣는다(번호 30000~30999, 파일 이름 「번호_이름.obs」).
"""
import argparse
import struct
from collections import deque

from PIL import Image

DIRS = {"back": 0, "side": 1, "front": 2}
FOOT_BELOW = 12          # 발밑이 그림 원점보다 아래로 내려오는 픽셀(파트1 · 파트2 인물 그림이 대개 이렇다)
TRANSPARENT = 0          # 팔레트 0 = 투명


def mask_by_key(cell, key, tol):
    px = cell.load()
    w, h = cell.size
    out = [[False] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y][:3]
            out[y][x] = max(abs(r - key[0]), abs(g - key[1]), abs(b - key[2])) <= tol
    return out


def mask_by_flood(cell, tol):
    """칸 가장자리에서 번지며 이웃과 색 차가 tol 이하인 곳을 배경으로."""
    px = cell.load()
    w, h = cell.size
    bg = [[False] * w for _ in range(h)]
    q = deque()
    for x in range(w):
        q.append((x, 0)); q.append((x, h - 1))
    for y in range(h):
        q.append((0, y)); q.append((w - 1, y))
    for x, y in q:
        bg[y][x] = True
    while q:
        x, y = q.popleft()
        c = px[x, y]
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not bg[ny][nx]:
                d = px[nx, ny]
                if max(abs(d[0] - c[0]), abs(d[1] - c[1]), abs(d[2] - c[2])) <= tol:
                    bg[ny][nx] = True
                    q.append((nx, ny))
    return bg


def largest_blob(fg):
    """앞그림 덩어리 가운데 가장 큰 것의 5% 이상인 것만 — 칸 귀퉁이에 남은 배경 부스러기를 버린다(떨어진 팔 · 무기는 남긴다)."""
    h, w = len(fg), len(fg[0])
    seen = [[False] * w for _ in range(h)]
    blobs = []
    for sy in range(h):
        for sx in range(w):
            if not fg[sy][sx] or seen[sy][sx]:
                continue
            blob, q = [], deque([(sx, sy)])
            seen[sy][sx] = True
            while q:
                x, y = q.popleft()
                blob.append((x, y))
                for nx in (x - 1, x, x + 1):
                    for ny in (y - 1, y, y + 1):
                        if 0 <= nx < w and 0 <= ny < h and fg[ny][nx] and not seen[ny][nx]:
                            seen[ny][nx] = True
                            q.append((nx, ny))
            blobs.append(blob)
    big = max((len(b) for b in blobs), default=0)
    keep = [[False] * w for _ in range(h)]
    for blob in blobs:
        if len(blob) * 20 >= big:
            for x, y in blob:
                keep[y][x] = True
    return keep


def cut(sheet, box, part):
    # 시트 가장자리 2픽셀은 뺀다 — AI 가 그린 시트는 끝 테두리에 바탕이 아닌 선이 남아 마지막 칸에 세로줄로 붙었다.
    x0, y0, x1, y1 = box
    box = (max(x0, 2), max(y0, 2), min(x1, sheet.width - 2), min(y1, sheet.height - 2))
    cell = sheet.crop(box).convert("RGB")
    bg = mask_by_key(cell, part["key"], part["key_tol"]) if part["key"] else mask_by_flood(cell, part["flood"])
    fg = largest_blob([[not v for v in row] for row in bg])
    w, h = cell.size
    rgba = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    src, dst = cell.load(), rgba.load()
    for y in range(h):
        for x in range(w):
            if fg[y][x]:
                dst[x, y] = src[x, y] + (255,)
    return rgba


def rear_foot(img, bb, d):
    """발끝 띠(그림 아래 6%, 적어도 3줄)의 불투명 픽셀 x — 옆모습은 오른쪽 끝(왼쪽을 보는 그림의 뒷발), 그 밖은 가운데."""
    a = img.getchannel("A").load()
    band = max(3, (bb[3] - bb[1]) * 6 // 100)
    xs = [x for y in range(bb[3] - band, bb[3]) for x in range(bb[0], bb[2]) if a[x, y]]
    if not xs:
        return (bb[0] + bb[2]) / 2
    return max(xs) if d == DIRS["side"] else (min(xs) + max(xs)) / 2


def hex_color(s):
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


def parse_rows(text):
    rows = []
    for spec in text.split(","):
        head, _, action = spec.partition("@")
        name, y0, y1 = head.split(":")
        if name not in DIRS:
            continue                                   # right 같은 줄은 안 쓴다(게임이 옆모습을 뒤집는다)
        rows.append((DIRS[name], int(action) if action else 1, int(y0), int(y1)))
    return rows


def parse_add(text, args):
    """--add 「그림|줄|첫칸x|칸너비|칸수[|key=rrggbb 또는 flood=n]」 — 다른 시트(서기 · 공격 …)를 한 그림에 더한다. 시트마다 따로 --height 로 맞춘다."""
    f = text.split("|")
    part = {"sheet": f[0], "rows": parse_rows(f[1]), "col_x": int(f[2]), "col_w": int(f[3]), "cols": int(f[4]),
            "key": args.key, "key_tol": args.key_tol, "flood": args.flood, "anchor": args.anchor, "frames": 0, "hit": [], "ability": 25, "ticks": 0, "sounds": []}
    for opt in f[5:]:
        k, _, v = opt.partition("=")
        if k == "key": part["key"] = hex_color(v)
        elif k == "flood": part["key"], part["flood"] = None, int(v)
        elif k == "tol": part["key_tol"] = int(v)
        elif k == "anchor": part["anchor"] = v
        elif k == "frames": part["frames"] = int(v)
        elif k == "hit": part["hit"] = [int(n) for n in v.split("+")]      # hit=3+6 — 3 · 6 번째 장마다 한 번씩(연속 공격)
        elif k == "ability": part["ability"] = int(v)
        elif k == "sound":                                                  # sound=20022@4+20026@5 — 4번째 장에 소리 20022, 5번째 장에 20026
            part["sounds"] = [tuple(int(n) for n in item.split("@")) for item in v.split("+")]
        elif k == "ticks": part["ticks"] = int(v)
        elif k == "origin": part["origin"] = tuple(int(n) for n in v.split(":"))   # origin=128:200 — 칸 안 원점(3D 렌더, render_3d.py)
        elif k == "px": part["px"] = float(v)                                       # px=230 — 시트에서 인물 키(픽셀), 배율을 재지 않고 이것으로
    return part


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sheet", help="첫 시트 — 「-」 면 없음(시트를 모두 --add 로 줄 때 · 편집기 「사용자 그림 만들기」가 이렇게 부른다)")
    ap.add_argument("--rows", default="", help="방향:위:아래[@동작] 을 쉼표로 — 예 back:40:280,front:285:515,side:520:755")
    ap.add_argument("--cols", type=int, default=8)
    ap.add_argument("--col-x", type=int, default=0, help="첫 칸 왼쪽 x")
    ap.add_argument("--col-w", type=int, default=192, help="칸 너비")
    ap.add_argument("--key", type=hex_color, help="배경 색 rrggbb (예 ff00ff)")
    ap.add_argument("--key-tol", type=int, default=40)
    ap.add_argument("--flood", type=int, default=14)
    ap.add_argument("--add", action="append", default=[], help="다른 시트 — 그림|줄|첫칸x|칸너비|칸수[|key=ff00ff|flood=5|tol=40]")
    ap.add_argument("--anchor", default="body", choices=["body", "cell", "foot"],
                    help="발밑 기준 — body 장마다 몸 가운데 · 발끝(기본), foot 디딘 뒷발(공격처럼 팔이 뻗는 동작 — 첫 장은 body 와 같게), cell 칸 가운데 · 줄의 발 높이")
    ap.add_argument("--height", type=int, default=100, help="게임 안 키(픽셀) — 인물은 대개 65~95")
    ap.add_argument("--ticks", type=int, default=3, help="걷기 한 장의 틱(30틱 = 1초)")
    ap.add_argument("--stand-ticks", type=int, default=8, help="서기(동작 0) 한 장의 틱")
    ap.add_argument("--id", type=int, default=30000)
    ap.add_argument("--name", default="")
    ap.add_argument("--out", required=True)
    ap.add_argument("--preview")
    args = ap.parse_args()

    parts = ([] if args.sheet == "-" else [{"sheet": args.sheet, "rows": parse_rows(args.rows), "col_x": args.col_x, "col_w": args.col_w, "cols": args.cols,
              "key": args.key, "key_tol": args.key_tol, "flood": args.flood, "anchor": args.anchor, "frames": 0, "hit": [], "ability": 25, "ticks": 0, "sounds": []}]) + [parse_add(a, args) for a in args.add]
    if not parts:
        raise SystemExit("시트가 없습니다")

    frames, keyed = [], []                             # frames: (RGBA, 원점 x, 원점 y) — keyed: (동작, 방향, [장 번호])
    for part in parts:
        sheet = Image.open(part["sheet"])
        # 줄마다 칸을 잘라 배경을 지운다 — 발밑은 그 줄 칸들 가운데 가장 낮은 그림 끝, 가로는 칸 가운데.
        cut_rows = []
        for d, action, y0, y1 in part["rows"]:
            used = part["frames"] or part["cols"]          # frames=n 이면 앞 n 장만(서기를 한 장으로 따위)
            cells = [cut(sheet, (part["col_x"] + i * part["col_w"], y0, part["col_x"] + (i + 1) * part["col_w"], y1), part) for i in range(used)]
            boxes = [c.getbbox() for c in cells]
            foot = max(b[3] for b in boxes if b)
            cut_rows.append((d, action, cells, boxes, foot))
        heights = sorted(foot - b[1] for _, _, _, bs, foot in cut_rows for b in bs if b)
        scale = args.height / (part.get("px") or heights[len(heights) // 2])
        print(f"{part['sheet']}: 배율 {scale:.3f}")
        for d, action, cells, boxes, foot in cut_rows:
            ids, row_shift = [], None
            row_top = next(y0 for dd, _, y0, _ in part["rows"] if dd == d)
            for ci, (cell, box) in enumerate(zip(cells, boxes)):
                if not box:
                    continue
                w, h = cell.size
                small = cell.resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)
                a = small.getchannel("A").point(lambda v: 255 if v >= 128 else 0)
                small.putalpha(a)
                bb = small.getbbox()
                if not bb:
                    continue
                # 발밑 점 — 가로는 그 장 몸의 가운데, 세로는 그 장의 발끝(가장 낮은 픽셀). AI 가 그린 시트는 칸 안 자리가 장마다 수십 픽셀씩 달라
                # 칸 가운데를 기준으로 하면 서기가 좌우로 크게 흔들렸다(사용자 보고). --anchor cell 이면 예전처럼 칸 가운데 · 그 줄의 발 높이.
                if part.get("origin"):
                    # 렌더러가 준 칸 안 원점 — cut 이 시트 가장자리 2픽셀을 빼서 맨 왼 칸 · 맨 윗줄이 밀린 만큼 당긴다.
                    ox, oy = part["origin"]
                    fx = (ox - max(0, 2 - (part["col_x"] + ci * part["col_w"]))) * scale
                    fy = (oy - max(0, 2 - row_top)) * scale
                elif part["anchor"] == "cell":
                    fx, fy = small.width / 2, foot * scale
                elif part["anchor"] == "foot":
                    # 디딘 뒷발 — 발끝 띠(그림 아래 6%)의 픽셀 가운데 옆모습은 뒤쪽(왼쪽을 보니 오른쪽 끝), 앞 · 뒷모습은 가운데.
                    # 첫 장(서기 자세)에서 뒷발과 몸 가운데의 차를 재 두고 모든 장에 더해, 첫 장이 서기 · 걷기와 같은 자리에 서게 한다.
                    rear = rear_foot(small, bb, d)
                    if row_shift is None:
                        row_shift = (bb[0] + bb[2]) / 2 - rear
                    fx, fy = rear + row_shift, bb[3]
                else:
                    fx, fy = (bb[0] + bb[2]) / 2, bb[3]
                piece = small.crop(bb)
                ids.append(len(frames))
                frames.append((piece, round(bb[0] - fx), round(bb[1] - fy) + FOOT_BELOW))
            keyed.append((action, d, ids, part["hit"], part["ability"], part["ticks"], part["sounds"]))

    # 모든 장을 한 팔레트(255색 + 투명 0)로.
    total_w = sum(f[0].width for f in frames)
    atlas = Image.new("RGB", (total_w, max(f[0].height for f in frames)), (0, 0, 0))
    x = 0
    for piece, _, _ in frames:
        atlas.paste(piece.convert("RGB"), (x, 0), piece)
        x += piece.width
    pal_img = atlas.quantize(colors=255, method=Image.MEDIANCUT)
    pal = pal_img.getpalette()[:255 * 3]
    palette = bytes([255, 0, 255]) + bytes(pal) + bytes(768 - 3 - len(pal))

    def indexed(piece):
        q = piece.convert("RGB").quantize(palette=pal_img, dither=Image.NONE)
        data, alpha = q.load(), piece.getchannel("A").load()
        out = bytearray()
        for yy in range(piece.height):
            for xx in range(piece.width):
                out.append(TRANSPARENT if alpha[xx, yy] == 0 else data[xx, yy] + 1)
        return bytes(out)

    def payload(px):
        recs, i = [], 0
        while i < len(px):
            n = min(1020, len(px) - i)
            quads, tail = n // 4, n % 4
            recs.append(struct.pack("<HBB", 0, quads, tail) + px[i:i + n])
            i += n
        return struct.pack("<H", len(recs)) + b"".join(recs)

    # 모션 — 동작 0 서기(걷기 첫 장), 동작 1 걷기(장마다 --ticks), 그 밖 동작은 줄 그대로.
    motions = []
    # 키: (종류, 시작, 길이, 인자 10) — 종류 0 그림(벌, 장), 6 타격(어빌리티 25 접근 · 26 원거리, 분석-모션 ba-10).
    for action, d, ids, hit, ability, ticks, sounds in keyed:
        if not ids:
            continue
        if action == 1 and not any(k[0] == 0 and k[1] == d for k in keyed):
            motions.append((0 * 3 + d, 1, [(0, 0, 1, (0, ids[0]))]))
        t = ticks or (args.stand_ticks if action == 0 else args.ticks)
        keys = [(0, k * t, t, (0, f)) for k, f in enumerate(ids)]
        for n in hit:                                  # hit=n(+m…) — 그 장이 서는 틱마다 판정(연 2타 = 동작 13 · 3타 = 14 는 타격 키 수만큼 친다)
            if 1 <= n <= len(ids):
                keys.append((6, (n - 1) * t, 1, (ability,)))
        for snd, n in sounds:                          # 소리 키(종류 1, 인자 0 = 소리 번호 — 파트1 소리는 20000 + 번호)
            if 1 <= n <= len(ids):
                keys.append((1, (n - 1) * t, 1, (snd,)))
        keys.sort(key=lambda k: (k[1], k[0]))
        motions.append((action * 3 + d, t * len(ids), keys))
    motions.sort()

    out = bytearray()
    w = lambda fmt, *v: out.extend(struct.pack(fmt, *v))
    w("<HHH", 0, 1, 0)
    sub_table = len(out); w("<HI", 0, 0)
    w("<HH", len(motions), max(m[0] for m in motions))
    motion_table = len(out)
    for mid, _, _ in motions:
        w("<HI", mid, 0)
    for i, (mid, length, keys) in enumerate(motions):
        struct.pack_into("<I", out, motion_table + 6 * i + 2, len(out))
        w("<HHHHH", mid, 0, len(keys), length, 0)
        for kind, start, ln, a in keys:
            w("<HHH", kind, start, ln)
            w("<10h", *(list(a) + [0] * (10 - len(a))))
    struct.pack_into("<I", out, sub_table + 2, len(out))
    w("<HHH", 0, len(frames), len(frames) - 1)
    out.append(TRANSPARENT)
    out.extend(palette)
    slot_table = len(out)
    for f in range(len(frames)):
        w("<HI", f, 0)
    for f, (piece, ox, oy) in enumerate(frames):
        struct.pack_into("<I", out, slot_table + 6 * f + 2, len(out))
        body = payload(indexed(piece))
        w("<HIHHhh", f, len(body), piece.width, piece.height, ox, oy)
        out.extend(body)
    with open(args.out, "wb") as fh:
        fh.write(out)
    print(f"{args.out}: 장 {len(frames)} · 모션 {[m[0] for m in motions]}")

    if args.preview:                                   # 핑크 바탕에 장을 늘어놓아 배경이 잘 지워졌는지 본다(2배)
        cols = max(len(k[2]) for k in keyed)
        cw = max(p.width for p, _, _ in frames) + 8
        ch = max(p.height for p, _, _ in frames) + 8
        prev = Image.new("RGB", (cw * cols, ch * len(keyed)), (255, 0, 255))
        left = -min(ox for _, ox, _ in frames) + 4
        top = -min(oy for _, _, oy in frames) + 4
        cw = max(left + ox + p.width for p, ox, _ in frames) + 4
        ch = max(top + oy + p.height for p, _, oy in frames) + 4
        prev = Image.new("RGB", (cw * cols, ch * len(keyed)), (255, 0, 255))
        for r, (_, _, ids, *_rest) in enumerate(keyed):     # 원점(발밑)을 칸마다 같은 자리에 — 게임에서처럼 흔들림이 보인다
            for c, f in enumerate(ids):
                piece, ox, oy = frames[f]
                prev.paste(piece.convert("RGB"), (c * cw + left + ox, r * ch + top + oy), piece)
        prev.resize((prev.width * 2, prev.height * 2), Image.NEAREST).save(args.preview)


if __name__ == "__main__":
    main()

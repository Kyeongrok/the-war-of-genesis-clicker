"""AI 가 그린 동작 시트를 사용자 그림 만들기가 바로 자를 꼴로 다듬는다.

AI 시트는 칸이 고르지 않고, 방향이 줄 중간에 바뀌고, 구석에 도구 표시(Gemini 반짝이)가 찍힌다. 이것은
1. 마젠타 바탕에서 인물 덩어리(이어진 픽셀)를 찾아 줄은 발밑 높이로 묶고, 반짝이 · 티끌 · 머리 위 잔상은 버리고,
2. 고른 칸만 남겨 뒤집을 칸은 좌우로 뒤집은 뒤,
3. 모든 칸을 같은 크기 · 같은 발밑 줄 · 가운데 맞춤으로 다시 늘어놓는다(줄 = 뒷 · 옆 · 앞모습, 바탕 #FF00FF).

    python tidy_ai_sheet.py in.png out.png [--also back.png] [--row "1/0:0,1,2,3" --row "1:0,1,2,~3" --row "2:..."]

--row "[파일/]원본줄:칸,칸,…" 를 내보낼 차례대로 준다(뒷 · 옆 · 앞). 칸 앞 ~ 는 좌우 뒤집기. 원본 줄 · 칸 번호는 0부터.
파일 번호 0 은 in.png, 1 부터는 --also 로 준 시트(뒷모습만 따로 받은 한 줄 시트 따위).
--row 를 안 주면 찾은 줄 · 칸을 그대로 늘어놓는다. --dry 는 찾은 줄 · 칸만 찍는다.
"""
import argparse
import numpy as np
from PIL import Image
from scipy import ndimage

MAGENTA = np.array([255, 0, 255])


def figure_mask(img: np.ndarray) -> np.ndarray:
    rgb = img[..., :3].astype(int)
    far = np.sqrt(((rgb - MAGENTA) ** 2).sum(-1)) > 110
    if img.shape[-1] == 4:
        far &= img[..., 3] > 0
    # Gemini 가 오른쪽 아래 구석에 찍는 연분홍 반짝이 — 은빛 칼(회색)과 달리 R · B 가 G 보다 뚜렷이 크다
    h, w = far.shape
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    pale_pink = (r > 200) & (b > 200) & (r - g > 25) & (b - g > 25)
    corner = np.zeros_like(far)
    corner[int(h * 0.88):, int(w * 0.88):] = True
    far &= ~(pale_pink & corner)
    return far


def find_cells(mask: np.ndarray):
    """인물 덩어리로 칸을 찾는다 — 빈 줄 · 빈 칸으로 가르면 다리 사이 틈에서 한 인물이 둘로 갈리고, 뻗은 칼이나 잔상이 이웃 칸을 잇는다.
    큰 덩어리(가장 키 큰 것의 40% 넘는 키) 하나가 인물 하나다. 작은 조각은 그 인물 칸 안이고 머리보다 아래면 붙이고(떨어진 칼 · 불꽃),
    머리 위(잔상)거나 어디에도 안 들면 버린다. 줄은 발밑 높이로 묶는다.
    돌려주는 것: 줄마다 칸 (왼, 위, 오른, 아래, 픽셀 수, 덩어리 번호들) 목록, 덩어리 번호 그림."""
    labels, n = ndimage.label(mask, structure=np.ones((3, 3)))
    if n == 0:
        return [], labels
    idx = range(1, n + 1)
    sizes = ndimage.sum(mask, labels, idx)
    boxes = ndimage.find_objects(labels)
    heights = [bx[0].stop - bx[0].start for bx in boxes]
    tall = max(heights)
    figs = [i for i in range(n) if heights[i] >= tall * 0.4 and sizes[i] >= 200]
    box = {i: (boxes[i][1].start, boxes[i][0].start, boxes[i][1].stop, boxes[i][0].stop) for i in figs}
    cells = {i: [*box[i], int(sizes[i]), [i + 1]] for i in figs}
    centers = ndimage.center_of_mass(mask, labels, idx)
    for j in range(n):
        if j in cells or sizes[j] < 20:
            continue
        cy, cx = centers[j]
        for i in figs:
            l, t, r, b = box[i]
            pad = (r - l) * 0.15
            if l - pad <= cx <= r + pad and t <= cy <= b:
                c = cells[i]
                c[0], c[1] = min(c[0], boxes[j][1].start), min(c[1], boxes[j][0].start)
                c[2], c[3] = max(c[2], boxes[j][1].stop), max(c[3], boxes[j][0].stop)
                c[4] += int(sizes[j])
                c[5].append(j + 1)
                break
    found = sorted(cells.values(), key=lambda c: c[3])
    med = float(np.median([c[3] - c[1] for c in found]))
    rows, last = [], None
    for c in found:
        if last is None or c[3] - last > med * 0.5:
            rows.append([])
        rows[-1].append(tuple(c))
        last = c[3]
    return [sorted(r, key=lambda c: c[0] + c[2]) for r in rows], labels


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("out")
    ap.add_argument("--also", action="append", default=[])
    ap.add_argument("--row", action="append", default=[])
    ap.add_argument("--margin", type=int, default=16)
    ap.add_argument("--dry", action="store_true")
    a = ap.parse_args()

    sheets = []
    for k, path in enumerate([a.src, *a.also]):
        img = np.array(Image.open(path).convert("RGBA"))
        rows, labels = find_cells(figure_mask(img))
        sheets.append((img, labels, rows))
        for i, r in enumerate(rows):
            print(f"{k}/줄 {i}: 칸 {len(r)} — " + ", ".join(f"{c[2]-c[0]}x{c[3]-c[1]}" for c in r))
    if a.dry:
        return

    if a.row:
        plan = []
        for spec in a.row:
            src_row, cols = spec.split(":")
            k, _, src_row = src_row.rpartition("/")
            plan.append([(int(k or 0), int(src_row), int(c.lstrip("~")), c.startswith("~")) for c in cols.split(",")])
    else:
        plan = [[(0, ri, ci, False) for ci in range(len(r))] for ri, r in enumerate(sheets[0][2])]

    # 시트마다 인물 키가 다르다(한 줄로 따로 받은 뒷모습은 더 크다) — 줄의 가장 큰 키를 in.png 줄의 가장 큰 키에 맞춘다
    pieces = []
    ref_h = max(rows[ri][ci][3] - rows[ri][ci][1] for line in plan for k, ri, ci, _ in line if k == 0 for rows in [sheets[0][2]])         if any(k == 0 for line in plan for k, *_ in line) else None
    for line in plan:
        out_line = []
        for k, ri, ci, flip in line:
            img, labels, rows = sheets[k]
            l, t, r, b, _, ids = rows[ri][ci]
            piece = img[t:b, l:r].copy()
            piece[~np.isin(labels[t:b, l:r], ids)] = [255, 0, 255, 255]
            piece[..., 3] = 255
            if flip:
                piece = piece[:, ::-1]
            out_line.append(piece)
        line_h = max(p.shape[0] for p in out_line)
        ref_h = ref_h or line_h
        if abs(line_h - ref_h) > ref_h * 0.05:
            f = ref_h / line_h
            out_line = [np.array(Image.fromarray(p).resize((max(1, round(p.shape[1] * f)), max(1, round(p.shape[0] * f))), Image.NEAREST))
                        for p in out_line]
        pieces.append(out_line)

    cw = max(p.shape[1] for l in pieces for p in l) + 2 * a.margin
    ch = max(p.shape[0] for l in pieces for p in l) + 2 * a.margin
    cols = max(len(l) for l in pieces)
    sheet = np.zeros((ch * len(pieces), cw * cols, 4), np.uint8)
    sheet[:] = [255, 0, 255, 255]
    for ri, line in enumerate(pieces):
        for ci, p in enumerate(line):
            y = ri * ch + ch - a.margin - p.shape[0]   # 발밑 줄을 맞춘다
            x = ci * cw + (cw - p.shape[1]) // 2
            sheet[y:y + p.shape[0], x:x + p.shape[1]] = p
    Image.fromarray(sheet[..., :3]).save(a.out)
    print(f"→ {a.out}: {len(pieces)}줄 × {cols}칸, 칸 {cw}x{ch}")


if __name__ == "__main__":
    main()

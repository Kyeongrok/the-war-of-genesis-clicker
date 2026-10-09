"""원본 게임 폴더가 없는 사용자가 내려받는 자산 꾸러미를 만든다 — 게임 쪽은 `duel-dx/AssetPack.cs`.

    python tools/make_asset_pack.py --plan                                  # 장마다 얼마나 되는지만 본다
    python tools/make_asset_pack.py <내보낼 폴더> [--game <원본 게임 폴더>]   # 꾸러미를 만든다

게임을 처음 켤 때 원본 게임 폴더를 「모른다」고 한 사용자는 자산을 GitHub 릴리즈에서 받는다. 받는 것은 세 갈래다.

  * 바탕(`base-NN.zip`) — 켤 때 폴더를 훑어 읽는 것들(`assets/originals.txt` 의 목록)과 풀어 둔 기술 영상(`effects/mov`).
    이것이 있어야 게임이 켜지므로 처음 한 번 진행 창을 띄우고 받는다.
  * 낱장(`files`) — 음악 · 전투 맵 · 인물 그림 · 모세스 그림 · 큰 효과음(`tools/asset_pack_order.txt`). 파일 하나씩 올려 두고 뒤에서 받는다.
    게임이 아직 없는 파일을 찾으면 그것부터 받는다.
  * 대사 음성(`voices-tNN.zip`) — 원본 `BGM` 폴더의 음성. 장마다 한 덩이(`--game` 을 줘야 만든다).

낱장과 음성에는 **장**(`tier`)이 붙는다 — 연대표(`EPS/Episode.dat`)의 줄 번호(0~14, 한 줄에 Episode 4 · Episode 5 한 챕터씩)다.
챕터 → 장소 → 필드 · 전투 → 맵 · 음악 · 그림 · 인물 · 음성을 따라가 그 파일을 처음 쓰는 줄을 적는다. 게임은 지금 들어간 줄 + 3 까지만
미리 받는다(끝까지 안 할 사람이 다 받지 않게 — 사용자 요청 menu-22). 어느 챕터에서도 안 닿는 파일(큰 효과음 · 군단 병사 그림 따위,
어느 장에서든 나올 수 있다)은 0장 맨 뒤에 둔다.

목록(`pack.json`) · 바탕 · 음성은 릴리즈 `assets-base-1` 에, 낱장은 릴리즈 하나에 파일 1,000개까지라 `assets-pack-1` · `assets-pack-2` … 로 나눠 올린다
(`pack.json` 의 `release`). 내보낼 폴더에 릴리즈 이름의 폴더가 생기고, 올리는 법은 끝에 찍힌다.
"""
import hashlib
import json
import os
import shutil
import struct
import sys
import zipfile

ROOT = os.path.normpath(os.path.join(os.path.dirname(__file__), '..'))
ASSETS = os.path.join(ROOT, 'assets')
DATA = os.path.join(ASSETS, 'data')
UNUSED = 99
PER_RELEASE = 900
ZIP_LIMIT = 48 << 20


def read(*parts):
    path = os.path.join(*parts)
    return open(path, 'rb').read() if os.path.exists(path) else None


class Words:
    def __init__(self, b, o=0):
        self.b, self.o = b, o

    def w(self):
        v = struct.unpack_from('<h', self.b, self.o)[0]
        self.o += 2
        return v

    def many(self, n):
        return [self.w() for _ in range(n)]


def events(r):
    """필드 · 챕터 스크립트 — 사건마다 (코드, 인자 여덟) 행동들."""
    acts = []
    for _ in range(r.w()):
        r.w()
        n = r.w()
        r.o += 18 * n
        for _ in range(r.w()):
            acts.append((r.w(), r.many(8)))
    return acts


class Refs:
    """한 챕터가 닿는 것들 — `assets` 밑 자리와 음성 번호."""

    def __init__(self):
        self.files, self.voices, self.fields, self.battles = set(), set(), set(), set()

    def bgm(self, n):
        if n > 1: self.files.add('bgm/%04d.bgm' % n); self.voices.add(n)

    def bgr(self, n):
        if n > 0: self.files.add('moses/bgr/%04d.bgr' % n)

    def obs(self, n):
        if 0 < n < 10000: self.files.add('moses/obs/%04d.obs' % n)

    def obj(self, n):
        """전투판의 물체 — 제 발자국 맵(`Obt`)을 따로 읽는다."""
        b = read(DATA, 'Obj', '%04d.obj' % n) if n > 0 else None
        if b and len(b) >= 14: self.files.add('maps/%04d.obt' % struct.unpack_from('<H', b, 12)[0])

    def person(self, code):
        """필드 · 통신 화면의 인물 — `.chr` 이 가리키는 그림과 얼굴."""
        b = read(DATA, 'Chr', '%04d.chr' % code)
        if b and len(b) >= 12:
            for off in (8, 10): self.obs(struct.unpack_from('<H', b, off)[0])

    def script(self, acts, todo_f, todo_b):
        for code, a in acts:
            if code == 6: todo_f.append(a[0])
            elif code == 10: todo_b.append(a[0])
            elif code in (901, 903, 904, 905, 906, 907, 908, 909): self.bgr(a[1])
            elif code == 302: self.obs(a[0])
            elif code == 512: self.bgm(a[0])
            elif code in (600, 601, 602): self.voices.add(a[2])
            elif code == 603: self.voices.add(a[1])
            elif code == 609: self.voices.add(a[3])

    def field(self, fid, todo_f, todo_b):
        b = read(DATA, 'Fld', '%04d.fld' % fid)
        if b is None: return
        r = Words(b)
        head = r.many(8)
        self.bgr(head[1]); self.bgm(head[5])
        for _ in range(head[6]): self.obs(r.many(8)[1])
        n = r.w(); r.w(); r.o += 16 * n
        n = r.w(); r.w()
        for _ in range(n): self.person(r.many(5)[1])
        self.script(events(r), todo_f, todo_b)

    def battle(self, bid, todo_f, todo_b):
        b = read(DATA, 'Btl', '%04d.btl' % bid)
        if b is None: return
        head = struct.unpack_from('<12h', b, 0)
        m = read(DATA, 'Map', '%04d.map' % head[1])
        if m:
            self.files.add('maps/%04d.obt' % struct.unpack_from('<H', m, 2)[0])
            # 맵이 놓는 물체(문 · 장식) — WarOfGenesis.Assets/BattleFile.cs 의 ObjectsOfMap.
            mo = 6 + (4 if struct.unpack_from('<H', m, 0)[0] >= 3 else 0)
            for i in range(struct.unpack_from('<H', m, mo)[0]):
                if mo + 2 + 22 * i + 22 <= len(m): self.obj(struct.unpack_from('<h', m, mo + 2 + 22 * i + 4)[0])
        self.bgm(head[8])
        o = 24
        for _ in range(max(0, head[10])):
            if o + 29 > len(b): return
            self.files.add('chr:%04d' % struct.unpack_from('<H', b, o + 2)[0])
            o += 29
        # 판에 놓인 물체와 전투 이벤트 — tools/re/btl_events.py 의 배치.
        for i in range(struct.unpack_from('<H', b, o)[0]): self.obj(struct.unpack_from('<h', b, o + 4 + 22 * i + 2)[0])
        o += 4 + 22 * struct.unpack_from('<H', b, o)[0]
        o += 4 + 12 * struct.unpack_from('<H', b, o)[0]
        n = struct.unpack_from('<H', b, o)[0]; o += 2
        for _ in range(n):
            o += 4
            o += 2 + 18 * struct.unpack_from('<H', b, o)[0]
            n2 = struct.unpack_from('<H', b, o)[0]; o += 2
            for _ in range(n2):
                code = struct.unpack_from('<H', b, o)[0]
                a = struct.unpack_from('<8h', b, o + 2); o += 18
                if code == 6: todo_f.append(a[0])
                elif code == 10: todo_b.append(a[0])
                elif code == 512: self.bgm(a[0])
                elif code == 500: self.voices.add(a[0])
                elif code in (600, 601): self.voices.add(a[3])

    def chapter(self, cid):
        b = read(ASSETS, 'moses', 'chp', '%04d.chp' % cid)
        if b is None: return self
        r = Words(b)
        head = r.many(24)
        self.bgr(head[1]); self.bgm(head[2])
        n = r.w(); r.w()
        for _ in range(n): self.obs(r.many(6)[1])                    # 성도 점
        n = r.w(); r.w()
        for _ in range(n): self.person(r.many(15)[1])                # 통신 페이지 인물
        n = r.w(); r.w()
        for _ in range(n): self.bgr(r.many(33)[32])                  # 항성계 배경
        n = r.w(); r.w()
        for _ in range(n):
            p = r.many(42)
            for k in (1, 34, 36, 38): self.obs(p[k])                 # 성도 그림 · 구체 · 줌 연출
        n = r.w(); r.w()
        todo_f, todo_b = [], []
        for _ in range(n):
            v = r.many(10)[2]
            if 0 < v < 10000: todo_b.append(v)
            elif 10000 <= v < 20000: todo_f.append(v - 10000)
        n = r.w(); r.w(); r.o += 4 * n
        try:
            self.script(events(r), todo_f, todo_b)
        except struct.error:
            pass
        while todo_f or todo_b:
            try:
                if todo_f:
                    fid = todo_f.pop()
                    if fid not in self.fields: self.fields.add(fid); self.field(fid, todo_f, todo_b)
                else:
                    bid = todo_b.pop()
                    if bid not in self.battles: self.battles.add(bid); self.battle(bid, todo_f, todo_b)
            except struct.error:
                pass                                                 # 배치가 다른 옛 파일 — 건너뛴다
        return self


def episode_rows():
    """연대표 줄마다 그 줄의 챕터들(왼쪽 Episode 4 · 오른쪽 Episode 5)."""
    b = read(DATA, 'EPS', 'Episode.dat')
    rows = []
    for r in range(struct.unpack_from('<H', b, 2)[0]):
        words = struct.unpack_from('<20h', b, 4 + 40 * r)
        rows.append([c for c in (words[9], words[18]) if c > 0])
    return rows


def plan(game):
    """(낱장 → 장, 음성 번호 → 장, 줄마다 챕터)."""
    lazy = [l.strip() for l in open(os.path.join(ROOT, 'tools', 'asset_pack_order.txt'), encoding='utf-8') if l.strip()]
    lazy = [n for n in lazy if os.path.exists(os.path.join(ASSETS, *n.split('/')))]
    chr_files = {}
    for n in lazy:
        if n.startswith('characters/'): chr_files.setdefault('chr:' + n.split('/')[1][:4], []).append(n)
    music = {int(f[:4]) for f in os.listdir(os.path.join(ASSETS, 'bgm')) if f[:4].isdigit()}
    in_game = set()
    if game:
        in_game = {int(f[:4]) for f in os.listdir(os.path.join(game, 'BGM')) if f[:4].isdigit() and f.lower().endswith('.bgm')} - music

    tiers, voices, rows = {}, {}, episode_rows()
    for tier, chapters in enumerate(rows):
        for cid in chapters:
            refs = Refs().chapter(cid)
            for f in refs.files:
                for n in chr_files.get(f, [f]): tiers.setdefault(n, tier)
            for v in refs.voices:
                if v in in_game: voices.setdefault(v, tier)
    return {n: tiers.get(n, UNUSED) for n in lazy}, voices, rows


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''): h.update(block)
    return h.hexdigest()


def zips(out, stem, items):
    """(자리 안 이름, 원본 경로)들을 ZIP_LIMIT 안팎의 덩이로 묶는다 — 덩이 설명 목록을 돌려준다."""
    parts, group, size = [], [], 0
    for item in items:
        group.append(item)
        size += os.path.getsize(item[1])
        if size >= ZIP_LIMIT: parts.append(group); group, size = [], 0
    if group: parts.append(group)
    made = []
    for i, group in enumerate(parts):
        name = '%s-%02d.zip' % (stem, i + 1)
        path = os.path.join(out, name)
        with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as z:
            for arc, src in group: z.write(src, arc)
        made.append({'asset': name, 'size': os.path.getsize(path), 'sha256': sha(path), 'count': len(group)})
    return made


def main():
    args = sys.argv[1:]
    game = args[args.index('--game') + 1] if '--game' in args else None
    tiers, voices, rows = plan(game)

    size = lambda n: os.path.getsize(os.path.join(ASSETS, *n.split('/')))
    print('장  챕터          낱장      MB   음성')
    for tier in list(range(len(rows))) + [UNUSED]:
        names = [n for n, t in tiers.items() if t == tier]
        label = ' · '.join('%04d' % c for c in rows[tier]) if tier < len(rows) else '(안 닿음)'
        print('%2d  %-12s %5d %7.1f  %5d' % (tier, label, len(names), sum(map(size, names)) / 1048576, sum(1 for t in voices.values() if t == tier)))
    if '--plan' in args or not args:
        return

    out = args[0]
    first = os.path.join(out, 'assets-base-1')        # 목록 · 바탕 · 음성이 올라가는 릴리즈
    os.makedirs(first, exist_ok=True)

    # 바탕 — 켤 때 훑어 읽는 것들과 풀어 둔 기술 영상.
    base = [l.strip() for l in open(os.path.join(ASSETS, 'originals.txt'), encoding='utf-8') if l.strip() and l[0] != '#']
    for folder, _, files in os.walk(os.path.join(ASSETS, 'effects')):
        base += [os.path.relpath(os.path.join(folder, f), ASSETS).replace(os.sep, '/') for f in files if f.lower().endswith('.png')]
    base = sorted(set(base))
    pack = {'base': zips(first, 'base', [(n, os.path.join(ASSETS, *n.split('/'))) for n in base])}

    # 낱장 — 장 차례, 같은 장 안에서는 받는 차례 파일의 차례.
    files, seen = [], set()
    for i, (name, tier) in enumerate(sorted(tiers.items(), key=lambda kv: (kv[1] % UNUSED, kv[1] == UNUSED))):
        tier %= UNUSED
        # 릴리즈 파일 이름은 ASCII 만 — 인물 폴더의 한글 이름은 뺀다(characters/0221_죠안/0338.obs → characters_0221__0338.obs).
        asset = ''.join(ch for ch in name.replace('/', '_') if ch.isascii() and (ch.isalnum() or ch in '._-'))
        assert asset not in seen, asset
        seen.add(asset)
        release = 'assets-pack-%d' % (1 + i // PER_RELEASE)
        src = os.path.join(ASSETS, *name.split('/'))
        os.makedirs(os.path.join(out, release), exist_ok=True)
        shutil.copyfile(src, os.path.join(out, release, asset))
        files.append({'name': name, 'asset': asset, 'size': os.path.getsize(src), 'sha256': sha(src), 'tier': tier, 'release': release})
    pack['files'] = files

    pack['voices'] = []
    for tier in sorted(set(voices.values())):
        ids = sorted(v for v, t in voices.items() if t == tier)
        for part in zips(first, 'voices-t%02d' % tier, [('%04d.bgm' % v, os.path.join(game, 'BGM', '%04d.bgm' % v)) for v in ids]):
            pack['voices'].append(dict(part, tier=tier))

    with open(os.path.join(first, 'pack.json'), 'w', encoding='utf-8') as f:
        json.dump(pack, f, indent=0)
    total = lambda rows_: sum(x['size'] for x in rows_) >> 20
    print('바탕 %d덩이 %dMB · 낱장 %d개 %dMB · 음성 %d덩이 %dMB → %s' % (
        len(pack['base']), total(pack['base']), len(files), total(files), len(pack['voices']), total(pack['voices']), out))
    print('올리기:')
    for release in ['assets-base-1'] + sorted({x['release'] for x in files}):
        print('  gh release create %s --prerelease --latest=false --title "자산 꾸러미" --notes "게임이 받는 파일" %s/%s/*' % (release, out, release))


if __name__ == '__main__':
    main()

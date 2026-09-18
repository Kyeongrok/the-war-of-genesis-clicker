"""챕터 하나가 닿는 자료(필드·전투·맵·대사·배경·음악·인물 그림)를 assets 로 묶는다.

    python bundle_chapter.py <스크래치의 g 폴더> <챕터번호> [<챕터번호> ...]

게임 폴더는 안 건드린다 — pak_extract.py 로 미리 풀어 둔 `g/` 에서만 베낀다.
"""
import os
import shutil
import struct
import sys

REPO = r'C:\Users\ocean\git\the-war-of-genesis-clicker'
G = sys.argv[1]
ARGS = sys.argv[2:]
CHAPTERS = [int(a) for a in ARGS if not a.startswith('f')]
START_FIELDS = [int(a[1:]) for a in ARGS if a.startswith('f')]   # f17 = 필드 17 부터

copied = {}


def take(kind, src_rel, dst_rel):
    src = os.path.join(G, src_rel)
    dst = os.path.join(REPO, dst_rel)
    if not os.path.exists(src) or os.path.exists(dst):
        return os.path.exists(dst)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    shutil.copy(src, dst)
    copied[kind] = copied.get(kind, 0) + os.path.getsize(src)
    return True


def words(b, o, n):
    return [struct.unpack_from('<h', b, o + 2 * i)[0] for i in range(n)]


def read_field(fid):
    """필드 하나를 묶고, 이어지는 필드·전투 번호를 돌려준다."""
    src = os.path.join(G, 'Fld', '%04d.fld' % fid)
    if not os.path.exists(src):
        return [], []
    take('fld', 'Fld/%04d.fld' % fid, 'assets/data/Fld/%04d.fld' % fid)
    take('tlf', 'Tlk/%04d.tlf' % fid, 'assets/data/Tlk/%04d.tlf' % fid)
    b = open(src, 'rb').read()
    o = [0]

    def W():
        v = struct.unpack_from('<h', b, o[0])[0]
        o[0] += 2
        return v

    head = [W() for _ in range(8)]
    if head[1] > 0:
        take('bgr', 'Bgr/%04d.bgr' % head[1], 'assets/moses/bgr/%04d.bgr' % head[1])
    if head[5] > 0:
        take('bgm', 'BGM/%04d.bgm' % head[5], 'assets/bgm/%04d.bgm' % head[5])
    for _ in range(head[6]):
        obj = [W() for _ in range(8)]
        if obj[1] > 0:
            take('obs', 'Obs/%04d.obs' % obj[1], 'assets/moses/obs/%04d.obs' % obj[1])
    n = W(); W(); o[0] += 16 * n
    n = W(); W()
    for _ in range(n):
        person = [W() for _ in range(5)]
        chr_path = os.path.join(G, 'Chr', '%04d.chr' % person[1])
        if os.path.exists(chr_path):
            take('chr', 'Chr/%04d.chr' % person[1], 'assets/data/Chr/%04d.chr' % person[1])
            cb = open(chr_path, 'rb').read()
            for off, kind in ((8, 'sprite'), (10, 'face')):
                obs = struct.unpack_from('<H', cb, off)[0]
                if obs > 0:
                    take(kind, 'Obs/%04d.obs' % obs, 'assets/moses/obs/%04d.obs' % obs)
    fields, battles = [], []
    events = W()
    for _ in range(events):
        W()
        nc = W(); o[0] += 18 * nc
        na = W()
        for _ in range(na):
            code = W()
            args = [W() for _ in range(8)]
            if code == 6:
                fields.append(args[0])
            elif code == 10:
                battles.append(args[0])
            elif code == 512 and args[0] > 0:
                take('bgm', 'BGM/%04d.bgm' % args[0], 'assets/bgm/%04d.bgm' % args[0])
            elif code == 302 and 0 < args[0] < 10000:
                take('obs', 'Obs/%04d.obs' % args[0], 'assets/moses/obs/%04d.obs' % args[0])
    return fields, battles


def read_battle(bid):
    src = os.path.join(G, 'Btl', '%04d.btl' % bid)
    if not os.path.exists(src):
        return []
    take('btl', 'Btl/%04d.btl' % bid, 'assets/data/Btl/%04d.btl' % bid)
    take('tlb', 'Tlk/%04d.tlb' % bid, 'assets/data/Tlk/%04d.tlb' % bid)
    b = open(src, 'rb').read()
    head = words(b, 0, 10)
    map_src = os.path.join(G, 'Map', '%04d.map' % head[1])
    if os.path.exists(map_src):
        obt = struct.unpack_from('<H', open(map_src, 'rb').read(), 2)[0]
        take('obt', 'Obt/%04d.obt' % obt, 'assets/maps/%04d.obt' % obt)
    if head[8] > 0:
        take('bgm', 'BGM/%04d.bgm' % head[8], 'assets/bgm/%04d.bgm' % head[8])
    return []


def bundle_chain(fields, battles):
    """필드·전투 번호에서 시작해 이어지는 것을 모두 묶는다."""
    seen_f, seen_b = set(), set()
    while fields or battles:
        while fields:
            fid = fields.pop()
            if fid in seen_f:
                continue
            seen_f.add(fid)
            more_f, more_b = read_field(fid)
            fields += more_f
            battles += more_b
        while battles:
            bid = battles.pop()
            if bid in seen_b:
                continue
            seen_b.add(bid)
            read_battle(bid)
    return seen_f, seen_b


for chapter in CHAPTERS:
    path = os.path.join(REPO, 'assets', 'moses', 'chp', '%04d.chp' % chapter)
    if not os.path.exists(path):
        take('chp', 'Chp/%04d.chp' % chapter, 'assets/moses/chp/%04d.chp' % chapter)
    b = open(path, 'rb').read()
    o = [0]

    def W():
        v = struct.unpack_from('<h', b, o[0])[0]
        o[0] += 2
        return v

    head = [W() for _ in range(24)]
    if head[1] > 0:
        take('bgr', 'Bgr/%04d.bgr' % head[1], 'assets/moses/bgr/%04d.bgr' % head[1])
    if head[2] > 0:
        take('bgm', 'BGM/%04d.bgm' % head[2], 'assets/bgm/%04d.bgm' % head[2])
    for size in (6, 15, 33, 42):
        n = W(); W()
        for _ in range(n):
            W_ = [W() for _ in range(size)]
            if size == 33 and W_[32] > 0:
                take('bgr', 'Bgr/%04d.bgr' % W_[32], 'assets/moses/bgr/%04d.bgr' % W_[32])
    n = W(); W()
    places = [[W() for _ in range(10)] for _ in range(n)]

    fields, battles = [], []
    for place in places:
        v = place[2]
        if 0 < v < 10000:
            battles.append(v)
        elif 10000 <= v < 20000:
            fields.append(v - 10000)

    seen_f, seen_b = bundle_chain(fields, battles)
    print('chapter %d: fields %d, battles %d' % (chapter, len(seen_f), len(seen_b)))

if START_FIELDS:
    f, b = bundle_chain(list(START_FIELDS), [])
    print('from fields: fields %d, battles %d' % (len(f), len(b)))

print('added:', {k: '%d KB' % (v // 1024) for k, v in sorted(copied.items())})

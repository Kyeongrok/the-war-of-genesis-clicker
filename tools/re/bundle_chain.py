"""챕터 하나가 닿는 것을 빠짐없이 묶는다 — bundle_chapter.py 위에 얹는 마무리 도구.

    python tools/re/bundle_chain.py <풀어 둔 g 폴더> <Chp 번호> [<Chp 번호> ...]

bundle_chapter.py 는 챕터 장소에서 출발한 필드·전투를 묶지만 **전투 이벤트(행동 6 필드 · 10 다음 전투)가 잇는 것**과
**필드 전환(901·903~909)이 부르는 배경**, 그리고 전투 인물의 `assets/characters/` 폴더(export_battle_chars.py)는 안 챙긴다.
이 도구는 그 셋을 마저 한다. 게임 폴더는 건드리지 않고 `g/` 에서만 베낀다.
"""
import glob
import os
import shutil
import struct
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, '..', '..'))
G = sys.argv[1]
CHAPTERS = [int(a) for a in sys.argv[2:]]
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


def read_fld(path):
    b = open(path, 'rb').read()
    o = [0]

    def W():
        v = struct.unpack_from('<h', b, o[0])[0]
        o[0] += 2
        return v

    head = [W() for _ in range(8)]
    objs = [[W() for _ in range(8)] for _ in range(head[6])]
    n = W(); W(); o[0] += 16 * n
    n = W(); W()
    people = [[W() for _ in range(5)] for _ in range(n)]
    events = []
    for _ in range(W()):
        W(); nc = W(); o[0] += 18 * nc
        na = W()
        acts = []
        for _ in range(na):
            code = W(); args = [W() for _ in range(8)]
            acts.append((code, args))
        events.append(acts)
    return head, objs, people, events


def read_btl_events(path):
    """Btl 이벤트의 (코드, 인자) 만 — btl_events.py 의 배치를 그대로 따른다(머리 24 + 배치 29B×수 + 이벤트)."""
    out = subprocess.run([sys.executable, os.path.join(HERE, 'btl_events.py'), G, os.path.basename(path)[:4]],
                         capture_output=True, env=dict(os.environ, PYTHONIOENCODING='utf-8')).stdout.decode('utf-8', 'replace')
    acts = []
    for line in out.splitlines():
        t = line.strip()
        if t.startswith('행동 '):
            code = int(t.split()[1])
            if '[' in t and ']' in t:
                args = eval(t[t.rindex('['):t.rindex(']') + 1])
                acts.append((code, args))
    return acts


def chp_places(chapter):
    b = open(os.path.join(REPO, 'assets', 'moses', 'chp', '%04d.chp' % chapter), 'rb').read()
    o = [0]

    def W():
        v = struct.unpack_from('<h', b, o[0])[0]
        o[0] += 2
        return v

    [W() for _ in range(24)]
    for size in (6, 15, 33, 42):
        n = W(); W(); o[0] += 2 * size * n
    n = W(); W()
    places = [[W() for _ in range(10)] for _ in range(n)]
    # 장소 뒤 4바이트 표(메일 방아쇠) 다음이 챕터 스크립트 — 사건의 행동 6(필드)·10(전투)도 따라간다(그레이 팬텀은 스크립트가 바로 필드 223 으로 간다)
    nt = W(); W(); o[0] += 4 * nt
    try:
        for _ in range(W()):
            W(); nc = W(); o[0] += 18 * nc
            for _ in range(W()):
                code = W(); args = [W() for _ in range(8)]
                if code == 6: places.append([0, 0, 10000 + args[0]] + [0] * 7)
                elif code == 10: places.append([0, 0, args[0]] + [0] * 7)
    except struct.error:
        pass
    return places


def bundle_field(fid):
    if not take('fld', 'Fld/%04d.fld' % fid, 'assets/data/Fld/%04d.fld' % fid):
        return None
    take('tlf', 'Tlk/%04d.tlf' % fid, 'assets/data/Tlk/%04d.tlf' % fid)
    head, objs, people, events = read_fld(os.path.join(REPO, 'assets', 'data', 'Fld', '%04d.fld' % fid))
    if head[1] > 0: take('bgr', 'Bgr/%04d.bgr' % head[1], 'assets/moses/bgr/%04d.bgr' % head[1])
    if head[5] > 0: take('bgm', 'BGM/%04d.bgm' % head[5], 'assets/bgm/%04d.bgm' % head[5])
    for ob in objs:
        if ob[1] > 0: take('obs', 'Obs/%04d.obs' % ob[1], 'assets/moses/obs/%04d.obs' % ob[1])
    for pe in people:
        if take('chr', 'Chr/%04d.chr' % pe[1], 'assets/data/Chr/%04d.chr' % pe[1]):
            cb = open(os.path.join(REPO, 'assets', 'data', 'Chr', '%04d.chr' % pe[1]), 'rb').read()
            for off in (8, 10):
                obs = struct.unpack_from('<H', cb, off)[0]
                if obs > 0: take('obs', 'Obs/%04d.obs' % obs, 'assets/moses/obs/%04d.obs' % obs)
    fields, battles = [], []
    for acts in events:
        for code, a in acts:
            if code == 6: fields.append(a[0])
            elif code == 10: battles.append(a[0])
            elif code in (901, 903, 904, 905, 906, 907, 908, 909) and a[1] > 0:
                take('bgr', 'Bgr/%04d.bgr' % a[1], 'assets/moses/bgr/%04d.bgr' % a[1])
            elif code == 302 and 0 < a[0] < 10000:
                take('obs', 'Obs/%04d.obs' % a[0], 'assets/moses/obs/%04d.obs' % a[0])
            elif code == 512 and a[0] > 0:
                take('bgm', 'BGM/%04d.bgm' % a[0], 'assets/bgm/%04d.bgm' % a[0])
    return fields, battles


def bundle_battle(bid):
    if not take('btl', 'Btl/%04d.btl' % bid, 'assets/data/Btl/%04d.btl' % bid):
        return None
    take('tlb', 'Tlk/%04d.tlb' % bid, 'assets/data/Tlk/%04d.tlb' % bid)
    b = open(os.path.join(REPO, 'assets', 'data', 'Btl', '%04d.btl' % bid), 'rb').read()
    head = words(b, 0, 10)
    if take('map', 'Map/%04d.map' % head[1], 'assets/data/Map/%04d.map' % head[1]):
        obt = struct.unpack_from('<H', open(os.path.join(REPO, 'assets', 'data', 'Map', '%04d.map' % head[1]), 'rb').read(), 2)[0]
        take('obt', 'Obt/%04d.obt' % obt, 'assets/maps/%04d.obt' % obt)
    if head[8] > 0: take('bgm', 'BGM/%04d.bgm' % head[8], 'assets/bgm/%04d.bgm' % head[8])
    fields, battles = [], []
    for code, a in read_btl_events(os.path.join(G, 'Btl', '%04d.btl' % bid)):
        if code == 6: fields.append(a[0])
        elif code == 10: battles.append(a[0])
    return fields, battles


all_battles = set()
for chapter in CHAPTERS:
    places = chp_places(chapter)
    todo_f = [p[2] - 10000 for p in places if 10000 <= p[2] < 20000]
    todo_b = [p[2] for p in places if 0 < p[2] < 10000]
    seen_f, seen_b = set(), set()
    while todo_f or todo_b:
        while todo_f:
            fid = todo_f.pop()
            if fid in seen_f: continue
            seen_f.add(fid)
            r = bundle_field(fid)
            if r is None: print('Fld %04d 없음' % fid); continue
            todo_f += r[0]; todo_b += r[1]
        while todo_b:
            bid = todo_b.pop()
            if bid in seen_b: continue
            seen_b.add(bid)
            r = bundle_battle(bid)
            if r is None: print('Btl %04d 없음' % bid); continue
            todo_f += r[0]; todo_b += r[1]
    print('chapter %d: fields %s battles %s' % (chapter, sorted(seen_f), sorted(seen_b)))
    all_battles |= seen_b

if all_battles:
    out = subprocess.run([sys.executable, os.path.join(HERE, 'export_battle_chars.py'), G] + [str(b) for b in sorted(all_battles)],
                         capture_output=True, env=dict(os.environ, PYTHONIOENCODING='utf-8')).stdout.decode('utf-8', 'replace')
    print(out.strip().splitlines()[-1] if out.strip() else '(인물 뽑기 출력 없음)')
print('added:', {k: '%d KB' % (v // 1024) for k, v in sorted(copied.items())})

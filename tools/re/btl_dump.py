"""전투 파일(Btl) 하나와 그 맵(Map)을 DLL 로더 순서대로 풀어 보여 준다.

근거: CBattle::LoadBtl 0x10062070, CBMap::LoadMap 0x100d6330, LoadTextData 0x1004a190.

쓰기:
    python tools/re/btl_dump.py "<게임 폴더>" 45

- 낱장 파일이 있으면 그것을, 없으면 <폴더>.idx/.pak 에서 읽는다(게임 폴더는 읽기만).
- 텍스트 번호는 Txr.dat 을 DLL 과 같은 방식으로 읽는다:
  머리 u16 ?, u16 레코드 수 n, u16 최대 번호, u32 본문 크기 / 레코드 10바이트 (u16 번호, u32 위치, u16 길이, u16 그룹) × n /
  본문은 파일 (n+1)*10 위치부터.

Btl 배치 (파일 기준, 모두 little endian)
  머리 12워드: 0 판(5), 1 맵 번호, 2·3 맵 초기 위치 x·y, 4 전투 이름 txr, 5 승리 조건 txr, 6 패배 조건 txr,
              7 [ctrl+0x3c66], 8 BGM 번호, 9 [ctrl+0x3c68], 10 캐릭터 수 A, 11 (안 씀)
  A × 29바이트: 순번, Chr 번호, x, y, u8 방향(0~3), 부대(army) 번호, +c, 레벨 보정(+e), For.dat 편대 번호(+10), 12바이트
  u16 B 수, u16 (안 씀), B × 22바이트: 순번, Obj 번호, x, y, 편, +a, 레벨(+c), +e, +10, +12, +14
  u16 C 수, u16 (안 씀), C × 12바이트: (안 씀), 번호(+6), x, y, 방향(+4), +8   ← 아군 배치영역
  u16 D 수, D × (u16 최대 발동, u16 ?, u16 조건 수 n1, (u16 코드 + 16바이트)×n1, u16 행동 수 n2, (u16 코드 + 16바이트)×n2)
"""
import os
import struct
import sys


def read_game_file(game, folder, name):
    p = os.path.join(game, folder, name)
    if os.path.isfile(p):
        return open(p, "rb").read()
    src = os.path.join(game, folder)
    for idx in [f for f in os.listdir(src) if f.lower().endswith(".idx")]:
        data = open(os.path.join(src, idx), "rb").read()
        total = struct.unpack_from("<I", data, 0)[0] // 0x10000
        off = 6
        for _ in range(total):
            rec = data[off:off + 36]
            off += 36
            n = bytes(b ^ 0xFF for b in rec[14:23]).split(b"\0")[0].decode("cp949", "replace")
            if n.lower() != name.lower():
                continue
            pak = bytes(b ^ 0xFF for b in rec[23:36]).split(b"\0")[0].decode("cp949", "replace")
            _, start, _, end, _ = struct.unpack_from("<HIHIH", rec, 0)
            with open(os.path.join(src, pak), "rb") as f:
                f.seek(start)
                return f.read(end + 1 - start)
    return None


def load_txr(game):
    d = open(os.path.join(game, "TXR", "Txr.dat"), "rb").read()
    _, n, _, _ = struct.unpack_from("<HHHI", d, 0)
    base = (n + 1) * 10
    table = {}
    for k in range(n):
        i, off, ln, grp = struct.unpack_from("<HIHH", d, 10 + k * 10)
        end = d.find(b"\0", base + off)
        table[i] = d[base + off:end].decode("cp949", "replace")
    return table


class R:
    def __init__(self, b):
        self.b, self.p = b, 0

    def h(self):
        v = struct.unpack_from("<h", self.b, self.p)[0]
        self.p += 2
        return v

    def raw(self, n):
        v = self.b[self.p:self.p + n]
        self.p += n
        return v


def parse_btl(b):
    r = R(b)
    hdr = [r.h() for _ in range(10)]
    na = r.h() & 0xFFFF
    r.h()
    A = []
    for _ in range(na):
        a0, a2, x, y = r.h(), r.h(), r.h(), r.h()
        d = r.raw(1)[0]
        army, ac, lv, squad = r.h(), r.h(), r.h(), r.h()
        A.append(dict(no=a0, chr=a2, x=x, y=y, dir=d, army=army, c=ac, lv=lv, squad=squad, tail=r.raw(12).hex()))
    nb = r.h() & 0xFFFF
    r.h()
    B = []
    for _ in range(nb):
        w = [r.h() for _ in range(11)]
        B.append(dict(no=w[0], obj=w[1], x=w[2], y=w[3], team=w[4], a=w[5], lv=w[6], rest=w[7:]))
    nc = r.h() & 0xFFFF
    r.h()
    C = []
    for _ in range(nc):
        w = [r.h() for _ in range(6)]
        C.append(dict(unused=w[0], id=w[1], x=w[2], y=w[3], dir=w[4], e=w[5]))
    nd = r.h()
    D = []
    for _ in range(max(nd, 0)):
        mx, e, n1 = r.h(), r.h(), r.h()
        conds = [(r.h(), r.raw(16).hex()) for _ in range(n1)]
        n2 = r.h()
        acts = [(r.h(), r.raw(16).hex()) for _ in range(n2)]
        D.append(dict(max=mx, e=e, conds=conds, acts=acts))
    return dict(hdr=hdr, A=A, B=B, C=C, D=D, used=r.p, size=len(b))


def parse_map(b):
    r = R(b)
    ver, obt, bsd = r.h(), r.h(), r.h()
    extra = (r.h(), r.h()) if ver >= 3 else (0, 0)
    nu = r.h()
    r.h()
    units = [[r.h() for _ in range(11)] for _ in range(nu)]
    no = r.h()
    r.h()
    objs = [[r.h() for _ in range(8)] for _ in range(no)]
    ne = r.h()
    effs = []
    if ne != -1:
        r.h()
        effs = [[r.h() for _ in range(7)] for _ in range(ne)]
    return dict(ver=ver, obt=obt, bsd=bsd, extra=extra, units=units, objs=objs, effs=effs, used=r.p, size=len(b))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    if len(sys.argv) < 3:
        print(__doc__)
        return
    game, bid = sys.argv[1], int(sys.argv[2])
    txr = load_txr(game)
    t = lambda i: f"{i}「{txr.get(i, '?')}」"

    def chr_name(code):
        c = read_game_file(game, "Chr", f"{code:04d}.chr")
        if not c or len(c) < 4:
            return "?"
        return txr.get(struct.unpack_from("<H", c, 2)[0], "?")

    b = read_game_file(game, "Btl", f"{bid:04d}.btl")
    if b is None:
        print("Btl 없음")
        return
    bt = parse_btl(b)
    h = bt["hdr"]
    print(f"Btl {bid:04d}: {bt['size']}바이트, 읽은 양 {bt['used']}")
    print(f"  판 {h[0]}, 맵 {h[1]}, 초기 위치 ({h[2]},{h[3]})")
    print(f"  이름 {t(h[4])}, 승리 {t(h[5])}, 패배 {t(h[6])}")
    print(f"  w7={h[7]}  BGM {h[8]}  w9={h[9]}")
    for a in bt["A"]:
        print(f"  캐릭터 #{a['no']} Chr {a['chr']} {chr_name(a['chr'])} ({a['x']},{a['y']}) 방향 {a['dir']} 부대 {a['army']}"
              f" 레벨보정 {a['lv']} 편대 {a['squad']} c={a['c']} tail={a['tail']}")
    for u in bt["B"]:
        print(f"  오브젝트 #{u['no']} Obj {u['obj']} ({u['x']},{u['y']}) 편 {u['team']} a={u['a']} 레벨 {u['lv']} {u['rest']}")
    for c in bt["C"]:
        print(f"  아군 배치영역 {c}")
    for i, d in enumerate(bt["D"]):
        print(f"  이벤트 {i}: 최대 {d['max']} e={d['e']} 조건 {[c for c, _ in d['conds']]} 행동 {[a for a, _ in d['acts']]}")
    m = read_game_file(game, "Map", f"{h[1]:04d}.map")
    if m is None:
        print(f"Map {h[1]:04d} 없음")
        return
    mp = parse_map(m)
    print(f"Map {h[1]:04d}: 판 {mp['ver']}, Obt {mp['obt']}, Bsd {mp['bsd']}, +54/+56 {mp['extra']}, "
          f"{mp['size']}바이트 중 {mp['used']} 읽음")
    for u in mp["units"]:
        print(f"  맵 오브젝트(유닛) #{u[0]} Obj {u[1]} ({u[2]},{u[3]}) {u[4:]}")
    for o in mp["objs"]:
        kind = f"Mov {o[1] - 10000}" if o[1] >= 10000 else f"그림 {o[1]}"
        print(f"  맵 장식 {kind}: {o}")
    for e in mp["effs"]:
        print(f"  맵 효과: {e}")


if __name__ == "__main__":
    main()

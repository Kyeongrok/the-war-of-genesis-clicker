"""창세기전3 파트2 — Status 창 능력치 칸(0x100d4740)에 찍히는 숫자를 세이브의 캐릭터 자료로 DLL 과 같은 식으로 계산한다.

쓰기:
    python tools/re/status_calc.py "<게임 폴더>" [--sav G3P_II20.sav] [--chr 221 ...] [--soul 40]

전투 밖 세이브에는 유닛 칸(현재 SOUL·현재 TP·유닛 가감 +0x4c8~+0x4d2)이 없으므로
현재 HP = CChr+0x38, 현재 TP = 최대 TP(차례가 온 유닛), 현재 SOUL = --soul(전투 시작값 40), 유닛 가감 = 0 으로 둔다.

줄 (패널 0x4af, 170×400, 줄 간격 e = (400−116)/25 + 4 = 15, y = 20 + 줄×e, 값은 오른쪽 맞춤 x = 폭−10):
  0 이름 TXR[CChr+6]            1 칭호 TXR[CChr+0x10]     2 계열 TXR[Dep[CChr+0x14].이름]   3 직업 TXR[Job[CChr+0x16].이름[체질]]
  5 LEVEL  CChr+0x2c            6 EXP  CChr+0x30
  8 HP     현재/최대(+보정)      10 SOUL 현재/최대(+보정)   12 TP 현재/최대(+보정)
  15 ATK 0x1007ab20   16 ACR 0x1007ab90   17 RDP 0x1007abf0
  19 LP 0x1007ac70    20 CTP 0x1007af10   21 STP 0x1007acf0   22 PSY(+보정)   23 DEP(+보정)   24 DEX(+보정)
보정 = 장비 6칸(0x10032c60) + 장착 어빌리티(0x10032af0) (+ 유닛 가감). 보정 > 0 이면 "(+n)", 아니면 괄호 없음
(음수용 "(%d)" 서식은 코드에 있으나 조건이 같아서 닿지 않음 — 0x100d49fd).
HP 는 화면값 = 내부값 × (갑옷 + Num[6]) / Num[6] 이고 보정도 같은 배율(0x10032e80).
"""
import argparse
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import sav_dump as sd  # noqa: E402
import skill_motion as sm  # noqa: E402
from btl_dump import load_txr  # noqa: E402


def cdiv(a, b):
    """C 정수 나눗셈(0 쪽으로 자름)."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def load_num(g):
    d = g.read('Dat', 'Num.dat')
    _, n, _ = struct.unpack_from('<3H', d)
    return {struct.unpack_from('<H', d, 6 + 6 * i)[0]: struct.unpack_from('<I', d, 8 + 6 * i)[0] for i in range(n)}


def load_items(g):
    d = g.read('Dat', 'itm.dat')
    _, n, _ = struct.unpack_from('<3H', d)
    out = {}
    for i in range(n):
        o = 6 + 48 * i
        out[struct.unpack_from('<H', d, o)[0]] = dict(
            name=struct.unpack_from('<H', d, o + 2)[0], type=d[o + 8], atk=struct.unpack_from('<h', d, o + 11)[0],
            arm=struct.unpack_from('<h', d, o + 13)[0],
            bonus=[struct.unpack_from('<Hh', d, o + 18 + 4 * k) for k in range(3)])
    return out


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('--sav', default=None)
    ap.add_argument('--chr', type=int, nargs='*')
    ap.add_argument('--soul', type=int, default=40)
    a = ap.parse_args()
    g = sm.Game(a.game)
    txr = load_txr(a.game)
    num = load_num(g)
    items = load_items(g)
    tables = sd.load_tables(g)
    works, abis = tables[1], tables[2]
    sav = a.sav or next(f for f in os.listdir(a.game) if f.lower().endswith('.sav'))
    _, chars, _, _ = sd.parse_sav(os.path.join(a.game, sav))
    dep_d = g.read('Dat', 'Dep.dat')
    job_d = g.read('Dat', 'Job.dat')
    deps = {}
    _, dn, _ = struct.unpack_from('<3H', dep_d)
    for i in range(dn):
        o = 6 + 33 * i
        deps[struct.unpack_from('<H', dep_d, o)[0]] = dict(name=struct.unpack_from('<H', dep_d, o + 2)[0], slots=dep_d[o + 4])
    jobs = {}
    _, jn, _ = struct.unpack_from('<3H', job_d)
    for i in range(jn):
        o = 6 + 67 * i
        jobs[struct.unpack_from('<H', job_d, o)[0]] = list(struct.unpack_from('<6H', job_d, o + 53))

    for c in chars:
        h = lambda off: struct.unpack_from('<h', c, off)[0]
        chr_no = struct.unpack_from('<H', c, 4)[0]
        if a.chr and chr_no not in a.chr:
            continue
        eq = struct.unpack_from('<6H', c, 0x4c)

        def equip(stat):                       # 0x10032c60
            return sum(v for it in eq if it and it in items for s, v in items[it]['bonus'] if s == stat)

        job = h(0x16)
        slots = 3 if job == 37 else deps.get(h(0x14), {}).get('slots', 0)

        def passive(stat):                     # 0x10032af0
            s = 0
            for i in range(slots):
                ab = h(0x142 + 2 * i)
                lv = c[0x7a + ab] if 0 < ab < 200 else 0xff
                if ab <= 0 or lv == 0xff or ab not in abis:
                    continue
                w = works.get(abis[ab]['levels'].get(lv))
                if w:
                    s += sum(w[0x24 + 2 * k] for k in range(3) if w[0x20 + k] == stat)
            return s

        bonus = lambda stat: equip(stat) + passive(stat)
        lp = struct.unpack_from('<i', c, 0x34)[0] + bonus(0x30)            # 0x1007ac70 (유닛 +0x4ce = 0)
        hp_cur = struct.unpack_from('<i', c, 0x38)[0]
        arm = items[eq[1]]['arm'] if eq[1] in items else 0                  # 0x10032ff0: 칸 1 Itm +0x12
        scr = lambda v: cdiv((arm + num[6]) * v, num[6])                    # 0x1007ad60 / 0x1007ada0 / 0x10032e80
        tp_max = h(0x3e) + bonus(0x21)                                     # 0x1007aeb0
        tp_cur = tp_max
        soul_max = num[19] + bonus(0x25)                                   # 0x1007ad20
        soul = a.soul
        psy = h(0x3c) + bonus(0x1f)
        dep = h(0x44) + bonus(0x20)
        dex = max(0, h(0x46) + bonus(0x1e))
        ctp = h(0x42)
        stp = cdiv(tp_max, h(0x40)) if h(0x40) else 0                      # 0x1007acf0
        watk = items[eq[0]]['atk'] if eq[0] in items else 0                 # 0x10032fc0: 칸 0 Itm +0x10
        atk = cdiv(cdiv((watk + num[1]) * psy, num[25]) * (soul + num[2]) * num[42], num[85])   # 0x1007ab20
        acr = cdiv(ctp + ctp + tp_max + tp_cur, num[9]) + cdiv(dex, num[8])                   # 0x1007ab90
        ratio = 1.0 - hp_cur / lp if lp else 0.0
        rdp = int(dep * (ratio * ratio * num[39] * 0.01 + 1.0))                               # 0x1007abf0 (가설: 상수 0.01)
        fmt = lambda cur, mx, b: f'{cur} / {mx}' + (f'(+{b})' if b > 0 else '')
        dep_name = txr.get(deps.get(h(0x14), {}).get('name'), '?')
        job_name = txr.get(jobs.get(job, [0] * 6)[c[0x12]] if c[0x12] < 6 else 0, '?')
        print(f"Chr {chr_no} {txr.get(h(6))} / {txr.get(h(0x10))} / {dep_name} / {job_name}")
        print(f"  LEVEL {h(0x2c)}  EXP {h(0x30)}")
        print(f"  HP {fmt(scr(hp_cur), scr(lp), scr(bonus(0x30)))}  SOUL {fmt(soul, soul_max, bonus(0x25))}  TP {fmt(tp_cur, tp_max, bonus(0x21))}")
        print(f"  ATK {atk}  ACR {acr}  RDP {rdp}")
        print(f"  LP {lp}  CTP {ctp}  STP {stp}  PSY {psy}{'(+%d)' % bonus(0x1f) if bonus(0x1f) > 0 else ''}"
              f"  DEP {dep}{'(+%d)' % bonus(0x20) if bonus(0x20) > 0 else ''}  DEX {dex}{'(+%d)' % bonus(0x1e) if bonus(0x1e) > 0 else ''}")
        print(f"  (무기 {eq[0]} 공격 {watk}, 갑옷 {eq[1]} 배율 {arm}, STP 나눗수 CChr+0x40 {h(0x40)}, WEAPON 띠 그림 CChr+0x48 {h(0x48)})")


if __name__ == '__main__':
    main()

"""
창세기전3 파트2 — 공격/어빌리티 한 번의 명중률·피해·회복량을 G3PartII.dll 식 그대로 계산한다 (구현 fg-5·fg-6).

사용법:
    python tools/re/combat_calc.py <게임 폴더> <공격 Chr> <대상 Chr> [--work N] [--soul 40]
           [--atk-tp N] [--def-tp N] [--def-hp N] [--stance 0|1|2]
    --work 생략 = 공격 Chr 의 기본공격(.chr +19).  TP 생략 = 최대 TP(턴 시작), HP 생략 = 최대 HP.
    예) python tools/re/combat_calc.py "C:/.../창세기전3 파트2" 221 193
        python tools/re/combat_calc.py "C:/.../창세기전3 파트2" 62 193 --work 467
        python tools/re/combat_calc.py "C:/.../창세기전3 파트2" 221 219 --work 58 --def-hp 300

근거 (옵시디안 분석-전투.md "공격·어빌리티 판정"):
  결과 함수 0x1007b6f0(맞는 유닛, 설명자{종류=att+0x1f, 공격자, work}) — 메시지 1001(0x3e9) → 0x10078e60 에서 부름
  빗나감 0x1007b580 : 명중 = (att+0x2c)*8/10 + 2*(Num7 + (aDEX-dDEX)/Num8 + (aTP-dTP)/Num9)/10  [− dDEX/Num10 (대상 자세 2)]
                      rand()%100 >= 명중 이면 Miss
  공격력 0x1007aa90 = (무기공격+Num1)*PSY/Num25 * (SOUL+Num2) * (Num42+att+0x2a) / Num85
  RDP   0x1007abf0 = trunc(DEP * (1 + 0.3*(1-HP/최대HP)^2))     (Num39=30, 상수 0.01)
  피해 = (Num3 - RDP) * 공격력 / Num3 ; 자세 1 이면 (DEX/Num11+Num12)% 로 한 번 더
         흔들기 v = 피해*Num22/100 : 피해 += rand()%v - 피해*Num22/200
         치명 rand()%100 <= att+0x2d : 피해 = 피해*Num23/100
  회복(종류 1·5) = 최대HP * att+0x2a / 100  (빗나감 없음)
  HP 적용 0x100797fb : HP(+0x148) -= 피해*Num6/(갑옷+Num6)  (갑옷 = 장비 2칸 Itm +0x12, 0 이면 그대로)
"""
import argparse
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
from skill_motion import Game, load_works, load_abis  # noqa: E402
from exp_tables import parse_num  # noqa: E402


def cdiv(a, b):
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b > 0) else -q


def s16(v):
    return v - 0x10000 if v >= 0x8000 else v


def load_items(g):
    d = g.read('Dat', 'Itm.dat')
    n = struct.unpack_from('<H', d, 2)[0]
    items = {}
    for i in range(n):
        o = 6 + 48 * i
        w = struct.unpack_from('<15H', d, o + 18)
        items[struct.unpack_from('<H', d, o)[0]] = {
            'type': d[o + 8],
            'attack': s16(struct.unpack_from('<H', d, o + 11)[0]),   # 메모리 +0x10 (0x10032fc0)
            'defense': s16(struct.unpack_from('<H', d, o + 13)[0]),  # 메모리 +0x12 (0x10032ff0)
            'bonus': [(w[2 * k], s16(w[2 * k + 1])) for k in range(7) if w[2 * k]],
        }
    return items


def load_unit(g, n, items):
    b = g.read('Chr', '%04d.chr' % n)
    if not b or len(b) != 90:
        raise SystemExit('Chr %04d 없음' % n)
    u = {'id': n, 'name': struct.unpack_from('<H', b, 2)[0], 'body': b[14], 'basic': struct.unpack_from('<H', b, 19)[0]}
    u['LP'] = struct.unpack_from('<I', b, 23)[0]
    u['PSY'], u['TP'], _, u['CTP'], u['DEP'], u['DEX'] = struct.unpack_from('<6H', b, 27)
    eq = struct.unpack_from('<6H', b, 42)

    def bonus(stat):   # 0x10032c60
        return sum(v for i in eq if i in items for s, v in items[i]['bonus'] if s == stat)
    u['weapon'] = items[eq[0]]['attack'] if eq[0] in items else 0
    u['armor'] = items[eq[1]]['defense'] if eq[1] in items else 0
    u['maxHP'] = u['LP'] + bonus(0x30)        # 0x10032eb0
    u['PSY'] += bonus(0x1f)                   # 0x10032f20
    u['DEP'] += bonus(0x20)                   # 0x10032f80
    u['DEX'] += bonus(0x1e)                   # 0x10032f50
    u['maxTP'] = u['TP'] + bonus(0x21)        # 0x10032ee0
    return u


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('attacker', type=int)
    ap.add_argument('target', type=int)
    ap.add_argument('--work', type=int)
    ap.add_argument('--soul', type=int, default=None, help='공격자 SOUL (기본 Num[20]=40)')
    ap.add_argument('--atk-tp', type=int)
    ap.add_argument('--def-tp', type=int)
    ap.add_argument('--def-hp', type=int, help='대상 현재 HP(내부값)')
    ap.add_argument('--stance', type=int, default=0, help='대상 +0x4d4: 1 방어 자세, 2 회피 자세(이스케이프)')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

    g = Game(a.game)
    N, _ = parse_num(g.read('Dat', 'Num.dat'))
    works = load_works(g)
    abis = load_abis(g, works)
    items = load_items(g)
    text = {}
    try:
        import extract_character as e
        for r in e.parse_txr(g.read('TXR', 'Txr.dat')):
            text.setdefault(r['txr_id'], r['text'])
    except Exception:
        pass

    A, D = load_unit(g, a.attacker, items), load_unit(g, a.target, items)
    wid = a.work if a.work is not None else A['basic']
    w = works[wid]
    soul = N[20] if a.soul is None else a.soul
    atp = A['maxTP'] if a.atk_tp is None else a.atk_tp
    dtp = D['maxTP'] if a.def_tp is None else a.def_tp
    dhp = D['maxHP'] if a.def_hp is None else a.def_hp
    kind = w[0x1f]
    ab = abis.get(w[0x4])
    wname = text.get(ab['name'], '?') if ab else '?'

    for tag, u in (('공격', A), ('대상', D)):
        print('%s Chr %04d %s: 최대HP %d PSY %d DEP %d DEX %d TP %d 무기공격 %d 갑옷 %d' % (
            tag, u['id'], text.get(u['name'], u['name']), u['maxHP'], u['PSY'], u['DEP'], u['DEX'], u['maxTP'],
            u['weapon'], u['armor']))
    print('work %d (%s Lv%d): 종류 +0x1f=%d  +0x2a=%d  명중 +0x2c=%d  치명 +0x2d=%d  대상 +0x13=%d  범위 +0x7=%d +0x8=%d +0xa=%d +0xc=%d  효과범위 +0x14=%d +0x1a=%d +0x1c=%d' % (
        wid, wname, w[0x6], kind, s16(w[0x2a]), w[0x2c], w[0x2d], w[0x13], w[0x7], w[0x8], w[0xa], w[0xc],
        w[0x14], s16(w[0x1a]), s16(w[0x1c])))

    if kind in (1, 5):
        heal = cdiv(D['maxHP'] * s16(w[0x2a]), 100)
        print('회복 = 최대HP %d × %d / 100 = %d  (빗나감 없음, 최대HP 에서 자름: %d → %d)' % (
            D['maxHP'], s16(w[0x2a]), heal, dhp, min(D['maxHP'], dhp + heal)))
        return
    if kind != 0:
        print('종류 %d: HP 변화 없음(상태/보조). 0x1007b6f0 은 양 0 을 돌려준다.' % kind)
        return

    hit = cdiv(w[0x2c] * 8, 10) + cdiv(2 * (N[7] + cdiv(A['DEX'] - D['DEX'], N[8]) + cdiv(atp - dtp, N[9])), 10)
    if a.stance == 2:
        hit -= cdiv(D['DEX'], N[10])
    print('명중 = %d*8/10 + 2*(%d + (%d-%d)/%d + (%d-%d)/%d)/10%s = %d  → 확률 %d%%' % (
        w[0x2c], N[7], A['DEX'], D['DEX'], N[8], atp, dtp, N[9],
        ' - %d/%d' % (D['DEX'], N[10]) if a.stance == 2 else '', hit, max(0, min(100, hit))))

    atk = cdiv(cdiv((A['weapon'] + N[1]) * A['PSY'], N[25]) * (soul + N[2]) * (N[42] + s16(w[0x2a])), N[85])
    print('공격력 = ((%d+%d)*%d/%d) * (%d+%d) * (%d+%d) / %d = %d' % (
        A['weapon'], N[1], A['PSY'], N[25], soul, N[2], N[42], s16(w[0x2a]), N[85], atk))
    ratio = dhp / D['maxHP']
    rdp = int(D['DEP'] * ((1 - ratio) ** 2 * N[39] * 0.01 + 1.0))
    dmg = cdiv((N[3] - rdp) * atk, N[3])
    print('RDP = trunc(%d × (1 + %d×0.01×(1-%d/%d)²)) = %d' % (D['DEP'], N[39], dhp, D['maxHP'], rdp))
    print('기본 피해 = (%d - %d) × %d / %d = %d' % (N[3], rdp, atk, N[3], dmg))
    if a.stance == 1:
        g2 = cdiv((N[3] - rdp) * dmg, N[3])
        print('방어 자세: %d%% 확률로 한 번 더 → %d' % (cdiv(D['DEX'], N[11]) + N[12], g2))
    v = cdiv(dmg * N[22], 100)
    lo = dmg - cdiv(dmg * N[22], 200)
    hi = lo + v - 1 if v else dmg
    print('흔들기: v=%d → 피해 %d ~ %d' % (v, lo, hi))
    print('치명 (rand%%100 <= %d, %d%%): ×%d/100 → %d ~ %d' % (
        w[0x2d], w[0x2d] + 1, N[23], cdiv(lo * N[23], 100), cdiv(hi * N[23], 100)))
    def hp_loss(x):
        return cdiv(N[6] * x, D['armor'] + N[6]) if D['armor'] else x
    print('HP 감소(갑옷 %d) = 피해×%d/(갑옷+%d): 보통 %d ~ %d, 대상 HP %d → %d ~ %d%s' % (
        D['armor'], N[6], N[6], hp_loss(lo), hp_loss(hi), dhp, dhp - hp_loss(hi), dhp - hp_loss(lo),
        '  (죽음 가능)' if dhp - hp_loss(hi) <= 0 else ''))
    print('맞은 쪽 SOUL += 피해/%d = %d ~ %d' % (N[43], lo // N[43], hi // N[43]))
    if kind == 0 and dmg == 0:
        print('피해 0 → 화면에 "Miss"(TXR 42)')


if __name__ == '__main__':
    main()

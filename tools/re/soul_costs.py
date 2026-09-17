"""
창세기전3 파트2 — 어빌리티(work) 마다 필요한/소모되는 SOUL·TP·HP 와 사용 뒤 차는 SOUL 을 체질별로 계산한다.

사용법:
    python soul_costs.py <게임 폴더>                       # Num.dat 소울 상수 + 모든 어빌리티 표
    python soul_costs.py <게임 폴더> --abi 2 10 34         # 어빌리티 번호로 거르기
    python soul_costs.py <게임 폴더> --name 힐 연           # 이름(TXR 24352)으로 거르기
    python soul_costs.py <게임 폴더> --work 210 17         # work 번호로

게임 폴더는 읽기만 한다. skill_motion.py(Abi/att 읽기), exp_tables.py(Num.dat) 를 함께 쓴다.

G3PartII.dll 근거 (옵시디안 분석-전투.md "Soul 시스템 (ba-4)"):
  유닛 +0x4d6 s16 = 현재 SOUL.  +0x122 = CChr+0x12 = 체질(0 무속성, 1 에텔, 2 멘탈, 3 아스트럴, 4 코절, 5 메텔)
  최대 SOUL 0x1007ad20 = Num[19](150) + 장비 효과 0x25 합(0x10032c60) + 패시브 어빌리티 효과 0x25 합(0x10032af0) + 유닛 +0x4d2
  처음 값 = Num[20](40) (유닛 생성 0x100727e3 / 0x10072eb9)
  필요 SOUL 0x10072440(work) = att+0x34 + att+0x2e × Num[체질 soul%] / 100 / Num[33](10), 효과 0x12 면 × (100+값)/100
  소모 SOUL 0x10072510(work) = (att+0x36 ≠ 0 ? att+0x34 : 0) + 같은 체질 덧붙임, 효과 0x12 동일
  필요=소모 TP 0x10072610(work) = att+0x32 + att+0x2e × Num[체질 TP%] / 100 / Num[34](4), 효과 0x14
  HP 0x100726e0(work) = att+0x2e × Num[체질 HP%] / 100 / Num[41](2)
  체질 % 표: HP·SOUL·TP = Num[44+3(c-1)], Num[45+3(c-1)], Num[46+3(c-1)]  (c = 1..5)
  사용 뒤 SOUL 증가 0x10076586: att+0x1f 가 0→Num[26] 1→Num[27] 2→Num[29] 3→Num[28], 나머지 0
  맞았을 때 (0x10079033, 0x100799ae): 받은 피해 / Num[43](25).  적 처치 (0x1007215c): Num[30](10)
  증가는 모두 0x10071d40 을 거친다: 효과 0x13 이면 안 오름, 최대치에서 자름.
"""
import argparse
import os
import pickle
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import skill_motion as sm  # noqa: E402
import exp_tables as et  # noqa: E402

CONST = ['무속성', '에텔', '멘탈', '아스트럴', '코절', '메텔']


def load_txr(root):
    from extract_character import parse_txr
    rows = parse_txr(open(os.path.join(root, 'TXR', 'Txr.dat'), 'rb').read())
    return {r['txr_id']: r['text'] for r in rows if r['group'] == 24352}


def s16(v):
    return v - 0x10000 if v >= 0x8000 else v


def cdiv(a, b):
    """C 의 정수 나눗셈(0 쪽으로 자름)."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b > 0) else -q


def costs(w, num, c):
    """체질 c 에서 (필요 SOUL, 소모 SOUL, TP, HP). 효과(상태이상) 보정은 뺀다."""
    e = s16(w[0x2e])
    hp_p, soul_p, tp_p = (num[44 + 3 * (c - 1)], num[45 + 3 * (c - 1)], num[46 + 3 * (c - 1)]) if 1 <= c <= 5 else (0, 0, 0)
    soul_add = cdiv(cdiv(e * soul_p, 100), num[33]) if soul_p is not None else 0
    tp_add = cdiv(cdiv(e * tp_p, 100), num[34])
    hp_add = cdiv(cdiv(e * hp_p, 100), num[41])
    need = s16(w[0x34]) + soul_add
    use = (s16(w[0x34]) if w[0x36] else 0) + soul_add
    return need, use, s16(w[0x32]) + tp_add, hp_add


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('--abi', type=int, nargs='*')
    ap.add_argument('--name', nargs='*')
    ap.add_argument('--work', type=int, nargs='*')
    a = ap.parse_args()

    g = sm.Game(a.game)
    works = sm.load_works(g)
    abis = sm.load_abis(g, works)
    num, _ = et.parse_num(g.read('Dat', 'Num.dat'))
    names = load_txr(a.game)

    print('Num.dat 소울 상수: 최대 기본[19]=%s 시작[20]=%s 처치[30]=%s 피해나눔[43]=%s 사용뒤[26..29]=%s 바닥[38]=%s'
          % (num[19], num[20], num[30], num[43], [num[i] for i in (26, 27, 29, 28)], num[38]))
    print('체질별 덧붙임 %% (HP, SOUL, TP): ' + ', '.join(
        '%s %s' % (CONST[c], (num[44 + 3 * (c - 1)], num[45 + 3 * (c - 1)], num[46 + 3 * (c - 1)])) for c in range(1, 6)))
    print('열: 레벨 work | +2e +32 +34 +36 +1f | 사용뒤SOUL | 체질별 필요SOUL/소모SOUL/TP/HP (에텔 멘탈 아스트럴 코절 메텔)')
    gain = {0: num[26], 1: num[27], 2: num[29], 3: num[28]}
    for aid, ab in sorted(abis.items()):
        nm = names.get(ab['name'], '?')
        if a.abi and aid not in a.abi:
            continue
        if a.name and nm not in a.name:
            continue
        lv_w = sorted(ab['levels'].items())
        if a.work:
            lv_w = [(lv, wid) for lv, wid in lv_w if wid in a.work]
            if not lv_w:
                continue
        if not lv_w:
            continue
        print('abi %d %s (%s)' % (aid, nm, ab['file']))
        for lv, wid in lv_w:
            w = works[wid]
            cols = ' '.join('%d/%d/%d/%d' % costs(w, num, c) for c in range(1, 6))
            print('  lv%-2d w%-5d | %4d %4d %4d %d %d | +%-2d | %s' % (
                lv, wid, s16(w[0x2e]), s16(w[0x32]), s16(w[0x34]), w[0x36], w[0x1f], gain.get(w[0x1f], 0), cols))


if __name__ == '__main__':
    main()

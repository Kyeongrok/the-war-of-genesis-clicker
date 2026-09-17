"""
창세기전3 파트2 — 캐릭터별 TP / CTP / STP 와 어빌리티 TP 비용을 G3PartII.dll 식 그대로 계산한다 (분석 ba-3).

사용법:
    python tools/re/tp_calc.py <게임 폴더> <Chr 번호…> [--levels N]

게임 폴더는 읽기만 한다. pak 안 파일은 skill_motion.Game 으로 메모리에서 꺼낸다.

필드 (CChr.Load 0x10031530, 파일 오프셋 → CChr 오프셋):
    23 u32 → +0x34 LP        27 u16 → +0x3c PSY      29 u16 → +0x3e TP(최대 TP 기본값)
    31 u16 → +0x40 TP 나눗수   33 u16 → +0x42 CTP       35 u16 → +0x44 DEP     37 u16 → +0x46 DEX
    14 u8  → +0x12 체질      15 u16 → +0x16 직업(Job.dat)   21 u16 → +0x2c 레벨

식 (전투 유닛, CChr 는 유닛 +0x110):
    최대 TP   0x1007aeb0 = CChr TP(+0x3e) + 아이템 보너스(종류 0x21) + 유닛 +0x4d0, 음수면 0
    현재 TP   유닛 +0x4d8 (0x1007aef0)
    CTP       0x1007af10 = CChr +0x42 그대로
    STP       0x1007acf0 = 최대 TP / CChr +0x40   (화면의 STP)
    시간 한 칸 0x10071db0: 현재 TP += 최대TP / +0x40 (+효과 0x26), 최대 TP 에서 자름
    ACR       0x1007ab90 = (2×CTP + 최대TP + 현재TP) / Num[9] + DEX / Num[8]
    work TP 비용 0x10072610 = att+0x32 + att+0x2e × Num[43+3×체질] / 100 / Num[34]   (체질 1~5), 효과 0x14 면 % 가감
    work HP 비용 0x100726e0 = att+0x2e × Num[41+3×체질] / 100 / Num[41]
    쓸 수 있나  (0x10069526, 0x100d587f) : 비용 ≤ 현재TP + CTP
    레벨업 0x100318d0: g = Job[+0x18] × 기본TP / 100 ; CTP − g ≥ 100 이면 TP += g, CTP −= g
                     아니면 CTP > 100 일 때 남은 만큼만 TP 로 옮기고 CTP = 100
"""
import argparse
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))

from skill_motion import Game, load_works, load_abis  # noqa: E402
from exp_tables import parse_num, parse_job  # noqa: E402

BODY = {0: '무속성', 1: '에텔', 2: '멘탈', 3: '아스트럴', 4: '코절', 5: '메텔', 255: 'NPC'}


def c_div(a, b):
    """C 의 정수 나눗셈 (0 쪽으로 자름)."""
    q = abs(a) // abs(b)
    return q if (a >= 0) == (b >= 0) else -q


def load_chr_stats(g, n):
    b = g.read('Chr', '%04d.chr' % n)
    if not b or len(b) != 90:
        return None
    r = {
        'name': struct.unpack_from('<H', b, 2)[0],
        'body': b[14],
        'job': struct.unpack_from('<H', b, 15)[0],
        'basic': struct.unpack_from('<H', b, 19)[0],
        'level': struct.unpack_from('<H', b, 21)[0],
        'LP': struct.unpack_from('<I', b, 23)[0],
    }
    r['PSY'], r['TP'], r['TPDIV'], r['CTP'], r['DEP'], r['DEX'] = struct.unpack_from('<6H', b, 27)
    r['abis'] = [struct.unpack_from('<2H', b, 56 + 4 * i) for i in range(8)]
    r['abis'] = [a for a in r['abis'] if a[0] and a[0] != 0xffff]
    return r


def tp_cost(w, body, num):
    cost = w[0x32]
    if 1 <= body <= 5:
        cost += c_div(c_div(w[0x2e] * num.get(0x2e + 3 * (body - 1), 100001), 100), num.get(0x22, 1))
    return cost


def hp_cost(w, body, num):
    if 1 <= body <= 5:
        return c_div(c_div(w[0x2e] * num.get(0x2c + 3 * (body - 1), 100001), 100), num.get(0x29, 1))
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('chrs', nargs='+', type=int)
    ap.add_argument('--levels', type=int, default=10, help='레벨업 몇 번까지 TP/CTP 변화를 찍을지')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

    g = Game(a.game)
    num, _ = parse_num(g.read('Dat', 'Num.dat'))
    jobs = {r['idx']: r for r in parse_job(g.read('Dat', 'Job.dat'))[0]}
    works = load_works(g)
    abis = load_abis(g, works)
    text = {}
    try:
        import extract_character as e
        for r in e.parse_txr(g.read('TXR', 'Txr.dat')):
            text.setdefault(r['txr_id'], r['text'])
    except Exception:
        pass

    for n in a.chrs:
        c = load_chr_stats(g, n)
        if not c:
            print('Chr %04d 없음' % n)
            continue
        stp = c_div(c['TP'], c['TPDIV']) if c['TPDIV'] else None
        acr = c_div(2 * c['CTP'] + 2 * c['TP'], num[9]) + c_div(c['DEX'], num[8])
        print('== Chr %04d %s  체질 %s  직업 %d  레벨 %d' % (
            n, text.get(c['name'], c['name']), BODY.get(c['body'], c['body']), c['job'], c['level']))
        print('   LP %d  PSY %d  TP %d  (+0x40 나눗수 %d)  CTP %d  DEP %d  DEX %d' % (
            c['LP'], c['PSY'], c['TP'], c['TPDIV'], c['CTP'], c['DEP'], c['DEX']))
        print('   STP = TP/나눗수 = %s   ACR(아이템 없이, TP 가득) = %d   한 턴에 쓸 수 있는 최대 TP = TP+CTP = %d' % (
            stp, acr, c['TP'] + c['CTP']))
        if stp:
            print('   TP 0 에서 가득 찰 때까지 시간 칸 = %d,  -CTP 에서 = %d' % (
                -(-c['TP'] // stp), -(-(c['TP'] + c['CTP']) // stp)))
        j = jobs.get(c['job'])
        if j:
            rate = j['f'].get(0x18, 0)
            gstep = c_div(rate * c['TP'], 100)
            tp, ctp, seq = c['TP'], c['CTP'], []
            for _ in range(a.levels):
                if ctp - gstep >= 100:
                    tp += gstep; ctp -= gstep
                elif ctp > 100:
                    tp += ctp - 100; ctp = 100
                seq.append('%d/%d' % (tp, ctp))
            print('   레벨업 TP 성장률 %d%% → 1레벨당 +%d.  레벨업마다 TP/CTP: %s' % (rate, gstep, ' '.join(seq)))
        rows = [('기본공격', c['basic'])]
        for abi, lv in c['abis']:
            ab = abis.get(abi)
            nm = text.get(ab['name'], ab['name']) if ab else abi
            for l in sorted((ab or {}).get('levels', {})):
                if l <= max(lv, 1):
                    rows.append(('어빌 %d %s Lv%d' % (abi, nm, l), ab['levels'][l]))
        for label, wid in rows:
            w = works.get(wid)
            if not w:
                print('   %-28s work %d (att 에 없음)' % (label, wid))
                continue
            tc, hc = tp_cost(w, c['body'], num), hp_cost(w, c['body'], num)
            print('   %-28s work %4d  +0x32=%3d +0x2e=%3d → TP 비용 %3d  HP 비용 %3d  %s' % (
                label, wid, w[0x32], w[0x2e], tc, hc, '쓸 수 있음' if tc <= c['TP'] + c['CTP'] else 'TP 부족'))


if __name__ == '__main__':
    main()

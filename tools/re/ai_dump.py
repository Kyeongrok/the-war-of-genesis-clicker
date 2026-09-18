"""창세기전3 파트2 — 전투 AI(ba-11)가 쓰는 자료를 찍는다.

쓰기:
    python tools/re/ai_dump.py "<게임 폴더>"              # Num.dat 의 AI 상수 + 전체 Btl 의 이동 방식·깨어남 분포
    python tools/re/ai_dump.py "<게임 폴더>" --btl 45     # 그 전투 유닛들의 AI 꼬리 12바이트 풀이
    python tools/re/ai_dump.py "<게임 폴더>" --chr 62     # 그 캐릭터의 AI work 목록(고르는 순서)과 판정 칸

근거 (G3PartII.dll, ImageBase 0x10000000):
  AI 생각 0x10060730 (전투 상태 8 `0x10068600` 이 부름)
  세력 지도 0x1005b210 / 세력 점수 0x1005b120 / 위험도 0x1005b5e0 / 안전한 칸 0x1005b6d0
  단계 0x1005b980(깨어남) 0x1005bac0(도망) 0x1005dfd0(자가회복) 0x1005e1e0(공격) 0x1005bd90(휴식) 0x1005bf70(이동)
  대상·칸 고르기 0x1005d070 / 접근 자리 0x1005cb40 / 값 매기기 0x1005c510 / 상태이상 점수 0x1005c480
  AI work 목록 0x10032370 (CChr+0x5a 8칸, 분류 1·2·4 만, 끝에 기본공격 CChr+0x1a)
자세한 것은 옵시디안 분석-전투.md "AI 가 차례에 하는 일 (ba-11)".
"""
import argparse
import collections
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import skill_motion as sm  # noqa: E402
import skill_list as sl  # noqa: E402
import btl_dump as bd  # noqa: E402

NUM_USE = {
    0x3d: '세력 점수 나눗수 1 (ACR×RDP×ATK ÷ 이 값)',
    0x3e: '세력 점수 나눗수 2 ((현재HP+최대HP) ÷ 이 값)',
    0x3f: '세력 점수 나눗수 3',
    0x40: '세력 점수 나눗수 4',
    0x41: '세력 지도 번짐 (점수×n ÷ (거리+n))',
    0x42: '도망 기준 HP %',
    0x44: '(코드에 있으나 닿지 않는 가지)',
    0x45: '도망갈 칸 안전 기준 ×0.01 (적세력/아군세력)',
    0x46: '자가 회복 기준 HP %',
    0x47: '휴식 기준 HP %',
    0x4a: '거리 가중치 (점수×n ÷ (n+거리))',
    0x4b: '오브젝트 기본 점수',
    0x4c: '오브젝트 점수 곱 1',
    0x4e: '오브젝트 점수 곱 2',
    0x4f: '회복기 기준 (잃은 HP % 합 > n×(칸+1))',
    0x5a: '도망 판정: 가장 가까운 적까지 거리 한도',
}
WAKE = {0: '0 즉시', 1: '1 깨어난 같은 편 AI 와 거리 ≤ 값', 2: '2 적과 거리 ≤ 값', 3: '3 시간 틱 ≥ 값'}
MOVE = {
    0: '0 적 목록 · 세력/거리 큰 쪽부터',
    1: '1 적 목록 · 그 칸 위험도/거리 큰 쪽부터',
    2: '2 적 목록 · 거리(높이 포함) 먼 쪽부터',
    3: '3 아군 목록 · 세력/거리 큰 쪽부터',
    4: '4 아군 목록 · 세력/거리 작은 쪽부터',
    5: '5 아군 목록 · 그 칸 위험도/거리 큰 쪽부터',
}
KIND = {0: '0 피해(대상 수)', 1: '1 회복(잃은 HP% 합)', 2: '2 보조(유닛 수)', 3: '3 보조(상태이상 이득)'}
FILTER = {0: '자기', 1: '적', 2: '자기', 3: '아무 칸', 4: '아군', 5: '아무 유닛', 6: '아무 칸', 7: '빈 칸', 8: '오브젝트',
          255: '없음'}
BASIS = ['현재 HP', 'SOUL', 'ATK', '현재 TP', 'RDP', 'ACR', '잃은 HP', '세력 점수', '위험도×1000']


def load_num(game):
    d = bd.read_game_file(game, 'Dat', 'Num.dat')
    _, n, _ = struct.unpack_from('<3H', d)
    return {struct.unpack_from('<H', d, 6 + 6 * i)[0]: struct.unpack_from('<I', d, 8 + 6 * i)[0] for i in range(n)}


def basis_text(v):
    if v > 17:
        return '%d (범위 밖 → 점수 0)' % v
    return '%d = %s %s' % (v, BASIS[v // 2], '최솟값이 좋음' if v & 1 else '최댓값이 좋음')


def battle_units(game, num):
    d = bd.read_game_file(game, 'Btl', '%04d.btl' % num)
    if not d or len(d) < 24:
        return None
    hdr = struct.unpack_from('<12H', d)
    off, out = 24, []
    for _ in range(hdr[10]):
        r = d[off:off + 29]
        off += 29
        if len(r) < 29:
            break
        seq, chrn, x, y = struct.unpack_from('<4H', r)
        army = struct.unpack_from('<H', r, 9)[0] & 0xff
        tail = r[17:29]
        out.append(dict(seq=seq, chr=chrn, x=x, y=y, army=army, tail=tail,
                        mode=struct.unpack_from('<H', tail, 0)[0],
                        wake=struct.unpack_from('<H', tail, 8)[0],
                        wval=struct.unpack_from('<H', tail, 10)[0]))
    return out


def ai_works(g, works, abis, abirec, chrn):
    """0x10032370 과 같은 순서: .chr 어빌리티 8칸(분류 1·2·4) → 기본공격."""
    c = sm.load_chr(g, chrn)
    if not c:
        return None, []
    out = []
    for aid, lv in c['abis']:
        if not aid or aid >= 200 or not lv:
            continue
        rec = abirec.get(aid)
        if not rec or rec['cls'] not in (1, 2, 4):
            continue
        w = (abis.get(aid) or {}).get('levels', {}).get(lv) or (abis.get(aid) or {}).get('levels', {}).get(1)
        out.append((aid, lv, w))
    out.append((None, None, c['basic']))
    return c, out


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('--btl', type=int)
    ap.add_argument('--chr', type=int)
    a = ap.parse_args()

    if a.btl is None and a.chr is None:
        num = load_num(a.game)
        print('# AI 가 쓰는 Num.dat 값')
        for k in sorted(NUM_USE):
            print('  Num[%2d] (0x%02x) = %-6s %s' % (k, k, num.get(k), NUM_USE[k]))
        modes, wakes, army_mode = collections.Counter(), collections.Counter(), collections.Counter()
        nb = 0
        for n in range(1000):
            us = battle_units(a.game, n)
            if not us:
                continue
            nb += 1
            for u in us:
                modes[u['mode']] += 1
                wakes[u['wake']] += 1
                army_mode[(u['army'], u['mode'])] += 1
        print('\n# 전투 %d개, 유닛 %d개' % (nb, sum(modes.values())))
        print('이동 방식 +0x4dc : ' + ', '.join('%d→%d' % kv for kv in sorted(modes.items())))
        print('깨어남 조건 +0x4e4 : ' + ', '.join('%d→%d' % kv for kv in sorted(wakes.items())))
        print('(이동 방식 6 이상, 깨어남 조건 4 이상은 코드에 가지가 없어 제자리 휴식)')
        print('부대×이동 방식 : ' + ', '.join('%s→%d' % (k, v) for k, v in sorted(army_mode.items())))
        return

    g = sm.Game(a.game)
    if a.btl is not None:
        txr = bd.load_txr(a.game)
        us = battle_units(a.game, a.btl)
        if not us:
            print('Btl %04d 없음' % a.btl)
            return
        print('# Btl %04d 의 AI 꼬리' % a.btl)
        for u in us:
            c = sm.load_chr(g, u['chr'])
            nm = txr.get(c['name'], '?') if c else '?'
            print('  #%-3d Chr %-4d %-10s (%2d,%2d) 부대 %d  이동 %s  깨어남 %s(값 %d)  꼬리 %s'
                  % (u['seq'], u['chr'], nm, u['x'], u['y'], u['army'],
                     MOVE.get(u['mode'], '%d = 없음 → 제자리 휴식' % u['mode']),
                     WAKE.get(u['wake'], '%d = 없음 → 늘 휴식' % u['wake']), u['wval'], u['tail'].hex()))
        return

    works = sm.load_works(g)
    abis = sm.load_abis(g, works)
    abirec = sl.load_abi_records(g)
    txr = bd.load_txr(a.game)
    c, lst = ai_works(g, works, abis, abirec, a.chr)
    if not c:
        print('Chr %04d 없음' % a.chr)
        return
    print('# Chr %04d %s — AI 가 보는 work 목록 (앞에서부터 쓸 수 있는 첫 번째를 쓴다)' % (a.chr, txr.get(c['name'], '?')))
    for aid, lv, w in lst:
        r = works.get(w)
        head = ('기본공격' if aid is None else '어빌 %-3d %-12s lv%-2d' % (aid, txr.get(abirec[aid]['name'], '?'), lv))
        if not r:
            print('  %-28s work %s (없음)' % (head, w))
            continue
        print('  %-28s work %-5d 사거리대상 %-8s 효과대상 %-8s 종류 %-16s 최소대상 %d  기준 %s'
              % (head, w, FILTER.get(r[0x13], r[0x13]), FILTER.get(r[0x3c], r[0x3c]),
                 KIND.get(r[0x1f], r[0x1f]), r[0x3d] + 1, basis_text(r[0x3e])))


if __name__ == '__main__':
    main()

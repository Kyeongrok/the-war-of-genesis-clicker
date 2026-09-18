"""창세기전3 파트2 — 어빌리티 한 번에 "몇 번 치나"를 뽑는다 (분석과제 ba-10 뒷부분).

`work_script.py` 가 핸들러의 모든 가지를 이어 붙여 보여 준다면, 이 도구는
  ① 「연」처럼 **어빌리티 레벨로 가지가 갈리는** 핸들러에서 그 레벨의 가지 하나만 고르고,
  ② 캐릭터 Obs 모션의 **종류 6 키(타격 판정)** 를 세어 실제 타수와 타격 시점(틱)을 찍는다.

쓰기:
    python work_hits.py "<게임 폴더>" --chr 221 219 62 193
    python work_hits.py "<게임 폴더>" --abil 1 --sprite 338        # 연 레벨 1~20 표
    python work_hits.py "<게임 폴더>" --sprite 338                 # 그 몸짓의 타격 키 전부

근거 (G3PartII.dll 정적 분석, ImageBase 0x10000000. 옵시디안 [[분석-모션]] ba-10 절):
  0x10072b40  CUnit 모션 키 콜백 — `키 종류 == 6` 일 때만 타격을 낸다.
              키 인자 p0 = **어빌리티 번호** → 0x100322e0 이 그 인물의 레벨로 work 번호를 고르고,
              0x1007b080 이 {종류 = work+0x1f, 공격자, work} 설명자를 채운 뒤
              범위 안의 유닛마다 메시지 1001(0x3e9)을 보낸다(0x10072d38). 곧 **키 하나 = 판정 한 번**.
  0x100322e0  어빌리티 → work: 25(접근 공격) → 1, 26(원거리 공격) → 6, 125(Hunter) → 1468,
              그 밖에는 Abi 레코드의 `+0x1c + 레벨*2`.
  0x1007f860  「연」 핸들러 — work `+0x6`(어빌리티 레벨)로 가지를 나눈다(아래 YEON 표).
              단계는 유닛 +0x94, `add [+0x94],0xa` 는 남은 단계 건너뛰기(0x100e82e0 이 +1).
  0x10072820  SetAction(기준, 방향, 반복) → 모션 = (기준/3)*3 + 방향
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))

from skill_motion import Game, load_abis, load_chr, load_obs_motions, load_works  # noqa: E402

# 「연」(어빌리티 1) 핸들러 0x1007f860 의 레벨별 동작 사슬 — 준비 동작(5 → 7)과 마무리 24 는 빼고 적었다.
# (0x1007f8b7 이 읽는 work +0x6 = 레벨. 0x1007f8bb/f8d7/f8e5/f8f3, 0x1007fb15/fb31 의 비교 그대로)
YEON = [(5, [13]), (9, [14]), (13, [14, 8]), (17, [13, 14]), (999, [13, 14, 8])]

# 레벨로 가지를 나누는 핸들러는 「연」뿐이다(158개 핸들러 가운데 work 표 +0x6 을 읽는 곳이 여기뿐).
LEVEL_BRANCH = {0x1007f860: YEON}


def yeon_chain(level):
    for cut, acts in YEON:
        if level < cut:
            return acts
    return YEON[-1][1]


def hit_keys(motions, action, direction=1):
    """동작 번호 → 그 모션의 타격 키 [(틱, 어빌리티 번호)]. 모션이 없으면 None."""
    if motions is None:
        return None
    mid = action * 3 + (1 if direction == 3 else direction)
    m = motions.get(mid)
    if m is None:
        return None
    return [(k['start'], k['p'][0]) for k in m['keys'] if k['kind'] == 6]


def abil_to_work(abis, abil, level):
    """0x100322e0 과 같은 셈 (인물이 그 어빌리티를 가졌다고 보고)."""
    if abil == 25:
        return 1
    if abil == 26:
        return 6
    if abil == 125:
        return 1468
    a = abis.get(abil)
    return a['levels'].get(level) if a else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('--chr', type=int, nargs='*', default=[])
    ap.add_argument('--sprite', type=int, nargs='*', default=[])
    ap.add_argument('--abil', type=int)
    ap.add_argument('--level', type=int)
    ap.add_argument('--dir', type=int, default=1)
    args = ap.parse_args()

    g = Game(args.game)
    works = load_works(g)
    abis = load_abis(g, works)

    sprites = list(args.sprite)
    for cn in args.chr:
        c = load_chr(g, cn)
        if c:
            sprites.append(c['sprite'])
            print('Chr %d → sprite %d, 기본공격 work %d, 어빌리티 %s'
                  % (cn, c['sprite'], c['basic'], c['abis']))

    if args.abil:
        a = abis[args.abil]
        spr = sprites[0] if sprites else None
        mo = load_obs_motions(g, spr) if spr else None
        print('\n어빌리티 %d (최대 레벨 %d), sprite %s' % (args.abil, a['maxlv'], spr))
        print('레벨 | work | 동작 사슬(준비 5·7 제외) | 타수 | 타격 틱')
        for lv in range(1, a['maxlv'] + 1):
            wid = a['levels'].get(lv)
            if wid is None:
                continue
            acts = yeon_chain(lv) if args.abil == 1 else []
            cells, total = [], 0
            for act in acts:
                ks = hit_keys(mo, act, args.dir) or []
                total += len(ks)
                cells.append('동작%d[%s]' % (act, ','.join('t%d→어빌%d' % k for k in ks)))
            print('%4d | %5d | %-22s | %3d | %s'
                  % (lv, wid, ' → '.join('동작%d' % a2 for a2 in acts), total, ' ; '.join(cells)))
        return

    for spr in sprites:
        mo = load_obs_motions(g, spr)
        if not mo:
            print('sprite %d — Obs 없음' % spr)
            continue
        print('\n== sprite %d (모션 %d개) 타격 키(종류 6), 방향 %d' % (spr, len(mo), args.dir))
        for act in sorted({m // 3 for m in mo}):
            ks = hit_keys(mo, act, args.dir)
            if ks:
                print('  동작 %-3d (모션 %-3d, %4d틱) %d타 : %s'
                      % (act, act * 3 + args.dir, mo[act * 3 + args.dir]['len'], len(ks),
                         ', '.join('t%d 어빌%d→work %s' % (t, ab, abil_to_work(abis, ab, 1)) for t, ab in ks)))


if __name__ == '__main__':
    main()

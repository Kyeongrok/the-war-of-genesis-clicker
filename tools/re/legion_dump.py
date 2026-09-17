"""창세기전3 파트2 — 군단(For.dat) 표와 군단이 쓰이는 곳(전투 배치·스크립트)을 뽑는다.

쓰기:
    python tools/re/legion_dump.py "<게임 폴더>"            # 군단 표 + 얻는 곳 + 전투별 사용
    python tools/re/legion_dump.py "<게임 폴더>" --md       # 마크다운 표로

게임 폴더는 읽기만 한다(낱장 우선, 없으면 .idx/.pak). 근거는 옵시디안 분석/분석-군단.md.

Dat\\For.dat — LoadForData 0x1004ce40, 전역 0x101b6840(64바이트 × (최대번호+1)), 0x101b6844 = 최대번호+1
  머리 u16 0, u16 레코드 수, u16 최대 번호. 레코드 59바이트(파일 끝까지 정확히 맞음):
  0  u16 번호                         (메모리 +0 = 1 이면 쓰는 칸)
  2  u16 이름 TXR                      +0x04
  4  u16×6 부하 Chr 번호(0 = 빈 칸)     +0x06
  16 u8  진형 0~5                      +0x12  → TXR 813+진형(학익진·일자형·십자형·역학익진·젓가락형·이자형),
                                                칸 오프셋 표 0x10164838 ((dx,dy)×6, 방향마다 돌림)
  17 u16×5 부하 능력치 보정 LP·TP·PSY·DEX·DEP  +0x14..+0x1c
        부하 게터가 대장의 CChr+0x148[군단](군단 세력, 로드 때 1000)× 값 /100 을 더함:
        LP 0x1007ac70(+0x14), PSY 0x1007ade0(+0x18), DEP 0x1007af20(+0x1c). TP·DEX(+0x16·+0x1a)는 읽는 곳 없음
        (부하의 TP·DEX 게터는 대장 것을 그대로 씀).
  27 (u16 어빌리티, u16 필요 세력, u16 대장 Chr)×5  +0x1e  군단기. 0x10032580/0x10032760: CChr+0x1c(배속 군단)가
        이 군단이고 필요 세력 ≤ CChr+0x148[군단] 이고 대장 칸이 0 이거나 CChr 번호와 같을 때 목록에 넣음
  57 u16 설명 TXR                      +0x3c
스크립트 행동(전투 이벤트 0x10056800 표, 필드·챕터 0x100f3600 표 — 번호가 같음):
  713 군단 얻기(인자0 = 군단)    → 파티 +0x910 군단 목록에 +1 (0x1004df50)
  714 군단 세력 계산(인자0 군단, 인자1 Chr, 인자2 0 더하기·1 빼기·2 곱하기·3 나누기, 인자3 값) → 유닛 CChr+0x148[군단]
  715 군단 세력 정하기(인자0 군단, 인자1 Chr, 인자2 값)
Btl 캐릭터 레코드 파일 15 = 군단 번호(1 = 머리 워드 7 이 켜진 전투에서 "그 인물에게 배속된 군단").
"""
import argparse
import collections
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from btl_dump import load_txr, parse_btl, read_game_file  # noqa: E402
from battle_list import list_ids, parse_chp, parse_fld  # noqa: E402

FORMATION = ['학익진', '일자형', '십자형', '역학익진', '젓가락형', '이자형']
STATS = ['LP', 'TP', 'PSY', 'DEX', 'DEP']
LEGION_ACTS = {713: '군단 얻기', 714: '군단 세력 계산', 715: '군단 세력 정하기'}


def load_for(game):
    d = read_game_file(game, 'Dat', 'For.dat')
    _, n, mx = struct.unpack_from('<3H', d, 0)
    assert 6 + n * 59 + 2 == len(d) or 6 + n * 59 == len(d), (n, len(d))
    out = []
    for i in range(n):
        r = d[6 + 59 * i:6 + 59 * (i + 1)]
        idx, name = struct.unpack_from('<2H', r, 0)
        out.append(dict(id=idx, name=name, chrs=list(struct.unpack_from('<6H', r, 4)), form=r[16],
                        stats=list(struct.unpack_from('<5H', r, 17)),
                        abis=[struct.unpack_from('<3H', r, 27 + 6 * k) for k in range(5)],
                        desc=struct.unpack_from('<H', r, 57)[0]))
    return out, mx


def chr_name(game, txr, n, cache={}):
    if n not in cache:
        b = read_game_file(game, 'Chr', '%04d.chr' % n)
        cache[n] = txr.get(struct.unpack_from('<H', b, 2)[0], '?') if b and len(b) == 90 else '?'
    return cache[n]


def abi_names(game, txr):
    out = {}
    b = read_game_file(game, 'Abi', '0019.abi')
    _, n, _ = struct.unpack_from('<3H', b, 0)
    for i in range(n):
        idx, name = struct.unpack_from('<2H', b, 6 + 26 * i)
        out[idx] = txr.get(name, '?')
    return out


def script_acts(ev_list, key_acts='acts'):
    for e in ev_list:
        for code, args in e[key_acts]:
            if code in LEGION_ACTS:
                yield code, args


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('--md', action='store_true')
    a = ap.parse_args()
    g = a.game
    txr = load_txr(g)
    t = lambda i: txr.get(i, '?')
    legions, mx = load_for(g)
    abis = abi_names(g, txr)
    by_id = {L['id']: L for L in legions}

    # 얻는 곳: 챕터·필드 스크립트, 전투 이벤트
    gets = collections.defaultdict(list)
    power = []
    for i in list_ids(g, 'Chp', '.chp'):
        try:
            c = parse_chp(read_game_file(g, 'Chp', f'{i:04d}.chp'))
        except struct.error:
            continue
        for code, args in script_acts(c['script']):
            if code == 713:
                gets[args[0]].append(f"Chp {i:04d}「{t(c['hdr']['title'])}」")
            else:
                power.append((f'Chp {i:04d}', code, args))
    for i in list_ids(g, 'Fld', '.fld'):
        try:
            f = parse_fld(read_game_file(g, 'Fld', f'{i:04d}.fld'))
        except struct.error:
            continue
        for code, args in script_acts(f['script']):
            if code == 713:
                gets[args[0]].append(f'Fld {i:04d}')
            else:
                power.append((f'Fld {i:04d}', code, args))
    uses = collections.defaultdict(list)
    allow = []
    for i in list_ids(g, 'Btl', '.btl'):
        try:
            bt = parse_btl(read_game_file(g, 'Btl', f'{i:04d}.btl'))
        except struct.error:
            continue
        if bt['hdr'][7]:
            allow.append(i)
        for r in bt['A']:
            if r['squad'] > 0:
                uses[r['squad']].append((i, r['chr'], r['army']))
        for e in bt['D']:
            for code, raw in e['acts']:
                if code in LEGION_ACTS:
                    args = struct.unpack('<8h', bytes.fromhex(raw))
                    if code == 713:
                        gets[args[0]].append(f'Btl {i:04d} 이벤트')
                    else:
                        power.append((f'Btl {i:04d}', code, args))

    bar = '|' if a.md else ' '
    print(f'For.dat 레코드 {len(legions)}개, 최대 번호 {mx}')
    if a.md:
        print('\n| 번호 | 이름 | 진형 | 부하 (Chr) | 보정 LP/TP/PSY/DEX/DEP | 군단기 (필요 세력, 대장) | 얻는 곳 | Btl 사용 |')
        print('|---|---|---|---|---|---|---|---|')
    for L in sorted(legions, key=lambda x: x['id']):
        mem = collections.Counter(c for c in L['chrs'] if c)
        mems = ', '.join(f"{chr_name(g, txr, c)}({c})" + (f'×{k}' if k > 1 else '') for c, k in mem.items())
        ab = '; '.join(f"{ai} {abis.get(ai, '?')}(≥{cond}, 대장 {chr_name(g, txr, ch) + '(' + str(ch) + ')' if ch else '누구나'})"
                       for ai, cond, ch in L['abis'] if ai)
        st = '/'.join(map(str, L['stats']))
        form = FORMATION[L['form']] if L['form'] < 6 else str(L['form'])
        got = ', '.join(gets.get(L['id'], [])) or '-'
        u = uses.get(L['id'], [])
        ustr = f"{len(u)}회 ({', '.join(sorted({'%04d' % b for b, _, _ in u})[:6])}{'…' if len({b for b, _, _ in u}) > 6 else ''})" if u else '-'
        cells = [str(L['id']), t(L['name']), form, mems or '-', st, ab or '-', got, ustr]
        if a.md:
            print('| ' + ' | '.join(cells) + ' |')
        else:
            print('  '.join(cells))
    print()
    print('군단 사용 허용 전투(머리 워드 7):', ' '.join('%04d' % b for b in allow) or '-')
    if power:
        print('군단 세력 스크립트:')
        for where, code, args in power:
            print(f'  {where} {code} {LEGION_ACTS[code]} {list(args)}')
    unk = [k for k in gets if k not in by_id]
    if unk:
        print('For.dat 에 없는 군단을 주는 스크립트:', unk)


if __name__ == '__main__':
    main()

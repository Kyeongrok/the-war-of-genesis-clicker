"""work 핸들러가 띄우는 그림을 <b>모두</b> 뽑는다 — `work_script` 가 놓치는 길까지 (fx-189).

    python tools/re/work_fx_extra.py "<게임 폴더>" [--out 표.tsv] [--json 표.json] [--work 59 735 ...]

`work_script.Analyzer.script` 는 `CEffect::CEffect`(0x100c1fa0) 를 <b>직접</b> 부르는 곳만 'eff' 로 적는다.
그래서 그림 없는 소리 껍데기(1338·1332·1324 …)만 남는 기술이 수백 개였다. 원본이 실제로 그림을 띄우는 길은 넷이다
(근거 주소는 옵시디안 [[분석-스킬]] 「소리 껍데기만 남은 기술의 그림 (fx-189)」):

  ① <b>CEffect 파생 클래스</b> — 생성자가 받은 인자 8개 (Obs, 모션, x, y, z, 붙일 곳, 주인, work) 를 그대로
     0x100c1fa0 에 넘기고, 움직임(날아가기·흩뿌리기·떨어지기)을 덧붙인다. 격려(work 59, 0x10091670) 의
     0x1009178e call 0x100c5940(670, 1, …) · 0x1009192a call 0x100c5b20(478, 3, 대상 둘레 …) 가 그 예.
  ② <b>몸 복제</b> — Obs 자리에 `[유닛+0x58]+0x8`(애니메이터의 지금 Obs = 그 유닛의 sprite)을 넣는다.
     파·혼(분신)·회피·이스케이프·웹폰 크래쉬(대상 몸).
  ③ <b>영상(Bink) 이펙트</b> — 0x100d0770(x, y, z, 붙일 곳, 주인, work) 뒤에 0x100d0970("Mov\\NNNN.mov") 과
     0x100d08b0(그리기, ?, ?, dx, dy, ?) 를 부른다. 준비 동작 +0x3f = 2·5 는 Mov 0041(파란 불기둥),
     3·6·7 은 Mov 0042 를 시전자에게 붙인다. 리 바이블은 Mov 0034 를 대상에게.
  ④ <b>코드로 그리는 이펙트</b> — 생성자 바탕이 0x100c1ef0(Obs 없는 6인자 CEffect). 그림 자료가 없다
     (익스퍼트 웨이브 0x100cfae0 파문, 다이나믹 크래쉬 0x100c3c50·0x100ca7f0 …).

이 도구는 호출 대상을 가리지 않고 <b>인자 끝이 (…, 붙일 곳, 나, 나.work) 인 호출</b>을 모두 이펙트로 보고,
생성자를 거슬러 올라가 바탕(0x100c1fa0 = Obs 있음 / 0x100c1ef0 = Obs 없음)을 가린다.

또 하나 고친 것: `work_script.Analyzer.targets()` 는 0xe8 바이트를 날로 훑어 가짜 함수 시작이 섞인다.
그 탓에 핸들러를 중간에서 잘랐다(엘레맨탈 아이스 0x1009efb0 이 0x1009f010 에서 끊김). 여기서는 실제로 풀린
call 명령의 대상 가운데 앞 바이트가 채움(0x90·0xcc)·ret 인 것만 함수 시작으로 본다.

쓰는 것: work_script(Analyzer·PRELUDE), obs_ui_dump.load_motions.
"""
import argparse
import bisect
import collections
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import work_script as ws                                    # noqa: E402
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_OP_REG  # noqa: E402
from obs_ui_dump import load_motions                        # noqa: E402

BASE_OBS = 0x100c1fa0      # CEffect(Obs, 모션, x, y, z, 붙일 곳, 주인, work)
BASE_CODE = 0x100c1ef0     # CEffect(x, y, z, 붙일 곳, 주인, work) — Obs 없음
MOV_CTOR = 0x100d0770      # 영상 이펙트 (바탕 BASE_CODE)
MOV_OPEN = 0x100d0970      # (파일 이름)
MOV_PARAM = 0x100d08b0     # (그리기 +0x134, +0x135, +0x136, dx +0x138, dy +0x13a, +0x64)
# 코드 이펙트(Obs 없는 바탕) 가운데 <b>뿌리개</b>: 생성 뒤 첫 설정 호출이 (Obs, 모션, 개수 …) 를 받아
# 안에서 CEffect/0x100c3340 을 여러 개 만든다. 값 = (Obs 인자 자리, 모션 인자 자리 또는 None = 방향마다 다름).
# 확인: 0x100cc890 → 0x100ccabe call 0x100c3340(movsx [ebp+8], [ebp+0xc], …),
#       0x100cd510 → +0x12c/+0x12e 에 두었다가 0x100cd5f4 call 0x100c1fa0([+0x12c], [+0x12e], …),
#       0x100ccbd0·0x100ccf60 → Obs = [ebp+8], 모션 = 돌이 번호(0~0x11, 방향 그림).
EMITTERS = {
    0x100cb980: (0, 1),    # 입자 (0x100cb8a0) — work_script 의 'partset'
    0x100cc890: (0, 1),    # 0x100cc810 (메테오스트라이크·블리자드 …)
    0x100cc680: (0, 1),    # 0x100cc600
    0x100cc280: (0, 1),    # 0x100cc200
    0x100cc470: (0, 1),    # 0x100cc3f0
    0x100cbe00: (0, 1),    # 0x100cbd20
    0x100cd510: (0, 1),    # 0x100cd490 (크래쉬 봄 …)
    0x100cae10: (0, 1),    # 0x100ca7f0 (다이나믹 크래쉬) — 가설: 필드 +0x12e/+0x130 을 그리기에서 씀
    0x100ccbd0: (0, None),  # 0x100ccb50 — 모션 = 방향 번호
    0x100ccf60: (0, None),  # 0x100ccee0 — 모션 = 방향 번호
}
DELAY = ws.DELAY           # 0x100c2530(n) 시작 지연
LIFE = ws.LIFE             # 0x100c24d0(n) 수명


class ExtraAnalyzer(ws.Analyzer):
    _argc = {}
    _base = {}

    def targets(self):
        """함수 시작 = 실제로 풀린 call 의 대상 가운데 앞 바이트가 채움(0x90·0xcc)·ret(0xc3, 0xc2 xx xx) 인 것."""
        if self._targets is None:
            s = set()
            lo, hi = 0x10001000, 0x10134000
            va = lo
            while va < hi:
                for a, size, mn, op in self.md.disasm_lite(self.d[va - ws.BASE:hi - ws.BASE], va):
                    va = a + size
                    if mn == 'call' and op.startswith('0x'):
                        t = int(op, 16)
                        if lo <= t < hi:
                            s.add(t)
                if va < hi:
                    va += 1
            self._targets = [t for t in sorted(s)
                             if self.d[t - ws.BASE - 1] in (0x90, 0xcc, 0xc3) or self.d[t - ws.BASE - 3] == 0xc2]
        return self._targets

    def argc(self, va):
        """stdcall/thiscall 함수가 스스로 걷는 인자 수(ret N / 4). cdecl 이면 0 (호출 뒤 add esp 가 걷는다)."""
        if va not in self._argc:
            n = None
            if 0x10001000 <= va < 0x10134000:
                for x in self.md.disasm(self.d[va - ws.BASE:va - ws.BASE + 0x4000], va):
                    if x.mnemonic == 'ret':
                        n = (x.operands[0].imm // 4) if x.operands else 0
                        break
                    if x.mnemonic == 'jmp' and x.operands[0].type == X86_OP_IMM \
                            and not (va <= x.operands[0].imm < va + 0x4000):
                        n = self.argc(x.operands[0].imm) if x.operands[0].imm != va else None
                        break
            self._argc[va] = n
        return self._argc[va]

    def base_of(self, va, depth=4):
        """생성자가 결국 부르는 CEffect 바탕 — BASE_OBS / BASE_CODE / None."""
        if va in (BASE_OBS, BASE_CODE):
            return va
        if va is None or depth == 0:
            return None
        if va not in self._base:
            self._base[va] = None
            for x in self.md.disasm(self.d[va - ws.BASE:va - ws.BASE + 0x100], va):
                if x.mnemonic == 'call' and x.operands[0].type == X86_OP_IMM:
                    b = self.base_of(x.operands[0].imm, depth - 1)
                    if b:
                        self._base[va] = b
                        break
                if x.mnemonic == 'ret':
                    break
        return self._base[va]

    def cstr(self, va):
        o = va - ws.BASE
        if not (0 <= o < len(self.d)):
            return None
        e = self.d.find(b'\0', o, o + 64)
        s = self.d[o:e] if e > o else b''
        return s.decode('ascii') if s and all(32 <= c < 127 for c in s) else None

    def effects(self, start, depth=2, _seen=None, _path=()):
        """핸들러 하나에서 이펙트 기록(dict) 목록."""
        _seen = _seen if _seen is not None else set()
        if start in _seen or depth < 0:
            return []
        _seen.add(start)
        t = self.targets()
        k = bisect.bisect_right(t, start)
        end = min(t[k] if k < len(t) else start + 0x1000, start + 0x6000)
        ins = list(self.md.disasm(self.d[start - ws.BASE:end - ws.BASE], start))
        reg = {'ecx': '나', 'esi': '나'}
        stack, out = [], []
        stage, last_str, last = None, None, None

        def rd(op, i):
            if op.type == X86_OP_IMM:
                return op.imm & 0xffffffff
            if op.type == X86_OP_REG:
                return reg.get(i.reg_name(op.reg), i.reg_name(op.reg))
            if op.type == X86_OP_MEM:
                m = op.mem
                b = reg.get(i.reg_name(m.base), i.reg_name(m.base)) if m.base else None
                if b is None:
                    return 'ds:0x%x' % (m.disp & 0xffffffff) if (m.disp & 0xffffffff) != ws.MAP_GLOBAL else '맵'
                f = ws.FIELD.get(m.disp)
                return '%s.%s' % (b, f) if f else '%s+0x%x' % (b, m.disp)
            return '?'

        for i in ins:
            m, ops = i.mnemonic, i.operands
            if m == 'push':
                v = rd(ops[0], i)
                stack.append(v)
                if isinstance(v, int) and 0x10140000 <= v < 0x101b0000:
                    s = self.cstr(v)
                    if s and s.lower().startswith('mov\\'):
                        last_str = s
            elif m in ('mov', 'movzx', 'movsx', 'lea') and ops[0].type == X86_OP_REG:
                r = i.reg_name(ops[0].reg)
                base = {'al': 'eax', 'ax': 'eax', 'bl': 'ebx', 'bx': 'ebx', 'cl': 'ecx', 'cx': 'ecx',
                        'dl': 'edx', 'dx': 'edx', 'di': 'edi', 'si': 'esi', 'bp': 'ebp'}.get(r, r)
                v = rd(ops[1], i)
                if m == 'lea' and ops[1].type == X86_OP_MEM:
                    v = '&' + v
                reg[base] = v
            elif m == 'add' and len(ops) == 2 and ops[0].type == X86_OP_REG and i.reg_name(ops[0].reg) == 'esp' \
                    and ops[1].type == X86_OP_IMM:
                n = ops[1].imm // 4                  # cdecl 호출 뒤 인자 걷기
                if n:
                    del stack[-n:]
            elif m in ('add', 'sub') and ops[0].type == X86_OP_REG:
                r = i.reg_name(ops[0].reg)
                base = {'cx': 'ecx', 'dx': 'edx', 'ax': 'eax', 'bx': 'ebx'}.get(r, r)
                v = reg.get(base)
                if isinstance(v, str) and not v.startswith('&'):
                    reg[base] = '%s%s%s' % (v, '+' if m == 'add' else '-',
                                            ops[1].imm if ops[1].type == X86_OP_IMM else '?')
            elif m == 'xor' and len(ops) == 2 and ops[0].type == X86_OP_REG and ops[1].type == X86_OP_REG \
                    and ops[0].reg == ops[1].reg:
                reg[i.reg_name(ops[0].reg)] = 0
            elif m == 'cmp' and ops[0].type == X86_OP_REG and ops[1].type == X86_OP_IMM:
                if reg.get({'al': 'eax'}.get(i.reg_name(ops[0].reg), i.reg_name(ops[0].reg))) == '나.단계':
                    stage = ops[1].imm
            elif m == 'test' and len(ops) == 2 and ops[0].type == X86_OP_REG and ops[0].reg == ops[1].reg:
                if reg.get({'al': 'eax'}.get(i.reg_name(ops[0].reg), i.reg_name(ops[0].reg))) == '나.단계':
                    stage = 0
            elif m == 'call':
                a = stack[::-1]
                tgt = ops[0].imm if ops[0].type == X86_OP_IMM else None
                hit = None
                for j in range(3, len(a) - 2):       # (…, x, y, z, 붙일 곳, 주인=나, work=나.work)
                    if a[j + 1] == '나' and a[j + 2] == '나.work':
                        hit = j
                        break
                if hit is not None:
                    base = self.base_of(tgt)
                    j = hit
                    rec = {'va': i.address, 'ctor': tgt, 'base': base, 'stage': stage, 'path': _path,
                           'x': a[j - 3], 'y': a[j - 2], 'z': a[j - 1], 'parent': a[j],
                           'obs': None, 'motion': None, 'kind': 'code', 'mov': None, 'movparam': None,
                           'delay': None, 'life': None, 'setter': None}
                    # 인자는 첫 인자가 a[0] 이다. 붙일 곳이 여섯째(j == 5)면 앞 둘이 (Obs, 모션),
                    # 넷째(j == 3)면 Obs 없는 6인자 꼴. 0x100c61c0 처럼 바탕은 Obs 없는 꼴을 쓰고
                    # Obs 는 따로 애니메이터(0x10025b80)로 도는 파생도 있어 바탕만으로 가르지 않는다.
                    if j >= 5 and tgt != MOV_CTOR:
                        rec['obs'], rec['motion'] = a[j - 5], a[j - 4]
                        o = rec['obs']
                        rec['kind'] = 'obs' if isinstance(o, int) else \
                            ('body:target' if isinstance(o, str) and '대상.애니' in o else
                             'body:self' if isinstance(o, str) and o.startswith('나.애니') else 'obs?')
                    elif tgt == MOV_CTOR:
                        rec['kind'] = 'mov'
                    out.append(rec)
                    last = rec
                elif last is not None and last['kind'] == 'code' and last['setter'] is None and tgt                         and reg.get('ecx') != '나' and tgt not in (DELAY, LIFE) and a:
                    last['setter'] = (tgt, a[:4])          # 코드 이펙트 바로 뒤 첫 설정 호출
                    if tgt in EMITTERS:
                        oi, mi = EMITTERS[tgt]
                        last['kind'] = 'emit'
                        last['obs'] = a[oi] if oi < len(a) else None
                        last['motion'] = a[mi] if mi is not None and mi < len(a) else None
                elif tgt == MOV_OPEN and last is not None and last['kind'] == 'mov':
                    last['mov'] = last_str
                elif tgt == MOV_PARAM and last is not None and len(a) >= 6:
                    last['movparam'] = [ws.sname(v) for v in a[:6]]
                elif tgt == DELAY and last is not None and a and last['delay'] is None:
                    last['delay'] = a[0]
                elif tgt == LIFE and last is not None and a and last['life'] is None:
                    last['life'] = a[0]
                elif tgt and depth > 0 and 0x10076000 <= tgt < 0x100c1f00:
                    out += self.effects(tgt, depth - 1, _seen, _path + (tgt,))
                n = self.argc(tgt) if tgt else None
                if n is None:
                    stack = []
                elif n:
                    del stack[-n:]
                reg.pop('eax', None)
        return out


_pics = {}


def pictures(game, obs, motion, depth=0):
    """(Obs, 모션) 이 실제로 그리는 (Obs, 모션) 들 — 그림 키(종류 0)가 있으면 자신, 자식 키(종류 2)는 따라간다."""
    if (obs, motion) in _pics:
        return _pics[(obs, motion)]
    out = []
    try:
        d = game.read('Obs', '%04d.obs' % obs)
        m = load_motions(d).get(motion) if d else None
    except Exception:
        m = None
    if m:
        if any(k['kind'] == 0 for k in m['keys']):
            out.append((obs, motion))
        if depth < 3:
            for k in m['keys']:
                if k['kind'] == 2 and k['p'] and k['p'][0] > 0:
                    for c in pictures(game, int(k['p'][0]), int(k['p'][1]), depth + 1):
                        if c not in out:
                            out.append(c)
    _pics[(obs, motion)] = out
    return out


def where_of(rec):
    """'target' · 'self' · 'cell'(코드가 셈한 자리 — 대부분 목표 칸 둘레)."""
    p, x = rec['parent'], rec['x']
    if isinstance(p, str) and '대상' in p:
        return 'target'
    if p == '나':
        return 'self'
    if isinstance(x, str) and '대상' in x:
        return 'target'
    if isinstance(x, str) and x.startswith('나.'):
        return 'self'
    return 'cell'


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('--out', help='TSV 로 적을 곳(없으면 표준출력)')
    ap.add_argument('--json', help='work 별 요약 JSON')
    ap.add_argument('--work', nargs='*', type=int)
    ap.add_argument('--repo', default=os.path.join(HERE, '..', '..'))
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except Exception:
        pass
    game = ws.Game(a.game)
    dll = ExtraAnalyzer(os.path.join(a.game, 'G3PartII.dll'))
    works = ws.load_works(game)
    abis = ws.load_abis(game, works)
    text = {}
    try:
        import extract_character as e
        for r in e.parse_txr(game.read('TXR', 'Txr.dat')):
            text.setdefault(r['txr_id'], r['text'])
    except Exception:
        pass
    have = set()
    fxdir = os.path.join(a.repo, 'assets', 'effects')
    if os.path.isdir(fxdir):
        have = {int(f[:4]) for f in os.listdir(fxdir) if f[:4].isdigit()}

    ids = a.work or sorted(works)
    rows, summary = [], {}
    kinds, missing, movs = collections.Counter(), collections.Counter(), collections.Counter()
    for wid in ids:
        w = works.get(wid)
        if not w:
            continue
        try:
            h = dll.handler(wid)
        except Exception:
            continue
        seq = ([('준비', ws.PRELUDE[w[0x3f]][0])] if w[0x3f] in ws.PRELUDE else []) + [('핸들러', h)]
        ab = abis.get(w[0x4])
        name = text.get(ab['name'], '?') if ab else '?'
        items = []
        for label, hh in seq:
            for r in dll.effects(hh):
                kind = r['kind']
                pics = []
                if kind in ('obs', 'emit') and isinstance(r['obs'], int):
                    if isinstance(r['motion'], int):
                        pics = pictures(game, r['obs'], r['motion'])
                    elif kind == 'emit':           # 모션이 방향마다 다름 — 그 Obs 의 모션 전부
                        try:
                            mm = load_motions(game.read('Obs', '%04d.obs' % r['obs']))
                        except Exception:
                            mm = {}
                        for mo in sorted(mm):
                            pics += [p for p in pictures(game, r['obs'], mo) if p not in pics]
                if kind == 'obs' and not isinstance(r['motion'], int):
                    kind = 'obs?'                     # 모션을 코드가 셈한다 — 손으로 볼 것
                elif kind == 'obs' and not pics:
                    kind = 'sound'                    # 소리 껍데기(그림 키 없음)
                kinds[kind] += 1
                for o, _ in pics:
                    if o not in have:
                        missing[o] += 1
                if r['mov']:
                    movs[r['mov']] += 1
                where = where_of(r)
                rows.append((wid, w[0x4], name, w[0x6], label, hex(hh), hex(r['va']),
                             hex(r['ctor']) if r['ctor'] else 'indirect', kind,
                             ws.sname(r['obs']) if r['obs'] is not None else '-',
                             ws.sname(r['motion']) if r['motion'] is not None else '-',
                             where, ws.sname(r['x']), ws.sname(r['y']), ws.sname(r['z']), ws.sname(r['parent']),
                             '-' if r['stage'] is None else r['stage'],
                             ws.sname(r['delay']) if r['delay'] is not None else '-',
                             ws.sname(r['life']) if r['life'] is not None else '-',
                             ' '.join('%d:%d' % p for p in pics) or '-',
                             r['mov'] or '-', ','.join(r['movparam']) if r['movparam'] else '-'))
                if kind in ('obs', 'emit', 'body:self', 'body:target', 'mov'):
                    items.append({'from': label, 'kind': kind, 'ctor': hex(r['ctor']) if r['ctor'] else None,
                                  'pics': pics, 'motion': r['motion'] if isinstance(r['motion'], int) else None,
                                  'where': where, 'stage': r['stage'],
                                  'delay': r['delay'] if isinstance(r['delay'], int) else None,
                                  'life': r['life'] if isinstance(r['life'], int) else None,
                                  'mov': r['mov'], 'movparam': r['movparam']})
        summary[wid] = {'abi': w[0x4], 'name': name, 'level': w[0x6], 'handler': hex(h), 'items': items}
    hdr = ('work', 'abi', '이름', 'Lv', '갈래', '핸들러', '호출VA', '생성자', '종류', 'Obs', '모션', '자리',
           'x', 'y', 'z', '붙일곳', '단계', '지연', '수명', '그림', '영상', '영상인자')
    lines = ['\t'.join(hdr)] + ['\t'.join(str(c) for c in r) for r in rows]
    if a.out:
        with open(a.out, 'w', encoding='utf-8') as f:
            f.write('\n'.join(lines) + '\n')
    else:
        print('\n'.join(lines))
    if a.json:
        with open(a.json, 'w', encoding='utf-8') as f:
            json.dump(summary, f, ensure_ascii=False, indent=0)
    print('종류별 수:', dict(kinds), file=sys.stderr)
    print('영상:', dict(movs), file=sys.stderr)
    print('assets/effects 에 없는 그림 Obs:', sorted(missing), file=sys.stderr)


if __name__ == '__main__':
    main()

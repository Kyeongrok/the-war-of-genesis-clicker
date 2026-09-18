"""창세기전3 파트2 — work(기술) 한 번이 실제로 재생하는 "대본"을 뽑는다.

`skill_motion.py` 가 "어떤 동작·이펙트를 부르나"까지 짚었다면, 이 도구는 그 호출의
**인자를 기호로 풀어** 단계(stage)·동작·틱·이펙트 자리(시전자냐 대상이냐)·소리까지 한 줄로 만든다.

쓰기:
    python work_script.py <게임 폴더> --chr 221 219 62 193
    python work_script.py <게임 폴더> --work 1 58 87 190 12 10 59 467 469 735 1583 1641
    python work_script.py <게임 폴더> --chr 62 --dir 1 --no-sound

근거 (G3PartII.dll 정적 분석; 자세한 것은 옵시디안 [[분석-모션]] ba-8·ba-10 절):
  0x1007ca40   work 실행 — +0x3f 준비 동작 핸들러 → work 번호 switch → work 별 핸들러
  0x10072820   CUnit::SetAction(기준 동작, 방향, 반복)   모션 = (기준/3)*3 + 방향
  0x100e53c0   애니메이터 PlayMotion(모션 번호)
  0x100c1fa0   CEffect::CEffect(Obs, 모션, x, y, z, 맵 0x101be2dc, 주인, work)
               → 화면 자리는 (x, y, z). 유닛 필드 +0x3e/+0x40/+0x42 = 월드 x·y·z
  0x100cb8a0   입자(particle) 이펙트 생성 (Obs 는 뒤이은 0x100cb980 의 첫 인자)
  0x100c2530(n) 이펙트 시작 지연 n틱, 0x100c24d0(n) 이펙트 수명/뒤늦은 처리 n틱
  유닛 필드: +0x80 지금 work 번호, +0x8c 목표 유닛, +0x94 단계, +0x96 단계 안 틱
  소리: Obs 모션 키 종류 1(Snd 번호) — 이펙트 Obs 의 모션에도 들어 있다 (obs_sounds.py)
"""
import argparse
import bisect
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))

import capstone                                                          # noqa: E402
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_OP_REG              # noqa: E402

from skill_motion import (BASE, Game, load_abis, load_chr, load_obs_motions,   # noqa: E402
                          load_works, resolve_motion, PRELUDE)
import obs_sounds                                                        # noqa: E402

SETACT = 0x10072820
SETMOT = 0x100e53c0
EFFECT = 0x100c1fa0
PARTICLE = 0x100cb8a0
PARTSET = 0x100cb980
DELAY = 0x100c2530
LIFE = 0x100c24d0
WORKEND = 0x100762d0
DISPATCH_SWITCH = 0x1007cb13
MAP_GLOBAL = 0x101be2dc

FIELD = {0x3e: 'x', 0x40: 'y', 0x42: 'z', 0x58: '애니', 0x5c: '방향',
         0x80: 'work', 0x8c: '대상', 0x94: '단계', 0x96: '틱'}


def sname(v):
    """기호 이름 다듬기."""
    return v if isinstance(v, str) else ('%d' % v)


class Analyzer:
    def __init__(self, path):
        self.d = open(path, 'rb').read()
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        self.md.detail = True
        self._targets = None

    def u8(self, va):
        return self.d[va - BASE]

    def u32(self, va):
        import struct
        return struct.unpack_from('<I', self.d, va - BASE)[0]

    def insn(self, va):
        return next(self.md.disasm(self.d[va - BASE:va - BASE + 16], va))

    def targets(self):
        import struct
        if self._targets is None:
            s = set()
            code = self.d[0x1000:0x134000]
            for mt in re.finditer(rb'\xe8', code):
                o = mt.start()
                if o + 5 > len(code):
                    break
                t = (0x10001000 + o + 5 + struct.unpack_from('<i', code, o + 1)[0]) & 0xffffffff
                if 0x10001000 <= t < 0x10134000:
                    s.add(t)
            self._targets = sorted(s)
        return self._targets

    def handler(self, wid):
        """0x1007ca40 의 work 번호 switch 를 따라가 핸들러 주소를 얻는다 (skill_motion 과 같음)."""
        eax, edx, flags, va = wid, 0, (0, 0), DISPATCH_SWITCH
        for _ in range(300):
            i = self.insn(va)
            m, s, ops = i.mnemonic, i.op_str, i.operands
            if m == 'and' and s == 'eax, 0xffff':
                eax &= 0xffff
            elif m == 'cmp' and s.startswith('eax, '):
                flags = (eax, ops[1].imm & 0xffffffff)
            elif m == 'add' and s.startswith('eax, '):
                eax = (eax + ops[1].imm) & 0xffffffff
            elif m == 'sub' and s.startswith('eax, '):
                eax = (eax - ops[1].imm) & 0xffffffff
                flags = (eax, 0)
            elif m == 'dec' and s == 'eax':
                eax = (eax - 1) & 0xffffffff
                flags = (eax, 0)
            elif m == 'xor' and s == 'edx, edx':
                edx = 0
            elif m == 'mov' and s.startswith('dl, byte ptr [eax + '):
                edx = self.u8(eax + ops[1].mem.disp)
            elif m == 'jmp' and ops[0].type == X86_OP_MEM:
                mm = ops[0].mem
                idx = edx if mm.index else eax
                va = self.u32((idx * mm.scale + mm.disp) & 0xffffffff)
                continue
            elif m == 'jmp':
                va = ops[0].imm
                continue
            elif m.startswith('j'):
                a, b = flags
                sa = a - (1 << 32) if a >= 1 << 31 else a
                sb = b - (1 << 32) if b >= 1 << 31 else b
                c = {'jg': sa > sb, 'jge': sa >= sb, 'jl': sa < sb, 'jle': sa <= sb, 'je': a == b, 'jne': a != b,
                     'ja': a > b, 'jae': a >= b, 'jb': a < b, 'jbe': a <= b}[m]
                if c:
                    va = ops[0].imm
                    continue
            elif m == 'call':
                return ops[0].imm
            va += i.size
        raise RuntimeError('switch loop')

    # ---------------- 기호 실행 ----------------

    def script(self, start, depth=1, _seen=None):
        """핸들러 하나를 훑어 [(VA, 종류, 값…)] 목록을 만든다.

        종류: 'stage'(단계 비교), 'act'(SetAction), 'mot'(PlayMotion),
              'eff'(이펙트), 'part'(입자), 'delay', 'life', 'end'(work 끝)
        """
        _seen = _seen if _seen is not None else set()
        if start in _seen or depth < 0:
            return []
        _seen.add(start)
        t = self.targets()
        k = bisect.bisect_right(t, start)
        end = min(t[k] if k < len(t) else start + 0x1000, start + 0x6000)
        ins = list(self.md.disasm(self.d[start - BASE:end - BASE], start))
        reg = {'ecx': '나', 'esi': '나'}       # 핸들러 진입: ecx = 유닛(나)
        stack = []                              # push 기호 목록
        out = []
        last_part = None

        def rd(op, i):
            if op.type == X86_OP_IMM:
                return op.imm & 0xffffffff
            if op.type == X86_OP_REG:
                return reg.get(i.reg_name(op.reg), i.reg_name(op.reg))
            if op.type == X86_OP_MEM:
                m = op.mem
                b = reg.get(i.reg_name(m.base), i.reg_name(m.base)) if m.base else None
                if b is None:
                    return 'ds:0x%x' % (m.disp & 0xffffffff) if (m.disp & 0xffffffff) != MAP_GLOBAL else '맵'
                f = FIELD.get(m.disp)
                return '%s.%s' % (b, f) if f else '%s+0x%x' % (b, m.disp)
            return '?'

        for i in ins:
            m, ops = i.mnemonic, i.operands
            if m == 'push':
                stack.append(rd(ops[0], i))
            elif m in ('mov', 'movzx', 'movsx', 'lea') and ops[0].type == X86_OP_REG:
                r = i.reg_name(ops[0].reg)
                base = {'al': 'eax', 'ax': 'eax', 'bl': 'ebx', 'bx': 'ebx', 'cl': 'ecx', 'cx': 'ecx',
                        'dl': 'edx', 'dx': 'edx', 'di': 'edi', 'si': 'esi', 'bp': 'ebp'}.get(r, r)
                v = rd(ops[1], i)
                if m == 'lea' and ops[1].type == X86_OP_MEM:
                    v = '&' + v
                reg[base] = v
            elif m in ('add', 'sub') and ops[0].type == X86_OP_REG and ops[1].type == X86_OP_IMM:
                r = i.reg_name(ops[0].reg)
                base = {'cx': 'ecx', 'dx': 'edx', 'ax': 'eax', 'bx': 'ebx'}.get(r, r)
                v = reg.get(base)
                if isinstance(v, str) and not v.startswith('&'):
                    reg[base] = '%s%s%d' % (v, '+' if m == 'add' else '-', ops[1].imm)
            elif m in ('xor',) and len(ops) == 2 and ops[0].type == X86_OP_REG and ops[1].type == X86_OP_REG \
                    and ops[0].reg == ops[1].reg:
                reg[i.reg_name(ops[0].reg)] = 0
            elif m == 'cmp' and ops[0].type == X86_OP_REG:
                v = reg.get({'al': 'eax'}.get(i.reg_name(ops[0].reg), i.reg_name(ops[0].reg)))
                if v == '나.단계' and ops[1].type == X86_OP_IMM:
                    out.append((i.address, 'stage', ops[1].imm))
            elif m == 'test' and len(ops) == 2 and ops[0].type == X86_OP_REG and ops[0].reg == ops[1].reg:
                if reg.get({'al': 'eax'}.get(i.reg_name(ops[0].reg), i.reg_name(ops[0].reg))) == '나.단계':
                    out.append((i.address, 'stage', 0))
            elif m == 'jmp' and ops[0].type == X86_OP_IMM and start <= ops[0].imm < end and stack:
                # push 인자; jmp 공용 호출자리  꼴 — 뛰어간 자리에 바로 SetAction 이 있으면 여기서 낸다
                nxt = list(self.md.disasm(self.d[ops[0].imm - BASE:ops[0].imm - BASE + 40], ops[0].imm))
                for x in nxt[:5]:
                    if x.mnemonic == 'push':
                        break
                    if x.mnemonic == 'call' and x.operands[0].type == X86_OP_IMM \
                            and x.operands[0].imm in (SETACT, SETMOT):
                        a = stack[::-1]
                        if x.operands[0].imm == SETACT and len(a) >= 3:
                            out.append((i.address, 'act', a[0], a[1], a[2], reg.get('ecx')))
                        stack = []
                        break
            elif m == 'call' and ops[0].type == X86_OP_IMM:
                tgt = ops[0].imm
                this = reg.get('ecx')
                # 인자는 거꾸로 push 되니 stack[-1] 이 첫 인자다.
                if tgt == SETACT and len(stack) >= 3:
                    a = stack[::-1]
                    out.append((i.address, 'act', a[0], a[1], a[2], this))
                elif tgt == SETMOT and stack:
                    out.append((i.address, 'mot', stack[-1], this))
                elif tgt == EFFECT and len(stack) >= 8:
                    a = stack[::-1][:8]
                    out.append((i.address, 'eff') + tuple(a))
                elif tgt == PARTICLE and len(stack) >= 6:
                    last_part = i.address
                    out.append((i.address, 'part', stack[::-1][:6]))
                elif tgt == PARTSET and len(stack) >= 16:
                    a = stack[::-1]
                    out.append((i.address, 'partset', a[0], a[1], last_part))
                elif tgt == DELAY and stack:
                    out.append((i.address, 'delay', stack[-1]))
                elif tgt == LIFE and stack:
                    out.append((i.address, 'life', stack[-1]))
                elif tgt == WORKEND:
                    out.append((i.address, 'end'))
                elif depth > 0 and 0x10076000 <= tgt < 0x100c1f00:
                    out += [(i.address,) + r[1:] for r in self.script(tgt, depth - 1, _seen)]
                stack = []
                reg.pop('eax', None)
        return out


# ---------------- 보이기 ----------------

def fmt_eff(rec):
    _, _, obs, mot, x, y, z, mp, owner, work = rec
    if isinstance(obs, str) and obs.startswith('나.애니'):
        return '내 지금 그림 그대로 (잔상) @나'
    where = sname(x)
    if isinstance(x, str):
        if '대상' in x:
            where = '대상'
        elif x.startswith('나.'):
            where = '나'
    zoff = ''
    if isinstance(z, str) and '+' in z:
        zoff = ' z+%s' % z.split('+')[-1]
    return 'Obs %s 모션 %s @%s%s' % (sname(obs), sname(mot), where, zoff)


def run(game, chrs, works, direction=1, with_sound=True, table=False):
    g = Game(game)
    dll = Analyzer(os.path.join(game, 'G3PartII.dll'))
    wk = load_works(g)
    abis = load_abis(g, wk)
    text = {}
    try:
        import extract_character as e
        for r in e.parse_txr(g.read('TXR', 'Txr.dat')):
            text.setdefault(r['txr_id'], r['text'])
    except Exception:
        pass

    def snd_of(obs, mid):
        if not with_sound:
            return []
        try:
            return sorted({n for _, n, _, _ in obs_sounds.sounds(g, obs, mid)})
        except Exception:
            return []

    def brief(wid, motions, sprite):
        """한 줄 요약: 동작 순서 | 이펙트 | 소리."""
        w = wk.get(wid)
        acts, effs, snds = [], [], set()
        seq = ([PRELUDE[w[0x3f]][0]] if w and w[0x3f] in PRELUDE else []) + [dll.handler(wid)]
        for h in seq:
            for rec in dll.script(h):
                if rec[1] == 'act' and isinstance(rec[2], int):
                    mid = resolve_motion(motions, rec[2], direction)
                    ln = motions[mid]['len'] if motions and mid in motions else -1
                    acts.append('%d(%d틱)' % (rec[2] // 3, ln))
                    snds |= set(snd_of(sprite, mid))
                elif rec[1] == 'eff':
                    o, mo = rec[2], rec[3]
                    if isinstance(o, int):
                        tag = '%d:%s' % (o, sname(mo))
                        if tag not in effs:
                            effs.append(tag)
                        if isinstance(mo, int):
                            snds |= set(snd_of(o, mo))
                    elif isinstance(o, str) and o.startswith('나.애니'):
                        if '잔상' not in effs:
                            effs.append('잔상')
        return ' → '.join(acts) or '-', ' '.join(effs) or '-', sorted(snds)

    def show(wid, motions, sprite, indent='    '):
        w = wk.get(wid)
        print('%swork %d%s' % (indent, wid, '' if not w else '  (파일 %s, 어빌 %d 레벨 %d, +0x13 대상종류 %d, +0x3f 준비 %d)'
                                             % (w['file'], w[0x4], w[0x6], w[0x13], w[0x3f])))
        seq = []
        if w and w[0x3f] in PRELUDE:
            seq.append(('준비', PRELUDE[w[0x3f]][0]))
        seq.append(('핸들러', dll.handler(wid)))
        for label, h in seq:
            print('%s  %s 0x%x' % (indent, label, h))
            stage = None
            for rec in dll.script(h):
                kind = rec[1]
                if kind == 'stage':
                    stage = rec[2]
                    print('%s    · 단계 %d' % (indent, stage))
                elif kind == 'act':
                    base = rec[2]
                    if isinstance(base, int):
                        mid = resolve_motion(motions, base, direction)
                        ln = motions[mid]['len'] if motions and mid in motions else -1
                        s = snd_of(sprite, mid) if sprite else []
                        print('%s      동작 %-2s (기준 %s → 모션 %d, %d틱, 반복 %s)%s'
                              % (indent, base // 3, base, mid, ln, sname(rec[4]),
                                 '  소리 %s' % s if s else ''))
                    else:
                        print('%s      동작 (계산값 %s, 반복 %s)' % (indent, sname(base), sname(rec[4])))
                elif kind == 'mot':
                    mid = rec[2]
                    ln = motions[mid]['len'] if motions and isinstance(mid, int) and mid in motions else -1
                    print('%s      모션 %s 그대로 (%d틱)' % (indent, sname(mid), ln))
                elif kind == 'eff':
                    obs, mot = rec[2], rec[3]
                    extra = ''
                    if isinstance(obs, int) and isinstance(mot, int):
                        mm = load_obs_motions(g, obs)
                        if mm and mot in mm:
                            extra = ' %d틱' % mm[mot]['len']
                        s = snd_of(obs, mot)
                        if s:
                            extra += '  소리 %s' % s
                    print('%s      이펙트 %s%s' % (indent, fmt_eff(rec), extra))
                elif kind == 'partset':
                    print('%s      입자 이펙트 Obs %s 모션 %s' % (indent, sname(rec[2]), sname(rec[3])))
                elif kind == 'delay':
                    print('%s        (지연 %s틱)' % (indent, sname(rec[2])))
                elif kind == 'life':
                    print('%s        (수명/지연2 %s틱)' % (indent, sname(rec[2])))
                elif kind == 'end':
                    print('%s      → work 끝' % indent)

    if table:
        print('| 인물 (Chr) | 어빌리티 | work | 동작 순서(동작번호(틱)) | 이펙트 Obs:모션 | 소리 |')
        print('|---|---|---|---|---|---|')
    for wid in works:
        show(wid, None, None, '')
    for n in chrs:
        c = load_chr(g, n)
        if not c:
            print('Chr %04d 없음' % n)
            continue
        motions = load_obs_motions(g, c['sprite'])
        who = '%s (%d, sprite %d)' % (text.get(c['name'], c['name']), n, c['sprite'])
        items = [('기본공격', c['basic'])]
        for aid, lv in c['abis']:
            ab = abis.get(aid)
            items.append(('%s Lv%d' % (text.get(ab['name'], '?') if ab else '?%d' % aid, lv),
                          ab['levels'].get(lv) if ab else None))
        if table:
            for label, wid in items:
                if wid is None:
                    print('| %s | %s | — | (레벨→work 없음) | | |' % (who, label))
                    continue
                a, e, s = brief(wid, motions, c['sprite'])
                print('| %s | %s | %d | %s | %s | %s |'
                      % (who, label, wid, a, e, ', '.join(str(x) for x in s) or '-'))
            continue
        print('== Chr %04d %s  sprite %d' % (n, text.get(c['name'], c['name']), c['sprite']))
        for label, wid in items:
            print('  %s:' % label)
            if wid is None:
                print('    (abi 레벨→work 없음)')
            else:
                show(wid, motions, c['sprite'])


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('--chr', nargs='*', type=int, default=[])
    ap.add_argument('--work', nargs='*', type=int, default=[])
    ap.add_argument('--dir', type=int, default=1)
    ap.add_argument('--no-sound', action='store_true')
    ap.add_argument('--table', action='store_true', help='인물×어빌리티 한 줄 요약 표(마크다운)')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    run(a.game, a.chr, a.work, a.dir, not a.no_sound, a.table)


if __name__ == '__main__':
    main()

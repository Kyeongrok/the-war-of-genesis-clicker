"""work 핸들러를 (단계, 틱, 방향)을 구체값으로 두고 흐름 따라 훑는 기호 실행기 (ba-20 X).

    python tools/re/work_fx_emu.py "<게임 폴더>" [work 번호 ...]      # 단계 순 대본을 사람이 읽게 찍는다
    (표를 고칠 때는 work_fx_patch.py 가 이 모듈의 analyze_all() 을 부른다)

`work_script.Analyzer.script` 는 핸들러를 코드 주소 순으로 한 줄로 읽는다. 그래서
  · 단계 0 이 함수 끝에 있는 핸들러는 차례가 뒤집히고(더블 브레이크 0x100825e0),
  · 레벨·방향 가지가 한 줄로 합쳐지고(연, 카운터 미사일),
  · 지연(0x100c24d0)·수명(0x100c2530)이 「바로 앞 이펙트」에 붙는다(힐 297:2 가 뿌리개 478:2 의 수명 96 을 받음).
여기서는 유닛 칸 +0x94(단계)·+0x96(틱)·+0x5c(방향)·+0x80(work)을 정해 놓고 분기를 따라간다.
  · work 표([0x101b6868] + id*0x44 + 칸) 읽기는 실제 .att 값으로 푼다 → 레벨 가지가 work 마다 갈린다.
  · 스택은 esp 치우침으로 따라가 지역 변수에 둔 자리까지 풀고, 호출 뒤 ret N 만큼 걷는다.
  · 모르는 분기는 양쪽 다 훑고(이미 본 자리는 다시 안 감), 값이 정해진 고리는 한 바퀴만 돈 뒤 빠져나간다.
  · 단계 전이는 0x100e82e0(단계+1, 틱 0)과 [+0x94] 쓰기로 따라간다. 못 봤으면 다음 번호에 새 호출 자리가 있을 때만 잇는다.
  · 틱 비교 상수(cmp [+0x96], N)를 모아 그 앞뒤 값으로 다시 돌려, 한 단계 안의 차례를 틱 순으로 세운다.
  · 이펙트 생성자가 돌려준 객체('fx#n')를 따라가 지연·수명·뿌리개 설정이 어느 이펙트의 것인지 가린다.
  · 사슬(0x100c2640·0x100c2680·0x100c2600)은 그 이펙트 기록의 'chain' = [끝/시작, 앞 것의 주소, 앞 것의 길, 자리복사, 지연],
    수명×n(0x100c2570)은 'loops', 카메라 따라가기/놓기(0x100eac00·0x100eac60)는 사건 'cam'·'camoff' 로 남긴다(ba-21 F4·F9·F7).
한계: 이펙트 객체의 콜백은 안 따라간다. 고리는 한 바퀴뿐이라 고리마다 달라지는 지연은 첫 값만 나온다.
근거·결과: 옵시디안 분석/원본차이/ba20-fxtable.md
"""
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import work_script as ws                                    # noqa: E402
import work_fx_extra as wx                                  # noqa: E402
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_OP_REG  # noqa: E402

BASE = ws.BASE
M32 = 0xffffffff
SETACT, SETMOT, SETMOT2 = 0x10072820, 0x100e53c0, 0x100e53e0
ANIM_PLAY, ANIM_PLAY2 = 0x10027090, 0x10027250
NEXTSTAGE = 0x100e82e0
WORKEND = 0x100762d0
DELAY, LIFE = ws.DELAY, ws.LIFE
MAP_GLOBAL, WT_GLOBAL = 0x101be2dc, 0x101b6868
HITSLOTS = {0x100c2950, 0x100c29b0, 0x100c28f0, 0x100c2a10, 0x100c2860}
MSG = 0x10001f40
NEW = 0x10134570
# 사슬(ba-21 F4) — (앞 이펙트, 자리복사, 지연): 앞 것이 끝나면('end') / 시작하면('start') 제 지연이 돈다
CHAIN = {0x100c2640: 'end', 0x100c2680: 'end', 0x100c2600: 'start'}
LIFELOOPS = 0x100c2570     # (n) 수명 = 모션 길이 × n (ba-21 F9)
CAMFOLLOW, CAMFREE = 0x100eac00, 0x100eac60    # 카메라가 this 를 따라간다(속도, 우선) / 놓는다 (ba-21 F7)

R32 = ['eax', 'ecx', 'edx', 'ebx', 'esp', 'ebp', 'esi', 'edi']
SUB = {}
for r in ('a', 'c', 'd', 'b'):
    SUB[r + 'l'] = ('e' + r + 'x', 0, 8)
    SUB[r + 'h'] = ('e' + r + 'x', 8, 8)
    SUB[r + 'x'] = ('e' + r + 'x', 0, 16)
for r in ('si', 'di', 'bp', 'sp'):
    SUB[r] = ('e' + r, 0, 16)
for r in R32:
    SUB[r] = (r, 0, 32)


class W(int):
    """work 번호(나.work) — 정수지만 이펙트 인자 끝을 알아보게 꼬리표를 단다."""


def signed(v, bits):
    v &= (1 << bits) - 1
    return v - (1 << bits) if v >> (bits - 1) else v


class State:
    __slots__ = ('reg', 'mem', 'fld', 'flags', 'va', 'npush')

    def copy(self):
        s = State()
        s.reg = dict(self.reg)
        s.mem = dict(self.mem)
        s.fld = dict(self.fld)
        s.flags = self.flags
        s.va = self.va
        s.npush = self.npush
        return s


class Emu:
    def __init__(self, game):
        self.dll = wx.ExtraAnalyzer(os.path.join(game, 'G3PartII.dll'))
        self.d = self.dll.d
        self.md = self.dll.md
        self.cache = {}
        self.g = ws.Game(game)
        self.works = ws.load_works(self.g)

    def insn(self, va):
        i = self.cache.get(va)
        if i is None:
            try:
                i = next(self.md.disasm(self.d[va - BASE:va - BASE + 16], va))
            except StopIteration:
                i = False
            self.cache[va] = i
        return i

    def u32(self, va):
        return struct.unpack_from('<I', self.d, va - BASE)[0]

    # ------------------------------------------------------------------
    def run(self, start, wid, stage, tick, direction=None, selfsym='나', depth=2, args=None, ctx=None, path=()):
        """한 번 훑기 — 기록 목록을 돌려준다. ctx = 공유 문맥(dict: events, seen_calls, fxs, tickconsts, wread)."""
        top = ctx is None
        if top:
            ctx = {'events': [], 'calls': set(), 'fxs': [], 'tickc': set(), 'wread': {}, 'steps': 0,
                   'wid': wid, 'stage': stage, 'tick': tick, 'dir': direction}
        st = State()
        st.reg = {r: None for r in R32}
        st.reg['ecx'] = selfsym
        st.reg['esp'] = ('sp', 0)
        st.mem = {}
        if args:
            for k, a in enumerate(args):
                st.mem[4 + 4 * k] = a
        st.fld = {}
        if selfsym == '나':
            st.fld = ctx.setdefault('fld', {0x94: stage, 0x96: tick})
        st.flags = None
        st.va = start
        st.npush = 0
        visited = set()
        work = [st]
        outer = ctx.get('vis')
        ctx['vis'] = visited
        while work:
            s = work.pop()
            prev = None
            while True:
                if ctx['steps'] > 60000:
                    break
                if s.va in visited:
                    # 고리 되돌이(뒤로 뛰어 이미 본 자리) — 고리 밖으로 나가는 첫 앞쪽 분기를 찾아 빠져나간다
                    if prev is None or s.va >= prev:
                        break
                    va, out = s.va, None
                    for _ in range(400):
                        j = self.insn(va)
                        if not j or j.mnemonic.startswith('ret'):
                            break
                        if j.mnemonic.startswith('j') and j.operands[0].type == X86_OP_IMM:
                            t = j.operands[0].imm
                            if t > prev:
                                out = t
                                break
                            if j.mnemonic == 'jmp':
                                if t in visited and t <= va:
                                    break
                                va = t
                                continue
                        elif j.mnemonic == 'jmp':
                            break
                        va = j.address + j.size
                        if va > prev:
                            out = va if va not in visited else None
                            break
                    if out is None or out in visited:
                        break
                    s.va = out
                    s.flags = None
                    prev = None
                    continue
                visited.add(s.va)
                ctx['steps'] += 1
                i = self.insn(s.va)
                if not i:
                    break
                nxt = self.step(s, i, work, ctx, depth, path)
                ctx['vis'] = visited
                if nxt is None:
                    break
                prev = s.va
                s.va = nxt
        ctx['vis'] = outer
        return ctx

    # ------------------------------------------------------------------
    def getr(self, s, name):
        full, sh, bits = SUB[name]
        v = s.reg.get(full)
        if bits == 32 or v is None:
            return v
        if isinstance(v, int):
            if sh == 0 and v < (1 << bits):
                return v                      # 꼬리표(W) 보존
            return (v >> sh) & ((1 << bits) - 1)
        if sh:
            return None
        return v                              # 기호 — 아랫부분도 같은 이름으로 본다

    def setr(self, s, name, v):
        full, sh, bits = SUB[name]
        if bits == 32:
            s.reg[full] = v
            return
        old = s.reg.get(full)
        if isinstance(v, int) and isinstance(old, int) and not isinstance(v, W):
            mask = ((1 << bits) - 1) << sh
            s.reg[full] = (old & ~mask & M32) | ((v << sh) & mask)
        elif sh:
            s.reg[full] = None
        else:
            s.reg[full] = v

    def addr(self, s, i, op):
        """메모리 피연산자의 주소값 — ('sp', n) · ('wt', n) · int · 기호 문자열 · None."""
        m = op.mem
        disp = signed(m.disp, 32)
        b = s.reg.get(SUB[i.reg_name(m.base)][0]) if m.base else 0
        x = s.reg.get(SUB[i.reg_name(m.index)][0]) if m.index else 0
        if m.index:
            if isinstance(x, int):
                x = signed(x, 32) * m.scale
            else:
                x = None
        if isinstance(b, tuple):
            if x is None:
                return (b[0] + '?', b[1] + disp)
            return (b[0], b[1] + x + disp)
        if isinstance(x, tuple) and m.scale == 1 and isinstance(b, int):
            return (x[0], x[1] + b + disp)
        if isinstance(b, int) and not isinstance(b, bool):
            if x is None:
                return None
            return (b + x + disp) & M32
        if isinstance(b, str):
            if x is None or x != 0:
                return b + '+?'
            return (b, disp)                  # (기호, 치우침)
        return None

    def load(self, s, a, ctx, bits=32):
        if a is None:
            return None
        if isinstance(a, int):
            if a == MAP_GLOBAL:
                return '맵'
            if a == WT_GLOBAL:
                return ('wt', 0)
            return 'ds:0x%x' % a
        if isinstance(a, str):
            return None
        tag, off = a
        if tag == 'sp':
            return s.mem.get(off)
        if tag == 'wt':
            wid, f = divmod(off, 0x44)
            w = self.works.get(wid)
            if w is not None and f in w and isinstance(w[f], int):
                ctx['wread'][f] = w[f]
                return w[f]
            return None
        if tag.endswith('?'):
            return None
        if isinstance(tag, tuple):
            return None
        if tag == '나':
            if off in s.fld and s.fld[off] is not None:
                return s.fld[off]
            if off == 0x94:
                return '나.단계'
            if off == 0x96:
                return '나.틱'
            if off == 0x80:
                return W(ctx['wid'])
            if off == 0x5c:
                return ctx['dir'] if ctx['dir'] is not None else '나.방향'
        f = ws.FIELD.get(off)
        if tag in ('나', '나.대상') and f:
            return '%s.%s' % (tag, f)
        if isinstance(tag, str) and tag.startswith('fx#'):
            return None
        return '%s+0x%x' % (tag, off & M32) if isinstance(tag, str) else None

    def store(self, s, a, v, i, ctx, path):
        if not isinstance(a, tuple):
            return
        tag, off = a
        if tag == 'sp':
            s.mem[off] = v
        elif tag == '나':
            if off == 0x94:
                s.fld[off] = v
                ctx['events'].append({'k': 'stage', 'va': i.address, 'to': v, 'path': path, 'stage': ctx['stage'], 'tick': ctx['tick']})
            elif off == 0x96:
                s.fld[off] = v
            elif off == 0x7a:
                ctx['events'].append({'k': 'rep', 'va': i.address, 'v': v, 'this': '나', 'path': path, 'stage': ctx['stage'], 'tick': ctx['tick']})
        elif tag == '나.대상' and off == 0x7a:
            ctx['events'].append({'k': 'rep', 'va': i.address, 'v': v, 'this': '나.대상', 'path': path, 'stage': ctx['stage'], 'tick': ctx['tick']})

    def rd(self, s, i, op, ctx):
        if op.type == X86_OP_IMM:
            return op.imm & M32
        if op.type == X86_OP_REG:
            n = i.reg_name(op.reg)
            return self.getr(s, n) if n in SUB else None
        if op.type == X86_OP_MEM:
            v = self.load(s, self.addr(s, i, op), ctx)
            if isinstance(v, int) and not isinstance(v, W) and op.size < 4:
                v &= (1 << (8 * op.size)) - 1
            return v
        return None

    def wr(self, s, i, op, v, ctx, path):
        if op.type == X86_OP_REG:
            n = i.reg_name(op.reg)
            if n in SUB:
                self.setr(s, n, v)
        elif op.type == X86_OP_MEM:
            self.store(s, self.addr(s, i, op), v, i, ctx, path)

    @staticmethod
    def arith(m, a, b, bits):
        mask = (1 << bits) - 1
        if isinstance(a, int) and isinstance(b, int):
            if m == 'add':
                return (a + b) & mask
            if m == 'sub':
                return (a - b) & mask
            if m == 'and':
                return a & b
            if m == 'or':
                return a | b
            if m == 'xor':
                return a ^ b
            if m == 'shl':
                return (a << (b & 31)) & mask
            if m == 'shr':
                return (a & mask) >> (b & 31)
            if m == 'sar':
                return (signed(a, bits) >> (b & 31)) & mask
            if m == 'imul':
                return (signed(a, bits) * signed(b, bits)) & mask
            return None
        if isinstance(a, tuple) and isinstance(b, int) and m in ('add', 'sub'):
            return (a[0], a[1] + (signed(b, bits) if m == 'add' else -signed(b, bits)))
        if isinstance(b, tuple) and isinstance(a, int) and m == 'add':
            return (b[0], b[1] + signed(a, bits))
        if isinstance(a, str) and isinstance(b, int) and m in ('add', 'sub'):
            n = signed(b, bits) * (1 if m == 'add' else -1)
            # 「나.z+30」 꼴 — 이미 치우침이 붙어 있으면 더한다
            base, sep, old = a.rpartition('+') if '+' in a and a.rsplit('+', 1)[1].lstrip('-').isdigit() else (a, '', '0')
            if not sep:
                base = a
                if '-' in a and a.rsplit('-', 1)[1].isdigit() and not a.endswith('-'):
                    base, _, o2 = a.rpartition('-')
                    old = '-' + o2
            n += int(old)
            return base if n == 0 else '%s%+d' % (base, n)
        if m == 'and' and isinstance(b, int) and b in (0xff, 0xffff, M32):
            return a
        return None

    # ------------------------------------------------------------------
    def step(self, s, i, work, ctx, depth, path):
        m, ops = i.mnemonic, i.operands
        nxt = i.address + i.size
        sp = s.reg['esp']
        if not isinstance(sp, tuple) or sp[0] != 'sp':
            if ctx.get('dbg'): print('esp lost at', hex(i.address), m, i.op_str)
            return None
        if m == 'push':
            v = self.rd(s, i, ops[0], ctx)
            sp = (sp[0], sp[1] - 4)
            s.reg['esp'] = sp
            s.mem[sp[1]] = v
            s.npush += 1
            return nxt
        if m == 'pop':
            v = s.mem.get(sp[1])
            s.reg['esp'] = (sp[0], sp[1] + 4)
            self.wr(s, i, ops[0], v, ctx, path)
            if s.npush:
                s.npush -= 1
            return nxt
        if m in ('mov', 'movzx', 'movsx'):
            v = self.rd(s, i, ops[1], ctx)
            if m == 'movsx' and isinstance(v, int) and not isinstance(v, W):
                v = signed(v, 8 * ops[1].size) & M32
            if m in ('movzx', 'movsx') and ops[0].type == X86_OP_REG:
                s.reg[SUB[i.reg_name(ops[0].reg)][0]] = v
            else:
                self.wr(s, i, ops[0], v, ctx, path)
            return nxt
        if m == 'lea':
            a = self.addr(s, i, ops[1])
            if isinstance(a, tuple) and isinstance(a[0], str) and a[0] not in ('sp', 'wt') and not a[0].endswith('?'):
                a = a[0] if a[1] == 0 else '%s%+d' % a if ws.FIELD.get(a[1]) is None or True else a
            elif isinstance(a, tuple) and a[0].endswith('?'):
                a = None
            elif isinstance(a, str):
                a = None
            self.wr(s, i, ops[0], a, ctx, path)
            return nxt
        if m in ('add', 'sub', 'and', 'or', 'xor', 'shl', 'shr', 'sar', 'imul') and len(ops) >= 2:
            bits = 8 * ops[0].size
            if m == 'xor' and ops[0].type == X86_OP_REG and ops[1].type == X86_OP_REG and ops[0].reg == ops[1].reg:
                res = 0
            elif m == 'imul' and len(ops) == 3:
                res = self.arith(m, self.rd(s, i, ops[1], ctx), self.rd(s, i, ops[2], ctx), bits)
            else:
                a, b = self.rd(s, i, ops[0], ctx), self.rd(s, i, ops[1], ctx)
                if m in ('add', 'sub') and ops[1].type == X86_OP_IMM and isinstance(b, int):
                    b &= (1 << bits) - 1
                res = self.arith(m, a, b, bits)
                if m == 'sub':
                    s.flags = (a, b, bits)
            if ops[0].type == X86_OP_REG and i.reg_name(ops[0].reg) == 'esp':
                if isinstance(res, tuple):
                    s.reg['esp'] = res
                    if m == 'add':
                        s.npush = 0
                return nxt
            self.wr(s, i, ops[0], res, ctx, path)
            if m != 'sub':
                s.flags = (res, 0, bits) if isinstance(res, int) else None
            return nxt
        if m in ('inc', 'dec', 'neg', 'not'):
            bits = 8 * ops[0].size
            a = self.rd(s, i, ops[0], ctx)
            res = None
            if isinstance(a, int):
                res = {'inc': a + 1, 'dec': a - 1, 'neg': -a, 'not': ~a}[m] & ((1 << bits) - 1)
            elif isinstance(a, str) and m in ('inc', 'dec'):
                res = self.arith('add' if m == 'inc' else 'sub', a, 1, bits)
            self.wr(s, i, ops[0], res, ctx, path)
            s.flags = (res, 0, bits) if isinstance(res, int) else None
            return nxt
        if m == 'cmp':
            a, b = self.rd(s, i, ops[0], ctx), self.rd(s, i, ops[1], ctx)
            bits = 8 * ops[0].size
            if isinstance(b, int) and ops[1].type == X86_OP_IMM:
                b &= (1 << bits) - 1
            if a == '나.틱' and isinstance(b, int):
                ctx['tickc'].add(b)
            if b == '나.틱' and isinstance(a, int):
                ctx['tickc'].add(a)
            s.flags = (a, b, bits)
            return nxt
        if m == 'test':
            a, b = self.rd(s, i, ops[0], ctx), self.rd(s, i, ops[1], ctx)
            bits = 8 * ops[0].size
            if isinstance(a, int) and isinstance(b, int):
                s.flags = (a & b, 0, bits)
            else:
                if a == '나.틱':
                    ctx['tickc'].add(0)
                s.flags = None
            return nxt
        if m == 'jmp':
            if ops[0].type == X86_OP_IMM:
                return ops[0].imm
            if ops[0].type == X86_OP_MEM and ops[0].mem.index and not ops[0].mem.base:
                mm = ops[0].mem
                idx = s.reg.get(SUB[i.reg_name(mm.index)][0])
                tbl = mm.disp & M32
                if isinstance(idx, int):
                    return self.u32((tbl + idx * mm.scale) & M32)
                # 모르는 값 — 표의 갈래를 모두 훑는다
                tg = []
                for k in range(64):
                    t = self.u32(tbl + 4 * k)
                    if not (i.address - 0x4000 < t < i.address + 0x6000):
                        break
                    if t not in tg:
                        tg.append(t)
                for t in reversed(tg[1:]):
                    c = s.copy()
                    c.va = t
                    work.append(c)
                return tg[0] if tg else None
            return None
        if m.startswith('j'):
            tgt = ops[0].imm
            c = None
            if s.flags is not None:
                a, b, bits = s.flags
                if isinstance(a, int) and isinstance(b, int):
                    mask = (1 << bits) - 1
                    a &= mask
                    b &= mask
                    sa, sb = signed(a, bits), signed(b, bits)
                    c = {'jg': sa > sb, 'jge': sa >= sb, 'jl': sa < sb, 'jle': sa <= sb, 'je': a == b, 'jne': a != b,
                         'ja': a > b, 'jae': a >= b, 'jb': a < b, 'jbe': a <= b,
                         'js': signed((a - b) & mask, bits) < 0, 'jns': signed((a - b) & mask, bits) >= 0}.get(m)
            if c is True:
                if tgt in ctx['vis'] and tgt < i.address:
                    return nxt                # 값이 정해진 고리 되돌이 — 한 바퀴만 돌고 나간다
                return tgt
            if c is False:
                return nxt
            cp = s.copy()
            cp.va = tgt
            work.append(cp)
            return nxt
        if m in ('ret', 'retn', 'int3', 'hlt'):
            return None
        if m == 'call':
            self.call(s, i, ctx, depth, path)
            return nxt
        if m in ('cdq', 'cwde', 'cbw'):
            if m == 'cdq':
                a = s.reg['eax']
                s.reg['edx'] = (0 if signed(a, 32) >= 0 else M32) if isinstance(a, int) else None
            return nxt
        if m in ('idiv', 'div'):
            a, b = s.reg['eax'], self.rd(s, i, ops[0], ctx)
            if isinstance(a, int) and isinstance(b, int) and b and s.reg['edx'] in (0, M32):
                sa, sb = signed(a, 32), signed(b, 32)
                q = abs(sa) // abs(sb) * (1 if (sa >= 0) == (sb >= 0) else -1)
                s.reg['eax'], s.reg['edx'] = q & M32, (sa - q * sb) & M32
            else:
                s.reg['eax'] = s.reg['edx'] = None
            return nxt
        if m.startswith('set') or m.startswith('cmov') or m in ('xchg', 'sbb', 'adc', 'mul', 'imul', 'rol', 'ror',
                                                                'shld', 'shrd', 'bt'):
            if ops and ops[0].type == X86_OP_REG and i.reg_name(ops[0].reg) in SUB:
                s.reg[SUB[i.reg_name(ops[0].reg)][0]] = None
            if m in ('mul', 'imul'):
                s.reg['eax'] = s.reg['edx'] = None
            return nxt
        if m.startswith('f') or m in ('nop', 'leave', 'wait', 'cld', 'std', 'rep stosd', 'rep movsd', 'sahf'):
            if m == 'fnstsw':
                s.reg['eax'] = None
            return nxt
        # 모르는 명령 — 결과 레지스터를 모름으로
        if ops and ops[0].type == X86_OP_REG and i.reg_name(ops[0].reg) in SUB:
            s.reg[SUB[i.reg_name(ops[0].reg)][0]] = None
        return nxt

    # ------------------------------------------------------------------
    def call(self, s, i, ctx, depth, path):
        ops = i.operands
        sp = s.reg['esp']
        a = [s.mem.get(sp[1] + 4 * k) for k in range(14)]
        this = s.reg['ecx']
        ev = ctx['events']
        cur = {'va': i.address, 'path': path, 'stage': ctx['stage'], 'tick': ctx['tick']}
        tgt = ops[0].imm if ops[0].type == X86_OP_IMM else None
        ret = None
        if tgt is None:
            s.reg['esp'] = (sp[0], sp[1] + 4 * s.npush)
            s.npush = 0
            s.reg['eax'] = None
            s.reg['ecx'] = s.reg['edx'] = None
            return
        # 덧기록(흐름은 안 바꾼다) — 사슬·수명×n 은 그 이펙트 기록에, 카메라 따라가기는 사건으로 (work_fx_timing.py 가 읽는다)
        tfx = ctx['fxs'][int(this[3:])] if isinstance(this, str) and this.startswith('fx#') else None
        if tgt in CHAIN and tfx is not None and 'chain' not in tfx:
            prev = ctx['fxs'][int(a[0][3:])] if isinstance(a[0], str) and a[0].startswith('fx#') else None
            tfx['chain'] = [CHAIN[tgt], prev['va'] if prev else None, str(prev['path']) if prev else None, a[1], a[2]]
        elif tgt == LIFELOOPS and tfx is not None and 'loops' not in tfx:
            tfx['loops'] = a[0] if isinstance(a[0], int) else '?'
        elif tgt in (CAMFOLLOW, CAMFREE):
            who = 'fx' if tfx is not None else this if this in ('나', '나.대상') else None
            ev.append(dict(cur, k='cam' if tgt == CAMFOLLOW else 'camoff', who=who, speed=a[0], prio=a[1],
                           fx=[tfx['va'], tfx.get('obs'), tfx.get('motion'), tfx['ctor'], tfx['kind']] if tfx is not None else None))
        if tgt == SETACT:
            ev.append(dict(cur, k='act', base=a[0], dir=a[1], rep=a[2], this=this))
        elif tgt in (SETMOT, SETMOT2):
            ev.append(dict(cur, k='mot', m=a[0], this=this, alt=tgt == SETMOT2))
        elif tgt in (ANIM_PLAY, ANIM_PLAY2):
            ev.append(dict(cur, k='mot', m=a[0], this=this, alt=tgt == ANIM_PLAY2, anim=True))
        elif tgt == NEXTSTAGE:
            if this == '나':
                v = s.fld.get(0x94)
                s.fld[0x94] = v + 1 if isinstance(v, int) else None
                s.fld[0x96] = 0
                ev.append(dict(cur, k='stage', to=s.fld[0x94]))
        elif tgt == WORKEND:
            ev.append(dict(cur, k='end', this=this))
        elif tgt in HITSLOTS:
            ev.append(dict(cur, k='slot', f=tgt, this=this, a=a[:4]))
        elif tgt == MSG and a[1] == 0x3e9:
            ev.append(dict(cur, k='hit', to=a[0]))
        elif tgt in (DELAY, LIFE):
            key = 'delay' if tgt == DELAY else 'life'
            fx = None
            if isinstance(this, str) and this.startswith('fx#'):
                fx = ctx['fxs'][int(this[3:])]
            elif ctx['fxs'] and this is None:
                fx = ctx['fxs'][-1]
            if fx is not None and fx.get(key) is None:
                fx[key] = a[0] if isinstance(a[0], int) else '?'      # 값을 못 푼 호출은 '?' (0 과 가린다)
        elif tgt == wx.MOV_OPEN:
            if isinstance(this, str) and this.startswith('fx#'):
                ctx['fxs'][int(this[3:])]['mov'] = self.dll.cstr(a[0]) if isinstance(a[0], int) else None
        elif tgt == wx.MOV_PARAM:
            if isinstance(this, str) and this.startswith('fx#'):
                ctx['fxs'][int(this[3:])]['movparam'] = a[:6]
        elif tgt == NEW:
            pass
        else:
            hit = None
            for j in range(3, 10):
                if a[j + 1] in ('나', '나.대상') and isinstance(a[j + 2], W):
                    hit = j
                    break
            if hit is not None and this is None:
                j = hit
                rec = dict(cur, k='fx', ctor=tgt, x=a[j - 3], y=a[j - 2], z=a[j - 1], parent=a[j], owner=a[j + 1],
                           obs=None, motion=None, kind='code', delay=None, life=None, setter=None, mov=None,
                           movparam=None, idx=len(ctx['fxs']))
                if j >= 5 and tgt != wx.MOV_CTOR:
                    rec['obs'], rec['motion'] = a[j - 5], a[j - 4]
                    o = rec['obs']
                    rec['kind'] = 'obs' if isinstance(o, int) else \
                        ('body:target' if isinstance(o, str) and '대상' in o else
                         'body:self' if isinstance(o, str) and o.startswith('나') else 'obs?')
                elif tgt == wx.MOV_CTOR:
                    rec['kind'] = 'mov'
                ctx['fxs'].append(rec)
                ev.append(rec)
                ret = 'fx#%d' % rec['idx']
            elif isinstance(this, str) and this.startswith('fx#'):
                fx = ctx['fxs'][int(this[3:])]
                if tgt in wx.EMITTERS and fx['kind'] == 'code':
                    oi, mi = wx.EMITTERS[tgt]
                    fx['kind'] = 'emit'
                    fx['obs'] = a[oi]
                    fx['motion'] = a[mi] if mi is not None else None
                    fx['count'] = a[2]
                    fx['setter'] = (tgt, a[:6])
                elif fx['setter'] is None:
                    fx['setter'] = (tgt, a[:6])
                else:
                    fx.setdefault('more', []).append((tgt, a[:6]))
            elif depth > 0 and this in ('나', '나.대상') and 0x10076000 <= tgt < 0x100c1f00 \
                    and (tgt, this) not in ctx['calls']:
                ctx['calls'].add((tgt, this))
                n = self.dll.argc(tgt) or 0
                self.run(tgt, ctx['wid'], ctx['stage'], ctx['tick'], ctx['dir'], this, depth - 1, a[:max(n, 4)], ctx,
                         path + (tgt,))
        n = self.dll.argc(tgt)
        if n:
            s.reg['esp'] = (sp[0], sp[1] + 4 * n)
        s.npush = 0
        s.reg['eax'] = ret
        s.reg['ecx'] = s.reg['edx'] = None


# ---------------------------------------------------------------- 단계 따라 걷기
MAXSTAGE = 60


def _ekey(ev):
    return (ev['va'], ev['path'])


def run_stage(e, h, wid, st, d):
    """한 단계 — (사건[첫 틱 순], 다음 단계 목록, 끝났나, 읽은 work 칸)."""
    c = e.run(h, wid, st, None, d)
    tc = sorted(c['tickc'])
    wread = dict(c['wread'])
    evs = c['events']
    first = {}
    if tc:
        ts = sorted({0} | {t for x in tc for t in (x - 1, x, x + 1) if 0 <= t < 5000})
        allev, order = {}, []
        for t in ts:
            cc = e.run(h, wid, st, t, d)
            wread.update(cc['wread'])
            for n, ev in enumerate(cc['events']):
                k = _ekey(ev)
                if k not in allev:
                    allev[k] = ev
                    first[k] = t
                    order.append((t, n, k))
        seen = set(allev)
        out = [allev[k] for _, _, k in sorted(order)]
        for ev in evs:                       # 틱을 모른 채로만 닿은 것
            if _ekey(ev) not in seen:
                ev = dict(ev)
                ev['tick'] = None
                out.append(ev)
        evs = out
    for ev in evs:
        ev['stage'] = st
        if ev['tick'] is None and _ekey(ev) in first:
            ev['tick'] = first[_ekey(ev)]
    nxt = []
    for ev in evs:
        if ev['k'] == 'stage' and ev['to'] not in nxt:
            nxt.append(ev['to'])
    return evs, nxt, any(ev['k'] == 'end' for ev in evs), wread


def walk(e, h, wid, d, prelude=False):
    """단계 0 부터 전이를 따라 — (사건, 단계 차례, 표시, 읽은 work 칸)."""
    seq, seen, order, flags = [], set(), [], []
    stack = [0]
    wread = {}
    while stack:
        st = stack.pop()
        if st in seen or not isinstance(st, int) or st > MAXSTAGE:
            continue
        seen.add(st)
        evs, nxt, end, wr = run_stage(e, h, wid, st, d)
        wread.update(wr)
        order.append(st)
        seq += evs
        nx = [n for n in nxt if n is not None and not (prelude and n == 0)]
        if None in nxt:
            flags.append('단계%d→모름' % st)
            nx.append(st + 1)
        if len([n for n in nx if n not in seen and n != st]) > 1:
            flags.append('단계%d 갈래%s' % (st, nx))
        if not nx and not (prelude and 0 in nxt):
            ev2, n2, _, _ = run_stage(e, h, wid, st + 1, d)
            have = {x['va'] for x in seq if x['k'] in ('act', 'mot', 'fx', 'end')}
            more = any(x['k'] in ('act', 'mot', 'fx', 'end') and x['va'] not in have for x in ev2)
            if more or any(isinstance(n, int) and n > st + 1 for n in n2):
                flags.append('단계%d 전이 못 봄' % st)
                nx = [st + 1]
        for n in reversed(nx):
            stack.append(n)
    return seq, order, flags, wread


def _clean(v):
    if isinstance(v, W):
        return int(v)
    if isinstance(v, tuple):
        return str(v)
    if isinstance(v, list):
        return [_clean(x) for x in v]
    return v


def analyze_all(game, ids=None):
    """({work: {'handler', 'prep', 'pre'?, 'main': {방향: {'events', 'order', 'flags'}}}}, Emu) — 방향 키는 1·0·2·3."""
    e = Emu(game)
    cache = {}

    def one(h, wid, prelude):
        w = e.works[wid]
        for wr, res in cache.get((h, prelude), []):
            if all(w.get(f) == v for f, v in wr.items()):
                return res
        res, wrall = {}, {}
        for d in (1, 0, 2, 3):
            seq, order, flags, wr = walk(e, h, wid, d, prelude)
            wrall.update(wr)
            res[d] = {'events': [{k: _clean(v) for k, v in x.items() if k != 'idx'} for x in seq],
                      'order': order, 'flags': flags}
        cache.setdefault((h, prelude), []).append((wrall, res))
        return res

    out = {}
    for wid in (ids or sorted(e.works)):
        w = e.works.get(wid)
        if w is None:
            continue
        try:
            h = e.dll.handler(wid)
        except Exception:
            continue
        r = {'handler': h, 'prep': w[0x3f]}
        if w[0x3f] in ws.PRELUDE:
            r['preh'] = ws.PRELUDE[w[0x3f]][0]
            r['pre'] = one(r['preh'], wid, True)
        r['main'] = one(h, wid, False)
        out[wid] = r
    return out, e


def main():
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    ids = [int(x) for x in sys.argv[2:]] or None
    res, _ = analyze_all(sys.argv[1], ids)
    for wid, x in res.items():
        for part in ('pre', 'main'):
            if part not in x:
                continue
            d = x[part][1]
            print('work %d %s 0x%x 단계 %s %s' % (wid, '준비' if part == 'pre' else '핸들러',
                                                 x['preh'] if part == 'pre' else x['handler'], d['order'], d['flags']))
            for ev in d['events']:
                tag = '   단계 %s 틱 %s' % (ev['stage'], ev['tick'])
                if ev['k'] == 'act':
                    print(tag, '동작', ev['base'] // 3 if isinstance(ev['base'], int) else ev['base'],
                          '반복', ev['rep'], ev['this'], hex(ev['va']))
                elif ev['k'] == 'mot':
                    print(tag, '모션', ev['m'], ev['this'], hex(ev['va']))
                elif ev['k'] == 'fx':
                    print(tag, '이펙트', ev['kind'], ev['obs'], ev['motion'],
                          'x=%s 붙일곳=%s 지연=%s 수명=%s' % (ev['x'], ev['parent'], ev['delay'], ev['life']), hex(ev['va']))
                elif ev['k'] in ('stage', 'end', 'hit', 'rep'):
                    print(tag, ev['k'], ev.get('to', ev.get('v', '')), hex(ev['va']))


if __name__ == '__main__':
    main()

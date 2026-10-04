"""이펙트 표(`duel-dx/Ability/AbilityScripts.g.cs`)의 줄마다 붙는 <b>덧정보</b> 표 `duel-dx/Ability/WorkFxExtra.g.cs` 를 만든다 (ba-21 F1·F2·F6·F10·F11).

    python tools/re/work_fx_extra2.py "<게임 폴더>" [--dry] [--report 줄.tsv] [--cs …/AbilityScripts.g.cs] [--out …/WorkFxExtra.g.cs]

차례: ① `work_fx_table.py` → ② `work_fx_patch.py` → ③ `work_fx_timing.py` → ④ 이 도구. 표(①~③)를 다시 뽑았으면 이것도 다시 돌린다
(줄을 (work, Obs, 모션, Delay, Facing) 으로 찾으므로 Delay 가 바뀌면 열쇠가 어긋난다). 이 도구는 표를 <b>읽기만</b> 한다.

`work_fx_emu.Emu` 를 이 도구 안에서만 덧씌워(파일은 안 고친다) 이펙트 객체에 건 <b>모든 호출</b>(주소 · 인자 10개)을 그 이펙트 기록의
'calls' 에 남기고, 스택의 낱말 둘(x, y 를 16비트씩 적은 자리 묶음)을 32비트로 읽을 때 (x, y) 짝으로 돌려준다 — 직선탄 도착 자리가
`sub esp, 8; mov [esp], <자리 묶음>; mov word [esp+4], z` 로 넘어가기 때문이다. 줄 맞추기는 `work_fx_timing` 의 셈(단계 시작 틱 + 사슬 + 제 지연)을 그대로 쓴다.

칸
  Mirror   `0x100e56c0(방향)`·`0x100e56f0`(자식까지) — 이펙트 `+0x5c = 방향`, <b>방향 == 3 이면</b> 애니메이터 깃발 `+0x16 |= 2`(뒤집기),
           아니면 `&= ~2` (0x100e56c0~0x100e56e5). 인자가 3 인 호출이 그 줄의 방향에서 걸리면 뒤집는다. 왼쪽(1)을 볼 때도 3 을 주면 2(늘),
           오른쪽(3)에서만 3 이면 1.
  Dx · Dy  생성자 자리 인자가 `나.x±N`·`나.대상.y±N` 꼴일 때 그 N. Dx 는 월드 x 그대로, Dy 는 월드 y × 0.8(칸 40:32). 표 줄의 자리(OnTarget)와
           기준이 다르면(표는 대상인데 원본은 시전자 따위) 안 적는다.
  PerTarget 층 0 유닛 모으기 `0x100df5c0` 뒤, 생성 호출이 <b>되돌이 고리 안</b>에 있고 자리가 시전자·겨눈 대상 기준이 아닌 것.
           Stagger = 그 이펙트의 지연(`0x100c24d0`) 인자로 들어가는 레지스터가 고리 안에서 `add reg, N` / `lea reg, [i*N]` 으로 느는 그 N.
           사슬(`0x100c2640` …)로 그런 이펙트에 매달린 것은 같은 N. 그렇게 읽은 것만 StaggerSure.
  Move 1   직선탄 `0x100c3490(도착 xy, 도착 z, 빠르기, 배율 double, 방식)` → `0x10037a80`. 틱 `0x10037b50`: 기다림(+0x24) 뒤, 지금 자리 == 도착이면 끝,
           3차원 거리 ≤ 빠르기면 도착 자리로, 아니면 방향 × 빠르기만큼 옮긴 뒤 `빠르기 = 방식 ? 빠르기 × 배율 : 빠르기 + 배율`,
           `빠르기 < 최소(+0x3c)` 면 최소, `빠르기 > 최대(+0x40)` 면 최대(0x10037cf3~0x10037d37). 최소 `0x100c25c0` · 최대 `0x100c25e0`.
           등속(더하기 0.0)은 Mode 1 · ScalePermille 1000 으로 적는다. 더하기 방식(Mode 0)이면 ScalePermille = 틱마다 더하는 px × 1000.
  Move 2   떠오름 `0x100c5c10(?, ?, 높이, 빠르기 double, …)` — MaxSpeed = 높이(월드 z, 음수 = 내려옴) · ScalePermille = 빠르기 × 1000.
  Move 3   떨굼 `0x100c5d40(Obs, 모션, x, y, z, 맵, 중력 double)` — ScalePermille = 중력 × 1000.
  Move 4   고리 `0x100cd310(가운데, 처음 반지름, 끝 반지름, 처음 각 double(×π), 각속도 double, 틱)` — Speed = 처음 반지름 · MaxSpeed = 끝 반지름 ·
           MinSpeed = 처음 각 × 1000 · ScalePermille = 각속도 × 1000 · Mode = 틱.
  Move 5   포물선 `0x100cb550(도착 xy, 도착 z, 틱)` — Speed = 틱.
  Move 9   그 밖(`0x100c57d0`·`0x100c59d0`·`0x100c5550`·`0x100d1970`·`0x100c3b80`·`0x100c4670`·`0x100c4810`·`0x100d1070`·`0x100c3c90`) — 종류만.
  From/To  0 시전자 · 1 대상/겨눈 칸 · 2 「출발 자리 + (Dx, Dy)」(To 에만 — 이때 Dx·Dy 는 출발 치우침이 아니라 도착까지의 화면 벡터, 높이 차 × 0.6 포함) ·
           3 못 읽음(코드가 셈한 자리 — 재생기는 표의 자리를 쓴다).

같은 열쇠의 Row 가 여럿이면 그 표 줄이 원본에서는 그 수만큼(자리·뒤집기가 다르게) 따로 뜬다는 뜻이다(브레인 브레이크 337:0 ×2).
전용 연출 work(`work_fx_timing.keep`)은 뺀다. 근거: 옵시디안 분석/원본차이/ba21-fx-playback.md 「덧정보 표 생성 기록」
"""
import argparse
import ast
import collections
import os
import re
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import work_fx_emu as emu                                   # noqa: E402
import work_fx_patch as wp                                  # noqa: E402
import work_fx_timing as wt                                 # noqa: E402
import work_hit_ticks as ht                                 # noqa: E402
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_OP_REG  # noqa: E402

BASE = emu.BASE
FACE, FACE_KIDS = 0x100e56c0, 0x100e56f0
LINE, MINSPEED, MAXSPEED = 0x100c3490, 0x100c25c0, 0x100c25e0
RISE, DROP, RING, ARC = 0x100c5c10, 0x100c5d40, 0x100cd310, 0x100cb550
OTHER = {0x100c57d0, 0x100c59d0, 0x100c5550, 0x100d1970, 0x100c3b80, 0x100c4670, 0x100c4810, 0x100d1070, 0x100c3c90}
COLLECT_UNITS, COLLECT_CELLS = 0x100df5c0, 0x100defb0
ARRIVE_SLOT = 0x100c29b0                                    # 판정 = 이 이펙트가 사라질 때
MOVE_NAME = {1: '직선탄', 2: '떠오름', 3: '떨굼', 4: '고리', 5: '포물선', 9: '그 밖'}
UNKNOWN = 3


class XY:
    """스택에 16비트씩 적은 (x, y) 를 32비트로 읽은 값."""
    __slots__ = ('x', 'y')

    def __init__(self, x, y):
        self.x, self.y = x, y

    def __repr__(self):
        return 'XY(%s, %s)' % (self.x, self.y)


class XEmu(emu.Emu):
    def store(self, s, a, v, i, ctx, path):
        super().store(s, a, v, i, ctx, path)
        if isinstance(a, tuple) and a[0] == 'sp':
            op = i.operands[0] if i.operands else None
            if op is not None and op.type == X86_OP_MEM and op.size == 2:
                s.mem[('w', a[1])] = (v,)
            else:
                s.mem.pop(('w', a[1]), None)

    @staticmethod
    def _word(s, off):
        m = s.mem.get(('w', off))
        if m is None:
            return None
        cur = s.mem.get(off)
        return m if (cur is m[0] or (cur is not None and cur == m[0])) else None

    def rd(self, s, i, op, ctx):
        if op.type == X86_OP_MEM and op.size == 4:
            a = self.addr(s, i, op)
            if isinstance(a, tuple) and a[0] == 'sp':
                lo, hi = self._word(s, a[1]), self._word(s, a[1] + 2)
                if lo is not None and hi is not None:
                    return XY(lo[0], hi[0])
        return super().rd(s, i, op, ctx)

    def getr(self, s, name):
        v = super().getr(s, name)
        if isinstance(v, XY) and emu.SUB[name][2] != 32:
            return v.x if emu.SUB[name][1] == 0 else None
        return v

    def call(self, s, i, ctx, depth, path):
        ops = i.operands
        if ops[0].type == X86_OP_IMM:
            tgt, this, sp = ops[0].imm, s.reg['ecx'], s.reg['esp']
            if isinstance(this, str) and this.startswith('fx#'):
                a = []
                for k in range(10):
                    v = s.mem.get(sp[1] + 4 * k)
                    if isinstance(v, str) and v.startswith('fx#'):
                        f = ctx['fxs'][int(v[3:])]
                        v = ['fx', f['va'], str(f['path'])]
                    elif isinstance(v, XY):
                        v = ['xy', v.x, v.y]
                    a.append(v)
                ctx['fxs'][int(this[3:])].setdefault('calls', []).append([tgt, i.address, a])
            elif tgt in (COLLECT_UNITS, COLLECT_CELLS):
                ctx['events'].append({'k': 'collect', 'va': i.address, 'path': path + ('collect',), 'stage': ctx['stage'],
                                      'tick': ctx['tick'], 'tgt': tgt})
            # 생성자의 x 인자가 자리 묶음이면 x 만
            n = len(ctx['fxs'])
            super().call(s, i, ctx, depth, path)
            for rec in ctx['fxs'][n:]:
                for k in ('x', 'y', 'z'):
                    if isinstance(rec[k], XY):
                        rec[k] = rec[k].x
            return
        super().call(s, i, ctx, depth, path)


def analyze(game):
    old = emu.Emu
    emu.Emu = XEmu
    try:
        return emu.analyze_all(game)
    finally:
        emu.Emu = old


# ---------------------------------------------------------------- 값 읽기
def s32(v):
    return v - (1 << 32) if isinstance(v, int) and v >= 1 << 31 else v


def dbl(lo, hi):
    if not isinstance(lo, int) or not isinstance(hi, int):
        return None
    return struct.unpack('<d', struct.pack('<II', lo & 0xffffffff, hi & 0xffffffff))[0]


POS = re.compile(r'^(나|나\.대상)\.([xyz])([+-]\d+)?$')


def pos(v, axis):
    """'나.x-200' → (0, -200) · '나.대상.y' → (1, 0) · 그 밖 → None.  axis 가 안 맞아도 None."""
    if not isinstance(v, str):
        return None
    m = POS.match(v)
    if not m or m.group(2) != axis:
        return None
    return (1 if '대상' in m.group(1) else 0, int(m.group(3) or 0))


def pair(v):
    """자리 묶음 인자 → (x 값, y 값). 32비트로 통째로 옮긴 '나.x' 는 (나.x, 나.y)."""
    if isinstance(v, list) and v and v[0] == 'xy':
        return v[1], v[2]
    if isinstance(v, str):
        p = pos(v, 'x')
        if p is not None and p[1] == 0:
            return v, v[:-1] + 'y'
    return None, None


def screen_dy(dy, dz=0):
    return int(round(dy * 0.8 - dz * 0.6))


# ---------------------------------------------------------------- 고리 찾기 (대상별 · 엇갈림)
class Loops:
    def __init__(self, e):
        self.e = e
        self.fn = {}

    def insns(self, start):
        """함수를 주소 순으로 — 마지막 ret 뒤 채움(nop/int3)에서 멈춘다."""
        if start not in self.fn:
            out, va = [], start
            while va < start + 0x6000:
                i = self.e.insn(va)
                if not i:
                    break
                out.append(i)
                va = i.address + i.size
                if i.mnemonic == 'ret' and self.e.d[va - BASE] in (0x90, 0xcc):
                    break
            self.fn[start] = out
        return self.fn[start]

    def loops_of(self, start, va):
        """va 를 감싸는 되돌이 고리들 [(머리, 꼬리 분기 주소)] — 안쪽부터."""
        out = set()
        for i in self.insns(start):
            if not i.mnemonic.startswith('j') or i.operands[0].type != X86_OP_IMM:
                continue
            t = i.operands[0].imm
            if start <= t <= va < i.address and i.address - t < 0x1400:
                out.add((t, i.address))
        return sorted(out, key=lambda p: p[1] - p[0])

    def steps(self, start, loop):
        """고리마다 느는 값들 — 꼬리(되돌이 분기 앞 14줄)의 `add reg, N`(N ≠ 1·4 — 그것은 셈·포인터 걸음)과
        고리 안에서 그 add 로만 바뀌는 레지스터의 `add reg, N`. {N}"""
        body = [i for i in self.insns(start) if loop[0] <= i.address <= loop[1]]
        writes = collections.defaultdict(set)
        for i in body:
            ops = i.operands
            if ops and ops[0].type == X86_OP_REG and i.mnemonic not in ('cmp', 'test', 'push'):
                writes[emu.SUB.get(i.reg_name(ops[0].reg), (None,))[0]].add(i.address)
        out = set()
        for n, i in enumerate(body):
            ops = i.operands
            if i.mnemonic != 'add' or len(ops) != 2 or ops[1].type != X86_OP_IMM or not (1 < ops[1].imm < 400) or ops[1].imm == 4:
                continue
            if ops[0].type == X86_OP_REG:
                r = emu.SUB.get(i.reg_name(ops[0].reg), (None,))[0]
                if r == 'esp':
                    continue
                stored = any(j.mnemonic == 'mov' and j.operands[0].type == X86_OP_MEM and j.operands[1].type == X86_OP_REG
                             and emu.SUB.get(j.reg_name(j.operands[1].reg), (None,))[0] == r for j in body[n + 1:])
                if (n >= len(body) - 14 and stored) or writes[r] == {i.address}:
                    out.add(ops[1].imm)
            elif ops[0].type == X86_OP_MEM and n >= len(body) - 14:
                out.add(ops[1].imm)
        return out

    def stagger(self, start, loops, callva):
        """고리 안의 지연 호출(callva)에 들어가는 인자가 고리마다 얼마씩 느는가 — (N, 확실). 상수면 (0, True), 못 읽으면 (0, False)."""
        body = [i for i in self.insns(start) if loops[-1][0] <= i.address <= loops[-1][1]]
        idx = next((n for n, i in enumerate(body) if i.address == callva), None)
        if idx is None:
            return 0, False
        push = next((body[n] for n in range(idx - 1, -1, -1) if body[n].mnemonic == 'push'), None)
        if push is None:
            return 0, False
        op = push.operands[0]
        if op.type == X86_OP_IMM:
            return 0, True
        if op.type == X86_OP_REG:
            # 엘레맨탈 썬더 꼴 — 지연 = 차례 × N 을 그 자리에서 셈한다(lea reg, [i*N])
            r = emu.SUB.get(push.reg_name(op.reg), (None,))[0]
            for n in range(idx - 1, -1, -1):                 # 그 레지스터에 마지막으로 적은 줄
                i = body[n]
                if (i.mnemonic in ('cmp', 'test', 'push') or not i.operands or i.operands[0].type != X86_OP_REG
                        or emu.SUB.get(i.reg_name(i.operands[0].reg), (None,))[0] != r):
                    if i.mnemonic == 'call' and r in ('eax', 'ecx', 'edx'):
                        break
                    continue
                if i.mnemonic == 'lea':
                    m = i.operands[1].mem
                    if m.index and not m.base and m.scale > 1 and m.disp == 0:
                        return m.scale, True
                break
        for lp in reversed(loops):                           # 바깥 고리부터 — 유닛 고리가 바깥이다(안쪽은 입자 수 고리)
            st = self.steps(start, lp)
            if len(st) == 1:
                return next(iter(st)), True
            if st:
                return 0, False
        return 0, False


# ---------------------------------------------------------------- 한 이펙트의 덧정보
def extra_of(ev, d, on_target, per, stats):
    """(Mirror 깃발, Dx, Dy, PerTarget, Stagger, Sure, Move, Speed, Scale, Mode, Min, Max, From, To, 메모) — Mirror 깃발은 그 방향에서 뒤집는가."""
    calls = ev.get('calls', [])
    flip = False
    for t, _, a in calls:
        if t in (FACE, FACE_KIDS) and isinstance(a[0], int):
            flip = (a[0] & 0xff) == 3
    px, py, pz = pos(ev['x'], 'x'), pos(ev['y'], 'y'), pos(ev['z'], 'z')
    base = px[0] if px and py and px[0] == py[0] else None
    dx = dy = 0
    note = []
    if base is not None and (px[1] or py[1]):
        if base == (1 if on_target else 0):
            dx, dy = px[1], screen_dy(py[1])
        else:
            stats['치우침 — 표 자리와 기준이 달라 뺌'] += 1
    move = speed = scale = mode = lo = hi = frm = to = 0
    for t, _, a in calls:
        if t == LINE and move == 0:
            move = 1
            speed = a[2] if isinstance(a[2], int) and 0 < a[2] < 2000 else 0
            k, mode = dbl(a[3], a[4]), 1 if a[5] else 0
            if k is None:
                scale, mode = 1000, 1
                note.append('배율 못 읽음')
            elif mode == 0 and k == 0.0:
                scale, mode = 1000, 1
            else:
                scale = int(round(k * 1000))
            if speed == 0:
                note.append('빠르기 못 읽음')
            frm, to, dx, dy = ends(ev, a[0], a[1], base, px, py, pz, dx, dy, on_target, note)
        elif t == ARC and move == 0:
            move = 5
            speed = a[2] if isinstance(a[2], int) and 0 < a[2] < 2000 else 0
            frm, to, dx, dy = ends(ev, a[0], a[1], base, px, py, pz, dx, dy, on_target, note)
        elif t == RISE and move == 0:
            move = 2
            k = dbl(a[3], a[4])
            hi = s32(a[2]) if isinstance(a[2], int) and abs(s32(a[2])) < 2000 else 0
            scale = int(round(k * 1000)) if k is not None and 0 < k < 100 else 0
        elif t == DROP and move == 0:
            move = 3
            k = dbl(a[6], a[7])
            scale = int(round(k * 1000)) if k is not None and 0 < k < 100 else 0
        elif t == RING and move == 0:
            move = 4
            speed = a[1] if isinstance(a[1], int) and a[1] < 5000 else 0
            hi = a[2] if isinstance(a[2], int) and a[2] < 5000 else 0
            ang, vel = dbl(a[3], a[4]), dbl(a[5], a[6])
            lo = int(round(ang * 1000)) if ang is not None and 0 <= ang < 10 else 0
            scale = int(round(vel * 1000)) if vel is not None and 0 < vel < 10 else 0
            mode = a[7] if isinstance(a[7], int) and a[7] < 5000 else 0
        elif t in OTHER and move == 0:
            move = 9
            note.append('이동기 0x%x' % t)
        elif t == MINSPEED and isinstance(a[0], int):
            lo = a[0] if move == 1 else lo
        elif t == MAXSPEED and isinstance(a[0], int):
            hi = a[0] if move == 1 else hi
    pt, stag, sure = per
    return (flip, dx, dy, pt, stag, sure, move, speed, scale, mode, lo, hi, frm, to, note)


def ends(ev, dest, destz, base, px, py, pz, dx, dy, on_target, note):
    """직선탄·포물선의 (From, To, Dx, Dy)."""
    frm = base if base is not None else UNKNOWN
    if frm == UNKNOWN:
        dx = dy = 0
    ex, ey = pair(dest)
    qx, qy, qz = pos(ex, 'x'), pos(ey, 'y'), pos(destz, 'z')
    if qx is None or qy is None or qx[0] != qy[0]:
        return frm, UNKNOWN, dx, dy
    if frm == UNKNOWN or qx[0] != frm:
        return frm, qx[0], dx, dy
    # 출발과 도착이 같은 기준 — 도착 = 출발 + 고정 벡터
    vx, vy = qx[1] - px[1], qy[1] - py[1]
    vz = (qz[1] - pz[1]) if (qz and pz and qz[0] == pz[0]) else 0
    if vx == 0 and vy == 0 and vz == 0:
        return frm, UNKNOWN, dx, dy                          # 제자리 — 도착을 잘못 읽은 것(묶음의 한쪽만 고친 코드)
    if qx[1] == 0 and qy[1] == 0 and vz == 0:
        return frm, frm, dx, dy                              # 치우친 데서 떠나 기준 자리로 — Dx·Dy 는 출발 치우침 그대로
    if px[1] or py[1]:
        note.append('출발 치우침 (%+d, %+d) 은 못 실음' % (px[1], py[1]))
    return frm, 2, vx, screen_dy(vy, vz)


# ---------------------------------------------------------------- 표 맞추기
def read_table(path):
    text = open(path, encoding='utf-8-sig').read()
    lines = text.split('\n')
    start = next(i for i, ln in enumerate(lines) if 'WorkScripts = new()' in ln)
    end = next(i for i in range(start, len(lines)) if lines[i].strip() == '};')
    rowre = re.compile(r'^(\s*)\[(\d+)\] = \(\[([^\]]*)\], \[(.*)\]\),\s*$')
    rows = {}
    for i in range(start + 2, end):
        m = rowre.match(lines[i])
        if not m:
            raise SystemExit('모르는 줄 %d: %s' % (i + 1, lines[i][:80]))
        rows[int(m.group(2))] = wp.parse_effs(m.group(4))
    return rows


def build(game, cs):
    tool = ht.Tool(game)                                     # 단계 시작 틱은 덧씌우지 않은 실행기로(work_fx_timing 과 같은 값)
    works = tool.e.works
    names = ht.names(tool.g, works)
    res, e = analyze(game)
    loops = Loops(e)
    table = read_table(cs)
    mlens = {}

    def mlen(obs, mo):
        if (obs, mo) not in mlens:
            mlens[(obs, mo)] = tool.motion_len(obs, mo)
        return mlens[(obs, mo)]

    stats = collections.Counter()
    out = collections.OrderedDict()                          # work → [(열쇠, 덧정보, 메모, 주소)]
    arrive = set()
    cellloop = set()
    odd = []
    for w in sorted(table):
        r = res.get(w)
        if r is None or not table[w]:
            continue
        if wt.keep(works, w):
            stats['뺀 work(전용 연출)'] += 1
            continue
        try:
            st = wt.stage_starts(tool, r['handler'], w)
        except Exception:
            st = {}
        effs = table[w]
        got = collections.OrderedDict()                      # 표 줄(번호) → {방향: [덧정보]}
        for d in (1, 0, 2, 3):
            evs = r['main'][d]['events']
            tm = wt.Timer(evs, st, mlen)
            first, last_collect, stag_of = {}, None, {}
            for n, ev in enumerate(evs):
                if ev['k'] == 'collect':
                    last_collect = ev['tgt']
                    continue
                if ev['k'] != 'fx':
                    continue
                # 고리 — 그림이 아닌 이펙트(몸 복제 따위)도 사슬의 앞 것이 될 수 있어 먼저 셈해 둔다
                path = ast.literal_eval(ev['path']) if isinstance(ev['path'], str) else ev['path']
                fstart = path[-1] if path and isinstance(path[-1], int) else r['handler']
                lp = loops.loops_of(fstart, ev['va'])
                per = (False, 0, False)
                free = pos(ev['x'], 'x') is None and ev['parent'] not in ('나', '나.대상')
                if lp and free and last_collect == COLLECT_UNITS:
                    stag, sure = 0, False
                    for t, cva, a in ev.get('calls', []):
                        if t == emu.DELAY and lp[-1][0] <= cva <= lp[-1][1]:
                            stag, sure = loops.stagger(fstart, lp, cva)
                            break
                    ch = ev.get('chain')
                    if not sure and ch and ch[1] is not None and stag_of.get((ch[1], ch[2]), (0, False))[1]:
                        stag, sure = stag_of[(ch[1], ch[2])]     # 사슬 — 앞 것이 엇갈리는 만큼 같이 엇갈린다
                    per = (True, stag, sure)
                    stag_of[(ev['va'], ev['path'])] = (stag, sure)
                elif lp and free and last_collect == COLLECT_CELLS and wt.is_pic(ev):
                    cellloop.add(w)
                if not wt.is_pic(ev):
                    continue
                key = (ev['obs'] & 0xffff, ev['motion'] & 0xffff)
                if first.setdefault(key, ev['stage']) != ev['stage']:
                    continue                                 # work_fx_timing 과 같이 — 첫 단계 것만
                start, _ = tm.span(n)
                cand = [k for k, y in enumerate(effs) if (y[0], y[1]) == key and y[8] in (-1, d)]
                if not cand:
                    stats['표에 없는 이펙트(방향 가지·덤)'] += 1
                    continue
                hit = [k for k in cand if effs[k][4] == start]
                if not hit:
                    if len({(effs[k][4], effs[k][8]) for k in cand}) != 1:
                        stats['못 맞춘 이펙트(때가 다른 줄이 여럿)'] += 1
                        continue
                    hit = cand                               # 때를 못 푼 그림(표 값을 지킨 줄) — 줄이 하나뿐이면 그것
                if len(hit) > 1:
                    lift = wp.zlift(ev['z'])
                    fly = ev['ctor'] in wp.FLYERS and ev['kind'] != 'emit'
                    hit = [k for k in hit if effs[k][3] == lift] or hit
                    hit = [k for k in hit if effs[k][6] == fly] or hit
                k = hit[0]
                x = extra_of(ev, d, effs[k][2], per, stats)
                if x[6] == 1 and any(t == ARRIVE_SLOT for t, _, _ in ev.get('calls', [])):
                    arrive.add(w)
                if x[6] and any(t == ARRIVE_SLOT for t, _, _ in ev.get('calls', [])):
                    stats['판정이 「움직이는 이펙트가 사라질 때」인 (work, 방향, 이펙트)'] += 1
                got.setdefault(k, {}).setdefault(d, []).append((x, ev['va']))
        rows = []
        for k, by in got.items():
            y = effs[k]
            if y[8] >= 0:
                # 방향 줄 — 오른쪽(3) 줄이면 「오른쪽을 볼 때」, 다른 방향 줄에서 3 을 주면 늘
                picked = [(((1 if y[8] == 3 else 2) if x[0] else 0,) + x[1:14], x[14], va) for x, va in by.get(y[8], [])]
            else:
                # 방향 없는 줄 — 왼쪽(1)을 볼 때 값을 쓴다. 오른쪽(3)에서만 뒤집히면 Mirror 1. 그 밖의 값이 방향마다 다르면 센다
                d0 = 1 if 1 in by else next(iter(by))
                ref, right = by[d0], by.get(3, [])
                if any(sorted(map(repr, (x[1:14] for x, _ in v))) != sorted(map(repr, (x[1:14] for x, _ in ref))) for v in by.values()):
                    # 값이 방향마다 다르다(앞쪽으로 치우치는 것) — 표 줄은 하나지만 Row 는 방향마다 낸다(Row.Facing = 방향)
                    stats['방향 없는 표 줄인데 값이 방향마다 달라 방향 Row 로 낸 (work, 그림)'] += 1
                    odd.append((w, y[0], y[1], {d: [x[1:14] for x, _ in v] for d, v in by.items()}))
                    for d in sorted(by):
                        for x, va in by[d]:
                            b = ((1 if d == 3 else 2) if x[0] else 0,) + x[1:14]
                            if ((y[0], y[1], y[4], d), b) not in [(r[0], r[1]) for r in rows]:
                                rows.append(((y[0], y[1], y[4], d), b, x[14], va))
                    continue
                picked = []
                for n, (x, va) in enumerate(ref):
                    if x[0]:
                        mir = 1 if d0 == 3 else 2
                    else:
                        mir = 1 if n < len(right) and right[n][0][0] else 0
                    picked.append(((mir,) + x[1:14], x[14], va))
            uniq = []
            for b, note, va in picked:
                if b not in [u[0] for u in uniq]:
                    uniq.append((b, note, va))
            if all(all(v in (0, False) for v in b) for b, _, _ in uniq):
                continue                                     # 덧정보 없음. 하나라도 있으면 빈 Row 도 남긴다(짝의 안 뒤집는 쪽)
            for b, note, va in uniq:
                rows.append(((y[0], y[1], y[4], y[8]), b, note, va))
        if rows:
            # 표에 열쇠가 같은 줄이 둘(높이만 다른 것) 있으면 같은 Row 가 두 번 나온다 — 하나로
            one = []
            for x in rows:
                if (x[0], x[1]) not in [(u[0], u[1]) for u in one]:
                    one.append(x)
            out[w] = one
    build.odd = odd
    return out, res, names, stats, arrive, cellloop, table


# ---------------------------------------------------------------- 쓰기
def fmt(key, b):
    o, m, dl, fc = key
    mir, dx, dy, pt, stag, sure, move, speed, scale, mode, lo, hi, frm, to = b
    return 'new(%d, %d, %d, %d, %d, %d, %d, %s, %d, %s, %d, %d, %d, %d, %d, %d, %d, %d)' % (
        o, m, dl, fc, mir, dx, dy, 'true' if pt else 'false', stag, 'true' if sure else 'false',
        move, speed, scale, mode, lo, hi, frm, to)


def write(path, out, res, names, arrive):
    L = ['// <auto-generated> tools/re/work_fx_extra2.py 가 만든다 — 손으로 고치지 않는다.',
         'namespace DuelDx;',
         '',
         '/// <summary>이펙트 표(AbilityScripts.g.cs)의 줄마다 붙는 덧정보 — 줄은 (work, Obs, 모션, Delay, Facing) 으로 찾는다(Facing 은 표와 같은 값, 방향 구분이 없으면 −1).</summary>',
         '/// <remarks>',
         '/// 같은 열쇠의 Row 가 여럿이면 그 표 줄이 원본에서는 그 수만큼 따로(자리·뒤집기가 다르게) 뜬다 — Row 마다 하나씩 띄운다',
         '/// (그때는 덧정보가 없는 쪽도 빈 Row 로 들어 있다 — 스피드 업 396:4 의 안 뒤집는 쪽).',
         '/// 표 줄은 방향이 없는데(−1) 덧정보가 방향마다 다르면(앞쪽으로 치우치는 것) Row 를 방향마다 냈다(Row.Facing = 0 위 · 1 왼 · 2 아래 · 3 오른) —',
         '/// 찾을 때 Facing 이 표와 같은 Row 가 없으면 (Obs, 모션, Delay) 가 같고 Facing 이 시전자 방향인 Row 를 쓴다.',
         '/// Dx·Dy 는 그 Row 의 방향에서 읽은 값 그대로다 — 뒤집을 때 부호를 또 바꾸지 않는다. Dx 는 월드 x, Dy 는 월드 y × 0.8(칸 40:32)이고',
         '/// 높이 z 는 화면 위로 × 0.6 — 표의 Lift 가 이미 그 값이라 여기에는 없다(To 가 2 인 벡터의 Dy 에만 높이 차가 들어 있다).',
         '/// 직선탄 빠르기는 월드 3차원(x, y, z) 거리의 틱당 px 다(원본 틱 0x10037b50) — 화면에서 재면 세로로 날수록 빨라 보인다.',
         '/// 줄 끝 주석: 이름 · 핸들러 주소. 못 읽은 값은 0(빠르기) 또는 From/To 3 이다.',
         '/// </remarks>',
         'internal static class WorkFxExtra',
         '{',
         '    /// <param name="Mirror">0 안 뒤집음 · 1 시전자가 오른쪽을 볼 때 뒤집음 · 2 늘 뒤집음</param>',
         '    /// <param name="Dx">기준 자리에서 x 치우침(px, 그 Facing 줄 기준 부호 포함) · Dy 는 화면 y(위가 −). To 가 2 면 출발에서 도착까지의 화면 벡터</param>',
         '    /// <param name="PerTarget">대상 유닛마다 하나씩 · Stagger = 대상 사이 틱(모르면 0) · StaggerSure</param>',
         '    /// <param name="Move">0 없음 · 1 직선탄 · 2 떠오름 · 3 떨굼 · 4 고리 · 5 포물선 · 9 그 밖</param>',
         '    /// <param name="Speed">틱당 px(직선탄) · ScalePermille 틱마다 곱(1000 = 등속) · Mode 0 더하기/1 곱하기 · MinSpeed/MaxSpeed(0 = 없음).',
         '    /// 떠오름: MaxSpeed = 높이, ScalePermille = 빠르기 × 1000. 떨굼: ScalePermille = 중력 × 1000.',
         '    /// 고리: Speed = 처음 반지름, MaxSpeed = 끝 반지름, MinSpeed = 처음 각(π 의 천분율), ScalePermille = 각속도 × 1000, Mode = 틱. 포물선: Speed = 걸리는 틱.</param>',
         '    /// <param name="From">0 시전자 · 1 대상/겨눈 칸 · 2 고정 치우침(To 에만 — 도착 = 출발 + (Dx, Dy)) · 3 못 읽음(표의 자리를 쓴다) ; To 도 같은 뜻</param>',
         '    public readonly record struct Row(int Obs, int Motion, int Delay, int Facing, int Mirror, int Dx, int Dy,',
         '                                      bool PerTarget, int Stagger, bool StaggerSure,',
         '                                      int Move, int Speed, int ScalePermille, int Mode, int MinSpeed, int MaxSpeed, int From, int To);',
         '',
         '    public static readonly Dictionary<int, Row[]> Table = new()',
         '    {']
    for w, rows in out.items():
        nm = ' '.join((names.get(w) or '').split())
        L.append('        [%d] = [%s],   // %s · 0x%x' % (w, ', '.join(fmt(k, b) for k, b, _, _ in rows), nm, res[w]['handler']))
    L += ['    };',
          '',
          '    /// <summary>판정이 「직선탄이 사라질 때」(0x100c29b0)에 걸린 work — 탄의 비행 틱이 곧 판정 틱이다.</summary>',
          '    public static readonly HashSet<int> ArriveHitWorks =',
          '    [']
    ids = sorted(arrive)
    blk = ['        ' + ' '.join('%d,' % x for x in ids[k:k + 20]) for k in range(0, len(ids), 20)]
    if blk:
        blk[-1] = blk[-1].rstrip(',')
    L += blk + ['    ];', '}', '']
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(L))


def summarize(out, table, stats, arrive, cellloop):
    c, wk = collections.Counter(), collections.defaultdict(set)

    def add(name, w):
        c[name] += 1
        wk[name].add(w)

    miss = dup = split = 0
    for w, rows in out.items():
        keys = {(y[0], y[1], y[4], y[8]) for y in table[w]}
        seen = collections.Counter()
        for k, b, note, va in rows:
            if k not in keys:
                if k[:3] + (-1,) in keys:
                    split += 1
                else:
                    miss += 1
            seen[k] += 1
            mir, dx, dy, pt, stag, sure, move, speed, scale, mode, lo, hi, frm, to = b
            if mir:
                add('뒤집기', w)
                add('뒤집기 %d' % mir, w)
            if (dx or dy) and to != 2:
                add('치우침', w)
                if abs(dx) >= 16 or abs(dy) >= 16:
                    add('치우침 16px 이상', w)
            if pt:
                add('대상별', w)
                add('대상별 · 엇갈림 %s' % ('확실 %d틱' % stag if sure else '불확실'), w)
                add('대상별 · ' + ('확실' if sure else '불확실'), w)
            if move == 1:
                add('직선탄', w)
                add('직선탄 · ' + ('등속' if scale == 1000 and mode == 1 else '가속' if (mode == 1 and scale > 1000) or (mode == 0 and scale > 0) else '감속'), w)
                add('직선탄 · 출발 %d → 도착 %d' % (frm, to), w)
                if speed == 0:
                    add('직선탄 · 빠르기 못 읽음', w)
            elif move:
                add('이동기 · ' + MOVE_NAME[move], w)
        dup += sum(1 for n in seen.values() if n > 1)
    print('work %d개 · 줄 %d개 · 표와 못 맞춘 줄 %d · 표는 방향 없는 줄인데 방향 Row 로 낸 줄 %d · 같은 열쇠가 여럿인 줄 %d' % (
        len(out), sum(len(v) for v in out.values()), miss, split, dup))
    for name in sorted(c):
        print('  %-28s %5d줄 / %4d work' % (name, c[name], len(wk[name])))
    print('  탄이 사라질 때 판정인 work %d · 칸 고리(유닛 아님) work %d' % (len(arrive), len(cellloop)))
    for k, v in sorted(stats.items()):
        print('  %s: %d' % (k, v))
    return miss


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('--cs', default=os.path.join(HERE, '..', '..', 'duel-dx', 'Ability', 'AbilityScripts.g.cs'))
    ap.add_argument('--out', default=os.path.join(HERE, '..', '..', 'duel-dx', 'Ability', 'WorkFxExtra.g.cs'))
    ap.add_argument('--dry', action='store_true', help='파일을 안 쓰고 통계만')
    ap.add_argument('--report', help='줄마다 (work, 이름, 핸들러, 줄, 메모, 생성 주소) 를 TSV 로')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    out, res, names, stats, arrive, cellloop, table = build(a.game, a.cs)
    # 표에 없는 열쇠는 뺀다(있으면 센다)
    dropped = 0
    for w in list(out):
        keys = {(y[0], y[1], y[4], y[8]) for y in table[w]}
        keep = [x for x in out[w] if x[0] in keys or x[0][:3] + (-1,) in keys]
        dropped += len(out[w]) - len(keep)
        if keep:
            out[w] = keep
        else:
            del out[w]
    if dropped:
        print('표에 없는 열쇠라 뺀 줄 %d' % dropped)
    summarize(out, table, stats, arrive, cellloop)
    if a.report:
        with open(a.report, 'w', encoding='utf-8') as f:
            f.write('work\t이름\t핸들러\t줄\t메모\t생성 주소\n')
            for w, rows in out.items():
                for k, b, note, va in rows:
                    f.write('%d\t%s\t0x%x\t%s\t%s\t0x%x\n' % (w, ' '.join((names.get(w) or '').split()), res[w]['handler'],
                                                              fmt(k, b), '; '.join(note), va))
    if not a.dry:
        write(a.out, out, res, names, arrive)
        print('%s 를 적었다' % os.path.relpath(a.out, os.path.join(HERE, '..', '..')))


if __name__ == '__main__':
    main()

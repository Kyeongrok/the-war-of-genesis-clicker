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
  Move 2   떠오름 `0x100c5c10(반폭 W, 마디 높이 H, 높이 T, 빠르기 double, 배율 double, 방식, 처음 쪽)` → `0x10038b70` — MaxSpeed = T(월드 z, 음수 = 내려옴) ·
           ScalePermille = 빠르기 × 1000. 길: 네모 (x ± W, z ~ z + H) 에 든 타원 둘레의 점 목록(`0x100095e0`·`0x10008e10`)을 반씩 좌우로 번갈아 타며
           (마디마다 z 가 H + 1 씩) 틱(`0x10038da0`)마다 「빠르기」 점씩 나아간다 — x 와 z 만 바뀐다. `|z − 처음 z| ≥ |T|` 면 끝.
           본 곳은 모두 W = rand % 3 + 1 · H = rand % 20 + 25 · 처음 쪽 = rand & 1 · 배율 0.0(더하기) — 곧 좌우 1~3 흔들리며 거의 곧게 오른다.
           T 가 `rand % N + C` 인 곳(상수가 아니라 실행기가 못 읽는다)은 코드를 따로 읽어 MaxSpeed = C + N/2 · MinSpeed = N (`rise_read`).
  Move 3   떨굼(튀는 조각) — 생성자 `0x100c5c50(Obs, 모션, x, y, z, 맵, 주인, work)` 가 이동기(`0x10038ee0`, 0x90 바이트)를 +0x78 에 달고,
           설정 `0x100c5d40(vx double, vy double, vz double, 튕김 double)` → `0x10038f40` 이 속도와 튕김 계수를 넣는다(중력 인자는 없다 —
           틱 `0x10038fa0` 의 상수 5.0). 틱: `x += vx/2; y += vy/2; 새vz = vz − 5; z += (vz + 새vz)/4; z ≤ 0 이면 z = −z, vz = −새vz × 튕김`,
           `|vz| < 5` 일 때 z 가 땅 높이(`0x100dfc00`) ± 5 안이거나 땅 아래·맵 밖(`0x100dfb70`)이면 끝.
           세 double 을 `fild`/`fstp` 로 스택에 적는 곳은 실행기가 못 읽어(생성자 인자가 남아 보인다) 생성~설정 사이 코드를 따로 읽는다(`drop_read`):
           `call 0x100083d0(rand); cdq; mov ecx, N; idiv ecx; add/sub …, C` = `rand % N + C`, 차례는 (vx, vy, vz) — 두 곳(0x10082310 · 0x100a9b60) 손으로 확인.
           ScalePermille = 튕김 × 1000 · MaxSpeed = 시작 높이(생성자 z 의 `나.z+N` 의 N, 월드 z) · Speed = 처음 vz × 1000(무작위면 가장 작은 값) ·
           MinSpeed = vz 무작위 폭 N · Mode = 수평 속도 무작위 폭 N(vx, vy = rand % N − N/2) · From · To = 시작 자리 x · y 무작위 폭(생성자 x, y 가
           `나.x + rand % N + C` 꼴 — 가운데 C + N/2 가 0 이 아니면 Dx·Dy 에).
  Move 4   고리 `0x100cd310(가운데, 처음 반지름, 끝 반지름, 처음 각 double(×π), 각속도 double, 틱)` — Speed = 처음 반지름 · MaxSpeed = 끝 반지름 ·
           MinSpeed = 처음 각 × 1000 · ScalePermille = 각속도 × 1000 · Mode = 틱.
  Move 5   포물선 `0x100cb550(도착 xy, 도착 z, 틱)` — Speed = 틱.
  Move 9   그 밖(`0x100c57d0`·`0x100c59d0`·`0x100c5550`·`0x100d1970`·`0x100c3b80`·`0x100c4670`·`0x100c4810`·`0x100d1070`·`0x100c3c90`) — 종류만.
  From/To  0 시전자 · 1 대상/겨눈 칸 · 2 「출발 자리 + (Dx, Dy)」(To 에만 — 이때 Dx·Dy 는 출발 치우침이 아니라 도착까지의 화면 벡터, 높이 차 × 0.6 포함) ·
           3 못 읽음(코드가 셈한 자리 — 재생기는 표의 자리를 쓴다).

떼 표 `duel-dx/Ability/WorkFxSwarm.g.cs`(`--swarm`) — 떠오름·고리 Row 의 생성이 되돌이 고리 안에 있을 때 그 고리가 뿌리는 낱알의 값(`swarm_read`):
  Count      생성을 감싸는 가장 안쪽 고리의 횟수 — `mov reg, N … dec reg; jne` · `mov [esp+X], N … dec; mov [esp+X]; jne` · `inc reg; cmp reg, N; jl`.
  XWidth·YWidth  new ~ 생성자 사이의 `rand % N + 자리 낱말(+0x3e x · +0x40 y) + C` 의 N (가운데 C + N/2 가 0 이 아니면 Sure = false).
  DelayRandom·DelayStep  지연 `0x100c24d0` 인자가 `rand % N` 이면 N, `lea reg, [i+i]`·`[i*N]` 이면 2·N.
  고리 인자   생성자 ~ 설정 사이에 민 낱말을 차례로 세어(`push imm` · `sub esp, 8` 은 모르는 double) 16개가 되면 A8~A15 를 읽는다 —
             dz0 (A8,A9) · 각속도 배율 k2 (A10,A11) · 방식2 A12 · dz 배율 k (A13,A14) · 방식 A15. 처음 각이 `fild 차례; fmul 상수` 면 AngleStep = 상수.
  StartZ     생성자 z 의 치우침. 실행기가 못 읽으면 `mov r16, [유닛+0x42]; add r16, N; push` 를 읽는다(`static_z`), z 가 `rand % N + C` 면 가운데 값.
  못 읽은 것이 하나라도 있으면 Sure = false. 고리 밖(Count 1)이고 값이 모두 0 인 줄, 고리 횟수를 못 읽은 줄은 안 싣는다.

뿌리개 표 `duel-dx/Ability/WorkFxSpray.g.cs`(`--spray`) — 그림 없는 바탕(`0x100c1ef0`) 위의 뿌리개 8종. 설정 호출의 인자(16개까지 적어 둔다)를 읽는다(`spray_of`).
생성자 ~ 설정 사이에 `fstp [esp…]` 가 있으면(실행기가 못 읽는 double) Sure = false. 줄은 이펙트 표의 (Obs, 모션, Delay) 로 맞춘다 — 모션이 방향 번호인 8번은 Obs 로만.
  1 `0x100cb8a0` + `0x100cb980(Obs, 모션, 가운데 xy, 반지름, 처음 자리 double, 빠르기 double, dz double, 틱, 배율 double, 방식, 방향)` — 뿌리개가 돌며 오르는 이동기
    (`0x10038940`, 리미트 크래쉬와 같은 것)를 타고, 틱(`0x100cba00`)마다 제자리에 (Obs, 모션) 한 장 + 떠오름 낱알 셋(둘째 Obs·모션 = +0x130/+0x132, 처음 높이까지 내려감).
  2 `0x100cc600` + `0x100cc680(Obs, 모션, 개수, x, y, z, rx, ry, rz, 빠르기, 배율 double, 방식)` — 한꺼번에: 가운데에서 상자 ±(rx, ry, rz) 안의 무작위 점으로 직선탄.
  3 `0x100cc810` + `0x100cc890(Obs, 모션, 개수, x, y, z, R, 빠르기, 배율 double, 방식)` — 한꺼번에: i 번째가 각 i × 2/개수(π)로 반지름 10 → R 직선탄.
  4 `0x100cd490` + `0x100cd510(Obs, 모션, 개수, 간격)` — 틱(`0x100cd560`): (간격 + 1)틱마다 한 장, n 번째는 z + 60n. 개수만큼 내면 끝.
  5 `0x100cc200` + `0x100cc280(…2 와 같음…)` — 한꺼번에: 상자 ±(rx, ry, rz) 의 무작위 점에서 가운데로 직선탄(2 의 거꾸로).
  6 `0x100cc3f0` + `0x100cc470(Obs, 모션, 개수, x, y, z, R, 빠르기, 배율 double, 방식)` — 각 rand % 400 × 0.005(π) · 반지름 rand % R 의 점에서 가운데로, 지연 rand % 30.
  7 `0x100cbd20` + `0x100cbe00(Obs, 모션, …13개 → 이동기 0x10038650)` — 틱(`0x100cbee0`)은 1 과 같다(낱알이 땅 높이까지). 이동기 길은 못 읽었다 → Sure = false.
  8 `0x100ccee0` + `0x100ccf60(Obs, x, y, z, R, 빠르기, 배율 double, 방식)` — 늘 36장: i 번째가 각 (i − 9)/18(π)로 반지름 10 → R, 모션 = 방향 번호(18부터는 뒤집음).
  2·3·5·8 의 낱알은 지연 −1 로 만들어져 뿌리개 나이 1 에 한꺼번에 풀린다(`0x100cc7d0` → `0x100c24f0(0)`), 수명 = 모션 길이 − 1, 닿으면 사라진다.

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
RAND = 0x100083d0                                           # 셈을 하나 올리고 CRT rand 로 뛴다
AXIS = {0x3e: 'x', 0x40: 'y', 0x42: 'z'}                    # 유닛·이펙트의 월드 자리 낱말
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
                for k in range(16):
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


# ---------------------------------------------------------------- 떨굼 — 속도 · 시작 자리 (코드를 따로 읽는다)
def rands(seq):
    """줄들 안의 `rand % N + C (+ 나의 자리 낱말)` 들 — [(N, C, 축 또는 None)] 나온 차례대로."""
    out = []
    for n, i in enumerate(seq):
        if i.mnemonic != 'call' or i.operands[0].type != X86_OP_IMM or i.operands[0].imm != RAND:
            continue
        mod, c, axis, regs, divided = None, 0, None, {'edx'}, False
        for j in seq[n + 1:]:
            ops = j.operands
            if j.mnemonic == 'call':
                break
            if j.mnemonic == 'idiv':
                divided = True
                continue
            r0 = emu.SUB.get(j.reg_name(ops[0].reg), (None,))[0] if ops and ops[0].type == X86_OP_REG else None
            if not divided:
                if j.mnemonic == 'mov' and r0 == 'ecx' and ops[1].type == X86_OP_IMM:
                    mod = ops[1].imm
                continue
            if j.mnemonic == 'mov' and r0 and r0 != 'esp' and ops[1].type == X86_OP_REG:
                if emu.SUB.get(j.reg_name(ops[1].reg), (None,))[0] in regs:
                    regs.add(r0)                             # 나머지를 다른 레지스터로 옮겨 셈한다
                else:
                    regs.discard(r0)
            elif j.mnemonic in ('add', 'sub') and r0 in regs and len(ops) == 2:
                if ops[1].type == X86_OP_IMM:
                    c += s32(ops[1].imm & 0xffffffff) * (1 if j.mnemonic == 'add' else -1)
                elif ops[1].type == X86_OP_MEM and j.mnemonic == 'add' and ops[1].mem.base and not ops[1].mem.index:
                    axis = AXIS.get(ops[1].mem.disp, '?')       # 유닛(나 · 대상)의 자리 낱말
                elif ops[1].type == X86_OP_REG:
                    axis = '?'                               # 레지스터 값을 더하고 뺀다 — 상수가 아니다
        if mod and divided:
            out.append((mod, c, axis))
    return out


def drop_read(site, ev, cva, a, note):
    """(처음 vz × 1000, vz 폭, 수평 폭, 시작 x 폭, 시작 y 폭, 가운데 dx, 가운데 dy(월드)) — 못 읽은 것은 0 + 메모."""
    loops, fstart = site if site else (None, None)
    seq = loops.insns(fstart) if loops else []
    at = {i.address: n for n, i in enumerate(seq)}
    if ev['va'] not in at or cva not in at:
        note.append('떨굼 속도 못 읽음')
        return 0, 0, 0, 0, 0, 0, 0
    n0, n1 = at[ev['va']], at[cva]
    span = seq[n0 + 1:n1]
    written = sum(1 for i in span if i.mnemonic == 'fstp' and i.operands[0].type == X86_OP_MEM
                  and i.reg_name(i.operands[0].mem.base) == 'esp')
    vz = vzn = hn = 0
    rs = rands(span)
    if written == 0 and not rs:
        v = [dbl(a[k], a[k + 1]) for k in (0, 2, 4)]         # 상수 여덟 낱말을 그대로 민 꼴 — 실행기가 읽은 값이 맞다
        if None in v or max(abs(x) for x in v) > 1000:
            note.append('떨굼 속도 못 읽음')
        else:
            vz = int(round(v[2] * 1000))
            if v[0] or v[1]:
                note.append('떨굼 수평 속도 (%g, %g) 은 못 실음' % (v[0], v[1]))
    elif written == 3 and len(rs) == 3 and all(r[2] is None for r in rs):
        (xn, xc, _), (yn, yc, _), (vzn, zc, _) = rs
        vz = zc * 1000
        if xn == yn and xc == yc == -(xn // 2):
            hn = xn
        else:
            note.append('떨굼 수평 속도 rand%%%d%+d · rand%%%d%+d 은 못 실음' % (xn, xc, yn, yc))
    else:
        note.append('떨굼 속도 못 읽음')
    # 시작 자리 — new 와 생성자 사이의 rand
    new = next((n for n in range(n0 - 1, max(n0 - 60, -1), -1) if seq[n].mnemonic == 'call'
                and seq[n].operands[0].type == X86_OP_IMM and seq[n].operands[0].imm == emu.NEW), None)
    sx = sy = cx = cy = 0
    for mod, c, axis in (rands(seq[new + 1:n0 + 1]) if new is not None else []):
        if axis == 'x':
            sx, cx = mod, c + mod // 2
        elif axis == 'y':
            sy, cy = mod, c + mod // 2
    return vz, vzn, hn, sx, sy, cx, cy


SWARM = {}                                                  # 생성 주소 → 떼 값 (extra_of 가 채운다)


def loop_count(loops, fstart, va):
    """va 를 감싸는 가장 안쪽 고리의 횟수 — (N, 확실). 고리 밖이면 (1, True), 못 읽으면 (0, False)."""
    lp = loops.loops_of(fstart, va)
    if not lp:
        return 1, True
    head, tail = lp[0]
    seq = loops.insns(fstart)
    at = {i.address: n for n, i in enumerate(seq)}
    if head not in at or tail not in at:
        return 0, False
    nh, nt = at[head], at[tail]
    before = seq[max(0, nh - 14):nh]
    for i in reversed(seq[max(nh, nt - 6):nt]):
        ops = i.operands
        if i.mnemonic == 'cmp' and ops[0].type == X86_OP_REG and ops[1].type == X86_OP_IMM and seq[nt].mnemonic in ('jl', 'jb'):
            return ops[1].imm, True                          # inc reg; cmp reg, N; jl — 0 부터
        if i.mnemonic == 'dec' and ops[0].type == X86_OP_REG:
            r = emu.SUB.get(i.reg_name(ops[0].reg), (None,))[0]
            n = at[i.address]
            nxt = seq[n + 1]
            if (nxt.mnemonic == 'mov' and nxt.operands[0].type == X86_OP_MEM and nxt.operands[1].type == X86_OP_REG
                    and emu.SUB.get(nxt.reg_name(nxt.operands[1].reg), (None,))[0] == r
                    and nxt.reg_name(nxt.operands[0].mem.base) == 'esp'):
                disp = nxt.operands[0].mem.disp              # 셈이 스택에 있다
                for j in reversed(before):
                    o = j.operands
                    if (j.mnemonic == 'mov' and o[0].type == X86_OP_MEM and o[1].type == X86_OP_IMM
                            and j.reg_name(o[0].mem.base) == 'esp' and o[0].mem.disp == disp and o[0].size == 4):
                        return o[1].imm, True
                return 0, False
            for j in reversed(before):
                o = j.operands
                if (j.mnemonic == 'mov' and o[0].type == X86_OP_REG and o[1].type == X86_OP_IMM
                        and emu.SUB.get(j.reg_name(o[0].reg), (None,))[0] == r):
                    return o[1].imm, True
            return 0, False
    return 0, False


def static_z(seq):
    """`mov r16, [유닛+0x42]; (add r16, N); push r` 의 N — 레지스터를 더하면 None."""
    r, off = None, 0
    for i in seq:
        ops = i.operands
        if (i.mnemonic == 'mov' and len(ops) == 2 and ops[0].type == X86_OP_REG and ops[1].type == X86_OP_MEM
                and ops[1].mem.disp == 0x42 and ops[1].size == 2 and ops[1].mem.base):
            r, off = emu.SUB.get(i.reg_name(ops[0].reg), (None,))[0], 0
        elif r and ops and ops[0].type == X86_OP_REG and emu.SUB.get(i.reg_name(ops[0].reg), (None,))[0] == r:
            if i.mnemonic == 'push':
                return off
            if i.mnemonic in ('add', 'sub') and ops[1].type == X86_OP_IMM:
                off += s32(ops[1].imm & 0xffffffff) * (1 if i.mnemonic == 'add' else -1)
            else:
                return None
    return None


def swarm_read(site, ev, cva, a, move):
    """떼 값 (Count, XWidth, YWidth, DelayRandom, DelayStep, AngleStep‰, W2‰, W2Multiply, Dz0‰, DzK‰, DzMultiply, StartZ, Sure)."""
    loops, fstart = site
    seq = loops.insns(fstart)
    at = {i.address: n for n, i in enumerate(seq)}
    if ev['va'] not in at or cva not in at:
        return None
    n0, n1 = at[ev['va']], at[cva]
    count, sure = loop_count(loops, fstart, ev['va'])
    # 시작 자리
    new = next((n for n in range(n0 - 1, max(n0 - 60, -1), -1) if seq[n].mnemonic == 'call'
                and seq[n].operands[0].type == X86_OP_IMM and seq[n].operands[0].imm == emu.NEW), None)
    xw = yw = 0
    zrand = None
    for mod, c, axis in (rands(seq[new + 1:n0 + 1]) if new is not None else []):
        if axis == 'x':
            xw = mod
        elif axis == 'y':
            yw = mod
        elif axis == 'z':
            zrand = c + mod // 2                             # 높이도 무작위 — 가운데만 싣는다(칸이 없다)
            continue
        else:
            sure = False
            continue
        if c + mod // 2:
            sure = False
    # 지연
    dr = ds = 0
    for t, dva, _ in ev.get('calls', []):
        if t != emu.DELAY or dva not in at:
            continue
        nd = at[dva]
        for k in range(nd - 1, max(nd - 9, -1), -1):
            i = seq[k]
            if i.mnemonic == 'call':
                break
            if i.mnemonic == 'idiv':
                m = next((j.operands[1].imm for j in reversed(seq[max(0, k - 4):k]) if j.mnemonic == 'mov'
                          and j.operands[0].type == X86_OP_REG and j.operands[1].type == X86_OP_IMM), None)
                if m:
                    dr = m
                else:
                    sure = False
                break
            if i.mnemonic == 'lea':
                m = i.operands[1].mem
                if m.index and m.base == m.index and m.scale == 1 and m.disp == 0:
                    ds = 2
                elif m.index and not m.base and m.disp == 0:
                    ds = m.scale
                else:
                    sure = False
                break
        break
    ang = w2 = dz0 = dzk = 0
    w2m = dzm = False
    if move == 4:
        slots, ok = [], True
        for i in seq[n0 + 1:n1]:
            ops = i.operands
            if i.mnemonic == 'push':
                slots.append(ops[0].imm & 0xffffffff if ops[0].type == X86_OP_IMM else None)
            elif i.mnemonic == 'sub' and ops[0].type == X86_OP_REG and i.reg_name(ops[0].reg) == 'esp' and ops[1].imm == 8:
                slots += [None, None]
            elif i.mnemonic == 'call':
                ok = False
            elif i.mnemonic == 'fmul' and ops and ops[0].type == X86_OP_MEM and not ops[0].mem.base and not ops[0].mem.index:
                ang = int(round(struct.unpack_from('<d', loops.e.d, ops[0].mem.disp - BASE)[0] * 1000))
        arg = slots[::-1]
        if ok and len(arg) == 16:
            v = [dbl(arg[8], arg[9]), dbl(arg[10], arg[11]), dbl(arg[13], arg[14])]
            if None in v or arg[12] is None or arg[15] is None or max(abs(x) for x in v) > 1000:
                sure = False
            else:
                dz0, w2, dzk = (int(round(x * 1000)) for x in v)
                w2m, dzm = bool(arg[12]), bool(arg[15])
            if arg[3] is None and not ang:
                sure = False                                 # 처음 각을 셈하는데 걸음을 못 읽었다
        else:
            sure = False
    pz = pos(ev['z'], 'z')
    z = pz[1] if pz else None
    if zrand is not None:
        z, sure = zrand, False
    elif z is None and new is not None:
        z = static_z(seq[new + 1:n0])                        # 고리 안의 유닛(목록에서 꺼낸 것) 기준 — 실행기는 못 읽는다
    if z is None:
        z, sure = 0, False
    return (count, xw, yw, dr, ds, ang, w2, w2m, dz0, dzk, dzm, z, sure)


SPRAY_KIND = {0x100cb980: 1, 0x100cc680: 2, 0x100cc890: 3, 0x100cd510: 4, 0x100cc280: 5, 0x100cc470: 6, 0x100cbe00: 7, 0x100ccf60: 8}


def spray_of(site, ev):
    """뿌리개 값 (Kind, Count, PerTick, RangeX, RangeY, RangeZ, Life, P0, P1, Sure) 또는 None."""
    call = next(((t, cva, a) for t, cva, a in ev.get('calls', []) if t in SPRAY_KIND), None)
    if call is None:
        return None
    t, cva, a = call
    kind = SPRAY_KIND[t]
    loops, fstart = site
    seq = loops.insns(fstart)
    at = {i.address: n for n, i in enumerate(seq)}
    sure = ev['va'] in at and cva in at
    if sure:
        sure = not any(i.mnemonic == 'fstp' and i.operands[0].type == X86_OP_MEM and i.reg_name(i.operands[0].mem.base) == 'esp'
                       for i in seq[at[ev['va']] + 1:at[cva]])
    bad = []

    def num(v, hi=5000):
        if isinstance(v, int) and abs(s32(v)) <= hi:
            return s32(v)
        bad.append(1)
        return 0

    def rate(lo, hi_, mode):
        """배율 — 곱하기면 × 1000, 더하기면 −(× 1000) (0 = 등속)."""
        k = dbl(lo, hi_)
        if k is None or not isinstance(mode, int) or abs(k) > 100:
            bad.append(1)
            return 0
        v = int(round(k * 1000))
        return v if mode else -v

    count = per = rx = ry = rz = life = p0 = p1 = 0
    if kind in (2, 5):
        count, rx, ry, rz, p0 = num(a[2]), num(a[6]), num(a[7]), num(a[8]), num(a[9])
        p1 = rate(a[10], a[11], a[12])
    elif kind in (3, 6):
        count, rx, p0 = num(a[2]), num(a[6]), num(a[7])
        p1 = rate(a[8], a[9], a[10])
    elif kind == 4:
        count, per, rz, p0 = num(a[2]), 1, 60, num(a[3])
    elif kind == 8:
        count, rx, p0 = 36, num(a[4]), num(a[5])
        p1 = rate(a[6], a[7], a[8])
    elif kind == 1:
        per, rx, life = 4, num(a[3]), num(a[10])
        v, dz = dbl(a[6], a[7]), dbl(a[8], a[9])
        if v is None or dz is None or abs(v) > 1000 or abs(dz) > 1000:
            bad.append(1)
        else:
            p0, p1 = int(round(v * 1000)), int(round(dz * 1000))
    else:
        per = 4
        bad.append(1)
    return (kind, count, per, rx, ry, rz, life, p0, p1, bool(sure and not bad))


def rise_read(site, ev, cva, note):
    """떠오름의 높이 T 가 `rand % N + C` 일 때 (가운데 C + N/2, 폭 N). 미는 차례는 (처음 쪽, T, H, W) — rand 셋 가운데 첫째가 T 다."""
    loops, fstart = site if site else (None, None)
    seq = loops.insns(fstart) if loops else []
    at = {i.address: n for n, i in enumerate(seq)}
    if ev['va'] in at and cva in at:
        rs = rands(seq[at[ev['va']] + 1:at[cva]])
        if len(rs) == 3 and rs[0][2] is None and abs(rs[0][1]) < 2000:
            return rs[0][1] + rs[0][0] // 2, rs[0][0]
    note.append('떠오름 높이 못 읽음')
    return 0, 0


# ---------------------------------------------------------------- 한 이펙트의 덧정보
def extra_of(ev, d, on_target, per, stats, site=None):
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
    setup = None
    for t, cva, a in calls:
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
            setup = (cva, a)
            k = dbl(a[3], a[4])
            hi = s32(a[2]) if isinstance(a[2], int) and abs(s32(a[2])) < 2000 else 0
            if not isinstance(a[2], int):
                hi, lo = rise_read(site, ev, cva, note)
            scale = int(round(k * 1000)) if k is not None and 0 < k < 100 else 0
        elif t == DROP and move == 0:
            move = 3
            k = dbl(a[6], a[7])
            scale = int(round(k * 1000)) if k is not None and 0 < k < 100 else 0
            speed, lo, mode, sx, sy, cx, cy = drop_read(site, ev, cva, a, note)
            if pz is not None and abs(pz[1]) < 5000:
                hi = pz[1]                                   # 시작 높이 — 기준 유닛의 z 위로
            else:
                note.append('떨굼 시작 높이 못 읽음(코드가 셈한 자리)')
            if sx or sy:
                if on_target:
                    note.append('떨굼 시작 자리 폭 (%d, %d) 은 시전자 기준이라 못 실음' % (sx, sy))
                else:
                    frm, to = sx, sy
                    dx, dy = dx + cx, dy + screen_dy(cy)
        elif t == RING and move == 0:
            move = 4
            setup = (cva, a)
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
    if setup and site:
        sw = swarm_read(site, ev, setup[0], setup[1], move)
        if sw:
            SWARM[ev['va']] = sw
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
    sprays = build.sprays = collections.OrderedDict()        # work → [(Obs, 모션, Delay, 뿌리개 값…)]
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
                if ev.get('kind') == 'emit' and isinstance(ev['obs'], int):
                    spath = ast.literal_eval(ev['path']) if isinstance(ev['path'], str) else ev['path']
                    sp = spray_of((loops, spath[-1] if spath and isinstance(spath[-1], int) else r['handler']), ev)
                    if sp:
                        mo = ev['motion'] & 0xffff if isinstance(ev['motion'], int) else None
                        cand = [y for y in effs if y[0] == ev['obs'] & 0xffff and (mo is None or y[1] == mo) and y[8] in (-1, d)]
                        at_ = [y for y in cand if y[4] == tm.span(n)[0]]
                        if not at_ and len({y[4] for y in cand}) == 1:
                            at_ = cand
                        if at_:
                            row = (at_[0][0], at_[0][1], at_[0][4]) + sp
                            if row not in sprays.setdefault(w, []):
                                sprays[w].append(row)
                        else:
                            stats['뿌리개 — 표 줄과 못 맞춤'] += 1
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
                x = extra_of(ev, d, effs[k][2], per, stats, (loops, fstart))
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
         '    /// 떠오름: MaxSpeed = 높이 T(월드 z — 화면은 × 0.6, 음수 = 내려옴), ScalePermille = 빠르기 × 1000(틱마다 길 위의 점 수 ≈ 월드 px),',
         '    /// MinSpeed = T 무작위 폭 N(T = MaxSpeed − N/2 + rand % N, 0 = 고정). 길(0x10038da0): 반폭 rand % 3 + 1 · 마디 높이 rand % 20 + 25 의 반타원을',
         '    /// 좌우로 번갈아 타며 오른다(x 와 z 만 바뀐다), |z − 처음 z| ≥ |T| 면 끝.',
         '    /// 떨굼(튀는 조각, 값은 모두 월드 단위 — 화면은 x 그대로 · y × 0.8 · z × 0.6): ScalePermille = 튕김 계수 × 1000(중력은 원본 상수라 칸이 없다),',
         '    /// MaxSpeed = 시작 높이 z(기준 자리 위로 — 표의 Lift 가 이미 이 값 × 0.6 이니 두 번 더하지 않는다), Speed = 처음 vz × 1000(위가 +, 무작위면 가장 작은 값),',
         '    /// MinSpeed = vz 무작위 폭 N(vz = Speed/1000 + rand % N, 0 = 고정), Mode = 수평 속도 무작위 폭 N(vx, vy = rand % N − N/2, 0 = 없음),',
         '    /// From · To = 시작 자리 x · y 무작위 폭 N(자리 = 기준 + rand % N − N/2, 0 = 없음 — 떨굼에서는 From/To 가 아래 뜻이 아니다).',
         '    /// 틱(0x10038fa0): x += vx/2, y += vy/2, 새vz = vz − 5, z += (vz + 새vz)/4, z ≤ 0(월드 0)이면 z = −z · vz = −새vz × 튕김.',
         '    /// |vz| &lt; 5 일 때 z 가 땅 높이 ± 5 안이거나 땅 아래·맵 밖이면 끝, 수명이 차도 끝(0x100c2360).',
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


def swarm_rows(out):
    """work → [(Obs, 모션, Delay, 떼 값…)] — 고리 밖이고 값이 모두 0 인 것은 뺀다."""
    res = collections.OrderedDict()
    for w, rows in out.items():
        got = []
        for k, b, _, va in rows:
            sw = SWARM.get(va)
            if b[6] not in (2, 4) or sw is None:
                continue
            if sw[0] == 0 or (sw[0] == 1 and not any(sw[1:11])):
                continue                                     # 횟수를 못 읽은 고리는 싣지 않는다
            r = (k[0], k[1], k[2]) + sw
            if r not in got:
                got.append(r)
        if got:
            res[w] = got
    return res


def write_swarm(path, sw, res, names):
    L = ['// <auto-generated> tools/re/work_fx_extra2.py 가 만든다 — 손으로 고치지 않는다.',
         'namespace DuelDx;',
         '',
         '/// <summary>떠오름·고리 이펙트(WorkFxExtra 의 Move 2 · 4)를 원본이 되돌이 고리로 뿌릴 때 낱알마다의 값 — WorkFxExtra.Row 와 (Obs, 모션, Delay) 로 맞춘다.</summary>',
         '/// <remarks>',
         '/// 원본 핸들러는 이 줄들을 한 장이 아니라 고리 안에서 Count 번 만든다(생성자 0x100c5b20 떠오름 · 0x100cd270 고리).',
         '/// 낱알 i (0 부터)의 시작 자리 = 기준 + (rand % XWidth − XWidth/2, rand % YWidth − YWidth/2) — 월드 단위(화면 y 는 × 0.8), 폭 0 = 안 흩어짐.',
         '/// 지연 = 표의 Delay + rand % DelayRandom + i × DelayStep 틱(0x100c24d0).',
         '/// 고리(0x100cd310 의 뒤쪽 인자, 점 만들기 0x10039410 · 틱 0x10039330): 처음 각 = i × AngleStepPermille/1000 (π 단위),',
         '/// 틱마다 각속도 W = W2Multiply ? W × W2Permille/1000 : W + W2Permille/1000 을 먼저 바꾼 뒤 각 += W (W 의 처음 값은 WorkFxExtra 의 ScalePermille),',
         '/// 높이 빠르기 dz = DzMultiply ? dz × DzKPermille/1000 : dz + DzKPermille/1000 을 먼저 바꾼 뒤 z += dz (dz 의 처음 값 Dz0Permille/1000, 월드 z — 화면은 × 0.6).',
         '/// StartZ = 생성자 z 의 치우침(월드) — 이펙트 표의 Lift 가 이미 이 값 × 0.6 이니 두 번 더하지 않는다.',
         '/// 떠오름 낱알의 좌우 흔들림(반폭 rand % 3 + 1 · 마디 높이 rand % 20 + 25 · 처음 쪽 rand &amp; 1)은 본 곳이 모두 같아 칸이 없다.',
         '/// Sure = false 는 못 읽은 값(0 으로 둠)이 있다는 뜻이다(시작 높이가 레지스터 값이거나 무작위 — 무작위면 StartZ 는 가운데 값). 고리 횟수를 못 읽은 줄은 안 실었다.',
         '/// 줄 끝 주석: 이름 · 핸들러 주소.',
         '/// </remarks>',
         'internal static class WorkFxSwarm',
         '{',
         '    public readonly record struct Row(int Obs, int Motion, int Delay, int Count, int XWidth, int YWidth, int DelayRandom, int DelayStep,',
         '                                      int AngleStepPermille, int W2Permille, bool W2Multiply, int Dz0Permille, int DzKPermille, bool DzMultiply,',
         '                                      int StartZ, bool Sure);',
         '',
         '    public static readonly Dictionary<int, Row[]> Table = new()',
         '    {']

    def one(r):
        return 'new(%s)' % ', '.join(('true' if v else 'false') if isinstance(v, bool) else '%d' % v for v in r)

    for w, rows in sw.items():
        nm = ' '.join((names.get(w) or '').split())
        L.append('        [%d] = [%s],   // %s · 0x%x' % (w, ', '.join(one(r) for r in rows), nm, res[w]['handler']))
    L += ['    };', '}', '']
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(L))


def write_spray(path, sp, res, names):
    L = ['// <auto-generated> tools/re/work_fx_extra2.py 가 만든다 — 손으로 고치지 않는다.',
         'namespace DuelDx;',
         '',
         '/// <summary>뿌리개 이펙트(그림 없는 개체가 낱알을 뿌린다)의 값 — 이펙트 표(AbilityScripts.g.cs)의 줄과 (Obs, 모션, Delay) 로 맞춘다.</summary>',
         '/// <remarks>',
         '/// 단위는 모두 월드(화면 x 그대로 · y × 0.8 − z × 0.6). 낱알은 그 줄의 (Obs, 모션)이고 수명은 Life(0 = 모션 길이 − 1).',
         '/// Kind (생성자 + 설정 주소):',
         '/// 1 0x100cb8a0 + 0x100cb980 — 뿌리개가 반지름 RangeX 의 원을 돌며(틱당 P0/1000 점) 틱마다 P1/1000 씩 오른다(이동기 0x10038940).',
         '///   Life = 이동기가 도는 틱 수 — 뿌리개 이펙트의 수명(이펙트 표의 Life, 핸들러가 0x100c2530 으로 준다)이 먼저 끝나면 거기서 끝난다.',
         '///   틱마다 제자리에 한 장 + 떠오름 낱알 셋(PerTick 4, 낱알은 둘째 Obs 로 처음 높이까지 내려간다 — 둘째 Obs 는 표에 없다). Count 0 = 끝날 때까지.',
         '/// 2 0x100cc600 + 0x100cc680 — Count 장을 한꺼번에: 가운데에서 상자 ±(RangeX, RangeY, RangeZ) 안의 무작위 점(rand % 2R − R)으로 직선탄.',
         '/// 3 0x100cc810 + 0x100cc890 — Count 장을 한꺼번에: i 번째가 각 i × 2/Count (π 단위)로 반지름 10 에서 RangeX 까지 직선탄.',
         '/// 4 0x100cd490 + 0x100cd510 — (P0 + 1)틱마다 한 장, n 번째는 z + RangeZ × n (기둥). Count 장을 내면 끝.',
         '/// 5 0x100cc200 + 0x100cc280 — 2 의 거꾸로: 상자 ±(RangeX, RangeY, RangeZ) 의 무작위 점에서 가운데로 직선탄.',
         '/// 6 0x100cc3f0 + 0x100cc470 — 각 rand % 400 × 0.005(π 단위) · 반지름 rand % RangeX 의 점에서 가운데로 직선탄, 낱알마다 지연 rand % 30.',
         '/// 7 0x100cbd20 + 0x100cbe00 — 1 과 같은 틱(낱알은 땅 높이까지 내려간다)이지만 이동기(0x10038650)의 길을 못 읽었다 — Sure = false.',
         '/// 8 0x100ccee0 + 0x100ccf60 — 늘 36장: i 번째가 각 (i − 9)/18 (π 단위)로 반지름 10 에서 RangeX 까지, 모션 = 방향 번호(i, 18 부터는 36 − i 를 뒤집어).',
         '/// 2·3·5·6·8 의 P0 = 직선탄 빠르기(틱당 px), P1 = 틱마다 빠르기 바꿈: 양수 = × P1/1000, 음수 = + |P1|/1000, 0 = 등속. 닿으면 사라진다.',
         '/// 2·3·5·8 의 낱알은 뿌리개가 뜬 다음 틱에 한꺼번에 떠난다(PerTick 0). 가운데 = 그 줄의 자리(표의 OnTarget · Lift).',
         '/// Sure = false: 못 읽은 값이 있다(0 으로 둠).',
         '/// 줄 끝 주석: 이름 · 핸들러 주소.',
         '/// </remarks>',
         'internal static class WorkFxSpray',
         '{',
         '    public readonly record struct Row(int Obs, int Motion, int Delay, int Kind, int Count, int PerTick, int RangeX, int RangeY, int RangeZ,',
         '                                      int Life, int P0, int P1, bool Sure);',
         '',
         '    public static readonly Dictionary<int, Row[]> Table = new()',
         '    {']

    def one(r):
        return 'new(%s)' % ', '.join(('true' if v else 'false') if isinstance(v, bool) else '%d' % v for v in r)

    for w, rows in sp.items():
        nm = ' '.join((names.get(w) or '').split())
        L.append('        [%d] = [%s],   // %s · 0x%x' % (w, ', '.join(one(r) for r in rows), nm, res[w]['handler']))
    L += ['    };', '}', '']
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
    ap.add_argument('--swarm', default=os.path.join(HERE, '..', '..', 'duel-dx', 'Ability', 'WorkFxSwarm.g.cs'))
    ap.add_argument('--spray', default=os.path.join(HERE, '..', '..', 'duel-dx', 'Ability', 'WorkFxSpray.g.cs'))
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
    sw = swarm_rows(out)
    print('떼 표: work %d개 · 줄 %d개 · 불확실 %d줄' % (len(sw), sum(len(v) for v in sw.values()),
                                              sum(1 for v in sw.values() for r in v if not r[-1])))
    if not a.dry:
        write(a.out, out, res, names, arrive)
        print('%s 를 적었다' % os.path.relpath(a.out, os.path.join(HERE, '..', '..')))
        write_swarm(a.swarm, sw, res, names)
        write_spray(a.spray, build.sprays, res, names)
        print('%s 를 적었다 (work %d개 · 줄 %d개)' % (os.path.relpath(a.spray, os.path.join(HERE, '..', '..')), len(build.sprays),
                                              sum(len(v) for v in build.sprays.values())))
        print('%s 를 적었다' % os.path.relpath(a.swarm, os.path.join(HERE, '..', '..')))


if __name__ == '__main__':
    main()

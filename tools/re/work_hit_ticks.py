"""work(기술) 핸들러가 대상에게 판정 메시지 1001 을 보내기까지의 틱 수를 뽑아 `duel-dx/WorkHitTicks.g.cs` 로 적는다 (ba-21 T1).

쓰는 법
    python tools/re/work_hit_ticks.py "<게임 폴더>"                 # duel-dx/WorkHitTicks.g.cs 를 다시 만든다
    python tools/re/work_hit_ticks.py "<게임 폴더>" --out 다른.cs    # 다른 곳에 적는다
    python tools/re/work_hit_ticks.py "<게임 폴더>" --dump 435 1589  # 그 work 의 단계별 셈을 찍기만 한다(파일 안 씀)
    python tools/re/work_hit_ticks.py "<게임 폴더>" --tsv 표.tsv     # 전체 work 의 단계별 셈을 TSV 로도 남긴다

무엇을 세나
    원본 work 핸들러는 단계 기계다(유닛 +0x94 = 단계, +0x96 = 틱). 리메이크가 「이펙트를 띄우는 틀」로 삼는 때는
    핸들러 단계 0 이 처음 불리는 틱이다. 거기서부터 첫 1001(`0x10001f40(대상, 0x3e9, …)`)까지 지난 틱을 센다.
    준비 동작(work +0x3f = 1~7 의 준비 핸들러)은 넣지 않는다 — 리메이크가 따로 재생한다.

단계 길이 규칙 (근거 주소는 G3PartII.dll, ImageBase 0x10000000)
    · 틱 세는 단계 — 핸들러가 `inc word [나+0x96]` 뒤 `cmp [나+0x96], N` 으로 넘어간다(예: 어스퀘이크 0x100ada4a·0x100ada83).
      넘어가는 호출에 들어설 때의 틱 값을 그 단계 길이로 센다(`cmp …,N / jbe` 면 N).
    · 끝 알림 기다림 — `inc` 없이 `cmp [나+0x96], 0` 으로 기다린다(예: 브레인 스톰 0x10095b37). 틱을 올리는 것은
      `0x100c2350(1)`(이펙트 +0x8c = 1)을 건 이펙트의 소멸자다(`0x100c209d~0x100c20aa: inc word [주인+0x96]`).
      길이 = 시작 지연(`0x100c24d0`) + 수명(`0x100c2530(n)`; n = 0 이면 Obs 모션 길이 − 1, `0x100c253a~0x100c2551`).
      이펙트가 앞 단계 도중에 생겼으면 그만큼 뺀다. 수명을 안 걸었거나(기본 0xffff = 이동기가 끝날 때까지,
      `0x100c23f8`) Obs 이펙트가 아니면(영상 `0x100d0770` 따위) 길이를 모른다.
    · 단계 전이 `0x100e82e0`(단계 + 1, 틱 = 0) 과 `[나+0x94]` 쓰기를 따라간다.
    · 길이를 정적으로 알 수 없는 단계 — 카메라 대기(`0x1006e850`), 탄 날기, 다른 유닛·동작이 끝나기를 기다리는 것 —
      는 0 으로 세고 그 work 을 Sure = false 로 적는다. 「모르는 조건」은 이렇게 가린다: 그 틱의 실행에서 핸들러 들머리부터
      전이·판정·끝(`0x100762d0`)을 **안 거치고** ret 에 닿는 길이 있으면 조건이 걸린 것이다.
    · `0x10030600(Obs, 모션)`(그 모션이 있나)은 늘 참으로 본다. 시전자 애니(`[나+0x58]` 의 모션 번호·컷)를 보는 갈래는
      「동작」(제 동작이 끝나기를 기다림), 카메라 갈래는 「카메라」, 나머지는 「조건」으로 줄 끝 주석에 적는다.
    · 화면 가득 영상(`0x100d05c0` + `0x100d0700(파일)`, 영상 이펙트 `0x100d0770` + `0x100d0970`)이 끝 알림이면 길이를
      0 으로 세고 Sure = false 다. Bink 를 실시간으로 돌리므로(`0x10025a22` BinkWait) 주석에 「≈N틱」(프레임 ÷ fps × 30)만 적는다.
    · 판정이 이펙트 슬롯으로 가는 work(핸들러에 1001 이 없고 이펙트가 판정한다): 슬롯을 거는 틱 + 슬롯 종류별 지연.
        `0x100c2950`(+0xac) = 이펙트 시작 지연 + 1틱(나이 1 에 판정, `0x100c2e93`)
        `0x100c29b0`(+0xcc) = 이펙트가 사라질 때(지연 + 수명, 소멸자 `0x100c2079`)
        `0x100c28f0`(+0xec) = 나이 1 + 칸을 옮길 때마다 — 서 있는 Obs 이펙트면 지연 + 1, 날아가는 탄이면 모름(Sure = false 「슬롯」)
        `0x100c2a10`·`0x100c2860`(+0x10c) = 주기 판정 — 모름(Sure = false 「슬롯」)
    · 값이 0 인 work(몸짓 타격 키로 치거나 첫 단계에서 바로 판정 — 전이 틱 +1 과 슬롯의 +1 만 있는 것 포함)은 표에 넣지 않는다.
    · 단계를 넘기는 호출이 다음 단계 코드까지 이어 돌지 않으면(대부분) 전이마다 1틱이 더 든다 — 주석의 「+1」.

한계
    · 끝 알림이 여럿이면 가장 먼저 끝나는 것으로 센다. 값을 못 푼 지연(rand 따위)은 0 으로 보고 Sure = false(「지연 모름」).
    · 방향 1(옆) 한 가지로만 돈다(방향 0·2·3 으로 돌려도 표가 같음을 확인했다). 고리는 한 바퀴만 돌므로 고리마다 달라지는 지연은 첫 값이다.
    · 기호 실행은 `work_fx_emu.Emu` 를 그대로 쓰고(고치지 않음) 여기서 `step` 만 덧씌운다.
근거·결과: 옵시디안 분석/원본차이/ba21-target-actions.md 「T1 표 생성 기록」
"""
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import work_script as ws                                    # noqa: E402
import work_fx_emu as emu                                   # noqa: E402
import work_fx_extra as wx                                   # noqa: E402
from capstone.x86 import X86_OP_IMM, X86_OP_MEM             # noqa: E402

NOTIFY = 0x100c2350        # (1) → 이펙트 +0x8c: 끝날 때 주인 +0x96 을 올린다
CAMBUSY = 0x1006e850       # 카메라가 아직 움직이는가
HASMOTION = 0x10030600     # (Obs, 모션) 그 모션이 있나 — 자료가 있으면 늘 참
LIFELOOPS = 0x100c2570     # (n) 수명 = 모션 길이 × n
SPRINTF = 0x1012823e
VIDEO_CTOR, VIDEO_OPEN = 0x100d05c0, 0x100d0700          # 화면 가득 영상(Mov\NNNN.mov) — 영상이 끝나면 사라진다(0x100d06a0)
MOVER = 0x100c3490         # 이동기(목표 x, z, 빠르기, …) — 탄 날기
SLOT_START = 0x100c2950    # +0xac: 나이 1 틱에 그 자리 판정(0x100c2e93~0x100c2ea5)
SLOT_CELL = 0x100c28f0     # +0xec: 나이 1 틱 + 칸을 옮길 때마다(0x100c2eaa~)
SLOT_END = 0x100c29b0      # +0xcc: 사라질 때(소멸자 0x100c2079~0x100c2087)
GOALS = ('stage', 'end', 'hit', 'slot')
MAXDEPTH = 24
TICKS_PER_SECOND = 30      # 영상 길이를 틱으로 어림할 때만 쓴다(주석용)


class HEmu(emu.Emu):
    """Emu + 틱 비교 상수(inc 뒤 꼴)·끝 알림·흐름 그래프 기록."""

    def step(self, s, i, work, ctx, depth, path):
        top = path == ()
        m, ops = i.mnemonic, i.operands
        n0, e0 = len(work), len(ctx['events'])
        tgt = ops[0].imm if m == 'call' and ops[0].type == X86_OP_IMM else None
        if top:
            last = s.mem.get('_last')
            if last is not None and last[1] != i.address:        # run() 의 고리 빠져나가기 — 이음매를 잇는다
                ctx.setdefault('cfg', {}).setdefault(last[0], set()).add(i.address)
        if top and s.fld.get(0x94) != ctx['stage'] and any(
                o.type == X86_OP_MEM and self.addr(s, i, o) == ('나', 0x94) for o in ops) and m != 'lea':
            ctx['fall'] = True                                   # 전이한 그 호출이 다음 단계 코드까지 이어 돈다
        if m in ('cmp', 'test'):
            a, b = self.rd(s, i, ops[0], ctx), self.rd(s, i, ops[1], ctx)
            kind = s.mem.get('_ret') if (ops[0].type != X86_OP_MEM and ops[1].type != X86_OP_MEM) else None
            for x, y in ((a, b), (b, a)):
                if isinstance(x, emu.W) and isinstance(y, int) and not isinstance(y, emu.W):
                    ctx['widcmp'] = True
                if m == 'cmp' and isinstance(x, str) and x.startswith('나.틱+') and isinstance(y, int):
                    try:
                        ctx['tickc'].add((y & 0xffff) - int(x[4:]))
                    except ValueError:
                        pass
                if isinstance(x, str) and x.startswith('나.애니'):
                    kind = '동작'                                # 시전자 애니(지금 모션 번호·컷)를 본다 — 제 동작이 끝나기를 기다림
            s.mem['_flag'] = kind
        elif m in ('inc', 'add') and ops and ops[0].type == X86_OP_MEM:
            if self.addr(s, i, ops[0]) == ('나', 0x96):
                ctx['inc'] = True
        elif tgt is not None:
            sp = s.reg['esp']
            arg = [s.mem.get(sp[1] + 4 * k) for k in range(2)] if isinstance(sp, tuple) else [None, None]
            this = s.reg['ecx']
            fx = ctx['fxs'][int(this[3:])] if isinstance(this, str) and this.startswith('fx#') else None
            if tgt == NOTIFY:
                if arg[0] != 0:
                    if fx is not None:
                        ctx.setdefault('notify', {})[fx['idx']] = True
                    elif ctx['fxs'] and this is None:
                        ctx.setdefault('notify', {})[len(ctx['fxs']) - 1] = False      # 어느 이펙트인지 가설
            elif tgt == LIFELOOPS and fx is not None:
                fx['loops'] = arg[0] if isinstance(arg[0], int) else '?'
            elif tgt == SPRINTF and isinstance(arg[1], int):
                s.mem['_str'] = self.dll.cstr(arg[1])
            elif tgt in (VIDEO_OPEN, wx.MOV_OPEN) and fx is not None:
                fx['file'] = s.mem.get('_str')
        nxt = emu.Emu.step(self, s, i, work, ctx, depth, path)
        if tgt is not None:
            if tgt == HASMOTION:
                s.reg['eax'] = 1
            s.mem['_ret'] = '카메라' if tgt == CAMBUSY else None    # 바로 뒤 test eax,eax 의 갈래에 이름을 붙인다
        if (m.startswith('j') and m != 'jmp' and ops[0].type == X86_OP_IMM and nxt == ops[0].imm
                and nxt < i.address and len(work) == n0):
            nxt = i.address + i.size                             # 값이 정해진 뒤로 뛰기 — 고리는 한 바퀴만 돌고 나간다
        if top:
            succ = ctx.setdefault('cfg', {}).setdefault(i.address, set())
            if nxt is not None:
                succ.add(nxt)
            elif m.startswith('ret'):
                succ.add('RET')
            for c in work[n0:]:
                succ.add(c.va)
                c.mem['_last'] = (i.address, c.va)
            if len(work) > n0 and m.startswith('j') and m != 'jmp' and s.mem.get('_flag'):
                ctx.setdefault('forkkind', {})[i.address] = s.mem['_flag']
            if any(ev['k'] in GOALS for ev in ctx['events'][e0:]):
                ctx.setdefault('goal', set()).add(i.address)
            s.mem['_last'] = (i.address, nxt)
        return nxt


def conditions(ctx, start):
    """들머리에서 전이·판정·끝을 안 거치고 ret 에 닿는 길이 있으면 — 그 길을 가르는 갈래들의 이름(카메라·동작·조건)."""
    cfg, goal, kinds = ctx.get('cfg', {}), ctx.get('goal', set()), ctx.get('forkkind', {})
    drop, out = set(), []
    for _ in range(8):
        prev, todo, path = {start: None}, [start], None
        while todo and path is None:
            va = todo.pop(0)
            if va in goal:
                continue
            for n in cfg.get(va, ()):
                if (va, n) in drop:
                    continue
                if n == 'RET':
                    path, x = ['RET'], va
                    while x is not None:
                        path.append(x)
                        x = prev[x]
                    path.reverse()
                    break
                if n not in prev:
                    prev[n] = va
                    todo.append(n)
        if path is None:
            return out
        can = set(goal)                                          # 전이·판정에 닿을 수 있는 자리
        grew = True
        while grew:
            grew = False
            for va, succ in cfg.items():
                if va not in can and any(n in can and (va, n) not in drop for n in succ):
                    can.add(va)
                    grew = True
        culprit = None
        for k in range(len(path) - 2, -1, -1):                   # 길 위의 마지막 「다른 쪽으로 갔으면 닿았을」 갈래
            va, on = path[k], path[k + 1]
            if any(n != on and n in can for n in cfg.get(va, ())):
                culprit = (va, on)
                break
        if culprit is None:
            out.append('조건')
            return out
        drop.add(culprit)
        out.append(kinds.get(culprit[0]) or '조건')
    return out


class Stage:
    __slots__ = ('st', 'T', 'to', 'end', 'inc', 'cond', 'hit', 'fx', 'empty', 'fall')


class Tool:
    def __init__(self, game):
        self.e = HEmu(game)
        self.g = self.e.g
        self.cache = {}
        self.obs = {}
        self.movs = {}
        self.direction = 1                                       # 옆(1) — 다른 방향으로 돌려 보려면 바꾸고 cache 를 비운다

    def motion_len(self, obs, motion):
        if obs not in self.obs:
            try:
                self.obs[obs] = ws.load_obs_motions(self.g, obs)
            except Exception:
                self.obs[obs] = None
        m = self.obs[obs]
        if not m or motion not in m:
            return None
        return m[motion]['len']

    def video_ticks(self, name):
        """Bink 머리(프레임 수·fps)로 어림한 영상 길이(틱) — 실시간 재생(BinkWait 0x10025a22)이라 주석에만 쓴다."""
        if not name or '%' in name or '\\' not in name:
            return None
        if name not in self.movs:
            self.movs[name] = None
            try:
                folder, fn = name.split('\\', 1)
                b = self.g.read(folder, fn)
                if b and b[:3] == b'BIK':
                    frames, = struct.unpack_from('<I', b, 8)
                    num, den = struct.unpack_from('<2I', b, 28)
                    if num:
                        self.movs[name] = (frames * den * TICKS_PER_SECOND + num - 1) // num
            except Exception:
                pass
        return self.movs[name]

    def fx_info(self, fx):
        """끝 알림 이펙트 → (생긴 때부터 사라질 때까지의 틱 또는 None, 이름, 흠)."""
        delay, life = fx['delay'], fx['life']
        obs = fx['kind'] == 'obs' and isinstance(fx['obs'], int) and isinstance(fx['motion'], int)
        name = '%s:%s' % (fx['obs'], fx['motion']) if obs else {'mov': '영상', 'emit': '뿌리개', 'code': '개체',
                                                                'body:self': '몸 그림', 'body:target': '몸 그림'}.get(
            fx['kind'], fx['kind'])
        if fx['ctor'] in (VIDEO_CTOR, wx.MOV_CTOR):
            f = fx.get('file')
            n = self.video_ticks(f)
            return None, '영상 %s%s' % (f or '?', '≈%d틱' % n if n else ''), '영상'
        flaw = ''
        if delay == '?':                                         # 값을 못 푼 지연(rand 따위) — 0 으로 본다
            delay, flaw = 0, '지연 모름'
        delay = delay or 0
        mlen = self.motion_len(fx['obs'], fx['motion']) if obs else None
        loops = fx.get('loops')
        if isinstance(loops, int) and loops > 0 and mlen and life is None:
            return delay + mlen * loops, name + '×%d' % loops, flaw
        if life == 0 and mlen:
            return delay + mlen - 1, name, flaw
        if isinstance(life, int) and 0 < life < 0x8000:
            return delay + life, name, flaw
        return None, name, '알림 길이 모름'                       # 수명을 안 걸었다 = 이동기(탄 날기)가 끝날 때까지

    def slot_info(self, cc, ev):
        """이펙트 슬롯 판정 → (슬롯을 건 때부터 첫 판정까지의 틱, 흠)."""
        this = ev.get('this')
        if not (isinstance(this, str) and this.startswith('fx#')):
            return 0, '슬롯'
        fx = cc['fxs'][int(this[3:])]
        delay = fx['delay'] if isinstance(fx['delay'], int) else 0
        flaw = '지연 모름' if fx['delay'] == '?' else ''
        calls = [x[0] for x in [fx.get('setter')] + fx.get('more', []) if x]
        still = fx['ctor'] == wx.BASE_OBS and MOVER not in calls
        f = ev['f']
        if f == SLOT_START:
            return delay + 1, flaw
        if f == SLOT_END:
            n, _, bad = self.fx_info(fx)
            return (delay, '슬롯') if n is None else (n, bad)
        if f == SLOT_CELL and still:
            return delay + 1, flaw
        return delay + 1, '슬롯'                                 # 날아가는 탄(칸마다)·주기 판정 — 닿는 때를 모른다

    # ------------------------------------------------------------------
    def stage(self, h, wid, st):
        w = self.e.works[wid]
        d = self.direction
        for wr, dep, owner, res in self.cache.get((h, st), ()):
            if (not dep or owner == wid) and all(w.get(f) == v for f, v in wr.items()):
                return res
        e = self.e
        c = e.run(h, wid, st, None, d)
        wread, dep = dict(c['wread']), bool(c.get('widcmp'))
        ts = sorted({0, 1} | {t for x in c['tickc'] for t in (x - 1, x, x + 1) if 0 <= t < 5000})
        r = Stage()
        r.st, r.T, r.to, r.end, r.inc, r.cond, r.hit, r.fx = st, None, [], False, bool(c.get('inc')), None, None, []
        r.fall = False
        r.empty = not any(ev['k'] in ('act', 'mot', 'fx') + GOALS for ev in c['events'])
        seenfx = set()
        for t in ts:
            cc = e.run(h, wid, st, t, d)
            wread.update(cc['wread'])
            dep = dep or bool(cc.get('widcmp'))
            evs = cc['events']
            for k, sure in sorted(cc.get('notify', {}).items()):
                fx = cc['fxs'][k]
                key = (fx['va'], fx['path'])
                if key not in seenfx and (r.T is None or t <= r.T):
                    seenfx.add(key)
                    n, name, flaw = self.fx_info(fx)
                    r.fx.append((t, n, name, flaw or ('' if sure else '알림 길이 모름')))
            if r.hit is None:
                best = None
                for ev in evs:
                    if ev['k'] in ('hit', 'slot'):
                        extra, flaw = (0, '') if ev['k'] == 'hit' else self.slot_info(cc, ev)
                        if best is None or extra < best[3]:
                            best = (t, ev['k'], ev['va'], extra, flaw)
                r.hit = best
            if r.T is None and any(ev['k'] in ('stage', 'end') for ev in evs):
                r.T = t
                r.to = sorted({ev['to'] for ev in evs if ev['k'] == 'stage' and isinstance(ev['to'], int)
                               and ev['to'] != st})
                if any(ev['k'] == 'stage' and not isinstance(ev['to'], int) for ev in evs):
                    r.to.append(None)
                r.end = any(ev['k'] == 'end' for ev in evs)
                r.fall = bool(cc.get('fall'))
            if (r.T == t or (r.hit is not None and r.hit[0] == t)) and r.cond is None:
                r.cond = list(dict.fromkeys(conditions(cc, h)))
        self.cache.setdefault((h, st), []).append((wread, dep, wid, r))
        return r

    # ------------------------------------------------------------------
    def walk(self, h, wid):
        """(틱, 확실한가, 카메라만 모르나, 판정 종류, 단계별 설명) — 판정을 못 찾으면 None."""
        budget = [200]

        def go(st, now, real, pend, seen, depth):
            # now = 단계 0 부터 지난 틱, real = 그중 단계 길이로 센 것(전이 틱 +1 은 뺌), pend = 아직 안 끝난
            # 끝 알림 이펙트 [(끝나는 틱 또는 None, 이름, 흠)]
            if depth > MAXDEPTH or st in seen or budget[0] <= 0 or not isinstance(st, int) or st > emu.MAXSTAGE:
                return None
            budget[0] -= 1
            r = self.stage(h, wid, st)
            # 끝 알림을 기다리는 단계(inc 없음)에서 틱 1 에 생긴 이펙트는 「알림이 온 뒤」에 생긴 것 — 기다림을 센 다음에 넣는다
            late = [f for f in r.fx if f[0] >= 1 and not r.inc]
            pend = pend + [(None if n is None else now + t + n, name, flaw) for t, n, name, flaw in r.fx
                           if not (t >= 1 and not r.inc)]

            def wait():
                """끝 알림 기다림 — (길이, 문제, 설명)."""
                cand = [p for p in pend]
                if not cand:
                    return 0, '알림 없음', '끝?'
                known = [p for p in cand if p[0] is not None]
                bad = next((p[2] for p in cand if p[2]), '')
                if not known:
                    return 0, bad, '끝(%s ?)' % cand[-1][1]
                end, name, _ = min(known, key=lambda p: p[0])
                return max(0, end - now), bad, '끝(%s=%d)' % (name, max(0, end - now))

            def length(t):
                """이 단계에서 틱 t 의 호출까지 걸린 시간."""
                if t == 0:
                    return 0, '', '0'
                if r.inc:
                    return t, '', str(t)
                return wait()

            # 판정이 이 단계에서 가나(전이보다 늦지 않게)
            if r.hit is not None and (r.T is None or r.hit[0] <= r.T):
                n, bad, txt = length(r.hit[0])
                extra = r.hit[3]
                why = [x for x in [bad] + (r.cond or []) + [r.hit[4]] if x]
                return now + n + extra, real + n + max(0, extra - 1), why, r.hit[1], ['%d:%s→%s@0x%x' % (
                    st, txt, '1001' if r.hit[1] == 'hit' else '슬롯+%d' % extra, r.hit[2])]
            if r.T is None:
                if r.empty:
                    return None
                nxt, n, bad, txt = [st + 1], 0, '전이 못 봄', '?'
            else:
                if r.end and not r.to:
                    return None
                n, bad, txt = length(r.T)
                nxt = [x if x is not None else st + 1 for x in r.to]
                if None in r.to:
                    bad = bad or '다음 단계 모름'
            why = [x for x in [bad] + ((r.cond or []) if r.T is not None else []) if x]
            consumed = (not r.inc) and r.T
            after = [(None if fn is None else now + n + fn, name, flaw) for t, fn, name, flaw in late]
            best = None
            for x in sorted(set(nxt)):
                sub = go(x, now + n + (0 if r.fall else 1), real + n, after if consumed else pend + after, seen | {st}, depth + 1)
                if sub is None:
                    continue
                if best is None:
                    best = sub
                elif sub[0] != best[0]:
                    why.append('갈래')
            if best is None:
                return None
            return best[0], best[1], why + best[2], best[3], ['%d:%s%s' % (st, txt, '' if r.fall else '+1')] + best[4]

        res = go(0, 0, 0, [], frozenset(), 0)
        if res is None:
            return None
        ticks, real, why, kind, parts = res
        if real <= 0:                                        # 단계 길이가 0 — 첫 틀에 바로 판정(전이 틱만 지남)
            ticks = 0
        why = list(dict.fromkeys(why))
        return ticks, not why, why == ['카메라'], kind, parts, why


def names(g, works):
    text = {}
    try:
        import extract_character as ec
        from skill_motion import load_abis
        abis = load_abis(g, works)
        for t in ec.parse_txr(g.read('TXR', 'Txr.dat')):
            text.setdefault(t['txr_id'], t['text'])
        return {wid: text.get(abis[w[0x4]]['name'], '') if w[0x4] in abis else '' for wid, w in works.items()}
    except Exception as ex:                                  # 이름은 주석용 — 못 읽어도 표는 만든다
        print('이름 읽기 실패:', ex, file=sys.stderr)
        return {}


def main():
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    args = sys.argv[1:]
    if not args:
        raise SystemExit(__doc__)
    game = args[0]
    out = os.path.normpath(os.path.join(HERE, '..', '..', 'duel-dx', 'WorkHitTicks.g.cs'))
    tsv, dump = None, None
    k = 1
    while k < len(args):
        if args[k] == '--out':
            out, k = args[k + 1], k + 2
        elif args[k] == '--tsv':
            tsv, k = args[k + 1], k + 2
        elif args[k] == '--dump':
            dump, k = [int(x) for x in args[k + 1:]], len(args)
        else:
            raise SystemExit(__doc__)
    tool = Tool(game)
    e = tool.e
    nm = names(tool.g, e.works)
    rows = []
    for wid in (dump or sorted(e.works)):
        if wid not in e.works:
            continue
        try:
            h = e.dll.handler(wid)
        except Exception:
            continue
        res = tool.walk(h, wid)
        if dump:
            print('work %d %s 핸들러 0x%x 준비 %s → %s' % (wid, nm.get(wid, ''), h, e.works[wid][0x3f], res))
        rows.append((wid, h, res))
    if dump:
        return
    if tsv:
        with open(tsv, 'w', encoding='utf-8') as f:
            for wid, h, res in rows:
                f.write('\t'.join(map(str, [wid, nm.get(wid, ''), hex(h), e.works[wid][0x3f]] +
                                      (list(res) if res else ['-']))) + '\n')
    keep = [(wid, h, res) for wid, h, res in rows if res is not None and res[0] > 0]
    lines = ['// <auto-generated> tools/re/work_hit_ticks.py 가 만든다 — 손으로 고치지 않는다.',
             'namespace DuelDx;',
             '',
             '/// <summary>work 번호 → 핸들러가 판정(1001)을 보내기까지의 틱(단계 0 부터). Sure 가 거짓이면 길이를 모르는 단계가 끼어 있다'
             '(그 단계는 0 으로 셌다).</summary>',
             '/// <remarks>준비 동작(work 표의 준비 종류 1~7)의 길이는 들어 있지 않다. 줄 끝 주석 = 이름 · 핸들러 · 「단계:길이」 차례'
             '(끝(Obs:모션) = 그 이펙트가 끝나기를 기다림) · 모르는 까닭.</remarks>',
             'internal static class WorkHitTicks',
             '{',
             '    public static readonly Dictionary<int, (int Ticks, bool Sure)> Table = new()',
             '    {']
    for wid, h, (ticks, sure, camonly, kind, parts, why) in keep:
        note = '%s 0x%x %s%s' % (nm.get(wid, ''), h, ' '.join(parts), (' — ' + '·'.join(why)) if why else '')
        lines.append('        [%d] = (%d, %s), // %s' % (wid, ticks, 'true' if sure else 'false',
                                                       ' '.join(note.replace('\r', ' ').replace('\n', ' ').split())))
    lines += ['    };',
              '',
              '    /// <summary>Sure 가 거짓인 까닭이 「카메라가 설 때까지 기다림」(0x1006e850) 하나뿐인 work — '
              '카메라 대기를 빼면 틱은 믿을 만하다.</summary>',
              '    public static readonly HashSet<int> CameraOnly = new()',
              '    {']
    cam = [wid for wid, h, res in keep if res[2]]
    for k in range(0, len(cam), 16):
        lines.append('        ' + ' '.join('%d,' % x for x in cam[k:k + 16]))
    lines += ['    };', '}', '']
    with open(out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(lines))
    vals = sorted(r[2][0] for r in keep)
    print('%s: work %d개 (Sure %d · 불확실 %d, 그중 카메라만 %d · 슬롯 %d) 중앙값 %s 최대 %s' % (
        out, len(keep), sum(1 for r in keep if r[2][1]), sum(1 for r in keep if not r[2][1]), len(cam),
        sum(1 for r in keep if r[2][3] == 'slot'), vals[len(vals) // 2] if vals else '-', vals[-1] if vals else '-'))


if __name__ == '__main__':
    main()

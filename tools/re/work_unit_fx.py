"""work 핸들러가 <b>유닛 자체</b>에 거는 숨김·밝기 사건 표 `duel-dx/Ability/WorkUnitFx.g.cs` 를 만든다 (ba-21 F13).

    python tools/re/work_unit_fx.py "<게임 폴더>" [--dry] [--report 사건.tsv] [--out …/WorkUnitFx.g.cs] [--cache 실행결과.pkl]

`work_fx_emu.Emu` 를 이 도구 안에서만 덧씌워(파일은 안 고친다) 아래 넷을 사건으로 남긴다. 흐름은 안 바꾼다.

  숨김    `0x100eadb0(n)` — this(와 그 자식들 `+0x18`)의 가상 함수 `+0xb4(n)`: n 틱 동안 안 그린다. 0 = 다시 보임.
          핸들러는 1000·10000 을 주고 끝에 `(0)` 으로 푼다 — 표의 Ticks 는 원본 값 그대로다(둘 다 「다시 보일 때까지」로 다루면 된다).
          애니메이터에 바로 거는 `0x100274d0(n)`(`[유닛+0x58]+0x2c = n`)도 같은 것으로 센다.
  밝기    `0x100d0430`(전환 개체 생성) + `0x100d0470(개체, 처음, 끝, 걸음틱)`: 개체의 애니메이터(`[개체+0x58]`) `+0x13` 을
          걸음틱마다 한 칸씩 처음 → 끝으로 옮기고 끝에 닿으면 전환 개체가 사라진다(틱 `0x100d04b0`, 적용 `0x100d0520`).
          값 9 = `+0x13 = 0`(또렷) · 1~8 = `+0x13 = 그 값` · 0 = 숨김(`0x100274d0(10000)`) + `+0x13 = 1`.
          첫 값은 거는 틱에 바로 들어가고(0x100d04a0) 그 뒤 걸음틱마다 한 칸 — 끝까지 |끝 − 처음| × 걸음틱.
          끝난 뒤에도 유닛은 끝 값에 <b>머문다</b>(전환 개체는 되돌리지 않는다).
  바로 쓰기 `mov byte [[유닛+0x58]+0x13], v` — 핸들러가 밝기를 그 자리에서 고친다(블라인드·안티 밸런싱의 끝 `= 0` 은 9 로 되돌리기).
          표에는 Kind 2 · From = To · Step 0 으로 적는다.

          전환 개체에 시작 지연(`0x100c24d0`)을 걸면 걸음은 그 뒤에 돈다 — 표의 Tick 은 <b>거는 틱 + 지연</b>이다.
          끝 알림(`0x100c2350`)을 건 전환을 다음 단계가 기다리면(리콜) 그 기다림을 전환 길이로 채워 뒤 단계를 민다.

누구: 숨김의 this / 밝기의 개체 인자 / 바로 쓰기의 바탕이 `나`(시전자) · `[나+0x8c]`(겨눈 대상)인 것만 `Table` 에 싣는다.
  화면의 유닛 목록(`[0x101be2f8]+0x3c6c`·`+0x3c74`)을 도는 고리에서 바로 쓰는 것(브레인 브레이크 — 시전자·겨눈 대상 말고 전부)은
  Who = 3 으로 `Others` 에 따로 싣는다.
  이펙트 생성자가 돌려준 개체(몸 복제·바위 따위)에 거는 것과 바탕을 못 읽은 것(유닛 목록 고리 안)은 뺀다(수는 찍는다).
  「범위 안 대상마다」(Who 2)는 층 0 유닛 모으기 `0x100df5c0` 뒤 되돌이 고리 안에서 걸리는 것 — 기호 실행은 고리 속 유닛을
  못 가리므로 지금은 한 건도 없다(칸만 있다).

틱: `work_fx_timing.at_of` — 단계 시작 틱(`work_hit_ticks.Tool.stage` 걷기) + 단계 안의 틱. 길이를 모르는 단계는 0 으로 세고
  그 뒤 사건은 Sure = false(「적어도 그만큼」). 걷기가 안 지나간 단계(첫 갈래만 따라간다)의 사건은 앞 단계 사건의 틱을 물려받고 Sure = false.

합성: 마지막 사건 뒤에도 유닛이 숨겨져 있거나 밝기가 9 가 아니면, 행동 끝(`0x100762d0` 을 부르는 틱 — 없으면 걷기의 마지막 단계 끝)에
  「다시 보임」(숨김) / 「밝기 9 로 바로」(밝기)를 넣는다 — 원본은 시전자가 죽거나(희생·익스플로젼) 끝 처리가 되살리지만
  리메이크에서 유닛이 안 보이는 채로 남으면 안 되기 때문이다. 합성한 줄은 Sure = false 이고 줄 끝 주석에 적는다.
  끝 틱을 못 구한 work 은 표에서 빼고 찍는다.

전용 연출 work(`work_fx_timing.keep` — 텔레포트·블랙홀·군단기·필살기 따위 230개)은 뺀다.
근거: 옵시디안 분석/원본차이/ba21-fx-playback.md 「유닛 숨김·명암 표 생성 기록」
"""
import argparse
import collections
import os
import pickle
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import work_fx_emu as emu                                   # noqa: E402
import work_fx_timing as wt                                 # noqa: E402
import work_hit_ticks as ht                                 # noqa: E402
from capstone.x86 import X86_OP_IMM, X86_OP_MEM             # noqa: E402

HIDE, ANIM_HIDE = 0x100eadb0, 0x100274d0
FADE = 0x100d0470
COLLECT_UNITS = 0x100df5c0
ANIM, LEVEL = 0x58, 0x13                                    # 유닛 +0x58 = 애니메이터, 애니메이터 +0x13 = 밝기 칸
UNITS = {'나': 0, '나.대상': 1}
ANIMS = {'나.애니': 0, '나.대상.애니': 1}
# 화면의 유닛 목록 둘([0x101be2f8]+0x3c6c · +0x3c74)을 도는 고리 — 브레인 브레이크가 시전자·겨눈 대상 말고 다 어둡게 한다(0x100953d0~0x10095464)
SCENE_LISTS = ('ds:0x101be2f8+0x3c6c+', 'ds:0x101be2f8+0x3c74+')
OTHERS = 3
WHO = ['시전자', '대상', '범위 안 대상마다', '그 밖의 유닛 전부']


class Rec:
    """숨김·밝기 사건을 덧기록한다 — 흐름·레지스터·스택은 그대로. `Emu`(전수 훑기)와 `HEmu`(틱 찾기)에 섞어 쓴다."""

    def _ev(self, ctx, i, path, kind, **kw):
        ctx['events'].append(dict(kw, k=kind, va=i.address, path=path + ('unitfx',), stage=ctx['stage'], tick=ctx['tick']))

    def call(self, s, i, ctx, depth, path):
        ops = i.operands
        if ops[0].type == X86_OP_IMM:
            tgt, this, sp = ops[0].imm, s.reg['ecx'], s.reg['esp']
            a = [s.mem.get(sp[1] + 4 * k) for k in range(4)]
            if tgt == HIDE:
                self._ev(ctx, i, path, 'uhide', who=this if isinstance(this, str) else None, n=a[0])
            elif tgt == ANIM_HIDE:
                self._ev(ctx, i, path, 'uhide', who={'나.애니': '나', '나.대상.애니': '나.대상'}.get(this, 'fx' if this is None else str(this)),
                         n=a[0], anim=True)
            elif tgt == FADE:
                obj = a[0]
                if isinstance(obj, str) and obj.startswith('fx#'):
                    f = ctx['fxs'][int(obj[3:])]
                    obj = 'fx:%s:%s' % (f.get('obs'), f.get('motion'))
                self._ev(ctx, i, path, 'ufade', who=obj if isinstance(obj, str) else None, a=a[1], b=a[2], step=a[3],
                         fxi=this if isinstance(this, str) and this.startswith('fx#') else None, delay=0, notify=False)
            elif tgt in (emu.DELAY, ht.NOTIFY) and isinstance(this, str) and this.startswith('fx#'):
                for ev in reversed(ctx['events']):           # 전환 개체에 건 시작 지연 / 끝 알림(다음 단계가 이것을 기다린다)
                    if ev['k'] == 'ufade' and ev['fxi'] == this:
                        if tgt == ht.NOTIFY:
                            ev['notify'] = a[0] != 0
                        else:
                            ev['delay'] = a[0] if isinstance(a[0], int) and 0 <= a[0] < wt.MAXDELAY else '?'
                        break
            elif tgt == COLLECT_UNITS:
                self._ev(ctx, i, path, 'ucollect')
        super().call(s, i, ctx, depth, path)

    def store(self, s, a, v, i, ctx, path):
        if isinstance(a, tuple) and a[1] == LEVEL and isinstance(a[0], str) and i.operands[0].type == X86_OP_MEM \
                and i.operands[0].size == 1 and a[0] != 'sp':
            self._ev(ctx, i, path, 'uset', who=a[0], v=v)
        super().store(s, a, v, i, ctx, path)


class UEmu(Rec, emu.Emu):
    pass


class TEmu(Rec, ht.HEmu):
    """`work_hit_ticks.HEmu` 는 `inc [나+0x96]` 뒤의 틱 비교 상수까지 모은다 — 사건이 단계 안 몇 틱에 처음 닿는지 찾을 때 쓴다."""


def first_ticks(te, h, wid, stages, d=1):
    """{(단계, 주소): 그 사건(숨김·밝기·행동 끝)에 처음 닿는 단계 안 틱} — `Tool.stage` 와 같은 틱들(0·1·비교 상수 ±1)로 돌린다."""
    out = {}
    for st in stages:
        c = te.run(h, wid, st, None, d)
        for t in sorted({0, 1} | {t for x in c['tickc'] for t in (x - 1, x, x + 1) if 0 <= t < 5000}):
            for ev in te.run(h, wid, st, t, d)['events']:
                if ev['k'] in ('uhide', 'ufade', 'uset', 'end'):
                    out.setdefault((st, ev['va']), t)
    return out


def analyze(game):
    old = emu.Emu
    emu.Emu = UEmu
    try:
        return emu.analyze_all(game)
    finally:
        emu.Emu = old


# ---------------------------------------------------------------- 사건 읽기
def byte(v):
    return v & 0xff if isinstance(v, int) else None


def level_of_anim(v):
    """애니메이터 +0x13 값 → 밝기(0~9). 0 = 9(또렷), 1~8 그대로. 그 밖(섞기 방식 14·17·20 따위)은 None."""
    v = byte(v)
    if v == 0:
        return 9
    return v if v is not None and 1 <= v <= 8 else None


def raw_events(r, d):
    """한 방향의 (종류, 누구 글, 값…, 사건) — 유닛 것만 가리기 전."""
    return [ev for ev in r['main'][d]['events'] if ev['k'] in ('uhide', 'ufade', 'uset')]


def classify(ev, stats, w):
    """→ (Who, Kind, Ticks, From, To, Step) 또는 None(유닛이 아니거나 못 읽음)."""
    k, who = ev['k'], ev.get('who')
    if k == 'uhide':
        if who not in UNITS:
            stats['숨김 — 유닛 아님/못 읽음(%s)' % ('애니메이터' if ev.get('anim') else who)].add(w)
            return None
        n = ev['n']
        if not isinstance(n, int):
            stats['숨김 — 틱 못 읽음'].add(w)
            return None
        n &= 0xffffffff
        return (UNITS[who], 1, 0, 0, 0, 0) if n == 0 else (UNITS[who], 0, n, 0, 0, 0)
    if k == 'ufade':
        if who not in UNITS:
            stats['밝기 — %s' % ('이펙트 개체' if isinstance(who, str) and who.startswith('fx:') else
                                '군단기 짝(나+0x500)' if isinstance(who, str) and who.startswith('나+0x500') else
                                '개체 못 읽음')].add(w)
            return None
        a, b, st = byte(ev['a']), byte(ev['b']), ev['step']
        if a is None or b is None or not isinstance(st, int) or a > 9 or b > 9:
            stats['밝기 — 값 못 읽음'].add(w)
            return None
        return (UNITS[who], 2, 0, a, b, st & 0xffff)
    if who in ANIMS:
        n = ANIMS[who]
    elif isinstance(who, str) and who.startswith(SCENE_LISTS) and who.endswith('+0x58'):
        n = OTHERS
    else:
        stats['바로 쓰기 — 바탕이 유닛이 아님/못 읽음'].add(w)   # 이펙트 개체의 애니메이터는 바탕이 None 이라 사건이 안 남는다
        return None
    lv = level_of_anim(ev['v'])
    if lv is None:
        stats['바로 쓰기 — 밝기 값이 아님'].add(w)
        return None
    return (n, 2, 0, lv, lv, 0)


def when(st, ticks, ev):
    """(틱, 확실) — 걷기가 안 지나간 단계면 None."""
    s = st.get(ev['stage'])
    if s is None:
        return None
    t = ticks.get((ev['stage'], ev['va']))
    sure = bool(s[1]) and t is not None                      # 틱을 정한 실행에서는 못 닿은 사건 — 단계 시작으로 센다
    return wt.at_of(st, dict(ev, tick=t)), sure


def end_tick(r, st, ticks, d=1):
    """행동이 끝나는 틱 (틱, 확실) — 0x100762d0 을 부르는 사건 가운데 걷기가 지나간 단계의 것. 없으면 걷기의 마지막 단계 끝. 못 구하면 None."""
    best = None
    for ev in r['main'][d]['events']:
        if ev['k'] == 'end' and ev.get('this') == '나' and ev['stage'] in st:
            t = when(st, ticks, ev)
            if best is None or t[0] > best[0]:
                best = t
    if best is not None:
        return best
    if not st:
        return None
    last = max(st, key=lambda k: st[k][0])
    now, sure, inc, T, n = st[last]
    if T is None:
        return None                                          # 마지막 단계의 길이를 모른다
    return now + n, False


def wait_for_fades(r, st, ticks, d=1):
    """끝 알림(0x100c2350)을 건 밝기 전환을 기다리는 단계의 길이를 채운다 — `Tool.stage` 는 전환 개체의 길이(시작 지연 + |끝 − 처음| × 걸음)를
    몰라 그 기다림을 0 으로 센다(리콜 단계 2). 전환이 끝나는 틱까지 그 단계를 늘리고 뒤 단계를 그만큼 민다. → 고친 단계 표."""
    st = dict(st)
    order = sorted(st, key=lambda k: st[k][0])
    for ev in r['main'][d]['events']:
        if ev['k'] != 'ufade' or not ev.get('notify') or ev['stage'] not in st:
            continue
        a, b, step = byte(ev['a']), byte(ev['b']), ev['step']
        if a is None or b is None or not isinstance(step, int) or not isinstance(ev['delay'], int):
            continue
        end = when(st, ticks, ev)[0] + ev['delay'] + abs(b - a) * (step & 0xffff)
        later = [k for k in order if st[k][0] > st[ev['stage']][0]]
        if st[ev['stage']][3] and not st[ev['stage']][2]:
            later = [ev['stage']] + later                    # 건 단계가 곧 기다리는 단계
        k = next((k for k in later if not st[k][2] and st[k][3]), None)      # 처음 나오는 「끝 알림 기다림」 단계
        if k is None or st[k][4] != 0:
            continue                                         # 이미 다른 알림으로 길이를 셌다
        delta = end - st[k][0]
        if delta <= 0:
            continue
        now, sure, inc, T, n = st[k]
        st[k] = (now, sure, inc, T, n + delta)
        for j in order:
            if st[j][0] > now:
                st[j] = (st[j][0] + delta,) + st[j][1:]
    return st


def events_of(r, st, ticks, d, stats, w):
    """[(틱, 확실, (Who, Kind, Ticks, From, To, Step), 주소, 단계)] — 핸들러 차례대로."""
    out, prev, level = [], 0, {}
    for ev in raw_events(r, d):
        c = classify(ev, stats, w)
        if c is None:
            continue
        t, sure = when(st, ticks, ev) or (prev, False)       # 걷기가 안 지나간 단계 — 앞 사건의 틱을 물려받는다
        t = max(t, prev)
        prev = t
        if ev['k'] == 'ufade':
            # 전환 개체의 시작 지연(0x100c24d0) — 걸음은 그 뒤에 돈다(0x100d04b8 `[+0x6c] != 0` 이면 그냥 나감).
            # 처음 값은 거는 틱에 바로 들어가므로(0x100d04a0) 그때 밝기와 다르면 「바로 그 값」 사건을 먼저 넣는다.
            delay = ev['delay']
            if delay == '?':
                delay, sure = 0, False
            if delay and c[3] != level.get(c[0], 9):
                out.append((t, sure, (c[0], 2, 0, c[3], c[3], 0), ev['va'], ev['stage']))
            t += delay
        if c[1] == 2:
            level[c[0]] = c[4]
        out.append((t, sure, c, ev['va'], ev['stage']))
    out.sort(key=lambda x: x[0])                             # 지연이 붙은 전환이 뒤 사건보다 늦을 수 있다(차례는 지킨다 — 안정 정렬)
    return out


def dedupe(evs):
    """같은 틱에 같은 사건이 되풀이되면(갈래가 같은 것을 또 거는 것) 하나로. 숨김 → 숨김(틱만 다른 것)도 뒤 것을 버린다."""
    out, hidden, level = [], {}, {}
    for x in evs:
        t, sure, c, va, stg = x
        who, kind = c[0], c[1]
        if any(y[0] == t and y[2] == c for y in out):
            continue
        if kind == 2:
            if c[3] == c[4] == level.get(who, 9) and c[5] == 0:
                continue                                     # 이미 그 밝기다(안티 밸런싱의 끝 `= 0` — 유닛은 내내 9)
            level[who] = c[4]
        if kind == 0:
            if hidden.get(who):
                continue                                     # 이미 숨겨져 있다(리콜 단계 2·3 의 (1000) 둘)
            hidden[who] = True
        elif kind == 1:
            hidden[who] = False
        out.append(x)
    return out


def final_state(evs):
    """{Who: (숨겨졌나, 밝기)} — 사건을 다 돈 뒤."""
    state = {}
    for t, sure, (who, kind, n, a, b, step), va, stg in evs:
        hid, lv = state.get(who, (False, 9))
        if kind == 0:
            hid = True
        elif kind == 1:
            hid = False
        else:
            lv = b
        state[who] = (hid, lv)
    return state


def build(game, cache=None):
    tool = ht.Tool(game)
    works = tool.e.works
    names = ht.names(tool.g, works)
    if cache and os.path.exists(cache):
        res = pickle.load(open(cache, 'rb'))
    else:
        res, _ = analyze(game)
        if cache:
            pickle.dump(res, open(cache, 'wb'))
    te = TEmu(game)
    stats = collections.defaultdict(set)
    out = collections.OrderedDict()                          # work → [(틱, 확실, 사건, 메모)]
    kept, noend, differ = [], [], []
    for w in sorted(res):
        r = res[w]
        if not any(raw_events(r, d) for d in (1, 0, 2, 3)):
            continue
        if wt.keep(works, w):
            if any(ev['k'] != 'uset' or ev.get('who') in ANIMS for ev in raw_events(r, 1)):
                kept.append(w)
            continue
        try:
            st = wt.stage_starts(tool, r['handler'], w)
        except Exception:
            st = {}
        ticks = first_ticks(te, r['handler'], w, sorted(st))
        st = wait_for_fades(r, st, ticks)
        per = {d: dedupe(events_of(r, st, ticks, d, stats, w)) for d in (1, 0, 2, 3)}
        evs = per[1]
        if any([(x[0], x[2]) for x in per[d]] != [(x[0], x[2]) for x in evs] for d in per):
            differ.append(w)                                 # 방향마다 다르다 — 왼쪽(1) 것을 쓴다
        if not evs:
            continue
        rows = [(t, sure, c, '') for t, sure, c, va, stg in evs]
        left = {who: s for who, s in final_state(evs).items() if s[0] or s[1] != 9}
        if left:
            end = end_tick(r, st, ticks)
            if end is None:
                noend.append(w)
                continue
            t = max([end[0]] + [x[0] + abs(x[2][4] - x[2][3]) * x[2][5] for x in evs])      # 마지막 전환이 끝난 뒤
            for who, (hid, lv) in sorted(left.items()):
                if hid:
                    rows.append((t, False, (who, 1, 0, 0, 0, 0), '끝에 %s 다시 보임(합성)' % WHO[who]))
                    stats['합성 — 다시 보임'].add(w)
                if lv != 9:
                    rows.append((t, False, (who, 2, 0, 9, 9, 0), '끝에 %s 밝기 9(합성)' % WHO[who]))
                    stats['합성 — 밝기 9'].add(w)
        out[w] = rows
    return out, res, names, stats, kept, noend, differ


# ---------------------------------------------------------------- 쓰기
def fmt(t, sure, c):
    who, kind, n, a, b, step = c
    return 'new(%d, %d, %d, %d, %d, %d, %d, %s)' % (t, who, kind, n, a, b, step, 'true' if sure else 'false')


def write(path, out, res, names):
    L = ['// <auto-generated> tools/re/work_unit_fx.py 가 만든다 — 손으로 고치지 않는다.',
         'namespace DuelDx;',
         '',
         '/// <summary>work 핸들러가 유닛 자체에 거는 숨김·밝기(0x100eadb0 · 0x100d0470) — 핸들러 단계 0 부터의 틱.</summary>',
         '/// <remarks>',
         '/// 사건은 틱 순(같은 틱은 핸들러 차례)이다. 숨김과 밝기는 따로 논다 — 숨김은 「안 그림」 깃발, 밝기는 0~9 값이고',
         '/// 「다시 보임」(Kind 1)은 숨김만 푼다(밝기는 그대로). 밝기 9 = 또렷, 1~8 = 반투명(원본 애니메이터 +0x13 = 그 값), 0 = 안 보임.',
         '/// 밝기 바꾸기는 거는 틱에 From 이 바로 들어가고 Step 틱마다 To 쪽으로 한 칸 — 끝까지 |To − From| × Step 틱, 끝난 뒤 To 에 머문다.',
         '/// From == To · Step 0 은 「그 틱에 바로 그 값」(핸들러가 애니메이터 칸을 바로 적는 것 — 대개 9 로 되돌리기)이다.',
         '/// 줄 끝 주석: 이름 · 핸들러 주소 · 합성한 사건(원본 핸들러에는 없고, 유닛이 숨은/어두운 채 남지 않게 행동 끝 틱에 넣은 것 — Sure=false).',
         '/// 전용 연출이 있는 work(텔레포트·블랙홀·군단기·필살기 — work_fx_timing.keep 의 230개)은 없다.',
         '/// 워핑(422~) · 리콜(421~) · 이스케이프(1583)는 그 목록 밖이라 들어 있지만 리메이크가 이미 손으로 Fade 를 움직인다',
         '/// (Push.cs ThrowRoutine · Teleport.cs RecallRoutine · Turn.cs EscapeWork) — 둘이 겹치지 않게 한쪽만 쓴다.',
         '/// 희생·익스플로젼은 시전자가 어두워진 채 죽는다(되돌리는 코드가 없다) — 합성한 「밝기 9」는 살아남았을 때만 뜻이 있다.',
         '/// </remarks>',
         'internal static class WorkUnitFx',
         '{',
         '    /// <param name="Tick">단계 0 부터의 틱(모르는 단계는 0 으로 셈 — Sure=false)</param>',
         '    /// <param name="Who">0 시전자 · 1 겨눈 대상 유닛 · 2 범위 안 대상마다 (· 3 그 밖의 유닛 전부 — Others 에만)</param>',
         '    /// <param name="Kind">0 숨김(Ticks 틱, 10000 = 다시 보일 때까지) · 1 다시 보임 · 2 밝기 바꾸기(From → To, 0~9, Step 틱마다 한 칸)</param>',
         '    public readonly record struct Ev(int Tick, int Who, int Kind, int Ticks, int From, int To, int Step, bool Sure);',
         '',
         '    public static readonly Dictionary<int, Ev[]> Table = new()',
         '    {']
    def block(pick):
        for w, allrows in out.items():
            rows = [x for x in allrows if pick(x[2][0])]
            if not rows:
                continue
            nm = ' '.join((names.get(w) or '').split())
            memo = ' · '.join(dict.fromkeys(m for _, _, _, m in rows if m))
            L.append('        [%d] = [%s],   // %s · 0x%x%s' % (w, ', '.join(fmt(t, s, c) for t, s, c, _ in rows), nm, res[w]['handler'],
                                                               ' · ' + memo if memo else ''))

    block(lambda who: who != OTHERS)
    L += ['    };',
          '',
          '    /// <summary>겨눈 대상도 시전자도 아닌 <b>화면의 다른 유닛 전부</b>에 거는 것(Who = 3) — Table 과 따로 둔다.</summary>',
          '    /// <remarks>브레인 브레이크(0x100953d0~0x10095464 · 0x100958f6~0x1009597d): 유닛 목록 둘을 돌며 시전자·겨눈 대상 말고 다 밝기 2 로, 판정 틱에 9 로 되돌린다.</remarks>',
          '    public static readonly Dictionary<int, Ev[]> Others = new()',
          '    {']
    block(lambda who: who == OTHERS)
    L += ['    };', '}', '']
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(L))


def summarize(out, res, names, stats, kept, noend, differ):
    kinds, whos = collections.Counter(), collections.Counter()
    kw, sure, total, synth = collections.defaultdict(set), 0, 0, 0
    for w, rows in out.items():
        for t, s, c, memo in rows:
            name = ['숨김', '다시 보임', '밝기'][c[1]] + (' (바로 쓰기)' if c[1] == 2 and c[5] == 0 and not memo else '') + (' (합성)' if memo else '')
            kinds[name] += 1
            kw[name].add(w)
            whos[WHO[c[0]]] += 1
            total += 1
            sure += 1 if s else 0
            synth += 1 if memo else 0
    print('work %d개 · 사건 %d개 (확실 %d · %.0f%%) · 합성 %d' % (len(out), total, sure, 100.0 * sure / max(1, total), synth))
    for k in sorted(kinds):
        print('  %-18s %4d건 / %3d work' % (k, kinds[k], len(kw[k])))
    print('  대상별: ' + ' · '.join('%s %d' % (k, whos[k]) for k in WHO))
    print('  Table work %d · Others work %d' % (sum(1 for v in out.values() if any(x[2][0] != OTHERS for x in v)),
                                             sum(1 for v in out.values() if any(x[2][0] == OTHERS for x in v))))
    by = collections.OrderedDict()
    for w in out:
        by.setdefault((res[w]['handler'], tuple((t, s, c) for t, s, c, m in out[w])), []).append(w)
    for (h, _), ws_ in by.items():
        w = ws_[0]
        print('  0x%x %s(%d) ×%d : %s' % (h, ' '.join((names.get(w) or '').split()), w, len(ws_),
                                         ' '.join(fmt(t, s, c) + ('[합성]' if m else '') for t, s, c, m in out[w])))
    for k in sorted(stats):
        print('  %s: work %d' % (k, len(stats[k])))
    print('  뺀 work(전용 연출) %d · 끝 틱을 몰라 뺀 work %d %s · 방향마다 달라 왼쪽 것을 쓴 work %d %s' % (
        len(kept), len(noend), noend[:20], len(differ), differ[:20]))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('--out', default=os.path.join(HERE, '..', '..', 'duel-dx', 'Ability', 'WorkUnitFx.g.cs'))
    ap.add_argument('--dry', action='store_true', help='파일을 안 쓰고 통계만')
    ap.add_argument('--report', help='사건마다 (work, 이름, 핸들러, 줄, 메모) 를 TSV 로')
    ap.add_argument('--cache', help='기호 실행 결과를 여기 두고 다시 쓴다(도구를 고치며 되풀이 돌릴 때)')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    out, res, names, stats, kept, noend, differ = build(a.game, a.cache)
    summarize(out, res, names, stats, kept, noend, differ)
    if a.report:
        with open(a.report, 'w', encoding='utf-8') as f:
            f.write('work\t이름\t핸들러\t줄\t메모\n')
            for w, rows in out.items():
                for t, s, c, memo in rows:
                    f.write('%d\t%s\t0x%x\t%s\t%s\n' % (w, ' '.join((names.get(w) or '').split()), res[w]['handler'], fmt(t, s, c), memo))
    if not a.dry:
        write(a.out, out, res, names)
        print('%s 를 적었다' % os.path.relpath(a.out, os.path.join(HERE, '..', '..')))


if __name__ == '__main__':
    main()

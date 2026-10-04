"""`duel-dx/Ability/AbilityScripts.g.cs` 의 이펙트 줄에 <b>뜨는 때</b>(Delay)와 수명(Life)을 원본 핸들러 차례대로 넣는다 (ba-21 F4·F5·F8·F9),
그리고 카메라 따라가기 work 집합 `duel-dx/Ability/WorkCameraFollow.g.cs` 를 만든다 (ba-21 F7).

    python tools/re/work_fx_timing.py "<게임 폴더>" [--dry] [--report 바뀐것.tsv] [--cs …/AbilityScripts.g.cs] [--camera …/WorkCameraFollow.g.cs]

차례: ① `work_fx_table.py` → ② `work_fx_patch.py` → ③ 이 도구. ①·② 를 다시 돌렸으면 이 도구도 다시 돌린다
(② 는 지연을 「제 시작 지연」으로 되돌린다). 이 도구만 두 번 돌려도 결과는 같다.

재생기(`BattleSceneWindow.AbilityFx.cs:SpawnAbilityEffects`)는 표의 줄을 <b>핸들러 단계 0 한 순간에</b> 다 띄우고 줄마다
`Delay` 틱 뒤에 그린다. 그래서 줄의 Delay 를 「단계 0 부터 센 틱」으로 적으면 재생기를 안 고치고도 차례가 맞는다.

  Delay = (그 이펙트를 만드는 단계의 시작 틱 + 그 단계 안의 틱)              … F8
          또는 (사슬로 매달렸으면 앞 이펙트의 끝 틱 + 사슬 지연)              … F4
          + 제 시작 지연(0x100c24d0)
  Life  = 수명(0x100c2530). 0 이면 0(재생기가 모션 한 번으로 본다). 수명×n(0x100c2570)이면 모션 길이 × n   … F9
  생성 주소마다 한 줄 — 같은 (Obs, 모션)이라도 때·수명·높이가 다르면 따로 적는다                           … F5
    (셋이 다 같은 줄은 하나로 둔다 — 표에 x·y 치우침 칸이 없어 같은 자리에 겹쳐 그려져 밝기만 두 배가 된다.)
    줄을 나누는 것은 <b>같은 단계 안</b>의 생성 주소들뿐이다. 같은 그림을 다른 단계에서 또 만드는 것은 첫 단계 것만 쓴다 —
    기호 실행은 모르는 갈래를 다 훑으므로 「레벨이 높을 때만 가는 단계」(카운터 미사일 Lv11↑ 의 둘째 발, 0x100a9ee1)와
    「이어서 가는 단계」를 가릴 수 없다. 나누면 낮은 레벨에서 두 발이 겹쳐 뜬다.

  · 단계 시작 틱 = `work_hit_ticks.Tool.stage` 의 단계 길이를 단계 0 부터 더한 것(`cmp [나+0x96], N` · 끝 알림 기다림 =
    알림 이펙트의 지연 + 수명 · 전이마다 +1). 길이를 모르는 단계(카메라·동작 대기)는 0 으로 센다 — 「적어도 그만큼」.
  · 단계 안의 틱 = 그 생성 호출이 처음 닿는 틱(틱 세는 단계). 끝 알림을 기다리는 단계에서 틱 1 이후에 만드는 것은 그 기다림 뒤.
  · 사슬 `0x100c2640(앞, 자리복사, 지연)`·`0x100c2680`(자식까지) = 앞 것이 <b>끝나면</b>, `0x100c2600` = 앞 것이 <b>시작하면</b>.
    앞 것의 길이 = 수명, 0 이면 모션 길이 − 1, 수명×n 이면 모션 길이 × n, 날아가는 것(Fly)은 모션 길이(재생기가 그동안 옮긴다).
  · 방향 — 줄이 뜨는 방향(Facing)은 표의 것을 지킨다. 네 방향의 때가 다 같으면 방향 없는 한 줄, 다르면 방향마다 한 줄씩.
  · 자리(OnTarget)·개수·날기는 표의 것을 지킨다. 높이(Lift)는 그 생성 호출의 z 를 읽었으면 그것, 못 읽었으면 표의 것.

안 건드리는 것
  · 표에만 있는 줄(「덤」 — 기호 실행이 못 닿은 가지), 준비 핸들러가 만드는 이펙트(1338·필살기 앞머리), 동작 차례·영상·몸 복제 표.
  · 전용 연출이 따로 있는 work(KEEP_ABI·KEEP_WORK·준비 7) — duel-dx 의 전용 루틴이 제 시각으로 그리므로 표 줄은 그대로 둔다.
근거: 옵시디안 분석/원본차이/ba21-fx-playback.md 「표 재생성 기록」
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
import work_fx_patch as wp                                  # noqa: E402
import work_hit_ticks as ht                                 # noqa: E402

# 전용 연출이 있는 어빌리티(work +0x4) — 비 · 소닉 블레이드 · 더블 브레이크 · 엘레맨탈 파이어 · 하이 텔레포트 · 서몬 몬스터 ·
# 밸런싱 둘 · 썬더 스톰 · 웹폰 크래쉬 · 아스트럴 애로우 · 메테오 · 그라비티 필드 · 블랙홀 · 크레이지 샷 · 나인 크루세이더 · 천지 파열무
KEEP_ABI = {3, 20, 27, 29, 37, 60, 65, 66, 75, 81, 87, 95, 110, 115, 126, 139, 163}
KEEP_WORK = {392} | set(range(1659, 1667)) | {1611, 1618}   # 군단기 · 아이템(메테오·썬더 스톰으로 바로 뛰는 핸들러)
KEEP_PREPARE = {7}                                          # 필살기 — 공통 앞머리(FinisherPrelude) 뒤 사슬이 핸들러 모션을 튼다
MAXDELAY, MAXLIFE = 600, 3000                               # work_fx_patch 와 같은 한도(넘으면 0 으로 본다)


def keep(works, wid):
    w = works.get(wid)
    return wid in KEEP_WORK or (w is not None and (w.get(0x4) in KEEP_ABI or w.get(0x3f) in KEEP_PREPARE))


def stage_starts(tool, h, wid):
    """{단계: (시작 틱, 확실한가, 틱 세는 단계인가, 전이 틱, 단계 길이)} — work_hit_ticks 의 걷기와 같은 셈(첫 갈래만 따라간다)."""
    res, now, sure, pend, st, seen = {}, 0, True, [], 0, set()
    while isinstance(st, int) and st not in seen and st <= emu.MAXSTAGE:
        seen.add(st)
        r = tool.stage(h, wid, st)
        late = [f for f in r.fx if f[0] >= 1 and not r.inc]
        pend = pend + [(None if n is None else now + t + n, name, flaw) for t, n, name, flaw in r.fx
                       if not (t >= 1 and not r.inc)]
        if r.T is None:
            res[st] = (now, sure, r.inc, None, 0)
            break
        if r.T == 0:
            n = 0
        elif r.inc:
            n = r.T
        else:
            known = [p for p in pend if p[0] is not None]
            if not pend or not known or any(p[2] for p in pend):
                sure = False
            n = max(0, min(p[0] for p in known) - now) if known else 0
        res[st] = (now, sure, r.inc, r.T, n)
        if r.cond:
            sure = False
        consumed = (not r.inc) and r.T
        after = [(None if fn is None else now + n + fn, name, flaw) for t, fn, name, flaw in late]
        pend = after if consumed else pend + after
        now += n + (0 if r.fall else 1)
        nx = [x for x in r.to if x is not None]
        if not nx:
            if None in r.to:
                st, sure = st + 1, False
            else:
                break
        else:
            st = nx[0]
    return res


def at_of(starts, ev):
    """그 사건이 일어나는 틱(단계 0 부터) — 모르는 단계는 0."""
    s = starts.get(ev['stage'])
    if s is None:
        return 0
    now, _, inc, T, n = s
    t = ev['tick'] if isinstance(ev['tick'], int) else 0
    if t <= 0:
        return now
    if inc:
        return now + (min(t, T) if T else t)
    return now + n


def is_pic(ev):
    return ev['k'] == 'fx' and ev['kind'] in ('obs', 'emit') and isinstance(ev['obs'], int) and isinstance(ev['motion'], int)


class Timer:
    """한 work · 한 방향의 사건 목록에서 이펙트마다 (시작 틱, 끝 틱)."""

    def __init__(self, evs, starts, mlen):
        self.evs, self.starts, self.mlen = evs, starts, mlen
        self.pos = collections.defaultdict(list)
        for n, ev in enumerate(evs):
            if ev['k'] == 'fx':
                self.pos[(ev['va'], ev['path'])].append(n)
        self.memo = {}

    def length(self, ev):
        own_life = ev.get('life')
        obs = is_pic(ev)
        ml = self.mlen(ev['obs'] & 0xffff, ev['motion'] & 0xffff) if obs else None
        loops = ev.get('loops')
        if ev['ctor'] in wp.FLYERS and ev['kind'] != 'emit':
            return ml or 0                                   # 재생기는 날아가는 것을 모션 길이 동안 옮긴다
        if isinstance(loops, int) and 0 < loops < 1000 and ml:
            return ml * loops
        if isinstance(own_life, int) and 0 < own_life < MAXLIFE:
            return own_life
        return max(0, ml - 1) if ml else 0

    def span(self, n, depth=0):
        if n in self.memo:
            return self.memo[n]
        ev = self.evs[n]
        own = ev['delay'] if isinstance(ev.get('delay'), int) and 0 < ev['delay'] < MAXDELAY else 0
        base, ch = at_of(self.starts, ev), ev.get('chain')
        if ch and ch[1] is not None and depth < 16:
            cand = self.pos.get((ch[1], ch[2]), [])
            prev = max([p for p in cand if p < n], default=cand[0] if cand else None)
            if prev is not None and prev != n:
                ps, pe = self.span(prev, depth + 1)
                d = ch[4] if isinstance(ch[4], int) and 0 <= ch[4] < MAXDELAY else 0
                base = max(base, (pe if ch[0] == 'end' else ps) + d)
        start = base + own
        self.memo[n] = (start, start + self.length(ev))
        return self.memo[n]

    def life(self, ev):
        """표의 Life 칸 — 못 푼 값이면 None(표 값을 지킨다)."""
        loops = ev.get('loops')
        if isinstance(loops, int) and 0 < loops < 1000:
            ml = self.mlen(ev['obs'] & 0xffff, ev['motion'] & 0xffff)
            if ml:
                return ml * loops
        if ev.get('life') == '?' or loops == '?':
            return None
        return wp.rng(ev.get('life'), MAXLIFE)


def retime(rows, res, works, starts_of, mlen, log):
    """rows = {work: [동작 글, [이펙트 줄]]} 를 그 자리에서 고친다. log 에 (work, 종류, Obs, 모션, 전, 후, 주소)."""
    stats, later = collections.Counter(), set()
    for w in sorted(rows):
        r = res.get(w)
        if r is None or keep(works, w):
            if r is not None:
                stats['그대로 둔 work(전용 연출)'] += 1
            continue
        st = starts_of(r['handler'], w)
        pre = set()
        if 'pre' in r:
            for d in (1, 0, 2, 3):
                pre |= {(ev['obs'] & 0xffff, ev['motion'] & 0xffff) for ev in r['pre'][d]['events'] if is_pic(ev)}
        gen, unknown = {}, set()                             # (Obs, 모션) → {방향: [(때, 수명, 높이, 주소, 날기)]}
        for d in (1, 0, 2, 3):
            evs = r['main'][d]['events']
            tm = Timer(evs, st, mlen)
            first = {}                                       # (Obs, 모션) → 그것을 처음 만드는 단계
            for n, ev in enumerate(evs):
                if not is_pic(ev):
                    continue
                key = (ev['obs'] & 0xffff, ev['motion'] & 0xffff)
                if first.setdefault(key, ev['stage']) != ev['stage']:
                    if d == 1:
                        later.add((w, key))
                    continue                                 # 단계가 갈리는 가지(레벨·조건)일 수 있다 — 첫 단계 것만 쓴다
                if ev.get('delay') == '?':
                    unknown.add(key)                         # 제 지연을 못 푼 호출(rand 따위) — 그 줄은 표 값을 지킨다
                start, _ = tm.span(n)
                gen.setdefault(key, {}).setdefault(d, []).append(
                    (start, tm.life(ev), wp.zlift(ev['z']), ev['va'], ev['ctor'] in wp.FLYERS and ev['kind'] != 'emit'))
        effs = rows[w][1]
        out, done = [], set()
        for x in effs:
            key = (x[0], x[1])
            if key not in gen or key in pre or key in unknown:
                out.append(x)
                continue
            if key in done:
                continue
            done.add(key)
            old = [y for y in effs if (y[0], y[1]) == key]
            show = (0, 1, 2, 3) if any(y[8] < 0 for y in old) else tuple(sorted({y[8] for y in old}))
            g = gen[key]
            fallback = next(g[d] for d in (1, 0, 2, 3) if d in g)
            per, hit = {}, set()
            for d in show:
                tpl = [y for y in old if y[8] in (-1, d)]
                kinds = list(dict.fromkeys((y[2], y[5], y[6]) for y in tpl))      # 표의 갈래 — (자리, 개수, 날기)
                lines, used = [], collections.Counter()
                for start, life, lift, va, fly in g.get(d) or fallback:
                    # 자리는 표의 것을 지킨다(실행이 x 를 못 푼 방향이 있어 방향마다 자리가 달리 읽힌다) — 날기만 맞춰 고른다
                    for kind in [v for v in kinds if v[2] == fly] or kinds:
                        same = [y for y in tpl if (y[2], y[5], y[6]) == kind]
                        y = list(same[min(used[kind], len(same) - 1)])
                        used[kind] += 1
                        hit.add(kind)
                        y[4] = start
                        if life is not None:
                            y[7] = life
                        if lift is not None:
                            y[3] = lift
                        y[8] = -1
                        if y not in lines:
                            lines.append(y)
                per[d] = sorted(lines, key=lambda y: (y[4], y[3], y[7], not y[2]))
            if len(show) == 4 and all(per[d] == per[show[0]] for d in show):
                new = per[show[0]]
            else:
                new = [y[:8] + [d] for d in show for y in per[d]]
            new += [y for y in old if (y[2], y[5], y[6]) not in hit]             # 실행이 못 본 갈래(자리가 다른 줄)는 그대로
            if [wp.fmt_eff(y) for y in new] != [wp.fmt_eff(y) for y in old]:
                log.append((w, '때 고침', key[0], key[1], ' '.join(wp.fmt_eff(y) for y in old),
                            ' '.join(wp.fmt_eff(y) for y in new), hex(fallback[0][3]), max(y[4] for y in new)))
            out += new
        rows[w][1] = out
    stats['다른 단계에서 또 만들어 첫 단계 것만 쓴 (work, 그림)'] = len(later)
    return stats


# ---------------------------------------------------------------- 카메라 따라가기 (F7)
def camera_sets(res, works, starts_of, names):
    """({'Target'|'Shot'|'Caster': {work}}, 핸들러별 설명 줄)."""
    sets = {'Target': set(), 'Shot': set(), 'Caster': set()}
    notes = collections.OrderedDict()
    name = {'나.대상': 'Target', 'fx': 'Shot', '나': 'Caster'}
    for w in sorted(res):
        r = res[w]
        seen = {}
        off = None
        st = None
        for d in (1, 0, 2, 3):
            for ev in r['main'][d]['events']:
                if ev['k'] not in ('cam', 'camoff'):
                    continue
                if st is None:
                    st = starts_of(r['handler'], w)
                s = st.get(ev['stage'])
                at = at_of(st, ev)
                sure = bool(s and s[1])
                if ev['k'] == 'camoff':
                    if off is None or at < off[1]:
                        off = (ev['stage'], at, sure, ev['va'])
                    continue
                who = name.get(ev['who'])
                if who is None:
                    continue
                sets[who].add(w)
                what = ''
                if ev['fx']:
                    _, o, m, ctor, kind = ev['fx']
                    what = '%s:%s' % (o, m) if isinstance(o, int) and isinstance(m, int) else \
                        {'body:self': '시전자 몸 복제', 'body:target': '대상 몸 복제'}.get(kind, '코드 이펙트 0x%x' % ctor)
                seen.setdefault((who, ev['va']), (ev['stage'], at, sure, ev['speed'], ev['prio'], what))
        if not seen:
            continue
        h = notes.setdefault(r['handler'], {'works': [], 'lines': None})
        h['works'].append(w)
        if h['lines'] is None:
            lines = []
            for (who, va), (stg, at, sure, speed, prio, what) in sorted(seen.items(), key=lambda kv: (kv[1][1], kv[0][1])):
                lines.append('%s%s 단계 %s 틱 %d%s 속도 %s 우선 %s @0x%x' % (
                    {'Target': '대상', 'Shot': '탄', 'Caster': '시전자'}[who], '(%s)' % what if what else '', stg, at,
                    '' if sure else '+', speed if isinstance(speed, int) else '?', prio if isinstance(prio, int) else '?', va))
            lines.append('놓기 단계 %s 틱 %d%s @0x%x' % (off[0], off[1], '' if off[2] else '+', off[3]) if off
                         else '놓기 없음(행동이 끝날 때)')
            h['lines'] = lines
    return sets, notes


def write_camera(path, sets, notes, names):
    def block(ids):
        ids = sorted(ids)
        out = ['        ' + ' '.join('%d,' % x for x in ids[k:k + 20]) for k in range(0, len(ids), 20)]
        if out:
            out[-1] = out[-1].rstrip(',')
        return out

    L = ['// <auto-generated> tools/re/work_fx_timing.py 가 만든다 — 손으로 고치지 않는다.',
         'namespace DuelDx;',
         '',
         '/// <summary>원본 핸들러가 카메라 따라가기(0x100eac00)를 거는 work — 무엇을 따라가는가.</summary>',
         '/// <remarks>',
         '/// 0x100eac00(속도, 우선) 은 this 를 카메라 큐 머리의 「따라가기」로 건다(→ 0x1006e620). 속도 0 = 자동. 0x100eac60 이 놓는다.',
         '/// 한 work 이 둘 이상에 들 수 있다(대상을 따라가다 탄을 따라가는 것 따위). 틱은 핸들러 단계 0 부터 센 것이고',
         '/// 「+」 는 길이를 모르는 단계(카메라·동작 대기)가 앞에 끼어 「적어도 그만큼」이라는 뜻이다.',
         '/// 핸들러별 — 주소 · 이름(첫 work) · work 수 : 거는 때 … / 놓는 때',
         '/// <code>']
    for h, x in sorted(notes.items()):
        nm = ' '.join((names.get(x['works'][0]) or '').split())
        L.append('/// 0x%x %s(%d) ×%d : %s' % (h, nm, x['works'][0], len(x['works']), ' / '.join(x['lines'])))
    L += ['/// </code>',
          '/// </remarks>',
          'internal static class WorkCameraFollow',
          '{',
          '    /// <summary>겨눈 대상 유닛을 따라간다(힐·큐어·배리어류).</summary>',
          '    public static readonly HashSet<int> Target =',
          '    ['] + block(sets['Target']) + [
          '    ];',
          '',
          '    /// <summary>날아가는 탄을 따라간다.</summary>',
          '    public static readonly HashSet<int> Shot =',
          '    ['] + block(sets['Shot']) + [
          '    ];',
          '',
          '    /// <summary>시전자를 따라간다.</summary>',
          '    public static readonly HashSet<int> Caster =',
          '    ['] + block(sets['Caster']) + [
          '    ];',
          '}',
          '']
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(L))


# ----------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('--cs', default=os.path.join(HERE, '..', '..', 'duel-dx', 'Ability', 'AbilityScripts.g.cs'))
    ap.add_argument('--camera', default=os.path.join(HERE, '..', '..', 'duel-dx', 'Ability', 'WorkCameraFollow.g.cs'))
    ap.add_argument('--dry', action='store_true', help='파일을 안 고치고 바뀔 것만 센다')
    ap.add_argument('--report', help='바뀐 것을 TSV 로 (work, 종류, Obs, 모션, 전, 후, 근거 주소)')
    ap.add_argument('--cache', help='기호 실행 결과를 여기 두고 다시 쓴다(도구를 고치며 되풀이 돌릴 때)')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

    tool = ht.Tool(a.game)
    works = tool.e.works
    if a.cache and os.path.exists(a.cache):
        res, starts, mlens, names = pickle.load(open(a.cache, 'rb'))
    else:
        res, _ = emu.analyze_all(a.game)
        starts, mlens, names = {}, {}, ht.names(tool.g, works)

    def starts_of(h, wid):
        if wid not in starts:
            try:
                starts[wid] = stage_starts(tool, h, wid)
            except Exception as ex:                          # 못 걸은 핸들러 — 단계 시작을 모른다(0)
                print('단계 걷기 실패 work %d: %s' % (wid, ex), file=sys.stderr)
                starts[wid] = {}
        return starts[wid]

    def mlen(obs, mo):
        if (obs, mo) not in mlens:
            mlens[(obs, mo)] = tool.motion_len(obs, mo)
        return mlens[(obs, mo)]

    text = open(a.cs, encoding='utf-8-sig').read()
    lines = text.split('\n')
    start = next(i for i, ln in enumerate(lines) if 'WorkScripts = new()' in ln)
    end = next(i for i in range(start, len(lines)) if lines[i].strip() == '};')
    import re
    rowre = re.compile(r'^(\s*)\[(\d+)\] = \(\[([^\]]*)\], \[(.*)\]\),\s*$')
    rows, order = {}, []
    for i in range(start + 2, end):
        m = rowre.match(lines[i])
        if not m:
            raise SystemExit('모르는 줄 %d: %s' % (i + 1, lines[i][:80]))
        rows[int(m.group(2))] = [m.group(3), wp.parse_effs(m.group(4))]
        order.append(int(m.group(2)))
    before = {w: [list(x) for x in rows[w][1]] for w in rows}

    log = []
    stats = retime(rows, res, works, starts_of, mlen, log)
    sets, notes = camera_sets(res, works, starts_of, names)
    if a.cache and not os.path.exists(a.cache):
        pickle.dump((res, starts, mlens, names), open(a.cache, 'wb'))

    body = ['        [%d] = ([%s], [%s]),' % (w, rows[w][0], ', '.join(wp.fmt_eff(x) for x in rows[w][1])) for w in order]
    new = '\n'.join(lines[:start + 2] + body + lines[end:])

    changed = sorted({x[0] for x in log})
    n0 = sum(len(v) for v in before.values())
    n1 = sum(len(rows[w][1]) for w in rows)
    print('바뀐 work %d개 · 바뀐 (Obs, 모션) %d개 · 이펙트 줄 %d → %d (%+d) · %s' % (len(changed), len(log), n0, n1, n1 - n0, dict(stats)))
    late = collections.defaultdict(int)                      # 바뀐 줄 가운데 가장 늦은 때
    for x in log:
        late[x[0]] = max(late[x[0]], x[7])
    top = sorted(((late[w], w) for w in changed), reverse=True)
    seenh, shown = set(), 0
    for dl, w in top:
        h = res[w]['handler']
        if h in seenh:
            continue
        seenh.add(h)
        print('  바뀐 줄의 Delay 최댓값 %d — work %d %s (0x%x)' % (dl, w, ' '.join((names.get(w) or '').split()), h))
        shown += 1
        if shown >= 10:
            break
    print('카메라 따라가기: 대상 %d · 탄 %d · 시전자 %d (핸들러 %d)' % (len(sets['Target']), len(sets['Shot']), len(sets['Caster']), len(notes)))
    if a.report:
        with open(a.report, 'w', encoding='utf-8') as f:
            f.write('work\t종류\tObs\t모션\t전\t후\t근거 주소\n')
            for x in log:
                f.write('\t'.join(str(v) for v in x[:7]) + '\n')
    if not a.dry:
        if new != text:
            with open(a.cs, 'w', encoding='utf-8-sig', newline='\n') as f:
                f.write(new)
            print('%s 를 고쳤다' % os.path.relpath(a.cs, os.path.join(HERE, '..', '..')))
        write_camera(a.camera, sets, notes, names)
        print('%s 를 적었다' % os.path.relpath(a.camera, os.path.join(HERE, '..', '..')))


if __name__ == '__main__':
    main()

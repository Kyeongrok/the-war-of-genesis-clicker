"""work(기술)마다 <b>동작 차례와 이펙트</b>를 뽑아 duel-dx 가 읽는 C# 표로 적는다.

    python tools/re/work_fx_table.py "<게임 폴더>" [출력.cs]
    python tools/re/work_fx_patch.py "<게임 폴더>"            ← 뽑은 뒤 <b>꼭</b> 이어서 돌린다 (ba-20 X)
    python tools/re/work_fx_timing.py "<게임 폴더>"           ← 그 뒤 <b>꼭</b> 이어서 돌린다 (ba-21 F — 뜨는 때·수명×n·카메라 집합)

이 도구는 핸들러를 코드 주소 순으로 읽어 지연·수명을 「바로 앞 이펙트」에 붙이고 이펙트를 16개에서 자른다.
`work_fx_patch.py` 가 단계 순 기호 실행(`work_fx_emu.py`)으로 지연·수명·높이·빠진 이펙트·아이템 work 줄을 바로잡는다.

`work_script.py` 가 이미 DLL 핸들러를 기호로 풀어 준다 — 여기서는 그 기록을 골라
`('act', 기준동작)` 은 동작 번호(기준÷3)로, `('eff', Obs, 모션, x, y)` 는 이펙트로 옮긴다.
동작은 <b>시전자에게 거는 것만</b> 넣는다 — this 가 대상(`나.대상`)이면 맞은 쪽 반응이라 뺀다(비·쇼크·브레인 스톰, fg-20).
모션 번호를 바로 트는 PlayMotion(`0x100e53c0`, 칼 꽂기 48→49→50 따위)은 동작 칸에 1000+모션(한 번)으로,
그 뒤에 되풀이 1000(`+0x7a`)을 적으면 2000+모션(붙듦)으로 넣는다. SetAction 의 되풀이 인자가 1000 이상이면(시전 자세 6 은 빼고)
3000+동작(붙듦)이다.
이펙트 자리는 x 식으로 가른다: `나.대상.*` 이면 대상 자리, `나.*` 면 시전자 자리,
그 밖(`esp+0x20` 처럼 코드가 셈해 넣는 자리 — 메테오의 떨어지는 자리 따위)은 <b>대상 자리</b>로 둔다.
예전에는 이것을 버렸는데, 남은 것이 소리만 든 Obs(1338·311·1324 …)뿐인 기술이 87개나 되어 그림이 한 장도 안 나왔다.
자리가 조금 틀려도 안 보이는 것보다 낫다.

소리 껍데기(그림 컷 없는 Obs)도 <b>남긴다</b> — duel-dx 가 그 소리 키로 소리를 낸다. 그림은 자식 키를 따라가고, 파생 클래스 생성자로
만드는 이펙트·뿌리개(`work_fx_extra.ExtraAnalyzer`, 분석-스킬 fx-189)도 더한다.

손으로 맞춘 표(`AbilityMotions`)가 있는 work 은 그것이 이긴다 — 이 표는 <b>빈자리를 채우는 용도</b>다.
"""
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import work_script as ws                                    # noqa: E402
from obs_ui_dump import load_motions                        # noqa: E402
import work_fx_extra as wx                                  # noqa: E402

GAME = sys.argv[1]
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, '..', '..', 'duel-dx', 'AbilityScripts.g.cs')

game = ws.Game(GAME)
dll = ws.Analyzer(os.path.join(GAME, 'G3PartII.dll'))
works = ws.load_works(game)


RAW_ONCE, RAW_HOLD = 1000, 2000                             # 동작 칸에 모션 번호를 적는 꼴 — 1000+m 한 번, 2000+m 붙듦(duel-dx RawOnce·RawHold)
ACT_HOLD = 3000                                             # 3000+동작 — 되풀이 1000 이상으로 붙드는 동작(duel-dx ActHold)


def is_target(this):
    """SetAction·PlayMotion 의 this 가 대상 유닛인가(ecx = [나+0x8c]) — 그런 동작은 시전자 사슬에 넣지 않는다."""
    return isinstance(this, str) and ('.대상' in this)


def where_of(x):
    """이펙트 자리 — 'target' · 'self'. 코드가 셈하는 자리(모르는 자리)는 대상 자리로 본다."""
    if isinstance(x, str) and x.startswith('나.') and '대상' not in x:
        return 'self'
    return 'target'


_pictures = {}


def pictures(obs, motion, depth=0):
    """그 Obs 모션이 실제로 그리는 (Obs, 모션)들 — 제 그림 컷(종류 0 키)이 있으면 자신, 자식 키(종류 2)가 부르는 Obs 는 따라간다.
    소리 키만 든 껍데기(1338 따위)는 빈 목록이다."""
    if (obs, motion) in _pictures:
        return _pictures[(obs, motion)]
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
                    for child in pictures(int(k['p'][0]), int(k['p'][1]), depth + 1):
                        if child not in out:
                            out.append(child)
    _pictures[(obs, motion)] = out
    return out


extra = wx.ExtraAnalyzer(os.path.join(GAME, 'G3PartII.dll'))


FLYERS = {0x100c3340, 0x100c5940}                            # 시전자 → 대상으로 날려 보내는 이펙트 생성자(fx-189)


def add(effs, key):
    """key = (Obs, 모션, 자리, 지연틱, 개수, 날기, 높이px, 수명틱). 같은 (Obs, 모션, 자리)는 하나만 — 아는 값이 많은 쪽을 남긴다."""
    default = (0, 1, False, 0, 0)
    key = tuple(key) + default[len(key) - 3:]
    for i, e in enumerate(effs):
        if e[:3] == key[:3]:
            if e[3:] == default and key[3:] != default:
                effs[i] = key
            return i
    effs.append(key)
    return len(effs) - 1


def zlift(z):
    """높이 — 월드 z 치우침 × 0.6 픽셀(화면 y = 월드y×0.8 − 월드z×0.6, 0x100ea910). 「나.z+50」·「나.대상.z+70」·숫자."""
    if isinstance(z, int):
        v = z - (1 << 32) if z >= 1 << 31 else z
        return round(v * 0.6) if abs(v) < 2000 else 0
    if isinstance(z, str):
        m = re.search(r'\.z([+-]\d+)$', z)
        if m:
            return round(int(m.group(1)) * 0.6)
    return 0


MOVIES = {34, 40, 41, 42, 61}                                # assets/effects/mov 에 풀어 둔 영상(mov_frames)
rows, movies, bodies = {}, {}, {}


def s32(v):
    try:
        v = int(v)
    except (TypeError, ValueError):
        return 0
    return v - (1 << 32) if v >= 1 << 31 else v


for wid in sorted(works):
    w = works[wid]
    try:
        seq = ([ws.PRELUDE[w[0x3f]][0]] if w and w[0x3f] in ws.PRELUDE else []) + [dll.handler(wid)]
    except Exception:
        continue
    acts, effs, movs, bods = [], [], [], []
    for hi, h in enumerate(seq):
        if not h:
            continue
        prelude = len(seq) == 2 and hi == 0
        try:
            recs = list(dll.script(h))
        except Exception:
            recs = []
        last_eff = None
        for rec in recs:
            kind = rec[1]
            if kind == 'act' and isinstance(rec[2], int):
                if is_target(rec[5]):
                    continue                                 # 대상에게 거는 동작(비·쇼크의 맞음 2·서기 0) — 시전자 사슬이 아니다
                act = rec[2] // 3
                # 되풀이 1000 이상 = 다음 단계까지 붙드는 동작(오메가 스윙 17 = 돌진하는 동안). 시전 자세 6 은 늘 붙들므로 뺀다.
                acts.append(ACT_HOLD + act if isinstance(rec[4], int) and rec[4] >= 1000 and act != 6 else act)
            elif kind == 'mot' and isinstance(rec[2], int) and not is_target(rec[3]):
                acts.append(RAW_ONCE + rec[2])               # 모션 번호를 바로 튼다(0x100e53c0)
            elif kind == 'rep' and isinstance(rec[2], int) and rec[2] >= 1000 and not is_target(rec[3]) \
                    and acts and RAW_ONCE <= acts[-1] < RAW_HOLD:
                acts[-1] += RAW_HOLD - RAW_ONCE              # 바로 앞 모션을 붙든다(반복 1000)
            elif kind == 'eff' and isinstance(rec[2], int) and isinstance(rec[3], int):
                place = where_of(rec[4])
                # 부모만 넣는다 — 자식 키는 duel-dx 가 DrawUnitLayers 로 그 틱·치우침대로 그린다(ba-15 R4, 넣으면 두 번 그려졌다).
                last_eff = add(effs, (rec[2], rec[3], place, 0, 1, False, zlift(rec[6]), 0))
            elif kind == 'delay' and isinstance(rec[2], int) and last_eff is not None and 0 < rec[2] < 600:
                e = effs[last_eff]
                effs[last_eff] = e[:3] + (rec[2],) + e[4:]
            elif kind == 'life' and isinstance(rec[2], int) and last_eff is not None and 0 < rec[2] < 3000:
                e = effs[last_eff]
                effs[last_eff] = e[:7] + (rec[2],)
        # 파생 클래스 생성자로 만드는 이펙트·뿌리개(fx-189) — 직접 호출만 보는 script() 가 놓친다.
        try:
            items = extra.effects(h)
        except Exception:
            items = []
        for r in items:
            if r['kind'] in ('body:self', 'body:target'):             # 몸 복제(파·혼 분신) — 모션을 코드가 셈하면 −1(지금 모션)
                if len(bods) < 12:
                    bods.append((r['motion'] if isinstance(r.get('motion'), int) else -1, r['kind'] == 'body:target'))
                continue
            if r['kind'] == 'mov' and r.get('mov') and r.get('movparam'):
                try:
                    num = int(os.path.basename(r['mov'])[:4])
                except ValueError:
                    num = -1
                if num in MOVIES:
                    key = (num, prelude, wx.where_of(r) != 'self', s32(r['movparam'][3]), s32(r['movparam'][4]))
                    if key not in movs:
                        movs.append(key)
                continue
            obs, mo = r.get('obs'), r.get('motion')
            if not isinstance(obs, int) or r['kind'] not in ('obs', 'emit'):
                continue
            place = 'self' if wx.where_of(r) == 'self' else 'target'
            delay = r['delay'] if isinstance(r.get('delay'), int) and 0 < r['delay'] < 600 else 0
            life = r['life'] if isinstance(r.get('life'), int) and 0 < r['life'] < 3000 else 0
            lift = zlift(r.get('z'))
            count = 1
            if r['kind'] == 'emit' and r.get('setter'):
                args = r['setter'][1]
                if len(args) > 2 and isinstance(args[2], int) and 1 < args[2] <= 16:
                    count = args[2]                          # 뿌리개 설정 인자 3 = 개수(크래쉬 봄 110 은 8)
            fly = r.get('ctor') in FLYERS
            if isinstance(mo, int):
                add(effs, (obs, mo, place, delay, count, fly, lift, life))   # 부모만(자식은 DrawUnitLayers)
            else:                                            # 뿌리개 모션을 코드가 고른다 — 그림 있는 첫 모션 둘
                try:
                    mm = sorted(wx.load_motions(game.read('Obs', '%04d.obs' % obs)))
                except Exception:
                    mm = []
                n = 0
                for m2 in mm:
                    for o, m3 in pictures(obs, m2):
                        if n < 2:
                            add(effs, (o, m3, place, delay, count, fly, lift, life))
                            n += 1
    if movs:
        movies[wid] = movs
    if bods:
        bodies[wid] = bods
    if not acts and not effs:
        continue
    rows[wid] = (acts[:12], effs[:16])

lines = [
    '// 이 파일은 tools/re/work_fx_table.py 가 만든다 — 손으로 고치지 말 것.',
    '// 원본 DLL 의 work 핸들러에서 뽑은 <동작 차례, 이펙트> 표다(분석-모션 ba-10).',
    '',
    'namespace DuelDx;',
    '',
    'internal sealed unsafe partial class BattleSceneWindow',
    '{',
    '    /// <summary>',
    '    /// work 번호 → (동작 차례, 이펙트) — <b>도구가 뽑은</b> 표. 손으로 맞춘 <see cref="AbilityMotions"/> 가 우선한다.',
    '    /// </summary>',
    '    /// <remarks>',
    '    /// 자리를 코드가 셈해 넣는 이펙트(메테오의 떨어지는 자리 같은 것)는 <b>대상 자리</b>로 들어 있다(정확한 자리는 모른다).',
    '    /// 그림 컷이 없는 Obs(소리 껍데기)는 빠져 있다.',
    '    /// 높이(Lift)는 월드 z × 0.6, 지연·수명은 0x100c24d0·0x100c2530 에서 읽었다(ba-15). 자식 키는 넣지 않는다 — 부모가 그린다.',
    '    /// </remarks>',
    '    private static readonly Dictionary<int, (int[] Actions, AbilityEffect[] Effects)> WorkScripts = new()',
    '    {',
]
for wid, (acts, effs) in rows.items():
    a = ', '.join(str(x) for x in acts)
    e = ', '.join('new(%d, %d, %s, %d%s)' % (o, m, 'true' if p == 'target' else 'false', lf,
                                            '' if (d, c, f, li) == (0, 1, False, 0) else ', %d, %d, %s, %d' % (d, c, 'true' if f else 'false', li))
                  for o, m, p, d, c, f, lf, li in effs)
    lines.append('        [%d] = ([%s], [%s]),' % (wid, a, e))
lines += ['    };', '',
          '    /// <summary>work 번호 → 시전 영상(Mov) — (영상, 준비 동작에서 띄우나, 대상에 붙나, dx, dy). 분석-스킬 fx-189 「Bink 영상」.</summary>',
          '    private static readonly Dictionary<int, MovieFx[]> WorkMovies = new()',
          '    {']
for wid, ms in movies.items():
    lines.append('        [%d] = [%s],' % (wid, ', '.join('new(%d, %s, %s, %d, %d)' % (n, 'true' if p else 'false', 'true' if t else 'false', dx, dy)
                                               for n, p, t, dx, dy in ms)))
lines += ['    };', '',
          '    /// <summary>work 번호 → 몸 복제(분신) — (모션, 대상 몸인가). 모션 −1 은 그 유닛의 지금 모션. 분석-스킬 fx-189 「파」.</summary>',
          '    private static readonly Dictionary<int, BodyFx[]> WorkBodies = new()',
          '    {']
for wid, bs in bodies.items():
    lines.append('        [%d] = [%s],' % (wid, ', '.join('new(%d, %s)' % (m, 'true' if t else 'false') for m, t in bs)))
lines += ['    };', '}', '']
with open(OUT, 'w', encoding='utf-8-sig', newline='\n') as f:
    f.write('\n'.join(lines))
print('work %d개를 %s 에 적었다' % (len(rows), os.path.relpath(OUT, os.path.join(HERE, '..', '..'))))
print('이어서 work_fx_patch.py → work_fx_timing.py 를 돌려 이펙트 칸·뜨는 때를 바로잡을 것')

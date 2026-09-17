"""
창세기전3 파트2 — 캐릭터가 기본공격·어빌리티를 쓸 때 재생하는 몸짓(Obs 모션)을 뽑는다.

사용법:
    python skill_motion.py <게임 폴더> <Chr 번호> [<Chr 번호> ...] [--dir 0|1|2|3] [--frames]
    python skill_motion.py <게임 폴더> --work <work 번호> ...
    예) python skill_motion.py "C:/.../창세기전3 파트2" 221 219 62 193 --frames

게임 폴더는 읽기만 한다(낱장 파일이 없으면 .idx/.pak 에서 메모리로 꺼낸다). capstone 필요.

흐름 (G3PartII.dll 정적 분석, 근거 VA 는 옵시디안 분석-모션.md "스킬 사용 모션" 절):
  Chr/NNNN.chr  +19 u16  기본공격 work 번호 (유닛 +0x12a, 상태 10 에서 이 번호로 공격)
                +56 (어빌리티 번호 u16, 레벨 u16) × 8
  Abi/NNNN.abi  (LoadAbilityData 0x1004b860, 파일 0012,0014~0020,0026)
                머리 u16 ?, u16 개수, u16 최대번호; 레코드 26바이트; 꼬리 u16.
                레코드: u16 번호, u16 이름 txr(+4), u16 최대레벨(+6), u16 +8, u8 +a, u16 +c, u8 +e,
                        u8 +f, u16 +10, u8 +12, u16 +14, u16 +16, u16 +18, u16 +1a, u16 +1c
                메모리 레코드 72바이트, +0x1c+레벨*2 = 그 레벨의 work 번호(Work 로더가 채움)
  Dat/NNNN.att  (LoadWorkData 0x1004bd90, 파일 0000~0009,0011,0012; 0010 은 안 읽음)
                머리 u16 ?, u16 개수, u16 최대번호; 레코드 62바이트; 꼬리 u16. 메모리 68바이트(0x101b6868).
                파일 순서: u16 번호, u16 +4 어빌리티, u8 +6 레벨, u8 +7, u8 +8, u16 +a, u16 +c, u8 +e..+18,
                u16 +1a, u16 +1c, u8 +1e, u8 +1f, (u8 +20+i, u16 +24+2i)×3, u16 +2a, u8 +2c, u8 +2d,
                u16 +2e, u16 +30, u16 +32, u16 +34, u8 +36, u16 +38, u8 +3a..+41, u16 +42
                +0x3f = 시전 준비 동작 종류(0 없음, 1·4, 2·5, 3·6, 7)
  Obs/NNNN.obs  (LoadG4ObsFile 0x1002f050) 머리 u16, u16 벌 개수, u16 최대번호, (u16 번호, u32 위치)×n,
                u16 모션 개수, u16 최대번호, (u16 번호, u32 위치)×m  ← 모션표
                모션 (0x1002fa00): u16 번호, u16 nA, u16 nB, u16 길이(틱), u16 nU,
                (u16 벌, u16 장)×nU  [쓰는 그림 목록],
                키 26바이트 × nA, 키 26바이트 × nB.
                키: u16 종류(+4), u16 시작틱(+0), u16 길이(+2), i16×10(+6..)
                    종류 0 = 그림 (+6 벌, +8 장), 1 = 소리(0x10031120), 2 = 다른 Obs 모션(+6 obs, +8 모션)

  모션 번호 = 동작 × 3 + 방향 (0 뒷모습, 1 옆모습(왼쪽), 2 앞모습, 3 = 옆모습 좌우반전 → +1).
  유닛 SetAction 0x10072820(동작 기준번호, 방향, 반복) 이 이 셈을 한다. 모션이 없으면 0/1(서기)로.
  work 실행 0x1007ca40: (+0x3f≠0 이면 준비 동작 핸들러 먼저) → work 번호 switch → work 별 핸들러.
"""
import argparse
import bisect
import os
import re
import struct
import sys

try:
    import capstone
    from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_REG_EAX, X86_REG_EDX
except ImportError:
    capstone = None

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))


# ---------------- 게임 파일 ----------------

class Game:
    def __init__(self, root):
        self.root = root
        self._idx = {}

    def read(self, folder, name):
        p = os.path.join(self.root, folder, name)
        if os.path.isfile(p):
            return open(p, 'rb').read()
        if folder not in self._idx:
            m = {}
            fdir = os.path.join(self.root, folder)
            for idx in [f for f in os.listdir(fdir) if f.lower().endswith('.idx')]:
                data = open(os.path.join(fdir, idx), 'rb').read()
                total = struct.unpack_from('<I', data, 0)[0] // 0x10000
                off = 6
                for _ in range(total):
                    rec = data[off:off + 36]
                    off += 36
                    fn = bytes(b ^ 0xFF for b in rec[14:23]).split(b'\0')[0].decode('cp949', 'replace')
                    pak = bytes(b ^ 0xFF for b in rec[23:36]).split(b'\0')[0].decode('cp949', 'replace')
                    _, s, _, e, _ = struct.unpack_from('<HIHIH', rec, 0)
                    m[fn.lower()] = (pak, s, e)
            self._idx[folder] = m
        ent = self._idx[folder].get(name.lower())
        if not ent:
            return None
        pak, s, e = ent
        with open(os.path.join(self.root, folder, pak), 'rb') as f:
            f.seek(s)
            return f.read(e - s + 1)


WORK_FIELDS = [(0x4, 'H'), (0x6, 'B'), (0x7, 'B'), (0x8, 'B'), (0xa, 'H'), (0xc, 'H')] + \
    [(o, 'B') for o in range(0xe, 0x19)] + [(0x1a, 'H'), (0x1c, 'H'), (0x1e, 'B'), (0x1f, 'B')]
for _i in range(3):
    WORK_FIELDS += [(0x20 + _i, 'B'), (0x24 + 2 * _i, 'H')]
WORK_FIELDS += [(0x2a, 'H'), (0x2c, 'B'), (0x2d, 'B'), (0x2e, 'H'), (0x30, 'H'), (0x32, 'H'), (0x34, 'H'),
                (0x36, 'B'), (0x38, 'H')] + [(o, 'B') for o in range(0x3a, 0x42)] + [(0x42, 'H')]
WORK_FMT = '<H' + ''.join(t for _, t in WORK_FIELDS)
WORK_FILES = ['%04d.att' % i for i in list(range(10)) + [11, 12]]
ABI_FMT = '<HHHHBHBBHBHHHHH'
ABI_FILES = ['%04d.abi' % i for i in (12, 14, 15, 16, 17, 18, 19, 20, 26)]


def load_works(g):
    works = {}
    size = struct.calcsize(WORK_FMT)
    for fn in WORK_FILES:
        d = g.read('Dat', fn)
        _, n, _ = struct.unpack_from('<3H', d)
        assert 6 + n * size + 2 == len(d), fn
        for i in range(n):
            v = struct.unpack_from(WORK_FMT, d, 6 + i * size)
            r = {o: x for (o, _), x in zip(WORK_FIELDS, v[1:])}
            r['file'] = fn
            works[v[0]] = r
    return works


def load_abis(g, works):
    abis = {}
    size = struct.calcsize(ABI_FMT)
    for fn in ABI_FILES:
        d = g.read('Abi', fn)
        _, n, _ = struct.unpack_from('<3H', d)
        assert 6 + n * size + 2 == len(d), fn
        for i in range(n):
            v = struct.unpack_from(ABI_FMT, d, 6 + i * size)
            abis[v[0]] = {'name': v[1], 'maxlv': v[2], 'file': fn, 'levels': {}}
    for wid, w in works.items():   # LoadWorkData: abi[w+4].levels[w+6] = wid
        if w[0x4] and w[0x6] and w[0x4] in abis:
            abis[w[0x4]]['levels'][w[0x6]] = wid
    return abis


def load_chr(g, n):
    b = g.read('Chr', '%04d.chr' % n)
    if not b or len(b) != 90:
        return None
    name, _, _, sprite, face = struct.unpack_from('<5H', b, 2)
    basic = struct.unpack_from('<H', b, 19)[0]
    abis = [struct.unpack_from('<2H', b, 56 + 4 * i) for i in range(8)]
    return {'name': name, 'sprite': sprite, 'face': face, 'basic': basic, 'abis': [a for a in abis if a[0]]}


def load_obs_motions(g, obs_id):
    d = g.read('Obs', '%04d.obs' % obs_id)
    if not d:
        return None
    _, n, _ = struct.unpack_from('<3H', d)
    o = 6 + 6 * n
    m, _ = struct.unpack_from('<2H', d, o)
    o += 4
    res = {}
    for i in range(m):
        mid, off = struct.unpack_from('<HI', d, o + 6 * i)
        p = off
        _, na, nb, length, nu = struct.unpack_from('<5H', d, p)
        p += 10 + 4 * nu
        keys = []
        for k in range(na + nb):
            f = struct.unpack_from('<3H10h', d, p)
            p += 26
            keys.append({'list': 'A' if k < na else 'B', 'kind': f[0], 'start': f[1], 'len': f[2], 'p': f[3:]})
        res[mid] = {'len': length, 'keys': keys}
    return res


def motion_frames(motions, mid):
    """모션의 그림 키(종류 0)를 (시작틱, 길이, 벌, 장) 로. 없으면 None."""
    m = motions.get(mid) if motions else None
    if m is None:
        return None
    return [(k['start'], k['len'], k['p'][0], k['p'][1]) for k in m['keys'] if k['list'] == 'B' and k['kind'] == 0]


def resolve_motion(motions, base, direction):
    """SetAction 0x10072820 과 같은 셈."""
    mid = (base // 3) * 3 + (1 if direction == 3 else direction)
    if motions is not None and mid >= max(motions) + 1:
        mid = 1 if direction == 3 else direction
    return mid


# ---------------- DLL ----------------

BASE = 0x10000000
SETACT = 0x10072820       # CUnit::SetAction(base, dir, repeat)
SETMOT = 0x100e53c0       # 애니메이터 PlayMotion(모션 번호 그대로)
EFFECT = 0x100c1fa0       # 이펙트 객체 생성자(Obs 번호, 모션, x, y, z, 맵, 유닛, work) → 0x100e5310 → SetObs 0x10026ed0
DISPATCH_SWITCH = 0x1007cb13
PRELUDE = {  # +0x3f 값 → (보통, 링크(+0x4f0) 유닛)
    1: (0x1007ddf0, 0x1007ddf0), 4: (0x1007ddf0, 0x1007ddf0),
    2: (0x1007def0, 0x1007db60), 5: (0x1007def0, 0x1007db60),
    3: (0x1007e140, 0x1007dc70), 6: (0x1007e140, 0x1007dc70),
    7: (0x1007e330, 0x1007dd30),
}


class Dll:
    def __init__(self, path):
        if capstone is None:
            raise SystemExit('capstone 이 필요하다: pip install capstone')
        self.d = open(path, 'rb').read()
        self.md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_32)
        self.md.detail = True
        self._targets = None
        self._cache = {}

    def u8(self, va):
        return self.d[va - BASE]

    def u32(self, va):
        return struct.unpack_from('<I', self.d, va - BASE)[0]

    def insn(self, va):
        return next(self.md.disasm(self.d[va - BASE:va - BASE + 16], va))

    def handler(self, wid):
        """0x1007ca40 의 work 번호 switch 를 그대로 따라가 핸들러 주소를 얻는다."""
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
                idx = edx if mm.index == X86_REG_EDX else eax
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
            else:
                raise RuntimeError('switch 해석 실패 %x %s %s' % (va, m, s))
            va += i.size
        raise RuntimeError('switch loop')

    def targets(self):
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

    def motions(self, start, depth=1):
        """함수(다음 call 대상까지 선형) 안의 SetAction/PlayMotion 호출. [(종류, 인자, 반복, VA)]"""
        key = (start, depth)
        if key in self._cache:
            return self._cache[key]
        t = self.targets()
        k = bisect.bisect_right(t, start)
        end = min(t[k] if k < len(t) else start + 0x1000, start + 0x4000)
        ins = list(self.md.disasm(self.d[start - BASE:end - BASE], start))
        pos = {x.address: n for n, x in enumerate(ins)}
        out = []

        def pushes_before(n, need):
            res = []
            for j in range(n - 1, max(-1, n - 40), -1):
                p = ins[j]
                if p.mnemonic == 'push':
                    res.append(p.op_str)
                if p.mnemonic in ('call', 'ret', 'jmp') or len(res) >= need:
                    break
            return res

        def val(x):
            try:
                return int(x, 16)
            except ValueError:
                return x

        for n, i in enumerate(ins):
            if i.mnemonic == 'call' and i.operands[0].type == X86_OP_IMM:
                tgt = i.operands[0].imm
                if tgt in (SETACT, SETMOT):
                    need = 3 if tgt == SETACT else 1
                    ps = pushes_before(n, need)
                    if ps:
                        out.append(('act' if tgt == SETACT else 'mot', val(ps[0]),
                                    val(ps[2]) if len(ps) > 2 else '', i.address))
                elif tgt == EFFECT:
                    ps = pushes_before(n, 2)
                    if len(ps) == 2:
                        out.append(('eff', val(ps[0]), val(ps[1]), i.address))
                elif depth > 0 and 0x10076000 <= tgt < 0x100c1f00 and tgt != 0x100762d0:
                    for r in self.motions(tgt, depth - 1):
                        out.append(r[:3] + (i.address,))
            elif i.mnemonic == 'jmp' and i.operands[0].type == X86_OP_IMM and i.operands[0].imm in pos:
                # push 인자; jmp 공용 호출자리  꼴
                j = pos[i.operands[0].imm]
                pc = 0
                for x in ins[j:j + 6]:
                    if x.mnemonic == 'push':
                        pc += 1
                    if x.mnemonic == 'call':
                        if x.operands[0].type == X86_OP_IMM and x.operands[0].imm in (SETACT, SETMOT) and pc == 0:
                            need = 3 if x.operands[0].imm == SETACT else 1
                            ps = pushes_before(n, need)
                            if ps:
                                out.append(('act' if x.operands[0].imm == SETACT else 'mot', val(ps[0]),
                                            val(ps[2]) if len(ps) > 2 else '', i.address))
                        break
        out.sort(key=lambda r: r[3])
        self._cache[key] = out
        return out


# ---------------- 출력 ----------------

def work_plan(dll, works, wid, linked=False):
    w = works.get(wid)
    plan = []
    if w and w[0x3f] in PRELUDE:
        h = PRELUDE[w[0x3f]][1 if linked else 0]
        plan.append(('준비(+0x3f=%d)' % w[0x3f], h, dll.motions(h)))
    h = dll.handler(wid)
    plan.append(('핸들러', h, dll.motions(h)))
    return plan


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('game')
    ap.add_argument('chrs', nargs='*', type=int)
    ap.add_argument('--work', nargs='*', type=int, default=[])
    ap.add_argument('--dir', type=int, default=1, help='방향 0 뒤, 1 옆, 2 앞, 3 옆(반전)')
    ap.add_argument('--frames', action='store_true', help='모션마다 그림(벌:장×틱) 목록도 찍기')
    a = ap.parse_args()
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass

    g = Game(a.game)
    dll = Dll(os.path.join(a.game, 'G3PartII.dll'))
    works = load_works(g)
    abis = load_abis(g, works)
    text = {}
    try:
        import extract_character as e
        for r in e.parse_txr(g.read('TXR', 'Txr.dat')):
            text.setdefault(r['txr_id'], r['text'])
    except Exception:
        pass

    def show_plan(wid, motions, indent='    '):
        w = works.get(wid)
        extra = '' if not w else ' (파일 %s, 어빌 %d 레벨 %d, +0x13=%d +0x3e=%d +0x3f=%d)' % (
            w['file'], w[0x4], w[0x6], w[0x13], w[0x3e], w[0x3f])
        print('%swork %d%s' % (indent, wid, extra if w else ' (att 에 없음)'))
        for label, h, calls in work_plan(dll, works, wid):
            effs = sorted({(arg, rep) for kind, arg, rep, va in calls if kind == 'eff'}, key=str)
            calls = [c for c in calls if c[0] != 'eff']
            desc = []
            for kind, arg, rep, va in calls:
                if kind == 'act' and isinstance(arg, int):
                    mid = resolve_motion(motions, arg, a.dir)
                    fr = motion_frames(motions, mid)
                    s = '동작%d(기준 %d→모션 %d, 반복 %s' % (arg // 3, arg, mid, rep)
                    if motions is None:
                        s += ')'
                    else:
                        s += ', 그림 없음)' if not fr else ', %d키 %d틱)' % (len(fr), motions[mid]['len'])
                elif kind == 'mot' and isinstance(arg, int):
                    fr = motion_frames(motions, arg)
                    s = '모션 %d 그대로' % arg + ('' if motions is None else '(%s)' % ('그림 없음' if not fr else '%d키' % len(fr)))
                else:
                    s = '%s(%s, 계산값)' % (kind, arg)
                desc.append(s + '@%x' % va)
            print('%s  %s 0x%x: %s' % (indent, label, h, ' → '.join(desc) if desc else '(몸짓 호출 없음)'))
            if effs:
                print('%s      이펙트(Obs, 모션): %s' % (indent, ' '.join('(%s,%s)' % e for e in effs)))
            if a.frames:
                for kind, arg, rep, va in calls:
                    if isinstance(arg, int):
                        mid = resolve_motion(motions, arg, a.dir) if kind == 'act' else arg
                        fr = motion_frames(motions, mid)
                        if fr:
                            print('%s      모션 %d: %s' % (indent, mid, ' '.join('%d:%d×%d' % (sb, sl, ln)
                                                                              for _, ln, sb, sl in fr)))

    for wid in a.work:
        show_plan(wid, None, '')
    for n in a.chrs:
        c = load_chr(g, n)
        if not c:
            print('Chr %04d 없음' % n)
            continue
        motions = load_obs_motions(g, c['sprite'])
        print('== Chr %04d %s  sprite %d (모션 %d개)' % (n, text.get(c['name'], c['name']), c['sprite'],
                                                     len(motions or {})))
        print('  기본공격 (chr +19):')
        show_plan(c['basic'], motions)
        for aid, lv in c['abis']:
            ab = abis.get(aid)
            nm = text.get(ab['name'], '') if ab else ''
            wid = ab['levels'].get(lv) if ab else None
            print('  어빌리티 %d %s 레벨 %d:' % (aid, nm, lv))
            if wid is None:
                print('    (abi 레벨→work 없음)')
            else:
                show_plan(wid, motions)


if __name__ == '__main__':
    main()

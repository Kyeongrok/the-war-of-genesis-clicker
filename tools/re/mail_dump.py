"""
창세기전3 파트2 — 모세스 메일(편지) 배달 규칙 검산 도구.

사용법:
    python mail_dump.py <게임 폴더> [--sav G3P_II20.sav] [--mails] [--chapters] [--scripts]
    (선택지를 하나도 안 주면 전부 찍는다)

게임 폴더는 읽기만 한다.

근거(G3PartII.dll, ImageBase 0x10000000) — 옵시디안 분석-모세스 「메일 배달과 진행 연동 (mo-mail)」:
  0x10102970  MAIL.DAT 로더. 머리 u16 ?, u16 N, u16 maxId. 레코드: id, 보낸이 Chr, 발신지 TXR, Bgm, len, 본문,
              조건 (깃발, 값, 연산자), 죽은 칸. 배열은 id 첨자.
  0x100f74b0  Chp 로더의 메일 표: u16 수(+0x2ee8), u16 여벌(+0x2eea, 아무도 안 읽음), (u16 방아쇠 +0x2ef0, u16 편지 +0x2eec)×수.
  0x100fc6e0  배달: **지금 챕터의 메일 표에 든 편지만** 훑어, 조건 깃발이 0xffff 면 무조건, 아니면
              0x100fda40(깃발값 byte 0x101b6050[깃발], 값, 연산자) 이 참이면 지금 파티 0x1004dd60(편지, 0).
  0x1004dd60  파티 우편함 넣기(+0xa98 수 u16, +0xa9a 편지 u16[512], +0xe9a 읽음 u8[512]). 이미 있으면 안 넣는다.
  0x100edc40  조건 503 [방아쇠]: 메일 표에서 방아쇠 → 편지, 지금 파티 우편함에 있고 읽음이면 참.
"""
import argparse
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, '..'))
import skill_motion as sm  # noqa: E402
from extract_character import parse_txr  # noqa: E402

OPS = ['==', '!=', '<', '<=', '>', '>=']
PARTY_SIZE = 0x10b0
CHR_SIZE = 0x3a4


def parse_mail(d):
    _, n, maxid = struct.unpack_from('<3H', d, 0)
    o = 6
    out = []
    for _ in range(n):
        mid, sender, place, bgm, ln = struct.unpack_from('<5H', d, o)
        body = d[o + 10:o + 10 + ln].split(b'\0')[0].decode('cp949', 'replace')
        o += 10 + ln
        flag, val, op, dead = struct.unpack_from('<4H', d, o)
        o += 8
        out.append(dict(id=mid, sender=sender, place=place, bgm=bgm, body=body, flag=flag, val=val, op=op, dead=dead))
    return out, o == len(d), maxid


def _cmds(b, o):
    n = struct.unpack_from('<h', b, o)[0]
    o += 2
    if n < 0:
        raise ValueError
    lst = []
    for _ in range(n):
        v = struct.unpack_from('<9h', b, o)
        o += 18
        lst.append((v[0], list(v[1:])))
    return lst, o


def _script(b, o):
    n = struct.unpack_from('<h', b, o)[0]
    o += 2
    if n < 0:
        raise ValueError
    ev = []
    for i in range(n):
        mx = struct.unpack_from('<h', b, o)[0]
        o += 2
        c, o = _cmds(b, o)
        a, o = _cmds(b, o)
        ev.append((i, mx, c, a))
    return ev, o


def parse_chp(b, headwords=24):
    """ChapterFile.cs 의 파서를 그대로 옮긴 것. (메일 표, 사건, 제목TXR, 정확히 끝났나)"""
    o = 0

    def W():
        nonlocal o
        v = struct.unpack_from('<h', b, o)[0]
        o += 2
        return v

    def cnt():
        n = W()
        W()
        if n < 0:
            raise ValueError
        return n
    head = [W() for _ in range(headwords)]
    n = cnt()
    o += 12 * n          # 성도 점
    n = cnt()
    o += 30 * n          # 인물
    n = cnt()
    o += 66 * n          # 항성계
    n = cnt()
    o += 84 * n          # 행성
    places = []
    for _ in range(cnt()):
        w = struct.unpack_from('<10h', b, o)
        o += 20
        places.append(w)
    tr = []
    for _ in range(cnt()):
        tr.append((W(), W()))
    ev, o = _script(b, o)
    return dict(head=head, places=places, mail=tr, events=ev, exact=o == len(b),
                title=head[23] if headwords == 24 else -1)


def load_chp(g, n):
    b = g.read('Chp', '%04d.chp' % n)
    if not b:
        return None
    best = None
    for hw in (24, 23):
        try:
            c = parse_chp(b, hw)
        except (ValueError, struct.error):
            continue
        if c['exact']:
            return c
        best = best or c
    return best


def parse_fld(b):
    o = 16
    head = struct.unpack_from('<8h', b, 0)
    o += 16 * head[6]
    n = struct.unpack_from('<h', b, o)[0]
    o += 4 + 16 * n
    n = struct.unpack_from('<h', b, o)[0]
    o += 4 + 10 * n
    ev, o = _script(b, o)
    return ev


def parse_btl(b):
    """BattleEvents.cs 배치: 머리 20B · nA×29 · nB×22 · nC×12 · 사건(최대, ?, 조건 수, 조건, 행동 수, 행동)."""
    o = 20
    for size in (29, 22, 12):
        n = struct.unpack_from('<H', b, o)[0]
        o += 4 + size * n
    nd = struct.unpack_from('<h', b, o)[0]
    o += 2
    ev = []
    for i in range(nd):
        mx, _, n1 = struct.unpack_from('<3h', b, o)
        o += 6
        cs = []
        for _ in range(n1):
            v = struct.unpack_from('<9h', b, o)
            o += 18
            cs.append((v[0], list(v[1:])))
        n2 = struct.unpack_from('<h', b, o)[0]
        o += 2
        acts = []
        for _ in range(n2):
            v = struct.unpack_from('<9h', b, o)
            o += 18
            acts.append((v[0], list(v[1:])))
        ev.append((i, mx, cs, acts))
    return ev


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('game')
    ap.add_argument('--sav')
    ap.add_argument('--mails', action='store_true')
    ap.add_argument('--chapters', action='store_true')
    ap.add_argument('--scripts', action='store_true')
    a = ap.parse_args()
    every = not (a.mails or a.chapters or a.scripts or a.sav)
    g = sm.Game(a.game)
    txr = {}
    for r in parse_txr(g.read('TXR', 'Txr.dat')):
        txr.setdefault(r['txr_id'], r['text'])

    def T(i):
        return txr.get(i, '?%d' % i).replace('\n', ' ')

    def chrname(n):
        c = sm.load_chr(g, n)
        return T(c['name']) if c else 'Chr%d' % n

    mails, exact, maxid = parse_mail(g.read('Dat', 'MAIL.DAT'))
    byid = {m['id']: m for m in mails}

    def cond(m):
        if m['flag'] == 0xffff:
            return '무조건'
        return '깃발%d %s %d' % (m['flag'], OPS[m['op']] if m['op'] < 6 else '?%d' % m['op'], m['val'])

    # 챕터 메일 표
    chps = {}
    for n in range(0, 100):
        c = load_chp(g, n)
        if c:
            chps[n] = c
    listed = {}
    for n, c in chps.items():
        for trig, mid in c['mail']:
            listed.setdefault(mid, []).append((n, trig))

    if every or a.mails:
        print('# MAIL.DAT: 편지 %d통, maxId %d, 파일 끝 일치 %s' % (len(mails), maxid, exact))
        print('id | 보낸이 | 발신지 | 조건 | 챕터(방아쇠) | 본문 앞')
        for m in mails:
            where = ', '.join('%d(%d)' % x for x in listed.get(m['id'], [])) or '-'
            print('%3d | %s | %s | %s | %s | bgm=%d dead=%d | %s' % (
                m['id'], chrname(m['sender']), T(m['place']), cond(m), where,
                struct.unpack('<h', struct.pack('<H', m['bgm']))[0], m['dead'], m['body'][:40].replace('\n', ' ')))

    # 깃발 설정 위치: 챕터·필드 스크립트의 102 [깃발, 값] / 103
    setters = {}
    fld = {}
    for n in range(0, 1000):
        b = g.read('Fld', '%04d.fld' % n)
        if not b:
            continue
        try:
            fld[n] = parse_fld(b)
        except (ValueError, struct.error):
            continue
    btl = {}
    for n in range(0, 1000):
        b = g.read('Btl', '%04d.btl' % n)
        if not b:
            continue
        try:
            btl[n] = parse_btl(b)
        except (ValueError, struct.error):
            continue
    for kind, src in (('Chp', chps.items()), ('Fld', ((k, {'events': v}) for k, v in fld.items())),
                      ('Btl', ((k, {'events': v}) for k, v in btl.items()))):
        for n, c in src:
            for i, mx, cs, acts in c['events']:
                for code, args in acts:
                    if code in (102, 103):
                        setters.setdefault(args[0], []).append('%s%d#%d:%d[%d]' % (kind, n, i, code, args[1]))

    if every or a.chapters:
        print('\n# 챕터별 메일 표 (방아쇠 → 편지)')
        for n, c in sorted(chps.items()):
            if not c['mail']:
                continue
            print('Chp %04d %s (정확 %s)' % (n, T(c['title']) if c['title'] >= 0 else '-', c['exact']))
            for trig, mid in c['mail']:
                m = byid.get(mid)
                if not m:
                    print('   방아쇠 %d → 편지 %d (MAIL.DAT 에 없음)' % (trig, mid))
                    continue
                s = setters.get(m['flag'], []) if m['flag'] != 0xffff else []
                print('   방아쇠 %3d → 편지 %3d  %s : %s  [%s]  깃발 세우는 곳: %s' % (
                    trig, mid, chrname(m['sender']), T(m['place']), cond(m), ' '.join(s[:6]) or '-'))
        never = [m for m in mails if m['id'] not in listed]
        print('\n# 어느 챕터 표에도 없는 편지(원본에서 절대 안 옴): %d통' % len(never))
        for m in never:
            print('   %3d %s : %s [%s] %s' % (m['id'], chrname(m['sender']), T(m['place']), cond(m),
                                            m['body'][:30].replace('\n', ' ')))

    if every or a.scripts:
        print('\n# 조건 503 쓰는 곳')
        for kind, src in (('Chp', chps.items()), ('Fld', ((k, {'events': v, 'mail': []}) for k, v in fld.items()))):
            for n, c in src:
                for i, mx, cs, acts in c['events']:
                    for code, args in cs:
                        if code == 503:
                            mid = [m for t, m in c.get('mail', []) if t == args[0]]
                            print('   %s %04d 사건 %d: 503 [%d] → 편지 %s ; 행동 %s' % (
                                kind, n, i, args[0], mid, [(x, y[:3]) for x, y in acts][:6]))
        print('\n# 조건 번호 분포(챕터/필드 전부)')
        hist = {}
        for n, c in chps.items():
            for _, _, cs, _ in c['events']:
                for code, _ in cs:
                    hist[('Chp', code)] = hist.get(('Chp', code), 0) + 1
        for n, ev in fld.items():
            for _, _, cs, _ in ev:
                for code, _ in cs:
                    hist[('Fld', code)] = hist.get(('Fld', code), 0) + 1
        print('  ', sorted(hist.items()))

    if a.sav or every:
        path = a.sav or next((f for f in os.listdir(a.game) if f.lower().endswith('.sav')), None)
        if path:
            path = path if os.path.isabs(path) else os.path.join(a.game, path)
            d = bytes(x ^ 0xFF for x in open(path, 'rb').read())
            v, ms, _, scene, arg = struct.unpack_from('<5I', d, 0)
            flags = d[22:22 + 2000]
            o = 22 + 2000
            nchr = struct.unpack_from('<H', d, o)[0]
            o += 2 + nchr * CHR_SIZE
            print('\n# 세이브 %s: 장면 %d 인자 %d' % (os.path.basename(path), scene, arg))
            print('  서 있는 깃발:', {i: flags[i] for i in range(2000) if flags[i]})
            for p in range(3):
                pb = d[o + p * PARTY_SIZE:o + (p + 1) * PARTY_SIZE]
                cntm = struct.unpack_from('<h', pb, 0xa98)[0]
                ids = struct.unpack_from('<%dh' % max(cntm, 0), pb, 0xa9a)
                rd = pb[0xe9a:0xe9a + max(cntm, 0)]
                print('  파티 %d 우편함 %d통:' % (p, cntm))
                for mid, r in zip(ids, rd):
                    m = byid.get(mid)
                    print('     편지 %3d 읽음 %d  %s : %s [%s] 챕터 %s' % (
                        mid, r, chrname(m['sender']) if m else '?', T(m['place']) if m else '?',
                        (cond(m) + (' 지금 %d' % flags[m['flag']] if m['flag'] != 0xffff else '')) if m else '?',
                        listed.get(mid)))
            cur = struct.unpack_from('<H', d, o + 3 * PARTY_SIZE)[0]
            print('  지금 파티', cur)


if __name__ == '__main__':
    main()

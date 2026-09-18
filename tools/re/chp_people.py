"""
창세기전3 파트2 — Chp 챕터 파일의 "인물"(통신 MESSAGE 페이지에 걸어다니는 사람) 레코드를 풀고,
그 사람의 대사를 Tlk\\%04d.Tlc 에서 꺼내 보여 준다.

사용법:
    python chp_people.py <게임 폴더 또는 풀어 놓은 폴더> [챕터번호 ...]

예:
    python chp_people.py "C:\\Users\\ocean\\Desktop\\창세기전3 파트2" 11 12 61

게임 폴더는 읽기만 한다. Chp/Tlk 가 .pak 로 묶여 있으면 tools/re/pak_extract.py 로 먼저 풀어야 한다
(이 도구는 풀어 놓은 폴더도 그대로 받는다).

근거(G3PartII.dll, ImageBase 0x10000000):
  0x100f6cb0  Chp 로더. 인물은 `fread(mem, 0x1e, 1, f)` 로 **파일 30바이트를 메모리 0x26 레코드 앞머리에 통째로** 넣는다
              → 파일 오프셋 = 메모리 오프셋 (0x00~0x1d). 그 뒤 +0x1e=0, +0x20=0, +0x22=rand()%12.
  0x100fc2b0  통신 페이지. 사람 그림 = Obs 1330 모션 `+0x22`(무작위), 이름 = Chr(`+0x02`) 의 이름 TXR.
  0x100ff0d4  사람을 누르면. 대사 번호 = `+0x10/+0x12/+0x14` 중 `+0x1e` 번째,
              **글은 `0x1004a650(번호)` = Tlk 표**에서 꺼낸다(TXR `0x1004a3f0` 아님).
  0x1004a420  Tlk 로더. 챕터 장면은 갈래 4 = `Tlk\\%04d.Tlc`(0x100f5839), 챕터 번호로 연다.
"""
import os
import struct
import sys

PERSON_SIZE = 30


def _read(path):
    with open(path, 'rb') as f:
        return f.read()


def _find(root, folder, name):
    """게임 폴더든 풀어 놓은 폴더든 <root>/<folder>/<name> 을 찾는다."""
    p = os.path.join(root, folder, name)
    if os.path.exists(p):
        return p
    for cand in (os.path.join(root, folder.lower(), name),
                 os.path.join(root, folder.upper(), name)):
        if os.path.exists(cand):
            return cand
    return None


def parse_text_table(data):
    """Txr.dat 과 Tlk\\*.Tlc 는 같은 서식이다(0x1004a190 / 0x1004a420).
    머리 10바이트(u16 ?, u16 개수 N, u16 최대번호, u32 글뭉치 크기) + N×(u16 번호, u32 위치, u32 길이),
    글뭉치는 파일 오프셋 10*(N+1) 부터.
    게임은 길이를 안 쓴다 — `0x1004a3f0`/`0x1004a650` 은 글뭉치+위치 를 그냥 C 문자열로 돌려준다.
    그래서 여기서도 널(0) 까지만 자른다(TXR 은 길이 칸 뒷쪽 2바이트가 무리 번호라 길이로 자르면 틀린다)."""
    _, n, _maxid, blob_size = struct.unpack_from('<HHHI', data, 0)
    base = 10 * (n + 1)
    out = {}
    for i in range(n):
        tid, off, _ln = struct.unpack_from('<HII', data, 10 + i * 10)
        raw = data[base + off:]
        z = raw.find(b'\0')
        if z >= 0:
            raw = raw[:z]
        try:
            out[tid] = raw.decode('cp949')
        except Exception:
            out[tid] = repr(raw)
    return out, base + blob_size


def _u16(data, off):
    return struct.unpack_from('<h', data, off)[0]


def parse_chp(data):
    """Chp 파일을 앞에서부터 읽어 인물 절까지 간다. 나머지 절도 세어 파일 끝과 맞는지 본다."""
    o = 0

    def w():
        nonlocal o
        v = _u16(data, o)
        o += 2
        return v

    head = [w() for _ in range(5)]           # ?, 제목TXR(+0x100), BGM(+0x178), +0x102, +0x104
    o += 16                                  # 목록 A (8워드, +0x126)
    o += 16                                  # 목록 B (8워드, +0x138)
    head += [w(), w(), w()]                  # +0x2e40, +0x2e42, +0x116
    n_land = w()
    w()                                      # +0x114
    landmarks = []
    for _ in range(n_land):
        landmarks.append(struct.unpack_from('<6h', data, o))
        o += 12
    n_person = w()
    w()                                      # +0x110
    people_off = o
    people = []
    for _ in range(n_person):
        people.append(struct.unpack_from('<15h', data, o))
        o += PERSON_SIZE
    n_star = w()
    w()
    stars_off = o
    stars = []
    for _ in range(n_star):
        head8 = struct.unpack_from('<8h', data, o)
        planets = [v for v in struct.unpack_from('<8h', data, o + 16) if v >= 0]
        misc = [v for v in struct.unpack_from('<8h', data, o + 32) if v >= 0]
        persons = [v for v in struct.unpack_from('<8h', data, o + 48) if v >= 0]
        bgr = _u16(data, o + 64)
        stars.append({'head': head8, 'planets': planets, 'misc': misc,
                      'persons': persons, 'bgr': bgr, 'off': o})
        o += 66
    n_planet = w()
    w()
    o += n_planet * 84
    n_place = w()
    w()
    o += n_place * 20
    return {
        'head': head, 'landmarks': landmarks,
        'people_off': people_off, 'people': people,
        'stars_off': stars_off, 'n_star': n_star, 'stars': stars,
        'n_planet': n_planet, 'n_place': n_place,
        'end': o, 'size': len(data),
    }


FIELDS = [
    (0x00, '번호'), (0x02, 'Chr'), (0x04, '?4'), (0x06, '?6'), (0x08, '연출Obs'),
    (0x0a, '입모양'), (0x0c, '?c'), (0x0e, '?e'),
    (0x10, '대사1'), (0x12, '대사2'), (0x14, '대사3'),
    (0x16, '조건변수'), (0x18, '조건값'), (0x1a, '조건연산'), (0x1c, '?1c'),
]


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    root = sys.argv[1]
    nums = [int(x) for x in sys.argv[2:]] or None
    chp_dir = None
    for cand in ('Chp', 'chp', 'CHP'):
        if os.path.isdir(os.path.join(root, cand)):
            chp_dir = os.path.join(root, cand)
            break
    if chp_dir is None:
        print('Chp 폴더가 없다:', root)
        return 1
    names = sorted(f for f in os.listdir(chp_dir) if f.lower().endswith('.chp'))
    if nums:
        names = [f for f in names if int(f[:4]) in nums]
    for name in names:
        num = int(name[:4])
        try:
            chp = parse_chp(_read(os.path.join(chp_dir, name)))
        except Exception as exc:
            print('=' * 78)
            print('Chp %04d  못 읽음(속 빈 챕터로 보인다): %s' % (num, exc))
            continue
        tlc = _find(root, 'Tlk', '%04d.tlc' % num) or _find(root, 'Tlk', '%04d.Tlc' % num)
        texts = {}
        if tlc:
            try:
                texts, _ = parse_text_table(_read(tlc))
            except Exception:
                tlc = None
        print('=' * 78)
        print('Chp %04d  인물 %d명 (파일 오프셋 %d)  파일 %d바이트  Tlk %s (글 %d개)'
              % (num, len(chp['people']), chp['people_off'], chp['size'],
                 '없음' if not tlc else os.path.basename(tlc), len(texts)))
        for si, st in enumerate(chp['stars']):
            print('  항성계 %d (파일+%d): 이름TXR %d, 배경 Bgr %d, 행성 %s, ?목록 %s, 인물 %s'
                  % (si, st['off'], st['head'][2], st['bgr'], st['planets'], st['misc'], st['persons']))
        for i, rec in enumerate(chp['people']):
            off = chp['people_off'] + i * PERSON_SIZE
            print('  [%d] 파일+%d %s' % (i, off, list(rec)))
            for k in (0x10, 0x12, 0x14):
                tid = rec[k // 2]
                if tid < 0:
                    continue
                t = texts.get(tid)
                t = '<없음>' if t is None else t.replace('\n', '\\n')
                print('       대사 %-5d %s' % (tid, t))
    return 0


if __name__ == '__main__':
    sys.exit(main())

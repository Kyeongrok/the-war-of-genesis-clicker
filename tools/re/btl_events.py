"""전투 파일(Btl)의 이벤트 스크립트를 조건·행동 이름과 함께 풀어 본다 (ba-9).

쓰기:
    python tools/re/btl_events.py "<게임 폴더>" 45          # 전투 하나
    python tools/re/btl_events.py "<게임 폴더>" --all        # 전부 훑어 통계
    python tools/re/btl_events.py "<게임 폴더>" --end        # 전투를 끝내는 이벤트만

근거(DLL, ImageBase 0x10000000):
  이벤트 검사  0x10056770(갈래)  — 이벤트마다 발동 갈래(+0xe)가 0 이거나 갈래와 같으면 조건을 본다.
                 갈래 1 = 레벨업 상태 끝(0x10068270), 2 = 행동 끝(0x100680a3, 0x1006a447), 3 = 새 턴(0x10067d5c).
  조건 판정    0x10056400 — 발동 횟수(+0x14)가 최대(+0xc, 0=무제한)에 닿으면 건너뜀. 조건은 모두 참이어야 한다(AND).
                 코드→함수: 1~201 표 0x10056598(바이트 0x100565bc), 202 = 0x1004f6c0, 203~458 표 0x10056688(바이트 0x100566a8)
  행동 실행    0x100567f0 — 코드→함수: 6~208 표 0x10056b7c(바이트 0x10056bb8, 색인 = 코드−6)
  전투 결과    행동 11 = [장면+0xa4] = 인자0+1, 10 = 결과 5(다음 전투), 6 = 결과 6(필드)  → 0x10050c80 / 0x10050c40 / 0x10050c00
"""
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from btl_dump import read_game_file, load_txr  # noqa: E402

COND = {
    1: "턴 수 == 2 (0x1004f210)",
    2: "전역 변수 0x101b6aa0[인자0] 비교 (0x1004f230)",
    3: "턴 수([CBattle+0x4cf0]) 비교 (0x1004f260)",
    100: "바이트 전역 0x101b69a0[인자0] 비교 (0x1004f290)",
    101: "게임 플래그 [CBattle+0xa4+인자0] 비교 (0x1004f2c0)",
    102: "그 캐릭터가 파티에 있나 (0x1004f2f0)",
    200: "유닛 사망 / 부대 전멸·생존 (0x1004f360)",
    201: "유닛(부대 전원)이 살아 있고 HP>0 (0x1004f550)",
    202: "유닛의 부대 번호 비교 (0x1004f6c0)",
    203: "미해석 (0x1004f710)",
    204: "미해석 (0x1004f890)",
    300: "미해석 (0x1004fac0)",
    301: "미해석 — 유닛 사이 관계로 보임 (0x100502d0)",
    400: "미해석 (0x10050710)",
    401: "부대 전멸 (0x10050810)",
    402: "위치 도달 (0x10050880)",
}
ACT = {
    6: "전투 끝 → 필드 인자0 (결과 6)",
    10: "전투 끝 → 다음 전투 인자0 (결과 5)",
    11: "전투 결과 = 인자0+1 (0 승리 / 3 패배)",
    100: "바이트 전역 0x101b69a0[인자0] = 인자1",
    101: "바이트 전역 0x101b69a0[인자0] 사칙연산",
    102: "게임 플래그 [CBattle+0xa4+인자0] = 인자1",
    103: "게임 플래그 사칙연산",
    200: "미해석 (0x10050eb0)",
    201: "미해석 (0x100519c0)",
    600: "대사 (0x10056885 갈래)",
    601: "대사",
}
RESULT = {1: "승리 → 장면 4 챕터", 2: "→ 타이틀", 3: "→ 타이틀", 4: "패배 → 장면 6 타이틀",
          5: "다음 전투", 6: "필드", 7: "전투 다시하기", 8: "불러오기", 9: "타이틀", 10: "네트워크"}


def parse(d):
    hdr = struct.unpack_from("<12h", d, 0)
    o = 24 + 29 * hdr[10]
    b = struct.unpack_from("<H", d, o)[0]; o += 4 + 22 * b
    c = struct.unpack_from("<H", d, o)[0]; o += 4 + 12 * c
    n = struct.unpack_from("<H", d, o)[0]; o += 2
    evs = []
    for _ in range(n):
        mx, kind = struct.unpack_from("<2H", d, o); o += 4
        conds = []
        n1 = struct.unpack_from("<H", d, o)[0]; o += 2
        for _ in range(n1):
            conds.append((struct.unpack_from("<H", d, o)[0], struct.unpack_from("<8h", d, o + 2))); o += 18
        n2 = struct.unpack_from("<H", d, o)[0]; o += 2
        acts = []
        for _ in range(n2):
            acts.append((struct.unpack_from("<H", d, o)[0], struct.unpack_from("<8h", d, o + 2))); o += 18
        evs.append((mx, kind, conds, acts))
    return hdr, evs, o


def subject(v):
    """조건 200/201 의 인자0 — 대상 고르는 법 (0x1004eaa0, 0x1004f3bd)."""
    if 0 < v < 10000:
        return "Chr %d" % v
    if 10000 <= v < 20000:
        return "Btl 순번 %d" % (v - 10000)
    if 20000 <= v <= 20009:
        team = [4, 4, 3, 3, 0, 0, 1, 1, 2, 2][v - 20000]
        return "부대 %d %s" % (team, "생존" if (v - 20000) % 2 else "전멸")
    return "값 %d" % v


def names(game):
    try:
        return load_txr(game)
    except Exception:
        return {}


def show(game, num, txr):
    d = read_game_file(game, "Btl", "%04d.btl" % num)
    if d is None:
        print("Btl %04d 없음" % num); return
    try:
        hdr, evs, o = parse(d)
    except Exception as e:
        print("Btl %04d 배치 안 맞음: %s" % (num, e)); return
    print("== Btl %04d  %s" % (num, txr.get(hdr[4], "?")))
    print("   승리 조건 글: %s / 패배 조건 글: %s" % (txr.get(hdr[5], "?"), txr.get(hdr[6], "?")))
    print("   머리 워드 9 = %d  (1 이면 엔진 기본 전멸 판정 0x1006ed10 을 켠다)" % hdr[9])
    print("   BGM %d,  파일 끝 %s" % (hdr[8], "일치" if o == len(d) else "안 맞음(%d/%d)" % (o, len(d))))
    for i, (mx, kind, cs, as_) in enumerate(evs):
        print("  이벤트 %d  최대 발동 %s  갈래 %d" % (i, "무제한" if mx == 0xFFFF else mx, kind))
        for c, p in cs:
            extra = "  대상: " + subject(p[0]) if c in (200, 201) else ""
            print("    조건 %-4d %-46s 인자 %s%s" % (c, COND.get(c, "?"), list(p[:4]), extra))
        for a, p in as_:
            extra = ""
            if a == 11:
                extra = "  → 결과 %d (%s)" % (p[0] + 1, RESULT.get(p[0] + 1, "?"))
            print("    행동 %-4d %-46s 인자 %s%s" % (a, ACT.get(a, "?"), list(p[:4]), extra))


def scan(game, only_end=False):
    import collections
    ca, cc, w9 = collections.Counter(), collections.Counter(), collections.Counter()
    ok = bad = 0
    for num in range(0, 400):
        d = read_game_file(game, "Btl", "%04d.btl" % num)
        if d is None:
            continue
        try:
            hdr, evs, o = parse(d)
            assert o == len(d)
        except Exception:
            bad += 1; continue
        ok += 1
        w9[hdr[9]] += 1
        for mx, kind, cs, as_ in evs:
            for c, p in cs:
                cc[c] += 1
            for a, p in as_:
                ca[a] += 1
                if only_end and a in (6, 10, 11):
                    print("Btl %04d  행동 %d 인자 %d  ← 조건 %s"
                          % (num, a, p[0], [(c, q[0]) for c, q in cs]))
    print("파일 %d 개 배치 일치, %d 개 안 맞음" % (ok, bad))
    print("머리 워드 9: %s" % dict(w9))
    print("조건 코드: %s" % sorted(cc.items()))
    print("행동 코드: %s" % sorted(ca.items()))


def main():
    if len(sys.argv) < 3:
        print(__doc__); return
    game = sys.argv[1]
    txr = names(game)
    if sys.argv[2] == "--all":
        scan(game)
    elif sys.argv[2] == "--end":
        scan(game, True)
    else:
        for a in sys.argv[2:]:
            show(game, int(a), txr)


if __name__ == "__main__":
    main()

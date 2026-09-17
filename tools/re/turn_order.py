"""
창세기전3 파트2 — 전투의 턴(행동 순서)을 G3PartII.dll 규칙대로 흉내 내어 보여 준다. (분석 과제 ba-2)

사용법:
    python turn_order.py <게임 폴더> <Btl 번호> [틱 수=20]
    예) python turn_order.py "C:/Users/ocean/Desktop/창세기전3 파트2" 45 30

게임 폴더는 읽기만 한다(.idx/.pak 을 메모리에서 푼다, 낱장 파일이 우선).

DLL 에서 확인한 규칙(자세한 근거는 옵시디안 분석-전투.md "턴 결정 (ba-2)"):
  - 유닛 배열 [CBattle+0x3c6c] 순서 = Btl 레코드 순서(0x100630d0 루프가 레코드마다 유닛을 만든다).
  - Btl 레코드(29바이트, 0x18 부터): u16 번호, u16 Chr, u16 x, u16 y, u8 방향, u16 세력(+0x78, 4 = 플레이어) ...
  - TP 최대 = CChr+0x3e(+장비 보너스 0x21, +유닛 +0x4d0)   (0x1007aeb0)
  - STP(틱당 TP 회복) = TP최대 / CChr+0x40            (0x1007acf0, 0x10071db0)
  - 틱(=화면 'Turn', [CBattle+0x4cf0]) 이 넘어갈 때: 모든 유닛 TP += STP (최대값에서 자름),
    그다음 TP == TP최대 인 유닛에만 행동 플래그(+0xfe)를 세운다(0x1006da40 → 0x100743b0).
  - 플래그 선 유닛을 배열 앞에서부터 하나씩 고른다(0x1006db20). 고른 유닛은 TP 가 0 이하가 되거나
    Rest(명령 0x2716)로 남은 TP 를 모두 버릴 때까지 계속 행동한다.
  - 전투 시작 때 TP 는 가득 차 있다고 본다(가설: 0x1007a8e0 레벨 설정).

흉내에서 뺀 것: 레벨(Lev.dat) 성장, 장비·어빌리티 보너스, 상태이상(5·6번은 차례를 건너뜀, 매 틱 3% 로 풀림),
TP 38번 상태 보너스. 행동은 '한 번에 TP 를 다 쓰고 끝낸다'로 단순화했다.
"""
import os
import struct
import sys


def read_packed(game_root, folder, name):
    """낱장 → .idx/.pak 순서로 파일 하나를 찾아 bytes 로 돌려준다."""
    src = os.path.join(game_root, folder)
    p = os.path.join(src, name)
    if os.path.isfile(p):
        return open(p, 'rb').read()
    for idx in [f for f in os.listdir(src) if f.lower().endswith('.idx')]:
        data = open(os.path.join(src, idx), 'rb').read()
        total = struct.unpack_from('<I', data, 0)[0] // 0x10000
        off = 6
        for _ in range(total):
            rec = data[off:off + 36]
            off += 36
            fn = bytes(b ^ 0xFF for b in rec[14:23]).split(b'\0')[0].decode('cp949', 'replace')
            if fn.lower() != name.lower():
                continue
            pak = bytes(b ^ 0xFF for b in rec[23:36]).split(b'\0')[0].decode('cp949', 'replace')
            _, start, _, end, _ = struct.unpack_from('<HIHIH', rec, 0)
            with open(os.path.join(src, pak), 'rb') as f:
                f.seek(start)
                return f.read(end + 1 - start)
    return None


def load_units(game_root, btl_no):
    b = read_packed(game_root, 'Btl', '%04d.btl' % btl_no)
    if b is None:
        raise SystemExit('Btl %04d 없음' % btl_no)
    count = struct.unpack_from('<H', b, 0x14)[0]
    units = []
    for i in range(count):
        o = 0x18 + 29 * i
        rid, chr_no, x, y = struct.unpack_from('<4H', b, o)
        force = struct.unpack_from('<H', b, o + 9)[0]
        c = read_packed(game_root, 'Chr', '%04d.chr' % chr_no)
        if c is None or len(c) < 39:
            tp, div = 0, 0xff
        else:
            stats = struct.unpack_from('<6H', c, 27)   # +0x3c PSY, +0x3e TP, +0x40 (TP 충전 제수), +0x42 CTP, +0x44 DEP, +0x46 DEX
            tp, div = stats[1], stats[2]
        units.append({'idx': i, 'chr': chr_no, 'x': x, 'y': y, 'force': force,
                      'tpmax': tp, 'div': div, 'tp': tp})
    return units


def simulate(units, ticks):
    lines = []
    for tick in range(1, ticks + 1):
        if tick > 1:
            for u in units:                                   # 0x100eaad0 → 가상함수 +0xb0 → 0x10071db0
                if u['div'] and u['div'] != 0xff:
                    u['tp'] = min(u['tpmax'], u['tp'] + u['tpmax'] // u['div'])
        ready = [u for u in units if u['div'] != 0xff and u['tpmax'] > 0 and u['tp'] == u['tpmax']]  # 0x100743b0
        order = []
        for u in ready:                                       # 0x1006db20: 배열 앞에서부터
            order.append('#%d(Chr%04d,%s)' % (u['idx'], u['chr'], '아군' if u['force'] == 4 else '세력%d' % u['force']))
            u['tp'] = 0                                       # 행동 후 Rest 로 끝냈다고 가정
        lines.append('Turn %3d: %s' % (tick, ' → '.join(order) if order else '(아무도 없음, 바로 다음 틱)'))
    return lines


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(1)
    sys.stdout.reconfigure(encoding='utf-8')
    game_root, btl_no = sys.argv[1], int(sys.argv[2])
    ticks = int(sys.argv[3]) if len(sys.argv) > 3 else 20
    units = load_units(game_root, btl_no)
    print('Btl %04d 유닛 (배열 순서 = 행동 우선순위)' % btl_no)
    print(' idx  Chr   세력  (x,y)      TP최대  제수  STP(틱당)  0에서 가득까지 틱')
    for u in units:
        stp = u['tpmax'] // u['div'] if u['div'] not in (0, 0xff) else 0
        need = -(-u['tpmax'] // stp) if stp else '-'
        print(' %3d  %04d  %4d  (%2d,%2d)   %6d  %4d  %9d  %s' % (u['idx'], u['chr'], u['force'], u['x'], u['y'],
                                                              u['tpmax'], u['div'], stp, need))
    print()
    for line in simulate(units, ticks):
        print(line)


if __name__ == '__main__':
    main()

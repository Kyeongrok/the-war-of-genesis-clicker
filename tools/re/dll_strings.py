"""
G3PartII.dll 에서 ASCII 문자열 중 주어진 낱말을 품은 것을 찾고, 코드에서 그 주소를 4바이트로 참조하는 자리를 보여 준다.

사용법:
    python dll_strings.py <G3PartII.dll> <낱말> [낱말 ...]
    예) python dll_strings.py G3PartII.dll "chr\\" "dat\\" Load

로더 함수는 보통 파일 이름 서식(`Chr\\%04d.chr`)이나 "... Not Found" 오류 문자열을 참조하므로,
나온 참조 주소 근처를 objdump 로 펼치면 파일을 읽는 순서를 따라갈 수 있다.
"""
import re
import struct
import sys

import pefile

path = sys.argv[1]
words = [w.encode() for w in sys.argv[2:]]
pe = pefile.PE(path)
d = open(path, 'rb').read()
base = pe.OPTIONAL_HEADER.ImageBase


def off2va(off):
    for s in pe.sections:
        if s.PointerToRawData <= off < s.PointerToRawData + s.SizeOfRawData:
            return base + s.VirtualAddress + off - s.PointerToRawData
    return None


for m in re.finditer(rb'[\x20-\x7e]{3,}\x00', d):
    s = m.group()[:-1]
    if not any(w.lower() in s.lower() for w in words):
        continue
    va = off2va(m.start())
    if va is None:
        continue
    refs = [off2va(r.start()) for r in re.finditer(re.escape(struct.pack('<I', va)), d)]
    print(hex(va), s.decode(), [hex(r) for r in refs if r][:6])

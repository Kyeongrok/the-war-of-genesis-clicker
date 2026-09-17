"""G3PartII.dll 에서 CP949 한글 문자열을 뽑고, 코드에서 그 주소를 참조하는 자리를 찾는다."""
import re
import struct
import sys

import pefile

path = sys.argv[1]
pe = pefile.PE(path)
d = open(path, 'rb').read()
base = pe.OPTIONAL_HEADER.ImageBase


def off2va(off):
    for s in pe.sections:
        if s.PointerToRawData <= off < s.PointerToRawData + s.SizeOfRawData:
            return base + s.VirtualAddress + off - s.PointerToRawData
    return None


pat = re.compile(rb'(?:[\x81-\xfe][\x41-\xfe]|[\x20-\x7e]){2,}\x00')
out = open(sys.argv[2], 'w', encoding='utf-8')
for m in pat.finditer(d):
    raw = m.group()[:-1]
    if not re.search(rb'[\x81-\xfe][\x41-\xfe]', raw):
        continue
    try:
        text = raw.decode('cp949')
    except UnicodeDecodeError:
        continue
    va = off2va(m.start())
    refs = [off2va(r.start()) for r in re.finditer(re.escape(struct.pack('<I', va)), d)] if va else []
    out.write(f'{va:#x}\t{text}\t{" ".join(hex(r) for r in refs if r)}\n')

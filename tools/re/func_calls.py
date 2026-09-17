"""함수 호출 나무 보기 (G3PartII.dll 정적 분석용).

주어진 VA 의 함수 본문을 점프를 따라가며 디스어셈블해서
- 부르는 함수(직접 call) 목록을 순서대로,
- push/mov 로 쓰는 문자열 상수(ASCII·CP949),
- 할당 크기(push N; call operator new 0x10134570)
를 뽑는다. --depth 로 몇 단계 아래까지 펼칠지 정한다.

쓰기:
    python tools/re/func_calls.py "<게임 폴더>" 0x10061610 [--depth 2] [--skip 0x10006090,0x10134570]

게임 폴더 안의 G3PartII.dll 을 읽는다(읽기만 함).
"""
import argparse
import os
import struct
import sys

import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
from capstone.x86 import X86_OP_IMM, X86_OP_MEM

NEW_FN = 0x10134570
FREAD_FN = 0x10006090


def load(dll_path):
    pe = pefile.PE(dll_path)
    base = pe.OPTIONAL_HEADER.ImageBase
    img = pe.get_memory_mapped_image()
    return pe, base, img


def cstr(img, base, va):
    off = va - base
    if off < 0 or off >= len(img):
        return None
    end = img.find(b"\0", off, off + 200)
    if end <= off + 2:
        return None
    raw = img[off:end]
    try:
        s = raw.decode("cp949")
    except UnicodeDecodeError:
        return None
    if all(ch.isprintable() or ch in "\r\n\t" for ch in s):
        return s
    return None


def walk(md, img, base, start, limit=20000):
    """점프를 따라 함수 본문 명령어를 모은다(주소순)."""
    seen = {}
    todo = [start]
    while todo and len(seen) < limit:
        addr = todo.pop()
        while addr not in seen:
            off = addr - base
            code = img[off:off + 16]
            ins = next(md.disasm(code, addr), None)
            if ins is None:
                break
            seen[addr] = ins
            m = ins.mnemonic
            if m == "ret" or m == "int3":
                break
            if m.startswith("j"):
                op = ins.operands[0] if ins.operands else None
                if op is not None and op.type == X86_OP_IMM:
                    todo.append(op.imm)
                if m == "jmp":
                    break
            addr += ins.size
    return [seen[a] for a in sorted(seen)]


def summarize(md, img, base, va):
    items = []
    prev_push = None
    pushes = []
    for ins in walk(md, img, base, va):
        m = ins.mnemonic
        if m == "push" and ins.operands and ins.operands[0].type == X86_OP_IMM:
            imm = ins.operands[0].imm & 0xFFFFFFFF
            s = cstr(img, base, imm) if imm > base else None
            if s:
                items.append((ins.address, "str", s))
            pushes.append(imm)
        elif m == "push":
            pushes.append(None)
        elif m == "call":
            op = ins.operands[0]
            if op.type == X86_OP_IMM:
                tgt = op.imm & 0xFFFFFFFF
                if tgt == NEW_FN and pushes and pushes[-1] is not None:
                    items.append((ins.address, "new", pushes[-1]))
                elif tgt == FREAD_FN:
                    sz = pushes[-2] if len(pushes) >= 2 else None
                    items.append((ins.address, "fread", sz))
                else:
                    items.append((ins.address, "call", tgt))
            else:
                items.append((ins.address, "icall", ins.op_str))
            pushes = []
        elif m == "mov" and len(ins.operands) == 2 and ins.operands[1].type == X86_OP_IMM:
            imm = ins.operands[1].imm & 0xFFFFFFFF
            s = cstr(img, base, imm) if imm > base else None
            if s:
                items.append((ins.address, "str", s))
    return items


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("game")
    ap.add_argument("va")
    ap.add_argument("--depth", type=int, default=1)
    ap.add_argument("--skip", default="")
    ap.add_argument("--no-fread", action="store_true", help="fread 줄 숨김")
    a = ap.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    pe, base, img = load(os.path.join(a.game, "G3PartII.dll"))
    md = Cs(CS_ARCH_X86, CS_MODE_32)
    md.detail = True
    skip = {int(x, 16) for x in a.skip.split(",") if x}
    done = set()

    def rec(va, d, ind):
        for addr, kind, val in summarize(md, img, base, va):
            if kind == "fread" and a.no_fread:
                continue
            if kind == "call":
                mark = "" if val not in done else " (위에 나옴)"
                print(f"{ind}{addr:#x} call {val:#x}{mark}")
                if d > 1 and val not in skip and val not in done:
                    done.add(val)
                    rec(val, d - 1, ind + "    ")
            elif kind == "str":
                print(f"{ind}{addr:#x} str  {val!r}")
            elif kind == "new":
                print(f"{ind}{addr:#x} new  {val:#x}")
            elif kind == "fread":
                print(f"{ind}{addr:#x} fread size={val}")
            else:
                print(f"{ind}{addr:#x} icall {val}")

    rec(int(a.va, 16), a.depth, "")


if __name__ == "__main__":
    main()

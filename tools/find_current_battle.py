"""
창세기전3 파트2 - 지금 게임이 띄워 놓은 전투(.btl) 찾기

사용법:
    1) 게임을 실행해서 알아보고 싶은 전투 화면(배치 화면)까지 간다.
    2) python find_current_battle.py [게임 폴더]

동작:
    Btl/*.btl (낱개 파일 + Btl.idx/Btl.pak 안의 것 전부)의 내용을 읽어 두고,
    실행 중인 게임 프로세스 메모리를 훑어서 그 바이트열이 통째로 들어 있는 전투를 찾는다.
    .btl 274개는 내용이 모두 달라서(가장 짧은 것 42바이트) 들어맞으면 그 전투다.
    이전에 불러왔던 전투가 풀린 메모리에 남아 있을 수도 있으니, 여러 개가 나오면
    전투를 막 시작했을 때 다시 돌려서 겹치는 것을 본다.
"""
import ctypes
import ctypes.wintypes as wt
import os
import struct
import sys

GAME_ROOT = r"C:/Users/ocean/Desktop/창세기전3 파트2"
PROCESS_HINTS = ("창세기전3 파트2 실행파일.exe", "g3partii", "g3p", "dosbox")


def load_btl_files(game_root):
    folder = os.path.join(game_root, "Btl")
    files = {}
    data = open(os.path.join(folder, "Btl.idx"), "rb").read()
    total = struct.unpack_from("<I", data, 0)[0] // 0x10000
    off = 6
    paks = {}
    for _ in range(total):
        rec = data[off:off + 36]
        off += 36
        fname = bytes(b ^ 0xFF for b in rec[14:23]).split(b"\x00")[0].decode("cp949")
        pakname = bytes(b ^ 0xFF for b in rec[23:36]).split(b"\x00")[0].decode("cp949")
        _, start, _, end, _ = struct.unpack_from("<HIHIH", rec, 0)
        if pakname not in paks:
            paks[pakname] = open(os.path.join(folder, pakname), "rb").read()
        files[fname.lower()] = paks[pakname][start:end + 1]
    # 낱개 파일이 pak 안의 것보다 우선한다고 보고 덮어쓴다(내용이 다르면 둘 다 찾아 본다).
    for f in os.listdir(folder):
        if f.lower().endswith(".btl"):
            loose = open(os.path.join(folder, f), "rb").read()
            if files.get(f.lower(), loose) != loose:
                files[f.lower() + " (pak)"] = files[f.lower()]
            files[f.lower()] = loose
    return files


PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_VM_READ = 0x0010
TH32CS_SNAPPROCESS = 0x2
MEM_COMMIT = 0x1000
PAGE_GUARD = 0x100
PAGE_NOACCESS = 0x01

k32 = ctypes.WinDLL("kernel32", use_last_error=True)


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [("dwSize", wt.DWORD), ("cntUsage", wt.DWORD), ("th32ProcessID", wt.DWORD),
                ("th32DefaultHeapID", ctypes.c_void_p), ("th32ModuleID", wt.DWORD),
                ("cntThreads", wt.DWORD), ("th32ParentProcessID", wt.DWORD),
                ("pcPriClassBase", ctypes.c_long), ("dwFlags", wt.DWORD),
                ("szExeFile", ctypes.c_wchar * 260)]


class MEMORY_BASIC_INFORMATION(ctypes.Structure):
    _fields_ = [("BaseAddress", ctypes.c_void_p), ("AllocationBase", ctypes.c_void_p),
                ("AllocationProtect", wt.DWORD), ("PartitionId", wt.WORD),
                ("RegionSize", ctypes.c_size_t), ("State", wt.DWORD),
                ("Protect", wt.DWORD), ("Type", wt.DWORD)]


k32.CreateToolhelp32Snapshot.restype = wt.HANDLE
k32.OpenProcess.restype = wt.HANDLE
k32.VirtualQueryEx.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.POINTER(MEMORY_BASIC_INFORMATION), ctypes.c_size_t]
k32.VirtualQueryEx.restype = ctypes.c_size_t
k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]


def find_game_pid():
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    entry = PROCESSENTRY32W()
    entry.dwSize = ctypes.sizeof(entry)
    found = []
    ok = k32.Process32FirstW(snap, ctypes.byref(entry))
    while ok:
        name = entry.szExeFile
        if any(h.lower() in name.lower() for h in PROCESS_HINTS):
            found.append((entry.th32ProcessID, name))
        ok = k32.Process32NextW(snap, ctypes.byref(entry))
    k32.CloseHandle(snap)
    return found


def read_regions(handle):
    addr = 0
    mbi = MEMORY_BASIC_INFORMATION()
    while addr < 0x7FFFFFFFFFFF and k32.VirtualQueryEx(handle, addr, ctypes.byref(mbi), ctypes.sizeof(mbi)):
        base, size = mbi.BaseAddress or 0, mbi.RegionSize
        if mbi.State == MEM_COMMIT and not (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS)):
            buf = ctypes.create_string_buffer(size)
            got = ctypes.c_size_t()
            if k32.ReadProcessMemory(handle, base, buf, size, ctypes.byref(got)) and got.value:
                yield base, buf.raw[:got.value]
        addr = base + size


def main():
    game_root = sys.argv[1] if len(sys.argv) > 1 else GAME_ROOT
    btls = load_btl_files(game_root)
    procs = find_game_pid()
    if not procs:
        print("게임 프로세스를 못 찾았습니다. 게임을 먼저 실행하세요.")
        return
    for pid, name in procs:
        handle = k32.OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, False, pid)
        if not handle:
            print(f"{name}({pid}) 열기 실패: {ctypes.get_last_error()}")
            continue
        hits = {}
        for base, mem in read_regions(handle):
            for fname, content in btls.items():
                i = mem.find(content)
                while i != -1:
                    hits.setdefault(fname, []).append(base + i)
                    i = mem.find(content, i + 1)
        k32.CloseHandle(handle)
        print(f"== {name} (pid {pid})")
        if not hits:
            print("  메모리에 올라온 .btl 이 없습니다(전투 화면에서 다시 돌려 보세요).")
        for fname, addrs in sorted(hits.items()):
            print(f"  {fname}: {len(addrs)}곳 " + ", ".join(hex(a) for a in addrs[:4]))


if __name__ == "__main__":
    main()

"""
창세기전3 파트2 - 캐릭터 이름으로 도트 모션/초상화 추출기

사용법:
    python extract_character.py <캐릭터 이름 일부>
    예) python extract_character.py 살라딘
    예) python extract_character.py 리엔

동작:
    1) TXR/Txr.dat 에서 이름 문자열 테이블을 읽어 이름 -> txr_id 를 찾음
    2) Chr/*.chr (필요하면 Chr.idx/Chr.pak 에서 추출) 를 스캔해서
       name_code / alt_name_code 가 그 txr_id 와 일치하는 레코드를 찾음
    3) 레코드의 sprite_code / face_code 에 해당하는 Obs/*.obs 를
       필요하면 Obs00~03.pak 에서 추출
    4) Obs 파일을 디코딩해서 PNG 프레임으로 assets/<이름>_sprites/ 에 저장
"""
import struct
import os
import sys
import argparse
from PIL import Image

GAME_ROOT = r"C:/Users/Administrator/Downloads/gen3pt2"
ASSETS_ROOT = r"C:/Users/Administrator/git/gen/assets"

CHAR_GROUPS = [0x750c, 0x31bc, 0x3290, 0x5c14, 0xfdcc]
OBS_PITCH = 0x280


# ---------- .idx/.pak 압축 해제 ----------

def pak_extract(folder, base, only_names=None):
    """only_names=None 이면 전체 추출, set()을 주면 그 파일명들만 추출."""
    idx_path = os.path.join(folder, base + ".idx")
    data = open(idx_path, "rb").read()
    total = struct.unpack_from("<I", data, 0)[0] // 0x10000
    off = 6
    written = []
    pak_cache = {}
    for _ in range(total):
        rec = data[off:off + 36]
        off += 36
        fname = rec[14:14 + 9]
        pakname = rec[23:23 + 13]
        fname = bytes((b ^ 0xFF) for b in fname).split(b"\x00")[0]
        pakname = bytes((b ^ 0xFF) for b in pakname).split(b"\x00")[0]
        fname_dec = fname.decode("cp949", errors="replace")
        pakname_dec = pakname.decode("cp949", errors="replace")
        if only_names is not None and fname_dec.lower() not in only_names:
            continue
        _, filestart, _, fileend, _ = struct.unpack_from("<HIHIH", rec, 0)
        out_path = os.path.join(folder, fname_dec)
        if os.path.exists(out_path):
            continue
        if pakname_dec not in pak_cache:
            pak_cache[pakname_dec] = open(os.path.join(folder, pakname_dec), "rb").read()
        pak_data = pak_cache[pakname_dec]
        chunk = pak_data[filestart:fileend + 1]
        with open(out_path, "wb") as f:
            f.write(chunk)
        written.append(fname_dec)
    return written


def ensure_file(folder, base, filename):
    path = os.path.join(folder, filename)
    if not os.path.exists(path):
        pak_extract(folder, base, only_names={filename.lower()})
    return os.path.exists(path)


# ---------- TXR 이름 테이블 ----------

def parse_txr(data):
    marker = bytes([0xbe, 0xf8, 0xc0, 0xbd, 0x00, 0x64, 0x65, 0x66, 0x61, 0x75, 0x6c, 0x74, 0x00])
    text_base = data.find(marker)
    if text_base < 0:
        raise Exception("TXR string marker not found")
    max_relative = len(data) - text_base
    rows = []
    record_offset = 0x0c
    while record_offset + 10 <= text_base:
        relative_offset = struct.unpack_from("<I", data, record_offset)[0]
        byte_length = struct.unpack_from("<H", data, record_offset + 4)[0]
        group = struct.unpack_from("<H", data, record_offset + 6)[0]
        # 번호는 이 10바이트 조각 바로 앞 2바이트다. 게임(G3PartII.dll LoadTextData 0x1004a190)은 머리 10바이트 뒤
        # 0x0a 부터 (u16 번호, u32 위치, u32 길이) 로 읽는다. 예전에는 +8 을 번호로 읽어 모든 글이 한 칸씩 밀렸다.
        txr_id = struct.unpack_from("<H", data, record_offset - 2)[0]
        text_offset = text_base + relative_offset
        record_offset += 10
        if relative_offset >= max_relative or byte_length == 0 or byte_length > 1000 or text_offset + byte_length > len(data):
            continue
        raw = data[text_offset:text_offset + byte_length]
        if not raw or raw[-1] != 0:
            continue
        try:
            text = raw[:-1].decode("cp949")
        except Exception:
            continue
        if not text or "\ufffd" in text:
            continue
        rows.append({"txr_id": txr_id, "group": group, "text": text})
    return rows


def build_lookup(rows):
    by_id = {}
    by_group_id = {}
    for r in rows:
        by_id.setdefault(r["txr_id"], r["text"])
        by_group_id[(r["group"], r["txr_id"])] = r["text"]

    def lookup(code, groups=()):
        for g in groups:
            v = by_group_id.get((g, code))
            if v:
                return v
        return by_id.get(code, "")

    return lookup


def find_name_codes(lookup, rows, query):
    """query 문자열을 포함하는 이름의 txr_id 목록 (character 그룹 한정)."""
    codes = set()
    for r in rows:
        if r["group"] in CHAR_GROUPS and query in r["text"]:
            codes.add(r["txr_id"])
    return codes


# ---------- Chr 레코드 ----------

def scan_chr_records(chr_folder):
    records = []
    for fname in sorted(os.listdir(chr_folder)):
        if not fname.lower().endswith(".chr"):
            continue
        path = os.path.join(chr_folder, fname)
        data = open(path, "rb").read()
        if len(data) < 14:
            continue
        name_code, alt_name_code, voice_code, sprite_code, face_code, job_code = struct.unpack_from("<6H", data, 2)
        records.append({
            "file": fname,
            "chr_code": int(os.path.splitext(fname)[0]),
            "name_code": name_code,
            "alt_name_code": alt_name_code,
            "voice_code": voice_code,
            "sprite_code": sprite_code,
            "face_code": face_code,
            "job_code": job_code,
        })
    return records


# ---------- Obs 디코딩 ----------

def u16(b, o): return struct.unpack_from("<H", b, o)[0]
def u32(b, o): return struct.unpack_from("<I", b, o)[0]
def s16(b, o): return struct.unpack_from("<h", b, o)[0]


def parse_obs_structure(b):
    if len(b) < 12:
        raise Exception("OBS header too short")
    subentry_count = u16(b, 2)
    if subentry_count < 1 or subentry_count > 512:
        raise Exception(f"invalid subentry count {subentry_count}")
    subentries = [{"id": 0, "offset": u32(b, 8)}]
    cursor = 0x0c
    for _ in range(subentry_count - 1):
        if cursor + 6 > len(b):
            raise Exception("subentry offset table truncated")
        subentries.append({"id": u16(b, cursor), "offset": u32(b, cursor + 2)})
        cursor += 6
    return {"subentries": subentries}


def parse_subentry(b, listed_offset, expected_id):
    for header_skip in range(0, 97):
        header = listed_offset + header_skip
        if header + 7 + 768 > len(b):
            continue
        id_ = u16(b, header)
        slot_count = u16(b, header + 2)
        slot_count_minus_one = u16(b, header + 4)
        if id_ != expected_id or slot_count < 1 or slot_count > 256 or slot_count_minus_one + 1 != slot_count:
            continue
        transparent_index = b[header + 6]
        palette_start = header + 7
        palette = b[palette_start:palette_start + 768]
        cursor = palette_start + 768
        slots = []
        valid = True
        for _ in range(slot_count):
            if cursor + 6 > len(b):
                valid = False
                break
            slot_id = u16(b, cursor)
            slot_offset = u32(b, cursor + 2)
            if slot_offset + 14 > len(b):
                valid = False
                break
            payload_size = u32(b, slot_offset + 2)
            width = u16(b, slot_offset + 6)
            height = u16(b, slot_offset + 8)
            if width <= 0 or height <= 0 or width > 2048 or height > 2048:
                valid = False
                break
            if payload_size > len(b) - slot_offset - 14:
                valid = False
                break
            slots.append({"id": slot_id, "offset": slot_offset})
            cursor += 6
        if valid:
            return {"id": id_, "transparent_index": transparent_index, "palette": palette, "slots": slots}
    raise Exception(f"subentry {expected_id} not found near offset {listed_offset}")


def decode_obs_payload(payload, width, height, pitch, fill):
    if len(payload) < 2:
        raise Exception("payload too short")
    record_count = u16(payload, 0)
    output = bytearray([fill & 255]) * (width * height)
    cursor = 2
    output_index = 0
    wrapped = False
    for _ in range(record_count):
        if cursor + 4 > len(payload):
            raise Exception("payload record header truncated")
        skip = u16(payload, cursor)
        cursor += 2
        quad_count = payload[cursor]
        cursor += 1
        tail_count = payload[cursor]
        cursor += 1

        span = skip
        if skip != 0:
            consumed = 0
            while consumed < (span & 0xffff):
                if wrapped:
                    wrapped = False
                    span += width - pitch
                    if consumed != (span & 0xffff):
                        output_index += 1
                else:
                    output_index += 1
                    if output_index % width == 0:
                        span += width - pitch
                consumed += 1

        literal_count = tail_count + (quad_count * 4)
        if cursor + literal_count > len(payload):
            raise Exception("payload literal data truncated")
        if output_index + literal_count > len(output):
            raise Exception("decoded literal run exceeds output buffer")
        output[output_index:output_index + literal_count] = payload[cursor:cursor + literal_count]
        output_index += literal_count
        cursor += literal_count

        if (tail_count + quad_count) != 0 and output_index % width == 0:
            wrapped = True
    return output


def decode_obs_slot(b, slot_offset, fill):
    slot_id = u16(b, slot_offset)
    payload_size = u32(b, slot_offset + 2)
    width = u16(b, slot_offset + 6)
    height = u16(b, slot_offset + 8)
    x = s16(b, slot_offset + 10)
    y = s16(b, slot_offset + 12)
    payload = b[slot_offset + 14:slot_offset + 14 + payload_size]
    indexed = decode_obs_payload(payload, width, height, OBS_PITCH, fill)
    return {"slot_id": slot_id, "width": width, "height": height, "x": x, "y": y, "indexed": indexed}


def indexed_to_rgba(indexed, width, height, palette, transparent_index):
    im = Image.new("RGBA", (width, height))
    px = im.load()
    for i, idx in enumerate(indexed):
        po = idx * 3
        r = palette[po] if po < len(palette) else 0
        g = palette[po + 1] if po + 1 < len(palette) else 0
        bl = palette[po + 2] if po + 2 < len(palette) else 0
        a = 0 if idx == transparent_index else 255
        px[i % width, i // width] = (r, g, bl, a)
    return im


def decode_obs_file(path, out_dir):
    b = open(path, "rb").read()
    structure = parse_obs_structure(b)
    os.makedirs(out_dir, exist_ok=True)
    count = 0
    # 둘째 몸짓벌부터는 색인표 자리가 실제 머리에서 크게 어긋날 수 있다. 벌들은 파일 안에
    # 잇달아 있으니, 앞 벌의 마지막 장이 끝난 자리를 먼저 짚고 안 되면 색인표 자리로 물러난다
    # (WarOfGenesis.Assets/ObsSprite.cs 의 Decode 와 같은 방식).
    next_search_from = structure["subentries"][0]["offset"]
    for sub_ref in structure["subentries"]:
        subentry = None
        for offset in (next_search_from, sub_ref["offset"]):
            try:
                subentry = parse_subentry(b, offset, sub_ref["id"])
                break
            except Exception:
                pass
        if subentry is None:
            print(f"  subentry {sub_ref['id']} not found near offset {sub_ref['offset']}")
            next_search_from = sub_ref["offset"]
            continue
        if subentry["slots"]:
            last = subentry["slots"][-1]["offset"]
            next_search_from = last + 14 + u32(b, last + 2)
        for slot_ref in subentry["slots"]:
            try:
                decoded = decode_obs_slot(b, slot_ref["offset"], subentry["transparent_index"])
                img = indexed_to_rgba(decoded["indexed"], decoded["width"], decoded["height"], subentry["palette"], subentry["transparent_index"])
                fname = f"sub{subentry['id']:02d}_slot{decoded['slot_id']:03d}_{decoded['width']}x{decoded['height']}_x{decoded['x']}_y{decoded['y']}.png"
                img.save(os.path.join(out_dir, fname))
                count += 1
            except Exception as e:
                print(f"  slot error sub={subentry['id']} slot={slot_ref['id']}: {e}")
    return count


# ---------- 메인 ----------

def main():
    parser = argparse.ArgumentParser(description="캐릭터 이름으로 도트 모션/초상화 추출")
    parser.add_argument("name", help="캐릭터 이름 (일부 포함 검색, 예: 살라딘)")
    args = parser.parse_args()

    txr_path = os.path.join(GAME_ROOT, "TXR", "Txr.dat")
    rows = parse_txr(open(txr_path, "rb").read())
    lookup = build_lookup(rows)
    name_codes = find_name_codes(lookup, rows, args.name)
    if not name_codes:
        print(f"'{args.name}' 이름을 TXR 문자열 테이블에서 찾지 못했습니다.")
        sys.exit(1)
    print(f"이름 코드 후보: {sorted(name_codes)}")

    chr_folder = os.path.join(GAME_ROOT, "Chr")
    # Chr 폴더가 비어있으면 전체 추출
    if not any(f.lower().endswith(".chr") for f in os.listdir(chr_folder)):
        print("Chr.pak 전체 추출 중...")
        pak_extract(chr_folder, "Chr")
    records = scan_chr_records(chr_folder)

    matches = [r for r in records if r["name_code"] in name_codes or r["alt_name_code"] in name_codes]
    if not matches:
        print("이름 코드는 찾았지만 일치하는 Chr 레코드가 없습니다.")
        sys.exit(1)

    print(f"\n일치하는 Chr 레코드 {len(matches)}개:")
    for m in matches:
        print(f"  {m['file']}: name={m['name_code']} sprite={m['sprite_code']} face={m['face_code']} job={m['job_code']}")

    obs_folder = os.path.join(GAME_ROOT, "Obs")
    safe_name = args.name.replace(" ", "_")
    out_root = os.path.join(ASSETS_ROOT, f"{safe_name}_sprites")

    seen_codes = set()
    for m in matches:
        for kind, code in (("sprite", m["sprite_code"]), ("face", m["face_code"])):
            if code == 0 or code in seen_codes:
                continue
            seen_codes.add(code)
            obs_name = f"{code:04d}.obs"
            if not ensure_file(obs_folder, "Obs", obs_name):
                print(f"  [{kind}] {obs_name} 없음 (건너뜀)")
                continue
            out_dir = os.path.join(out_root, f"{kind}_{code:04d}")
            n = decode_obs_file(os.path.join(obs_folder, obs_name), out_dir)
            print(f"  [{kind}] {obs_name} -> {n} 프레임 -> {out_dir}")

    print(f"\n완료. 결과: {out_root}")


if __name__ == "__main__":
    main()

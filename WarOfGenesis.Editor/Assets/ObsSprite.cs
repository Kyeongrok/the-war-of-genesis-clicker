using System.IO;

namespace WarOfGenesis.Editor.Assets;

/// <summary>몸짓(Obs) 한 장 — 자리(X·Y)까지 포함해 BGRA 로 다 풀어 둔 것.</summary>
public sealed record ObsFrame(int SlotId, int Width, int Height, int X, int Y, byte[] Bgra);

/// <summary>몸짓 하나 — 여러 장(<see cref="ObsFrame"/>)이 한 벌이다. 이 벌 하나가 「한 동작」이다.</summary>
public sealed record ObsMotion(int Id, IReadOnlyList<ObsFrame> Frames);

/// <summary>
/// <c>Obs/*.obs</c> — 창세기전3 파트2의 도트 그림(전투 모션·초상)을 담은 파일.
/// </summary>
/// <remarks>
/// <c>tools/extract_character.py</c> 의 <c>parse_obs_structure</c>/<c>parse_subentry</c>/
/// <c>decode_obs_payload</c>/<c>decode_obs_slot</c>/<c>indexed_to_rgba</c> 를 그대로 옮겼다 —
/// 갈무리로 하나하나 맞춰 본 자리표라 값 하나도 손대지 않았다.
///
/// 한 파일 안에 <b>몸짓(subentry) 여럿</b>이 있고, 몸짓 하나는 <b>장(slot) 여럿</b>으로
/// 된 한 벌이다 — 이 장 벌을 한 칸씩 넘겨 보는 것이 "전투 모션을 한 컷씩 본다"는 것이다.
/// </remarks>
public static class ObsSprite
{
    /// <summary>장을 풀 때 쓰는 가로 보폭. 원본 캔버스 폭이다(<c>0x280</c> = 640).</summary>
    private const int Pitch = 0x280;

    private readonly record struct SubRef(ushort Id, uint Offset);

    private sealed record SubEntry(ushort Id, byte TransparentIndex, byte[] Palette, List<SlotRef> Slots);

    private readonly record struct SlotRef(ushort Id, uint Offset);

    private sealed record DecodedSlot(ushort SlotId, int Width, int Height, short X, short Y, byte[] Indexed);

    public static List<ObsMotion> Decode(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        var subrefs = ParseStructure(b);

        var motions = new List<ObsMotion>();
        foreach (var subref in subrefs)
        {
            SubEntry entry;
            try { entry = ParseSubEntry(b, subref.Offset, subref.Id); }
            catch { continue; }

            var frames = new List<ObsFrame>();
            foreach (var slotRef in entry.Slots)
            {
                try
                {
                    var slot = DecodeSlot(b, slotRef.Offset, entry.TransparentIndex);
                    byte[] bgra = ToBgra(slot.Indexed, slot.Width, slot.Height, entry.Palette, entry.TransparentIndex);
                    frames.Add(new ObsFrame(slot.SlotId, slot.Width, slot.Height, slot.X, slot.Y, bgra));
                }
                catch { /* 이 장만 건너뛴다 — 원본 도구도 그랬다. */ }
            }
            if (frames.Count > 0) motions.Add(new ObsMotion(entry.Id, frames));
        }
        return motions;
    }

    private static List<SubRef> ParseStructure(byte[] b)
    {
        if (b.Length < 12) throw new InvalidDataException("OBS 머리가 너무 짧습니다.");

        int subentryCount = U16(b, 2);
        if (subentryCount is < 1 or > 512)
            throw new InvalidDataException($"몸짓 수가 이상합니다: {subentryCount}");

        var subrefs = new List<SubRef> { new(0, U32(b, 8)) };
        int cursor = 0x0c;
        for (int i = 0; i < subentryCount - 1; i++)
        {
            if (cursor + 6 > b.Length) throw new InvalidDataException("몸짓 자리표가 잘렸습니다.");
            subrefs.Add(new SubRef((ushort)U16(b, cursor), U32(b, cursor + 2)));
            cursor += 6;
        }
        return subrefs;
    }

    /// <summary>
    /// 몸짓 머리를 찾는다. 알려 준 자리에서 최대 96바이트 어긋나 있을 수 있어 하나씩 밀어 본다.
    /// </summary>
    private static SubEntry ParseSubEntry(byte[] b, uint listedOffset, ushort expectedId)
    {
        for (int headerSkip = 0; headerSkip < 97; headerSkip++)
        {
            long header = listedOffset + headerSkip;
            if (header + 7 + 768 > b.Length) continue;

            int id = U16(b, header);
            int slotCount = U16(b, header + 2);
            int slotCountMinusOne = U16(b, header + 4);
            if (id != expectedId || slotCount is < 1 or > 256 || slotCountMinusOne + 1 != slotCount) continue;

            byte transparentIndex = b[header + 6];
            var palette = new byte[768];
            Array.Copy(b, header + 7, palette, 0, 768);

            long cursor = header + 7 + 768;
            var slots = new List<SlotRef>();
            bool valid = true;

            for (int i = 0; i < slotCount; i++)
            {
                if (cursor + 6 > b.Length) { valid = false; break; }
                ushort slotId = (ushort)U16(b, cursor);
                uint slotOffset = U32(b, cursor + 2);
                if (slotOffset + 14 > b.Length) { valid = false; break; }

                uint payloadSize = U32(b, slotOffset + 2);
                int width = U16(b, slotOffset + 6);
                int height = U16(b, slotOffset + 8);
                if (width <= 0 || height <= 0 || width > 2048 || height > 2048) { valid = false; break; }
                if (payloadSize > b.Length - slotOffset - 14) { valid = false; break; }

                slots.Add(new SlotRef(slotId, slotOffset));
                cursor += 6;
            }

            if (valid) return new SubEntry((ushort)id, transparentIndex, palette, slots);
        }
        throw new InvalidDataException($"몸짓 {expectedId} 을(를) {listedOffset} 부근에서 못 찾았습니다.");
    }

    private static DecodedSlot DecodeSlot(byte[] b, uint slotOffset, byte fill)
    {
        ushort slotId = (ushort)U16(b, slotOffset);
        uint payloadSize = U32(b, slotOffset + 2);
        int width = U16(b, slotOffset + 6);
        int height = U16(b, slotOffset + 8);
        short x = S16(b, slotOffset + 10);
        short y = S16(b, slotOffset + 12);

        var payload = new byte[payloadSize];
        Array.Copy(b, slotOffset + 14, payload, 0, payloadSize);

        byte[] indexed = DecodePayload(payload, width, height, Pitch, fill);
        return new DecodedSlot(slotId, width, height, x, y, indexed);
    }

    /// <summary>
    /// 장 하나의 색인 그림을 편다 — 줄마다 건너뛰는 조각을 이어 붙이는 자체 RLE다.
    /// </summary>
    /// <remarks>
    /// <b>원본 도구의 셈을 한 걸음도 안 바꾸고 그대로 옮겼다.</b> <c>span</c> 이 도는 중에
    /// 값이 바뀌면 <c>while</c> 조건(<c>consumed &lt; (span &amp; 0xffff)</c>)도 매번 다시
    /// 셈해진다 — 파이썬과 똑같이 C# 의 <c>while</c> 도 그렇게 돈다.
    /// </remarks>
    private static byte[] DecodePayload(byte[] payload, int width, int height, int pitch, byte fill)
    {
        if (payload.Length < 2) throw new InvalidDataException("장 그림이 너무 짧습니다.");

        int recordCount = U16(payload, 0);
        var output = new byte[width * height];
        Array.Fill(output, fill);

        int cursor = 2;
        int outputIndex = 0;
        bool wrapped = false;

        for (int r = 0; r < recordCount; r++)
        {
            if (cursor + 4 > payload.Length) throw new InvalidDataException("장 레코드 머리가 잘렸습니다.");
            int skip = U16(payload, cursor); cursor += 2;
            int quadCount = payload[cursor]; cursor += 1;
            int tailCount = payload[cursor]; cursor += 1;

            long span = skip;
            if (skip != 0)
            {
                int consumed = 0;
                while (consumed < (span & 0xffff))
                {
                    if (wrapped)
                    {
                        wrapped = false;
                        span += width - pitch;
                        if (consumed != (span & 0xffff)) outputIndex++;
                    }
                    else
                    {
                        outputIndex++;
                        if (outputIndex % width == 0) span += width - pitch;
                    }
                    consumed++;
                }
            }

            int literalCount = tailCount + quadCount * 4;
            if (cursor + literalCount > payload.Length) throw new InvalidDataException("장 그림 자료가 잘렸습니다.");
            if (outputIndex + literalCount > output.Length) throw new InvalidDataException("풀린 그림이 칸을 넘칩니다.");

            Array.Copy(payload, cursor, output, outputIndex, literalCount);
            outputIndex += literalCount;
            cursor += literalCount;

            if (tailCount + quadCount != 0 && outputIndex % width == 0) wrapped = true;
        }
        return output;
    }

    /// <summary>색인 그림 + 팔레트 → BGRA(WPF <c>Bgra32</c> 순서: B,G,R,A).</summary>
    private static byte[] ToBgra(byte[] indexed, int width, int height, byte[] palette, byte transparentIndex)
    {
        var bgra = new byte[width * height * 4];
        for (int i = 0; i < indexed.Length; i++)
        {
            byte idx = indexed[i];
            int po = idx * 3;
            byte r = po < palette.Length ? palette[po] : (byte)0;
            byte g = po + 1 < palette.Length ? palette[po + 1] : (byte)0;
            byte bl = po + 2 < palette.Length ? palette[po + 2] : (byte)0;
            byte a = idx == transparentIndex ? (byte)0 : (byte)255;

            int o = i * 4;
            bgra[o] = bl;
            bgra[o + 1] = g;
            bgra[o + 2] = r;
            bgra[o + 3] = a;
        }
        return bgra;
    }

    private static int U16(byte[] b, long o) => b[o] | (b[o + 1] << 8);
    private static uint U32(byte[] b, long o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
    private static short S16(byte[] b, long o) => (short)U16(b, o);
}

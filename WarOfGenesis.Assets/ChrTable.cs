using System.IO;

namespace WarOfGenesis.Assets;

/// <summary>
/// <c>Chr/*.chr</c> 인물 레코드 — 이름 번호와 몸짓·초상 그림(Obs) 번호를 잇는다.
/// <c>tools/extract_character.py</c> 의 <c>scan_chr_records</c> 를 그대로 옮겼다.
/// </summary>
public sealed record ChrRecord(string File, int ChrCode, ushort NameCode, ushort AltNameCode,
                               ushort VoiceCode, ushort SpriteCode, ushort FaceCode, ushort JobCode);

public static class ChrTable
{
    /// <summary>
    /// 게임 폴더의 인물 레코드 전부 — 낱장(<c>Chr\*.chr</c>)과 <c>Chr.pak</c> 안의 것을 합친다(같은 번호면 낱장).
    /// 게임 폴더에는 낱장이 86개쯤만 풀려 있고 나머지(살라딘 0219 같은 이야기용 레코드 포함)는 pak 에만 있다.
    /// </summary>
    public static List<ChrRecord> ScanAll(GameFiles files)
    {
        var records = new List<ChrRecord>();
        foreach (var name in files.List("Chr", ".chr").Keys)
            if (files.Read("Chr", name) is { Length: >= 14 } data)
                records.Add(FromBytes(name, data));
        return [.. records.OrderBy(r => r.ChrCode)];
    }

    private static ChrRecord FromBytes(string fileName, byte[] data)
    {
        int chrCode = int.TryParse(Path.GetFileNameWithoutExtension(fileName), out int n) ? n : -1;
        return new ChrRecord(fileName, chrCode, BitConverter.ToUInt16(data, 2), BitConverter.ToUInt16(data, 4), BitConverter.ToUInt16(data, 6),
                             BitConverter.ToUInt16(data, 8), BitConverter.ToUInt16(data, 10), data.Length >= 17 ? BitConverter.ToUInt16(data, 15) : (ushort)0);
    }

    public static List<ChrRecord> Scan(string chrFolder)
    {
        var records = new List<ChrRecord>();
        foreach (var path in Directory.EnumerateFiles(chrFolder, "*.chr").OrderBy(p => p))
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 17) continue;

            ushort nameCode = BitConverter.ToUInt16(data, 2);
            ushort altNameCode = BitConverter.ToUInt16(data, 4);
            ushort voiceCode = BitConverter.ToUInt16(data, 6);
            ushort spriteCode = BitConverter.ToUInt16(data, 8);
            ushort faceCode = BitConverter.ToUInt16(data, 10);
            // 직업은 파일 15(CharacterData.JobId 와 같은 자리) — 12 는 칭호 TXR 이다(예전에는 12 를 직업으로 읽었다).
            ushort jobCode = BitConverter.ToUInt16(data, 15);

            string fileName = Path.GetFileName(path);
            int chrCode = int.TryParse(Path.GetFileNameWithoutExtension(path), out int n) ? n : -1;

            records.Add(new ChrRecord(fileName, chrCode, nameCode, altNameCode, voiceCode,
                                      spriteCode, faceCode, jobCode));
        }
        return records;
    }
}

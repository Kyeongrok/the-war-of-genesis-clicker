using System.IO;

namespace WarOfGenesis.Editor.Assets;

/// <summary>
/// <c>Chr/*.chr</c> 인물 레코드 — 이름 번호와 몸짓·초상 그림(Obs) 번호를 잇는다.
/// <c>tools/extract_character.py</c> 의 <c>scan_chr_records</c> 를 그대로 옮겼다.
/// </summary>
public sealed record ChrRecord(string File, int ChrCode, ushort NameCode, ushort AltNameCode,
                               ushort VoiceCode, ushort SpriteCode, ushort FaceCode, ushort JobCode);

public static class ChrTable
{
    public static List<ChrRecord> Scan(string chrFolder)
    {
        var records = new List<ChrRecord>();
        foreach (var path in Directory.EnumerateFiles(chrFolder, "*.chr").OrderBy(p => p))
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 14) continue;

            ushort nameCode = BitConverter.ToUInt16(data, 2);
            ushort altNameCode = BitConverter.ToUInt16(data, 4);
            ushort voiceCode = BitConverter.ToUInt16(data, 6);
            ushort spriteCode = BitConverter.ToUInt16(data, 8);
            ushort faceCode = BitConverter.ToUInt16(data, 10);
            ushort jobCode = BitConverter.ToUInt16(data, 12);

            string fileName = Path.GetFileName(path);
            int chrCode = int.TryParse(Path.GetFileNameWithoutExtension(path), out int n) ? n : -1;

            records.Add(new ChrRecord(fileName, chrCode, nameCode, altNameCode, voiceCode,
                                      spriteCode, faceCode, jobCode));
        }
        return records;
    }
}

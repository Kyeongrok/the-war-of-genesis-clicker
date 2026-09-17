using System.IO;
using System.Text;

namespace WarOfGenesis.Editor.Assets;

/// <summary>
/// 창세기전3 파트2의 <c>.idx</c>/<c>.pak</c> 묶음 — 폴더 하나(Chr, Obs 따위)의 낱장 파일들을
/// 커다란 <c>.pak</c> 파일 몇 개에 몰아 담고 <c>.idx</c> 로 찾아보는 색인이다.
/// </summary>
/// <remarks>
/// <c>tools/extract_character.py</c> 의 <c>pak_extract</c>/<c>ensure_file</c> 을 그대로
/// 옮긴 것이다. 색인 레코드의 파일 이름은 바이트마다 <c>0xFF</c> 로 뒤집혀 있다.
/// </remarks>
public static class PakArchive
{
    private const int RecordSize = 36;

    private static Encoding Cp949Encoding()
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949);
    }

    private static readonly Lazy<Encoding> Cp949 = new(Cp949Encoding);

    /// <summary>그 폴더의 <paramref name="baseName"/>.idx 를 훑어 <paramref name="wanted"/> 에 든 파일만 꺼낸다.</summary>
    /// <param name="wanted">찾을 파일 이름(소문자). null 이면 색인에 든 것을 전부 꺼낸다.</param>
    public static List<string> Extract(string folder, string baseName, HashSet<string>? wanted = null)
    {
        string idxPath = Path.Combine(folder, baseName + ".idx");
        byte[] data = File.ReadAllBytes(idxPath);

        uint headerValue = BitConverter.ToUInt32(data, 0);
        int total = (int)(headerValue / 0x10000);
        int off = 6;

        var written = new List<string>();
        var pakCache = new Dictionary<string, byte[]>();

        for (int i = 0; i < total; i++)
        {
            if (off + RecordSize > data.Length) break;

            // 레코드 앞 14바이트는 <c>&lt;HIHIH&gt;</c> 꼴이다 — 우리가 쓰는 것은
            // 둘째(시작)와 넷째(끝) 자리뿐이라 나머지 셋은 건너뛴다.
            uint filestart = BitConverter.ToUInt32(data, off + 2);
            uint fileend = BitConverter.ToUInt32(data, off + 8);

            string fname = UnmaskName(data, off + 14, 9);
            string pakname = UnmaskName(data, off + 23, 13);
            off += RecordSize;

            if (wanted != null && !wanted.Contains(fname.ToLowerInvariant())) continue;

            string outPath = Path.Combine(folder, fname);
            if (File.Exists(outPath)) continue;

            if (!pakCache.TryGetValue(pakname, out var pakData))
            {
                pakData = File.ReadAllBytes(Path.Combine(folder, pakname));
                pakCache[pakname] = pakData;
            }

            int start = (int)filestart, end = (int)fileend;
            int length = end - start + 1;
            var chunk = new byte[length];
            Array.Copy(pakData, start, chunk, 0, length);
            File.WriteAllBytes(outPath, chunk);
            written.Add(fname);
        }
        return written;
    }

    private static string UnmaskName(byte[] data, int offset, int length)
    {
        var raw = new byte[length];
        for (int i = 0; i < length; i++) raw[i] = (byte)(data[offset + i] ^ 0xFF);

        int end = Array.IndexOf(raw, (byte)0);
        int count = end < 0 ? length : end;
        return Cp949.Value.GetString(raw, 0, count);
    }

    /// <summary>그 파일이 폴더에 없으면 색인에서 그것 하나만 꺼낸다.</summary>
    public static bool EnsureFile(string folder, string baseName, string fileName)
    {
        string path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) Extract(folder, baseName, [fileName.ToLowerInvariant()]);
        return File.Exists(path);
    }
}

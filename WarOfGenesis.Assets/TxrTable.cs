using System.IO;
using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>
/// <c>TXR/Txr.dat</c> 의 글 조각 표 — 인물 이름을 비롯한 온갖 문자열이 번호(<c>txr_id</c>)로
/// 찾아지는 곳이다. <c>tools/extract_character.py</c> 의 <c>parse_txr</c>/<c>build_lookup</c> 을
/// 그대로 옮겼다.
/// </summary>
public sealed class TxrTable
{
    /// <summary>인물 이름이 속한 갈래 번호. 물건·땅 이름 따위는 다른 갈래다.</summary>
    private static readonly ushort[] CharacterGroups = [0x750c, 0x31bc, 0x3290, 0x5c14, 0xfdcc];

    public readonly record struct Row(ushort TxrId, ushort Group, string Text);

    public IReadOnlyList<Row> Rows { get; }

    private readonly Dictionary<ushort, string> _byId = [];

    private TxrTable(List<Row> rows)
    {
        Rows = rows;
        foreach (var row in rows) _byId.TryAdd(row.TxrId, row.Text);
    }

    public static TxrTable Open(string path) => Parse(File.ReadAllBytes(path));

    public static TxrTable Parse(byte[] data)
    {
        Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);

        byte[] marker = [0xbe, 0xf8, 0xc0, 0xbd, 0x00, 0x64, 0x65, 0x66, 0x61, 0x75, 0x6c, 0x74, 0x00];
        int textBase = IndexOf(data, marker);
        if (textBase < 0) throw new InvalidDataException("TXR 문자열 표식을 못 찾았습니다.");

        int maxRelative = data.Length - textBase;
        var rows = new List<Row>();
        int recordOffset = 0x0c;

        while (recordOffset + 10 <= textBase)
        {
            uint relativeOffset = BitConverter.ToUInt32(data, recordOffset);
            ushort byteLength = BitConverter.ToUInt16(data, recordOffset + 4);
            ushort group = BitConverter.ToUInt16(data, recordOffset + 6);
            // 번호는 이 10바이트 조각 바로 앞 2바이트다. 게임(G3PartII.dll LoadTextData 0x1004a190)은 머리 10바이트 뒤
            // 0x0a 부터 (u16 번호, u32 위치, u32 길이) 로 읽는다. 예전에는 +8 을 번호로 읽어 모든 글이 한 칸씩 밀렸다.
            ushort txrId = BitConverter.ToUInt16(data, recordOffset - 2);
            long textOffset = textBase + relativeOffset;
            recordOffset += 10;

            if (relativeOffset >= maxRelative || byteLength == 0 || byteLength > 1000
                || textOffset + byteLength > data.Length) continue;

            var raw = new ReadOnlySpan<byte>(data, (int)textOffset, byteLength);
            if (raw.Length == 0 || raw[^1] != 0) continue;

            string text;
            try { text = cp949.GetString(raw[..^1]); }
            catch { continue; }

            if (string.IsNullOrEmpty(text) || text.Contains('�')) continue;
            rows.Add(new Row(txrId, group, text));
        }
        return new TxrTable(rows);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    /// <summary>인물 이름 갈래에서, 그 글자를 담은 이름들의 <c>txr_id</c> 모음.</summary>
    public HashSet<ushort> FindNameCodes(string query)
    {
        var codes = new HashSet<ushort>();
        foreach (var row in Rows)
            if (Array.IndexOf(CharacterGroups, row.Group) >= 0 && row.Text.Contains(query, StringComparison.Ordinal))
                codes.Add(row.TxrId);
        return codes;
    }

    /// <summary>그 번호의 첫 글. 없으면 빈 문자열.</summary>
    public string TextOf(ushort txrId) => _byId.GetValueOrDefault(txrId, "");
}

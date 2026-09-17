using System.IO;
using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>WAV 머리에서 읽은 것 — 이름표(<c>LIST/INFO/INAM</c>)는 있는 파일만.</summary>
public sealed record WaveInfo(int SampleRate, int Channels, int BitsPerSample, long DataBytes, string? Name)
{
    public TimeSpan Duration => SampleRate == 0 || Channels == 0 || BitsPerSample < 8 ? TimeSpan.Zero
        : TimeSpan.FromSeconds((double)DataBytes / (SampleRate * Channels * (BitsPerSample / 8)));
}

/// <summary>
/// <c>Snd/*.snd</c> — 효과음. 이름만 <c>.snd</c> 일 뿐 속은 평범한 RIFF WAVE(PCM)다
/// (800개 모두 형식 1, 8/16비트, 11025·22050·44100 Hz, 대부분 22050 Hz 모노).
/// </summary>
public static class WaveSound
{
    /// <summary>머리 부분만으로 형식·길이를 읽는다. <c>data</c> 조각이 머리 밖에 있으면 파일 크기로 어림한다.</summary>
    public static WaveInfo? ReadInfo(byte[] head, long fileSize)
    {
        if (head.Length < 12 || !head.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !head.AsSpan(8, 4).SequenceEqual("WAVE"u8)) return null;
        int rate = 0, channels = 0, bits = 0;
        long dataBytes = -1;
        string? name = null;
        foreach (var (id, offset, size) in Chunks(head))
        {
            switch (id)
            {
                case "fmt " when offset + 16 <= head.Length:
                    channels = BitConverter.ToUInt16(head, offset + 2);
                    rate = BitConverter.ToInt32(head, offset + 4);
                    bits = BitConverter.ToUInt16(head, offset + 14);
                    break;
                case "data":
                    dataBytes = Math.Min(size, fileSize - offset);
                    break;
                case "LIST":
                    name ??= InfoName(head, offset, size);
                    break;
            }
        }
        if (rate == 0) return null;
        if (dataBytes < 0) dataBytes = Math.Max(0, fileSize - 44);
        return new WaveInfo(rate, channels, bits, dataBytes, name);
    }

    /// <summary>PCM WAV 를 16비트로 편다(8비트는 부호 없는 값을 옮겨 늘린다).</summary>
    public static PcmSound Parse(byte[] data)
    {
        var info = ReadInfo(data, data.Length) ?? throw new InvalidDataException("WAV 파일이 아닙니다.");
        var (_, offset, size) = Chunks(data).FirstOrDefault(c => c.Id == "data");
        int format = Chunks(data).Where(c => c.Id == "fmt ").Select(c => (int)BitConverter.ToUInt16(data, c.Offset)).FirstOrDefault();
        if (offset == 0) throw new InvalidDataException("WAV 에 data 조각이 없습니다.");
        if (format != 1) throw new NotSupportedException($"PCM 이 아닌 WAV(형식 {format})는 못 풉니다.");
        int length = (int)Math.Min(size, data.Length - offset);

        short[] samples;
        if (info.BitsPerSample == 16)
        {
            samples = new short[length / 2];
            Buffer.BlockCopy(data, offset, samples, 0, samples.Length * 2);
        }
        else if (info.BitsPerSample == 8)
        {
            samples = new short[length];
            for (int i = 0; i < length; i++) samples[i] = (short)((data[offset + i] - 128) << 8);
        }
        else throw new NotSupportedException($"{info.BitsPerSample}비트 WAV 는 못 풉니다.");

        int channels = Math.Max(1, info.Channels);
        if (samples.Length % channels != 0) Array.Resize(ref samples, samples.Length - samples.Length % channels);
        return new PcmSound(samples, info.SampleRate, channels);
    }

    private static IEnumerable<(string Id, int Offset, long Size)> Chunks(byte[] d)
    {
        int o = 12;
        while (o + 8 <= d.Length)
        {
            string id = Encoding.ASCII.GetString(d, o, 4);
            uint size = BitConverter.ToUInt32(d, o + 4);
            yield return (id, o + 8, size);
            long next = o + 8L + size + (size & 1);
            if (next > int.MaxValue) yield break;
            o = (int)next;
        }
    }

    private static string? InfoName(byte[] d, int offset, long size)
    {
        if (offset + 4 > d.Length || !d.AsSpan(offset, 4).SequenceEqual("INFO"u8)) return null;
        long end = Math.Min(d.Length, offset + size);
        int o = offset + 4;
        while (o + 8 <= end)
        {
            uint len = BitConverter.ToUInt32(d, o + 4);
            if (d.AsSpan(o, 4).SequenceEqual("INAM"u8) && o + 8 + len <= end)
            {
                var raw = d.AsSpan(o + 8, (int)len);
                int zero = raw.IndexOf((byte)0);
                if (zero >= 0) raw = raw[..zero];
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(949).GetString(raw).Trim();
            }
            o += 8 + (int)len + (int)(len & 1);
        }
        return null;
    }
}

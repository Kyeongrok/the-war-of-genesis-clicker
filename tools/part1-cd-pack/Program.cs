// 파트1 시디 트랙(사용자 폴더의 뜬 트랙, 흔히 bgm\Track02.mp3)을 게임이 MF 없이 읽는 WAV 로 바꿔 릴리즈 에셋으로 낸다 —
// MF(Media Foundation)가 없는 PC(유닉스 이식 · Windows N)도 곡 15 · 29 가 나게.
//
//   WAROFGENESIS_PART1=<파트1 폴더> dotnet run --project tools/part1-cd-pack -- <내보낼 폴더>
//
// 트랙마다(_inmm.ini 의 줄 = 트랙 2, 3, … — Part1Game.CdTrackFiles) 사용자 파일을 16비트 PCM 으로 풀고(WAV 는 우리 읽기, 그 밖은 게임과 같은
// MediaFoundationAudio) MS ADPCM WAV(형식 2 — 파트1 BGMs\*.bgm 과 같은 꼴: 블록 2048바이트 · 표준 계수 7)로 싼다. 싼 것을 게임의 풀기
// (Part1Game.AdpcmToPcm)로 다시 풀어 인코더가 셈한 것과 표본 하나까지 같은지 보고, 원래 소리와의 SNR 을 찍는다.
// 내보낼 폴더/assets-part1cd-1 에 올릴 파일(part1_cd_TrackNN.wav)을, tools/part1_cd_entries.json 에 pack.json 의 files 줄
// (part1/cd/TrackNN.wav, tier 90 = 찾을 때만 받는 것)을 쓴다 — make_asset_pack.py 가 이 줄을 덧붙인다(--part1-only 도).
// 파일 곡도 — 이 설치본은 곡 6~14 · 26(디스크 2~4 의 시디 곡)을 시디에서 떠 BGMs\NNNN.bgm 에 PCM(형식 1)으로 넣어 둔 판이다.
// 다른 판에는 그 파일이 없어 전투가 무음이었다(카페 제보 fg-39 인페르노 지하 곡 10). PCM 인 BGMs 파일을 같은 ADPCM 으로 싸
// 내보낼 폴더/assets-part1bgm-1(part1_bgm_NNNN.wav) · tools/part1_bgm_entries.json(part1/bgm/NNNN.wav) 에 쓴다.
using System.Security.Cryptography;
using System.Text.Json;
using DuelDx;
using WarOfGenesis.Assets;

const int OnDemandTier = 90, BlockAlign = 2048;
const string Release = "assets-part1cd-1", SongRelease = "assets-part1bgm-1";

if (args.Length < 1) { Console.Error.WriteLine("사용법: part1-cd-pack <내보낼 폴더>"); return 1; }
if (Part1Game.Root == null) { Console.Error.WriteLine("파트1 폴더를 못 찾았습니다 — WAROFGENESIS_PART1=<폴더> 로 알려 주세요."); return 1; }
string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string upload = Path.Combine(Path.GetFullPath(args[0]), Release);
if (Directory.Exists(upload)) Directory.Delete(upload, true);
Directory.CreateDirectory(upload);

var entries = new List<Dictionary<string, object>>();
for (int track = 2; track < 100; track++)
{
    var files = Part1Game.CdTrackFiles(track);
    if (files.Count == 0) break;
    string from = files[0];
    PcmSound? pcm = File.ReadAllBytes(from) is var raw && raw.AsSpan(0, 4).SequenceEqual("RIFF"u8)
        ? WaveSound.Parse(Part1Game.AdpcmToPcm(raw)) : MediaFoundationAudio.Decode(from);
    if (pcm == null) { Console.Error.WriteLine($"트랙 {track}: {from} 을 못 풀었습니다 — 건너뜀"); continue; }
    // 이 설치본의 Track04 는 4초 무음이다(봉우리 ±몇 — 시디 표 줄 1 은 트랙 2 · 3 만 쓴다) — 무음은 안 올린다(없으면 게임도 무음).
    if (pcm.Samples.Max(s => Math.Abs((int)s)) < 64) { Console.WriteLine($"트랙 {track}: {Path.GetFileName(from)} 은 무음 — 건너뜀"); continue; }

    var (wav, rebuilt) = Adpcm.Encode(pcm, BlockAlign);
    // 게임의 풀기로 다시 푼다 — 인코더가 셈한 복원과 같아야 한다.
    var back = WaveSound.Parse(Part1Game.AdpcmToPcm(wav));
    if (back.SampleRate != pcm.SampleRate || back.Channels != pcm.Channels || !back.Samples.AsSpan().SequenceEqual(rebuilt))
        throw new InvalidDataException($"트랙 {track}: 다시 푼 것이 인코더의 복원과 다릅니다 ({back.Samples.Length} / {rebuilt.Length} 표본)");
    double signal = 0, noise = 0;
    for (int i = 0; i < pcm.Samples.Length; i++) { signal += (double)pcm.Samples[i] * pcm.Samples[i]; noise += Math.Pow(pcm.Samples[i] - back.Samples[i], 2); }

    string name = $"part1/cd/Track{track:D2}.wav", asset = name.Replace('/', '_');
    File.WriteAllBytes(Path.Combine(upload, asset), wav);
    entries.Add(new()
    {
        ["name"] = name, ["asset"] = asset, ["size"] = wav.Length, ["sha256"] = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant(),
        ["tier"] = OnDemandTier, ["release"] = Release,
    });
    Console.WriteLine($"트랙 {track}: {Path.GetFileName(from)} {new FileInfo(from).Length >> 10}KB → {asset} {wav.Length >> 10}KB · {pcm.Duration.TotalSeconds:0.0}초 {pcm.SampleRate}Hz {pcm.Channels}ch · SNR {10 * Math.Log10(signal / Math.Max(1, noise)):0.0}dB");
}
string list = Path.Combine(repo, "tools", "part1_cd_entries.json");
File.WriteAllText(list, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"{entries.Count}개 → {upload}, 목록 {list}");
Console.WriteLine($"올리기: gh release create {Release} --prerelease --latest=false --title \"파트1 시디 트랙\" --notes \"게임이 받는 파일\" {upload}/*");

// 파일 곡 — BGMs 의 PCM(형식 1) 것만. ADPCM(형식 2) 은 원래 판 그대로라 어느 판에나 있다고 본다(가설 — 판마다 다 본 것은 아님).
string songUpload = Path.Combine(Path.GetFullPath(args[0]), SongRelease);
if (Directory.Exists(songUpload)) Directory.Delete(songUpload, true);
Directory.CreateDirectory(songUpload);
var songs = new List<Dictionary<string, object>>();
for (int n = 0; n < 100; n++)
{
    if (Part1Game.Read("BGMs", $"{n:D4}.bgm") is not { } raw || WavFormat(raw) != 1) continue;
    var pcm = WaveSound.Parse(Part1Game.AdpcmToPcm(raw));
    var (wav, rebuilt) = Adpcm.Encode(pcm, BlockAlign);
    var back = WaveSound.Parse(Part1Game.AdpcmToPcm(wav));
    if (back.SampleRate != pcm.SampleRate || back.Channels != pcm.Channels || !back.Samples.AsSpan().SequenceEqual(rebuilt))
        throw new InvalidDataException($"곡 {n}: 다시 푼 것이 인코더의 복원과 다릅니다");
    string name = $"part1/bgm/{n:D4}.wav", asset = name.Replace('/', '_');
    File.WriteAllBytes(Path.Combine(songUpload, asset), wav);
    songs.Add(new()
    {
        ["name"] = name, ["asset"] = asset, ["size"] = wav.Length, ["sha256"] = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant(),
        ["tier"] = OnDemandTier, ["release"] = SongRelease,
    });
    Console.WriteLine($"곡 {n}: {raw.Length >> 10}KB → {asset} {wav.Length >> 10}KB · {pcm.Duration.TotalSeconds:0.0}초 {pcm.SampleRate}Hz {pcm.Channels}ch");
}
string songList = Path.Combine(repo, "tools", "part1_bgm_entries.json");
File.WriteAllText(songList, JsonSerializer.Serialize(songs, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"{songs.Count}곡 → {songUpload}, 목록 {songList}");
Console.WriteLine($"올리기: gh release create {SongRelease} --prerelease --latest=false --title \"파트1 파일 곡\" --notes \"게임이 받는 파일\" {songUpload}/*");
return 0;

/// <summary>RIFF WAV 의 형식 번호(fmt 첫 낱말) — 못 찾으면 −1.</summary>
static int WavFormat(byte[] wav)
{
    for (int o = 12; o + 10 <= wav.Length;)
    {
        int len = BitConverter.ToInt32(wav, o + 4);
        if (wav.AsSpan(o, 4).SequenceEqual("fmt "u8)) return BitConverter.ToUInt16(wav, o + 8);
        o += 8 + len + (len & 1);
    }
    return -1;
}

/// <summary>
/// MS ADPCM(형식 2) 인코더 — 블록마다 채널별로 표준 계수 7개 × 시작 델타 여러 개를 다 해 보고 오차가 가장 작은 것을 고른다.
/// 셈은 게임의 풀기(<see cref="Part1Game.AdpcmToPcm"/>: 예측 ÷256 은 0 쪽 버림, 델타 = max(16, 델타 × 적응표 ÷ 256))를 그대로 따라
/// 인코더가 셈한 복원이 게임이 듣는 소리와 표본 하나까지 같다.
/// </summary>
static class Adpcm
{
    static readonly (int C1, int C2)[] Coef = [(256, 0), (512, -256), (0, 0), (192, 64), (240, 0), (460, -208), (392, -232)];
    static readonly int[] Adapt = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];
    static readonly int[] Deltas = [.. Enumerable.Range(0, 20).Select(i => (int)(16 * Math.Pow(1.5, i))).Where(d => d <= short.MaxValue)];

    /// <summary>(WAV 파일, 그것을 풀면 나올 16비트 표본).</summary>
    public static (byte[] Wav, short[] Rebuilt) Encode(PcmSound pcm, int blockAlign)
    {
        int ch = pcm.Channels, frames = pcm.FrameCount;
        int perBlock = (blockAlign - 7 * ch) * 2 / ch + 2;
        int blocks = (frames + perBlock - 1) / perBlock;
        var outBlocks = new byte[blocks][];
        var rebuilt = new short[frames * ch];
        Parallel.For(0, blocks, b =>
        {
            int start = b * perBlock, n = Math.Min(perBlock, frames - start), padded = Math.Max(2, n);
            var block = new byte[7 * ch + ((padded - 2) * ch + 1) / 2];
            var nibbles = new byte[ch][];
            for (int c = 0; c < ch; c++)
            {
                short At(int f) => f < n ? pcm.Samples[(start + f) * ch + c] : (short)0;
                long best = long.MaxValue;
                (int Pred, int Delta) pick = (0, 16);
                for (int p = 0; p < Coef.Length; p++)
                    foreach (int d in Deltas)
                    {
                        long err = Run(At, padded, p, d, null, null, best);
                        if (err < best) { best = err; pick = (p, d); }
                    }
                nibbles[c] = new byte[padded];
                var rec = new short[padded];
                Run(At, padded, pick.Pred, pick.Delta, nibbles[c], rec, long.MaxValue);
                for (int f = 0; f < n; f++) rebuilt[(start + f) * ch + c] = rec[f];
                block[c] = (byte)pick.Pred;
                BitConverter.TryWriteBytes(block.AsSpan(ch + 2 * c), (short)pick.Delta);
                BitConverter.TryWriteBytes(block.AsSpan(3 * ch + 2 * c), At(1));
                BitConverter.TryWriteBytes(block.AsSpan(5 * ch + 2 * c), At(0));
            }
            // 바이트마다 높은 4비트가 앞 것 — 표본 차례로 채널을 번갈아(스테레오면 왼쪽 · 오른쪽).
            for (int k = 0; k < (padded - 2) * ch; k++)
            {
                byte v = nibbles[k % ch][2 + k / ch];
                block[7 * ch + k / 2] |= (byte)(k % 2 == 0 ? v << 4 : v);
            }
            outBlocks[b] = block;
        });

        int dataLen = outBlocks.Sum(x => x.Length);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(4 + (8 + 50) + (8 + 4) + 8 + dataLen + (dataLen & 1)); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(50);
        w.Write((ushort)2); w.Write((ushort)ch); w.Write(pcm.SampleRate); w.Write((int)((long)pcm.SampleRate * blockAlign / perBlock));
        w.Write((ushort)blockAlign); w.Write((ushort)4); w.Write((ushort)32); w.Write((ushort)perBlock); w.Write((ushort)Coef.Length);
        foreach (var (c1, c2) in Coef) { w.Write((short)c1); w.Write((short)c2); }
        w.Write("fact"u8); w.Write(4); w.Write(frames);
        w.Write("data"u8); w.Write(dataLen);
        foreach (var block in outBlocks) w.Write(block);
        if ((dataLen & 1) != 0) w.Write((byte)0);
        w.Flush();
        return (ms.ToArray(), rebuilt);
    }

    /// <summary>한 채널 한 블록을 그 계수 · 시작 델타로 싸 본다 — 제곱 오차(<paramref name="stop"/> 를 넘으면 그만). 줄 곳이 있으면 4비트 · 복원을 적는다.</summary>
    static long Run(Func<int, short> at, int n, int pred, int delta, byte[]? nibbles, short[]? rec, long stop)
    {
        var (c1, c2) = Coef[pred];
        int s2 = at(0), s1 = at(1);
        if (rec != null) { rec[0] = (short)s2; rec[1] = (short)s1; }
        long err = 0;
        for (int f = 2; f < n; f++)
        {
            int x = at(f), guess = (s1 * c1 + s2 * c2) / 256, e = x - guess;
            int q = Math.Clamp(e >= 0 ? (e + delta / 2) / delta : -((-e + delta / 2) / delta), -8, 7);
            // 끝(±32767)에 걸려 잘리면 한 칸 안쪽이 나을 수 있다.
            int y = Math.Clamp(guess + q * delta, short.MinValue, short.MaxValue);
            for (int alt = q - 1; alt <= q + 1; alt += 2)
                if (alt is >= -8 and <= 7 && Math.Clamp(guess + alt * delta, short.MinValue, short.MaxValue) is var ya && Math.Abs(x - ya) < Math.Abs(x - y)) { q = alt; y = ya; }
            err += (long)(x - y) * (x - y);
            if (err >= stop) return err;
            int nib = q & 15;
            if (nibbles != null) nibbles[f] = (byte)nib;
            if (rec != null) rec[f] = (short)y;
            (s2, s1) = (s1, y);
            delta = Math.Max(16, delta * Adapt[nib] / 256);
        }
        return err;
    }
}

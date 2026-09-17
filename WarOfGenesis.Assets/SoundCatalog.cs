using System.IO;

namespace WarOfGenesis.Assets;

public enum SoundKind { Music, Effect, Voice }

/// <summary>소리 파일 하나 — 목록 한 줄. 소리는 <see cref="SoundCatalog.Load"/> 할 때 푼다.</summary>
public sealed record SoundEntry(SoundKind Kind, int Id, string Folder, string FileName, long Size, TimeSpan Duration,
                                int SampleRate, int Channels, int BitsPerSample, string Note);

/// <summary>
/// 게임 폴더의 소리 목록 — <c>BGM/*.bgm</c>(Bink: 배경음악 + 인물 대사)과 <c>Snd/*.snd</c>(WAV 효과음, <c>Snd.idx</c> 묶음 + 낱장).
/// 목록은 머리만 읽어 채우고, 소리는 고를 때 푼다.
/// </summary>
/// <remarks>
/// BGM 폴더는 음악과 대사가 한 번호대에 섞여 있다. 스테레오 = 배경음악, 모노 = 인물 대사로 가른다
/// (0019~0063 스테레오 음악 34곡, 3390~3394·3479~3487 스테레오, 나머지 3407개 모노 음성). 근거는 분석-사운드 노트.
/// </remarks>
public static class SoundCatalog
{
    public const string BgmFolder = "BGM";
    public const string SndFolder = "Snd";

    /// <summary>BGM 폴더를 훑어 음악과 대사로 나눈다. 폴더가 없으면 빈 목록.</summary>
    public static (List<SoundEntry> Music, List<SoundEntry> Voices) ScanBgm(string gameRoot)
    {
        var music = new List<SoundEntry>();
        var voices = new List<SoundEntry>();
        string dir = Path.Combine(gameRoot, BgmFolder);
        if (!Directory.Exists(dir)) return (music, voices);

        foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.bgm").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(file.Name), out int id)) continue;
            BinkInfo? info;
            try { info = BinkAudio.ReadInfo(file.FullName); }
            catch (IOException) { continue; }
            if (info == null) continue;
            var kind = info.Channels == 2 ? SoundKind.Music : SoundKind.Voice;
            var entry = new SoundEntry(kind, id, BgmFolder, file.Name, file.Length, info.Duration, info.SampleRate, info.Channels, 16, "");
            (kind == SoundKind.Music ? music : voices).Add(entry);
        }
        return (music, voices);
    }

    /// <summary>Snd 폴더의 효과음 — 색인(<c>Snd.idx</c>)과 낱장을 합친다.</summary>
    public static List<SoundEntry> ScanSnd(GameFiles files)
    {
        var result = new List<SoundEntry>();
        foreach (var (name, size) in files.List(SndFolder, ".snd"))
        {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(name), out int id)) continue;
            var head = files.ReadHead(SndFolder, name, 4096);
            if (head == null || WaveSound.ReadInfo(head, size) is not { } info) continue;
            result.Add(new SoundEntry(SoundKind.Effect, id, SndFolder, name, size, info.Duration, info.SampleRate, info.Channels,
                                      info.BitsPerSample, info.Name ?? ""));
        }
        return result;
    }

    /// <summary>고른 파일을 16비트 PCM 으로 푼다.</summary>
    public static PcmSound Load(GameFiles files, SoundEntry entry)
    {
        byte[] data = files.Read(entry.Folder, entry.FileName)
                      ?? throw new FileNotFoundException($"{entry.Folder}\\{entry.FileName} 을(를) 못 찾았습니다.");
        return entry.Folder == BgmFolder ? BinkAudio.Open(data).Decode() : WaveSound.Parse(data);
    }
}

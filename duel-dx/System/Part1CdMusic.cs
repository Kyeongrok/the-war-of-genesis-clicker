using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 배경음악 n — 원본이 시디 곡으로 트는 곡(<see cref="Part1Game.CdTrack"/>: 이 설치본은 15 · 29)은 뜬 트랙을, 아니면 <c>BGMs\NNNN.bgm</c>(<see cref="Part1Game.Music"/>).
/// </summary>
/// <remarks>
/// 원본 <c>0x100094e0</c> 은 시디 표를 <b>먼저</b> 본다 — 표에 있으면 BGMs 에 파일이 있어도 시디다(<c>0x1000951a</c>). 트랙은 이 차례로 찾는다.
/// <list type="number">
/// <item>사용자 폴더의 WAV(<see cref="Part1Game.CdTrackFiles"/> 중 <c>.wav</c>) — 우리 WAV 읽기로 돈다.</item>
/// <item>이미 받아 둔(실린) 우리 WAV(MS ADPCM, <c>tools/part1-cd-pack</c> · 릴리즈 <c>assets-part1cd-1</c>, <c>assets/part1/cd</c>).</item>
/// <item>사용자 폴더의 그 밖의 것(흔히 <c>bgm\Track02.mp3</c>) — Windows Media Foundation(<see cref="MediaFoundationAudio"/>). 유닉스 이식(user32 흉내)이나
/// MF 가 빠진 Windows N 에서는 DLL 이 없다 — 그 실패는 삼킨다.</item>
/// <item>그래도 없으면 우리 WAV 를 받아 온다 — 받는 동안 화면 오른쪽 아래에 알린다(<see cref="Notice"/>). 뒤 스레드에서 불려 받는 동안 기다린다 —
/// 화면은 안 멈추고, 파트2 곡처럼 오면 그때 튼다.</item>
/// <item>다 없으면 원본처럼 무음 — 파일 곡으로 넘어가지 않는다(곡 15 · 29 는 BGMs 에 없기도 하다).</item>
/// </list>
/// 시디 곡인지는 사용자 폴더의 <c>Tracks.ini</c> · <c>G3CD_k</c> 로 정하므로 파트1 폴더가 없으면 시디 곡도 없다(파트1 은 그 폴더가 있어야 켜진다, <c>Part1TitleScreen</c>).
/// 시험: <c>DUELDX_NOMF=1</c> 이면 MF 가 없는 PC 처럼 MF 를 안 부른다.
/// </remarks>
internal static class Part1CdMusic
{
    /// <summary>받아 온 트랙을 기다리는 가장 긴 시간(초) — 파트2 곡(<c>AssetPack.EnsureNow("bgm", …, 90)</c>)과 같다.</summary>
    private const int FetchSeconds = 90;

    private static readonly bool NoMediaFoundation = Environment.GetEnvironmentVariable("DUELDX_NOMF") == "1";

    /// <summary>곡 n 의 소리 — 없거나 못 풀면 null. <paramref name="log"/> 는 시디 곡을 고른 것 · 못 푼 까닭을 받는다(화면 밖 시험의 자취).</summary>
    public static PcmSound? Music(int n, Action<string>? log = null)
    {
        if (Part1Game.CdTrack(n) is not { } track)
        {
            // 파일 곡(BGMs\NNNN.bgm). 곡 6~14 · 26 은 원래 디스크 2~4 의 시디 곡이라, 판에 따라 BGMs 에 없어 무음이었다(카페 제보 fg-39 인페르노 지하 곡 10).
            // 없거나 못 풀면 우리가 올린 WAV(assets-part1bgm-1, tools/part1-cd-pack)를 받아 쓴다.
            PcmSound? song = null;
            try { song = Part1Game.Music(n); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IndexOutOfRangeException) { log?.Invoke($"music 파트1 곡 {n} BGMs 못 풂: {ex.Message}"); }
            if (song != null) return song;
            if (Fetch(AssetPack.Part1SongKind, $"{n:D4}.wav", $"BGMs\\{n:D4}.bgm 이 없어") is { } songAsset && Decode(songAsset, log, wavOnly: true) is { } fetched)
            {
                log?.Invoke($"music 파트1 곡 {n} → 자산 WAV {Path.GetFileName(songAsset)} {fetched.Duration.TotalSeconds:0.0}초");
                return fetched;
            }
            log?.Invoke($"music 파트1 곡 {n} → BGMs\\{n:D4}.bgm 없음 · 자산 없음 (무음)");
            return null;
        }
        var files = Part1Game.CdTrackFiles(track);
        static bool IsWav(string p) => p.EndsWith(".wav", StringComparison.OrdinalIgnoreCase);
        PcmSound? Use(string path, string from, PcmSound? pcm)
        {
            if (pcm != null) log?.Invoke($"music 파트1 곡 {n} → 시디 트랙 {track}: {from} {Path.GetFileName(path)} {pcm.Duration.TotalSeconds:0.0}초 {pcm.SampleRate}Hz {pcm.Channels}ch");
            return pcm;
        }

        // 1. 사용자 폴더의 WAV.
        foreach (string path in files.Where(IsWav))
            if (Use(path, "사용자 WAV", Decode(path, log)) is { } pcm) return pcm;
        // 2. 이미 받아 둔(실린) WAV — 우리 읽기로만(MF 를 안 탄다).
        string file = $"Track{track:D2}.wav";
        if (AssetPack.FetchExtra(AssetPack.Part1CdKind, file) is { } have && Use(have, "자산 WAV", Decode(have, log, wavOnly: true)) is { } cached) return cached;
        // 3. 사용자 폴더의 그 밖의 것(mp3 · ogg) — 윈도우 디코더(MF).
        foreach (string path in files.Where(p => !IsWav(p)))
            if (Use(path, "사용자 파일(MF)", Decode(path, log)) is { } pcm) return pcm;
        // 4. 그래도 없으면 받아 온다 — 받는 동안 오른쪽 아래에 알린다(사용자 요청: 무영릉 곡이 안 들린다는 제보).
        if (Fetch(AssetPack.Part1CdKind, file, files.Count == 0 ? "시디 트랙 파일이 없어" : "mp3 를 윈도우 디코더로 못 읽어") is { } asset
            && Use(asset, "자산 WAV", Decode(asset, log, wavOnly: true)) is { } got) return got;
        log?.Invoke($"music 파트1 곡 {n} → 시디 트랙 {track}: {(files.Count == 0 ? "트랙 파일 없음" : "못 풂")} · 자산 없음 (무음)");
        return null;
    }

    /// <summary>
    /// 배경음악을 받는 중 · 못 받음 알림 — 화면 오른쪽 아래 귀퉁이(<see cref="GameWindow.DrawMusicNotice"/>). 뒤 스레드가 쓰고 그리기가 읽는다.
    /// </summary>
    internal static volatile Tuple<string, DateTime>? Notice;

    /// <summary>받을 파일 — 이미 있으면 그 자리(알림 없음), 없으면 받기를 걸고 「<paramref name="why"/> 내려받는 중」을 띄운 채 기다린다.</summary>
    private static string? Fetch(string kind, string file, string why)
    {
        if (AssetPack.FetchExtra(kind, file) is { } here) return here;
        var asking = Tuple.Create($"배경음악 {why} 내려받는 중…", DateTime.MaxValue);
        Notice = asking;
        string? got = AssetDownload.WaitExtra(kind, file, FetchSeconds);
        if (ReferenceEquals(Notice, asking))
            Notice = got != null ? Tuple.Create("배경음악을 내려받았습니다", DateTime.UtcNow.AddSeconds(3))
                                 : Tuple.Create("배경음악을 내려받지 못했습니다 — 인터넷 연결을 확인해 주세요", DateTime.UtcNow.AddSeconds(10));
        return got;
    }

    /// <summary>
    /// 오디오 파일 하나를 16비트 PCM 으로 — RIFF WAV(PCM · MS ADPCM, <see cref="Part1Game.AdpcmToPcm"/>)는 우리 읽기로(MF 없이도 돈다),
    /// 그 밖(MP3 · OGG …, 또는 우리가 못 읽는 WAV)은 Media Foundation(<paramref name="wavOnly"/> 면 안 탄다). 못 풀면 null.
    /// </summary>
    public static PcmSound? Decode(string path, Action<string>? log = null, bool wavOnly = false)
    {
        try
        {
            byte[] head = new byte[12];
            using (var f = File.OpenRead(path)) f.ReadExactly(head);
            if (head.AsSpan(0, 4).SequenceEqual("RIFF"u8) && head.AsSpan(8, 4).SequenceEqual("WAVE"u8))
                return WaveSound.Parse(Part1Game.AdpcmToPcm(File.ReadAllBytes(path)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        {
            log?.Invoke($"music {Path.GetFileName(path)} WAV 못 읽음: {ex.Message}");
        }
        if (wavOnly) return null;
        try
        {
            if (NoMediaFoundation) throw new DllNotFoundException("DUELDX_NOMF=1 — MF 가 없는 PC 흉내");
            return MediaFoundationAudio.Decode(path);
        }
        catch (Exception ex)
        {
            // DllNotFoundException · EntryPointNotFoundException · COMException · BadImageFormatException … — 무음으로 둔다(튕기지 않는다).
            log?.Invoke($"music {Path.GetFileName(path)} 못 풂: {ex.GetType().Name} {ex.Message}");
            return null;
        }
    }
}

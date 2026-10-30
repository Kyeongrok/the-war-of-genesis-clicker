using System.Text;

namespace WarOfGenesis.Assets;

/// <summary>파트1 성우 음성 하나 — <c>Snd/NNNN.snd</c> 번호와 처음 나오는 대사, 그 번호를 쓰는 대사 명령 수.</summary>
public sealed record Part1Voice(int Snd, VoiceLine Line, int Uses);

/// <summary>
/// 파트1 성우 대사 — 필드(<c>Flds/NNNN.fld</c>)와 챕터 사건의 행동 206 · 207 · 208 [얼굴, 줄, Snd](분석-파트1-챕터, <c>Part1SceneScript</c>)에서
/// Snd 번호마다 말하는 이 · 대사 글(<c>.flg</c> · <c>.clg</c> 줄)을 잇는다. 편집기 에셋 &gt; 인물 대사의 「파트1 대사」 탭이 쓴다(사용자 요청 asset-5).
/// </summary>
/// <remarks>파트1 전투 대사(200)에는 음성 칸이 없다(분석-파트1-전투 36절). 시판 자료: 필드 1507줄 · 챕터 1줄, 음성 1432개.</remarks>
public static class Part1VoiceLines
{
    public const string Folder = "Snd";

    public static IReadOnlyList<Part1Voice> Load()
    {
        var found = new Dictionary<int, (VoiceLine Line, int Uses)>();
        void Scan(string source, IEnumerable<Part1BattleEvent> events, IReadOnlyList<string> lines)
        {
            foreach (var e in events)
                foreach (var a in e.Actions)
                {
                    if (a.Code is < 206 or > 208 || a.Args.Length < 3 || a.Args[2] <= 0) continue;
                    int snd = a.Args[2];
                    if (found.TryGetValue(snd, out var had)) { found[snd] = (had.Line, had.Uses + 1); continue; }
                    string raw = a.Args[1] >= 0 && a.Args[1] < lines.Count ? lines[a.Args[1]] : "";
                    string text = Part1Game.PlainText(raw).Replace('\n', ' ').Trim();
                    found[snd] = (new VoiceLine(Part1Game.Speaker(raw).Trim(), $"{source} 사건 {e.Index}", text), 1);
                }
        }
        for (int id = 0; id < 1000; id++)
            if (Part1Field.Load(id) is { } f) Scan($"필드 {id:D4}", f.Events, f.Lines);
        for (int id = 0; id < 1000; id++)
            if (Part1Chapter.Load(id) is { } c) Scan($"챕터 {id:D4}", c.Events, c.Lines);
        return [.. found.OrderBy(p => p.Key).Select(p => new Part1Voice(p.Key, p.Value.Line, p.Value.Uses))];
    }

    /// <summary>
    /// 소리 파일 한 줄(목록용) — 파트1 소리는 MS ADPCM 이라 길이를 블록 수 × 블록당 표본(<c>fmt</c> +18) ÷ 표본율로 센다
    /// (머리의 초당 바이트는 맞지 않는다 — 0099 가 2.1초로 나오는데 풀면 8.5초). 파일이 없으면 null.
    /// </summary>
    public static SoundEntry? Entry(Part1Voice voice)
    {
        string name = $"{voice.Snd:D4}.snd";
        if (Part1Game.Read(Folder, name) is not { Length: >= 12 } wav) return null;
        int rate = 0, channels = 0, bits = 0, blockAlign = 0, perBlock = 0;
        long data = 0;
        for (int o = 12; o + 8 <= wav.Length;)
        {
            string id = Encoding.ASCII.GetString(wav, o, 4);
            int len = BitConverter.ToInt32(wav, o + 4);
            if (len < 0) break;
            if (id == "fmt " && o + 28 <= wav.Length)
            {
                channels = BitConverter.ToUInt16(wav, o + 10);
                rate = BitConverter.ToInt32(wav, o + 12);
                blockAlign = BitConverter.ToUInt16(wav, o + 20);
                bits = BitConverter.ToUInt16(wav, o + 22);
                perBlock = BitConverter.ToUInt16(wav, o + 26);
            }
            else if (id == "data") data = Math.Min(len, wav.Length - o - 8);
            o += 8 + len + (len & 1);
        }
        var duration = blockAlign > 0 && rate > 0 ? TimeSpan.FromSeconds((double)(data + blockAlign - 1) / blockAlign * perBlock / rate) : TimeSpan.Zero;
        return new SoundEntry(SoundKind.Voice, voice.Snd, $"파트1 {Folder}", name, wav.Length, duration, rate, channels, bits, "");
    }

    /// <summary>소리를 푼다 — ADPCM 을 PCM 으로(<see cref="Part1Game.AdpcmToPcm"/>) 바꾼 뒤 <see cref="WaveSound.Parse"/>.</summary>
    public static PcmSound Decode(int snd) =>
        WaveSound.Parse(Part1Game.AdpcmToPcm(Part1Game.Read(Folder, $"{snd:D4}.snd")
                                             ?? throw new FileNotFoundException($"파트1 Snd\\{snd:D4}.snd 을(를) 못 찾았습니다.")));
}

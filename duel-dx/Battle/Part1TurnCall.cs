using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 인물의 차례 목소리 — <b>원본에 없는 리메이크만의 것</b>(사용자 요청). 원본 파트1 은 차례가 와도 목소리를 안 낸다.
/// 필드 대사의 성우 음성(행동 206~208 [얼굴, 줄, Snd]) 가운데 어울리는 한 마디를 골라, 내가 움직이는 그 인물의 차례가 오면 낸다
/// (<see cref="BattleScene.PlayTurnCall"/> — 파트2 차례 목소리와 같은 자리 · 같은 조건). 모드 &gt; 일반 「파트1 차례 목소리」로 끈다(기본 켬).
/// </summary>
/// <remarks>인물을 더하려면 <see cref="People"/> 에 (그 사람의 파트1 Chr 번호들, 목소리들) 한 줄을 넣는다. 목소리가 여럿이면 차례마다 틱으로 고른다.
/// 긴 대사의 한 토막만 쓰려면 (Snd, 시작 초, 끝 초)로 적는다 — 원본 소리를 저장소에 싣지 않고 이 PC 의 파트1 Snd 에서 그 구간만 잘라 튼다.
/// 파트1 에셋 팩(<see cref="Part1Skill.PackList"/>)에도 저절로 실린다.</remarks>
internal static class Part1TurnCall
{
    /// <summary>목소리 한 마디 — 파트1 Snd 번호와 그 안의 구간(초). <c>To</c> 가 0 이면 끝까지.</summary>
    internal readonly record struct Clip(int Snd, double From = 0, double To = 0)
    {
        public bool Whole => From <= 0 && To <= 0;
        public static implicit operator Clip(int snd) => new(snd);
    }

    /// <summary>
    /// 인물 묶음 — 같은 사람이 판마다 다른 Chr 번호로 나오므로(버몬트 대공 30 · 107 · 152 …) 번호 여럿에 같은 목소리들을 준다.
    /// 목소리는 인물마다 몇 개든 — 차례마다 틱으로 하나를 고른다.
    /// 사용자가 골라 둔 목소리(fg-88)는 바탕화면 「음성」 폴더의 「번호_인물_대사.wav」 — 번호가 파트1 Snd 이고, 잘라 낸 것은 원본에서 찾은 구간을 적었다.
    /// </summary>
    private static readonly (int[] Chrs, Clip[] Clips, string Who)[] People =
    [
        ([0, 179], [new(726, 8.650), new(759, 3.433, 5.927)], "살라딘"),   // 「그때까지만 버티면 되는 거지」 · 「모든 일을 긍정적으로 생각하세요」
        ([1, 365], [1410, 1422], "마르자나"),                              // 「제대로 찾은 것 같군요」 · 「하지만 뭐죠?」
        ([2], [new(642, 1.602)], "무카파"),                                  // 「자, 모두들 서두릅시다!」(사용자 토막 — 사이 쉼 0.05초를 뺐는데 여기서는 그대로)
        ([10], [1107, 1141], "셰라자드"),   // 필드 0022 사건 3 「살라딘님!」(208 [311, 16, 1107], fg-45) · 필드 0029 사건 2 「왜 웃으세요?」(207 [311, 26, 1141], fg-87)
        ([11, 611], [new(1438, 0, 1.728)], "이븐 시나"),                    // 「기가 막힌 아이디업니다」
        ([12], [new(1823, 2.033)], "샤프리야르"),                           // 「많은 도움을 주었습니다」
        ([16, 487], [new(1248, 0, 3.046)], "얀 지슈카"),                    // 「정말? 너 같은 약골이? …」(사용자 파일은 가운데 쉼 0.34초를 뺐다 — 여기서는 그대로)
        ([30, 107, 152, 178, 316, 362], [298, 304], "버몬트 대공"),          // 「조금 늦었군」 · 「물론이지」
        ([32, 181, 317, 364], [new(696, 2.028)], "어윈 록슬리"),              // 「이 시간에 여기는 어쩐 일로…」
        ([38], [1729], "R.엘핀스톤"),                                       // 「너무 일찍 왔나」
        ([42], [1566, new(1568, 0, 1.291)], "롤랑 솔즈베리"),               // 「그렇다면 대공께서는」 · 「그건 알고 있어요」
        ([65, 184], [124, 148, 157, 1872], "크리스티앙"),   // 필드 0094 「좋아, 좋아. 그럼 열심히 하자구!」(207 [340, 13, 124]) · 「뭐라고요」 · 「어이, 아저씨」 · 「알겠다구요!」
        ([71, 302], [1622, 1650], "죠안 카트라이트"),                       // 「뭐라고요!」 · 「알겠습니다」
    ];

    /// <summary>파트1 Chr 번호(<see cref="Part1Game.ChrBase"/> 를 뺀 것) → 목소리들.</summary>
    internal static readonly IReadOnlyDictionary<int, Clip[]> Calls =
        People.SelectMany(p => p.Chrs.Select(c => (c, p.Clips))).ToDictionary(x => x.c, x => x.Clips);

    /// <summary>그 유닛의 차례 목소리 — 없으면 null.</summary>
    internal static Clip[]? For(UnitState u) =>
        u.ChrCode > Part1Game.ChrBase && Calls.TryGetValue(u.ChrCode - Part1Game.ChrBase, out var clips) ? clips : null;

    /// <summary>에셋 팩에 실을 파트1 Snd 번호.</summary>
    internal static IEnumerable<int> Sounds => People.SelectMany(p => p.Clips).Select(c => c.Snd).Distinct();

    /// <summary>잘라 낸 토막을 넣어 둘 소리 번호의 바탕 — 파트1 소리(20000+)와 겹치지 않게.</summary>
    private const int ClipSoundBase = 60000;

    private static readonly List<Clip> _clipIds = [];

    /// <summary>
    /// 틀 소리 번호 — 통째면 파트1 Snd 그대로, 토막이면 원본을 읽어(파트1 폴더에서 바로 푼다) 그 구간만 잘라 따로 번호를 붙여 둔다.
    /// 원본을 아직 못 읽었으면 0(이번 차례는 조용히 — <see cref="GameWindow.Play"/> 와 같다).
    /// </summary>
    internal static int SoundOf(GameWindow host, Clip clip)
    {
        int whole = Part1Game.ObsBase + clip.Snd;
        if (clip.Whole) return whole;
        int index = _clipIds.IndexOf(clip);
        if (index < 0) { _clipIds.Add(clip); index = _clipIds.Count - 1; }
        int id = ClipSoundBase + index;
        lock (host._sfx) if (host._sfx.ContainsKey(id)) return id;
        PcmSound? full;
        lock (host._sfx) host._sfx.TryGetValue(whole, out full);
        if (full == null && host.PrefetchSound(whole) is { } task && task.Wait(300))
            lock (host._sfx) host._sfx.TryGetValue(whole, out full);
        if (full == null) return 0;
        int frames = full.FrameCount;
        int from = Math.Clamp((int)(clip.From * full.SampleRate), 0, frames);
        int to = clip.To > 0 ? Math.Clamp((int)(clip.To * full.SampleRate), from, frames) : frames;
        var cut = new PcmSound(full.Samples[(from * full.Channels)..(to * full.Channels)], full.SampleRate, full.Channels);
        lock (host._sfx) host._sfx[id] = cut;
        return id;
    }
}

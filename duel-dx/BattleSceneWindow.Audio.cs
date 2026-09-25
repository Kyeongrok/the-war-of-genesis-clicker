using System.IO;
using System.Threading;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투 소리 — 동작(Obs 모션)에 박힌 효과음(fa-4)과 전투 배경음악(fa-5). 옵시디안 분석-사운드 "전투 효과음 · 전투 배경음악".
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>효과음은 코드가 아니라 <b>자료</b>에 있다 — Obs 모션의 시간줄 소리 키(종류 1, 인자 0 = Snd 번호)를 그 틱에 튼다.
///   종류 2 키가 띄우는 자식 이펙트(예 제이슨 기본공격의 Obs 0426)의 소리까지 따라간다.</item>
/// <item>어빌리티는 핸들러가 띄우는 이펙트에 소리가 있어, 그 결과만 <see cref="AbilityEffectSounds"/> 표로 옮겼다(힐 85, 큐어 695 …).</item>
/// <item>맞으면 <c>Dat/Dmg.dat</c> 의 「맞는 목소리」 둘 중 하나(같은 목소리가 울리는 중이면 안 냄), 차례가 오면 「부르는 목소리」 넷 중 하나.</item>
/// <item>쓰러질 때 106. 배경음악은 Btl 머리 8번째 워드(코어헌터 훈련장 = 42) 를 통째로 되풀이, 승리 3392·패배 55 는 한 번.</item>
/// </list>
/// 원본의 좌우 소리(팬)·음량 감쇠·휴식 소리는 넣지 않았다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private readonly AudioMixer _mixer = new();
    private readonly Dictionary<int, PcmSound> _sfx = [];
    private readonly Dictionary<int, ObsMotionTable?> _effectTables = [];
    private readonly List<(double Time, int Sound)> _pendingSounds = [];
    private readonly Dictionary<int, (int[] Hurt, int[] Call)> _voices = [];

    private const int SoundDeath = 106, SoundLevelUp = 107;
    private const float MusicGain = 0.9f;

    /// <summary>효과음 크기(0~1) — 시스템 메뉴 음량 창에서 바꾼다.</summary>
    private float _effectGain = 0.9f;

    /// <summary>work 번호 → 그 어빌리티를 쓸 때 (때리는 순간부터 몇 틱 뒤, Snd 번호). 분석-사운드 표에서 옮겼다.</summary>
    private static readonly (int Work, (int Tick, int Sound)[] Sounds)[] AbilityEffectSounds =
    [
        (58, [(0, 694), (1, 85)]),      // 힐
        (87, [(0, 694), (1, 695)]),     // 큐어
        (59, [(0, 694), (1, 662), (1, 675)]),   // 격려
        (467, [(0, 694), (1, 123)]),    // 블레이드 미사일
        (469, [(0, 694), (1, 18), (1, 19)]),    // 메테오
        (735, [(0, 694), (1, 738)]),    // 크래쉬 봄
        (1583, [(1, 657)]),             // 이스케이프
        (10, [(1, 86)]),                // 비
        (1479, [(23, 770)]),            // 현혹령 기본공격 — 이펙트 Obs 1401 은 그림 없이 소리 770 만 23틱에 낸다(work_script 1479)
    ];

    /// <summary>어빌리티 번호 → 효과음(레벨이 달라도 같은 소리로 본다).</summary>
    private readonly Dictionary<int, (int Tick, int Sound)[]> _abilitySounds = [];

    /// <summary>효과음·배경음악·목소리 표를 읽는다. 소리 자료가 없으면 조용히 넘어간다.</summary>
    private void LoadAudio()
    {
        try
        {
            string folder = AssetsFolder.Find("sounds");
            foreach (string path in Directory.EnumerateFiles(folder, "*.wav"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int id))
                    _sfx[id] = WaveSound.Parse(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or DirectoryNotFoundException) { }

        try
        {
            foreach (string path in Directory.EnumerateFiles(AssetsFolder.Find("effects"), "*.obs"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int id))
                    _effectTables[id] = ObsMotionTable.Load(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or DirectoryNotFoundException) { }

        if (_db != null)
        {
            foreach (var (work, sounds) in AbilityEffectSounds)
                if (_db.Works.TryGetValue(work, out var w) && w.AbilityId != 0) _abilitySounds[w.AbilityId] = sounds;

            // Dat/Dmg.dat — 14바이트 레코드(묶음 번호, 맞는 목소리 2, 부르는 목소리 4). .chr 파일 6 이 묶음 번호다.
            if (_db.Files.Read("Dat", "Dmg.dat") is { } dmg)
                for (int o = 6; o + 14 <= dmg.Length; o += 14)
                {
                    int set = BitConverter.ToUInt16(dmg, o);
                    int[] Ids(int start, int count) => [.. Enumerable.Range(0, count)
                        .Select(i => (int)BitConverter.ToUInt16(dmg, o + start + 2 * i)).Where(v => v is > 0 and < 0xFFFF)];
                    _voices[set] = (Ids(2, 2), Ids(6, 4));
                }
        }

        // 타이틀에서 시작하면 아직 아무 전투도 안 열렸다 — 그때 전투 음악을 걸면 타이틀 음악을 튼 뒤에야
        // 풀리기가 끝나 타이틀 위로 전투 음악이 덮어씌워진다.
        if (_battleLoaded) StartBattleMusic();
    }

    /// <summary>DUELDX_MUTE=1 이면 소리를 내지 않는다 — 자동 테스트가 사용자 스피커로 소리를 내지 않게.</summary>
    private static readonly bool Muted = Environment.GetEnvironmentVariable("DUELDX_MUTE") == "1";

    private void Play(int sound, int tag = 0)
    {
        bool loaded = _sfx.TryGetValue(sound, out var pcm);
        // DUELDX_TRACE 면 무슨 소리를 틀었는지(파일이 있었는지) 적는다 — 음소거한 화면 밖 시험에서도 재생 여부를 본다.
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"sound {sound} {(loaded ? "재생" : "파일 없음")} at {_lastTime:0.00}" + Environment.NewLine);
        if (!Muted && loaded) _mixer.PlayEffect(pcm!, _effectGain, tag);
    }

    /// <summary>배경음악(assets/bgm) 한 곡 — Bink 음악을 풀어 튼다. 푸는 데 1~2초 걸려 배경 실에서 한다.</summary>
    private void PlayMusicFile(int id, bool loop)
    {
        if (Muted) return;
        // 새 음악은 늘 제 크기로 시작한다 — 앞 장면이 줄여 둔 크기를 물려받으면 안 들린다.
        _musicFade = null;
        _musicGain = MusicGain;
        // 풀리는 동안 다른 곡을 걸었으면 이 곡은 버린다 — 늦게 풀린 곡이 새 곡을 덮어쓰지 않게.
        int request = Interlocked.Increment(ref _musicRequest);
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                string path = Path.Combine(AssetsFolder.Find("bgm"), $"{id:D4}.bgm");
                if (!File.Exists(path)) return;
                var pcm = BinkAudio.Open(path).Decode();
                if (request != Volatile.Read(ref _musicRequest)) return;
                _mixer.PlayMusic(pcm, loop, MusicGain);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or DirectoryNotFoundException) { }
        });
    }

    /// <summary>이미 푼 대사 소리(BGM 번호 → 소리) — 행동 500 이 같은 것을 두 번 풀지 않게.</summary>
    private readonly Dictionary<int, PcmSound> _eventVoices = [];

    /// <summary>
    /// 전투 이벤트 행동 500 — <c>BGM\NNNN.bgm</c> 을 <b>한 번</b> 틀고 그 소리가 끝날 때까지 이벤트를 멈춘다.
    /// </summary>
    /// <remarks>
    /// 원본 <c>0x10053ec0</c> 은 0x74 바이트짜리 개체를 만들어(<c>0x100d2de0</c>) 인자0 번호로 <c>Bgm\%04d.bgm</c> 을 걸고,
    /// 그 개체의 <c>+0x58</c> 이 0 이 될 때까지(=다 울릴 때까지) 다음 줄로 안 간다. 배경음악과는 <b>다른 개체</b>라 전투 BGM 은 그대로 흐른다 —
    /// 그래서 여기서도 배경음악을 끄지 않고 효과음 쪽으로 낸다. 인자1 이 0 이 아니면 그 유닛에 매단다(좌우 소리는 안 넣었다).
    /// 쓰는 곳은 Btl 0334~0338 의 3485 하나뿐이다.
    /// </remarks>
    private void PlayEventVoice(int id)
    {
        if (id <= 0) return;
        if (CachedClip(id) is { } have) { StartEventVoice(have); return; }
        _eventSoundLoading = true;
        LoadClip(id, pcm =>
        {
            if (pcm != null) StartEventVoice(pcm);
            else _eventSoundSeconds = 0.1f;              // 소리가 없으면 곧바로 다음 줄로
            _eventSoundLoading = false;
        });
    }

    /// <summary>지금 울리는 대사 음성의 표지 — 다음 대사가 뜨거나 대사를 닫으면 끊는다.</summary>
    private int _talkVoiceTag;

    /// <summary>
    /// 대사 음성 — 전투 대사 상자(600) 인자 3, 필드 600~602 인자 2 · 603 인자 1 · 609 인자 3 이 <c>Bgm\NNNN.bgm</c> 번호다(0 이면 목소리 없음).
    /// 원본은 음성을 <c>0x100f5170</c> 으로 튼다. 배경음악과 다른 개체라 BGM 은 그대로 흐른다. 푸는 사이에 다음 대사로 넘어갔으면 틀지 않는다.
    /// </summary>
    private void PlayTalkVoice(int id)
    {
        StopTalkVoice();
        if (id <= 0) return;
        int tag = _talkVoiceTag = ++_soundTag;
        LoadClip(id, pcm =>
        {
            if (!Muted && pcm != null && tag == Volatile.Read(ref _talkVoiceTag)) _mixer.PlayEffect(pcm, _effectGain, tag);
            if (Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                                   $"talk voice {id}: {(pcm == null ? "파일 없음" : $"{ClipSeconds(pcm):0.0}초")}" + Environment.NewLine);
        });
    }

    private void StopTalkVoice()
    {
        if (_talkVoiceTag != 0) _mixer.StopEffect(_talkVoiceTag);
        _talkVoiceTag = 0;
    }

    /// <summary><c>assets/bgm/NNNN.bgm</c> 한 자락을 배경 실에서 풀어 <paramref name="then"/> 에 넘긴다(못 읽으면 null).</summary>
    private void LoadClip(int id, Action<PcmSound?> then)
    {
        if (CachedClip(id) is { } cached) { then(cached); return; }
        System.Threading.Tasks.Task.Run(() =>
        {
            PcmSound? pcm = null;
            try
            {
                string path = Path.Combine(AssetsFolder.Find("bgm"), $"{id:D4}.bgm");
                if (File.Exists(path)) pcm = BinkAudio.Open(path).Decode();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or DirectoryNotFoundException) { }
            if (pcm != null) lock (_eventVoices) _eventVoices[id] = pcm;
            then(pcm);
        });
    }

    /// <summary>이미 푼 소리 — 배경 실이 같은 표에 쓰니 <b>잠그고</b> 본다(안 잠그면 Dictionary 가 깨진다).</summary>
    private PcmSound? CachedClip(int id)
    {
        lock (_eventVoices) return _eventVoices.GetValueOrDefault(id);
    }

    private static float ClipSeconds(PcmSound pcm)
    {
        int frames = pcm.Channels > 0 ? pcm.Samples.Length / pcm.Channels : 0;
        return pcm.SampleRate > 0 ? Math.Max(0.1f, frames / (float)pcm.SampleRate) : 0.1f;
    }

    /// <summary>
    /// 필드 스크립트의 <b>소리 채널</b>(행동 501·504·505) — 채널 번호 → (끝나는 때, 소리표). 푸는 중이면 끝나는 때가 <see cref="double.MaxValue"/>.
    /// </summary>
    /// <remarks>
    /// 원본은 채널마다 0x58 바이트 개체를 만들어 <c>[0x101bfe1c + 채널*4 + 0x200]</c> 에 넣고(<c>0x100ee960</c>),
    /// 그 개체가 <c>Bgm\%04d.bgm</c> 을 튼다. 행동 <b>504</b> 는 스크립트 진행기(<c>0x100f489b</c>)가 직접 보는 자리라
    /// 그 칸이 빌 때까지 다음 줄로 안 간다. 행동 <b>505</b> 는 개체를 지워 소리를 끊는다.
    /// </remarks>
    private readonly Dictionary<int, (double Until, int Tag)> _soundChannels = [];

    private int _soundTag;

    /// <summary>행동 501 — 채널에 소리를 걸고 <b>기다리지 않는다</b>.</summary>
    private void PlayChannelSound(int channel, int id, bool loop)
    {
        StopChannelSound(channel);
        if (id <= 0) return;
        int tag = ++_soundTag;
        lock (_soundChannels) _soundChannels[channel] = (double.MaxValue, tag);
        LoadClip(id, pcm =>
        {
            lock (_soundChannels)
            {
                if (!_soundChannels.TryGetValue(channel, out var cur) || cur.Tag != tag) return;   // 그 사이 다른 소리로 갈렸다
                if (pcm == null) { _soundChannels.Remove(channel); return; }
                // 되풀이하는 소리는 505 로 끌 때까지 도니, 504 가 영영 기다리지 않게 끝나는 때를 지금으로 둔다.
                _soundChannels[channel] = (loop ? 0 : _lastTime + ClipSeconds(pcm), tag);
            }
            if (!Muted && pcm != null) _mixer.PlayEffect(pcm, _effectGain, tag, loop);
        });
    }

    /// <summary>행동 505 — 그 채널의 소리를 끊는다.</summary>
    private void StopChannelSound(int channel)
    {
        int tag;
        lock (_soundChannels)
        {
            if (!_soundChannels.TryGetValue(channel, out var cur)) return;
            tag = cur.Tag;
            _soundChannels.Remove(channel);
        }
        _channelFades.Remove(channel);
        _mixer.StopEffect(tag);
    }

    /// <summary>
    /// 행동 <b>506</b> 이 걸어 둔 채널 음량 바꾸기 — 채널 번호 → (시작 크기, 목표 크기, 시작한 때, 걸리는 초).
    /// </summary>
    /// <remarks>
    /// 원본 <c>0x100eeb80</c> 은 한 줄에 머물며 매 틀 <c>지금 = 시작 + (목표 − 시작) × 지난 틀 ÷ 인자2</c> 로 채널 개체의
    /// 음량(<c>+0x4c</c>, 처음은 100)을 바꾸고(<c>0x100f5330</c>), 인자2 틀이 지나야 다음 줄로 간다. 배경음악의 517 과 같은 꼴이다.
    /// 자료는 `506 [1, 0, 80]`·`506 [1, 0, 40]` 둘 — 채널 1 을 0 까지 서서히 줄여 끈다.
    /// </remarks>
    private readonly Dictionary<int, (float From, float To, double Start, double Seconds)> _channelFades = [];

    /// <summary>행동 506 — 채널 음량을 <paramref name="percent"/>(0~100)까지 <paramref name="ticks"/> 틱에 걸쳐 바꾼다.</summary>
    private void FadeChannelSound(int channel, int percent, int ticks)
    {
        float to = Math.Clamp(percent, 0, 100) / 100f * _effectGain;
        // 바꾸는 도중에 또 걸면 지금 크기에서 이어 간다.
        float from = _channelFades.TryGetValue(channel, out var cur) ? ChannelGainNow(cur, _lastTime) : _effectGain;
        _channelFades[channel] = (from, to, _lastTime, Math.Max(1, ticks) / TicksPerSecond);
    }

    private static float ChannelGainNow((float From, float To, double Start, double Seconds) f, double now)
    {
        double t = Math.Clamp((now - f.Start) / f.Seconds, 0, 1);
        return (float)(f.From + (f.To - f.From) * t);
    }

    /// <summary>걸어 둔 채널 음량 바꾸기를 한 걸음 나아가게 한다 — 매 틀 부른다.</summary>
    private void StepChannelFades()
    {
        if (_channelFades.Count == 0) return;
        foreach (int channel in _channelFades.Keys.ToArray())
        {
            var f = _channelFades[channel];
            double t = Math.Clamp((_lastTime - f.Start) / f.Seconds, 0, 1);
            float gain = ChannelGainNow(f, _lastTime);
            int tag;
            lock (_soundChannels)
            {
                if (!_soundChannels.TryGetValue(channel, out var cur)) { _channelFades.Remove(channel); continue; }
                tag = cur.Tag;
            }
            _mixer.SetEffectGain(tag, gain);
            if (t >= 1) _channelFades.Remove(channel);
        }
    }

    /// <summary>행동 504 — 그 채널이 아직 울리고 있나.</summary>
    private bool ChannelBusy(int channel)
    {
        lock (_soundChannels)
            return _soundChannels.TryGetValue(channel, out var cur) && cur.Until > _lastTime;
    }

    /// <summary>필드를 떠날 때 채널을 모두 끈다.</summary>
    private void StopAllChannelSounds()
    {
        int[] channels;
        lock (_soundChannels) channels = [.. _soundChannels.Keys];
        foreach (int ch in channels) StopChannelSound(ch);
    }

    /// <summary>푼 소리를 틀고 그 길이만큼 이벤트를 멈추게 한다.</summary>
    private void StartEventVoice(PcmSound pcm)
    {
        if (!Muted) _mixer.PlayEffect(pcm, _effectGain);
        _eventSoundSeconds = ClipSeconds(pcm);
    }

    /// <summary>마지막으로 건 곡의 번호표 — 풀리는 데 1~2초 걸리는 사이 다른 곡이 걸리면 먼저 것은 버린다.</summary>
    private int _musicRequest;

    private void StartBattleMusic() => PlayMusicFile(_scene.Bgm, loop: true);

    private void PlayOutcomeMusic(bool win)
    {
        _mixer.StopMusic();
        PlayMusicFile(win ? 3392 : 55, loop: false);
    }

    // ── 동작에 박힌 소리 ─────────────────────────────────────────────────────

    /// <summary>동작을 시작할 때, 그 모션(과 자식 이펙트 모션)의 소리를 틱에 맞춰 예약한다.</summary>
    private void ScheduleActionSounds(UnitState u, int action)
    {
        if (!_sprites.TryGetValue(u.ChrCode, out var sprite) || sprite.Clip(action, u.Facing) is not { } clip) return;
        foreach (var (start, sound) in clip.Sounds) _pendingSounds.Add((_lastTime + start / TicksPerSecond, sound));
        foreach (var (start, obs, motion, _, _, _, _) in clip.Children)
        {
            if (_effectTables.GetValueOrDefault(obs)?.Clips.GetValueOrDefault(motion) is not { } child) continue;
            foreach (var (s, sound) in child.Sounds) _pendingSounds.Add((_lastTime + (start + s) / TicksPerSecond, sound));
        }
    }

    /// <summary>어빌리티 이펙트 소리를 때리는 순간 기준으로 예약한다.</summary>
    private void ScheduleAbilitySounds(WorkData work)
    {
        if (!_abilitySounds.TryGetValue(work.AbilityId, out var sounds)) return;
        foreach (var (tick, sound) in sounds) _pendingSounds.Add((_lastTime + tick / TicksPerSecond, sound));
    }

    /// <summary>한 걸음 뗄 때 나는 소리 — 걷기 모션(동작 1)의 소리 키를 그 틱에 맞춰 예약한다(가이아버그처럼 걸을 때 소리가 나는 인물이 있다).</summary>
    private void PlayWalkSound(UnitState unit)
    {
        if (!_sprites.TryGetValue(unit.ChrCode, out var sprite)) return;
        foreach (var (tick, sound) in sprite.WalkSounds(unit.Facing))
            _pendingSounds.Add((_lastTime + tick / TicksPerSecond, sound));
    }

    /// <summary>배경음악 크기가 옮겨 가는 중 — (시작 크기, 목표 크기, 걸리는 틱, 시작한 때). 필드 행동 517.</summary>
    private (float From, float To, int Ticks, double Start)? _musicFade;

    /// <summary>
    /// 배경음악 크기를 <paramref name="percent"/>(0~100)까지 <paramref name="ticks"/> 틱에 걸쳐 옮긴다.
    /// </summary>
    /// <remarks>
    /// 필드 행동 517 이 쓴다. 자료의 a0 은 299번이 0(끄기) · 179번이 80 · 127번이 100 이고,
    /// a1 은 20~80 틱이 대부분이라 <b>정말로 서서히 옮기는 연출</b>이다. 0 까지 다 내려가면 음악을 끊는다.
    /// </remarks>
    private void FadeMusic(int percent, int ticks)
    {
        float to = Math.Clamp(percent, 0, 100) / 100f * MusicGain;
        if (ticks <= 0)
        {
            _musicFade = null;
            if (to <= 0) _mixer.StopMusic();
            else _mixer.SetMusicGain(to);
            return;
        }
        _musicFade = (_musicGain, to, ticks, _lastTime);
    }

    /// <summary>지금 배경음악 크기 — 옮기는 중에도 어디까지 왔는지 알아야 해서 따로 들고 있다.</summary>
    private float _musicGain = MusicGain;

    private void StepMusicFade()
    {
        if (_musicFade is not { } fade) return;
        double step = Math.Clamp((_lastTime - fade.Start) * TicksPerSecond / fade.Ticks, 0, 1);
        _musicGain = (float)(fade.From + (fade.To - fade.From) * step);
        _mixer.SetMusicGain(_musicGain);
        if (step < 1) return;
        _musicFade = null;
        if (_musicGain <= 0) _mixer.StopMusic();
    }

    private void UpdateSounds()
    {
        StepMusicFade();
        for (int i = _pendingSounds.Count - 1; i >= 0; i--)
            if (_pendingSounds[i].Time <= _lastTime)
            {
                Play(_pendingSounds[i].Sound);
                _pendingSounds.RemoveAt(i);
            }
    }

    // ── 목소리 ───────────────────────────────────────────────────────────────

    /// <summary>맞았을 때 나는 목소리 — 같은 인물의 목소리가 아직 울리는 중이면 안 낸다.</summary>
    private void PlayHurtVoice(UnitState u)
    {
        int tag = 1000 + Array.IndexOf(_units, u);
        if (u.Data == null || _mixer.IsPlaying(tag)) return;
        var hurt = _voices.GetValueOrDefault(u.Data.VoiceSet).Hurt;
        if (hurt is { Length: > 0 }) Play(hurt[_rng.Next(hurt.Length)], tag);
    }

    /// <summary>차례가 왔을 때 부르는 목소리(넷 중 하나).</summary>
    private void PlayTurnVoice(UnitState u)
    {
        if (u.Data == null) return;
        var call = _voices.GetValueOrDefault(u.Data.VoiceSet).Call;
        if (call is { Length: > 0 }) Play(call[_tick & 3], 1000 + Array.IndexOf(_units, u));
    }
}

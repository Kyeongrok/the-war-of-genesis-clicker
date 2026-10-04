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
/// 효과음은 화면 x 로 좌우를 가른다(감사4 S1, <c>0x10028b60</c>). 화면 밖 거리 감쇠·휴식 소리는 넣지 않았다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>믹서 — B.G.M 설정은 켤 때부터 곱한다(감사4 V1·V2).</summary>
    private readonly AudioMixer _mixer = new() { MusicVolume = Math.Clamp(UserSettings.Current.BgmVolume, 5, 100) / 100f };
    private readonly Dictionary<int, PcmSound> _sfx = [];
    private readonly Dictionary<int, ObsMotionTable?> _effectTables = [];
    private readonly SoundQueue _pendingSounds = [];
    private readonly Dictionary<int, (int[] Hurt, int[] Call)> _voices = [];

    /// <summary>
    /// 예약한 효과음 — (때, Snd 번호, 자리 x). x 는 전투면 판 픽셀, 필드·모세스면 640 틀 안 x, 모르면 NaN(가운데).
    /// 자리 없이 (때, 번호) 둘만 넣는 곳(다른 파일)도 그대로 받는다.
    /// </summary>
    private sealed class SoundQueue : List<(double Time, int Sound, float X)>
    {
        public void Add((double Time, int Sound) s) => Add((s.Time, s.Sound, float.NaN));
        public void AddRange(IEnumerable<(double Time, int Sound)> list) { foreach (var s in list) Add(s); }
    }

    private const int SoundDeath = 106, SoundLevelUp = 107;

    /// <summary>
    /// 곡의 제 크기(논리 100 %). 설정 창 B.G.M 막대는 믹서의 <see cref="AudioMixer.MusicVolume"/> 가 <b>모든 곡·페이드·줄이기</b>에 곱한다
    /// (<c>0x10025320</c>·<c>0x100253e0</c>, 감사4 V1) — 전에는 0.9 상수라 막대를 내려도 다음 곡부터 원래 크기로 돌아왔다.
    /// </summary>
    private const float MusicGain = 1f;

    /// <summary>음성(Bink) 크기(0~1) = SE 설정/100, 선형(<c>0x100250c0</c>). 시작 때 저장값으로 채운다(<see cref="LoadAudio"/>, 감사4 V2).</summary>
    private float _effectGain = Math.Clamp(UserSettings.Current.SeVolume, 5, 100) / 100f;

    /// <summary>
    /// Snd 효과음 크기 — 원본은 DirectSound <c>SetVolume(SE×5000/100 − 5000)</c>(1/100 dB, <c>0x10028bc0~0x10028c0e</c>)라
    /// <b>막대 1점당 −0.5 dB</b>(SE 50 = −25 dB)다(감사4 V3). 음성은 선형 <see cref="_effectGain"/> 그대로.
    /// </summary>
    private float SndGain => _seVolume >= 100 ? 1f : (float)Math.Pow(10, (_seVolume - 100) / 40.0);

    /// <summary>work 번호 → 그 어빌리티를 쓸 때 (때리는 순간부터 몇 틱 뒤, Snd 번호). 분석-사운드 표에서 옮겼다.</summary>
    // 시전 소리 694 는 UseWorkRoutine 이 준비 2·3·5·6 에 시작 +2틱으로 낸다 — 여기에도 있으면 두 번 난다(ba-20 Q S-1).
    // 손으로 적은 표는 비웠다(ba-21 sound D3) — 아홉 줄 모두 같은 번호가 스크립트 이펙트의 소리 키에 있어(312:0 85 · 312:1 695 · 1324:1 662 …)
    // 이펙트가 제 틱·제 자리에서 낸다. 표가 있으면 가운데에서 한 번 더(크래쉬 봄 738 은 5틱 이르게) 났다.
    private static readonly (int Work, (int Tick, int Sound)[] Sounds)[] AbilityEffectSounds = [];

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

        // 저장된 B.G.M·S.E 크기를 시작 때 바로 넣는다 — 전에는 막대를 눌러야만 먹어 재시작하면 무시됐다(감사4 V2).
        ApplyVolumes();

        // 타이틀에서 시작하면 아직 아무 전투도 안 열렸다 — 그때 전투 음악을 걸면 타이틀 음악을 튼 뒤에야
        // 풀리기가 끝나 타이틀 위로 전투 음악이 덮어씌워진다.
        if (_battleLoaded) StartBattleMusic();
    }

    /// <summary>DUELDX_MUTE=1 이면 소리를 내지 않는다 — 자동 테스트가 사용자 스피커로 소리를 내지 않게.</summary>
    private static readonly bool Muted = Environment.GetEnvironmentVariable("DUELDX_MUTE") == "1";

    /// <summary>Snd 효과음을 한 번 튼다.</summary>
    /// <param name="screenX">640 틀 기준 화면 x — 좌우를 가른다(NaN 이면 가운데). <see cref="SoundScreenX"/> 로 바꿔 넣는다.</param>
    /// <param name="loop">되풀이 — 되풀이 소리(감사4 S3)만 쓴다. 끌 때는 <see cref="AudioMixer.EndLoop"/>.</param>
    private void Play(int sound, int tag = 0, float screenX = float.NaN, bool loop = false)
    {
        bool loaded = _sfx.TryGetValue(sound, out var pcm);
        var (left, right) = SndPan(screenX);
        float gain = SndGain;
        // 화면 밖에서 난 소리는 작게 들린다 — 원본(0x10028c18~0x10028cef)은 화면 밖이면 −(d² % 5000)/100 dB(평균 약 −15 dB, 거리에 단조가 아님)다.
        // 리메이크는 화면 너비가 달라 그 식을 그대로 못 쓰니 평균값으로 줄인다(근사 — ba-21 sound D6). 전에는 화면 밖 소리도 제 크기였다.
        if (!float.IsNaN(screenX) && (screenX < 0 || screenX > 640)) gain *= 0.18f;
        bool played = !Muted && loaded && _mixer.PlayEffect(pcm!, gain, tag, loop, left, right, slotted: true);
        // DUELDX_TRACE 면 무슨 소리를 틀었는지(파일이 있었는지·크기·좌우) 적는다 — 음소거한 화면 밖 시험에서도 재생 여부를 본다.
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"sound {sound} {(!loaded ? "파일 없음" : Muted || played ? "재생" : "칸 참(버림)")} gain {gain:0.000} L {left:0.00} R {right:0.00}"
                + $"{(float.IsNaN(screenX) ? "" : $" x {screenX:0}")}{(loop ? " loop" : "")} at {_lastTime:0.00}" + Environment.NewLine);
    }

    /// <summary>
    /// Snd 효과음 좌우 — 원본 <c>0x10028b60</c> 은 화면 안이면 <c>SetPan((x−320)×7)</c>(1/100 dB, 가장자리 ±22.4 dB)이고,
    /// DirectSound 팬은 <b>반대쪽만</b> 줄인다: 팬 &lt; 0 이면 오른쪽 × 10^(팬/2000), 팬 &gt; 0 이면 왼쪽 × 10^(−팬/2000)(감사4 S1).
    /// 화면 밖의 거리 감쇠(<c>0x10028c18~</c>)는 옮기지 않고 가장자리 팬으로 둔다.
    /// </summary>
    private static (float Left, float Right) SndPan(float screenX)
    {
        if (float.IsNaN(screenX)) return (1f, 1f);
        float pan = (Math.Clamp(screenX, 0, 640) - 320) * 7;
        float cut = (float)Math.Pow(10, -Math.Abs(pan) / 2000.0);
        return pan < 0 ? (1f, cut) : (cut, 1f);
    }

    /// <summary>
    /// 음성 좌우 — 원본 <c>0x100f5350</c> 은 화면 안이면 <c>BinkSetPan(clamp((x−192)×256, 0, 65536))</c>(32768 = 가운데):
    /// x ≤ 192 는 <b>완전 왼쪽</b>, x ≥ 448 은 <b>완전 오른쪽</b>, 그 사이는 곧게(<c>0x100f54cd~0x100f54ef</c>, 감사4 S2).
    /// Bink 팬 곡선은 모른다(가설) — 가운데는 두 쪽 다 제 크기, 한쪽으로 갈수록 반대쪽만 곧게 줄인다.
    /// </summary>
    private static (float Left, float Right) VoicePan(float screenX)
    {
        if (float.IsNaN(screenX)) return (1f, 1f);
        float p = Math.Clamp((screenX - 192) / 256f, 0f, 1f);
        return (Math.Min(1f, 2 * (1 - p)), Math.Min(1f, 2 * p));
    }

    /// <summary>
    /// 소리 자리 x 를 640 틀 화면 x 로 — 전투는 판 픽셀에서 카메라를 빼고 창 너비를 640 으로 줄인다(<c>(x−camX)×640/ViewWidth</c>),
    /// 필드·모세스는 이미 640 틀 안 x 다.
    /// </summary>
    private float SoundScreenX(float x)
    {
        if (float.IsNaN(x) || FieldOpen || _mosesOpen) return x;
        return (x - _camX) * 640f / Math.Max(1, ViewWidth);
    }

    /// <summary>배경음악(assets/bgm) 한 곡 — Bink 음악을 풀어 튼다. 푸는 데 1~2초 걸려 배경 실에서 한다.</summary>
    /// <summary>지금 걸린(또는 마지막으로 건) 곡 번호 — 타이틀이 같은 곡을 다시 안 걸고, 배경음악 체크 상자가 켤 때 되살린다.</summary>
    private int _musicId;

    /// <summary>음악을 멈춘다 — 곡 번호도 지워 타이틀이 「같은 곡이 돌고 있다」고 착각하지 않게.</summary>
    private void StopMusic()
    {
        _musicId = 0;
        _musicPaused = false;
        _musicAudible = null;
        _musicDuckRestoreAt = null;
        _chapterEventMusic = false;
        // 아직 풀리는 곡도 버린다 — 전에는 전투 곡(푸는 데 1~2초)을 건 직후 필드로 가면 늦게 풀린 전투 곡이 머리 곡 없는 필드에서 울렸다.
        Interlocked.Increment(ref _musicRequest);
        _mixer.StopMusic();
    }

    /// <summary>
    /// 필드 행동 512 — 새 곡을 <b>옛 곡의 지금 크기</b>로 튼다(<c>0x100eed8a</c>: 옛 음악 개체의 음량 <c>+0xc</c> 를 새 곡에 넣는다,
    /// <c>0x10025320</c>). 옛 곡이 없으면(필드 머리 BGM 이 없었으면) <b>0</b> 에서 시작한다.
    /// 514 는 개체를 되감아 멈출 뿐 지우지 않아(<c>0x10024fb0</c>) 그 크기를 물려받는다(감사4 M2 — <see cref="RewindMusic"/>).
    /// 자료 512 358곳 중 346곳이 바로 뒤 517 로 키우는 페이드인이다.
    /// </summary>
    private void PlayMusicInherit(int id)
    {
        float gain = _musicId != 0 ? _musicGain : 0;
        StopMusic();
        if (id > 0) PlayMusicFile(id, loop: true, gain);
    }

    /// <summary>
    /// 필드 행동 514(<c>0x100eee30</c> → <c>0x10024fb0</c>) — 곡을 처음으로 되감아 멈춘다. 개체와 크기(%)는 남아
    /// 뒤따르는 512 가 그 크기로 바로 튼다(감사4 M2). 전에는 <see cref="StopMusic"/> 이라 512 가 0 에서 시작해 20~80틱 페이드 인이 됐다.
    /// </summary>
    private void RewindMusic()
    {
        _musicFade = null;
        _musicPaused = true;
        _mixer.PauseMusic(rewind: true);
    }

    /// <summary>필드 행동 515(<c>0x100eee50</c> → <c>0x10025480</c>) — 멈춘 곡을 그 자리에서 이어 튼다(감사4 M5).</summary>
    private void ResumeMusic()
    {
        if (!_musicPaused) return;
        _musicPaused = false;
        _mixer.ResumeMusic();
    }

    /// <summary>곡이 멈춰 있나 — 517 이 0 에 닿았거나(<c>0x10025000</c>) 514 로 되감았다.</summary>
    private bool _musicPaused;

    /// <summary>
    /// 논리 크기(<see cref="_musicGain"/>)와 다르게 <b>들리는</b> 크기 — 필드 머리 곡은 논리 0 으로 걸지만 100 % 로 울린다(감사4 M3).
    /// 페이드·크기 바꾸기가 한 번 걸리면 지운다.
    /// </summary>
    private float? _musicAudible;

    /// <param name="gain">시작 크기(논리 %/100) — 없으면 제 크기(<see cref="MusicGain"/>). 필드 밖(전투·타이틀)의 새 곡은 늘 제 크기로 시작한다.</param>
    /// <param name="audible">들리는 크기가 논리 크기와 다를 때(필드 머리 곡, 감사4 M3).</param>
    private void PlayMusicFile(int id, bool loop, float? gain = null, float? audible = null)
    {
        _musicId = id;
        _musicPaused = false;
        _musicDuckRestoreAt = null;
        _chapterEventMusic = false;
        if (Muted || !_bgmOn) return;
        // 새 음악은 제 크기로 시작한다 — 앞 장면이 줄여 둔 크기를 물려받으면 안 들린다. 필드 512 만 물려받는다(PlayMusicInherit).
        _musicFade = null;
        _musicGain = gain ?? MusicGain;
        _musicAudible = audible;
        // 풀리는 동안 다른 곡을 걸었으면 이 곡은 버린다 — 늦게 풀린 곡이 새 곡을 덮어쓰지 않게.
        int request = Interlocked.Increment(ref _musicRequest);
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                string path = VoicePack.BgmPath(id);
                // 곡 파일이 없으면 조용하다(Btl 0014 의 머리 곡 5 — 원본은 무음) — 전에는 앞 곡이 계속 났다(ba-21 sound D10).
                if (!File.Exists(path)) { if (request == Volatile.Read(ref _musicRequest)) _mixer.StopMusic(); return; }
                var pcm = BinkAudio.Open(path).Decode();
                if (request != Volatile.Read(ref _musicRequest)) return;
                // 푸는 동안 517 페이드가 크기를 옮겼을 수 있다 — 지금 크기로 튼다(B.G.M 설정은 믹서가 곱한다).
                _mixer.PlayMusic(pcm, loop, _musicAudible ?? _musicGain);
                if (_musicPaused) _mixer.PauseMusic();          // 푸는 동안 517 이 0 에 닿았다 — 멈춘 채로 둔다
                if (Trace) _backgroundTrace.Enqueue($"music {id} 시작 gain {_musicAudible ?? _musicGain:0.00} × BGM {_bgmVolume}%{(_musicPaused ? " (멈춤)" : "")}");
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
    /// <param name="screenX">말하는 이의 640 틀 화면 x — 좌우를 가른다(<see cref="VoicePan"/>, 감사4 S2). NaN 이면 가운데.</param>
    private void PlayTalkVoice(int id, float screenX = float.NaN)
    {
        StopTalkVoice();
        if (id <= 0) return;
        int tag = _talkVoiceTag = ++_soundTag;
        var (left, right) = VoicePan(screenX);
        LoadClip(id, pcm =>
        {
            if (!Muted && pcm != null && tag == Volatile.Read(ref _talkVoiceTag)) _mixer.PlayEffect(pcm, _effectGain, tag, left: left, right: right);
            // 이 부름은 배경 실에서 온다 — 기록 파일에 바로 쓰면 주 실의 기록(필드 스크립트 줄)과 부딪쳐 IOException 으로 죽는다.
            // 대사가 줄을 안 막게 된 뒤로 대사 바로 다음 줄을 같은 틀에 적어 자주 부딪쳤다. 주 실(UpdateSounds)이 대신 적는다.
            if (Trace) _backgroundTrace.Enqueue($"talk voice {id}: {(pcm == null ? "파일 없음" : $"{ClipSeconds(pcm):0.0}초")} gain {_effectGain:0.00} L {left:0.00} R {right:0.00}");
        });
    }

    /// <summary>배경 실이 남긴 추적 줄 — 주 실이 <see cref="UpdateSounds"/> 에서 파일에 옮겨 적는다.</summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _backgroundTrace = new();

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
                string path = VoicePack.BgmPath(id);
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
    /// <param name="screenX">매단 인물의 640 틀 화면 x(<c>0x100f5300</c> 이 매단 물체 <c>+0x54</c>) — NaN 이면 가운데(감사4 S2).</param>
    private void PlayChannelSound(int channel, int id, bool loop, float screenX = float.NaN)
    {
        StopChannelSound(channel);
        if (id <= 0) return;
        int tag = ++_soundTag;
        var (left, right) = VoicePan(screenX);
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
            if (!Muted && pcm != null) _mixer.PlayEffect(pcm, _effectGain, tag, loop, left, right);
            if (Trace) _backgroundTrace.Enqueue($"channel {channel} sound {id}: {(pcm == null ? "파일 없음" : $"{ClipSeconds(pcm):0.0}초")} L {left:0.00} R {right:0.00}{(loop ? " loop" : "")}");
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

    /// <summary>
    /// 전투 머리 곡(word 8). 원본 <c>0x10061bf0</c> 은 전역 음악 포인터를 <b>먼저 0 으로</b> 두고, 곡 번호가 −1·0·1 이면 아무 곡도 안 건다
    /// → 앞 장면이 지운 뒤라 <b>무음</b>이다(감사4 B5). 전에는 <c>PlayMusicFile(0)</c> 이 앞 곡을 안 멈춰 필드 10·전투 잇기로 들어가면 앞 곡이 계속됐다.
    /// </summary>
    private void StartBattleMusic()
    {
        if (_scene.Bgm is <= 1 or 0xFFFF) { StopMusic(); return; }
        PlayMusicFile(_scene.Bgm, loop: true);
    }

    private void PlayOutcomeMusic(bool win)
    {
        StopMusic();
        PlayMusicFile(win ? 3392 : 55, loop: false);
    }

    // ── 동작에 박힌 소리 ─────────────────────────────────────────────────────

    /// <summary>동작을 시작할 때, 그 모션(과 자식 이펙트 모션)의 소리를 틱에 맞춰 예약한다.</summary>
    private void ScheduleActionSounds(UnitState u, int action)
    {
        if (!_sprites.TryGetValue(u.ChrCode, out var sprite) || sprite.Clip(action, u.Facing) is not { } clip) return;
        float x = UnitFoot(u).X;                          // 좌우 소리(감사4 S1) — 그 인물의 발 자리
        foreach (var (start, sound) in clip.Sounds) _pendingSounds.Add((_lastTime + start / TicksPerSecond, sound, x));
        foreach (var (start, obs, motion, _, _, _, _) in clip.Children)
        {
            if (_effectTables.GetValueOrDefault(obs)?.Clips.GetValueOrDefault(motion) is not { } child) continue;
            foreach (var (s, sound) in child.Sounds) _pendingSounds.Add((_lastTime + (start + s) / TicksPerSecond, sound, x));
        }
    }

    /// <summary>어빌리티 이펙트 소리를 때리는 순간 기준으로 예약한다.</summary>
    private void ScheduleAbilitySounds(WorkData work)
    {
        if (!_abilitySounds.TryGetValue(work.AbilityId, out var sounds)) return;
        foreach (var (tick, sound) in sounds) _pendingSounds.Add((_lastTime + tick / TicksPerSecond, sound));
    }

    /// <summary>
    /// 예전 걷는 소리 자리(군단 부하가 길을 받을 때 한 번) — 이제 <see cref="StepWalkSounds"/> 가 걷는 모든 인물에게
    /// 걷기 모션 한 바퀴마다 내므로 여기서는 아무것도 안 한다(겹치지 않게).
    /// </summary>
    private void PlayWalkSound(UnitState unit) { }

    /// <summary>인물마다 지난 틀의 모션 틱 — 걷기 소리 키를 지나쳤는지 본다.</summary>
    private readonly Dictionary<UnitState, int> _walkTicks = [];

    /// <summary>
    /// 걷는 소리 — 걷기 모션(동작 1)은 되풀이 모션이라 원본은 소리 키(B 목록 종류 1)를 <b>한 바퀴마다</b> 낸다
    /// (가이아리더 367 등 걷기 소리 있는 몸짓 Obs 20여 개, 감사4 S4). 전에는 군단 부하만 길 하나에 한 번 냈고 주 유닛은 걸어도 조용했다.
    /// 매 틀 걷는 인물의 모션 틱이 소리 키 틱(+한 바퀴 길이의 배수)을 지났으면 튼다.
    /// </summary>
    private void StepWalkSounds()
    {
        if (FieldOpen || _mosesOpen || _titleOpen || _episodesOpen || _recordsOpen || _units is not { Length: > 0 } units) return;
        if (_walkTicks.Count > units.Length * 2) _walkTicks.Clear();   // 다른 전투의 인물은 버린다
        foreach (var u in units)
        {
            if (!_sprites.TryGetValue(u.ChrCode, out var sprite)) continue;
            var (clip, tick) = sprite.CurrentClip(u);
            // 모션이 처음부터 다시 돌면(서 있다 걷기 시작) 틱이 줄어든다 — 그때는 0 틱 키도 지난 것으로 본다.
            int prev = _walkTicks.TryGetValue(u, out int p) && p <= tick ? p : -1;
            _walkTicks[u] = tick;
            if (!u.Alive || !u.OnField || !u.IsMoving || u.Action >= 0 || u.Motion >= 0 || clip is not { Sounds.Count: > 0 }) continue;
            int length = Math.Max(clip.Length, clip.Keys.Count > 0 ? clip.Keys[^1].Start + clip.Keys[^1].Length : 0);
            if (length <= 0 || tick == prev) continue;
            float x = UnitFoot(u).X;
            foreach (var (start, sound) in clip.Sounds)
            {
                // prev < start + k·length ≤ tick 인 k 가 있나 — 한 틀에 여러 바퀴를 건너도 한 번만 낸다.
                int k = (int)Math.Floor((prev - start) / (double)length) + 1;
                if (start + (long)k * length <= tick) Play(sound, 0, SoundScreenX(x));
            }
        }
    }

    /// <summary>
    /// 이펙트 수명 동안 되풀이하는 소리 — (시작, 끝, Snd 번호, 자리 x, 표). 표가 0 이면 아직 안 틀었다.
    /// 원본은 Obs 시작(A) 목록의 소리 키(종류 1)를 모션이 도는 동안 되풀이하고(<c>0x100d2aa0</c>), 모션이 바뀌면 되풀이를 끄고 끝까지 울린다(감사4 S3).
    /// </summary>
    private readonly List<(double Start, double End, int Sound, float X, int Tag)> _loopSounds = [];

    /// <summary>
    /// 되풀이 소리가 든 이펙트 모션 → 그 소리. 전 Obs 중 <b>217:0(소리 114)</b> 하나(썬더 스톰 436·1449~1457). ObsMotionTable 은 A 목록 소리를 버리므로 여기 적는다.
    /// </summary>
    private static readonly Dictionary<(int Obs, int Motion), int> EffectLoopSounds = new() { [(217, 0)] = 114 };

    /// <summary>그 이펙트 모션이 되풀이 소리를 가졌으면 이펙트 수명(<paramref name="lifeTicks"/>) 동안 되풀이하게 건다.</summary>
    private void QueueEffectLoopSound(int obs, int motion, double start, double lifeTicks, float x)
    {
        if (!EffectLoopSounds.TryGetValue((obs, motion), out int sound) || lifeTicks <= 0) return;
        _loopSounds.Add((start, start + lifeTicks / TicksPerSecond, sound, x, 0));
    }

    /// <summary>
    /// 이펙트 모션 한 바퀴의 길이(틱). <see cref="_effectTables"/>(assets/effects)에 없는 Obs(썬더 스톰의 217 은 moses/obs 에 있다)도
    /// 그림 캐시(<see cref="UiFor"/>)로 찾는다.
    /// </summary>
    private int EffectTicks(int obs, int motion) => Math.Max(0, UiFor(obs)?.MotionLength(motion) ?? 0);

    private void StepLoopSounds()
    {
        for (int i = _loopSounds.Count - 1; i >= 0; i--)
        {
            var l = _loopSounds[i];
            if (l.Tag == 0 && l.Start <= _lastTime)
            {
                l.Tag = ++_soundTag;
                Play(l.Sound, l.Tag, SoundScreenX(l.X), loop: true);
                _loopSounds[i] = l;
            }
            if (l.Tag != 0 && l.End <= _lastTime)
            {
                _mixer.EndLoop(l.Tag);                          // 되풀이만 끄고 이번 바퀴는 끝까지 울린다
                if (Trace) File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"sound {l.Sound} loop end at {_lastTime:0.00}" + Environment.NewLine);
                _loopSounds.RemoveAt(i);
            }
        }
    }

    /// <summary>필살기 음악 줄이기를 되돌릴 때 — 이때 <c>FadeMusic(100, 20)</c>.</summary>
    private double? _musicDuckRestoreAt;

    /// <summary>
    /// 필살기 핸들러의 음악 줄이기 — work 1467·1521~1524·1528·1588~1590 은 핸들러 시작에 <c>0x100c7710(40, 20)</c>,
    /// 끝 무렵 <c>(100, 20)</c> 을 부른다(<c>0x100929a4</c>/<c>0x10092ad9</c> 등 22곳, 감사4 B4). 필드 517 과 같은 선형 페이더(<c>0x100c7780</c>).
    /// </summary>
    /// <param name="seconds">줄여 둘 동안 — 이펙트가 다 돌 때까지(짧아도 40틱).</param>
    private void DuckMusicForWork(double seconds)
    {
        FadeMusic(40, 20);
        _musicDuckStart = _lastTime;
        _musicDuckRestoreAt = _lastTime + Math.Max(seconds, 40 / TicksPerSecond);
    }

    /// <summary>필살기 음악 줄이기를 건 때.</summary>
    private double _musicDuckStart;

    /// <summary>
    /// 줄이기를 되돌릴 때가 됐나 — 기술 차례(<see cref="_routine"/>)가 끝났을 때(원본은 핸들러 마지막 단계에서 되돌린다).
    /// 줄이는 페이드(20틱)가 다 돈 뒤부터 보고, 차례가 안 끝나도 가장 늦은 이펙트 끝 + 10초면 되돌린다(막힘 대비).
    /// </summary>
    private bool DuckRestoreDue(double restoreAt) =>
        _lastTime >= restoreAt + 10 || (_routine == null && _lastTime >= _musicDuckStart + 20 / TicksPerSecond);

    /// <summary>배경음악 크기가 옮겨 가는 중 — (시작 크기, 목표 크기, 걸리는 틱, 시작한 때). 필드 행동 517.</summary>
    private (float From, float To, int Ticks, double Start)? _musicFade;

    /// <summary>
    /// 배경음악 크기를 <paramref name="percent"/>(0~100)까지 <paramref name="ticks"/> 틱에 걸쳐 옮긴다.
    /// </summary>
    /// <remarks>
    /// 필드 행동 517 이 쓴다. 자료의 a0 은 299번이 0(끄기) · 179번이 80 · 127번이 100 이고,
    /// a1 은 20~80 틱이 대부분이라 <b>정말로 서서히 옮기는 연출</b>이다.
    /// 크기 0 은 <b>일시정지</b>(<c>0x10025000</c>)이고, 0 이 아닌 크기가 걸리면 멈춰 있던 곡을 그 자리에서 잇는다(<c>0x10025480</c>, 감사4 M5).
    /// 전에는 0 이면 곡을 끊어 버렸다. 크기는 논리 %/100 이고 B.G.M 설정은 믹서가 곱한다(V1).
    /// </remarks>
    private void FadeMusic(int percent, int ticks)
    {
        float to = Math.Clamp(percent, 0, 100) / 100f * MusicGain;
        // 필드 머리 곡(논리 0, 들리기는 100 %)은 첫 517 이 0 에서 올린다 — 첫 틀에 소리가 뚝 떨어졌다 커진다(감사4 M3).
        _musicAudible = null;
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"music fade {_musicGain * 100:0}% → {to * 100:0}% / {ticks}틱{(_musicPaused ? " (멈춤에서)" : "")} × BGM {_bgmVolume}% at {_lastTime:0.00}" + Environment.NewLine);
        if (ticks <= 0)
        {
            _musicFade = null;
            SetMusicLevel(to);                           // 다음 512 가 이 크기를 물려받는다
            return;
        }
        _musicFade = (_musicGain, to, ticks, _lastTime);
        SetMusicLevel(_musicGain);
    }

    /// <summary>지금 배경음악 크기(논리 %/100) — 옮기는 중에도 어디까지 왔는지 알아야 해서 따로 들고 있다. 512 가 물려받는다.</summary>
    private float _musicGain = MusicGain;

    /// <summary>논리 크기를 믹서에 넣는다 — 0 이면 멈추고(위치 유지), 0 이 아니면 멈춰 있던 곡을 잇는다.</summary>
    private void SetMusicLevel(float gain)
    {
        _musicGain = gain;
        if (gain <= 0)
        {
            if (!_musicPaused && Trace) File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"music pause at {_lastTime:0.00}" + Environment.NewLine);
            _musicPaused = true;
            _mixer.PauseMusic();
            return;
        }
        if (_musicPaused && Trace) File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"music resume at {_lastTime:0.00}" + Environment.NewLine);
        _mixer.SetMusicGain(gain);
        ResumeMusic();
    }

    /// <summary>챕터 사건의 512 가 건 곡 — 챕터 창이 틀마다 80 % 미만이면 +1 한다(<c>0x100f80c9~0x100f80ea</c>, 감사4 M1).</summary>
    private bool _chapterEventMusic;

    /// <summary>챕터 곡 올리기를 마지막으로 셈한 때.</summary>
    private double _chapterRiseAt;

    /// <summary>필드 실행기가 <b>챕터 사건</b>에서 512 를 돌았다 — 그 곡은 80 % 까지 틱마다 1 % 오른다.</summary>
    private void MarkChapterEventMusic()
    {
        _chapterEventMusic = true;
        _chapterRiseAt = _lastTime;
    }

    private void StepMusicFade()
    {
        if (_musicFade is not { } fade)
        {
            // 챕터 창(0x100f8040)은 사건 실행기의 곡을 넘겨받아 80 % 미만이면 틀마다 +1 %.
            if (!_chapterEventMusic || !_mosesOpen || _musicId == 0) return;
            int ticks = (int)((_lastTime - _chapterRiseAt) * TicksPerSecond);
            if (ticks <= 0) return;
            _chapterRiseAt += ticks / TicksPerSecond;
            if (_musicGain < 0.8f) SetMusicLevel(Math.Min(0.8f, _musicGain + ticks / 100f));
            return;
        }
        double step = Math.Clamp((_lastTime - fade.Start) * TicksPerSecond / fade.Ticks, 0, 1);
        SetMusicLevel((float)(fade.From + (fade.To - fade.From) * step));
        if (step < 1) return;
        _musicFade = null;
    }

    private void UpdateSounds()
    {
        while (_backgroundTrace.TryDequeue(out string? line))
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), line + Environment.NewLine);
        StepMusicFade();
        if (_musicDuckRestoreAt is { } restore && DuckRestoreDue(restore))
        {
            _musicDuckRestoreAt = null;
            FadeMusic(100, 20);                                 // 필살기 끝 무렵 되돌리기(0x100c7710(100, 20))
        }
        for (int i = _pendingSounds.Count - 1; i >= 0; i--)
            if (_pendingSounds[i].Time <= _lastTime)
            {
                var (_, sound, x) = _pendingSounds[i];
                _pendingSounds.RemoveAt(i);
                Play(sound, 0, SoundScreenX(x));
            }
        StepLoopSounds();
        StepWalkSounds();
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

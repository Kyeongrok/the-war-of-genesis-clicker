using System.IO;
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

        StartBattleMusic();
    }

    /// <summary>DUELDX_MUTE=1 이면 소리를 내지 않는다 — 자동 테스트가 사용자 스피커로 소리를 내지 않게.</summary>
    private static readonly bool Muted = Environment.GetEnvironmentVariable("DUELDX_MUTE") == "1";

    private void Play(int sound, int tag = 0)
    {
        if (!Muted && _sfx.TryGetValue(sound, out var pcm)) _mixer.PlayEffect(pcm, _effectGain, tag);
    }

    /// <summary>배경음악(assets/bgm) 한 곡 — Bink 음악을 풀어 튼다. 푸는 데 1~2초 걸려 배경 실에서 한다.</summary>
    private void PlayMusicFile(int id, bool loop)
    {
        if (Muted) return;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                string path = Path.Combine(AssetsFolder.Find("bgm"), $"{id:D4}.bgm");
                if (File.Exists(path)) _mixer.PlayMusic(BinkAudio.Open(path).Decode(), loop, MusicGain);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or DirectoryNotFoundException) { }
        });
    }

    private void StartBattleMusic() => PlayMusicFile(BattleDemoScene.Bgm, loop: true);

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
        foreach (var (start, obs, motion) in clip.Children)
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

    private void UpdateSounds()
    {
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

using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>장면 사이 페이드와 스냅숏 — 타이틀·연대표·기록·모세스·전투가 같이 쓴다.</summary>
internal sealed unsafe partial class GameWindow
{
    // ── 전투 시작·끝 페이드 (감사4 C6·C7, 사운드 B3) ─────────────────────────────────────────

    /// <summary>판을 새 전투 맵으로 잡았다 — 다음 전투 틀에 시작 카메라와 페이드인을 건다.</summary>
    internal bool _battleIntroPending;

    /// <summary>페이드인·아웃이 시작된 때(게임 초). −1 이면 안 하는 중.</summary>
    internal double _fadeInStart = -1, _fadeOutStart = -1;

    /// <summary>페이드인이 끝나면 전투 음악을 걸어야 하나(페이드 앞에 멈춰 둔 것).</summary>
    internal bool _fadeInMusicHeld;

    internal const int SceneFadeTicks = 16;

    internal bool SceneFading => _fadeInStart >= 0 || _fadeOutStart >= 0;

    /// <summary>페이드아웃이 끝나면 할 일 — 없으면 전투 결과대로 다음 장면(<see cref="LeaveFinishedBattleNow"/>).</summary>
    internal Action? _afterFadeOut;

    /// <summary>나가는 동안 음악을 안 건드리나 — 타이틀 ↔ 기록 화면은 곡이 이어진다(0x101056b9 · 0x101050e1).</summary>
    internal bool _fadeOutKeepMusic;

    /// <summary>
    /// 장면을 떠난다 — 16틀 동안 화면을 검게, 음악을 100 → 10% 로 줄인 뒤 <paramref name="next"/> 를 한다. 타이틀(0x10105420)·연대표(0x101060d0)·
    /// 기록(0x10104e60) 화면이 모두 이렇게 나간다(ba-21 outer #1). 전에는 뚝 바뀌었다. 이미 나가는 중이면 아무것도 안 한다.
    /// </summary>
    internal void LeaveScene(Action next, bool keepMusic = false)
    {
        if (_fadeOutStart >= 0) return;
        _afterFadeOut = next;
        _fadeOutKeepMusic = keepMusic;
        _fadeInStart = -1;
        _fadeOutStart = _lastTime;
    }

    /// <summary>새 장면이 섰다 — 검정에서 밝아진다(타이틀·연대표·기록은 15틀, 세기 31 − 2i).</summary>
    internal void EnterSceneFade()
    {
        _fadeOutStart = -1;
        _afterFadeOut = null;
        _fadeInMusicHeld = false;
        _fadeInStart = _lastTime;
    }

    /// <summary>
    /// 전투 틀마다 — 새 판이면 시작 카메라를 놓고 16틀 검정→화면 페이드인을 건다. 원본 0x10061ad0 은 페이드가 <b>끝난 뒤</b>
    /// BGM(워드 8)을 100% 로 건다 — 판을 세울 때 이미 건 음악은 멈춰 두었다가 페이드 끝에 다시 건다.
    /// 페이드아웃이 끝나면 음악을 끄고 다음 장면으로.
    /// </summary>
    internal void StepSceneFade()
    {
        if (_battleIntroPending)
        {
            _battleIntroPending = false;
            Btl.PlaceStartCamera();
            _fadeInStart = _lastTime;
            // 켜자마자 읽는 첫 판은 음악을 LoadAudio 가 다 읽은 뒤 건다 — 그때는 건드리지 않는다.
            _fadeInMusicHeld = !_loading;
            if (_fadeInMusicHeld)
            {
                Interlocked.Increment(ref _musicRequest);   // 풀고 있는 곡이 뒤늦게 울리지 않게
                StopMusic();
            }
        }
        StepSceneFadeClock();
    }

    /// <summary>페이드 시계 — 전투가 아닌 화면(타이틀·연대표·기록·모세스)에서도 돈다.</summary>
    internal void StepSceneFadeClock()
    {
        if (_fadeInStart >= 0 && (_lastTime - _fadeInStart) * TicksPerSecond >= SceneFadeTicks)
        {
            _fadeInStart = -1;
            if (_fadeInMusicHeld) StartBattleMusic();
            _fadeInMusicHeld = false;
        }
        if (_fadeOutStart >= 0)
        {
            int t = (int)((_lastTime - _fadeOutStart) * TicksPerSecond);
            if (t < SceneFadeTicks)
            {
                // 틀마다 음악 100, 94, … , 10 %(0x10061e83~0x10061e9d).
                if (!_fadeOutKeepMusic) _mixer.SetMusicGain(_musicGain * Math.Max(10, 100 - 6 * t) / 100f);
                return;
            }
            _fadeOutStart = -1;
            if (!_fadeOutKeepMusic)
            {
                // 장면 소멸자가 음악 개체를 지운다(0x10062020) — 끄고 크기는 되돌린다(다음 곡은 제 크기로 튼다).
                Interlocked.Increment(ref _musicRequest);
                StopMusic();
                _mixer.SetMusicGain(_musicGain);
            }
            _fadeOutKeepMusic = false;
            var next = _afterFadeOut ?? Btl.LeaveFinishedBattleNow;
            _afterFadeOut = null;
            next();
        }
    }

    /// <summary>페이드 중 판 밝기(0~31) — 페이드인 2·틱, 페이드아웃 31 − 2·틱. 페이드가 없으면 31.</summary>
    internal int SceneFadeLevel()
    {
        if (_fadeInStart >= 0) return Math.Clamp(2 * (int)((_lastTime - _fadeInStart) * TicksPerSecond), 0, 31);
        if (_fadeOutStart >= 0) return Math.Clamp(31 - 2 * (int)((_lastTime - _fadeOutStart) * TicksPerSecond), 0, 31);
        return 31;
    }

    /// <summary>보이는 판을 페이드 밝기로 어둡게 — c·k/31(방식 2).</summary>
    internal void DrawSceneFade()
    {
        // 필드는 제 화면 전환(900)이 있다. 타이틀·연대표·기록·모세스 위에도 덮는다(Compose 맨 끝).
        if (!SceneFading || (FieldOpen && _afterFadeOut == null)) return;
        int k = SceneFadeLevel();
        if (k >= 31) return;
        for (int y = _camY; y < _camY + ViewHeight && y < BoardHeight; y++)
            for (int x = _camX; x < _camX + ViewWidth && x < BoardWidth; x++)
            {
                int i = y * BoardWidth + x;
                uint c = _fb[i];
                uint Ch(int shift) => (uint)((int)(c >> shift & 0xFF) * k / 31);
                _fb[i] = 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
            }
    }

    /// <summary>지금 보이는 영역을 <c>%TEMP%\dueldx_snapshot.png</c> 로 저장한다 — 창을 화면에 띄우지 않고 확인하는 테스트용.</summary>
    internal void SaveSnapshot()
    {
        using var bmp = new Bitmap(ViewWidth, ViewHeight, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, ViewWidth, ViewHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < ViewHeight; y++)
                _fb.AsSpan((_camY + y) * BoardWidth + _camX, ViewWidth).CopyTo(new Span<uint>((void*)(data.Scan0 + y * data.Stride), ViewWidth));
        }
        finally { bmp.UnlockBits(data); }
        bmp.Save(Path.Combine(Path.GetTempPath(), "dueldx_snapshot.png"), ImageFormat.Png);
    }

    internal readonly Random _rng = new();

    /// <summary>전투를 넘어 이어지는 파티 상태 — Chr 번호 → 그 인물의 레벨·경험치·장비·어빌리티.</summary>
    internal readonly Dictionary<int, CharacterData> _party = [];

    /// <summary>보이는 영역의 왼쪽 위(판 픽셀) — 판이 창보다 클 때만 0 이 아니다. 전투 카메라가 움직이고 모든 장면의 그리기가 읽는다.</summary>
    internal int _camX, _camY;
}

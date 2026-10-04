using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DuelDx;

/// <summary>
/// 카메라 — 창은 판의 일부(<see cref="ViewHeight"/>·<see cref="ViewWidth"/>)만 보여 주고, 원본처럼 <b>카메라 명령</b>으로
/// 대상을 화면 한가운데에 둔다(감사4 C1). 마우스 휠로 직접 올리고 내릴 수 있다. 머리 줄·알림·창들은 보이는 영역 기준으로 그린다.
/// </summary>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>보이는 영역의 맨 윗줄(판 픽셀).</summary>
    private int _camY;
    private double _camPos, _camTarget;

    /// <summary>보이는 영역의 맨 왼쪽(판 픽셀) — 640 보다 넓은 맵에서만 0 이 아니다.</summary>
    private int _camX;
    private double _camPosX, _camTargetX;

    /// <summary>카메라가 오른쪽으로 갈 수 있는 끝 — 맵 그림 너비까지.</summary>
    private int CamMaxX => Math.Max(0, Math.Min(BoardWidth, BoardIsMap ? Math.Max(ViewWidth, _map!.Width) : BoardWidth) - ViewWidth);
    private void ScrollCamera(double delta) => _camTarget = Math.Clamp(_camTarget + delta, 0, CamMax);

    private void ScrollCameraX(double delta) => _camTargetX = Math.Clamp(_camTargetX + delta, 0, CamMaxX);

    // ── 카메라 명령 (원본 CBattle +0x3ccc 큐의 축소판, 감사4 C1·C19) ─────────────────────────────
    // 원본은 「이 월드 점을 화면 한가운데에」 명령을 큐 머리에 덮어쓰고(0x1006e570), 틀마다 0x1006dcb0 이
    // 속도 0 이면 max(4, ⌊거리/5⌋) px 씩 곧은 선으로 다가간다. 거리 < 속도면 도착 — 점 명령(종류 0)은 빠지고,
    // 따라가기(종류 1, 0x1006e730)는 주인 자리를 틀마다 다시 읽으며 저절로 안 끝난다. 여백 규칙은 없다.
    // 상태·이벤트는 큐가 빌 때까지(0x1006e850) 다음 단계로 안 간다 — 그래서 행동·대사·창은 카메라가 선 뒤에 나온다.

    /// <summary>지금 카메라 명령 — 가운데에 둘 판 픽셀(X, Y), 속도(0 = 자동), 따라갈 유닛 번호(−1 = 점 명령).</summary>
    private (double X, double Y, int Speed, int Follow)? _camGoal;

    /// <summary>카메라가 점 명령을 수행 중인가 — 기다리는 조건(0x1006e850). 따라가기는 저절로 안 끝나므로 기다림에서 뺀다.</summary>
    private bool CameraBusy => _camGoal is { Follow: < 0 };

    /// <summary>틱 단위로 움직이려고 모아 두는 틱 조각.</summary>
    private double _camTickAcc;

    /// <summary>판 픽셀 (x, y) 를 화면 가운데로(0x1006e570 — 머리 칸을 덮어쓴다, 쌓지 않음).</summary>
    private void CenterOn(double x, double y, int speed = 0)
    {
        _camGoal = (x, y, speed, -1);
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"{_lastTime:F2} camera centre ({x:F0},{y:F0}) from ({_camPosX:F0},{_camPos:F0})" + Environment.NewLine);
    }

    /// <summary>칸 가운데를 화면 가운데로 — 칸은 판 안으로 자른다.</summary>
    private void CenterOnCell(int col, int row)
    {
        col = Math.Clamp(col, 0, Cols - 1);
        row = Math.Clamp(row, 0, Rows - 1);
        CenterOn(col * TileW + TileW / 2, CellCenterY(col, row));
    }

    /// <summary>유닛 발 자리를 한 번 가운데로(0x100eabe0 — 종류 0, 따라가기 아님).</summary>
    private void CenterOnUnit(UnitState u)
    {
        var (fx, fy) = UnitFoot(u);
        CenterOn(fx, fy);
    }

    /// <summary>걷는 유닛 따라가기(0x100eac40(8) — 틱당 최대 8px). 도착하면 <see cref="UpdateCamera"/> 가 풀어 준다(0x100eac60).</summary>
    private void FollowUnit(int index, int speed = 8)
    {
        if ((uint)index < _units.Length) _camGoal = (0, 0, speed, index);
    }

    /// <summary>
    /// 점 명령을 걸고 멈출 때까지 기다리는 루틴 조각 — 원본 상태 하위 0 의 「가운데 → 0x1006e850 == 0 까지 머묾」.
    /// 막히는 일이 없게 3초에서 끊는다.
    /// </summary>
    private IEnumerable<bool> CenterAndWait(double x, double y)
    {
        CenterOn(x, y);
        for (double end = _lastTime + 3; CameraBusy && _lastTime < end;) yield return true;
    }

    private IEnumerable<bool> CenterUnitAndWait(UnitState u)
    {
        var (fx, fy) = UnitFoot(u);
        return CenterAndWait(fx, fy);
    }

    /// <summary>AI 차례(상태 8 → 14 CHRWORK 0x10069df0) — 그 인물을 가운데로 보내고 멈춘 뒤에 명령을 실행한다(감사4 C4).</summary>
    private IEnumerator<bool> CameraThen(UnitState u, IEnumerator<bool> inner)
    {
        foreach (bool b in CenterUnitAndWait(u)) yield return b;
        while (inner.MoveNext()) yield return inner.Current;
    }

    /// <summary>링(상태 9)·대상 고르기(10·11·12)에 들어설 때 행동 인물을 가운데로 — 지난 틀에 본 값.</summary>
    private int _camSeenRing = -1;
    private bool _camSeenAim;

    /// <summary>
    /// 플레이어가 움직일 수 있는 상태인가 — 원본 가장자리 스크롤(0x1006d910)은 상태 2(배치)·3·7·10·11·12·22 에서만 부른다(감사4 C10).
    /// AI 차례·행동·이벤트·결과 중에는 안 민다.
    /// </summary>
    private bool EdgeScrollAllowed =>
        !Mos._mosesOpen && !FieldOpen && !_titleOpen && !_episodesOpen && _ringUnit < 0 && !_abilityMenu && _statusUnit < 0 && !SystemOpen
        && !SceneFading && !EventsBusy && _outcome.Length == 0 && !LevelUpOpen
        && (_deployOpen || (IsPlayerTurn && !_units[_turn].IsBusy));

    private void UpdateCamera(double dt)
    {
        // 링이 열릴 때(상태 9 0x1006904c)·대상 고르기를 시작할 때(10·11·12) 행동 인물을 가운데로. 차례 시작(상태 22)은 안 옮긴다(감사4 C3).
        if (_ringUnit != _camSeenRing)
        {
            _camSeenRing = _ringUnit;
            if ((uint)_ringUnit < _units.Length) CenterOnUnit(_units[_ringUnit]);
        }
        bool aiming = _targetWork >= 0 || _abilityMenu;
        if (aiming && !_camSeenAim && (uint)_turn < _units.Length) CenterOnUnit(_units[_turn]);
        _camSeenAim = aiming;

        // 틱마다(초당 30) 한 번씩 — 원본 이동은 틀 단위다.
        _camTickAcc += dt * TicksPerSecond;
        int ticks = Math.Min(6, (int)_camTickAcc);
        _camTickAcc -= (int)_camTickAcc;
        for (int i = 0; i < ticks; i++)
        {
            StepCameraGoal();
            StepEdgeScroll();
        }

        // 휠 스크롤(리메이크 편의)만 부드럽게 따라간다 — 명령·가장자리 스크롤은 _camPos 를 바로 옮기고 목표도 같이 맞춘다.
        _camPos += (_camTarget - _camPos) * Math.Min(1, dt * 8);
        if (Math.Abs(_camTarget - _camPos) < 0.5) _camPos = _camTarget;
        _camY = Math.Clamp((int)Math.Round(_camPos), 0, CamMax);
        _camPosX += (_camTargetX - _camPosX) * Math.Min(1, dt * 8);
        if (Math.Abs(_camTargetX - _camPosX) < 0.5) _camPosX = _camTargetX;
        _camX = Math.Clamp((int)Math.Round(_camPosX), 0, CamMaxX);
        // 기술의 화면 흔들림 — 원본 흔들림 개체(0x100c7040)는 지금 스크롤 기준 +세기 → −세기 로 번갈아 카메라 명령을 걸어
        // 스크롤이 원자리 ↔ 원자리+세기 를 오간다(한쪽, 폭 = 세기, 감사4 C18). ShakeOffset 의 ±세기 중 음수 쪽을 0 으로 접는다.
        var (sx, sy) = ShakeOffset();
        sx = Math.Max(0, sx);
        sy = Math.Max(0, sy);
        if (sx != 0 || sy != 0)
        {
            _camX = Math.Clamp(_camX + sx, 0, CamMaxX);
            _camY = Math.Clamp(_camY + sy, 0, CamMax);
        }
    }

    /// <summary>카메라 명령 한 틱(0x1006dcb0).</summary>
    private void StepCameraGoal()
    {
        if (_camGoal is not { } g) return;
        double gx = g.X, gy = g.Y;
        if (g.Follow >= 0)
        {
            if (g.Follow >= _units.Length) { _camGoal = null; return; }
            var u = _units[g.Follow];
            // 걷기가 끝나면 따라가기를 뺀다(0x10077085 → 0x100eac60) — 카메라는 그 자리에 선다.
            if (!u.IsMoving && u.Path.Count == 0) { _camGoal = null; return; }
            (int fx, int fy) = UnitFoot(u);
            (gx, gy) = (fx, fy);
        }
        double tx = Math.Clamp(gx - ViewWidth / 2.0, 0, CamMaxX), ty = Math.Clamp(gy - ViewHeight / 2.0, 0, CamMax);
        double dx = tx - _camPosX, dy = ty - _camPos, dist = Math.Sqrt(dx * dx + dy * dy);
        int step = g.Speed > 0 ? g.Speed : Math.Max(4, (int)(dist / 5));
        if (dist < step)
        {
            (_camPosX, _camPos) = (tx, ty);
            if (g.Follow < 0) _camGoal = null;          // 점 명령은 도착하면 빠진다(0x1006e7b0)
        }
        else
        {
            _camPosX += dx / dist * step;                // 곧은 선(0x1001e3a0)
            _camPos += dy / dist * step;
        }
        (_camTargetX, _camTarget) = (_camPosX, _camPos);
    }

    /// <summary>
    /// 가장자리 스크롤 한 틱 — 원본 0x1006d910 은 틱마다 가로 20·세로 16px 을 <b>바로</b> 더한다(초당 600/480px, 감사4 C11).
    /// 원본은 끝 픽셀에서만 밀지만 창 모드라 24px 띠는 그대로 둔다(의도). 방향키는 걷기 그대로(사용자 결정).
    /// </summary>
    private void StepEdgeScroll()
    {
        if (!EdgeScrollAllowed) return;
        const int edge = 24;
        // 마우스는 화면(보이는 영역) 자리로 본다 — 판 자리로 보면 카메라가 한 번 밀린 뒤 마우스가 띠 밖으로 나간 셈이 되어 멈췄다.
        var (mx, my) = _mouseView;
        if (_mouse.X < 0 || mx < 0 || mx >= ViewWidth || my < 0 || my >= ViewHeight) return;
        double ddx = mx < edge ? -20 : mx >= ViewWidth - edge ? 20 : 0;
        double ddy = my < GridTop + edge && my >= GridTop ? -16 : my >= ViewHeight - edge ? 16 : 0;
        if (ddx == 0 && ddy == 0) return;
        _camPosX = _camTargetX = Math.Clamp(_camPosX + ddx, 0, CamMaxX);
        _camPos = _camTarget = Math.Clamp(_camPos + ddy, 0, CamMax);
        _mouse = (mx + (int)Math.Round(_camPosX), my + (int)Math.Round(_camPos));   // 마우스 밑의 판 자리도 따라 옮긴다
    }

    /// <summary>마우스의 보이는 영역 안 자리(판 픽셀, 카메라 뺀 것) — WM_MOUSEMOVE 가 적는다.</summary>
    private (int X, int Y) _mouseView = (-1, -1);

    /// <summary>
    /// 새 판의 카메라를 지운다 — 앞 전투의 세로 자리·명령이 남지 않게(감사4 C5: 예전엔 가로만 지웠다).
    /// </summary>
    private void ResetCamera()
    {
        _camGoal = null;
        _camTickAcc = 0;
        _camSeenRing = -1;
        _camSeenAim = false;
        _camX = _camY = 0;
        _camPos = _camTarget = _camPosX = _camTargetX = 0;
    }

    /// <summary>
    /// 시작 카메라 — Btl 머리 워드 2·3 칸을 가운데에 두고 맵 그림 안으로 자른다, 보간 없이 바로(0x10063e44~0x10063f5x, 감사4 C5).
    /// </summary>
    private void PlaceStartCamera()
    {
        ResetCamera();
        if (_scene.StartCol < 0 || _scene.StartRow < 0 || !BoardIsMap) return;
        int col = Math.Clamp(_scene.StartCol, 0, Cols - 1), row = Math.Clamp(_scene.StartRow, 0, Rows - 1);
        _camPosX = _camTargetX = Math.Clamp(col * TileW + TileW / 2 - ViewWidth / 2, 0, CamMaxX);
        _camPos = _camTarget = Math.Clamp(CellCenterY(col, row) - ViewHeight / 2, 0, CamMax);
        _camX = (int)_camPosX;
        _camY = (int)_camPos;
    }

    // ── 전투 시작·끝 페이드 (감사4 C6·C7, 사운드 B3) ─────────────────────────────────────────

    /// <summary>판을 새 전투 맵으로 잡았다 — 다음 전투 틀에 시작 카메라와 페이드인을 건다.</summary>
    private bool _battleIntroPending;

    /// <summary>페이드인·아웃이 시작된 때(게임 초). −1 이면 안 하는 중.</summary>
    private double _fadeInStart = -1, _fadeOutStart = -1;

    /// <summary>페이드인이 끝나면 전투 음악을 걸어야 하나(페이드 앞에 멈춰 둔 것).</summary>
    private bool _fadeInMusicHeld;

    private const int SceneFadeTicks = 16;

    private bool SceneFading => _fadeInStart >= 0 || _fadeOutStart >= 0;

    /// <summary>페이드아웃이 끝나면 할 일 — 없으면 전투 결과대로 다음 장면(<see cref="LeaveFinishedBattleNow"/>).</summary>
    private Action? _afterFadeOut;

    /// <summary>나가는 동안 음악을 안 건드리나 — 타이틀 ↔ 기록 화면은 곡이 이어진다(0x101056b9 · 0x101050e1).</summary>
    private bool _fadeOutKeepMusic;

    /// <summary>
    /// 장면을 떠난다 — 16틀 동안 화면을 검게, 음악을 100 → 10% 로 줄인 뒤 <paramref name="next"/> 를 한다. 타이틀(0x10105420)·연대표(0x101060d0)·
    /// 기록(0x10104e60) 화면이 모두 이렇게 나간다(ba-21 outer #1). 전에는 뚝 바뀌었다. 이미 나가는 중이면 아무것도 안 한다.
    /// </summary>
    private void LeaveScene(Action next, bool keepMusic = false)
    {
        if (_fadeOutStart >= 0) return;
        _afterFadeOut = next;
        _fadeOutKeepMusic = keepMusic;
        _fadeInStart = -1;
        _fadeOutStart = _lastTime;
    }

    /// <summary>새 장면이 섰다 — 검정에서 밝아진다(타이틀·연대표·기록은 15틀, 세기 31 − 2i).</summary>
    private void EnterSceneFade()
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
    private void StepSceneFade()
    {
        if (_battleIntroPending)
        {
            _battleIntroPending = false;
            PlaceStartCamera();
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
    private void StepSceneFadeClock()
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
            var next = _afterFadeOut ?? LeaveFinishedBattleNow;
            _afterFadeOut = null;
            next();
        }
    }

    /// <summary>페이드 중 판 밝기(0~31) — 페이드인 2·틱, 페이드아웃 31 − 2·틱. 페이드가 없으면 31.</summary>
    private int SceneFadeLevel()
    {
        if (_fadeInStart >= 0) return Math.Clamp(2 * (int)((_lastTime - _fadeInStart) * TicksPerSecond), 0, 31);
        if (_fadeOutStart >= 0) return Math.Clamp(31 - 2 * (int)((_lastTime - _fadeOutStart) * TicksPerSecond), 0, 31);
        return 31;
    }

    /// <summary>보이는 판을 페이드 밝기로 어둡게 — c·k/31(방식 2).</summary>
    private void DrawSceneFade()
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
    private void SaveSnapshot()
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
}

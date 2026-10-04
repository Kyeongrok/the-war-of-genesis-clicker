using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 레벨업(fg-9) — 쓰러뜨리면 경험치가 들어오고, 그 행동이 끝난 뒤 그 자리에서 레벨업 창이 뜬다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-캐릭터 "레벨업 알림 창": 경험치는 <b>처치할 때만</b> `Num[14] + Num[15]×(적 레벨 − 내 레벨)`(10~100),
/// 레벨 = 쌓인 경험치 ÷ 100, 오름은 직업 성장률 × 기본값(난수 없음), HP·TP 회복은 없다.
/// 창은 화면 한가운데에 제목 TXR 1090 「Level Up」과 본문 TXR 1091 + 오른 능력치 줄, 효과음 Snd 107,
/// 배경음악 40% 로 줄었다가 창이 닫히면 되돌아온다. 180틱(6초) 뒤 저절로 닫히고 아무 키·클릭으로도 닫힌다.
/// 여러 명이면 한 명씩 잇따라 뜬다. 창을 띄우기 전에 카메라를 그 유닛 가운데로 보내고 멈출 때까지 기다린다(0x1006823e).
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    /// <summary>레벨업 창이 저절로 닫히기까지 — 원본은 180틱이지만 사용자 요청으로 2초.</summary>
    internal const double LevelUpSeconds = 2.0;
    internal const float DuckedMusicGain = 0.4f;

    internal readonly Queue<int> _levelUpQueue = new();
    internal int _levelUpUnit = -1;
    internal double _levelUpUntil;
    internal string _levelUpTitle = "", _levelUpBody = "";

    internal bool LevelUpOpen => _levelUpUnit >= 0;

    /// <summary>줄 머리 유닛에게 카메라 명령을 걸었나 — 멈추면 창을 띄운다.</summary>
    internal bool _levelUpCamSent;

    /// <summary>쓰러뜨린 쪽에 경험치를 준다(메시지 1016). 여럿이 나누면 <paramref name="share"/> 로 나눈 몫(1 이상).</summary>
    internal void GainKillExp(UnitState killer, UnitState victim, int share = 1)
    {
        // 경험치는 <b>사람이 명령하는 유닛</b>만 받는다(0x100742e0 — 편 3 동맹 AI 는 못 받는다).
        if (host._db == null || killer.Data is not { } k || victim.Data is not { } v || !killer.PlayerControlled) return;
        // 11(경험치 증가)은 자르기 <b>앞</b>에 더한다(0x10072285~) — 그래서 한 번에 상한(Num 17)을 넘지 않는다.
        int exp = Math.Max(1, host._db.ExpForKill(k, v.Level, killer.Status(11)) / share);
        killer.Data = k with { Exp = k.Exp + exp, CumExp = k.CumExp + exp };
        Popup(killer, $"EXP +{exp}", 0xFF90D0FF, 15);
    }

    /// <summary>
    /// 도구 > 적 정리(시험용) — 판에 선 적을 모두 쓰러뜨리고, 적마다의 처치 경험치를 <b>내가 움직이는 동료</b>(AI 가 움직이는 동맹 NPC 는 뺌)끼리
    /// 똑같이 나눈다. 죽음은 조건대로 사건을 부르고(바루스가 죽어야 끝나는 전투 따위) 승패 판정도 그대로 돈다.
    /// </summary>
    internal void ClearEnemiesForTest()
    {
        if (host._db == null || !host._battleLoaded || host.Mos._mosesOpen || host.FieldOpen || _outcome.Length > 0) { host.Toast("전투 중에만 쓸 수 있습니다"); return; }
        var receivers = host._units.Where(u => u.Alive && u.OnField && IsMine(u) && u.Data != null).ToList();
        var victims = host._units.Where(u => u.Alive && u.OnField && !u.IsAlly).ToList();
        if (victims.Count == 0) { host.Toast("쓰러뜨릴 적이 없습니다"); return; }
        var gained = new Dictionary<UnitState, int>();
        foreach (var v in victims)
        {
            if (v.Data is { } vd)
                foreach (var r in receivers)
                {
                    int exp = Math.Max(1, host._db.ExpForKill(r.Data!, vd.Level) / receivers.Count);
                    r.Data = r.Data! with { Exp = r.Data!.Exp + exp, CumExp = r.Data!.CumExp + exp };
                    gained[r] = gained.GetValueOrDefault(r) + exp;
                }
            v.Alive = false;
        }
        foreach (var (r, exp) in gained) Popup(r, $"EXP +{exp}", 0xFF90D0FF, 15);
        host.Play(SoundDeath);
        QueueLevelUps();
        host.Toast($"적 {victims.Count}명 정리 — 경험치를 {receivers.Count}명이 나눴습니다");
        // 레벨업 창이 먼저, 전멸 판정은 그 뒤(상태 21 → 4, ba-15 Q7).
        if (_levelUpQueue.Count == 0) CheckOutcome();
        else _outcomeAfterLevelUp = true;
    }

    /// <summary>
    /// 행동이 끝난 뒤 — 레벨이 오른 아군을 줄 세운다(원본 상태 21 부속 0, <c>0x100681ef~0x1006820b</c>).
    /// 원본은 <c>0x1006e940(u,2)</c>(살아 있음·판 안)인 유닛만 본다 — 쓰러진 인물은 죽은 채 레벨업하지 않는다(ba-15 Q7).
    /// </summary>
    internal void QueueLevelUps()
    {
        for (int i = 0; i < host._units.Length; i++)
            if (host._units[i] is { IsAlly: true, Alive: true, OnField: true, Data: { } c } && c.CumExp / 100 > c.Level && !_levelUpQueue.Contains(i))
                _levelUpQueue.Enqueue(i);
    }

    /// <summary>
    /// 레벨업 줄이 빌 때까지 미뤄 둔 전멸 판정 — 원본은 상태 21(레벨업) 뒤 상태 4 에서 전멸을 본다(<c>0x1006ed10</c>).
    /// 마지막 처치의 레벨업 창이 결과 배너에 가려 다음 전투로 밀리지 않게 한다(ba-15 Q7).
    /// </summary>
    internal bool _outcomeAfterLevelUp;

    /// <summary>창을 띄울 차례면 띄우고, 시간이 다 되면 닫는다. 창이 떠 있는 동안 true.</summary>
    internal bool UpdateLevelUp()
    {
        if (LevelUpOpen)
        {
            if (host._lastTime < _levelUpUntil) return true;
            CloseLevelUp();
            // 줄에 남은 사람은 같은 틀에 곧바로 띄운다 — 사이에 한 틀이라도 차례가 돌면 틱이 흘러 미뤄 둔 전멸 판정을 앞지른다.
        }
        if (_routine != null) return false;

        // 창을 숨겨 둔 사람(설정 > 레벨업 창 보이기)에게는 줄에 선 사람을 <b>한 틀에 모두</b> 올려 준다 —
        // 레벨은 그대로 오르고 창만 안 뜬다.
        while (_levelUpQueue.Count > 0)
        {
            int index = _levelUpQueue.Peek();
            var unit = host._units[index];
            if (host._db == null || !unit.Alive || unit.Data is not { } c || c.CumExp / 100 <= c.Level) { _levelUpQueue.Dequeue(); _levelUpCamSent = false; continue; }
            // 21 레벨업(0x1006823e) — 그 유닛을 가운데로 보내고 멈춘 뒤에 올리고 창을 띄운다(감사4 C17). 창을 숨겨 둔 사람은 안 기다린다.
            if (_showLevelUp && !_levelUpCamSent) { _levelUpCamSent = true; CenterOnUnit(unit); return true; }
            if (_showLevelUp && CameraBusy) return true;
            _levelUpQueue.Dequeue();
            _levelUpCamSent = false;

            unit.Data = host._db.LevelUp(c, out var gains);
            host.RefreshUnitStats(unit);
            if (!_showLevelUp) { host.Play(SoundLevelUp); continue; }

            _levelUpUnit = index;
            _levelUpUntil = host._lastTime + LevelUpSeconds;
            _levelUpTitle = host._db.T(1090) is { Length: > 0 } t ? t : "Level Up";
            _levelUpBody = $"{host.UnitName(index)}의 레벨이 {unit.Data.Level}이 되었습니다.\n"
                         + string.Join("\n", gains.Select(g => $"{g.Stat}가 {g.Amount} 상승하였습니다."));
            host._selected = index;
            host.Play(SoundLevelUp);
            host._mixer.SetMusicGain(DuckedMusicGain);
            return true;
        }
        if (_outcomeAfterLevelUp)
        {
            _outcomeAfterLevelUp = false;
            CheckOutcome();
            if (_outcome.Length > 0 || EventsBusy) return true;   // 결과·사건이 섰으면 이번 틀의 차례는 돌리지 않는다
        }
        return false;
    }

    /// <summary>레벨업 창을 띄울지 — 설정 > 레벨업 창 보이기. 꺼도 레벨은 그대로 오른다(사용자 요청).</summary>
    internal bool _showLevelUp = UserSettings.Current.ShowLevelUp;

    /// <summary>DUELDX_LEVELUP=1 이면 시작하자마자 레벨업 창을 띄운다(화면 밖 시험용).</summary>
    internal void OpenLevelUpIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_LEVELUP") != "1" || host._db == null) return;
        _levelUpUnit = Array.FindIndex(host._units, u => u.IsAlly);
        _levelUpUntil = host._lastTime + 3600;
        _levelUpTitle = host._db.T(1090) is { Length: > 0 } t ? t : "Level Up";
        _levelUpBody = $"{host.UnitName(_levelUpUnit)}의 레벨이 {host._units[_levelUpUnit].Data?.Level + 1}이 되었습니다.\n"
                     + "HP가 30 상승하였습니다.\nATK가 4 상승하였습니다.";
    }

    internal void CloseLevelUp()
    {
        _levelUpUnit = -1;
        _eventCheckDue |= 1 << 1;                // 갈래 1 = 레벨업 끝(0x10068270)
        if (_levelUpQueue.Count == 0) host._mixer.SetMusicGain(MusicGain);
    }

    /// <summary>
    /// 레벨업 알림 창(fa-11) — 원본 메시지 창 그대로.
    /// </summary>
    /// <remarks>
    /// 옵시디안 분석-시스템메뉴 「메시지 창 틀 (fa-11)」: 메시지 창(<c>0x10034540</c>)은 글 크기에 맞춰 늘어난다 —
    /// <c>안쪽폭 = max(제목폭 + 100, 본문폭)</c>, <c>창 = (안쪽폭 + 40) × (본문높이 + 80)</c>, 본문은 덩어리 가운데 맞춤·흰색,
    /// O.K 단추는 <c>(창폭/2 − 38, 창높이 − 40)</c> 에 76×23 — 원본 그림 Obs 0471 모션 31(기준점이 한가운데).
    /// 틀·제목줄은 <see cref="DrawGameFrame"/> 가 그린다.
    /// </remarks>
    internal void DrawLevelUp()
    {
        if (!LevelUpOpen) return;
        string[] lines = _levelUpBody.Split('\n');
        // 줄 내림 16(굴림 9pt + 4) — 메시지 창 0x10034540.
        int lineH = 16, bodyH = lines.Length * lineH;
        var (_, titleW, _) = host.GetText(_levelUpTitle, White, 15);
        int bodyW = lines.Max(l => host.GetText(l, White).W);
        int innerW = Math.Max(titleW + 100, bodyW);
        int w = innerW + 40, h = bodyH + 80;
        int x = host._camX + (host.ViewWidth - w) / 2, y = host._camY + (host.ViewHeight - h) / 2 + FrameTitleH / 2;

        host.DrawGameFrame(x, y, w, h, _levelUpTitle);
        // 본문은 덩어리째 가운데, 줄마다 <b>왼쪽 맞춤</b>·흰색(0x10034540 — 첫 줄도 흰색).
        int left = x + 20 + (innerW - bodyW) / 2;
        for (int i = 0; i < lines.Length; i++)
            host.DrawText(lines[i], left, y + 20 + i * lineH, White);

        // O.K 단추 — 원본 그림(76×22)은 기준점이 한가운데라 칸 가운데에 찍는다. 글자는 그림에 들어 있다.
        int bx = x + w / 2 - 38, by = y + h - 40;
        // 단추 그림은 <b>두 장</b>이다 — 평소(31)와 골라짐(32). 원본도 마우스가 얹히면 밝은 쪽으로 바꿔 그린다.
        bool over = host._mouse.X >= bx && host._mouse.X < bx + 76 && host._mouse.Y >= by && host._mouse.Y < by + 23;
        if (host.DrawUi(OkButtonObs, over ? OkButtonMotionOver : OkButtonMotion, 0, bx + 38, by + 11, UiBlend.Alpha, loop: false)) return;
        host.FillRect(bx, by, 76, 23, HeadBg);
        host.StrokeRect(bx, by, 76, 23, BoxLine);
        var (_, ow, _) = host.GetText("O.K", White);
        host.DrawText("O.K", bx + (76 - ow) / 2, by + 4, White);
    }

    /// <summary>원본 O.K 단추 그림 — Obs 0471 모션 31(평소)·32(눌림).</summary>
    internal const int OkButtonObs = 471, OkButtonMotion = 31, OkButtonMotionOver = 32;
}

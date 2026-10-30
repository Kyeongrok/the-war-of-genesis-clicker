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
        if (host._scene.IsPart1) { GainPart1KillExp(killer, victim); return; }
        // 경험치는 <b>사람이 명령하는 유닛</b>만 받는다(0x100742e0 — 편 3 동맹 AI 는 못 받는다).
        if (host._db == null || killer.Data is not { } k || victim.Data is not { } v || !killer.PlayerControlled) return;
        // 11(경험치 증가)은 자르기 <b>앞</b>에 더한다(0x10072285~) — 그래서 한 번에 상한(Num 17)을 넘지 않는다.
        // 장비 보정 42(경험치 %)는 자른 뒤에 얹는다 — 원본은 이 번호를 안 읽어 반지가 헛것이었다(ItemBook.ExpStat, 사용자 요청 ed-2 · menu-86).
        int exp = Math.Max(1, host._db.WithEquipExp(k, host._db.ExpForKill(k, v.Level, killer.Status(11))) / share);
        killer.Data = k with { Exp = k.Exp + exp, CumExp = k.CumExp + exp };
        Popup(killer, $"EXP +{exp}", 0xFF90D0FF, 15);
    }

    /// <summary>
    /// 파트1 처치 보상(<c>0x1003de76</c> → <c>0x10035908</c>~) — 때린 이가 편 4 이고 파트1 Chr &lt; 106 이면 경험치 = 죽은 이 레벨 × 20 / 때린 이 레벨,
    /// eld += Luck × 죽은 이 경험치 / 1000(<see cref="Part1Combat.KillReward"/>). 레벨(경험치 ÷ 100)이 오르면 직업 어빌리티 하나를 올리는데
    /// (<see cref="Part1Job.LevelUpCandidates"/>), 원본처럼 플레이어가 후보에서 바로 고른다(<c>0x1009dac0</c>) — 오른 레벨을 쌓으면(<see cref="Part1Progress.LevelPicks"/>)
    /// 전투가 멈추고 고르기 창이 뜬다. 자란 것은 파트1 진행에 남아 다음 판·세이브로 간다.
    /// </summary>
    /// <summary>
    /// 고른 어빌리티를 지금 판의 그 인물에게도 바로 붙인다 — 전투 유닛 자료는 판을 열 때 한 번 만들어서, 전투 안에서 배운 기술(필살 6연사 따위)이
    /// 다음 판에야 목록에 떴다. 어빌리티 칸만 새 자료로 바꾸고 HP · TP · 소울 · 자리는 그대로 둔다.
    /// </summary>
    internal void RefreshPart1Abilities(int chrCode)
    {
        if (!host._battleLoaded || !host._scene.IsPart1 || host._db?.Character(chrCode) is not { } fresh) return;
        // 능력치 어빌리티(1 HP · 2~9 STR · DEX …)도 오르므로 최대 HP(Lp) · STR(Psy) · AC(Dep) · DEX 까지 새 값으로 — 전에는 어빌리티 목록만 바꿔
        // 전투 안에서 「HP」를 올려도 최대 HP 가 그대로였다(사용자 제보 fg-90). 지금 HP 는 그대로 두고 최대치만 오른다(원본 0x1009ea50 은 최대 HP 를 그때그때 센다).
        foreach (var u in host._units)
            if (u.ChrCode == chrCode && u.Data is { } d)
            {
                u.Data = d with { Abilities = fresh.Abilities, Lp = fresh.Lp, Psy = fresh.Psy, Dep = fresh.Dep, Dex = fresh.Dex };
                int before = u.MaxHp;
                host.StatusScr.RefreshUnitStats(u);
                if (Trace)
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                        $"part1 pick refresh chr {chrCode}: 최대 HP {before} → {u.MaxHp} (HP {u.Hp}) STR {fresh.Psy} AC {fresh.Dep} DEX {fresh.Dex}" + Environment.NewLine);
            }
    }

    /// <summary>파트1 판에서 아직 안 고른 레벨업 어빌리티가 있다 — 사건·승패 판정을 그 뒤로 미룬다.</summary>
    internal bool Part1PickPending => host._scene.IsPart1 && host.Part1.LevelPicks.Count > 0;

    internal void GainPart1KillExp(UnitState killer, UnitState victim)
    {
        // 용병(부하)이 쓰러뜨리면 살아 있는 대장이 쓰러뜨린 것으로 친다(사용자 요청 fg-44, 리메이크 — 원본은 용병 처치에 아무것도 안 준다).
        // 대장이 죽었으면 부대가 풀려(PromoteFollower: 부하는 대장 연결이 끊기고 편 3) 받을 이가 없고, 뽑힌 대장은 용병이라(Chr ≥ 106) 안 받는다.
        if ((uint)killer.LeaderIndex < host._units.Length && host._units[killer.LeaderIndex] is { Alive: true, Side: 4 } leader) killer = leader;
        // 탈것(아지다하카 · 마장기 · 비행기 …, Chr ≥ 106)이 쓰러뜨리면 탄 사람이 받는다 — 원본 0x10035908~ 은 탈것 · 탄 이의 보상을 짝에게 넘긴다.
        // 전에는 탈것이 받을 이로 남아 Chr 조건에 걸려 아무도 못 받았다(카페 제보 fg-96). 숫자는 판에 선 탈것 위에 띄운다.
        var shownOn = killer;
        if (_part1Riders.TryGetValue(killer, out var rider) && rider.Alive) killer = rider;
        if (host._db is not { } db || killer.Side != 4 || killer.ChrCode - Part1Game.ChrBase is not (>= 0 and < 106)
            || Part1.Fighter(killer) is not { } k || Part1.Fighter(victim) is not { } v) return;
        int before = k.Chr.Experience;
        var (exp, eld) = Part1Combat.KillReward(v.Chr.Experience, before, k.Stat(Part1Chr.Luck));
        if (exp <= 0) return;
        var growth = host.Part1.GrowthOf(killer.ChrCode, before);
        growth.Experience += exp;
        host.Part1.Eld += eld;
        // 번 eld 도 함께 띄운다(리메이크 덧붙임 — 처치해도 돈이 안 들어온다는 카페 제보 fg-43: 들어오고 있었지만 보이는 곳이 상단 소지금뿐이었다).
        Popup(shownOn, eld > 0 ? $"EXP +{exp}  {eld} eld" : $"EXP +{exp}", 0xFF90D0FF, 15);
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 kill {killer.ChrCode} → {victim.ChrCode}: EXP {before} + {exp} = {growth.Experience}, eld + {eld}" + Environment.NewLine);
        for (int level = before / 100 + 1; level <= growth.Experience / 100; level++)
        {
            host.Part1.LevelPicks.Add((killer.ChrCode, level));
            host.Toast($"{k.Chr.Name} — 어빌리티를 하나 올릴 수 있습니다");   // 고르기 창이 바로 뜬다(GameWindow.Part1PickOpen). 파트1 은 레벨이 없다
            if (Trace) File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"part1 level {killer.ChrCode} {level}" + Environment.NewLine);
        }
        db.ForgetPart1Characters();
        Part1.Forget(killer.ChrCode);
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
                    int exp = Math.Max(1, host._db.WithEquipExp(r.Data!, host._db.ExpForKill(r.Data!, vd.Level)) / receivers.Count);
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
            host.StatusScr.RefreshUnitStats(unit);
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
        if (host._hiResUi && host.BattleCovered) return;   // 층에 그리는 창은 판을 덮은 화면(모세스 · 필드 …)이 가려 주지 못한다
        string[] lines = _levelUpBody.Split('\n');
        // 줄 내림 16(굴림 9pt + 4) — 메시지 창 0x10034540.
        int lineH = 16, bodyH = lines.Length * lineH;
        var (_, titleW, _) = host.UiGetText(_levelUpTitle, White, 15);
        int bodyW = lines.Max(l => host.UiGetText(l, White).W);
        int innerW = Math.Max(titleW + 100, bodyW);
        int w = innerW + 40, h = bodyH + 80;
        int x = host._camX + (host.ViewWidth - w) / 2, y = host._camY + (host.ViewHeight - h) / 2 + FrameTitleH / 2;

        host.UiFrame(x, y, w, h, _levelUpTitle);
        // 본문은 덩어리째 가운데, 줄마다 <b>왼쪽 맞춤</b>·흰색(0x10034540 — 첫 줄도 흰색).
        int left = x + 20 + (innerW - bodyW) / 2;
        for (int i = 0; i < lines.Length; i++)
            host.UiText(lines[i], left, y + 20 + i * lineH, White);

        // O.K 단추 — 원본 그림(76×22)은 기준점이 한가운데라 칸 가운데에 찍는다. 글자는 그림에 들어 있다.
        int bx = x + w / 2 - 38, by = y + h - 40;
        // 단추 그림은 <b>두 장</b>이다 — 평소(31)와 골라짐(32). 원본도 마우스가 얹히면 밝은 쪽으로 바꿔 그린다.
        bool over = host._mouse.X >= bx && host._mouse.X < bx + 76 && host._mouse.Y >= by && host._mouse.Y < by + 23;
        if (host.UiSprite(OkButtonObs, over ? OkButtonMotionOver : OkButtonMotion, 0, bx + 38, by + 11, loop: false)) return;
        host.UiFill(bx, by, 76, 23, StatusScreen.HeadBg);
        host.UiStroke(bx, by, 76, 23, StatusScreen.BoxLine);
        var (_, ow, _) = host.UiGetText("O.K", White);
        host.UiText("O.K", bx + (76 - ow) / 2, by + 4, White);
    }

    /// <summary>원본 O.K 단추 그림 — Obs 0471 모션 31(평소)·32(눌림).</summary>
    internal const int OkButtonObs = 471, OkButtonMotion = 31, OkButtonMotionOver = 32;
}

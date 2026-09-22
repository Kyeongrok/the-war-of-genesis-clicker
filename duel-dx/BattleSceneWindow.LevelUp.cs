using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 레벨업(fg-9) — 쓰러뜨리면 경험치가 들어오고, 그 행동이 끝난 뒤 그 자리에서 레벨업 창이 뜬다.
/// </summary>
/// <remarks>
/// 옵시디안 분석-캐릭터 "레벨업 알림 창": 경험치는 <b>처치할 때만</b> `Num[14] + Num[15]×(적 레벨 − 내 레벨)`(10~100),
/// 레벨 = 쌓인 경험치 ÷ 100, 오름은 직업 성장률 × 기본값(난수 없음), HP·TP 회복은 없다.
/// 창은 화면 한가운데에 제목 TXR 1090 「Level Up」과 본문 TXR 1091 + 오른 능력치 줄, 효과음 Snd 107,
/// 배경음악 40% 로 줄었다가 창이 닫히면 되돌아온다. 180틱(6초) 뒤 저절로 닫히고 아무 키·클릭으로도 닫힌다.
/// 여러 명이면 한 명씩 잇따라 뜬다. 원본의 카메라 이동은 우리 화면 따라가기로 대신한다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>레벨업 창이 저절로 닫히기까지 — 원본은 180틱이지만 사용자 요청으로 2초.</summary>
    private const double LevelUpSeconds = 2.0;
    private const float DuckedMusicGain = 0.4f;

    private readonly Queue<int> _levelUpQueue = new();
    private int _levelUpUnit = -1;
    private double _levelUpUntil;
    private string _levelUpTitle = "", _levelUpBody = "";

    private bool LevelUpOpen => _levelUpUnit >= 0;

    /// <summary>쓰러뜨린 쪽에 경험치를 준다(메시지 1016).</summary>
    private void GainKillExp(UnitState killer, UnitState victim)
    {
        if (_db == null || killer.Data is not { } k || victim.Data is not { } v || !killer.IsAlly) return;
        int exp = _db.ExpForKill(k, v.Level);
        // 11(경험치 증가) — 값% 만큼 더 받는다(0x100721dd).
        if (killer.Status(11) is var more and > 0) exp += exp * more / 100;
        killer.Data = k with { Exp = k.Exp + exp, CumExp = k.CumExp + exp };
        Popup(killer, $"EXP +{exp}", 0xFF90D0FF, 15);
    }

    /// <summary>행동이 끝난 뒤 — 레벨이 오른 아군을 줄 세운다(원본 상태 21).</summary>
    private void QueueLevelUps()
    {
        for (int i = 0; i < _units.Length; i++)
            if (_units[i].IsAlly && _units[i].Data is { } c && c.CumExp / 100 > c.Level && !_levelUpQueue.Contains(i))
                _levelUpQueue.Enqueue(i);
    }

    /// <summary>창을 띄울 차례면 띄우고, 시간이 다 되면 닫는다. 창이 떠 있는 동안 true.</summary>
    private bool UpdateLevelUp()
    {
        if (LevelUpOpen)
        {
            if (_lastTime < _levelUpUntil) return true;
            CloseLevelUp();
            return LevelUpOpen;
        }
        if (_levelUpQueue.Count == 0 || _routine != null) return false;

        int index = _levelUpQueue.Dequeue();
        var unit = _units[index];
        if (_db == null || unit.Data is not { } c || c.CumExp / 100 <= c.Level) return false;

        unit.Data = _db.LevelUp(c, out var gains);
        RefreshUnitStats(unit);
        _levelUpUnit = index;
        _levelUpUntil = _lastTime + LevelUpSeconds;
        _levelUpTitle = _db.T(1090) is { Length: > 0 } t ? t : "Level Up";
        _levelUpBody = $"{UnitName(index)}의 레벨이 {unit.Data.Level}이 되었습니다.\n"
                     + string.Join("\n", gains.Select(g => $"{g.Stat}가 {g.Amount} 상승하였습니다."));
        _selected = index;
        Play(SoundLevelUp);
        _mixer.SetMusicGain(DuckedMusicGain);
        return true;
    }

    /// <summary>DUELDX_LEVELUP=1 이면 시작하자마자 레벨업 창을 띄운다(화면 밖 시험용).</summary>
    private void OpenLevelUpIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_LEVELUP") != "1" || _db == null) return;
        _levelUpUnit = Array.FindIndex(_units, u => u.IsAlly);
        _levelUpUntil = _lastTime + 3600;
        _levelUpTitle = _db.T(1090) is { Length: > 0 } t ? t : "Level Up";
        _levelUpBody = $"{UnitName(_levelUpUnit)}의 레벨이 {_units[_levelUpUnit].Data?.Level + 1}이 되었습니다.\n"
                     + "HP가 30 상승하였습니다.\nATK가 4 상승하였습니다.";
    }

    private void CloseLevelUp()
    {
        _levelUpUnit = -1;
        if (_levelUpQueue.Count == 0) _mixer.SetMusicGain(MusicGain);
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
    private void DrawLevelUp()
    {
        if (!LevelUpOpen) return;
        string[] lines = _levelUpBody.Split('\n');
        int lineH = 20, bodyH = lines.Length * lineH;
        var (_, titleW, _) = GetText(_levelUpTitle, White, 15);
        int innerW = Math.Max(titleW + 100, lines.Max(l => GetText(l, White).W));
        int w = innerW + 40, h = bodyH + 80;
        int x = _camX + (ViewWidth - w) / 2, y = _camY + (ViewHeight - h) / 2 + FrameTitleH / 2;

        DrawGameFrame(x, y, w, h, _levelUpTitle);
        for (int i = 0; i < lines.Length; i++)
        {
            var (_, lw, _) = GetText(lines[i], White);
            DrawText(lines[i], x + (w - lw) / 2, y + 20 + i * lineH, i == 0 ? 0xFFFFE070 : White);
        }

        // O.K 단추 — 원본 그림(76×22)은 기준점이 한가운데라 칸 가운데에 찍는다. 글자는 그림에 들어 있다.
        int bx = x + w / 2 - 38, by = y + h - 40;
        // 단추 그림은 <b>두 장</b>이다 — 평소(31)와 골라짐(32). 원본도 마우스가 얹히면 밝은 쪽으로 바꿔 그린다.
        bool over = _mouse.X >= bx && _mouse.X < bx + 76 && _mouse.Y >= by && _mouse.Y < by + 23;
        if (DrawUi(OkButtonObs, over ? OkButtonMotionOver : OkButtonMotion, 0, bx + 38, by + 11, UiBlend.Alpha, loop: false)) return;
        FillRect(bx, by, 76, 23, HeadBg);
        StrokeRect(bx, by, 76, 23, BoxLine);
        var (_, ow, _) = GetText("O.K", White);
        DrawText("O.K", bx + (76 - ow) / 2, by + 4, White);
    }

    /// <summary>원본 O.K 단추 그림 — Obs 0471 모션 31(평소)·32(눌림).</summary>
    private const int OkButtonObs = 471, OkButtonMotion = 31, OkButtonMotionOver = 32;
}

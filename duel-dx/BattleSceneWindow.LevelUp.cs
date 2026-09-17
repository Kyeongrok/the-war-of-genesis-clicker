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
    private const int LevelUpTicks = 180;
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
        _levelUpUntil = _lastTime + LevelUpTicks / TicksPerSecond;
        _levelUpTitle = _db.T(1090) is { Length: > 0 } t ? t : "Level Up";
        _levelUpBody = $"{UnitName(index)}의 레벨이 {unit.Data.Level}이 되었습니다.\n"
                     + string.Join("\n", gains.Select(g => $"{g.Stat}가 {g.Amount} 상승하였습니다."));
        _selected = index;
        Play(SoundLevelUp);
        _mixer.SetMusicGain(DuckedMusicGain);
        return true;
    }

    private void CloseLevelUp()
    {
        _levelUpUnit = -1;
        if (_levelUpQueue.Count == 0) _mixer.SetMusicGain(MusicGain);
    }

    private void DrawLevelUp()
    {
        if (!LevelUpOpen) return;
        string[] lines = _levelUpBody.Split('\n');
        int w = Math.Max(240, lines.Max(l => GetText(l, White).W) + 40);
        int h = 36 + lines.Length * 20 + 40;
        int x = (BoardWidth - w) / 2, y = _camY + (ViewHeight - h) / 2;

        FillRect(x, y, w, h, PanelBg);
        StrokeRect(x, y, w, h, BoxLine);
        FillRect(x, y, w, 26, HeadBg);
        var (_, tw, _) = GetText(_levelUpTitle, White, 15);
        DrawText(_levelUpTitle, x + (w - tw) / 2, y + 4, White, 15);
        for (int i = 0; i < lines.Length; i++) DrawText(lines[i], x + 20, y + 36 + i * 20, i == 0 ? 0xFFFFE070 : White);

        int bx = x + (w - 76) / 2, by = y + h - 30;
        FillRect(bx, by, 76, 23, HeadBg);
        StrokeRect(bx, by, 76, 23, BoxLine);
        var (_, ow, _) = GetText("확인", White);
        DrawText("확인", bx + (76 - ow) / 2, by + 3, White);
    }
}

using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 상태이상 — 거는 규칙, 매 틱 피해, 행동 제약(분석-전투 「6. 상태이상」).
/// </summary>
/// <remarks>
/// work 의 (번호, 값) 짝 셋(<c>.att</c> 파일 28·31·34)이 곧 상태이상이다. 거는 차례(<c>0x1007c210</c>):
/// <list type="number">
/// <item>피해와 같은 명중식을 먼저 본다(빗나가면 아무것도 안 걸린다).</item>
/// <item>종류 0·2 이고 <b>공격자 레벨 ≤ 대상 레벨</b>이면 번호 5·6·12·19·22·23·24 는 실패한다.</item>
/// <item>번호 30·31·32·33·37·48 은 슬롯이 아니라 전투 보정(DEX·PSY·DEP·최대TP·최대SOUL·최대HP)에 바로 더한다.</item>
/// <item>나머지는 칸 셋에 — 같은 번호면 |값| 이 큰 쪽, 빈 칸, 그것도 없으면 이번에 안 쓴 칸 중 아무 데나.</item>
/// <item>44·45·46 은 이름이 「없음」인 <b>빈칸 표시</b>다 — 큐어처럼 셋을 걸면 칸 셋이 모두 지워진다.</item>
/// </list>
/// 지속 칸은 없다. 5(마비)·6(빙결)만 TP 틱마다 3% 로 풀리고, 47 은 쓰러질 때 쓰이며 지워지고, 나머지는 전투가 끝날 때까지 남는다.
/// 매 틱 피해(상태 16 → <c>0x1007a360</c>): 2·3·17 은 <c>값% × 최대 HP</c>(Num36 밑으로는 안 내려감), 16 은 TP(바닥 Num37), 15 는 SOUL(바닥 Num38).
/// 이름은 원본 <c>Dat/Sta.dat</c> 의 설명 TXR 이지만, 우리는 분석 노트의 뜻 표를 그대로 짧게 적어 쓴다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>번호 → 짧은 이름(분석-전투 6절 표). 여기 없는 번호는 칸에 번호만 보인다.</summary>
    private static readonly Dictionary<int, string> AilmentNames = new()
    {
        [1] = "DEX 저하", [2] = "화염", [3] = "중독", [4] = "버서커", [5] = "마비", [6] = "빙결",
        [7] = "피해 감소", [8] = "자동 회복", [9] = "소울 습득", [10] = "피격 가속", [11] = "경험치 증가",
        [12] = "어빌 봉인", [13] = "공격력 변화", [14] = "방어력 변화", [15] = "소울 소모", [16] = "TP 소모",
        [17] = "체력 소모", [18] = "소울 비용", [19] = "소울 정지", [20] = "TP 비용", [21] = "소울 습득 변화",
        [22] = "소울 사망", [23] = "TP 사망", [24] = "소울 만사망", [25] = "이동 불가", [26] = "휴식 불가",
        [27] = "악세사리 무시", [29] = "무기 공격력", [38] = "턴 속도", [40] = "DEP 저하",
        [41] = "반사", [42] = "EXP 증가", [43] = "소울 증가", [47] = "전투불능 방지",
    };

    /// <summary>슬롯이 아니라 전투 보정으로 들어가는 번호들.</summary>
    private static bool IsStatBonus(int id) => id is 30 or 31 or 32 or 33 or 37 or 48;

    /// <summary>레벨이 같거나 낮으면 안 걸리는 번호들(종류 0·2 일 때).</summary>
    private static bool NeedsLevelEdge(int id) => id is 5 or 6 or 12 or 19 or 22 or 23 or 24;

    private readonly Random _ailmentRandom = new();

    /// <summary>work 의 상태이상을 대상에게 건다(명중은 부른 쪽에서 이미 봤다).</summary>
    private void ApplyAilments(UnitState attacker, UnitState target, WorkData w)
    {
        if (w.Bonuses.Length == 0) return;
        var used = new List<int>();
        foreach (var (id, value) in w.Bonuses)
        {
            if (id == 0) continue;
            if (w.Kind is 0 or 2 && NeedsLevelEdge(id)
                && (attacker.Data?.Level ?? 0) <= (target.Data?.Level ?? 0)) continue;

            if (IsStatBonus(id))
            {
                AddStatBonus(target, id, value);
                continue;
            }
            PutAilment(target, (byte)id, value, used);
        }
        RefreshUnitStats(target);
        if (target.Hp > target.MaxHp) target.Hp = target.MaxHp;
    }

    private static void AddStatBonus(UnitState u, int id, int value)
    {
        switch (id)
        {
            case 30: u.BonusDex += value; break;
            case 31: u.BonusPsy += value; break;
            case 32: u.BonusDep += value; break;
            case 33: u.BonusMaxTp += value; break;
            case 37: u.BonusMaxSoul += value; break;
            case 48: u.BonusMaxHp += value; break;
        }
    }

    /// <summary>칸 셋에 넣기 — 같은 번호면 센 쪽, 빈 칸, 아니면 이번에 안 쓴 칸 중 아무 데나.</summary>
    private void PutAilment(UnitState u, byte id, short value, List<int> used)
    {
        for (int i = 0; i < 3; i++)
            if (u.StatusId[i] == id)
            {
                if (Math.Abs(value) > Math.Abs(u.StatusValue[i])) u.StatusValue[i] = value;
                used.Add(i);
                return;
            }
        for (int i = 0; i < 3; i++)
            if (u.StatusId[i] == 0)
            {
                (u.StatusId[i], u.StatusValue[i]) = (id, value);
                used.Add(i);
                return;
            }
        var free = Enumerable.Range(0, 3).Where(i => !used.Contains(i)).ToList();
        if (free.Count == 0) return;
        int slot = free[_ailmentRandom.Next(free.Count)];
        (u.StatusId[slot], u.StatusValue[slot]) = (id, value);
        used.Add(slot);
    }

    /// <summary>TP 틱마다 — 매 턴 깎이는 것들과 마비·빙결 풀림(3%).</summary>
    private void TickAilments()
    {
        if (_db is not { } db) return;
        foreach (var u in _units)
        {
            if (!u.Alive) continue;

            for (int i = 0; i < 3; i++)
                if (u.StatusId[i] is 5 or 6 && _ailmentRandom.Next(100) < 3)
                    (u.StatusId[i], u.StatusValue[i]) = (0, 0);

            int percent = u.Status(2) + u.Status(3) + u.Status(17);
            if (percent > 0)
            {
                int damage = u.MaxHp * percent / 100;
                int floor = db.N(36);
                int hp = Math.Max(Math.Min(u.Hp, floor), u.Hp - damage);
                if (hp < u.Hp)
                {
                    ShowNumber(u, $"{db.T(159)} {u.Hp - hp}", 0xFFFF6060);
                    u.Hp = hp;
                }
            }
            if (u.Status(16) is var tpLoss and > 0)
            {
                int floor = db.N(37);
                int tp = Math.Max(Math.Min(u.Tp, floor), u.Tp - tpLoss);
                if (tp < u.Tp) { ShowNumber(u, $"{db.T(38)} {u.Tp - tp}", 0xFF80D0FF); u.Tp = tp; }
            }
            if (u.Status(15) is var soulLoss and > 0)
            {
                int floor = db.N(38);
                int soul = Math.Max(Math.Min(u.Soul, floor), u.Soul - soulLoss);
                if (soul < u.Soul) { ShowNumber(u, $"{db.T(41)} {u.Soul - soul}", 0xFFC0A0FF); u.Soul = soul; }
            }

            // 턴 속도(38) — TP 가 차는 속도에 그대로 더한다
            if (u.Status(38) is var speed and > 0) u.Tp = Math.Min(u.MaxTp, u.Tp + speed);
        }
    }

    /// <summary>차례를 받을 수 있나 — 마비·빙결이면 못 받는다(<c>0x1007c480</c>).</summary>
    private static bool CanTakeTurn(UnitState u) => !u.HasStatus(5) && !u.HasStatus(6);

    /// <summary>쓰러질 때 — 47(전투불능 방지)이 있으면 한 번 살아나고 그 칸이 지워진다.</summary>
    private bool SurvivesFatal(UnitState u)
    {
        for (int i = 0; i < 3; i++)
            if (u.StatusId[i] == 47)
            {
                (u.StatusId[i], u.StatusValue[i]) = (0, 0);
                u.Hp = Math.Max(1, _db?.N(36) ?? 10);
                Popup(u, AilmentNames[47], 0xFFFFE070, 15);
                return true;
            }
        return false;
    }

    /// <summary>상태이상이 매 틱 깎아 쓰러뜨린 인물 — 동작 없이 바로 눕힌다.</summary>
    private void KillUnit(UnitState u)
    {
        u.Alive = false;
        Play(SoundDeath);
        CheckOutcome();
    }

    /// <summary>피해 보정 — 때리는 쪽 13(공격력), 맞는 쪽 14(방어력)·7(피해 감소).</summary>
    private static int AilmentDamage(UnitState attacker, UnitState target, int amount)
    {
        if (attacker.Status(13) is var atk and not 0) amount = amount * (100 + atk) / 100;
        if (target.Status(14) is var def and not 0) amount = amount * (100 - def) / 100;
        if (target.Status(7) is var cut and > 0) amount = amount * (100 - cut) / 100;
        return Math.Max(0, amount);
    }

    /// <summary>41(반사) — 받은 피해의 값% 를 때린 쪽에 돌려준다.</summary>
    private void Counterattack(UnitState attacker, UnitState target, int amount)
    {
        int percent = target.Status(41);
        if (percent <= 0 || amount <= 0 || !attacker.Alive) return;
        int back = amount * percent / 100;
        if (back <= 0) return;
        attacker.Hp = Math.Max(0, attacker.Hp - back);
        ShowNumber(attacker, $"{_db?.T(159)} {back}", DamageColor);
        if (attacker.Hp <= 0 && !SurvivesFatal(attacker)) KillUnit(attacker);
    }

    /// <summary>칸에 보이는 상태이상 이름들(빈 칸은 제외).</summary>
    private List<string> AilmentLabels(UnitState u)
    {
        var list = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            int id = u.StatusId[i];
            if (id is 0 or 44 or 45 or 46) continue;
            string name = AilmentNames.GetValueOrDefault(id, $"상태 {id}");
            list.Add(u.StatusValue[i] != 0 ? $"{name} {u.StatusValue[i]}" : name);
        }
        return list;
    }
}

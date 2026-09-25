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
    /// <summary>상태이상 칸 그림 — <c>Obs 0489</c>(전투가 미리 읽어 두는 공용 그림 스물넷 중 하나).</summary>
    private const int AilmentIconObs = 489;

    /// <summary>
    /// 그 유닛의 <paramref name="slot"/> 번째 상태이상 칸에 그릴 <c>Obs 0489</c> 모션.
    /// </summary>
    /// <remarks>
    /// 원본은 칸마다 <c>Sta.dat[번호]</c> 의 아이콘 번호를 그대로 모션으로 쓰고, <b>0 이면 「EMPTY」 판(모션 0)</b>이 나온다
    /// (분석-전투 「상태이상 칸 3개」, 창 228). 「없음」인 44·45·46 도 자료에서 아이콘이 0 이라 저절로 빈 칸이 된다.
    /// </remarks>
    private int AilmentIconMotion(UnitState u, int slot)
    {
        int id = (uint)slot < 3 ? u.StatusId[slot] : 0;
        return id != 0 && _db?.Statuses.GetValueOrDefault(id) is { } sta ? sta.Icon : 0;
    }

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

    /// <summary>
    /// 상태이상까지 얹은 능력치 — 판정·이동 범위·능력치 표시는 모두 이것을 써야 한다.
    /// </summary>
    /// <remarks>
    /// 칸 아닌 보정 <b>30 DEX · 31 PSY · 32 DEP</b> 를 더하고, 칸에 걸린 <b>1(DEX −1, <c>0x1007ae8e</c>)</b> 과
    /// <b>40(DEP −1, <c>0x1007af85</c>)</b> 을 뺀다. 둘 다 0 밑으로는 안 내려간다. 최대 HP·TP·SOUL(33·37·48)은
    /// <see cref="RefreshUnitStats"/> 가 이미 반영한다.
    /// </remarks>
    private static CharacterData? EffectiveData(UnitState u)
    {
        if (u.Data is not { } c) return null;
        int dex = c.Dex + u.BonusDex - (u.HasStatus(1) ? 1 : 0);
        int psy = c.Psy + u.BonusPsy;
        int dep = c.Dep + u.BonusDep - (u.HasStatus(40) ? 1 : 0);
        // 27(악세사리 무시) — 장비 셋째 칸을 없는 것으로 친다. 그 칸 보정이 능력치 계산에서 빠진다.
        bool noAccessory = u.HasStatus(27) && c.Items.Length > 2 && c.Items[2] != 0;
        if (dex == c.Dex && psy == c.Psy && dep == c.Dep && !noAccessory) return c;

        var items = c.Items;
        if (noAccessory)
        {
            items = [.. c.Items];
            items[2] = 0;
        }
        return c with
        {
            Dex = (ushort)Math.Max(0, dex),
            Psy = (ushort)Math.Max(0, psy),
            Dep = (ushort)Math.Max(0, dep),
            Items = items,
        };
    }

    /// <summary>
    /// 그 인물 눈에 상대가 적으로 보이나 — <b>4(버서커)</b> 가 걸려 있으면 <b>자기 말고 모두</b>가 적이다(<c>0x1006fde0</c>).
    /// </summary>
    /// <remarks>판정은 <b>움직이는 쪽</b> 기준이다. 버서커가 걸린 인물만 편을 못 가리고, 남들이 그 인물을 보는 눈은 그대로다.</remarks>
    private static bool SeesAsFoe(UnitState viewer, UnitState other) =>
        viewer != other && (viewer.HasStatus(4) || viewer.IsAlly != other.IsAlly);

    /// <summary>레벨이 같거나 낮으면 안 걸리는 번호들(종류 0·2 일 때).</summary>
    private static bool NeedsLevelEdge(int id) => id is 5 or 6 or 12 or 19 or 22 or 23 or 24;

    private readonly Random _ailmentRandom = new();

    /// <summary>
    /// work 의 상태이상을 대상에게 건다. 기본공격이면 <b>무기 아이템의 효과</b>를 건다.
    /// </summary>
    /// <remarks>
    /// 거는 함수(<c>0x1007bcc0</c>)는 들어오자마자 <b>명중을 제 손으로 한 번 더 굴린다</b> — 부르는 곳이 한 군데뿐이라
    /// 피해형(종류 0)은 <b>두 번째</b> 굴림이고, 회복·보조(1·2·3)에는 이것이 <b>유일한</b> 굴림이다.
    /// </remarks>
    private void ApplyAilments(UnitState attacker, UnitState target, WorkData w)
    {
        if (_db is { } hitDb && attacker.Data is { } ha && target.Data is { } ht
            && _ailmentRandom.Next(100) >= hitDb.HitChance(ha, attacker.Tp, ht, target.Tp, w, target.Stance)) return;

        // 기본공격(work 번호 == 인물의 기본 work)이면 work 이 아니라 <b>무기 Itm 의 공격 효과</b>(파일 30/34/38)를 건다.
        // 무기가 없으면 아무것도 안 건다(0x1007bda4). 지금 판 자료에는 이 칸이 든 아이템이 하나도 없다.
        (int Id, int Value)[] effects;
        if (attacker.Data is { } ac && w.Id == ac.BasicWorkId)
        {
            if (ac.Items[0] == 0 || _db?.Items.GetValueOrDefault(ac.Items[0]) is not { } weapon) return;
            effects = [.. (weapon.AttackEffects ?? []).Select(e => ((int)e.Status, (int)e.Value))];
        }
        else effects = [.. w.Bonuses.Select(b => ((int)b.Stat, (int)b.Value))];
        if (effects.Length == 0) return;
        // 레벨 조건(표 0x1007be84)은 그 효과 하나만 빼는 것이 아니다 — 종류 0·2 이고 공격자 레벨이 대상 이하인데
        // 세 효과 중 하나라도 5·6·12·19·22·23·24 이면 <b>work 전체가 실패</b>하고 회피 반응이 나온다(0x1007bcc0).
        if (w.Kind is 0 or 2 && (attacker.Data?.Level ?? 0) <= (target.Data?.Level ?? 0)
            && effects.Any(e => NeedsLevelEdge(e.Id))) return;

        var used = new List<int>();
        foreach (var (id, value) in effects)
        {
            if (id == 0) continue;
            if (IsStatBonus(id))
            {
                AddStatBonus(target, id, value);
                continue;
            }
            PutAilment(target, (byte)id, (short)value, used, attacker);
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
    private void PutAilment(UnitState u, byte id, short value, List<int> used, UnitState? source = null)
    {
        // 마비·빙결·붙잡힘(5·6·25)이 걸리면 걷던 걸음을 그 자리에서 멈춘다(0x1007c480 이 이동을 막는다).
        if (id is 5 or 6 or 25) u.Path.Clear();
        for (int i = 0; i < 3; i++)
            if (u.StatusId[i] == id)
            {
                if (Math.Abs(value) > Math.Abs(u.StatusValue[i])) (u.StatusValue[i], u.StatusSource[i]) = (value, source);
                used.Add(i);
                return;
            }
        for (int i = 0; i < 3; i++)
            if (u.StatusId[i] == 0)
            {
                (u.StatusId[i], u.StatusValue[i], u.StatusSource[i]) = (id, value, source);
                used.Add(i);
                return;
            }
        var free = Enumerable.Range(0, 3).Where(i => !used.Contains(i)).ToList();
        if (free.Count == 0) return;
        int slot = free[_ailmentRandom.Next(free.Count)];
        (u.StatusId[slot], u.StatusValue[slot], u.StatusSource[slot]) = (id, value, source);
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
                    (u.StatusId[i], u.StatusValue[i], u.StatusSource[i]) = (0, 0, null);

            // 세 값을 <b>각각</b> 「값% × 최대 HP」 로 셈해 더한다 — 퍼센트를 먼저 합치면 정수 나눗셈에서 한둘 어긋난다.
            int percent = u.Status(2) + u.Status(3) + u.Status(17);
            if (percent > 0)
            {
                int damage = u.MaxHp * u.Status(2) / 100 + u.MaxHp * u.Status(3) / 100 + u.MaxHp * u.Status(17) / 100;
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

        }
    }

    /// <summary>SOUL 을 채운다 — 19(소울이 차지 않음)가 걸려 있으면 하나도 안 들어간다(<c>0x10071d45</c>).</summary>
    private static void AddSoul(UnitState u, int amount)
    {
        if (amount <= 0 || u.HasStatus(19)) return;
        u.Soul = Math.Min(u.MaxSoul, u.Soul + amount);
    }

    /// <summary>work 이 무는 SOUL — 18(소울 소모량 %)만큼 늘거나 준다(<c>0x100724c4</c>).</summary>
    private int SoulCostFor(UnitState u, CharacterData c, int workId) =>
        Math.Max(0, (_db?.WorkSoulCost(c, workId) ?? 0) * (100 + u.Status(18)) / 100);

    /// <summary>work 을 쓰려면 있어야 하는 SOUL — 비용과 같은 보정을 받는다.</summary>
    private int SoulNeedFor(UnitState u, CharacterData c, int workId) =>
        Math.Max(0, (_db?.WorkSoulNeed(c, workId) ?? 0) * (100 + u.Status(18)) / 100);

    /// <summary>work 이 무는 TP — 20(TP 소모량 %)만큼 늘거나 준다(<c>0x10072694</c>).</summary>
    private int TpCostFor(UnitState u, CharacterData c, int workId) =>
        Math.Max(0, (_db?.WorkTpCost(c, workId) ?? 0) * (100 + u.Status(20)) / 100);

    /// <summary>
    /// 상태이상이 거는 사망 조건 — 22 SOUL 이 값 아래 · 23 TP 가 값 아래 · 24 SOUL 이 가득(<c>0x1007c689</c>~).
    /// </summary>
    private static bool DiesByStatus(UnitState u) =>
        (u.HasStatus(22) && u.Status(22) > u.Soul)
        || (u.HasStatus(23) && u.Status(23) > u.Tp)
        || (u.HasStatus(24) && u.Soul >= u.MaxSoul);

    /// <summary>
    /// 차례를 시작할 때 8(자동 회복) — HP 가 값(최대 HP 한도)보다 적으면 그 값까지 채운다(<c>0x10067dd1</c>).
    /// </summary>
    private void AutoHeal(UnitState u)
    {
        if (!u.HasStatus(8)) return;
        int upTo = Math.Min(u.Status(8), u.MaxHp);
        if (u.Hp >= upTo) return;
        int before = u.Hp;
        u.Hp = upTo;
        ShowNumber(u, _db?.T(159) ?? "", HealColor2, rise: false, count: (before, u.Hp));
    }

    /// <summary>차례를 받을 수 있나 — 마비·빙결이면 못 받는다(<c>0x1007c480</c>).</summary>
    private static bool CanTakeTurn(UnitState u) => !u.HasStatus(5) && !u.HasStatus(6);

    /// <summary>쓰러질 때 — 47(전투불능 방지)이 있으면 한 번 살아나고 그 칸이 지워진다.</summary>
    private bool SurvivesFatal(UnitState u)
    {
        for (int i = 0; i < 3; i++)
            if (u.StatusId[i] == 47)
            {
                (u.StatusId[i], u.StatusValue[i], u.StatusSource[i]) = (0, 0, null);
                u.Hp = Math.Max(1, _db?.N(36) ?? 10);
                Popup(u, AilmentNames[47], 0xFFFFE070, 15);
                return true;
            }
        return false;
    }

    /// <summary>
    /// 상태이상으로 쓰러진 인물의 처치 경험치 — 그 상태이상을 건 인물들(살아 있고 편이 다른 쪽, 겹치면 한 번)이 똑같이 나눈다.
    /// 원본은 피해를 준 순간에만 경험치를 줘서(메시지 1016) 커스 따위로 쓰러지면 아무도 못 받았다.
    /// </summary>
    private void GainAilmentKillExp(UnitState victim)
    {
        var sources = victim.StatusSource.OfType<UnitState>().Distinct()
                            .Where(s => s.Alive && s.IsAlly != victim.IsAlly).ToList();
        foreach (var s in sources) GainKillExp(s, victim, sources.Count);
        if (sources.Count > 0) QueueLevelUps();
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
        // 곱하는 차례가 결과를 바꾼다(정수 나눗셈) — 원본은 7 → 13 → 14 순이다(0x1007b880 · 0x1007b8b3 · 0x1007b8e2).
        if (target.Status(7) is var cut and > 0) amount = amount * (100 - cut) / 100;
        if (attacker.Status(13) is var atk and not 0) amount = amount * (100 + atk) / 100;
        if (target.Status(14) is var def and not 0) amount = amount * (100 - def) / 100;
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

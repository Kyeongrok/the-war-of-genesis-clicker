using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 난이도(설정 > 난이도) — 원본에 없는 기능이다. 보통이 원본 그대로이고, 어려움부터 적의 최대 HP·주는 피해에 배율을 걸고
/// 적 AI 가 기술과 대상을 더 잘 고른다.
/// </summary>
/// <remarks>
/// 아군은 레벨마다 직업 성장률로 선형으로 오르는데(Lv60 에 HP·PSY 약 3.9배) 적은 <c>Lev.dat</c> 표로 오른다(Lv60 에 LP +158% · PSY +151% · DEP +50%).
/// 그래서 챕터 6 쯤이면 같은 레벨의 일반병사가 살라딘을 쓰러뜨리는 데 120타가 넘게 걸려 전투가 지루했다(사용자 보고).
/// 적 HP 만 올리면 오래 때리기만 해서 더 지루하므로, 적이 주는 피해를 더 크게 올린다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    internal const int MenuDifficultyBase = 1140;

    /// <summary>난이도 단계 — (이름, 적 최대 HP %, 적이 주는 피해 %).</summary>
    internal static readonly (string Name, int HpPercent, int DamagePercent)[] DifficultyChoices =
    [
        ("보통 (원본)", 100, 100),
        ("어려움", 150, 250),
        ("매우 어려움", 200, 400),
    ];

    internal int _difficulty = Math.Clamp(UserSettings.Current.Difficulty, 0, DifficultyChoices.Length - 1);

    /// <summary>난이도가 거는 쪽 — 적(편 0~2). 동맹 AI 는 내 편이라 안 건다.</summary>
    internal static bool IsFoeSide(UnitState u) => !u.IsAlly;

    /// <summary>적이면 최대 HP 에 난이도 배율을 건다.</summary>
    internal int ScaleMaxHp(UnitState u, int maxHp) =>
        IsFoeSide(u) ? Math.Max(1, (int)((long)maxHp * DifficultyChoices[_difficulty].HpPercent / 100)) : maxHp;

    /// <summary>적이 내 편을 때린 피해에 난이도 배율을 건다(적끼리·내 편끼리는 그대로).</summary>
    internal int ScaleDamage(UnitState attacker, UnitState target, int amount) =>
        IsFoeSide(attacker) && !IsFoeSide(target) ? (int)((long)amount * DifficultyChoices[_difficulty].DamagePercent / 100) : amount;

    /// <summary>
    /// 강화된 AI 를 쓰나 — 어려움 이상의 적만. 원본 AI(<c>0x10060730</c>)는 목록에서 <b>쓸 수 있는 첫 work</b> 을 쓰고,
    /// 대상은 work <c>+0x3e</c> 기준(HP·SOUL 따위의 최댓값)으로 고른다. 강화 AI 는 피해 기술을 모두 재어 가장 많이 깎는 것을 고른다.
    /// </summary>
    internal bool SmartAi(UnitState u) => _difficulty > 0 && IsFoeSide(u);

    /// <summary>
    /// 강화 AI 의 대상 점수 — 맞는 적마다 「기대 피해가 그 적 최대 HP 의 몇 ‰ 인가」를 더하고, 쓰러뜨릴 수 있으면 크게 더한다.
    /// 제 편이 맞으면(피아무관 기술) 그만큼 뺀다. 그래서 약해진 적을 몰아친다.
    /// </summary>
    internal int SmartValue(UnitState user, WorkData w, List<int> targets)
    {
        if (_db is not { } db || user.Data is not { } a) return 0;
        long total = 0;
        foreach (int i in targets)
        {
            var t = _units[i];
            if (t.Data is not { } d || t.MaxHp <= 0) continue;
            int dmg = (db.N(3) - db.Rdp(d, t.Hp, t.MaxHp)) * db.Atk(a, AttackSoul(w, user.Soul), w.Power) / Math.Max(1, db.N(3));
            dmg = ScaleDamage(user, t, Math.Max(0, dmg));
            int hit = Math.Clamp(db.HitChance(a, user.Tp, d, t.Tp, w, t.Stance), 0, 100);
            long value = (long)Math.Min(dmg, t.Hp) * 1000 / t.MaxHp * hit / 100;
            if (dmg >= t.Hp) value += 1500 * hit / 100;
            total += SeesAsFoe(user, t) ? value : -value;
        }
        return (int)Math.Clamp(total, 0, 1_000_000);
    }

    /// <summary>
    /// 강화 AI 의 공격 고르기 — 쓸 수 있는 <b>피해 기술</b>마다 가장 좋은 (설 칸, 겨눌 칸)을 찾아 점수가 가장 높은 것을 고른다.
    /// 피해 기술을 못 쓰면 null(원본 차례대로 돌아간다).
    /// </summary>
    internal (WorkData Work, (int Stand, int Col, int Row, int Score) Use)? SmartPick(int index, MoveRange range)
    {
        (WorkData, (int, int, int, int))? best = null;
        foreach (var w in AiWorks(_units[index]))
        {
            if (!w.IsDamage || BestUse(index, w, ComputeRange(_units[index], w.Id) ?? range) is not { } use) continue;   // 이동 예산은 그 기술 기준(ba-20 J N13)
            if (best is not { } b || use.Score > b.Item2.Item4) best = (w, use);
        }
        if (Trace && best is var (bw, bu))
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                $"smart ai {_units[index].ChrCode}: work {bw.Id} → ({bu.Item2},{bu.Item3}) 점수 {bu.Item4}" + Environment.NewLine);
        return best;
    }

    internal static void AppendDifficultyMenu(IntPtr settings)
    {
        IntPtr menu = Win32.CreatePopupMenu();
        for (int i = 0; i < DifficultyChoices.Length; i++)
        {
            var (name, hp, dmg) = DifficultyChoices[i];
            string label = i == 0 ? name : $"{name} — 적 HP {hp}% · 적 피해 {dmg}% · AI 강화";
            Win32.AppendMenuW(menu, Win32.MF_STRING | (UserSettings.Current.Difficulty == i ? Win32.MF_CHECKED : 0u), (nuint)(MenuDifficultyBase + i), label);
        }
        Win32.AppendMenuW(settings, Win32.MF_POPUP, (nuint)menu, "난이도(&D)");
    }

    /// <summary>메뉴에서 난이도를 골랐으면 true.</summary>
    internal bool OnDifficultyMenu(int id)
    {
        if (id < MenuDifficultyBase || id >= MenuDifficultyBase + DifficultyChoices.Length) return false;
        _difficulty = id - MenuDifficultyBase;
        for (int i = 0; i < DifficultyChoices.Length; i++)
            Win32.CheckMenuItem(Win32.GetMenu(_hwnd), (uint)(MenuDifficultyBase + i), Win32.MF_BYCOMMAND | (i == _difficulty ? Win32.MF_CHECKED : Win32.MF_UNCHECKED));
        // 피해 배율과 AI 는 바로, 최대 HP 는 다음 전투부터 바뀐다(싸우는 도중에 적 HP 가 튀지 않게).
        Toast($"난이도: {DifficultyChoices[_difficulty].Name} — 적 최대 HP 는 다음 전투부터 바뀝니다");
        SaveSettings();
        return true;
    }
}

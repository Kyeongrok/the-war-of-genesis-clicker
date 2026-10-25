namespace DuelDx;

using WarOfGenesis.Assets;
using static DuelDx.GameWindow;

/// <summary>
/// 파트1 보조 마법의 실제 효과 — 세기 식(Attack +0x42)으로 가른다(볼트 분석-파트1-기술-b 9절 「Game effects」).
/// (6 힐 · 힐 윈드는 파트2 회복 갈래에서 양만 바꾼다 — <see cref="Part1Rules.HealAmount"/>.) 16 큐어: 상태 · 강화 · 속성 부여를 모두 지운다.
/// 15 블래스(소울 +20 더) · 17 스피디(SPD +8) · 20 배리어(마법 피해 0, 그 밖의 피해 ×2) · 22 쉐도우 실드(AC ×1.5 · MR ×0.5)
/// · 23 블러드 러스트(무기 공격 ×1.5) · 14 아이스 실드(능력치 · 피해에 아무 효과 없음 — 맞을 때 막 27:0 만 번쩍인다)는 <see cref="Part1Rules.Buffs"/> 에 남은 차례로 건다. 21 커즈: 소울 0 + 20% 씩 컨디션이상 · 중독 · 최면.
/// 25 컨디션이상 · 27 최면 · 28 혼란은 파트1 상태(<see cref="Part1Rules.Ailments"/>)로 건다(분석-파트1-전투 25절).
/// </summary>
/// <remarks>
/// 길이 = 적에게 세기 ÷ MR ÷ 10 + (+0x58), 같은 편에게 √세기 ÷ 10 + (+0x58)(원본, 255 에서 자름). 원본은 빈 시간 틱마다 1씩 줄인다
/// (<c>0x10035150</c> → <c>0x10041c00</c>, 상태 3~12 · 22 — 분석-파트1-전투 25절).
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    private static readonly Dictionary<int, string> Part1BuffNames = new()
    {
        [14] = "아이스 실드", [15] = "블래스", [17] = "스피디", [20] = "배리어", [22] = "쉐도우 실드", [23] = "블러드 러스트",
    };

    /// <summary>파트1 보조 마법이면 그 효과를 걸고 true — 파트2 보조 틀(ApplyAilments)은 거치지 않는다.</summary>
    private bool ApplyPart1Support(UnitState a, UnitState t, WorkData w, int result)
    {
        if (!host._scene.IsPart1 || Part1.AttackOf(w) is not { } attack) return false;
        // 희생 · 해체대법 · 도발 — 핸들러가 t0 에 직접 고친다(0x1006c0a0 · 0x1006c8f0 · 0x1006d360, 분석-파트1-기술-b 10절). 소울 칸 +0xe8(최대 150).
        switch (attack.Id)
        {
            case 376 when t != a:                     // 희생 — 같은 편 모두 HP 가득 · 소울 150(시전자는 그대로)
                t.Hp = t.MaxHp;
                t.Soul = Math.Min(t.MaxSoul, 150);
                Popup(t, "희생", HealColor2, 12, rise: 88);
                MarkBuffed(a, t, w);
                return true;
            case 377:                                 // 해체대법 — 대상 HP 1 · 소울 150
                t.Hp = Math.Min(t.Hp, 1);
                t.Soul = Math.Min(t.MaxSoul, 150);
                Popup(t, "해체대법", AilmentPopupColor, 12, rise: 88);
                return true;
            case 382:                                 // 도발 — 소울 −20(0 아래로는 안)
                t.Soul = Math.Max(0, t.Soul - 20);
                Popup(t, "도발", AilmentPopupColor, 12, rise: 88);
                return true;
            case 376:
                return true;
        }
        int sel = attack.Formula;
        // 최면(27) · 혼란(28) · 컨디션이상(25) · 쇼크 웨이브(13) — 명중 굴림 없이 걸림 굴림만 한다(0x1003e3ba → 0x10042290: 대상 거르기 · 0x10041960 · 거는 칸).
        // 못 걸면 「빗나감」 글(0x1011c0f8 「Miss」), 걸리면 맞음 동작(0x10035fa0(15)). 피해는 없다. 원본은 상태 이름 글을 안 띄운다 — 리메이크는 이름을 띄운다.
        // 13 은 기계(칸 10 = 2)에게만 듣는다 — 다른 대상은 거르기에서 떨어져 「Miss」(0x100422a5 결과 3). 거는 값은 세기 ÷ MR ÷ 10 + (+0x58 = 25~45)라
        // 늘 0 보다 커 걸린다(0x10041b80 · 0x1004232b) — 그 값은 줄지 않고 따로 굴려 풀린다(RollPart1Recovery).
        if (sel is 13 or 25 or 27 or 28)
        {
            bool lands = Part1.StatusLands(host._rng, a, t, attack.Id);
            if (lands) LandPart1Ailment(t, Part1Rules.AilmentOf(sel));
            else ShowNumber(t, host._db?.T(42) is { Length: > 0 } m ? m : "Miss", MissColor);
            if (Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"part1 ailment {a.ChrCode} → unit {Array.IndexOf(host._units, t)} chr {t.ChrCode} {attack.Name}(식 {sel}): {(lands ? "걸림" : "안 걸림")} · 지금 [{string.Join(",", Part1.Ailments.GetValueOrDefault(t) ?? [])}]" + Environment.NewLine);
            if (lands) MarkBuffed(a, t, w);
            return true;
        }
        if (sel is not (14 or 15 or 16 or 17 or 20 or 21 or 22 or 23)) return false;
        // 아이스 실드(14 → 상태 10, +0x250) — 걸림 굴림은 0x10041960 갈래 0(표 0x10041b68[4] = 0): 같은 편이면 늘, 적이면 보정 247 이 없고
        // rand()%400 < 세기 ÷ MR 일 때(Part1Rules.StatusLands). 대상 거르기 (4, 1, 0) — 기계 · 큰 유닛 말고 — 는 ApplyWork 가 먼저 본다.
        if (sel == 14 ? !Part1.StatusLands(host._rng, a, t, attack.Id) : result == 3)
        {
            ShowNumber(t, host._db?.T(42) is { Length: > 0 } miss ? miss : "Miss", MissColor);
            return true;
        }
        int power = Part1.Fighter(a) is { } fa ? Part1Combat.Power(sel, fa.AttackerAt(a.Soul)) + attack.PowerAdd * attack.PowerAdd : 0;
        switch (sel)
        {
            case 16:
                t.ClearStatus();
                // 큐어는 집중(상태 22)을 안 지운다(0x10042150 의 목록에 없다).
                if (Part1.Buffs.TryGetValue(t, out var cured))
                {
                    foreach (int k in cured.Keys.Where(k => k != Part1Rules.FocusStatus).ToList()) cured.Remove(k);
                    if (cured.Count == 0) Part1.Buffs.Remove(t);
                }
                Part1.Enchant.Remove(t);
                Part1.CureAilments(t);   // 쇼크(2)는 원본대로 안 풀린다(0x1004223f)
                Popup(t, "큐어", HealColor2, 12, rise: 88);
                break;
            case 21:
                // 커즈(0x10042375) — 소울 0, 그다음 rand()%100 이 0~19 면 컨디션이상 · 20~39 면 중독(상태 13, 파트2 상태 3 으로) · 40~59 면 최면.
                t.Soul = 0;
                Popup(t, "커즈", AilmentPopupColor, 12, rise: 88);
                switch (host._rng.Next(32768) % 100)
                {
                    case < 20: LandPart1Ailment(t, Part1Rules.Condition); break;
                    case < 40: PutAilment(t, 3, 5, [], a); break;
                    case < 60: LandPart1Ailment(t, Part1Rules.Hypnosis); break;
                }
                break;
            default:
                bool ally = !Hostile(a, t);
                int mr = Part1.Fighter(t)?.Stat(Part1Chr.Mr, t.Soul) ?? 1;
                int length = Math.Min(255, (ally ? (int)Math.Sqrt(power) / 10 : power / Math.Max(1, mr) / 10) + (attack.Raw.GetValueOrDefault(0x58) & 0xff));
                if (!Part1.Buffs.TryGetValue(t, out var buffs)) Part1.Buffs[t] = buffs = [];
                buffs[sel] = Math.Max(buffs.GetValueOrDefault(sel), Math.Max(10, length));
                Popup(t, Part1BuffNames[sel], ally ? HealColor2 : AilmentPopupColor, 12, rise: 88);
                break;
        }
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 support {a.ChrCode} → {t.ChrCode} {attack.Name}(식 {sel}): 세기 {power}{(Part1.Buffs.GetValueOrDefault(t)?.GetValueOrDefault(sel) is > 0 and var left ? $" · 길이 {left}" : "")} HP {t.Hp}/{t.MaxHp}" + Environment.NewLine);
        MarkBuffed(a, t, w);
        host.StatusScr.RefreshUnitStats(t);
        return true;
    }

    private static readonly Dictionary<int, string> Part1AilmentNames = new()
    {
        [Part1Rules.Shock] = "쇼크", [Part1Rules.Hypnosis] = "최면", [Part1Rules.Condition] = "컨디션 이상", [Part1Rules.Confusion] = "혼란",
    };

    /// <summary>체력바 아래 딱지 글(두 글자).</summary>
    internal static string Part1AilmentChip(int status) => status switch { Part1Rules.Shock => "쇼크", Part1Rules.Hypnosis => "최면", Part1Rules.Confusion => "혼란", _ => "컨디" };

    /// <summary>
    /// 파트1 상태 14 · 15 · 16 을 건다 — <see cref="Part1Rules.AddAilment"/>(최면은 15 · 16 을, 혼란은 15 를 지운다). 맞음 동작과 이름 글.
    /// 혼란이 걸린 내 인물은 풀릴 때까지 AI 가 움직인다(<see cref="IsMine"/>), 최면이 걸린 사람은 차례를 못 받는다(<see cref="CanTakeTurn"/>).
    /// </summary>
    internal void LandPart1Ailment(UnitState t, int status)
    {
        Part1.AddAilment(t, status);
        PlayHitReaction(t, damaged: false);
        Popup(t, Part1AilmentNames[status], AilmentPopupColor, 12, rise: 88);
        host.StatusScr.RefreshUnitStats(t);
    }

    /// <summary>
    /// 빈 시간 틱마다 — 파트1 상태 15 · 14 · 16 의 풀림 굴림(<c>0x10035150</c> 의 첫 고리, 차례 [15, 13, 14, 16]). 원본은 이 셋을 줄여 가지 않는다.
    /// 쇼크(2)는 그 뒤(<c>0x100353a1</c>)에 틱 % 행동 간격 == 1 일 때만 굴린다(<see cref="Part1Rules.ShockRecovers"/>).
    /// </summary>
    internal void RollPart1Recovery(UnitState u, int interval)
    {
        RollPart1Ailments(u);
        if (!Part1.HasAilment(u, Part1Rules.Shock) || _tick == 1 || _tick % Math.Max(1, interval) != 1) return;
        int roll = _ailmentRandom.Next(32768);
        bool off = Part1Rules.ShockRecovers(u.Soul, roll);
        if (off) Part1.ClearAilment(u, Part1Rules.Shock);
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 shock unit {Array.IndexOf(host._units, u)} chr {u.ChrCode} 풀림 굴림 (틱 {_tick}, 간격 {interval}, 소울 {u.Soul}): {roll % 150} ≤ {(u.Soul + 150) / 5} → {(off ? "풀림" : "그대로")}" + Environment.NewLine);
        if (off) host.StatusScr.RefreshUnitStats(u);
    }

    private void RollPart1Ailments(UnitState u)
    {
        if (!Part1.Ailments.TryGetValue(u, out var set)) return;
        foreach (int s in (ReadOnlySpan<int>)[Part1Rules.Condition, Part1Rules.Hypnosis, Part1Rules.Confusion])
        {
            if (!set.Contains(s) || !Part1Rules.RecoveryRoll(s, u.Hp, u.MaxHp, u.Soul, _ailmentRandom.Next(32768))) continue;
            Part1.ClearAilment(u, s);
            if (Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"part1 ailment {u.ChrCode} 풀림 {Part1AilmentNames[s]} (틱 {_tick}, HP {u.Hp}/{u.MaxHp}, 소울 {u.Soul})" + Environment.NewLine);
        }
    }

    /// <summary>차례가 올 때 소울을 더한 뒤 — 중독(상태 13, 리메이크는 파트2 상태 3)이면 120, 컨디션이상이면 50 에서 자른다(<c>0x10034f80</c>).</summary>
    private void CapPart1Soul(UnitState u)
    {
        if (u.HasStatus(3)) u.Soul = Math.Min(u.Soul, 120);
        if (Part1.HasAilment(u, Part1Rules.Condition)) u.Soul = Math.Min(u.Soul, 50);
    }

    /// <summary>
    /// 머리 위 표지(<c>0x10041c60</c>, 상태가 지워지면 스스로 사라진다 — <c>0x10083a70</c> · <c>0x10083bd0</c> · <c>0x10083cb0</c>):
    /// 컨디션이상 = Obs 181 모션 0 하나(자리 0, 30, 높이 120) · 최면 = Obs 180 모션 5 다섯 · 혼란 = Obs 180 모션 6 + rand%4 다섯 —
    /// 다섯은 높이 100 에서 반지름 18 원(8r = 144 점)을 틱마다 5 점씩 돈다(원 따라가기 <c>+0x110(−1, 5, …, 18)</c>), 6 틱씩 늦게 나선다.
    /// </summary>
    internal void DrawPart1AilmentMarks()
    {
        if (!host._scene.IsPart1) return;
        int tick = (int)(host._lastTime * TicksPerSecond);
        DrawPart1BuffMarks(tick);
        if (Part1.Ailments.Count == 0) return;
        foreach (var (u, set) in Part1.Ailments)
        {
            if (!u.Alive || !u.OnField) continue;
            var (fx, fy) = host.Btl.UnitFoot(u);
            if (set.Contains(Part1Rules.Condition))
                host.DrawUi(Part1Game.ObsBase + 181, 0, tick, fx, fy + (int)(30 * 0.8 - 120 * 0.6), UiBlend.Alpha);
            bool hypnosis = set.Contains(Part1Rules.Hypnosis);
            if (!hypnosis && !set.Contains(Part1Rules.Confusion)) continue;
            for (int k = 0; k < 5; k++)
            {
                double angle = (tick - 6 * k) * 5 / 144.0 * 2 * Math.PI;
                int x = fx + (int)Math.Round(18 * Math.Sin(angle)), y = fy - (int)(100 * 0.6) - (int)Math.Round(18 * 0.8 * Math.Cos(angle));
                int motion = hypnosis ? 5 : 6 + (int)((uint)(u.ChrCode * 7 + k * 13) % 4);
                host.DrawUi(Part1Game.ObsBase + 180, motion, tick, x, y, UiBlend.Alpha);
            }
        }
    }

    /// <summary>
    /// 강화 표지 — 유닛 오른쪽 아래에 붙어 따라다니는 Obs 180 의 작은 그림(<c>0x10041c60</c> 갈래 6 · 7 · 8 · 12, 걸 때와 유닛을 만들 때
    /// <c>0x1003446a~</c>). 모두 자리 (x + 12, y + 19) 이고 높이만 달라 겹쳐 걸리면 세로로 쌓인다:
    /// 블래스(상태 6, 클래스 <c>0x10083950</c>) 모션 3 · 높이 40 / 스피디(7, <c>0x10083870</c>) 모션 0 · 60 /
    /// 버써크(8, <c>0x100838e0</c>) 모션 1 · 80 / 블러드 러스트(12, <c>0x100839c0</c>) 모션 2 · 100.
    /// 틱마다 상태 칸(<c>+0x24c</c> · <c>+0x24d</c> · <c>+0x24e</c> · <c>+0x252</c>)이 0 이면 스스로 지운다.
    /// 배리어(9) · 쉐도우 실드(11) · 아이스 실드(10)는 늘 붙은 표지가 없고(갈래 9 · 10 · 11 = Obs 24 · 27 · 14 를 z 40 에 한 번 — <c>0x10041e47</c> ·
    /// <c>0x10041eab</c> · <c>0x10041e79</c> → <c>0x10085e00</c>), 걸 때(<c>0x10042570</c> 갈래 7 = 아무것도)가 아니라 맞을 때만 부른다
    /// (<c>0x1003e249~</c> — <see cref="FlashPart1Shield"/>).
    /// </summary>
    private void DrawPart1BuffMarks(int tick)
    {
        foreach (var u in host._units)
        {
            if (!u.Alive || !u.OnField) continue;
            bool berserk = u.Status(4) > 0;                       // 리메이크는 파트1 버써크를 파트2 상태 4 로 건다
            if (!berserk && !Part1.Buffs.ContainsKey(u)) continue;
            var (fx, fy) = host.Btl.UnitFoot(u);
            foreach (var (on, motion, height) in (ReadOnlySpan<(bool, int, int)>)
                     [(Part1.Has(u, 15), 3, 40), (Part1.Has(u, 17), 0, 60), (berserk, 1, 80), (Part1.Has(u, 23), 2, 100)])
                if (on)
                    host.DrawUi(Part1Game.ObsBase + 180, motion, tick, fx + 12, fy + (int)(19 * 0.8 - height * 0.6), UiBlend.Alpha);
        }
    }

    /// <summary>
    /// 집중(Attack 378, <c>0x1006cad0</c>) — 대상이 판에 있으면 상태 22(+0x25c) := 40(<c>0x1006cbd7</c>, 늘 덮어쓴다). 피해 · 굴림 · 글은 없다.
    /// </summary>
    internal void ApplyPart1Focus(UnitState a, UnitState t)
    {
        if (!t.Alive || !t.OnField) return;
        if (!Part1.Buffs.TryGetValue(t, out var buffs)) Part1.Buffs[t] = buffs = [];
        buffs[Part1Rules.FocusStatus] = 40;
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 support {a.ChrCode} → {t.ChrCode} 집중: 상태 22 = 40 (사거리 높이 차 무시)" + Environment.NewLine);
        host.StatusScr.RefreshUnitStats(t);
    }

    /// <summary>
    /// 기도(Attack 375, <c>0x1006bbe0</c>)의 덧기술 — 다음 명령 칸에 넣은 공격(198 썬 라이트 · 193 힐 윈드5)을 행동 끝에 앞머리 없이 공짜로 쏜다
    /// (무기 덧기술과 같은 <see cref="_part1Proc"/> 길). 그 work 가 없으면 아무것도 안 한다.
    /// </summary>
    internal void QueuePart1FollowUp(UnitState a, int attackId)
    {
        var w = Work(Part1Game.ChrBase + attackId);
        if (w != null) _part1Proc = (Array.IndexOf(host._units, a), w, a.Col, a.Row);
        if (Trace)
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                $"part1 follow-up {a.ChrCode}: Attack {attackId} → work {(w != null ? w.Id.ToString() : "없음")}" + Environment.NewLine);
    }

    /// <summary>시간 틱마다 파트1 강화의 남은 길이를 1 줄인다(원본 <c>0x10041c00</c>).</summary>
    private void TickPart1Buffs(UnitState u)
    {
        if (!Part1.Buffs.TryGetValue(u, out var buffs)) return;
        foreach (int sel in buffs.Keys.ToList())
            if (--buffs[sel] <= 0) buffs.Remove(sel);
        if (buffs.Count == 0) Part1.Buffs.Remove(u);
    }
}

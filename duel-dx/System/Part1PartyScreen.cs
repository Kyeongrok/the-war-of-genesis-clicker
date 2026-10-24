using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 일행 — 일행 명단 · 고른 인물의 능력치와 장비 다섯 칸. 칸을 누르면 가방(<see cref="Part1Progress.Bag"/>)에서 그 칸에 맞는 것을 골라 바꿔 낀다.
/// </summary>
/// <remarks>
/// 장비 칸(인물 레코드 39~43): 0 무기 · 1 둘째 손(칼 · 방패 · 스코프) · 2 몸 · 3·4 장신구(분석-파트1-전투 9.1 — 살라딘 [다마스커스, 다마스커스, 방탄복]).
/// 낄 수 있나는 원본 규칙(<see cref="Part1Item.CanEquip"/> — 무기 갈래마다 다루는 어빌리티 · 칸마다 받는 갈래)대로.
/// </remarks>
internal sealed class Part1PartyScreen(GameWindow host, Part1TitleScreen shell)
{
    internal bool _open;
    private Action? _onLeave;
    private int _member;
    private int _slot = -1;
    private int _hover = -1;

    internal const int ListX = 24, ListY = 70, RowW = 170, RowH = 24, InfoX = 210, SlotY = 230, BagX = 420, ButtonHit = 1000, SlotHit = 2000, BagHit = 3000, RideHit = 4000;

    internal static readonly string[] SlotNames = ["무기", "둘째 손", "갑옷", "장신구", "장신구"];

    internal void Open(Action onLeave, int member = 0)
    {
        _onLeave = onLeave;
        _member = member;
        _slot = -1;
        _hover = -1;
        _open = true;
    }

    /// <summary>지금 무리의 사람(무리가 없으면 일행).</summary>
    private List<int> Members => host.Part1.GroupMembers();

    private Part1Chr? Member(int i) =>
        i >= 0 && i < Members.Count && Part1Chr.Load(Members[i] - Part1Game.ChrBase) is { } raw ? host.Part1.Apply(raw) : null;

    private List<int> BagFor(Part1Chr chr, int slot)
    {
        var items = Part1Item.LoadAll();
        return [.. host.Part1.Bag.Distinct().Where(id => Part1Item.ById(items, id) is { } item && item.CanEquip(chr, slot, chr.Equipment, items))];
    }

    private int HitAt(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        int x = bx - ox, y = by - oy;
        if (x >= 250 && x < 250 + Part1TitleScreen.ButtonW && y >= 446 && y < 446 + Part1TitleScreen.ButtonH) return ButtonHit;
        for (int i = 0; i < Members.Count; i++)
            if (x >= ListX && x < ListX + RowW && y >= ListY + i * (RowH + 2) && y < ListY + i * (RowH + 2) + RowH) return i;
        for (int s = 0; s < 5; s++)
            if (x >= InfoX && x < InfoX + 200 && y >= SlotY + s * RowH && y < SlotY + (s + 1) * RowH - 2) return SlotHit + s;
        if (x >= InfoX && x < InfoX + 200 && y >= RideY && y < RideY + RowH - 2) return RideHit;
        if (_slot >= 0 && Member(_member) is { } chr)
        {
            int count = BagFor(chr, _slot).Count + 1;                       // 맨 끝 줄은 「빼기」
            for (int i = 0; i < count; i++)
                if (x >= BagX && x < BagX + 196 && y >= SlotY + i * RowH && y < SlotY + (i + 1) * RowH - 2) return BagHit + i;
        }
        return -1;
    }

    internal void UpdateHover(int bx, int by) => _hover = HitAt(bx, by);

    internal void OnClick(int bx, int by)
    {
        int hit = HitAt(bx, by);
        if (hit == ButtonHit) { Leave(); return; }
        if (hit == RideHit) { NextMount(); return; }
        if (hit >= BagHit) { Equip(hit - BagHit); return; }
        if (hit >= SlotHit) { _slot = hit - SlotHit; return; }
        if (hit >= 0 && hit < Members.Count) { _member = hit; _slot = -1; }
    }

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE) { if (_slot >= 0) _slot = -1; else Leave(); }
    }

    private void Leave()
    {
        var onLeave = _onLeave;
        _onLeave = null;
        host.LeaveScene(() => { _open = false; onLeave?.Invoke(); });
    }

    /// <summary>탑승 줄 — 장비 다섯 칸 아래.</summary>
    private const int RideY = SlotY + 5 * RowH + 6;

    /// <summary>이 사람이 탈 수 있고 남은 것이 있는 탈것(20000 + 파트1 번호) — 다른 사람이 정한 만큼은 뺀다.</summary>
    private List<int> MountsFor(Part1Chr chr)
    {
        var items = Part1Item.LoadAll();
        var equipment = chr.Equipment.Select(id => Part1Item.ById(items, id)).ToList();
        int code = Part1Game.ChrBase + chr.Code;
        return [.. host.Part1.Mounts.Where(m => m.Value - host.Part1.Riding.Count(r => r.Key != code && r.Value == m.Key) > 0
                                               && chr.CanRide(m.Key - Part1Game.ChrBase, equipment)).Select(m => m.Key).OrderBy(m => m)];
    }

    /// <summary>탑승 줄을 누르면 — 안 탐 → 탈 수 있는 탈것 차례로 → 안 탐.</summary>
    private void NextMount()
    {
        if (Member(_member) is not { } chr) return;
        int code = Part1Game.ChrBase + chr.Code;
        var options = MountsFor(chr);
        if (options.Count == 0) { host.Toast("탈 수 있는 탈것이 없다(탑승 어빌리티 · 남은 탈것)"); return; }
        int now = host.Part1.Riding.TryGetValue(code, out int m) ? options.IndexOf(m) : -1;
        if (now + 1 < options.Count) host.Part1.Riding[code] = options[now + 1];
        else host.Part1.Riding.Remove(code);
        shell.Chapter.SaveProgress();
    }

    /// <summary>가방의 i 번째 맞는 것을 그 칸에 낀다(끼던 것은 가방으로). 맨 끝 줄은 칸을 비운다.</summary>
    private void Equip(int i)
    {
        if (Member(_member) is not { } chr || _slot < 0) return;
        var options = BagFor(chr, _slot);
        int code = Part1Game.ChrBase + chr.Code;
        var slots = (int[])chr.Equipment.Clone();
        int old = slots[_slot];
        int next = i < options.Count ? options[i] : 0;
        if (next == old) return;
        if (next != 0) host.Part1.Bag.Remove(next);
        if (old != 0) host.Part1.Bag.Add(old);
        slots[_slot] = next;
        host.Part1.Equipped[code] = slots;
        host._db?.ForgetPart1Characters();
        host.Btl.Part1.Forget(code);
        shell.Chapter.SaveProgress();
    }

    internal void Draw()
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        host.UiFill(ox, oy, MosesScene.MosesW, MosesScene.MosesH, 0xFF141A24);
        host.UiText("일행", ox + 24, oy + 18, GameWindow.White, 18);
        string eld = $"eld {host.Part1.Eld}";
        host.UiText(eld, ox + 616 - host.UiGetText(eld, 0xFFFFE080, 13).W, oy + 22, 0xFFFFE080, 13);
        var items = Part1Item.LoadAll();
        for (int i = 0; i < Members.Count; i++)
        {
            int y = oy + ListY + i * (RowH + 2);
            string name = Member(i)?.Name ?? $"Chr {Members[i]}";
            host.UiFill(ox + ListX, y, RowW, RowH, i == _member ? 0xF0305080u : _hover == i ? 0xE0283850u : 0xC0202838u);
            host.UiText(name, ox + ListX + 8, y + 4, GameWindow.White, 12);
        }
        if (Member(_member) is { } chr)
        {
            host.UiText($"{chr.Name}  Lv {chr.Level}  EXP {chr.Experience}  HP {chr.MaxHp}", ox + InfoX, oy + ListY, 0xFFFFE080, 13);
            int[] stats = [Part1Chr.Str, Part1Chr.Skill, Part1Chr.Dex, Part1Chr.Int, Part1Chr.Luck, Part1Chr.Spd, Part1Chr.Ac, Part1Chr.Mr];
            string[] labels = ["STR", "SKILL", "DEX", "INT", "LUCK", "SPD", "AC", "MR"];
            var fighter = Part1Fighter.From(chr, items);
            for (int k = 0; k < stats.Length; k++)
                host.UiText($"{labels[k]} {fighter.Stat(stats[k])}", ox + InfoX + k % 4 * 100, oy + ListY + 30 + k / 4 * 22, GameWindow.White, 12);
            host.UiText("장비 — 칸을 누르면 가방에서 고른다", ox + InfoX, oy + SlotY - 24, 0xFFB0C0D8, 12);
            for (int s = 0; s < 5; s++)
            {
                int y = oy + SlotY + s * RowH;
                string item = Part1Item.ById(items, chr.Equipment[s])?.Name ?? "—";
                host.UiFill(ox + InfoX, y, 200, RowH - 2, s == _slot ? 0xF0305080u : _hover == SlotHit + s ? 0xE0283850u : 0xC0202838u);
                host.UiText($"{SlotNames[s]}: {item}", ox + InfoX + 8, y + 4, GameWindow.White, 12);
            }
            {
                int code = Part1Game.ChrBase + chr.Code;
                string ride = host.Part1.Riding.TryGetValue(code, out int mount) && Part1Chr.Load(mount - Part1Game.ChrBase) is { } m
                    ? m.Name : "—";
                host.UiFill(ox + InfoX, oy + RideY, 200, RowH - 2, _hover == RideHit ? 0xE0283850u : 0xC0202838u);
                host.UiText($"탑승: {ride}", ox + InfoX + 8, oy + RideY + 4, GameWindow.White, 12);
            }
            if (_slot >= 0)
            {
                var options = BagFor(chr, _slot);
                for (int i = 0; i <= options.Count; i++)
                {
                    int y = oy + SlotY + i * RowH;
                    string label = i < options.Count ? $"{Part1Item.ById(items, options[i])?.Name} ×{host.Part1.Bag.Count(b => b == options[i])}" : "(빼기)";
                    host.UiFill(ox + BagX, y, 196, RowH - 2, _hover == BagHit + i ? 0xF0305080u : 0xC0202838u);
                    host.UiText(label, ox + BagX + 8, y + 4, GameWindow.White, 12);
                }
                if (options.Count == 0) host.UiText("가방에 맞는 것이 없다", ox + BagX, oy + SlotY - 24, 0xFFB0C0D8, 12);
            }
        }
        shell.DrawButton(ox + 250, oy + 446, Part1TitleScreen.ButtonW, Part1TitleScreen.ButtonH, "나가기", _hover == ButtonHit);
        host.DrawToast();
    }
}

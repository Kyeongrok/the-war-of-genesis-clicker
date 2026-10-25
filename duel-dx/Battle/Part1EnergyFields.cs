using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 파트1 에너지 필드(공격 152~160, 핸들러 <c>0x1004d850</c>)가 칸마다 남기는 기운 — 원본 클래스 <c>0x1007c5f0</c>(vt <c>0x10105a04</c>, 종류 6, Obs 81).
/// </summary>
/// <remarks>
/// <para>핸들러는 범위 칸마다 타이머 → 기운 81:6 → 이 기운(모션 r = rand%3) → 흩어지는 81:r+3 을 사슬로 잇고, 기운에 길이 40 을 준다
/// (<c>vt+0xfc(40, 0)</c> → <c>0x1007b8f0</c> 이 +0x144 에 넣는다). 길이는 <b>시간 틱</b>(<c>[게임+0xc9bc]</c>, 아무도 움직일 수 없을 때 오르는 것 —
/// <c>0x10030086</c>)으로 잰다: 첫 장에 그때 틱을 +0x140 에 적고(<c>0x1007b910</c>), 틱 ≥ +0x140 + 40 이 되면 끝난다(<c>0x1007b940</c>).</para>
/// <para>시간 틱이 오를 때마다 판의 물체에 vt+0x54 를 돌리는데(<c>0x10030097</c> → <c>0x10099fb0</c>), 이 기운의 것(<c>0x1007c8b0</c>)은
/// 제 칸의 유닛(<c>0x1008f7c0</c> · <c>0x1008f390</c>)마다 메시지 14 를 보낸다. 첫 장에도 한 번 돌린다(<c>0x1007c7ee</c>).
/// 메시지 14(<c>0x1003561d</c>): 종족(+0xb8)이 2(기계)이고 쇼크(+0x248)가 아니면 rand()%50 == 0 일 때 상태 2(쇼크)를 건다(<c>0x10042570(2, 1)</c>).
/// 풀림은 <see cref="Part1Rules.ShockRecovers"/> 그대로다.</para>
/// <para>그리기: 기운이 사는 동안 81:r 을 되풀이하고, 끝나면 81:r+3 을 한 번 그린다. 원본은 사는 동안 장마다 1/1000 로 81:3~5 반짝임을 더 띄운다 — 옮기지 않았다.</para>
/// </remarks>
internal sealed class Part1EnergyFields(GameWindow host)
{
    /// <summary>기운의 길이(시간 틱) — 핸들러 <c>0x1004dae2</c> 의 push 0x28.</summary>
    internal const int Life = 40;

    private sealed class Field
    {
        internal int Col, Row, Motion, Start;
        internal double DrawFrom, EndedAt = -1;
    }

    private readonly List<Field> _fields = [];

    internal void Clear() => _fields.Clear();

    internal int Count => _fields.Count(f => f.EndedAt < 0);

    /// <summary>기운 하나를 그 칸에 깐다 — 깔린 장에 한 번 굴린다(<c>0x1007c7ee</c>).</summary>
    internal void Add(int col, int row, int motion)
    {
        var f = new Field { Col = col, Row = row, Motion = motion, Start = host.Btl._tick, DrawFrom = host._lastTime };
        _fields.Add(f);
        Roll(f);
    }

    /// <summary>
    /// 저장 — 산 기운마다 (열, 줄, 모션 r, 깐 지 몇 시간 틱). 원본은 맵 층 저장이 기술 152~160 의 기운을 싣는다
    /// (<c>0x100901c0</c> 의 0x98~0xa0 갈래 → <c>0x1007c660</c>, 바탕 <c>0x1007b7d0</c>: 깐 틱 +0x140 · 길이 +0x144).
    /// </summary>
    internal int[]? Save()
    {
        var live = _fields.Where(f => f.EndedAt < 0).ToList();
        return live.Count == 0 ? null : [.. live.SelectMany(f => new[] { f.Col, f.Row, f.Motion, host.Btl._tick - f.Start })];
    }

    /// <summary>되살리기 — <paramref name="tick"/> = 불러온 시간 틱. 깔 때의 한 번 굴림은 다시 안 한다. 옛 세이브(null)면 비운다.</summary>
    internal void Restore(int[]? saved, int tick)
    {
        _fields.Clear();
        if (saved == null || saved.Length % 4 != 0) return;
        for (int at = 0; at < saved.Length; at += 4)
            _fields.Add(new Field { Col = saved[at], Row = saved[at + 1], Motion = saved[at + 2], Start = tick - saved[at + 3], DrawFrom = host._lastTime });
    }

    /// <summary>시간 틱이 올랐을 때(<see cref="BattleScene.AdvanceTick"/>) — 산 기운마다 제 칸을 굴리고, 길이가 다 된 것은 끝낸다.</summary>
    internal void Step()
    {
        foreach (var f in _fields)
        {
            if (f.EndedAt >= 0) continue;
            Roll(f);
            if (host.Btl._tick >= f.Start + Life) f.EndedAt = host._lastTime;
        }
    }

    private void Roll(Field f)
    {
        var btl = host.Btl;
        var rules = btl.Part1;
        foreach (var u in host._units)
        {
            if (u is not { Alive: true, OnField: true } || u.Col != f.Col || u.Row != f.Row) continue;
            if (!rules.IsMachine(u) || rules.HasAilment(u, Part1Rules.Shock)) continue;
            int roll = btl._ailmentRandom.Next(32768);
            bool lands = roll % 50 == 0;
            if (BattleScene.Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"part1 energy field ({f.Col},{f.Row}) 틱 {btl._tick}(깐 틱 {f.Start}) → unit {Array.IndexOf(host._units, u)} chr {u.ChrCode}: {roll % 50} → {(lands ? "쇼크" : "그대로")}" + Environment.NewLine);
            if (!lands) continue;
            rules.AddAilment(u, Part1Rules.Shock);
            btl.Popup(u, "쇼크", BattleScene.AilmentPopupColor, 12, rise: 88);
            host.StatusScr.RefreshUnitStats(u);
        }
    }

    /// <summary>판에 그린다 — 물체와 함께, 인물보다 먼저.</summary>
    internal void Draw()
    {
        int id = Part1Game.ObsBase + 81;
        if (_fields.Count == 0 || host.UiFor(id) is not { } sprite) return;
        _fields.RemoveAll(f =>
        {
            int x = f.Col * TileW + TileW / 2, y = host.CellCenterY(f.Col, f.Row);
            if (f.EndedAt < 0)
            {
                int tick = (int)((host._lastTime - f.DrawFrom) * TicksPerSecond);
                int key = sprite.BlendAt(f.Motion, tick % Math.Max(1, sprite.MotionLength(f.Motion)));
                host.DrawUi(id, f.Motion, tick, x, y, BlendOf(key), loop: true, fade: BlendFade(key));
                return false;
            }
            int age = (int)((host._lastTime - f.EndedAt) * TicksPerSecond);
            if (age >= Math.Max(1, sprite.MotionLength(f.Motion + 3))) return true;
            int fadeKey = sprite.BlendAt(f.Motion + 3, age);
            host.DrawUi(id, f.Motion + 3, age, x, y, BlendOf(fadeKey), loop: false, fade: BlendFade(fadeKey));
            return false;
        });
    }
}

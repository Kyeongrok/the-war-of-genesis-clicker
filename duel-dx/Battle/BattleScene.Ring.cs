using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 링 커맨드 — 원본 그림(Obs)·움직임·소리 그대로(옵시디안 분석-링커맨드 "ui-1").
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>항목 번호 i 의 기본 각 θ₀ = 2π(6−i)/6 − π/2, 반지름 66. 위부터 시계 반대로 공격·어빌·아이템·시스템·상태·휴식(<c>0x100dff30</c>).</item>
/// <item>중심 = 유닛 자리에서 40 픽셀 위, 판 가장자리에서 89 픽셀 안쪽.</item>
/// <item>열기 10틱: r = (35−3n)(10−n)+66, θ = θ₀ − (10−n)π/10 · 다시 열기 8틱: r = 12n−30, θ = θ₀ − (8−n)π/8 ·
///   고름 8틱: r = 66−12n, θ = θ₀ − nπ/8 · 취소 10틱: r = 3n²+5n+66, θ = θ₀ − nπ/10. 투명도 변화는 없다.</item>
/// <item>그림: 아이콘 Obs 0087~0092 모션 1(숨쉬기), 테두리 0086 모션 1(열기 7틱째부터), 바탕 0106 모션 0(땅을 11/31 로 어둡게),
///   마우스 올린 빛 0105 모션 0(아이콘 중심 −(1,1)), 이름 글자 0452(아이콘 중심 + (−23, +7)). 아이콘·테두리·빛은 더하기 섞기.</item>
/// <item>소리: 65 마우스 올림, 67 열기, 68 고름, 69 취소.</item>
/// <item>꺼진 항목을 어둡게 그리는 원본 섞기(색조 2, 세기 16)는 모양을 몰라 아이콘을 40% 밝기로 더한다.</item>
/// </list>
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    internal enum RingCommand { Attack, Ability, Item, System, Status, Rest }

    internal static readonly (RingCommand Command, int IconObs, int LabelMotion, string Hover)[] RingItems =
    [
        (RingCommand.Attack, 92, 1, "ATTACK"),
        (RingCommand.Ability, 87, 0, "ABILITY"),
        (RingCommand.Item, 89, 4, "ITEM"),
        (RingCommand.System, 90, 3, "SYSTEM"),
        (RingCommand.Status, 88, 5, "STATUS"),
        (RingCommand.Rest, 91, 2, "REST"),
    ];

    internal enum RingPhase { Opening, Reopening, Idle, Picking, Cancelling }

    internal const int RingRadius = 66, RingLift = 40, RingEdge = 89, RingHitRadius = 18;
    internal const int SoundHover = 65, SoundOpen = 67, SoundPick = 68, SoundCancel = 69;

    internal int _ringUnit = -1;
    internal int _ringHover = -1;
    internal RingPhase _ringPhase;
    internal double _ringPhaseStart, _ringOpenedAt, _ringHoverAt;
    internal int _ringPicked = -1;


    /// <summary>링 그림(assets/ui)·소리(assets/sounds)를 읽는다. 없으면 글자 링으로 그린다.</summary>
    internal void LoadRingAssets()
    {
        try
        {
            lock (host._ui)
                foreach (string folder in new[] { "ui", "effects", Path.Combine("moses", "obs") })
                    foreach (string path in Directory.EnumerateFiles(AssetsFolder.Find(folder), "*.obs"))
                        if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int id))
                            host._uiPaths[id] = path;

            // 링 그림만 미리 풀어 둔다 — 나머지(이펙트·무기 층)는 처음 쓸 때 푼다.
            foreach (int id in RingItems.Select(r => r.IconObs).Concat([86, 105, 106, 452])) host.UiFor(id);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            host._loadError = $"링 그림: {ex.Message}";
        }
    }


    internal void PlaySound(int id) => host.Play(id);

    internal int RingTick(double since) => (int)((host._lastTime - since) * TicksPerSecond);

    /// <summary>링 중심 — 유닛 자리에서 40픽셀 위, 판 가장자리에서 89픽셀 안쪽.</summary>
    internal (int X, int Y) RingCenter(UnitState unit)
    {
        var (fx, fy) = host.Btl.UnitFoot(unit);
        return (Math.Clamp(fx, host._camX + RingEdge, host._camX + host.ViewWidth - RingEdge), Math.Clamp(fy - RingLift, host._camY + GridTop + RingEdge, host._camY + host.ViewHeight - RingEdge));
    }

    /// <summary>지금 단계·틱에서 i 번 항목의 중심(링 중심 기준).</summary>
    internal (int X, int Y) RingItemOffset(int i)
    {
        double baseAngle = 2 * Math.PI * (6 - i) / 6 - Math.PI / 2;
        int n = RingTick(_ringPhaseStart) + 1;
        double r, a;
        switch (_ringPhase)
        {
            case RingPhase.Opening: n = Math.Min(n, 10); r = (35 - 3 * n) * (10 - n) + 66; a = baseAngle - (10 - n) * Math.PI / 10; break;
            case RingPhase.Reopening: n = Math.Min(n, 8); r = 12 * n - 30; a = baseAngle - (8 - n) * Math.PI / 8; break;
            case RingPhase.Picking: n = Math.Min(n, 8); r = 66 - 12 * n; a = baseAngle - n * Math.PI / 8; break;
            case RingPhase.Cancelling: n = Math.Min(n, 10); r = 3 * n * n + 5 * n + 66; a = baseAngle - n * Math.PI / 10; break;
            default: r = RingRadius; a = baseAngle; break;
        }
        return ((int)(Math.Cos(a) * r), (int)(Math.Sin(a) * r));
    }

    internal int RingItemAt(int bx, int by)
    {
        if (_ringUnit < 0 || _ringPhase != RingPhase.Idle) return -1;
        var (cx, cy) = RingCenter(host._units[_ringUnit]);
        for (int i = 0; i < RingItems.Length; i++)
        {
            var (ox, oy) = RingItemOffset(i);
            int dx = bx - cx - ox, dy = by - cy - oy;
            if (dx * dx + dy * dy <= RingHitRadius * RingHitRadius) return i;
        }
        return -1;
    }

    internal void OnRingMouseMove(int bx, int by)
    {
        int hover = RingItemAt(bx, by);
        if (hover == _ringHover) return;
        _ringHover = hover;
        _ringHoverAt = host._lastTime;
        if (hover >= 0) PlaySound(SoundHover);
    }

    internal bool RingItemEnabled(RingCommand command, int ringUnit)
    {
        var unit = host._units[ringUnit];
        if (command is RingCommand.Status or RingCommand.System) return true;
        if (!IsPlayerTurn || ringUnit != _turn || unit.IsBusy) return false;
        // 원본 0x100e16b0 — Attack 은 늘 켜져 있고, Ability·Item 은 TP+CTP 가 문턱(Num 4) 아래면 꺼진다. Ability 는 상태이상 12(봉인)로도 꺼진다.
        // 전에는 거꾸로 Attack·Ability 를 끄고 Item 은 안 껐다.
        // 문턱은 +0x4da = TP − (기준 칸 → 지금 칸 걸음 비용)을 본다 — 플레이어 걷기(+0xa8 = 0)도 이 값을 고쳐 적는다(0x10076437, ba-20 M3).
        int walked = ComputeRange(unit) is { } walkRange && walkRange.CanReach(unit.Row * host.Cols + unit.Col) ? walkRange.Cost[unit.Row * host.Cols + unit.Col] : 0;
        bool enoughTp = host._db == null || unit.Tp - walked + unit.Ctp >= host._db.N(4);
        return command switch
        {
            RingCommand.Ability => enoughTp && !unit.HasStatus(12),
            RingCommand.Item => enoughTp,
            _ => true,
        };
    }

    internal void OpenRing(int unit, bool reopen = false)
    {
        _ringUnit = unit;
        _ringPhase = reopen ? RingPhase.Reopening : RingPhase.Opening;
        _ringPhaseStart = _ringOpenedAt = host._lastTime;
        _ringHover = -1;
        _ringPicked = -1;
        PlaySound(SoundOpen);
    }

    /// <summary>링을 취소 움직임으로 닫는다(이미 닫히는 중이면 그대로).</summary>
    internal void CancelRing()
    {
        if (_ringUnit < 0 || _ringPhase is RingPhase.Picking or RingPhase.Cancelling) return;
        _ringPhase = RingPhase.Cancelling;
        _ringPhaseStart = host._lastTime;
        _ringHover = -1;
        PlaySound(SoundCancel);
    }

    /// <summary>링 단계를 넘긴다 — 열기가 끝나면 대기, 고름이 끝나면 명령 실행, 취소가 끝나면 없앤다.</summary>
    internal void UpdateRing()
    {
        if (_ringUnit < 0) return;
        int n = RingTick(_ringPhaseStart) + 1;
        switch (_ringPhase)
        {
            case RingPhase.Opening when n > 10:
            case RingPhase.Reopening when n > 8:
                _ringPhase = RingPhase.Idle;
                break;
            case RingPhase.Picking when n > 8:
                int unit = _ringUnit, item = _ringPicked;
                _ringUnit = -1;
                RunRingCommand(unit, RingItems[item].Command);
                break;
            case RingPhase.Cancelling when n > 10:
                _ringUnit = -1;
                break;
        }
    }

    /// <summary>우클릭: 열린 창·링을 닫거나, 목록·대상 고르기를 취소한다(걸음은 안 물림). 취소할 것이 없고 인물 위면 링을 연다.</summary>
    internal void OnRightClick(int bx, int by)
    {
        if (host._afterFadeOut != null) return;       // 장면을 떠나는 페이드 동안은 입력을 안 받는다
        // 결과 배너는 우클릭으로도 넘긴다(0x1006b1fd~ — Esc · Space · 좌클릭 · 우클릭, ba-21 battle-flow 3).
        if (_outcome.Length > 0 && !host.Mos._mosesOpen && !host.FieldOpen && !host.EpisodesScr._episodesOpen && !host.TitleScr._titleOpen) { if (host.OutcomeInputReady) host.Btl.LeaveFinishedBattle(); return; }
        // 대사 중 우클릭은 <b>그 장면을 통째로</b> 건너뛴다(왼쪽 클릭은 한 줄씩).
        if (host.Tlk.OnTalkInput(skipAll: true)) return;
        if (_deployOpen) { _deployPick = null; return; }   // 배치 중 우클릭 = 고른 사람 놓기
        // 창은 모달이다(0x1003fc80) — 레벨업·알림·결과 배너·시스템 창이 떠 있으면 우클릭이 뒤의 유닛 정보 창·링을 열지 않는다(ba-20 G2).
        if (!host.Mos._mosesOpen && !host.FieldOpen)
        {
            if (LevelUpOpen || host.SlotsScr._notice != null || _outcome.Length > 0) return;
            if (host.Sys.SystemOpen) { host.Sys.CloseSystemWindow(); return; }
            if (_itemMenu) { CancelStep(undoMove: false); return; }
        }
        // 모세스 항성계 옮기기(100틱 대기)·페이드가 도는 동안은 입력을 안 받는다(ba-20 G4).
        if (host.Mos._mosesOpen && (host.Mos._mosesSystemSwitch != null || host.Mos._mosesFade > 0)) return;
        // 모세스에서는 우클릭이 <b>뒤로</b>다(원본과 같게, 사용자 보고) — 행성에서 우클릭하면 행성 고르기로 돌아가
        // 다른 행성을 고를 수 있다. Esc 와 같은 길을 탄다.
        if (host.Mos._mosesOpen)
        {
            if (host._statusUnit >= 0) { if (!host.StatusScr.OnStatusRightClick(bx, by)) host._statusUnit = -1; return; }   // 줄 위 = 누르고 있는 동안 설명
            if (host.Mos.OnMosesStyleRightDown(bx, by)) return;   // 전직 화면 어빌리티 줄 = 누르고 있는 동안 설명
            if (host.Mos.OnMosesShopRightDown(bx, by)) return;    // 상점 아이템 줄 = 누르고 있는 동안 설명
            if (host.Sys.CloseSystemWindow()) return;
            if (host.Mos._mosesPage != -1) host.Mos.MosesGoBack();
            return;
        }
        if (OnAbilityMenuRightDown(bx, by)) return;   // 어빌리티 목록 줄 = 누르고 있는 동안 설명
        if (host._statusUnit >= 0) { if (!host.StatusScr.OnStatusRightClick(bx, by)) host._statusUnit = -1; return; }   // 줄 위 = 누르고 있는 동안 설명, 빈 곳 = 닫기
        if (_ringUnit >= 0)
        {
            // 항목 위에서 오른쪽 단추를 누르고 있는 동안 설명(TXR 1375~1380, 0x1003fc80 모달 · 떼면 0x10042ac0) — 항목 밖 우클릭만 링 취소(0x100e1540). ba-14 U1.
            int item = RingItemAt(bx, by);
            if (item >= 0 && _ringPhase == RingPhase.Idle) { _ringHelp = host._db?.T((ushort)(1375 + item)); return; }
            CancelRing();
            return;
        }
        // 어빌리티·아이템 대상을 고르는 중이면 우클릭은 <b>취소</b>다(원본 상태 11) — 인물 위라도 정보 창을 열지 않는다.
        // 일반공격 대상 고르기(상태 10 0x10069330)는 다르다 — 마우스 칸에 유닛이 있으면 정보 창(0x100696af~0x1006976e → 0x1006974b),
        // 칸이 −1 이거나 비었으면 취소(0x10069110). 전에는 일반공격도 무조건 취소해 공격 전에 적 정보를 못 봤다(감사4 I1).
        if (_targetWork >= 0 && !_targetIsBasicAttack && CancelStep(undoMove: false)) return;
        if (OpenUnitInfo(bx, by)) return;   // 인물 위 = 정보 창(fa-8), 단추를 떼면 닫힌다(일반공격 대상 고르기 중에도)
        if (CancelStep(undoMove: false)) return;   // 일반공격 대상에서 빈 칸 = 취소

        int index = host.UnitAtPoint(bx, by);
        if (index >= 0 && host._units[index].IsAlly) { host._selected = index; OpenRing(index); return; }
        if (index >= 0) return;   // 적군은 링을 안 연다(적 상태 창은 fa-8)

        // 빈 칸에서 우클릭해도 차례인 아군의 링을 연다(fa-6 — 걷고 나서 바로 명령).
        if (IsPlayerTurn && !host._units[_turn].IsBusy) OpenRing(_turn);
    }

    /// <summary>링 키: 고른 인물의 링을 열거나 닫는다. 고른 인물이 없으면 알려 준다.</summary>
    internal void ToggleRingForSelected()
    {
        if (_ringUnit >= 0) { CancelRing(); return; }
        if ((uint)host._selected >= host._units.Length) { host.Hint("먼저 인물을 고르세요 (클릭·Tab)"); return; }
        OpenRing(host._selected);
    }

    /// <summary>링이 열려 있으면 클릭을 처리하고 true. 항목이면 고름 움직임 뒤 실행, 링 밖이면 취소.</summary>
    internal bool OnRingClick(int bx, int by)
    {
        if (_ringUnit < 0) return false;
        if (_ringPhase != RingPhase.Idle) return true;
        int item = RingItemAt(bx, by);
        if (item < 0) { CancelRing(); return true; }
        PickRingItem(item);
        return true;
    }

    internal void PickRingItem(int item)
    {
        var (command, _, _, hover) = RingItems[item];
        if (!RingItemEnabled(command, _ringUnit))
        {
            host.Toast(_ringUnit != _turn || !host._units[_ringUnit].IsAlly ? $"{hover}: 차례인 아군만 쓸 수 있습니다" : $"{hover}: TP 가 모자랍니다");
            return;
        }
        _ringPicked = item;
        _ringPhase = RingPhase.Picking;
        _ringPhaseStart = host._lastTime;
        PlaySound(SoundPick);
    }

    /// <summary>단축키로 링 명령을 바로 실행한다 — 링이 열려 있으면 고름 움직임을 거친다.</summary>
    internal void RingShortcut(RingCommand command)
    {
        int item = Array.FindIndex(RingItems, r => r.Command == command);
        if (_ringUnit >= 0)
        {
            if (_ringPhase == RingPhase.Idle) PickRingItem(item);
            return;
        }
        if (!IsPlayerTurn || _abilityMenu || _targetWork >= 0) return;
        if (!RingItemEnabled(command, _turn)) { host.Toast($"{RingItems[item].Hover}: 지금은 쓸 수 없습니다"); return; }
        RunRingCommand(_turn, command);
    }

    internal void RunRingCommand(int unit, RingCommand command)
    {
        switch (command)
        {
            case RingCommand.Status: host._statusUnit = unit; break;
            case RingCommand.Rest: host.Toast($"{host.UnitName(unit)} 휴식"); Rest(unit); break;
            case RingCommand.Attack: BeginAttackTargeting(); break;
            case RingCommand.Ability:
                // 12(어빌리티 사용 불가)면 목록 자체가 안 열린다(0x100e17d8).
                if (host._units[unit].HasStatus(12)) { host.Toast("어빌리티를 쓸 수 없습니다"); break; }
                CommitMoveForAction();
                _abilityMenu = true;
                _abilityTop = 0;
                break;
            case RingCommand.System: host.Sys.OpenSystemMenu(); break;
            case RingCommand.Item:
                CommitMoveForAction();
                _itemMenu = true;
                _itemTop = 0;
                break;
        }
    }

    internal void DrawRing()
    {
        if (_ringUnit < 0) return;
        var (cx, cy) = RingCenter(host._units[_ringUnit]);
        int t = RingTick(_ringOpenedAt);
        int n = RingTick(_ringPhaseStart) + 1;
        bool closing = _ringPhase is RingPhase.Picking or RingPhase.Cancelling;

        if (!closing) host.DrawUi(106, 0, t, cx, cy, UiBlend.Darken);
        if (!closing && (_ringPhase != RingPhase.Opening || n >= 7)) host.DrawUi(86, 1, t, cx, cy, UiBlend.Add);

        for (int i = 0; i < RingItems.Length; i++)
        {
            var (command, iconObs, labelMotion, hoverName) = RingItems[i];
            var (ox, oy) = RingItemOffset(i);
            int x = cx + ox, y = cy + oy;
            bool hover = _ringPhase == RingPhase.Idle && i == _ringHover;
            bool enabled = RingItemEnabled(command, _ringUnit);

            if (hover) host.DrawUi(105, 0, RingTick(_ringHoverAt), x - 1, y - 1, UiBlend.Add);
            if (!host.DrawUi(iconObs, 1, t, x, y, enabled ? UiBlend.Add : UiBlend.AddDim))
            {
                // 그림이 없으면 글자로
                host.FillCircle(x, y, 20, 0xE0142850);
                var (_, w, h) = host.GetText(hoverName[..3], enabled ? White : DimGray);
                host.DrawText(hoverName[..3], x - w / 2, y - h / 2, enabled ? White : DimGray);
            }
            if (hover && !host.DrawUi(452, labelMotion, RingTick(_ringHoverAt), x - 23, y + 7, UiBlend.Alpha))
                host.DrawText(hoverName, x - 23, y + 12, White, 15);
        }
        if (_ringHelp is { Length: > 0 } help) host.StatusScr.DrawDescriptionTip(help, host._mouse.X, host._mouse.Y, host._camX, host._camY, host.ViewWidth, host.ViewHeight);
    }

    /// <summary>오른쪽 단추를 누르고 있는 동안 보이는 링 항목 설명 — 떼면 지운다(WM_RBUTTONUP).</summary>
    internal string? _ringHelp;
}

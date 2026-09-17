using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

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
internal sealed unsafe partial class BattleSceneWindow
{
    private enum RingCommand { Attack, Ability, Item, System, Status, Rest }

    private static readonly (RingCommand Command, int IconObs, int LabelMotion, string Hover)[] RingItems =
    [
        (RingCommand.Attack, 92, 1, "ATTACK"),
        (RingCommand.Ability, 87, 0, "ABILITY"),
        (RingCommand.Item, 89, 4, "ITEM"),
        (RingCommand.System, 90, 3, "SYSTEM"),
        (RingCommand.Status, 88, 5, "STATUS"),
        (RingCommand.Rest, 91, 2, "REST"),
    ];

    private enum RingPhase { Opening, Reopening, Idle, Picking, Cancelling }

    private const int RingRadius = 66, RingLift = 40, RingEdge = 89, RingHitRadius = 18;
    private const int SoundHover = 65, SoundOpen = 67, SoundPick = 68, SoundCancel = 69;

    private int _ringUnit = -1;
    private int _ringHover = -1;
    private RingPhase _ringPhase;
    private double _ringPhaseStart, _ringOpenedAt, _ringHoverAt;
    private int _ringPicked = -1;

    private readonly Dictionary<int, UiSprite> _ui = [];
    private readonly Dictionary<int, byte[]> _sounds = [];

    /// <summary>링 그림(assets/ui)·소리(assets/sounds)를 읽는다. 없으면 글자 링으로 그린다.</summary>
    private void LoadRingAssets()
    {
        try
        {
            foreach (string path in Directory.EnumerateFiles(AssetsFolder.Find("ui"), "*.obs"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int id))
                    _ui[id] = new UiSprite(ObsSprite.Decode(path), ObsMotionTable.Load(path));
            foreach (string path in Directory.EnumerateFiles(AssetsFolder.Find("sounds"), "*.wav"))
                if (int.TryParse(Path.GetFileNameWithoutExtension(path), out int id))
                    _sounds[id] = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            _loadError = $"링 그림: {ex.Message}";
        }
    }

    private void PlaySound(int id)
    {
        if (_sounds.TryGetValue(id, out var wav)) SoundPlayer.Play(wav);
    }

    private int RingTick(double since) => (int)((_lastTime - since) * TicksPerSecond);

    /// <summary>링 중심 — 유닛 자리에서 40픽셀 위, 판 가장자리에서 89픽셀 안쪽.</summary>
    private (int X, int Y) RingCenter(UnitState unit)
    {
        var (fx, fy) = UnitFoot(unit);
        return (Math.Clamp(fx, RingEdge, BoardWidth - RingEdge), Math.Clamp(fy - RingLift, _camY + GridTop + RingEdge, _camY + ViewHeight - RingEdge));
    }

    /// <summary>지금 단계·틱에서 i 번 항목의 중심(링 중심 기준).</summary>
    private (int X, int Y) RingItemOffset(int i)
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

    private int RingItemAt(int bx, int by)
    {
        if (_ringUnit < 0 || _ringPhase != RingPhase.Idle) return -1;
        var (cx, cy) = RingCenter(_units[_ringUnit]);
        for (int i = 0; i < RingItems.Length; i++)
        {
            var (ox, oy) = RingItemOffset(i);
            int dx = bx - cx - ox, dy = by - cy - oy;
            if (dx * dx + dy * dy <= RingHitRadius * RingHitRadius) return i;
        }
        return -1;
    }

    private void OnRingMouseMove(int bx, int by)
    {
        int hover = RingItemAt(bx, by);
        if (hover == _ringHover) return;
        _ringHover = hover;
        _ringHoverAt = _lastTime;
        if (hover >= 0) PlaySound(SoundHover);
    }

    private bool RingItemEnabled(RingCommand command, int ringUnit)
    {
        var unit = _units[ringUnit];
        if (command is RingCommand.Status or RingCommand.System) return true;
        if (!IsPlayerTurn || ringUnit != _turn || unit.IsBusy) return false;
        if (command is not (RingCommand.Attack or RingCommand.Ability)) return true;
        return _db == null || unit.Tp + unit.Ctp >= _db.N(4);
    }

    private void OpenRing(int unit, bool reopen = false)
    {
        _ringUnit = unit;
        _ringPhase = reopen ? RingPhase.Reopening : RingPhase.Opening;
        _ringPhaseStart = _ringOpenedAt = _lastTime;
        _ringHover = -1;
        _ringPicked = -1;
        PlaySound(SoundOpen);
    }

    /// <summary>링을 취소 움직임으로 닫는다(이미 닫히는 중이면 그대로).</summary>
    private void CancelRing()
    {
        if (_ringUnit < 0 || _ringPhase is RingPhase.Picking or RingPhase.Cancelling) return;
        _ringPhase = RingPhase.Cancelling;
        _ringPhaseStart = _lastTime;
        _ringHover = -1;
        PlaySound(SoundCancel);
    }

    /// <summary>링 단계를 넘긴다 — 열기가 끝나면 대기, 고름이 끝나면 명령 실행, 취소가 끝나면 없앤다.</summary>
    private void UpdateRing()
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
    private void OnRightClick(int bx, int by)
    {
        if (_statusUnit >= 0) { _statusUnit = -1; return; }
        if (_ringUnit >= 0) { CancelRing(); return; }
        if (CancelStep(undoMove: false)) return;
        int index = UnitAtBoard(bx, by);
        if (index < 0) return;
        _selected = index;
        OpenRing(index);
    }

    /// <summary>링 키: 고른 인물의 링을 열거나 닫는다. 고른 인물이 없으면 알려 준다.</summary>
    private void ToggleRingForSelected()
    {
        if (_ringUnit >= 0) { CancelRing(); return; }
        if ((uint)_selected >= _units.Length) { Toast("먼저 인물을 고르세요 (클릭·Tab)"); return; }
        OpenRing(_selected);
    }

    /// <summary>링이 열려 있으면 클릭을 처리하고 true. 항목이면 고름 움직임 뒤 실행, 링 밖이면 취소.</summary>
    private bool OnRingClick(int bx, int by)
    {
        if (_ringUnit < 0) return false;
        if (_ringPhase != RingPhase.Idle) return true;
        int item = RingItemAt(bx, by);
        if (item < 0) { CancelRing(); return true; }
        PickRingItem(item);
        return true;
    }

    private void PickRingItem(int item)
    {
        var (command, _, _, hover) = RingItems[item];
        if (!RingItemEnabled(command, _ringUnit))
        {
            Toast(_ringUnit != _turn || !_units[_ringUnit].IsAlly ? $"{hover}: 차례인 아군만 쓸 수 있습니다" : $"{hover}: TP 가 모자랍니다");
            return;
        }
        _ringPicked = item;
        _ringPhase = RingPhase.Picking;
        _ringPhaseStart = _lastTime;
        PlaySound(SoundPick);
    }

    /// <summary>단축키로 링 명령을 바로 실행한다 — 링이 열려 있으면 고름 움직임을 거친다.</summary>
    private void RingShortcut(RingCommand command)
    {
        int item = Array.FindIndex(RingItems, r => r.Command == command);
        if (_ringUnit >= 0)
        {
            if (_ringPhase == RingPhase.Idle) PickRingItem(item);
            return;
        }
        if (!IsPlayerTurn || _abilityMenu || _targetWork >= 0) return;
        if (!RingItemEnabled(command, _turn)) { Toast($"{RingItems[item].Hover}: 지금은 쓸 수 없습니다"); return; }
        RunRingCommand(_turn, command);
    }

    private void RunRingCommand(int unit, RingCommand command)
    {
        switch (command)
        {
            case RingCommand.Status: _statusUnit = unit; break;
            case RingCommand.Rest: Toast($"{UnitName(unit)} 휴식"); Rest(unit); break;
            case RingCommand.Attack: BeginAttackTargeting(); break;
            case RingCommand.Ability: CommitMoveForAction(); _abilityMenu = true; break;
            default: Toast($"{RingItems[(int)command].Hover}: 아직 구현하지 않았습니다"); break;
        }
    }

    private void DrawRing()
    {
        if (_ringUnit < 0) return;
        var (cx, cy) = RingCenter(_units[_ringUnit]);
        int t = RingTick(_ringOpenedAt);
        int n = RingTick(_ringPhaseStart) + 1;
        bool closing = _ringPhase is RingPhase.Picking or RingPhase.Cancelling;

        if (!closing) DrawUi(106, 0, t, cx, cy, UiBlend.Darken);
        if (!closing && (_ringPhase != RingPhase.Opening || n >= 7)) DrawUi(86, 1, t, cx, cy, UiBlend.Add);

        for (int i = 0; i < RingItems.Length; i++)
        {
            var (command, iconObs, labelMotion, hoverName) = RingItems[i];
            var (ox, oy) = RingItemOffset(i);
            int x = cx + ox, y = cy + oy;
            bool hover = _ringPhase == RingPhase.Idle && i == _ringHover;
            bool enabled = RingItemEnabled(command, _ringUnit);

            if (hover) DrawUi(105, 0, RingTick(_ringHoverAt), x - 1, y - 1, UiBlend.Add);
            if (!DrawUi(iconObs, 1, t, x, y, enabled ? UiBlend.Add : UiBlend.AddDim))
            {
                // 그림이 없으면 글자로
                FillCircle(x, y, 20, 0xE0142850);
                var (_, w, h) = GetText(hoverName[..3], enabled ? White : DimGray);
                DrawText(hoverName[..3], x - w / 2, y - h / 2, enabled ? White : DimGray);
            }
            if (hover && !DrawUi(452, labelMotion, RingTick(_ringHoverAt), x - 23, y + 7, UiBlend.Alpha))
                DrawText(hoverName, x - 23, y + 12, White, 15);
        }
    }

    private enum UiBlend { Alpha, Add, AddDim, Darken }

    /// <summary>UI Obs 한 장을 모션표 틱에 맞춰 (x, y) 에 그린다(컷의 X·Y 가 기준점에서 왼쪽 위까지 거리). 그렸으면 true.</summary>
    private bool DrawUi(int obs, int motion, int tick, int x, int y, UiBlend blend)
    {
        if (!_ui.TryGetValue(obs, out var sprite) || sprite.FrameAt(motion, tick) is not { } f) return false;
        int left = x + f.X, top = y + f.Y;
        for (int yy = 0; yy < f.H; yy++)
        {
            int py = top + yy;
            if ((uint)py >= BoardHeight) continue;
            for (int xx = 0; xx < f.W; xx++)
            {
                int px = left + xx;
                if ((uint)px >= BoardWidth) continue;
                uint c = f.Px[yy * f.W + xx];
                if ((c & 0xFF000000) == 0) continue;
                int i = py * BoardWidth + px;
                uint d = _fb[i];
                _fb[i] = blend switch
                {
                    UiBlend.Add => AddColor(d, c, 256),
                    UiBlend.AddDim => AddColor(d, c, 100),
                    UiBlend.Darken => ScaleColor(d, 11, 31),
                    _ => c | 0xFF000000,
                };
            }
        }
        return true;
    }

    private static uint AddColor(uint d, uint c, int weight)
    {
        uint Ch(int shift) => (uint)Math.Min(255, (int)(d >> shift & 0xFF) + (int)(c >> shift & 0xFF) * weight / 256);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint ScaleColor(uint d, int num, int den)
    {
        uint Ch(int shift) => (uint)((int)(d >> shift & 0xFF) * num / den);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }
}

/// <summary>UI 용 Obs 하나 — 벌·컷 그림과 모션표.</summary>
internal sealed class UiSprite
{
    private readonly Dictionary<(int Sub, int Slot), SpriteFrame> _frames = [];
    private readonly ObsMotionTable? _table;

    public UiSprite(IReadOnlyList<ObsMotion> motions, ObsMotionTable? table)
    {
        _table = table;
        foreach (var motion in motions)
            for (int i = 0; i < motion.Frames.Count; i++)
                _frames[(motion.Id, i)] = SpriteFrame.From(motion.Frames[i]);
    }

    /// <summary>모션 m 의 tick 째 컷(되풀이). 모션표가 없으면 벌 0 첫 컷.</summary>
    public SpriteFrame? FrameAt(int motion, int tick)
    {
        if (_table?.Clips.GetValueOrDefault(motion)?.KeyAt(tick, loop: true) is { } k)
            return _frames.GetValueOrDefault((k.SubentryId, k.Slot));
        return _frames.GetValueOrDefault((0, 0));
    }
}

/// <summary>WAV 한 개를 비동기로 튼다(winmm <c>PlaySound</c>, 메모리). 새 소리가 앞 소리를 끊는다.</summary>
internal static class SoundPlayer
{
    private static System.Runtime.InteropServices.GCHandle _pinned;

    [System.Runtime.InteropServices.DllImport("winmm.dll")]
    private static extern bool PlaySoundW(IntPtr sound, IntPtr module, uint flags);

    private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4;

    public static void Play(byte[] wav)
    {
        // 비동기 재생은 버퍼를 계속 읽으니, 앞 소리를 멈춘 뒤 새 버퍼를 고정해 둔다.
        PlaySoundW(IntPtr.Zero, IntPtr.Zero, 0);
        if (_pinned.IsAllocated) _pinned.Free();
        _pinned = System.Runtime.InteropServices.GCHandle.Alloc(wav, System.Runtime.InteropServices.GCHandleType.Pinned);
        PlaySoundW(_pinned.AddrOfPinnedObject(), IntPtr.Zero, SND_ASYNC | SND_NODEFAULT | SND_MEMORY);
    }
}

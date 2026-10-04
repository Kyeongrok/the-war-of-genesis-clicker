using System.Drawing;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>몸짓 한 컷 — BGRA 픽셀과, 발 자리에서 그림 왼쪽 위까지의 거리(X·Y).</summary>
internal sealed record SpriteFrame(uint[] Px, int W, int H, int X, int Y)
{
    public static SpriteFrame From(ObsFrame f)
    {
        var px = new uint[f.Width * f.Height];
        Buffer.BlockCopy(f.Bgra, 0, px, 0, f.Bgra.Length);
        return new SpriteFrame(px, f.Width, f.Height, f.X, f.Y);
    }

    /// <summary>좌우로 뒤집은 컷 — 발 자리를 축으로 뒤집으니 X 도 따라 옮긴다.</summary>
    public SpriteFrame Mirrored()
    {
        var px = new uint[Px.Length];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                px[y * W + x] = Px[y * W + (W - 1 - x)];
        return new SpriteFrame(px, W, H, -(X + W), Y);
    }
}

/// <summary>인물 하나의 컷 전부와 걷기 표. 오른쪽 컷은 처음 쓸 때 뒤집어 둔다.</summary>
internal sealed class UnitSprite
{
    internal readonly Dictionary<(int Sub, int Slot), SpriteFrame> _frames = [];
    internal readonly Dictionary<(int Sub, int Slot), SpriteFrame> _mirrored = [];
    internal readonly SpriteFrame _first;
    internal readonly ObsMotionTable? _table;

    public UnitSprite(IReadOnlyList<ObsMotion> motions, ObsMotionTable? table)
    {
        _table = table;
        // 컷은 장 번호로 담는다(모션표 키가 장 번호다 — 분석-모션 「Obs 파일 갈래」).
        foreach (var motion in motions)
            foreach (var frame in motion.Frames)
                _frames[(motion.Id, frame.SlotId)] = SpriteFrame.From(frame);
        _first = SpriteFrame.From(motions[0].Frames[0]);
    }

    /// <summary>
    /// 지금 보일 컷 — 걷는 중이면 걷기(동작 1), 아니면 서기(동작 0, 까딱이는 숨쉬기) 모션을 틱에 맞춰 넘긴다.
    /// 오른쪽을 보면 옆모습 컷을 뒤집는다.
    /// </summary>
    /// <summary>지금 재생 중인 모션(물들이기·자리 키까지 들어 있다)과 그 틱.</summary>
    public (ObsMotionClip? Clip, int Tick) CurrentClip(UnitState unit)
    {
        return (ClipOf(unit), (int)(unit.AnimTime * GameWindow.TicksPerSecond));
    }

    /// <summary>지금 모션 — 모션 번호를 바로 튼 중이면(<see cref="UnitState.Motion"/>) 그것, 아니면 동작·방향으로 찾은 것.</summary>
    internal ObsMotionClip? ClipOf(UnitState unit) =>
        unit.Motion >= 0 && _table?.Clips.GetValueOrDefault(unit.Motion) is { Keys.Count: > 0 } raw
            ? raw
            : _table?.Resolve(ActionOf(unit), ObsMotionTable.DirectionOf(unit.Facing));

    /// <summary>
    /// 지금 재생할 동작 — 걷는 중이면 걷기(1), 아니면 서기(0). 다만 <b>걷기 그림이 한 컷뿐인 인물</b>(카르마타처럼
    /// 자료에 걷는 그림이 없는 쪽)은 걷는 동안에도 서기 모션을 돌려 미끄러지듯 굳어 보이지 않게 한다.
    /// </summary>
    internal int ActionOf(UnitState unit)
    {
        if (unit.Action >= 0) return unit.Action;
        if (!unit.IsMoving && !unit.Entering) return ObsMotionTable.ActionStand;
        var walk = _table?.Resolve(ObsMotionTable.ActionWalk, ObsMotionTable.DirectionOf(unit.Facing));
        return walk is { Keys.Count: > 1 } ? ObsMotionTable.ActionWalk : ObsMotionTable.ActionStand;
    }

    /// <summary>걷기 모션에 달린 소리 키들(시작 틱, Snd 번호).</summary>
    public IReadOnlyList<(int Start, int Sound)> WalkSounds(Facing facing) =>
        _table?.Resolve(ObsMotionTable.ActionWalk, ObsMotionTable.DirectionOf(facing))?.Sounds ?? [];

    public SpriteFrame FrameFor(UnitState unit)
    {
        var clip = ClipOf(unit);
        var key = clip?.KeyAt((int)(unit.AnimTime * GameWindow.TicksPerSecond), loop: unit.Loops);
        if (key is not { } k || !_frames.TryGetValue((k.SubentryId, k.Slot), out var frame)) return _first;
        if (unit.Facing != Facing.Right) return frame;

        if (!_mirrored.TryGetValue((k.SubentryId, k.Slot), out var mirrored))
            _mirrored[(k.SubentryId, k.Slot)] = mirrored = frame.Mirrored();
        return mirrored;
    }

    /// <summary>모션 번호(동작·방향이 아니라 Obs 안의 번호)로 그 틱의 컷 — 몸 복제(분신) 이펙트가 쓴다. 없으면 null.</summary>
    /// <summary>그 모션의 길이(틱) — 없으면 0. 한 번 도는 분신을 언제 지울지 셀 때 쓴다.</summary>
    public ObsMotionClip? RawClip(int motion) => _table?.Clips.GetValueOrDefault(motion);

    public int MotionTicks(int motion) =>
        _table?.Clips.GetValueOrDefault(motion) is { } clip ? Math.Max(clip.Length, clip.Keys.Count > 0 ? clip.Keys[^1].Start + clip.Keys[^1].Length : 0) : 0;

    public SpriteFrame? FrameOfMotion(int motion, int tick, bool mirror)
    {
        if (_table?.Clips.GetValueOrDefault(motion) is not { } clip || clip.KeyAt(tick, loop: false) is not { } k
            || !_frames.TryGetValue((k.SubentryId, k.Slot), out var frame)) return null;
        if (!mirror) return frame;
        if (!_mirrored.TryGetValue((k.SubentryId, k.Slot), out var mirrored))
            _mirrored[(k.SubentryId, k.Slot)] = mirrored = frame.Mirrored();
        return mirrored;
    }

    /// <summary>동작·방향의 모션(소리 키까지 들어 있다). 없으면 null.</summary>
    public ObsMotionClip? Clip(int action, Facing facing) => _table?.Resolve(action, ObsMotionTable.DirectionOf(facing));

    /// <summary>
    /// 그 동작에서 <b>타격이 나는 순간</b>(초) — 모션에 붙은 첫 소리 키 자리, 없으면 길이의 60%.
    /// 원본도 때리는 소리와 함께 피해가 뜬다(분석-사운드·분석-모션).
    /// </summary>
    public double HitMomentSeconds(int action, Facing facing)
    {
        if (_table?.Resolve(action, ObsMotionTable.DirectionOf(facing)) is not { } clip) return 0;
        // 치는 동작이 뜨고 아주 잠깐(0.05초) 뒤에 피해가 뜬다 — 사용자가 원본을 보고 알려 준 감각이다.
        // 모션이 그보다 짧으면 모션 길이를 넘지 않는다.
        return Math.Min(0.05, Math.Max(0, clip.Length - 1) / GameWindow.TicksPerSecond);
    }

    /// <summary>한 번 재생할 동작의 길이(초). 모션표에 없으면 0.</summary>
    public double ActionSeconds(int action, Facing facing) =>
        (_table?.Resolve(action, ObsMotionTable.DirectionOf(facing))?.Length ?? 0) / GameWindow.TicksPerSecond;
}

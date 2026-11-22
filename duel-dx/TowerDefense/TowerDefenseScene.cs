using System.Collections.Concurrent;
using System.IO;
using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 타워 디펜스(사용자 요청 td-1, 원본에 없다) — 타이틀의 「타워 디펜스」 단추로 연다. 파트2 전투 맵 한 네모 위로 파트1 병사들이 길을 따라 오고,
/// 적을 잡아 번 골드로 병종(용병) 타워와 영웅 타워를 짓는다. 영웅은 비싸고 하나씩만 지으며 둘레 타워를 세게 하는 오라를 두른다.
/// </summary>
/// <remarks>
/// <para>판은 640×480 틀(<see cref="MosesScene.MosesOrigin"/>) — 위 640×400 이 맵, 아래 80 이 짓기 칸(병종 · 영웅 탭)이다.
/// 길은 맵의 걸을 수 있는 칸(깃발 &amp; 9 = 0, 이웃 칸과 높이 차 1 이하)을 잇는다 — 맵에 거쳐 갈 칸이 안 적혀 있으면 왼쪽 끝 → 위 1/3 → 아래 2/3 → 오른쪽 끝의
/// 가장 가까운 칸을 BFS 로 이어 S 자로 만든다.</para>
/// <para>인물 그림은 전투와 같은 길로 읽는다(assets/characters 목록, 없으면 원본 · 받은 자료 — <see cref="GameWindow.LoadFromGameFolder"/>). 처음 열 때 뒤에서 읽고,
/// 다 읽기 전에는 그 자리에 이름표만 선다. 파트1 그림이 없는 PC 는 적마다 정해 둔 파트2 병사로 대신 선다.</para>
/// <para>저장은 본편 슬롯과 따로 — <c>towerdefense.json</c>(<see cref="TowerDefenseSave"/>)에 맵마다 가장 멀리 간 웨이브와 깬 횟수만 적는다.</para>
/// </remarks>
internal sealed class TowerDefenseScene(GameWindow host)
{
    internal bool Open;

    private const int FieldW = MosesScene.MosesW, FieldH = 400, PanelY = 400, Cell = ObtMap.CellWidth, CellH = ObtMap.CellHeight;
    private const int CardW = 56, CardH = 54, CardY = PanelY + 22, TabW = 56;
    private const uint Gold = 0xFFFFD860, Red = 0xFFFF7070, Green = 0xFF80F080, Dim = 0xFF98A0B8;

    private enum Phase { Playing, Won, Lost }

    private sealed class Tower(TdTowerDef def, int col, int row, double x, double y)
    {
        internal readonly TdTowerDef Def = def;
        internal readonly int Col = col, Row = row;
        internal readonly double X = x, Y = y;
        internal double Cooldown, AnimAt = -9;
        internal int Action;
        internal Facing Facing = Facing.Down;
        internal int Spent = def.Cost;
        internal int Kills;
    }

    private sealed class Enemy(TdEnemyDef def, int hp)
    {
        internal readonly TdEnemyDef Def = def;
        internal readonly int MaxHp = hp;
        internal double Hp = hp, Dist, X, Y, DeadAt = -1, HitAt = -9;
        internal Facing Facing = Facing.Right;
        internal bool Alive => DeadAt < 0;
    }

    private sealed class Shot(double x, double y, Enemy target, Tower from, double damage)
    {
        internal double X = x, Y = y;
        internal readonly Enemy Target = target;
        internal readonly Tower From = from;
        internal readonly double Damage = damage;
    }

    private TdMap _map = TdData.Maps[0];
    private int _mapIndex;
    private ObtMapImage? _img;
    private int _camX, _camY;
    private readonly HashSet<(int, int)> _pathCells = [];
    private readonly List<(int Col, int Row)> _pathOrder = [];
    private readonly List<(double X, double Y)> _path = [];
    private readonly List<double> _pathLen = [];

    private readonly List<Tower> _towers = [];
    private readonly List<Enemy> _enemies = [];
    private readonly List<Shot> _shots = [];
    private readonly List<(double X, double Y, double At, int Kind)> _fx = [];
    private readonly List<(string Text, double X, double Y, double At, uint Color)> _floats = [];
    private readonly List<(double At, int Enemy)> _spawns = [];

    private int _gold, _lives, _wave;
    private double _sim, _lastClock = -1, _nextWaveIn = -1;
    private int _speed = 1;
    private Phase _phase;

    /// <summary>짓기 칸에서 고른 타워(<see cref="TdData.Towers"/> 차례), 판에서 고른 타워, 보는 탭(0 병종 · 1 영웅).</summary>
    private int _pick = -1, _tab, _hoverCard = -1;
    private Tower? _selected;
    private (int Col, int Row) _hoverCell = (-1, -1);
    private string? _hoverButton;

    private readonly ConcurrentDictionary<int, UnitSprite?> _sprites = new();
    private Task? _loading;
    private int _loaded, _toLoad;
    private Dictionary<int, ExportedCharacter>? _manifests;

    private TowerDefenseSave _save = new();

    // ── 열기 · 닫기 ──

    /// <summary><c>DUELDX_TD=1</c> 이면 타이틀을 거치지 않고 바로 연다(화면 밖 시험). 값이 2 이상이면 그 웨이브 · 골드로 시작한다(<c>DUELDX_TD_GOLD</c>).</summary>
    internal void OpenIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_TD") is not { Length: > 0 } v || !int.TryParse(v, out int n) || n <= 0) return;
        Start(0);
        if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_TD_GOLD"), out int g)) _gold = g;
        if (n > 1) _wave = Math.Min(n - 1, TdData.Waves.Length - 1);
    }

    internal void Start(int mapIndex)
    {
        host.TitleScr._titleOpen = false;
        host.Mos._mosesOpen = false;
        if (host._part1TitleScr is { } p1) p1._open = false;
        if (host.Cols != TitleBoardCols || host.Rows != TitleBoardRows) { host.ResizeBoard(TitleBoardCols, TitleBoardRows); host._battleLoaded = false; }
        Open = true;
        _mapIndex = Math.Clamp(mapIndex, 0, TdData.Maps.Length - 1);
        _map = TdData.Maps[_mapIndex];
        // 화면 밖 시험: DUELDX_TD_MAP=part1:0093 따위로 다른 맵을 본다.
        if (Environment.GetEnvironmentVariable("DUELDX_TD_MAP") is { Length: > 0 } test) _map = _map with { Name = "시험", File = test, Waypoints = [] };
        _save = TowerDefenseSave.Load();
        LoadMap();
        Reset();
        BeginLoadingSprites();
        host.StopMusic();
        host.PlayMusicFile(_map.Bgm, loop: true);
        host.EnterSceneFade();
    }

    private void Reset()
    {
        _towers.Clear(); _enemies.Clear(); _shots.Clear(); _fx.Clear(); _floats.Clear(); _spawns.Clear();
        _gold = TdData.StartGold;
        _lives = TdData.StartLives;
        _wave = 0;
        _sim = 0;
        _lastClock = -1;
        _nextWaveIn = -1;
        _speed = 1;
        _phase = Phase.Playing;
        _pick = -1;
        _selected = null;
    }

    private void Leave()
    {
        if (_phase == Phase.Playing && _wave > 0) Record(false);
        host.LeaveScene(() =>
        {
            Open = false;
            host.StopMusic();
            host.TitleScr.OpenTitle();
        });
    }

    private void Record(bool won)
    {
        string key = _map.File;
        int reached = won ? TdData.Waves.Length : Math.Max(0, _wave - 1);
        if (reached > _save.BestWave.GetValueOrDefault(key)) _save.BestWave[key] = reached;
        if (won) _save.Clears[key] = _save.Clears.GetValueOrDefault(key) + 1;
        _save.Write();
    }

    // ── 맵 · 길 ──

    private void LoadMap()
    {
        _img = null;
        _path.Clear(); _pathLen.Clear(); _pathCells.Clear(); _pathOrder.Clear();
        // 「part1:NNNN」 은 파트1 맵(동굴 · 무영릉 같은 좁은 길) — 파트1 이 없으면 못 연다.
        try
        {
            _img = _map.File.StartsWith(DemoScene.Part1MapPrefix, StringComparison.Ordinal)
                ? Part1Map.Load(int.Parse(_map.File[DemoScene.Part1MapPrefix.Length..])) ?? throw new InvalidDataException(_map.File)
                : ObtMap.Load(AssetPack.MapPath(_map.File));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or FormatException)
        {
            if (_map != TdData.FallbackMap) { _map = TdData.FallbackMap; LoadMap(); return; }
            host.Toast($"맵 {_map.File} 을 못 읽었습니다");
            return;
        }
        _camX = _map.CamX >= 0 ? _map.CamX : Math.Max(0, (_img.Width - FieldW) / 2);
        _camY = _map.CamY >= 0 ? _map.CamY : Math.Max(0, (_img.Height - FieldH) / 2);
        BuildPath();
    }

    /// <summary>칸 가운데(발 자리)의 맵 그림 좌표 — 판의 <c>CellTop</c> 과 같은 셈(그림 맨 윗줄 = 판 OriginY).</summary>
    private (double X, double Y) CellFoot(int col, int row)
    {
        int h = _img?.HeightAt(col, row) ?? 0;
        return (col * Cell + Cell / 2.0, row * CellH + CellH / 2.0 - h * HeightStep - (_img?.OriginY ?? 0));
    }

    private bool Visible(int col, int row, int margin = 0)
    {
        var (x, y) = CellFoot(col, row);
        return x - Cell / 2 >= _camX + margin && x + Cell / 2 <= _camX + FieldW - margin && y - CellH / 2 >= _camY + margin && y + CellH / 2 <= _camY + FieldH - margin;
    }

    private bool Walkable(int col, int row) =>
        _img is { } m && col >= 0 && row >= 0 && col < m.Cols && row < m.Rows && (m.FlagsAt(col, row) & 0x9) == 0 && Visible(col, row);

    private void BuildPath()
    {
        if (_img is not { } m) return;
        var marks = new List<(int, int)>();
        // 적힌 경유점은 화면 칸(맵 네모의 왼위에서 40×32 칸) — 그 자리에서 가장 가까운 걸을 수 있는 칸으로.
        if (_map.Waypoints.Length > 0)
            for (int i = 0; i < _map.Waypoints.Length; i++)
            {
                var (vc, vr) = _map.Waypoints[i];
                marks.Add(Nearest((_camX + vc * Cell + Cell / 2) / Cell, (_camY + vr * CellH + CellH / 2 + m.OriginY) / CellH, edge: i == 0 || i == _map.Waypoints.Length - 1));
            }
        else
        {
            int c0 = (int)Math.Ceiling(_camX / (double)Cell), c1 = (_camX + FieldW) / Cell - 1;
            int midRow = (_camY + FieldH / 2 + m.OriginY) / CellH;
            int topRow = (_camY + FieldH / 5 + m.OriginY) / CellH, bottomRow = (_camY + FieldH * 4 / 5 + m.OriginY) / CellH;
            marks.Add(Nearest(c0, midRow, edge: true));
            marks.Add(Nearest(c0 + (c1 - c0) / 3, topRow));
            marks.Add(Nearest(c0 + (c1 - c0) * 2 / 3, bottomRow));
            marks.Add(Nearest(c1, midRow, edge: true));
        }
        for (int i = 0; i < marks.Count; i++)
        {
            var leg = i == 0 ? [marks[0]] : Route(marks[i - 1], marks[i]);
            foreach (var c in leg)
                if (_pathOrder.Count == 0 || _pathOrder[^1] != c) _pathOrder.Add(c);
        }
        foreach (var c in _pathOrder) _pathCells.Add(c);

        // 화면 밖 왼쪽에서 들어와 오른쪽 밖으로 나간다.
        var first = CellFoot(_pathOrder[0].Col, _pathOrder[0].Row);
        var last = CellFoot(_pathOrder[^1].Col, _pathOrder[^1].Row);
        _path.Add((_camX - 30, first.Y));
        foreach (var (c, r) in _pathOrder) _path.Add(CellFoot(c, r));
        _path.Add((_camX + FieldW + 30, last.Y));
        _pathLen.Add(0);
        for (int i = 1; i < _path.Count; i++)
            _pathLen.Add(_pathLen[^1] + Math.Sqrt(Math.Pow(_path[i].X - _path[i - 1].X, 2) + Math.Pow(_path[i].Y - _path[i - 1].Y, 2)));
    }

    /// <summary>(col, row) 에서 가장 가까운 걸을 수 있는 칸 — <paramref name="edge"/> 면 그 열에서만 찾는다.</summary>
    private (int, int) Nearest(int col, int row, bool edge = false)
    {
        (int, int) best = (col, row);
        int bestD = int.MaxValue;
        for (int c = col - (edge ? 0 : 3); c <= col + (edge ? 0 : 3); c++)
            for (int r = row - 8; r <= row + 8; r++)
            {
                if (!Walkable(c, r)) continue;
                int d = Math.Abs(c - col) * 3 + Math.Abs(r - row);
                if (d < bestD) (bestD, best) = (d, (c, r));
            }
        return best;
    }

    /// <summary>두 칸을 걸을 수 있는 칸(높이 차 1 이하)으로 잇는 가장 짧은 길(BFS) — 못 이으면 곧은 줄.</summary>
    private List<(int Col, int Row)> Route((int Col, int Row) from, (int Col, int Row) to)
    {
        var prev = new Dictionary<(int, int), (int, int)>();
        var queue = new Queue<(int Col, int Row)>();
        queue.Enqueue(from);
        prev[from] = from;
        (int, int)[] steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == to) break;
            foreach (var (dc, dr) in steps)
            {
                var next = (cur.Col + dc, cur.Row + dr);
                if (prev.ContainsKey(next) || !Walkable(next.Item1, next.Item2)) continue;
                if (Math.Abs(_img!.HeightAt(next.Item1, next.Item2) - _img.HeightAt(cur.Col, cur.Row)) > 1) continue;
                prev[next] = cur;
                queue.Enqueue(next);
            }
        }
        var route = new List<(int, int)>();
        if (!prev.ContainsKey(to))
        {
            int n = Math.Max(Math.Abs(to.Col - from.Col), Math.Abs(to.Row - from.Row));
            for (int i = 1; i <= n; i++) route.Add((from.Col + (to.Col - from.Col) * i / n, from.Row + (to.Row - from.Row) * i / n));
            return route;
        }
        for (var c = to; c != from; c = prev[c]) route.Add(c);
        route.Reverse();
        return route;
    }

    private (double X, double Y, Facing Facing) PointAt(double dist)
    {
        int i = 1;
        while (i < _path.Count - 1 && _pathLen[i] < dist) i++;
        var (a, b) = (_path[i - 1], _path[i]);
        double seg = _pathLen[i] - _pathLen[i - 1], t = seg > 0 ? Math.Clamp((dist - _pathLen[i - 1]) / seg, 0, 1) : 1;
        double dx = b.X - a.X, dy = b.Y - a.Y;
        var facing = Math.Abs(dx) >= Math.Abs(dy) ? (dx >= 0 ? Facing.Right : Facing.Left) : (dy >= 0 ? Facing.Down : Facing.Up);
        return (a.X + dx * t, a.Y + dy * t, facing);
    }

    // ── 그림 ──

    private void BeginLoadingSprites()
    {
        if (_loading is { IsCompleted: false }) return;
        var wanted = TdData.Towers.Select(t => t.Chr).Concat(TdData.Enemies.Select(e => e.Chr)).Distinct().Where(c => !_sprites.ContainsKey(c)).ToList();
        _toLoad = wanted.Count;
        _loaded = 0;
        if (wanted.Count == 0) return;
        _loading = Task.Run(() =>
        {
            foreach (int chr in wanted)
            {
                _sprites[chr] = LoadSprite(chr);
                Interlocked.Increment(ref _loaded);
            }
            // 파트1 그림이 없는 적은 파트2 병사로.
            foreach (var e in TdData.Enemies.Where(e => _sprites.GetValueOrDefault(e.Chr) == null && !_sprites.ContainsKey(e.Fallback)))
                _sprites[e.Fallback] = LoadSprite(e.Fallback);
        });
    }

    private UnitSprite? LoadSprite(int chr)
    {
        if (host._sprites.TryGetValue(chr, out var battle)) return battle;
        try
        {
            string root = FindRepoAssetsRoot();
            _manifests ??= CollectExportedManifests(root);
            string path = _manifests.TryGetValue(chr, out var m)
                ? Path.Combine(root, CharacterExport.FolderNameFor(chr, m.Name), CharacterExport.ObsFileName(m.SpriteCode))
                : host.LoadFromGameFolder(chr).ObsPath;
            path = CharacterFile(path, 30);
            var (motions, table) = SharedSprites.Load(ref path);
            return motions.Count > 0 ? new UnitSprite(motions, table) : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or NullReferenceException) { return null; }
    }

    private UnitSprite? SpriteOf(TdEnemyDef e) => _sprites.GetValueOrDefault(e.Chr) ?? _sprites.GetValueOrDefault(e.Fallback);

    /// <summary>인물 그림 한 컷을 발 자리 (sx, sy) 에 — 동작이 없으면 서기. 그린 컷의 윗끝(y)을 돌려준다.</summary>
    private int DrawSprite(UnitSprite? s, int action, Facing facing, double seconds, bool loop, int sx, int sy, double fade = 1)
    {
        if (s == null) return sy - 50;
        int dir = ObsMotionTable.DirectionOf(facing);
        var clip = s._table?.Resolve(action, dir) ?? s._table?.Resolve(ObsMotionTable.ActionStand, dir);
        int tick = (int)(seconds * TicksPerSecond);
        var frame = clip?.KeyAt(tick, loop) is { } k && s._frames.TryGetValue((k.SubentryId, k.Slot), out var f) ? f : s._first;
        bool mirror = facing == Facing.Right;
        if (mirror && clip?.KeyAt(tick, loop) is { } mk)
        {
            if (!s._mirrored.TryGetValue((mk.SubentryId, mk.Slot), out var mf)) s._mirrored[(mk.SubentryId, mk.Slot)] = mf = frame.Mirrored();
            frame = mf;
        }
        host.DrawFrame(frame, sx, sy, UiBlend.Alpha, fade);
        host.Btl.DrawUnitLayers(clip, tick, sx, sy, mirror, fade, loop);
        return sy + frame.Y;
    }

    private bool HasAction(UnitSprite? s, int action) => s?._table?.Resolve(action, ObsMotionTable.DirectionSide) is { Keys.Count: > 0 };

    // ── 판 진행 ──

    private void Advance()
    {
        double now = host._lastTime;
        double dt = _lastClock < 0 ? 0 : Math.Clamp(now - _lastClock, 0, 0.1);
        _lastClock = now;
        if (_phase != Phase.Playing || host.SceneFading) return;
        for (int i = 0; i < _speed; i++) Step(dt);
    }

    private void Step(double dt)
    {
        _sim += dt;

        // 웨이브
        if (_nextWaveIn > 0 && (_nextWaveIn -= dt) <= 0) StartWave();
        while (_spawns.Count > 0 && _spawns[0].At <= _sim)
        {
            var def = TdData.Enemies[_spawns[0].Enemy];
            int hp = (int)(def.Hp * (1 + TdData.HpPerWave * (_wave - 1)));
            var e = new Enemy(def, hp);
            (e.X, e.Y, e.Facing) = PointAt(0);
            _enemies.Add(e);
            _spawns.RemoveAt(0);
            if (_spawns.Count == 0 && _wave < TdData.Waves.Length) _nextWaveIn = 18;
        }

        // 적 걷기
        double total = _pathLen.Count > 0 ? _pathLen[^1] : 0;
        foreach (var e in _enemies.Where(e => e.Alive))
        {
            e.Dist += e.Def.Speed * Cell * dt;
            (e.X, e.Y, e.Facing) = PointAt(e.Dist);
            if (e.Dist >= total)
            {
                e.DeadAt = _sim;
                e.Hp = 0;
                _lives = Math.Max(0, _lives - e.Def.Leak);
                _floats.Add(($"-{e.Def.Leak}", _camX + FieldW - 40, e.Y - 40, _sim, Red));
                if (_lives == 0) { _phase = Phase.Lost; Record(false); host.Play(GameOverSound); }
            }
        }
        _enemies.RemoveAll(e => !e.Alive && _sim - e.DeadAt > 0.8);

        // 타워
        foreach (var t in _towers)
        {
            var (power, haste, range) = AuraOn(t);
            t.Cooldown -= dt * (1 + haste);
            if (t.Cooldown > 0) continue;
            double reach = t.Def.Stats.Range * t.Def.Power * (t.Def.Hero ? 0.6 : 1) * (1 + range) * Cell;
            reach = Math.Max(reach, t.Def.Stats.Range * Cell * (1 + range));
            Enemy? target = null;
            foreach (var e in _enemies)
                if (e.Alive && Dist2(t.X, t.Y, e.X, e.Y) <= reach * reach && (target == null || e.Dist > target.Dist)) target = e;
            if (target == null) continue;
            t.Cooldown = t.Def.Stats.Interval;
            t.AnimAt = _sim;
            var sprite = _sprites.GetValueOrDefault(t.Def.Chr);
            t.Action = t.Def.Stats.Shot > 0 && HasAction(sprite, 9) ? 9 : HasAction(sprite, 8) ? 8 : ObsMotionTable.ActionStand;
            double dx = target.X - t.X, dy = target.Y - t.Y;
            t.Facing = Math.Abs(dx) >= Math.Abs(dy) ? (dx >= 0 ? Facing.Right : Facing.Left) : (dy >= 0 ? Facing.Down : Facing.Up);
            double damage = t.Def.Stats.Damage * t.Def.Power * (1 + power);
            if (t.Def.Stats.Shot > 0) _shots.Add(new Shot(t.X, t.Y - 24, target, t, damage));
            else Strike(t, target, damage);
        }

        // 탄
        foreach (var s in _shots.ToList())
        {
            double speed = s.From.Def.Stats.Shot * Cell * dt;
            double tx = s.Target.X, ty = s.Target.Y - 22, d = Math.Sqrt(Dist2(s.X, s.Y, tx, ty));
            if (d <= speed || !s.Target.Alive)
            {
                _shots.Remove(s);
                if (s.Target.Alive) Strike(s.From, s.Target, s.Damage);
                continue;
            }
            s.X += (tx - s.X) / d * speed;
            s.Y += (ty - s.Y) / d * speed;
        }

        _fx.RemoveAll(f => _sim - f.At > 0.6);
        _floats.RemoveAll(f => _sim - f.At > 1.0);

        if (_wave >= TdData.Waves.Length && _spawns.Count == 0 && !_enemies.Any(e => e.Alive) && _phase == Phase.Playing)
        {
            _phase = Phase.Won;
            Record(true);
        }
    }

    /// <summary>파트2 게임 오버 소리 대신 — 전투에서 진 때 쓰는 효과음이 따로 없어 짧은 알림음을 쓴다.</summary>
    private const int GameOverSound = 578;

    private void Strike(Tower t, Enemy target, double damage)
    {
        var w = t.Def.Stats;
        var hit = new List<Enemy> { target };
        if (w.Splash > 0)
            hit.AddRange(_enemies.Where(e => e != target && e.Alive && Dist2(e.X, e.Y, target.X, target.Y) <= Math.Pow(w.Splash * Cell, 2)));
        if (w.Pierce > 0)
            hit.AddRange(_enemies.Where(e => e != target && e.Alive).OrderBy(e => Dist2(e.X, e.Y, target.X, target.Y))
                                 .Where(e => Dist2(e.X, e.Y, target.X, target.Y) <= Cell * Cell * 1.2).Take(w.Pierce));
        foreach (var e in hit)
        {
            double dealt = Math.Max(1, (e == target ? damage : damage * 0.6) - e.Def.Armor);
            e.Hp -= dealt;
            e.HitAt = _sim;
            if (e.Hp <= 0 && e.Alive)
            {
                e.DeadAt = _sim;
                _gold += e.Def.Bounty;
                t.Kills++;
                _floats.Add(($"+{e.Def.Bounty}", e.X, e.Y - 50, _sim, Gold));
            }
        }
        _fx.Add((target.X, target.Y - 20, _sim, w.Splash > 0 ? 1 : 0));
    }

    /// <summary>둘레 영웅들의 오라 — (공격력 + 몫, 빠르기 + 몫, 사거리 + 몫). 같은 갈래는 가장 센 것 하나.</summary>
    private (double Power, double Haste, double Range) AuraOn(Tower t)
    {
        double p = 0, h = 0, r = 0, reach = TdData.AuraRange * Cell;
        foreach (var hero in _towers)
        {
            if (!hero.Def.Hero || Dist2(hero.X, hero.Y, t.X, t.Y) > reach * reach) continue;
            switch (hero.Def.Aura)
            {
                case TdAura.Power: p = Math.Max(p, hero.Def.AuraValue); break;
                case TdAura.Haste: h = Math.Max(h, hero.Def.AuraValue); break;
                case TdAura.Range: r = Math.Max(r, hero.Def.AuraValue); break;
            }
        }
        return (p, h, r);
    }

    private static double Dist2(double ax, double ay, double bx, double by) => (ax - bx) * (ax - bx) + (ay - by) * (ay - by);

    private double Reach(Tower t)
    {
        var (_, _, range) = AuraOn(t);
        return Math.Max(t.Def.Stats.Range * t.Def.Power * (t.Def.Hero ? 0.6 : 1), t.Def.Stats.Range) * (1 + range) * Cell;
    }

    private void StartWave()
    {
        if (_wave >= TdData.Waves.Length || _phase != Phase.Playing) return;
        // 앞당겨 부르면 남은 초만큼 골드
        if (_nextWaveIn > 0)
        {
            int bonus = (int)Math.Ceiling(_nextWaveIn) * TdData.EarlyBonusPerSecond;
            _gold += bonus;
            if (bonus > 0) _floats.Add(($"앞당김 +{bonus}", _camX + FieldW / 2, _camY + 60, _sim, Gold));
        }
        _nextWaveIn = -1;
        double at = _sim + 0.5;
        foreach (var group in TdData.Waves[_wave])
        {
            for (int i = 0; i < group.Count; i++) _spawns.Add((at + i * group.Every, group.Enemy));
            at += group.Count * group.Every * 0.6;
        }
        _spawns.Sort((a, b) => a.At.CompareTo(b.At));
        _wave++;
    }

    // ── 짓기 · 팔기 ──

    private IEnumerable<int> TabCards() => Enumerable.Range(0, TdData.Towers.Length).Where(i => TdData.Towers[i].Hero == (_tab == 1));

    private bool HeroBuilt(TdTowerDef def) => def.Hero && _towers.Any(t => t.Def == def);

    private string? CannotBuild(TdTowerDef def) =>
        HeroBuilt(def) ? "영웅은 하나만 지을 수 있습니다" : _gold < def.Cost ? $"골드가 모자랍니다 ({def.Cost - _gold} 더)" : null;

    private bool CanPlace(int col, int row) =>
        Walkable(col, row) && !_pathCells.Contains((col, row)) && !_towers.Any(t => t.Col == col && t.Row == row);

    private void Place(int col, int row)
    {
        if (_pick < 0) return;
        var def = TdData.Towers[_pick];
        if (CannotBuild(def) is { } why) { host.Toast(why); _pick = -1; return; }
        if (!CanPlace(col, row)) { host.Toast("여기에는 지을 수 없습니다"); return; }
        var (x, y) = CellFoot(col, row);
        _gold -= def.Cost;
        _towers.Add(new Tower(def, col, row, x, y));
        host.Play(PlaceSound);
        // 병종은 이어서 지을 수 있게 고른 채로, 영웅 · 골드가 모자라면 풀어 둔다.
        if (def.Hero || _gold < def.Cost) _pick = -1;
    }

    /// <summary>짓는 소리 — 모세스 단추 소리를 빌린다.</summary>
    private const int PlaceSound = 578;

    private void Sell(Tower t)
    {
        int back = (int)(t.Spent * TdData.SellRate);
        _gold += back;
        _towers.Remove(t);
        _floats.Add(($"+{back}", t.X, t.Y - 50, _sim, Gold));
        _selected = null;
    }

    // ── 입력 ──

    private (int X, int Y) Local(int bx, int by)
    {
        var (ox, oy) = host.Mos.MosesOrigin();
        return (bx - ox, by - oy);
    }

    private (int Col, int Row) CellAt(int lx, int ly)
    {
        if (_img is not { } m || lx < 0 || ly < 0 || lx >= FieldW || ly >= FieldH) return (-1, -1);
        int x = lx + _camX, y = ly + _camY, col = x / Cell;
        // 높이 때문에 겹치면 앞(아래) 줄이 이긴다 — 판의 RowAt 과 같은 셈.
        for (int row = m.Rows - 1; row >= 0; row--)
        {
            int top = row * CellH - m.HeightAt(col, row) * HeightStep - m.OriginY;
            if (y >= top && y < top + CellH) return (col, row);
        }
        return (-1, -1);
    }

    /// <summary>오른쪽 위 단추들 — (이름, x, y, 폭, 높이).</summary>
    private IEnumerable<(string Id, string Label, int X, int Y, int W, int H)> Buttons()
    {
        if (_phase != Phase.Playing)
        {
            yield return ("again", "다시 하기", FieldW / 2 - 130, 250, 120, 28);
            yield return ("leave", "타이틀로", FieldW / 2 + 10, 250, 120, 28);
            yield break;
        }
        string wave = _wave >= TdData.Waves.Length ? "마지막 웨이브" : _nextWaveIn > 0 ? $"다음 웨이브 ({Math.Ceiling(_nextWaveIn)})" : _wave == 0 ? "웨이브 시작" : "웨이브 진행 중";
        yield return ("wave", wave, FieldW - 300, 6, 138, 24);
        yield return ("speed", $"속도 x{_speed}", FieldW - 156, 6, 72, 24);
        yield return ("leave", "나가기", FieldW - 78, 6, 72, 24);
        if (_selected != null) yield return ("sell", $"팔기 +{(int)(_selected.Spent * TdData.SellRate)}", FieldW - 112, PanelY - 34, 106, 26);
    }

    private string? ButtonAt(int lx, int ly) =>
        Buttons().FirstOrDefault(b => lx >= b.X && lx < b.X + b.W && ly >= b.Y && ly < b.Y + b.H).Id;

    private int CardAt(int lx, int ly)
    {
        if (ly < CardY || ly >= CardY + CardH) return -1;
        int i = 0;
        foreach (int card in TabCards())
        {
            int x = TabW + 6 + i++ * (CardW + 2);
            if (lx >= x && lx < x + CardW) return card;
        }
        return -1;
    }

    private int TabAt(int lx, int ly) => lx >= 2 && lx < TabW && ly >= PanelY + 4 && ly < PanelY + 76 ? (ly - PanelY - 4) / 36 : -1;

    internal bool OnClick(int bx, int by)
    {
        if (!Open) return false;
        var (lx, ly) = Local(bx, by);
        switch (ButtonAt(lx, ly))
        {
            case "again": Reset(); return true;
            case "leave": Leave(); return true;
            case "wave": if (_wave < TdData.Waves.Length && (_wave == 0 || _nextWaveIn > 0)) StartWave(); return true;
            case "speed": _speed = _speed == 1 ? 2 : _speed == 2 ? 3 : 1; return true;
            case "sell": if (_selected != null) Sell(_selected); return true;
        }
        if (_phase != Phase.Playing) return true;
        if (TabAt(lx, ly) is var tab and >= 0 and <= 1) { _tab = tab; _pick = -1; return true; }
        if (CardAt(lx, ly) is var card and >= 0)
        {
            _selected = null;
            if (_pick == card) { _pick = -1; return true; }
            if (CannotBuild(TdData.Towers[card]) is { } why) { host.Toast(why); return true; }
            _pick = card;
            return true;
        }
        if (ly >= FieldH) return true;
        var (col, row) = CellAt(lx, ly);
        if (_pick >= 0) { Place(col, row); return true; }
        _selected = _towers.FirstOrDefault(t => t.Col == col && t.Row == row);
        return true;
    }

    internal bool OnRightClick()
    {
        if (!Open) return false;
        _pick = -1;
        _selected = null;
        return true;
    }

    internal bool OnKey(int key)
    {
        if (!Open) return false;
        if (key == Win32.VK_ESCAPE)
        {
            if (_pick >= 0 || _selected != null) { _pick = -1; _selected = null; }
            else Leave();
        }
        else if (key == Win32.VK_SPACE && _phase == Phase.Playing && (_wave == 0 || _nextWaveIn > 0)) StartWave();
        else if (key is >= '1' and <= '9')
        {
            var cards = TabCards().ToList();
            int i = key - '1';
            if (i < cards.Count) { if (CannotBuild(TdData.Towers[cards[i]]) is { } why) host.Toast(why); else _pick = cards[i]; }
        }
        else if (key == Win32.VK_TAB) { _tab ^= 1; _pick = -1; }
        else if (key == 'S' && _selected != null) Sell(_selected);
        return true;
    }

    internal void UpdateHover(int bx, int by)
    {
        if (!Open) return;
        var (lx, ly) = Local(bx, by);
        _hoverButton = ButtonAt(lx, ly);
        _hoverCard = CardAt(lx, ly);
        _hoverCell = CellAt(lx, ly);
    }

    // ── 그리기 ──

    internal void Draw()
    {
        if (!Open) return;
        Advance();
        var (ox, oy) = host.Mos.MosesOrigin();
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        DrawMap(ox, oy);

        var clipBefore = host._uiClip;
        host._uiClip = (ox, oy, FieldW, FieldH);
        DrawPath(ox, oy);
        DrawPlacement(ox, oy);
        DrawBodies(ox, oy);
        DrawShots(ox, oy);
        host._uiClip = clipBefore;

        DrawTopBar(ox, oy);
        DrawPanel(ox, oy);
        DrawInfo(ox, oy);
        foreach (var (text, x, y, at, color) in _floats)
        {
            int sx = ox + (int)x - _camX, sy = oy + (int)(y - (_sim - at) * 30) - _camY;
            if (sy > oy && sy < oy + FieldH) Outlined(text, sx - text.Length * 3, sy, color, 12);
        }
        if (_phase != Phase.Playing) DrawResult(ox, oy);
        if (_loading is { IsCompleted: false }) Outlined($"인물 그림을 불러오는 중… {_loaded}/{_toLoad}", ox + 8, oy + FieldH - 22, Dim, 11);
        host.DrawToast();
    }

    private void DrawMap(int ox, int oy)
    {
        if (_img is not { } m) { host.UiText("맵을 못 읽었습니다", ox + 240, oy + 190, Red); return; }
        var src = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(m.Bgra.AsSpan());
        int x0 = Math.Max(0, _camX), x1 = Math.Min(m.Width, _camX + FieldW);
        for (int y = 0; y < FieldH; y++)
        {
            int my = _camY + y, by = oy + y;
            if (my < 0 || my >= m.Height || by < 0 || by >= host.BoardHeight || x0 >= x1) continue;
            int bx0 = ox + x0 - _camX, len = x1 - x0;
            if (bx0 < 0) { x0 -= bx0; len += bx0; bx0 = 0; }
            if (bx0 + len > host.BoardWidth) len = host.BoardWidth - bx0;
            if (len <= 0) continue;
            src.Slice(my * m.Width + x0, len).CopyTo(host._fb.AsSpan(by * host.BoardWidth + bx0, len));
        }
    }

    private (int X, int Y) CellTopLeft(int ox, int oy, int col, int row)
    {
        var (x, y) = CellFoot(col, row);
        return (ox + (int)x - Cell / 2 - _camX, oy + (int)y - CellH / 2 - _camY);
    }

    private void DrawPath(int ox, int oy)
    {
        foreach (var (c, r) in _pathOrder)
        {
            var (x, y) = CellTopLeft(ox, oy, c, r);
            host.FillRect(x + 2, y + 2, Cell - 4, CellH - 4, 0x50D8A050);
        }
        // 길의 들어오는 곳 · 나가는 곳
        if (_pathOrder.Count > 0)
        {
            var (sx, sy) = CellTopLeft(ox, oy, _pathOrder[0].Col, _pathOrder[0].Row);
            var (ex, ey) = CellTopLeft(ox, oy, _pathOrder[^1].Col, _pathOrder[^1].Row);
            host.StrokeRect(sx + 1, sy + 1, Cell - 2, CellH - 2, 0xFFFF6060);
            host.StrokeRect(ex + 1, ey + 1, Cell - 2, CellH - 2, 0xFF60A0FF);
        }
    }

    private void DrawPlacement(int ox, int oy)
    {
        if (_pick >= 0)
        {
            var def = TdData.Towers[_pick];
            // 지을 수 있는 칸을 옅게, 마우스 칸은 짙게 · 사거리
            for (int c = 0; c < (_img?.Cols ?? 0); c++)
                for (int r = 0; r < (_img?.Rows ?? 0); r++)
                    if (CanPlace(c, r)) { var (x, y) = CellTopLeft(ox, oy, c, r); host.FillRect(x + 1, y + 1, Cell - 2, CellH - 2, 0x2860FF60); }
            if (_hoverCell.Col >= 0)
            {
                var (x, y) = CellTopLeft(ox, oy, _hoverCell.Col, _hoverCell.Row);
                bool ok = CanPlace(_hoverCell.Col, _hoverCell.Row);
                host.FillRect(x, y, Cell, CellH, ok ? 0x7060FF60u : 0x70FF4040u);
                var (fx, fy) = CellFoot(_hoverCell.Col, _hoverCell.Row);
                double reach = Math.Max(def.Stats.Range * def.Power * (def.Hero ? 0.6 : 1), def.Stats.Range) * Cell;
                Circle(ox + (int)fx - _camX, oy + (int)fy - _camY, (int)reach, ok ? 0xC0A0FFA0u : 0xC0FF8080u);
                if (def.Hero) Circle(ox + (int)fx - _camX, oy + (int)fy - _camY, (int)(TdData.AuraRange * Cell), 0xA0FFD860);
            }
        }
        if (_selected is { } t)
        {
            int sx = ox + (int)t.X - _camX, sy = oy + (int)t.Y - _camY;
            Circle(sx, sy, (int)Reach(t), 0xE0FFFFFF);
            if (t.Def.Hero) Circle(sx, sy, (int)(TdData.AuraRange * Cell), 0xC0FFD860);
        }
    }

    private void DrawBodies(int ox, int oy)
    {
        // 발 자리 y 차례로 — 아래에 선 것이 앞에 온다.
        var items = new List<(double Y, Action Draw)>();
        foreach (var t in _towers)
        {
            var tower = t;
            items.Add((t.Y, () =>
            {
                int sx = ox + (int)tower.X - _camX, sy = oy + (int)tower.Y - _camY;
                // 영웅 발밑에는 금빛 테 — 오라가 있다는 표시
                if (tower.Def.Hero) Ellipse(sx, sy, 16, 6, 0xFFFFD860);
                else if (AuraOn(tower) is var a && a.Power + a.Haste + a.Range > 0) Ellipse(sx, sy, 13, 5, 0xA0FFE8A0);
                double age = _sim - tower.AnimAt;
                bool attacking = age < 0.6 && tower.Action != ObsMotionTable.ActionStand;
                var sprite = _sprites.GetValueOrDefault(tower.Def.Chr);
                int top = DrawSprite(sprite, attacking ? tower.Action : ObsMotionTable.ActionStand, tower.Facing, attacking ? age : _sim, !attacking, sx, sy);
                if (sprite == null) NameTag(tower.Def.Name, sx, sy - 30, tower.Def.Hero ? Gold : 0xFFFFFFFF);
                else if (tower == _selected) host.StrokeRect(sx - 18, top - 2, 36, sy - top + 6, 0xFFFFFFFF);
            }));
        }
        foreach (var e in _enemies)
        {
            var enemy = e;
            items.Add((e.Y, () =>
            {
                int sx = ox + (int)enemy.X - _camX, sy = oy + (int)enemy.Y - _camY;
                var sprite = SpriteOf(enemy.Def);
                if (!enemy.Alive)
                {
                    double fade = 1 - (_sim - enemy.DeadAt) / 0.8;
                    DrawSprite(sprite, BattleScene.HitAction, enemy.Facing, _sim - enemy.DeadAt, false, sx, sy, Math.Max(0, fade));
                    return;
                }
                bool hurt = _sim - enemy.HitAt < 0.2;
                int top = DrawSprite(sprite, hurt ? BattleScene.HitAction : ObsMotionTable.ActionWalk, enemy.Facing, hurt ? _sim - enemy.HitAt : _sim + enemy.MaxHp % 7, !hurt, sx, sy);
                if (sprite == null) NameTag(enemy.Def.Name, sx, sy - 30, Red);
                // HP 막대
                int w = enemy.MaxHp >= 800 ? 40 : 26, barY = Math.Max(oy + 2, top - 6);
                host.FillRect(sx - w / 2 - 1, barY - 1, w + 2, 5, 0xFF000000);
                host.FillRect(sx - w / 2, barY, (int)(w * Math.Clamp(enemy.Hp / enemy.MaxHp, 0, 1)), 3, enemy.MaxHp >= 800 ? 0xFFFF9030u : 0xFFE04040u);
            }));
        }
        foreach (var (_, draw) in items.OrderBy(i => i.Y)) draw();

        foreach (var (x, y, at, kind) in _fx)
        {
            int sx = ox + (int)x - _camX, sy = oy + (int)y - _camY, tick = (int)((_sim - at) * TicksPerSecond);
            if (!host.DrawUi(BattleScene.HitEffectObs, kind == 1 ? 1 : 0, tick, sx, sy, UiBlend.Add, loop: false))
            {
                int r = 4 + tick;
                Circle(sx, sy, r, 0xC0FFE080);
            }
            if (kind == 1) Circle(sx, sy, (int)(TdData.Weapons[TdWeapon.Magic].Splash * Cell * Math.Min(1, (_sim - at) * 4)), 0x80A0C0FF);
        }
    }

    private void DrawShots(int ox, int oy)
    {
        foreach (var s in _shots)
        {
            int sx = ox + (int)s.X - _camX, sy = oy + (int)s.Y - _camY;
            uint color = s.From.Def.Stats.ShotColor;
            if (s.From.Def.Weapon == TdWeapon.Magic)
            {
                host.FillRect(sx - 4, sy - 4, 8, 8, 0x8090C0FF);
                host.FillRect(sx - 2, sy - 2, 4, 4, 0xFFFFFFFF);
            }
            else
            {
                double tx = s.Target.X - s.X, ty = s.Target.Y - 22 - s.Y, d = Math.Max(1, Math.Sqrt(tx * tx + ty * ty));
                for (int i = 0; i < 7; i++) host.SetPixel(sx - (int)(tx / d * i), sy - (int)(ty / d * i), color);
                host.SetPixel(sx + 1, sy, color);
            }
        }
    }

    private void DrawTopBar(int ox, int oy)
    {
        host.FillRect(ox, oy, 330, 34, 0xA0000000);
        Outlined($"골드 {_gold}", ox + 8, oy + 9, Gold, 13);
        Outlined($"생명 {_lives}", ox + 100, oy + 9, _lives <= 5 ? Red : Green, 13);
        Outlined($"웨이브 {_wave}/{TdData.Waves.Length}", ox + 180, oy + 9, 0xFFFFFFFF, 13);
        int best = _save.BestWave.GetValueOrDefault(_map.File);
        if (best > 0) Outlined($"최고 {best}", ox + 276, oy + 11, Dim, 11);
        foreach (var (id, label, x, y, w, h) in Buttons())
            if (y < FieldH - 40 && _phase == Phase.Playing) host.Part1TitleScr.DrawButton(ox + x, oy + y, w, h, label, _hoverButton == id);
    }

    private void DrawPanel(int ox, int oy)
    {
        int py = oy + PanelY;
        host.FillRect(ox, py, FieldW, 80, 0xFF141A2C);
        host.FillRect(ox, py, FieldW, 1, 0xFF6078A0);
        string[] tabs = ["병종", "영웅"];
        for (int i = 0; i < 2; i++)
        {
            int ty = py + 4 + i * 36;
            host.FillRect(ox + 2, ty, TabW - 4, 32, _tab == i ? 0xFF34407Au : 0xFF20283Eu);
            host.StrokeRect(ox + 2, ty, TabW - 4, 32, _tab == i ? 0xFFB0D0FFu : 0xFF404C70u);
            Outlined(tabs[i], ox + 14, ty + 8, _tab == i ? 0xFFFFFFFFu : Dim, 13);
        }
        Outlined(_tab == 0 ? "병종 타워 — 1~7 키 · Tab 영웅" : "영웅 타워 — 하나씩만 · 둘레 타워를 세게 하는 오라", ox + TabW + 8, py + 4, Dim, 11);
        int n = 0;
        foreach (int card in TabCards())
        {
            var def = TdData.Towers[card];
            int x = ox + TabW + 6 + n++ * (CardW + 2), y = oy + CardY;
            bool built = HeroBuilt(def), poor = _gold < def.Cost;
            host.FillRect(x, y, CardW, CardH, _pick == card ? 0xFF3A5A30u : _hoverCard == card ? 0xFF2C3860u : 0xFF1E2640u);
            var clipBefore = host._uiClip;
            host._uiClip = (x + 1, y + 1, CardW - 2, CardH - 2);
            var sprite = _sprites.GetValueOrDefault(def.Chr);
            if (sprite != null) DrawSprite(sprite, ObsMotionTable.ActionStand, Facing.Down, 0, true, x + CardW / 2, y + CardH + 6, built || poor ? 0.45 : 1);
            host._uiClip = clipBefore;
            host.StrokeRect(x, y, CardW, CardH, _pick == card ? 0xFF90FF90u : def.Hero ? 0xFFB09040u : 0xFF404C70u);
            string cost = built ? "지음" : def.Cost.ToString();
            host.FillRect(x + 1, y + CardH - 14, CardW - 2, 13, 0xB0000000);
            Outlined(cost, x + CardW / 2 - cost.Length * 3 - 2, y + CardH - 14, built ? Dim : poor ? Red : Gold, 11);
            if (sprite == null) Outlined(def.Name.Length > 4 ? def.Name[..4] : def.Name, x + 4, y + 14, 0xFFFFFFFF, 10);
        }
    }

    /// <summary>짚은 카드 · 고른 타워의 설명 — 짓기 칸 바로 위.</summary>
    private void DrawInfo(int ox, int oy)
    {
        int card = _hoverCard >= 0 ? _hoverCard : _pick;
        string[]? lines = null;
        uint head = 0xFFFFFFFF;
        if (card >= 0)
        {
            var def = TdData.Towers[card];
            head = def.Hero ? Gold : 0xFFFFFFFF;
            lines = [$"{def.Name} — {def.Stats.Name}  ·  {def.Cost} 골드", StatLine(def, 0, 0, 0), WeaponNote(def)];
            if (def.Hero) lines = [.. lines, AuraText(def)];
        }
        else if (_selected is { } t)
        {
            head = t.Def.Hero ? Gold : 0xFFFFFFFF;
            var (p, h, r) = AuraOn(t);
            lines = [$"{t.Def.Name} — {t.Def.Stats.Name}  ·  처치 {t.Kills}", StatLine(t.Def, p, h, r), WeaponNote(t.Def)];
            if (t.Def.Hero) lines = [.. lines, AuraText(t.Def)];
            if (p + h + r > 0) lines = [.. lines, $"오라 받는 중: {(p > 0 ? $"공격 +{p:P0} " : "")}{(h > 0 ? $"속도 +{h:P0} " : "")}{(r > 0 ? $"사거리 +{r:P0}" : "")}"];
        }
        if (lines == null) return;
        int w = 300, hgt = 8 + lines.Length * 16, x0 = ox + 6, y0 = oy + PanelY - hgt - 6;
        host.FillRect(x0, y0, w, hgt, 0xD0101828);
        host.StrokeRect(x0, y0, w, hgt, 0xFF6078A0);
        for (int i = 0; i < lines.Length; i++) host.UiText(lines[i], x0 + 8, y0 + 4 + i * 16, i == 0 ? head : 0xFFD8DCE8, i == 0 ? 12 : 11);
        if (_selected != null && card < 0)
            foreach (var b in Buttons().Where(b => b.Id == "sell")) host.Part1TitleScr.DrawButton(ox + b.X, oy + b.Y, b.W, b.H, b.Label, _hoverButton == "sell");
    }

    private static string StatLine(TdTowerDef def, double power, double haste, double range)
    {
        var w = def.Stats;
        double reach = Math.Max(w.Range * def.Power * (def.Hero ? 0.6 : 1), w.Range) * (1 + range);
        return $"피해 {w.Damage * def.Power * (1 + power):0}  ·  간격 {w.Interval / (1 + haste):0.00}초  ·  사거리 {reach:0.0}칸";
    }

    private static string WeaponNote(TdTowerDef def) => def.Weapon switch
    {
        TdWeapon.Spear => $"창 — 겨눈 적 둘레 {def.Stats.Pierce}명까지 꿰뚫는다",
        TdWeapon.DualBlade => "쌍검 — 빠르게 여러 번 벤다",
        TdWeapon.GreatSword => "대검 — 둘레의 적까지 함께 벤다",
        TdWeapon.Bow => "활 — 멀리서 빠르게 쏜다",
        TdWeapon.Gun => "총 — 멀리서 세게 쏜다",
        TdWeapon.Magic => "마법 — 맞은 자리 둘레에 폭발",
        _ => "장검 — 고르게 센 근접 공격",
    };

    private static string AuraText(TdTowerDef def) => def.Aura switch
    {
        TdAura.Power => $"오라: 둘레 {TdData.AuraRange}칸 타워의 피해 +{def.AuraValue:P0}",
        TdAura.Haste => $"오라: 둘레 {TdData.AuraRange}칸 타워의 공격 속도 +{def.AuraValue:P0}",
        TdAura.Range => $"오라: 둘레 {TdData.AuraRange}칸 타워의 사거리 +{def.AuraValue:P0}",
        _ => "",
    };

    private void DrawResult(int ox, int oy)
    {
        host.FillRect(ox + FieldW / 2 - 170, oy + 150, 340, 145, 0xE0101828);
        host.StrokeRect(ox + FieldW / 2 - 170, oy + 150, 340, 145, _phase == Phase.Won ? Gold : Red);
        string title = _phase == Phase.Won ? "방어 성공!" : "방어 실패";
        Outlined(title, ox + FieldW / 2 - title.Length * 9, oy + 168, _phase == Phase.Won ? Gold : Red, 20);
        string sub = _phase == Phase.Won ? $"남은 생명 {_lives} · 타워 {_towers.Count}개" : $"웨이브 {_wave}/{TdData.Waves.Length} 에서 무너졌습니다";
        Outlined(sub, ox + FieldW / 2 - sub.Length * 5, oy + 208, 0xFFFFFFFF, 12);
        foreach (var (id, label, x, y, w, h) in Buttons()) host.Part1TitleScr.DrawButton(ox + x, oy + y, w, h, label, _hoverButton == id);
    }

    private void Outlined(string text, int x, int y, uint color, float size)
    {
        host.UiText(text, x + 1, y + 1, 0xFF000000, size);
        host.UiText(text, x, y, color, size);
    }

    private void NameTag(string name, int sx, int sy, uint color)
    {
        host.FillRect(sx - 14, sy - 14, 28, 28, 0xA0203050);
        host.StrokeRect(sx - 14, sy - 14, 28, 28, color);
        Outlined(name.Length > 3 ? name[..3] : name, sx - 14, sy + 16, color, 10);
    }

    private void Circle(int cx, int cy, int r, uint color)
    {
        if (r <= 0) return;
        int steps = Math.Max(24, r * 3);
        for (int i = 0; i < steps; i++)
        {
            double a = i * Math.PI * 2 / steps;
            int x = cx + (int)Math.Round(Math.Cos(a) * r), y = cy + (int)Math.Round(Math.Sin(a) * r * 0.8);
            if (InField(x, y)) host.SetPixel(x, y, color);
        }
    }

    private void Ellipse(int cx, int cy, int rx, int ry, uint color)
    {
        for (int i = 0; i < 48; i++)
        {
            double a = i * Math.PI * 2 / 48;
            int x = cx + (int)Math.Round(Math.Cos(a) * rx), y = cy + (int)Math.Round(Math.Sin(a) * ry);
            if (InField(x, y)) host.SetPixel(x, y, color);
        }
    }

    private bool InField(int x, int y) => host._uiClip is not { } c || (x >= c.Left && x < c.Left + c.Width && y >= c.Top && y < c.Top + c.Height);
}

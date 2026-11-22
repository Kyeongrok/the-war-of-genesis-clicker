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

    private const int FieldW = MosesScene.MosesW, FieldH = 400, PanelY = 400, Top = 36, ViewH = FieldH - Top, Cell = ObtMap.CellWidth, CellH = ObtMap.CellHeight;
    private const int CardW = 56, CardH = 54, CardY = PanelY + 22, TabW = 56;
    private const uint Gold = 0xFFFFD860, Red = 0xFFFF7070, Green = 0xFF80F080, Dim = 0xFF98A0B8;

    private enum Phase { Playing, Won, Lost, Select }

    private sealed class Tower(TdTowerDef def, int col, int row, double x, double y)
    {
        internal readonly TdTowerDef Def = def;
        internal readonly int Col = col, Row = row;
        internal readonly double X = x, Y = y;
        internal double Cooldown, AnimAt = -9;
        internal int Action;
        internal double AnimLen = 0.5, PendingAt = -1, PendingDamage;
        internal Enemy? PendingTarget;
        internal Facing Facing = Facing.Down;
        internal int Spent = def.Cost;
        internal int Kills;
    }

    private sealed class Enemy(TdEnemyDef def, int hp)
    {
        internal readonly TdEnemyDef Def = def;
        internal readonly int MaxHp = hp;
        internal double Hp = hp, Dist, X, Y, DeadAt = -1, HitAt = -9, SlowUntil = -1, Slow;
        internal Facing Facing = Facing.Right;
        internal bool Alive => DeadAt < 0;
    }

    private sealed class Shot(double x, double y, Enemy target, Tower from, double damage)
    {
        internal double X = x, Y = y;
        internal readonly Enemy Target = target;
        internal readonly Tower From = from;
        internal readonly double Damage = damage;
        internal Part1Skill.BasicStyle Style;
        internal uint Color = 0xFFFFFFFF;
        internal int AttackId;
    }

    private TdStage _stage = TdData.Stages[0];
    private TdMap _map = TdData.Stages[0].Map;
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
    private readonly List<(double X1, double Y1, double X2, double Y2, double At, uint Color)> _beams = [];
    private readonly Random _rng = new();
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

    /// <summary>타워 인물의 기본 공격 — 이 게임 동작 번호 · 핸들러 갈래 · Attack 번호(파트1).</summary>
    private readonly ConcurrentDictionary<int, (int Action, Part1Skill.BasicStyle Style, int AttackId)> _attacks = new();

    private TowerDefenseSave _save = new();

    // ── 열기 · 닫기 ──

    /// <summary><c>DUELDX_TD=1</c> 이면 타이틀을 거치지 않고 바로 연다(화면 밖 시험). 값이 2 이상이면 그 웨이브 · 골드로 시작한다(<c>DUELDX_TD_GOLD</c>).</summary>
    internal void OpenIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_TD") is not { Length: > 0 } v || !int.TryParse(v, out int n) || n <= 0) return;
        Start(int.TryParse(Environment.GetEnvironmentVariable("DUELDX_TD_STAGE"), out int st) ? st : 0);
        if (int.TryParse(Environment.GetEnvironmentVariable("DUELDX_TD_GOLD"), out int g)) _gold = g;
        if (n > 1) _wave = Math.Min(n - 1, _stage.Waves.Length - 1);
    }

    /// <summary>타워 디펜스를 연다 — <paramref name="stageIndex"/> 가 음수면 스테이지가 둘 이상일 때 고르는 창부터(편집기가 스테이지를 더한다, td-4).</summary>
    internal void Start(int stageIndex)
    {
        TdData.Reload();
        host.TitleScr._titleOpen = false;
        host.Mos._mosesOpen = false;
        if (host._part1TitleScr is { } p1) p1._open = false;
        if (host.Cols != TitleBoardCols || host.Rows != TitleBoardRows) { host.ResizeBoard(TitleBoardCols, TitleBoardRows); host._battleLoaded = false; }
        Open = true;
        _save = TowerDefenseSave.Load();
        host.StopMusic();
        if (stageIndex < 0 && TdData.Stages.Length > 1)
        {
            _phase = Phase.Select;
            _img = null;
            host.PlayMusicFile(TdData.Stages[0].Map.Bgm, loop: true);
            host.EnterSceneFade();
            return;
        }
        StartStage(Math.Max(0, stageIndex));
        host.EnterSceneFade();
    }

    private void StartStage(int index)
    {
        _stage = TdData.Stages[Math.Clamp(index, 0, TdData.Stages.Length - 1)];
        _map = _stage.Map;
        // 화면 밖 시험: DUELDX_TD_MAP=part1:0093 따위로 다른 맵을 본다.
        if (Environment.GetEnvironmentVariable("DUELDX_TD_MAP") is { Length: > 0 } test) _map = _map with { Name = "시험", File = test, Waypoints = [] };
        LoadMap();
        Reset();
        BeginLoadingSprites();
        host.StopMusic();
        host.PlayMusicFile(_map.Bgm, loop: true);
    }

    private void Reset()
    {
        _towers.Clear(); _enemies.Clear(); _shots.Clear(); _fx.Clear(); _beams.Clear(); _floats.Clear(); _spawns.Clear();
        _gold = _stage.StartGold;
        _lives = _stage.StartLives;
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
        string key = _stage.Id;
        int reached = won ? _stage.Waves.Length : Math.Max(0, _wave - 1);
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
        _camY = _map.CamY >= 0 ? _map.CamY : Math.Max(0, (_img.Height - ViewH) / 2);
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
        return x - Cell / 2 >= _camX + margin && x + Cell / 2 <= _camX + FieldW - margin && y - CellH / 2 >= _camY + margin && y + CellH / 2 <= _camY + ViewH - margin;
    }

    private bool Walkable(int col, int row) =>
        _img is { } m && col >= 0 && row >= 0 && col < m.Cols && row < m.Rows && (m.FlagsAt(col, row) & 0x9) == 0 && Visible(col, row);

    private void BuildPath()
    {
        if (_img is not { } m) return;
        // 경유점 → 걸을 수 있는 칸으로 잇기는 편집기와 같은 셈(TowerDefenseBook.BuildPath). 옛 꼴(보는 네모 안 칸)은 맵 칸으로 바꾼다.
        var marks = _map.Waypoints.Select(p => _map.ViewCells ? TowerDefenseBook.ViewToMap(m, _camX, _camY, p.Col, p.Row) : p).ToList();
        _pathOrder.AddRange(TowerDefenseBook.BuildPath(m, _camX, _camY, marks));
        if (_pathOrder.Count == 0) return;
        foreach (var c in _pathOrder) _pathCells.Add(c);

        // 입구 · 출구 칸에서 가장 가까운 화면 가장자리 밖으로 들어오고 나간다(편집기 스테이지는 위 · 아래에서도 들어온다).
        _path.Add(Outside(CellFoot(_pathOrder[0].Col, _pathOrder[0].Row)));
        foreach (var (c, r) in _pathOrder) _path.Add(CellFoot(c, r));
        _path.Add(Outside(CellFoot(_pathOrder[^1].Col, _pathOrder[^1].Row)));
        _pathLen.Add(0);
        for (int i = 1; i < _path.Count; i++)
            _pathLen.Add(_pathLen[^1] + Math.Sqrt(Math.Pow(_path[i].X - _path[i - 1].X, 2) + Math.Pow(_path[i].Y - _path[i - 1].Y, 2)));
    }

    /// <summary>그 자리에서 가장 가까운 보는 네모 가장자리의 30px 밖.</summary>
    private (double X, double Y) Outside((double X, double Y) p)
    {
        double left = p.X - _camX, right = _camX + FieldW - p.X, top = p.Y - _camY, bottom = _camY + ViewH - p.Y;
        double min = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        return min == left ? (_camX - 30, p.Y) : min == right ? (_camX + FieldW + 30, p.Y) : min == top ? (p.X, _camY - 30) : (p.X, _camY + ViewH + 30);
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
        var wanted = TdData.Towers.Select(t => t.Chr).Concat(_stage.Enemies.Select(e => e.Chr)).Distinct().Where(c => !_sprites.ContainsKey(c)).ToList();
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
            // 타워의 기본 공격(파트1 인물 레코드)과 그 동작의 소리를 미리 — 처음 칠 때 조용하지 않게.
            foreach (var def in TdData.Towers)
            {
                var (action, _, _) = AttackOf(def);
                if (_sprites.GetValueOrDefault(def.Chr) is { } sp)
                    host.PreloadSounds([.. new[] { action, 8, 9 }.SelectMany(a => new[] { Facing.Left, Facing.Up, Facing.Down }.Select(f => sp.Clip(a, f)))
                                                     .Where(c => c != null).SelectMany(c => c!.Sounds.Select(x => x.Sound))]);
            }
            // 탄 · 맞은 자리 소리(전투의 기본 공격 핸들러가 따로 내는 것) — 활 시위 56 · 화살 793 · 맞은 자리 183, CSound 2038 · 마법 6 · 81 · 80
            host.PreloadSounds([.. new[] { 2038, 6, 81, 80 }.Select(n => Part1Game.ObsBase + n),
                                .. new[] { (56, 1), (793, 4), (183, 0), (183, 1), (183, 2), (55, 1), (198, 0), (48, 0), (275, 4), (51, 0), (276, 0), (71, 0), (505, 9) }.SelectMany(o => host.UiFor(Part1Game.ObsBase + o.Item1)?.Clip(o.Item2)?.Sounds.Select(x => x.Sound) ?? [])]);
            // 파트1 그림이 없는 적은 파트2 병사로.
            foreach (var e in _stage.Enemies.Where(e => _sprites.GetValueOrDefault(e.Chr) == null && !_sprites.ContainsKey(e.Fallback)))
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

    /// <summary>
    /// 타워 인물의 기본 공격 — 파트1 인물은 레코드 칸 9(없으면 무기 갈래)의 Attack 번호로 전투와 같은 표를 쓴다(<see cref="Part1Skill.BasicKind"/> ·
    /// 동작은 칼 1 = 2 · 3, 맨 갈래 2~5 = 2, 빔라이플 10 = 3, 용병 갈래 = 2 + 레벨 → <see cref="Part1Skill.Part2Action"/>).
    /// 파트2 인물은 무기 갈래로 근접 동작 8 · 쏘기 9.
    /// </summary>
    private (int Action, Part1Skill.BasicStyle Style, int AttackId) AttackOf(TdTowerDef def)
    {
        if (_attacks.TryGetValue(def.Chr, out var known)) return known;
        var style = def.Weapon switch
        {
            TdWeapon.Gun => Part1Skill.BasicStyle.Shot, TdWeapon.Beam => Part1Skill.BasicStyle.Beam, TdWeapon.Bow => Part1Skill.BasicStyle.Arrow,
            TdWeapon.Magic or TdWeapon.Frost => Part1Skill.BasicStyle.Missile, _ => Part1Skill.BasicStyle.Melee,
        };
        var info = (style == Part1Skill.BasicStyle.Melee ? 8 : 9, style, 0);
        try
        {
            if (def.Chr >= Part1Game.ChrBase && Part1Chr.Load(def.Chr - Part1Game.ChrBase) is { } c)
            {
                int id = c.BasicAttackId > 0 ? c.BasicAttackId : Part1Item.BasicAttack(c.Equipment[0] > 0 ? Part1Item.ById(Part1Tables.Items, c.Equipment[0]) : null);
                if (Part1Skill.BasicKind(id) is var kind and not Part1Skill.BasicStyle.None)
                {
                    int part1 = id switch { 1 or 2 or 3 or 4 or 5 => 2, 10 or 362 => 3, 280 => 20, _ => 2 + Part1Skill.BasicLevel(id) };
                    info = (Part1Skill.Part2Action(part1), kind, id);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException) { }
        return _attacks[def.Chr] = info;
    }

    /// <summary>탄의 빠르기(칸/초) — 총알이 가장 빠르고 마법탄이 가장 느리다.</summary>
    private static double ShotSpeed(Part1Skill.BasicStyle style) => style switch
    {
        Part1Skill.BasicStyle.Shot or Part1Skill.BasicStyle.Volley => 26,
        Part1Skill.BasicStyle.Arrow or Part1Skill.BasicStyle.Throw or Part1Skill.BasicStyle.Boomerang => 13,
        Part1Skill.BasicStyle.Cannon => 10,
        _ => 8,
    };

    /// <summary>탄 색 — 화이어 애로우 324~ 주황 · 아이스미사일 327~ 하늘 · 라이트닝볼츠 330~ 노랑, 총알 노랑, 화살 · 창 나무색.</summary>
    private static uint ShotColorOf(Part1Skill.BasicStyle style, int attackId) => style switch
    {
        Part1Skill.BasicStyle.Missile => attackId switch { >= 330 and <= 332 => 0xFFFFFF80, >= 327 and <= 329 => 0xFF90E8FF, >= 324 and <= 326 => 0xFFFF9040, _ => 0xFF90C0FF },
        Part1Skill.BasicStyle.Shot or Part1Skill.BasicStyle.Volley => 0xFFFFF070,
        _ => 0xFFE8D8A0,
    };

    /// <summary>그 동작 모션의 소리 키(자식 그림의 소리까지)를 판 시계에 맞춰 건다 — 전투의 ScheduleActionSounds 와 같은 셈.</summary>
    private void QueueSounds(ObsMotionClip? clip, double x)
    {
        if (clip == null) return;
        var (ox, _) = host.Mos.MosesOrigin();
        float sx = (float)(ox + x - _camX);
        double t0 = host._lastTime;
        foreach (var (start, sound) in clip.Sounds) host._pendingSounds.Add((t0 + start / TicksPerSecond / _speed, sound, sx));
        foreach (var (start, obs, motion, _, _, _, _) in clip.Children)
            if (host.UiFor(obs)?.Clip(motion) is { } child)
                foreach (var (cs, sound) in child.Sounds) host._pendingSounds.Add((t0 + (start + cs) / TicksPerSecond / _speed, sound, sx));
    }

    /// <summary>파트1 그림(Obs 번호) 한 모션의 소리 키를 지금부터 건다.</summary>
    private void ObsSounds(int part1Obs, int motion, double x) => QueueSounds(host.UiFor(Part1Game.ObsBase + part1Obs)?.Clip(motion), x);

    /// <summary>파트1 소리(CSound 번호) 하나를 지금 낸다.</summary>
    private void PlayPart1(int sound, double x)
    {
        var (ox, _) = host.Mos.MosesOrigin();
        host._pendingSounds.Add((host._lastTime, Part1Game.ObsBase + sound, (float)(ox + x - _camX)));
    }

    /// <summary>탄이 닿을 때의 소리 — 화살 · 던지기는 CSound 2038, 화살 · 총알은 맞은 자리 그림(183)의 소리 키도(전투와 같다).</summary>
    private void Arrived(Shot s)
    {
        if (s.Style is Part1Skill.BasicStyle.Arrow or Part1Skill.BasicStyle.Throw or Part1Skill.BasicStyle.Boomerang) PlayPart1(2038, s.X);
        if (s.Style is Part1Skill.BasicStyle.Arrow or Part1Skill.BasicStyle.Shot or Part1Skill.BasicStyle.Volley) ObsSounds(183, _rng.Next(3), s.X);
        if (s.Style == Part1Skill.BasicStyle.Missile) ObsSounds(MissileParts(s.AttackId).Burst, 0, s.X);
    }

    /// <summary>
    /// 용병 마법 셋의 소리 · 그림(전투 BasicMissile 과 같은 표) — 화이어 애로우(소리 6 · 빛 55 · 불덩이 198 · 터짐 48), 아이스미사일(81 · 56 · 얼음살 275 · 51),
    /// 라이트닝볼츠(80 · 56 · 276 · 71). 아이스 마스터는 아이스미사일 327 이라 얼음 쏘는 소리 81 과 얼음살 · 터짐 소리 키가 난다(td-5).
    /// </summary>
    private static (int Sound, int Glow, int Shot, int Burst) MissileParts(int attackId) => attackId switch
    {
        <= 326 => (6, 55, 198, 48),
        <= 329 => (81, 56, 275, 51),
        _ => (80, 56, 276, 71),
    };

    private Enemy? TargetFor(Tower t, double reach)
    {
        Enemy? target = null;
        foreach (var e in _enemies)
            if (e.Alive && Dist2(t.X, t.Y, e.X, e.Y) <= reach * reach && (target == null || e.Dist > target.Dist)) target = e;
        return target;
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
            var def = _stage.Enemies[_spawns[0].Enemy];
            int hp = (int)(def.Hp * (1 + _stage.HpPerWave * (_wave - 1)));
            var e = new Enemy(def, hp);
            (e.X, e.Y, e.Facing) = PointAt(0);
            _enemies.Add(e);
            _spawns.RemoveAt(0);
            if (_spawns.Count == 0 && _wave < _stage.Waves.Length) _nextWaveIn = 18;
        }

        // 적 걷기
        double total = _pathLen.Count > 0 ? _pathLen[^1] : 0;
        foreach (var e in _enemies.Where(e => e.Alive))
        {
            e.Dist += e.Def.Speed * (_sim < e.SlowUntil ? 1 - e.Slow : 1) * Cell * dt;
            (e.X, e.Y, e.Facing) = PointAt(e.Dist);
            if (e.Dist >= total)
            {
                e.DeadAt = _sim;
                e.Hp = 0;
                _lives = Math.Max(0, _lives - e.Def.Leak);
                _floats.Add(($"-{e.Def.Leak}", Math.Clamp(e.X, _camX + 20, _camX + FieldW - 40), Math.Clamp(e.Y - 40, _camY + 10, _camY + ViewH - 20), _sim, Red));
                if (_lives == 0) { _phase = Phase.Lost; Record(false); host.Play(GameOverSound); }
            }
        }
        _enemies.RemoveAll(e => !e.Alive && _sim - e.DeadAt > 0.8);

        // 타워 — 겨누면 공격 동작을 걸고, 그 모션의 타격 키(없으면 첫 소리 키 · 길이 절반)에서 친다 · 쏜다.
        // 간격은 모션 길이보다 짧지 않게 하고 그만큼 피해를 키워 초당 피해는 그대로 둔다(동작이 끊기지 않게).
        foreach (var t in _towers)
        {
            var (power, haste, _) = AuraOn(t);
            if (t.PendingAt >= 0 && _sim >= t.PendingAt) { t.PendingAt = -1; Release(t); }
            t.Cooldown -= dt * (1 + haste);
            if (t.Cooldown > 0 || t.PendingAt >= 0) continue;
            if (TargetFor(t, Reach(t)) is not { } target) continue;
            var sprite = _sprites.GetValueOrDefault(t.Def.Chr);
            var atk = AttackOf(t.Def);
            int action = atk.AttackId == 1 ? Part1Skill.Part2Action(_rng.Next(2) == 0 ? 2 : 3) : atk.Action;
            int plain = atk.Style == Part1Skill.BasicStyle.Melee ? 8 : 9;
            if (!HasAction(sprite, action)) action = HasAction(sprite, plain) ? plain : HasAction(sprite, 8) ? 8 : ObsMotionTable.ActionStand;
            double dx = target.X - t.X, dy = target.Y - t.Y;
            t.Facing = Math.Abs(dx) >= Math.Abs(dy) * 1.25 ? (dx >= 0 ? Facing.Right : Facing.Left) : (dy >= 0 ? Facing.Down : Facing.Up);
            var clip = sprite?.Clip(action, t.Facing);
            double len = sprite?.ActionSeconds(action, t.Facing) ?? 0;
            if (len <= 0) len = 0.5;
            double hit = clip is { Hits.Count: > 0 } ? clip.Hits[0].Start / TicksPerSecond : clip is { Sounds.Count: > 0 } ? clip.Sounds[0].Start / TicksPerSecond : len * 0.5;
            double interval = Math.Max(t.Def.Stats.Interval, len);
            t.Cooldown = interval;
            t.AnimAt = _sim;
            t.AnimLen = len;
            t.Action = action;
            t.PendingAt = _sim + Math.Min(hit, len);
            t.PendingTarget = target;
            t.PendingDamage = t.Def.Stats.Damage * t.Def.Power * (1 + power) * interval / t.Def.Stats.Interval;
            QueueSounds(clip, t.X);
        }

        // 탄
        foreach (var s in _shots.ToList())
        {
            double speed = ShotSpeed(s.Style) * Cell * dt;
            double tx = s.Target.X, ty = s.Target.Y - 22, d = Math.Sqrt(Dist2(s.X, s.Y, tx, ty));
            if (d <= speed || !s.Target.Alive)
            {
                _shots.Remove(s);
                if (s.Target.Alive) Strike(s.From, s.Target, s.Damage);
                Arrived(s);
                continue;
            }
            s.X += (tx - s.X) / d * speed;
            s.Y += (ty - s.Y) / d * speed;
        }

        _fx.RemoveAll(f => _sim - f.At > 0.6);
        _beams.RemoveAll(b => _sim - b.At > 0.3);
        _floats.RemoveAll(f => _sim - f.At > 1.0);

        if (_wave >= _stage.Waves.Length && _spawns.Count == 0 && !_enemies.Any(e => e.Alive) && _phase == Phase.Playing)
        {
            _phase = Phase.Won;
            Record(true);
        }
    }

    /// <summary>파트2 게임 오버 소리 대신 — 전투에서 진 때 쓰는 효과음이 따로 없어 짧은 알림음을 쓴다.</summary>
    private const int GameOverSound = 578;

    /// <summary>타격 키 — 칼은 그 자리에서 치고, 빔은 곧은 줄로 꿰뚫고, 나머지는 탄을 쏜다. 겨눈 적이 그새 죽거나 벗어났으면 다른 적을.</summary>
    private void Release(Tower t)
    {
        double reach = Reach(t) * 1.15;
        var target = t.PendingTarget is { Alive: true } p && Dist2(t.X, t.Y, p.X, p.Y) <= reach * reach ? p : TargetFor(t, reach);
        if (target == null) return;
        var atk = AttackOf(t.Def);
        double muzzle = t.Facing == Facing.Right ? 12 : t.Facing == Facing.Left ? -12 : 0;
        switch (atk.Style)
        {
            case Part1Skill.BasicStyle.Melee:
                Strike(t, target, t.PendingDamage);
                break;
            case Part1Skill.BasicStyle.Beam:
                ObsSounds(505, 9, t.X);   // 빔 줄 그림의 소리 키(전투 BasicBeam 과 같다)
                _beams.Add((t.X + muzzle, t.Y - 28, target.X, target.Y - 22, _sim, t.Def.Stats.ShotColor != 0 ? t.Def.Stats.ShotColor : 0xFF80FFFF));
                Strike(t, target, t.PendingDamage);
                break;
            default:
                _shots.Add(new Shot(t.X + muzzle, t.Y - 28, target, t, t.PendingDamage) { Style = atk.Style, Color = ShotColorOf(atk.Style, atk.AttackId), AttackId = atk.AttackId });
                // 쏘는 순간의 소리 — 활은 시위(56) · 화살(793) 그림의 소리 키, 마법탄은 불 6 · 얼음 81 · 번개 80(전투 BasicArrow · BasicMissile 과 같다)
                if (atk.Style == Part1Skill.BasicStyle.Arrow) { ObsSounds(56, 1, t.X); ObsSounds(793, 4, t.X); }
                else if (atk.Style == Part1Skill.BasicStyle.Missile)
                {
                    var (sound, glow, shot, _) = MissileParts(atk.AttackId);
                    PlayPart1(sound, t.X);
                    ObsSounds(glow, 1, t.X);
                    ObsSounds(shot, shot == 275 ? 4 : 0, t.X);
                }
                break;
        }
    }

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
            if (w.Slow > 0)
            {
                double slow = e.MaxHp >= 800 ? w.Slow * 0.5 : w.Slow;
                if (_sim >= e.SlowUntil || slow >= e.Slow) e.Slow = slow;
                e.SlowUntil = _sim + w.SlowSeconds;
            }
            if (e.Hp <= 0 && e.Alive)
            {
                e.DeadAt = _sim;
                _gold += e.Def.Bounty;
                t.Kills++;
                _floats.Add(($"+{e.Def.Bounty}", e.X, e.Y - 50, _sim, Gold));
            }
        }
        _fx.Add((target.X, target.Y - 20, _sim, w.Slow > 0 ? 2 : w.Splash > 0 ? 1 : 0));
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
        if (_wave >= _stage.Waves.Length || _phase != Phase.Playing) return;
        // 앞당겨 부르면 남은 초만큼 골드
        if (_nextWaveIn > 0)
        {
            int bonus = (int)Math.Ceiling(_nextWaveIn) * TdData.EarlyBonusPerSecond;
            _gold += bonus;
            if (bonus > 0) _floats.Add(($"앞당김 +{bonus}", _camX + FieldW / 2, _camY + 60, _sim, Gold));
        }
        _nextWaveIn = -1;
        double at = _sim + 0.5;
        foreach (var group in _stage.Waves[_wave])
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
        if (_img is not { } m || lx < 0 || ly < Top || lx >= FieldW || ly >= FieldH) return (-1, -1);
        int x = lx + _camX, y = ly - Top + _camY, col = x / Cell;
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
        if (_phase == Phase.Select)
        {
            // 스테이지 고르기 — 한 줄에 8개, 넘치면 오른쪽 줄
            for (int i = 0; i < TdData.Stages.Length; i++)
            {
                var st = TdData.Stages[i];
                int best = _save.BestWave.GetValueOrDefault(st.Id), clears = _save.Clears.GetValueOrDefault(st.Id);
                string mark = clears > 0 ? $"  ★{clears}" : best > 0 ? $"  최고 {best}/{st.Waves.Length}" : "";
                int col = i / 8, cols = Math.Min(2, (TdData.Stages.Length + 7) / 8), w = cols == 1 ? 360 : 290;
                yield return ($"stage:{i}", $"{i + 1}. {st.Map.Name}{mark}", FieldW / 2 - (cols * (w + 10) - 10) / 2 + col * (w + 10), 92 + i % 8 * 36, w, 28);
            }
            yield return ("editor", "편집기", FieldW / 2 - 130, 400, 120, 28);
            yield return ("leave", "타이틀로", FieldW / 2 + 10, 400, 120, 28);
            yield break;
        }
        if (_phase != Phase.Playing)
        {
            bool many = TdData.Stages.Length > 1;
            yield return ("again", "다시 하기", FieldW / 2 - (many ? 195 : 130), 250, 120, 28);
            if (many) yield return ("stages", "스테이지", FieldW / 2 - 60, 250, 120, 28);
            yield return ("leave", "타이틀로", FieldW / 2 + (many ? 75 : 10), 250, 120, 28);
            yield break;
        }
        string wave = _wave >= _stage.Waves.Length ? "마지막 웨이브" : _nextWaveIn > 0 ? $"다음 웨이브 ({Math.Ceiling(_nextWaveIn)})" : _wave == 0 ? "웨이브 시작" : "웨이브 진행 중";
        yield return ("editor", "편집기", FieldW - 366, 6, 62, 24);
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
        string? button = ButtonAt(lx, ly);
        if (button?.StartsWith("stage:", StringComparison.Ordinal) == true) { StartStage(int.Parse(button[6..])); return true; }
        switch (button)
        {
            case "again": Reset(); return true;
            case "stages": _phase = Phase.Select; _img = null; return true;
            case "editor": EditorLauncher.Open(host.Toast, "--open=towerdefense"); return true;
            case "leave": Leave(); return true;
            case "wave": if (_wave < _stage.Waves.Length && (_wave == 0 || _nextWaveIn > 0)) StartWave(); return true;
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
        if (ly >= FieldH || ly < Top) return true;
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
        if (_phase == Phase.Select)
        {
            if (key == Win32.VK_ESCAPE) Leave();
            else if (key is >= '1' and <= '9' && key - '1' < TdData.Stages.Length) StartStage(key - '1');
            return true;
        }
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
        int fy = oy + Top;   // 맵 칸의 윗끝 — 위 막대(골드 · 단추)가 적을 가리지 않게 그 아래부터 맵을 깐다(td-2)
        host.FillRect(host._camX, host._camY, host.ViewWidth, host.ViewHeight, 0xFF000000);
        if (_phase == Phase.Select) { DrawSelect(ox, oy); host.DrawToast(); return; }
        DrawMap(ox, fy);

        var clipBefore = host._uiClip;
        host._uiClip = (ox, fy, FieldW, ViewH);
        DrawPath(ox, fy);
        DrawGates(ox, fy);
        DrawPlacement(ox, fy);
        DrawBodies(ox, fy);
        DrawShots(ox, fy);
        DrawBeams(ox, fy);
        host._uiClip = clipBefore;

        DrawTopBar(ox, oy);
        DrawPanel(ox, oy);
        DrawInfo(ox, oy);
        foreach (var (text, x, y, at, color) in _floats)
        {
            int sx = ox + (int)x - _camX, sy = fy + (int)(y - (_sim - at) * 30) - _camY;
            if (sy > fy && sy < oy + FieldH) Outlined(text, sx - text.Length * 3, sy, color, 12);
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
        for (int y = 0; y < ViewH; y++)
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

    /// <summary>
    /// 길 — 입구(초록)에서 출구(빨강)로 색이 바뀌고, 칸마다 다음 칸 쪽 화살표, 그 위로 밝은 띠가 입구에서 출구로 흐른다(td-2: 「어디가 시작이고 끝인지 한눈에」).
    /// </summary>
    private void DrawPath(int ox, int oy)
    {
        int n = _pathOrder.Count, flow = (int)(_sim * 8) % Math.Max(1, n + 6);
        for (int i = 0; i < n; i++)
        {
            var (c, r) = _pathOrder[i];
            var (x, y) = CellTopLeft(ox, oy, c, r);
            double k = n > 1 ? i / (double)(n - 1) : 0;
            uint rr = (uint)(80 + 175 * k), gg = (uint)(220 - 150 * k), bb = 70;
            bool lit = i <= flow && i > flow - 3;
            host.FillRect(x + 2, y + 2, Cell - 4, CellH - 4, (lit ? 0x90000000u : 0x48000000u) | rr << 16 | gg << 8 | bb);
            var next = i + 1 < n ? _pathOrder[i + 1] : (c + 1, r);
            Arrow(x + Cell / 2, y + CellH / 2, Math.Sign(next.Item1 - c), Math.Sign(next.Item2 - r), lit ? 0xFFFFFFFF : 0xC0FFFFFF);
        }
    }

    /// <summary>(cx, cy) 가운데 (dx, dy) 쪽을 가리키는 작은 화살표.</summary>
    private void Arrow(int cx, int cy, int dx, int dy, uint color)
    {
        for (int i = 0; i < 6; i++)
            for (int j = -i; j <= i; j++)
            {
                int along = 3 - i, side = j;
                int px = cx + along * dx - side * dy, py = cy + along * dy + side * dx;
                if (InField(px, py)) host.SetPixel(px, py, color);
            }
    }

    /// <summary>입구 · 출구 깃발 — 칸 위에 이름표.</summary>
    private void DrawGates(int ox, int oy)
    {
        if (_pathOrder.Count == 0) return;
        Gate(ox, oy, _pathOrder[0], "입구", 0xFF40C060);
        Gate(ox, oy, _pathOrder[^1], "출구", 0xFFE04848);
    }

    private void Gate(int ox, int oy, (int Col, int Row) cell, string label, uint color)
    {
        var (x, y) = CellTopLeft(ox, oy, cell.Col, cell.Row);
        bool pulse = (int)(_sim * 3) % 2 == 0;
        host.StrokeRect(x, y, Cell, CellH, color);
        host.StrokeRect(x + 1, y + 1, Cell - 2, CellH - 2, pulse ? 0xFFFFFFFF : color);
        int lx = Math.Clamp(x + Cell / 2 - 18, ox + 2, ox + FieldW - 40), ly = y - 20 < oy + 2 ? y + CellH + 2 : y - 20;
        host.FillRect(lx, ly, 36, 17, color);
        host.StrokeRect(lx, ly, 36, 17, 0xFFFFFFFF);
        Outlined(label, lx + 5, ly + 1, 0xFFFFFFFF, 11);
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
                bool attacking = age < tower.AnimLen && tower.Action != ObsMotionTable.ActionStand;
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
                // 늦춰진 적 — 발밑 하늘색 고리와 HP 막대 위 하늘색 줄
                if (_sim < enemy.SlowUntil)
                {
                    Ellipse(sx, sy, 14, 5, 0xC090E8FF);
                    host.FillRect(sx - w / 2, barY - 3, (int)(w * Math.Clamp((enemy.SlowUntil - _sim) / 2.0, 0, 1)), 2, 0xFF90E8FF);
                }
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
            if (kind == 2) Circle(sx, sy, (int)(TdData.Weapons[TdWeapon.Frost].Splash * Cell * Math.Min(1, (_sim - at) * 4)), 0x8090E8FF);
            if (kind == 1) Circle(sx, sy, (int)(TdData.Weapons[TdWeapon.Magic].Splash * Cell * Math.Min(1, (_sim - at) * 4)), 0x80A0C0FF);
        }
    }

    private void DrawShots(int ox, int oy)
    {
        foreach (var s in _shots)
        {
            int sx = ox + (int)s.X - _camX, sy = oy + (int)s.Y - _camY;
            uint color = s.Color;
            double tx = s.Target.X - s.X, ty = s.Target.Y - 22 - s.Y, d = Math.Max(1, Math.Sqrt(tx * tx + ty * ty));
            if (s.Style == Part1Skill.BasicStyle.Missile)
            {
                host.FillRect(sx - 5, sy - 5, 10, 10, (color & 0x00FFFFFF) | 0x50000000);
                host.FillRect(sx - 3, sy - 3, 6, 6, (color & 0x00FFFFFF) | 0xB0000000);
                host.FillRect(sx - 1, sy - 1, 3, 3, 0xFFFFFFFF);
            }
            else
            {
                int tail = s.Style is Part1Skill.BasicStyle.Shot or Part1Skill.BasicStyle.Volley ? 10 : 14;
                Line(sx, sy, sx - (int)(tx / d * tail), sy - (int)(ty / d * tail), color, s.Style == Part1Skill.BasicStyle.Throw ? 2 : 1);
            }
        }
    }

    private void DrawBeams(int ox, int oy)
    {
        foreach (var (x1, y1, x2, y2, at, color) in _beams)
        {
            double k = Math.Clamp(1 - (_sim - at) / 0.3, 0, 1);
            int ax = ox + (int)x1 - _camX, ay = oy + (int)y1 - _camY, bx = ox + (int)x2 - _camX, by = oy + (int)y2 - _camY;
            uint a = (uint)(k * 255) << 24, half = (uint)(k * 110) << 24;
            Line(ax, ay, bx, by, (color & 0x00FFFFFF) | half, 7);
            Line(ax, ay, bx, by, (color & 0x00FFFFFF) | a, 3);
            Line(ax, ay, bx, by, 0x00FFFFFF | a, 1);
        }
    }

    /// <summary>굵기 <paramref name="thick"/> 의 곧은 줄(맵 칸 안만).</summary>
    private void Line(int x1, int y1, int x2, int y2, uint color, int thick)
    {
        int n = Math.Max(1, Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1)));
        bool flat = Math.Abs(x2 - x1) > Math.Abs(y2 - y1);
        for (int i = 0; i <= n; i++)
        {
            int x = x1 + (x2 - x1) * i / n, y = y1 + (y2 - y1) * i / n;
            for (int o = -(thick / 2); o <= thick / 2; o++)
            {
                int px = flat ? x : x + o, py = flat ? y + o : y;
                if (InField(px, py)) host.SetPixel(px, py, color);
            }
        }
    }

    private void DrawTopBar(int ox, int oy)
    {
        host.FillRect(ox, oy, FieldW, Top, 0xFF10141E);
        host.FillRect(ox, oy + Top - 1, FieldW, 1, 0xFF404C70);
        Outlined($"골드 {_gold}", ox + 8, oy + 9, Gold, 13);
        Outlined($"생명 {_lives}", ox + 100, oy + 9, _lives <= 5 ? Red : Green, 13);
        int best = _save.BestWave.GetValueOrDefault(_stage.Id);
        Outlined($"웨이브 {_wave}/{_stage.Waves.Length}", ox + 180, oy + (best > 0 ? 3 : 9), 0xFFFFFFFF, 13);
        if (best > 0) Outlined($"최고 {best}", ox + 182, oy + 21, Dim, 9);   // 웨이브 밑 — 오른쪽에 「편집기」 단추가 선다(td-6)
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
        Outlined(_tab == 0 ? $"병종 타워 — 1~{Math.Min(9, TdData.Towers.Count(d => !d.Hero))} 키 · Tab 영웅" : "영웅 타워 — 하나씩만 · 둘레 타워를 세게 하는 오라", ox + TabW + 8, py + 4, Dim, 11);
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
        TdWeapon.Frost => $"냉기 — 맞은 자리 둘레 적을 {def.Stats.SlowSeconds:0}초 동안 {def.Stats.Slow:P0} 느리게",
        _ => "장검 — 고르게 센 근접 공격",
    };

    private static string AuraText(TdTowerDef def) => def.Aura switch
    {
        TdAura.Power => $"오라: 둘레 {TdData.AuraRange}칸 타워의 피해 +{def.AuraValue:P0}",
        TdAura.Haste => $"오라: 둘레 {TdData.AuraRange}칸 타워의 공격 속도 +{def.AuraValue:P0}",
        TdAura.Range => $"오라: 둘레 {TdData.AuraRange}칸 타워의 사거리 +{def.AuraValue:P0}",
        _ => "",
    };

    /// <summary>스테이지 고르기 — 편집기(타워 디펜스)에서 스테이지를 둘 이상 만들면 타이틀 단추가 이 창을 연다.</summary>
    private void DrawSelect(int ox, int oy)
    {
        host.FillRect(ox, oy, FieldW, 480, 0xFF0C1018);
        Outlined("타워 디펜스 — 스테이지", ox + FieldW / 2 - 110, oy + 40, Gold, 20);
        foreach (var (id, label, x, y, w, h) in Buttons()) host.Part1TitleScr.DrawButton(ox + x, oy + y, w, h, label, _hoverButton == id);
    }

    private void DrawResult(int ox, int oy)
    {
        host.FillRect(ox + FieldW / 2 - 170, oy + 150, 340, 145, 0xE0101828);
        host.StrokeRect(ox + FieldW / 2 - 170, oy + 150, 340, 145, _phase == Phase.Won ? Gold : Red);
        string title = _phase == Phase.Won ? "방어 성공!" : "방어 실패";
        Outlined(title, ox + FieldW / 2 - title.Length * 9, oy + 168, _phase == Phase.Won ? Gold : Red, 20);
        string sub = _phase == Phase.Won ? $"남은 생명 {_lives} · 타워 {_towers.Count}개" : $"웨이브 {_wave}/{_stage.Waves.Length} 에서 무너졌습니다";
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

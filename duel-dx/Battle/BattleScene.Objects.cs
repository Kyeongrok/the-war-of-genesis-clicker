using WarOfGenesis.Assets;
namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 전투판에 놓인 물체 — 상자·문·포탑·바리케이트(분석-전투 「물체(오브젝트) 배열 <c>+0x3c74</c>」).
/// </summary>
/// <remarks>
/// 물체는 <b>파일이 놓는 것이 전부</b>다 — 전투 중에 새로 생기지 않는다. 원본처럼 제 Obt 를 판에 <b>찍어</b>
/// 발자국 칸의 플래그·높이를 바꾼다(아래 「물체 도장 판」, 감사3 R1) — 상자·포탑·바리케이트·크리스탈·닫힌 문은 벽이다.
/// 열린 문(종류 1·4)은 도장에서 빠져 지나갈 수 있지만 걸어 들어갈 목표 칸은 될 수 없다(<c>0x100746b0</c>).
/// </remarks>
internal sealed unsafe partial class BattleScene
{
    internal IReadOnlyList<DemoObject> Objects => host._scene.Objects ?? [];

    /// <summary>
    /// 그 칸의 물체 — 없으면 null. 원본 물체 격자 <c>+0x74</c>(<c>0x100d93e0</c>)처럼 <b>Obt 발자국 전체</b>(비트 0x10 아닌 칸)가 제 칸이고,
    /// <b>열린 문·연 상자도 돌려준다</b>(열린 문은 도장은 빠져도 격자에 남는다, 감사3 R1). 부서진 물체·터진 폭탄 상자만 뺀다.
    /// 예전에는 그림 폭(.obj 16 ≥ 36 이면 두 칸)으로 짐작하고 연 물체를 뺐다 — 1×2 바리케이트·3×1 문(Obj 71)이 틀렸다.
    /// </summary>
    internal DemoObject? ObjectAt(int col, int row)
    {
        EnsureStamp();
        if ((uint)col >= (uint)_stampCols || (uint)row >= (uint)_stampRows) return null;
        int k = _objGrid[row * _stampCols + col];
        var objects = Objects;
        return k >= 0 && k < objects.Count && !ObjectGone(objects[k]) ? objects[k] : null;
    }

    /// <summary>판에서 사라진 물체 — 부서졌거나(<c>+0x101</c>) 터진 폭탄 상자.</summary>
    internal bool ObjectGone(DemoObject o) => !o.Alive || (o.Data.Kind == 8 && _opened.Contains(o));

    /// <summary>
    /// Obt 가 없을 때의 짐작 폭 — 그림 보정 가로(.obj 16)가 36 이상이면 두 칸(예전 규칙). 저장소에 Obt 가 없는 물체만 쓴다.
    /// </summary>
    internal static int ObjectWidth(DemoObject o) => o.Data.DrawW >= 36 ? 2 : 1;

    /// <summary>물체 발자국 칸들(판 좌표) — Obt 격자에서 비트 0x10 이 아닌 칸. Obt 가 없으면 그림 폭으로 짐작한 가로 칸들.</summary>
    internal static IEnumerable<(int Col, int Row, int Raw, ushort Flags)> FootprintCells(DemoObject o)
    {
        if (o.Footprint is not { } fp)
        {
            for (int c = 0; c < ObjectWidth(o); c++) yield return (o.Col + c, o.Row, 0, 0x1);
            yield break;
        }
        for (int r = 0; r < fp.Rows; r++)
            for (int c = 0; c < fp.Cols; c++)
            {
                int i = r * fp.Cols + c;
                if ((fp.Flags[i] & 0x10) == 0) yield return (o.Col + c, o.Row + r, fp.Raw[i], fp.Flags[i]);
            }
    }

    // ── 물체 도장 판(감사3 R1) ─────────────────────────────────────────────────────────────
    // 원본 판(CBMap)은 지형 위에 물체 Obt 를 찍은 배열을 따로 든다:
    //   +0x78 걷기 높이(모든 물체) — 걷기 높이차·걸음 비용·ZOC·시야, +0x80 겨눔 높이(종류 표 +0x14 가 선 종류 0·3·4·5 만) — 거리 자·같은 높이·모드 7·화면 창,
    //   +0x88 플래그(Obt 플래그로 덮어씀) — 걷기 &9 · 사거리 &8, +0x74 물체 번호 격자(발자국 전체).
    // LoadBtl 0x10062b11(+0x78/+0x88)·0x10062b78(+0x80) 이 0x10028620 으로 찍고, 부서짐(0x100e7be9)·문 여닫기(0x100e7c70·0x100e7cc5)가
    // 판에 0x3f1 을 보내면 0x100ead80 이 지형으로 되돌린 뒤(0x100d91e0) 물체마다 다시 찍는다(0x100e6620: 사라진 것 +0x101 과 열린 문 1·4 는 건너뜀).
    // 상자(2)는 열어도(0x100e7ce0, 0x3f1 을 안 보낸다) 계속 찍힌다.
    internal ObtMapImage? _stampMap;
    internal IReadOnlyList<DemoObject>? _stampObjects;
    internal int _stampKey, _stampCols, _stampRows;
    internal double _stampTime = double.NaN;
    internal bool _stampDirty = true;
    internal int[] _walkH = [], _aimH = [], _objGrid = [];
    internal ushort[] _cellFlags = [];

    /// <summary>물체 상태가 바뀌었다(부서짐·여닫기) — 원본이 판에 0x3f1 을 보내는 자리. 다음 조회 때 판을 다시 찍는다.</summary>
    internal void RestampObjects() => _stampDirty = true;

    /// <summary>도장 판이 지금 판·물체 상태와 맞는지 보고, 아니면 다시 찍는다. 물체 상태 서명은 틀마다 한 번만 잰다(세이브 되살리기도 잡힌다).</summary>
    internal void EnsureStamp()
    {
        if (host._map is not { } map) { _stampCols = _stampRows = 0; return; }
        var objects = Objects;
        if (!_stampDirty && ReferenceEquals(map, _stampMap) && ReferenceEquals(objects, _stampObjects))
        {
            if (_stampTime == host._lastTime) return;
            _stampTime = host._lastTime;
            if (StampKey(objects) == _stampKey) return;
        }
        RebuildStamp(map, objects);
    }

    internal int StampKey(IReadOnlyList<DemoObject> objects)
    {
        var hash = new HashCode();
        foreach (var o in objects) hash.Add((o.Alive ? 1 : 0) | (_opened.Contains(o) ? 2 : 0));
        return hash.ToHashCode();
    }

    /// <summary>지형에서 새로 시작해 살아 있고(종류 1·4 면) 닫혀 있는 물체를 차례로 찍는다(<c>0x100ead80</c> → <c>0x100e6620</c> → <c>0x10028620</c>).</summary>
    internal void RebuildStamp(ObtMapImage map, IReadOnlyList<DemoObject> objects)
    {
        int cols = map.Cols, rows = map.Rows, n = cols * rows;
        if (_walkH.Length != n) { _walkH = new int[n]; _aimH = new int[n]; _cellFlags = new ushort[n]; _objGrid = new int[n]; }
        Array.Copy(map.Heights, _walkH, n);
        Array.Copy(map.Heights, _aimH, n);
        Array.Copy(map.Flags, _cellFlags, n);
        Array.Fill(_objGrid, -1);
        for (int k = 0; k < objects.Count; k++)
        {
            var o = objects[k];
            // 물체 격자 +0x74 — 발자국 전체. 열린 문도 남는다(0x100746b0 목표 칸 금지에 걸린다).
            // 겹치면 사라지지 않은 물체가 이긴다.
            foreach (var (c, r, _, _) in FootprintCells(o))
                if ((uint)c < cols && (uint)r < rows && (_objGrid[r * cols + c] < 0 || ObjectGone(objects[_objGrid[r * cols + c]])))
                    _objGrid[r * cols + c] = k;
            if (ObjectGone(o) || (o.Data.Kind is 1 or 4 && _opened.Contains(o))) continue;
            // 바닥층 = 기준점 칸 높이를 바이트로 자른 값(원본 bl). 겨눔 높이(+0x80)는 종류 표 +0x14 가 선 종류(0·3·4·5)만 찍는다(0x10062b78).
            bool aim = o.Data.Kind is 0 or 3 or 4 or 5;
            bool originInside = (uint)o.Col < cols && (uint)o.Row < rows;
            int walkBase = originInside ? _walkH[o.Row * cols + o.Col] & 0xFF : 0;
            int aimBase = originInside ? _aimH[o.Row * cols + o.Col] & 0xFF : 0;
            foreach (var (c, r, raw, flags) in FootprintCells(o))
            {
                if ((uint)c >= cols || (uint)r >= rows) continue;
                int i = r * cols + c;
                if (raw != 0)
                {
                    _walkH[i] = (raw + 19) / 20 + walkBase;
                    if (aim) _aimH[i] = (raw + 19) / 20 + aimBase;
                }
                _cellFlags[i] = flags;                  // 플래그는 그대로 덮어쓴다(0x10028620)
            }
            if (Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"stamp: obj {o.Data.Id} kind {o.Data.Kind} at ({o.Col},{o.Row}) cells "
                    + string.Join(" ", FootprintCells(o).Where(p => (uint)p.Col < cols && (uint)p.Row < rows)
                                                        .Select(p => $"({p.Col},{p.Row}) h{_walkH[p.Row * cols + p.Col]} f{_cellFlags[p.Row * cols + p.Col]:x}"))
                    + Environment.NewLine);
        }
        (_stampMap, _stampObjects, _stampCols, _stampRows) = (map, objects, cols, rows);
        _stampKey = StampKey(objects);
        _stampTime = host._lastTime;
        _stampDirty = false;
    }

    /// <summary>걷기 높이(<c>+0x78</c>, 모든 물체 포함) — 걷기 높이차·걸음 비용·ZOC·시야가 쓴다. 판 밖은 0.</summary>
    internal int WalkHeightAt(int col, int row)
    {
        EnsureStamp();
        return (uint)col < (uint)_stampCols && (uint)row < (uint)_stampRows ? _walkH[row * _stampCols + col] : 0;
    }

    /// <summary>겨눔 높이(<c>+0x80</c>, 종류 0·3·4·5 물체만) — 거리 자·같은 높이·모드 7·화면 창이 쓴다. 판 밖은 0.</summary>
    internal int AimHeightAt(int col, int row)
    {
        EnsureStamp();
        return (uint)col < (uint)_stampCols && (uint)row < (uint)_stampRows ? _aimH[row * _stampCols + col] : 0;
    }

    /// <summary>물체까지 찍은 칸 플래그(<c>+0x88</c>) — &amp;9 걷기 금지 · &amp;8 사거리 제외. 판 밖은 0x8.</summary>
    internal ushort CellFlagsAt(int col, int row)
    {
        EnsureStamp();
        return (uint)col < (uint)_stampCols && (uint)row < (uint)_stampRows ? _cellFlags[row * _stampCols + col] : (ushort)0x8;
    }

    /// <summary>문이 열린 때 — 그때부터 여는 모션(1)을 한 번 돌고 열린 모션(2)에 머문다.</summary>
    internal readonly Dictionary<DemoObject, double> _openedAt = [];

    /// <summary>
    /// 그 칸이 물체 때문에 설 수 없는 칸인가 — 문과 스위치문뿐이다(<c>0x100746b0</c>). 닫힌 문은 도장 플래그로 이미 벽이고,
    /// 이것이 실제로 거르는 것은 <b>열린 문</b>이다 — 지나갈 수는 있어도 멈춰 설 수는 없다(감사3 R1).
    /// </summary>
    internal bool ObjectBlocks(int col, int row) => ObjectAt(col, row) is { Data.BlocksStanding: true };

    /// <summary>
    /// 물체가 그 인물에게 적인가 — 편 행렬(편 3·4 만 한편)로 가른다(0x1006fde0). 중립(−1) 물체는 바리케이트(7)만 누구나 부술 수 있고
    /// 포탑·힐 크리스탈(9·10)은 손을 댄 쪽 편이 되기 전엔 적이 아니다. 전에는 편 4 기준 이분법이라 편 3 동맹을 안 쐈다(fg-22).
    /// </summary>
    internal static bool ObjectHostile(DemoObject obj, UnitState u) =>
        obj.Team < 0 ? obj.Data.Kind is not (9 or 10) : obj.Team != u.Side && !(obj.Team >= 3 && u.Side >= 3);

    // ── 물체 레벨 성장(감사4 K2) ────────────────────────────────────────────────────────────
    // 원본 0x100e73f0: 레벨 = Btl 레코드 w6 + 파티 레벨(LoadBtl 0x10062ab2 · Map 물체 0x10062fd3·0x1006331c), 1 미만이면 1.
    // Lev.dat 메모리 레코드(+6 LP% · +0xa PSY%, 유닛 성장 0x1007a8e0 과 같은 표)로 최대·현재 HP += HP×LP%/100, ATK += ATK×PSY%/100 을 한 번.
    // DemoObject 는 파일값만 들고 있어 성장값은 이 사전에 따로 둔다 — 판(물체 목록)이 바뀌면 다시 셈한다.
    internal readonly Dictionary<DemoObject, (int Level, int MaxHp, int Attack)> _objGrowth = [];
    internal IReadOnlyList<DemoObject>? _objGrowthFor;

    /// <summary>판의 물체들에 파티 레벨 성장을 먹인다 — 판마다 한 번. 현재 HP 가 파일값 그대로인 물체만 새 최대로 채운다(세이브에서 되살린 HP 는 그대로).</summary>
    internal void EnsureObjectGrowth()
    {
        var objects = Objects;
        if (ReferenceEquals(objects, _objGrowthFor) || !host._battleLoaded || host._units.Length == 0) return;
        _objGrowthFor = objects;
        _objGrowth.Clear();
        int party = PartyLevel();
        var rows = host._db?.LevelGrowth ?? [];
        foreach (var o in objects)
        {
            int level = Math.Max(1, o.Record.LevelOffset + party);
            int maxHp = o.Data.MaxHp, attack = o.Data.Attack;
            if (rows.Count > 0)
            {
                var g = rows[Math.Min(level, rows.Count) - 1];
                maxHp += maxHp * g.Lp / 100;
                attack += attack * g.Psy / 100;
            }
            _objGrowth[o] = (level, maxHp, attack);
            if (o.Hp == o.Data.MaxHp) o.Hp = maxHp;
            if (Trace && (o.Data.Breakable || attack > 0))
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"obj growth: no {o.Record.No} at ({o.Col},{o.Row}) obj {o.Data.Id} kind {o.Data.Kind} team {o.Team} breakable {o.Data.Breakable} lv {level} hp {o.Data.MaxHp}→{maxHp} atk {o.Data.Attack}→{attack}" + Environment.NewLine);
        }
    }

    /// <summary>성장을 먹인 물체 레벨(<c>0x100e73f0</c>).</summary>
    internal int ObjLevel(DemoObject o) { EnsureObjectGrowth(); return _objGrowth.TryGetValue(o, out var g) ? g.Level : 1; }

    /// <summary>성장을 먹인 물체 공격력 — 포탑·함정·폭탄 상자가 쓴다.</summary>
    internal int ObjAttack(DemoObject o) { EnsureObjectGrowth(); return _objGrowth.TryGetValue(o, out var g) ? g.Attack : o.Data.Attack; }

    /// <summary>
    /// 물체를 부순 인물에게 처치 경험치(메시지 1016)를 준다 — 물체 레벨로 셈한다(<c>0x100e79fc~</c>). SOUL +10 과 따로다.
    /// 군단 부하가 부수면 대장이 받고(<c>0x10079b14</c>), 사람이 명령하는 유닛만 받는다(<c>0x100742e0</c>).
    /// </summary>
    internal void GainObjectKillExp(UnitState breaker, DemoObject obj)
    {
        var killer = breaker.LeaderIndex >= 0 && breaker.LeaderIndex < host._units.Length ? host._units[breaker.LeaderIndex] : breaker;
        if (host._db == null || killer.Data is not { } k || !killer.PlayerControlled) return;
        int exp = Math.Max(1, host._db.ExpForKill(k, ObjLevel(obj), killer.Status(11) + host._db.EquipBonus(k, ItemBook.ExpStat)));
        killer.Data = k with { Exp = k.Exp + exp, CumExp = k.CumExp + exp };
        Popup(killer, $"EXP +{exp}", 0xFF90D0FF, 15);
        QueueLevelUps();
    }

    /// <summary>
    /// 물체가 차례를 받을 수 있는 자리인가 — 칸이 맵 경계 상자(<c>맵+0x390..+0x396</c>) 안이어야 한다(<c>0x100e7130</c>, 감사4 K10).
    /// 리메이크는 이벤트로 경계를 줄이는 길을 아직 안 들고 있어 판 전체를 경계로 본다.
    /// </summary>
    internal bool ObjectInBounds(DemoObject o) => (uint)o.Col < (uint)host.Cols && (uint)o.Row < (uint)host.Rows;

    /// <summary>물체에 피해를 준다 — 부서지면 폭발·SOUL +10·든 것 떨구기(0x100e7ba0~). 어빌리티 범위 피해도 여기로 온다.</summary>
    internal void DamageObject(UnitState user, DemoObject obj, int damage)
    {
        if (host._db is null || damage <= 0 || !obj.Alive) return;
        EnsureObjectGrowth();                            // HP 가 레벨 성장을 먹은 뒤에 깎는다(0x100e73f0)
        if (Trace) File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"), $"obj damage: no {obj.Record.No} at ({obj.Col},{obj.Row}) by {user.ChrCode} atk {damage} hp {obj.Hp}" + Environment.NewLine);
        // 물체 쪽 1001 처리(0x100e77e0 → 0x100e72f0)는 명중·RDP·치명 없이 공격자 ATK ±10% 만 뺀다(ba-14 O1).
        damage = damage * (90 + host._rng.Next(21)) / 100;
        obj.Hp -= damage;
        ShowNumberAt(obj.Col * TileW + TileW / 2, host.CellCenterY(obj.Col, obj.Row), damage.ToString(), DamageColor);   // 숫자는 물체 자리에
        if (obj.Hp > 0) return;
        RestampObjects();                                 // 부서지면 판을 다시 찍는다(0x100e7be9 → 0x3f1)
        ObjectSounds(obj, 4, host._lastTime);
        _brokenAt[obj] = (host._lastTime, 4);                  // 제 그림의 모션 4 를 한 번 돌고 사라진다 — 0x3f1 은 그림 번호가 아니라 판 다시 찍기 메시지다(ba-20 O P4)
        user.Soul = Math.Min(user.MaxSoul, user.Soul + 10);
        GainObjectKillExp(user, obj);                     // 1016 + 물체 레벨(0x100e79fc~, 감사4 K3)
        host.Toast($"{host._db.T((ushort)obj.Data.NameId)} 이(가) 부서졌습니다.");
        GiveObjectSpoils(obj, user);
    }

    /// <summary>
    /// 물체를 만지는 work <b>386</b> — 상하좌우 한 칸, <b>TP 80</b>. 원본은 명령 0x2714 가 이 work 으로 물체에 손을 댄다.
    /// </summary>
    internal const int ObjectTouchTp = 80;

    /// <summary>부서진(모션 4)·터진(폭탄 상자 모션 8) 물체가 마지막 모션을 한 번 도는 동안 — (때, 모션).</summary>
    internal readonly Dictionary<DemoObject, (double At, int Motion)> _brokenAt = [];

    /// <summary>
    /// 차례인 인물이 그 물체에 손을 대러 가는 길 — 못 닿으면 null, 이미 옆이면 빈 길.
    /// </summary>
    /// <remarks>
    /// 원본은 이동 칸(층 2)마다 work 386(십자 한 칸, 대상 방식 8 = 오브젝트)을 그려 <b>「닿을 수 있는 오브젝트 칸」 층 1</b> 로 모은다
    /// (<c>0x10074f20</c>, 상태 10 <c>0x1006961c</c>, 분석-UI 「칸 깃발」). 곧 <b>걸어가서 옆에 설 수 있는</b> 물체면 된다 — 지금 옆일 필요는 없다.
    /// 노랑 칸·주먹 커서는 이 층 그대로 <b>TP 를 따로 안 본다</b>. 실제로 열 때(<paramref name="needTp"/>)만 원본 이동 예산
    /// <c>걷는 비용 ≤ 현재 TP + min(0, CTP − 80)</c> 을 본다(명령 0x2714 가 work 386 으로 예산을 셈한다, 분석-캐릭터 「CTP」) —
    /// 곧 TP 가 모자라도 CTP 만큼은 끌어 쓴다(사용자 보고).
    /// 옆 칸 가운데 가장 싼 곳으로 가고, 지금 선 자리가 이미 닿는 자리면 걷지 않는다.
    /// </remarks>
    internal List<(int Col, int Row)>? FindTouchPath(UnitState user, DemoObject obj, MoveRange? known = null, bool needTp = false)
    {
        // 만질 수 있는 종류는 종류 표 0x1016f438 의 [중립, 아군, 적] 바이트가 1 인 것(work 386):
        //   1 문 · 2 상자 · 6 스위치 · 8 폭탄 상자 = 누구나, 9 포탑 = 중립일 때만(만지면 편 가져오기 0x100e6184 → 0x2717, 감사4 K1),
        //   10 힐 크리스탈 = 중립이거나 제 편이고 다 찼을 때만(0x1006fef0 이 +0x160 을 본다) — 적 편은 표 2 라 때려 부술 뿐 못 만진다(K6).
        // 연 상자(2)만 더 손댈 것이 없다 — 0x100e7ce0 은 +0x164(열림)이면 아무것도 안 한다. 문(1)·스위치(6)는 토글이라 다시 만져 닫는다(0x2713·0x2715, K5).
        bool touchable = obj.Data.Kind switch
        {
            1 or 6 => true,
            2 or 8 => !_opened.Contains(obj),
            9 => obj.Team < 0,
            10 => obj.Team < 0 || (!ObjectHostile(obj, user) && obj.Charged),
            _ => false,
        };
        if (!touchable || ObjectGone(obj) || (known ?? ComputeRange(user)) is not { } range) return null;
        bool Affordable(int col, int row) =>
            (uint)col < host.Cols && (uint)row < host.Rows && range.CanReach(row * host.Cols + col)
            && (!needTp || range.Cost[row * host.Cols + col] <= user.Tp + Math.Min(0, user.Ctp - ObjectTouchTp));
        // work 386 은 십자 한 칸, 대상 8(물체) — 발자국 칸 어디든 옆이면 닿는다(여러 칸짜리 문·아크방전기).
        var cells = FootprintCells(obj).Select(c => (c.Col, c.Row)).ToList();
        if (cells.Count == 0) cells.Add((obj.Col, obj.Row));
        bool Adjacent(int col, int row) => !cells.Contains((col, row)) && cells.Any(c => Math.Abs(c.Col - col) + Math.Abs(c.Row - row) == 1);
        if (Adjacent(user.Col, user.Row) && Affordable(user.Col, user.Row)) return [];
        int best = -1, bestCost = int.MaxValue;
        foreach (var (oc, or) in cells)
            foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
            {
                int col = oc + dx, row = or + dy;
                if (!Adjacent(col, row) || !Affordable(col, row) || range.Cost[row * host.Cols + col] >= bestCost) continue;
                best = row * host.Cols + col;
                bestCost = range.Cost[best];
            }
        return best < 0 ? null : PathWithin(range, user.Col, user.Row, best);
    }

    /// <summary>걸어가서 열 물체 — 인물이 옆 칸에 멈추면 <see cref="ResolvePendingTouch"/> 가 연다(원본 명령 0x2714 가 걷고 나서 work 386 을 쓴다).</summary>
    internal DemoObject? _pendingTouch;

    /// <summary>
    /// 물체 칸을 눌렀을 때 — 닿을 수 있으면 옆까지 걸어가서 연다(이미 옆이면 바로). 처리했으면 true.
    /// </summary>
    internal bool TryTouchObject(int col, int row)
    {
        if (!IsPlayerTurn || host._units[_turn].IsBusy) return false;
        if (ObjectAt(col, row) is not { } obj || FindTouchPath(host._units[_turn], obj) == null) return false;
        if (FindTouchPath(host._units[_turn], obj, needTp: true) is not { } path)
        {
            host.Hint($"TP 가 모자랍니다 — 손을 대려면 옆 칸까지 걷고도 TP+CTP 로 {ObjectTouchTp} 을 낼 수 있어야 합니다");
            return true;
        }
        if (path.Count == 0) return TouchObject(obj);
        foreach (var cell in path) host._units[_turn].Path.Enqueue(cell);
        _pendingTouch = obj;
        return true;
    }

    /// <summary>걸어간 인물이 멈췄으면 기다리던 물체를 연다 — 매 틀 부른다. 차례가 바뀌었거나 못 닿게 됐으면 그만둔다.</summary>
    internal void ResolvePendingTouch()
    {
        if (_pendingTouch is not { } obj) return;
        if (!IsPlayerTurn) { _pendingTouch = null; return; }
        if (host._units[_turn].IsBusy) return;
        _pendingTouch = null;
        if (ObjectAt(obj.Col, obj.Row) == obj && FindTouchPath(host._units[_turn], obj, needTp: true) is { Count: 0 }) TouchObject(obj);
    }

    /// <summary>
    /// 상자를 연다 — 아이템이 들었으면 가방에, 아니면 돈을 지갑에 넣고 물체를 치운다(<c>0x100e7ce0</c>).
    /// </summary>
    internal bool TouchObject(DemoObject obj)
    {
        if (obj.Data.Kind == 10) return TouchHealCrystal(obj);
        var user = host._units[_turn];
        string name = host._db?.T((ushort)obj.Data.NameId) ?? "";

        // 중립 포탑(9) — 만진 쪽 편이 되어 그때부터 차례를 받아 적을 쏜다(갈래 0x100e6184: +0x78 = 만진 이 편 → 명령 0x2717 깨우기 0x100e7ff0, 감사4 K1).
        if (obj.Data.Kind == 9)
        {
            if (obj.Team >= 0) return false;
            CommitMove(user);
            user.Tp -= ObjectTouchTp;
            obj.Team = user.Side;
            _wokenAt[obj] = host._lastTime;
            host.Toast($"{name} 이(가) 아군 편이 되었습니다.");
            return true;
        }

        // 문(1)·스위치(6) — 만질 때마다 여닫기 토글(0x2713 0x100e7c00 · 0x2715 0x100e7da0, 감사4 K5/K9).
        // 열린 것을 만지면 닫힌다: 문은 모션 3 → 0, +0x164 = 0, 판을 다시 찍는다(0x3f1). 스위치의 연결 물체(w8~w10)는 시판 자료에 없어 안 한다.
        if (obj.Data.Kind is 1 or 6)
        {
            CommitMove(user);
            user.Tp -= ObjectTouchTp;
            bool open = !_opened.Contains(obj);
            SetObjectOpen(obj, open);
            host.Toast($"{name} 이(가) {(open ? "열렸" : "닫혔")}습니다.");
            return true;
        }

        CommitMove(user);
        user.Tp -= ObjectTouchTp;
        _opened.Add(obj);
        _openedAt[obj] = host._lastTime;
        RestampObjects();                                 // 상자는 열려도 그대로 찍힌다(0x100e7ce0 은 0x3f1 을 안 보낸다). 폭탄 상자만 사라진다.

        if (obj.Data.Kind == 8)
        {
            Explode(obj);
            return true;
        }

        if (obj.Record.ItemId > 0 || obj.Record.Gold > 0) GiveObjectSpoils(obj, host._units[_turn]);
        else host.Toast("비어 있습니다.");

        return true;
    }

    /// <summary>
    /// 화면 밖 시험용 — 차례인 인물을 가장 가까운 상자 옆으로 세우고 그 상자를 연다(T 키, <c>DUELDX_TOUCH=1</c>).
    /// 상자까지 걸어가는 데 여러 칸이 걸려 클릭으로는 확인하기 번거롭다.
    /// </summary>
    internal bool TouchNearestObjectForTest()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_TOUCH") is not { Length: > 0 }) return false;
        // 시험은 차례를 안 기다린다 — 첫 아군으로 친다.
        var user = host._units.FirstOrDefault(u => u.Alive && u.PlayerControlled);
        if (user is null) return false;
        // B 키면 부술 수 있는 물체를, 그 밖에는 열 수 있는 물체를 고른다.
        bool breaking = Environment.GetEnvironmentVariable("DUELDX_TOUCH") == "break";
        // 「열,줄」 이면 그 칸의 물체를 연다(두 칸 문의 오른쪽 칸 따위를 시험할 때).
        var cell = Environment.GetEnvironmentVariable("DUELDX_TOUCH")!.Split(',');
        if (cell.Length == 2 && int.TryParse(cell[0], out int tc) && int.TryParse(cell[1], out int tr))
        {
            if (ObjectAt(tc, tr) is not { } there) return false;
            user.ResetTo(there.Col, Math.Clamp(there.Row + 1, 0, host.Rows - 1), keepFacing: true);
            // 차례를 잠깐 빌렸다가 돌려준다 — 안 돌려주면 진짜 차례인 인물이 영영 안 움직여 자동 진행이 멈췄다.
            int was = _turn;
            _turn = Array.IndexOf(host._units, user);
            bool done = TouchObject(there);
            if (was >= 0 && was != _turn) _turn = was;
            return done;
        }
        var target = Objects.Where(o => !_opened.Contains(o) && o.Alive
                                        && (breaking ? o.Data.Breakable && o.Team != 4 : o.Data.Kind is 2 or 6 or 8))
                            .OrderBy(o => Math.Abs(o.Col - user.Col) + Math.Abs(o.Row - user.Row))
                            .FirstOrDefault();
        if (target is null) return false;
        // 열 때는 옆 한 칸, 칠 때는 기본공격 자리(정확히 두 칸)로 세운다.
        user.ResetTo(target.Col, Math.Clamp(target.Row + (breaking ? 2 : 1), 0, host.Rows - 1), keepFacing: true);
        if (!breaking) { _turn = Array.IndexOf(host._units, user); return TouchObject(target); }
        _turn = Array.IndexOf(host._units, user);
        return TryBreakObject(target.Col, target.Row, force: true);
    }

    /// <summary>
    /// 힐 크리스탈(종류 10) 만지기 — 원본 갈래 <c>0x100e60fb</c>. 중립(−1)이면 만진 쪽 편이 되고(명령 0x2717),
    /// 제 편이고 다 찼으면(<c>+0x160</c>) <b>만진 인물의 칸</b>에 제 work(<c>+0x134</c>, Obj 59 는 1472 = 최대 HP 50% 회복)을 쓴 뒤
    /// 꺼지고 다시 찬다(명령 0x271a → <c>0x100e80a0</c> → 0x2719 <c>0x100e8070</c>). 덜 찼으면 아무 일도 없다.
    /// 예전에는 만질 수 없는 종류로 쳐서 크리스탈 옆에서 눌러도 아무것도 안 됐다(사용자 보고: Btl 0147 (3,24)).
    /// </summary>
    internal bool TouchHealCrystal(DemoObject obj)
    {
        var user = host._units[_turn];
        string name = host._db?.T((ushort)obj.Data.NameId) ?? "";
        if (obj.Team < 0)
        {
            CommitMove(user);
            user.Tp -= ObjectTouchTp;
            obj.Team = user.Side;
            host.Toast($"{name} 이(가) 아군 편이 되었습니다.");
            return true;
        }
        if (!obj.Charged)
        {
            host.Hint($"{name} 이(가) 아직 충전 중입니다 ({obj.Charge}/{Math.Max(1, obj.Data.TurnEvery)})");
            return true;
        }
        // 적 편 크리스탈은 만질 수 없다(표 [1,1,2] 의 2 = 일반 공격, 감사4 K6) — FindTouchPath 가 걸러도 시험 훅 따위가 곧장 부를 수 있다.
        if (ObjectHostile(obj, user)) return false;
        CommitMove(user);
        user.Tp -= ObjectTouchTp;
        (obj.Charged, obj.Charge) = (false, 0);
        var w = Work(obj.Data.WorkId);
        if (w is { IsHeal: true } && user.Hp < user.MaxHp)
        {
            int before = user.Hp;
            user.Hp = Math.Min(user.MaxHp, user.Hp + user.MaxHp * w.Power / 100);
            ShowNumber(user, (user.Hp - before).ToString(), HealColor2);
        }
        else if (w is { Kind: 7 })
        {
            // 그린크리스탈(Obj 27, work 1474 0x100e8ad0) — 1472 와 같은 꼴에 종류 7(보조): 만진 이에게 work 의 효과(상태 43 값 40)를 건다(감사4 K7).
            var used = new List<int>();
            var effects = w.Bonuses.Where(b => b.Stat != 0).ToList();
            if (effects.Count == 0) effects.Add((43, 40));
            foreach (var (id, value) in effects)
            {
                if (IsStatBonus(id)) AddStatBonus(user, id, value);
                else PutAilment(user, id, value, used, null);
            }
            if (Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"crystal buff: work {w.Id} → chr {user.ChrCode} " + string.Join(" ", effects.Select(e => $"{e.Stat}={e.Value}")) + Environment.NewLine);
        }
        return true;
    }

    /// <summary>
    /// 힐 크리스탈 충전 — 편이 있으면 한 번에 1 씩 차고 한도(.obj 차례 간격, Obj 59 는 15)에서 멈춘다(<c>0x100e65f0</c>).
    /// 한도에 닿으면 차례를 받아(<c>0x100e71f0</c>) 켜진다(명령 0x2718 → <c>0x100e8040</c>, 모션 9).
    /// 부르는 주기는 <b>전투 틱마다</b>다 — 가상 함수 칸 +0xb0 을 판 전체에 돌리는 <c>0x100eaad0</c> 을 GETNEXT 의 틱 넘기기(<c>0x10067d47</c>)만 부른다(ba-22 확인).
    /// </summary>
    internal static void ChargeHealCrystal(DemoObject obj)
    {
        int full = Math.Max(1, obj.Data.TurnEvery);
        obj.Charge = Math.Min(full, obj.Charge + 1);
        if (obj.Charge >= full) obj.Charged = true;
    }

    /// <summary>
    /// 물체를 친다 — 물체에는 RDP 가 없어 <b>공격력이 그대로 피해</b>가 되고(<c>0x100e79ac</c>), 명중·치명·흔들기도 없다.
    /// HP 가 0 이하면 부서지면서 친 인물에게 소울 10 과 경험치를 준다(<c>0x1006ab70</c>).
    /// </summary>
    internal bool TryBreakObject(int col, int row, bool force = false)
    {
        if (!force && (!IsPlayerTurn || host._units[_turn].IsBusy)) return false;
        if (ObjectAt(col, row) is not { Data.Breakable: true } obj) return false;
        var user = host._units[_turn];
        // 제 편 물체는 못 친다 — 종류 7(바리케이트)은 적·중립이면, 9·10 은 적이면 칠 수 있다.
        if (!ObjectHostile(obj, user)) return false;
        if (user.Data is not { } c || host._db is null || Work(c.BasicWorkId) is not { } w) return false;
        // 기본공격과 같은 자리 규칙 — 옆 두 칸(모양 2 십자, 사거리 5~8) 안이어야 친다.
        if (!InWorkRange(w, user.Col, user.Row, col, row, user)) return false;

        // 보통 기본공격이다(0x1007f0a0 — 타격 키 0x10072b40 → 1001): 칼질을 하고, 피해 ±10%·숫자·부서짐은 물체 피해 고리(DamageObject)가 낸다
        // (ba-20 O P3). 전에는 동작 없이 곧바로 깎았고 ±10% 도 없었다.
        _routine = UseWorkRoutine(_turn, w, -1, col, row, []);
        return true;
    }

    /// <summary>
    /// 부술 수 있는 적 물체(기총포탑 따위)를 적처럼 친다 — 사거리 밖이면 기본공격 자리까지 걸어가서 친다.
    /// 전에는 이미 사거리 안에 서 있을 때만 클릭이 먹고, 링 「공격」 겨누기는 유닛만 봐서 Btl 0081 의 기총포탑을 못 쳤다(사용자 보고).
    /// </summary>
    internal bool TryAttackObject(int col, int row)
    {
        if (!IsPlayerTurn || host._units[_turn].IsBusy || _routine != null) return false;
        if (ObjectAt(col, row) is not { Data.Breakable: true } obj) return false;
        var user = host._units[_turn];
        if (!ObjectHostile(obj, user)) return false;
        if (user.Data is not { } c || Work(c.BasicWorkId) is not { } w || !CanAfford(user, w)) return false;
        if (InWorkRange(w, user.Col, user.Row, obj.Col, obj.Row, user)) return TryBreakObject(obj.Col, obj.Row);
        if (ComputeRange(user) is not { } range) return false;
        int best = -1, bestCost = int.MaxValue;
        for (int i = 0; i < range.Cost.Length; i++)
        {
            if (range.Cost[i] >= bestCost || !range.CanReach(i) || !InWorkRange(w, i % host.Cols, i / host.Cols, obj.Col, obj.Row, user)) continue;
            bestCost = range.Cost[i];
            best = i;
        }
        if (best < 0 || PathWithin(range, user.Col, user.Row, best) is not { } path)
        {
            host.Hint("거기까지 가서 칠 수 없습니다");
            return true;
        }
        CommitMoveForAction();
        _commitUndo = null;
        _routine = WalkThenBreak(user, path, obj.Col, obj.Row);
        return true;
    }

    internal IEnumerator<bool> WalkThenBreak(UnitState user, List<(int Col, int Row)> path, int col, int row)
    {
        foreach (var cell in path) user.Path.Enqueue(cell);
        while (user.IsBusy) yield return true;
        user.Facing = FacingToward(user.Col, user.Row, col, row);
        if (ObjectAt(col, row) is { Data.Breakable: true, Alive: true } obj && ObjectHostile(obj, user)
            && user.Data is { } c && Work(c.BasicWorkId) is { } w && InWorkRange(w, user.Col, user.Row, col, row, user))
        {
            var attack = UseWorkRoutine(Array.IndexOf(host._units, user), w, -1, col, row, []);
            while (attack.MoveNext()) yield return true;
        }
    }

    /// <summary>부서지거나 열린 상자가 든 것을 준다 — 아이템이 있으면 아이템, 없으면 GP(0x100e7d31, 명령 0x3f4·0x3f5).</summary>
    /// <remarks>
    /// 아이템 얻기(유닛 메시지 0x3f4, <c>0x10071ffc~0x1007209c</c>): 받는 인물이 파티에 들었으면 장비 칸 표 <c>0x10032d70</c>
    /// (무기 종류 == 제 무기 → 0 · 갑옷 2 → 1 · 6 → 2 · 5 → 3 · 4 → 4 · 3 → 5, 캡슐 따위 → 없음)로 칸을 찾아 <b>그 칸이 비었으면 바로 낀다</b>(<c>+0x15c[칸]</c>).
    /// 칸이 없거나 차 있으면 가방으로. 받는 사람은 부서진·열린 물체 칸의 인물이다 — 없으면 연 사람.
    /// 전에는 늘 가방에만 넣었다(감사3 I2). 가방은 파티별이 아니라 지금 가방 하나다(파티가 갈린 챕터는 원본과 다를 수 있다).
    /// </remarks>
    internal void GiveObjectSpoils(DemoObject obj, UnitState? opener = null)
    {
        if (obj.Record.ItemId > 0)
        {
            var taker = LiveUnitAt(obj.Col, obj.Row) ?? opener;
            if (!TryEquipSpoil(taker, obj.Record.ItemId))
                host.StatusScr._inventory[obj.Record.ItemId] = host.StatusScr._inventory.GetValueOrDefault(obj.Record.ItemId) + 1;
            string itemName = host._db?.Items.GetValueOrDefault(obj.Record.ItemId) is { } item ? host._db.T(item.NameId) : "";
            ShowSpoilMessage(taker, "Item 획득", $"{(itemName.Length > 0 ? itemName : $"아이템 {obj.Record.ItemId}")} 1개를 획득하였습니다.");
        }
        else if (obj.Record.Gold > 0)
        {
            host.Mos._shopMoney += obj.Record.Gold;
            ShowSpoilMessage(LiveUnitAt(obj.Col, obj.Row) ?? opener, "GP 획득", $"{obj.Record.Gold}GP를 획득하였습니다.");
        }
    }

    /// <summary>
    /// 상자에서 얻은 것을 알리는 창 — 원본은 레벨업 창과 같은 메시지 창(0x10034540, 120틱, 제목 「Item 획득」/「GP 획득」)이 떠 있는 동안
    /// 전투 상태 기계가 선다(0x10066206, ba-21 battle-flow 1). 받는 이가 판 위의 사람 조종 편일 때만 띄운다(0x1006e9a0) — 그 밖에는 전처럼 알림 글.
    /// </summary>
    internal void ShowSpoilMessage(UnitState? taker, string title, string body)
    {
        int index = taker == null ? -1 : Array.IndexOf(host._units, taker);
        if (index < 0 || !taker!.OnField || !taker.PlayerControlled || AutoPlay || LevelUpOpen) { host.Toast(body); return; }
        _levelUpUnit = index;
        _levelUpUntil = host._lastTime + 120 / TicksPerSecond;
        _levelUpTitle = title;
        _levelUpBody = body;
    }

    /// <summary>얻은 장비를 그 인물의 맞는 빈 칸에 바로 낀다(<c>0x10032d70</c> → <c>+0x15c[칸]</c>). 꼈으면 true.</summary>
    internal bool TryEquipSpoil(UnitState? taker, int itemId)
    {
        // 파티에 든 인물만(0x1007b030 ≠ −1) — 동맹 손님·군단 부하·적은 가방으로.
        if (taker is not { Alive: true, Data: { } c } || taker.LeaderIndex >= 0 || !taker.IsAlly
            || !(host.Mos._members.Contains(taker.ChrCode) || host._party.ContainsKey(taker.ChrCode)) || host._db is not { } db
            || !db.Items.TryGetValue(itemId, out var item)) return false;
        for (int slot = 0; slot < Math.Min(6, c.Items.Length); slot++)
        {
            if (!db.FitsSlot(c, item, slot)) continue;
            if (c.Items[slot] != 0) return false;           // 맞는 칸이 이미 차 있으면 가방으로
            var items = (ushort[])c.Items.Clone();
            items[slot] = (ushort)itemId;
            taker.Data = c with { Items = items };
            int oldHp = taker.Hp, oldMax = taker.MaxHp;   // 화면 HP 는 새 갑옷 배율로 같은 비율(ba-20 C3, Status.cs SetEquipment)
            host.StatusScr.RefreshUnitStats(taker);
            if (oldMax > 0 && taker.MaxHp != oldMax) taker.Hp = Math.Clamp((int)((long)oldHp * taker.MaxHp / oldMax), oldHp > 0 ? 1 : 0, taker.MaxHp);
            if (host._party.ContainsKey(taker.ChrCode)) host._party[taker.ChrCode] = taker.Data;
            if (Trace)
                System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dueldx_trace.log"),
                    $"spoil equip: chr {taker.ChrCode} slot {slot} ← item {itemId}" + Environment.NewLine);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 폭탄 상자(종류 8)가 터진다 — 제 <c>ATK</c> 로 <b>반경 안의 모두</b>를 친다(적아 안 가린다).
    /// </summary>
    internal void Explode(DemoObject obj)
    {
        ObjectSounds(obj, 8, host._lastTime);
        _brokenAt[obj] = (host._lastTime, 8);   // 폭탄 상자는 모션 8(Obs 460 = 35틱)을 한 번 돈다(0x100e7e90)
        if (host._db is null) return;

        int reach = Math.Max(1, obj.Data.Radius);
        // 반경은 맨해튼 + |Δ높이|/2, 편 검사 없음, (1000−RDP)×공격력/1000 ±10%(0x100e7e90 → work 1596, ba-14 O3).
        foreach (var u in host._units.Where(u => u.Alive && u.OnField && u.Data is not null
                                            && Math.Abs(u.Col - obj.Col) + Math.Abs(u.Row - obj.Row)
                                               + Math.Abs(HeightAt(u.Col, u.Row) - HeightAt(obj.Col, obj.Row)) / 2 <= reach))
        {
            int damage = (host._db.N(3) - host._db.Rdp(u.Data!, u.Hp, u.MaxHp)) * ObjAttack(obj) / Math.Max(1, host._db.N(3));
            damage = damage * (90 + host._rng.Next(21)) / 100;
            if (damage <= 0) continue;
            u.Hp = Math.Max(0, u.Hp - damage);
            ShowNumber(u, damage.ToString(), DamageColor);
            AddSoul(u, damage / Math.Max(1, host._db.N(43)));   // 물체에 맞아도 SOUL 은 오른다(0x10078f8d)
        }
        host.Toast($"{host._db.T((ushort)obj.Data.NameId)} 이(가) 터졌습니다.");
        // 폭발로 HP 0 이 된 유닛은 그 행동 끝에 쓰러진다 — 전에는 HP 0 인 채 계속 움직였다(ba-20 O P5). 만진 본인이 죽으면 UpdateTurn 이 차례를 넘긴다.
        SweepTickDeaths();
    }

    /// <summary>
    /// 물체의 차례 — 물체는 TP 게이지를 안 쓰고 <b><c>전투틱 % wtp == 0</c></b> 인 틱에만 움직인다(<c>0x100e71f0</c>).
    /// 종류 3·9·10 만 차례를 받고, 그중 9·10 은 편이 중립(−1)이면 영영 안 움직인다.
    /// </summary>
    internal void StepObjects()
    {
        EnsureObjectGrowth();
        foreach (var obj in Objects)
        {
            if (!ObjectDue(obj)) continue;
            if (Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"object tick {_tick} no {obj.Record.No} work {obj.Data.WorkId} team {obj.Team} at ({obj.Col},{obj.Row})" + Environment.NewLine);
            ObjectActs(obj);
        }
    }

    /// <summary>이번 틱에 차례가 온 물체인가 — 힐 크리스탈은 여기서 충전만 하고 거짓을 돌려준다.</summary>
    internal bool ObjectDue(DemoObject obj)
    {
        // 0x100e7130: 사라지지 않았고 칸이 맵 경계 상자 안이고 종류 표 +0xc(차례)가 선 것만(감사4 K10).
        if (!obj.Alive || _opened.Contains(obj) || !obj.Data.Acts || !ObjectInBounds(obj)) return false;
        if (obj.Data.Kind is 9 or 10 && obj.Team < 0) return false;
        if (obj.Data.Kind == 10) { ChargeHealCrystal(obj); return false; }
        return _tick % Math.Max(1, obj.Data.TurnEvery) == 0;
    }

    /// <summary>그 물체가 이번 차례에 쏠 상대가 있나(대략 — 사거리 안, 흩뿌리는 포탑은 4칸 안에 적대 유닛).</summary>
    internal bool ObjectHasTarget(DemoObject obj)
    {
        if (obj.Data.Attack <= 0) return false;
        var work = Work(obj.Data.WorkId);
        bool heals = work is { IsHeal: true };
        return host._units.Any(u => u.Alive && u.OnField && u.Data is not null
            && (heals ? !ObjectHostile(obj, u) && obj.Team >= 0 && u.Hp < u.MaxHp : ObjectHostile(obj, u))
            && (work is { Id: 1525 or 1526 or 1527 } ? BarrageReaches(obj, u)
                : work is { } w ? InWorkRange(w, obj.Col, obj.Row, u.Col, u.Row)
                : Math.Abs(u.Col - obj.Col) + Math.Abs(u.Row - obj.Row) <= 1));
    }

    /// <summary>흩뿌리는 포탑의 후보 칸(제 칸과 네 이웃) 가운데 그 유닛이 반경 3(높이 차 절반 포함) 안에 드는 칸이 있나 — ObjectBarrage 와 같은 잣대.</summary>
    internal bool BarrageReaches(DemoObject obj, UnitState u)
    {
        foreach (var (c, r) in new[] { (obj.Col, obj.Row), (obj.Col, obj.Row - 1), (obj.Col + 1, obj.Row), (obj.Col, obj.Row + 1), (obj.Col - 1, obj.Row) })
            if (Math.Abs(u.Col - c) + Math.Abs(u.Row - r) + Math.Abs(HeightAt(u.Col, u.Row) - HeightAt(c, r)) / 2 <= 3) return true;
        return false;
    }

    /// <summary>
    /// 물체 모션의 소리 키를 그 틱에 물체 자리에서 낸다 — 원본 물체는 유닛·이펙트와 같은 0x100e5410 으로 제 Obs 모션 소리를 낸다(ba-21 sound D1).
    /// 부서짐 모션 4 · 폭탄 상자 8 → Snd 92, 포탑·함정의 14~16 등. 전에는 물체 모션의 소리를 전혀 안 읽었다.
    /// </summary>
    internal void ObjectSounds(DemoObject obj, int motion, double at)
    {
        if (host.UiFor(obj.Data.SpriteId)?.Clip(motion) is not { } clip) return;
        float x = obj.Col * TileW + TileW / 2;
        foreach (var (tick, sound) in clip.Sounds) host._pendingSounds.Add((at + tick / TicksPerSecond, sound, x));
    }

    /// <summary>쏘는 동작(모션 14 → 15 → 16)을 도는 물체와 시작한 때 — DrawObjects 가 그린다.</summary>
    internal readonly Dictionary<DemoObject, double> _objActing = [];

    /// <summary>
    /// 물체 차례(상태 16, 0x1006a4c0) — 물체 하나씩: 상대가 있으면 카메라를 물체로 보내고 설 때까지 기다린 뒤(0x1006e570 · 0x1006e850)
    /// 쏘는 모션 14 → 15 → 16 을 돌며 실행한다(0x100e7510). 상대가 없는 물체는 카메라 없이 건너뛴다(ba-20 O P1).
    /// 전에는 한 틀에 전부 숫자만 떴다.
    /// </summary>
    internal IEnumerator<bool> ObjectTurns()
    {
        EnsureObjectGrowth();
        foreach (var obj in Objects.ToList())
        {
            if (_outcome.Length > 0) yield break;
            if (!ObjectDue(obj)) continue;
            if (!ObjectHasTarget(obj)) { ObjectActs(obj); continue; }
            foreach (bool _ in CenterAndWait(obj.Col * TileW + TileW / 2, host.CellCenterY(obj.Col, obj.Row))) yield return true;
            var sprite = host.UiFor(obj.Data.SpriteId);
            int wind = sprite?.MotionLength(14) ?? 0, fire = sprite?.MotionLength(15) ?? 0, rest = sprite?.MotionLength(16) ?? 0;
            if (wind + fire + rest > 0) _objActing[obj] = host._lastTime;
            // 쏘는 소리는 물체 제 모션의 소리 키다(기총포탑 14 → 405 · 15 → 423 · 16 → 404, 포탑 424/425 …) — 전에는 흩뿌리는 포탑에만 423 을 박아 냈다.
            ObjectSounds(obj, 14, host._lastTime);
            ObjectSounds(obj, 15, host._lastTime + wind / TicksPerSecond);
            ObjectSounds(obj, 16, host._lastTime + (wind + fire) / TicksPerSecond);
            for (double end = host._lastTime + wind / TicksPerSecond; host._lastTime < end;) yield return true;
            if (Trace)
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "dueldx_trace.log"),
                    $"object tick {_tick} no {obj.Record.No} work {obj.Data.WorkId} team {obj.Team} at ({obj.Col},{obj.Row})" + Environment.NewLine);
            ObjectActs(obj);
            for (double end = host._lastTime + (fire + rest) / TicksPerSecond; host._lastTime < end;) yield return true;
            _objActing.Remove(obj);
        }
        SweepTickDeaths();
    }

    /// <summary>
    /// 포탑·힐 크리스탈이 한 번 움직인다 — 사거리 안의 상대를 친다.
    /// 피해는 <c>(1000 − 대상 RDP) × 물체 공격력 / 1000</c> 이고 PSY·무기·치명타가 없다(<c>0x1007b8f4</c>).
    /// </summary>
    internal void ObjectActs(DemoObject obj)
    {
        if (host._db is null || obj.Data.Attack <= 0) return;
        int attack = ObjAttack(obj);                     // 레벨 성장을 먹은 ATK(0x100e73f0)
        // 어디까지 닿는지는 그 물체가 쓰는 work 이 정한다(파일 +31). work 이 없으면 옆 한 칸으로 본다.
        var work = Work(obj.Data.WorkId);
        bool ally = obj.Team == 4;

        bool Reaches(UnitState u) => work is { } w
            ? InWorkRange(w, obj.Col, obj.Row, u.Col, u.Row)
            : Math.Abs(u.Col - obj.Col) + Math.Abs(u.Row - obj.Row) <= 1;

        if (work is { Id: 1525 or 1526 or 1527 } area) { ObjectBarrage(obj, area, ally); return; }

        // 힐 크리스탈(종류 10)의 work 은 회복이다 — 제 편을 고쳐 준다. 나머지는 상대를 친다.
        bool heals = work is { IsHeal: true };
        var target = host._units.Where(u => u.Alive && u.OnField && (heals ? !ObjectHostile(obj, u) && obj.Team >= 0 : ObjectHostile(obj, u)) && Reaches(u))
                           // 겨냥은 0x10060830 → 0x1005dd60: 값 N74 × (1,000,000 − HP) × 10 / (N74 + 거리) 가 큰 쪽 — 사실상 가장 가까운 적(ba-20 O P6).
                           .OrderBy(u => heals ? u.Hp * 100 / Math.Max(1, u.MaxHp)
                                               : -CDiv((1000000 - u.Hp) * host._db.N(74) * 10, host._db.N(74) + Math.Abs(u.Col - obj.Col) + Math.Abs(u.Row - obj.Row)))
                           .FirstOrDefault();
        if (target?.Data is not { } tc) return;

        if (heals)
        {
            int heal = target.MaxHp * (work?.Power ?? 0) / 100;
            if (heal <= 0 || target.Hp >= target.MaxHp) return;
            int before = target.Hp;
            target.Hp = Math.Min(target.MaxHp, target.Hp + heal);
            ShowNumber(target, (target.Hp - before).ToString(), HealColor2);
            return;
        }

        int damage = (host._db.N(3) - host._db.Rdp(tc, target.Hp, target.MaxHp)) * attack / Math.Max(1, host._db.N(3));
        damage = damage * (90 + host._rng.Next(21)) / 100;   // 물체가 주는 피해도 ±Num22% 흔든다(0x1007b8f4 종류 4, ba-20 O P9)
        if (damage <= 0) return;
        target.Hp = Math.Max(0, target.Hp - damage);
        ShowNumber(target, damage.ToString(), DamageColor);
        AddSoul(target, damage / Math.Max(1, host._db.N(43)));   // 물체에 맞아도 SOUL 은 오른다(0x10078f8d)
    }

    /// <summary>
    /// 기총포탑(Obj 54, work 1525)·산성 웅덩이(1526) — 한 칸이 아니라 <b>범위</b>를 쏜다(핸들러 <c>0x100e9400</c>).
    /// 제 칸과 위아래·좌우 네 칸 가운데 반지름 3(맨해튼) 안의 상대가 가장 많은 칸을 고르고(<c>0x1005dd60</c>), 없으면 안 쏜다.
    /// 그 안의 상대를 <b>하나마다 네 번</b> <c>(1000 − RDP) × 공격력 / 1000</c> 씩 친다. 불꽃 Obs 0330(모션 0~2)을 120틱 동안 흩뿌리고
    /// 사격 소리 423 을 낸다. 모션 14→15→16 과 전투를 멈추고 기다리는 것은 아직 없다(가설 — 1526 의 그림·타수는 확인 못 함).
    /// Btl 0298 해적선의 초록 불빛 원판이 이것이다(사용자 보고 「작동을 안 한다」).
    /// </summary>
    internal void ObjectBarrage(DemoObject obj, WorkData work, bool ally)
    {
        if (host._db is null) return;
        int Dist(UnitState u, (int Col, int Row) c) => Math.Abs(u.Col - c.Col) + Math.Abs(u.Row - c.Row) + Math.Abs(HeightAt(u.Col, u.Row) - HeightAt(c.Col, c.Row)) / 2;
        int attack = ObjAttack(obj);                     // 레벨 성장을 먹은 ATK(0x100e73f0)
        int One(UnitState u) => (host._db.N(3) - host._db.Rdp(u.Data!, u.Hp, u.MaxHp)) * attack / Math.Max(1, host._db.N(3)) * (90 + host._rng.Next(21)) / 100;   // ±10%

        // 겨냥은 1525·1526 이 같다 — 물체 차례 0x1006a541 → 0x10060830 → 0x1005dd60 을 둘 다 타고, work 표의 겨냥 인자
        // (+0x14/+0x16/+0x1a/+0x3c)도 한 바이트 안 다르다. 제 칸과 네 이웃 칸 가운데, 반경 3 안의 적 최저 HP 로 점수(1,000,000 − 최저 HP)를 매기고
        // 값 × Num74 × 10 / (Num74 + 거리) 가 가장 큰 칸(0x1005c510·0x1005dd60). 적이 없는 칸은 후보가 아니고, 후보가 없으면 안 쏜다(0x10060894).
        bool Hostile(UnitState u) => u.Alive && u.OnField && u.Data is not null && ObjectHostile(obj, u);
        int num74 = host._db.N(74);
        (int Col, int Row)[] candidates = [(obj.Col, obj.Row), (obj.Col, obj.Row - 1), (obj.Col + 1, obj.Row), (obj.Col, obj.Row + 1), (obj.Col - 1, obj.Row)];
        var scored = candidates.Select(c => (Cell: c, Hits: host._units.Where(u => Hostile(u) && Dist(u, c) <= 3).ToList()))
                               .Where(x => x.Hits.Count > 0)
                               .Select(x => (x.Cell, x.Hits, Score: CDiv((1000000 - x.Hits.Min(u => u.Hp)) * num74 * 10, num74 + Math.Abs(x.Cell.Col - obj.Col) + Math.Abs(x.Cell.Row - obj.Row))))
                               .OrderByDescending(x => x.Score).ToList();
        if (scored.Count == 0) return;
        var best = scored[0];

        // 산성 웅덩이 1526 — <b>고른 칸</b> 둘레 반경 3(효과 범위 층 0) 안 <b>전원</b>(편 안 가림, 아군 포함)에게 즉시 1타 +
        // 중독(상태 3, 값 10) 100%(종류 4 는 굴림 없음). 핸들러 0x100e96d0 의 고리 0x100e9974~0x100e999f 는 적대 검사·늦춤이 없다.
        if (work.Id == 1526)
        {
            var all = host._units.Where(u => u.Alive && u.OnField && u.Data is not null && Dist(u, best.Cell) <= 3).ToList();
            foreach (var u in all)
            {
                int dmg = One(u);
                if (dmg > 0) { u.Hp = Math.Max(0, u.Hp - dmg); ShowNumber(u, dmg.ToString(), DamageColor); AddSoul(u, dmg / Math.Max(1, host._db.N(43))); }
                PutAilment(u, 3, 10, [], null);
                if (u.Hp <= 0 && !SurvivesFatal(u)) KillUnit(u);
            }
            return;
        }

        // 럭키가이 1527 — 고른 칸의 효과 범위(층 0, +0x1a = 3)를 0x100df5c0 로 모아 0x1006fde0 이 적대라 보는 유닛마다
        // 투사체 1발(0x100ea040~0x100ea307, 감사4 K4) — 탄 Obs 158 모션 3 이 틱당 40px 로 날아가(0x100c3b30 · 0x100c3490(40)) 닿을 때 판정(슬롯 0x100c29b0)하고,
        // 닿은 자리에 Obs 111 모션 0 과 Obs 1431 모션 1 이 뜬다(0x100ea1c3 · 0x100ea2cb). 꼬리 109:4 와 파편 253:0(0x100cc890)은 안 넣었다.
        // 전에는 그림 없이 10~40틱 뒤 피해만 들어갔다(ba-22).
        if (work.Id == 1527)
        {
            int fromX = obj.Col * TileW + TileW / 2, fromY = host.CellCenterY(obj.Col, obj.Row);
            foreach (var u in best.Hits)
            {
                int one = One(u);
                var (tx, ty) = host.Btl.UnitFoot(u);
                double flight = Math.Max(1, Math.Ceiling(Math.Sqrt((tx - fromX) * (double)(tx - fromX) + (ty - fromY) * (double)(ty - fromY)) / 40));
                double land = host._lastTime + flight / TicksPerSecond;
                _shots.Add((158, 3, host._lastTime, fromX, fromY, tx, ty, 40, 0, 0, 40, 40, tx > fromX));
                _effects.Add((111, 0, land, tx, ty));
                _effects.Add((1431, 1, land, tx, ty));
                if (one > 0) _delayedHits.Add((land, u, one));
            }
            return;
        }

        // 기총포탑 1525 — 고른 칸 둘레의 적대 유닛마다 4타(0x100e9400).
        for (int k = 0; k < 24; k++)
        {
            int dc = host._rng.Next(-3, 4), dr = host._rng.Next(-3 + Math.Abs(dc), 4 - Math.Abs(dc));
            int col = best.Cell.Col + dc, row = best.Cell.Row + dr;
            _effects.Add((330, host._rng.Next(3), host._lastTime + host._rng.Next(0, 120) / TicksPerSecond, col * TileW + TileW / 2, host.CellCenterY(col, row)));
        }
        // 적마다 독립 타격 개체 넷 — 각각 rand()%100+2 틱 뒤에 따로 맞는다(숫자 넷). 물체는 안 맞는다.
        foreach (var u in best.Hits)
        {
            int one = One(u);
            if (one <= 0) continue;
            for (int k = 0; k < 4; k++) _delayedHits.Add((host._lastTime + (host._rng.Next(100) + 2) / TicksPerSecond, u, one));
        }
    }

    /// <summary>나중에 맞는 타격들(포탑 사격) — (때, 대상, 피해).</summary>
    internal readonly List<(double At, UnitState Unit, int Damage)> _delayedHits = [];

    internal void StepDelayedHits()
    {
        if (_delayedHits.Count == 0 || host._db is null) return;
        for (int i = _delayedHits.Count - 1; i >= 0; i--)
        {
            var (at, u, damage) = _delayedHits[i];
            if (host._lastTime < at) continue;
            _delayedHits.RemoveAt(i);
            if (!u.Alive || u.Hp <= 0) continue;
            int dmg = Math.Min(u.Hp, damage);
            u.Hp -= dmg;
            ShowNumber(u, dmg.ToString(), DamageColor);
            AddSoul(u, dmg / Math.Max(1, host._db.N(43)));
            if (u.Hp <= 0 && !SurvivesFatal(u)) KillUnit(u);
        }
    }

    /// <summary>이미 연 물체(원본 <c>+0x164</c>) — 열린 문은 도장이 빠지고, 상자는 열린 그림으로 남아 계속 길을 막는다. 터진 폭탄 상자만 사라진다.</summary>
    internal readonly HashSet<DemoObject> _opened = [];

    /// <summary>
    /// 전투 이벤트 행동 <b>907</b> — 번호로 물체를 찾아 <b>여닫는다</b>(원본 <c>0x10055a50</c>).
    /// </summary>
    /// <remarks>
    /// 원본은 물체 배열에서 <c>+0x140</c>(배치 번호)이 인자0 인 것을 찾고(<c>0x1004ed40</c>), 인자1 이 0 이 아니면
    /// <b>닫혀 있을 때만</b>, 0 이면 <b>열려 있을 때만</b> 명령 <c>0x2713</c>(여닫기 토글, <c>0x100e7c00</c>)을 보낸 뒤
    /// 그 물체가 다 움직일 때까지(<c>0x1006e320</c>) 다음 줄로 안 간다. 물체의 <c>+0x164</c> 가 0 닫힘 · 1 열림이다.
    /// 번호는 <b>Btl 배치와 Map 배치를 이어 붙인 것</b>이라, 지도가 놓은 문도 이 번호로 잡힌다(Btl 0109 의 101 = Map 0127 의 문 Obj 71).
    /// 쓰는 곳은 Btl 0109 한 곳뿐 — 둘째 턴에 카메라를 (7,18) 로 옮기고 그 옆 문을 연다.
    /// 문 그림(Obs 1224)은 한 장뿐이라 열리면 <b>사라진다</b> — 데모가 손으로 연 문과 똑같이 다룬다.
    /// </remarks>
    internal void ToggleObject(int no, bool open)
    {
        if (Objects.FirstOrDefault(o => o.Record.No == no) is not { } obj) return;
        if (open == _opened.Contains(obj)) return;               // 이미 그 꼴이면 원본도 아무것도 안 한다
        // 원본 0x2713 은 모션 1→2(열기)/3→0(닫기)만 튼다 — 폭발 그림은 없다(감사4 K8). 전에는 Obs 1009 를 띄웠다.
        SetObjectOpen(obj, open);
    }

    /// <summary>문이 닫힌 때 — 그때부터 닫는 모션(3)을 한 번 돌고 닫힌 모션(0)으로 선다(<c>0x100e7c00</c>: 모션 3 → 0, <c>+0x164 = 0</c>).</summary>
    internal readonly Dictionary<DemoObject, double> _closedAt = [];

    /// <summary>중립 포탑을 깨운 때 — 깨우기 모션 9 를 한 번 돈다(명령 0x2717 <c>0x100e7ff0</c>).</summary>
    internal readonly Dictionary<DemoObject, double> _wokenAt = [];

    /// <summary>문·스위치를 열거나 닫는다 — 모션 시각을 적고 판을 다시 찍는다(0x100e7c70·0x100e7cc5 → 0x3f1).</summary>
    internal void SetObjectOpen(DemoObject obj, bool open)
    {
        if (open) { _opened.Add(obj); _openedAt[obj] = host._lastTime; _closedAt.Remove(obj); }
        else { _opened.Remove(obj); _openedAt.Remove(obj); _closedAt[obj] = host._lastTime; }
        RestampObjects();
    }

    /// <summary>
    /// 모드 &gt; 상자 내용물 보기 — 지금 전투의 상자(종류 2)·폭탄 상자(8)와 든 것을 화면 왼쪽 위에 적는다. 연 것은 흐리게.
    /// 원본에 없는 도움 기능이다(사용자 요청). 든 것 = 아이템(.btl 물체 +0x144)이 있으면 아이템, 없으면 GP(+0x146).
    /// </summary>
    /// <summary>상자 목록을 접어 두었나 — 머리 줄만 남긴다. 켤 때마다 펼친 채로 시작한다.</summary>
    internal bool _chestListFolded;

    /// <summary>상자 목록의 접기 단추 자리(판 좌표) — 목록이 안 떠 있으면 null.</summary>
    internal (int X, int Y, int W, int H)? _chestFoldButton;

    /// <summary>접기 단추를 눌렀으면 접거나 펴고 true.</summary>
    internal bool OnChestListClick(int bx, int by)
    {
        if (_chestFoldButton is not var (fx, fy, fw, fh) || bx < fx || bx >= fx + fw || by < fy || by >= fy + fh) return false;
        _chestListFolded = !_chestListFolded;
        return true;
    }

    internal void DrawChestList()
    {
        _chestFoldButton = null;
        if (!host._showChestContents || !host._battleLoaded || host.Mos._mosesOpen || host.FieldOpen || host.TitleScr._titleOpen || host.EpisodesScr._episodesOpen || host._db is not { } db) return;
        var chests = Objects.Where(o => o.Data.Kind is 2 or 8).OrderBy(o => _opened.Contains(o)).ThenBy(o => o.Row).ThenBy(o => o.Col).ToList();
        var lines = new List<(string Text, uint Color)>
        {
            (chests.Count == 0 ? "상자 없음" : $"상자 {chests.Count(o => !_opened.Contains(o))}/{chests.Count}", 0xFFFFE070),
        };
        foreach (var o in _chestListFolded ? [] : chests)
        {
            string what = o.Data.Kind == 8 ? $"폭탄 (공격 {ObjAttack(o)}, 반경 {o.Data.Radius})"
                : o.Record.ItemId > 0 ? (db.Items.GetValueOrDefault(o.Record.ItemId) is { } item ? db.T(item.NameId) : $"아이템 {o.Record.ItemId}")
                : o.Record.Gold > 0 ? $"{o.Record.Gold} GP" : "비어 있음";
            bool opened = _opened.Contains(o);
            lines.Add(($"({o.Col},{o.Row}) {what}{(opened ? " — 열었음" : "")}", opened ? 0xFF808080 : White));
        }
        int x = host._camX + 8, y = host._camY + (host._showStatusBar ? GridTop + 4 : 8);
        // 오른쪽 위에 접기 단추(− 접기 · + 펴기) 자리를 남긴다.
        int w = Math.Max(lines.Max(l => host.GetText(l.Text, l.Color, 12).W) + 12, host.GetText(lines[0].Text, lines[0].Color, 12).W + 40), lineH = 17;
        host.FillRect(x - 4, y - 3, w, lines.Count * lineH + 6, 0xB0000000);
        for (int i = 0; i < lines.Count; i++) host.DrawText(lines[i].Text, x + 2, y + i * lineH, lines[i].Color, 12);
        var fold = (X: x - 4 + w - 19, Y: y - 1, W: 16, H: 15);
        _chestFoldButton = fold;
        bool over = host._mouse.X >= fold.X && host._mouse.X < fold.X + fold.W && host._mouse.Y >= fold.Y && host._mouse.Y < fold.Y + fold.H;
        host.FillRect(fold.X, fold.Y, fold.W, fold.H, over ? 0xFF2A4A8A : 0xFF303030);
        host.StrokeRect(fold.X, fold.Y, fold.W, fold.H, 0xFF909090);
        host.FillRect(fold.X + 4, fold.Y + 7, 8, 1, White);
        if (_chestListFolded) host.FillRect(fold.X + 7, fold.Y + 4, 1, 8, White);
    }

    /// <summary>물체를 칸에 그린다 — 인물보다 먼저(뒤에) 그려 인물이 앞에 서게 한다.</summary>
    internal void DrawObjects()
    {
        EnsureObjectGrowth();                            // 판을 세운 첫 틀에 레벨 성장을 먹인다(0x100e73f0)
        // 겨누는 동안(원본 전투 상태 0xa·0xb) 종류 표 +8(만질 수 있음: 1·2·6·8·10)·+0x10(HP 있음: 7·9·10)이 선 종류는
        // 그림 그리기 방식 바이트([+0x58]+0x13)를 4 로 — 섞기 방식 4 = 그림 가중 16/31 반투명(0x100e6250, 되돌림 0x100e62a0).
        // 리메이크의 상태 0xa = 차례인 인물의 이동 영역이 떠 있을 때, 0xb = 어빌리티·공격 대상을 고를 때.
        bool aiming = host._battleLoaded && IsPlayerTurn && ((_rangeUnit == _turn && _range != null) || _targetWork >= 0);
        foreach (var obj in Objects)
        {
            if (obj.Data.SpriteId <= 0) continue;
            // 부서진·터진 물체는 마지막 모션(4 · 폭탄 상자 8)을 한 번 돌고 사라진다.
            if (_brokenAt.TryGetValue(obj, out var broken))
            {
                int sinceBroken = (int)((host._lastTime - broken.At) * TicksPerSecond);
                if (sinceBroken >= 0 && sinceBroken < (host.UiFor(obj.Data.SpriteId)?.MotionLength(broken.Motion) ?? 0))
                    host.DrawUi(obj.Data.SpriteId, broken.Motion, sinceBroken, obj.Col * TileW + obj.Data.DrawW, host.CellTop(obj.Col, obj.Row) + obj.Data.DrawH, UiBlend.Alpha, loop: false);
                else _brokenAt.Remove(obj);
                continue;
            }
            if (!obj.Alive) continue;
            // 열린 물체 — 문(1·4)은 여는 모션 1 을 한 번 돌고 열린 모션 2 로 남는다(Obs 1217: 0 닫힘 · 1 열림 11틱 · 2 열린 채). 상자 따위는 치운다.
            int motion = 0, tick = (int)(host._lastTime * TicksPerSecond);
            // 상자(2)·스위치(6)도 원본은 치우지 않는다 — 여는 모션 1 을 한 번 돌고 모션 2 에 머물며(0x100e7cfb·0x100e7d28) 판에 계속 찍힌다(감사3 R1).
            // 그림에 모션 2 가 없으면 닫힌 그림(0)으로 둔다 — 치우면 보이지 않는 벽이 된다. 터진 폭탄 상자(8)만 사라진다.
            if (_opened.Contains(obj))
            {
                if (ObjectGone(obj)) continue;
                var sprite = host.UiFor(obj.Data.SpriteId);
                if (obj.Data.Kind is 1 or 4 || (sprite?.MotionLength(2) ?? 0) > 0)
                {
                    int since = (int)((host._lastTime - _openedAt.GetValueOrDefault(obj, host._lastTime)) * TicksPerSecond);
                    int length = sprite?.MotionLength(1) ?? 0;
                    (motion, tick) = since < length ? (1, since) : (2, 0);
                }
            }
            // 그림 기준점 = 칸 왼쪽 위 + .obj 파일 16·18 의 그림 보정(원본은 ×4 · ×40/32 해서 화면 자리 +0x3e/+0x40 에 더한다, 분석-전투 「Obj 배치」).
            // 1칸 물체는 가로 16~20(칸 가운데), 2칸 문은 36·39(두 칸 가운데)라 픽셀 거리로 본다(가설). 예전에는 칸 아래 모서리에 놓아
            // 상자가 반 칸 넘게 내려가 노랑 칸(닿는 물체 칸)과 어긋났다(사용자 보고).
            // 힐 크리스탈은 다 차면 켜진 모션 9, 쓰고 나면 꺼진 모션 10(0x100e8040 · 0x100e8070).
            if (obj.Data.Kind == 10 && !_opened.Contains(obj)) motion = obj.Charged ? 9 : 10;
            // 닫힌 문 — 닫는 모션 3 을 한 번 돈 뒤 닫힌 모션 0(0x100e7c00). 그림에 모션 3 이 없으면 곧장 0.
            bool once = false;
            if (!_opened.Contains(obj) && _closedAt.TryGetValue(obj, out double closedAt))
            {
                int since = (int)((host._lastTime - closedAt) * TicksPerSecond);
                if (since < (host.UiFor(obj.Data.SpriteId)?.MotionLength(3) ?? 0)) (motion, tick, once) = (3, since, true);
                else _closedAt.Remove(obj);
            }
            // 깨운 포탑 — 깨우기 모션 9 를 한 번(0x2717 0x100e7ff0). 그림에 없으면 그대로.
            if (obj.Data.Kind == 9 && _wokenAt.TryGetValue(obj, out double wokenAt))
            {
                int since = (int)((host._lastTime - wokenAt) * TicksPerSecond);
                if (since < (host.UiFor(obj.Data.SpriteId)?.MotionLength(9) ?? 0)) (motion, tick, once) = (9, since, true);
                else _wokenAt.Remove(obj);
            }
            // 쏘는 중인 포탑·함정 — 모션 14 → 15 → 16 을 차례로 한 번씩.
            if (_objActing.TryGetValue(obj, out double actingAt) && host.UiFor(obj.Data.SpriteId) is { } actSprite)
            {
                int since = (int)((host._lastTime - actingAt) * TicksPerSecond), l14 = actSprite.MotionLength(14), l15 = actSprite.MotionLength(15), l16 = actSprite.MotionLength(16);
                if (since < l14) (motion, tick, once) = (14, since, true);
                else if (since < l14 + l15) (motion, tick, once) = (15, since - l14, true);
                else if (since < l14 + l15 + l16) (motion, tick, once) = (16, since - l14 - l15, true);
            }
            int x = obj.Col * TileW + obj.Data.DrawW, y = host.CellTop(obj.Col, obj.Row) + obj.Data.DrawH;
            double fade = aiming && obj.Data.Kind is 1 or 2 or 6 or 7 or 8 or 9 or 10 ? BlendFade(4) : 1;
            host.DrawUi(obj.Data.SpriteId, motion, tick, x, y, UiBlend.Alpha, loop: !once && motion is 0 or 9 or 10, fade: fade);
        }
    }
}

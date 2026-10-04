using System.Drawing;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>판 위 인물 하나의 지금 상태 — 칸 자리, 바라보는 쪽, 걷는 중이면 어디서 어디로 얼마나 왔는지.</summary>
internal sealed class UnitState(DemoUnit unit)
{
    public int ChrCode { get; } = unit.ChrCode;

    /// <summary>편 3·4 는 내 쪽이다 — 이벤트 행동 708 이 편을 바꾸면 이 값도 따라 바뀐다.</summary>
    public bool IsAlly => Side >= 3;

    /// <summary>Btl 레코드의 편 번호 — 4 내 부대 · 3 동맹 · 0~2 적. 이벤트 조건이 이 번호로 부대를 고른다.</summary>
    public int Side { get; set; } = unit.Side;

    /// <summary>Btl 배치표에서의 레코드 번호 — 이벤트가 <c>10000+N</c> 으로 가리키는 번호. 부하는 대장 것을 물려받는다.</summary>
    public int Record { get; } = unit.Record;

    /// <summary>플레이어가 직접 움직이는가 — 편 4 만 그렇다. 편 3(동맹)은 제 차례에 AI 가 움직인다(ba-6).</summary>
    /// <summary>사람이 명령하는 유닛인가 — 편 4. 이벤트 행동 708 로 편이 바뀌면 조종 주체도 따라간다(원본 WAITNEXT 는 매번 +0x78 을 본다).</summary>
    public bool PlayerControlled => Side == 4;

    /// <summary>For.dat 군단 번호(Btl 레코드 파일 15) — 0 이 아니면 부하들이 진형을 지어 따라다닌다.</summary>
    public int LegionId { get; set; } = unit.Legion;

    /// <summary>부하면 대장의 자리 번호, 대장·혼자면 −1. 부하는 차례를 안 받는다(분석-군단).</summary>
    public int LeaderIndex { get; set; } = -1;

    /// <summary>대장일 때 진형에서 내 자리(부하마다 0~5).</summary>
    public int FormationSlot { get; set; } = -1;

    /// <summary>군단 세력(원본 <c>CChr+0x148[군단]</c>) — 처음 1000, 대장이 죽어 물려받으면 0.6배.</summary>
    public int LegionPowerPercent { get; set; } = 1000;

    /// <summary>도착할(걷는 중이면 향하는) 칸. 자리 차지 판정도 이 칸으로 한다.</summary>
    /// <summary>배치 레코드의 레벨 보정 — 파티 레벨에 더해 그 인물의 레벨을 낸다.</summary>
    public int LevelOffset { get; } = unit.LevelOffset;

    /// <summary>AI 이동 방식·깨어남 조건 — 배치 레코드가 사람마다 들고 있다.</summary>
    public int AiMove { get; } = unit.AiMove;

    public int WakeCondition { get; } = unit.WakeCondition;

    public int WakeValue { get; } = unit.WakeValue;

    /// <summary>
    /// 깨어났나 — 깨기 전에는 제 차례마다 쉬기만 한다. 사람이 움직이는 인물(편 4)만 늘 깨어 있다.
    /// 동맹 AI(편 3)도 적처럼 AI 꼬리의 깨어나는 조건을 따른다 — Btl 0096 의 루시엔은 세뇌당해 「조건 3 · 1000틱」으로
    /// 묶여 있는데, 편 3 을 처음부터 깨워 두어 대사와 달리 멋대로 움직였다(사용자 보고).
    /// </summary>
    public bool Awake { get; set; } = unit.PlayerControlled;

    /// <summary>
    /// 지금 전장에 서 있나 — <b>배치 칸이 (0,0) 이면 「아직 안 나온 사람」</b>이다.
    /// </summary>
    /// <remarks>
    /// 원본 로더는 그런 줄의 자리를 <c>(−100,−100)</c> 으로 덮어써 맵 밖으로 보낸다(<c>0x10062263</c>).
    /// 그래서 전멸 판정·차례·이벤트 조건이 그들을 세지 않는다. 이벤트 행동 200·214 가 불러들이고 201 이 내보낸다.
    /// 자료에 그런 줄이 <b>221개</b>(아군 편 제외), 그것을 부르는 전투가 <b>74개</b>다.
    /// </remarks>
    public bool OnField { get; set; } = unit.Col != 0 || unit.Row != 0;

    /// <summary>
    /// 이 사람을 <b>마지막으로 때린</b> 사람 — 원본 <c>유닛+0xfc</c>. 전투 내내 안 지운다.
    /// </summary>
    /// <remarks>이벤트 조건 301 이 「누가 마지막에 때렸나」를 이 값으로 본다. 죽은 뒤에도 남아 있어야 한다.</remarks>
    public UnitState? LastHitBy { get; set; }

    public int Col { get; private set; } = unit.Col;
    public int Row { get; private set; } = unit.Row;
    public Facing Facing { get; set; } = unit.Facing;

    internal int _fromCol = unit.Col, _fromRow = unit.Row;
    internal double _progress = 1;

    public bool IsMoving => _progress < 1;

    /// <summary>그릴 밝기(0~1) — 이스케이프(work 1583)가 겨눈 칸으로 사라졌다 나타날 때만 코드가 손으로 움직인다.</summary>
    public double Fade { get; set; } = 1;

    /// <summary>마비·빙결·이동 불가로 군단에서 떨어진 부하(Legion.cs DetachFollower) — 대장의 레코드 번호로 사건 대상에 잡히거나 파티에 들어가지 않게.</summary>
    public bool Detached { get; set; }

    /// <summary>군단 부하로 판에 선 유닛 — 대장을 물려받아도 파티(_party)에는 안 들어간다(절반 레벨·옛 능력치가 다음 전투로 새지 않게).</summary>
    public bool WasFollower { get; set; }

    // ── 전투 수치 (게임 표를 읽은 뒤 채운다) ──
    public CharacterData? Data { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Tp { get; set; }
    public int MaxTp { get; set; }
    public int Stp { get; set; }
    public int Soul { get; set; }
    public int MaxSoul { get; set; }
    public int Ctp => Data?.Ctp ?? 0;
    public bool HasTurn { get; set; }
    public bool Alive { get; set; } = true;

    /// <summary>전투를 시작한 칸 — RESTART 로 되돌릴 때 쓴다.</summary>
    public int StartCol { get; set; } = unit.Col;   // 배치 단계가 옮기면 RESTART 도 그 자리로
    public int StartRow { get; set; } = unit.Row;

    /// <summary>전투를 시작할 때 보는 쪽 — Btl 레코드의 방향(파일 8, <c>SetAction(0, 방향)</c>: 0 위 · 1 왼 · 2 아래 · 3 오른).</summary>
    public Facing StartFacing { get; set; } = unit.Facing;

    /// <summary>
    /// 전투를 처음부터 다시 할 때 — 자리·상태를 처음으로 돌린다(수치는 InitBattle 이 다시 채운다).
    /// 보는 쪽은 Btl 에 적힌 방향으로 되돌린다 — 전에는 아군은 오른쪽·적은 왼쪽으로 박아서, 오른쪽에서 시작하는
    /// 아군(Btl 0281 의 란·베라모드, 방향 1)도 벽을 보고 섰다(사용자 보고). <paramref name="keepFacing"/> 면 지금 쪽을 둔다.
    /// </summary>
    public void ResetTo(int col, int row, bool keepFacing = false)
    {
        WarpTo(col, row);
        (EntryX, EntryY) = (0, 0);
        OriginCol = col;
        OriginRow = row;
        if (!keepFacing) Facing = StartFacing;
        Alive = true;
        HasTurn = true;
        Stance = 0;
        Action = -1;
        Motion = -1;
        MotionLoops = false;
        Fade = 1;
        ClearStatus();
    }

    /// <summary>자세(<c>+0x4d4</c>) — 1 방어(work 516), 2 회피(work 515). 다음 차례가 오면 풀린다.</summary>
    public int Stance { get; set; }

    /// <summary>상태이상 칸 셋(<c>+0x4bf[3]</c> 번호 · <c>+0x4c2[3]</c> 값) — 분석-전투 「6. 상태이상」.</summary>
    public byte[] StatusId { get; } = new byte[3];
    public short[] StatusValue { get; } = new short[3];

    /// <summary>
    /// 칸마다 그 상태이상을 건 인물 — 원본에는 없다. 상태이상(22·23·24 사망 조건)으로 쓰러지면 처치 경험치를 이들이 나눠 받는다.
    /// 세이브에는 안 실린다(불러온 뒤에 쓰러지면 아무도 안 받는다).
    /// </summary>
    public UnitState?[] StatusSource { get; } = new UnitState?[3];

    /// <summary>슬롯이 아니라 전투용 보정으로 바로 더해지는 것들(번호 30·31·32·33·37·48).</summary>
    public int BonusDex { get; set; }
    public int BonusPsy { get; set; }
    public int BonusDep { get; set; }
    public int BonusMaxTp { get; set; }
    public int BonusMaxSoul { get; set; }
    public int BonusMaxHp { get; set; }

    /// <summary>그 상태이상이 걸려 있으면 값, 아니면 0. 44·45·46 은 「없음」이라 세지 않는다.</summary>
    public int Status(int id)
    {
        for (int i = 0; i < 3; i++)
            if (StatusId[i] == id && id is not (44 or 45 or 46)) return StatusValue[i];
        return 0;
    }

    public bool HasStatus(int id)
    {
        for (int i = 0; i < 3; i++) if (StatusId[i] == id && id is not (44 or 45 or 46)) return true;
        return false;
    }

    public void ClearStatus()
    {
        Array.Clear(StatusId);
        Array.Clear(StatusValue);
        Array.Clear(StatusSource);
        BonusDex = BonusPsy = BonusDep = BonusMaxTp = BonusMaxSoul = BonusMaxHp = 0;
    }

    /// <summary>차례를 시작한 칸 — 이동 영역과 걸음 비용을 이 칸에서 센다.</summary>
    public int OriginCol { get; set; } = unit.Col;
    public int OriginRow { get; set; } = unit.Row;

    /// <summary>앞으로 밟을 칸들 — 한 칸 다 걸으면 다음 칸을 꺼낸다.</summary>
    public Queue<(int Col, int Row)> Path { get; } = new();

    /// <summary>한 번 재생 중인 동작(공격 등). −1 이면 서기/걷기를 알아서 고른다.</summary>
    public int Action { get; private set; } = -1;

    internal double _actionLeft;

    /// <summary>걷거나, 걸을 길이 남았거나, 동작을 재생하는 중.</summary>
    public bool IsBusy => IsMoving || Path.Count > 0 || Action >= 0 || Entering;

    /// <summary>
    /// 그릴 때만 더하는 픽셀 어긋남 — 증원이 맵 변 밖(혼자 140px · 군단 대장 100px · 부하 140~220px)에서 걸어 들어올 때 쓴다(ba-21 B-4).
    /// 논리 칸은 들어설 칸 그대로다(맵 밖 칸을 Col/Row 에 넣으면 칸 번호가 겹친다).
    /// </summary>
    public double EntryX { get; private set; }
    public double EntryY { get; private set; }

    public bool Entering => EntryX != 0 || EntryY != 0;

    public void BeginEntry(double x, double y) => (EntryX, EntryY) = (x, y);

    /// <summary>어긋남을 0 쪽으로 <paramref name="px"/> 만큼 줄인다.</summary>
    public void StepEntry(double px)
    {
        EntryX = Math.Abs(EntryX) <= px ? 0 : EntryX - Math.Sign(EntryX) * px;
        EntryY = Math.Abs(EntryY) <= px ? 0 : EntryY - Math.Sign(EntryY) * px;
    }

    public void PlayAction(int action, double seconds)
    {
        Action = action;
        Motion = -1;
        MotionLoops = false;
        AnimTime = 0;
        _actionLeft = seconds;
    }

    /// <summary>
    /// 동작·방향이 아니라 <b>모션 번호</b>를 바로 트는 중이면 그 번호, 아니면 −1 — 원본 <c>0x100e53c0</c>(PlayMotion).
    /// 천지 파열무의 칼 꽂기(모션 48 → 49 → 50)처럼 방향과 상관없이 한 모션을 쓰는 핸들러가 부른다.
    /// </summary>
    public int Motion { get; private set; } = -1;

    /// <summary>바로 튼 모션을 되풀이하나(원본 반복 1000 — 다음 모션을 틀 때까지 붙들고 있는 자세).</summary>
    public bool MotionLoops { get; private set; }

    /// <summary>모션 컷을 되풀이해 그리나 — 서기·걷기, 또는 되풀이로 튼 모션.</summary>
    public bool Loops => Action < 0 || MotionLoops;

    /// <summary>모션 번호를 <paramref name="seconds"/> 동안 튼다(그동안 바쁨). <paramref name="loop"/> 면 컷을 되풀이한다.</summary>
    public void PlayMotion(int motion, double seconds, bool loop)
    {
        PlayAction(motion / 3, seconds);
        Motion = motion;
        MotionLoops = loop;
    }

    /// <summary>지금 동작(서기/걷기)을 시작한 뒤 흐른 시간(초). 동작이 바뀌면 0 부터 다시 센다.</summary>
    public double AnimTime { get; private set; }

    // 인물마다 서기 숨쉬기가 한꺼번에 맞춰 움직이지 않게 시작 위치를 조금씩 흩뜨린다.
    internal readonly double _idleOffset = (unit.Col * 7 + unit.Row * 13) % 10 / 10.0;

    /// <summary>그릴 자리(칸 단위, 소수) — 걷는 중이면 두 칸 사이.</summary>
    public double X => _fromCol + (Col - _fromCol) * _progress;
    public double Y => _fromRow + (Row - _fromRow) * _progress;

    /// <summary>방금 한 칸을 다 걸었는데 아직 다음 칸이 정해지지 않았다 — 이번 프레임 안에 이어 걸으면 걷기 컷을 잇는다.</summary>
    internal bool _justArrived;

    /// <summary>이번 칸에서 남은 틱(한 칸 = <see cref="GameWindow.StepTicks"/>) — 이어 걸을 때 다음 칸에 넘겨 속도가 들쭉날쭉하지 않게 한다.</summary>
    internal double _carry;

    /// <summary>이번 칸을 걷기 시작한 뒤 흐른 틱 — 자리는 <b>다 채운 틱</b>만큼만 나아간다(틱마다 같은 픽셀).</summary>
    internal double _stepTicks;

    /// <summary>걷기 없이 바로 그 칸에 세운다(걸음 물리기).</summary>
    public void WarpTo(int col, int row)
    {
        Path.Clear();
        _justArrived = false;
        _sliding = false;
        _fromCol = Col = col;
        _fromRow = Row = row;
        _progress = 1;
    }

    /// <summary>밀려나는 중 — 걷기와 달리 두 칸 사이 자리를 코드가 준다(<see cref="SetSlide"/>).</summary>
    internal bool _sliding;

    /// <summary>걷지 않고 그 칸으로 밀려나기 시작한다(비의 넉백) — 자리는 <see cref="SetSlide"/> 로 민다.</summary>
    public void BeginSlide(int col, int row)
    {
        Path.Clear();
        _justArrived = false;
        _fromCol = Col; _fromRow = Row;
        Col = col; Row = row;
        OriginCol = col; OriginRow = row;
        _progress = 0;
        _sliding = true;
    }

    /// <summary>밀려난 정도(0~1) — 1 이면 다 밀려나 그 칸에 선다.</summary>
    public void SetSlide(double progress)
    {
        _progress = Math.Clamp(progress, 0, 1);
        if (_progress < 1) return;
        _sliding = false;
        _fromCol = Col; _fromRow = Row;
    }

    /// <summary>이번 칸을 걷는 데 드는 틱 — 평지 5, 높이가 다르면 6·8·10(<see cref="GameWindow.StepTicksBetween"/>).</summary>
    internal int _stepLength = GameWindow.StepTicks;

    public void BeginStep(int col, int row, int ticks = GameWindow.StepTicks)
    {
        _stepLength = Math.Max(1, ticks);
        if (!IsMoving && !_justArrived) AnimTime = 0;
        double carry = _justArrived ? _carry : 0;
        _justArrived = false;
        _fromCol = Col; _fromRow = Row;
        Col = col; Row = row;
        _stepTicks = Math.Min(carry, _stepLength - 1);
        _progress = Math.Min(Math.Floor(_stepTicks) / _stepLength, 0.99);
    }

    /// <param name="ticks">이번 프레임에 흐른 틱(초당 30).</param>
    public void Advance(double ticks, double dt)
    {
        AnimTime += dt;
        if (Action >= 0 && (_actionLeft -= dt) <= 0) { Action = -1; Motion = -1; MotionLoops = false; AnimTime = _idleOffset; }
        if (!IsMoving || _sliding) return;
        _stepTicks += ticks;
        _progress = Math.Min(1, Math.Floor(_stepTicks) / _stepLength);
        if (!IsMoving) { _justArrived = true; _carry = _stepTicks - _stepLength; }
    }

    /// <summary>이어 걷지 않고 멈췄으면 서기 숨쉬기로 돌린다.</summary>
    public void SettleIfStopped()
    {
        if (!_justArrived) return;
        _justArrived = false;
        AnimTime = _idleOffset;
    }
}

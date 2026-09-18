namespace WarOfGenesis.Assets;

/// <summary>
/// 필드 파일 <c>Fld\NNNN.fld</c> — 장면 3(필드)이 쓰는 배경·물체·인물·스크립트.
/// </summary>
/// <remarks>
/// 로더 <c>0x100eccc0</c> 를 그대로 옮겼다. 필드는 <b>걸어다니는 화면이 아니라 처음부터 끝까지 스크립트가 도는 연출 장면</b>이다 —
/// 누를 수 있는 것이 없고, 사람이 개입하는 곳은 <b>고르기(행동 604·605)</b> 뿐이며, 나가는 길도 스크립트의
/// 행동 <b>6</b>(다른 필드) · <b>7</b>(끝내고 모세스) · <b>10</b>(전투) · <b>11</b>(끝) · <b>12</b>(타이틀) 뿐이다.
/// <para>배치(모두 u16, 332개 전부 파일 끝과 맞는다):</para>
/// <list type="bullet">
/// <item>머리 8워드 — 0 판(늘 2) · <b>1 배경 <c>Bgr</c></b> · 2·3 첫 화면 자리 · 4 ? · <b>5 BGM</b> · <b>6 물체 수</b> · 7 예비.</item>
/// <item>물체 16바이트 × n — 0 열쇠(스크립트가 <c>10000+열쇠</c> 로 가리킨다) · 1 그림 · 2·3 자리 · 4 갈래 · <b>5 층 0~7</b>.</item>
/// <item>[수, 예비] + 그림 16바이트 × n — 배경 위에 얹는 <c>Bgr</c> 조각(332개 중 307개가 0장이다).</item>
/// <item>[수, 예비] + 인물 10바이트 × n — 0 열쇠 · <b>1 <c>Chr</c> 번호</b> · 2·3 자리 · 4 층(−1 이면 화면 붙박이).</item>
/// <item>스크립트 — 수 한 워드, 이벤트마다 (최대 발동 수, 조건 수, 조건 18바이트씩, 행동 수, 행동 18바이트씩).</item>
/// </list>
/// 이벤트 <b>0</b> 은 「어떤 이벤트를 살려 둘까」 목록이다 — 매 틀 그 행동들의 인자 0 을 훑어 그 번호의 이벤트를 돌린다(<c>0x100f49a0</c>).
/// 스크립트 꼴이 <c>Chp</c> 와 같아 실행기도 같다.
/// </remarks>
public sealed record FieldFile(int Id, int Background, int CameraX, int CameraY, int Bgm,
                               IReadOnlyList<FieldObject> Objects, IReadOnlyList<FieldPerson> People,
                               IReadOnlyList<FieldEvent> Events, int TailBytes)
{
    /// <summary>스크립트까지 읽고 파일 끝과 딱 맞았나.</summary>
    public bool Exact => TailBytes == 0;

    public static FieldFile? Parse(int id, byte[]? b)
    {
        if (b == null || b.Length < 16) return null;
        try
        {
            int o = 0;
            short W() { short v = BitConverter.ToInt16(b, o); o += 2; return v; }
            short[] Words(int n) { var a = new short[n]; for (int i = 0; i < n; i++) a[i] = W(); return a; }
            int Count() { int n = W(); W(); return n >= 0 ? n : throw new ArgumentException("묶음 개수가 음수입니다."); }

            var head = Words(8);
            int objectCount = head[6];
            if (objectCount < 0) return null;

            var objects = new List<FieldObject>();
            for (int i = 0; i < objectCount; i++)
            {
                var w = Words(8);
                objects.Add(new FieldObject(w[0], w[1], w[2], w[3], w[4], Math.Clamp(w[5], (short)0, (short)7)));
            }

            int pictureCount = Count();
            o += 16 * pictureCount;                        // 그림 조각은 아직 안 쓴다

            int peopleCount = Count();
            var people = new List<FieldPerson>();
            for (int i = 0; i < peopleCount; i++)
            {
                var w = Words(5);
                people.Add(new FieldPerson(w[0], w[1], w[2], w[3], Math.Min(w[4], (short)7)));
            }

            var events = new List<FieldEvent>();
            int eventCount = W();
            if (eventCount < 0) return null;
            for (int i = 0; i < eventCount; i++)
            {
                int maxFire = W();
                var conditions = ReadCommands(b, ref o);
                var actions = ReadCommands(b, ref o);
                events.Add(new FieldEvent(i, maxFire, conditions, actions));
            }

            return new FieldFile(id, head[1], head[2], head[3], head[5], objects, people, events, b.Length - o);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static List<ScriptCommand> ReadCommands(byte[] b, ref int o)
    {
        int n = BitConverter.ToInt16(b, o);
        o += 2;
        if (n < 0) throw new ArgumentException("명령 수가 음수입니다.");
        var list = new List<ScriptCommand>(n);
        for (int i = 0; i < n; i++)
        {
            int code = BitConverter.ToInt16(b, o);
            var args = new short[8];
            for (int k = 0; k < 8; k++) args[k] = BitConverter.ToInt16(b, o + 2 + 2 * k);
            o += 18;
            list.Add(new ScriptCommand(code, args));
        }
        return list;
    }
}

/// <summary>필드에 놓인 그림 하나 — 스크립트는 <c>10000+<see cref="Key"/></c> 로 가리킨다.</summary>
public sealed record FieldObject(int Key, int Picture, int X, int Y, int Kind, int Layer);

/// <summary>필드에 선 인물 하나 — <see cref="ChrCode"/> 가 파티에 있으면 그 인물을 그대로 쓴다.</summary>
public sealed record FieldPerson(int Key, int ChrCode, int X, int Y, int Layer);

/// <summary>필드 스크립트의 이벤트 하나. <see cref="MaxFire"/> 가 0 이면 몇 번이고 돈다.</summary>
public sealed record FieldEvent(int Index, int MaxFire,
                                IReadOnlyList<ScriptCommand> Conditions, IReadOnlyList<ScriptCommand> Actions);

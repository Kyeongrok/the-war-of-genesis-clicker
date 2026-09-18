using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 필드 화면(장면 3) — 챕터가 「저절로 들어가는 장소」로 쓰는 연출 장면.
/// </summary>
/// <remarks>
/// 필드는 <b>걸어다니는 화면이 아니다</b>. 누를 수 있는 것이 없고 처음부터 끝까지 스크립트가 돌며,
/// 사람이 개입하는 곳은 <b>고르기(행동 604·605)</b> 뿐이다. 나가는 길도 스크립트뿐 —
/// <c>6</c> 다른 필드 · <c>7</c> 끝내고 모세스 · <c>10</c> 전투 · <c>11</c> 끝 · <c>12</c> 타이틀.
/// <para>
/// 이게 있어야 <b>진행 깃발이 서기 시작한다</b>. <c>Chp 0010</c> 은 장소 5(프롤로그, 자동 발생)로 <c>Fld 0019</c> 를 열고,
/// 그것이 <c>Fld 0012</c> 로 넘어가 고르기 끝에 <b>깃발 13 = 1</b> 을 세운다 — 그래야 「코어헌터 훈련장(<c>Btl 0045</c>)」이 열린다.
/// </para>
/// 데모는 <b>연출을 뺀 최소판</b>이다: 배경 한 장과 대사·고르기만 그리고, 인물·물체·카메라·화면 전환(202·208·302·400번대·900)은 넘긴다.
/// 진행에 필요한 조건 <c>0·100·101</c> 과 행동 <c>0·1·2·3·6·7·10·11·12·100·101·102·103·600·601·604·605</c> 는 모두 돈다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private FieldFile? _field;
    private TalkTable? _fieldTalk;

    /// <summary>필드 안에서만 쓰는 바이트 변수 — 고르기 답이 여기 들어간다(<c>0x101bfeac</c>).</summary>
    private readonly byte[] _fieldVars = new byte[256];

    private int _fieldEvent = -1, _fieldPc;
    private double _fieldWaitUntil;

    /// <summary>이벤트마다 지금까지 돈 횟수.</summary>
    private int[] _fieldFired = [];

    /// <summary>고르는 중인 항목들 — (글, 몇 번째). 고른 차례가 <see cref="_fieldChoiceVar"/> 에 들어간다.</summary>
    private List<string>? _fieldChoices;
    private int _fieldChoiceVar, _fieldChoicePick;

    private bool FieldOpen => _field != null;

    /// <summary>그 필드를 연다. 자료가 없으면 false.</summary>
    private bool OpenField(int id)
    {
        try
        {
            var files = GameFiles.FromFolder(AssetsFolder.Find("data"));
            if (FieldFile.Parse(id, files.Read("Fld", $"{id:D4}.fld")) is not { } field) return false;
            _field = field;
            _fieldTalk = TalkTable.Parse(files.Read("Tlk", $"{id:D4}.tlf"));
            _fieldFired = new int[field.Events.Count];
            Array.Clear(_fieldVars);
            _fieldEvent = -1;
            _fieldPc = 0;
            _fieldWaitUntil = 0;
            _fieldChoices = null;
            _mosesOpen = false;
            _talk = null;
            // 필드 배경은 640×480 보다 넓다 — 머리가 정한 첫 화면 자리부터 보여 준다.
            ShowMosesBackground(field.Background, field.CameraX, field.CameraY);
            _mixer.StopMusic();
            if (field.Bgm > 0) PlayMusicFile(field.Bgm, loop: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            return false;
        }
    }

    private void CloseField()
    {
        _field = null;
        _fieldTalk = null;
        _talk = null;
        _fieldChoices = null;
    }

    /// <summary>
    /// 필드 스크립트를 한 걸음 나아가게 한다.
    /// </summary>
    /// <remarks>
    /// 이벤트 <b>0</b> 의 행동 인자 0 이 「살려 둘 이벤트」 목록이다(<c>0x100f49a0</c>) — 그 차례로 조건을 보고 하나를 돌린다.
    /// </remarks>
    private void UpdateField()
    {
        if (_field is not { } field) return;
        if (_talk != null || _fieldChoices != null) return;          // 대사·고르기가 떠 있으면 기다린다
        if (_fieldWaitUntil > _lastTime) return;

        if (_fieldEvent < 0)
        {
            foreach (var wanted in field.Events.Count > 0 ? field.Events[0].Actions : [])
            {
                int index = wanted.Args.Length > 0 ? wanted.Args[0] : -1;
                if ((uint)index >= field.Events.Count || index == 0) continue;
                var e = field.Events[index];
                if (e.MaxFire > 0 && _fieldFired[index] >= e.MaxFire) continue;
                if (!e.Conditions.All(FieldCondition)) continue;
                _fieldFired[index]++;
                _fieldEvent = index;
                _fieldPc = 0;
                break;
            }
            if (_fieldEvent < 0) { LeaveField(); return; }           // 더 돌 이벤트가 없으면 나간다
        }

        var running = field.Events[_fieldEvent];
        while (_fieldEvent >= 0 && _talk == null && _fieldWaitUntil <= _lastTime)
        {
            if (_fieldPc >= running.Actions.Count) { _fieldEvent = -1; return; }
            // 고르기(604)를 낸 뒤에는 뒤따르는 605 들을 <b>먼저 다 읽어</b> 항목을 채우고, 그다음에 사람을 기다린다.
            if (_fieldChoices != null && running.Actions[_fieldPc].Code != 605) return;
            if (!RunFieldAction(running.Actions[_fieldPc++])) return;  // false = 필드를 떠났다
        }
    }

    /// <summary>나갈 길이 없는 필드(찌꺼기)는 그냥 모세스로 돌아간다.</summary>
    private void LeaveField()
    {
        CloseField();
        OpenMoses();
    }

    private bool FieldCondition(ScriptCommand c)
    {
        short A(int i) => i < c.Args.Length ? c.Args[i] : (short)0;
        return c.Code switch
        {
            0 => true,                                                          // 언제나
            100 => Compare(_fieldVars[A(0) & 0xFF], A(1), A(2)),                // 필드 변수
            101 => Compare(A(0) >= 0 && A(0) < _flags.Length ? _flags[A(0)] : 0, A(1), A(2)),
            _ => false,                                                         // 안 만든 조건은 안 터뜨린다
        };
    }

    /// <summary>행동 하나. 필드를 떠났으면 false.</summary>
    private bool RunFieldAction(ScriptCommand a)
    {
        short A(int i) => i < a.Args.Length ? a.Args[i] : (short)0;
        switch (a.Code)
        {
            case 0:                                          // 다른 이벤트 부르기 — 이벤트 0 목록이 이미 돌리므로 넘긴다
            case 1: break;                                   // 띄운 것이 끝나기를 기다림 — 데모는 대사마다 이미 멈춘다
            case 2: _fieldWaitUntil = _lastTime + A(0) / TicksPerSecond; break;
            case 3: _fieldEvent = -1; break;                 // 이 이벤트 접기

            case 6:                                          // 다른 필드로
                if (OpenField(A(0))) return false;
                LeaveField();
                return false;
            case 7:
            case 11: LeaveField(); return false;             // 필드 끝 — 챕터가 있으니 모세스로
            case 10:                                         // 전투
                CloseField();
                if (!StartBattle(A(0))) OpenMoses();
                return false;
            case 12: CloseField(); OpenTitle(); return false;

            case 100: _fieldVars[A(0) & 0xFF] = (byte)Math.Clamp((int)A(1), 0, 255); break;
            case 101: _fieldVars[A(0) & 0xFF] = FieldArith(_fieldVars[A(0) & 0xFF], A(1), A(2)); break;
            case 102:
                if (A(0) > 0 && A(0) < _flags.Length) _flags[A(0)] = (byte)Math.Clamp((int)A(1), 0, 255);
                break;
            case 103:
                // 원본은 네 갈래가 모두 마지막으로 떨어져 <b>연산자 번호를 한 번 더 더한다</b>(0x100f30ef) — 그 흠까지 그대로 옮긴다.
                if (A(0) > 0 && A(0) < _flags.Length)
                    _flags[A(0)] = (byte)Math.Clamp(FieldArith(_flags[A(0)], A(1), A(2)) + A(1), 0, 255);
                break;

            case 600:
            case 601:
            case 602: ShowFieldTalk(a.Code == 600, A(0), A(1)); break;
            case 604: BeginFieldChoice(A(0), A(1), A(2)); break;
            case 605: _fieldChoices?.Add(FieldText(A(0))); break;
        }
        return true;
    }

    private static byte FieldArith(byte now, int op, int value) => (byte)Math.Clamp(op switch
    {
        0 => now + value,
        1 => now - value,
        2 => now * value,
        _ => value != 0 ? now / value : now,
    }, 0, 255);

    private string FieldText(int id) => _fieldTalk?[id] ?? "";

    /// <summary>필드 대사 — 말하는 이는 <c>10000+인물 열쇠</c> 다.</summary>
    private void ShowFieldTalk(bool box, int speaker, int textId)
    {
        string name = "";
        if (_field is { } field && speaker >= 10000
            && field.People.FirstOrDefault(p => p.Key == speaker - 10000) is { } person
            && _db?.Character(person.ChrCode) is { } c)
            name = _db.T(c.NameId);
        _talk = (box, -1, name, FieldText(textId), 0, _lastTime);
        _talkFilled = false;
    }

    /// <summary>고르기 시작(행동 604) — 뒤이은 605 들이 항목을 더한다.</summary>
    private void BeginFieldChoice(int speaker, int textId, int variable)
    {
        _fieldChoices = [FieldText(textId)];
        _fieldChoiceVar = variable & 0xFF;
        _fieldChoicePick = 0;
    }

    /// <summary>고르기 창이 떠 있으면 클릭을 처리하고 true.</summary>
    private bool OnFieldClick(int bx, int by)
    {
        if (_fieldChoices is not { Count: > 0 } choices) return false;
        var (x, y, w, h) = FieldChoiceRect(choices.Count);
        int row = (by - y - 12) / 22;
        if (bx < x || bx >= x + w || row < 0 || row >= choices.Count) return true;
        _fieldVars[_fieldChoiceVar] = (byte)(row + 1);      // 고른 차례는 1부터
        _fieldChoices = null;
        Play(MosesClickSound);
        return true;
    }

    /// <summary>고르기 창 — 640×480 틀 안, 대사 상자 바로 위에 놓는다.</summary>
    private (int X, int Y, int W, int H) FieldChoiceRect(int rows)
    {
        var (fx, fy) = MosesOrigin();
        int w = 360, h = rows * 22 + 24;
        return (fx + (MosesW - w) / 2, fy + 360 - h, w, h);
    }

    private void DrawField()
    {
        if (_field is null) return;
        var (ox, oy) = MosesOrigin();

        FillRect(0, _camY, BoardWidth, ViewHeight, 0xFF000000);
        if (_mosesBg is { } bg)
            for (int y = 0; y < MosesH; y++)
                for (int x = 0; x < MosesW; x++)
                    SetPixel(ox + x, oy + y, bg[y * MosesW + x] | 0xFF000000);

        DrawTalk();

        if (_fieldChoices is { Count: > 0 } choices)
        {
            var (x, y, w, h) = FieldChoiceRect(choices.Count);
            DarkenRect(x - 1, y - FrameTitleH - 1, w + 2, h + FrameTitleH + 2, 8);
            DrawGameFrame(x, y, w, h, "");
            for (int i = 0; i < choices.Count; i++)
                DrawText(choices[i], x + 16, y + 14 + i * 22, i == _fieldChoicePick ? 0xFF00FFFF : White, 13);
        }
        DrawToast();
    }
}

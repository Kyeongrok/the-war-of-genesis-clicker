using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 도구 &gt; 캐릭터 에디터 — 인물마다 레벨(누적 경험치)과 장비 칸의 아이템·어빌리티, 파티의 소지금을 게임 안에서 바꾼다(사용자 요청, 원본에 없는 시험용 도구).
/// </summary>
/// <remarks>
/// 대상은 지금 파티의 동료(전직 화면에 서는 인물)다 — 자료는 판에 서 있으면 그 유닛의 것, 아니면 명부(<c>_party</c>)의 것. 레벨은 <see cref="GameDatabase.SetLevel"/>(스크립트 805 와 같은 식)로
/// 능력치까지 다시 세고 누적 경험치를 레벨 × 100 으로 맞춘다. 장비는 칸마다 아이템 번호를 앞뒤로 넘긴다(0 = 비움). 바꾸면 명부와 판 위의 유닛에 바로 든다.
/// 오른쪽 칸은 「장비」·「어빌리티」·「외형」 갈래 — 어빌리티 갈래는 게임의 모든 어빌리티를 줄로 올려 ＋ 로 넣고(Lv1) 레벨을 올리고, − 로 내리다 Lv1 에서 지운다.
/// 외형 갈래는 다른 인물 레코드의 그림·초상(「모습」)과 목소리 묶음(「목소리」)을 따로 빌려 온다 — 살라딘을 다른 복장으로, 베라모드를 유블레인 모습으로(사용자 요청 menu-67).
/// 전투 그림·초상·목소리(맞을 때·차례)에 들고, 필드에서 걷는 그림은 원본 레코드 그대로다.
/// 직업 목록·선행 어빌리티·EXP 는 따지지 않는다(사용자 요청 menu-59).
/// 고칠 인물이 없으면(타이틀처럼 아직 아무것도 안 불러온 때) 세이브 파일 목록부터 보인다 — 고르면 그 세이브를 불러온 뒤 고친다(사용자 요청 menu-8).
/// 맨 위 줄은 파티 것: 소지금 올리고 내리기와 「나야트레이 등장」(Chr 563 을 801 처럼 지금 파티에 넣는다).
/// </remarks>
internal sealed unsafe class CharEditScreen(GameWindow host)
{
    internal const int MenuCharEdit = 1160;
    internal const int W = 600, H = 440, PartyH = 36, SaveRowH = 24, MoneyMax = 99_999_999;
    internal const int NayatreiChr = 563;

    /// <summary>DUELDX_CHAREDIT 가 있으면 열어 둔 채 시작한다(화면 밖 시험용).</summary>
    internal bool _open = Environment.GetEnvironmentVariable("DUELDX_CHAREDIT") != null;
    internal int _chosen;           // 고른 인물(목록 순번)
    internal bool _pickSave;        // 인물이 있어도 세이브 목록을 보인다(「세이브 고르기」)
    internal int _saveTop;          // 세이브 목록 스크롤
    internal int _tab;              // 오른쪽 칸 갈래 — 0 장비 · 1 어빌리티 · 2 외형
    internal int _abTop;            // 어빌리티 목록 스크롤
    internal int _lookTop;          // 외형 목록 스크롤
    private (int Chr, List<int> Ids)? _abRows;
    private (int Chr, List<Look> Rows)? _lookRows;
    private List<Look>? _looks;
    /// <summary>에디터로 처음 바꾸기 전의 모습 — 「원래대로」가 되돌린다(701 이 바꿔 둔 복장일 수도 있어 원본 레코드가 아니라 이것).</summary>
    private readonly Dictionary<int, (ushort Sprite, ushort Face, ushort Voice)> _firstLook = [];
    private List<(int Slot, SystemMenu.SaveState Head)>? _saves;

    /// <summary>파트1 을 하는 동안 고치는 쪽 — eld · 무리 가방 · 파트1 장비 다섯 칸 · 파트1 어빌리티(<see cref="Part1CharEdit"/>).</summary>
    private Part1CharEdit? _p1;
    internal Part1CharEdit P1 => _p1 ??= new Part1CharEdit(host, this);

    /// <summary>장비 칸의 아이템 고르기 창(<see cref="CharEditItemPick"/>, menu-95).</summary>
    private CharEditItemPick? _itemPick;
    internal CharEditItemPick ItemPick => _itemPick ??= new CharEditItemPick(host, this);

    /// <summary>파트1 자료를 고치나 — 파트1 타이틀 · 챕터 · 마을 · 전투.</summary>
    internal bool Part1Mode => P1.Active;

    /// <summary>파일이 있는 세이브 칸들 — 파일을 통째로 읽는 일이라 창을 열 때 한 번만 훑는다.</summary>
    internal List<(int Slot, SystemMenu.SaveState Head)> Saves() =>
        _saves ??= [.. Enumerable.Range(0, SlotsScreen.SaveSlots + SlotsScreen.AutoSlots)
                       .Select(slot => (Slot: slot, Head: host.SlotsScr.SlotHead(slot))).Where(s => s.Head != null).Select(s => (s.Slot, s.Head!))];

    /// <summary>고칠 게임이 아직 없나 — 타이틀·기록 고르기 화면 뒤에는 시연용 기본 판이 서 있어서 인물 수만으로는 모른다.</summary>
    /// <remarks>파트1 은 파트2 타이틀 위에서 열려 그 표시(<c>_titleOpen</c>)가 챕터 · 마을에서도 서 있다 — 파트1 은 파트1 타이틀 차림표만 본다. 전에는 파트1 챕터 · 마을에서
    /// 에디터를 열면 늘 세이브 목록만 나오고, 불러와도 다시 목록이었다(카페 제보).</remarks>
    internal bool NoGame(List<int> people) =>
        people.Count == 0 || host.RecordsScr._recordsOpen || (Part1Mode ? host.Part1TitleScr.TitleMenuOpen : host.TitleScr._titleOpen);

    internal void Toggle()
    {
        _open = !_open;
        _pickSave = false;
        _saves = null;
        _abRows = null;
        _lookRows = null;
        _p1?.Forget();
        _itemPick?.Close();
    }

    /// <summary>고른 세이브를 불러온다 — 그 뒤로는 불러온 게임의 인물을 고친다.</summary>
    internal void LoadSave(int slot)
    {
        if (!host.Sys.LoadBattleFrom(SlotsScreen.SlotPath(slot))) return;
        (_pickSave, _chosen, _pickerOpen) = (false, 0, false);
    }

    internal void ChangeMoney(int delta) => host.Mos._shopMoney = Math.Clamp(host.Mos._shopMoney + delta, 0, MoneyMax);

    /// <summary>나야트레이(Chr 563)를 지금 파티에 넣는다 — 스크립트 801 과 같은 길이라 판에는 다음 전투부터 선다.</summary>
    internal void AddNayatrei()
    {
        if (host.Mos._members.Contains(NayatreiChr)) { host.Toast("나야트레이는 이미 파티에 있습니다"); return; }
        host.PartySt.AddMember(host.EpisodesScr._partyNo, NayatreiChr);
        if (!host._party.ContainsKey(NayatreiChr)) { host.Toast("나야트레이 자료(Chr 0563)를 못 읽었습니다"); return; }
        _chosen = Math.Max(0, People().IndexOf(NayatreiChr));
        host.Toast("나야트레이가 파티에 들어왔습니다 — 전투에는 다음 전투부터 나옵니다");
    }

    internal static readonly (int Dx, int Delta, string Text)[] MoneySteps = [(170, 1000, "+1천"), (228, 100000, "+10만")];   // ±1만은 뺐다(사용자 요청 menu-102)

    /// <summary>
    /// 고칠 수 있는 인물 — 전직 화면에 서는 인물과 같다(<see cref="MosesScene.StyleParty"/>: 스크립트 801 로 들어온 동료, 그 차례대로).
    /// 전에는 명부 전체에 판 위의 내 편을 더해서 동맹 NPC(제이슨)나 파티를 떠난 인물까지 나왔다(사용자 요청 menu-10).
    /// 전투 중이면 그 뒤에 이 판에 우리 편(편 4 · 동맹 편 3)으로 나온 인물을 더한다 — Btl 0078 의 루시엔(아직 801 전)·샤크바리(동맹)처럼
    /// 동료가 아닌데 함께 싸우는 인물(사용자 요청 menu-82). 이들은 명부에 없어 바꾼 것이 이 전투에만 든다.
    /// </summary>
    internal List<int> People()
    {
        if (Part1Mode) return P1.People();
        var people = host.Mos.StyleParty();
        if (!host._battleLoaded || host.Mos._mosesOpen || host.FieldOpen) return people;
        foreach (var u in host._units)
            if (OnOurSide(u) && !people.Contains(u.ChrCode)) people.Add(u.ChrCode);
        return people;
    }

    private static bool OnOurSide(UnitState u) => u.Side is 3 or 4 && u.LeaderIndex < 0;

    internal CharacterData? DataOf(int chr) =>
        host._units.FirstOrDefault(u => u.ChrCode == chr && OnOurSide(u))?.Data ?? host._party.GetValueOrDefault(chr);

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    /// <summary>바꾼 기록을 명부와 판 위의 그 인물에 넣고 최대치를 다시 센다.</summary>
    internal void Apply(int chr, CharacterData changed)
    {
        host.Stats._charEdited = true;
        if (host._party.ContainsKey(chr) || host.Mos._members.Contains(chr)) host._party[chr] = changed;
        foreach (var u in host._units.Where(u => u.ChrCode == chr && OnOurSide(u)))
        {
            u.Data = changed;
            host.StatusScr.RefreshUnitStats(u);
            u.Hp = Math.Min(Math.Max(1, u.Hp), u.MaxHp);
        }
    }

    internal void ChangeLevel(int chr, int delta)
    {
        if (host._db is not { } db || DataOf(chr) is not { } c) return;
        int level = Math.Clamp(c.Level + delta, 1, 99);
        Apply(chr, db.SetLevel(c, level) with { Level = (ushort)level, CumExp = level * 100 });
    }

    internal void ChangeItem(int chr, int slot, int delta)
    {
        if (host._db is not { } db || DataOf(chr) is not { } c || (uint)slot >= c.Items.Length) return;
        // 0(비움)과 있는 아이템 번호들을 한 줄로 놓고 앞뒤로 넘긴다.
        var ids = new List<int> { 0 };
        // 아이템 표에는 갈래 머리줄(「=== 요요 ===」)과 이름 없는 빈 줄이 섞여 있다 — 건너뛴다.
        ids.AddRange(db.Items.Where(kv => kv.Key != 0 && db.T(kv.Value.NameId) is { Length: > 0 } name && !name.StartsWith('=')).Select(kv => kv.Key).OrderBy(i => i));
        int at = Math.Max(0, ids.IndexOf(c.Items[slot]));
        int next = ids[((at + delta) % ids.Count + ids.Count) % ids.Count];
        Apply(chr, c with { Items = [.. c.Items.Select((it, k) => k == slot ? (ushort)next : it)] });
    }

    /// <summary>그 칸에 그 아이템을 낀다(고르기 창) — 0 은 비우기.</summary>
    internal void SetItem(int chr, int slot, int id)
    {
        if (DataOf(chr) is not { } c || (uint)slot >= c.Items.Length) return;
        Apply(chr, c with { Items = [.. c.Items.Select((it, k) => k == slot ? (ushort)id : it)] });
    }

    /// <summary>
    /// 어빌리티 갈래의 줄 — 이름이 있고 없애지 않은(<see cref="GameDatabase.IsHidden"/>) 어빌리티 모두. 그 인물이 가진 것을 앞에 놓는다.
    /// 줄 차례는 인물을 고를 때 한 번 정해 둔다 — 넣고 지울 때마다 다시 세우면 누르던 줄이 발밑에서 달아난다.
    /// </summary>
    internal List<int> AbilityRows(GameDatabase db, CharacterData c)
    {
        if (_abRows is { } rows && rows.Chr == c.Code) return rows.Ids;
        // 어빌리티 표에도 갈래 머리줄(「=== … ===」)과 이름 없는 빈 줄이 섞여 있다 — 건너뛴다. work 가 하나도 없는 것은 넣어도 쓸 수 없다(패시브는 빼고).
        List<int> ids = [.. db.Abilities.Values
            .Where(ab => ab.Id != 0 && !db.IsHidden(ab.Id) && (ab.WorkByLevel.Count > 0 || ab.IsPassive || c.HasAbility(ab.Id))
                         && db.T(ab.NameId) is { Length: > 0 } name && !name.StartsWith('='))
            .OrderBy(ab => c.HasAbility(ab.Id) ? 0 : 1).ThenBy(ab => ab.Id).Select(ab => ab.Id)];
        (_abRows, _abTop) = ((c.Code, ids), 0);
        return ids;
    }

    /// <summary>어빌리티를 넣거나(없던 것 +1 = Lv1) 레벨을 올리고 내린다 — Lv1 에서 내리면 지우고, 장착 칸에 끼워 둔 패시브였으면 그 칸도 비운다.</summary>
    internal void ChangeAbility(int chr, int id, int delta)
    {
        if (host._db is not { } db || DataOf(chr) is not { } c || !db.Abilities.TryGetValue(id, out var ab)) return;
        var list = c.Abilities.ToList();
        int i = list.FindIndex(a => a.Ability == id);
        if (i < 0 && delta < 0) return;
        int level = (i < 0 ? 0 : list[i].Level) + delta;
        if (level > Math.Max(1, (int)ab.MaxLevel)) { host.Toast("최대 레벨입니다"); return; }
        var passives = c.Passives;
        if (level <= 0)
        {
            list.RemoveAt(i);
            passives = [.. passives.Select(p => p == id ? (ushort)0 : p)];
        }
        else if (i < 0) list.Add(((ushort)id, (ushort)level));
        else list[i] = ((ushort)id, (ushort)level);
        Apply(chr, c with { Abilities = [.. list], Passives = passives });
    }

    /// <summary>
    /// 빌려 올 수 있는 모습 하나 — 그 그림을 쓰는 인물 레코드(이름·칭호를 보이는 데 쓴다)와 그 인물의 목소리 묶음.
    /// 파트1(전작) 인물이면 <paramref name="Part1Name"/> 에 이름이 들고 <paramref name="Chr"/> 는 파트1 레코드 번호, 그림 번호는 <see cref="Part1Game.ObsBase"/> 위다.
    /// </summary>
    internal sealed record Look(int Chr, ushort NameId, ushort TitleId, ushort Sprite, ushort Face, ushort Voice, string? Part1Name = null, string? CustomName = null);

    /// <summary>사용자 그림(<see cref="CustomSprites"/>, menu-103) — 초상 · 목소리는 없어(0) 입혀도 제 것 그대로.</summary>
    /// <summary>
    /// 외형 줄을 다시 짓게 한다 — 갈래를 누를 때마다. 사용자 그림은 게임을 켠 채 폴더에 넣을 수 있어 처음 지은 줄을 그대로 두면 안 보였다(사용자 보고).
    /// </summary>
    internal void ForgetLooks() => _lookRows = null;

    internal static IEnumerable<Look> CustomLooks() =>
        CustomSprites.List().Select(s => new Look(0, 0, 0, (ushort)s.Id, 0, 0, CustomName: s.Name));

    /// <summary>외형 갈래에 올리는 파트1 인물 레코드 — 지금은 마르자나(Chr 0001) 하나(사용자 요청 menu-76: 「그 캐릭터 하나만 일단」).</summary>
    internal static IEnumerable<int> Part1Chrs => Part1Game.Shipped.Select(p => p.Code);

    /// <summary>파트1 인물의 모습 — 파트1 폴더가 없거나 그림이 전투에 못 서면 뺀다. 목소리는 아직 안 옮겼다(0).</summary>
    internal IEnumerable<Look> Part1Looks() =>
        Part1Chrs.Select(Part1Game.Character).OfType<Part1Character>()
            .Where(p => p.SpriteId > 0 && BattleReady(Part1Game.ObsBase + p.SpriteId))
            .Select(p => new Look(p.Code, 0, 0, (ushort)(Part1Game.ObsBase + p.SpriteId), (ushort)(Part1Game.ObsBase + p.FaceId), 0, p.Name));

    internal static string LookName(GameDatabase db, Look l) =>
        l.CustomName is { } custom ? $"{custom} · 사용자 그림" : l.Part1Name is { } p1 ? $"{p1} · 파트1" : db.T(l.NameId);

    internal const int LookScanMax = 1200;

    /// <summary>
    /// 모든 인물 레코드의 모습 — 이름과 그림이 같은 것은 하나로 친다(그림만 같아도 이름이 다르면 따로 — 살라딘 868 이 다른 인물 줄에 묻혔다). 큰 유닛(3×3)은 한 칸 인물에게 못 입혀 뺀다.
    /// 복장만 다른 레코드(살라딘 7벌 따위)는 목소리가 0 이라, 이름이 같은 다른 레코드의 목소리를 쓴다.
    /// </summary>
    internal List<Look> AllLooks(GameDatabase db)
    {
        if (_looks != null) return _looks;
        // 게임 자료(assets/data)에는 전투·필드가 부르는 레코드만 꺼내 두었다(원본 576개 중 383개) — 흑태자(Chr 598)처럼 빠진 것은 원본 게임 폴더에서 읽는다.
        // 자료 폴더에 넣지 않는 것은, 넣으면 지금까지 없는 인물로 빠지던 사건 인물이 판·필드에 새로 서기 때문이다.
        var original = OriginalGame.Files;
        var records = Enumerable.Range(1, LookScanMax)
            .Select(i => db.Character(i) ?? CharacterData.Parse(i, original?.Read("Chr", $"{i:D4}.chr"))).OfType<CharacterData>()
            .Where(c => c.SpriteId is > 0 and < ushort.MaxValue && !c.Big && db.T(c.NameId) is { Length: > 0 } name && !name.StartsWith('=') && name != "없음")
            .Where(c => BattleReady(c.SpriteId)).ToList();
        var voiceOf = records.Where(c => c.VoiceSet > 0).GroupBy(c => c.NameId).ToDictionary(g => g.Key, g => g.First().VoiceSet);
        // 여러 인물이 함께 쓰는 자리 채움 그림(55 · 202 따위 — 원본이 안 쓰는 레코드에 박아 둔 것)은 이름마다 줄을 내지 않고 한 줄로 — 「베라모드 #202」가 베라모드가 아니다.
        var shared = records.GroupBy(c => c.SpriteId).Where(g => g.Select(c => c.NameId).Distinct().Count() >= 3).Select(g => g.Key).ToHashSet();
        return _looks = [.. records.GroupBy(c => (c.SpriteId, NameId: shared.Contains(c.SpriteId) ? (ushort)0 : c.NameId))
            .Select(g => g.OrderByDescending(c => db.Character(c.Code) != null).ThenByDescending(c => c.VoiceSet > 0).First())
            .Select(c => new Look(c.Code, c.NameId, c.TitleId, c.SpriteId, c.FaceId, c.VoiceSet > 0 ? c.VoiceSet : voiceOf.GetValueOrDefault(c.NameId))), .. Part1Looks()];
    }

    /// <summary>외형 갈래의 줄 — 이름이 같은 인물(제 다른 복장)을 맨 앞에, 그다음 파트1 인물, 그다음 주역, 그다음 게임 자료에 없고 원본에만 있는 레코드(흑태자 · 블랙 레이븐 · 팬텀마리아 …), 그다음 나머지. 인물을 고를 때 한 번 정한다.</summary>
    internal List<Look> LookRows(GameDatabase db, CharacterData c)
    {
        if (_lookRows is { } rows && rows.Chr == c.Code) return rows.Rows;
        // 파트1 인물은 파트1 인물 레코드의 모습만(Part1CharEdit.LookRows — 파트2 그림은 파트1 동작이 없다).
        if (Part1Mode) { var p1 = P1.LookRows(c.Code); (_lookRows, _lookTop) = ((c.Code, p1), 0); return p1; }
        var own = db.Character(c.Code);
        // 어느 레코드에도 없고 스크립트 701 만 입히는 복장(살라딘의 건슬라이서 1185)은 그 인물 폴더의 그림 파일에서 찾는다.
        var listed = AllLooks(db).Where(l => l.NameId == c.NameId).Select(l => (int)l.Sprite).ToHashSet();
        var extra = OwnFolderSprites(c.Code).Where(o => o != c.FaceId && o != own?.FaceId && BattleReady(o) && listed.Add(o))
            .Select(o => new Look(c.Code, c.NameId, c.TitleId, (ushort)o, own?.FaceId ?? c.FaceId, own?.VoiceSet ?? c.VoiceSet));
        List<Look> list = [.. CustomLooks(), .. AllLooks(db).Concat(extra).OrderBy(l => l.CustomName != null ? 0 : l.Part1Name != null ? 1 : l.NameId == c.NameId || l.NameId == own?.NameId ? 0 : db.LevelExempt.Contains(l.Chr) ? 2 : db.Character(l.Chr) == null ? 3 : 4).ThenBy(l => l.Chr)];
        (_lookRows, _lookTop) = ((c.Code, list), 0);
        return list;
    }

    /// <summary>전투에 설 수 있는 그림의 모션 번호 끝 — 전투 그림은 모션이 78~92개로 번호가 86 이상까지 간다(맞기 · 쓰러지기 · 기술 모션).</summary>
    internal const int BattleMotionMax = 86;

    private readonly Dictionary<int, bool> _battleReady = [];

    /// <summary>
    /// 그 그림이 전투에 설 수 있나 — 이벤트 · 필드 전용 그림(살라딘 752 · 1351 따위)은 서기 · 걷기 몇 개(모션 3~60개)뿐이라 입히면 전투에서 동작이 없다(사용자 요청 menu-68).
    /// 모션표만 읽으면 되니 원본 묶음에서 바로 읽고(257개에 0.4초), 없으면 꺼내 둔 파일을 본다. 못 읽으면 뺀다.
    /// </summary>
    internal bool BattleReady(int obs)
    {
        if (_battleReady.TryGetValue(obs, out bool ready)) return ready;
        byte[]? bytes = OriginalGame.Files?.Read("Obs", $"{obs:D4}.obs");
        if (bytes == null && AssetPack.Fetch("moses/obs", $"{obs:D4}.obs", 0) is { } path)
            try { bytes = File.ReadAllBytes(path); } catch (IOException) { }
        // 파트1 그림은 동작을 파트2 번호로 옮겨 쓴 것이라(Part1Game.ActionMap) 모션 번호가 47 에서 끝난다 — 기본 공격(동작 8 = 모션 24)이 있으면 선다.
        ready = bytes != null && ObsMotionTable.Parse(bytes) is { } table
                && (obs > Part1Game.ObsBase ? table.Clips.ContainsKey(24) : table.Clips.Keys.DefaultIfEmpty(-1).Max() >= BattleMotionMax);
        return _battleReady[obs] = ready;
    }

    /// <summary>그 인물 폴더(<c>assets/characters/NNNN_이름</c>)에 든 그림 번호들 — 초상도 섞여 있어 부르는 쪽이 뺀다.</summary>
    internal static IEnumerable<int> OwnFolderSprites(int chr)
    {
        try
        {
            string root = FindRepoAssetsRoot();
            if (root.Length == 0) return [];
            return Directory.EnumerateDirectories(root, $"{chr:D4}_*").SelectMany(d => Directory.EnumerateFiles(d, "*.obs"))
                .Select(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out int n) ? n : 0).Where(n => n > 0).Distinct().Order().ToList();
        }
        catch (IOException) { return []; }
    }

    /// <summary>그 모습의 그림·초상(<paramref name="voice"/> 면 목소리)을 입힌다 — 판 위에 서 있으면 그림을 바로 다시 읽는다.</summary>
    internal void ChangeLook(int chr, Look look, bool voice)
    {
        if (DataOf(chr) is not { } c) return;
        _firstLook.TryAdd(chr, (c.SpriteId, c.FaceId, c.VoiceSet));
        Apply(chr, voice ? c with { VoiceSet = look.Voice } : c with { SpriteId = look.Sprite, FaceId = look.Face > 0 ? look.Face : c.FaceId });   // 사용자 그림은 초상이 없어 제 것 그대로
        if (voice) PlayVoiceSample(look.Voice);
        else host.LoadRosterSprites();
    }

    /// <summary>그 모습의 초상만 입힌다(사용자 요청 menu-101: 「초상화도 바꿀 수 있게」) — 그림 · 목소리는 그대로.</summary>
    internal void ChangeFace(int chr, Look look)
    {
        if (DataOf(chr) is not { } c) return;
        _firstLook.TryAdd(chr, (c.SpriteId, c.FaceId, c.VoiceSet));
        Apply(chr, c with { FaceId = look.Face });
    }

    /// <summary>고를 수 있는 목소리 묶음 번호 — Dmg.dat 에서 한 마디라도 있는 것, 번호 차례.</summary>
    internal List<int> VoiceSets() =>
        [.. host._voices.Where(kv => kv.Key > 0 && kv.Value.Hurt.Length + kv.Value.Call.Length > 0).Select(kv => kv.Key).Order()];

    /// <summary>
    /// 목소리 묶음만 번호로 앞뒤로 넘긴다 — 모습과 목소리를 따로 고르게(사용자 요청 ed-3: 「네리사 목소리 3 → 14(데미안)」처럼 번호로 말하는 요청).
    /// 지금 번호가 목록에 없으면(0 따위) 그 번호 바로 다음(◀ 면 바로 앞) 것으로.
    /// </summary>
    internal void StepVoice(int chr, int delta)
    {
        if (DataOf(chr) is not { } c || VoiceSets() is not { Count: > 0 } sets) return;
        int at = sets.IndexOf(c.VoiceSet);
        int next = at >= 0 ? sets[((at + delta) % sets.Count + sets.Count) % sets.Count]
                 : delta > 0 ? sets.FirstOrDefault(v => v > c.VoiceSet, sets[0]) : sets.LastOrDefault(v => v < c.VoiceSet, sets[^1]);
        _firstLook.TryAdd(chr, (c.SpriteId, c.FaceId, c.VoiceSet));
        Apply(chr, c with { VoiceSet = (ushort)next });
        PlayVoiceSample(next);
    }

    /// <summary>그 목소리 묶음을 쓰는 인물 이름(번호가 가장 작은 레코드) — 없으면 빈 글.</summary>
    internal string VoiceOwner(GameDatabase db, int set) =>
        AllLooks(db).Where(l => l.Voice == set).OrderBy(l => l.Chr).Select(l => db.T(l.NameId)).FirstOrDefault() ?? "";

    /// <summary>들어 보기 목소리의 효과음 표 — 다음 것을 틀 때 앞 것을 끊는다.</summary>
    internal const int VoiceSampleTag = 990;
    private int _voiceTurn;
    private (int Sound, double Until)? _pendingVoice;

    /// <summary>
    /// 그 목소리 묶음의 한 마디를 들려준다 — 누를 때마다 차례 부르기 넷과 맞을 때 둘을 돌아가며(사용자 요청 menu-69).
    /// 목소리 파일은 처음 쓸 때 가져오는 것이라 아직 없으면 조용하다 — 그때는 몇 초 안에 오면 그때 튼다(<see cref="TickVoice"/>).
    /// </summary>
    internal void PlayVoiceSample(int set)
    {
        var v = host._voices.GetValueOrDefault(set);
        int[] lines = [.. v.Call ?? [], .. v.Hurt ?? []];
        if (lines.Length == 0) return;
        int sound = lines[_voiceTurn++ % lines.Length];
        host._mixer.StopEffect(VoiceSampleTag);
        bool loaded;
        lock (host._sfx) loaded = host._sfx.ContainsKey(sound);
        host.Play(sound, VoiceSampleTag);
        _pendingVoice = loaded ? null : (sound, host._lastTime + 5);
    }

    /// <summary>가져오는 중이던 들어 보기 목소리가 왔으면 튼다.</summary>
    internal void TickVoice()
    {
        if (_pendingVoice is not { } p) return;
        if (host._lastTime > p.Until) { _pendingVoice = null; return; }
        bool loaded;
        lock (host._sfx) loaded = host._sfx.ContainsKey(p.Sound);
        if (!loaded) return;
        _pendingVoice = null;
        host.Play(p.Sound, VoiceSampleTag);
    }

    /// <summary>에디터로 바꾸기 전 모습으로 — 이번에 안 바꿨으면 원본 레코드의 것.</summary>
    internal void ResetLook(int chr)
    {
        if (DataOf(chr) is not { } c) return;
        (ushort Sprite, ushort Face, ushort Voice) back = _firstLook.TryGetValue(chr, out var first) ? first
                 : host._db?.Character(chr) is { } b ? (b.SpriteId, b.FaceId, b.VoiceSet) : default;
        if (back.Sprite == 0) return;
        Apply(chr, c with { SpriteId = back.Sprite, FaceId = back.Face, VoiceSet = back.Voice });
        host.LoadRosterSprites();
    }

    // ── 배치 ── 왼쪽 인물 목록을 없애고(파티가 몇 명 안 된다) 탭 왼쪽의 펼침 상자로 고른다 — 오른쪽 칸을 창 너비 그대로 쓴다(사용자 요청 menu-72).
    internal const int ContentDx = 16, ContentW = W - 2 * ContentDx;
    internal const int PickW = 210, PickRowH = 22;
    internal const int TabW = 72, AbRowH = 22, AbRows = 9;
    internal const int SearchDy = 106, RowsDy = 130;
    internal const int LookNameDx = 42, LookShapeDx = 192, LookFaceDx = 236, LookVoiceDx = 280, LookVoiceW = 74, LookListW = 356, LookPreviewDx = 368, LookPreviewW = ContentW - LookPreviewDx;
    internal const int ResetDy = H - PartyH - 70;
    // 외형 갈래 아래 줄 — 목소리 묶음 번호 ◀ ▶ 와 듣기(사용자 요청 ed-3).
    internal const int VoicePrevDx = 70, VoiceNextDx = 252, VoicePlayDx = 288;
    internal static readonly string[] Tabs = ["장비", "어빌리티", "외형"];

    internal static int TabX(int px, int t) => TabX(px, t, Tabs.Length);

    /// <summary>갈래 단추 자리 — 오른쪽 끝에 붙여 <paramref name="count"/> 개를 늘어놓는다(파트1 은 넷 — <see cref="Part1CharEdit.Tabs"/>).</summary>
    internal static int TabX(int px, int t, int count) => px + ContentW - (count - t) * (TabW + 4) + 4;

    internal bool In(int bx, int by, int x, int y, int w, int h) => bx >= x && bx < x + w && by >= y && by < y + h;

    // ── 이름 찾기 ── 어빌리티 · 외형 갈래에서 글자를 치면 바로 찾기 칸에 든다(사용자 요청 menu-71). 한글은 조합 중인 글자도 함께 찾는다.
    internal string _search = "";
    internal string _imeComp = "";
    internal bool _pickerOpen;      // 인물 펼침 상자가 펼쳐졌나

    /// <summary>찾기 칸이 글자를 받나 — 어빌리티 · 외형 갈래(파트1 은 가방 · 외형도)가 열려 있을 때.</summary>
    internal bool SearchActive => _open && !_pickSave && (_tab is 1 or 2 or 3 || _itemPick is { Open: true }) && !NoGame(People());

    internal string Query => (_search + _imeComp).Trim();

    /// <summary>다 된 글자 하나(WM_CHAR) — Backspace 는 한 글자 지운다. 다른 제어 글자(Esc · Enter)는 키 쪽이 다룬다.</summary>
    internal void OnChar(char ch)
    {
        if (ch == '\b') { if (_search.Length > 0) _search = _search[..^1]; }
        else if (ch >= ' ' && _search.Length < 20) _search += ch;
        else return;
        (_abTop, _lookTop) = (0, 0);
        _itemPick?.ResetTop();
    }

    /// <summary>한글 조합 중인 글자(WM_IME_COMPOSITION) — 다 되면 WM_CHAR 로 따로 온다.</summary>
    internal void OnComposition(string text)
    {
        if (_imeComp == text) return;
        _imeComp = text;
        (_abTop, _lookTop) = (0, 0);
        _itemPick?.ResetTop();
    }

    /// <summary>찾는 말에 맞나 — 이름 · 칭호에 들었거나, 숫자면(앞의 # 은 떼고) 번호가 같다.</summary>
    internal static bool Matches(string query, IEnumerable<string> names, IEnumerable<int> numbers)
    {
        if (query.Length == 0) return true;
        if (int.TryParse(query.TrimStart('#'), out int n)) return numbers.Contains(n);
        return names.Any(s => s.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    internal List<int> AbilityView(GameDatabase db, CharacterData c) =>
        Query is { Length: > 0 } q ? [.. AbilityRows(db, c).Where(id => Matches(q, [db.T(db.Abilities[id].NameId)], [id]))] : AbilityRows(db, c);

    internal List<Look> LookView(GameDatabase db, CharacterData c) =>
        Query is { Length: > 0 } q ? [.. LookRows(db, c).Where(l => Matches(q, [LookName(db, l), db.T(l.TitleId)], [l.Sprite, LookNo(l), l.Chr]))] : LookRows(db, c);

    /// <summary>줄 앞에 보이는 그림 번호 — 파트1 모습은 파트1 번호(<see cref="Part1Game.ObsBase"/> 를 뺀 것).</summary>
    internal static int LookNo(Look l) => l.Part1Name != null && l.Sprite > Part1Game.ObsBase ? l.Sprite - Part1Game.ObsBase : l.Sprite;

    /// <summary>열려 있으면 클릭을 먹는다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        var (x, y) = Origin();
        var people = People();
        if (In(bx, by, x + W - 116, y + H - 40, 100, 28)) { _open = false; return true; }
        if (!NoGame(people) && In(bx, by, x + W - 246, y + H - 40, 120, 28)) { _pickSave = !_pickSave; _saves = null; _pickerOpen = false; return true; }
        if (_pickSave || NoGame(people))
        {
            var saves = Saves();
            int at = _saveTop + (by - y - 62) / SaveRowH;
            if (In(bx, by, x + 12, y + 62, W - 24, SaveRows * SaveRowH) && at < saves.Count) LoadSave(saves[at].Slot);
            return true;
        }
        // 아이템 고르기 창이 맨 위에 떠 있다 — 먼저 본다.
        if (_itemPick is { Open: true } pick) return pick.OnClick(bx, by, x, y);
        int px = x + ContentDx;
        // 펼친 인물 목록이 맨 위에 덮여 있다 — 먼저 본다. 바깥을 누르면 접기만 한다.
        if (_pickerOpen)
        {
            _pickerOpen = false;
            int row = (by - (y + PartyH + 42 + 24)) / PickRowH;
            if (In(bx, by, px, y + PartyH + 42 + 24, PickW, people.Count * PickRowH) && row < people.Count) _chosen = row;
            return true;
        }
        if (Part1Mode) return P1.OnClick(bx, by, x, y, people);
        // 파티 줄 — 소지금 · 나야트레이
        foreach (var (dx, delta, _) in MoneySteps)
            if (In(bx, by, x + dx, y + 36, 54, 22)) { ChangeMoney(delta); return true; }
        if (In(bx, by, x + W - 16 - 150, y + 36, 150, 22)) { AddNayatrei(); return true; }
        y += PartyH;
        if (In(bx, by, px, y + 42, PickW, 22)) { _pickerOpen = true; return true; }
        if (_chosen >= people.Count) return true;
        int chr = people[_chosen];
        for (int t = 0; t < Tabs.Length; t++)
            if (In(bx, by, TabX(px, t), y + 42, TabW, 22))
            {
                if (t != _tab) (_search, _imeComp) = ("", "");   // 찾는 말은 갈래마다 따로 — 남겨 두면 다른 갈래가 빈 목록으로 열린다
                _tab = t;
                ForgetLooks();
                return true;
            }
        // 레벨 단추 — −10 · −1 · +1 · +10
        int ly = y + 76;
        (int Dx, int Delta)[] steps = [(170, -10), (214, -1), (258, 1), (302, 10)];
        foreach (var (dx, delta) in steps)
            if (bx >= px + dx && bx < px + dx + 40 && by >= ly && by < ly + 22) { ChangeLevel(chr, delta); return true; }
        if (_tab == 2)
        {
            if (In(bx, by, px + LookPreviewDx, y + ResetDy, LookPreviewW, 20)) { ResetLook(chr); return true; }
            if (In(bx, by, px + VoicePrevDx, y + ResetDy, 30, 20)) { StepVoice(chr, -1); return true; }
            if (In(bx, by, px + VoiceNextDx, y + ResetDy, 30, 20)) { StepVoice(chr, 1); return true; }
            if (In(bx, by, px + VoicePlayDx, y + ResetDy, 60, 20) && DataOf(chr) is { } vc) { PlayVoiceSample(vc.VoiceSet); return true; }
            if (host._db is { } ldb && DataOf(chr) is { } lc)
            {
                var looks = LookView(ldb, lc);
                for (int i = 0; i < AbRows && _lookTop + i < looks.Count; i++)
                {
                    int ry = y + RowsDy + i * AbRowH;
                    if (In(bx, by, px + LookShapeDx, ry, 40, 20)) { ChangeLook(chr, looks[_lookTop + i], voice: false); return true; }
                    if (In(bx, by, px + LookFaceDx, ry, 40, 20) && looks[_lookTop + i].Face > 0) { ChangeFace(chr, looks[_lookTop + i]); return true; }
                    if (In(bx, by, px + LookVoiceDx, ry, LookVoiceW, 20) && looks[_lookTop + i].Voice > 0) { ChangeLook(chr, looks[_lookTop + i], voice: true); return true; }
                }
            }
            return true;
        }
        if (_tab == 1)
        {
            // 어빌리티 줄 — − ＋
            if (host._db is { } db && DataOf(chr) is { } ac)
            {
                var ids = AbilityView(db, ac);
                for (int i = 0; i < AbRows && _abTop + i < ids.Count; i++)
                {
                    int ry = y + RowsDy + i * AbRowH;
                    if (In(bx, by, px + ContentW - 64, ry, 30, 20)) { ChangeAbility(chr, ids[_abTop + i], -1); return true; }
                    if (In(bx, by, px + ContentW - 30, ry, 30, 20)) { ChangeAbility(chr, ids[_abTop + i], 1); return true; }
                }
            }
            return true;
        }
        // 장비 칸 — 이름을 누르면 고르기 창, ◀◀ ◀ ▶ ▶▶ 는 10 · 1 씩
        if (DataOf(chr) is { } c)
            for (int slot = 0; slot < c.Items.Length; slot++)
            {
                int iy = y + 140 + slot * 30;
                if (In(bx, by, px, iy, ContentW - 140, 22)) { ItemPick.Show(chr, slot, $"{slot + 1}번 칸"); return true; }
                (int Dx, int Delta)[] turns = [(-136, -10), (-102, -1), (-68, 1), (-34, 10)];
                foreach (var (dx, delta) in turns)
                    if (bx >= px + ContentW + dx && bx < px + ContentW + dx + 30 && by >= iy && by < iy + 22) { ChangeItem(chr, slot, delta); return true; }
            }
        return true;
    }

    internal const int SaveRows = (H - 62 - 50) / SaveRowH;

    /// <summary>오른쪽 단추를 누르고 있는 동안 뜨는 어빌리티 설명 — 떼면 지운다(WM_RBUTTONUP).</summary>
    internal (string Text, int X, int Y)? _tip;

    /// <summary>
    /// 오른쪽 단추 — 어빌리티 갈래의 줄 위면 Status 화면과 같은 설명(설명글 · 레벨별 효과 · 필살기 방식)을 띄운다(사용자 요청 menu-73).
    /// 효과는 가진 레벨, 없는 것은 Lv1 로 보인다.
    /// </summary>
    internal void OnRightDown(int bx, int by)
    {
        if (Part1Mode)
        {
            var p1People = People();
            if (!_pickSave && !_pickerOpen && !NoGame(p1People)) { var (ox, oy) = Origin(); _tip = P1.TipAt(bx, by, ox, oy, p1People); }
            return;
        }
        if (_tab != 1 || _pickSave || _pickerOpen || host._db is not { } db) return;
        var people = People();
        if (NoGame(people) || _chosen >= people.Count || DataOf(people[_chosen]) is not { } c) return;
        var (x, y) = Origin();
        int px = x + ContentDx, top = y + PartyH + RowsDy;
        var ids = AbilityView(db, c);
        int row = (by - top) / AbRowH;
        if (by < top || !In(bx, by, px, top, ContentW - 70, AbRows * AbRowH) || _abTop + row >= ids.Count) return;
        var ab = db.Abilities[ids[_abTop + row]];
        string desc = db.AbilityDescription(ab) is { Length: > 0 } d ? d : "(설명 없음)";
        int level = Math.Max(1, c.AbilityLevel(ab.Id));
        _tip = (desc + host.StatusScr.AbilityEffectText(ab, level) + host.StatusScr.UltimateStyleText(ab, c), bx, by);
    }

    internal void OnWheel(int delta)
    {
        var people = People();
        if (_itemPick is { Open: true } pick) pick.OnWheel(delta);
        else if (_pickSave || NoGame(people)) _saveTop = Math.Clamp(_saveTop - delta, 0, Math.Max(0, Saves().Count - SaveRows));
        // 어빌리티 · 외형 목록은 줄이 많아 한 번에 세 줄 — 넘친 값은 그릴 때 자른다.
        else if (_tab == 1) _abTop -= delta * 3;
        else if (_tab is 2 or 3) _lookTop -= delta * 3;   // 파트1 은 2 가방 · 3 외형 — 둘 다 이 자리를 쓴다
    }

    internal void OnKey(int key)
    {
        if (key != Win32.VK_ESCAPE) return;
        // Esc 는 펼친 목록 → 찾는 말 → 창 차례로 닫는다.
        if (_itemPick is { Open: true } pick) pick.OnEscape();
        else if (_pickerOpen) _pickerOpen = false;
        else if (_search.Length > 0 || _imeComp.Length > 0) (_search, _imeComp, _abTop, _lookTop) = ("", "", 0, 0);
        else _open = false;
    }

    internal void Button(int bx, int by, int w, string text, bool on = false, int h = 22)
    {
        bool here = In(host._mouse.X, host._mouse.Y, bx, by, w, h);
        host.UiFill(bx, by, w, h, here || on ? 0xFF2A4A8A : StatusScreen.BoxBg);
        host.UiStroke(bx, by, w, h, StatusScreen.BoxLine);
        var (_, tw, _) = host.UiGetText(text, White, 12);
        host.UiText(text, bx + (w - tw) / 2, by + (h - 16) / 2, White, 12);
    }

    /// <summary>찾기 칸 — 친 글자와 조합 중인 글자(밑줄), 깜박이는 자리 표시. 오른쪽 끝에 <paramref name="right"/> 글(몇 줄 중 몇째 따위).</summary>
    internal void DrawSearch(int px, int y, string right, int width = ContentW)
    {
        int sy = y + SearchDy, boxX = px + 34, boxW = 200;
        host.UiText("찾기", px, sy + 3, DimGray, 12);
        host.UiFill(boxX, sy, boxW, 20, StatusScreen.BoxBg);
        host.UiStroke(boxX, sy, boxW, 20, StatusScreen.BoxLine);
        int tx = boxX + 6;
        if (_search.Length > 0) { host.UiText(_search, tx, sy + 2, White, 12); tx += host.UiGetText(_search, White, 12).W; }
        if (_imeComp.Length > 0)
        {
            int cw = host.UiGetText(_imeComp, White, 12).W;
            host.UiText(_imeComp, tx, sy + 2, 0xFF00FFFF, 12);
            host.UiFill(tx, sy + 17, cw, 1, 0xFF00FFFF);
            tx += cw;
        }
        if ((int)(host._lastTime * 2) % 2 == 0) host.UiFill(tx + 1, sy + 3, 1, 14, White);
        if (_search.Length == 0 && _imeComp.Length == 0) host.UiText("이름이나 번호를 치세요", boxX + 10, sy + 2, 0xFF5A6A8A, 12);
        else host.UiText("Esc 비우기", boxX + boxW + 8, sy + 3, DimGray, 12);
        host.UiText(right, px + width - host.UiGetText(right, DimGray, 12).W, sy + 3, DimGray, 12);
    }

    /// <summary>어빌리티 갈래 — 가진 것은 밝게 레벨과 함께, 없는 것은 흐리게. 줄마다 − ＋.</summary>
    internal void DrawAbilities(GameDatabase db, CharacterData c, int px, int y)
    {
        var ids = AbilityView(db, c);
        _abTop = Math.Clamp(_abTop, 0, Math.Max(0, ids.Count - AbRows));
        DrawSearch(px, y, ids.Count == 0 ? "없음" : $"{_abTop + 1}–{Math.Min(ids.Count, _abTop + AbRows)} / {ids.Count}");
        for (int i = 0; i < AbRows && _abTop + i < ids.Count; i++)
        {
            var ab = db.Abilities[ids[_abTop + i]];
            int ry = y + RowsDy + i * AbRowH;
            bool has = c.HasAbility(ab.Id);
            string no = $"#{ab.Id}";
            host.UiText(no, px + LookNameDx - 6 - host.UiGetText(no, DimGray, 12).W, ry + 2, DimGray, 12);
            host.UiText($"{db.T(ab.NameId)}{(ab.IsPassive ? "  패시브" : "")}", px + LookNameDx, ry + 2, has ? White : DimGray, 12);
            if (has) host.UiText($"Lv{c.AbilityLevel(ab.Id)}/{Math.Max(1, (int)ab.MaxLevel)}", px + ContentW - 140, ry + 2, 0xFF00FFFF, 12);
            Button(px + ContentW - 64, ry, 30, "−", h: 20); Button(px + ContentW - 30, ry, 30, "＋", h: 20);
        }
    }

    /// <summary>
    /// 외형 갈래 — 줄마다 그림 번호 · 이름 · 칭호와 「모습」·「목소리」 단추. 오른쪽 칸에 마우스가 얹힌 줄의 모습(없으면 지금 모습)을 앞모습 · 옆모습 나란히 보인다.
    /// </summary>
    internal void DrawLooks(GameDatabase db, CharacterData c, int px, int y)
    {
        TickVoice();
        var rows = LookView(db, c);
        _lookTop = Math.Clamp(_lookTop, 0, Math.Max(0, rows.Count - AbRows));
        int now = c.SpriteId > Part1Game.ObsBase ? c.SpriteId - Part1Game.ObsBase : c.SpriteId;   // 파트1 그림은 파트1 번호로
        DrawSearch(px, y, $"지금 그림 #{now} · 목소리 {c.VoiceSet}");
        Look? hover = null;
        for (int i = 0; i < AbRows && _lookTop + i < rows.Count; i++)
        {
            var look = rows[_lookTop + i];
            int ry = y + RowsDy + i * AbRowH;
            if (In(host._mouse.X, host._mouse.Y, px, ry, LookListW, AbRowH)) { hover = look; host.UiFill(px - 4, ry, LookShapeDx, AbRowH - 2, 0xFF1C2E52); }
            string title = look.Part1Name == null && db.T(look.TitleId) is { Length: > 0 } t && t != "없음" ? $" · {t}" : "";
            // 앞 칸은 그림(Obs) 번호 — 인물 번호(Chr)가 아니다. 오른쪽에 맞춰 세워 이름 칸이 한 줄로 선다(사용자 요청 menu-70).
            string label = $"{LookName(db, look)}{title}";
            while (label.Length > 2 && host.UiGetText(label, White, 12).W > LookShapeDx - LookNameDx - 6) label = label[..^1];
            bool wearing = look.Sprite == c.SpriteId;
            string no = $"#{LookNo(look)}";
            host.UiText(no, px + LookNameDx - 6 - host.UiGetText(no, DimGray, 12).W, ry + 2, wearing ? 0xFF00FFFF : DimGray, 12);
            host.UiText(label, px + LookNameDx, ry + 2, wearing ? 0xFF00FFFF : White, 12);
            Button(px + LookShapeDx, ry, 40, "모습", on: wearing, h: 20);
            if (look.Face > 0) Button(px + LookFaceDx, ry, 40, "초상", on: look.Face == c.FaceId, h: 20);
            if (look.Voice > 0) Button(px + LookVoiceDx, ry, LookVoiceW, $"목소리 {look.Voice}", on: look.Voice == c.VoiceSet, h: 20);
        }
        if (rows.Count == 0) host.UiText("맞는 모습이 없습니다", px + LookNameDx, y + RowsDy + 2, DimGray, 12);
        // 미리 보기 — 왼쪽 앞모습(서기 모션 2), 오른쪽 옆모습(서기 모션 1). 창 층에 그려야 창 바탕에 안 가린다(DrawUi 는 판 밑그림에 그린다).
        int bx = px + LookPreviewDx, by = y + RowsDy, bw = LookPreviewW, bh = AbRows * AbRowH - 2, half = bw / 2;
        host.UiFill(bx, by, bw, bh, StatusScreen.BoxBg);
        host.UiStroke(bx, by, bw, bh, StatusScreen.BoxLine);
        host.UiFill(bx + half, by + 4, 1, bh - 26, StatusScreen.BoxLine);
        int sprite = hover?.Sprite ?? c.SpriteId, tick = (int)(host._lastTime * TicksPerSecond);
        host.UiSprite(sprite, ObsMotionTable.DirectionFront, tick, bx + half / 2, by + bh - 24, loop: true);
        host.UiSprite(sprite, ObsMotionTable.DirectionSide, tick, bx + half + half / 2, by + bh - 24, loop: true);
        host.UiText("앞", bx + 4, by + 2, DimGray, 12);
        host.UiText("옆", bx + half + 4, by + 2, DimGray, 12);
        // 코드에서 찾아 고칠 수 있게 번호를 다 보인다 — Chr · 모습(Obs) · 목소리 묶음(사용자 요청 ed-3).
        string caption = hover is { Part1Name: not null } ? $"파트1 Chr {hover.Chr:D4} · 모습 {hover.Sprite - Part1Game.ObsBase}"
                       : hover != null ? $"Chr {hover.Chr:D4} · 모습 {hover.Sprite} · 초상 {hover.Face} · 목소리 {hover.Voice}" : $"모습 {now} · 초상 {c.FaceId} · 목소리 {c.VoiceSet}";
        host.UiText(caption, bx + (bw - host.UiGetText(caption, DimGray, 12).W) / 2, by + bh - 18, DimGray, 12);
        Button(bx, y + ResetDy, bw, "원래대로", h: 20);
        // 목소리만 번호로 고르기 — 모습과 따로.
        int vy = y + ResetDy;
        host.UiText("목소리", px, vy + 2, DimGray, 12);
        Button(px + VoicePrevDx, vy, 30, "◀", h: 20);
        string owner = c.VoiceSet == 0 ? "없음" : VoiceOwner(db, c.VoiceSet), voice = owner.Length > 0 ? $"#{c.VoiceSet}  {owner}" : $"#{c.VoiceSet}";
        while (voice.Length > 2 && host.UiGetText(voice, White, 12).W > VoiceNextDx - VoicePrevDx - 40) voice = voice[..^1];
        host.UiText(voice, px + VoicePrevDx + 30 + (VoiceNextDx - VoicePrevDx - 30 - host.UiGetText(voice, White, 12).W) / 2, vy + 2, 0xFF00FFFF, 12);
        Button(px + VoiceNextDx, vy, 30, "▶", h: 20);
        Button(px + VoicePlayDx, vy, 60, "듣기", h: 20);
    }

    /// <summary>세이브 고르기 쪽 — 파일이 있는 칸만 줄로 올린다. 누르면 불러온다.</summary>
    internal void DrawSaves(int x, int y, bool hasPeople)
    {
        var saves = Saves();
        host.UiText(saves.Count == 0 ? "세이브 파일이 없습니다 — 새 게임을 시작해 전투나 챕터에 들어간 뒤에 여세요."
                      : hasPeople ? "불러올 세이브 파일을 고르세요 — 지금 게임의 저장하지 않은 것은 없어집니다."
                      : "먼저 고칠 세이브 파일을 고르세요 — 불러온 뒤 인물과 소지금을 고칩니다.", x + 16, y + 38, DimGray, 12);
        _saveTop = Math.Clamp(_saveTop, 0, Math.Max(0, saves.Count - SaveRows));
        for (int i = 0; i < SaveRows && _saveTop + i < saves.Count; i++)
        {
            var (slot, head) = saves[_saveTop + i];
            int ry = y + 62 + i * SaveRowH;
            if (In(host._mouse.X, host._mouse.Y, x + 12, ry, W - 24, SaveRowH)) host.UiFill(x + 12, ry, W - 24, SaveRowH - 2, 0xFF2A4A8A);
            string label = slot == SlotsScreen.AutoSlot ? "[자동 저장]" : slot > SlotsScreen.AutoSlot ? $"[자동 {slot - SlotsScreen.AutoSlot}차례 전]"
                           : $"[{slot:D2}:{head.SceneKind switch { 4 => "챕  터", 7 => "연대표", _ => "전  투" }}]";
            host.UiText(label, x + 18, ry + 4, SlotsScreen.SlotLabelColor, 12);
            host.UiText(host._db?.T((ushort)head.SceneText) ?? "", x + 130, ry + 4, White, 12);
            host.UiText($"{head.Money}GP", x + 330, ry + 4, DimGray, 12);
            var (_, sw, _) = host.UiGetText(head.SavedAt, White, 12);
            host.UiText(head.SavedAt, x + W - 18 - sw, ry + 4, SlotsScreen.SlotTimeColor, 12);
        }
    }

    internal string PersonLabel(GameDatabase db, int chr) => Part1Mode ? P1.Label(chr) : DataOf(chr) is { } d ? $"{db.T(d.NameId)}  Lv{d.Level}  (Chr {chr})" : $"Chr {chr}";

    internal void Draw()
    {
        if (!_open || host._db is not { } db) return;
        var (x, y) = Origin();
        host.UiFill(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.UiFill(x, y, W, H, StatusScreen.PanelBg);
        host.UiStroke(x, y, W, H, StatusScreen.BoxLine);
        host.UiFill(x, y, W, 28, StatusScreen.HeadBg);
        host.UiText(Part1Mode ? "캐릭터 에디터 (파트1) — 레벨·장비·어빌리티·가방·외형·eld 바꾸기(시험용)" : "캐릭터 에디터 — 레벨·장비·어빌리티·외형·소지금 바꾸기(시험용)", x + 10, y + 6, White);

        var people = People();
        bool picking = _pickSave || NoGame(people);
        int fy = y + H - 40;
        host.UiFill(x + W - 116, fy, 100, 28, StatusScreen.HeadBg);
        host.UiText("닫기", x + W - 80, fy + 6, White);
        if (!NoGame(people))
        {
            string text = picking ? "돌아가기" : "세이브 고르기";
            host.UiFill(x + W - 246, fy, 120, 28, StatusScreen.HeadBg);
            host.UiText(text, x + W - 246 + (120 - host.UiGetText(text, White).W) / 2, fy + 6, White);
        }
        host.UiText("Esc: 닫기 · 휠: 목록", x + 16, fy + 7, DimGray);
        if (picking) { DrawSaves(x, y, !NoGame(people)); return; }

        _chosen = Math.Clamp(_chosen, 0, Math.Max(0, people.Count - 1));
        if (Part1Mode)
        {
            P1.Draw(x, y, people);
            if (_tip is { } p1Tip) host.StatusScr.DrawDescriptionTip(p1Tip.Text, p1Tip.X, p1Tip.Y, x, y, W, H);
            DrawPicker(db, x + ContentDx, y + PartyH, people);
            if (_itemPick is { Open: true } p1Pick) p1Pick.Draw(x, y, P1.CurrentItem(people[_chosen]));
            return;
        }
        // 파티 줄 — 소지금 · 나야트레이
        host.UiText($"소지금 {host.Mos._shopMoney}GP", x + 16, y + 39, White);
        foreach (var (dx, _, text) in MoneySteps) Button(x + dx, y + 36, 54, text);
        Button(x + W - 16 - 150, y + 36, 150, host.Mos._members.Contains(NayatreiChr) ? "나야트레이 (파티에 있음)" : "나야트레이 등장");
        host.UiFill(x + 12, y + 36 + 28, W - 24, 1, StatusScreen.BoxLine);
        y += PartyH;

        _chosen = Math.Clamp(_chosen, 0, Math.Max(0, people.Count - 1));
        int px = x + ContentDx;
        if (DataOf(people[_chosen]) is { } c)
        {
            host.UiText($"레벨 {c.Level}  ·  누적 EXP {c.CumExp}", px, y + 78, White);
            Button(px + 170, y + 76, 40, "−10"); Button(px + 214, y + 76, 40, "−1"); Button(px + 258, y + 76, 40, "+1"); Button(px + 302, y + 76, 40, "+10");
            for (int t = 0; t < Tabs.Length; t++) Button(TabX(px, t), y + 42, TabW, Tabs[t], on: t == _tab);
            if (_tab == 1) DrawAbilities(db, c, px, y);
            else if (_tab == 2) DrawLooks(db, c, px, y);
            else host.UiText("장비 (이름을 누르면 고르기 창 — ◀◀ ▶▶ 는 10개씩 넘긴다)", px, y + 116, DimGray, 12);
            for (int slot = 0; _tab == 0 && slot < c.Items.Length; slot++)
            {
                int iy = y + 140 + slot * 30;
                int id = c.Items[slot];
                string name = id == 0 ? "(비움)" : db.Items.TryGetValue(id, out var item) ? db.T(item.NameId) : $"아이템 {id}";
                host.UiText($"{slot + 1}", px, iy + 3, DimGray, 12);
                host.UiText($"{name}{(id != 0 ? $"  #{id}" : "")}", px + 18, iy + 3, White, 12);
                int bx = px + ContentW;
                Button(bx - 136, iy, 30, "◀◀"); Button(bx - 102, iy, 30, "◀"); Button(bx - 68, iy, 30, "▶"); Button(bx - 34, iy, 30, "▶▶");
            }
            if (_tab != 2) host.UiText(_tab == 1 ? "+ 넣기·올리기 · − 내리기·지우기 — 바꾸면 바로 든다." : "바꾸면 바로 든다 — 레벨은 능력치까지 다시 센다.", px, y + H - PartyH - 66, DimGray, 12);
        }

        if (_tip is { } tip) host.StatusScr.DrawDescriptionTip(tip.Text, tip.X, tip.Y, x, y - PartyH, W, H);

        DrawPicker(db, px, y, people);
        if (_itemPick is { Open: true } pick) pick.Draw(x, y - PartyH, ItemPick.Slot is int s && DataOf(people[_chosen]) is { } pc && s < pc.Items.Length ? pc.Items[s] : -1);
    }

    /// <summary>인물 펼침 상자 — 마지막에 그려 펼친 목록이 아래 줄들을 덮는다. <paramref name="y"/> 는 파티 줄 아래.</summary>
    internal void DrawPicker(GameDatabase db, int px, int y, List<int> people)
    {
        string chosen = people.Count > 0 ? PersonLabel(db, people[_chosen]) : "";
        host.UiFill(px, y + 42, PickW, 22, _pickerOpen ? 0xFF2A4A8A : StatusScreen.BoxBg);
        host.UiStroke(px, y + 42, PickW, 22, StatusScreen.BoxLine);
        host.UiText(chosen, px + 8, y + 45, 0xFF00FFFF, 12);
        host.UiText("▼", px + PickW - 18, y + 45, White, 12);
        if (!_pickerOpen) return;
        int ly = y + 42 + 24;
        host.UiFill(px, ly, PickW, people.Count * PickRowH, StatusScreen.PanelBg);
        host.UiStroke(px, ly, PickW, people.Count * PickRowH, StatusScreen.BoxLine);
        for (int i = 0; i < people.Count; i++)
        {
            int ry = ly + i * PickRowH;
            bool on = i == _chosen, here = In(host._mouse.X, host._mouse.Y, px, ry, PickW, PickRowH);
            if (on || here) host.UiFill(px + 1, ry + 1, PickW - 2, PickRowH - 2, here ? 0xFF2A4A8A : 0xFF1C2E52);
            host.UiText(PersonLabel(db, people[i]), px + 8, ry + 3, on ? 0xFF00FFFF : White, 12);
        }
    }
}

using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파티 셋(0 살라딘 · 1 베라모드 · 2 크리스티앙, 원본 <c>0x101b6888[0..2]</c>) — 지금 파티는 <c>_members</c>·<c>_ownedLegions</c>·
/// <c>_shopMoney</c>·<c>_inventory</c> 가 들고, 나머지 파티는 여기 은행에 넣어 둔다. 스크립트 703·705·801·802 의 첫 인자가 파티 번호이고,
/// 804 는 인물을 파티 사이로 옮기며(닥터 엠블라가 크리스티앙·죠안을 2번 파티로), 803 은 파티 둘을 합친다.
/// <para>
/// <b>인물 자료는 전역 명부 하나</b>다 — 원본 <c>0x101b6884</c>(CChr 932B × N)이 파티와 무관하게 모든 인물을 들고, 파티 객체(4272B)는
/// <c>+8</c> Chr 번호·가방·GP·군단·우편함만 든다(분석-시스템메뉴 2.2). 리메이크는 <c>_party</c> 가 그 명부이고(이름은 옛 그대로),
/// 파티(지금 파티의 <c>_members</c>, 은행의 <see cref="PartyState.Members"/>)는 번호만 든다. 그래서 805(레벨 맞추기)·801(합류)이
/// 다른 파티에 있거나 파티에서 빠진 인물을 <b>그 인물 그대로</b> 쓴다 — 전에는 파티마다 사본을 들어서 Chp 62 의 805[220/221]이
/// 파티 2 의 크리스티앙·죠안에 안 닿고, Chp 58 의 801[243/244]가 장비·어빌리티·직업을 .chr 처음 값으로 되돌렸다(감사 F7·R4 G1).
/// </para>
/// </summary>
internal sealed unsafe partial class GameWindow
{
    internal sealed class PartyState
    {
        /// <summary>
        /// <b>옛 세이브 전용</b> — 파티마다 인물 사본을 싣던 판의 은행 <c>Units</c>. 불러올 때 <see cref="MergeLegacyBankCopies"/> 가
        /// 명부(<c>_party</c>)로 옮기고 비운다. 지금은 아무도 여기 쓰지 않는다.
        /// </summary>
        public Dictionary<int, CharacterData> Party { get; } = [];
        public HashSet<int> Members { get; } = [];
        /// <summary>군단 번호 — 개수만큼 되풀이(원본 +0x910 (군단, 개수), 감사3 L8).</summary>
        public List<int> Legions { get; } = [];
        public int Money { get; set; }
        public Dictionary<int, int> Inventory { get; } = [];
        public List<int> Mailbox { get; } = [];
        public HashSet<int> MailRead { get; } = [];
    }

    /// <summary>지금 파티가 아닌 파티들의 상태(파티 번호 → 상태). 세이브에 실린다.</summary>
    internal readonly Dictionary<int, PartyState> _partyBank = [];

    internal PartyState BankFor(int party) =>
        _partyBank.TryGetValue(party, out var s) ? s : _partyBank[party] = new PartyState();

    /// <summary>
    /// 파티에 인물을 넣는다(801, 원본 <c>0x1004ddd0</c>) — 파티에는 번호만 넣고, 자료는 명부에 있는 그 인물을 그대로 쓴다.
    /// 명부에 아직 없으면(처음 나오는 인물) .chr 처음 값으로 만든다. 이미 파티에 있으면 두 번 넣지 않는다(가설).
    /// </summary>
    internal void AddMember(int party, int chr, CharacterData? data = null)
    {
        if (chr <= 0) return;
        if (data != null) _party[chr] = data;
        else if (!_party.ContainsKey(chr) && _db?.Character(chr) is { } c) _party[chr] = c;
        if (party == EpisodesScr._partyNo) Mos._members.Add(chr);
        else BankFor(party).Members.Add(chr);
    }

    /// <summary>
    /// 파티에서 인물을 뺀다(802, 원본 <c>0x1004de10</c>) — 번호만 빼고 <b>자료는 명부에 남긴다</b>(원본 명부는 파티와 무관, 감사 R4 G1).
    /// 전에는 자료를 버려서, 빠졌다 다시 들어온 인물이 .chr 처음 값으로 돌아왔다. 뺀 인물의 자료를 돌려준다(804 가 옮길 때 쓴다).
    /// </summary>
    internal CharacterData? RemoveMember(int party, int chr)
    {
        var data = _units.FirstOrDefault(u => u.ChrCode == chr && u.IsAlly)?.Data ?? _party.GetValueOrDefault(chr);
        if (data != null && chr > 0) _party[chr] = data;
        if (party == EpisodesScr._partyNo) Mos._members.Remove(chr);
        else BankFor(party).Members.Remove(chr);
        return data;
    }

    /// <summary>
    /// 파티마다 인물 사본을 싣던 옛 세이브 — 은행 사본을 명부로 옮긴다. 명부에 없으면 그대로 넣고, 둘 다 있으면 <b>그 인물이 동료인 파티의 사본</b>을
    /// 남긴다(옛 판에서 805 가 지금 파티에 동료 아닌 사본을 따로 만들던 것보다 실제로 싸운 쪽이 맞다).
    /// </summary>
    internal void MergeLegacyBankCopies()
    {
        foreach (var bank in _partyBank.Values)
        {
            foreach (var (chr, data) in bank.Party)
                if (!_party.ContainsKey(chr) || (bank.Members.Contains(chr) && !Mos._members.Contains(chr)))
                    _party[chr] = data;
            bank.Party.Clear();
        }
    }

    internal void AddMoney(int party, int amount)
    {
        if (party == EpisodesScr._partyNo) Mos._shopMoney += amount;
        else BankFor(party).Money += amount;
    }

    internal void AddItem(int party, int item, int count)
    {
        if (item <= 0) return;
        if (party == EpisodesScr._partyNo) _inventory[item] = _inventory.GetValueOrDefault(item) + count;
        else BankFor(party).Inventory[item] = BankFor(party).Inventory.GetValueOrDefault(item) + count;
    }

    /// <summary>803 [A, B] — 파티 B 의 인원·돈·아이템·군단을 A 에 합친다(가설: <c>0x100f0940</c> 이 부르는 함수들로 본 것).</summary>
    internal void MergeParties(int into, int from)
    {
        if (into == from) return;
        // B 를 은행 꼴로 꺼낸다
        PartyState src = from == EpisodesScr._partyNo ? TakeCurrentAsState() : BankFor(from);
        foreach (int chr in src.Members) AddMember(into, chr);   // 인원 번호만 옮긴다 — 자료는 명부에 그대로 있다
        // 우편함도 옮긴다 — B 의 편지를 A 에 붙이고, B 에서 읽은 것은 A 에서도 읽음(0x1004dd60(id, 읽음), 분석-모세스 mo-mail).
        var (box, read) = into == EpisodesScr._partyNo ? (Mos._mailbox, Mos._mailRead) : (BankFor(into).Mailbox, BankFor(into).MailRead);
        foreach (int mail in src.Mailbox)
        {
            if (!box.Contains(mail) && box.Count < MosesScene.MailboxLimit) box.Add(mail);
            if (src.MailRead.Contains(mail)) read.Add(mail);
        }
        AddMoney(into, src.Money);
        foreach (var (item, n) in src.Inventory) AddItem(into, item, n);
        foreach (int legion in src.Legions)
            if (into == EpisodesScr._partyNo) Mos._ownedLegions.Add(legion); else BankFor(into).Legions.Add(legion);
        if (from == EpisodesScr._partyNo) LoadState(new PartyState());
        else _partyBank.Remove(from);
    }

    /// <summary>
    /// 지금 파티의 상태를 은행 꼴로 떠 낸다(지금 값은 그대로 둔다) — 인원 번호·돈·가방·군단·우편함만. 인물 자료는 명부에 남는다
    /// (전투판에 선 아군의 지금 값을 명부에 먼저 적어 둔다).
    /// </summary>
    internal PartyState TakeCurrentAsState()
    {
        var s = new PartyState { Money = Mos._shopMoney };
        foreach (int chr in _party.Keys.ToList())
            if (_units.FirstOrDefault(u => u.ChrCode == chr && u.IsAlly)?.Data is { } live) _party[chr] = live;
        foreach (int m in Mos._members) s.Members.Add(m);
        foreach (int l in Mos._ownedLegions) s.Legions.Add(l);
        foreach (var (item, n) in _inventory) s.Inventory[item] = n;
        s.Mailbox.AddRange(Mos._mailbox);
        foreach (int id in Mos._mailRead) s.MailRead.Add(id);
        return s;
    }

    /// <summary>은행의 파티를 지금 파티로 꺼낸다 — 명부(<c>_party</c>)는 건드리지 않는다(원본은 <c>[0x101b6894] = 파티</c> 만 바꾼다).</summary>
    internal void LoadState(PartyState s)
    {
        Mos._members.Clear();
        foreach (int m in s.Members) Mos._members.Add(m);
        Mos._ownedLegions.Clear();
        foreach (int l in s.Legions) Mos._ownedLegions.Add(l);
        _inventory.Clear();
        foreach (var (item, n) in s.Inventory) _inventory[item] = n;
        Mos._shopMoney = s.Money;
        Mos._mailbox.Clear();
        Mos._mailbox.AddRange(s.Mailbox);
        Mos._mailRead.Clear();
        foreach (int id in s.MailRead) Mos._mailRead.Add(id);
    }

    /// <summary>연대표에서 다른 파티의 에피소드로 갈 때 — 지금 파티를 은행에 넣고 그 파티를 꺼낸다(원본 <c>[0x101b6894] = 파티</c>).</summary>
    internal void SwitchParty(int to)
    {
        if (to == EpisodesScr._partyNo) return;
        _partyBank[EpisodesScr._partyNo] = TakeCurrentAsState();
        LoadState(_partyBank.TryGetValue(to, out var next) ? next : new PartyState());
        _partyBank.Remove(to);
        EpisodesScr._partyNo = to;
    }

    /// <summary>세이브에 실리는 다른 파티 하나.</summary>
    internal sealed record SaveParty(int No, int[] Members, int Money, Dictionary<string, int> Inventory, int[] Legions, SaveUnit[] Units,
                                    int[]? Mailbox = null, int[]? MailRead = null);

    /// <summary>
    /// 은행을 세이브 꼴로 — 인원 번호·돈·가방·군단·우편함만. <c>Units</c> 는 이제 늘 비어 있다(인물 자료는 <c>SaveState.Party</c> 명부에,
    /// 원본 세이브도 명부 0x101b6884 를 한 번만 적는다). 옛 세이브의 <c>Units</c> 는 <see cref="RestoreBank"/> 가 읽는다.
    /// </summary>
    internal SaveParty[] SaveBank() =>
        [.. _partyBank.Select(p => new SaveParty(p.Key, [.. p.Value.Members], p.Value.Money,
            p.Value.Inventory.ToDictionary(i => i.Key.ToString(), i => i.Value), [.. p.Value.Legions], [],
            [.. p.Value.Mailbox], [.. p.Value.MailRead]))];

    internal void RestoreBank(SaveParty[]? bank)
    {
        _partyBank.Clear();
        foreach (var sp in bank ?? [])
        {
            var s = new PartyState { Money = sp.Money };
            foreach (int m in sp.Members) s.Members.Add(m);
            foreach (int l in sp.Legions) s.Legions.Add(l);
            foreach (var (id, n) in sp.Inventory) if (int.TryParse(id, out int item)) s.Inventory[item] = n;
            s.Mailbox.AddRange(sp.Mailbox ?? []);
            foreach (int id in sp.MailRead ?? []) s.MailRead.Add(id);
            // 옛 세이브의 파티별 인물 사본 — 명부를 채운 뒤 MergeLegacyBankCopies 가 옮긴다.
            foreach (var u in sp.Units ?? [])
                if (_db?.Character(u.ChrCode) is { } pc)
                    s.Party[u.ChrCode] = Restored(pc, u, regrow: true);
            _partyBank[sp.No] = s;
        }
    }
}

using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파티 셋(0 살라딘 · 1 베라모드 · 2 크리스티앙, 원본 <c>0x101b6888[0..2]</c>) — 지금 파티는 <c>_party</c>·<c>_members</c>·<c>_ownedLegions</c>·
/// <c>_shopMoney</c>·<c>_inventory</c> 가 들고, 나머지 파티는 여기 은행에 넣어 둔다. 스크립트 703·705·801·802 의 첫 인자가 파티 번호이고,
/// 804 는 인물을 파티 사이로 옮기며(닥터 엠블라가 크리스티앙·죠안을 2번 파티로), 803 은 파티 둘을 합친다.
/// </summary>
internal sealed unsafe partial class BattleSceneWindow
{
    private sealed class PartyState
    {
        public Dictionary<int, CharacterData> Party { get; } = [];
        public HashSet<int> Members { get; } = [];
        public HashSet<int> Legions { get; } = [];
        public int Money { get; set; }
        public Dictionary<int, int> Inventory { get; } = [];
        public List<int> Mailbox { get; } = [];
        public HashSet<int> MailRead { get; } = [];
    }

    /// <summary>지금 파티가 아닌 파티들의 상태(파티 번호 → 상태). 세이브에 실린다.</summary>
    private readonly Dictionary<int, PartyState> _partyBank = [];

    private PartyState BankFor(int party) =>
        _partyBank.TryGetValue(party, out var s) ? s : _partyBank[party] = new PartyState();

    /// <summary>파티에 인물을 넣는다 — 이미 있으면 자료만 남기고 그대로(원본 <c>0x1004ddd0</c> 도 두 번 넣지 않는다, 가설).</summary>
    private void AddMember(int party, int chr, CharacterData? data = null)
    {
        if (chr <= 0) return;
        if (party == _partyNo)
        {
            if (!_party.ContainsKey(chr) && (data ?? _db?.Character(chr)) is { } c) _party[chr] = c;
            else if (data != null) _party[chr] = data;
            _members.Add(chr);
            return;
        }
        var bank = BankFor(party);
        if (!bank.Party.ContainsKey(chr) && (data ?? _db?.Character(chr)) is { } bc) bank.Party[chr] = bc;
        else if (data != null) bank.Party[chr] = data;
        bank.Members.Add(chr);
    }

    /// <summary>파티에서 인물을 뺀다 — 뺀 인물의 자료를 돌려준다(804 가 다른 파티로 옮길 때 쓴다).</summary>
    private CharacterData? RemoveMember(int party, int chr)
    {
        if (party == _partyNo)
        {
            var data = _units.FirstOrDefault(u => u.ChrCode == chr)?.Data ?? _party.GetValueOrDefault(chr);
            _party.Remove(chr);
            _members.Remove(chr);
            return data;
        }
        var bank = BankFor(party);
        var bd = bank.Party.GetValueOrDefault(chr);
        bank.Party.Remove(chr);
        bank.Members.Remove(chr);
        return bd;
    }

    private void AddMoney(int party, int amount)
    {
        if (party == _partyNo) _shopMoney += amount;
        else BankFor(party).Money += amount;
    }

    private void AddItem(int party, int item, int count)
    {
        if (item <= 0) return;
        if (party == _partyNo) _inventory[item] = _inventory.GetValueOrDefault(item) + count;
        else BankFor(party).Inventory[item] = BankFor(party).Inventory.GetValueOrDefault(item) + count;
    }

    /// <summary>803 [A, B] — 파티 B 의 인원·돈·아이템·군단을 A 에 합친다(가설: <c>0x100f0940</c> 이 부르는 함수들로 본 것).</summary>
    private void MergeParties(int into, int from)
    {
        if (into == from) return;
        // B 를 은행 꼴로 꺼낸다
        PartyState src = from == _partyNo ? TakeCurrentAsState() : BankFor(from);
        foreach (var (chr, data) in src.Party) AddMember(into, chr, data);
        // 우편함도 옮긴다 — B 의 편지를 A 에 붙이고, B 에서 읽은 것은 A 에서도 읽음(0x1004dd60(id, 읽음), 분석-모세스 mo-mail).
        var (box, read) = into == _partyNo ? (_mailbox, _mailRead) : (BankFor(into).Mailbox, BankFor(into).MailRead);
        foreach (int mail in src.Mailbox)
        {
            if (!box.Contains(mail) && box.Count < MailboxLimit) box.Add(mail);
            if (src.MailRead.Contains(mail)) read.Add(mail);
        }
        AddMoney(into, src.Money);
        foreach (var (item, n) in src.Inventory) AddItem(into, item, n);
        foreach (int legion in src.Legions)
            if (into == _partyNo) _ownedLegions.Add(legion); else BankFor(into).Legions.Add(legion);
        if (from == _partyNo) LoadState(new PartyState());
        else _partyBank.Remove(from);
    }

    /// <summary>지금 파티의 상태를 은행 꼴로 떠 낸다(지금 값은 그대로 둔다).</summary>
    private PartyState TakeCurrentAsState()
    {
        var s = new PartyState { Money = _shopMoney };
        foreach (var (chr, data) in _party) s.Party[chr] = _units.FirstOrDefault(u => u.ChrCode == chr)?.Data ?? data;
        foreach (int m in _members) s.Members.Add(m);
        foreach (int l in _ownedLegions) s.Legions.Add(l);
        foreach (var (item, n) in _inventory) s.Inventory[item] = n;
        s.Mailbox.AddRange(_mailbox);
        foreach (int id in _mailRead) s.MailRead.Add(id);
        return s;
    }

    private void LoadState(PartyState s)
    {
        _party.Clear();
        foreach (var (chr, data) in s.Party) _party[chr] = data;
        _members.Clear();
        foreach (int m in s.Members) _members.Add(m);
        _ownedLegions.Clear();
        foreach (int l in s.Legions) _ownedLegions.Add(l);
        _inventory.Clear();
        foreach (var (item, n) in s.Inventory) _inventory[item] = n;
        _shopMoney = s.Money;
        _mailbox.Clear();
        _mailbox.AddRange(s.Mailbox);
        _mailRead.Clear();
        foreach (int id in s.MailRead) _mailRead.Add(id);
    }

    /// <summary>연대표에서 다른 파티의 에피소드로 갈 때 — 지금 파티를 은행에 넣고 그 파티를 꺼낸다(원본 <c>[0x101b6894] = 파티</c>).</summary>
    private void SwitchParty(int to)
    {
        if (to == _partyNo) return;
        _partyBank[_partyNo] = TakeCurrentAsState();
        LoadState(_partyBank.TryGetValue(to, out var next) ? next : new PartyState());
        _partyBank.Remove(to);
        _partyNo = to;
    }

    /// <summary>세이브에 실리는 다른 파티 하나.</summary>
    private sealed record SaveParty(int No, int[] Members, int Money, Dictionary<string, int> Inventory, int[] Legions, SaveUnit[] Units,
                                    int[]? Mailbox = null, int[]? MailRead = null);

    private SaveParty[] SaveBank() =>
        [.. _partyBank.Select(p => new SaveParty(p.Key, [.. p.Value.Members], p.Value.Money,
            p.Value.Inventory.ToDictionary(i => i.Key.ToString(), i => i.Value), [.. p.Value.Legions],
            [.. p.Value.Party.Select(c => new SaveUnit(c.Key, 0, 0, 0, 0, 0, 0, true, false, c.Value.Level, c.Value.CumExp, c.Value.Exp,
                                                       c.Value.Items, c.Value.Passives, [.. c.Value.Abilities.Select(a => new SaveAbility(a.Ability, a.Level))],
                                                       Char: SaveCharOf(c.Value)))],
            [.. p.Value.Mailbox], [.. p.Value.MailRead]))];

    private void RestoreBank(SaveParty[]? bank)
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
            foreach (var u in sp.Units)
                if (_db?.Character(u.ChrCode) is { } pc)
                    s.Party[u.ChrCode] = Restored(pc, u, regrow: true);
            _partyBank[sp.No] = s;
        }
    }
}

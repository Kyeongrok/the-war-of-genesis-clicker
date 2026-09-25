using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 모든 캐릭터 레코드(<c>Chr/*.chr</c>)의 기본정보·능력치를 보는 창 — 왼쪽 목록에서 고르면 오른쪽에 상세가 나온다
/// (이름, 칭호, 체질, 계열, 직업, Status 화면 수치 LV·HP·SOUL·TP·ATK·ACR·RDP·LP·CTP·STP·PSY·DEP·DEX, 무기·장비, 어빌리티).
/// 예전에는 22열짜리 표 한 장이라 읽기 어려웠다(사용자 요청).
/// </summary>
/// <remarks>
/// 수치는 <see cref="GameDatabase"/> 의 식(<c>G3PartII.dll</c> 에서 옮김)으로 셈한다. 죠안(Chr 0221)은 게임 Status
/// 화면과 HP 만 빼고 모두 맞는다(HP 는 어빌리티 패시브 보너스를 아직 안 넣음).
/// </remarks>
public partial class CharacterStatsWindow : Window
{
    /// <summary>상세의 「이름 : 값」 한 줄.</summary>
    public sealed record Stat(string Label, string Value);

    public sealed record Row(int Code, string Name, string Title, string Body, string Family, string Job, int Level,
                             int Hp, string Soul, int Tp, int Atk, int Acr, int Rdp, uint Lp, int Ctp, int Stp,
                             int Psy, int Dep, int Dex, string Weapon, IReadOnlyList<string> EquipmentItems,
                             IReadOnlyList<string> AbilityItems, bool IsPlaceholder)
    {
        /// <summary>목록 둘째 줄 — 칭호 · 직업.</summary>
        public string Subtitle => string.Join(" · ", new[] { Title, Job }.Where(t => t.Length > 0));

        /// <summary>상세 머리 밑 한 줄 — 칭호 · 체질 · 계열 · 직업.</summary>
        public string Profile => string.Join(" · ", new[] { Title, Body, Family, Job }.Where(t => t.Length > 0));

        public IReadOnlyList<Stat> Vitals => [new("LV", Level.ToString()), new("HP", Hp.ToString()), new("SOUL", Soul), new("TP", Tp.ToString())];
        public IReadOnlyList<Stat> Combat => [new("ATK", Atk.ToString()), new("ACR", Acr.ToString()), new("RDP", Rdp.ToString())];
        public IReadOnlyList<Stat> Bases => [new("LP", Lp.ToString()), new("CTP", Ctp.ToString()), new("STP", Stp.ToString()),
                                             new("PSY", Psy.ToString()), new("DEP", Dep.ToString()), new("DEX", Dex.ToString())];
    }

    private readonly ICollectionView _view;

    public CharacterStatsWindow(GameDatabase db, IEnumerable<int> codes)
    {
        InitializeComponent();

        var rows = new List<Row>();
        foreach (int code in codes.OrderBy(c => c))
        {
            if (db.Character(code) is not { } c) continue;
            rows.Add(MakeRow(db, c));
        }
        List.ItemsSource = rows;
        _view = CollectionViewSource.GetDefaultView(rows);
        _view.Filter = Accept;
        StatusText.Text = $"{_view.Cast<object>().Count()}명";
        List.SelectedIndex = 0;
    }

    private void List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        Detail.DataContext = List.SelectedItem;

    private static Row MakeRow(GameDatabase db, CharacterData c)
    {
        string name = db.T(c.NameId);
        int tp = db.MaxTp(c);
        int soul = db.SoulStart;

        string ItemName(ushort id) => id != 0 && db.Items.TryGetValue(id, out var it) ? db.T(it.NameId) : "";
        var weapon = c.Items[0] != 0 && db.Items.TryGetValue(c.Items[0], out var w) ? w : null;
        string weaponText = weapon == null ? "" : $"{db.T(weapon.NameId)} ({GameDatabase.WeaponTypeName(weapon.Type)}, 공격 {weapon.Attack})";
        var equipment = c.Items.Skip(1).Select(ItemName).Where(s => s.Length > 0).ToList();
        if (equipment.Count == 0) equipment.Add("없음");

        var abilities = c.Abilities.OrderBy(a => a.Ability).Select(a =>
        {
            if (!db.Abilities.TryGetValue(a.Ability, out var ab)) return $"#{a.Ability} Lv{a.Level}";
            string next = a.Level < ab.MaxLevel && ab.WorkByLevel.TryGetValue(a.Level, out int wid) && db.Works.TryGetValue(wid, out var work)
                ? $" ({work.ExpCost}exp)" : "";
            return $"{db.T(ab.NameId)} Lv{a.Level}{next}";
        }).ToList();
        if (abilities.Count == 0) abilities.Add("없음");

        bool placeholder = name.Length == 0 || c.Lp <= 1 || name.StartsWith('<') || name.StartsWith('=') || name.StartsWith('-');
        return new Row(c.Code, name, db.T(c.TitleId), db.BodyName(c.Body), db.FamilyName(c), db.JobName(c), c.Level,
                       db.MaxHp(c), $"{soul}/{db.MaxSoul(c)}", tp, db.Atk(c, soul), db.Acr(c, tp), db.RdpAtFullHp(c),
                       c.Lp, c.Ctp, db.Stp(c), db.Psy(c), c.Dep, db.Dex(c), weaponText.Length > 0 ? weaponText : "없음", equipment, abilities, placeholder);
    }

    private bool Accept(object o)
    {
        if (o is not Row r) return false;
        if (HideEmptyToggle.IsChecked == true && r.IsPlaceholder) return false;
        string q = FilterBox.Text.Trim();
        return q.Length == 0 || r.Name.Contains(q) || r.Title.Contains(q) || r.Job.Contains(q) || r.Family.Contains(q)
               || r.Code.ToString("D4").Contains(q);
    }

    private void FilterBox_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        _view.Refresh();
        StatusText.Text = $"{_view.Cast<object>().Count()}명";
        if (List.SelectedItem == null) List.SelectedIndex = 0;
    }
}

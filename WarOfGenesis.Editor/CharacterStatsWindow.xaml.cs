using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 모든 캐릭터 레코드(<c>Chr/*.chr</c>)의 기본정보·능력치를 한 표로 보는 창 — 이름, 칭호, 체질, 계열, 직업,
/// Status 화면 수치(LV·HP·SOUL·TP·ATK·ACR·RDP·LP·CTP·STP·PSY·DEP·DEX), 무기·장비, 어빌리티.
/// </summary>
/// <remarks>
/// 수치는 <see cref="GameDatabase"/> 의 식(<c>G3PartII.dll</c> 에서 옮김)으로 셈한다. 죠안(Chr 0221)은 게임 Status
/// 화면과 HP 만 빼고 모두 맞는다(HP 는 어빌리티 패시브 보너스를 아직 안 넣음).
/// </remarks>
public partial class CharacterStatsWindow : Window
{
    public sealed record Row(int Code, string Name, string Title, string Body, string Family, string Job, int Level,
                             int Hp, string Soul, int Tp, int Atk, int Acr, int Rdp, uint Lp, int Ctp, int Stp,
                             int Psy, int Dep, int Dex, string Weapon, string Equipment, string Abilities, bool IsPlaceholder);

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
        Grid.ItemsSource = rows;
        _view = CollectionViewSource.GetDefaultView(rows);
        _view.Filter = Accept;
        StatusText.Text = $"레코드 {rows.Count}개";
    }

    private static Row MakeRow(GameDatabase db, CharacterData c)
    {
        string name = db.T(c.NameId);
        int tp = db.MaxTp(c);
        int soul = db.SoulStart;

        string ItemName(ushort id) => id != 0 && db.Items.TryGetValue(id, out var it) ? db.T(it.NameId) : "";
        var weapon = c.Items[0] != 0 && db.Items.TryGetValue(c.Items[0], out var w) ? w : null;
        string weaponText = weapon == null ? "" : $"{db.T(weapon.NameId)} ({GameDatabase.WeaponTypeName(weapon.Type)}, 공격 {weapon.Attack})";
        string equipment = string.Join(" / ", c.Items.Skip(1).Select(ItemName).Where(s => s.Length > 0));

        string abilities = string.Join(", ", c.Abilities.OrderBy(a => a.Ability).Select(a =>
        {
            if (!db.Abilities.TryGetValue(a.Ability, out var ab)) return $"#{a.Ability} Lv{a.Level}";
            string next = a.Level < ab.MaxLevel && ab.WorkByLevel.TryGetValue(a.Level, out int wid) && db.Works.TryGetValue(wid, out var work)
                ? $" ({work.ExpCost}exp)" : "";
            return $"{db.T(ab.NameId)} Lv{a.Level}{next}";
        }));

        bool placeholder = name.Length == 0 || c.Lp <= 1 || name.StartsWith('<') || name.StartsWith('=') || name.StartsWith('-');
        return new Row(c.Code, name, db.T(c.TitleId), db.BodyName(c.Body), db.FamilyName(c), db.JobName(c), c.Level,
                       db.MaxHp(c), $"{soul}/{db.MaxSoul(c)}", tp, db.Atk(c, soul), db.Acr(c, tp), db.RdpAtFullHp(c),
                       c.Lp, c.Ctp, db.Stp(c), db.Psy(c), c.Dep, db.Dex(c), weaponText, equipment, abilities, placeholder);
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
        StatusText.Text = $"{_view.Cast<object>().Count()}개 보임";
    }
}

using System.ComponentModel;
using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 모든 캐릭터 레코드(<c>Chr/*.chr</c>)의 기본정보·능력치를 보는 편집기 첫 화면 — 왼쪽 목록에서 고르면 오른쪽에 상세가 나온다
/// (이름, 칭호, 체질, 계열, 직업, Status 화면 수치 LV·HP·SOUL·TP·ATK·ACR·RDP·LP·CTP·STP·PSY·DEP·DEX, 무기·장비, 어빌리티).
/// 예전에는 22열짜리 표 한 장이라 읽기 어려웠다(사용자 요청).
/// </summary>
/// <remarks>
/// 수치는 <see cref="GameDatabase"/> 의 식(<c>G3PartII.dll</c> 에서 옮김)으로 셈한다. 죠안(Chr 0221)은 게임 Status
/// 화면과 HP 만 빼고 모두 맞는다(HP 는 어빌리티 패시브 보너스를 아직 안 넣음).
/// </remarks>
public partial class CharacterStatsView : UserControl
{
    /// <summary>상세의 「이름 : 값」 한 줄.</summary>
    public sealed record Stat(string Label, string Value);

    /// <summary>초상화 한 장 — 모션(표정) 하나의 첫 컷.</summary>
    public sealed record Portrait(ImageSource Picture, int Width, int Height, string Label)
    {
        public string Size => $"{Width}×{Height}";
    }

    public sealed record Row(int Code, int FaceId, string Name, string Title, string Body, string Family, string Job, int Level,
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

    private ICollectionView? _view;
    private GameDatabase? _db;

    /// <summary>목록 우클릭 「모션 매핑 보기」 — 고른 레코드 번호.</summary>
    public event Action<int>? MotionMappingRequested;

    /// <summary>목록 우클릭 「내보내기」 — 고른 레코드 번호.</summary>
    public event Action<int>? ExportRequested;

    public CharacterStatsView() => InitializeComponent();

    /// <summary>게임 폴더를 열 때마다 목록을 새로 채운다.</summary>
    public void Load(GameDatabase db, IEnumerable<int> codes)
    {
        _db = db;
        _portraits.Clear();

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

    private void List_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        Detail.DataContext = List.SelectedItem;
        var faceId = (List.SelectedItem as Row)?.FaceId ?? 0;
        var portraits = PortraitsOf(faceId);
        Portraits.ItemsSource = portraits;
        PortraitHeader.Text = faceId == 0 ? "초상화 (없음)" : $"초상화 — Obs {faceId:D4} · {portraits.Count}장";
    }

    private readonly Dictionary<int, IReadOnlyList<Portrait>> _portraits = [];

    /// <summary>초상 Obs 의 모션(표정)마다 첫 컷 — 게임 pak 에서 읽고, 한 번 푼 것은 들고 있는다.</summary>
    private IReadOnlyList<Portrait> PortraitsOf(int faceId)
    {
        if (faceId == 0) return [];
        if (_portraits.TryGetValue(faceId, out var cached)) return cached;
        var list = new List<Portrait>();
        try
        {
            if (_db?.Files.Read("Obs", $"{faceId:D4}.obs") is { } bytes)
                foreach (var motion in ObsSprite.Decode(bytes))
                {
                    var f = motion.Frames[0];
                    var bitmap = BitmapSource.Create(f.Width, f.Height, 96, 96, PixelFormats.Bgra32, null, f.Bgra, f.Width * 4);
                    bitmap.Freeze();
                    list.Add(new Portrait(bitmap, f.Width, f.Height, $"#{motion.Id}"));
                }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { }
        return _portraits[faceId] = list;
    }

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
        return new Row(c.Code, c.FaceId, name, db.T(c.TitleId), db.BodyName(c.Body), db.FamilyName(c), db.JobName(c), c.Level,
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

    private void MotionMappingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is Row r) MotionMappingRequested?.Invoke(r.Code);
    }

    private void ExportMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is Row r) ExportRequested?.Invoke(r.Code);
    }

    /// <summary>우클릭한 항목을 먼저 고른다 — 컨텍스트 메뉴가 그 캐릭터를 대상으로 하도록.</summary>
    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonDown(e);
        if (ItemsControl.ContainerFromElement(List, (DependencyObject)e.OriginalSource) is ListBoxItem item) item.IsSelected = true;
    }

    private void FilterBox_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        _view.Refresh();
        StatusText.Text = $"{_view.Cast<object>().Count()}명";
        if (List.SelectedItem == null) List.SelectedIndex = 0;
    }
}

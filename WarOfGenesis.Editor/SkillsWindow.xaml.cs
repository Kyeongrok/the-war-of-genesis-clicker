using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 모든 어빌리티(<c>Abi/*.abi</c>)와 레벨별 work(<c>Dat/*.att</c>)를 한 표로 보는 창 — 판정에 쓰는 칸(종류·위력·명중·치명·사거리·대상·효과 범위)과
/// TP·SOUL·EXP 비용, 그 어빌리티를 목록에 가진 직업.
/// </summary>
/// <remarks>칸 뜻은 옵시디안 분석-전투 "공격·어빌리티 판정"의 work 필드 표를 따랐다.</remarks>
public partial class SkillsWindow : Window
{
    public sealed record Row(int AbilityId, string Name, int Level, int MaxLevel, int WorkId, string Kind, int Power, int Accuracy,
                             int Critical, string Range, string Target, string Area, string Tp, int Soul, string Exp, string Jobs);

    private readonly ICollectionView _view;

    public SkillsWindow(GameDatabase db)
    {
        InitializeComponent();

        var jobsByAbility = new Dictionary<int, List<string>>();
        foreach (var job in db.Jobs.Values)
        {
            string name = job.NamesByBody.Select(db.T).FirstOrDefault(n => n.Length > 0) ?? $"직업 {job.Id}";
            foreach (ushort a in job.AbilityList.Where(a => a != 0).Distinct())
                (jobsByAbility.TryGetValue(a, out var list) ? list : jobsByAbility[a] = []).Add(name);
        }

        var rows = new List<Row>();
        foreach (var ab in db.Abilities.Values.OrderBy(a => a.Id))
        {
            string name = db.T(ab.NameId);
            string jobs = jobsByAbility.TryGetValue(ab.Id, out var j) ? string.Join(", ", j.Distinct()) : "";
            foreach (var (level, workId) in ab.WorkByLevel.OrderBy(p => p.Key))
            {
                if (!db.Works.TryGetValue(workId, out var w)) continue;
                rows.Add(new Row(ab.Id, name, level, ab.MaxLevel, workId, KindName(w.Kind), w.Power, w.Accuracy, w.Critical + 1,
                                 RangeText(w), TargetText(w.TargetMode),
                                 $"모양 {w.AreaShape} · {w.AreaArg}칸", w.HpFactor == 0 ? $"{w.TpBase}" : $"{w.TpBase} + 체질({w.HpFactor})",
                                 w.SoulBase, level < ab.MaxLevel && w.ExpCost > 0 ? w.ExpCost.ToString() : "", jobs));
            }
        }

        Grid.ItemsSource = rows;
        _view = CollectionViewSource.GetDefaultView(rows);
        _view.Filter = Accept;
        StatusText.Text = $"어빌리티 {rows.Select(r => r.AbilityId).Distinct().Count()}개 · 줄 {rows.Count}개";
    }

    private static string KindName(byte kind) => kind switch
    {
        0 => "피해",
        1 or 5 => "회복",
        2 or 3 => "보조",
        4 => "오브젝트",
        _ => kind.ToString(),
    };

    /// <summary>대상 방식(+0x13·+0x1e) — 분석-전투 "어빌리티 범위·자세·상태이상".</summary>
    private static string TargetText(byte mode) => mode switch
    {
        0 or 2 => "자기 자리",
        1 => "적 하나",
        3 or 6 => "아무 칸",
        4 => "아군 하나",
        5 => "아무 유닛",
        7 => "빈 칸",
        8 => "오브젝트",
        _ => $"모드 {mode}",
    };

    private static string RangeText(WorkData w)
    {
        string shape = w.RangeShape switch
        {
            0 => "제자리", 1 => "마름모", 2 => "십자", 3 => "부채꼴", 4 => "화면 전체",
            5 => "직선", 6 => "폭3 줄", 7 => "폭5 줄", 8 => "대각선", 9 => "삼각형", _ => $"모양 {w.RangeShape}",
        };
        return w.RangeShape == 0 ? shape : $"{shape} {w.RangeMin}~{w.RangeMax}칸";
    }

    private bool Accept(object o)
    {
        if (o is not Row r) return false;
        if (LevelsToggle.IsChecked != true && r.Level != 1) return false;
        string q = FilterBox.Text.Trim();
        return q.Length == 0 || r.Name.Contains(q) || r.Jobs.Contains(q) || r.AbilityId.ToString("D4").Contains(q) || r.WorkId.ToString() == q;
    }

    private void FilterBox_TextChanged(object sender, RoutedEventArgs e)
    {
        if (_view == null) return;
        _view.Refresh();
        StatusText.Text = $"{_view.Cast<object>().Count()}줄 보임";
    }
}

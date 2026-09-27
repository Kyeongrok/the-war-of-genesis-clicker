using System.Windows;
using WarOfGenesis.Assets;

namespace WarOfGenesis.Editor;

/// <summary>
/// 구성원 고르기 창 — 군단 편집기에서 리더·부하 칸을 누르면 뜬다(ed-1). 캐릭터 스탯 창과 같은 목록·초상·능력치를 보며 인물을 고른다.
/// </summary>
public partial class MemberPickWindow : Window
{
    /// <summary>고른 Chr 번호 — 「비우기」면 0, 취소면 null.</summary>
    public int? Chosen { get; private set; }

    public MemberPickWindow(GameDatabase db, int current, string what)
    {
        InitializeComponent();
        Title = $"구성원 고르기 — {what}";
        ClearButton.Content = what.StartsWith("리더") ? "전투 자료대로" : "비우기";
        Stats.Load(db, Enumerable.Range(0, 1000));
        if (current > 0) Loaded += (_, _) => Stats.Select(current);
        Stats.RowDoubleClicked += code => { Chosen = code; DialogResult = true; };
    }

    private void Pick_Click(object sender, RoutedEventArgs e)
    {
        if (Stats.SelectedCode is not { } code) return;
        Chosen = code;
        DialogResult = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Chosen = 0;
        DialogResult = true;
    }
}

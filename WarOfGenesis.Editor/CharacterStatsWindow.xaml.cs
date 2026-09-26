using System.Windows;

namespace WarOfGenesis.Editor;

/// <summary>
/// 캐릭터 스탯 창 — 메뉴 캐릭터 &gt; 캐릭터 스탯. 예전에는 편집기 첫 화면이었는데 첫 화면을 스킬 편집으로 바꾸면서 창으로 옮겼다(사용자 요청).
/// </summary>
public partial class CharacterStatsWindow : Window
{
    public CharacterStatsWindow() => InitializeComponent();

    public CharacterStatsView View => Stats;
}

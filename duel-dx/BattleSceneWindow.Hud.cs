using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투 화면에 늘 떠 있는 작은 창(fa-10) — 전장 이름·소지금·고른 인물의 TP·SOUL·커서 자리.
/// </summary>
/// <remarks>
/// 옵시디안 분석-전투 「전투 화면 표시 (fa-10)」: 오른쪽 위 정보와 왼쪽 아래 자리는 <b>따로 있는 창이 아니라 한 창의 좌우</b>다.
/// 원본은 640×480 화면 (490,10) 에 <b>140×60 창 하나</b>(창번호 0x489, 제목줄 없음)를 놓고 그 안에 이렇게 그린다:
/// <list type="bullet">
/// <item>전장 이름 — <c>TXR[Btl 머리 워드 4]</c>(0045 = 1068 「코어헌터 훈련장(1)」), (70, 4) 가운데, 노랑</item>
/// <item>POSITION 그림 — Obs 0894 모션 12, (8, 30) 65틱 되풀이</item>
/// <item>칸 높이 (40, 26) · 커서 칸 x (64, 40) · y (20, 46) — 모두 가운데 맞춤, 흰색</item>
/// <item><c>%dGP</c> (134, 26) · <c>Tp:%d</c> (134, 38) · <c>Soul:%d</c> (134, 50) — 오른쪽 맞춤, 흰색</item>
/// </list>
/// 창은 대사 중이나 몇몇 상태에서 숨고, TP·SOUL 은 고른 인물이 없으면 안 나온다.
/// 소지금은 모세스 상점과 같은 지갑을 보여 준다(데모는 5000GP 로 시작).
/// 자리는 원본과 같은 "오른쪽 위에서 10픽셀" 로 잡되, 판이 640 보다 넓어 판 오른쪽에 붙인다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int HudW = 140, HudH = 60, HudObs = 894, HudPositionMotion = 12;
    private const uint HudYellow = 0xFFFFFF00;

    // 소지금은 모세스 상점이 쓰는 그 값이다(원본은 파티 레코드 +0x10c 의 GP).

    private void DrawHud()
    {
        if (_mosesOpen || _chaptersOpen || _outcome.Length > 0 || _map is not { } map) return;

        int x = _camX + ViewWidth - HudW - 10, y = _camY + GridTop + 10;
        DrawGameFrame(x, y, HudW, HudH);

        // 원본 글꼴은 9~11픽셀이다 — 우리 기본 13픽셀이면 창 밖으로 나간다.
        const float size = 11f;
        void Center(string s, int cx, int cy, uint color)
        {
            var (_, w, _) = GetText(s, color, size);
            DrawText(s, x + cx - w / 2, y + cy, color, size);
        }
        void Right(string s, int rx, int ry)
        {
            var (_, w, _) = GetText(s, White, size);
            DrawText(s, x + rx - w, y + ry, White, size);
        }

        Center(_scene.Title, 70, 3, HudYellow);
        DrawUi(HudObs, HudPositionMotion, (int)(_lastTime * TicksPerSecond), x + 8, y + 30, UiBlend.Alpha);

        // 커서가 놓인 칸과 그 높이
        int col = Math.Clamp(_mouse.X / TileW, 0, Cols - 1);
        int row = Math.Clamp(RowAt(_mouse.X, _mouse.Y), 0, Rows - 1);
        Center($"{map.HeightAt(col, row)}", 40, 24, White);
        Center($"{col}", 64, 38, White);
        Center($"{row}", 20, 44, White);

        Right($"{_shopMoney}GP", 134, 24);
        if (_selected >= 0 && _units[_selected] is { Alive: true } unit)
        {
            Right($"Tp:{unit.Tp}", 134, 36);
            Right($"Soul:{unit.Soul}", 134, 48);
        }
    }
}

using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

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
internal sealed unsafe partial class BattleScene
{
    internal const int HudW = 140, HudH = 60, HudObs = 894, HudPositionMotion = 12;
    internal const uint HudYellow = 0xFFFFFF00;

    // 소지금은 모세스 상점이 쓰는 그 값이다(원본은 파티 레코드 +0x10c 의 GP).

    internal void DrawHud()
    {
        if (host.Mos._mosesOpen || host.ChaptersScr._chaptersOpen || _outcome.Length > 0 || host._map is not { } map) return;
        // 원본 그리기(0x100e4600)는 이벤트 스크립트가 도는 동안(0x10056f90)·배치(상태 1·2)·끝맺음(24) 에 창을 통째로 숨긴다.
        if (EventsBusy || _deployOpen) return;
        if (host._hiResUi && host.BattleCovered) return;   // 층에 그리는 창은 판을 덮은 화면(모세스 · 필드 …)이 가려 주지 못한다

        // 세로는 왼쪽 위 상자 목록(DrawChestList 의 y − 3)과 윗선을 맞춘다(사용자 요청 fa-34) — 전에는 GridTop + 10 이라 조금 처져 보였다.
        int x = host._camX + host.ViewWidth - HudW - 10, y = host._camY + (host._showStatusBar ? GridTop + 1 : 5);
        host.UiFrame(x, y, HudW, HudH);

        // 원본 글꼴은 9~11픽셀이다 — 우리 기본 13픽셀이면 창 밖으로 나간다.
        const float size = 11f;
        void Center(string s, int cx, int cy, uint color)
        {
            var (_, w, _) = host.UiGetText(s, color, size);
            host.UiText(s, x + cx - w / 2, y + cy, color, size);
        }
        void Right(string s, int rx, int ry)
        {
            var (_, w, _) = host.UiGetText(s, White, size);
            host.UiText(s, x + rx - w, y + ry, White, size);
        }

        Center(host._scene.Title, 70, 3, HudYellow);
        host.UiSprite(HudObs, HudPositionMotion, (int)(host._lastTime * TicksPerSecond), x + 8, y + 30);

        // 커서가 놓인 칸과 그 높이 — 판 밖(칸 −1)이면 원본은 숫자를 지운다(0x100e4250).
        int col = host._mouse.X >= 0 ? host._mouse.X / TileW : -1;
        int row = host.RowAt(host._mouse.X, host._mouse.Y);
        if ((uint)col < host.Cols && (uint)row < host.Rows)
        {
            Center($"{map.HeightAt(col, row)}", 40, 24, White);
            Center($"{col}", 64, 38, White);
            Center($"{row}", 20, 44, White);
        }

        // 원본 자리는 (134,26)·(134,38)·(134,50)인데 우리 글꼴이 아래로 처져 보여 조금 올린다(사용자 요청).
        Right(host._scene.IsPart1 ? $"{host.Part1.Eld} eld" : $"{host.Mos._shopMoney}GP", 134, 20);   // 파트1 판은 파트1 돈(eld)
        // Tp·Soul 은 고른 유닛이 아니라 <b>지금 차례인 유닛</b>([CBattle+0x4ce8])의 것이다(0x100e3f60, ba-20 G16) — AI 차례에도 그 유닛 값이 보인다.
        if (_turn >= 0 && _turn < host._units.Length && host._units[_turn] is { Alive: true } unit)
        {
            Right($"Tp:{unit.Tp}", 134, 32);
            Right($"Soul:{unit.Soul}", 134, 44);
        }

        // 겨누는 중인 어빌리티·아이템 이름 — 창 바로 아래 같은 폭의 칸에(사용자 요청 fa-48, 원본에 없다).
        // 목록이 닫힌 뒤에는 잠깐 뜨는 안내 줄 말고는 무엇을 골랐는지 알 길이 없었다.
        if (AimingName() is { } aiming)
        {
            const int nameH = 22;
            int ny = y + HudH + 2;
            host.UiFrame(x, ny, HudW, nameH);
            var (_, w, _) = host.UiGetText(aiming, HudYellow, size);
            host.UiText(aiming, x + Math.Max(4, (HudW - w) / 2), ny + 4, HudYellow, size);
        }
    }

    /// <summary>대상을 고르는 중인 어빌리티(아이템이면 아이템) 이름 — 기본 공격이거나 고르는 중이 아니면 null.</summary>
    internal string? AimingName()
    {
        if (_targetWork < 0 || _targetIsBasicAttack || _abilityMenu || !IsPlayerTurn || host._db is not { } db || Work(_targetWork) is not { } w) return null;
        if (_targetItem != 0) return db.Items.GetValueOrDefault(_targetItem) is { } item ? db.T(item.NameId) : null;
        if (!db.Abilities.TryGetValue(w.AbilityId, out var ab)) return null;
        // 군단기(분류 2)는 목록에서도 레벨 없이 보인다.
        return ab.Category == 2 ? db.T(ab.NameId) : $"{db.T(ab.NameId)} Lv{w.Level}";
    }
}

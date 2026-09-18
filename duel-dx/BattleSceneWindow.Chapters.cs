using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 챕터 고르기 화면(menu-4) — 게임에 있는 챕터를 모두 늘어놓고 고르면 그 챕터의 모세스 화면을 연다.
/// </summary>
/// <remarks>
/// 원본에는 챕터를 고르는 화면이 없다(스토리가 챕터를 정한다). 이 데모는 <c>Chp\NNNN.chp</c> 를 모두 읽어
/// 제목(TXR 2284~2313 이 이야기 순서)·항성계·행성·장소 수를 보여 주고, 고르면 [[분석-모세스]] 의 항행 페이지를 그 챕터 자료로 연다.
/// 파일 끝이 안 맞는 챕터(0002·0009·0024·0037)는 배치가 다른 것이라 건너뛴다.
/// 행성 구체 그림(Obs 0606~0635)은 챕터마다 1MB 가까이 되어 <b>0010 챕터 것만</b> 넣었다 — 나머지 챕터는 구체 없이 장소 칸만 나온다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    private const int ChaptersW = 900, ChaptersRowH = 22, ChaptersTop = 52, ChaptersCols = 2;

    private bool _chaptersOpen;
    private List<(int Id, string Title, MosesChapterFile Chp)>? _chapters;
    private int _chaptersHover = -1;

    /// <summary>assets/moses/chp 를 모두 읽어 이야기 순서(제목 TXR)로 늘어놓는다.</summary>
    private List<(int Id, string Title, MosesChapterFile Chp)> Chapters()
    {
        if (_chapters != null) return _chapters;
        var list = new List<(int, string, MosesChapterFile)>();
        string folder = Path.Combine(AssetsFolder.Find("moses"), "chp");
        if (Directory.Exists(folder))
            foreach (string path in Directory.EnumerateFiles(folder, "*.chp"))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(path), out int id)) continue;
                if (MosesChapterFile.Parse(File.ReadAllBytes(path)) is not { } chp) continue;
                chp.Id = id;
                list.Add((id, Text(chp.TitleText), chp));
            }
        // 제목 TXR 2284~2313 이 이야기 순서다. 그 밖(외전·시험용)은 뒤로.
        int Rank((int Id, string Title, MosesChapterFile Chp) c) => c.Chp.TitleText is >= 2284 and <= 2313 ? c.Chp.TitleText : 9999;
        return _chapters = [.. list.OrderBy(Rank).ThenBy(c => c.Item1)];
    }

    private (int X, int Y, int H) ChaptersPanel()
    {
        int rows = (Chapters().Count + ChaptersCols - 1) / ChaptersCols;
        int h = ChaptersTop + rows * ChaptersRowH + 30;
        return ((BoardWidth - ChaptersW) / 2, _camY + (ViewHeight - h) / 2, h);
    }

    private int ChapterAt(int bx, int by)
    {
        var (x, y, _) = ChaptersPanel();
        int rows = (Chapters().Count + ChaptersCols - 1) / ChaptersCols, colW = (ChaptersW - 24) / ChaptersCols;
        int col = (bx - x - 12) / colW, row = (by - y - ChaptersTop) / ChaptersRowH;
        if (col < 0 || col >= ChaptersCols || row < 0 || row >= rows) return -1;
        int index = col * rows + row;
        return index < Chapters().Count ? index : -1;
    }

    private bool OnChaptersClick(int bx, int by)
    {
        if (!_chaptersOpen) return false;
        int index = ChapterAt(bx, by);
        if (index < 0) { _chaptersOpen = false; return true; }
        _chaptersOpen = false;
        Play(66);
        OpenMoses(Chapters()[index].Chp);
        return true;
    }

    private void UpdateChaptersHover(int bx, int by)
    {
        if (_chaptersOpen) _chaptersHover = ChapterAt(bx, by);
    }

    private void DrawChapters()
    {
        if (!_chaptersOpen) return;
        var (x, y, h) = ChaptersPanel();
        var list = Chapters();
        int rows = (list.Count + ChaptersCols - 1) / ChaptersCols, colW = (ChaptersW - 24) / ChaptersCols;

        FillRect(0, _camY, BoardWidth, ViewHeight, 0xC0000000);
        FillRect(x, y, ChaptersW, h, PanelBg);
        StrokeRect(x, y, ChaptersW, h, BoxLine);
        DrawText("챕터 고르기", x + 12, y + 14, 0xFFFFE8A0, 17);
        DrawText("고르면 그 챕터의 모세스 항행 화면이 열립니다. Esc 로 닫습니다.", x + 150, y + 18, DimGray);

        for (int i = 0; i < list.Count; i++)
        {
            var (id, title, chp) = list[i];
            int rx = x + 12 + i / rows * colW, ry = y + ChaptersTop + i % rows * ChaptersRowH;
            if (i == _chaptersHover) FillRect(rx, ry - 2, colW - 8, ChaptersRowH, 0x4060A0FF);
            DrawText($"Chp {id:D4}", rx, ry, DimGray);
            // 제목 칸이 아닌 챕터(0001 등)는 엉뚱한 긴 글이 들어 있어 칸 너비만큼만 보인다.
            string label = title.Length > 0 ? title : "(제목 없음)";
            DrawText(label.Length > 18 ? label[..18] + "…" : label, rx + 70, ry, White);
            DrawText($"행성 {chp.Planets.Count} · 장소 {chp.Places.Count}", rx + colW - 130, ry, DimGray);
        }
    }
}

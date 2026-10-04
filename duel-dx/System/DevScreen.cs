using System.Diagnostics;
using DuelDx.Native;
using WarOfGenesis.Assets;

namespace DuelDx;

using static DuelDx.GameWindow;

/// <summary>
/// 개발 창 — 메뉴 막대의 「개발」을 누르면 모드 창처럼 뜬다(사용자 요청). 전에 펼침 메뉴에 있던 것(어빌리티 반영 · 챕터 고르기 · 편집기 열기)과 도구 메뉴의 「적 정리」를
/// 단추로 두고, 게임이 쓰는 폴더들(따로 받는 에셋 · 세이브와 설정 · 실린 assets · 받아 둔 편집기)을 보인다. 원본에 없는 도구다.
/// </summary>
internal sealed unsafe class DevScreen(GameWindow host)
{
    internal const int MenuDev = 1162;
    internal const int W = 600, H = 400, RowY = 40, RowH = 32, ButtonW = 150, FolderY = 186, FolderH = 42, OpenW = 80;

    /// <summary>DUELDX_DEV 가 있으면 열어 둔 채 시작한다(화면 밖 시험용).</summary>
    internal bool _open = Environment.GetEnvironmentVariable("DUELDX_DEV") != null;

    /// <summary>단추 줄 — 누르면 그 메뉴 명령을 그대로 돌린다.</summary>
    internal (string Label, string Note, int Command, bool Close)[] Buttons() =>
    [
        ("어빌리티 반영", "편집기에서 고친 스킬 자료를 다시 읽는다", MenuReloadSkills, false),
        ("챕터 고르기…", "챕터를 골라 그 자리에서 연다", MenuChapters, true),
        ("편집기 열기…", "없으면 받아서 연다(약 60MB)", MenuEditor, false),
        ("적 정리", "지금 전투의 적을 모두 쓰러뜨린다(시험용)", MenuClearEnemies, true),
    ];

    /// <summary>폴더 줄 — 이름, 자리, 덧붙일 말.</summary>
    internal (string Label, string Path, string Note)[] Folders()
    {
        string assets;
        try { assets = Path.GetDirectoryName(AssetsFolder.Find("data")) ?? ""; }
        catch (DirectoryNotFoundException) { assets = ""; }
        string editor = Path.Combine(UserDataFolder.Path, "editor");
        return
        [
            ("받는 에셋", AssetPack.Folder, PackNote()),
            ("세이브·설정", UserDataFolder.Path, ""),
            ("실린 에셋", assets, ""),
            ("받은 편집기", editor, Directory.Exists(editor) ? "" : "아직 안 받음"),
        ];
    }

    private (double At, string Text) _packNote = (double.MinValue, "");

    /// <summary>받은 파일 수·크기와 받기 상태 — 폴더를 훑는 일이라 2초에 한 번만 다시 센다.</summary>
    internal string PackNote()
    {
        if (host._lastTime - _packNote.At < 2 && host._lastTime >= _packNote.At) return _packNote.Text;
        string text;
        try
        {
            var files = Directory.Exists(AssetPack.Folder) ? new DirectoryInfo(AssetPack.Folder).GetFiles("*", SearchOption.AllDirectories) : [];
            text = files.Length == 0 ? "받은 파일 없음" : $"파일 {files.Length}개 · {files.Sum(f => f.Length) / 1048576.0:0}MB";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { text = "폴더를 못 읽음"; }
        if (AssetPack.Status.Length > 0) text += $" · {AssetPack.Status}";
        _packNote = (host._lastTime, text);
        return text;
    }

    internal (int X, int Y) Origin() => (host._camX + (host.ViewWidth - W) / 2, host._camY + (host.ViewHeight - H) / 2);

    internal bool MouseIn(int x, int y, int w, int h) => host._mouse.X >= x && host._mouse.X < x + w && host._mouse.Y >= y && host._mouse.Y < y + h;

    /// <summary>열려 있으면 클릭을 먹는다.</summary>
    internal bool OnClick(int bx, int by)
    {
        if (!_open) return false;
        var (x, y) = Origin();
        if (bx >= x + W - 116 && bx < x + W - 16 && by >= y + H - 40 && by < y + H - 12) { _open = false; return true; }
        var buttons = Buttons();
        for (int i = 0; i < buttons.Length; i++)
        {
            int ry = y + RowY + i * RowH;
            if (bx < x + 16 || bx >= x + 16 + ButtonW || by < ry || by >= ry + RowH - 8) continue;
            if (buttons[i].Close) _open = false;
            host.OnMenuCommand(buttons[i].Command);
            return true;
        }
        var folders = Folders();
        for (int i = 0; i < folders.Length; i++)
        {
            int ry = y + FolderY + i * FolderH;
            if (bx < x + W - 16 - OpenW || bx >= x + W - 16 || by < ry || by >= ry + 24) continue;
            OpenFolder(folders[i].Path);
            return true;
        }
        return true;
    }

    /// <summary>탐색기로 그 폴더를 연다 — 없으면 만들지 않고 알린다(받는 폴더는 받을 것이 생길 때 생긴다).</summary>
    internal void OpenFolder(string path)
    {
        if (path.Length == 0 || !Directory.Exists(path)) { host.Toast("아직 없는 폴더입니다"); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = false }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { host.Toast($"폴더를 못 열었습니다: {ex.Message}"); }
    }

    internal void OnKey(int key)
    {
        if (key == Win32.VK_ESCAPE) _open = false;
    }

    internal void Draw()
    {
        if (!_open) return;
        var (x, y) = Origin();
        host.FillRect(x - 4, y - 4, W + 8, H + 8, 0x80000000);
        host.FillRect(x, y, W, H, StatusScreen.PanelBg);
        host.StrokeRect(x, y, W, H, StatusScreen.BoxLine);
        host.FillRect(x, y, W, 28, StatusScreen.HeadBg);
        host.DrawText("개발 — 만들 때 쓰는 것", x + 10, y + 6, White);

        var buttons = Buttons();
        for (int i = 0; i < buttons.Length; i++)
        {
            int ry = y + RowY + i * RowH;
            bool here = MouseIn(x + 16, ry, ButtonW, RowH - 8);
            host.FillRect(x + 16, ry, ButtonW, RowH - 8, here ? 0xFF2A4A8A : StatusScreen.BoxBg);
            host.StrokeRect(x + 16, ry, ButtonW, RowH - 8, StatusScreen.BoxLine);
            host.DrawText(buttons[i].Label, x + 16 + (ButtonW - host.GetText(buttons[i].Label, White).W) / 2, ry + 4, White);
            host.DrawText(buttons[i].Note, x + 16 + ButtonW + 14, ry + 6, DimGray, 12);
        }

        host.FillRect(x + 12, y + FolderY - 12, W - 24, 1, StatusScreen.BoxLine);
        var folders = Folders();
        for (int i = 0; i < folders.Length; i++)
        {
            int ry = y + FolderY + i * FolderH;
            host.DrawText(folders[i].Label, x + 16, ry + 4, White);
            if (folders[i].Note.Length > 0) host.DrawText(folders[i].Note, x + 110, ry + 5, 0xFFFFE070, 12);
            host.DrawText(Shorten(folders[i].Path.Length > 0 ? folders[i].Path : "(못 찾음)", W - 32), x + 16, ry + 24, DimGray, 12);
            bool here = MouseIn(x + W - 16 - OpenW, ry, OpenW, 24);
            host.FillRect(x + W - 16 - OpenW, ry, OpenW, 24, here ? 0xFF2A4A8A : StatusScreen.BoxBg);
            host.StrokeRect(x + W - 16 - OpenW, ry, OpenW, 24, StatusScreen.BoxLine);
            host.DrawText("폴더 열기", x + W - 16 - OpenW + (OpenW - host.GetText("폴더 열기", White, 12).W) / 2, ry + 5, White, 12);
        }

        int fy = y + H - 40;
        host.FillRect(x + W - 116, fy, 100, 28, StatusScreen.HeadBg);
        host.DrawText("닫기", x + W - 80, fy + 6, White);
        host.DrawText("Esc: 닫기", x + 16, fy + 7, DimGray);
    }

    /// <summary>긴 경로는 가운데를 줄여 창 너비에 맞춘다.</summary>
    internal string Shorten(string path, int width)
    {
        if (host.GetText(path, White, 12).W <= width) return path;
        for (int keep = path.Length - 4; keep > 12; keep -= 2)
        {
            string cut = path[..(keep / 2)] + "…" + path[^(keep / 2)..];
            if (host.GetText(cut, White, 12).W <= width) return cut;
        }
        return path[..12] + "…";
    }
}

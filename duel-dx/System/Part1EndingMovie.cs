using System.Diagnostics;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 파트1 엔딩 영상 — 챕터 행동 705(<see cref="Part1ChapterScreen"/>)가 트는 <c>Setup\_sys2.cab</c>. 원본(0x100d5500)은 이 이름을 그대로 DirectShow 에 넘긴다 —
/// 오프닝 <c>G3.pak</c> 와 같은 길이라, 이름만 cab 이고 속은 오프닝처럼 MPEG-1 시스템 스트림(<c>00 00 01 BA</c>)일 것이다(DirectShow 는 확장자가 아니라 속을 보고 연다).
/// 이 파일은 설치 폴더에 없으면 시디에만 있어 흔한 파트1 폴더에는 없다.
/// </summary>
/// <remarks>
/// <para>찾는 곳: 파트1 폴더의 <c>Setup\_sys2.cab</c> · <c>_sys2.cab</c>, 그리고 사용자가 미리 풀어 두었을 <c>_sys2.*</c> · <c>ending.*</c>(mpg · mpeg · avi · dat).</para>
/// <para>속을 본다 — MPEG(<c>00 00 01 BA/B3</c>) · AVI(<c>RIFF</c>)면 그대로, 진짜 캐비닛(<c>MSCF</c>)이면 Windows <c>expand.exe</c> 로 받은 자료 폴더에 꺼내
/// 그 안의 가장 큰 영상을 쓴다. 속에 맞는 확장자(<c>.mpg</c> · <c>.avi</c>)로 받은 자료 폴더에 베껴 둔 것을 DirectShow 로 튼다(<see cref="DirectShowVideo"/>).</para>
/// <para>못 찾으면 <see cref="Find"/> 가 null — 부르는 쪽이 「엔딩 영상 없음」을 띄운다. 찾았는데 못 틀면(꼴이 다르거나 풀개가 없음) 「엔딩 영상을 틀 수 없습니다」를 띄우고 지나간다.</para>
/// </remarks>
internal static class Part1EndingMovie
{
    /// <summary>시험 문 — 이 파일을 엔딩 영상으로 친다(오프닝 <c>G3.pak</c> 로 재생 길을 본다).</summary>
    private static string? TestPath => Environment.GetEnvironmentVariable("DUELDX_P1ENDINGFILE") is { Length: > 0 } p ? p : null;

    private static string CacheFolder => UserDataFolder.File(Path.Combine("part1", "ending"));

    /// <summary>엔딩 영상일 법한 파일 — 없으면 null.</summary>
    internal static string? Find()
    {
        if (TestPath is { } test) return test == "none" || !File.Exists(test) ? null : test;
        if (Part1Game.Root is not { } root) return null;
        string[] names = ["_sys2.cab", "_sys2.mpg", "_sys2.mpeg", "_sys2.avi", "_sys2.dat", "ending.mpg", "ending.mpeg", "ending.avi", "ending.dat"];
        foreach (string folder in new[] { Path.Combine(root, "Setup"), root })
            foreach (string name in names)
            {
                string path = Path.Combine(folder, name);
                try { if (File.Exists(path) && new FileInfo(path).Length > 0) return path; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        return null;
    }

    /// <summary>
    /// 모드 &gt; 엔딩 영상 탭의 글 — 있으면 「있음」과 자리 · 꼴, 없으면 「없음」과 찾는 곳 · 둘 파일. 빛깔 0 은 흐린 글.
    /// </summary>
    internal static (string Text, uint Color)[] Status()
    {
        const uint Yes = 0xFF80FF80, No = 0xFFFF9070, Plain = 0xFFF2EAD6;
        if (Find() is { } file)
        {
            string kind = Sniff(file) switch { Kind.Mpeg => "MPEG 영상", Kind.Avi => "AVI 영상", Kind.Cab => "캐비닛 — 틀 때 꺼낸다", _ => "알 수 없는 꼴 — 못 틀 수 있다" };
            return
            [
                ("파트1 엔딩 영상: 있음", Yes),
                (Shorten(file), Plain),
                ($"꼴: {kind}", 0),
                ("파트1 마지막 챕터가 끝나면 이 영상을 튼다(클릭 · Esc 로 넘김)", 0),
            ];
        }
        string root = Part1Game.Root is { } known ? Shorten(known) : "(파트1 폴더를 아직 모른다 — 파트2 타이틀의 PART 1 에서 고른다)";
        return
        [
            ("파트1 엔딩 영상: 없음", No),
            ($"파트1 폴더: {root}", Plain),
            (@"찾는 곳: 파트1 폴더\Setup\_sys2.cab (원본 시디의 Setup\_sys2.cab 을 베껴 둔다)", 0),
            (@"또는 파트1 폴더나 그 Setup 에 _sys2.mpg · ending.mpg · ending.avi", 0),
            ("없으면 마지막 챕터가 끝날 때 「엔딩 영상 없음」을 띄우고 타이틀로 간다", 0),
        ];
    }

    /// <summary>긴 자리는 앞을 「…」로 줄인다 — 창 너비(600)를 넘지 않게.</summary>
    private static string Shorten(string path) => path.Length <= 64 ? path : "…" + path[^63..];

    private enum Kind { None, Mpeg, Avi, Cab }

    private static Kind Sniff(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            Span<byte> h = stackalloc byte[4];
            if (f.Read(h) < 4) return Kind.None;
            if (h[0] == 0 && h[1] == 0 && h[2] == 1 && h[3] is 0xBA or 0xB3) return Kind.Mpeg;
            if (h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F') return Kind.Avi;
            if (h[0] == 'M' && h[1] == 'S' && h[2] == 'C' && h[3] == 'F') return Kind.Cab;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Kind.None;
    }

    /// <summary>
    /// 그 파일을 틀 수 있는 자리로 — 캐비닛은 꺼내고, 속에 맞는 확장자(<c>.mpg</c> · <c>.avi</c>)로 받은 자료 폴더에 베껴 둔다
    /// (DirectShow 가 <c>.cab</c> 이름을 보고 다른 필터를 고르지 않게). 뒤 스레드에서 부른다. 못 틀 꼴이면 null.
    /// </summary>
    internal static string? Prepare(string source)
    {
        var kind = Sniff(source);
        if (kind == Kind.Cab)
        {
            if (Extract(source) is not { } inner) return null;
            source = inner;
            kind = Sniff(source);
        }
        if (kind is not (Kind.Mpeg or Kind.Avi)) return null;
        string playable = Path.Combine(CacheFolder, "ending" + (kind == Kind.Mpeg ? ".mpg" : ".avi"));
        Directory.CreateDirectory(CacheFolder);
        if (!File.Exists(playable) || new FileInfo(playable).Length != new FileInfo(source).Length) File.Copy(source, playable, overwrite: true);
        return playable;
    }

    /// <summary>캐비닛을 받은 자료 폴더에 꺼낸다(Windows <c>expand.exe -F:*</c>) — 꺼낸 것 가운데 가장 큰 MPEG · AVI. 없거나 못 꺼내면 null.</summary>
    private static string? Extract(string cab)
    {
        string dir = Path.Combine(CacheFolder, "cab");
        Directory.CreateDirectory(dir);
        string? Best() => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => Sniff(f) is Kind.Mpeg or Kind.Avi).OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        if (Best() is { } done) return done;
        string expand = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "expand.exe");
        if (!File.Exists(expand)) return null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(expand) { ArgumentList = { "-F:*", cab, dir }, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
            if (p == null) return null;
            p.StandardOutput.ReadToEndAsync();
            p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(120_000)) { try { p.Kill(); } catch (InvalidOperationException) { } return null; }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
        return Best();
    }
}

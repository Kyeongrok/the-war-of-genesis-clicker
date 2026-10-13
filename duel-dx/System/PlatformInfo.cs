using System.Runtime.InteropServices;
using DuelDx.Native;
using Microsoft.Win32;

namespace DuelDx;

/// <summary>
/// 게임이 도는 기기의 갈래 — 전투 통계(<see cref="BattleStats"/>)에 실어 데스크톱 · 노트북 · 휴대용(UMPC) · 리눅스(Wine)로 얼마나 하는지 본다(사용자 요청 menu-61).
/// </summary>
/// <remarks>
/// 기종 이름은 <b>보내지 않는다</b> — 여기서 갈래 하나로 줄인다(설치 번호와 묶이면 사람을 가리게 된다).
/// <list type="bullet">
/// <item><c>deck</c> — 스팀 덱(Proton 이 주는 <c>SteamDeck=1</c>, 또는 BIOS 기종 Jupiter · Galileo).</item>
/// <item><c>umpc</c> — BIOS 제조사 · 기종 이름이 알려진 휴대용 PC(ROG Ally · Legion Go · GPD · AYANEO …)와 맞는다. 목록에 없는 기종은 노트북으로 잡힌다.</item>
/// <item><c>wine</c> — Wine 위(리눅스 · 맥 · 안드로이드의 Winlator 따위). 폰인지는 여기서 모른다 — 함께 보내는 터치 여부 · 모니터 크기로 짐작한다.</item>
/// <item><c>laptop</c> · <c>desktop</c> — 배터리가 있나 없나.</item>
/// </list>
/// 폰으로 PC 화면을 받아 하는 것(스트리밍)은 게임이 PC 에서 돌아 알 수 없다.
/// </remarks>
internal static class PlatformInfo
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerStatus { public byte AcLine, BatteryFlag, BatteryPercent, SystemStatus; public int LifeTime, FullLifeTime; }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out PowerStatus status);

    private const int SM_MAXIMUMTOUCHES = 95;

    /// <summary>알려진 휴대용 PC — BIOS 의 제조사 · 기종 · 판 · 제품군 글 어디에든 들어 있으면 맞는 것으로 친다(대소문자 안 가림).</summary>
    private static readonly string[] Handhelds =
    [
        "ROG Ally", "RC71L", "RC72L", "ROG Xbox Ally", "Legion Go", "GPD", "AYANEO", "AYA NEO", "AOKZOE", "ONEXPLAYER", "ONE-NETBOOK",
        "Claw A1M", "Claw 7", "Claw 8", "Loki", "ZOTAC GAMING ZONE", "Win600", "Steam Deck", "Jupiter", "Galileo",
    ];

    private static (string Kind, bool Touch, string Screen)? _cached;

    /// <summary>기기 갈래, 터치 화면이 있나, 모니터 크기(「가로x세로」) — 켠 동안 바뀌지 않는 것으로 보고 한 번만 알아낸다.</summary>
    internal static (string Kind, bool Touch, string Screen) Current => _cached ??= Detect();

    private static (string Kind, bool Touch, string Screen) Detect()
    {
        bool touch = Win32.GetSystemMetrics(SM_MAXIMUMTOUCHES) > 0;
        string screen = $"{Win32.GetSystemMetrics(Win32.SM_CXSCREEN)}x{Win32.GetSystemMetrics(Win32.SM_CYSCREEN)}";
        return (Kind(), touch, screen);
    }

    private static string Kind()
    {
        string bios = Bios();
        bool wine = false;
        try { wine = GetProcAddress(GetModuleHandleW("ntdll.dll"), "wine_get_version") != IntPtr.Zero; }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
        bool deckModel = bios.Contains("Jupiter", StringComparison.OrdinalIgnoreCase) || bios.Contains("Galileo", StringComparison.OrdinalIgnoreCase)
                         || bios.Contains("Steam Deck", StringComparison.OrdinalIgnoreCase);
        if (Environment.GetEnvironmentVariable("SteamDeck") == "1" || deckModel) return "deck";
        if (Handhelds.Any(h => bios.Contains(h, StringComparison.OrdinalIgnoreCase))) return "umpc";
        if (wine) return "wine";
        // BatteryFlag: 128 = 배터리 없음, 255 = 모름.
        return GetSystemPowerStatus(out var power) && power.BatteryFlag is not (128 or 255) ? "laptop" : "desktop";
    }

    /// <summary>BIOS 가 적어 둔 제조사 · 기종 글을 한 줄로 — 못 읽으면 빈 글.</summary>
    private static string Bios()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (key == null) return "";
            return string.Join(' ', new[] { "SystemManufacturer", "SystemProductName", "SystemVersion", "SystemFamily", "BaseBoardProduct" }
                .Select(name => key.GetValue(name) as string ?? ""));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return ""; }
    }
}

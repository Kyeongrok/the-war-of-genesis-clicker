using System.IO;
using System.Text.Json;

namespace DuelDx;

/// <summary>
/// 타워 디펜스 기록 — 본편 세이브 슬롯과 따로 <c>towerdefense.json</c> 한 파일(사용자 요청 td-1: 「세이브는 타워디펜스는 따로」).
/// 맵(파일 이름)마다 가장 멀리 간 웨이브와 깬 횟수.
/// </summary>
internal sealed class TowerDefenseSave
{
    public Dictionary<string, int> BestWave { get; set; } = [];
    public Dictionary<string, int> Clears { get; set; } = [];

    private static string FilePath => UserDataFolder.File("towerdefense.json");

    internal static TowerDefenseSave Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<TowerDefenseSave>(File.ReadAllText(FilePath)) ?? new() : new(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    internal void Write()
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder.Path);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

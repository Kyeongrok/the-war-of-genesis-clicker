using System.Text.Json;

namespace DuelDx;

/// <summary>
/// 메뉴에서 켜고 끄는 것들 — 다음에 켤 때도 그대로 남게 <c>%APPDATA%\DuelDx\settings.json</c> 에 적는다.
/// 모드(동맹을 AI 가 움직임)·격자·체력바.
/// </summary>
internal sealed record UserSettings(bool AllyAi = true, bool ShowGrid = false, bool ShowGauges = false)
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DuelDx", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>켤 때 한 번 읽는다 — 파일이 없거나 깨졌으면 기본값.</summary>
    public static UserSettings Current { get; private set; } = Load();

    private static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath), Json) is { } loaded)
                return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        return new UserSettings();
    }

    /// <summary>바뀐 값을 바로 적는다. 못 적어도 게임은 그대로 간다.</summary>
    public static void Save(UserSettings settings)
    {
        Current = settings;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

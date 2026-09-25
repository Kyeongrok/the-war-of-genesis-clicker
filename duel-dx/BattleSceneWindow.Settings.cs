using System.Text.Json;

namespace DuelDx;

/// <summary>
/// 메뉴에서 켜고 끄는 것들 — 다음에 켤 때도 그대로 남게 <c>%APPDATA%\DuelDx\settings.json</c> 에 적는다.
/// 모드(동맹을 AI 가 움직임)·격자·체력바.
/// </summary>
/// <param name="ZoomPercent">화면 배율 % — 0 이면 자동(모니터에 맞춤, 최대 200%). 100·150·200·300·400 을 고를 수 있다.</param>
/// <param name="ViewW">창 너비(화면 픽셀) — 원본 640. 배율 자동이면 모세스·타이틀은 이 크기를 채우고, 큰 맵은 이만큼 보인다.</param>
/// <param name="ViewH">창 높이(화면 픽셀, 머리줄 제외) — 원본 480.</param>
/// <param name="ShowHints">조작 안내 글(「…을(를) 노립니다 — 클릭·Enter…」 따위)을 보일지. 설정 > 안내 글 켜기·끄기.</param>
/// <param name="ShowLevelUp">레벨업 창을 띄울지. 꺼도 <b>레벨은 그대로 오른다</b> — 창만 안 뜬다(사용자 요청).</param>
/// <param name="ShowStatusBar">화면 맨 위 상태 줄(전투 이름·아군/적군 수·차례 인물 HP/TP/SOUL·조작 안내)을 보일지. 원본에 없는 데모 줄이라 기본은 끔(사용자 요청).</param>
/// <param name="GameSpeed">게임 속도 % — 100 이 원본(33ms 타이머 = 30틱/초, <c>0x100081f0</c>). 150·200 이면 모션·걷기·이펙트가 그만큼 빨리 돈다(사용자 요청).</param>
/// <param name="TalkPauseSeconds">대사와 대사 사이의 멈춤(행동 2, 보통 1초 — 클릭 뒤 배경만 보이는 틈)을 몇 초로 줄일지. 음수면 원본대로 스크립트 값.
/// 스크립트 값보다 길게는 안 늘린다. 기본 0.1초(사용자 요청).</param>
/// <param name="ShowSceneTag">화면 왼쪽 아래에 지금 장면 번호(Fld·Btl·Chp, 사건·줄)를 보일지 — 대화할 때 짚기 쉽게(사용자 요청, 기본 켬).</param>
/// <param name="KeepExpOnJobChange">전직(세부 체질 바꾸기)할 때 남은 EXP 를 그대로 둘지. 원본은 0 으로 비운다 — 켜면 원본과 다르다(사용자 요청).</param>
internal sealed record UserSettings(bool AllyAi = true, bool ShowGrid = false, bool ShowGauges = false, int ZoomPercent = 0,
                                    int ViewW = 1280, int ViewH = 960, bool ShowHints = true, bool ShowLevelUp = true,
                                    bool KeepExpOnJobChange = false, bool ShowStatusBar = false, int GameSpeed = 100,
                                    double TalkPauseSeconds = 0.1, bool ShowSceneTag = true, bool ShowChestContents = false)
{
    private static string FilePath => UserDataFolder.File("settings.json");

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

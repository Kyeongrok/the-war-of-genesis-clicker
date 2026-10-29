namespace DuelDx;

/// <summary>
/// 파트1 세이브 연동 이스터에그를 모드 창에서 켜는 것(사용자 요청 menu-113: 「파일 넣는 거 말고 모드에서 켜면 발생하게」).
/// <para>
/// 원본: NEW GAME(<c>0x1004d870</c> 끝 <c>0x1004d908~</c>)이 레지스트리 <c>HKLM\SOFTWARE\Softmax\창세기전3</c> 의 <c>Directory</c> 에서
/// 파트1 세이브 <c>G3%04d.sav</c> 1~8 번을 열어 인물 106명 · 파티 16개 가방의 아이템 <b>0x87 · 0x88 · 0x89</b>(135 · 136 · 137) 개수를 세어
/// 진행 깃발 <b>185 · 186 · 187</b> 에 <c>max(지금, 개수)</c> 로 넣는다(<c>0x1004b085~0x1004b09e</c>, 분석 노트 원본차이/ba16-flow F11).
/// 그 깃발을 보는 곳은 챕터 스크립트 셋뿐이다(분석-모세스 메일 표 95 · 96):
/// </para>
/// <list type="bullet">
/// <item>깃발 185 ≥ 5 → Chp 0011 샤이닝 스타 사건 28 이 깃발 305 = 1 → 메일 95 「UNKNOWN : 배달」(카오스 큐브 판매). 읽으면 사건 29 가
///   705[0, 30000] · 깃발 306 = 1 → Chp 0061 블랙 레이븐 사건 24(깃발 306 == 1 · 행성 11 다리엘 방문 505[11]) → 801[0, 563] 나야트레이 합류.</item>
/// <item>깃발 186 ≥ 5 → Chp 0050 의혹 사건 10 이 깃발 307 = 1 → 파치나 장소 28 「알 수 없는 지도의 동굴」(조건 깃발 307 == 1, 전투 328)이 열린다.</item>
/// <item>깃발 187 ≥ 5 → Chp 0050 의혹 사건 11 이 깃발 308 = 1 → 메일 96 「UNKNOWN : 빅 도그마」. 읽으면 사건 12 가 대사 뒤 703[1, 131, 1](아이템 131 건슬라이서 맘모스).</item>
/// </list>
/// <para>
/// 그래서 리메이크는 305~308 을 직접 세우지 않고 원본이 세이브에서 넣는 <b>185 · 186 · 187</b> 만 5 로 올린다 — 그 뒤(때 · 조건 · 메일 · 합류)는 원본 챕터 스크립트가 그대로 한다.
/// 원본은 NEW GAME 때 한 번이지만, 이미 진행 중인 세이브에서 켜도 되게 모세스를 열 때마다(챕터 스크립트가 돌기 전) 올린다.
/// 세 깃발을 읽는 것이 위 사건 셋(모두 한 번짜리)뿐이라 다시 올려도 결과는 같다. 끈다고 내리지는 않는다(원본도 max 로만 넣는다; NEW GAME 이 비운다).
/// 나야트레이는 깃발 306 을 메일 95 를 읽은 사건 29 가 세우므로 카오스 큐브와 한 체크로 묶었다 — 샤이닝 스타(0011)를 지난 뒤에 켜면 나야트레이는 안 온다(원본과 같음).
/// 135 · 136 · 137 이 파트1 의 카오스 큐브 · 알 수 없는 지도 · 알 수 없는 씨앗이라는 짝은 깃발 → 메일 내용으로 맞춘 것(파트1 아이템 이름은 대조하지 않음, 가설).
/// </para>
/// </summary>
internal static class MosesEasterEggs
{
    /// <summary>챕터 스크립트 조건(101[깃발, 5, 5] = 깃발 ≥ 5)을 넘는 값 — 원본은 파트1 가방의 그 아이템 개수.</summary>
    internal const int EggCount = 5;

    internal const int FlagChaosCube = 185, FlagUnknownMap = 186, FlagUnknownSeed = 187;

    /// <summary>켠 이스터에그의 깃발을 <see cref="EggCount"/> 이상으로 올린다 — <see cref="MosesScene.OpenMoses"/> 머리에서 부른다.</summary>
    internal static void Apply(GameWindow host)
    {
        var flags = host.FlagSt._flags;
        void Raise(int flag)
        {
            if (flag < flags.Length && flags[flag] < EggCount) flags[flag] = EggCount;
        }
        if (host._eggChaosCube) Raise(FlagChaosCube);
        if (host._eggUnknownMap) Raise(FlagUnknownMap);
        if (host._eggUnknownSeed) Raise(FlagUnknownSeed);
    }
}

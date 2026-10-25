using System.IO;
using System.Text.Json;
using WarOfGenesis.Assets;

namespace DuelDx;

internal sealed partial class Part1Skill
{
    /// <summary>
    /// 파트1 에셋 팩(<c>tools/part1-pack</c>)에 실을 파트1 번호 — 모든 파트1 기술 work(손으로 쓴 것 · Attack 0~999 로 만든 것)의 <see cref="NeedsFor"/>,
    /// 기본 공격의 그림 · 소리, 기술 핸들러의 소리 표, 타격 불꽃 · 쓰러짐 소리 · 상태 표지 · 아이콘(Obs 11 어빌리티 · 12 아이템) · 지도/본부/가게/교환 창 그림.
    /// 기술 핸들러가 직접 부르는 <c>Sound(N, …)</c> · <c>Put(N, …)</c> 같은 글자 그대로의 번호는 팩 도구가 소스를 훑어 더한다.
    /// </summary>
    /// <remarks>
    /// <c>DUELDX_P1NEEDS=&lt;파일&gt;</c> 로 켜면 이 목록을 JSON(<c>{"obs":[…],"sounds":[…]}</c>, 파트1 번호)으로 쓰고 바로 끝낸다 —
    /// <c>tools/part1-pack</c> 이 <c>tools/part1_needs.json</c> 으로 읽는다. 파트1 폴더는 안 본다(번호 표는 모두 코드에 있다).
    /// </remarks>
    internal static (SortedSet<int> Obs, SortedSet<int> Sounds) PackList()
    {
        var obs = new SortedSet<int>();
        var sounds = new SortedSet<int>();
        IEnumerable<int> works = Works.Concat(Enumerable.Range(0, Part1SkillBook.WorkStride).Select(a => Part1Game.ChrBase + a));
        foreach (int work in works.Where(Handles)) obs.UnionWith(NeedsFor(work));
        for (int a = 0; a < Part1SkillBook.WorkStride; a++) obs.UnionWith(BasicNeeds(a));
        obs.UnionWith(Part1Game.SkillObs);
        obs.UnionWith([Part1Game.HitSparkObs, 11, 12, 180, 181]);
        // 파트1 판의 지도 · 본부 · 교환 · 가게 창(Part1MapArt · Part1ChapterScreen · Part1HqStatusWindow · Part1ExchangeWindow · Part1TownWindow · Part1FieldScreen).
        obs.UnionWith([111, Part1ChapterScreen.BannerObs, Part1ChapterScreen.TeamObs, Part1ChapterScreen.RingObs, 217, 516, 613, 678, 74]);
        obs.Add(677);   // 전투 승리 · 패배 조건 창(Part1ConditionWindow)의 깃발 · 해골
        sounds.UnionWith(Part1Game.SkillSounds);
        sounds.UnionWith(BasicSounds);
        sounds.UnionWith([Part1Game.DeathSnd, 0x34]);   // 쓰러짐 · 가게 사고팔기(Part1TownWindow)
        obs.RemoveWhere(n => n <= 0);
        sounds.RemoveWhere(n => n <= 0);
        return (obs, sounds);
    }

    /// <summary>DUELDX_P1NEEDS 가 있으면 <see cref="PackList"/> 를 그 파일에 쓰고 true(게임은 켜지 않는다).</summary>
    internal static bool WritePackListIfAsked()
    {
        if (Environment.GetEnvironmentVariable("DUELDX_P1NEEDS") is not { Length: > 0 } file) return false;
        var (obs, sounds) = PackList();
        File.WriteAllText(file, JsonSerializer.Serialize(new { obs, sounds }));
        return true;
    }
}

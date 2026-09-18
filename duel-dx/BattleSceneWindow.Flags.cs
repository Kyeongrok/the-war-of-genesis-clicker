using System.IO;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 진행 깃발 — 「그 장소가 언제 열리는지」를 정하는 바이트 배열 2000칸(전역 <c>0x101b6050</c>).
/// </summary>
/// <remarks>
/// 항성계·행성·장소·인물 레코드의 조건 세 워드는 <b>(깃발 번호, 견줄 값, 연산자)</b> 이고,
/// 항행 화면이 목록을 만들 때마다 <c>0x100fdaf0</c>(장소)·<c>0x100fdc16</c>(행성)·<c>0x100fdcd7</c>(항성계)가 검사한다.
/// 깃발 번호가 <b>0 이하면 조건이 없다</b>. 연산자는 <c>0x100fda40</c> 의 표 그대로 —
/// 0 <c>==</c> · 1 <c>!=</c> · 2 <c>&lt;</c> · 3 <c>&lt;=</c> · 4 <c>&gt;</c> · 5 <c>&gt;=</c>.
/// <para>
/// 같은 배열을 챕터·필드 스크립트(행동 <c>0x100f3050</c> = 대입, <c>0x100f3080</c> = 사칙연산)와
/// 전투 이벤트가 함께 쓴다. 전투를 시작할 때 <c>0x10064800</c> 이 통째로 <c>CBattle+0xa4</c> 로 베끼고,
/// 끝날 때 <c>0x10064920</c> 이 되돌려 베낀다 — 그래서 <b>전투 이벤트가 세운 깃발이 항행 화면에 그대로 남는다</b>.
/// 세이브에도 통째로 들어간다(<c>0x1004d9de</c>, 0x7d0 바이트).
/// </para>
/// 예: 코어헌터 훈련장은 <c>깃발[13] == 1</c> 이어야 열리고, Btl 0045 를 이기면 그 이벤트가 <c>깃발[1] = 1</c> 을 세운다.
/// </remarks>
internal sealed unsafe partial class BattleSceneWindow
{
    /// <summary>진행 깃발 2000칸. 새 게임이면 모두 0 이다.</summary>
    private readonly byte[] _flags = new byte[2000];

    /// <summary>조건 하나를 견준다 — 깃발 번호가 0 이하면 조건이 없는 것으로 친다.</summary>
    private bool FlagAllows(int variable, int value, int op)
    {
        if (variable <= 0 || variable >= _flags.Length) return true;
        int now = _flags[variable];
        return op switch
        {
            0 => now == value,
            1 => now != value,
            2 => now < value,
            3 => now <= value,
            4 => now > value,
            _ => now >= value,
        };
    }

    /// <summary>레코드에 붙은 조건이 모두 통하나.</summary>
    private bool FlagsAllow(IReadOnlyList<(int Variable, int Value, int Operator)> conditions) =>
        conditions.All(c => FlagAllows(c.Variable, c.Value, c.Operator));

    /// <summary>
    /// 그 전투의 이벤트가 세우는 깃발을 적용한다 — 이긴 뒤 다음 화면이 달라지게.
    /// </summary>
    /// <remarks>
    /// 원본은 이벤트 조건이 맞을 때만 그 행동을 돌리지만, 데모는 <b>이긴 전투의 깃발 대입만</b> 훑어 적용한다.
    /// 행동 <c>102</c> 가 진행 깃발 대입(인자 0 = 번호, 인자 1 = 값)이다.
    /// </remarks>
    private void ApplyBattleFlags(int battleId)
    {
        try
        {
            var files = GameFiles.FromFolder(AssetsFolder.Find("data"));
            if (files.Read("Btl", $"{battleId:D4}.btl") is not { } bytes) return;
            if (BattleEvents.Parse(bytes, out _) is not { } events) return;
            foreach (var action in events.SelectMany(e => e.Actions))
                if (action.Code == 102 && action.Args.Length >= 2 && (uint)action.Args[0] < _flags.Length)
                    _flags[action.Args[0]] = (byte)Math.Clamp((int)action.Args[1], 0, 255);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
    }
}

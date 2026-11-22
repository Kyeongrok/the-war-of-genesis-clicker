using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>타워의 무기 갈래(사용자 요청 td-1) — 공격 방식이 갈래마다 다르다.</summary>
internal enum TdWeapon { Sword, Spear, DualBlade, GreatSword, Bow, Gun, Beam, Magic }

/// <summary>영웅 타워의 오라 — 둘레(<see cref="TdData.AuraRange"/>) 타워를 세게 한다. 같은 갈래는 겹치지 않고 가장 센 것만 먹는다.</summary>
internal enum TdAura { None, Power, Haste, Range }

/// <summary>무기 갈래의 바탕 수치 — 사거리 · 간격은 칸(40px) · 초 단위.</summary>
/// <param name="Splash">맞은 자리에서 이 반경(칸) 안의 적도 맞는다(0 = 하나만).</param>
/// <param name="Pierce">창 — 겨눈 적 뒤로 한 줄에 선 적까지 이만큼 더 꿰뚫는다.</param>
/// <param name="Shot">날아가는 탄의 빠르기(칸/초) — 0 이면 근접(치는 순간 맞는다).</param>
internal sealed record TdWeaponStats(string Name, double Range, int Damage, double Interval, double Splash, int Pierce, double Shot, uint ShotColor);

/// <summary>지을 수 있는 타워 하나 — 인물(Chr 번호, 파트1 은 20000+)과 갈래.</summary>
/// <param name="Power">갈래 바탕 피해 · 사거리에 곱한다(영웅은 1 보다 크다).</param>
internal sealed record TdTowerDef(int Chr, string Name, TdWeapon Weapon, int Cost, double Power = 1, bool Hero = false, TdAura Aura = TdAura.None, double AuraValue = 0)
{
    internal TdWeaponStats Stats => TdData.Weapons[Weapon];
}

/// <summary>적 하나 — <paramref name="Fallback"/> 은 파트1 그림이 없는 PC 에서 대신 쓰는 파트2 인물.</summary>
/// <param name="Speed">칸/초.</param>
/// <param name="Leak">끝까지 가면 깎는 생명.</param>
internal sealed record TdEnemyDef(int Chr, int Fallback, string Name, int Hp, double Speed, int Bounty, int Leak = 1, int Armor = 0);

/// <summary>웨이브 한 묶음 — 그 적을 몇 마리, 몇 초 간격으로.</summary>
internal sealed record TdSpawn(int Enemy, int Count, double Every);

/// <summary>맵 — 파트2 전투 맵(Obt) 한 장의 한 네모(640×400)와 적이 지나는 칸들.</summary>
/// <param name="Waypoints">길이 거쳐 가는 화면 칸(640×400 네모 안 40×32 칸, 첫 · 끝은 들어오고 나가는 곳) — 칸 사이는 걸을 수 있는 칸으로 잇는다(<see cref="TowerDefenseScene"/>).</param>
internal sealed record TdMap(string Name, string File, int CamX, int CamY, (int Col, int Row)[] Waypoints, int Bgm);

internal static class TdData
{
    /// <summary>오라가 닿는 반경(칸).</summary>
    internal const double AuraRange = 2.6;

    internal const int StartGold = 250, StartLives = 20;

    /// <summary>웨이브를 앞당겨 부르면 남은 기다림 1초마다 주는 골드.</summary>
    internal const int EarlyBonusPerSecond = 2;

    /// <summary>판 골드 — 타워를 팔면 지은 값의 이만큼을 돌려준다.</summary>
    internal const double SellRate = 0.7;

    internal static readonly Dictionary<TdWeapon, TdWeaponStats> Weapons = new()
    {
        [TdWeapon.Sword] = new("장검", 1.6, 16, 0.80, 0, 0, 0, 0),
        [TdWeapon.Spear] = new("창", 2.1, 13, 0.95, 0, 2, 0, 0),
        [TdWeapon.DualBlade] = new("쌍검", 1.5, 8, 0.40, 0, 0, 0, 0),
        [TdWeapon.GreatSword] = new("대검", 1.6, 30, 1.50, 0.9, 0, 0, 0),
        [TdWeapon.Bow] = new("활", 4.2, 10, 0.70, 0, 0, 11, 0xFFE8D8A0),
        [TdWeapon.Gun] = new("총", 3.6, 24, 1.10, 0, 0, 22, 0xFFFFF070),
        [TdWeapon.Beam] = new("빔", 3.4, 20, 1.20, 0, 2, 0, 0xFF80FFFF),
        [TdWeapon.Magic] = new("마법", 3.0, 20, 1.60, 1.1, 0, 7, 0xFF90C0FF),
    };

    /// <summary>
    /// 지을 수 있는 타워 — 앞은 병종(용병) 타워, 뒤는 영웅 타워. 병종은 파트1 병사를 그 인물의 기본 공격(칸 9 · 무기 갈래 — 칼 297 · 창 300 ·
    /// 단발사격 306 · 빔라이플 315 · 화이어 애로우 324 · 활 4)으로 골랐다. 파트1 에 활을 쓰는 병사가 없어 궁병은 연적심(활 4)이다.
    /// 영웅은 비싸고 하나씩만 지을 수 있으며 오라를 두른다(사용자 요청 td-1).
    /// </summary>
    internal static readonly TdTowerDef[] Towers =
    [
        new(Part1Game.ChrBase + 155, "검병", TdWeapon.Sword, 50),
        new(Part1Game.ChrBase + 159, "창병", TdWeapon.Spear, 60),
        new(Part1Game.ChrBase + 387, "쌍검병", TdWeapon.DualBlade, 70),
        new(Part1Game.ChrBase + 192, "대검병", TdWeapon.GreatSword, 90),
        new(Part1Game.ChrBase + 89, "궁병", TdWeapon.Bow, 70),
        new(Part1Game.ChrBase + 144, "총병", TdWeapon.Gun, 80),
        new(Part1Game.ChrBase + 256, "빔병", TdWeapon.Beam, 100),
        new(Part1Game.ChrBase + 168, "마법사", TdWeapon.Magic, 110),

        new(Part1Game.ChrBase + 0, "살라딘", TdWeapon.DualBlade, 450, 1.9, true, TdAura.Haste, 0.25),
        new(Part1Game.ChrBase + 30, "버몬트", TdWeapon.GreatSword, 500, 1.8, true, TdAura.Power, 0.30),
        new(Part1Game.ChrBase + 65, "크리스티앙", TdWeapon.Gun, 450, 1.9, true, TdAura.Range, 0.20),
        new(Part1Game.ChrBase + 10, "셰라자드", TdWeapon.Magic, 520, 1.8, true, TdAura.Power, 0.25),
        new(Part1Game.ChrBase + 32, "어윈 록슬리", TdWeapon.Magic, 420, 1.8, true, TdAura.Range, 0.15),
        new(Part1Game.ChrBase + 16, "얀 지슈카", TdWeapon.GreatSword, 420, 1.8, true, TdAura.Haste, 0.20),
        new(219, "살라딘(2부)", TdWeapon.DualBlade, 480, 2.0, true, TdAura.Haste, 0.25),
        new(220, "크리스티앙(2부)", TdWeapon.Gun, 480, 2.0, true, TdAura.Power, 0.25),
        new(221, "죠안", TdWeapon.Magic, 480, 1.9, true, TdAura.Range, 0.20),
        new(223, "베라모드", TdWeapon.GreatSword, 520, 1.9, true, TdAura.Power, 0.30),
    ];

    /// <summary>적 — 파트1 병사들(없으면 파트2 병사로 대신 선다).</summary>
    internal static readonly TdEnemyDef[] Enemies =
    [
        new(Part1Game.ChrBase + 144, 230, "솔져", 60, 1.30, 6),
        new(Part1Game.ChrBase + 145, 311, "도적", 45, 1.90, 6),
        new(Part1Game.ChrBase + 132, 363, "좀비", 120, 0.80, 8),
        new(Part1Game.ChrBase + 130, 265, "나이트", 160, 1.10, 11, 1, 3),
        new(Part1Game.ChrBase + 153, 231, "파이크", 110, 1.20, 9, 1, 2),
        new(Part1Game.ChrBase + 208, 251, "위자드", 90, 1.20, 10),
        new(Part1Game.ChrBase + 131, 300, "캡틴 스켈톤", 260, 0.95, 16, 2, 4),
        new(Part1Game.ChrBase + 229, 362, "드래군", 220, 1.70, 16, 2, 2),
        new(Part1Game.ChrBase + 136, 255, "리치", 900, 0.70, 80, 5, 4),
        new(Part1Game.ChrBase + 221, 304, "장군", 1600, 0.75, 150, 10, 6),
    ];

    /// <summary>웨이브 10개 — 적 번호는 <see cref="Enemies"/> 차례. 5 · 10 에 우두머리.</summary>
    internal static readonly TdSpawn[][] Waves =
    [
        [new(0, 8, 1.1)],
        [new(0, 6, 0.9), new(1, 6, 0.7)],
        [new(2, 8, 1.2), new(1, 6, 0.6)],
        [new(4, 8, 1.0), new(5, 4, 1.4)],
        [new(3, 6, 1.2), new(8, 1, 1)],
        [new(1, 14, 0.45), new(4, 8, 0.9)],
        [new(3, 8, 1.0), new(5, 6, 1.0), new(6, 3, 2)],
        [new(7, 10, 0.8), new(2, 10, 0.6)],
        [new(6, 6, 1.2), new(3, 10, 0.8), new(7, 6, 0.8)],
        [new(7, 8, 0.7), new(6, 6, 1.0), new(9, 1, 1), new(8, 2, 3)],
    ];

    /// <summary>한 웨이브가 지날 때마다 적 HP 에 곱하는 덧셈 몫(웨이브 n 은 1 + n × 이 값).</summary>
    internal const double HpPerWave = 0.10;

    /// <summary>파트1 맵을 못 읽는 PC(파트1 이 없다)에서 대신 여는 파트2 맵.</summary>
    internal static readonly TdMap FallbackMap = new("사막", "0153.obt", -1, -1, [], BattleDemoScene.Bgm);

    /// <summary>맵 — 길이 좁게 굽는 동굴 · 던전 지형(사용자 요청 td-1). 지하 미궁은 파트1 Btl 0415 의 Obt 86.</summary>
    internal static readonly TdMap[] Maps =
    [
        new("지하 미궁", "part1:0086", -1, -1, [(0, 6), (1, 1), (15, 1), (15, 6)], BattleDemoScene.Bgm),
    ];
}

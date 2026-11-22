using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>타워의 무기 갈래(사용자 요청 td-1) — 공격 방식이 갈래마다 다르다.</summary>
internal enum TdWeapon { Sword, Spear, DualBlade, GreatSword, Bow, Gun, Beam, Magic, Frost }

/// <summary>영웅 타워의 오라 — 둘레(<see cref="TdData.AuraRange"/>) 타워를 세게 한다. 같은 갈래는 겹치지 않고 가장 센 것만 먹는다.</summary>
internal enum TdAura { None, Power, Haste, Range }

/// <summary>무기 갈래의 바탕 수치 — 사거리 · 간격은 칸(40px) · 초 단위.</summary>
/// <param name="Splash">맞은 자리에서 이 반경(칸) 안의 적도 맞는다(0 = 하나만).</param>
/// <param name="Pierce">창 — 겨눈 적 뒤로 한 줄에 선 적까지 이만큼 더 꿰뚫는다.</param>
/// <param name="Shot">날아가는 탄의 빠르기(칸/초) — 0 이면 근접(치는 순간 맞는다).</param>
/// <param name="Slow">맞은 적의 걸음을 이만큼 늦춘다(0.45 = 45% 느리게) — 우두머리는 절반만 먹는다.</param>
/// <param name="SlowSeconds">늦추는 시간(초) — 다시 맞으면 새로 잰다.</param>
internal sealed record TdWeaponStats(string Name, double Range, int Damage, double Interval, double Splash, int Pierce, double Shot, uint ShotColor, double Slow = 0, double SlowSeconds = 0);

/// <summary>지을 수 있는 타워 하나 — 인물(Chr 번호, 파트1 은 20000+)과 갈래.</summary>
/// <param name="Power">갈래 바탕 피해 · 사거리에 곱한다(영웅은 1 보다 크다).</param>
/// <param name="Buildable">짓기 칸에 나오는가 — 거짓이면 다른 타워에서 올려야만 된다.</param>
/// <param name="Upgrades">올릴 수 있는 다음 타워들의 키(td-12).</param>
internal sealed record TdTowerDef(int Chr, string Name, TdWeapon Weapon, int Cost, double Power = 1, bool Hero = false, TdAura Aura = TdAura.None, double AuraValue = 0,
                                  string Key = "", bool Buildable = true, string[]? Upgrades = null)
{
    /// <summary>올릴 수 있는 다음 타워 — 키가 없는 것은 버린다.</summary>
    internal TdTowerDef[] Next => (Upgrades ?? []).Select(k => TdData.Towers.FirstOrDefault(t => t.Key == k)).OfType<TdTowerDef>().Where(t => t != this).ToArray();

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
/// <param name="ViewCells">참이면 경유점이 보는 네모 안 칸(옛 꼴), 거짓이면 맵 칸(편집기가 적는 꼴).</param>
/// <param name="AreaW">싸움터 네모 크기(0 = 화면 크기) — 크면 굴려 본다(td-8).</param>
/// <param name="Open">손으로 연 칸 · <paramref name="Blocked"/> 막은 칸(td-10).</param>
internal sealed record TdMap(string Name, string File, int CamX, int CamY, (int Col, int Row)[] Waypoints, int Bgm, bool ViewCells = true,
                            int AreaW = 0, int AreaH = 0, int[][]? Open = null, int[][]? Blocked = null);

/// <summary>스테이지 — 맵과 시작 골드 · 생명, 적과 웨이브(편집기 「타워 디펜스」가 고친다, 사용자 요청 td-4).</summary>
internal sealed record TdStage(string Id, TdMap Map, int StartGold, int StartLives, double HpPerWave, TdEnemyDef[] Enemies, TdSpawn[][] Waves);

internal static class TdData
{
    /// <summary>오라가 닿는 반경(칸).</summary>
    internal const double AuraRange = 2.6;

    /// <summary>웨이브를 앞당겨 부르면 남은 기다림 1초마다 주는 골드.</summary>
    internal const int EarlyBonusPerSecond = 2;

    /// <summary>판 골드 — 타워를 팔면 지은 값의 이만큼을 돌려준다.</summary>
    internal const double SellRate = 0.7;

    /// <summary>무기 갈래 수치 — <see cref="Reload"/> 가 채운다.</summary>
    internal static Dictionary<TdWeapon, TdWeaponStats> Weapons { get; private set; } = [];

    /// <summary>지을 수 있는 타워 — 병종(용병)과 영웅(하나씩만, 오라).</summary>
    internal static TdTowerDef[] Towers { get; private set; } = [];

    internal static TdStage[] Stages { get; private set; } = [];

    static TdData() => Reload();

    /// <summary>
    /// <c>assets/data/towerdefense/</c> 의 towers.json · stage-NN.json 을 다시 읽는다(<see cref="TowerDefenseBook"/>) — 타워 디펜스를 열 때마다 불러
    /// 편집기에서 고친 것이 게임을 다시 켜지 않아도 먹는다. 파일이 없으면 바탕 값.
    /// </summary>
    internal static void Reload()
    {
        GameFiles? files = null;
        try { files = GameFiles.FromFolder(AssetsFolder.Find("data")); }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException or InvalidOperationException) { }
        var towers = files != null ? TowerDefenseBook.LoadTowers(files) : TowerDefenseBook.DefaultTowers();
        var stages = files != null ? TowerDefenseBook.LoadStages(files) : TowerDefenseBook.DefaultStages();

        Weapons = towers.Weapons.Where(w => Enum.TryParse<TdWeapon>(w.Key, out _)).ToDictionary(
            w => Enum.Parse<TdWeapon>(w.Key),
            w => new TdWeaponStats(w.Name, Math.Max(0.5, w.Range), Math.Max(0, w.Damage), Math.Max(0.1, w.Interval), Math.Max(0, w.Splash), Math.Max(0, w.Pierce),
                                   Math.Max(0, w.Shot), uint.TryParse(w.ShotColor, System.Globalization.NumberStyles.HexNumber, null, out uint c) ? c : 0,
                                   Math.Clamp(w.Slow, 0, 0.95), Math.Max(0, w.SlowSeconds)));
        Towers = towers.Towers.Where(t => Enum.TryParse<TdWeapon>(t.Weapon, out var w) && Weapons.ContainsKey(w))
            .Select(t => new TdTowerDef(t.Chr, t.Name, Enum.Parse<TdWeapon>(t.Weapon), Math.Max(0, t.Cost), t.Power > 0 ? t.Power : 1, t.Hero,
                                        Enum.TryParse<TdAura>(t.Aura, out var a) ? a : TdAura.None, t.AuraValue, t.KeyOrName, t.Buildable, [.. t.Upgrades])).ToArray();
        Stages = stages.Select(s => new TdStage(
            s.Id,
            new TdMap(s.Name, s.Map, s.CamX, s.CamY, s.Waypoints.Select(p => (p[0], p[1])).ToArray(), s.Bgm, s.ViewCells, s.AreaW, s.AreaH, [.. s.Open], [.. s.Blocked]),
            s.StartGold, s.StartLives, Math.Max(0, s.HpPerWave),
            s.Enemies.Select(e => new TdEnemyDef(e.Chr, e.Fallback, e.Name, Math.Max(1, e.Hp), Math.Max(0.1, e.Speed), Math.Max(0, e.Bounty), Math.Max(0, e.Leak), Math.Max(0, e.Armor))).ToArray(),
            s.Waves.Select(w => w.Select(g => new TdSpawn(g.Enemy, g.Count, Math.Max(0.1, g.Every))).ToArray()).ToArray())).ToArray();
    }

    /// <summary>파트1 맵을 못 읽는 PC(파트1 이 없다)에서 대신 여는 파트2 맵.</summary>
    internal static readonly TdMap FallbackMap = new("사막", "0153.obt", -1, -1, [], BattleDemoScene.Bgm);
}

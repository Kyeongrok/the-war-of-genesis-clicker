using System.Net.Http;
using System.Text;
using System.Text.Json;
using WarOfGenesis.Assets;

namespace DuelDx;

/// <summary>
/// 전투 통계 — 내 인물이 어느 어빌리티를 몇 번 썼고 피해를 얼마나 넣었는지 센다(사용자 요청 menu-14, 원본에 없다).
/// </summary>
/// <remarks>
/// 세는 곳은 셋이다: 기술을 한 번 쓸 때(<see cref="Cast"/> — <c>UseWorkRoutine</c> 의 비용 치르는 자리),
/// 맞힐 때마다(<see cref="Hit"/> · <see cref="Heal"/> — <c>ApplyWork</c>), 쓰러뜨릴 때(<see cref="Kill"/>).
/// <b>내가 움직이는 편(편 4)의 대장</b>이 한 것만 센다 — 군단 부하가 한 것은 안 센다. 전투가 끝나면(<see cref="Finish"/>)
/// 그 판의 것을 누적 파일(<c>stats.json</c>)에 더하고, 「통계 보내기」를 켰으면 요약 한 덩이를 받는 곳(<c>tools/stats-worker</c>)으로 보낸다.
/// 전투 도중에 세이브를 불러오면 그 판의 셈은 처음부터 다시 한다(세이브에 안 싣는다).
/// </remarks>
internal sealed class BattleStats(GameWindow host)
{
    /// <summary>받는 곳의 주소를 적어 두는 파일 — 저장소 뿌리에 있고 exe 옆에 실린다.</summary>
    internal const string UrlFile = "stats-url.txt";

    /// <summary>
    /// 통계를 받는 곳(Cloudflare Worker — <c>tools/stats-worker</c> 를 올리면 찍히는 주소). <see cref="UrlFile"/> 의 첫 줄(<c>#</c> 줄 · 빈 줄 빼고)이다.
    /// <b>비어 있거나 https 주소가 아니면 아무것도 안 보낸다.</b>
    /// </summary>
    internal static readonly string UploadUrl = ReadUrl();

    private static string ReadUrl()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, UrlFile);
            if (!File.Exists(path)) return "";
            string url = File.ReadLines(path).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) ?? "";
            return url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url : "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ""; }
    }

    /// <summary>한 줄의 셈 — 인물 × 어빌리티(레벨마다 따로).</summary>
    internal sealed class Row
    {
        public int Uses { get; set; }
        public int Hits { get; set; }
        public long Damage { get; set; }
        public int Kills { get; set; }
        public long Heal { get; set; }

        internal void Add(Row o) => (Uses, Hits, Damage, Kills, Heal) = (Uses + o.Uses, Hits + o.Hits, Damage + o.Damage, Kills + o.Kills, Heal + o.Heal);
    }

    /// <summary>열쇠 — 일반 공격은 인물마다 work 가 달라서 (어빌리티 0, work 0)으로 모은다. 아이템 따위는 (0, work).</summary>
    internal readonly record struct Key(int Chr, int Ability, int Work, int Level)
    {
        public override string ToString() => $"{Chr}:{Ability}:{Work}:{Level}";

        internal static Key? Parse(string s) =>
            s.Split(':') is [var a, var b, var c, var d] && int.TryParse(a, out int chr) && int.TryParse(b, out int ability)
            && int.TryParse(c, out int work) && int.TryParse(d, out int level) ? new Key(chr, ability, work, level) : null;
    }

    /// <summary>누적 파일의 꼴.</summary>
    internal sealed record Lifetime(int Battles, int Wins, Dictionary<string, Row> Skills);

    /// <summary>지금 전투의 셈.</summary>
    internal readonly Dictionary<Key, Row> _battle = [];

    private Dictionary<Key, Row>? _total;
    private int _totalBattles, _totalWins;

    /// <summary>방금 기술을 쓴 유닛과 그 work — 「연」의 막타는 일반 공격 work 로 맞아서, 그 피해를 쓴 기술 몫으로 돌리는 데 쓴다.</summary>
    private (UnitState? User, WorkData? Work) _cast;

    /// <summary>이 게임을 켠 뒤 캐릭터 에디터로 무엇을 바꿨나 — 그런 판의 수치는 따로 본다(보내는 요약의 표시).</summary>
    internal bool _charEdited;

    internal static string TotalPath => UserDataFolder.File("stats.json");
    internal static string InstallPath => UserDataFolder.File("install-id.txt");
    internal static string QueueFolder => UserDataFolder.File("stats-queue");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>전투를 새로 열 때 — 그 판의 셈을 비운다.</summary>
    internal void Reset()
    {
        _battle.Clear();
        _cast = default;
    }

    /// <summary>
    /// 셈을 받을 인물 — 내가 움직이는 편의 <b>대장(주인공)</b>만. 군단 부하(<c>LeaderIndex ≥ 0</c>)가 한 것은 세지 않는다 —
    /// 전에는 대장 몫으로 넣어 부하가 칠 때마다 대장의 일반 공격 「사용」이 늘었다(사용 639 에 명중 455, 사용자 지적).
    /// </summary>
    private static UnitState? Owner(UnitState a) => a.LeaderIndex < 0 && a.PlayerControlled ? a : null;

    private Row? RowFor(UnitState a, WorkData w)
    {
        if (Owner(a) is not { } owner) return null;
        // 「연」의 막타: 방금 그 유닛이 다른 기술을 썼고 이번 타가 제 일반 공격 work 면 쓴 기술 몫이다.
        if (_cast.User == a && _cast.Work is { } cast && cast.Id != w.Id && a.Data?.BasicWorkId == w.Id) w = cast;
        bool basic = a.Data?.BasicWorkId == w.Id;
        var key = basic ? new Key(owner.ChrCode, 0, 0, 0) : new Key(owner.ChrCode, w.AbilityId, w.AbilityId > 0 ? 0 : w.Id, w.Level);
        return _battle.TryGetValue(key, out var row) ? row : _battle[key] = new Row();
    }

    /// <summary>기술을 쓰기 시작할 때 — 누가 무엇을 쓰는지만 적어 둔다.</summary>
    internal void BeginCast(UnitState a, WorkData w) => _cast = (a, w);

    /// <summary>기술을 한 번 썼다(비용을 치른 자리에서 한 번).</summary>
    internal void Cast(UnitState a, WorkData w)
    {
        if (RowFor(a, w) is { } row) row.Uses++;
    }

    /// <summary>한 대 맞혔다 — <paramref name="dealt"/> 는 실제로 깎인 HP.</summary>
    internal void Hit(UnitState a, WorkData w, int dealt)
    {
        if (RowFor(a, w) is not { } row) return;
        row.Hits++;
        row.Damage += Math.Max(0, dealt);
    }

    internal void Heal(UnitState a, WorkData w, int healed)
    {
        if (RowFor(a, w) is { } row) row.Heal += Math.Max(0, healed);
    }

    internal void Kill(UnitState a, WorkData w)
    {
        if (RowFor(a, w) is { } row) row.Kills++;
    }

    /// <summary>누적 셈 — 처음 볼 때 파일에서 읽는다.</summary>
    internal (int Battles, int Wins, Dictionary<Key, Row> Skills) Total()
    {
        if (_total == null)
        {
            _total = [];
            try
            {
                if (File.Exists(TotalPath) && JsonSerializer.Deserialize<Lifetime>(File.ReadAllText(TotalPath), Json) is { } saved)
                {
                    (_totalBattles, _totalWins) = (saved.Battles, saved.Wins);
                    foreach (var (text, row) in saved.Skills ?? [])
                        if (Key.Parse(text) is { } key) _total[key] = row;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* 못 읽으면 빈 데서 다시 쌓는다 */ }
        }
        return (_totalBattles, _totalWins, _total);
    }

    /// <summary>전투가 끝났다 — 그 판의 셈을 누적에 더하고, 켜 두었으면 보낼 줄에 세운다. 그 판의 셈은 다음 전투가 열릴 때까지 남는다(통계 창).</summary>
    internal void Finish(bool won)
    {
        if (_battle.Count == 0) return;
        var (_, _, total) = Total();
        foreach (var (key, row) in _battle)
        {
            if (!total.TryGetValue(key, out var sum)) total[key] = sum = new Row();
            sum.Add(row);
        }
        _totalBattles++;
        if (won) _totalWins++;
        try
        {
            Directory.CreateDirectory(UserDataFolder.Path);
            File.WriteAllText(TotalPath, JsonSerializer.Serialize(new Lifetime(_totalBattles, _totalWins, total.ToDictionary(p => p.Key.ToString(), p => p.Value)), Json));
            if (CanUpload) Enqueue(won);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 못 적어도 게임은 그대로 간다 */ }
        UploadInBackground();
    }

    /// <summary>보내도 되나 — 받는 곳이 있고, 사용자가 켰고, 릴리즈 판이고, 시험 실행이 아닐 때만.</summary>
    internal bool CanUpload => host._sendStats && CanUploadBuild;

    /// <summary>
    /// 이 판이 보낼 수 있는 판인가(사용자의 답과 무관) — 받는 곳 주소가 있고, 태그로 낸 릴리즈 판이고(손으로 빌드한 판은 「v0.22.0+3」처럼 + 가 붙는다),
    /// 시험 실행이 아닐 때.
    /// </summary>
    internal bool CanUploadBuild =>
        UploadUrl.Length > 0 && !GameWindow.Offscreen && !GameWindow.TestRun && GameWindow.AppVersion.StartsWith('v') && !GameWindow.AppVersion.Contains('+');

    /// <summary>설치마다 하나인 무작위 번호 — 누구인지는 모르고 같은 설치인지만 안다.</summary>
    private static string InstallId()
    {
        if (File.Exists(InstallPath) && Guid.TryParse(File.ReadAllText(InstallPath).Trim(), out var known)) return known.ToString();
        string id = Guid.NewGuid().ToString();
        File.WriteAllText(InstallPath, id);
        return id;
    }

    /// <summary>이 판의 요약을 보낼 줄(파일 하나)에 세운다 — 인터넷이 없으면 다음에 켤 때 다시 보낸다.</summary>
    private void Enqueue(bool won)
    {
        string report = Guid.NewGuid().ToString();
        var body = new
        {
            v = 1,
            report,
            install = InstallId(),
            version = GameWindow.AppVersion.TrimStart('v'),
            battle = host._scene.Id,
            difficulty = host.Btl._difficulty,
            outcome = won ? "win" : "lose",
            turns = host.Btl._turnNo,
            flags = new { charEdit = _charEdited, soulWeight = host.TuningScr._soulWeight, fullSoul = host._fullSoulAtStart },
            skills = _battle.Select(p => new
            {
                chr = p.Key.Chr, ability = p.Key.Ability, work = p.Key.Work, level = p.Key.Level,
                uses = p.Value.Uses, hits = p.Value.Hits, damage = p.Value.Damage, kills = p.Value.Kills, heal = p.Value.Heal,
            }),
        };
        Directory.CreateDirectory(QueueFolder);
        File.WriteAllText(Path.Combine(QueueFolder, report + ".json"), JsonSerializer.Serialize(body, Json));
    }

    private static int _uploading;

    /// <summary>줄에 선 요약을 뒤에서 보낸다 — 받은 것은 지우고, 안 되면 조용히 남겨 둔다. 켤 때와 전투가 끝날 때 부른다.</summary>
    internal void UploadInBackground()
    {
        if (!CanUpload || !Directory.Exists(QueueFolder) || Interlocked.Exchange(ref _uploading, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                // 오래 못 보낸 것이 한없이 쌓이지 않게 새 것부터 50 개만 보내고 나머지는 버린다.
                var files = new DirectoryInfo(QueueFolder).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                foreach (var old in files.Skip(50)) old.Delete();
                foreach (var file in files.Take(50))
                {
                    using var content = new StringContent(await File.ReadAllTextAsync(file.FullName), Encoding.UTF8, "application/json");
                    using var reply = await http.PostAsync(UploadUrl.TrimEnd('/') + "/v1/battle", content);
                    // 받는 쪽이 「못 쓸 글」이라 하면(4xx) 다시 보내도 같다 — 지운다. 그 밖의 실패는 다음에 다시.
                    if (reply.IsSuccessStatusCode || (int)reply.StatusCode is >= 400 and < 500) file.Delete();
                    else break;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"[Stats] {ex.GetType().Name}: {ex.Message}");
            }
            finally { Interlocked.Exchange(ref _uploading, 0); }
        });
    }
}

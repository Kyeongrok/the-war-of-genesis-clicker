using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WarOfGenesis.Assets;

/// <summary>
/// 아이템 덮어쓰기 — <c>assets/data/items/NNNN.json</c> 에 고친 아이템만 적는다. 원본 <c>Dat/Itm.dat</c> 은 그대로 두고,
/// 게임·편집기는 <see cref="LoadAll"/> 로 원본을 읽은 뒤 이 파일들을 덮어쓴다(<see cref="LegionBook"/> 과 같은 꼴, 사용자 요청 ed-2).
/// </summary>
/// <remarks>
/// 적는 칸: 값 · 공격 · 방어 · 사거리 · 장비 보정(능력치 번호, 값)×최대 3 · 기본공격이 거는 상태이상(번호, 값)×최대 3 · 쓰는 work.
/// 이름·설명 TXR 과 종류·그림은 원본 것을 그대로 쓴다(<c>name</c> 은 보기용).
/// </remarks>
public static class ItemBook
{
    public const string Folder = "items";

    /// <summary>장비 보정 번호의 이름 — 30~48 은 원본이 읽는 것(<c>0x10032c60</c>), 42 는 원본이 안 읽어 이 게임이 따로 먹인다(경험치 %).</summary>
    public static readonly IReadOnlyDictionary<int, string> StatNames = new Dictionary<int, string>
    {
        [30] = "DEX", [31] = "PSY", [32] = "DEP", [33] = "최대 TP", [37] = "최대 SOUL", [ExpStat] = "경험치 %", [48] = "LP",
    };

    /// <summary>
    /// 보정 번호 42 — 반지류(금반지 15 … 지혜의 반지 90)에만 적혀 있고 설명은 「경험치를 추가로 얻는다」인데, 원본 DLL 에는 이 번호를 읽는 곳이 없다
    /// (장비 보정을 읽는 <c>0x10032c60</c> 호출 42곳이 30·31·32·33·37·48 만 넘긴다). 이 게임은 쓰러뜨려 얻는 경험치에 그 %를 더한다.
    /// </summary>
    public const int ExpStat = 42;

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string FileName(int id) => $"{id:D4}.json";

    /// <summary><c>Itm.dat</c> 를 그대로 읽는다 — 레코드 48바이트, 로더 <c>0x1004b120</c>.</summary>
    public static Dictionary<int, ItemData> ParseOriginal(byte[] d)
    {
        static ushort U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
        var items = new Dictionary<int, ItemData>();
        for (int i = 0, n = U16(d, 2), o = 6; i < n && o + 48 <= d.Length; i++, o += 48)
        {
            // 장비 보정 셋 — 파일 18/22/26 이 능력치 번호, 20/24/28 이 값(부호 있는 수).
            var bonuses = Enumerable.Range(0, 3)
                .Select(k => (Stat: U16(d, o + 18 + 4 * k), Value: (short)U16(d, o + 20 + 4 * k)))
                .Where(p => p.Stat != 0).ToArray();
            // 기본공격이 거는 상태이상 셋 — 파일 30/34/38 이 번호(엔진은 낮은 바이트만 쓴다), 32/36/40 이 값.
            var attackEffects = Enumerable.Range(0, 3)
                .Select(k => (Status: d[o + 30 + 4 * k], Value: (short)U16(d, o + 32 + 4 * k)))
                .Where(p => p.Status != 0).ToArray();
            items[U16(d, o)] = new ItemData(U16(d, o), U16(d, o + 2), BitConverter.ToUInt32(d, o + 4), d[o + 8],
                                            U16(d, o + 11), U16(d, o + 13), bonuses, U16(d, o + 9),
                                            U16(d, o + 42), U16(d, o + 46), U16(d, o + 16), attackEffects);
        }
        return items;
    }

    public static string ToJson(ItemData item, string name)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append($"  \"id\": {item.Id},\n");
        sb.Append($"  \"name\": {JsonSerializer.Serialize(name, Json)},\n");
        sb.Append($"  \"price\": {item.Price},\n");
        sb.Append($"  \"attack\": {item.Attack},\n");
        sb.Append($"  \"defense\": {item.Defense},\n");
        sb.Append($"  \"range\": {item.Range},\n");
        sb.Append($"  \"useWork\": {item.UseWork},\n");
        sb.Append($"  \"bonuses\": [{string.Join(", ", item.Bonuses.Select(b => $"{{ \"stat\": {b.Stat}, \"value\": {b.Value} }}"))}],\n");
        sb.Append($"  \"attackEffects\": [{string.Join(", ", (item.AttackEffects ?? []).Select(e => $"{{ \"status\": {e.Status}, \"value\": {e.Value} }}"))}]\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>JSON 을 원본 아이템 위에 덮는다 — 원본에 없는 번호는 버린다(이름·종류·그림을 알 수 없다). 빠진 칸은 원본 값을 쓴다.</summary>
    public static ItemData? FromJson(string text, ItemData? original)
    {
        if (original == null || JsonNode.Parse(text) is not JsonObject o) return null;
        static int I(JsonNode? n, int fallback, int min, int max) => Math.Clamp(n?.GetValue<long>() ?? fallback, min, max) is var v ? (int)v : fallback;
        var bonuses = (o["bonuses"] as JsonArray)?.OfType<JsonObject>()
                          .Select(b => ((ushort)I(b["stat"], 0, 0, ushort.MaxValue), (short)I(b["value"], 0, short.MinValue, short.MaxValue)))
                          .Where(b => b.Item1 != 0).Take(3).ToArray() ?? original.Bonuses;
        var effects = (o["attackEffects"] as JsonArray)?.OfType<JsonObject>()
                          .Select(e => ((byte)I(e["status"], 0, 0, byte.MaxValue), (short)I(e["value"], 0, short.MinValue, short.MaxValue)))
                          .Where(e => e.Item1 != 0).Take(3).ToArray() ?? original.AttackEffects;
        return original with
        {
            Price = o["price"] is { } price ? (uint)Math.Clamp(price.GetValue<long>(), 0, uint.MaxValue) : original.Price,
            Attack = (ushort)I(o["attack"], original.Attack, 0, ushort.MaxValue),
            Defense = (ushort)I(o["defense"], original.Defense, 0, ushort.MaxValue),
            Range = (ushort)I(o["range"], original.Range, 0, ushort.MaxValue),
            UseWork = (ushort)I(o["useWork"], original.UseWork, 0, ushort.MaxValue),
            Bonuses = bonuses,
            AttackEffects = effects,
        };
    }

    /// <summary>원본 Itm.dat 만 — 편집기가 「원본과 다른가」를 가를 때.</summary>
    public static Dictionary<int, ItemData> LoadOriginal(GameFiles files) =>
        ParseOriginal(files.Read("Dat", "itm.dat") ?? throw new FileNotFoundException("Dat/itm.dat 을(를) 못 찾았습니다."));

    /// <summary>원본 Itm.dat 에 <c>items/*.json</c> 덮어쓰기를 얹은 아이템 표 — 게임이 쓰는 것.</summary>
    public static Dictionary<int, ItemData> LoadAll(GameFiles files)
    {
        var all = LoadOriginal(files);
        IReadOnlyDictionary<string, long> names;
        try { names = files.List(Folder, ".json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return all; }
        foreach (var name in names.Keys)
        {
            if (files.Read(Folder, name) is not { } b) continue;
            try
            {
                string text = Encoding.UTF8.GetString(b);
                int id = JsonNode.Parse(text)?["id"]?.GetValue<int>() ?? -1;
                if (FromJson(text, all.GetValueOrDefault(id)) is { } item) all[item.Id] = item;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        }
        return all;
    }

    /// <summary>두 아이템이 같은 수치인가 — 편집기가 원본과 같으면 덮어쓰기 파일을 안 남긴다.</summary>
    public static bool SameContent(ItemData a, ItemData b) =>
        a.Price == b.Price && a.Attack == b.Attack && a.Defense == b.Defense && a.Range == b.Range && a.UseWork == b.UseWork
        && a.Bonuses.SequenceEqual(b.Bonuses) && (a.AttackEffects ?? []).SequenceEqual(b.AttackEffects ?? []);
}

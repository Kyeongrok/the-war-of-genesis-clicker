using System.IO;
using System.Text.Json;

namespace WarOfGenesis.Assets;

/// <summary>
/// 그림을 함께 쓰는 Obs — <c>assets/data/shared-sprites.json</c>. 색만 다른 그림(파트2 일반병사 231 · 232 · 233 = 0841 · 0845 · 0843)은
/// 바탕 그림 하나의 장 · 모션표(모션 손질 포함)를 쓰고, 색 대응표(<c>AARRGGBB → AARRGGBB</c>)로 물들인다. 원본에는 없다(사용자 요청 asset-4).
/// 그래서 편집기에서 바탕 그림의 모션을 고치면 함께 쓰는 인물이 다 따라간다. 원본 모션 틱은 그림마다 조금 달랐다(232 는 24개, 233 은 28개 모션) — 바탕 것으로 맞췄다.
/// </summary>
public static class SharedSprites
{
    /// <summary>함께 쓰는 그림 하나 — 바탕 Obs 번호와 색 대응표(없으면 같은 색).</summary>
    public sealed record Entry(int Base, IReadOnlyDictionary<uint, uint>? Recolor);

    private sealed record Raw(int Base, string? Note, Dictionary<string, string>? Recolor);

    private static Dictionary<int, Entry>? _all;

    public static string FilePath => Path.Combine(AssetsFolder.Find("data"), "shared-sprites.json");

    private static Dictionary<int, Entry> All()
    {
        if (_all != null) return _all;
        _all = [];
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, Raw>>(File.ReadAllText(FilePath)) is { } loaded)
                foreach (var (key, raw) in loaded)
                    if (int.TryParse(key, out int obs) && raw.Base > 0 && raw.Base != obs)
                        _all[obs] = new(raw.Base, raw.Recolor?.ToDictionary(kv => Convert.ToUInt32(kv.Key, 16), kv => Convert.ToUInt32(kv.Value, 16)));
        }
        catch (Exception ex) when (ex is IOException or JsonException or DirectoryNotFoundException or UnauthorizedAccessException or FormatException) { }
        return _all;
    }

    /// <summary>그 Obs 가 다른 그림을 빌려 쓰나 — 아니면 null.</summary>
    public static Entry? Of(int obs) => All().GetValueOrDefault(obs);

    /// <summary>
    /// 그 그림 파일 대신 읽을 바탕 그림 파일 — 같은 폴더, 그다음 이웃 인물 폴더(<c>assets/characters/*/</c>)에서 <c>NNNN.obs</c> 를 찾는다. 함께 쓰지 않거나 못 찾으면 null.
    /// </summary>
    public static (string Path, Entry Entry)? BaseFile(string obsPath)
    {
        if (!int.TryParse(Path.GetFileNameWithoutExtension(obsPath), out int obs) || Of(obs) is not { } e) return null;
        string name = $"{e.Base:D4}.obs";
        string? folder = Path.GetDirectoryName(obsPath);
        if (folder == null) return null;
        string near = Path.Combine(folder, name);
        if (File.Exists(near)) return (near, e);
        try
        {
            if (Path.GetDirectoryName(folder) is { } parent)
                foreach (var dir in Directory.EnumerateDirectories(parent))
                    if (Path.Combine(dir, name) is var p && File.Exists(p)) return (p, e);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>장들을 색 대응표로 물들인다 — 표에 없는 색 · 투명은 그대로.</summary>
    public static IReadOnlyList<ObsMotion> Recolor(IReadOnlyList<ObsMotion> motions, IReadOnlyDictionary<uint, uint>? map)
    {
        if (map is not { Count: > 0 }) return motions;
        return [.. motions.Select(m => m with { Frames = [.. m.Frames.Select(f => f with { Bgra = Recolored(f.Bgra, map) })] })];
    }

    private static byte[] Recolored(byte[] bgra, IReadOnlyDictionary<uint, uint> map)
    {
        var px = (byte[])bgra.Clone();
        for (int i = 0; i + 3 < px.Length; i += 4)
            if (px[i + 3] != 0 && map.TryGetValue(BitConverter.ToUInt32(px, i), out uint to)) BitConverter.TryWriteBytes(px.AsSpan(i, 4), to);
        return px;
    }

    /// <summary>
    /// 그 그림 파일의 장 · 모션표 — 함께 쓰는 그림이면 바탕 그림 것을 물들여서, 아니면 그 파일 그대로.
    /// <paramref name="obsPath"/> 는 바탕 파일로 바꿔 돌려준다(모션 손질 · 그림 번호가 바탕 것을 따른다).
    /// </summary>
    public static (IReadOnlyList<ObsMotion> Motions, ObsMotionTable? Table) Load(ref string obsPath)
    {
        if (BaseFile(obsPath) is { } shared)
        {
            obsPath = shared.Path;
            return (Recolor(ObsSprite.Decode(shared.Path), shared.Entry.Recolor), ObsMotionTable.Load(shared.Path));
        }
        return (ObsSprite.Decode(obsPath), ObsMotionTable.Load(obsPath));
    }
}

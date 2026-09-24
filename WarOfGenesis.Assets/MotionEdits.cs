using System.IO;
using System.Text.Json;

namespace WarOfGenesis.Assets;

/// <summary>
/// 모션 틱 손질 — 편집기 「모션 매핑」에서 그림 키를 지우거나 길이를 바꾼 것을 <c>assets/data/motion_edits.json</c> 에 적고,
/// 게임과 편집기가 Obs 모션표를 읽을 때(<see cref="ObsMotionTable.Load(string)"/>) 덧입힌다. 원본 자료에는 없다(사용자 요청).
/// </summary>
/// <remarks>
/// 한 모션의 손질은 <b>남길 원본 그림 키의 차례 번호</b>와 <b>그 키의 새 길이(틱)</b> 두 줄이다. 새 키는 차례대로 이어 붙인다.
/// 소리·타격·자식 그림·섞기·물들이기·떨림 키는 원래 들어 있던 그림 키 안의 자리를 새 길이에 비례해 옮긴다 —
/// 지운 키 안에 있던 것은 다음에 남은 키의 처음으로 간다. 그래서 빠르게 해도 타격·소리가 그림과 어긋나지 않는다.
/// </remarks>
public static class MotionEdits
{
    /// <summary>한 모션의 손질 — 남길 원본 키 번호(시작 틱 차례)와 그 새 길이.</summary>
    public sealed record Edit(int[] Keep, int[] Lengths);

    private static Dictionary<string, Dictionary<string, Edit>>? _all;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>손질 파일 자리 — 저장소 <c>assets/data/motion_edits.json</c>(배포판은 함께 묶인 사본).</summary>
    public static string FilePath => Path.Combine(AssetsFolder.Find("data"), "motion_edits.json");

    private static Dictionary<string, Dictionary<string, Edit>> All()
    {
        if (_all != null) return _all;
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Edit>>>(File.ReadAllText(FilePath)) is { } loaded)
                return _all = loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or DirectoryNotFoundException or UnauthorizedAccessException) { }
        return _all = [];
    }

    /// <summary>그 Obs 모션의 손질 — 없으면 null.</summary>
    public static Edit? Get(int obs, int motion) =>
        All().TryGetValue($"{obs:D4}", out var byMotion) && byMotion.TryGetValue(motion.ToString(), out var e) ? e : null;

    /// <summary>손질을 적는다(null 이면 지워 원본으로). 바로 파일에 쓴다.</summary>
    public static void Set(int obs, int motion, Edit? edit)
    {
        var all = All();
        string key = $"{obs:D4}";
        if (edit == null)
        {
            if (all.TryGetValue(key, out var m)) { m.Remove(motion.ToString()); if (m.Count == 0) all.Remove(key); }
        }
        else
        {
            if (!all.TryGetValue(key, out var m)) all[key] = m = [];
            m[motion.ToString()] = edit;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all, Json));
    }

    /// <summary>파일을 다시 읽게 한다(편집기가 저장한 뒤).</summary>
    public static void Reload() => _all = null;

    /// <summary>원본 모션에 손질을 덧입힌 모션을 만든다.</summary>
    public static ObsMotionClip Apply(ObsMotionClip clip, Edit edit)
    {
        var keys = clip.Keys.OrderBy(k => k.Start).ToList();
        if (keys.Count == 0 || edit.Keep.Length == 0 || edit.Keep.Length != edit.Lengths.Length) return clip;
        var newStart = new int[edit.Keep.Length];
        var newKeys = new List<MotionKey>();
        int t = keys[0].Start;
        for (int p = 0; p < edit.Keep.Length; p++)
        {
            int i = edit.Keep[p];
            if ((uint)i >= keys.Count) continue;
            int len = Math.Max(1, edit.Lengths[p]);
            newStart[p] = t;
            newKeys.Add(keys[i] with { Start = t, Length = len });
            t += len;
        }
        int total = t;

        // 원래 틱 → 새 틱. 그 틱이 든 원본 키를 찾아, 남았으면 그 키 안의 자리를 새 길이에 비례해, 지웠으면 다음에 남은 키의 처음으로.
        int Map(int tick)
        {
            if (tick < keys[0].Start) return tick;
            int i = keys.FindLastIndex(k => k.Start <= tick);
            int p = Array.IndexOf(edit.Keep, i);
            if (p >= 0)
            {
                int oldLen = Math.Max(1, keys[i].Length);
                return newStart[p] + Math.Min(edit.Lengths[p] - 1, (tick - keys[i].Start) * Math.Max(1, edit.Lengths[p]) / oldLen);
            }
            for (int q = 0; q < edit.Keep.Length; q++) if (edit.Keep[q] > i) return newStart[q];
            return Math.Max(0, total - 1);
        }

        return clip with
        {
            Length = total,
            Keys = newKeys,
            Sounds = [.. clip.Sounds.Select(s => (Map(s.Start), s.Sound))],
            Children = [.. clip.Children.Select(c => c with { Start = Map(c.Start) })],
            Hits = [.. clip.Hits.Select(h => (Map(h.Start), h.Ability))],
            Blends = [.. clip.Blends.Select(b => (Map(b.Start), b.Mode))],
            Tints = [.. clip.Tints.Select(x => (Map(x.Start), x.Mode, x.Strength))],
            Offsets = [.. clip.Offsets.Select(o => (Map(o.Start), o.X, o.Y))],
        };
    }
}

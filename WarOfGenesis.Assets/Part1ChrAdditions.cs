namespace WarOfGenesis.Assets;

/// <summary>
/// 원본 파트1 인물 자료(<c>Chars\NNNN.chr</c>)에 리메이크가 덧대는 값 — 사용자 결정으로 고친 칸만 적는다(<see cref="Part1Chr.Load"/> 가 건다).
/// </summary>
public static class Part1ChrAdditions
{
    /// <summary>Chr → (칸, 값) 들.</summary>
    private static readonly Dictionary<int, (int Field, int Value)[]> Fields = new()
    {
        // 489 캡틴 스켈톤 — 원본 레벨 70 · HP 1800 · STR 180 에 둘레 2칸 용병파(Attack 280)라, 레벨 10~12 일행이 처음 마주치는
        // 한밤중의 숲(Btl 0151)에서 한 번에 쓰러졌다(버그 제보 #43). HP 만 1000 으로(사용자 결정). 0151~0160 · 0164(셋) · 0238 · 0275 · 0360 · 0394 에 나온다.
        [489] = [(19, 1000)],
    };

    public static Part1Chr Apply(Part1Chr chr)
    {
        if (!Fields.TryGetValue(chr.Code, out var adds)) return chr;
        var fields = (int[])chr.Fields.Clone();
        foreach (var (field, value) in adds) fields[field] = value;
        return chr with { Fields = fields };
    }
}

namespace PokerPlanning.Models;

public enum ScaleType
{
    Fibonacci,
    TShirt,
    PowersOf2,
    Sequential,
    Risk
}

public static class ScaleDefinitions
{
    public static readonly Dictionary<ScaleType, string[]> Scales = new()
    {
        [ScaleType.Fibonacci] = ["1", "2", "3", "5", "8", "13", "21", "?"],
        [ScaleType.TShirt] = ["XS", "S", "M", "L", "XL", "XXL", "?"],
        [ScaleType.PowersOf2] = ["1", "2", "4", "8", "16", "32", "?"],
        [ScaleType.Sequential] = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "?"],
        [ScaleType.Risk] = ["Low", "Medium", "High", "Critical", "?"]
    };

    public static string[] GetScale(ScaleType type) =>
        Scales.TryGetValue(type, out var scale) ? scale : Scales[ScaleType.Fibonacci];

    public static string GetDisplayName(ScaleType type) => type switch
    {
        ScaleType.Fibonacci => "Fibonacci (1, 2, 3, 5, 8, 13, 21)",
        ScaleType.TShirt => "T-Shirt (XS, S, M, L, XL, XXL)",
        ScaleType.PowersOf2 => "Powers of 2 (1, 2, 4, 8, 16, 32)",
        ScaleType.Sequential => "Sequential (1–10)",
        ScaleType.Risk => "Risk (Low, Medium, High, Critical)",
        _ => type.ToString()
    };
}

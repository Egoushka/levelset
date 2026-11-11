namespace Levelset;

/// <summary>
/// Each category starts at 100 and loses points once per failing check: an error costs 12, a warning 4, and a
/// draft or vendor check at most 3. Info costs nothing. A category with nothing applicable has no score and its
/// weight goes to the others. The total is the weighted mean of the scored categories.
/// </summary>
public static class Scorer
{
    public const int ErrorLoss = 12;
    public const int WarningLoss = 4;
    public const int DraftCap = 3;

    public static readonly IReadOnlyDictionary<Category, int> Weights = new Dictionary<Category, int>
    {
        [Category.WebMcp] = 30,
        [Category.Mcp] = 20,
        [Category.X402] = 15,
        [Category.Discovery] = 25,
        [Category.Hygiene] = 10,
    };

    public static int Loss(CheckInfo check)
    {
        var loss = check.Severity switch
        {
            Severity.Error => ErrorLoss,
            Severity.Warning => WarningLoss,
            _ => 0,
        };
        return check.Status is CheckStatus.Draft or CheckStatus.Vendor ? Math.Min(loss, DraftCap) : loss;
    }

    /// <param name="applicable">Categories that had at least one applicable scored check.</param>
    /// <param name="failed">Checks that produced a finding.</param>
    public static (int Total, Dictionary<string, int?> Categories) Score(
        IReadOnlySet<Category> applicable, IEnumerable<CheckInfo> failed)
    {
        var losses = failed.GroupBy(c => c.Category).ToDictionary(g => g.Key, g => g.Sum(Loss));
        var categories = new Dictionary<string, int?>();
        double weighted = 0;
        double weights = 0;
        foreach (var (category, weight) in Weights)
        {
            if (!applicable.Contains(category))
            {
                categories[category.Key()] = null;
                continue;
            }

            var score = Math.Max(0, 100 - losses.GetValueOrDefault(category));
            categories[category.Key()] = score;
            weighted += score * weight;
            weights += weight;
        }

        var total = weights == 0 ? 100 : (int)Math.Round(weighted / weights, MidpointRounding.AwayFromZero);
        return (total, categories);
    }

    public static string Grade(int score) => score switch
    {
        >= 90 => "A",
        >= 75 => "B",
        >= 60 => "C",
        >= 40 => "D",
        _ => "F",
    };
}

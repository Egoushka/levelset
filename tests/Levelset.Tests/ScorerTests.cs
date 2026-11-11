namespace Levelset.Tests;

public class ScorerTests
{
    private static CheckInfo Check(Category c, Severity s, CheckStatus st = CheckStatus.Standard) =>
        new("T-001", c, s, st, "t", "https://example.test", "fix");

    [Fact]
    public void NothingFailingScoresAHundred()
    {
        var (total, cats) = Scorer.Score(new HashSet<Category> { Category.Discovery, Category.Hygiene }, []);
        Assert.Equal(100, total);
        Assert.Null(cats["webmcp"]);
        Assert.Equal(100, cats["discovery"]);
    }

    [Fact]
    public void ErrorsAndWarningsCostPointsInTheirCategory()
    {
        var (total, cats) = Scorer.Score(
            new HashSet<Category> { Category.Discovery, Category.Hygiene },
            [Check(Category.Discovery, Severity.Error), Check(Category.Discovery, Severity.Warning)]);
        Assert.Equal(84, cats["discovery"]);
        Assert.Equal(100, cats["hygiene"]);
        Assert.Equal(89, total); // (84 * 25 + 100 * 10) / 35
    }

    [Fact]
    public void DraftChecksLoseAtMostThree()
    {
        Assert.Equal(3, Scorer.Loss(Check(Category.WebMcp, Severity.Error, CheckStatus.Draft)));
        Assert.Equal(3, Scorer.Loss(Check(Category.WebMcp, Severity.Warning, CheckStatus.Vendor)));
        Assert.Equal(0, Scorer.Loss(Check(Category.WebMcp, Severity.Info)));
    }

    [Theory]
    [InlineData(100, "A")]
    [InlineData(89, "B")]
    [InlineData(60, "C")]
    [InlineData(40, "D")]
    [InlineData(39, "F")]
    public void GradeBands(int score, string grade) => Assert.Equal(grade, Scorer.Grade(score));
}

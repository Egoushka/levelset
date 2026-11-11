namespace Levelset.Tests;

public class GateTests
{
    private static Finding F(string id, string target, string severity) =>
        new(id, severity, target, "m", new Fix("fix"), "https://x.test");

    private static Report R(int score, params Finding[] f) =>
        new(Report.SchemaId, "https://x.test/", score, Scorer.Grade(score), new(), f, [], null, 0);

    private static Baseline B(int score, params (string, string, string)[] f) =>
        new(Baseline.SchemaId, "https://x.test/", score, f.Select(x => new BaselineFinding(x.Item1, x.Item2, x.Item3)).ToList());

    [Fact]
    public void FloorFailsBelowAndPassesAtOrAbove()
    {
        Assert.False(Gate.Evaluate(R(74), null, new GateOptions(FailBelow: 75)).Passed);
        Assert.True(Gate.Evaluate(R(75), null, new GateOptions(FailBelow: 75)).Passed);
    }

    [Fact]
    public void DropAgainstBaselineFails()
    {
        var g = Gate.Evaluate(R(80), B(90), new GateOptions(FailOnDrop: 3, FailOn: "none"));
        Assert.False(g.Passed);
        Assert.Equal(-10, g.ScoreDelta);
        Assert.True(Gate.Evaluate(R(88), B(90), new GateOptions(FailOnDrop: 3, FailOn: "none")).Passed);
    }

    [Fact]
    public void OnlyNewErrorsFailAndKnownOnesDoNot()
    {
        var known = F("WM-020", "tool:a", "error");
        var fresh = F("WM-020", "tool:b", "error");
        var baseline = B(80, ("WM-020", "tool:a", "error"));
        Assert.True(Gate.Evaluate(R(80, known), baseline, new GateOptions()).Passed);
        var g = Gate.Evaluate(R(76, known, fresh), baseline, new GateOptions());
        Assert.False(g.Passed);
        Assert.Single(g.New);
    }

    [Fact]
    public void WarningsOnlyFailWhenAsked()
    {
        var w = F("DISC-001", "/llms.txt", "warning");
        Assert.True(Gate.Evaluate(R(96, w), null, new GateOptions()).Passed);
        Assert.False(Gate.Evaluate(R(96, w), null, new GateOptions(FailOn: "warning")).Passed);
    }

    [Fact]
    public void FixedFindingsAreListedAndRendered()
    {
        var g = Gate.Evaluate(R(100), B(90, ("DISC-001", "/llms.txt", "warning")), new GateOptions());
        Assert.Single(g.Fixed);
        Assert.Contains("Fixed since the baseline", Gate.Markdown(R(100), g));
    }

    [Fact]
    public void BaselineRoundTripsAndDropsInfoFindings()
    {
        var b = Baseline.From(R(90, F("A-1", "t", "warning"), F("I-1", "t", "info")));
        var parsed = Baseline.Parse(b.ToJson());
        Assert.Single(parsed.Findings);
        Assert.Equal(90, parsed.Score);
        Assert.Throws<InvalidDataException>(() => Baseline.Parse("{\"schema\":\"other\"}"));
    }
}

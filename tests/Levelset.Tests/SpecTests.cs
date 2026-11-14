using System.Reflection;
using System.Text.RegularExpressions;

namespace Levelset.Tests;

public class SpecTests
{
    [Fact]
    public void EveryAcceptanceCheckInTheSpecHasATest()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "docs", "specs", "agent-output.md"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var spec = File.ReadAllText(Path.Combine(dir!, "docs", "specs", "agent-output.md"));
        var wanted = Regex.Matches(spec, @"^- (AC-[A-Z0-9]+-\d+):", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToHashSet();
        var have = typeof(SpecTests).Assembly.GetTypes().SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetCustomAttributes<AcAttribute>()).Select(a => a.Id).ToHashSet();
        Assert.NotEmpty(wanted);
        Assert.Empty(wanted.Except(have));
        Assert.Empty(have.Except(wanted));
    }
}

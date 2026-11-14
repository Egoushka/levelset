using System.Text;

namespace Levelset;

/// <summary>Builds the instruction a coding agent can apply for a finding. It never asks for a tool to be run.</summary>
public static class FixPrompts
{
    public static string ForCheck(string site, CheckInfo info, IReadOnlyList<Problem> problems, string? dotnet)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Fix {info.Id} ({info.Title}) for {site}.");
        foreach (var p in problems.Take(10)) sb.AppendLine($"- {p.Target}: {p.Message}");
        sb.AppendLine($"What to do: {info.FixText}");
        if (info.FixSnippet is { Length: > 0 }) sb.AppendLine($"Start from this snippet:\n{info.FixSnippet}");
        if (dotnet is { Length: > 0 }) sb.AppendLine($"For ASP.NET Core:\n{dotnet}");
        sb.AppendLine($"Reference: {info.Spec}");
        sb.Append($"Done when `levelset check {site}` no longer reports {info.Id}.");
        return sb.ToString();
    }

    /// <summary>One prompt covering several fixes in the order given.</summary>
    public static string Combine(string site, IEnumerable<(string Id, string Prompt)> fixes, int max = 10)
    {
        var list = fixes.Take(max).ToList();
        if (list.Count == 0) return $"Nothing to fix on {site}.";
        var sb = new StringBuilder();
        sb.AppendLine($"Make {site} more ready for AI agents. Apply these {list.Count} fixes in order, then re-run `levelset check {site}`.");
        for (var i = 0; i < list.Count; i++)
        {
            sb.AppendLine();
            sb.AppendLine($"{i + 1}. {list[i].Prompt}");
        }

        return sb.ToString().TrimEnd();
    }
}

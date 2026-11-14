using System.Globalization;

namespace Levelset.Web;

/// <summary>A flat two-part SVG badge: "agent ready" and the grade with the score.</summary>
public static class Badge
{
    private const string Label = "agent ready";

    public static string Svg(Report? report)
    {
        var value = report is null ? "unknown" : $"{report.Grade} {report.Score}";
        var color = report?.Grade switch
        {
            "A" => "#2e7d32",
            "B" => "#558b2f",
            "C" => "#b7791f",
            "D" => "#c05621",
            "F" => "#c53030",
            _ => "#6b7280",
        };
        var left = Width(Label);
        var right = Width(value);
        var total = left + right;
        return string.Create(CultureInfo.InvariantCulture,
            $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{total}" height="20" role="img" aria-label="{Label}: {value}">
            <title>{Label}: {value}</title>
            <rect width="{left}" height="20" rx="3" fill="#374151"/>
            <rect x="{left}" width="{right}" height="20" rx="3" fill="{color}"/>
            <rect x="{left}" width="4" height="20" fill="{color}"/>
            <g fill="#fff" font-family="Verdana,DejaVu Sans,sans-serif" font-size="11" text-anchor="middle">
            <text x="{left / 2}" y="14">{Label}</text>
            <text x="{left + right / 2}" y="14">{value}</text>
            </g>
            </svg>
            """);
    }

    private static int Width(string text) => text.Length * 7 + 12;
}

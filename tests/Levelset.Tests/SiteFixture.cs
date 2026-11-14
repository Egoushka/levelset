using System.Text.Json;

namespace Levelset.Tests;

/// <summary>A saved site: the responses a crawl would have seen. Tests never touch the network.</summary>
public static class SiteFixture
{
    private sealed record Route(int Status, string ContentType, string Body, Dictionary<string, string>? Headers);

    private sealed record Doc(string Url, Dictionary<string, Route> Routes);

    public static async Task<Report> ScanAsync(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json");
        var doc = JsonSerializer.Deserialize<Doc>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var fetcher = new FakeFetcher();
        var origin = new Uri(doc.Url);
        foreach (var (p, r) in doc.Routes)
            fetcher.Add(new Uri(origin, p).ToString(), r.Status, r.Body, r.ContentType,
                (r.Headers ?? []).Select(h => (h.Key, h.Value)).ToArray());
        return await new Scanner(fetcher).ScanAsync(origin);
    }
}

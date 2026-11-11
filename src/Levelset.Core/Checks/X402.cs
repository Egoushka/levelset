using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Levelset;

public static partial class Catalog
{
    private static readonly Dictionary<string, string> UsdcByNetwork = new()
    {
        ["eip155:8453"] = "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913",
        ["eip155:84532"] = "0x036CbD53842c5426634e7929541eC2318f3dCF7e",
    };

    private static IEnumerable<(X402Challenge C, JsonObject A)> Accepts(Snapshot s) =>
        (s.X402?.Challenges ?? []).SelectMany(c => (c.Body["accepts"] as JsonArray ?? []).OfType<JsonObject>().Select(a => (c, a)));

    private static IEnumerable<Check> X402Checks()
    {
        const Category X = Category.X402;
        yield return Def("X4-001", X, Severity.Info, CheckStatus.DeFacto, "Paid surface detected", X402Spec,
            "Detected only: a 402 response or a paid MCP tool.",
            s => s.X402 is { } x
                ? Many(x.Challenges.Select(c => new Problem(c.Source.AbsolutePath, "HTTP 402 challenge"))
                    .Concat(x.PaidToolHints.Select(t => new Problem("tool:" + t, "paid MCP tool hint"))))
                : null);
        yield return Def("X4-010", X, Severity.Error, CheckStatus.DeFacto, "The 402 parses as a v2 PaymentRequired", X402Spec,
            "Return the PaymentRequired object (x402Version: 2, accepts: [...]) as JSON in the body and base64 in the PAYMENT-REQUIRED header.",
            s => s.X402 is { Challenges.Count: > 0 } x ? Many(x.Challenges.SelectMany(ChallengeProblems)) : null,
            snippet: "{ \"x402Version\": 2, \"accepts\": [{ \"scheme\": \"exact\", \"network\": \"eip155:84532\", \"asset\": \"0x036CbD53842c5426634e7929541eC2318f3dCF7e\", \"amount\": \"10000\", \"payTo\": \"0x...\", \"maxTimeoutSeconds\": 60 }] }");
        yield return Def("X4-011", X, Severity.Error, CheckStatus.DeFacto, "Each accepts entry is complete and valid", X402Spec,
            "Give every accepts entry scheme, network (CAIP-2 such as eip155:84532), asset, amount (a positive integer string in base units), payTo and maxTimeoutSeconds.",
            s => s.X402 is { Challenges.Count: > 0 } ? Many(Accepts(s).SelectMany(t => AcceptProblems(t.C, t.A))) : null);
        yield return Def("X4-012", X, Severity.Warning, CheckStatus.DeFacto, "USDC price is sane", X402Spec,
            "USDC has 6 decimals: 10000 is 0.01 USDC. A price above 10 USDC or below 0.0001 for a read is usually a unit mistake.",
            s => s.X402 is { Challenges.Count: > 0 } ? Many(Accepts(s).SelectMany(t =>
            {
                var net = t.A["network"]?.ToString() ?? "";
                var known = UsdcByNetwork.TryGetValue(net, out var usdc) && string.Equals(t.A["asset"]?.ToString(), usdc, StringComparison.OrdinalIgnoreCase);
                if (!known || !BigInteger.TryParse(t.A["amount"]?.ToString(), out var amount)) return [];
                return amount > 10_000_000 || amount < 100 ? [new Problem(t.C.Source.AbsolutePath, $"amount {amount} is {(decimal)amount / 1_000_000m} USDC")] : Array.Empty<Problem>();
            })) : null);
        yield return Def("X4-013", X, Severity.Error, CheckStatus.DeFacto, "The asset is a known contract for its network", X402Spec,
            "Use the official USDC contract: Base 0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913, Base Sepolia 0x036CbD53842c5426634e7929541eC2318f3dCF7e.",
            s => s.X402 is { Challenges.Count: > 0 } ? Many(Accepts(s).SelectMany(t =>
            {
                var net = t.A["network"]?.ToString() ?? "";
                var asset = t.A["asset"]?.ToString() ?? "";
                if (!UsdcByNetwork.TryGetValue(net, out var usdc) || string.Equals(asset, usdc, StringComparison.OrdinalIgnoreCase)) return [];
                var claimsUsdc = (t.A["extra"]?["name"]?.ToString() ?? "").Contains("USD", StringComparison.OrdinalIgnoreCase);
                return claimsUsdc ? [new Problem(t.C.Source.AbsolutePath, $"{asset} is not USDC on {net}")] : Array.Empty<Problem>();
            })) : null);
        yield return Def("X4-020", X, Severity.Error, CheckStatus.DeFacto, "The facilitator supports the offered scheme and network", X402Spec,
            "Point at a facilitator whose /supported lists the (scheme, network) pair you offer.",
            s =>
            {
                if (s.X402 is not { Facilitator: not null, FacilitatorSupported: { Ok: true } sup }) return null;
                var kinds = (Collector.TryParse(sup.Body)?["kinds"] as JsonArray ?? []).Select(k => (k?["scheme"]?.ToString(), k?["network"]?.ToString())).ToHashSet();
                return Many(Accepts(s).Where(t => !kinds.Contains((t.A["scheme"]?.ToString(), t.A["network"]?.ToString())))
                    .Select(t => new Problem(t.C.Source.AbsolutePath, $"{t.A["scheme"]}/{t.A["network"]} is not in the facilitator's /supported")));
            });
        yield return Def("X4-021", X, Severity.Warning, CheckStatus.DeFacto, "The challenge says what is being bought", X402Spec,
            "Include resource { url, description, mimeType } in the PaymentRequired so an agent knows what it pays for.",
            s => s.X402 is { Challenges.Count: > 0 } x ? Many(x.Challenges.Where(c =>
                    c.Body["resource"] is not JsonObject r || r["url"] is null || r["description"] is null || r["mimeType"] is null)
                .Select(c => new Problem(c.Source.AbsolutePath, "resource.url, description or mimeType is missing"))) : null);
        yield return Def("X4-030", X, Severity.Info, CheckStatus.DeFacto, "Network is a testnet", X402Spec,
            "Informational: the price is unchanged on mainnet.",
            s => s.X402 is { Challenges.Count: > 0 } ? Many(Accepts(s).Where(t => Collector.IsTestnet(t.A["network"]?.ToString() ?? ""))
                .Select(t => new Problem(t.C.Source.AbsolutePath, t.A["network"] + " is a testnet"))) : null);
    }

    private static IEnumerable<Problem> ChallengeProblems(X402Challenge c)
    {
        var path = c.Source.AbsolutePath;
        if (c.Body["x402Version"] is not JsonValue v || !v.TryGetValue<int>(out var ver)) yield return new Problem(path, "x402Version is missing");
        else if (ver != 2) yield return new Problem(path, $"x402Version is {ver}, expected 2");
        else if (c.Body["accepts"] is not JsonArray { Count: > 0 }) yield return new Problem(path, "accepts is missing or empty");
    }

    private static IEnumerable<Problem> AcceptProblems(X402Challenge c, JsonObject a)
    {
        var path = c.Source.AbsolutePath;
        var network = a["network"]?.ToString() ?? "";
        foreach (var f in new[] { "scheme", "network", "asset", "amount", "payTo" })
            if (string.IsNullOrWhiteSpace(a[f]?.ToString())) yield return new Problem(path, $"accepts entry has no {f}");
        if (network.Length > 0 && !network.Contains(':')) yield return new Problem(path, $"network \"{network}\" is not CAIP-2");
        if (a["amount"] is { } amt && (!BigInteger.TryParse(amt.ToString(), out var n) || n <= 0 || amt.ToString().Any(ch => !char.IsAsciiDigit(ch))))
            yield return new Problem(path, "amount is not a positive integer string");
        if (a["maxTimeoutSeconds"] is null) yield return new Problem(path, "accepts entry has no maxTimeoutSeconds");
        foreach (var f in new[] { "asset", "payTo" })
            if (a[f]?.ToString() is { Length: > 0 } addr && !ValidAddress(network, addr))
                yield return new Problem(path, $"{f} \"{addr}\" is not a valid address for {network}");
    }

    private static bool ValidAddress(string network, string addr) =>
        network.StartsWith("eip155:", StringComparison.Ordinal) ? EvmAddress().IsMatch(addr)
        : !network.StartsWith("solana:", StringComparison.Ordinal) || SolAddress().IsMatch(addr);

    [GeneratedRegex("^0x[0-9a-fA-F]{40}$")]
    private static partial Regex EvmAddress();

    [GeneratedRegex("^[1-9A-HJ-NP-Za-km-z]{32,44}$")]
    private static partial Regex SolAddress();
}

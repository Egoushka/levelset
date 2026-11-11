# Levelset

> Paste a URL, get a score out of 100 for how ready the site is for AI agents, with a fix for every finding.

Levelset checks the things agents run into: WebMCP tools, MCP servers, x402 payment challenges, `llms.txt`,
`robots.txt`, markdown negotiation and basic hygiene. Every finding carries an ID, a severity, a link to the spec
it comes from and a copy-paste fix. The checker reads descriptors only: it never runs a tool, submits a form or
sends a payment.

## Use it

Web page and API (run `dotnet run --project src/Levelset.Web`):

```
GET /api/v1/check?url=https://example.com      # JSON, schema levelset/v1
POST /mcp                                      # MCP, tool check_site(url)
```

The report:

```json
{ "schema": "levelset/v1", "url": "...", "score": 78, "grade": "B",
  "categories": { "webmcp": 82, "mcp": null, "x402": null, "discovery": 70, "hygiene": 95 },
  "findings": [ { "id": "WM-020", "severity": "error", "target": "tool:delete_note", "message": "...",
                  "fix": { "text": "...", "snippet": "...", "dotnet": "..." }, "spec": "https://..." } ],
  "passed": ["DISC-001"], "browser": { "mode": "shim", "chrome": "..." }, "durationMs": 7400 }
```

## How it scores

Each category starts at 100. A failing check costs once, whatever the number of tools it hit: an error 12, a
warning 4, info 0; a check backed by a draft or vendor spec costs at most 3 (all WebMCP checks are draft). The total
is the weighted mean of the categories that apply: WebMCP 30, MCP 20, x402 15 (only when the site has a paid
surface), Discovery 25, Hygiene 10. A category with nothing to check has no score and its weight goes to the rest.

## Checks

| Category | IDs |
|---|---|
| WebMCP (browser) | WM-001 tools found, WM-002 `document.modelContext`, WM-010 name, description, execute, WM-011 input schema, WM-012 description quality, WM-013 property descriptions, WM-014 output schema, WM-020 state-changing tool marked consequential, WM-021 auto-submitting form, WM-022 read-only contradiction, WM-023 untrusted content hint, WM-030 more than 40 tools, WM-033 late registration |
| MCP | MCP-001 discovery, MCP-002 initialize, MCP-003 card matches tools, MCP-010 tools/list, MCP-011 descriptions, MCP-012 annotations, MCP-013 payload size, MCP-020 destructive tool on an open server, MCP-021 foreign Origin, MCP-030 OAuth metadata |
| x402 | X4-001 paid surface, X4-010 PaymentRequired v2, X4-011 accepts entries, X4-012 price sanity, X4-013 known asset, X4-020 facilitator supports the pair, X4-021 resource description, X4-030 testnet |
| Discovery | DISC-001 llms.txt, DISC-002 llms-full.txt, DISC-003 llms-txt link, DISC-010 robots.txt, DISC-011 Content Signals, DISC-020 markdown negotiation, DISC-021 sitemap, DISC-030 API catalog, DISC-031 agent card, DISC-040 server-rendered content, DISC-041 JSON-LD |
| Hygiene | HYG-001 HTTPS and mixed content, HYG-002 bot challenge, HYG-004 response time |

WebMCP detection loads the page in headless Chrome with a recorder injected before any page script. If the browser
has no `document.modelContext`, the recorder supplies one that only records descriptors, so detection does not
depend on Chrome's rollout stage. It never calls a tool and never submits a form.

## Safe fetching

Every request goes through one fetcher that resolves the name itself, refuses loopback, private, link-local and
tailnet addresses on every connection (so redirects are covered), caps bodies at 5 MB and time at 15 seconds. The
browser blocks the same addresses and downloads. The hosted service refuses local targets; run the Action for those.

## GitHub Action

Runs the engine inside your runner, so private and localhost sites are free and nothing leaves your CI. No account
and no token.

```yaml
- uses: Egoushka/levelset@v1
  with:
    url: http://127.0.0.1:5080/
    start-command: dotnet run --project src/MySite --urls http://127.0.0.1:5080
    fail-below: 75          # absolute floor
    fail-on-drop: 3         # points lost against the baseline
    fail-on: error          # a new finding at this level fails (error, warning, none)
    baseline: levelset-baseline.json
```

Create the baseline once with `update-baseline: true` (or `levelset check <url> --update-baseline`) and commit the
file. After that only new findings fail the run; findings already in the baseline do not. On pull requests the Action
posts one comment with the new and fixed findings (it needs `pull-requests: write`; without it the result stays in the
job summary). The WebMCP check uses the Chrome on the runner; set `browser: none` to skip it. The same thing runs
locally as `dotnet run --project src/Levelset.Cli -- check <url> --fail-below 75`.

## Levelset on itself

`DogfoodTests` starts the site, scans it with the real engine and Chrome, and requires a score of 100. A change that
loses points fails CI.

## Free and paid

Essentials are free and stay free: the web check, every catalog check, fix hints, JSON output, and the GitHub
Action, which runs in your own CI, so private and localhost sites cost nothing and nothing leaves your runner.
Only the hosted scanning of private sites is paid, because it runs on a server we pay for.

## Develop

```
dotnet build
pwsh tests/Levelset.Tests/bin/Debug/net10.0/playwright.ps1 install chromium   # once, for the browser tests
dotnet test
```

Set `LEVELSET_CHROME_PATH` to use a Chrome or Chromium binary you already have, or `LEVELSET_CHROME_CHANNEL=chrome`
for installed stable Chrome.

## License

Source-available under [FSL-1.1-ALv2](LICENSE.md): use it, modify it, self-host it; each release becomes Apache 2.0
two years after it ships. Contributions need a DCO sign-off, see [CONTRIBUTING.md](CONTRIBUTING.md).

Related: Handrail is the .NET library for exposing WebMCP tools; Levelset suggests it as the fix on .NET sites.

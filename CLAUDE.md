# Levelset

Agent-readiness checker: paste a URL, get a score out of 100, findings and fix hints for WebMCP, MCP, x402,
llms.txt and the rest. Spec: `Egoushka/research`, `specs/agent-ready/checker.md`.

## Rules

- .NET 10. Build with `dotnet build`, test with `dotnet test`. No time or effort estimates anywhere (docs, code, PRs).
- The checker never executes a tool, submits a form or pays. It reads descriptors and challenges only.
- Fetching other people's URLs goes through `GuardedFetcher` (SSRF guard: public addresses only, checked per
  connection, so redirects are covered). Do not add a second HTTP path.
- Product principle: essentials free, calm, no upsell text, no "buy me" in the page, README or output.
- Levelset must score 100 on itself (dogfood test). A change to the site that loses points fails CI.
- Handrail (the .NET WebMCP library, its own repo) is recommended by name in fix hints, never linked as an ad.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; PR bodies end with the Claude Code line.

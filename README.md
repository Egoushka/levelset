# Levelset

> Paste a URL, get a score out of 100 for how ready the site is for AI agents, with a fix for every finding.

Levelset checks the things agents run into: WebMCP tools, MCP servers, x402 payment challenges, `llms.txt`,
`robots.txt`, markdown negotiation and basic hygiene. Every finding carries an ID, a severity, a link to the spec
it comes from and a copy-paste fix. The checker reads descriptors only: it never runs a tool, submits a form or
sends a payment.

Status: early. The scoring model and the check catalog are in `src/Levelset.Core`.

## Free and paid

Essentials are free and stay free: the web check, every catalog check, fix hints, JSON output, and the GitHub
Action, which runs in your own CI, so private and localhost sites cost nothing and nothing leaves your runner.
Only the hosted scanning of private sites is paid, because it runs on a server we pay for.

## Name and license

Source-available under [FSL-1.1-ALv2](LICENSE.md): use it, modify it, self-host it; each release becomes Apache 2.0
two years after it ships. Contributions need a DCO sign-off, see [CONTRIBUTING.md](CONTRIBUTING.md).

Related: Handrail is the .NET library for exposing WebMCP tools; Levelset
suggests it as the fix on .NET sites.

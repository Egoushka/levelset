# Agent output: ranked fixes and fix prompts

Proposals P01 (weighted categories, letter grade, output an agent can act on) and P02 (each finding carries a prompt
to fix it) of the agent-readiness funnel. P04 (badge) is at the end.

The weighted categories and the letter grade already exist (`Scorer`). This spec adds what an agent needs to act.

## Model

- `Finding.gain`: points of the total score that fixing the finding's check would add, to one decimal. Computed by
  re-scoring without that check. Info findings gain 0.
- `Fix.prompt`: a self-contained instruction for a coding agent: the site, the check ID and title, where it failed
  and why, the fix, the optional snippet and .NET variant, the spec, and a done condition. No tool is run by it.
- `Report.fixes`: one entry per failing scored check, ranked by gain (then severity, then ID): `rank`, `id`,
  `title`, `severity`, `category`, `gain`, `targets`, `prompt`.
- Agent format, schema `levelset-agent/v1`: `url`, `score`, `grade`, `passed` (no error-level finding), `categories`
  (score, grade, weight each), `fixes` (as above) and `prompt` (one instruction covering the top 10 fixes in
  rank order). Same data as the report, smaller.

## Where it shows

| Surface | Behaviour |
|---|---|
| Web page | grade and score, a "+N points" label and a copy-prompt button on each finding, one copy button for all fixes |
| API | `GET /api/v1/check?url=...` returns `fixes` and per-finding `gain` and `fix.prompt`; `&format=agent` returns the agent format |
| MCP | `check_site` takes optional `format` (`full` default, `agent`) |
| CLI | `--format text\|agent`; with no flag, agent JSON when stdout is redirected, text on a terminal; `--agent <file>` writes the agent JSON |
| Action | passes `--agent` and exposes it as output `agent-report`; the PR comment shows the points gained per new finding and a prompt for them |

## Acceptance checks

Each is a test carrying `[Ac("<id>")]`; `SpecTests` fails when an id here has no test.

- AC-P01-1: with no failing check the score is 100; a report with no fixes gives an agent report that passed and says there is nothing to fix.
- AC-P01-2: gain equals the total score drop avoided by removing that check (exact, one decimal), and fixes are sorted by gain, then severity, then ID.
- AC-P01-3: the saved fixture `bare-site` produces the expected score, grade and ranked fix IDs (no live crawl).
- AC-P01-4: the agent format has schema `levelset-agent/v1`, per-category grade and weight, `passed` false when any error exists, and a combined prompt that names the top fixes in rank order.
- AC-P01-5: the CLI picks agent JSON when output is redirected and text when `--format text`; unknown format is a usage error.
- AC-P02-1: every catalog check yields a non-empty prompt containing the check ID, the target, the fix text and a done condition.
- AC-P02-2: the .NET variant and snippet appear in the prompt only when present for the site.
- AC-P02-3: the saved fixture `bare-site` prompts carry the fix, the snippet and the done condition, and none asks to run a tool, submit a form or pay.
- AC-API-1: `/api/v1/check?format=agent` returns the agent format; no format returns the full report with fixes; a bad format is 400.
- AC-MCP-1: `check_site` with `format: agent` returns the agent format; the tool schema lists `format`.
- AC-ACT-1: the PR comment lists gain per new finding and a prompt block, capped at 10 findings.
- AC-P04-1: `/badge.svg?url=` returns an SVG with the grade and score; an unreachable or refused URL returns a grey "unknown" badge, never an error body.
- AC-BADGE-2: a badge read does not depend on the caller's daily scan limit: when the limit is spent and the 10 minute cache is cold, `/badge.svg` shows the last score seen for that address, not "unknown".
- AC-DOG-1: Levelset still scores 100 on itself (`DogfoodTests`).

## Not in this change

The public leaderboard of P04 needs stored scores and an opt-in flow; it is Yehor's call and is not built here.

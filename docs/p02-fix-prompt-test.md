# P02 test: do the fix prompts close findings?

Test from the proposal "Each finding carries a prompt to fix it": apply 10 prompts to a test site and check the findings close.

## Method
- 10 throwaway static fixture sites (no real third-party site), each with a different mix of gaps: bare SPA shell, no robots/sitemap/llms.txt, robots without a sitemap, llms.txt without a link, WebMCP tools with a missing description, an unmarked state-changing tool, a read-only hint on a delete tool, a too-short description.
- Scanned with the CLI (`levelset check`, real engine and Chrome). The agent-format `prompt` (the combined top-ten prompt) was given to one Sonnet subagent per site, with nothing else. The subagent edited only its site folder and could not run Levelset.
- Rescanned. A finding counts as closed when its ID no longer appears (errors and warnings only; info is ignored).

## Result
| | Findings |
|---|---|
| Before | 55 |
| Closed by the prompts | 45 (82%) |
| Still open | 11 |
| of which DISC-020 (markdown negotiation) | 10 |
| New after the edits | 1 (WM-020 on s07) |

Scores went from 86–91 to 97–98 on every site.

## What did not close
- **DISC-020, 10 of 10 sites.** The fixture server is static and cannot answer `Accept: text/markdown`, and each subagent correctly said so and skipped it. This is a limit of the fixtures, not of the prompt. Excluding it, the prompts closed 45 of 45 findings the fixtures could close.
- **WM-020 on s07, a real prompt gap.** The WM-022 prompt (read-only hint contradicts a state-changing tool) led the subagent to delete the `readOnlyHint`. That uncovered WM-020: a `delete_message` tool with no consequential mark. The WM-022 fix text should say to mark the tool consequential rather than only drop the hint.

## Follow-ups
- Test DISC-020 with a fixture server that negotiates content, so the prompt for it is tested too.
- Change the WM-022 fix text as above.

Fixtures and scripts: `scratch/levelset-p02/` in the workspace (not committed).

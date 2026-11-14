# Levelset

Paste the address of a site and see how ready it is for AI agents. You get a score out of 100, and for every finding a fix you can copy.

Each finding shows the points fixing it would add and a prompt a coding agent can apply. Check a site with `GET /api/v1/check?url=https://example.com` (add `&format=agent` for the grade and ranked fixes only), or call the `check_site` tool on the MCP endpoint at `/mcp`.

It looks at WebMCP tools, an MCP server, an x402 payment challenge, llms.txt, robots.txt, a sitemap, markdown for agents, structured data and basic hygiene. It only reads: it never runs a tool, submits a form or sends a payment. The check is free and needs no account.

# About Levelset

Levelset checks how ready a website is for AI agents. It fetches your pages with the user agent `Levelset/1`, loads the page once in a headless browser to see which WebMCP tools it registers, and talks to an MCP endpoint if it finds one. It refuses private and local addresses. It never runs a tool, submits a form or sends a payment.

To check a private or local site, run the GitHub Action in your own CI. The source is at github.com/Egoushka/levelset under the FSL-1.1-ALv2 license.

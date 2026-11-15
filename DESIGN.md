# Design

Levelset's page is plain and calm: system fonts, one accent, no decoration. Tokens live in `src/Levelset.Web/wwwroot/style.css`.

## Tokens
- Colour (OKLCH, light and dark under `prefers-color-scheme`): `--bg`, `--surface`, `--line`, `--text`, `--muted`, `--accent`, `--on-accent`, `--error`, `--warning`.
- Spacing: `--space-1` to `--space-5` (4 px base steps).
- Type: system-ui; weights 400 body, 600 labels and buttons, 700 figures and headings.

## Kept on purpose
- No upsell text, no "buy me", no gradients or glow (2026-11-15, from the product principle).
- One solid primary action (Check); copy buttons are outlined (2026-11-15).

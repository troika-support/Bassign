# Theme kit

Ready-made theme and tokens for Bassign, built from `docs/design-system.md`. Contrast is
script-verified AAA for text.

## Files
- `BassignTheme.cs` — the custom MudTheme (palette light/dark, layout, typography). Copy into
  `src/Bassign.Web/Theme/` and pass to `<MudThemeProvider Theme="@BassignTheme.Build()" />`.
- `tokens.css` — CSS variables for the shared RCL field components and any raw CSS. Import once
  (e.g. in the web app host and the MAUI `index.html`).
- `motion.css` — motion helper + the mandatory `prefers-reduced-motion` reset. Import after tokens.
- `logo.svg` — PLACEHOLDER wordmark; replace with the real logo before launch.
- `components/` — signature shared components (StatusChip, FieldTaskView) + `field.css`.

## Brand accent
At runtime, read the organization's `PrimaryColorHex`, validate it is >= 4.5:1 against white (button
text), then (a) set `--ba-accent` on `:root` and (b) set `PaletteLight.Primary`/`PaletteDark.Primary`
on the theme before passing it to the provider. Every screen then carries that company's colour.

## Dark mode
`tokens.css` supports system dark mode and an explicit `data-theme="dark"`/`light` override. Wire
MudBlazor's `IsDarkMode` to the same signal so MudBlazor and the raw-CSS components agree.

---
description: "Blazor + MudBlazor UI rules"
applyTo: "src/Bassign.Web/**/*.razor,src/Bassign.Web/**/*.cs,src/Bassign.UI.Shared/**/*.razor,src/Bassign.UI.Shared/**/*.cs"
---

- Blazor Web App, Interactive Server render mode. MudBlazor with the custom MudTheme from
  assets/theme-kit — never raw MudBlazor defaults.
- Components get data through a service layer (typed services calling the API/Mediator), never a
  DbContext or repository directly.
- Follow docs/design-system.md: colour tokens (§2), type scale (§3), components (§6), and the
  per-screen layouts (§8). Cite the section you are building.
- Accessibility is AAA: use the tokens as given (contrast verified); status always = word + icon +
  colour, never colour alone; wrap every animation so prefers-reduced-motion makes it instant.
- Manager UI is dense (MudDataGrid, filters, bulk actions); field UI (shared RCL) is one task,
  big touch targets, calm. Never port manager density onto the phone.
- Theme the accent from the organization's brand settings at runtime; validate the accent for
  contrast before applying.

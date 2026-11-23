---
description: "Blazor Web App (Interactive Server), MudBlazor custom theme, layout, role nav, error pages."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
The manager web shell per design-system §7, themed from the organization's brand settings.

## Do
1. Create the Blazor Web App (Global Interactive Server) with cookie auth. Install the theme kit from assets/theme-kit (MudTheme + tokens CSS).
2. Build the layout: MudDrawer role-based nav (design-system §7), MudAppBar with the org logo + user menu. Error pages.
3. Load the brand accent + logo from the organization settings (set once per install); map the accent to Palette.Primary. Validate the accent for contrast (design-system §2).
4. Add a typed service layer (components call services, not repositories/DbContext).
5. Wire the shared UI.Shared RCL.

## Done when
App runs; nav reflects role; the org accent + logo show; dark mode works.

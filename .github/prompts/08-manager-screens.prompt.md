---
description: "Workload dashboard, task list, task detail, assign dialog (with AI chip stub)."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
The manager core per design-system §8.3–8.6 and spec §3.A/§3.D.

## Do
1. Workload dashboard (§8.3): metric cards (Overdue/Due today/Unassigned/On-time) + per-person workload rows. The landing screen.
2. Task list (§8.4): MudDataGrid with search, status-filter pills, bulk actions, status chips.
3. Task detail (§8.5): meta, proof photo, activity timeline.
4. Assign/reassign dialog (§8.6): AI suggestion chip offered + overridable; stub the AI call for now (prompt 13 wires it).
5. Apply design-system voice, states, spacing; use services, not data access.

## Done when
All four screens work against real data; empty/loading states render.

---
description: "Manager analytics (on-time %, trend, per-person completion, bottleneck). Clarity, not surveillance."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
Spec §3.D analytics on design-system §8.7 — no staff ranking.

## Do
1. Add analytics queries projected to DTOs: on-time %, avg time-to-done, completed count, per-person completion, workload, bottleneck detection.
2. Build §8.7 with MudChart: metric cards, on-time-by-week bars, completion-by-person bars (low = warning colour, not shamed), bottleneck callout.
3. Add an exportable weekly digest (PDF/CSV). Staff-facing views show only their own work.
4. Add HybridCache for the heavier read models.

## Tests first
On-time % matches a known fixture.

## Done when
Analytics screen renders; export produces a file; staff can't see peers.

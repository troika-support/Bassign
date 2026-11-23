---
description: "TaskItem aggregate, enums, Result type, domain events. Pure, tests first."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
The core aggregates with business rules as guarded methods (spec §3.A), no framework dependencies.

## Do
1. Model TaskItem (spec §6) with Create/AssignTo/Accept/Complete/Decline as guarded methods enforcing the status flow (§3.A.2) and idempotent Complete (§3.A.3). Raise domain events.
2. Add enums (TaskStatus), the Result<T> + Error types, and value objects as needed. No EF, no ASP.NET.
3. Keep rules on the entity, not in future handlers.

## Tests first
Write first: cannot reassign a Done/Declined task; Todo→Accepted→Done allowed, other transitions rejected; Complete twice is a no-op; Create rejects empty title and past due date (§3.A.5).

## Done when
Domain tests pass; Domain project has no EF/ASP.NET reference.

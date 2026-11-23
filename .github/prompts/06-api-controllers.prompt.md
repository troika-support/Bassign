---
description: "MVC controllers for the task workflow via Mediator, versioned, ProblemDetails; mobile sync endpoints."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
The top-down workflow (spec §3.A) exposed as thin controllers, plus the mobile sync contract.

## Do
1. Add commands/queries + handlers: CreateTask, AssignTask, AcceptTask, CompleteTask, DeclineTask, ListMyTasks, ListTeamWorkload. Thin controllers send them via Mediator and map Result → IActionResult/ProblemDetails.
2. Version the API (Asp.Versioning); wire Scalar for docs. Secure manager actions with the Managers policy.
3. Add sync endpoints: POST /api/v{n}/sync/push (idempotent — dedupe on outbox item id) and GET /sync/pull (delta token) for the mobile app.

## Tests first
CreateTask persists; a non-manager is forbidden; push is idempotent (same item twice → one effect).

## Done when
Endpoints respond in Scalar; workflow tests pass.

---
description: "Source-generated Mediator, validation behaviour, clock, audit, formatting, options."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
The application pipeline your conventions specify: Mediator + FluentValidation behaviour + cross-cutting.

## Do
1. Add the source-generated Mediator (martinothamar Mediator, MIT). Set up command/query/handler conventions.
2. Add a FluentValidation pipeline behaviour (validate before handling; return a Result/ProblemDetails on failure).
3. Add an audit behaviour writing AuditEntry for sensitive actions (assign, complete, invite, role change); a TimeProvider-based clock; the fixed formatting helpers (design-system §5).
4. Confirm options + ValidateOnStart for all settings groups.

## Tests first
Validation behaviour short-circuits an invalid command; audit behaviour records an entry.

## Done when
Build passes; a command flows request→validation→handler→Result; an audit row is written.

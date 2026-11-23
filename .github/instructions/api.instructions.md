---
description: "API layer rules — controllers, Mediator, repositories"
applyTo: "src/Bassign.Api/**/*.cs,src/Bassign.Application/**/*.cs,src/Bassign.Infrastructure/**/*.cs"
---

- Controllers are thin MVC controllers: model-bind, send a command/query via the source-generated
  Mediator, map the Result to IActionResult/ProblemDetails. No business logic, no data access.
- Commands/queries are records; one handler each. Validate with FluentValidation as a Mediator
  pipeline behaviour, not inside the handler.
- Data access is Repository + Unit of Work per aggregate. Do not inject DbContext into handlers;
  go through the repository. The UoW commits.
- Return ProblemDetails (RFC 9457) for failures; use the Result pattern for expected errors and
  reserve exceptions for the unexpected.
- Enforce role-based authorization at the endpoint/handler (Staff act only on their own tasks).
- Version the API (Asp.Versioning). Put cross-cutting concerns (auth, audit, validation) in
  behaviours/filters, not copied into each handler.

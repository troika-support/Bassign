---
description: "Testing rules"
applyTo: "tests/**/*.cs"
---

- xUnit, AAA pattern. Integration tests use Testcontainers (real SQL Server), not in-memory.
- Tests first for: business rules (spec §3), parsers, and integration protocols (WhatsApp).
  List the concrete cases in the test file before implementing.
- Cover the risky paths explicitly: offline sync push/pull/merge/conflict; AI parse/validate/
  degrade; WhatsApp signature + dedupe; role-based authorization (Staff can't reach manager actions).
- Use TimeProvider fakes for time-based rules (reminders, escalations, quiet hours).

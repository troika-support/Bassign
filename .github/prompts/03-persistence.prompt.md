---
description: "DbContext, EF configs with unique indexes, Repository + Unit of Work per aggregate, migration, seed."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
Data access via Repository + Unit of Work per aggregate (your convention), on SQL Server.

## Do
1. Add AppDbContext (SQL Server) with IEntityTypeConfiguration<T> per entity and the unique indexes from spec §6 (bold ones).
2. Implement Repository + Unit of Work per aggregate (ITaskRepository, IUnitOfWork, etc.) in Infrastructure; interfaces in Application. Do not expose DbContext to handlers.
3. Add a design-time factory and the initial migration as a reviewed script. Add dev seed data (an admin user + a few sample tasks).
4. Connection string via user-secrets (dev); never appsettings. Don't auto-apply migrations in production.

## Tests first
Repository round-trip; a unique-index violation surfaces as a handled conflict.

## Done when
Migration applies to LocalDB; seed runs; a repository test passes.

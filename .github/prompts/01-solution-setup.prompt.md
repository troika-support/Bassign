---
description: "Create the Clean Architecture solution, projects, central packages, build props, DI entry points."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
A building skeleton matching the stack in copilot-instructions (fixed) and the structure from prompt 00.

## Do
1. Create a .slnx solution `Bassign` with projects: Domain, Application, Infrastructure, Api, Web, Mobile, UI.Shared under src/, and test projects under tests/.
2. References point inward only (Api/Web/Mobile → Application → Domain; Infrastructure → Application).
3. Add Directory.Packages.props (central package management) with the pinned versions from the README; Directory.Build.props (nullable, implicit usings, file-scoped namespaces, warnings-as-errors); .editorconfig for C# 14.
4. Wire Serilog and a DI composition root per project (AddApplication, AddInfrastructure, AddApi).
5. Add options types with ValidateOnStart for the settings groups (DB, Auth, WhatsApp, AI) — bound from config, no secrets in source.

## Done when
`dotnet build` succeeds; all projects present; dependencies point inward; Serilog logs on startup.

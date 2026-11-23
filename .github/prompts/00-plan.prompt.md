---
description: "Read the kit and reply with a build plan, questions, and package list. No code."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
Understand the whole project before writing any code (spec, design system, integration docs, instructions).

## Do
1. Read docs/spec.md, docs/design-system.md, docs/whatsapp.md, and all of .github/.
2. Reply with: a 5-line summary; the Clean Architecture solution structure you'll create; the entity list from spec §6; and the package list (from the README version matrix).
3. List any remaining [set by CLIENT] values (most are resolved in docs/decisions.md) as numbered questions.
4. Flag the riskiest integrations (WhatsApp §12, AI §13) and confirm the build order.

## Done when
You've posted the summary, structure, entities, packages, and any questions. No code yet.

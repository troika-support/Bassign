---
description: "First-run admin setup, team & invites, settings & branding."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
First-run setup + team management (spec §3.G + §5.8–5.9).

## Do
1. First-run setup (§8.2): the first user becomes Admin and sets the organization name, logo, and brand accent with a live preview.
2. Team & invites (§8.8): staff list, invite by email+role (token + expiry, §3.G.2), pending invites.
3. Settings & branding (§8.9): org name, logo, accent (contrast-validated), notification defaults (§3.C values), proof-photo default, quiet hours, timezone.

## Tests first
Invite creates a token with the role; accepting it creates a user; an expired invite is rejected.

## Done when
An admin can set up branding and invite a teammate who can accept and sign in.

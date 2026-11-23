---
description: "ASP.NET Identity, JWT (API/mobile) + cookie (web), roles, refresh tokens, invites."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
Auth per spec §5.14: ASP.NET Identity, role-based, JWT for mobile and cookie for web.

## Do
1. Add ASP.NET Identity with ApplicationUser (DisplayName, Role, IsActive). Configure JWT bearer (API/mobile) and cookie auth (Blazor web) over the same user store.
2. Issue an access token with role claims; add rotating, hashed, server-stored refresh tokens (rotate-on-use, reuse detection) and /auth/login + /auth/refresh.
3. Policy-based authorization (Admin, Managers policies). Google + Microsoft external logins (not Facebook).
4. Invite-based user onboarding (spec §3.G): the invite token carries the role + expiry; the first user to set up the app becomes Admin.

## Tests first
Login issues a token with the right roles; refresh rotates + detects reuse; a non-manager is forbidden from a manager action.

## Done when
Auth tests pass; you can sign in and hit a protected endpoint.

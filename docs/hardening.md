# Hardening checklist (step 15)

Tick each item before sign-off. "Test" = covered by `dotnet test`; "Check" = verify by hand.

## 1. Secrets & configuration
- [x] No secrets in source: JWT key, WhatsApp/AI/SendGrid keys come from user-secrets (dev, shared store `bassign-dev`) or IIS environment variables. Dev-only placeholders are labelled `DEV ONLY`.
- [x] **ValidateOnStart** for Database, Auth, WhatsApp, Email and AI options: a weak/missing secret or a dev-only provider in production stops startup with a readable message (secrets are never echoed). *Test: SecurityTests.*
- [x] **Data Protection** keys persisted (`DataProtection:KeysPath`, default `%LOCALAPPDATA%\Bassign\keys`) and encrypted with DPAPI on Windows, so restarts don't sign everyone out.
- [x] Stored secrets are hashed, not encrypted: refresh tokens, invite tokens and WhatsApp link codes are SHA-256 hashes. Nothing reversible is stored.

## 2. Transport & access
- [x] JWT: issuer, audience, lifetime and signature validated; **HS256 only**; key ≥ 32 bytes; 30 s clock skew; **15-minute** access tokens.
- [x] Deactivated or erased users are refused **within a minute** on the API (token check), within **5 minutes** on the web (cookie and open Blazor circuits), and their refresh tokens are revoked immediately.
- [x] **CORS**: none configured on purpose. The API serves the native app only, so other browser origins are refused.
- [x] **Security headers**: nosniff, `X-Frame-Options: DENY`, Referrer-Policy, Permissions-Policy, COOP; a strict **CSP** on the web (report-only in development), `default-src 'none'` and `no-store` on the API; HSTS outside development. *Check: browser dev tools → Network → response headers.*
- [x] **Rate limits**: sign-in, token refresh and invite acceptance at 10 per minute per IP (429 after that); WhatsApp webhook at 600 per minute.
- [x] Hangfire dashboard: Admins only.
- [x] Cookie: HttpOnly, SameSite=Lax, Secure in production, 8 h sliding.

## 3. Zimbabwe Cyber and Data Protection Act (spec §7)
- [x] **Privacy notice** at `/privacy`, linked from sign-in and invite acceptance.
- [x] **Consent register** (`Consents` table, append-only): privacy notice accepted on joining; WhatsApp consent granted on link, withdrawn on unlink. Shown on Profile.
- [x] **Access / portability**: Profile → *Download my data* (JSON); API `GET /me/data`.
- [x] **Rectification**: Profile → edit name; API `PUT /me/name`.
- [x] **Erasure**: Team → *Erase data…* (Admin, deactivated members only). Anonymises the account ("Former staff member"; email, phone and password removed) and deletes their WhatsApp link and messages, notifications, tokens, invites and proof photos. Task history and the audit trail stay. *Test: ErasureTests.*
- [x] **Retention**: daily Hangfire job `retention` hard-deletes past-retention data. Defaults (config section `Retention`): WhatsApp messages 90 d, webhook inbox 30 d, notifications 90 d, AI usage 365 d, sync ops 30 d, expired/revoked tokens 30 d, invites 90 d, proof photos 365 d after Done, audit 7 years.
- [x] **No PII in logs**: emails masked (`te***@domain`), phone numbers masked, invite tokens and the webhook token redacted from request paths; erasure logs the user id only. *Test: SecurityTests.*
- [ ] **Legal** (not code): POTRAZ Data Controller licence; confirm retention periods and the AI/WhatsApp/SendGrid cross-border transfers with a local advisor; name a data-protection contact in the privacy notice.

## 4. Accessibility (WCAG 2.2 AAA)
- [x] **Contrast 7:1** for every text token in light and dark, the status chips, and the brand accent. Previously muted, success, warning, danger, info and accent were 4.6–6.3:1; all are now 7–8:1. Any accent an Admin picks is darkened at runtime until white text on it is 7:1 (web, theme and phone app). *Test: AccessibilityTests.*
- [x] **Status never colour alone**: chips carry an icon and a word (○ To do, ✓ Done, ! Overdue); metric cards say "Needs attention".
- [x] **prefers-reduced-motion**: all animations and transitions off (`motion.css`). *Check: Windows → Accessibility → Visual effects → Animation effects off, then reload.*
- [ ] *Check:* keyboard-only pass (Tab through Tasks, a task, the assign dialog, Profile) and a screen-reader spot check (Narrator).

## 5. Operations
- [x] Health: `/health/live` (process up), `/health/ready` (database, plus job-server heartbeat). Both hosts; JSON with status only, no exception details.
- [x] **Correlation id** on every log line of a request (`X-Correlation-ID` accepted if clean, else the trace id; echoed in the response header).

## Production settings (IIS environment variables)
```
Auth__JwtSigningKey        48+ random characters
WhatsApp__Provider=360dialog  WhatsApp__ApiKey  WhatsApp__BusinessNumber  WhatsApp__WebhookToken (24+ random)  WhatsApp__AppSecret
Email__Provider=sendgrid   Email__ApiKey  Email__FromAddress  Email__PublicBaseUrl=https://<web host>
Ai__ApiKey                 (or Ai__Provider=none)
DataProtection__KeysPath   e.g. D:\Bassign\keys (app pool identity needs modify rights)
```

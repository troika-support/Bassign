# Bassign — settings decisions (recommended defaults)

Resolved values for the `[set by CLIENT]`-style choices in `spec.md`, with sensible
Zimbabwe-market defaults. Each is a **recommendation to confirm or tweak**, not a fixed
commitment. (Bassign is single-tenant, deployed per client org — there is no subscription billing.)

## Technical defaults (safe to adopt as-is)

| # | Decision (spec ref) | Recommended default | Why |
|---|---|---|---|
| 1 | WhatsApp provider (§2) | **360dialog** | Official Meta BSP, popular in Africa, flat pricing; swappable behind a port. |
| 2 | Email/SMTP (§2) | **SendGrid** to start (fastest), or **Amazon SES** for lowest cost | Reliable deliverability from a Windows Server/IIS host. |
| 3 | Escalations during quiet hours (§3.C.5) | **Allow** (managers only); suppress routine reminders to staff | Escalation exists for urgency; staff still get quiet hours. Admin-configurable. |
| 4 | Hangfire edition (§8) | **Hangfire OSS (LGPL) + Hangfire.SqlServer** | Free; Pro features not needed at this scale. |
| 5 | SQL Server (§7/§8) | Dev: **LocalDB/Express** · Prod: **SQL Server Standard** on the Windows Server, or **Azure SQL** | One database per install; start free, scale up. |
| 6 | LLM provider (§3.F/§8) | **Anthropic Claude** via Microsoft.Extensions.AI — Haiku-class for routine auto-assign/summaries | Cheap per call; M.E.AI keeps provider a config detail, so Azure OpenAI stays a drop-in. |

## To confirm before launch (not a build blocker)

| # | Item (spec ref) | Note |
|---|---|---|
| 7 | WhatsApp BSP pricing (§8) | Confirm current 360dialog + Meta per-conversation rates; utility/service categories are the cheap ones you'll mostly use. |
| 8 | ZW Data Protection Act scope (§7) | **Build the baseline from v1**: consent register, data-subject access/erasure, PII encryption at rest, breach-notification process, retention. Confirm POTRAZ Data Controller *licensing* with a local legal advisor. |

## Already-defaulted settings — keep as-is unless you disagree

Reminder lead time **2h** · overdue escalation after **1h**, then **24h** cooldown · daily digest
**06:00** org-local · quiet hours **20:00–06:00** · invite expiry **7 days** · AI auto-assign
confidence threshold **0.6**. All are admin-configurable.

## Step 16b / 17 decisions (2026-11-25)

| # | Decision | Choice | Why |
|---|---|---|---|
| 9 | Who configures integrations | **Admins in Settings**; any value set as a **server environment variable overrides and locks** it | One install per client: clients own their WhatsApp number and email domain; you keep control where you manage the install. |
| 10 | Secrets set in Settings | Stored **encrypted** with Data Protection (keys on the server, not in the DB); **write-only** in the UI; every change audited; only a whitelist of keys is editable (never the DB connection, JWT key or setup code) | A leaked DB backup can't be decrypted; admins can replace a key but never read it back. |
| 11 | Email providers | **SMTP** (MailKit), **Gmail** (SMTP preset + app password), **Microsoft 365** (Graph, Mail.Send), **SendGrid**, **Off** | SMTP fits clients who already have M365/Workspace/cPanel mail; Graph avoids M365 tenants that disable SMTP AUTH. Gmail API (OAuth) skipped: Google verification for the send scope takes weeks. |
| 12 | WhatsApp providers | **360dialog**, **Meta Cloud API direct**, **Twilio**, **Off** (everyone falls back to email) | Meta direct shares 360dialog's message format; Twilio is separate work, for clients already on Twilio. |
| 13 | Outside working hours | Working days/hours + **Zimbabwe public holidays** behave like quiet hours: reminders and new-task messages wait for the next working morning; **escalations still go out** | Consistent with quiet hours (#3). |
| 14 | AI account | **Your key by default** (env at deploy); a client Admin may switch to their own provider/key; per-feature on/off | You can offer AI out of the box; clients with data-residency concerns can switch it off or use their own. |
| 15 | First Admin on a real install | **One-time setup page** on first visit, protected by a **setup code** (env var), gone once an Admin exists | No password in config; a stranger who finds a fresh install can't claim it. |
| 16 | Phone app | **One shared Play Store app**. Connects to a client by **QR code** (web Profile/Team/after invite) or **invite link** → `https://tasks.<client>/connect` (opens the app, else Play Store). https-only; shows org name + API host before sign-in. **One organisation at a time**; switching warns about unsynced changes, then clears local data. No central Bassign service in v1. | One build to maintain; anti-phishing confirmation; every install stays independent. |
| 17 | Publish | **Framework-dependent** + .NET 10 Hosting Bundle on the server | Smaller deploys; one runtime install per server. |
| 18 | Hostnames | `tasks.<client-domain>` (Web), `api.<client-domain>` (API) | Client's own domain; two IIS sites, one database. |
| 19 | Work calendar details (#13) | Default **Mon–Fri 08:00–17:00**. New-task messages and reminders wait for working time; **escalations always go out**; the **digest** keeps its own hour (quiet hours still apply) and is **skipped on days off**. A reminder that could only arrive after the deadline is dropped. Holidays are **computed in code** (General Notice 1361 of 2025 + **Munhumutapa Day, 15 Sept, from 2026**, SI 148 of 2026); a fixed-date holiday on a **Sunday** moves to the next free day; no Saturday substitute. Short-notice Government holidays and company shutdowns → Admin adds a **closure**. | No yearly data update needed for the regular list; the closure list covers what can't be predicted. If the law changes, `ZimbabweHolidays` is the one file to edit. |
| 20 | Task defaults (chunk 6) | **Decline reason required** (default on): staff pick *Too busy / Not my area / Need more information / Can't make the deadline / Other (type it)* — on WhatsApp a list after tapping Decline; the phone app always asks. **Categories**: an Admin-managed list, optional per task, filter on the Tasks page; archived, never deleted. **Proof photo by default**: an Admin switch (default off) that pre-ticks the box. **Date without a time** = due at the **end of the working day** (Work calendar, 17:00 by default). | Reasons as labels make declines reportable; one extra tap is the cost. Archiving keeps old tasks and reports correct. A 17:00 due time means reminders and escalations land while people are at work. |
| 21 | Security & privacy (chunk 7) | **Invites**: expiry 1–30 days (default 7); optional **allowed email domains** (exact match, sub-domains not included; existing members unaffected). **Sessions**: website signs out after N hours unused (default 8, 1–72, from next sign-in); phone app stays signed in N days unused (default 30, 1–90). **Retention** (deleted nightly) is Admin-editable within floors — audit trail ≥ 1 year, proof photos ≥ 30 days; technical logs (webhook inbox, sync log, tokens) stay server-only. **Privacy contact** (name/email/phone) and the live retention periods appear on the privacy notice. Deactivation still cuts access within minutes regardless. | Clients set these to their own policies and data-protection advice; floors stop an Admin from wiping the accountability record by accident. |

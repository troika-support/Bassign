# Notifications, jobs & email (step 14)

| What (spec §3.C) | When | To | Channel |
|---|---|---|---|
| New task (C.1) | on assignment | assignee | WhatsApp if linked, else email |
| Reminder (C.2) | due − 2h, skipped if Done | assignee | same |
| Escalation (C.3) | overdue 1h, then every 24h until resolved | every **Manager and Admin** (there is no reporting line yet) | same |
| Daily digest (C.4) | 06:00 org-local, with the AI summary | every Manager and Admin | same |

All times and delays are set by an Admin in **Settings**. **Quiet hours** (C.5, 20:00–06:00): everything except escalations waits until they end; a reminder that would arrive after its deadline is dropped.

## How it runs
- **Hangfire** (SQL Server, schema `hangfire`, created automatically). The **API** processes jobs; the **Web** app hosts the dashboard at `/hangfire` (Admins only). Recurring: `reminders` and `escalations` every 5 min, `daily-digest` every 15 min.
- Each job is an idempotent scan over current data; every queued notification has a **dedupe key**, so re-runs, overlaps and restarts never double-send. The digest is summarised once and sent to each manager.
- Jobs only **queue** into the outbox; the outbox sender (every 3 s) builds the message from current data at send time — a task finished in the meantime is skipped, never announced.
- The two latency-sensitive pollers (outbox sender, WhatsApp inbox) stay as lightweight background services: Hangfire's minimum interval is one minute, too slow for a WhatsApp conversation.
- `Jobs:Enabled=false` turns Hangfire off (integration tests).

## Email
- Development: `Email:Provider = pickup` writes `.eml` files to `%LOCALAPPDATA%\Bassign\mail` — double-click to open.
- Production: `Email__Provider=sendgrid`, `Email__ApiKey=SG...`, `Email__FromAddress` (a verified sender), `Email__PublicBaseUrl=https://<web host>` as IIS environment variables.
- Invites are now emailed too (link to `/invite/{token}`; the token is never logged).

## WhatsApp templates to get approved (outside the 24-hour window)
| Name | Body parameters | Quick reply |
|---|---|---|
| `task_assigned` | {{1}} title, {{2}} due | Accept, Decline |
| `task_reminder` | {{1}} title, {{2}} due | View task |
| `task_overdue` | {{1}} title, {{2}} assignee, {{3}} due | — |
| `daily_digest` | {{1}} organisation, {{2}} counts, {{3}} summary | — |
Category: **Utility**. Until a template is approved, messages that need it fail (they are **not** re-sent by email — the person *is* reachable on WhatsApp). Get all four approved before go-live; the simulator doesn't need them.

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

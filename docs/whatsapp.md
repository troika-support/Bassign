# Integration reference — WhatsApp Business Platform

Reference for the field-staff channel. Copilot won't know these rules reliably, so follow this
doc, not guesses. Provider: **360dialog or Twilio** `[set by CLIENT]` — hide behind a port so the
choice is swappable. Firmware/version-independent, but **provider payload shapes differ** — verify
field names against the chosen provider's current docs (flagged inline).

## Project rules (non-negotiable)

1. WhatsApp is a **delivery channel behind `INotificationChannel`** (Application layer). Domain
   events trigger it; the Domain never references WhatsApp.
2. **24-hour window:** free-form messages are allowed only within 24h of the user's last inbound
   message. Outside it, you MUST use a **pre-approved template**. Free text outside the window is
   silently dropped — model the window explicitly, don't attempt the send.
3. **Verify the webhook signature** before processing. Reject unverified.
4. **Idempotency:** dedupe on the provider message id (`ProcessedWebhookMessage`). Retries resend.
5. **Sender resolution:** map the sender's number → user via a **verified** `WhatsAppBinding`.
   Unknown/unverified numbers fail closed.
6. **Ack fast** (HTTP 200) and offload slow work to a Hangfire job.

## Outbound — interactive buttons (within window)

Assignment/task messages use interactive reply buttons; the command is encoded in the button id.

```jsonc
// POST {graph}/{phone-number-id}/messages   (Cloud API shape; Twilio differs — verify)
{
  "messaging_product": "whatsapp",
  "to": "<E164>",
  "type": "interactive",
  "interactive": {
    "type": "button",
    "body": { "text": "New task: {title}\nDue: {due}" },
    "action": { "buttons": [
      { "type": "reply", "reply": { "id": "accept:{taskId}", "title": "Accept" } },
      { "type": "reply", "reply": { "id": "decline:{taskId}", "title": "Decline" } },
      { "type": "reply", "reply": { "id": "done:{taskId}",    "title": "Mark done" } }
    ] }
  }
}
```

## Outbound — templates (outside window)

Reminders, escalations, digests land outside the window → pre-approved templates by name
(`task_reminder`, `task_escalation`, `daily_digest`). Register/approve them in the provider
console first (a setup step, not code). Body params are positional.

```jsonc
{ "messaging_product":"whatsapp","to":"<E164>","type":"template",
  "template": { "name":"task_reminder","language":{"code":"en"},
    "components":[ { "type":"body","parameters":[ {"type":"text","text":"{title}"},
                                                  {"type":"text","text":"{due}"} ] } ] } }
```

## Inbound — webhook

- **GET** verification: echo `hub.challenge` when `hub.verify_token` matches the configured token.
- **POST**: verify signature (`X-Hub-Signature-256` HMAC-SHA256 of the raw body with the app
  secret — *verify header name per provider*), then:
  1. dedupe on message id;
  2. resolve the sender's number to a user;
  3. parse: button reply id `action:{taskId}` → command; text → best-effort command; media → proof;
  4. dispatch the Application command; ack 200.
- Consume **status callbacks** (sent/delivered/read/failed); on `failed`, retry or escalate.

## Media (proof-of-work)

Inbound media arrives as an id. Fetch the (short-lived) media URL promptly, download, **compress**,
store, link to the task. Don't block the webhook — do it in a job.

## Enrollment (phone → user)

User adds their number in-app → send a one-time code (template or in-app) → mark
`WhatsAppBinding.Verified` on confirmation. Only verified bindings resolve to a user.

## Suggested code shape

- `INotificationChannel` (Application) — `SendTaskAssignedAsync`, `SendReminderAsync`, …
- `WhatsAppChannel : INotificationChannel` (Infrastructure) — typed `HttpClient` + resilience.
- `WhatsAppWebhookController` (Api) — verify → dedupe → dispatch. (MVC controller per conventions.)
- `WhatsAppOptions` — token, phone-number-id/sender, app secret (secrets via config / encrypted;
  never in code).

## Tests

- Signature rejection; duplicate message id processed once; button id → correct command; the sender is
  resolved from receiving number; unknown number → enrollment, no task data. Feed recorded payloads.

## Troubleshooting checklist

- Message not delivered → outside 24h window without a template? template not approved?
- Webhook 403 loop → verify token mismatch (GET) or signature check wrong (POST).
- Double actions → missing dedupe.
- Wrong user → sender-number binding wrong.
- Media 404 → media URL expired (fetch too late).

## How Bassign implements it (steps 12a + 12b)

**Out:** a task change raises a domain event → `UnitOfWork` writes an `OutboundNotifications` row in the same transaction → `NotificationWorker` (API host) picks a channel → `WhatsAppChannel` sends buttons inside the 24h window, the `task_assigned` template outside it.

**In:** `POST /webhooks/whatsapp/{WebhookToken}` (token compared in constant time; `X-Hub-Signature-256` checked when `WhatsApp:AppSecret` is set) → `WhatsAppWebhookIntake` stores each message/status in `InboundWebhookMessages` keyed by the provider id (a retried webhook is a no-op) → returns 200 at once → `WhatsAppInboxWorker` (woken by the webhook, otherwise every 2 s) → `HandleInboundWhatsAppCommand`:

| Message | Result |
|---|---|
| `LINK 123456` | verifies the number for whoever was shown that code (max 6 tries/hour per number) |
| any message from an unlinked number | enrollment prompt — never any task data |
| button / list / template reply `accept:` `decline:` `done:` `task:` + id | the matching command, **run as the linked user** (`IRunAs`) so permissions + audit apply |
| a photo | proof for the task awaiting one → `CompleteTaskCommand` with the stored proof |
| `tasks` / `hi` / `menu` | the person's open tasks (one card, or a list of up to 10) |
| anything else | short help |

Replies are sent after the inbox item is marked done, so a failed send never re-runs a command. The webhook path is redacted from request logs.

**Development:** `WhatsApp:Provider = simulator`. Run **both** hosts; open **WhatsApp simulator** in the Web app's nav. The simulator builds a real provider-shaped webhook body and puts it through the same intake, so the API worker handles it exactly like production.

**Going live (360dialog):** set `WhatsApp__Provider=360dialog`, `WhatsApp__ApiKey`, `WhatsApp__BusinessNumber`, `WhatsApp__WebhookToken` (long random), optionally `WhatsApp__AppSecret`, as environment variables on IIS — never in appsettings. Point the 360dialog webhook at `https://<api-host>/webhooks/whatsapp/<WebhookToken>`. Get the `task_assigned` template approved first (see above).

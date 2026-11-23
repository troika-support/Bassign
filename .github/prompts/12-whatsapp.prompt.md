---
description: "INotificationChannel, interactive buttons, templates/24h window, signed webhook, binding. Riskiest integration."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
The primary field surface per docs/whatsapp.md. Follow that doc, not guesses.

## Do
1. INotificationChannel (Application) triggered by domain events. WhatsAppChannel (Infrastructure) for 360dialog as a resilient typed HttpClient; options/secrets from config.
2. Outbound: interactive buttons (ids action:{taskId}); templates for reminders/digests with the 24-hour-window rule (whatsapp.md).
3. Inbound: signed webhook (verify signature; GET challenge), dedupe on message id, map the sender's number to a user, map to a command, ack fast, offload slow work to a job. Consume status callbacks.
4. Media (proof-of-work) download+compress+store. Phone→user binding with a verified one-time code; unknown numbers fail closed.

## Tests first
Signature rejection; duplicate message id processed once; button id → correct command; outside-window send uses a template; unknown number → enrollment, no task data.

## Done when
A simulated inbound Accept updates the task; an outbound assignment renders buttons.

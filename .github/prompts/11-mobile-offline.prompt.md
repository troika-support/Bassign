---
description: "MAUI Blazor Hybrid, SQLite, outbox, sync, conflict merge, camera, secure tokens, field UI."
---

Use **Agent mode**. Follow `.github/copilot-instructions.md`.

## Goal
The field app per the mobile instructions and design-system §8.11. Offline is normal.

## Do
1. Generate the MAUI Blazor Hybrid app (Android-first) referencing UI.Shared. Point the BlazorWebView at a Main root.
2. Local SQLite (sqlite-net-pcl): TaskRow/OutboxItem/SyncMeta + LocalDb. Optimistic store (ITaskStore) that writes locally + enqueues an outbox item.
3. SyncService: idempotent push + delta pull against /sync/* (prompt 06); last-write-wins that never clobbers un-pushed dirty rows (push before pull). Sync on connectivity/resume/timer.
4. Camera service (MediaPicker + compress); SecureTokenStore (access + refresh in SecureStorage). Device holds NO API keys.
5. Field task view (§8.10): one task, big Accept/Decline/Done, quiet Pending badge, 'all caught up' empty state. A 401 on sync refreshes the token and retries once.

## Tests first
Optimistic write enqueues one outbox item; push drains + dedupes; pull merges by token; a dirty local edit survives an older remote.

## Done when
App runs on an emulator; works offline; syncs on reconnect; a photo attaches to completion.

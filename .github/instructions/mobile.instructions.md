---
description: "MAUI Blazor Hybrid offline rules"
applyTo: "src/Bassign.Mobile/**/*.cs,src/Bassign.Mobile/**/*.razor"
---

- MAUI Blazor Hybrid, Android-first. UI is Razor components from UI.Shared (one UI, two hosts).
- Offline-first: local SQLite (sqlite-net-pcl) is the UI's source of truth. Mutations write
  locally AND enqueue an outbox item; a SyncService does idempotent push + delta pull when online.
- Conflict resolution is last-write-wins that never clobbers un-pushed dirty rows (push before
  pull). Optimistic UI; quiet Pending badge; never block on the network.
- The device holds NO API keys. Auth tokens (access + rotating refresh) live in SecureStorage;
  all provider calls go through the API.
- Compress proof-of-work photos before queuing (data cost). Mind payload size on sync.

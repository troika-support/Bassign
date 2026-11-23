# Bassign — Design System

Governs every surface: manager web (Blazor + MudBlazor), field mobile (MAUI Blazor Hybrid), and
WhatsApp copy. Cited by the UI prompts. Accessibility target: **WCAG AAA** for text (contrast
verified by script, §2), with `prefers-reduced-motion` honoured as a hard requirement (§4).

Built with a **full custom MudTheme** (not MudBlazor defaults). The ready-made theme lives in
`assets/theme-kit/`.

---

## 1. Brand

- **Product:** Bassign (*basa* — Shona for "work" — + *assign*). **Logo:**
  `[CLIENT logo → assets/theme-kit/logo.svg]` (placeholder wordmark shipped; replace before launch
  — never auto-generate a final logo).
- **Brand accent is per install.** The default accent is blue `#1D4ED8`; each organization sets its
  own accent via the organization settings (§2), applied at runtime. The accent is the only strong
  colour on a screen.
- **Signature feel:** two deliberately different personalities over one data set — *dense and
  authoritative* for managers, *calm and single-focus* for field staff. Clean, professional, never
  sterile; warm, human microcopy.

---

## 2. Colour tokens

Contrast ratios computed against the token's normal background (`scripts`-verified). **Level** is
the WCAG level for the role the token is used in. Design rule: **all body/secondary/meta text
reaches AAA**; accent and status colours are used for fills, chips, icons, and large/semibold
labels only (AAA-large), never as small body text — and **status is never conveyed by colour
alone** (§5), so the readable label is always `ink` or white (AAA).

**Light**

| Token | Hex | On | Ratio | Level | Use |
|---|---|---|---|---|---|
| `--ba-bg` | #FFFFFF | — | — | — | page background |
| `--ba-surface` | #F7F8FA | — | — | — | cards, table zebra |
| `--ba-ink` | #16191D | bg | 17.6 | AAA | primary text |
| `--ba-secondary` | #44505E | bg | 8.2 | AAA | secondary text |
| `--ba-muted` | #4C5864 | bg | 7.3 | AAA | meta, captions |
| `--ba-accent` | #1D4ED8 | bg | 6.7 | AAA-large | accent fills, links, icons, button bg (white text 6.7) |
| `--ba-accent-tint` | #EEF2FF | — | — | — | accent surfaces (AI chip, selected) |
| `--ba-success` | #0B7A3B | bg | 5.4 | AAA-large | done / on-time (chip + icon) |
| `--ba-warning` | #B0520A | bg | 5.2 | AAA-large | due-soon (chip + icon) |
| `--ba-danger` | #C01B1B | bg | 6.2 | AAA-large | overdue / failed (chip + icon; white-on-danger 6.2) |
| `--ba-line` | #E3E7EC | — | — | — | borders, dividers |

**Dark** (every text token reaches AAA)

| Token | Hex | On | Ratio | Level |
|---|---|---|---|---|
| `--ba-bg` | #15181C | — | — | — |
| `--ba-surface` | #1E232A | — | — | — |
| `--ba-ink` | #E7EAEE | bg | 14.8 | AAA |
| `--ba-secondary` | #AEB6C0 | bg | 8.7 | AAA |
| `--ba-muted` | #9AA4B0 | bg | 7.1 | AAA |
| `--ba-accent` | #7AA2FF | bg | 7.2 | AAA (dark text on it 7.2) |
| `--ba-success` | #56D08A | bg | 9.2 | AAA |
| `--ba-warning` | #F0A24B | bg | 8.5 | AAA |
| `--ba-danger` | #FF8585 | bg | 7.6 | AAA |

**Brand accent:** at runtime set `--ba-accent` from the organization's `PrimaryColorHex` and map it
to MudTheme `Palette.Primary`. Validate the chosen accent meets ≥4.5:1 with white (for button
text); reject or auto-darken if it fails.

---

## 3. Type scale

- **UI / body:** Inter (fallback: system UI). **Data/numerals:** Inter tabular figures
  (`font-feature-settings:"tnum"`) so columns align.
- Weights: **400** body, **500** emphasis/titles. (Two weights only.)

| Token | Size | Weight | Use |
|---|---|---|---|
| caption | 0.8rem | 400 | meta, timestamps |
| body | 1.0rem | 400 | default |
| subtitle | 1.15rem | 500 | section labels |
| h-dense | 1.25rem | 500 | manager card/section headings |
| field-title | 1.5rem | 500 | the one field task title |
| page | 2.0rem | 500 | page titles |

Field side runs larger with more spacing (glanceable, thumb-friendly); manager side runs denser.

---

## 4. Space, shape, elevation, motion

- **Spacing unit:** 4px. Scale 4 / 8 / 12 / 16 / 24 / 32.
- **Radius:** 12px field cards/buttons; 6–8px dense manager controls.
- **Elevation:** flat by default; one soft shadow level for dialogs/menus only.
- **Touch targets:** field ≥ 48px.
- **Motion:** micro-interactions are core (button press, chip accept, status change, list
  settle) — subtle and purposeful. **Hard requirement:** every animation is wrapped so that under
  `@media (prefers-reduced-motion: reduce)` it reduces to an instant state change (no transform/
  opacity transitions). Motion never gates comprehension.

---

## 5. Content

- **Voice:** active, plain, sentence case. Warm but efficient on the field side ("Done — thanks").
  Professional, not chummy; never cold.
- **An action keeps its name through the flow:** a "Mark done" button → "Marked done" confirmation.
- **Fixed formats**

| Thing | Format | Example |
|---|---|---|
| Date | ddd d MMM | Tue 20 Oct |
| Date+time | ddd d MMM, h:mm tt | Tue 20 Oct, 4:00 PM |
| Time zone | org-local | — |
| Relative | "Today" / "Overdue" | — |

- **Status vocabulary** (word + colour + icon — never colour alone)

| Word | Meaning | Light colour | Icon |
|---|---|---|---|
| To do | assigned, not started | muted | circle |
| In progress | accepted | warning | dot |
| Done | completed | success | check |
| Overdue | past due, not done | danger | alert |
| Pending | saved offline, unsynced | muted | small dot |

---

## 6. Components (build with MudBlazor)

| Component | Build with | Notes |
|---|---|---|
| App shell / nav | `MudLayout`, `MudDrawer`, `MudAppBar` | role-based nav; org logo + accent |
| Data grid (tasks) | `MudDataGrid<T>` | filters, sort, bulk actions; tabular figures |
| Metric card | custom (`MudPaper`) | label + 24px/500 number; status colour on number |
| Workload bar | custom | per-person load; colour by load, label alongside |
| Assign dialog | `MudDialog` | AI suggestion chip (accent-tint) — offered, overridable |
| Status chip | `MudChip` | word + icon + colour per §5 |
| Charts (analytics) | `MudChart` | on-time trend, completion bars |
| Field task card | shared RCL component | one task, big Accept/Decline/Done |
| Sync badge | shared RCL component | quiet "Pending" (muted), never a blocking error |
| Forms | `MudForm` + `MudTextField` | FluentValidation messages inline |
| Empty / error states | custom | §8 copy; empty = invitation, offline = silent |

Shared field components live in the **Razor Class Library** (reused by web + mobile); data-dense
manager components live in the Web project.

---

## 7. Layout

- **Manager (web, desktop-first):** left `MudDrawer` nav (Dashboard, Tasks, Analytics, Team,
  Settings for Admin), top app bar with org logo + user menu. Content is dense; the **workload
  dashboard is the landing screen**. Responsive via MudBlazor grid/breakpoints.
- **Field (mobile):** no chrome to speak of — one task, full-screen, big actions. The task *is* the
  screen.
- **Navigation by role:** Staff never see manager nav; Managers see all but Settings; Admin sees all.

---

## 8. Screens

Each screen: route, then regions top-to-bottom with real copy and states. Role differences noted.

**8.1 Sign in — `/sign-in`**
- Region 1: org logo + "Sign in to {Organization}".
- Region 2: email + password; "Continue with Google" / "Continue with Microsoft".
- Region 3: "Forgot password?" link.
- Errors: inline, plain ("That email or password doesn't match.").

**8.2 First-run setup — `/setup`** (Admin, first run)
- Stepper: 1 Organization · 2 Team · 3 Done.
- Step 1: organization name, logo upload, brand-colour swatches with a **live branded-header preview**.
- Step 2: invite teammates by email + role (skippable).
- Step 3: "You're set" → go to dashboard.

**8.3 Workload dashboard — `/`** (manager landing, anchor screen)
- Region 1: top bar — org logo, "{Organization} · Workload · today", "New task" button.
- Region 2: metric cards — Overdue (danger), Due today (warning), Unassigned (ink), On-time 30d
  (success).
- Region 3: "Team workload" — per-person row: avatar, name, role, load bar, "N active · M overdue".
- Empty: "No tasks yet — assign your first one."

**8.4 Task list — `/tasks`**
- Region 1: search + status filter pills (All / To do / In progress / Overdue).
- Region 2: `MudDataGrid` — Task, Assignee, Due, Status chip. Row → 8.5. Bulk assign/complete.
- Empty: "No tasks match these filters" + clear-filters.

**8.5 Task detail — `/tasks/{id}`**
- Header: title + status chip. Meta: assignee, due/completed, location.
- Proof-of-work photo (if present). Activity timeline (assigned → accepted → done, with channel).
- Manager actions: reassign, edit, mark done.

**8.6 Assign / reassign (dialog)**
- Title "Assign task" + task name.
- **AI suggestion** card (accent-tint): "Suggested · {name} · {reason}" + Accept. Offered, not
  forced; shows confidence-driven reasoning.
- "Or choose someone" list with each person's active count. Cancel / Assign.

**8.7 Accountability analytics — `/analytics`**
- Metric cards: On-time %, Avg time-to-done, Completed.
- On-time rate by week (bar). Completion by person (bars; low values warning-coloured, not shamed).
- Bottleneck callout (accent-tint): e.g. "{site} tasks slip most often." **No staff ranking.**

**8.8 Team & invites — `/team`**
- List of staff (name, role, status). Invite form (email + role). Pending invites with expiry.

**8.9 Settings & branding — `/settings`** (Admin)
- Organization name, logo, accent colour (with contrast validation), notification defaults (§3.C
  values), proof-photo default, quiet hours, timezone.

**8.10 Field task view — `/` (mobile)**
- One actionable task: title, detail, due. Big **Accept / Decline** (Todo) or **Mark done /
  Add photo** (Accepted). Quiet "Pending" badge when unsynced.
- Empty: "You're all caught up — new tasks will appear here." Offline: silent, no error.

**8.11 Invite accept — `/invite/{token}`**
- Branded "Join {Organization}" + role, big "Accept invite" → straight to first task (field) or
  dashboard (manager). Time-to-first-value is the metric; no tour.

**8.12 WhatsApp task flow** (conversation)
- Assignment: short body (title + due) + Accept / Decline / Done buttons.
- Confirmations: one warm line ("Accepted — due Tue. Send a photo here when it's done.").
- Outside the 24-hour window: approved templates only (see `docs/whatsapp.md`).
- Unknown number: enrollment prompt, never task content.

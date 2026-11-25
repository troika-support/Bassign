# Deploying Bassign on IIS (step 17)

One install per client organisation (decision #9). Each install is **two IIS sites on one Windows Server,
sharing one SQL Server database**:

| Site | Address | What it is |
|---|---|---|
| Bassign Web | `https://tasks.<client-domain>` | The web app (Blazor Server), Settings, the jobs dashboard, `/connect` for phones |
| Bassign API | `https://api.<client-domain>` | The phone app's API, WhatsApp webhooks, and the background jobs (reminders, digests, retention) |

Published **framework-dependent** for the **.NET 10 Hosting Bundle** (decision #17). Everything below is
scripted in `deploy/`; the scripts are in every release package.

---

## 1. Before you start (per client)

- [ ] **Windows Server 2019 or later**, with an administrator account and remote access.
- [ ] **SQL Server** (Express is fine to start; Standard or Azure SQL for larger clients):
  - Create an empty database, e.g. `Bassign`.
  - Create a login and user for the app. It needs **`db_owner` on this database only**, because migrations
    and the background-jobs tables (Hangfire) create their own schema. Never use `sa`.
  - Connection string, e.g.
    `Server=SQL01;Database=Bassign;User Id=bassign_app;Password=…;Encrypt=true;TrustServerCertificate=true`
- [ ] **DNS:** `tasks.<client-domain>` and `api.<client-domain>`, both pointing at the server.
- [ ] **A TLS certificate** covering both names (a SAN or wildcard certificate), imported into
  *Local Computer → Personal* (`LocalMachine\My`). Note its thumbprint. Let's Encrypt via
  [win-acme](https://www.win-acme.com/) works and renews itself.
- [ ] **Firewall:** 443 open (and 80, which only redirects to https).
- [ ] **.NET 10 Hosting Bundle** installed (dotnet.microsoft.com → .NET 10 → ASP.NET Core Runtime → *Hosting Bundle*).
  Then run `net stop was /y` and `net start w3svc`.
- [ ] Decide the data folder, e.g. `D:\Bassign` (not the system drive if you can avoid it).

## 2. Build the release (on your PC)

```powershell
.\deploy\Build-Release.ps1            # runs unit tests, publishes both apps, builds the migration bundle
```

This produces `artifacts\bassign-<date>-<commit>.zip` containing `web\`, `api\`, `migrations\efbundle.exe`,
`deploy\` (scripts and this runbook) and `VERSION.txt`. Development settings are never included.
It needs the `dotnet-ef` tool (`dotnet tool install -g dotnet-ef`).

Copy the zip to the server, for example to `D:\Bassign\releases\`, and unzip it once to get the scripts.

## 3. Prepare the server (once)

In an **elevated** PowerShell, from the unzipped `deploy` folder:

```powershell
.\Install-BassignServer.ps1 -WebHost tasks.mintedmoments.co.zw -ApiHost api.mintedmoments.co.zw `
    -CertThumbprint 1A2B3C4D... -Root D:\Bassign
```

This does the following:

- Installs the IIS features. **WebSocket Protocol is required**: without it the web app reconnects endlessly.
  Application Initialization is installed too.
- Checks that the Hosting Bundle is present.
- Creates `web`, `api`, `keys`, `proof`, `logs`, `releases` and `backups` under the root folder.
- Creates the two app pools with *No Managed Code*, *AlwaysRunning*, no idle timeout and a nightly recycle at
  03:15. AlwaysRunning keeps the background jobs running all day.
- Creates both sites with https bindings, an http binding that redirects, and *preload* switched on.
- Sets folder permissions: each pool can read its own code, and only the two pools (plus Administrators and
  SYSTEM) can touch `keys`.

## 4. Settings and secrets

```powershell
.\Set-BassignEnvironment.ps1 -WebHost tasks.mintedmoments.co.zw -ApiHost api.mintedmoments.co.zw `
    -ConnectionString 'Server=…;Database=Bassign;…' -Root D:\Bassign `
    -DefaultAiApiKey 'sk-ant-…'      # optional: your AI key, used until the client enters their own
```

It writes **environment variables on each app pool**. IIS stores them in `applicationHost.config`, which
only Administrators can read. Nothing secret goes into the app folders, appsettings files or source control.
It also generates the phone sign-in key and **prints the one-time setup code**. Write that code down: it isn't
shown again.

### Environment variables

| Variable | Web | API | Notes |
|---|:-:|:-:|---|
| `ASPNETCORE_ENVIRONMENT=Production` | ✓ | ✓ | |
| `Database__ConnectionString` | ✓ | ✓ | |
| `DataProtection__KeysPath` | ✓ | ✓ | **Same folder for both.** It encrypts Admin-entered secrets (email/WhatsApp/AI keys). Back it up (§9). |
| `Media__ProofRoot` | ✓ | ✓ | Same folder: the API saves proof photos and the web app shows them |
| `Public__ApiBaseUrl` | ✓ | ✓ | `https://api.<client>/`. Used for webhook URLs and phone connect |
| `Email__PublicBaseUrl` | ✓ | ✓ | `https://tasks.<client>`. Used for links in emails |
| `AllowedHosts` | ✓ | ✓ | Each site's own host name only |
| `Serilog__WriteTo__0__Args__path` | ✓ | ✓ | `D:\Bassign\logs\web-.log` / `api-.log`, one file per day, 30 days kept |
| `Auth__JwtSigningKey` | | ✓ | Generated. `-NewJwtKey` rotates it; phone users stay signed in |
| `Setup__Code` | ✓ | | Generated. `-RemoveSetupCode` after the first Admin exists |
| `Default__Ai__ApiKey` | ✓ | ✓ | Optional. A default the client's Admin can replace. It is **not** locked |

**Admin settings vs locked settings.** Email, WhatsApp, AI, retention periods, session lengths, the work
calendar and so on are set by the client's Admin in **Settings**. If you set any of them here as an environment
variable (e.g. `Email__Provider`, `Ai__ApiKey`), the value **overrides and locks** it, and Settings shows
*"Set by your provider"* (decision #9). Only do that on purpose.

## 5. First deployment

```powershell
.\Deploy-Bassign.ps1 -Package D:\Bassign\releases\bassign-2026.11.25-1400-a1b2c3d.zip -Root D:\Bassign
```

The script runs these steps in order:

1. Unpack the release.
2. Back up the current files (the last 5 releases are kept).
3. Put up the maintenance page (`app_offline.htm`).
4. Apply database migrations with `efbundle.exe`.
5. Copy the new files.
6. Take the maintenance page down.
7. Run the smoke test (§7).

If migrations fail, **nothing has changed**: the old files are still in place and the maintenance page stays
up until you fix it or roll back.

## 6. Hand over to the client

1. Phone the client's first Admin with the **setup code** (don't email it).
2. They open `https://tasks.<client>`. A fresh install sends them to **Set up Bassign** (`/first-admin`), where
   they enter the code, their name, email and a password. The page refuses once an Admin exists.
3. They complete the workspace setup: organisation name, brand colour and time zone.
4. **You:** close the setup door.
   ```powershell
   .\Set-BassignEnvironment.ps1 -WebHost … -ApiHost … -RemoveSetupCode
   ```
5. The Admin then works through **Settings**:
   - Email provider, then *Send test email*.
   - WhatsApp provider. Paste the generated webhook URL into Meta, 360dialog or Twilio, then *Send test message*.
   - Work calendar, task categories, and Security & privacy (privacy contact, invite domains).
6. They invite staff (Team → Invite). Staff install the phone app with **Team → Phone app QR code**, or from the
   link on the page they land on after accepting an invite.

## 7. Smoke test (any time, from anywhere)

```powershell
.\Test-BassignDeployment.ps1 -WebHost tasks.mintedmoments.co.zw -ApiHost api.mintedmoments.co.zw
```

Read-only. It checks:

- both `/health/ready` endpoints (each app can reach the database);
- `/connect/info` points at the https API;
- the API answers the phone app;
- sign-in (or that first-Admin setup is still pending);
- the jobs dashboard isn't public;
- http redirects to https;
- the security headers are present and no server details are leaked;
- the TLS certificates are valid and not expiring within 21 days.

It exits with code 1 if any check fails, so it can be scheduled.

## 8. Updates and rolling back

**Update:** build a new release (§2), copy it over, then run `Deploy-Bassign.ps1 -Package …`. Users see the
maintenance page for a minute or two. The phone app keeps working offline and syncs afterwards. Pick a quiet
time; the background jobs catch up afterwards (reminders and escalations are re-checked every 5 minutes).

**Roll back the files:**

```powershell
.\Deploy-Bassign.ps1 -Rollback
```

This restores the most recent backup. **The database is not rolled back.** Migrations are written to be additive,
so the previous release normally runs fine on the newer schema. If a release's notes say a migration is *not*
backward-compatible:

- Restore the database backup taken before the deploy (§9), **or**
- Run the old release's bundle with a target migration:
  `releases\<old>\migrations\efbundle.exe <MigrationName> --connection "…"`.

## 9. Backups (set these up on day one)

| What | Where | How often | Why |
|---|---|---|---|
| SQL database | the client database | full nightly + log backups if in Full recovery | All tasks, users and settings |
| `keys\` | `D:\Bassign\keys` | after install and whenever it changes (rare) | **Without it, Admin-entered secrets can't be decrypted.** It is useless without the server's machine key (DPAPI), so restoring to a *new* server means re-entering those secrets in Settings |
| `proof\` | `D:\Bassign\proof` | nightly | Proof-of-work photos (the retention job deletes old ones) |

Test a restore at least once per client.

## 10. Monitoring and troubleshooting

- **Logs:** `D:\Bassign\logs\web-YYYYMMDD.log` and `api-…`. Every request has a CorrelationId; the web app's
  error page shows it.
- **Jobs:** `https://tasks.<client>/hangfire` (Admins only), or Settings → Background jobs. The *Servers* count
  must be 1. If it's 0, the API pool isn't running: check it's *Started* and *AlwaysRunning*.

| Symptom | Likely cause |
|---|---|
| **HTTP 500.30** "failed to start" | A required setting is missing or invalid. The start-up validators name it in the log. If there's no log, turn on `stdoutLogEnabled="true"` in that site's `web.config` and check `.\logs\stdout*`, then turn it off again |
| **HTTP 500.19** | Hosting Bundle not installed (or IIS not restarted after installing it) |
| **HTTP 503** | App pool stopped. Check *Event Viewer → Windows Logs → Application* |
| Web app keeps saying "Reconnecting…" | The IIS *WebSocket Protocol* feature is missing |
| Saved email/WhatsApp keys stop working after a move | The `keys` folder or machine changed. Re-enter the secrets in Settings |
| Phone app: "isn't set up for the phone app yet" | `Public__ApiBaseUrl` is missing or not https |
| Phone app: "couldn't reach its server" | The API site, its DNS or its certificate |
| `/first-admin` says the setup code isn't set | Run `Set-BassignEnvironment.ps1 … -NewSetupCode` |
| Authenticator codes always "don't match" | The phone's or the **server's** clock is wrong. Both must use automatic time (check `w32tm /query /status` on the server) |

## 11. Two-factor sign-in (decision #24)

- **Admins always use it.** The client can extend it to Managers or everyone in Settings → Security & privacy.
- **Email codes** (Managers and Staff only) need a working email provider on the Email tab. If email is
  switched off later, people on email codes sign in with a recovery code or get an Admin reset.
- **Passkeys** (website only) are tied to the site's address, `tasks.<client-domain>`. If that address ever
  changes, everyone's passkeys stop working and they add new ones after signing in with their password.
  They need https, which production always has.
- **Phone app:** asks for the code when signing in (authenticator, emailed or recovery code). Two-factor is
  set up on the website first; the app tells people so if they haven't done it yet.
- **Someone lost their phone:** another Admin uses Team → **Reset 2FA…** (after checking it's really them). This also removes their passkeys.
  If it's required for their role, they set it up again at their next sign-in.
- **The only Admin is locked out**, with no phone and no recovery codes:
  ```powershell
  .\Reset-BassignTwoFactor.ps1 -Email rudo@mintedmoments.co.zw
  ```
  Call them back on a number you already have before running it. The reset is written to the Windows
  Application log.
- **Secrets at rest:** the authenticator secret is encrypted with the `keys` folder, and recovery codes are
  stored as hashes. If the `keys` folder is lost, people simply set up their authenticator app again.

## 12. Per-client checklist

- [ ] DNS for both names resolves to the server
- [ ] Certificate imported, thumbprint noted, auto-renewal working
- [ ] Database and app login created (`db_owner` on that database only)
- [ ] `Install-BassignServer.ps1` ran clean
- [ ] `Set-BassignEnvironment.ps1` ran clean; setup code given to the Admin by phone
- [ ] `Deploy-Bassign.ps1` ran and the smoke test passed
- [ ] First Admin created; `-RemoveSetupCode` done
- [ ] Email and WhatsApp test messages received
- [ ] SQL, `keys` and `proof` backups scheduled; one restore tested
- [ ] Smoke test scheduled daily (Task Scheduler), with failures alerting you

# Running the field app (Bassign.Mobile)

## Emulator (default)
1. Run **Bassign.Api** (it listens on `http://localhost:5080` as well as https 7080).
2. Startup project **Bassign.Mobile**, target your Android emulator, F5.
   Debug builds talk to `http://10.0.2.2:5080` — the emulator's address for your PC.

## Physical Android phone (USB)
1. On the phone: Settings → About phone → tap **Build number** 7× → Developer options → enable **USB debugging**.
2. Plug in the phone and accept the "Allow USB debugging?" prompt.
3. Run **Bassign.Api**, then from the repo root: `pwsh scripts/phone-usb.ps1`
   (tunnels the phone's `localhost:5080` to your PC — re-run after reconnecting the cable).
4. In Visual Studio pick the phone as the target and press F5.
   Debug builds detect a physical device and use `http://localhost:5080` automatically.

Why USB + `adb reverse` rather than Wi-Fi: no PC IP address to configure, no firewall rule, and no need
to allow plain HTTP to LAN addresses (the app only permits cleartext to `10.0.2.2`/`localhost`).

## Release builds
Release builds use HTTPS only and the per-client API address in `MobileConfig.cs` (set in step 17).

## Step 11 acceptance checks
- [ ] **Offline:** airplane mode on → Accept a task → it updates instantly and shows "Pending 1".
      Airplane mode off → within a few seconds the badge returns to "Synced hh:mm", and the web task
      timeline shows "Accepted by Tendai Ncube".
- [ ] **Photo:** on a task that requires proof, Accept → "Add photo & mark done" → take a photo →
      "Marked done — thanks". The web task detail shows the photo **upright**.
- [ ] **Pull to refresh:** assign a new task to the staff member on the web, then drag down on the phone — it appears.
- [ ] **Sign out:** avatar → Sign out. With unsynced changes, it warns first.

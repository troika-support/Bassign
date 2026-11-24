<#
  Lets a USB-connected Android phone reach the Bassign API running on this PC.
  The phone's localhost:5080 is tunnelled to this PC's port 5080 (no Wi-Fi IP, firewall rule
  or cleartext exception needed). Run once after plugging the phone in (re-run after a reconnect).
  Usage:  pwsh scripts/phone-usb.ps1
#>
$candidates = @(
    $(if ($env:ANDROID_HOME) { Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe' }),
    $(if ($env:ANDROID_SDK_ROOT) { Join-Path $env:ANDROID_SDK_ROOT 'platform-tools\adb.exe' }),
    'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe',
    (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe')
) | Where-Object { $_ -and (Test-Path $_) }

$adb = $candidates | Select-Object -First 1
if (-not $adb) { Write-Error "adb.exe not found. Install Android SDK platform-tools (Visual Studio installs them with the MAUI workload)."; exit 1 }

& $adb devices
& $adb reverse tcp:5080 tcp:5080
if ($LASTEXITCODE -eq 0) { Write-Host "Phone -> PC tunnel ready: the app's http://localhost:5080 now reaches Bassign.Api." -ForegroundColor Green }
else { Write-Error "adb reverse failed - is the phone connected with USB debugging enabled and authorised?" }

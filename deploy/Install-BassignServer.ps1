<#
.SYNOPSIS
  One-time preparation of a Windows Server for Bassign: IIS features, folders, two app pools, two https
  sites (tasks.<client> and api.<client>) and folder permissions. Safe to run again.

.DESCRIPTION
  Run in an elevated PowerShell on the server, AFTER installing the .NET 10 Hosting Bundle and importing the
  TLS certificate(s) into LocalMachine\My. Then run Set-BassignEnvironment.ps1 and Deploy-Bassign.ps1.

.EXAMPLE
  .\Install-BassignServer.ps1 -WebHost tasks.mintedmoments.co.zw -ApiHost api.mintedmoments.co.zw `
      -CertThumbprint 1A2B3C... -Root D:\Bassign
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $WebHost,
    [Parameter(Mandatory)] [string] $ApiHost,
    [Parameter(Mandatory)] [string] $CertThumbprint,
    [string] $ApiCertThumbprint = $CertThumbprint,   # same cert if it covers both names (SAN or wildcard)
    [string] $Root = 'D:\Bassign'
)

$ErrorActionPreference = 'Stop'
function Step($text) { Write-Host "› $text" -ForegroundColor Cyan }

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this in an elevated (Administrator) PowerShell.'
}

Step 'IIS features (WebSockets is required: the web app is Blazor Server)'
$features = 'Web-Server', 'Web-WebSockets', 'Web-AppInit', 'Web-Http-Logging', 'Web-Request-Monitor', 'Web-Filtering', 'Web-Mgmt-Console'
if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
    Install-WindowsFeature -Name $features | Out-Null
} else {
    Write-Warning 'Install-WindowsFeature not found (not Windows Server?). Enable IIS, WebSocket Protocol and Application Initialization in Windows Features.'
}

Step '.NET 10 Hosting Bundle'
$ancm = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
$runtimes = & "$env:ProgramFiles\dotnet\dotnet.exe" --list-runtimes 2>$null
if (-not (Test-Path $ancm) -or -not ($runtimes -match 'Microsoft\.AspNetCore\.App 10\.')) {
    throw 'The .NET 10 Hosting Bundle is missing. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 (ASP.NET Core Runtime → Hosting Bundle), then run: net stop was /y; net start w3svc'
}

Step "Certificates in LocalMachine\My"
foreach ($t in @($CertThumbprint, $ApiCertThumbprint) | Select-Object -Unique) {
    if (-not (Get-Item "Cert:\LocalMachine\My\$t" -ErrorAction SilentlyContinue)) { throw "Certificate $t not found in LocalMachine\My." }
}

Step "Folders under $Root"
$folders = 'web', 'api', 'keys', 'proof', 'logs', 'releases', 'backups'
foreach ($f in $folders) { New-Item -ItemType Directory -Force (Join-Path $Root $f) | Out-Null }

Import-Module WebAdministration

Step 'App pools'
foreach ($pool in 'Bassign.Web', 'Bassign.Api') {
    if (-not (Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool | Out-Null }
    $p = "IIS:\AppPools\$pool"
    Set-ItemProperty $p managedRuntimeVersion ''                       # No Managed Code (ASP.NET Core)
    Set-ItemProperty $p startMode 'AlwaysRunning'                      # the API runs reminders/digests (Hangfire)
    Set-ItemProperty $p processModel.idleTimeout ([TimeSpan]::Zero)    # never idle-shutdown
    Set-ItemProperty $p processModel.loadUserProfile $true
    Set-ItemProperty $p recycling.periodicRestart.time ([TimeSpan]::Zero)   # no 29-hour recycle mid-day…
    Clear-ItemProperty $p -Name recycling.periodicRestart.schedule
    Set-ItemProperty $p -Name recycling.periodicRestart.schedule -Value @{ value = '03:15:00' }   # …recycle quietly at night
}

function Ensure-Site([string] $name, [string] $hostName, [string] $path, [string] $pool, [string] $thumb) {
    if (-not (Get-Website -Name $name -ErrorAction SilentlyContinue)) {
        New-Website -Name $name -PhysicalPath $path -ApplicationPool $pool -HostHeader $hostName -Port 443 -Ssl -SslFlags 1 | Out-Null
    } else {
        Set-ItemProperty "IIS:\Sites\$name" physicalPath $path
        Set-ItemProperty "IIS:\Sites\$name" applicationPool $pool
    }
    if (-not (Get-WebBinding -Name $name -Protocol https -HostHeader $hostName)) {
        New-WebBinding -Name $name -Protocol https -Port 443 -HostHeader $hostName -SslFlags 1
    }
    (Get-WebBinding -Name $name -Protocol https -HostHeader $hostName).AddSslCertificate($thumb, 'My')
    if (-not (Get-WebBinding -Name $name -Protocol http -HostHeader $hostName)) {
        New-WebBinding -Name $name -Protocol http -Port 80 -HostHeader $hostName   # the app redirects to https
    }
    Set-ItemProperty "IIS:\Sites\$name" applicationDefaults.preloadEnabled $true   # start with IIS, not on first visit
}

Step 'Sites'
Ensure-Site 'Bassign Web' $WebHost (Join-Path $Root 'web') 'Bassign.Web' $CertThumbprint
Ensure-Site 'Bassign API' $ApiHost (Join-Path $Root 'api') 'Bassign.Api' $ApiCertThumbprint

Step 'Permissions (each app pool: read its code; modify keys, proof photos and logs)'
foreach ($pool in 'Bassign.Web', 'Bassign.Api') {
    $id = "IIS AppPool\$pool"
    & icacls (Join-Path $Root ($pool -replace 'Bassign\.', '').ToLower()) /grant "${id}:(OI)(CI)RX" /T /Q | Out-Null
    foreach ($shared in 'keys', 'proof', 'logs') {
        & icacls (Join-Path $Root $shared) /grant "${id}:(OI)(CI)M" /T /Q | Out-Null
    }
}
# The Data Protection keys decrypt Admin-entered secrets: nobody else needs them.
& icacls (Join-Path $Root 'keys') /inheritance:r /grant 'Administrators:(OI)(CI)F' 'SYSTEM:(OI)(CI)F' `
    'IIS AppPool\Bassign.Web:(OI)(CI)M' 'IIS AppPool\Bassign.Api:(OI)(CI)M' /Q | Out-Null

Write-Host ''
Write-Host 'Server prepared. Next:' -ForegroundColor Green
Write-Host "  1. .\Set-BassignEnvironment.ps1 -WebHost $WebHost -ApiHost $ApiHost -Root $Root -ConnectionString '...'"
Write-Host "  2. .\Deploy-Bassign.ps1 -Package <bassign-....zip> -Root $Root"

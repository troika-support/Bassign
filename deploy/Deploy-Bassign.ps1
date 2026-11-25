<#
.SYNOPSIS
  Installs or updates Bassign on a prepared server from a release zip (Build-Release.ps1):
  backup → maintenance page → database migrations → new files → back online → smoke test.

.EXAMPLE
  .\Deploy-Bassign.ps1 -Package C:\Temp\bassign-2026.11.25-1400-a1b2c3d.zip
.EXAMPLE
  # Put the previous release's files back (the database is NOT rolled back — see docs/deploy-iis.md).
  .\Deploy-Bassign.ps1 -Rollback
#>
[CmdletBinding(DefaultParameterSetName = 'Deploy')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Deploy')] [string] $Package,
    [Parameter(Mandatory, ParameterSetName = 'Rollback')] [switch] $Rollback,
    [string] $Root = 'D:\Bassign',
    [switch] $SkipSmokeTest
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration
function Step($text) { Write-Host "› $text" -ForegroundColor Cyan }

$apps = @{ web = 'Bassign.Web'; api = 'Bassign.Api' }
$offline = Join-Path $PSScriptRoot 'app_offline.htm'

function Get-PoolEnv([string] $pool, [string] $name) {
    (Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' `
        -Filter "system.applicationHost/applicationPools/add[@name='$pool']/environmentVariables" -Name Collection |
        Where-Object { $_.name -eq $name }).value
}

function Set-Offline([bool] $on) {
    foreach ($app in $apps.Keys) {
        $target = Join-Path $Root "$app\app_offline.htm"
        if ($on) { Copy-Item $offline $target -Force } elseif (Test-Path $target) { Remove-Item $target -Force }
    }
    if ($on) { Start-Sleep -Seconds 3 }   # let IIS stop the apps and release their files
}

function Copy-App([string] $from, [string] $to) {
    # /MIR mirrors; app_offline.htm is excluded so it stays up until we remove it. Robocopy exit codes < 8 are success.
    & robocopy $from $to /MIR /XF app_offline.htm /R:3 /W:2 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying files to $to failed (robocopy exit $LASTEXITCODE)." }
}

$backups = Join-Path $Root 'backups'

if ($Rollback) {
    $last = Get-ChildItem $backups -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $last) { throw "No backup found in $backups." }
    Step "Rolling back to the files saved at $($last.Name)"
    Set-Offline $true
    foreach ($app in $apps.Keys) { Copy-App (Join-Path $last.FullName $app) (Join-Path $Root $app) }
    Set-Offline $false
    Write-Host 'Files restored. If the failed release changed the database, see "Rolling back" in deploy-iis.md.' -ForegroundColor Yellow
    return
}

if (-not (Test-Path $Package)) { throw "Package not found: $Package" }
$name = [IO.Path]::GetFileNameWithoutExtension($Package)
$release = Join-Path $Root "releases\$name"

Step "Unpacking $name"
if (Test-Path $release) { Remove-Item $release -Recurse -Force }
Expand-Archive $Package $release
foreach ($required in 'web\Bassign.Web.dll', 'api\Bassign.Api.dll', 'migrations\efbundle.exe') {
    if (-not (Test-Path (Join-Path $release $required))) { throw "The package is missing $required." }
}
$offline = Join-Path $release 'deploy\app_offline.htm'

Step 'Backing up the current release'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
foreach ($app in $apps.Keys) {
    $current = Join-Path $Root $app
    if (Get-ChildItem $current -ErrorAction SilentlyContinue | Select-Object -First 1) {
        Copy-App $current (Join-Path $backups "$stamp\$app")
    }
}
Get-ChildItem $backups -Directory | Sort-Object Name -Descending | Select-Object -Skip 5 | Remove-Item -Recurse -Force   # keep 5

$connection = Get-PoolEnv $apps.api 'Database__ConnectionString'
if (-not $connection) { throw 'No connection string on the API app pool — run Set-BassignEnvironment.ps1 first.' }

Step 'Maintenance page on'
Set-Offline $true
try {
    Step 'Database migrations'
    & (Join-Path $release 'migrations\efbundle.exe') --connection $connection
    if ($LASTEXITCODE -ne 0) { throw 'Migrations failed. The old version is still in place; the maintenance page stays up until you fix or roll back.' }

    Step 'Copying the new release'
    foreach ($app in $apps.Keys) { Copy-App (Join-Path $release $app) (Join-Path $Root $app) }
}
catch {
    Write-Host $_ -ForegroundColor Red
    Write-Host "Maintenance page is still up. Fix the problem and run again, or: .\Deploy-Bassign.ps1 -Rollback" -ForegroundColor Yellow
    throw
}

Step 'Back online'
Set-Offline $false
foreach ($pool in $apps.Values) { Restart-WebAppPool $pool }
Set-Content (Join-Path $Root 'VERSION.txt') (Get-Content (Join-Path $release 'VERSION.txt'))

if (-not $SkipSmokeTest) {
    Start-Sleep -Seconds 5
    $web = (Get-WebBinding -Name 'Bassign Web' -Protocol https | Select-Object -First 1).bindingInformation.Split(':')[2]
    $api = (Get-WebBinding -Name 'Bassign API' -Protocol https | Select-Object -First 1).bindingInformation.Split(':')[2]
    & (Join-Path $PSScriptRoot 'Test-BassignDeployment.ps1') -WebHost $web -ApiHost $api
}

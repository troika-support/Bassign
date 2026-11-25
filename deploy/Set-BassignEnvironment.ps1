<#
.SYNOPSIS
  Sets Bassign's server settings and secrets as environment variables on the two IIS app pools
  (stored in IIS's applicationHost.config, readable only by Administrators — never in the app folders).

.DESCRIPTION
  Safe to run again: values you pass are replaced; generated secrets (JWT key, setup code) are created
  only if missing, unless you ask for new ones. Email, WhatsApp, AI keys, retention and session lengths
  are set by the client's Admin in Settings — only set them here to LOCK them (see docs/deploy-iis.md).

.EXAMPLE
  .\Set-BassignEnvironment.ps1 -WebHost tasks.mintedmoments.co.zw -ApiHost api.mintedmoments.co.zw `
      -ConnectionString 'Server=.;Database=Bassign;User Id=bassign_app;Password=...;TrustServerCertificate=true;Encrypt=true'

.EXAMPLE
  # After the first Admin has signed in: close the setup door.
  .\Set-BassignEnvironment.ps1 -WebHost ... -ApiHost ... -RemoveSetupCode
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $WebHost,
    [Parameter(Mandatory)] [string] $ApiHost,
    [string] $ConnectionString,          # required the first time
    [string] $Root = 'D:\Bassign',
    [string] $DefaultAiApiKey,           # your AI key, used until a client Admin enters their own (Default__Ai__ApiKey)
    [switch] $NewSetupCode,
    [switch] $RemoveSetupCode,
    [switch] $NewJwtKey,                 # phone app users stay signed in (refresh tokens live in the database)
    [switch] $NoRestart
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

$apphost = 'MACHINE/WEBROOT/APPHOST'
function PoolFilter([string] $pool) { "system.applicationHost/applicationPools/add[@name='$pool']/environmentVariables" }

function Get-PoolEnv([string] $pool, [string] $name) {
    (Get-WebConfigurationProperty -PSPath $apphost -Filter (PoolFilter $pool) -Name Collection |
        Where-Object { $_.name -eq $name }).value
}

function Set-PoolEnv([string] $pool, [string] $name, [string] $value) {
    if (Get-PoolEnv $pool $name) {
        Remove-WebConfigurationProperty -PSPath $apphost -Filter (PoolFilter $pool) -Name . -AtElement @{ name = $name }
    }
    Add-WebConfigurationProperty -PSPath $apphost -Filter (PoolFilter $pool) -Name . -Value @{ name = $name; value = $value }
}

function Remove-PoolEnv([string] $pool, [string] $name) {
    if (Get-PoolEnv $pool $name) {
        Remove-WebConfigurationProperty -PSPath $apphost -Filter (PoolFilter $pool) -Name . -AtElement @{ name = $name }
    }
}

function New-Secret([int] $bytes) {
    $buffer = New-Object byte[] $bytes
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    $buffer
}

function New-SetupCode {
    # 12 characters without look-alikes (0/O, 1/I/L), shown as XXXX-XXXX-XXXX.
    $alphabet = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'
    $chars = (New-Secret 12) | ForEach-Object { $alphabet[$_ % $alphabet.Length] }
    ('{0}-{1}-{2}' -f (-join $chars[0..3]), (-join $chars[4..7]), (-join $chars[8..11]))
}

foreach ($pool in 'Bassign.Web', 'Bassign.Api') {
    if (-not (Test-Path "IIS:\AppPools\$pool")) { throw "App pool $pool not found — run Install-BassignServer.ps1 first." }
}

$web = 'Bassign.Web'
$api = 'Bassign.Api'
$both = $web, $api

if (-not $ConnectionString -and -not (Get-PoolEnv $api 'Database__ConnectionString')) {
    throw 'Pass -ConnectionString the first time.'
}

Write-Host '› Shared settings' -ForegroundColor Cyan
foreach ($pool in $both) {
    Set-PoolEnv $pool 'ASPNETCORE_ENVIRONMENT' 'Production'
    if ($ConnectionString) { Set-PoolEnv $pool 'Database__ConnectionString' $ConnectionString }
    Set-PoolEnv $pool 'DataProtection__KeysPath' (Join-Path $Root 'keys')   # SAME folder: both apps read Admin-entered secrets
    Set-PoolEnv $pool 'Media__ProofRoot' (Join-Path $Root 'proof')          # the API stores photos, the web app shows them
    Set-PoolEnv $pool 'Public__ApiBaseUrl' "https://$ApiHost/"
    Set-PoolEnv $pool 'Email__PublicBaseUrl' "https://$WebHost"
    if ($DefaultAiApiKey) { Set-PoolEnv $pool 'Default__Ai__ApiKey' $DefaultAiApiKey }
}
Set-PoolEnv $web 'AllowedHosts' $WebHost
Set-PoolEnv $api 'AllowedHosts' $ApiHost
Set-PoolEnv $web 'Serilog__WriteTo__0__Args__path' (Join-Path $Root 'logs\web-.log')
Set-PoolEnv $api 'Serilog__WriteTo__0__Args__path' (Join-Path $Root 'logs\api-.log')

Write-Host '› Phone app sign-in key (API)' -ForegroundColor Cyan
if ($NewJwtKey -or -not (Get-PoolEnv $api 'Auth__JwtSigningKey')) {
    Set-PoolEnv $api 'Auth__JwtSigningKey' ([Convert]::ToBase64String((New-Secret 48)))
    Write-Host '  New signing key generated.'
} else { Write-Host '  Kept the existing key.' }

Write-Host '› First-Admin setup code (web)' -ForegroundColor Cyan
if ($RemoveSetupCode) {
    Remove-PoolEnv $web 'Setup__Code'
    Write-Host '  Removed. New people join by invite only.'
} elseif ($NewSetupCode -or -not (Get-PoolEnv $web 'Setup__Code')) {
    $code = New-SetupCode
    Set-PoolEnv $web 'Setup__Code' $code
    Write-Host ''
    Write-Host "  SETUP CODE: $code" -ForegroundColor Yellow
    Write-Host "  Give it to the client's first Admin (by phone, not email). It works once, at https://$WebHost/first-admin"
    Write-Host '  It is not shown again — run with -NewSetupCode to make another.'
    Write-Host ''
} else { Write-Host '  A setup code is already set (run with -NewSetupCode to replace it, -RemoveSetupCode once the Admin exists).' }

if (-not $NoRestart) {
    Write-Host '› Restarting app pools' -ForegroundColor Cyan
    foreach ($pool in $both) {
        if ((Get-WebAppPoolState $pool).Value -eq 'Started') { Restart-WebAppPool $pool } else { Start-WebAppPool $pool }
    }
}
Write-Host 'Environment set.' -ForegroundColor Green

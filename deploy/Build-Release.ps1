<#
.SYNOPSIS
  Builds a Bassign release package on your PC: both apps (framework-dependent, win-x64), the database
  migration bundle, and the server scripts — zipped, ready to copy to a client's server.

.EXAMPLE
  .\deploy\Build-Release.ps1
  .\deploy\Build-Release.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$publish = Join-Path $root 'artifacts\publish'
Push-Location $root
try {
    $commit = (git rev-parse --short HEAD 2>$null)
    if (-not $commit) { $commit = 'nogit' }
    $version = '{0}-{1}' -f (Get-Date -Format 'yyyy.MM.dd-HHmm'), $commit
    Write-Host "Building Bassign $version" -ForegroundColor Cyan

    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

    if (-not $SkipTests) {
        Write-Host '› Unit tests'
        dotnet test tests/Bassign.UnitTests -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Unit tests failed — not packaging.' }
    }

    Write-Host '› Web'
    dotnet publish src/Bassign.Web -c Release -p:PublishProfile=IIS
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the Web app failed.' }

    Write-Host '› API'
    dotnet publish src/Bassign.Api -c Release -p:PublishProfile=IIS
    if ($LASTEXITCODE -ne 0) { throw 'Publishing the API failed.' }

    # One self-contained exe that applies pending migrations; the server needs no SDK or dotnet-ef.
    Write-Host '› Migration bundle'
    New-Item -ItemType Directory -Force (Join-Path $publish 'migrations') | Out-Null
    dotnet ef migrations bundle --project src/Bassign.Infrastructure --startup-project src/Bassign.Api `
        --configuration Release --self-contained -r win-x64 --force `
        --output (Join-Path $publish 'migrations\efbundle.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Building the migration bundle failed (is dotnet-ef installed? dotnet tool install -g dotnet-ef).' }

    Write-Host '› Scripts and runbook'
    $deploy = Join-Path $publish 'deploy'
    New-Item -ItemType Directory -Force $deploy | Out-Null
    Copy-Item (Join-Path $root 'deploy\*.ps1') $deploy
    Copy-Item (Join-Path $root 'deploy\app_offline.htm') $deploy
    Copy-Item (Join-Path $root 'docs\deploy-iis.md') $deploy
    Set-Content (Join-Path $publish 'VERSION.txt') $version

    # Never ship development settings.
    Get-ChildItem $publish -Recurse -Include 'appsettings.Development.json' | Remove-Item -Force

    $zip = Join-Path $root "artifacts\bassign-$version.zip"
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force
    Write-Host "Done: $zip" -ForegroundColor Green
}
finally {
    Pop-Location
}

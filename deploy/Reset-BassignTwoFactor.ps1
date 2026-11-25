<#
.SYNOPSIS
  Last resort: turns off two-factor sign-in for ONE account, when no other Admin can reset it in Team
  (e.g. the only Admin lost their phone and their recovery codes). Run on the server as an Administrator.

.DESCRIPTION
  Check it's really them first (call them back on a number you already have). The account's authenticator
  secret, recovery codes and passkeys are deleted and its other sessions are signed out. If their role requires
  two-factor, they set it up again at their next sign-in. The action is written to the Windows Application log.

.EXAMPLE
  .\Reset-BassignTwoFactor.ps1 -Email rudo@mintedmoments.co.zw
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)] [string] $Email
)

$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

$connection = (Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' `
    -Filter "system.applicationHost/applicationPools/add[@name='Bassign.Api']/environmentVariables" -Name Collection |
    Where-Object { $_.name -eq 'Database__ConnectionString' }).value
if (-not $connection) { throw 'No connection string on the Bassign.Api app pool.' }

$sql = @'
DECLARE @id nvarchar(450) = (SELECT Id FROM dbo.AspNetUsers WHERE NormalizedEmail = @email);
IF @id IS NULL BEGIN SELECT 0; RETURN; END
UPDATE dbo.AspNetUsers
   SET TwoFactorEnabled = 0, SecurityStamp = CONVERT(nvarchar(36), NEWID()), AccessFailedCount = 0, LockoutEnd = NULL
 WHERE Id = @id;
DELETE FROM dbo.AspNetUserTokens WHERE UserId = @id AND LoginProvider = '[Bassign]';
IF OBJECT_ID(N'dbo.AspNetUserPasskeys') IS NOT NULL DELETE FROM dbo.AspNetUserPasskeys WHERE UserId = @id;
SELECT 1;
'@

if (-not $PSCmdlet.ShouldProcess($Email, 'Turn off two-factor sign-in')) { return }

$conn = New-Object System.Data.SqlClient.SqlConnection $connection
try {
    $conn.Open()
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    [void] $cmd.Parameters.AddWithValue('@email', $Email.Trim().ToUpperInvariant())
    $found = [int] $cmd.ExecuteScalar()
} finally { $conn.Dispose() }

if ($found -ne 1) { throw "No Bassign account with the email $Email." }

$source = 'Bassign'
if (-not [Diagnostics.EventLog]::SourceExists($source)) { New-EventLog -LogName Application -Source $source }
Write-EventLog -LogName Application -Source $source -EventId 2401 -EntryType Warning `
    -Message "Two-factor sign-in was reset from the server for $Email by $env:USERDOMAIN\$env:USERNAME."

Write-Host "Two-factor sign-in reset for $Email. They can sign in with their password; if required, they'll set it up again." -ForegroundColor Green

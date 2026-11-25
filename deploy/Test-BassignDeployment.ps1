<#
.SYNOPSIS
  Smoke test of a live Bassign install, from the server or any PC. Read-only: changes nothing.
  Exit code 0 = all checks passed.

.EXAMPLE
  .\Test-BassignDeployment.ps1 -WebHost tasks.mintedmoments.co.zw -ApiHost api.mintedmoments.co.zw
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $WebHost,
    [Parameter(Mandatory)] [string] $ApiHost,
    [int] $CertWarningDays = 21
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12   # Windows PowerShell 5.1; PowerShell 7 negotiates TLS 1.3 itself

$handler = New-Object System.Net.Http.HttpClientHandler
$handler.AllowAutoRedirect = $false
$http = New-Object System.Net.Http.HttpClient($handler)
$http.Timeout = [TimeSpan]::FromSeconds(30)

$results = New-Object System.Collections.Generic.List[object]
function Check([string] $name, [scriptblock] $test) {
    try {
        $detail = & $test
        $results.Add([pscustomobject]@{ Result = 'PASS'; Check = $name; Detail = "$detail" })
    } catch {
        $results.Add([pscustomobject]@{ Result = 'FAIL'; Check = $name; Detail = $_.Exception.Message })
    }
}
function HttpGet([string] $url) { $http.GetAsync($url).GetAwaiter().GetResult() }
function Expect($response, [int[]] $codes) {
    $code = [int]$response.StatusCode
    if ($codes -notcontains $code) { throw "HTTP $code (expected $($codes -join ' or '))" }
    "HTTP $code"
}
function Header($response, [string] $name) {
    $values = $null
    if ($response.Headers.TryGetValues($name, [ref] $values)) { return ($values -join ', ') }
    if ($response.Content.Headers.TryGetValues($name, [ref] $values)) { return ($values -join ', ') }
    $null
}

$web = "https://$WebHost"
$api = "https://$ApiHost"

Check 'Web is up (database reachable)' { Expect (HttpGet "$web/health/ready") 200 }
Check 'API is up (database reachable)' { Expect (HttpGet "$api/health/ready") 200 }

Check 'Phone connect info' {
    $r = HttpGet "$web/connect/info"; Expect $r 200 | Out-Null
    $info = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    $apiUri = [Uri] $info.apiBaseUrl
    if ($apiUri.Scheme -ne 'https') { throw "apiBaseUrl is not https: $($info.apiBaseUrl)" }
    if ($apiUri.Host -ne $ApiHost) { throw "apiBaseUrl points at $($apiUri.Host), not $ApiHost (check Public__ApiBaseUrl)" }
    "org '$($info.organizationName)', api $($info.apiBaseUrl)"
}

Check 'API answers the phone app (branding)' { Expect (HttpGet "$api/organization/branding") 200 }

Check 'Sign-in page (or first-Admin setup)' {
    $r = HttpGet "$web/sign-in"
    $code = [int]$r.StatusCode
    if ($code -eq 200) { return 'sign-in shown' }
    $location = if ($r.Headers.Location) { $r.Headers.Location.ToString() } else { '' }
    if ($code -in 301, 302, 303, 307, 308 -and $location -like '*first-admin*') { return 'NO ADMIN YET → /first-admin (give the client the setup code)' }
    throw "HTTP $code $location"
}

Check 'Jobs dashboard is not public' {
    $code = [int](HttpGet "$web/hangfire").StatusCode
    if ($code -eq 200) { throw '/hangfire answered 200 without signing in' }
    "HTTP $code"
}

Check 'Plain http redirects to https' {
    try { $r = HttpGet "http://$WebHost/" } catch { return 'port 80 closed (fine if only 443 is open)' }
    $code = [int]$r.StatusCode
    if ($code -notin 301, 302, 307, 308 -or -not "$($r.Headers.Location)".StartsWith('https://')) { throw "HTTP $code → $($r.Headers.Location)" }
    "HTTP $code → https"
}

Check 'Security headers (web)' {
    $r = HttpGet "$web/sign-in"
    $missing = 'Strict-Transport-Security', 'X-Content-Type-Options', 'Content-Security-Policy', 'Referrer-Policy' |
        Where-Object { -not (Header $r $_) }
    if ($missing) { throw "missing: $($missing -join ', ')" }
    'HSTS, nosniff, CSP, Referrer-Policy'
}

Check 'Server details are not leaked' {
    $r = HttpGet "$api/health/live"
    $leaks = 'X-Powered-By', 'X-AspNet-Version' | Where-Object { Header $r $_ }
    if ($leaks) { throw "remove: $($leaks -join ', ')" }
    'none'
}

foreach ($h in $WebHost, $ApiHost | Select-Object -Unique) {
    Check "TLS certificate for $h" {
        $tcp = New-Object Net.Sockets.TcpClient($h, 443)
        try {
            $ssl = New-Object Net.Security.SslStream($tcp.GetStream(), $false)
            $ssl.AuthenticateAsClient($h)   # fails on an untrusted or wrong-name certificate
            $cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2($ssl.RemoteCertificate)
            $days = [int]($cert.NotAfter - (Get-Date)).TotalDays
            if ($days -lt $CertWarningDays) { throw "expires in $days days ($($cert.NotAfter.ToString('d MMM yyyy'))) — renew it" }
            "valid, $days days left"
        } finally { $tcp.Dispose() }
    }
}

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object Result -eq 'FAIL').Count
if ($failed -gt 0) { Write-Host "$failed check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All checks passed.' -ForegroundColor Green

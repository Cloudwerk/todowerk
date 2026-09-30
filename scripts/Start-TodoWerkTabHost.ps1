#Requires -Version 7.0
<#
.SYNOPSIS
    Runs TodoWerk the way the Teams tab needs it: behind a tunnel, on a stable host, in an
    environment where the session cookie can survive somebody else's iframe.

.DESCRIPTION
    The Teams tab walk cannot be done with `dotnet run`. Three things about a Development host make
    the tab silently useless, and only one of them announces itself:

    - **The session cookie is `SameSite=Lax` in Development** and `SameSite=None; Secure` everywhere
      else (`SecureCookiePolicy`). A Lax cookie is simply absent inside a third-party iframe, so the
      tab signs somebody in and then shows the blocked-cookie card on every load — the same symptom
      Safari produces, from an entirely different cause, which is the worst possible way to start a
      walk that is partly about that card.
    - **No content security policy is sent in Development**, so `frame-ancestors` is never exercised.
      The tab would frame anywhere, and the one header the walk is meant to prove goes untested.
    - **User secrets are only loaded in Development.** Any other environment starts with no client
      id, no secret and no connection string, and fails at startup.

    So this script runs the app as Staging, promotes the user secrets it would otherwise lose into
    environment variables, and supplies the two things a non-Development host refuses to start
    without: a persisted Data Protection key ring and a certificate to encrypt it with.

    It does not start the tunnel. Run that in its own terminal — it is a long-lived process with its
    own output, and pretending otherwise makes both harder to read. The command is printed below.

.PARAMETER Domain
    The tunnel's stable host, with no scheme, no port and no trailing slash — for example
    `<your-tunnel-domain>.ngrok-free.app`. It must be stable: the Application ID URI is
    host-qualified and the App Package bakes this host into three manifest fields, so a host that
    changes per restart means re-registering and repackaging every time.

.PARAMETER Port
    The local HTTP port to serve on, and the one the tunnel forwards to. The default is the HTTP
    address in launchSettings.json. Plain HTTP on purpose: the tunnel terminates TLS with a
    certificate Teams already trusts, which is the whole reason a tunnel is being used.

.PARAMETER Environment
    Anything but Development. Staging by default.

.PARAMETER SkipRun
    Prepare and print everything, then stop without starting the application. For checking what the
    run would use.

.EXAMPLE
    pwsh ./scripts/Start-TodoWerkTabHost.ps1 -Domain <your-tunnel-domain>.ngrok-free.app
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Domain,

    [int] $Port = 5080,

    [string] $Environment = 'Staging',

    [switch] $SkipRun
)

$ErrorActionPreference = 'Stop'

# A scheme or a trailing slash here produces an Application ID URI Teams rejects with an error that
# says nothing about domains, so it is refused now rather than diagnosed in three days' time.
if ($Domain -match '^\w+://' -or $Domain.EndsWith('/') -or $Domain.Contains(':')) {
    throw "Domain must be a bare host — no scheme, no port, no trailing slash. Got '$Domain'."
}

if ($Environment -eq 'Development') {
    throw 'Development is the one environment the tab cannot work in: the session cookie stays SameSite=Lax and is absent inside the Teams iframe. Use Staging.'
}

$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'src/TodoWerk.Web'
$walk = Join-Path $repository 'artifacts/walk'

New-Item -ItemType Directory -Force -Path $walk | Out-Null

# ---------------------------------------------------------------------------------------------
# The user secrets, which this environment would not load

$userSecretsId = (Select-Xml -Path (Join-Path $project 'TodoWerk.Web.csproj') -XPath '//UserSecretsId').Node.InnerText
$secretsPath = Join-Path $env:APPDATA "Microsoft/UserSecrets/$userSecretsId/secrets.json"

if (-not (Test-Path $secretsPath)) {
    throw "No user secrets at $secretsPath. Development reads them and this environment does not, so they have to be promoted — set them up first (CONTRIBUTING § Development setup)."
}

$secrets = Get-Content -Path $secretsPath -Raw | ConvertFrom-Json -AsHashtable

$settings = @{}

# `dotnet user-secrets set` writes flat keys with colons, so that is the shape this normally meets.
# Nested objects are still valid in the file and a hand-edit produces them, and a secret silently
# dropped here surfaces as a startup failure about a setting the developer can see is present.
function Add-Setting([hashtable] $target, [string] $prefix, $value) {
    if ($value -is [System.Collections.IDictionary]) {
        foreach ($key in $value.Keys) {
            Add-Setting $target ($prefix ? "$prefix`:$key" : $key) $value[$key]
        }
    } else {
        # Configuration's own separator for environment variables.
        $target[$prefix.Replace(':', '__')] = $value
    }
}

Add-Setting $settings '' $secrets

foreach ($required in 'EntraId__ClientId', 'EntraId__ClientSecret', 'EntraId__TenantId', 'ConnectionStrings__TodoWerk') {
    if (-not $settings.ContainsKey($required) -or [string]::IsNullOrWhiteSpace($settings[$required])) {
        throw "User secrets are missing $($required.Replace('__', ':')). The application will not start without it."
    }
}

$clientId = $settings['EntraId__ClientId']
$applicationIdUri = "api://$Domain/$clientId"

# ---------------------------------------------------------------------------------------------
# A key ring and a certificate to encrypt it with, which a non-Development host refuses to start
# without

$keyRing = Join-Path $walk 'keyring'
$certificate = Join-Path $walk 'dataprotection.pfx'
$passwordFile = Join-Path $walk 'dataprotection.password'

New-Item -ItemType Directory -Force -Path $keyRing | Out-Null

if (-not (Test-Path $certificate)) {
    Write-Host 'Creating a Data Protection certificate for this walk...' -ForegroundColor Cyan

    # Not the TLS certificate — the tunnel provides that one, and this never faces the network. All
    # it does is encrypt the key ring at rest, so self-signed is exactly right and a public CA would
    # be meaningless. Kept in a gitignored folder with its password beside it: the threat it answers
    # is "the master keys are sitting on disk in plain XML", not a local attacker.
    $password = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $secure = ConvertTo-SecureString -String $password -AsPlainText -Force

    $created = New-SelfSignedCertificate `
        -Subject 'CN=TodoWerk Teams tab walk — Data Protection' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -KeyExportPolicy Exportable `
        -KeySpec KeyExchange `
        -NotAfter (Get-Date).AddYears(2)

    try {
        Export-PfxCertificate -Cert $created -FilePath $certificate -Password $secure | Out-Null
        Set-Content -Path $passwordFile -Value $password -NoNewline
    } finally {
        # The file is the copy that matters; leaving it in the store as well is one more place to
        # remember to clean up.
        Remove-Item -Path "Cert:\CurrentUser\My\$($created.Thumbprint)" -Force
    }
}

$settings['DataProtection__KeyRingPath'] = $keyRing
$settings['DataProtection__CertificatePath'] = $certificate
$settings['DataProtection__CertificatePassword'] = (Get-Content -Path $passwordFile -Raw)

# ---------------------------------------------------------------------------------------------
# The rest

$settings['EntraId__ApplicationIdUri'] = $applicationIdUri
$settings['ASPNETCORE_ENVIRONMENT'] = $Environment

# Without these the tunnel's X-Forwarded-Proto is not believed, the app sees plain HTTP, and
# UseHttpsRedirection — which only runs outside Development — bounces every request to an HTTPS port
# that is not the one the tunnel is forwarding to. The loopback only: TodoWerk deliberately trusts
# forwarded headers from named proxies alone, and the tunnel agent is running on this machine.
$settings['ForwardedHeaders__KnownProxies__0'] = '127.0.0.1'
$settings['ForwardedHeaders__KnownProxies__1'] = '::1'

foreach ($name in $settings.Keys) {
    Set-Item -Path "Env:$name" -Value $settings[$name]
}

# ---------------------------------------------------------------------------------------------
# What the rest of the walk needs to agree with

Write-Host ''
Write-Host 'Tunnel — run this in another terminal, and leave it running:' -ForegroundColor Cyan
Write-Host "  ngrok http $Port --url=$Domain" -ForegroundColor White
Write-Host '  (an older agent spells that flag --domain)' -ForegroundColor DarkGray
Write-Host ''
Write-Host 'The app registration must name this host — Entra ID -> App registrations -> your app:' -ForegroundColor Cyan
Write-Host "  Expose an API -> Application ID URI   $applicationIdUri" -ForegroundColor White
Write-Host '  Authentication -> Web -> Redirect URIs:' -ForegroundColor White
foreach ($path in 'signin-oidc', 'signout-callback-oidc', 'auth/tenant-consent/callback', 'teams/auth-end') {
    Write-Host "    https://$Domain/$path" -ForegroundColor White
}
Write-Host ''
Write-Host 'The App Package has to be rebuilt for this host — see packaging/teams/values.self-host.json:' -ForegroundColor Cyan
Write-Host "  Host              $Domain" -ForegroundColor White
Write-Host "  ApplicationIdUri  $applicationIdUri" -ForegroundColor White
Write-Host '  pwsh ./scripts/New-TodoWerkTeamsAppPackage.ps1 -ValuesPath ./my-values.json' -ForegroundColor White
Write-Host ''
Write-Host "Environment $Environment — cookies are SameSite=None; Secure and the CSP is sent, which is what the tab needs." -ForegroundColor Green
Write-Host ''

if ($SkipRun) {
    return
}

# --no-launch-profile, because launchSettings.json pins ASPNETCORE_ENVIRONMENT to Development and a
# launch profile wins over the variable set above — which would put everything back to Lax cookies
# and no policy, silently.
& dotnet run --project $project --no-launch-profile --urls "http://localhost:$Port"

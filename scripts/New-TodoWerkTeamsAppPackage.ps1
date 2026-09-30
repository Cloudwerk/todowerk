#Requires -Version 7.0
<#
.SYNOPSIS
    Builds a Microsoft Teams App Package for TodoWerk from the one source manifest.

.DESCRIPTION
    Every package comes from one manifest, and that is the point. A Self-Host builds its own against
    its own host and its own Entra ID app registration, because Teams single sign-on does not
    support several domains per app (docs/adr/0011-two-app-packages-and-a-template.md).

    Everything except four values - the manifest id, the host, the client id and the Application ID
    URI - is generated from packaging/teams/manifest.template.json, so packages built for different
    hosts cannot drift apart in the ways that matter and nobody has to diff JSON files to find out.

    This script reads those values from a values file you point it at; the one in the repository
    (packaging/teams/values.self-host.json) holds placeholders only.

.PARAMETER ValuesPath
    A JSON file holding ManifestId, Host, ClientId, ApplicationIdUri and Version. Copy
    packaging/teams/values.self-host.json and fill it in.

.PARAMETER ManifestId
    Overrides ManifestId from the values file. A GUID, generated once and never changed: it is the
    identity every tenant that installs the package records, and changing it later is a
    tenant-by-tenant uninstall rather than a deploy.

.PARAMETER HostName
    Overrides Host. The host TodoWerk answers on, with no scheme and no trailing slash.
    (Named HostName rather than Host because $Host is one of PowerShell's own variables.)

.PARAMETER ClientId
    Overrides ClientId. The application (client) id of the Entra ID app registration.

.PARAMETER ApplicationIdUri
    Overrides ApplicationIdUri. Defaults to api://<HostName>/<ClientId>, which is the only shape
    Teams single sign-on accepts for a tab.

.PARAMETER DeveloperName
    Overrides DeveloperName. What a tenant's admin centre shows as the publisher of the app it just
    uploaded, so it has to be the legal name of whoever runs the deployment rather than of whoever
    wrote the software. The template ships a placeholder and the script refuses to build with it.

.PARAMETER AllowPlaceholderDeveloperName
    Builds anyway with the placeholder developer name. There is exactly one legitimate use - the
    Self-Host template package, whose whole purpose is to ship values nobody has filled in yet,
    alongside its zeroed GUIDs and its example.com host. Passing it for any package that will reach
    a tenant produces a package that names nobody.

.PARAMETER MpnId
    Overrides MpnId. The Microsoft Partner Network id of the organisation publishing the package.
    Optional and usually absent: the schema says to supply it only if you are already in the Partner
    Network, so a Self-Host that is not simply leaves it out and the property is dropped entirely
    rather than emitted empty.

.PARAMETER PackageVersion
    Overrides Version. Increase it whenever you re-upload, or Teams will refuse the update.

.PARAMETER OutputPath
    Where to write the .zip. Defaults to artifacts/teams/todowerk-teams-<host>.zip.

.EXAMPLE
    pwsh ./scripts/New-TodoWerkTeamsAppPackage.ps1 -ValuesPath ./my-values.json

.EXAMPLE
    pwsh ./scripts/New-TodoWerkTeamsAppPackage.ps1 `
        -ManifestId 6f2b1c9e-... -HostName todowerk.contoso.com -ClientId 1a2b3c4d-...

.NOTES
    Validate the result before it goes anywhere: https://dev.teams.microsoft.com/tools/store-validation
    Its verdict covers both distribution channels - a package that passes is as good for a manual
    upload to a tenant's own catalog as it is for the Store.
#>
[CmdletBinding()]
param(
    [string] $ValuesPath,
    [string] $ManifestId,
    [string] $HostName,
    [string] $ClientId,
    [string] $ApplicationIdUri,
    [string] $DeveloperName,
    [switch] $AllowPlaceholderDeveloperName,
    [string] $MpnId,
    [string] $PackageVersion,
    [string] $OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$packagingRoot = Join-Path $repositoryRoot 'packaging/teams'
$templatePath = Join-Path $packagingRoot 'manifest.template.json'

# The developer name the template ships with. It is a value rather than a literal because a
# Self-Host's package must not name CloudWerk GmbH as the developer of software CloudWerk did not
# deploy - the admin centre shows this string to the tenant that installed it.
$placeholderDeveloperName = 'Your organisation'

foreach ($required in @($templatePath, (Join-Path $packagingRoot 'color.png'), (Join-Path $packagingRoot 'outline.png'))) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing $required. This script builds the package from packaging/teams; run it from a checkout of the repository."
    }
}

$values = @{}

if ($ValuesPath) {
    if (-not (Test-Path -LiteralPath $ValuesPath)) {
        throw "No values file at $ValuesPath."
    }

    $fromFile = Get-Content -LiteralPath $ValuesPath -Raw | ConvertFrom-Json

    foreach ($name in @('ManifestId', 'Host', 'ClientId', 'ApplicationIdUri', 'DeveloperName', 'MpnId', 'Version')) {
        if ($fromFile.PSObject.Properties.Name -contains $name) {
            $values[$name] = $fromFile.$name
        }
    }
}

# Explicit parameters win over the file, so one value can be changed without a second file.
if ($ManifestId) { $values['ManifestId'] = $ManifestId }
if ($HostName) { $values['Host'] = $HostName }
if ($ClientId) { $values['ClientId'] = $ClientId }
if ($ApplicationIdUri) { $values['ApplicationIdUri'] = $ApplicationIdUri }
if ($DeveloperName) { $values['DeveloperName'] = $DeveloperName }
if ($MpnId) { $values['MpnId'] = $MpnId }
if ($PackageVersion) { $values['Version'] = $PackageVersion }

if (-not $values.ContainsKey('Version') -or -not $values['Version']) {
    $values['Version'] = '1.0.0'
}

foreach ($name in @('ManifestId', 'Host', 'ClientId', 'DeveloperName')) {
    if (-not $values.ContainsKey($name) -or -not $values[$name]) {
        throw "$name was not supplied. Pass -ValuesPath, or -$(if ($name -eq 'Host') { 'HostName' } else { $name })."
    }
}

if (-not $values.ContainsKey('ApplicationIdUri') -or -not $values['ApplicationIdUri']) {
    # The only shape Teams single sign-on accepts for a tab, and the one the app registration has
    # to be configured with (docs/runbooks/teams-app-registration.md).
    $values['ApplicationIdUri'] = "api://$($values['Host'])/$($values['ClientId'])"
}

if ($values['Host'] -match '^[a-z]+://' -or $values['Host'].EndsWith('/')) {
    throw "Host must be a bare host name - no scheme and no trailing slash. Got '$($values['Host'])'."
}

# A Self-Host that never changed this ships a package whose publisher, as the installing tenant's
# admin centre shows it, is a placeholder - or worse, somebody else. Refused rather than warned,
# because nothing downstream looks at it again and the tenant is the first to see it. The Self-Host
# template package is the one build where that is the point, and it says so on the command line.
if ($values['DeveloperName'].Trim() -eq $placeholderDeveloperName -and -not $AllowPlaceholderDeveloperName) {
    throw "DeveloperName is still '$placeholderDeveloperName'. Set it to the legal name of whoever runs this deployment - it is what a tenant's admin centre shows as the publisher of the app it installed."
}

foreach ($name in @('ManifestId', 'ClientId')) {
    $parsed = [guid]::Empty

    if (-not [guid]::TryParse($values[$name], [ref] $parsed)) {
        throw "$name must be a GUID. Got '$($values[$name])'."
    }
}

$manifest = Get-Content -LiteralPath $templatePath -Raw

# mpnId is the one optional value, and an empty one is worse than none: the schema caps it at ten
# characters and says to supply it only if you are already in the Partner Network, so a Self-Host
# that is not gets the property removed rather than emitted blank.
if ($values.ContainsKey('MpnId') -and $values['MpnId']) {
    if ($values['MpnId'].Length -gt 10) {
        throw "MpnId must be at most 10 characters. Got '$($values['MpnId'])'."
    }
}
else {
    $manifest = $manifest -replace '(?m)^[ \t]*"mpnId": "\{\{MpnId\}\}",?\r?\n', ''
}

foreach ($name in @('ManifestId', 'Host', 'ClientId', 'ApplicationIdUri', 'DeveloperName', 'MpnId', 'Version')) {
    if ($values.ContainsKey($name)) {
        $manifest = $manifest.Replace("{{$name}}", $values[$name])
    }
}

# Nothing may reach a tenant with a placeholder still in it: an unsubstituted id installs as a
# different app, and an unsubstituted host is a tab that loads somebody else's page.
if ($manifest -match '\{\{[A-Za-z]+\}\}') {
    throw "The manifest still contains a placeholder ($($Matches[0])). Every value must be supplied."
}

# The theme placeholders are the app host's to substitute, not this script's, and they must survive
# verbatim - `{app.theme}` for desktop and web, `{theme}` for mobile, which substitutes only the v1
# spellings. A build that lost them is a tab that always paints light.
foreach ($placeholder in @('{app.theme}', '{theme}')) {
    if (-not $manifest.Contains($placeholder)) {
        throw "The manifest lost the $placeholder placeholder, so the tab would not follow the Teams theme."
    }
}

# Parses, and is the shape the tab depends on. A malformed manifest is rejected on upload with a
# message about JSON rather than about what is wrong.
$parsedManifest = $manifest | ConvertFrom-Json

if ($parsedManifest.staticTabs[0].scopes -ne 'personal') {
    throw 'The static tab must be personal-scoped: the Hashtag Manager is one person''s hashtags and has nothing to show a team.'
}

if (-not $OutputPath) {
    $OutputPath = Join-Path $repositoryRoot "artifacts/teams/todowerk-teams-$($values['Host']).zip"
}

$outputDirectory = Split-Path -Parent $OutputPath

if ($outputDirectory -and -not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

# Staged in a temporary directory and zipped from there, because a Teams App Package is a zip whose
# three files sit at its root - not inside a folder. Compress-Archive on a directory would nest them.
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "todowerk-teams-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $staging -Force | Out-Null

try {
    Set-Content -LiteralPath (Join-Path $staging 'manifest.json') -Value $manifest -Encoding utf8NoBOM
    Copy-Item -LiteralPath (Join-Path $packagingRoot 'color.png') -Destination $staging
    Copy-Item -LiteralPath (Join-Path $packagingRoot 'outline.png') -Destination $staging

    if (Test-Path -LiteralPath $OutputPath) {
        Remove-Item -LiteralPath $OutputPath -Force
    }

    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $OutputPath -CompressionLevel Optimal
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Built $OutputPath"
Write-Host "  manifest id       $($values['ManifestId'])"
Write-Host "  host              $($values['Host'])"
Write-Host "  client id         $($values['ClientId'])"
Write-Host "  Application ID URI $($values['ApplicationIdUri'])"
Write-Host "  developer name    $($values['DeveloperName'])"
Write-Host "  MPN id            $(if ($values.ContainsKey('MpnId') -and $values['MpnId']) { $values['MpnId'] } else { '(none - property omitted)' })"
Write-Host "  version           $($values['Version'])"
Write-Host ''
Write-Host 'Validate it before it goes anywhere: https://dev.teams.microsoft.com/tools/store-validation'

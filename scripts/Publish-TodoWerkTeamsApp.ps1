#Requires -Version 7.0
<#
.SYNOPSIS
    Uploads a TodoWerk Teams App Package to an organisation's app catalog, over raw Microsoft Graph.

.DESCRIPTION
    A convenience, and documented as one. The install an administrator is *told* to perform is
    manual - upload the package in the Teams admin center, or install from the Microsoft Store where
    there is a listing (docs/runbooks/teams-app-install.md). That path needs no tooling, no module,
    and no trust in a script from the internet.

    This exists for the administrator who would rather not click through the admin center. It calls
    the same Graph endpoint the admin center calls, with Invoke-RestMethod and nothing else: no
    MicrosoftTeams module, no Microsoft.Graph.Teams module, no dependency to install before you can
    evaluate the product.

    UNATTENDED USE IS IMPOSSIBLE, AND NOT BECAUSE THIS SCRIPT DECLINES TO OFFER IT. Publishing to an
    organisation's app catalog requires AppCatalog.ReadWrite.All as a delegated permission: the
    Graph publish operation supports no application permission, so there is no client credential
    that can do this and no version of this script that could run in a pipeline. It signs an
    administrator in interactively, every time, with a device code.

.PARAMETER PackagePath
    The .zip built by New-TodoWerkTeamsAppPackage.ps1.

.PARAMETER TenantId
    The tenant to publish into - a tenant id or a verified domain. Defaults to 'organizations',
    which lets the person signing in decide.

.PARAMETER TeamsAppId
    The catalog id of an already-published TodoWerk, to publish a new version of it rather than a
    new app. Printed by a previous run, and shown in the Teams admin center.

.PARAMETER ClientId
    The public client the device-code sign-in runs as. Defaults to Microsoft's own Graph PowerShell
    client, which exists in every tenant and needs no app registration of your own - which is the
    point. Override it if your organisation blocks that client, or if you would rather use your own.

.EXAMPLE
    pwsh ./scripts/Publish-TodoWerkTeamsApp.ps1 -PackagePath ./artifacts/teams/todowerk-teams-contoso.zip

.EXAMPLE
    pwsh ./scripts/Publish-TodoWerkTeamsApp.ps1 -PackagePath ./package.zip -TeamsAppId 8a1b2c3d-...

.NOTES
    Nothing in the runbook requires this script. If it fails for a reason you would rather not
    debug, the admin center upload does the same thing and always works.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [string] $TenantId = 'organizations',

    [string] $TeamsAppId,

    # Microsoft Graph PowerShell. A first-party public client present in every tenant, so nobody
    # has to create an app registration before they can try TodoWerk.
    [string] $ClientId = '14d82eec-204b-4c2f-b7e8-296a70dab67e'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

<#
    Microsoft returns its OAuth failures as a JSON body on a 400, which PowerShell surfaces as a
    terminating error with the body attached. Read rather than guessed at, because the device-code
    loop is driven entirely by which failure came back: "nobody has clicked yet" and "they said no"
    are the same status code.
#>
function Read-OAuthError {
    param([System.Management.Automation.ErrorRecord] $Record)

    try {
        return ($Record.ErrorDetails.Message | ConvertFrom-Json).error
    }
    catch {
        return 'unknown_error'
    }
}

<# Graph's own error shape, so a failure reads as a sentence rather than as a status code. #>
function Read-GraphError {
    param([System.Management.Automation.ErrorRecord] $Record)

    try {
        return ($Record.ErrorDetails.Message | ConvertFrom-Json).error.message
    }
    catch {
        return $Record.Exception.Message
    }
}

function Get-StatusCode {
    param([System.Management.Automation.ErrorRecord] $Record)

    $response = $Record.Exception.PSObject.Properties['Response']

    if ($response -and $response.Value) {
        return [int] $response.Value.StatusCode
    }

    return 0
}

if (-not (Test-Path -LiteralPath $PackagePath)) {
    throw "No package at $PackagePath. Build one first: ./scripts/New-TodoWerkTeamsAppPackage.ps1"
}

$scope = 'https://graph.microsoft.com/AppCatalog.ReadWrite.All offline_access'
$authority = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0"

Write-Host 'Signing in. This is interactive every time: publishing to an app catalog is a'
Write-Host 'delegated-only permission, so there is no unattended path and none can be added.'
Write-Host ''

$deviceCode = Invoke-RestMethod -Method Post -Uri "$authority/devicecode" -Body @{
    client_id = $ClientId
    scope     = $scope
}

Write-Host $deviceCode.message
Write-Host ''

$token = $null
$deadline = (Get-Date).AddSeconds([int] $deviceCode.expires_in)
$interval = [Math]::Max([int] $deviceCode.interval, 1)

while ($null -eq $token) {
    if ((Get-Date) -gt $deadline) {
        throw 'The device code expired before anybody signed in. Run this again.'
    }

    Start-Sleep -Seconds $interval

    try {
        $token = Invoke-RestMethod -Method Post -Uri "$authority/token" -Body @{
            client_id   = $ClientId
            grant_type  = 'urn:ietf:params:oauth:grant-type:device_code'
            device_code = $deviceCode.device_code
        }
    }
    catch {
        # Plain conditionals rather than a switch: `continue` inside a PowerShell switch belongs to
        # the switch rather than to the loop around it, and a polling loop is the worst place to
        # rely on remembering that.
        $failure = Read-OAuthError $_

        if ($failure -eq 'authorization_pending') {
            # Nobody has finished signing in yet, which is what most of this loop is.
        }
        elseif ($failure -eq 'slow_down') {
            # Microsoft asking for a slower poll, rather than refusing.
            $interval += 5
        }
        elseif ($failure -eq 'authorization_declined') {
            throw 'Sign-in was declined. Nothing was published.'
        }
        elseif ($failure -eq 'expired_token') {
            throw 'The device code expired before anybody signed in. Run this again.'
        }
        else {
            throw "Sign-in failed ($failure). Nothing was published."
        }
    }
}

$headers = @{ Authorization = "Bearer $($token.access_token)" }
$package = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $PackagePath))

# Two endpoints, and which is right depends on whether this app is already in the catalog. Posting
# a package for an app that is already there is refused as a conflict rather than treated as an
# update, and the message Graph returns for it does not say so.
$uri = if ($TeamsAppId) {
    "https://graph.microsoft.com/v1.0/appCatalogs/teamsApps/$TeamsAppId/appDefinitions"
}
else {
    'https://graph.microsoft.com/v1.0/appCatalogs/teamsApps'
}

try {
    $published = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers `
        -ContentType 'application/zip' -Body $package
}
catch {
    $status = Get-StatusCode $_
    $detail = Read-GraphError $_

    if ($status -eq 401) {
        throw "Microsoft rejected the token. Sign in again, or pass -ClientId naming an app registration your organisation allows. ($detail)"
    }

    if ($status -eq 403) {
        throw @'
The account that signed in is not allowed to publish apps to this organisation's catalog.

Publishing needs AppCatalog.ReadWrite.All, which only a Teams Administrator or a Global
Administrator can consent to and use. Either sign in as one, or take the manual route instead:
Teams admin center > Teams apps > Manage apps > Upload new app.
'@
    }

    if ($status -eq 409) {
        throw @"
This organisation's catalog already has an app with this manifest id.

That is an update rather than a new app, and Graph will not treat it as one on its own. Find the
catalog id in the Teams admin center and run this again with -TeamsAppId <id>. If you meant to
publish something new, it needs a manifest id of its own. ($detail)
"@
    }

    throw "Publishing failed with HTTP $status. $detail"
}

Write-Host ''
Write-Host 'Published.'

if ($published.PSObject.Properties['id']) {
    Write-Host "  catalog id  $($published.id)"
    Write-Host '  Keep this: publishing the next version needs it, as -TeamsAppId.'
}

Write-Host ''
Write-Host 'It can take a few minutes to appear for everybody in Teams.'

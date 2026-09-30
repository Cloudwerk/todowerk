#Requires -Version 7.4
<#
.SYNOPSIS
    Walks a human through verifying Tenant Consent against a real Entra ID tenant.

.DESCRIPTION
    Interactive wizard for the verification documented in
    docs/runbooks/tenant-consent-verification.md. It performs no step itself — every action
    happens in your browser, as the administrator — but it opens the right pages, says exactly
    what to click, asks what you observed, and writes a results file you can paste findings from.

    The results file lands OUTSIDE the repository by default, because it names your tenant.
    This repository is public: paste generalised findings into an issue, never the raw file.

    What is verified (decline before approve, deliberately):
      1. The admin-consent redirect URI is registered on the app registration.
      2. An administrator DECLINING records nothing — the invitation survives.
         (The real decline redirect carries admin_consent=True alongside error=consent_required,
         which is why this step exists: the flag names the flow, not the decision.)
      3. An administrator APPROVING lands the grant: in TodoWerk, and in Entra ID's
         enterprise-app Permissions blade as delegated admin consent.
      4. A second, never-consented user signs in with no consent prompt — the point of it all.

.PARAMETER BaseUrl
    The TodoWerk origin to verify against. Default: https://localhost:7080 (run from source).

.PARAMETER ResultsPath
    Where observations are written. Default: a dated markdown file in your user profile,
    outside any repository.

.EXAMPLE
    pwsh ./scripts/Verify-TenantConsent.ps1
    pwsh ./scripts/Verify-TenantConsent.ps1 -BaseUrl https://todowerk.example.com
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://localhost:7080',
    [string]$ResultsPath = (Join-Path $env:USERPROFILE ("todowerk-consent-verification-{0:yyyy-MM-dd}.md" -f (Get-Date)))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BaseUrl = $BaseUrl.TrimEnd('/')
$script:StageIndex = 0
$script:TotalStages = 6
$script:Results = [System.Collections.Generic.List[string]]::new()

function Write-Stage {
    param([string]$Title)
    $script:StageIndex++
    Clear-Host
    Write-Host ''
    Write-Host ("=== Stage {0} of {1} — {2} ===" -f $script:StageIndex, $script:TotalStages, $Title) -ForegroundColor Cyan
    Write-Host ''
}

function Say { param([string]$Text) Write-Host $Text }
function Step { param([string]$Text) Write-Host ("  -> {0}" -f $Text) -ForegroundColor Yellow }

function Open-Page {
    param([string]$Url)
    Step "Opening: $Url"
    Start-Process $Url
}

function Ask {
    param([string]$Prompt)
    Write-Host ''
    Read-Host ("{0}" -f $Prompt)
}

function Confirm-Step {
    param([string]$Prompt)
    while ($true) {
        $answer = (Read-Host ("{0} [y/n]" -f $Prompt)).Trim().ToLowerInvariant()
        if ($answer -in @('y', 'yes')) { return $true }
        if ($answer -in @('n', 'no')) { return $false }
        Say "Please answer y or n."
    }
}

function Record {
    param([string]$Line)
    $script:Results.Add($Line)
}

function Wait-Enter {
    param([string]$Prompt = 'Press Enter when done')
    $null = Read-Host $Prompt
}

try {
    # --- Stage 1: prerequisites -----------------------------------------------------------
    Write-Stage 'Prerequisites'
    Say  "This wizard verifies Tenant Consent against: $BaseUrl"
    Say  ''
    Say  'You need, before continuing:'
    Step 'An account able to edit the app registration (owner or Application Administrator).'
    Step 'An account able to grant tenant-wide admin consent (e.g. Global Administrator).'
    Step 'A second, NEVER-consented member account in the tenant, for the no-prompt check.'
    Step "TodoWerk answering at $BaseUrl (from source: CONTRIBUTING > Development setup)."
    Say  ''

    try {
        $response = Invoke-WebRequest -Uri "$BaseUrl/" -SkipCertificateCheck -Method Get -TimeoutSec 10
        Say ("[OK] {0} answers with HTTP {1}." -f $BaseUrl, $response.StatusCode)
    }
    catch {
        Say ("[WARN] {0} did not answer: {1}" -f $BaseUrl, $_.Exception.Message)
        if (-not (Confirm-Step 'Continue anyway (start the app first in another terminal)?')) { exit 1 }
    }

    Record ("# Tenant Consent verification — {0:yyyy-MM-dd HH:mm}" -f (Get-Date))
    Record ''
    Record ("Origin verified: {0}" -f $BaseUrl)
    Record ''
    Say ''
    Say "Observations are written to: $ResultsPath"
    Say '[WARN] That file will name your tenant. Keep it out of the public repository.'
    Wait-Enter

    # --- Stage 2: redirect URI ------------------------------------------------------------
    Write-Stage 'Register the admin-consent redirect URI'
    $callbackUri = "$BaseUrl/auth/tenant-consent/callback"
    Say  'The app registration must carry the consent callback as a Web redirect URI —'
    Say  'Microsoft matches it exactly, and a missing entry fails with a redirect-URI'
    Say  'mismatch before any TodoWerk code runs.'
    Say  ''
    Open-Page 'https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade'
    Step 'Open your TodoWerk app registration > Manage > Authentication.'
    Step "Under the Web platform, ensure this exact URI is listed (add it if not):"
    Say  ''
    Say  "    $callbackUri"
    Say  ''
    if (Confirm-Step 'Is the URI registered (added now, or already present)?') {
        Record ("- [x] Redirect URI registered: ``{0}``" -f $callbackUri)
    }
    else {
        Record ("- [ ] Redirect URI NOT registered — remaining stages will fail at Microsoft.")
        if (-not (Confirm-Step 'Continue anyway?')) { exit 1 }
    }

    # --- Stage 3: decline pass ------------------------------------------------------------
    Write-Stage 'Decline pass (before approving — order matters)'
    Say  'A decline must record nothing. The real decline redirect carries admin_consent=True'
    Say  'ALONGSIDE error=consent_required, so this pass exists to prove the callback reads'
    Say  'the error, not the flag. Running it before any approval observes the pristine state.'
    Say  ''
    Say  'Optional byte-exact capture: press F12 first, open the Network tab, tick'
    Say  '"Preserve log" — the callback request with its full query string will be kept.'
    Say  ''
    Open-Page "$BaseUrl/tenant"
    Step 'Sign in as the administrator if asked.'
    Step 'Confirm the page shows the approval invitation ("Approve TodoWerk for everyone").'
    Step 'Click "Approve for the organisation" and, on the Microsoft consent screen, click CANCEL.'
    Step 'You land back on the organisation page.'
    Wait-Enter
    Say ''
    $invitationSurvived = Confirm-Step 'Does the invitation still show, with NO approval recorded?'
    Record ($invitationSurvived `
        ? '- [x] Decline records nothing: the invitation survived Cancel.' `
        : '- [ ] DEFECT: a decline was recorded as an approval — the invitation is gone.')
    $captured = Ask 'If you captured the callback query string, paste it (Enter to skip)'
    if ($captured) { Record ("  - Decline callback observed: ``{0}``" -f $captured) }
    if (-not $invitationSurvived) {
        Say '[ERROR] A declined consent was recorded. Stop and file an issue before approving.'
        if (-not (Confirm-Step 'Continue to the approve pass anyway?')) { throw 'Stopped after decline defect.' }
    }

    # --- Stage 4: approve pass ------------------------------------------------------------
    Write-Stage 'Approve pass'
    Open-Page "$BaseUrl/tenant"
    Step 'Click "Approve for the organisation" again.'
    Step 'On the Microsoft consent screen, check the permission list matches what sign-in'
    Step 'shows an individual (profile, maintain access, tasks read/write) plus the'
    Step '"for all users in your organisation" sentence — then click ACCEPT.'
    Step 'You land back on the organisation page.'
    Wait-Enter
    Say ''
    $recorded = Confirm-Step 'Does the page now show "Approved for the organisation"?'
    Record ($recorded `
        ? '- [x] Approval recorded through TodoWerk: invitation gave way to the note.' `
        : '- [ ] DEFECT: approval did not record — the invitation still shows after Accept.')
    $screenMatched = Confirm-Step 'Did the consent screen list exactly the sign-in permissions (nothing wider)?'
    Record ($screenMatched `
        ? '- [x] Consent screen shows the same permissions sign-in shows an individual.' `
        : '- [ ] Consent screen differed from sign-in — record how, and compare GraphScopes.')

    # --- Stage 5: ground truth in Entra ---------------------------------------------------
    Write-Stage 'Ground truth: the grant in Entra ID'
    Say  'TodoWerk records its own note; Entra ID holds the truth. Check both agree.'
    Say  ''
    Open-Page 'https://entra.microsoft.com/#view/Microsoft_AAD_IAM/StartboardApplicationsMenuBlade/~/AppAppsPreview'
    Step 'Enterprise applications > your TodoWerk app > Security > Permissions.'
    Step 'On the "Admin consent" tab: Microsoft Graph delegated permissions, granted by'
    Step 'an administrator. There must be NO application permission of any kind (ADR-0008).'
    Wait-Enter
    Say ''
    $delegatedOnly = Confirm-Step 'Delegated admin-consent grants present, and no application permission?'
    Record ($delegatedOnly `
        ? '- [x] Entra ID shows delegated admin consent only — no application permission.' `
        : '- [ ] Entra ID disagrees with expectations — record exactly what the blade shows.')

    # --- Stage 6: second user, no prompt --------------------------------------------------
    Write-Stage 'Second user signs in with no consent prompt'
    Say  'The point of it all. The account must never have consented to TodoWerk individually —'
    Say  'an account that once consented proves nothing.'
    Say  ''
    Open-Page "$BaseUrl/"
    Step 'Sign the administrator out of TodoWerk.'
    Step 'Sign in as the never-consented member account (use "Use another account" if offered).'
    Step 'Watch for one thing: between credentials/MFA and the Workbench, does a'
    Step '"Permissions requested" consent screen appear at any point?'
    Wait-Enter
    Say ''
    $noPrompt = Confirm-Step 'Did the second user land in the Workbench with NO consent screen?'
    Record ($noPrompt `
        ? '- [x] Second user signed in with no consent prompt.' `
        : '- [ ] DEFECT: the second user was still prompted — tenant consent did not suppress it.')

    # --- Summary --------------------------------------------------------------------------
    Clear-Host
    Record ''
    Record 'File findings as generalised issue comments; do not commit this file to the public repository.'
    Set-Content -LiteralPath $ResultsPath -Value ($script:Results -join [Environment]::NewLine)
    Write-Host ''
    Write-Host '=== Verification complete ===' -ForegroundColor Cyan
    Write-Host ''
    foreach ($line in $script:Results) { Write-Host $line }
    Write-Host ''
    Write-Host ("[OK] Results written to: {0}" -f $ResultsPath) -ForegroundColor Green
    Write-Host '[WARN] The file names your tenant. Paste generalised findings into an issue; never the raw file.'
}
catch {
    Write-Error ("[ERROR] {0}" -f $_.Exception.Message)
    if ($script:Results.Count -gt 0) {
        Set-Content -LiteralPath $ResultsPath -Value ($script:Results -join [Environment]::NewLine)
        Write-Host ("Partial results written to: {0}" -f $ResultsPath)
    }
    exit 1
}

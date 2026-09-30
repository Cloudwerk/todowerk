#Requires -Version 7.4
#Requires -Modules Microsoft.Graph.Authentication

<#
.SYNOPSIS
    Seeds a scratch Microsoft To Do list with probe tasks that pin down what the To Do
    clients treat as a hashtag, and reports what Microsoft Graph stored byte-for-byte.

.DESCRIPTION
    Microsoft documents nothing about hashtags in To Do. They are a client-side rendering
    affordance over todoTask.title, absent from the Graph schema, from the To Do API
    reference, and from the PST export field mapping. TodoWerk therefore has to establish
    the grammar by observation, and ADR-0005 records the result.

    The script produces two kinds of evidence:

      Automatic  - what Graph accepted and returned, compared codepoint by codepoint.
                   This settles Unicode normalisation on its own, no human needed.
      Manual     - what the clients actually highlight and what clicking a hashtag finds.
                   Only eyes on a real client can answer this, so the script emits a
                   Markdown results template to fill in.

    Run it against a test tenant, open the probe list in at least two clients,
    fill in the template, then feed the answers back into ADR-0005.

.PARAMETER ListName
    Display name of the scratch list. Created if missing.

.PARAMETER ResultsPath
    Where to write the Markdown results template. Defaults to
    ./hashtag-probe-results.md in the current directory.

.PARAMETER Force
    Required to wipe tasks from an existing probe list before reseeding.

.PARAMETER Cleanup
    Delete the probe list and everything in it, then exit.

.EXAMPLE
    ./Probe-HashtagGrammar.ps1
    Seeds the probes and writes the results template.

.EXAMPLE
    ./Probe-HashtagGrammar.ps1 -Cleanup
    Removes the probe list from the tenant.

.NOTES
    Requires the Tasks.ReadWrite delegated scope. It writes only to its own scratch list
    and never touches an existing one unless you point -ListName at it and pass -Force.
#>

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [ValidateNotNullOrEmpty()]
    [string] $ListName = 'TodoWerk hashtag probe',

    [string] $ResultsPath = (Join-Path -Path (Get-Location) -ChildPath 'hashtag-probe-results.md'),

    [switch] $Force,

    [switch] $Cleanup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Probe definitions
#
# Titles are deliberately German: an ASCII-only extractor silently truncating
# "#Pruefung" at the umlaut is the single highest-risk failure for this product, and it
# has to be visible in the very first probe.
#
# Every probe owns a unique token. Sharing "#work" across the punctuation probes would
# pollute the case-collision probe's search results and make P23/P24 unreadable.
# ---------------------------------------------------------------------------

$nfc = "P02 NFC #Pr$([char]0x00FC)fung".Normalize([Text.NormalizationForm]::FormC)
$nfd = "P03 NFD #Pr$([char]0x00FC)fung".Normalize([Text.NormalizationForm]::FormD)

$probes = @(
    @{ Id = 'P01'; Title = "P01 Umlaut #Pr$([char]0x00FC)fung"
       Question = 'Is the whole "#Prüfung" highlighted, or does the highlight stop before the "ü"?'
       Why      = 'An ASCII-only character class truncates German tags to "#Pr" and corrupts the index.' }

    @{ Id = 'P02'; Title = $nfc; Unicode = $true
       Question = 'Compare with P03 on screen — do they look identical?'
       Why      = 'Pairs with P03 to expose Unicode normalisation.' }

    @{ Id = 'P03'; Title = $nfd; Unicode = $true
       Question = 'Does clicking the tag on P02 also surface P03 (and the reverse)?'
       Why      = 'NFC and NFD "ü" are visually identical and byte-different. If Graph stores both verbatim and the clients treat them as two tags, TodoWerk must NFC-normalise or it will show two identical-looking rows.' }

    @{ Id = 'P04'; Title = "P04 Eszett #Stra$([char]0x00DF)e"; Unicode = $true
       Question = 'Is the whole "#Straße" highlighted?'
       Why      = 'The ß/ss fold is where .NET invariant-culture comparison and SQL CI collations disagree.' }

    @{ Id = 'P05'; Title = "P05 T$([char]0x00FC)rkisch #$([char]0x0130)stanbul"; Unicode = $true
       Question = 'Is the whole "#İstanbul" highlighted?'
       Why      = 'Dotted capital I is the classic case-folding trap; confirms the client accepts it as a tag character at all.' }

    @{ Id = 'P06'; Title = 'P06 Punkt am Ende #punktende.'
       Question = 'Does the highlight include the trailing "." or stop before it?'
       Why      = 'If "." is a tag character, "#punktende" and "#punktende." become two inventory rows — pure noise.' }

    @{ Id = 'P07'; Title = 'P07 Komma #kommatag, danach mehr Text'
       Question = 'Does the highlight include the ","?'
       Why      = 'Same duplication risk as P06, for tags written mid-sentence.' }

    @{ Id = 'P08'; Title = 'P08 Beschreibung (mit Klammer) #ProjectAlpha'
       Question = 'Is "#ProjectAlpha" highlighted normally?'
       Why      = 'A shape real titles take: a closing parenthesis and a space immediately before the tag. It must not interfere.' }

    @{ Id = 'P09'; Title = 'P09 Klammer um den Tag (#klammertag)'
       Question = 'Does the highlight stop before the ")"?'
       Why      = 'Decides whether ")" terminates a tag when it follows one directly.' }

    @{ Id = 'P10'; Title = 'P10 Doppelpunkt #Kunde:Contoso'
       Question = 'Is "#Kunde:Contoso" one tag, or does the highlight stop at "#Kunde"?'
       Why      = 'A common "Prefix: Value" list taxonomy competes with hashtags. If ":" is a tag character the two schemes collide inside the tag namespace.' }

    @{ Id = 'P11'; Title = 'P11 Bindestrich #kunde-contoso'
       Question = 'Is the whole "#kunde-contoso" highlighted, or only "#kunde"?'
       Why      = 'Hyphen is the most commonly assumed tag character and the least verified.' }

    @{ Id = 'P12'; Title = 'P12 Unterstrich #kunde_contoso'
       Question = 'Is the whole "#kunde_contoso" highlighted?'
       Why      = 'Same as P11 for underscore.' }

    @{ Id = 'P13'; Title = 'P13 Schraegstrich #kunde/contoso'
       Question = 'Does the highlight stop at "#kunde"?'
       Why      = 'Confirms "/" is a terminator rather than a tag character.' }

    @{ Id = 'P14'; Title = 'P14 Nur Ziffern #2026'
       Question = 'Is "#2026" highlighted as a tag at all?'
       Why      = 'Digit-only tokens are frequently excluded by hashtag grammars. Including them when the client does not would put phantom rows in the inventory.' }

    @{ Id = 'P15'; Title = 'P15 Ziffernstart #2026review'
       Question = 'Is "#2026review" highlighted?'
       Why      = 'Separates "no digits at all" from "no leading digit".' }

    @{ Id = 'P16'; Title = 'P16 Programmiersprache C# und mehr Text'
       Question = 'Is anything after the "#" highlighted? The expected answer is no.'
       Why      = 'If a "#" without a preceding boundary starts a tag, every task mentioning C# gets a bogus tag — and TodoWerk would offer to rename it.' }

    @{ Id = 'P17'; Title = 'P17 Mittendrin foo#bar Ende'
       Question = 'Is "#bar" highlighted? The expected answer is no.'
       Why      = 'Generalises P16: pins down that the rule is a preceding boundary, not a special case for "C#".' }

    @{ Id = 'P18'; Title = 'P18 Doppelte Raute ##doppelt'
       Question = 'What is highlighted — "##doppelt", "#doppelt", or nothing?'
       Why      = 'Decides whether "#" is itself a tag character and whether the extractor must handle repeated markers.' }

    @{ Id = 'P19'; Title = 'P19 Raute allein # ohne Wort'
       Question = 'Is anything highlighted?'
       Why      = 'A lone "#" must never produce an empty-named tag, which would be an unrenameable inventory row.' }

    @{ Id = 'P20'; Title = '#anfangstag P20 Tag steht am Anfang'
       Question = 'Is "#anfangstag" highlighted even though nothing precedes it?'
       Why      = 'Every third-party write-up says "space + #". If start-of-title does not count as a boundary, a whole class of real tags is invisible to the clients and must be excluded from the index too.' }

    @{ Id = 'P21'; Title = "P21 Emoji #emojitag$([char]0x2705)"; Unicode = $true
       Question = 'Is the "✅" part of the highlighted token or outside it?'
       Why      = 'Unicode letter classes exclude symbols. If the client includes them, the grammar is not a letter class.' }

    @{ Id = 'P22'; Title = 'P22 Tag nur in der Notiz'
       Notes    = 'Diese Notiz enthaelt #nurnotiz mitten im Text.'
       Question = 'Open the task. Is "#nurnotiz" highlighted in the note body, and is it clickable? Does search find it?'
       Why      = 'CONTEXT.md scopes Hashtag to the task title. If the clients tag note bodies too, that scope is a deliberate v1 limitation to state out loud, not an oversight.' }

    @{ Id = 'P23'; Title = 'P23 Grossschreibung #Kasus'
       Question = 'Click "#Kasus" here. Does P24 appear in the results?'
       Why      = 'THE case-sensitivity probe. Note this does not change TodoWerk identity — ADR-0005 folds case either way — but it decides whether the "casing inconsistent" flag describes a real defect in the user data or only a cosmetic one.' }

    @{ Id = 'P24'; Title = 'P24 Kleinschreibung #kasus'
       Question = 'Click "#kasus" here. Does P23 appear in the results?'
       Why      = 'The reverse direction of P23. Asymmetry would be a finding in itself.' }
)

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Format-CodePoint {
    <#
        .SYNOPSIS
            Renders a string as its Unicode codepoints, so an NFC/NFD difference that is
            invisible on screen becomes visible in the console.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)

    $points = [System.Collections.Generic.List[string]]::new()
    $i = 0
    while ($i -lt $Text.Length) {
        $value = [char]::ConvertToUtf32($Text, $i)
        $points.Add(('U+{0:X4}' -f $value))
        $i += if ([char]::IsHighSurrogate($Text[$i])) { 2 } else { 1 }
    }
    return ($points -join ' ')
}

function Get-ProbeList {
    <#
        .SYNOPSIS
            Returns the scratch list, or $null. Pages through every list rather than
            trusting $filter, which To Do list queries support inconsistently.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $DisplayName)

    $uri = 'https://graph.microsoft.com/v1.0/me/todo/lists'
    while ($uri) {
        $page = Invoke-MgGraphRequest -Method GET -Uri $uri
        foreach ($list in @($page['value'])) {
            # Ordinal, not -ceq: PowerShell's -ceq is culture-aware and would match a
            # canonically-equivalent name that is not the same string.
            if ([string]::Equals([string] $list['displayName'], $DisplayName, [StringComparison]::Ordinal)) {
                return $list
            }
        }
        $uri = if ($page.ContainsKey('@odata.nextLink')) { $page['@odata.nextLink'] } else { $null }
    }
    return $null
}

function Get-ListTask {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $ListId)

    $tasks = [System.Collections.Generic.List[hashtable]]::new()
    $uri = "https://graph.microsoft.com/v1.0/me/todo/lists/${ListId}/tasks"
    while ($uri) {
        $page = Invoke-MgGraphRequest -Method GET -Uri $uri
        foreach ($task in @($page['value'])) { $tasks.Add($task) }
        $uri = if ($page.ContainsKey('@odata.nextLink')) { $page['@odata.nextLink'] } else { $null }
    }
    return $tasks
}

# ---------------------------------------------------------------------------
# Connect
# ---------------------------------------------------------------------------

$context = Get-MgContext
if ($null -eq $context -or $context.Scopes -notcontains 'Tasks.ReadWrite') {
    Write-Host 'Signing in to Microsoft Graph (Tasks.ReadWrite)...' -ForegroundColor Cyan
    Connect-MgGraph -Scopes 'Tasks.ReadWrite' -NoWelcome
    $context = Get-MgContext
}
Write-Host "Signed in as $($context.Account)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# Cleanup mode
# ---------------------------------------------------------------------------

if ($Cleanup) {
    $existing = Get-ProbeList -DisplayName $ListName
    if ($null -eq $existing) {
        Write-Host "No list named '${ListName}' — nothing to clean up." -ForegroundColor Yellow
        return
    }

    $count = @(Get-ListTask -ListId $existing['id']).Count
    if ($PSCmdlet.ShouldProcess("list '${ListName}' and its ${count} task(s)", 'Delete')) {
        Invoke-MgGraphRequest -Method DELETE -Uri "https://graph.microsoft.com/v1.0/me/todo/lists/$($existing['id'])" | Out-Null
        Write-Host "Deleted '${ListName}' and its ${count} task(s)." -ForegroundColor Green
    }
    return
}

# ---------------------------------------------------------------------------
# Seed
# ---------------------------------------------------------------------------

$list = Get-ProbeList -DisplayName $ListName
if ($null -eq $list) {
    Write-Host "Creating list '${ListName}'..." -ForegroundColor Cyan
    $list = Invoke-MgGraphRequest -Method POST `
        -Uri 'https://graph.microsoft.com/v1.0/me/todo/lists' `
        -Body @{ displayName = $ListName }
}
else {
    $stale = @(Get-ListTask -ListId $list['id'])
    if ($stale.Count -gt 0) {
        if (-not $Force) {
            throw "List '${ListName}' already holds $($stale.Count) task(s). Re-run with -Force to wipe and reseed it, or with -Cleanup to remove the list entirely."
        }
        if ($PSCmdlet.ShouldProcess("$($stale.Count) task(s) in '${ListName}'", 'Delete')) {
            foreach ($task in $stale) {
                Invoke-MgGraphRequest -Method DELETE `
                    -Uri "https://graph.microsoft.com/v1.0/me/todo/lists/$($list['id'])/tasks/$($task['id'])" | Out-Null
            }
            Write-Host "Wiped $($stale.Count) stale task(s)." -ForegroundColor Yellow
        }
    }
}

$listId = $list['id']
$sent = [ordered]@{}

Write-Host ''
Write-Host "Seeding $($probes.Count) probe tasks..." -ForegroundColor Cyan

foreach ($probe in $probes) {
    $body = @{ title = $probe['Title'] }
    if ($probe.ContainsKey('Notes')) {
        $body['body'] = @{ content = $probe['Notes']; contentType = 'text' }
    }

    Invoke-MgGraphRequest -Method POST `
        -Uri "https://graph.microsoft.com/v1.0/me/todo/lists/${listId}/tasks" `
        -Body $body | Out-Null

    $sent[$probe['Id']] = $probe['Title']
    Write-Host "  $($probe['Id'])  $($probe['Title'])"
}

# ---------------------------------------------------------------------------
# Automatic finding: did Graph store the titles verbatim?
# ---------------------------------------------------------------------------

Write-Host ''
Write-Host '=== Automatic findings: Graph round-trip ===' -ForegroundColor Cyan
Write-Host 'Whether Graph normalises Unicode on write. No human eyes required.'
Write-Host ''

$returned = @{}
foreach ($task in Get-ListTask -ListId $listId) {
    $title = [string] $task['title']
    if ($title -match '^(P\d{2})\b' -or $title -match '\b(P\d{2})\b') {
        $returned[$Matches[1]] = $title
    }
}

$roundTripClean = $true
foreach ($probe in $probes) {
    $id = $probe['Id']
    if (-not $returned.ContainsKey($id)) {
        Write-Host "  ${id}: NOT READ BACK — investigate" -ForegroundColor Red
        $roundTripClean = $false
        continue
    }

    $before = [string] $sent[$id]
    $after = [string] $returned[$id]

    # Ordinal is the whole point. PowerShell's -ceq (and .NET's InvariantCulture) compare
    # by canonical equivalence, so they report NFC and NFD "ü" as equal — exactly the
    # difference this check exists to expose.
    if ([string]::Equals($before, $after, [StringComparison]::Ordinal)) {
        if ($probe.ContainsKey('Unicode')) {
            Write-Host "  ${id}: verbatim  $(Format-CodePoint -Text $after)" -ForegroundColor Green
        }
        continue
    }

    $roundTripClean = $false
    Write-Host "  ${id}: ALTERED BY GRAPH" -ForegroundColor Red
    Write-Host "      sent:     $(Format-CodePoint -Text $before)"
    Write-Host "      returned: $(Format-CodePoint -Text $after)"
}

Write-Host ''
if ($roundTripClean) {
    Write-Host 'Graph stored every title verbatim, NFD included. Normalisation is therefore TodoWerk''s job:' -ForegroundColor Yellow
    Write-Host 'two visually identical tags will arrive as two distinct byte sequences unless the extractor folds them.' -ForegroundColor Yellow
}
else {
    Write-Host 'Graph altered at least one title. Record exactly which, and how, in ADR-0005 — it changes what' -ForegroundColor Yellow
    Write-Host 'the extractor can assume about its input.' -ForegroundColor Yellow
}

# ---------------------------------------------------------------------------
# Manual findings: emit the template
# ---------------------------------------------------------------------------

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Hashtag grammar probe results')
$lines.Add('')
$lines.Add("Seeded into the To Do list ``${ListName}`` by ``scripts/Probe-HashtagGrammar.ps1``.")
$lines.Add('')
$lines.Add('Fill in one column per client. A divergence between two clients is itself a finding,')
$lines.Add('and the most important one the probe can produce: it means there is no single grammar')
$lines.Add('to copy, and TodoWerk must pick the intersection.')
$lines.Add('')
$lines.Add('| Client | Version | Checked on |')
$lines.Add('| --- | --- | --- |')
$lines.Add('| Windows app | | |')
$lines.Add('| Web (to-do.office.com) | | |')
$lines.Add('| iOS / Android | | |')
$lines.Add('| Outlook web — My Day | | |')
$lines.Add('')
$lines.Add('## Probes')
$lines.Add('')

foreach ($probe in $probes) {
    $lines.Add("### $($probe['Id']) — ``$($probe['Title'])``")
    $lines.Add('')
    if ($probe.ContainsKey('Notes')) {
        $lines.Add("Notes body: ``$($probe['Notes'])``")
        $lines.Add('')
    }
    $lines.Add("**Question:** $($probe['Question'])")
    $lines.Add('')
    $lines.Add("**Why it matters:** $($probe['Why'])")
    $lines.Add('')
    $lines.Add('**Observed:**')
    $lines.Add('')
    $lines.Add('- Windows app: ')
    $lines.Add('- Web: ')
    $lines.Add('- Mobile: ')
    $lines.Add('')
}

$lines.Add('## Anything the probes missed')
$lines.Add('')
$lines.Add('Behaviour noticed while clicking around that no probe asked about:')
$lines.Add('')
$lines.Add('- ')
$lines.Add('')

Set-Content -LiteralPath $ResultsPath -Value $lines -Encoding utf8NoBOM

Write-Host ''
Write-Host '=== Next ===' -ForegroundColor Cyan
Write-Host "1. Open '${ListName}' in at least two To Do clients."
Write-Host '2. Answer each probe in the template, noting the client alongside each answer.'
Write-Host "   Template: ${ResultsPath}"
Write-Host '3. Feed the answers into docs/adr/0005-what-a-hashtag-is.md, replacing the'
Write-Host '   provisional rules with observed ones and shrinking the uncertainty section.'
Write-Host "4. Tear the list down: ./Probe-HashtagGrammar.ps1 -Cleanup"
Write-Host ''

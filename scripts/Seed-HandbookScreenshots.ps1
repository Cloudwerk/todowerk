#Requires -Version 7.4
#Requires -Modules Microsoft.Graph.Authentication

<#
.SYNOPSIS
    Seeds two Microsoft To Do lists with screenshot fixtures for product documentation.

.DESCRIPTION
    Screenshots of the Workbench have to show the grammar rather than somebody's real tasks: a
    hashtag written two ways, two that are nearly the same, one worth renaming, several worth
    folding into one. This seeds exactly that into the To Do of the account it is run as - a Test
    Tenant user, never a real one - so the screenshots can be retaken whenever the Workbench moves.

    What it cannot stage is the "unused for a long time" flag. TodoWerk computes that from the
    lastModifiedDateTime Microsoft Graph sets on every write, which nobody can backdate, so a
    freshly seeded account cannot show it for 180 days. Documentation has to describe that flag in
    words.

    It writes only to the two lists it names, and never touches one that already holds tasks
    unless you pass -Force. A contributor who wants a populated Workbench against their own
    development tenant can use it for that too.

.PARAMETER Force
    Required to wipe the tasks from the two lists before reseeding them.

.PARAMETER Cleanup
    Delete both lists and everything in them, then exit.

.EXAMPLE
    ./Seed-HandbookScreenshots.ps1
    Creates the two lists if they are missing and seeds them.

.EXAMPLE
    ./Seed-HandbookScreenshots.ps1 -Force
    Wipes and reseeds lists that already hold tasks.

.EXAMPLE
    ./Seed-HandbookScreenshots.ps1 -Cleanup
    Removes both lists from the account.

.NOTES
    Requires the Tasks.ReadWrite delegated scope - the same one TodoWerk itself asks for. A
    sibling of Probe-HashtagGrammar.ps1, which seeds the probes ADR-0005 was settled from.
#>

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [switch] $Force,

    [switch] $Cleanup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# What the pictures have to be able to show
#
#   Written more than one way   #Work / #work, #Customer / #customer, #Admin / #admin
#   Similar to another          #Customer / #Customers, #Meeting / #Meetings
#   Worth renaming              #Prio1 -> #Priority1
#   Worth folding together      #Meeting + #Meetings, #Planning + #Offsite
#   Not a hashtag at all        "C#" mid-sentence, which the grammar refuses (ADR-0005)
#   Across how many lists       #Work, #Planning and #FollowUp span both lists
#
# Two lists rather than one, so the "lists" column in the inventory has something to count.
# ---------------------------------------------------------------------------

$lists = [ordered]@{
    'Projects' = @(
        'Draft the Q3 roadmap #Work #Planning'
        'Review the vendor contract #work #Legal'
        'Prepare the board slides #Work #Prio1'
        'Book a venue for the offsite #Planning #Offsite'
        'Send the offer to Contoso #Customer #Prio1'
        'Call Fabrikam about the renewal #Customers #Prio1'
        'Update the onboarding checklist #HR #Onboarding'
        'Fix the newsletter template #Marketing #Newsletter'
        'Renew the domain registrations #IT #Admin'
        'Write the release notes #Release #Work'
        'Learn C# pattern matching'
    )
    'Follow-ups' = @(
        'Chase invoice 4711 #Finance #FollowUp'
        'Reply to Northwind about the pilot #customer #FollowUp'
        'Order new badges #Office #admin'
        'Plan the team lunch #Team #Offsite'
        "Archive last year's projects #work #Archive"
        'Prepare the all-hands agenda #Meeting #Planning'
        'Minutes from the steering meeting #Meetings #FollowUp'
    )
}

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Get-TodoList {
    <#
        .SYNOPSIS
            Returns the list with this display name, or $null. Pages through every list rather
            than trusting $filter, which To Do list queries support inconsistently.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $DisplayName)

    $uri = 'https://graph.microsoft.com/v1.0/me/todo/lists'
    while ($uri) {
        $page = Invoke-MgGraphRequest -Method GET -Uri $uri
        foreach ($list in @($page['value'])) {
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
    foreach ($name in $lists.Keys) {
        $existing = Get-TodoList -DisplayName $name
        if ($null -eq $existing) {
            Write-Host "No list named '${name}' - nothing to clean up." -ForegroundColor Yellow
            continue
        }

        $count = @(Get-ListTask -ListId $existing['id']).Count
        if ($PSCmdlet.ShouldProcess("list '${name}' and its ${count} task(s)", 'Delete')) {
            Invoke-MgGraphRequest -Method DELETE -Uri "https://graph.microsoft.com/v1.0/me/todo/lists/$($existing['id'])" | Out-Null
            Write-Host "Deleted '${name}' and its ${count} task(s)." -ForegroundColor Green
        }
    }
    return
}

# ---------------------------------------------------------------------------
# Seed
# ---------------------------------------------------------------------------

$total = 0

foreach ($name in $lists.Keys) {
    $list = Get-TodoList -DisplayName $name
    if ($null -eq $list) {
        Write-Host "Creating list '${name}'..." -ForegroundColor Cyan
        $list = Invoke-MgGraphRequest -Method POST `
            -Uri 'https://graph.microsoft.com/v1.0/me/todo/lists' `
            -Body @{ displayName = $name }
    }
    else {
        $stale = @(Get-ListTask -ListId $list['id'])
        if ($stale.Count -gt 0) {
            if (-not $Force) {
                throw "List '${name}' already holds $($stale.Count) task(s). Re-run with -Force to wipe and reseed it, or with -Cleanup to remove both lists."
            }
            if ($PSCmdlet.ShouldProcess("$($stale.Count) task(s) in '${name}'", 'Delete')) {
                foreach ($task in $stale) {
                    Invoke-MgGraphRequest -Method DELETE `
                        -Uri "https://graph.microsoft.com/v1.0/me/todo/lists/$($list['id'])/tasks/$($task['id'])" | Out-Null
                }
                Write-Host "Wiped $($stale.Count) stale task(s) from '${name}'." -ForegroundColor Yellow
            }
        }
    }

    $listId = $list['id']
    Write-Host ''
    Write-Host "Seeding '${name}'..." -ForegroundColor Cyan

    foreach ($title in $lists[$name]) {
        Invoke-MgGraphRequest -Method POST `
            -Uri "https://graph.microsoft.com/v1.0/me/todo/lists/${listId}/tasks" `
            -Body @{ title = $title } | Out-Null
        $total++
        Write-Host "  $title"
    }
}

Write-Host ''
Write-Host "Seeded ${total} tasks across $($lists.Count) lists." -ForegroundColor Green
Write-Host 'Sign in to TodoWerk as this account, let the first scan finish, and the Workbench will show:'
Write-Host '  written more than one way   #Work, #Customer, #Admin'
Write-Host '  similar to another          #Customer / #Customers, #Meeting / #Meetings'
Write-Host '  a rename worth previewing   #Prio1 -> #Priority1'
Write-Host '  a merge worth previewing    #Meeting + #Meetings'
Write-Host '  unused for a long time      nothing - Graph sets the dates, and 180 days have to pass'

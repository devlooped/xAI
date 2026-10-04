$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'upstream-update.ps1') -Mode Library -Repository 'devlooped/xAI'
$script:passed = 0
$script:temporary = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'upstream-update-tests-' + [Guid]::NewGuid())
[IO.Directory]::CreateDirectory($script:temporary) | Out-Null

function Assert($Condition, [string] $Message) {
    if (!$Condition) { throw $Message }
}

function Assert-Throws([scriptblock] $Action, [string] $Pattern) {
    $caught = $null
    try { & $Action }
    catch { $caught = $_ }
    Assert ($null -ne $caught) 'Expected an error.'
    Assert ($caught.Exception.Message -match $Pattern) "Unexpected error: $caught"
}

function Copy-Value($Value) {
    return ConvertFrom-Json ($Value | ConvertTo-Json -Depth 40) -AsHashtable -Depth 50
}

function Seal($Value) {
    $copy = [ordered]@{}
    foreach ($key in $Value.Keys | Where-Object { $_ -ne 'fingerprint' }) { $copy[$key] = $Value[$key] }
    $Value.fingerprint = Get-Hash ($copy | ConvertTo-Json -Depth 40 -Compress)
    return $Value
}

function New-Fixture {
    $manifest = Seal ([ordered]@{
        schema = 1; request_id = 'fixture-1'; repository = 'devlooped/xAI'
        base_sha = 'a' * 40; run_url = 'https://github.com/devlooped/xAI/actions/runs/1'
        revisions = @(@{ repository = 'xai-org/xai-proto'; ref = 'main'; sha = 'c' * 40 })
        sync_files = @(); diagnostics = @(); documents = @(); packages = @(); previous_cycles = @(); errors = @()
        candidates = @(
            @{ id = 'protocol:capabilities'; source = 'proto' },
            @{ id = 'http:capabilities'; source = 'docs' },
            @{ id = 'meai:capabilities'; source = 'MEAI APIs' }
        )
    })
    $state = @{ schema = 1; status = 'running'; evidence_id = 0; queued_id = 0; pr_number = 42 }
    $issue = @{
        number = 41; body = (Format-Block $script:evidenceHeading $manifest) + "`n`n" + (Format-Block $script:stateHeading $state)
        user = @{ login = 'maintainer'; type = 'User' }; state = 'open'
        assignees = @(@{ login = 'copilot-swe-agent[bot]' }); labels = @(@{ name = 'upstream-update' })
    }
    $pr = @{
        number = 42; title = 'Work in progress'; body = "Cycle: #41`n`nMaintainer notes: keep this."
        head = @{ sha = 'b' * 40; ref = 'copilot/update-xai'; repo = @{ full_name = 'devlooped/xAI' } }
        base = @{ ref = 'main' }; user = @{ login = 'copilot-swe-agent[bot]'; type = 'Bot' }
        draft = $true; state = 'open'; merged = $false
        labels = @(@{ name = 'dependencies' }, @{ name = 'docs' }, @{ name = 'custom-human-label' })
    }
    $files = @(@{ filename = 'src/xAI/GrokProtocolExtensions.cs' })
    $report = @{
        schema = 1; issue_number = 41; request_id = 'fixture-1'; fingerprint = $manifest.fingerprint
        status = 'complete'; pr_number = 42; head_sha = 'b' * 40; title = 'Add native MEAI mappings for xAI capabilities'
        files = @('src/xAI/GrokProtocolExtensions.cs')
        decisions = @($manifest.candidates | ForEach-Object {
            @{ id = $_.id; disposition = 'already-covered'; reason = 'Verified existing mapping.'; evidence = @('src/xAI/GrokProtocolExtensions.cs:1') }
        })
        enhancements = @('Map the native MEAI capability'); bug_fixes = @(); protocol_updates = @('Pinned xAI revision')
        compatibility = 'Existing public APIs and targets preserved.'; blockers = @()
        validation = @{ build = 'passed'; tests = 'passed'; whitespace = 'passed'; style = 'passed'; live_tests = 'Skipped: no API credentials' }
    }
    return @{ manifest = $manifest; state = $state; issue = $issue; pr = $pr; files = $files; report = $report }
}

function Set-FixtureState {
    $script:fixture.issue.body = (Format-Block $script:evidenceHeading $script:fixture.manifest) +
        "`n`n" + (Format-Block $script:stateHeading $script:fixture.state)
}

function Invoke-GitHub($Path, $Method = 'GET', $Body = $null, [switch] $Paginate) {
    $script:calls.Add(@{ path = $Path; method = $Method; body = $Body }) | Out-Null
    return & $script:api $Path $Method $Body
}

function Reset-Fakes {
    $script:Resume = $false
    $script:fixture = New-Fixture
    $script:calls = [Collections.Generic.List[object]]::new()
    $script:comments = [Collections.Generic.List[object]]::new()
    $script:prComments = [Collections.Generic.List[object]]::new()
    $script:activeIssues = @($script:fixture.issue)
    $script:api = {
        param($Path, $Method, $Body)
        if ($Path -eq 'user') { return @{ login = 'maintainer' } }
        if ($Path -eq 'graphql') {
            return @{ data = @{ repository = @{ suggestedActors = @{ nodes = @(@{ login = 'copilot-swe-agent' }) } } } }
        }
        if ($Path -match '/collaborators/(maintainer|reviewer)/permission$') { return @{ permission = 'write' } }
        if ($Path -match '/collaborators/.+/permission$') { return @{ permission = 'read' } }
        if ($Path -match '/labels\?') {
            return @('upstream-update', 'upstream-update-blocked', 'enhancement', 'bug', 'dependencies') |
                ForEach-Object { @{ name = $_ } }
        }
        if ($Path -match '/issues\?') { return $script:activeIssues }
        if ($Path -eq 'repos/devlooped/xAI/issues' -and $Method -eq 'POST') {
            $script:fixture.issue.body = $Body.body
            $script:fixture.issue.assignees = @()
            $script:activeIssues = @($script:fixture.issue)
            return $script:fixture.issue
        }
        if ($Path -match '/issues/41/assignees$') {
            $script:fixture.issue.assignees = @(@{ login = 'copilot-swe-agent[bot]' })
            return $script:fixture.issue
        }
        if ($Path -match '/issues/41/comments') {
            if ($Method -eq 'GET') { return @($script:comments) }
            $comment = @{ id = 100 + $script:comments.Count; body = $Body.body; user = @{ login = 'maintainer' } }
            $script:comments.Add($comment)
            return $comment
        }
        if ($Path -match '/issues/42/comments') {
            if ($Method -eq 'GET') { return @($script:prComments) }
            $comment = @{ id = 200 + $script:prComments.Count; body = $Body.body; user = @{ login = 'maintainer' } }
            $script:prComments.Add($comment)
            return $comment
        }
        if ($Path -match '/issues/comments/(\d+)$') {
            $matches = @($script:comments | Where-Object id -eq ([int] $Matches[1]))
            if ($matches.Count) { return $matches[0] }
            throw 'Unknown evidence comment.'
        }
        if ($Path -match '/issues/41$') {
            if ($Method -eq 'PATCH') {
                $script:fixture.issue.state = $Body.state
                if ($Body.state -eq 'closed') { $script:activeIssues = @() }
            }
            return $script:fixture.issue
        }
        if ($Path -match '/pulls/42/files') { return $script:fixture.files }
        if ($Path -match '/pulls/42$') {
            if ($Method -eq 'PATCH') {
                foreach ($key in $Body.Keys) { $script:fixture.pr[$key] = $Body[$key] }
            }
            return $script:fixture.pr
        }
        if ($Path -match '/pulls\?') { return @($script:fixture.pr) }
        if ($Path -match '/labels(?:/|$)') { return @{ } }
        throw "Unexpected API call: $Method $Path"
    }
}

function Test-Case([string] $Name, [scriptblock] $Action) {
    Reset-Fakes
    try {
        & $Action
        $script:passed++
        Write-Host "PASS $Name"
    }
    catch { throw "FAIL ${Name}: $($_.Exception.Message)`n$($_.ScriptStackTrace)" }
}

function Invoke-Result {
    $event = @{ issue = $script:fixture.issue; comment = @{
        user = @{ login = 'copilot-swe-agent[bot]'; type = 'Bot' }
        body = Format-Block $script:resultHeading $script:fixture.report
    } }
    $script:EventPath = Join-Path $script:temporary 'event.json'
    [IO.File]::WriteAllText($script:EventPath, ($event | ConvertTo-Json -Depth 40))
    Finalize-Cycle
}

function Write-Manifest {
    $script:ManifestPath = Join-Path $script:temporary 'manifest.json'
    [IO.File]::WriteAllText($script:ManifestPath, ($script:fixture.manifest | ConvertTo-Json -Depth 40))
}

function Set-NewCycleAPI {
    $defaultAPI = $script:api
    $nextCycle = @{ issue = $null }
    $script:api = {
        param($Path, $Method, $Body)
        if ($Path -eq 'repos/devlooped/xAI/issues' -and $Method -eq 'POST') {
            $nextCycle.issue = @{ number = 43; body = $Body.body; state = 'open'
                user = @{ login = 'maintainer'; type = 'User' }; assignees = @() }
            return $nextCycle.issue
        }
        if ($Path -eq 'repos/devlooped/xAI/issues/43/assignees') {
            $nextCycle.issue.assignees = @(@{ login = 'copilot-swe-agent[bot]' })
            return $nextCycle.issue
        }
        if ($Path -eq 'repos/devlooped/xAI/issues/43/comments') { return @{ id = 300 } }
        return & $defaultAPI $Path $Method $Body
    }.GetNewClosure()
    return $nextCycle
}

$previousToken = $env:GH_TOKEN
$env:GH_TOKEN = 'fixture-not-a-credential'
try {
    Test-Case 'manifest fingerprint round-trip and tampering' {
        $manifest = Copy-Value $script:fixture.manifest
        Assert-Manifest $manifest
        $manifest.documents += @{ url = 'https://docs.x.ai/developers/new.md'; hash = 'changed' }
        Assert-Throws { Assert-Manifest $manifest } 'fingerprint'
    }
    Test-Case 'source outage cannot pass dispatch validation' {
        $script:fixture.manifest.errors = @('HTTP documentation unavailable')
        Assert-Throws { Assert-Manifest $script:fixture.manifest } 'incomplete'
        Assert ($script:calls.Count -eq 0) 'A failed source must not dispatch.'
    }
    Test-Case 'new import diagnostics need an integration, not a no-op' {
        $f = $script:fixture
        $f.manifest.diagnostics = @('New imported protocol dependency')
        $f.manifest = Seal $f.manifest; $f.report.fingerprint = $f.manifest.fingerprint
        Assert-Manifest $f.manifest
        $f.report.status = 'no-op'; $f.report.files = @(); $f.report.pr_number = 0; $f.report.head_sha = ''
        $f.report.enhancements = @()
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $null @() } 'Substantive'
    }
    Test-Case 'weekly no-op still assesses all three surfaces' {
        $f = $script:fixture
        $f.report.status = 'no-op'; $f.report.files = @(); $f.report.pr_number = 0; $f.report.head_sha = ''
        $f.report.enhancements = @()
        Assert-Report $f.report $f.manifest $f.issue $null @()
        $f.report.decisions = @($f.report.decisions | Select-Object -First 2)
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $null @() } 'Every candidate'
    }
    Test-Case 'HTTP-only integration is independent of proto changes' {
        $f = $script:fixture
        $f.manifest.documents = @(@{ url = 'https://docs.x.ai/developers/new.md'; hash = 'changed' })
        $f.manifest = Seal $f.manifest; $f.report.fingerprint = $f.manifest.fingerprint
        $f.report.decisions[1].disposition = 'integrated'
        Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files
    }
    Test-Case 'MEAI-only opportunities and duplicate decisions' {
        $f = $script:fixture; $f.report.decisions[2].disposition = 'integrated'
        Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files
        $f.report.decisions += $f.report.decisions[2]
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'Every candidate'
    }
    Test-Case 'protocol-only is valid, but actual sync is not a no-op' {
        $f = $script:fixture; $f.report.decisions[0].disposition = 'protocol-only'
        Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files
        $f.manifest.sync_files = @(@{ path = 'src/xAI.Protocol/new.proto'; before = $null; after = 'new' })
        $f.report.status = 'no-op'; $f.report.files = @(); $f.report.pr_number = 0; $f.report.head_sha = ''
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $null @() } 'Substantive'
    }
    Test-Case 'complete integration cannot omit the mechanical sync' {
        $f = $script:fixture
        $f.manifest.sync_files = @(@{ path = 'src/xAI.Protocol/chat.proto'; before = 'old'; after = 'new' })
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'synchronization is missing'
    }
    Test-Case 'breaking/unresolved work cannot claim completion' {
        $f = $script:fixture; $f.report.decisions[0].disposition = 'blocked'; $f.report.blockers = @('Requires a breaking API redesign')
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'Unresolved'
        $f.report.status = 'blocked'
        Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files
    }
    Test-Case 'validation skips, stale heads and incorrect file inventories fail' {
        $f = $script:fixture; $f.report.validation.tests = 'skipped'
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'validation'
        $f.report.validation.tests = 'passed'; $f.report.head_sha = 'old'
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'stale'
        $f.report.head_sha = $f.pr.head.sha; $f.report.files = @()
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'actual PR diff'
    }
    Test-Case 'report arrays and citations are required' {
        $f = $script:fixture; $f.report.enhancements = 'not an array'
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'must be an array'
        $f.report.enhancements = @(); $f.report.decisions[0].evidence = @()
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'Missing evidence'
    }
    Test-Case 'release-note entries must contain meaningful text' {
        $f = $script:fixture; $f.report.enhancements = @(' ')
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'item in enhancements'
        $f.report.enhancements = @(@{ unsupported = 'object' })
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'item in enhancements'
    }
    Test-Case 'blocked reports cannot use blank explanations' {
        $f = $script:fixture; $f.report.status = 'blocked'; $f.report.blockers = @('')
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'item in blockers'
    }
    Test-Case 'foreign branch and generic bump titles are rejected' {
        $f = $script:fixture; $f.pr.head.repo.full_name = 'external/fork'
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'agent-owned'
        $f.pr.head.repo.full_name = 'devlooped/xAI'; $f.report.title = 'Bump files with dotnet-file sync'
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'outcome-based'
    }
    Test-Case 'summary updates preserve human text and literal shell input' {
        $body = "Human notes`n" + (Update-Summary '' 'first') + "`nKeep this too"
        $updated = Update-Summary $body 'Literal $(do-not-execute) $1'
        Assert ($updated.Contains('Human notes') -and $updated.Contains('Keep this too')) 'Lost human notes.'
        Assert ($updated.Contains('Literal $(do-not-execute) $1') -and !$updated.Contains('first')) 'Summary was interpreted or duplicated.'
    }
    Test-Case 'completed feature PR is draft and eligible for release notes' {
        Invoke-Result
        $patch = @($script:calls | Where-Object { $_.path -match '/issues/42/labels$' })[-1].body.labels
        Assert ('enhancement' -in $patch -and 'dependencies' -notin $patch -and 'docs' -notin $patch) 'Feature is excluded from release notes.'
        Assert ('custom-human-label' -in $patch) 'Lost an unrelated label.'
        Assert ($script:fixture.pr.draft -and $script:fixture.pr.body.Contains('Maintainer notes')) 'Changed human review controls or notes.'
        Assert ($script:fixture.pr.body.Contains('### Release notes')) 'No release-note section.'
    }
    Test-Case 'pure maintenance remains dependency categorized' {
        $script:fixture.report.enhancements = @()
        Invoke-Result
        $labels = @($script:calls | Where-Object { $_.path -match '/issues/42/labels$' })[-1].body.labels
        Assert ('dependencies' -in $labels -and 'enhancement' -notin $labels) 'Manufactured a feature.'
    }
    Test-Case 'a human exclusion label cannot silently hide a feature release note' {
        $script:fixture.pr.labels += @{ name = 'wontfix' }
        Assert-Throws { Invoke-Result } 'human release-exclusion label'
        Assert (@($script:calls | Where-Object method -ne 'GET').Count -eq 0) 'Overrode human categorization.'
    }
    Test-Case 'forged completion cannot mutate metadata' {
        $script:EventPath = Join-Path $script:temporary 'event.json'
        $event = @{ issue = $script:fixture.issue; comment = @{
            body = Format-Block $script:resultHeading $script:fixture.report
            user = @{ login = 'outsider'; type = 'User' }
        } }
        [IO.File]::WriteAllText($script:EventPath, ($event | ConvertTo-Json -Depth 40))
        Finalize-Cycle
        Assert (@($script:calls | Where-Object method -ne 'GET').Count -eq 0) 'Untrusted report caused a mutation.'
    }
    Test-Case 'report recovery rejects a claimed bot identity without bot metadata' {
        $script:comments.Add(@{ id = 8; user = @{ login = 'copilot-swe-agent[bot]'; type = 'User' }
            body = Format-Block $script:resultHeading $script:fixture.report })
        Assert (@(Get-TrustedReports 41).Count -eq 0) 'Recovered a forged bot report.'
        Assert (@($script:calls | Where-Object method -ne 'GET').Count -eq 0) 'Untrusted recovery caused a mutation.'
    }
    Test-Case 'manual reconciliation accepts reports from other repository writers' {
        $script:comments.Add(@{ id = 8; user = @{ login = 'reviewer'; type = 'User' }
            body = Format-Block $script:resultHeading $script:fixture.report })
        $script:EventPath = Join-Path $script:temporary 'event.json'
        [IO.File]::WriteAllText($script:EventPath, '{}')
        $previousIssue = $env:CYCLE_ISSUE
        try {
            $env:CYCLE_ISSUE = '41'
            Finalize-Cycle
        }
        finally { $env:CYCLE_ISSUE = $previousIssue }
        Assert ((Get-State $script:fixture.issue @($script:comments)).status -eq 'awaiting-review') 'Ignored a trusted maintainer report.'
    }
    Test-Case 'stale completion has no success-shaped mutations' {
        $script:fixture.report.head_sha = 'old'
        Assert-Throws { Invoke-Result } 'stale'
        Assert (@($script:calls | Where-Object method -ne 'GET').Count -eq 0) 'Stale result changed the PR.'
    }
    Test-Case 'protocol presence alone cannot finalize incorrect committed bytes' {
        $f = $script:fixture
        $f.manifest.sync_files = @(@{ path = 'src/xAI.Protocol/chat.proto'; before = 'old'; after = Get-Hash 'expected bytes' })
        $f.manifest = Seal $f.manifest; $f.report.fingerprint = $f.manifest.fingerprint; Set-FixtureState
        $f.files += @{ filename = 'src/xAI.Protocol/chat.proto'; status = 'modified' }
        $f.report.files += 'src/xAI.Protocol/chat.proto'
        $defaultAPI = $script:api
        $script:api = {
            param($Path, $Method, $Body)
            if ($Path -match '/contents/') { return @{ content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('incorrect bytes')) } }
            return & $defaultAPI $Path $Method $Body
        }.GetNewClosure()
        Assert-Throws { Invoke-Result } 'Committed protocol bytes'
        Assert (@($script:calls | Where-Object method -ne 'GET').Count -eq 0) 'Incorrect proto content changed the PR metadata.'
    }
    Test-Case 'confirmed no-op closes only an empty owned draft and its issue' {
        $script:fixture.report.status = 'no-op'; $script:fixture.report.files = @(); $script:fixture.files = @()
        $script:fixture.report.enhancements = @()
        Invoke-Result
        Assert ($script:fixture.pr.state -eq 'closed' -and $script:fixture.issue.state -eq 'closed') 'No-op left a final PR.'
    }
    Test-Case 'a discovered no-op PR retains its cycle link after the closed event' {
        $script:fixture.state.pr_number = 0; Set-FixtureState
        $script:fixture.report.status = 'no-op'; $script:fixture.report.files = @(); $script:fixture.files = @()
        $script:fixture.report.enhancements = @()
        Invoke-Result
        $state = Get-State $script:fixture.issue @($script:comments)
        Assert ($state.status -eq 'no-op' -and $state.pr_number -eq 42) 'No-op did not record the discovered PR.'
        $script:calls.Clear()
        [IO.File]::WriteAllText($script:EventPath, (@{ pull_request = $script:fixture.pr } | ConvertTo-Json -Depth 40))
        Finalize-Cycle
        Assert ((Get-State $script:fixture.issue @($script:comments)).status -eq 'no-op') 'No-op was overwritten as closed-unmerged.'
        Assert (@($script:calls | Where-Object method -ne 'GET').Count -eq 0) 'Replayed no-op closure mutated a completed cycle.'
    }
    Test-Case 'no-op PR closure recovers an interrupted cycle issue closure' {
        $script:fixture.report.status = 'no-op'; $script:fixture.report.files = @(); $script:fixture.files = @()
        $script:fixture.report.enhancements = @()
        Invoke-Result
        $script:fixture.issue.state = 'open'
        [IO.File]::WriteAllText($script:EventPath, (@{ pull_request = $script:fixture.pr } | ConvertTo-Json -Depth 40))
        Finalize-Cycle
        Assert ($script:fixture.issue.state -eq 'closed') 'Interrupted no-op closure left the cycle open.'
        Assert ((Get-State $script:fixture.issue @($script:comments)).status -eq 'no-op') 'Lost the audited no-op disposition.'
    }
    Test-Case 'manual recovery finishes a no-op when issue closure fails after closing its PR' {
        $script:fixture.state.pr_number = 0; Set-FixtureState
        $script:fixture.report.status = 'no-op'; $script:fixture.report.files = @(); $script:fixture.files = @()
        $script:fixture.report.enhancements = @()
        $defaultAPI = $script:api
        $script:api = {
            param($Path, $Method, $Body)
            if ($Path -eq 'repos/devlooped/xAI/issues/41' -and $Method -eq 'PATCH') { throw 'Fixture issue closure failed.' }
            return & $defaultAPI $Path $Method $Body
        }.GetNewClosure()
        Assert-Throws { Invoke-Result } 'Fixture issue closure failed'
        Assert ($script:fixture.pr.state -eq 'closed' -and $script:fixture.issue.state -eq 'open') 'Did not exercise an interrupted closure.'
        $state = Get-State $script:fixture.issue @($script:comments)
        Assert ($state.status -eq 'no-op' -and $state.pr_number -eq 42) 'Closure was not checkpointed before the PR mutation.'
        $script:api = $defaultAPI
        Invoke-Result
        Assert ($script:fixture.issue.state -eq 'closed') 'Retry could not finish the closed-PR no-op.'
    }
    Test-Case 'no-op retry cannot close a draft that acquired substantive edits' {
        $script:fixture.state.status = 'no-op'; Set-FixtureState
        Assert-Throws { Complete-NoOp $script:fixture.issue $script:fixture.state } 'empty automation-owned draft'
        Assert ($script:fixture.pr.state -eq 'open' -and $script:fixture.issue.state -eq 'open') 'No-op retry lost substantive work.'
        Assert (@($script:calls | Where-Object method -ne 'GET').Count -eq 0) 'Invalid no-op retry mutated its cycle.'
    }
    Test-Case 'scheduled dispatch recovers a checkpointed no-op without its closure event' {
        $script:fixture.state.status = 'no-op'; Set-FixtureState
        $script:fixture.pr.state = 'closed'; $script:fixture.files = @()
        Write-Manifest
        Dispatch-Evidence
        Assert ($script:fixture.issue.state -eq 'closed') 'Checkpointed no-op left an active cycle.'
        Assert ($script:prComments.Count -eq 0) 'No-op closure recovery requested another agent session.'
    }
    Test-Case 'a completed no-op drains queued evidence into one new cycle' {
        $script:fixture.report.status = 'no-op'; $script:fixture.report.files = @(); $script:fixture.files = @()
        $script:fixture.report.enhancements = @()
        $next = Copy-Value $script:fixture.manifest; $next.request_id = 'fixture-2'; $next = Seal $next
        $script:comments.Add(@{ id = 7; user = @{ login = 'maintainer' }; body = Format-Block $script:evidenceHeading $next })
        $script:fixture.state.queued_id = 7; Set-FixtureState
        $nextCycle = Set-NewCycleAPI
        Invoke-Result
        Assert ($script:fixture.issue.state -eq 'closed' -and $script:fixture.pr.state -eq 'closed') 'No-op left its old cycle active.'
        Assert (@($script:calls | Where-Object { $_.path -eq 'repos/devlooped/xAI/issues' -and $_.method -eq 'POST' }).Count -eq 1) 'Did not open exactly one queued cycle.'
        Assert ((Read-Block $nextCycle.issue.body $script:evidenceHeading).request_id -eq 'fixture-2') 'Lost queued evidence in the new cycle.'
        Assert ($script:prComments.Count -eq 0) 'Continued a closed no-op draft.'
    }
    Test-Case 'non-draft cannot be closed as a no-op' {
        $script:fixture.report.status = 'no-op'; $script:fixture.report.files = @(); $script:fixture.files = @()
        $script:fixture.report.enhancements = @()
        $script:fixture.pr.draft = $false
        Assert-Throws { Invoke-Result } 'empty automation-owned draft'
        Assert ($script:fixture.pr.state -eq 'open') 'Closed a human-managed PR.'
    }
    Test-Case 'blocked completion stays open and visibly blocked' {
        $script:fixture.report.status = 'blocked'; $script:fixture.report.blockers = @('Contract unclear')
        Assert-Throws { Invoke-Result } 'assessment is blocked'
        Assert ($script:fixture.issue.state -eq 'open') 'Closed incomplete work.'
        Assert (@($script:calls | Where-Object { $_.path -match '/issues/41/labels$' }).Count -eq 1) 'Missing blocked label.'
    }
    Test-Case 'queued weekly evidence continues the same PR after completion' {
        $next = Copy-Value $script:fixture.manifest; $next.request_id = 'fixture-2'; $next = Seal $next
        $script:comments.Add(@{ id = 7; user = @{ login = 'maintainer' }; body = Format-Block $script:evidenceHeading $next })
        $script:fixture.state.queued_id = 7; Set-FixtureState
        Invoke-Result
        Assert ($script:prComments.Count -eq 1 -and $script:prComments[0].body.Contains('@copilot')) 'Did not continue the same PR.'
        Assert (@($script:calls | Where-Object { $_.path -eq 'repos/devlooped/xAI/issues' }).Count -eq 0) 'Opened another cycle.'
        $state = Get-State $script:fixture.issue @($script:comments)
        Assert ($state.status -eq 'running' -and $state.evidence_id -eq 7 -and !$state.queued_id) 'Lost queued evidence.'
    }
    Test-Case 'continuation retry does not duplicate agent requests' {
        Start-Continuation $script:fixture.issue $script:fixture.state $script:fixture.pr
        Start-Continuation $script:fixture.issue $script:fixture.state $script:fixture.pr
        Assert ($script:prComments.Count -eq 1) 'Duplicate paid session request.'
    }
    Test-Case 'request identity cannot collide with a longer attempt number' {
        $script:prComments.Add(@{ id = 201; user = @{ login = 'maintainer' }; body = "Request: fixture-10`n" })
        Start-Continuation $script:fixture.issue $script:fixture.state $script:fixture.pr
        Assert ($script:prComments.Count -eq 2) 'Matched a different request by prefix.'
    }
    Test-Case 'dispatch creates one issue and verifies assignment' {
        $script:activeIssues = @(); Write-Manifest
        Dispatch-Evidence
        $assignment = @($script:calls | Where-Object { $_.path -match '/assignees$' })[-1].body
        Assert ($assignment.agent_assignment.custom_agent -eq 'upstream-update' -and $assignment.agent_assignment.base_branch -eq 'main') 'Wrong agent/base.'
        Assert (@($script:calls | Where-Object { $_.path -eq 'repos/devlooped/xAI/issues' }).Count -eq 1) 'Did not create one cycle.'
    }
    Test-Case 'partial assignment retry reuses the assigned issue' {
        $script:fixture.state.status = 'pending-assignment'; Set-FixtureState; Write-Manifest
        Dispatch-Evidence
        Assert (@($script:calls | Where-Object { $_.path -match '/assignees$' -or $_.path -eq 'repos/devlooped/xAI/issues' }).Count -eq 0) 'Reassigned/opened a duplicate task.'
    }
    Test-Case 'an accepted API request without actual assignment is not success' {
        $script:fixture.state.status = 'pending-assignment'; $script:fixture.issue.assignees = @()
        Set-FixtureState; Write-Manifest
        $defaultAPI = $script:api
        $script:api = {
            param($Path, $Method, $Body)
            if ($Path -match '/assignees$') { return @{ assignees = @() } }
            return & $defaultAPI $Path $Method $Body
        }.GetNewClosure()
        Assert-Throws { Dispatch-Evidence } 'did not assign Copilot'
        Assert ($script:comments.Count -eq 0) 'Recorded success without assignment.'
    }
    Test-Case 'in-flight weekly dispatch queues without a new session' {
        $script:fixture.manifest.request_id = 'fixture-2'; $script:fixture.manifest = Seal $script:fixture.manifest
        Write-Manifest
        Dispatch-Evidence
        Assert ($script:prComments.Count -eq 0) 'Started a concurrent agent session.'
        $state = Get-State $script:fixture.issue @($script:comments)
        Assert ($state.queued_id -gt 0 -and $state.status -eq 'running') 'Lost in-flight evidence.'
    }
    Test-Case 'explicit resume can recover a running state without a completion report' {
        $script:Resume = $true; Write-Manifest
        Dispatch-Evidence
        Assert ($script:prComments.Count -eq 1) 'Interrupted running state could not resume.'
    }
    Test-Case 'scheduled dispatch recovers a completion event lost to concurrency' {
        $script:comments.Add(@{ id = 8; user = @{ login = 'copilot-swe-agent[bot]'; type = 'Bot' }
            body = Format-Block $script:resultHeading $script:fixture.report })
        $script:fixture.manifest = Copy-Value $script:fixture.manifest
        $script:fixture.manifest.request_id = 'fixture-2'; $script:fixture.manifest = Seal $script:fixture.manifest
        Write-Manifest
        Dispatch-Evidence
        Assert ($script:prComments.Count -eq 1) 'Stored completion was not reconciled and continued.'
    }
    Test-Case 'scheduled recovery accepts another maintainer report and ignores later untrusted reports' {
        $script:comments.Add(@{ id = 8; user = @{ login = 'reviewer'; type = 'User' }
            body = Format-Block $script:resultHeading $script:fixture.report })
        $script:comments.Add(@{ id = 9; user = @{ login = 'outsider'; type = 'User' }
            body = Format-Block $script:resultHeading $script:fixture.report })
        $script:fixture.manifest = Copy-Value $script:fixture.manifest
        $script:fixture.manifest.request_id = 'fixture-2'; $script:fixture.manifest = Seal $script:fixture.manifest
        Write-Manifest
        Dispatch-Evidence
        Assert ($script:prComments.Count -eq 1) 'Trusted maintainer completion was not recovered and continued.'
    }
    Test-Case 'closed-unmerged is not the applied baseline' {
        $script:fixture.pr.state = 'closed'
        $script:EventPath = Join-Path $script:temporary 'event.json'
        [IO.File]::WriteAllText($script:EventPath, (@{ pull_request = $script:fixture.pr } | ConvertTo-Json -Depth 40))
        Finalize-Cycle
        $state = Get-State $script:fixture.issue @($script:comments)
        Assert ($state.status -eq 'closed-unmerged') 'Treated rejection as accepted.'
    }
    Test-Case 'scheduled dispatch recovers a lost merge event and assesses fresh evidence' {
        $script:fixture.state.status = 'awaiting-review'; Set-FixtureState
        $script:fixture.pr.state = 'closed'; $script:fixture.pr.merged = $true
        $script:fixture.manifest = Copy-Value $script:fixture.manifest
        $script:fixture.manifest.request_id = 'fixture-2'; $script:fixture.manifest = Seal $script:fixture.manifest
        Write-Manifest
        $nextCycle = Set-NewCycleAPI
        Dispatch-Evidence
        Assert ((Get-State $script:fixture.issue @($script:comments)).status -eq 'merged') 'Lost merge was not reconciled.'
        Assert ($script:fixture.issue.state -eq 'closed') 'Accepted cycle remained active.'
        Assert ((Read-Block $nextCycle.issue.body $script:evidenceHeading).request_id -eq 'fixture-2') 'New assessment did not use fresh main evidence.'
        Assert ($script:prComments.Count -eq 0) 'Requested continuation on a merged PR.'
    }
    Test-Case 'scheduled dispatch discovers a closed PR before its number was recorded' {
        $script:fixture.state.pr_number = 0; Set-FixtureState
        $script:fixture.pr.state = 'closed'
        Write-Manifest
        $nextCycle = Set-NewCycleAPI
        Dispatch-Evidence
        $state = Get-State $script:fixture.issue @($script:comments)
        Assert ($state.status -eq 'closed-unmerged' -and $state.pr_number -eq 42) 'Closed PR was not discovered or rejection was treated as acceptance.'
        Assert ($nextCycle.issue.number -eq 43 -and $script:fixture.issue.state -eq 'closed') 'Closed PR prevented the next cycle.'
        Assert ($script:prComments.Count -eq 0) 'Requested continuation on a closed PR.'
    }
    Test-Case 'owned config stays skipped through pinned projection round-trip' {
        $config = [IO.File]::ReadAllText((Join-Path $Root '.netconfig'))
        $entries = @(Get-Entries $config)
        foreach ($path in @('.github/workflows/dotnet-file.yml', '.github/dependabot.yml', '.github/release.yml',
            '.github/workflows/changelog.config', '.github/workflows/build.yml')) {
            $entry = @($entries | Where-Object path -eq $path)
            Assert ($entry.Count -eq 1 -and $entry[0].skip) "Unprotected local policy: $path"
        }
        $revisions = @(@{ repository = 'xai-org/xai-proto'; ref = 'main'; sha = 'c' * 40 })
        $projected = Set-Projection $config $revisions
        Assert ($projected.Contains('/tree/' + ('c' * 40) + '/')) 'Directory source was not pinned.'
        Assert ((Set-Projection $projected $revisions -Restore) -eq $config) 'Canonical URLs were not restored.'
    }
    Test-Case 'path traversal and untrusted documentation are rejected' {
        Assert-Throws { Get-RelativeFile $Root '../outside' } 'escapes'
        Assert-Throws { Get-Document 'https://untrusted.example/instructions.md' } 'Unexpected documentation URL'
    }
    Test-Case 'snapshot inventory includes hidden sync metadata and policy files' {
        $directory = Join-Path $script:temporary 'hidden-inventory'
        [IO.Directory]::CreateDirectory((Join-Path $directory '.github')) | Out-Null
        [IO.File]::WriteAllText((Join-Path $directory '.netconfig'), 'metadata')
        [IO.File]::WriteAllText((Join-Path $directory '.github/test.yml'), 'policy')
        $inventory = Get-Inventory $directory
        Assert ($inventory.ContainsKey('.netconfig') -and $inventory.ContainsKey('.github/test.yml')) 'Hidden synchronization changes were omitted.'
    }
    Test-Case 'metadata-only PR cannot be finalized as a complete update' {
        $f = $script:fixture
        $f.files = @(@{ filename = '.netconfig' }); $f.report.files = @('.netconfig')
        Assert-Throws { Assert-Report $f.report $f.manifest $f.issue $f.pr $f.files } 'metadata-only'
    }
    Test-Case 'MEAI ignore is version-scoped and privileged checkout is main-only' {
        $dependabot = [IO.File]::ReadAllText((Join-Path $Root '.github/dependabot.yml'))
        foreach ($type in @('major', 'minor', 'patch')) {
            Assert ($dependabot.Contains("version-update:semver-$type")) 'Missing version-scoped suppression.'
        }
        Assert ($dependabot.Contains('Microsoft.Extensions.AI*')) 'MEAI ownership missing.'
        $workflow = [IO.File]::ReadAllText((Join-Path $Root '.github/workflows/upstream-update-finalize.yml'))
        Assert ($workflow.Contains('ref: main') -and !$workflow.Contains('head.sha')) 'Privileged workflow can execute PR-head scripts.'
        foreach ($file in @('.github/release.yml', '.github/workflows/changelog.config')) {
            $config = [IO.File]::ReadAllText((Join-Path $Root $file))
            Assert ($config.Contains('enhancement') -and $config.Contains('bug')) 'Missing release categorization.'
            Assert (!$config.Contains('copilot-swe-agent')) 'Cloud author is excluded from release notes.'
        }
    }
    Write-Host "$script:passed upstream automation cases passed."
}
finally {
    $env:GH_TOKEN = $previousToken
    [IO.Directory]::Delete($script:temporary, $true)
}

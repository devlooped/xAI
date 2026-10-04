param(
    [ValidateSet('Library', 'Collect', 'Sync', 'Dispatch', 'Finalize')]
    [string] $Mode = 'Collect',
    [string] $Repository = $env:GITHUB_REPOSITORY,
    [string] $Root = (Join-Path $PSScriptRoot '../..'),
    [string] $Output,
    [string] $ManifestPath,
    [string] $EventPath,
    [switch] $Resume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (!$Repository) { $Repository = 'devlooped/xAI' }
if ($Repository -notmatch '^[\w.-]+/[\w.-]+$') { throw 'Invalid repository name.' }
$Root = [IO.Path]::GetFullPath($Root)
$script:cycleLabel = 'upstream-update'
$script:blockedLabel = 'upstream-update-blocked'
$script:evidenceHeading = '## Upstream update evidence'
$script:stateHeading = '## Upstream update state'
$script:resultHeading = '## Upstream update result'

function Invoke-Tool([string] $Name, [string[]] $Arguments, [int[]] $ExitCodes = @(0)) {
    $text = & $Name @Arguments 2>&1
    if ($LASTEXITCODE -notin $ExitCodes) {
        throw "$Name failed ($LASTEXITCODE): $($text -join "`n")"
    }
    return ($text -join "`n")
}

function Invoke-GitHub([string] $Path, [string] $Method = 'GET', $Body = $null, [switch] $Paginate) {
    $arguments = @('api', $Path, '--method', $Method, '-H', 'Accept: application/vnd.github+json',
        '-H', 'X-GitHub-Api-Version: 2022-11-28')
    if ($Paginate) { $arguments += @('--paginate', '--slurp') }
    $inputFile = $null
    try {
        if ($null -ne $Body) {
            $inputFile = [IO.Path]::GetTempFileName()
            [IO.File]::WriteAllText($inputFile, ($Body | ConvertTo-Json -Depth 40))
            $arguments += @('--input', $inputFile)
        }
        $text = Invoke-Tool gh $arguments
        if (!$text) { return $null }
        $value = ConvertFrom-Json -InputObject $text -AsHashtable -Depth 50
        if ($Paginate) {
            return @($value | ForEach-Object { foreach ($item in $_) { $item } })
        }
        return $value
    }
    finally {
        if ($inputFile) { [IO.File]::Delete($inputFile) }
    }
}

function Get-Hash([string] $Text) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()
}

function Format-Block([string] $Heading, $Value) {
    return $Heading + "`n" + '```json' + "`n" + ($Value | ConvertTo-Json -Depth 40) + "`n" + '```'
}

function Read-Block([string] $Text, [string] $Heading) {
    $match = [regex]::Match($Text, '(?m)^' + [regex]::Escape($Heading) + '\r?\n```json\r?\n([\s\S]*?)\r?\n```')
    if (!$match.Success) { throw "Missing JSON block: $Heading" }
    return ConvertFrom-Json $match.Groups[1].Value -AsHashtable -Depth 50
}

function Assert-Text($Value, [string] $Name) {
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) { throw "Missing text: $Name" }
}

function Get-Entries([string] $Config) {
    $entries = @()
    $entry = $null
    foreach ($line in ($Config -split '\r?\n')) {
        if ($line -match '^\[file(?: "([^"]+)")?\]$') {
            if ($entry) { $entries += $entry }
            $entry = @{ path = $Matches[1]; url = ''; skip = $false }
        }
        elseif ($entry -and $line -match '^\s*url\s*=\s*(\S+)\s*$') { $entry.url = $Matches[1] }
        elseif ($entry -and $line -match '^\s*skip(?:\s*=\s*true)?\s*$') { $entry.skip = $true }
    }
    if ($entry) { $entries += $entry }
    return @($entries | Where-Object { $_.path -and $_.url })
}

function Get-RelativeFile([string] $Directory, [string] $Path) {
    $full = [IO.Path]::GetFullPath((Join-Path $Directory $Path))
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (!$full.StartsWith($Directory.TrimEnd([char[]] @('/', '\')) + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw "Path escapes the workspace: $Path"
    }
    return $full
}

function Get-Revisions($Entries) {
    $revisions = @{}
    foreach ($entry in $Entries | Where-Object { !$_.skip }) {
        if ($entry.url -notmatch '^https://github\.com/([^/]+/[^/]+)/(?:blob|tree)/([^/]+)/') {
            throw "Cannot pin configured source: $($entry.url)"
        }
        $repo = $Matches[1]
        $ref = $Matches[2]
        $key = "$repo@$ref"
        if (!$revisions.ContainsKey($key)) {
            $commit = Invoke-GitHub "repos/$repo/commits/$([Uri]::EscapeDataString($ref))"
            if ($commit.sha -notmatch '^[a-f0-9]{40}$') { throw "Invalid revision for $key" }
            $revisions[$key] = @{ repository = $repo; ref = $ref; sha = $commit.sha }
        }
    }
    return @($revisions.Values | Sort-Object repository, ref)
}

function Set-Projection([string] $Config, $Revisions, [switch] $Restore) {
    foreach ($revision in $Revisions) {
        $from = if ($Restore) { $revision.sha } else { $revision.ref }
        $to = if ($Restore) { $revision.ref } else { $revision.sha }
        $pattern = '(https://github\.com/' + [regex]::Escape($revision.repository) +
            '/(?:blob|tree)/)' + [regex]::Escape($from) + '/'
        $Config = [regex]::Replace($Config, $pattern, { param($match) $match.Groups[1].Value + $to + '/' })
    }
    return $Config
}

function Invoke-Sync([string] $Directory, $Revisions, [switch] $Assessment) {
    $path = Join-Path $Directory '.netconfig'
    $canonical = [IO.File]::ReadAllText($path)
    foreach ($entry in Get-Entries $canonical | Where-Object { !$_.skip }) {
        if ($entry.url -notmatch '^https://github\.com/([^/]+/[^/]+)/(?:blob|tree)/([^/]+)/' -or
            !@($Revisions | Where-Object { $_.repository -eq $Matches[1] -and $_.ref -eq $Matches[2] }).Count) {
            throw "Configured source is not in the captured revisions: $($entry.url)"
        }
    }
    $projected = Set-Projection $canonical $Revisions
    [IO.File]::WriteAllText($path, $projected)
    Push-Location $Directory
    try {
        $log = Invoke-Tool dnx @('--yes', 'dotnet-file', '--', 'sync')
        if ($log -match '(?m)^\s*[x\u2717\u2718]\s') { throw "dotnet-file reported incomplete synchronization:`n$log" }
        Write-Host $log
    }
    finally {
        Pop-Location
        $current = [IO.File]::ReadAllText($path)
        [IO.File]::WriteAllText($path, (Set-Projection $current $Revisions -Restore))
    }
    $diagnostics = @()
    if (Test-Path (Join-Path $Directory 'src/xAI.Protocol')) {
        $normalization = & dotnet run --file (Join-Path $Root 'src/protofix.cs') (Join-Path $Directory 'src/xAI.Protocol') 2>&1
        $code = $LASTEXITCODE
        $text = $normalization -join "`n"
        Write-Host $text
        if ($code -ne 0) {
            if ($Assessment -and $code -eq 1 -and $text -match 'import not found') {
                $diagnostics += "New protocol imports require integration before the SDK can build:`n$text"
            }
            else { throw "Protocol normalization failed ($code): $text" }
        }
    }
    Invoke-Includes $Directory $Revisions
    return $diagnostics
}

function Invoke-Includes([string] $Directory, $Revisions) {
    $revision = @($Revisions | Where-Object repository -eq 'devlooped/actions-includes')
    if ($revision.Count -ne 1) { throw 'Missing pinned markdown-includes action revision.' }
    $file = Invoke-GitHub "repos/devlooped/actions-includes/contents/resolve-file-includes.ps1?ref=$($revision[0].sha)"
    $scriptPath = Join-Path ([IO.Path]::GetTempPath()) ("upstream-includes-$([Guid]::NewGuid()).ps1")
    $previous = $env:RESOLVE_VALIDATE
    try {
        [IO.File]::WriteAllBytes($scriptPath, [Convert]::FromBase64String($file.content))
        $env:RESOLVE_VALIDATE = 'true'
        Push-Location $Directory
        try { Write-Host (Invoke-Tool pwsh @('-NoProfile', '-File', $scriptPath)) }
        finally { Pop-Location }
    }
    finally {
        $env:RESOLVE_VALIDATE = $previous
        [IO.File]::Delete($scriptPath)
    }
}

function Get-Inventory([string] $Directory) {
    $inventory = @{}
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse -Force) {
        $relative = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\', '/')
        $inventory[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $inventory
}

function New-Snapshot($Entries, $Revisions, [string] $Directory) {
    $original = Join-Path $Directory 'base'
    $snapshot = Join-Path $Directory 'snapshot'
    [IO.Directory]::CreateDirectory($original) | Out-Null
    [IO.Directory]::CreateDirectory($snapshot) | Out-Null
    $tracked = (Invoke-Tool git @('-C', $Root, 'ls-files')) -split '\n'
    foreach ($file in $tracked) {
        $included = $file -eq '.netconfig' -or $file.EndsWith('.md')
        foreach ($entry in $Entries) {
            $prefix = $entry.path.TrimEnd('.').TrimEnd('/')
            if ($file -eq $entry.path -or ($entry.path.EndsWith('/.') -and $file.StartsWith("$prefix/"))) {
                $included = $true
            }
        }
        if ($included) {
            foreach ($target in @($original, $snapshot)) {
                $destination = Get-RelativeFile $target $file
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
                [IO.File]::Copy((Get-RelativeFile $Root $file), $destination, $true)
            }
        }
    }
    $diagnostics = @(Invoke-Sync $snapshot $Revisions -Assessment)
    $before = Get-Inventory $original
    $after = Get-Inventory $snapshot
    $files = @()
    foreach ($path in @(@($before.Keys) + @($after.Keys) | Sort-Object -Unique)) {
        if ($before[$path] -ne $after[$path]) {
            $files += @{ path = $path; before = $before[$path]; after = $after[$path] }
        }
    }
    Push-Location $Directory
    try {
        $patch = Invoke-Tool git @('diff', '--no-index', '--no-ext-diff', 'base', 'snapshot') @(0, 1)
        [IO.File]::WriteAllText((Join-Path $Directory 'sync.patch'), $patch + "`n")
    }
    finally { Pop-Location }
    return @{ files = $files; diagnostics = $diagnostics }
}

function Get-Document([string] $Uri) {
    if ($Uri -notmatch '^https://docs\.x\.ai/(?:llms\.txt|developers/[^?#]+\.md)$') {
        throw "Unexpected documentation URL: $Uri"
    }
    $response = Invoke-WebRequest -Uri $Uri -TimeoutSec 30 -MaximumRetryCount 2
    if ($response.Content.Length -gt 524288) { throw "Documentation page exceeds the evidence limit: $Uri" }
    return [string] $response.Content
}

function Collect-Evidence {
    Assert-Text $Output 'Output'
    if ((Test-Path -LiteralPath $Output) -and @(Get-ChildItem -LiteralPath $Output -Force).Count) {
        throw 'Evidence output must be empty; use a fresh directory for each collection.'
    }
    [IO.Directory]::CreateDirectory($Output) | Out-Null
    $Output = [IO.Path]::GetFullPath($Output)
    $manifest = [ordered]@{
        schema = 1
        request_id = if ($env:GITHUB_RUN_ID) { "$env:GITHUB_RUN_ID-$env:GITHUB_RUN_ATTEMPT" } else { [Guid]::NewGuid().ToString() }
        repository = $Repository
        base_sha = Invoke-Tool git @('-C', $Root, 'rev-parse', 'HEAD')
        run_url = if ($env:GITHUB_RUN_ID) { "https://github.com/$Repository/actions/runs/$env:GITHUB_RUN_ID" } else { 'Local collection only' }
        revisions = @()
        sync_files = @()
        diagnostics = @()
        documents = @()
        packages = @()
        candidates = @(
            @{ id = 'protocol:capabilities'; source = 'src/xAI.Protocol and GrokClient factory/DI coverage' },
            @{ id = 'http:capabilities'; source = 'https://docs.x.ai/llms.txt' },
            @{ id = 'meai:capabilities'; source = 'src/xAI adapters and current/target MEAI public APIs' }
        )
        previous_cycles = @()
        errors = @()
    }
    try {
        $entries = Get-Entries ([IO.File]::ReadAllText((Join-Path $Root '.netconfig')))
        $includeSource = @{ path = 'includes-action'; skip = $false
            url = 'https://github.com/devlooped/actions-includes/blob/v2/resolve-file-includes.ps1' }
        $manifest.revisions = @(Get-Revisions (@($entries) + @($includeSource)))
        $snapshot = New-Snapshot $entries $manifest.revisions $Output
        $manifest.sync_files = @($snapshot.files)
        $manifest.diagnostics = @($snapshot.diagnostics)
        if ($manifest.diagnostics.Count) {
            $manifest.candidates += @{ id = 'protocol:imports'; source = 'Normalization diagnostics; resolve new imports from authoritative pinned sources.' }
            Write-Warning 'Captured new protocol imports require agent integration; they are not a successful SDK build.'
        }
        foreach ($file in $manifest.sync_files | Where-Object { $_.path -ne '.netconfig' }) {
            $manifest.candidates += @{ id = "sync:$($file.path)"; source = $file.path }
        }
    }
    catch { $manifest.errors += "Synchronization: $($_.Exception.Message)"; Write-Warning $manifest.errors[-1] }
    try {
        $index = Get-Document 'https://docs.x.ai/llms.txt'
        [IO.File]::WriteAllText((Join-Path $Output 'llms.txt'), $index)
        $urls = @([regex]::Matches($index, 'https://docs\.x\.ai/developers/[^)\s]+\.md') |
            ForEach-Object Value | Sort-Object -Unique)
        if (!$urls.Count -or $urls.Count -gt 200) { throw "Unexpected documentation inventory size: $($urls.Count)" }
        [IO.Directory]::CreateDirectory((Join-Path $Output 'docs')) | Out-Null
        foreach ($url in $urls) {
            $document = Get-Document $url
            $hash = Get-Hash $document
            [IO.File]::WriteAllText((Join-Path $Output "docs/$hash.md"), $document)
            $manifest.documents += @{ url = $url; hash = $hash }
        }
    }
    catch { $manifest.errors += "HTTP documentation: $($_.Exception.Message)"; Write-Warning $manifest.errors[-1] }
    try {
        $references = @{}
        foreach ($project in Get-ChildItem (Join-Path $Root 'src') -Filter '*.csproj' -Recurse) {
            [xml] $xml = [IO.File]::ReadAllText($project.FullName)
            foreach ($reference in $xml.SelectNodes('//PackageReference[starts-with(@Include,"Microsoft.Extensions.AI")]')) {
                $name = $reference.GetAttribute('Include')
                $version = $reference.GetAttribute('Version')
                if ($version -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { throw "Unresolved MEAI version: $name $version" }
                if (!$references.ContainsKey($name)) { $references[$name] = @() }
                $references[$name] += @{ project = [IO.Path]::GetRelativePath($Root, $project.FullName).Replace('\', '/'); version = $version }
            }
        }
        if (!$references.Count) { throw 'No MEAI package references found.' }
        foreach ($name in $references.Keys | Sort-Object) {
            $uri = "https://api.nuget.org/v3-flatcontainer/$($name.ToLowerInvariant())/index.json"
            $versions = Invoke-RestMethod -Uri $uri -TimeoutSec 30 -MaximumRetryCount 2
            $stable = @($versions.versions | Where-Object { $_ -match '^\d+\.\d+\.\d+(?:\.\d+)?$' } |
                Sort-Object { [version] $_ })
            if (!$stable.Count) { throw "No stable releases for $name" }
            $manifest.packages += @{ name = $name; references = $references[$name]; latest_stable = $stable[-1]; source = $uri }
            $manifest.candidates += @{ id = "meai:$name"; source = "https://www.nuget.org/packages/$name/$($stable[-1])" }
        }
        $closed = @(Invoke-GitHub "repos/$Repository/issues?labels=$script:cycleLabel&state=closed&sort=updated&per_page=5")
        $manifest.previous_cycles = @($closed | Where-Object { !$_.ContainsKey('pull_request') } |
            ForEach-Object { @{ number = $_.number; url = $_.html_url; title = $_.title } })
    }
    catch { $manifest.errors += "MEAI/assessment history: $($_.Exception.Message)"; Write-Warning $manifest.errors[-1] }
    $manifest.fingerprint = Get-Hash ($manifest | ConvertTo-Json -Depth 40 -Compress)
    [IO.File]::WriteAllText((Join-Path $Output 'manifest.json'), ($manifest | ConvertTo-Json -Depth 40))
    if ($manifest.errors.Count) { throw 'Evidence collection is incomplete; inspect manifest.json. No agent was dispatched.' }
    Write-Host "Evidence: $(Join-Path $Output 'manifest.json')"
}

function Assert-Manifest($Manifest) {
    if ($Manifest.schema -ne 1 -or $Manifest.repository -ne $Repository -or $Manifest.errors.Count) {
        throw 'Invalid or incomplete evidence manifest.'
    }
    Assert-Text $Manifest.request_id 'request_id'
    if ($Manifest.base_sha -notmatch '^[a-f0-9]{40}$' -or $Manifest.fingerprint -notmatch '^[a-f0-9]{64}$') {
        throw 'Invalid evidence revision/fingerprint.'
    }
    $copy = [ordered]@{}
    foreach ($key in $Manifest.Keys | Where-Object { $_ -ne 'fingerprint' }) { $copy[$key] = $Manifest[$key] }
    if ((Get-Hash ($copy | ConvertTo-Json -Depth 40 -Compress)) -ne $Manifest.fingerprint) {
        throw 'Evidence fingerprint does not match the manifest.'
    }
    if (!$Manifest.candidates.Count -or
        @($Manifest.candidates | ForEach-Object id | Sort-Object -Unique).Count -ne $Manifest.candidates.Count) {
        throw 'Duplicate evidence candidate IDs.'
    }
}

function Sync-Evidence {
    $manifest = ConvertFrom-Json ([IO.File]::ReadAllText($ManifestPath)) -AsHashtable -Depth 50
    Assert-Manifest $manifest
    if ((Invoke-Tool git @('-C', $Root, 'status', '--porcelain')) -ne '') {
        throw 'Apply mechanical sync before making edits, in a clean checkout.'
    }
    foreach ($file in $manifest.sync_files | Where-Object { $_.path -ne '.netconfig' }) {
        $path = Get-RelativeFile $Root $file.path
        $hash = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() } else { $null }
        if ($hash -ne $file.before -and $hash -ne $file.after) {
            throw "Local/previous-cycle changes overlap synchronization: $($file.path). Merge the captured snapshot explicitly; do not overwrite them."
        }
    }
    Invoke-Sync $Root $manifest.revisions
    foreach ($file in $manifest.sync_files | Where-Object { $_.path -ne '.netconfig' }) {
        $path = Get-RelativeFile $Root $file.path
        $hash = if (Test-Path -LiteralPath $path) { (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() } else { $null }
        if ($hash -ne $file.after) { throw "Synchronized content does not match evidence: $($file.path)" }
    }
}

function Get-Comments([int] $Issue) {
    return @(Invoke-GitHub "repos/$Repository/issues/$Issue/comments?per_page=100" -Paginate)
}

function Test-ReportAuthor($User) {
    if ($User.login -in @('copilot-swe-agent[bot]', 'Copilot') -and $User['type'] -eq 'Bot') {
        return $true
    }
    $permission = Invoke-GitHub "repos/$Repository/collaborators/$([Uri]::EscapeDataString($User.login))/permission"
    return $permission.permission -in @('admin', 'maintain', 'write')
}

function Get-TrustedReports([int] $Issue) {
    return @(Get-Comments $Issue | Where-Object {
        $_.body.StartsWith($script:resultHeading) -and (Test-ReportAuthor $_.user)
    })
}

function Get-State($Issue, $Comments) {
    $state = Read-Block $Issue.body $script:stateHeading
    foreach ($comment in $Comments) {
        if ($comment.user.login -eq $Issue.user.login -and $comment.body.StartsWith($script:stateHeading)) {
            $state = Read-Block $comment.body $script:stateHeading
        }
    }
    if ($state.schema -ne 1) { throw 'Unknown cycle state schema.' }
    return $state
}

function Save-State($Issue, $State) {
    Invoke-GitHub "repos/$Repository/issues/$($Issue.number)/comments" POST @{
        body = Format-Block $script:stateHeading $State
    } | Out-Null
}

function Get-Evidence($Issue, [int] $CommentId) {
    $body = $Issue.body
    if ($CommentId) {
        $comment = Invoke-GitHub "repos/$Repository/issues/comments/$CommentId"
        if ($comment.user.login -ne $Issue.user.login) { throw 'Untrusted evidence comment.' }
        $body = $comment.body
    }
    $manifest = Read-Block $body $script:evidenceHeading
    Assert-Manifest $manifest
    return $manifest
}

function Get-CyclePR($Issue, $State) {
    if ($State.pr_number) { return Invoke-GitHub "repos/$Repository/pulls/$($State.pr_number)" }
    $prs = @(Invoke-GitHub "repos/$Repository/pulls?state=all&per_page=100" -Paginate |
        Where-Object { $_.body -match "(?m)^Cycle: #$($Issue.number)\s*$" })
    if ($prs.Count -gt 1) { throw 'More than one PR is associated with the cycle.' }
    if ($prs.Count) { return $prs[0] }
    return $null
}

function Assert-CyclePR($PR, $Issue) {
    if ($PR.head.repo.full_name -ne $Repository -or $PR.base.ref -ne 'main' -or
        $PR.head.ref -notlike 'copilot/*' -or $PR.head.sha -notmatch '^[a-f0-9]{40}$' -or
        $PR.user.login -notin @('copilot-swe-agent[bot]', 'Copilot') -or
        $PR.body -notmatch "(?m)^Cycle: #$($Issue.number)\s*$") {
        throw 'PR is not the agent-owned branch for this cycle.'
    }
}

function Set-Blocked($Issue, [string] $Message) {
    Invoke-GitHub "repos/$Repository/issues/$($Issue.number)/labels" POST @{ labels = @($script:blockedLabel) } | Out-Null
    Invoke-GitHub "repos/$Repository/issues/$($Issue.number)/comments" POST @{ body = "Update blocked: $Message" } | Out-Null
}

function Start-Continuation($Issue, $State, $PR) {
    Assert-CyclePR $PR $Issue
    if ($PR.state -ne 'open') { throw 'Cannot continue a closed update PR.' }
    $manifest = Get-Evidence $Issue $State.evidence_id
    $marker = "Request: $($manifest.request_id)"
    $comments = @(Get-Comments $PR.number)
    if (!@($comments | Where-Object { $_.user.login -eq $Issue.user.login -and
        $_.body -match ('(?m)^' + [regex]::Escape($marker) + '\s*$') }).Count) {
        Invoke-GitHub "repos/$Repository/issues/$($PR.number)/comments" POST @{
            body = "@copilot Assess this week's evidence and update this same PR without overwriting human edits. " +
                "Preserve public compatibility. Follow the upstream-update agent completion contract; post your result on cycle #$($Issue.number).`n`n" +
                "$marker`nCurrent head: $($PR.head.sha)`n`n" + (Format-Block $script:evidenceHeading $manifest)
        } | Out-Null
    }
    $State.status = 'running'
    $State.pr_number = $PR.number
    Save-State $Issue $State
}

function Dispatch-Evidence($Manifest = $null) {
    if (!$env:GH_TOKEN) { throw 'UPSTREAM_UPDATE_TOKEN is required; GITHUB_TOKEN cannot assign Copilot.' }
    if (!$Manifest) { $Manifest = ConvertFrom-Json ([IO.File]::ReadAllText($ManifestPath)) -AsHashtable -Depth 50 }
    $manifest = $Manifest
    Assert-Manifest $manifest
    $actor = Invoke-GitHub 'user'
    $actors = Invoke-GitHub graphql POST @{ query = "query { repository(owner:`"$($Repository.Split('/')[0])`", name:`"$($Repository.Split('/')[1])`") { suggestedActors(capabilities:[CAN_BE_ASSIGNED],first:100) { nodes { login } } } }" }
    if ('copilot-swe-agent' -notin @($actors.data.repository.suggestedActors.nodes | ForEach-Object login)) {
        throw 'Copilot is not assignable with this user token and repository policy.'
    }
    $labels = @(Invoke-GitHub "repos/$Repository/labels?per_page=100" -Paginate)
    foreach ($label in @($script:cycleLabel, $script:blockedLabel, 'enhancement', 'bug', 'dependencies')) {
        if ($label -notin @($labels | ForEach-Object name)) {
            Invoke-GitHub "repos/$Repository/labels" POST @{ name = $label; color = '0366d6' } | Out-Null
        }
    }
    $issues = @(Invoke-GitHub "repos/$Repository/issues?labels=$script:cycleLabel&state=open&per_page=100" -Paginate |
        Where-Object { !$_.ContainsKey('pull_request') })
    if ($issues.Count -gt 1) { throw 'Multiple open update cycles; reconcile them before dispatching.' }
    if (!$issues.Count) {
        $state = @{ schema = 1; status = 'pending-assignment'; evidence_id = 0; queued_id = 0; pr_number = 0 }
        $body = "Weekly comprehensive SDK assessment. Implement actionable xAI/HTTP/MEAI gaps in one compatible draft PR, " +
            "or report a justified no-op. Follow the upstream-update custom agent. Keep Cycle: #<this issue> in the PR body.`n`n" +
            (Format-Block $script:evidenceHeading $manifest) + "`n`n" + (Format-Block $script:stateHeading $state)
        if ($body.Length -gt 60000) { throw 'Evidence exceeds the issue limit; no cycle was created.' }
        $issue = Invoke-GitHub "repos/$Repository/issues" POST @{
            title = 'Assess and integrate upstream xAI and MEAI updates'
            body = $body
            labels = @($script:cycleLabel)
        }
    }
    else {
        $issue = $issues[0]
        if ($issue.user.login -ne $actor.login) { throw 'The cycle is owned by another automation user; rotate tokens within the same identity.' }
        $state = Get-State $issue (Get-Comments $issue.number)
        if ($state.status -ne 'no-op') {
            $pr = Get-CyclePR $issue $state
            if ($pr -and $pr.state -eq 'closed') {
                Complete-ClosedPR $issue $state $pr
                Dispatch-Evidence $manifest
                return
            }
        }
        $current = Get-Evidence $issue $state.evidence_id
        if ($current.request_id -ne $manifest.request_id) {
            $body = Format-Block $script:evidenceHeading $manifest
            if ($body.Length -gt 60000) { throw 'Evidence exceeds the comment limit.' }
            $comment = Invoke-GitHub "repos/$Repository/issues/$($issue.number)/comments" POST @{ body = $body }
            $state.queued_id = $comment.id
            Save-State $issue $state
        }
        if ($state.status -eq 'no-op') {
            Complete-NoOp $issue $state
            return
        }
        if ($state.status -eq 'running' -and !$Resume) {
            $reports = @(Get-TrustedReports $issue.number)
            if ($reports.Count) {
                $report = Read-Block $reports[-1].body $script:resultHeading
                if ($report.request_id -eq $current.request_id -and $report.fingerprint -eq $current.fingerprint) {
                    Complete-Report $issue $report
                    return
                }
            }
        }
    }
    if ($state.status -eq 'pending-assignment') {
        if (!@($issue.assignees | Where-Object { $_.login -in @('copilot-swe-agent[bot]', 'Copilot') }).Count) {
            $assigned = Invoke-GitHub "repos/$Repository/issues/$($issue.number)/assignees" POST @{
                assignees = @('copilot-swe-agent[bot]')
                agent_assignment = @{
                    target_repo = $Repository; base_branch = 'main'; custom_agent = 'upstream-update'
                    custom_instructions = 'Assess all evidence, including HTTP-only and MEAI opportunities. Preserve compatibility. Report every candidate and the exact remote head SHA using the upstream-update completion contract.'
                }
            }
            if (!@($assigned.assignees | Where-Object { $_.login -in @('copilot-swe-agent[bot]', 'Copilot') }).Count) {
                throw 'GitHub did not assign Copilot; the same pending cycle will be retried.'
            }
        }
        $state.status = 'running'
        Save-State $issue $state
    }
    elseif ($state.status -in @('awaiting-review', 'pending-continuation') -or
        ($state.status -in @('blocked', 'running') -and $Resume)) {
        $pr = Get-CyclePR $issue $state
        if (!$pr) { throw 'No linked PR to resume. Inspect the cloud session and explicitly reassign the existing issue if needed.' }
        if ($state.queued_id) { $state.evidence_id = $state.queued_id; $state.queued_id = 0 }
        $state.status = 'pending-continuation'
        Save-State $issue $state
        Start-Continuation $issue $state $pr
    }
    elseif ($state.status -eq 'running') {
        Write-Host "Cycle #$($issue.number) is running; new evidence remains queued."
        Write-Warning 'If the session has stopped without a report, inspect it and explicitly resume; do not start a concurrent session.'
    }
    elseif ($state.status -eq 'blocked') {
        throw "Cycle #$($issue.number) is blocked. Inspect it before using the manual resume input."
    }
    else { throw "Unexpected active cycle status: $($state.status)" }
}

function Assert-Report($Report, $Manifest, $Issue, $PR, $Files) {
    if ($Report.schema -ne 1 -or $Report.issue_number -ne $Issue.number -or
        $Report.request_id -ne $Manifest.request_id -or $Report.fingerprint -ne $Manifest.fingerprint) {
        throw 'Completion report does not match the active cycle evidence.'
    }
    if ($Report.status -notin @('complete', 'no-op', 'blocked')) { throw 'Invalid completion status.' }
    foreach ($field in @('decisions', 'files', 'enhancements', 'bug_fixes', 'protocol_updates', 'blockers')) {
        if ($Report[$field] -isnot [array]) { throw "Report $field must be an array." }
    }
    foreach ($field in @('files', 'enhancements', 'bug_fixes', 'protocol_updates', 'blockers')) {
        foreach ($item in $Report[$field]) { Assert-Text $item "item in $field" }
    }
    if ($Report.validation -isnot [System.Collections.IDictionary]) { throw 'Report validation must be an object.' }
    $expected = @($Manifest.candidates | ForEach-Object id | Sort-Object)
    $actual = @($Report.decisions | ForEach-Object id | Sort-Object)
    if (($expected -join "`n") -ne ($actual -join "`n")) { throw 'Every candidate needs exactly one disposition.' }
    foreach ($decision in $Report.decisions) {
        if ($decision.disposition -notin @('integrated', 'protocol-only', 'already-covered', 'irrelevant', 'blocked')) {
            throw "Invalid disposition for $($decision.id)"
        }
        Assert-Text $decision.reason "reason for $($decision.id)"
        if ($decision.evidence -isnot [array] -or !$decision.evidence.Count) { throw "Missing evidence for $($decision.id)" }
        foreach ($citation in $decision.evidence) { Assert-Text $citation 'citation' }
    }
    if ($PR) {
        Assert-CyclePR $PR $Issue
        if ($Report.pr_number -ne $PR.number -or $Report.head_sha -ne $PR.head.sha -or $PR.state -ne 'open') {
            throw 'Report PR/head is stale or closed.'
        }
    }
    elseif ($Report.pr_number -ne 0 -or $Report.head_sha) { throw 'Report names a missing PR.' }
    if ((@($Report.files | Sort-Object) -join "`n") -ne (@($Files | ForEach-Object filename | Sort-Object) -join "`n")) {
        throw 'Reported files do not match the actual PR diff.'
    }
    Assert-Text $Report.compatibility 'compatibility'
    if ($Report.status -eq 'complete') {
        if (!$PR -or !@($Files | Where-Object filename -ne '.netconfig').Count) {
            throw 'A complete update requires a substantive PR, not metadata-only bookkeeping.'
        }
        if ($Report.blockers.Count -or @($Report.decisions | Where-Object disposition -eq 'blocked').Count) {
            throw 'Unresolved gaps cannot be declared complete.'
        }
        foreach ($check in @('build', 'tests', 'whitespace', 'style')) {
            if ($Report.validation[$check] -ne 'passed') { throw "Required validation has not passed: $check" }
        }
        Assert-Text $Report.validation.live_tests 'live test disclosure'
        Assert-Text $Report.title 'outcome-based title'
        if ($Report.title -match '(?i)bump files|files with dotnet-file|^bump\b' -or $Report.title.Length -gt 120) {
            throw 'Use an outcome-based title, not a generic bump.'
        }
        foreach ($file in $Manifest.sync_files | Where-Object path -ne '.netconfig') {
            if ($file.path -notin @($Files | ForEach-Object filename)) {
                throw "Required synchronization is missing from the PR: $($file.path)"
            }
        }
    }
    if ($Report.status -eq 'no-op' -and ($Files.Count -or $Report.blockers.Count -or $Manifest.diagnostics.Count -or
        $Report.enhancements.Count -or $Report.bug_fixes.Count -or
        @($Manifest.sync_files | Where-Object path -ne '.netconfig').Count -or
        @($Report.decisions | Where-Object { $_.disposition -in @('integrated', 'blocked') }).Count)) {
        throw 'Substantive changes or unresolved gaps cannot be closed as a no-op.'
    }
    if ($Report.status -eq 'blocked' -and !$Report.blockers.Count) { throw 'A blocked result must explain the blockers.' }
}

function Format-Summary($Report, $Manifest, [int] $Issue) {
    $text = "## Upstream SDK update`n`nCycle: #$Issue`n`n"
    foreach ($section in @(
        @('Enhancements', 'enhancements'), @('Bug fixes', 'bug_fixes'), @('Protocol and dependency updates', 'protocol_updates')
    )) {
        $text += "### $($section[0])`n"
        $items = $Report[$section[1]]
        $text += if ($items.Count) { ($items | ForEach-Object { "- $_" }) -join "`n" } else { 'None.' }
        $text += "`n`n"
    }
    $text += "### Compatibility`n$($Report.compatibility)`n`n### Coverage decisions`n"
    foreach ($decision in $Report.decisions) {
        $text += "- **$($decision.id)** ($($decision.disposition)): $($decision.reason) Sources: $($decision.evidence -join ', ')`n"
    }
    $text += "`n### Validation`n"
    foreach ($check in $Report.validation.Keys | Sort-Object) { $text += "- ${check}: $($Report.validation[$check])`n" }
    $text += "`n### Release notes`n"
    foreach ($item in $Report.enhancements) { $text += "- Enhancement: $item`n" }
    foreach ($item in $Report.bug_fixes) { $text += "- Fix: $item`n" }
    if (!$Report.enhancements.Count -and !$Report.bug_fixes.Count) { $text += "- Protocol/dependency maintenance; no new SDK behavior claimed.`n" }
    return $text + "`nEvidence: $($Manifest.run_url)"
}

function Update-Summary([string] $Body, [string] $Summary) {
    $start = '<!-- upstream-update:summary -->'
    $end = '<!-- upstream-update:end -->'
    $managed = "$start`n$Summary`n$end"
    $pattern = [regex]::Escape($start) + '[\s\S]*?' + [regex]::Escape($end)
    if ([regex]::IsMatch($Body, $pattern)) { return [regex]::Replace($Body, $pattern, { $managed }) }
    return $Body.TrimEnd() + "`n`n" + $managed
}

function Complete-ClosedPR($Issue, $State, $PR) {
    Assert-CyclePR $PR $Issue
    if ($PR.state -ne 'closed') { throw 'Cannot reconcile an open PR as closed.' }
    if ($State.pr_number -and $State.pr_number -ne $PR.number) { throw 'Closed PR does not match the recorded cycle.' }
    $State.pr_number = $PR.number
    if ($State.status -eq 'no-op') {
        if ($Issue.state -eq 'open') { Complete-NoOp $Issue $State }
        Write-Host 'No-op PR closure is already reconciled.'
        return
    }
    $State.status = if ($PR.merged) { 'merged' } else { 'closed-unmerged' }
    Save-State $Issue $State
    Invoke-GitHub "repos/$Repository/issues/$($Issue.number)" PATCH @{ state = 'closed' } | Out-Null
}

function Finalize-Cycle {
    if (!$env:GH_TOKEN) { throw 'UPSTREAM_UPDATE_TOKEN is required for cycle reconciliation.' }
    $event = ConvertFrom-Json ([IO.File]::ReadAllText($EventPath)) -AsHashtable -Depth 50
    if ($event.ContainsKey('comment')) {
        $issue = $event.issue
        if ($issue.ContainsKey('pull_request') -or $script:cycleLabel -notin @($issue.labels | ForEach-Object name)) { return }
        if (!(Test-ReportAuthor $event.comment.user)) { Write-Warning 'Ignored completion from an untrusted actor.'; return }
        $report = Read-Block $event.comment.body $script:resultHeading
    }
    elseif ($event.ContainsKey('pull_request')) {
        $pr = $event.pull_request
        if ($pr.body -notmatch '(?m)^Cycle: #(\d+)\s*$') { throw 'Missing cycle link on the closed PR.' }
        $issue = Invoke-GitHub "repos/$Repository/issues/$($Matches[1])"
        $state = Get-State $issue (Get-Comments $issue.number)
        Complete-ClosedPR $issue $state $pr
        if ($state.status -eq 'no-op') { return }
        if ($state.queued_id) {
            throw 'Cycle closed with queued evidence. Run the weekly workflow to collect fresh evidence against main.'
        }
        return
    }
    else {
        $number = [int] $env:CYCLE_ISSUE
        if (!$number) { throw 'Manual reconciliation requires a cycle issue number.' }
        $issue = Invoke-GitHub "repos/$Repository/issues/$number"
        $reports = @(Get-TrustedReports $number)
        if (!$reports.Count) { Set-Blocked $issue 'No completion report; inspect the agent session before resuming.'; throw 'Missing completion report.' }
        $report = Read-Block $reports[-1].body $script:resultHeading
    }
    Complete-Report $issue $report
}

function Complete-NoOp($Issue, $State) {
    $pr = Get-CyclePR $Issue $State
    if ($pr) {
        Assert-CyclePR $pr $Issue
        $files = @(Invoke-GitHub "repos/$Repository/pulls/$($pr.number)/files?per_page=100" -Paginate)
        if (!$pr.draft -or $files.Count) { throw 'Only an empty automation-owned draft can be closed as a no-op.' }
        if ($pr.state -notin @('open', 'closed')) { throw 'Unknown no-op PR state.' }
        $State.pr_number = $pr.number
    }
    $State.status = 'no-op'
    Save-State $Issue $State
    if ($pr -and $pr.state -eq 'open') {
        Invoke-GitHub "repos/$Repository/pulls/$($pr.number)" PATCH @{ state = 'closed' } | Out-Null
    }
    if ($script:blockedLabel -in @($Issue.labels | ForEach-Object name)) {
        Invoke-GitHub "repos/$Repository/issues/$($Issue.number)/labels/$script:blockedLabel" DELETE | Out-Null
    }
    Invoke-GitHub "repos/$Repository/issues/$($Issue.number)" PATCH @{ state = 'closed' } | Out-Null
    if ($State.queued_id) {
        $queued = Get-Evidence $Issue $State.queued_id
        Dispatch-Evidence $queued
    }
}

function Complete-Report($Issue, $Report) {
    $comments = Get-Comments $issue.number
    $state = Get-State $issue $comments
    if ($state.status -in @('no-op', 'merged', 'closed-unmerged') -and $issue.state -eq 'closed') {
        Write-Host 'Cycle is already reconciled.'
        return
    }
    if ($state.status -eq 'no-op') {
        Complete-NoOp $issue $state
        return
    }
    $manifest = Get-Evidence $issue $state.evidence_id
    $pr = Get-CyclePR $issue $state
    $files = @()
    if ($pr) { $files = @(Invoke-GitHub "repos/$Repository/pulls/$($pr.number)/files?per_page=100" -Paginate) }
    Assert-Report $report $manifest $issue $pr $files
    if ($report.status -eq 'complete') {
        foreach ($file in $manifest.sync_files | Where-Object { $_.path.EndsWith('.proto') }) {
            if (!$file.after) {
                $change = @($files | Where-Object filename -eq $file.path)[0]
                if ($change.status -ne 'removed') { throw "Protocol removal does not match evidence: $($file.path)" }
            }
            else {
                $path = [Uri]::EscapeDataString($file.path).Replace('%2F', '/')
                $content = Invoke-GitHub "repos/$Repository/contents/${path}?ref=$($pr.head.sha)"
                $bytes = [Convert]::FromBase64String($content.content)
                $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
                if ($hash -ne $file.after) { throw "Committed protocol bytes do not match evidence: $($file.path)" }
            }
        }
    }
    if ($report.status -eq 'blocked') {
        $state.status = 'blocked'
        if ($pr) { $state.pr_number = $pr.number }
        Save-State $issue $state
        Set-Blocked $issue ($report.blockers -join '; ')
        throw 'Agent assessment is blocked; the cycle remains open.'
    }
    if ($report.status -eq 'no-op') {
        Complete-NoOp $issue $state
        return
    }
    if ($report.status -eq 'complete') {
        $body = Update-Summary $pr.body (Format-Summary $report $manifest $issue.number)
        if ($body.Length -gt 60000) { throw 'Final PR summary exceeds the GitHub body limit.' }
        $labels = @($pr.labels | ForEach-Object name |
            Where-Object { $_ -notin @('dependencies', 'docs', 'documentation', 'enhancement', 'bug', $script:blockedLabel) })
        $labels += $script:cycleLabel
        if ($report.enhancements.Count) { $labels += 'enhancement' }
        if ($report.bug_fixes.Count) { $labels += 'bug' }
        if (!$report.enhancements.Count -and !$report.bug_fixes.Count) { $labels += 'dependencies' }
        elseif (@($labels | Where-Object { $_ -in @('bydesign', 'duplicate', 'discussion', 'question',
            'invalid', 'wontfix', 'need info', 'techdebt') }).Count) {
            throw 'A human release-exclusion label remains on this feature/fix PR. Resolve its categorization before finalizing.'
        }
        Invoke-GitHub "repos/$Repository/pulls/$($pr.number)" PATCH @{ title = $report.title; body = $body } | Out-Null
        Invoke-GitHub "repos/$Repository/issues/$($pr.number)/labels" PUT @{ labels = @($labels | Sort-Object -Unique) } | Out-Null
        $state.status = 'awaiting-review'
        $state.pr_number = $pr.number
    }
    if ($script:blockedLabel -in @($issue.labels | ForEach-Object name)) {
        Invoke-GitHub "repos/$Repository/issues/$($issue.number)/labels/$script:blockedLabel" DELETE | Out-Null
    }
    Save-State $issue $state
    if ($state.queued_id) {
        $state.evidence_id = $state.queued_id
        $state.queued_id = 0
        $state.status = 'pending-continuation'
        Save-State $issue $state
        Start-Continuation $issue $state $pr
    }
}

switch ($Mode) {
    'Collect' { Collect-Evidence }
    'Sync' { Sync-Evidence }
    'Dispatch' { Dispatch-Evidence }
    'Finalize' { Finalize-Cycle }
}

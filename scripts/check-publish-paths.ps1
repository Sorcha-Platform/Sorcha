#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Publish-path coverage gate.
#
# docker-publish.yml rebuilds an image only when a file under one of that service's
# SERVICE_PATHS entries changed. A project can ProjectReference a library living under a
# DIFFERENT app's directory — and when it does, the referencing image is NEVER rebuilt if
# only that library changed. It publishes a stale bundle, with no error anywhere.
#
# That happened: Sorcha.UI.Components.User (the F122 shared user-facing component library)
# lives under src/Apps/Sorcha.UI/ and is consumed by wallet-pwa and verifier too. A slider
# render-loop fix (#1319) rebuilt ui-web and shipped a stale wallet-pwa carrying the same
# frozen form — which reads as "the fix didn't work on the phone", not "the image is stale".
#
# Changes under src/Common/ and src/Core/ already force a rebuild-all in docker-publish.yml,
# so this gate covers exactly the gap: cross-references INSIDE src/Apps/.
#
# Exits non-zero listing each (service -> referenced project) pair that is not covered.

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# BOTH workflows carry their own copy of the SERVICE_PATHS map, and both must stay covered.
# docker-publish.yml decides what gets REBUILT AND SHIPPED; docker-ci.yml decides what gets
# BUILD-VALIDATED on a PR. Fixing only the publish copy (as #1320 did) leaves PR validation
# under-building: a change to a shared library never proves the consuming images still build,
# and the first sign of trouble is a publish failure on master.
$workflowNames = @('docker-publish.yml', 'docker-ci.yml')

# --- Parse SERVICE_PATHS["svc"]="p1 p2 ..." and DOCKERFILES["svc"]="path/to/Dockerfile" ---
function Read-WorkflowMaps([string]$workflowName) {
    $workflow = Join-Path $repoRoot ".github/workflows/$workflowName"
    if (-not (Test-Path $workflow)) {
        Write-Error "$workflowName not found at $workflow"
        exit 1
    }

    $servicePaths = @{}
    $dockerfiles  = @{}
    foreach ($line in (Get-Content $workflow)) {
        if ($line -match 'SERVICE_PATHS\["([^"]+)"\]="([^"]*)"') {
            $servicePaths[$Matches[1]] = @($Matches[2] -split '\s+' | Where-Object { $_ })
        }
        elseif ($line -match 'DOCKERFILES\["([^"]+)"\]="([^"]*)"') {
            $dockerfiles[$Matches[1]] = $Matches[2]
        }
    }

    if ($servicePaths.Count -eq 0) {
        Write-Error "Parsed zero SERVICE_PATHS entries from $workflowName — the workflow format changed; update this gate."
        exit 1
    }

    # A multi-path entry is only meaningful if the consuming loop WORD-SPLITS it. Grepping the
    # whole value as one pattern makes a two-path entry match nothing at all — strictly worse
    # than the single path it replaced, and completely silent. That regression was written and
    # nearly shipped: docker-ci.yml's map was widened while its loop still did
    # `grep -q "^${SERVICE_PATHS[$SVC]}"`, and the only symptom was two images being validated
    # where three had been before.
    $hasMultiPath = $servicePaths.Values | Where-Object { $_.Count -gt 1 }
    if ($hasMultiPath) {
        $raw = Get-Content $workflow -Raw
        if ($raw -notmatch 'for\s+SVC_PATH\s+in\s+\$\{SERVICE_PATHS\[\$SVC\]\}') {
            Write-Host ''
            Write-Host 'publish-paths gate FAILED' -ForegroundColor Red
            Write-Host ''
            Write-Host ("{0} has SERVICE_PATHS entries with more than one path, but its matching" -f $workflowName)
            Write-Host 'loop does not word-split them. Every multi-path entry silently matches NOTHING.'
            Write-Host ''
            Write-Host 'Fix: iterate the value, e.g.'
            Write-Host '    for SVC_PATH in ${SERVICE_PATHS[$SVC]}; do   # unquoted on purpose'
            Write-Host ''
            exit 1
        }
    }

    return @{ ServicePaths = $servicePaths; Dockerfiles = $dockerfiles }
}

function Normalize([string]$p) { ($p -replace '\\', '/').TrimEnd('/') }

# Resolve a csproj's ProjectReference targets to repo-relative directories.
function Get-ProjectReferences([string]$csprojPath) {
    if (-not (Test-Path $csprojPath)) { return @() }
    $dir = Split-Path -Parent $csprojPath
    $refs = @()
    foreach ($m in [regex]::Matches((Get-Content $csprojPath -Raw), 'ProjectReference\s+Include="([^"]+)"')) {
        $resolved = Join-Path $dir $m.Groups[1].Value
        try { $full = (Resolve-Path $resolved -ErrorAction Stop).Path } catch { continue }
        $rel = Normalize ($full.Substring($repoRoot.Length).TrimStart('\', '/'))
        $refs += (Split-Path -Parent $rel) -replace '\\', '/'
    }
    return $refs
}

# Walk references transitively — a two-hop dependency goes just as stale as a one-hop one.
function Get-TransitiveAppRefs([string]$csprojPath) {
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($csprojPath)
    $result = @()
    while ($queue.Count -gt 0) {
        $current = $queue.Dequeue()
        if (-not $seen.Add((Normalize $current))) { continue }
        $dir = Split-Path -Parent $current
        foreach ($m in [regex]::Matches((Get-Content $current -Raw -ErrorAction SilentlyContinue), 'ProjectReference\s+Include="([^"]+)"')) {
            $resolved = Join-Path $dir $m.Groups[1].Value
            try { $full = (Resolve-Path $resolved -ErrorAction Stop).Path } catch { continue }
            $rel = Normalize ($full.Substring($repoRoot.Length).TrimStart('\', '/'))
            if ($rel -like 'src/Apps/*') { $result += (Split-Path -Parent $rel) -replace '\\', '/' }
            $queue.Enqueue($full)
        }
    }
    return $result | Sort-Object -Unique
}

$violations = @()

foreach ($workflowName in $workflowNames) {
$maps = Read-WorkflowMaps $workflowName
$servicePaths = $maps.ServicePaths
$dockerfiles  = $maps.Dockerfiles

foreach ($svc in $servicePaths.Keys | Sort-Object) {
    if (-not $dockerfiles.ContainsKey($svc)) { continue }

    # The service's own project directory is the Dockerfile's directory.
    $ownDir = Normalize (Split-Path -Parent $dockerfiles[$svc])
    $csproj = Get-ChildItem -Path (Join-Path $repoRoot $ownDir) -Filter *.csproj -ErrorAction SilentlyContinue |
              Select-Object -First 1
    if (-not $csproj) { continue }

    $watched = $servicePaths[$svc] | ForEach-Object { Normalize $_ }

    foreach ($refDir in (Get-TransitiveAppRefs $csproj.FullName)) {
        # Covered when any watched path is a prefix of the referenced directory.
        $covered = $false
        foreach ($w in $watched) {
            if ($refDir -eq $w -or $refDir.StartsWith("$w/")) { $covered = $true; break }
        }
        if (-not $covered) {
            $violations += [pscustomobject]@{
                Workflow   = $workflowName
                Service    = $svc
                Referenced = $refDir
                Watched    = ($watched -join ', ')
            }
        }
    }
}
}

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host 'publish-paths gate FAILED' -ForegroundColor Red
    Write-Host ''
    Write-Host 'These images ProjectReference a project under src/Apps/ that their SERVICE_PATHS'
    Write-Host 'entry does not watch. A change to that project would rebuild some images but ship'
    Write-Host 'this one STALE, with no error:'
    Write-Host ''
    foreach ($v in $violations) {
        Write-Host ("  {0}  [{1}]" -f $v.Service, $v.Workflow) -ForegroundColor Yellow
        Write-Host ("      references : {0}" -f $v.Referenced)
        Write-Host ("      watches    : {0}" -f $v.Watched)
    }
    Write-Host ''
    Write-Host 'Fix: add the referenced directory to that service''s SERVICE_PATHS entry in the'
    Write-Host 'named workflow (entries are space-separated, ANY-match). Both docker-publish.yml'
    Write-Host 'and docker-ci.yml carry their own copy of the map — keep them in step.'
    Write-Host ''
    exit 1
}

Write-Host ("publish-paths gate passed — every src/Apps/ cross-reference is watched by its consuming image in {0}." -f ($workflowNames -join ' + ')) -ForegroundColor Green
exit 0

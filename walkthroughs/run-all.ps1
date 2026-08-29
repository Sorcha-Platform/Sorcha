#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
<#
.SYNOPSIS
    Runs the Sorcha walkthrough suite and reports a verdict per step.

.DESCRIPTION
    The CORE suite is the eighteen steps that are actually maintained and that constitute the
    platform's end-to-end regression check. It is what "18/18" refers to in MASTER-TASKS and in the
    node-state notes.

    Four things this runner has to get right, each of which has produced a WRONG verdict before:

    1. AN EXIT CODE IS NOT A VERDICT. ConstructionPermit/run-agents.ps1 prints "ERROR (exit 1)" for
       every agent and still exits 0; TradeFinance's setup has printed a raw HTTP 500 and exited 0.
       A run of this suite once scored a step PASS with all five of its agents dead against the
       wrong host. So every step is judged on its exit code AND on markers found in its transcript.

    2. OUTPUT MUST BE KEPT. The previous version of this script piped every step to Out-Null, so a
       failure told you the name of the step and nothing else. Each step now writes a log.

    3. ConstructionPermit and SelfBuildHouse must use run.ps1 -Scenario all, NOT run-agents.ps1.
       The agent launchers hard-code actors/*.json whose gatewayUrl is literally http://localhost,
       and only three of ConstructionPermit's five actors have a -remote variant, so they cannot
       target a remote node at all. The scenario runners read their URLs from state.json. That is
       where "3/3" comes from. (CyberEssentialsUac's run-agents.ps1 is safe despite its name: it
       spawns no agents and drives the API itself.)

    4. ORDER IS LOAD-BEARING, twice over:
         * ConstructionPermit runs FIRST because its setup enables the Public org node-wide, and
           three of the seven walkthroughs never enable it themselves. On a freshly-initialised
           database that is the difference between a run and a wall of 403s that read as
           permissions problems.
         * run-suspension MUST precede run-revocation. Revocation is terminal by design, so it
           consumes the only ACTIVE credential and suspension then fails at its first step — on the
           innocent script.

    Nothing aborts the run. A suite that stops at the first red tells you about one walkthrough when
    it was about to tell you seven.

.PARAMETER Profile
    Target node. 'n1' targets https://n1.sorcha.dev. Use -GatewayUrl for any other node.

.PARAMETER GatewayUrl
    Deploy-anywhere override, e.g. http://tiny:8090. Takes precedence over -Profile.

.PARAMETER Suite
    'core' (default) — the eighteen maintained steps.
    'legacy' — the older foundation/single-org walkthroughs, which are NOT part of the regression
               baseline and are not all currently maintained.
    'all' — both.

.PARAMETER AuthGapMs
    Spacing between /auth/ calls, passed to the shared module as SORCHA_WT_AUTH_GAP_MS. The module
    default (8000) assumes the shipped rate limits. A node with the RATELIMIT_* knobs raised
    (n1 runs AUTH_PERMIT=1200) can go far lower; 1000 is ~60/min.
    NOTE: lowering this compresses the timeline and has surfaced latent races that the slow default
    hid — that is a feature, but expect it to find things.

.EXAMPLE
    pwsh walkthroughs/run-all.ps1 -Profile n1

.EXAMPLE
    pwsh walkthroughs/run-all.ps1 -Profile n1 -StartAt 9      # resume after a fix
#>
[CmdletBinding()]
param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [string]$GatewayUrl,
    [ValidateSet('core', 'legacy', 'all')]
    [string]$Suite = 'core',
    [string]$LogDir,
    [int]$AuthGapMs = 0,
    [int]$StartAt = 1,
    [switch]$OnlySetup,
    [switch]$SkipAgentBuild
)

$ErrorActionPreference = 'Continue'
$wtRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $wtRoot

Import-Module (Join-Path $wtRoot 'modules/SorchaWalkthrough/SorchaWalkthrough.psm1') -Force

if (-not $LogDir) { $LogDir = Join-Path $wtRoot '.run-logs' }
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

if ($AuthGapMs -gt 0) { $env:SORCHA_WT_AUTH_GAP_MS = "$AuthGapMs" }

# Target arguments threaded into every setup. -GatewayUrl wins where a script supports it.
$targetArgs = if ($GatewayUrl) { @('-GatewayUrl', $GatewayUrl) } else { @('-Profile', $Profile) }

Write-WtBanner "Sorcha Walkthroughs — $Suite suite"
Write-Host ("  target : {0}" -f $(if ($GatewayUrl) { $GatewayUrl } else { $Profile }))
Write-Host ("  logs   : {0}" -f $LogDir)
if ($AuthGapMs -gt 0) { Write-Host ("  authgap: {0}ms" -f $AuthGapMs) }
Write-Host ""

# Secrets must exist before anything runs.
$secretsFile = Join-Path $wtRoot '.secrets/passwords.json'
if (-not (Test-Path $secretsFile)) {
    Write-WtInfo "Generating secrets..."
    & pwsh (Join-Path $wtRoot 'initialize-secrets.ps1')
}

# ---------------------------------------------------------------------------------------------
# The suite
# ---------------------------------------------------------------------------------------------
# Kind: 'setup'  — a provisioning script; receives the target args (+ -Force).
#       'run'    — a scenario/conformance script; reads its URLs from state.json.
#       'runp'   — a run script that needs the target passed explicitly.
$core = @(
    @{ W = 'ConstructionPermit';    Label = 'setup';       Script = 'setup.ps1';               Kind = 'setup' }
    @{ W = 'ConstructionPermit';    Label = 'all-3';       Script = 'run.ps1';                 Kind = 'run';  Extra = @('-Scenario','all') }
    @{ W = 'ForestryCertification'; Label = 'setup';       Script = 'setup.ps1';               Kind = 'setup' }
    @{ W = 'ForestryCertification'; Label = 'golden-path'; Script = 'run.ps1';                 Kind = 'run';  Extra = @('-Scenario','golden-path') }
    @{ W = 'TradeFinance';          Label = 'setup';       Script = 'setup.ps1';               Kind = 'setup' }
    @{ W = 'TradeFinance';          Label = 'all-3';       Script = 'run.ps1';                 Kind = 'run';  Extra = @('-Scenario','all') }
    @{ W = 'SelfBuildHouse';        Label = 'setup';       Script = 'setup.ps1';               Kind = 'setup' }
    @{ W = 'SelfBuildHouse';        Label = 'all-3';       Script = 'run.ps1';                 Kind = 'run';  Extra = @('-Scenario','all') }
    @{ W = 'AssuredIdentity';       Label = 'setup';       Script = 'setup.ps1';               Kind = 'setup' }
    @{ W = 'AssuredIdentity';       Label = 'phase1';      Script = 'run-phase1-identity.ps1'; Kind = 'run' }
    @{ W = 'CyberEssentialsUac';    Label = 'setup';       Script = 'setup.ps1';               Kind = 'setup' }
    @{ W = 'CyberEssentialsUac';    Label = 'scenarios';   Script = 'run-agents.ps1';          Kind = 'runp' }
    @{ W = 'CyberEssentialsUac';    Label = 'suspension';  Script = 'run-suspension.ps1';      Kind = 'runp' }
    @{ W = 'CyberEssentialsUac';    Label = 'revocation';  Script = 'run-revocation.ps1';      Kind = 'runp' }
    @{ W = 'CredentialLifecycle';   Label = 'setup';       Script = 'setup.ps1';               Kind = 'setup' }
    @{ W = 'CredentialLifecycle';   Label = 'conformance'; Script = 'run-conformance.ps1';     Kind = 'runp' }
    # EncryptionAtRest reads the node's MongoDB directly (ssh + docker exec), so it is the only
    # step that needs more than the gateway. Against -Profile n1 it derives the ssh host itself;
    # against a local Docker stack it uses docker here. It goes LAST because it promotes its own
    # register one-way and therefore provisions a fresh one on every run.
    @{ W = 'EncryptionAtRest';      Label = 'setup';       Script = 'setup.ps1';               Kind = 'setup' }
    @{ W = 'EncryptionAtRest';      Label = 'conformance'; Script = 'run-conformance.ps1';     Kind = 'runp' }
)

# NOT part of the regression baseline. Kept so the scripts stay reachable, not because they are
# currently maintained against every node profile.
$legacy = @(
    @{ W = 'AdminIntegration';      Label = 'test';        Script = 'test-admin-integration.ps1'; Kind = 'runp' }
    @{ W = 'McpServerBasics';       Label = 'test';        Script = 'test-mcp-server.ps1';        Kind = 'runp'; LocalOnly = $true }
    @{ W = 'RegisterCreationFlow';  Label = 'setup';       Script = 'setup.ps1';                  Kind = 'setup' }
    @{ W = 'RegisterCreationFlow';  Label = 'run';         Script = 'run.ps1';                    Kind = 'run' }
    @{ W = 'WalletVerification';    Label = 'setup';       Script = 'setup.ps1';                  Kind = 'setup' }
    @{ W = 'WalletVerification';    Label = 'run';         Script = 'run.ps1';                    Kind = 'run' }
    @{ W = 'PayloadTests';          Label = 'setup';       Script = 'setup.ps1';                  Kind = 'setup' }
    @{ W = 'PayloadTests';          Label = 'run';         Script = 'run.ps1';                    Kind = 'run' }
)

$steps = switch ($Suite) {
    'core'   { $core }
    'legacy' { $legacy }
    'all'    { $core + $legacy }
}

# Number them after selection so -StartAt lines up with what is printed.
for ($i = 0; $i -lt $steps.Count; $i++) { $steps[$i].N = $i + 1 }

# ---------------------------------------------------------------------------------------------
# Pre-build any agent the suite spawns. Five concurrent `dotnet run` invocations race to build the
# same assembly and ALL of them die on the file lock — reported as "The build failed" inside each
# agent's own log while the launcher still exits 0.
# ---------------------------------------------------------------------------------------------
if (-not $SkipAgentBuild) {
    $agentProj = Join-Path $repoRoot 'src/Apps/Sorcha.Agent/Sorcha.Agent.csproj'
    if (Test-Path $agentProj) {
        Write-WtInfo "Pre-building Sorcha.Agent (concurrent dotnet run would race on the build)..."
        & dotnet build $agentProj -v q --nologo 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-WtWarn "Agent pre-build failed — agent-spawning steps may fail on a build race" }
    }
}

# Markers that mean failure REGARDLESS of the exit code. Each one is here because a step once
# reported success while the transcript said otherwise.
$failMarkers = @(
    'ERROR \(exit',                 # run-agents.ps1 per-agent summary; the launcher still exits 0
    'The build failed',             # concurrent dotnet run losing the build race
    'actively refused',             # pointed at the wrong host
    'No connection could be made',
    'Unable to connect',
    '\[FAIL\]',                     # conformance-style per-check verdict
    '"status":50[0-9]',             # a server error surfaced in the transcript
    'Invoke-RestMethod:',           # an unhandled REST failure printed by the script
    'Invoke-WebRequest:'
)

$results = [System.Collections.Generic.List[object]]::new()
$suiteStart = Get-Date

foreach ($s in $steps) {
    if ($s.N -lt $StartAt) { continue }
    if ($OnlySetup -and $s.Kind -ne 'setup') { continue }

    $dir = Join-Path $wtRoot $s.W
    $path = Join-Path $dir $s.Script
    $tag = "{0:D2}-{1}-{2}" -f $s.N, $s.W, $s.Label
    $log = Join-Path $LogDir "$tag.log"

    if (-not (Test-Path $path)) {
        Write-Host ("[{0,2}] {1,-22} {2,-12} SKIP (script not found)" -f $s.N, $s.W, $s.Label) -ForegroundColor Gray
        $results.Add([pscustomobject]@{ N = $s.N; W = $s.W; Label = $s.Label; Status = 'SKIP'; Secs = 0; Note = 'script not found' })
        continue
    }

    # A step that CANNOT run against the selected target is SKIPPED, not FAILED.
    #
    # Some walkthroughs drive a local container stack (they check Docker, then `docker compose`
    # against localhost) and have no remote equivalent. Reporting those as failures against -Profile
    # n1 is worse than useless: it manufactures red that no code change can ever turn green, and it
    # buries the failures that are real. Six legacy steps did exactly that until 2026-08-29 — their
    # ValidateSet simply predated the n1 profile, so PowerShell rejected the argument and the step
    # never executed, yet the suite still printed FAIL.
    if ($s.LocalOnly -and ($GatewayUrl -or $Profile -eq 'n1')) {
        Write-Host ("[{0,2}] {1,-22} {2,-12} SKIP (local-only: needs a local Docker stack)" -f $s.N, $s.W, $s.Label) -ForegroundColor Gray
        $results.Add([pscustomobject]@{ N = $s.N; W = $s.W; Label = $s.Label; Status = 'SKIP'; Secs = 0; Note = 'local-only' })
        continue
    }

    $stepArgs = switch ($s.Kind) {
        'setup' { $targetArgs + @('-Force') }
        'runp'  { $targetArgs }
        default { @() }
    }
    if ($s.Extra) { $stepArgs += $s.Extra }

    Write-Host ("[{0,2}] {1,-22} {2,-12} running..." -f $s.N, $s.W, $s.Label) -NoNewline
    $t0 = Get-Date
    Push-Location $dir
    try {
        & pwsh -NoProfile -File $path @stepArgs *> $log
        $code = $LASTEXITCODE
        if ($null -eq $code) { $code = 0 }
    } catch {
        $_ | Out-File -FilePath $log -Append
        $code = 1
    } finally {
        Pop-Location
    }
    $secs = [int]((Get-Date) - $t0).TotalSeconds

    $markers = @()
    if (Test-Path $log) {
        $body = Get-Content $log -Raw -ErrorAction SilentlyContinue
        foreach ($m in $failMarkers) { if ($body -match $m) { $markers += $m } }
    }
    $ok = ($code -eq 0) -and ($markers.Count -eq 0)

    $note = ''
    if ($markers.Count -gt 0) { $note = "markers: $($markers -join ', '). " }
    if (-not $ok -and (Test-Path $log)) {
        $tail = Get-Content $log -Tail 40 -ErrorAction SilentlyContinue |
                Where-Object { $_ -match 'FAIL|rror|refused|40[0-9]|429|50[0-9]|timed out|already exists' }
        if ($tail) { $note += (($tail | Select-Object -Last 2) -join ' | ') }
        if ($note.Length -gt 400) { $note = $note.Substring(0, 400) }
    }

    $results.Add([pscustomobject]@{
        N = $s.N; W = $s.W; Label = $s.Label
        Status = $(if ($ok) { 'PASS' } else { 'FAIL' }); Secs = $secs; Note = $note
    })

    if ($ok) {
        Write-Host ("`r[{0,2}] {1,-22} {2,-12} PASS  {3,4}s   " -f $s.N, $s.W, $s.Label, $secs) -ForegroundColor Green
    } else {
        Write-Host ("`r[{0,2}] {1,-22} {2,-12} FAIL  {3,4}s   (exit $code, markers=$($markers.Count))" -f $s.N, $s.W, $s.Label, $secs) -ForegroundColor Red
        if ($note) { Write-Host "        $note" -ForegroundColor DarkYellow }
        Write-Host "        log: $log" -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------------------------------------
$pass = @($results | Where-Object { $_.Status -eq 'PASS' }).Count
$fail = @($results | Where-Object { $_.Status -eq 'FAIL' }).Count
$skip = @($results | Where-Object { $_.Status -eq 'SKIP' }).Count
$mins = [int]((Get-Date) - $suiteStart).TotalMinutes

Write-Host ""
Write-WtBanner "Results"
foreach ($r in $results) {
    $colour = switch ($r.Status) { 'PASS' { 'Green' } 'FAIL' { 'Red' } default { 'Gray' } }
    Write-Host ("  [{0,2}] {1,-4} {2,-22} {3,-12} {4,4}s" -f $r.N, $r.Status, $r.W, $r.Label, $r.Secs) -ForegroundColor $colour
}
Write-Host ""
Write-Host ("  {0} pass, {1} fail, {2} skip — {3} min" -f $pass, $fail, $skip, $mins) -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $LogDir 'summary.json')
Write-Host "  logs: $LogDir"
Write-Host ""

# The suite passing is NOT the whole verdict on a pinning-capable node. Feature 194/195 degrade to
# the old behaviour rather than to an error, so the counter is the positive check.
if ($Suite -ne 'legacy') {
    Write-Host "  Still to check by hand — the suite cannot see it:" -ForegroundColor Yellow
    Write-Host "    docker logs sorcha-blueprint-service 2>&1 | grep -c 'pre-Feature-194 fallback'" -ForegroundColor DarkGray
    Write-Host "    Expect 0. A rejection scenario currently makes it non-zero — that is #1576." -ForegroundColor DarkGray
    Write-Host ""
}

exit $(if ($fail -eq 0) { 0 } else { 1 })
